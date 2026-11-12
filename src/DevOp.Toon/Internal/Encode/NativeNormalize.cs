#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Internal.Decode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Converts arbitrary supported CLR values into the internal native node graph consumed by the TOON encoders.
/// </summary>
internal static class NativeNormalize
{
    private const string JsonElementTypeName = "System.Text.Json.JsonElement";
    private const string JsonDocumentTypeName = "System.Text.Json.JsonDocument";
    private const string JsonNodeTypeName = "System.Text.Json.Nodes.JsonNode";
    private static readonly ConcurrentDictionary<Type, PropertyMetadata[]> PropertyCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object, string>?> JsonNodeWriters = new();
    private static readonly ConcurrentDictionary<Type, bool> JsonDomTypes = new();
    private static readonly ConcurrentDictionary<Type, bool> PlainObjectTypes = new();
    [ThreadStatic]
    private static HashSet<Type>? propertyBuildTypes;

    /// <summary>
    /// Normalizes a boxed CLR value, dictionary, enumerable, <see cref="ToonNode"/>, JSON DOM value, or plain object into native nodes.
    /// </summary>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The normalized native node, or a null primitive for unsupported leaf values.</returns>
    public static NativeNode? Normalize(object? value)
    {
        return Normalize(value, options: null);
    }

    public static NativeNode? Normalize(object? value, ResolvedEncodeOptions? options)
    {
        if (TryNormalizeByteSequence(value, options, out var byteSequencePrimitive))
            return byteSequencePrimitive;

        if (TryNormalizePrimitive(value, out var primitive))
            return primitive;

        if (value is ToonNode toonNode)
            return ToonNodeConverter.ToNativeNode(toonNode);

        if (value is object jsonValue && TryNormalizeJsonValue(jsonValue, out var jsonNode))
            return jsonNode;

        if (DictionaryEntries.Get(value) is { } entries)
        {
            var objectNode = new NativeObjectNode();
            foreach (var entry in entries)
            {
                objectNode[entry.Key?.ToString() ?? string.Empty] = Normalize(entry.Value, options);
            }

            return objectNode;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var arrayNode = new NativeArrayNode();
            foreach (var item in enumerable)
            {
                arrayNode.Add(Normalize(item, options));
            }

            return arrayNode;
        }

        if (value is object plainObject && IsPlainObject(plainObject))
            return NormalizePlainObject(plainObject, options);

        return new NativePrimitiveNode(null);
    }

    /// <summary>
    /// Normalizes a generic CLR value while preserving compile-time type information for common scalar checks.
    /// </summary>
    /// <typeparam name="T">The compile-time value type.</typeparam>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The normalized native node, or a null primitive for unsupported leaf values.</returns>
    public static NativeNode? Normalize<T>(T value)
    {
        return Normalize((object?)value, options: null);
    }

    public static NativeNode? Normalize<T>(T value, ResolvedEncodeOptions? options)
    {
        if (TryNormalizeByteSequence(value, options, out var byteSequencePrimitive))
            return byteSequencePrimitive;

        if (TryNormalizePrimitive(value, out var primitive))
            return primitive;

        if (value is ToonNode toonNode)
            return ToonNodeConverter.ToNativeNode(toonNode);

        if (value is object jsonValue && TryNormalizeJsonValue(jsonValue, out var jsonNode))
            return jsonNode;

        if (DictionaryEntries.Get(value) is { } entries)
        {
            var objectNode = new NativeObjectNode();
            foreach (var entry in entries)
            {
                objectNode[entry.Key?.ToString() ?? string.Empty] = Normalize(entry.Value, options);
            }

            return objectNode;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var arrayNode = new NativeArrayNode();
            foreach (var item in enumerable)
            {
                arrayNode.Add(Normalize(item, options));
            }

            return arrayNode;
        }

        if (value is object plainObject && IsPlainObject(plainObject))
            return NormalizePlainObject(plainObject, options);

        return new NativePrimitiveNode(null);
    }

    private static NativeObjectNode NormalizePlainObject(object plainObject, ResolvedEncodeOptions? options)
    {
        var type = plainObject.GetType();
        var properties = GetProperties(type);
        var extension = ExtensionDataMetadata.Find(type);
        var objectNode = new NativeObjectNode();
        IDictionary<string, object?>? entries = null;
        foreach (var property in properties)
        {
            if (extension != null && ExtensionDataMetadata.IsExtension(property.Info, extension))
            {
                entries = (IDictionary<string, object?>?)property.Getter(plainObject);
                continue;
            }
            objectNode[property.Name] = Normalize(property.Getter(plainObject), options);
        }

        if (entries != null)
        {
            foreach (var entry in entries)
            {
                if (IsMemberName(entry.Key, type, properties, extension!, options?.IsV4 == true)) continue;
                objectNode[entry.Key] = Normalize(entry.Value, options);
            }
        }

        return objectNode;
    }

    private static bool IsMemberName(string key, Type type, PropertyMetadata[] properties, PropertyInfo extension, bool isV4)
    {
        foreach (var property in properties)
        {
            if (ExtensionDataMetadata.IsExtension(property.Info, extension)) continue;
            if (property.Name == key || property.Info.Name == key) return true;
        }
        // Decoding would bind the key to a member (constructor-bound types match case-insensitively), so the entry cannot round-trip.
        // The legacy decoders also bind constructor parameter names that have no matching property.
        return V4Materialization.BindsMember(type, key) ||
               (!isV4 && (DirectMaterializer.BindsMember(type, key) || NativeTypedMaterializer.BindsMember(type, key)));
    }

    private static bool TryNormalizeByteSequence(object? value, ResolvedEncodeOptions? options, out NativePrimitiveNode primitive)
    {
        if (options?.ByteArrayFormat == ToonByteArrayFormat.Base64String &&
            ByteSequenceText.TryToBase64String(value, out var base64))
        {
            primitive = new NativePrimitiveNode(base64);
            return true;
        }

        primitive = default!;
        return false;
    }

    /// <summary>
    /// Returns whether a native node is a primitive value or null.
    /// </summary>
    /// <param name="value">The native node to test.</param>
    /// <returns><see langword="true"/> for null and scalar nodes.</returns>
    public static bool IsPrimitive(NativeNode? value) => value is null || value is NativePrimitiveNode;

    /// <summary>
    /// Returns whether a native node is an array node.
    /// </summary>
    /// <param name="value">The native node to test.</param>
    /// <returns><see langword="true"/> when the node is a <see cref="NativeArrayNode"/>.</returns>
    public static bool IsArray(NativeNode? value) => value is NativeArrayNode;

    /// <summary>
    /// Returns whether a native node is an object node.
    /// </summary>
    /// <param name="value">The native node to test.</param>
    /// <returns><see langword="true"/> when the node is a <see cref="NativeObjectNode"/>.</returns>
    public static bool IsObject(NativeNode? value) => value is NativeObjectNode;

    /// <summary>
    /// Returns whether a native node is an object with no properties.
    /// </summary>
    /// <param name="value">The native node to test.</param>
    /// <returns><see langword="true"/> when the node is an empty object.</returns>
    public static bool IsEmptyObject(NativeNode? value) => value is NativeObjectNode objectNode && objectNode.Count == 0;

    /// <summary>
    /// Returns whether all array items are primitive values or null.
    /// </summary>
    /// <param name="array">The array to inspect.</param>
    /// <returns><see langword="true"/> when every item is scalar or null.</returns>
    public static bool IsArrayOfPrimitives(NativeArrayNode array)
    {
        foreach (var item in array)
            if (!IsPrimitive(item)) return false;
        return true;
    }

    /// <summary>
    /// Returns whether all array items are arrays.
    /// </summary>
    /// <param name="array">The array to inspect.</param>
    /// <returns><see langword="true"/> when every item is a native array node.</returns>
    public static bool IsArrayOfArrays(NativeArrayNode array)
    {
        foreach (var item in array)
            if (!IsArray(item)) return false;
        return true;
    }

    /// <summary>
    /// Returns whether all array items are objects.
    /// </summary>
    /// <param name="array">The array to inspect.</param>
    /// <returns><see langword="true"/> when every item is a native object node.</returns>
    public static bool IsArrayOfObjects(NativeArrayNode array)
    {
        foreach (var item in array)
            if (!IsObject(item)) return false;
        return true;
    }

    /// <summary>
    /// Returns true if <paramref name="value"/> is a primitive value (including null) without allocating any objects.
    /// Use instead of <see cref="TryNormalizePrimitive"/> when the caller does not need the normalized node.
    /// </summary>
    internal static bool IsPrimitiveOrNull(object? value) => value switch
    {
        null or string or char or bool or int or long or decimal or byte or sbyte or short or ushort or uint or ulong or double or float or DateTime or DateTimeOffset or TimeSpan or Guid or Uri or Version => true,
#if NET6_0_OR_GREATER
        DateOnly or TimeOnly => true,
#endif
        Enum => true,
        _ => false
    };

    /// <summary>
    /// Attempts to normalize a CLR scalar into a native primitive node.
    /// </summary>
    /// <param name="value">The candidate scalar value.</param>
    /// <param name="primitive">The normalized primitive node when supported.</param>
    /// <returns><see langword="true"/> when the value is supported as a scalar primitive.</returns>
    internal static bool TryNormalizePrimitive(object? value, out NativePrimitiveNode primitive)
    {
        switch (value)
        {
            case null:
                primitive = new NativePrimitiveNode(null);
                return true;
            case string or char or bool or int or long or decimal or byte or sbyte or short or ushort or uint or ulong:
                primitive = new NativePrimitiveNode(value);
                return true;
            case double d:
                var normalizedDouble = FloatUtils.NormalizeSignedZero(d);
                primitive = new NativePrimitiveNode(NumericUtils.IsFinite(normalizedDouble) ? normalizedDouble : null);
                return true;
            case float f:
                var normalizedFloat = FloatUtils.NormalizeSignedZero(f);
                primitive = new NativePrimitiveNode(NumericUtils.IsFinite(normalizedFloat) ? normalizedFloat : null);
                return true;
            case DateTime dt:
                primitive = new NativePrimitiveNode(new DeferredDateTimeValue(dt));
                return true;
            case DateTimeOffset dto:
                primitive = new NativePrimitiveNode(new DeferredDateTimeOffsetValue(dto));
                return true;
            case TimeSpan timeSpan:
                primitive = new NativePrimitiveNode(timeSpan);
                return true;
            case Guid guid:
                primitive = new NativePrimitiveNode(guid);
                return true;
            case Uri uri:
                primitive = new NativePrimitiveNode(uri);
                return true;
            case Version version:
                primitive = new NativePrimitiveNode(version);
                return true;
            case Enum enumValue:
                primitive = new NativePrimitiveNode(enumValue);
                return true;
#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                primitive = new NativePrimitiveNode(new DeferredDateOnlyValue(dateOnly));
                return true;
            case TimeOnly timeOnly:
                primitive = new NativePrimitiveNode(new DeferredTimeOnlyValue(timeOnly));
                return true;
#endif
            default:
                primitive = default!;
                return false;
        }
    }

    private static bool TryNormalizeJsonValue(object value, out NativeNode? node)
    {
        var type = value.GetType();
        if (!IsJsonNamespace(type))
        {
            node = null;
            return false;
        }

        if (GetJsonNodeToJsonString(type) is { } toJsonString)
        {
            node = ToonNodeConverter.ToNativeNode(ToonNode.Parse(toJsonString(value)));
            return true;
        }

        if (string.Equals(type.FullName, JsonElementTypeName, StringComparison.Ordinal))
        {
            node = NormalizeJsonElement(value, type);
            return true;
        }

        if (string.Equals(type.FullName, JsonDocumentTypeName, StringComparison.Ordinal))
        {
            var rootElement = type.GetProperty("RootElement")?.GetValue(value);
            if (rootElement is null)
            {
                node = new NativePrimitiveNode(null);
                return true;
            }

            node = NormalizeJsonElement(rootElement, rootElement.GetType());
            return true;
        }

        node = null;
        return false;
    }

    /// <summary>
    /// Returns whether <paramref name="type"/> is a System.Text.Json DOM type (<c>JsonElement</c>, <c>JsonDocument</c>
    /// or a <c>JsonNode</c>), which encodes as its JSON text rather than as a CLR object.
    /// </summary>
    internal static bool IsJsonDomType(Type type)
        => IsJsonNamespace(type) && JsonDomTypes.GetOrAdd(type, static t => t.FullName is JsonElementTypeName or JsonDocumentTypeName || FindJsonNodeType(t) is not null);

    /// <summary>
    /// Returns whether a member declared as <paramref name="declaredType"/> can hold a System.Text.Json DOM value at run time:
    /// the DOM types derive only from <see cref="object"/> and <see cref="ValueType"/> and from each other, and implement
    /// only interfaces, so any other declared type rules them out without a per-value check.
    /// </summary>
    internal static bool MayHoldJsonDomValue(Type declaredType)
    {
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        return type == typeof(object) || type == typeof(ValueType) || type.IsInterface || IsJsonNamespace(type);
    }

    // The library does not reference System.Text.Json on netstandard2.0, so its DOM types are matched by name.
    // JsonNode subclasses cannot be declared outside System.Text.Json, so the namespace check is exact for them.
    private static bool IsJsonNamespace(Type type)
        => type.Namespace is "System.Text.Json" or "System.Text.Json.Nodes";

    private static Type? FindJsonNodeType(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.FullName, JsonNodeTypeName, StringComparison.Ordinal))
                return current;
        }

        return null;
    }

    private static Func<object, string>? GetJsonNodeToJsonString(Type type)
        => JsonNodeWriters.GetOrAdd(type, static t => CreateJsonNodeToJsonString(t));

    private static Func<object, string>? CreateJsonNodeToJsonString(Type type)
    {
        var jsonNodeType = FindJsonNodeType(type);
        if (jsonNodeType is null)
            return null;

        MethodInfo? toJsonString = null;
        foreach (var method in jsonNodeType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name == "ToJsonString" && method.ReturnType == typeof(string) && method.GetParameters().Length == 1)
            {
                toJsonString = method;
                break;
            }
        }

        if (toJsonString is null)
            return null;

        var instance = Expression.Parameter(typeof(object), "node");
        var call = Expression.Call(
            Expression.Convert(instance, jsonNodeType),
            toJsonString,
            Expression.Constant(null, toJsonString.GetParameters()[0].ParameterType));
        return Expression.Lambda<Func<object, string>>(call, instance).Compile();
    }

    private static NativeNode? NormalizeJsonElement(object value, Type type)
    {
        var rawText = type.GetMethod("GetRawText", Type.EmptyTypes)?.Invoke(value, null) as string;
        if (rawText is null)
        {
            return new NativePrimitiveNode(null);
        }

        return ToonNodeConverter.ToNativeNode(ToonNode.Parse(rawText));
    }

    internal static bool IsPrimitiveClrType(Type type)
    {
        var actualType = Nullable.GetUnderlyingType(type) ?? type;
        return actualType == typeof(string)
            || actualType == typeof(char)
            || actualType == typeof(bool)
            || actualType == typeof(int)
            || actualType == typeof(long)
            || actualType == typeof(decimal)
            || actualType == typeof(byte)
            || actualType == typeof(sbyte)
            || actualType == typeof(short)
            || actualType == typeof(ushort)
            || actualType == typeof(uint)
            || actualType == typeof(ulong)
            || actualType == typeof(double)
            || actualType == typeof(float)
            || actualType == typeof(DateTime)
            || actualType == typeof(DateTimeOffset)
            || actualType == typeof(TimeSpan)
            || actualType == typeof(Guid)
            || actualType == typeof(Uri)
            || actualType == typeof(Version)
            || actualType.IsEnum
#if NET6_0_OR_GREATER
            || actualType == typeof(DateOnly)
            || actualType == typeof(TimeOnly)
#endif
            ;
    }

    internal static bool IsPlainObjectType(Type type)
        => PlainObjectTypes.TryGetValue(type, out var plain) ? plain : PlainObjectTypes.GetOrAdd(type, static t => ClassifyPlainObjectType(t));

    private static bool ClassifyPlainObjectType(Type type)
    {
        if (typeof(ToonNode).IsAssignableFrom(type) || typeof(NativeNode).IsAssignableFrom(type) || IsJsonDomType(type)) return false;
        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return false;

#if NET6_0_OR_GREATER
        if (type == typeof(DateOnly) || type == typeof(TimeOnly))
            return false;
#endif

        if (typeof(IEnumerable).IsAssignableFrom(type))
            return false;

        return type.IsClass || type.IsValueType;
    }

    private static bool IsPlainObject(object value)
    {
        return IsPlainObjectType(value.GetType());
    }

    internal static IReadOnlyList<PropertyMetadata> GetCachedProperties(Type type)
    {
        return GetProperties(type);
    }

    private static PropertyMetadata[] GetProperties(Type type)
    {
        return PropertyCache.GetOrAdd(type, static t => BuildProperties(t));
    }

    private static PropertyMetadata[] BuildProperties(Type type)
    {
        var activeTypes = propertyBuildTypes ??= new HashSet<Type>();
        activeTypes.Add(type);
        try { return BuildPropertiesCore(type); }
        finally { activeTypes.Remove(type); }
    }

    private static PropertyMetadata[] BuildPropertiesCore(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var metadata = new List<PropertyMetadata>(properties.Length);
        for (int i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var propertyName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name ?? property.Name;
            var enumerableElementType = GetEnumerableElementType(property.PropertyType);
            var valueDeclaresExtensionData = IsPlainObjectType(property.PropertyType) && DeclaresExtensionData(property.PropertyType);
            var nestedObjectProperties = IsPlainObjectType(property.PropertyType)
                ? CreateNestedPropertyCache(property.PropertyType)
                : null;

            metadata.Add(new PropertyMetadata(
                property,
                propertyName,
                NativePrimitives.EncodeKey(propertyName),
                CreateGetter(property),
                enumerableElementType,
                BuildCachedEmptyEnumerableHeader(propertyName, enumerableElementType),
                nestedObjectProperties,
                valueDeclaresExtensionData,
                MayHoldJsonDomValue(property.PropertyType)));
        }

        return metadata.ToArray();
    }

    /// <summary>
    /// Returns whether <paramref name="type"/> marks any public instance property with <see cref="ToonExtensionDataAttribute"/>,
    /// without validating it; validation happens when a value of the type is encoded.
    /// </summary>
    internal static bool DeclaresExtensionData(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (Attribute.IsDefined(property, typeof(ToonExtensionDataAttribute), inherit: true)) return true;
        }

        return false;
    }

    /// <summary>
    /// Stores reflection metadata and cached accessors for an encoded CLR property.
    /// </summary>
    internal readonly struct PropertyMetadata
    {
        /// <summary>
        /// Initializes property metadata used by normalization and fast typed encoding.
        /// </summary>
        /// <param name="info">The reflected property.</param>
        /// <param name="name">The effective TOON property name.</param>
        /// <param name="encodedName">The pre-encoded TOON key.</param>
        /// <param name="getter">A compiled getter for the property value.</param>
        /// <param name="enumerableElementType">The enumerable element type when the property is a non-string enumerable.</param>
        /// <param name="cachedEmptyEnumerableHeader">A cached empty array header for enumerable properties.</param>
        /// <param name="nestedObjectProperties">Cached nested properties when the property type is a plain object.</param>
        /// <param name="valueDeclaresExtensionData">Whether the property type declares a <see cref="ToonExtensionDataAttribute"/> property.</param>
        /// <param name="mayHoldJsonDom">Whether the declared property type admits a System.Text.Json DOM value.</param>
        public PropertyMetadata(
            PropertyInfo info,
            string name,
            string encodedName,
            Func<object, object?> getter,
            Type? enumerableElementType,
            string? cachedEmptyEnumerableHeader,
            PropertyMetadata[]? nestedObjectProperties,
            bool valueDeclaresExtensionData,
            bool mayHoldJsonDom)
        {
            Info = info;
            Name = name;
            EncodedName = encodedName;
            Getter = getter;
            EnumerableElementType = enumerableElementType;
            CachedEmptyEnumerableHeader = cachedEmptyEnumerableHeader;
            NestedObjectProperties = nestedObjectProperties;
            ValueDeclaresExtensionData = valueDeclaresExtensionData;
            MayHoldJsonDom = mayHoldJsonDom;
        }

        /// <summary>Gets the reflected property.</summary>
        public PropertyInfo Info { get; }

        /// <summary>Gets the effective TOON property name.</summary>
        public string Name { get; }

        /// <summary>Gets the encoded TOON key for this property.</summary>
        public string EncodedName { get; }

        /// <summary>Gets the compiled property getter.</summary>
        public Func<object, object?> Getter { get; }

        /// <summary>Gets the element type for non-string enumerable properties.</summary>
        public Type? EnumerableElementType { get; }

        /// <summary>Gets a cached empty array header for enumerable properties when available.</summary>
        public string? CachedEmptyEnumerableHeader { get; }

        /// <summary>Gets cached nested object properties when the property type is a plain object.</summary>
        public PropertyMetadata[]? NestedObjectProperties { get; }

        /// <summary>Gets whether the property type declares extension data, so its values are flattened rather than written member by member.</summary>
        public bool ValueDeclaresExtensionData { get; }

        /// <summary>Gets whether values of this property can be System.Text.Json DOM values, which encode as their JSON text.</summary>
        public bool MayHoldJsonDom { get; }
    }

    private static PropertyMetadata[]? CreateNestedPropertyCache(Type type)
    {
        // Metadata must remain acyclic; finite recursive values are normalized separately.
        if (propertyBuildTypes is { } activeTypes && (activeTypes.Contains(type) || activeTypes.Count >= 256)) return null;
        var properties = GetProperties(type);
        return properties.Length == 0 ? null : properties;
    }

    private static string? BuildCachedEmptyEnumerableHeader(string propertyName, Type? enumerableElementType)
    {
        if (enumerableElementType is null)
        {
            return null;
        }

        return NativePrimitives.EncodeKey(propertyName) + Constants.OPEN_BRACKET + "0" + Constants.CLOSE_BRACKET + Constants.COLON;
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        var interfaces = type.GetInterfaces();
        for (int i = 0; i < interfaces.Length; i++)
        {
            var current = interfaces[i];
            if (!current.IsGenericType || current.GetGenericTypeDefinition() != typeof(IEnumerable<>))
            {
                continue;
            }

            return current.GetGenericArguments()[0];
        }

        return null;
    }

    private static Func<object, object?> CreateGetter(PropertyInfo property)
    {
        var instanceParameter = Expression.Parameter(typeof(object), "instance");
        var typedInstance = Expression.Convert(instanceParameter, property.DeclaringType!);
        var propertyAccess = Expression.Property(typedInstance, property);
        var boxedResult = Expression.Convert(propertyAccess, typeof(object));
        return Expression.Lambda<Func<object, object?>>(boxedResult, instanceParameter).Compile();
    }

}
