#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Encode;

internal static class NativeNormalize
{
    private static readonly ConcurrentDictionary<Type, PropertyMetadata[]> PropertyCache = new();

    public static NativeNode? Normalize(object? value)
    {
        if (TryNormalizePrimitive(value, out var primitive))
            return primitive;

        if (value is ToonNode toonNode)
            return ToonNodeConverter.ToNativeNode(toonNode);

        if (value is IDictionary dict)
        {
            var objectNode = new NativeObjectNode();
            foreach (DictionaryEntry entry in dict)
            {
                objectNode[entry.Key?.ToString() ?? string.Empty] = Normalize(entry.Value);
            }

            return objectNode;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var arrayNode = new NativeArrayNode();
            foreach (var item in enumerable)
            {
                arrayNode.Add(Normalize(item));
            }

            return arrayNode;
        }

        if (value is object plainObject && IsPlainObject(plainObject))
        {
            var objectNode = new NativeObjectNode();
            foreach (var property in GetProperties(plainObject.GetType()))
            {
                objectNode[property.Name] = Normalize(property.Getter(plainObject));
            }

            return objectNode;
        }

        return new NativePrimitiveNode(null);
    }

    public static NativeNode? Normalize<T>(T value)
    {
        if (TryNormalizePrimitive(value, out var primitive))
            return primitive;

        if (value is ToonNode toonNode)
            return ToonNodeConverter.ToNativeNode(toonNode);

        if (value is IDictionary dict)
        {
            var objectNode = new NativeObjectNode();
            foreach (DictionaryEntry entry in dict)
            {
                objectNode[entry.Key?.ToString() ?? string.Empty] = Normalize(entry.Value);
            }

            return objectNode;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var arrayNode = new NativeArrayNode();
            foreach (var item in enumerable)
            {
                arrayNode.Add(Normalize(item));
            }

            return arrayNode;
        }

        if (value is object plainObject && IsPlainObject(plainObject))
        {
            var objectNode = new NativeObjectNode();
            foreach (var property in GetProperties(plainObject.GetType()))
            {
                objectNode[property.Name] = Normalize(property.Getter(plainObject));
            }

            return objectNode;
        }

        return new NativePrimitiveNode(null);
    }

    public static bool IsPrimitive(NativeNode? value) => value is null || value is NativePrimitiveNode;

    public static bool IsArray(NativeNode? value) => value is NativeArrayNode;

    public static bool IsObject(NativeNode? value) => value is NativeObjectNode;

    public static bool IsEmptyObject(NativeNode? value) => value is NativeObjectNode objectNode && objectNode.Count == 0;

    public static bool IsArrayOfPrimitives(NativeArrayNode array) => array.All(IsPrimitive);

    public static bool IsArrayOfArrays(NativeArrayNode array) => array.All(IsArray);

    public static bool IsArrayOfObjects(NativeArrayNode array) => array.All(IsObject);

    /// <summary>
    /// Returns true if <paramref name="value"/> is a primitive value (including null) without allocating any objects.
    /// Use instead of <see cref="TryNormalizePrimitive"/> when the caller does not need the normalized node.
    /// </summary>
    internal static bool IsPrimitiveOrNull(object? value) => value switch
    {
        null or string or bool or int or long or decimal or byte or sbyte or short or ushort or uint or ulong or double or float or DateTime or DateTimeOffset => true,
#if NET6_0_OR_GREATER
        DateOnly or TimeOnly => true,
#endif
        _ => false
    };

    internal static bool TryNormalizePrimitive(object? value, out NativePrimitiveNode primitive)
    {
        switch (value)
        {
            case null:
                primitive = new NativePrimitiveNode(null);
                return true;
            case string or bool or int or long or decimal or byte or sbyte or short or ushort or uint or ulong:
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

    internal static bool IsPrimitiveClrType(Type type)
    {
        var actualType = Nullable.GetUnderlyingType(type) ?? type;
        return actualType == typeof(string)
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

    internal readonly struct PropertyMetadata
    {
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

        public PropertyInfo Info { get; }

        public string Name { get; }

        public string EncodedName { get; }

        public Func<object, object?> Getter { get; }

        public Type? EnumerableElementType { get; }

        public string? CachedEmptyEnumerableHeader { get; }

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
