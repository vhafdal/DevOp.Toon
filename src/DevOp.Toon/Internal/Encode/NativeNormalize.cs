#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Converts arbitrary supported CLR values into the internal native node graph consumed by the TOON encoders.
/// </summary>
internal static class NativeNormalize
{
    private static readonly ConcurrentDictionary<Type, PropertyMetadata[]> PropertyCache = new();

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

        if (value is IDictionary dict)
        {
            var objectNode = new NativeObjectNode();
            foreach (DictionaryEntry entry in dict)
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
        {
            var objectNode = new NativeObjectNode();
            foreach (var property in GetProperties(plainObject.GetType()))
            {
                objectNode[property.Name] = Normalize(property.Getter(plainObject), options);
            }

            return objectNode;
        }

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

        if (value is IDictionary dict)
        {
            var objectNode = new NativeObjectNode();
            foreach (DictionaryEntry entry in dict)
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
        {
            var objectNode = new NativeObjectNode();
            foreach (var property in GetProperties(plainObject.GetType()))
            {
                objectNode[property.Name] = Normalize(property.Getter(plainObject), options);
            }

            return objectNode;
        }

        return new NativePrimitiveNode(null);
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
        if (string.Equals(type.FullName, "System.Text.Json.JsonElement", StringComparison.Ordinal))
        {
            node = NormalizeJsonElement(value, type);
            return true;
        }

        if (string.Equals(type.FullName, "System.Text.Json.JsonDocument", StringComparison.Ordinal))
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
    {
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
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var metadata = new List<PropertyMetadata>(properties.Length);
        for (int i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (!property.CanRead)
            {
                continue;
            }

            var propertyName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name ?? property.Name;
            var enumerableElementType = GetEnumerableElementType(property.PropertyType);
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
                nestedObjectProperties));
        }

        return metadata.ToArray();
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
        public PropertyMetadata(
            PropertyInfo info,
            string name,
            string encodedName,
            Func<object, object?> getter,
            Type? enumerableElementType,
            string? cachedEmptyEnumerableHeader,
            PropertyMetadata[]? nestedObjectProperties)
        {
            Info = info;
            Name = name;
            EncodedName = encodedName;
            Getter = getter;
            EnumerableElementType = enumerableElementType;
            CachedEmptyEnumerableHeader = cachedEmptyEnumerableHeader;
            NestedObjectProperties = nestedObjectProperties;
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
    }

    private static PropertyMetadata[]? CreateNestedPropertyCache(Type type)
    {
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
