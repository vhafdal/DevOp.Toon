#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon.Internal.Decode;

/// <summary>
/// Converts the internal native node graph into CLR target types after generic parsing has completed.
/// </summary>
internal static class NativeTypedMaterializer
{
    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<Type, TypePlan> PlanCache = new Dictionary<Type, TypePlan>();

    /// <summary>
    /// Attempts to convert a native node into the requested CLR type.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="node">The native node to convert.</param>
    /// <param name="value">The converted value when conversion succeeds.</param>
    /// <returns><see langword="true"/> when the node can be materialized as <typeparamref name="T"/>; otherwise, <see langword="false"/>.</returns>
    public static bool TryConvert<T>(NativeNode? node, out T? value)
    {
        if (!TryConvert(node, typeof(T), out var boxed))
        {
            value = default;
            return false;
        }

        value = (T?)boxed;
        return true;
    }

    /// <summary>
    /// Attempts to convert a native node into the requested runtime type.
    /// </summary>
    /// <param name="node">The native node to convert.</param>
    /// <param name="type">The target runtime type.</param>
    /// <param name="value">The converted value when conversion succeeds.</param>
    /// <returns><see langword="true"/> when the node can be materialized as <paramref name="type"/>; otherwise, <see langword="false"/>.</returns>
    public static bool TryConvert(NativeNode? node, Type type, out object? value)
    {
        var plan = GetPlan(type);
        return TryConvert(node, plan, out value);
    }

    private static TypePlan GetPlan(Type type)
    {
        lock (SyncRoot)
        {
            if (PlanCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var plan = BuildPlan(type, new HashSet<Type>());
            PlanCache[type] = plan;
            return plan;
        }
    }

    private static TypePlan BuildPlan(Type type, HashSet<Type> building)
    {
        if (PlanCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        if (building.Contains(type))
        {
            return TypePlan.Unsupported(type);
        }

        building.Add(type);
        try
        {
            if (type == typeof(object))
            {
                return TypePlan.Any(type);
            }

            if (type.IsEnum)
            {
                return TypePlan.Primitive(type);
            }

            var nullableType = Nullable.GetUnderlyingType(type);
            if (nullableType != null)
            {
                var underlyingPlan = BuildPlan(nullableType, building);
                return underlyingPlan.IsSupported
                    ? TypePlan.Nullable(type, underlyingPlan)
                    : TypePlan.Unsupported(type);
            }

            if (IsPrimitiveType(type))
            {
                return TypePlan.Primitive(type);
            }

            if (TryBuildDictionaryPlan(type, building, out var dictionaryPlan))
            {
                return dictionaryPlan;
            }

            if (TryBuildCollectionPlan(type, building, out var collectionPlan))
            {
                return collectionPlan;
            }

            if ((!type.IsClass && !type.IsValueType) || type == typeof(string))
            {
                return TypePlan.Unsupported(type);
            }

            Func<object> factory;
            if (type.IsValueType)
            {
                factory = CreateValueTypeFactory(type);
            }
            else
            {
                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    return TypePlan.Unsupported(type);
                }

                factory = CreateFactory(ctor);
            }

            var properties = new Dictionary<string, PropertyPlan>(StringComparer.Ordinal);
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanWrite || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                var propertyPlan = BuildPlan(property.PropertyType, building);
                if (!propertyPlan.IsSupported)
                {
                    return TypePlan.Unsupported(type);
                }

                var propertyEntry = new PropertyPlan(propertyPlan, CreateSetter(property));
                properties[property.Name] = propertyEntry;

                var toonName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name;
                if (toonName is { Length: > 0 })
                {
                    properties[toonName] = propertyEntry;
                }
            }

            return TypePlan.Object(type, factory, properties);
        }
        finally
        {
            building.Remove(type);
        }
    }

    private static bool TryBuildDictionaryPlan(Type type, HashSet<Type> building, out TypePlan plan)
    {
        plan = TypePlan.Unsupported(type);
        if (!type.IsGenericType)
        {
            return false;
        }

        var genericDefinition = type.GetGenericTypeDefinition();
        if ((genericDefinition != typeof(Dictionary<,>) &&
             genericDefinition != typeof(IDictionary<,>) &&
             genericDefinition != typeof(IReadOnlyDictionary<,>)) ||
            type.GetGenericArguments()[0] != typeof(string))
        {
            return false;
        }

        var valueType = type.GetGenericArguments()[1];
        var valuePlan = BuildPlan(valueType, building);
        if (!valuePlan.IsSupported)
        {
            return false;
        }

        var concreteDictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
        if (!type.IsAssignableFrom(concreteDictionaryType) && type != concreteDictionaryType)
        {
            return false;
        }

        plan = TypePlan.Dictionary(
            type,
            valuePlan,
            CreateDictionaryFactory(concreteDictionaryType));
        return true;
    }

    private static bool TryBuildCollectionPlan(Type type, HashSet<Type> building, out TypePlan plan)
    {
        plan = TypePlan.Unsupported(type);

        if (type.IsArray && type.GetArrayRank() == 1)
        {
            var elementType = type.GetElementType();
            if (elementType == null)
            {
                return false;
            }

            var elementPlan = BuildPlan(elementType, building);
            if (!elementPlan.IsSupported)
            {
                return false;
            }

            plan = TypePlan.Array(
                type,
                elementPlan,
                capacity => new List<object?>(capacity),
                list => ToArray(elementType, list));
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var genericDefinition = type.GetGenericTypeDefinition();
        if (genericDefinition != typeof(List<>) &&
            genericDefinition != typeof(IList<>) &&
            genericDefinition != typeof(ICollection<>) &&
            genericDefinition != typeof(IEnumerable<>) &&
            genericDefinition != typeof(IReadOnlyList<>))
        {
            return false;
        }

        var elementTypeForList = type.GetGenericArguments()[0];
        var elementPlanForList = BuildPlan(elementTypeForList, building);
        if (!elementPlanForList.IsSupported)
        {
            return false;
        }

        var concreteListType = typeof(List<>).MakeGenericType(elementTypeForList);
        if (!type.IsAssignableFrom(concreteListType) && type != concreteListType)
        {
            return false;
        }

        plan = TypePlan.Collection(
            type,
            elementPlanForList,
            CreateListFactory(concreteListType),
            list => list);
        return true;
    }

    private static bool TryConvert(NativeNode? node, TypePlan plan, out object? value)
    {
        if (node == null || node is NativePrimitiveNode { Value: null })
        {
            if (plan.Kind == PlanKind.Nullable || plan.Kind == PlanKind.Any || !plan.Type.IsValueType)
            {
                value = null;
                return true;
            }

            value = null;
            return false;
        }

        switch (plan.Kind)
        {
            case PlanKind.Any:
                value = ConvertUntyped(node);
                return true;
            case PlanKind.Nullable:
                return TryConvert(node, plan.UnderlyingPlan!, out value);
            case PlanKind.Primitive:
                return TryConvertPrimitive(node, plan.Type, out value);
            case PlanKind.Object:
                return TryConvertObject(node, plan, out value);
            case PlanKind.Collection:
            case PlanKind.Array:
                return TryConvertCollection(node, plan, out value);
            case PlanKind.Dictionary:
                return TryConvertDictionary(node, plan, out value);
            default:
                value = null;
                return false;
        }
    }

    private static bool TryConvertObject(NativeNode? node, TypePlan plan, out object? value)
    {
        if (node is not NativeObjectNode objectNode || plan.Factory == null || plan.Properties == null)
        {
            value = null;
            return false;
        }

        var instance = plan.Factory();
        foreach (var kvp in objectNode)
        {
            if (!plan.Properties.TryGetValue(kvp.Key, out var property))
            {
                continue;
            }

            if (!TryConvert(kvp.Value, property.Plan, out var propertyValue))
            {
                value = null;
                return false;
            }

            property.Setter(instance, propertyValue);
        }

        value = instance;
        return true;
    }

    private static bool TryConvertCollection(NativeNode? node, TypePlan plan, out object? value)
    {
        if (node is not NativeArrayNode arrayNode || plan.ElementPlan == null || plan.CreateList == null || plan.FinalizeList == null)
        {
            value = null;
            return false;
        }

        var list = plan.CreateList(arrayNode.Count);
        foreach (var item in arrayNode)
        {
            if (!TryConvert(item, plan.ElementPlan, out var elementValue))
            {
                value = null;
                return false;
            }

            list.Add(elementValue);
        }

        value = plan.FinalizeList(list);
        return true;
    }

    private static bool TryConvertDictionary(NativeNode? node, TypePlan plan, out object? value)
    {
        if (node is not NativeObjectNode objectNode || plan.ElementPlan == null || plan.CreateDictionary == null)
        {
            value = null;
            return false;
        }

        var dictionary = plan.CreateDictionary();
        foreach (var kvp in objectNode)
        {
            if (!TryConvert(kvp.Value, plan.ElementPlan, out var elementValue))
            {
                value = null;
                return false;
            }

            dictionary[kvp.Key] = elementValue;
        }

        value = dictionary;
        return true;
    }

    private static bool TryConvertPrimitive(NativeNode? node, Type targetType, out object? value)
    {
        if (node is not NativePrimitiveNode primitive)
        {
            value = null;
            return false;
        }

        var primitiveValue = primitive.Value;

        if (targetType == typeof(string))
        {
            value = primitiveValue as string;
            return primitiveValue is string;
        }

        if (targetType == typeof(char))
        {
            if (primitiveValue is string charText && charText.Length == 1)
            {
                value = charText[0];
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(bool))
        {
            if (primitiveValue is bool boolValue)
            {
                value = boolValue;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(byte))
        {
            return TryConvertIntegerPrimitive(primitiveValue, byte.MinValue, byte.MaxValue, static candidate => (byte)candidate, out value);
        }

        if (targetType == typeof(sbyte))
        {
            return TryConvertIntegerPrimitive(primitiveValue, sbyte.MinValue, sbyte.MaxValue, static candidate => (sbyte)candidate, out value);
        }

        if (targetType == typeof(short))
        {
            return TryConvertIntegerPrimitive(primitiveValue, short.MinValue, short.MaxValue, static candidate => (short)candidate, out value);
        }

        if (targetType == typeof(ushort))
        {
            return TryConvertUnsignedIntegerPrimitive(primitiveValue, ushort.MaxValue, static candidate => (ushort)candidate, out value);
        }

        if (targetType == typeof(int))
        {
            if (primitiveValue is int intValue)
            {
                value = intValue;
                return true;
            }

            if (primitiveValue is long longValue && longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                value = (int)longValue;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(uint))
        {
            return TryConvertUnsignedIntegerPrimitive(primitiveValue, uint.MaxValue, static candidate => (uint)candidate, out value);
        }

        if (targetType == typeof(long))
        {
            if (primitiveValue is long longPrimitive)
            {
                value = longPrimitive;
                return true;
            }

            if (primitiveValue is int intPrimitive)
            {
                value = (long)intPrimitive;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(ulong))
        {
            if (primitiveValue is ulong ulongValue)
            {
                value = ulongValue;
                return true;
            }

            if (primitiveValue is long longUlong && longUlong >= 0)
            {
                value = (ulong)longUlong;
                return true;
            }

            if (primitiveValue is int intUlong && intUlong >= 0)
            {
                value = (ulong)intUlong;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(double))
        {
            if (primitiveValue is double doubleValue)
            {
                value = doubleValue;
                return true;
            }

            if (primitiveValue is int intDouble)
            {
                value = (double)intDouble;
                return true;
            }

            if (primitiveValue is long longDouble)
            {
                value = (double)longDouble;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(float))
        {
            if (primitiveValue is double floatDouble)
            {
                value = (float)floatDouble;
                return true;
            }

            if (primitiveValue is int intFloat)
            {
                value = (float)intFloat;
                return true;
            }

            if (primitiveValue is long longFloat)
            {
                value = (float)longFloat;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(decimal))
        {
            if (primitiveValue is decimal decimalValue)
            {
                value = decimalValue;
                return true;
            }

            if (primitiveValue is double decimalDouble)
            {
                value = Convert.ToDecimal(decimalDouble, CultureInfo.InvariantCulture);
                return true;
            }

            if (primitiveValue is int intDecimal)
            {
                value = (decimal)intDecimal;
                return true;
            }

            if (primitiveValue is long longDecimal)
            {
                value = (decimal)longDecimal;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(DateTime))
        {
            if (primitiveValue is string dateTimeText &&
                DateTime.TryParse(dateTimeText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            {
                value = dateTime;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(DateTimeOffset))
        {
            if (primitiveValue is string dateTimeOffsetText &&
                DateTimeOffset.TryParse(dateTimeOffsetText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset))
            {
                value = dateTimeOffset;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(TimeSpan))
        {
            if (primitiveValue is string timeSpanText &&
                TimeSpan.TryParse(timeSpanText, CultureInfo.InvariantCulture, out var timeSpan))
            {
                value = timeSpan;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(Uri))
        {
            if (primitiveValue is string uriText && Uri.TryCreate(uriText, UriKind.RelativeOrAbsolute, out var uri))
            {
                value = uri;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(Version))
        {
            if (primitiveValue is string versionText && Version.TryParse(versionText, out var version))
            {
                value = version;
                return true;
            }

            value = null;
            return false;
        }

#if NET6_0_OR_GREATER
        if (targetType == typeof(DateOnly))
        {
            if (primitiveValue is string dateOnlyText &&
                DateOnly.TryParse(dateOnlyText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            {
                value = dateOnly;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType == typeof(TimeOnly))
        {
            if (primitiveValue is string timeOnlyText &&
                TimeOnly.TryParse(timeOnlyText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly))
            {
                value = timeOnly;
                return true;
            }

            value = null;
            return false;
        }
#endif

        if (targetType == typeof(Guid))
        {
            if (primitiveValue is string guidText && Guid.TryParse(guidText, out var guid))
            {
                value = guid;
                return true;
            }

            value = null;
            return false;
        }

        if (targetType.IsEnum)
        {
            if (primitiveValue is string enumName && Enum.IsDefined(targetType, enumName))
            {
                value = Enum.Parse(targetType, enumName, false);
                return true;
            }

            if (TryConvertEnumFromNumeric(primitiveValue, targetType, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        value = null;
        return false;
    }

    private static bool TryConvertIntegerPrimitive<TInteger>(object? primitiveValue, long minValue, long maxValue, Func<long, TInteger> converter, out object? value)
        where TInteger : struct
    {
        if (primitiveValue is long longValue && longValue >= minValue && longValue <= maxValue)
        {
            value = converter(longValue);
            return true;
        }

        if (primitiveValue is int intValue && intValue >= minValue && intValue <= maxValue)
        {
            value = converter(intValue);
            return true;
        }

        if (primitiveValue is double doubleValue &&
            doubleValue >= minValue &&
            doubleValue <= maxValue &&
            Math.Abs(doubleValue % 1d) < double.Epsilon)
        {
            value = converter((long)doubleValue);
            return true;
        }

        if (primitiveValue is decimal decimalValue &&
            decimalValue >= minValue &&
            decimalValue <= maxValue &&
            decimal.Truncate(decimalValue) == decimalValue)
        {
            value = converter((long)decimalValue);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryConvertUnsignedIntegerPrimitive<TInteger>(object? primitiveValue, ulong maxValue, Func<ulong, TInteger> converter, out object? value)
        where TInteger : struct
    {
        if (primitiveValue is long longValue && longValue >= 0 && (ulong)longValue <= maxValue)
        {
            value = converter((ulong)longValue);
            return true;
        }

        if (primitiveValue is int intValue && intValue >= 0 && (ulong)intValue <= maxValue)
        {
            value = converter((ulong)intValue);
            return true;
        }

        if (primitiveValue is double doubleValue &&
            doubleValue >= 0 &&
            doubleValue <= maxValue &&
            Math.Abs(doubleValue % 1d) < double.Epsilon)
        {
            value = converter((ulong)doubleValue);
            return true;
        }

        if (primitiveValue is decimal decimalValue &&
            decimalValue >= 0 &&
            decimalValue <= maxValue &&
            decimal.Truncate(decimalValue) == decimalValue)
        {
            value = converter((ulong)decimalValue);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryConvertEnumFromNumeric(object? primitiveValue, Type enumType, out object? value)
    {
        var underlyingType = Enum.GetUnderlyingType(enumType);
        if (underlyingType == typeof(byte) && TryConvertIntegerPrimitive(primitiveValue, byte.MinValue, byte.MaxValue, static candidate => (byte)candidate, out var byteValue))
        {
            value = Enum.ToObject(enumType, (byte)byteValue!);
            return true;
        }

        if (underlyingType == typeof(sbyte) && TryConvertIntegerPrimitive(primitiveValue, sbyte.MinValue, sbyte.MaxValue, static candidate => (sbyte)candidate, out var sbyteValue))
        {
            value = Enum.ToObject(enumType, (sbyte)sbyteValue!);
            return true;
        }

        if (underlyingType == typeof(short) && TryConvertIntegerPrimitive(primitiveValue, short.MinValue, short.MaxValue, static candidate => (short)candidate, out var shortValue))
        {
            value = Enum.ToObject(enumType, (short)shortValue!);
            return true;
        }

        if (underlyingType == typeof(ushort) && TryConvertUnsignedIntegerPrimitive(primitiveValue, ushort.MaxValue, static candidate => (ushort)candidate, out var ushortValue))
        {
            value = Enum.ToObject(enumType, (ushort)ushortValue!);
            return true;
        }

        if (underlyingType == typeof(int) && TryConvertIntegerPrimitive(primitiveValue, int.MinValue, int.MaxValue, static candidate => (int)candidate, out var intValue))
        {
            value = Enum.ToObject(enumType, (int)intValue!);
            return true;
        }

        if (underlyingType == typeof(uint) && TryConvertUnsignedIntegerPrimitive(primitiveValue, uint.MaxValue, static candidate => (uint)candidate, out var uintValue))
        {
            value = Enum.ToObject(enumType, (uint)uintValue!);
            return true;
        }

        if (underlyingType == typeof(long) && primitiveValue is long longValue)
        {
            value = Enum.ToObject(enumType, longValue);
            return true;
        }

        if (underlyingType == typeof(long) &&
            TryConvertIntegerPrimitive(primitiveValue, long.MinValue, long.MaxValue, static candidate => candidate, out var parsedLong))
        {
            value = Enum.ToObject(enumType, (long)parsedLong!);
            return true;
        }

        if (underlyingType == typeof(ulong))
        {
            if (primitiveValue is ulong ulongValue)
            {
                value = Enum.ToObject(enumType, ulongValue);
                return true;
            }

            if (primitiveValue is long signedLongValue && signedLongValue >= 0)
            {
                value = Enum.ToObject(enumType, (ulong)signedLongValue);
                return true;
            }

            if (TryConvertUnsignedIntegerPrimitive(primitiveValue, ulong.MaxValue, static candidate => candidate, out var parsedUlong))
            {
                value = Enum.ToObject(enumType, (ulong)parsedUlong!);
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? ConvertUntyped(NativeNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case NativePrimitiveNode primitive:
                return primitive.Value;
            case NativeArrayNode array:
                var list = new List<object?>(array.Count);
                foreach (var item in array)
                {
                    list.Add(ConvertUntyped(item));
                }

                return list;
            case NativeObjectNode obj:
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kvp in obj)
                {
                    dictionary[kvp.Key] = ConvertUntyped(kvp.Value);
                }

                return dictionary;
            default:
                return null;
        }
    }

    private static bool IsPrimitiveType(Type type)
    {
        return type == typeof(string) ||
               type == typeof(char) ||
               type == typeof(bool) ||
               type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(double) ||
               type == typeof(float) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(TimeSpan) ||
               type == typeof(Uri) ||
               type == typeof(Version) ||
#if NET6_0_OR_GREATER
               type == typeof(DateOnly) ||
               type == typeof(TimeOnly) ||
#endif
               type == typeof(Guid) ||
               type.IsEnum;
    }

    private static Func<object> CreateFactory(ConstructorInfo constructor)
    {
        var body = Expression.New(constructor);
        return Expression.Lambda<Func<object>>(Expression.Convert(body, typeof(object))).Compile();
    }

    private static Func<object> CreateValueTypeFactory(Type type)
    {
        var body = Expression.Default(type);
        return Expression.Lambda<Func<object>>(Expression.Convert(body, typeof(object))).Compile();
    }

    private static Func<int, IList> CreateListFactory(Type concreteListType)
    {
        var capacityCtor = concreteListType.GetConstructor(new[] { typeof(int) });
        if (capacityCtor != null)
        {
            var capacity = Expression.Parameter(typeof(int), "capacity");
            var body = Expression.New(capacityCtor, capacity);
            return Expression.Lambda<Func<int, IList>>(Expression.Convert(body, typeof(IList)), capacity).Compile();
        }

        var defaultCtor = concreteListType.GetConstructor(Type.EmptyTypes);
        if (defaultCtor != null)
        {
            var capacity = Expression.Parameter(typeof(int), "capacity");
            var body = Expression.New(defaultCtor);
            return Expression.Lambda<Func<int, IList>>(Expression.Convert(body, typeof(IList)), capacity).Compile();
        }

        return capacity => (IList)Activator.CreateInstance(concreteListType)!;
    }

    private static Func<IDictionary> CreateDictionaryFactory(Type concreteDictionaryType)
    {
        var defaultCtor = concreteDictionaryType.GetConstructor(Type.EmptyTypes);
        if (defaultCtor != null)
        {
            var body = Expression.New(defaultCtor);
            return Expression.Lambda<Func<IDictionary>>(Expression.Convert(body, typeof(IDictionary))).Compile();
        }

        return () => (IDictionary)Activator.CreateInstance(concreteDictionaryType)!;
    }

    private static object ToArray(Type elementType, IList list)
    {
        var array = Array.CreateInstance(elementType, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            array.SetValue(list[i], i);
        }

        return array;
    }

    private static Action<object, object?> CreateSetter(PropertyInfo property)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var convertedTarget = Expression.Convert(target, property.DeclaringType!);
        var convertedValue = Expression.Convert(value, property.PropertyType);
        var body = Expression.Assign(Expression.Property(convertedTarget, property), convertedValue);
        return Expression.Lambda<Action<object, object?>>(body, target, value).Compile();
    }

    private sealed class PropertyPlan
    {
        /// <summary>
        /// Initializes a property materialization plan.
        /// </summary>
        /// <param name="plan">The plan used to convert the property value.</param>
        /// <param name="setter">A compiled setter for assigning the converted value.</param>
        public PropertyPlan(TypePlan plan, Action<object, object?> setter)
        {
            Plan = plan;
            Setter = setter;
        }

        /// <summary>Gets the plan used to convert the property value.</summary>
        public TypePlan Plan { get; }

        /// <summary>Gets the compiled setter for assigning the property value.</summary>
        public Action<object, object?> Setter { get; }
    }

    /// <summary>
    /// Describes how a target CLR type is materialized from native nodes.
    /// </summary>
    private sealed class TypePlan
    {
        private TypePlan(Type type, PlanKind kind)
        {
            Type = type;
            Kind = kind;
        }

        /// <summary>Gets the target CLR type.</summary>
        public Type Type { get; }

        /// <summary>Gets the materialization category for the target type.</summary>
        public PlanKind Kind { get; }

        /// <summary>Gets whether this target type is supported by the native materializer.</summary>
        public bool IsSupported => Kind != PlanKind.Unsupported;

        /// <summary>Gets the underlying plan for nullable value types.</summary>
        public TypePlan? UnderlyingPlan { get; private set; }

        /// <summary>Gets the element or value plan for collections, arrays, and dictionaries.</summary>
        public TypePlan? ElementPlan { get; private set; }

        /// <summary>Gets the factory used to construct object targets.</summary>
        public Func<object>? Factory { get; private set; }

        /// <summary>Gets writable property plans keyed by encoded TOON field name.</summary>
        public Dictionary<string, PropertyPlan>? Properties { get; private set; }

        /// <summary>Gets the factory used to create mutable collection instances.</summary>
        public Func<int, IList>? CreateList { get; private set; }

        /// <summary>Gets the converter used to finalize mutable list storage into the target collection type.</summary>
        public Func<IList, object>? FinalizeList { get; private set; }

        /// <summary>Gets the factory used to create dictionary instances.</summary>
        public Func<IDictionary>? CreateDictionary { get; private set; }

        public static TypePlan Unsupported(Type type) => new TypePlan(type, PlanKind.Unsupported);

        public static TypePlan Primitive(Type type) => new TypePlan(type, PlanKind.Primitive);

        public static TypePlan Any(Type type) => new TypePlan(type, PlanKind.Any);

        public static TypePlan Nullable(Type type, TypePlan underlyingPlan)
        {
            return new TypePlan(type, PlanKind.Nullable)
            {
                UnderlyingPlan = underlyingPlan
            };
        }

        public static TypePlan Object(Type type, Func<object> factory, Dictionary<string, PropertyPlan> properties)
        {
            return new TypePlan(type, PlanKind.Object)
            {
                Factory = factory,
                Properties = properties
            };
        }

        public static TypePlan Collection(Type type, TypePlan elementPlan, Func<int, IList> createList, Func<IList, object> finalizeList)
        {
            return new TypePlan(type, PlanKind.Collection)
            {
                ElementPlan = elementPlan,
                CreateList = createList,
                FinalizeList = finalizeList
            };
        }

        public static TypePlan Array(Type type, TypePlan elementPlan, Func<int, IList> createList, Func<IList, object> finalizeList)
        {
            return new TypePlan(type, PlanKind.Array)
            {
                ElementPlan = elementPlan,
                CreateList = createList,
                FinalizeList = finalizeList
            };
        }

        public static TypePlan Dictionary(Type type, TypePlan valuePlan, Func<IDictionary> createDictionary)
        {
            return new TypePlan(type, PlanKind.Dictionary)
            {
                ElementPlan = valuePlan,
                CreateDictionary = createDictionary
            };
        }
    }

    private enum PlanKind
    {
        Unsupported,
        Primitive,
        Nullable,
        Object,
        Collection,
        Array,
        Dictionary,
        Any
    }
}
