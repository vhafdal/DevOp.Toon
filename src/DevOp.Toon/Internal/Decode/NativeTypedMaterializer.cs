#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode;

/// <summary>
/// Converts the internal native node graph into CLR target types after generic parsing has completed.
/// </summary>
internal static class NativeTypedMaterializer
{
    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<Type, TypePlan> PlanCache = new Dictionary<Type, TypePlan>();
    private const double MaxExactDoubleInteger = 9007199254740992d;
    // (double)decimal.MaxValue rounds up past decimal.MaxValue, so every double at or above it overflows the conversion.
    private const double MaxDecimalAsDouble = (double)decimal.MaxValue;
    private const decimal MaxExactParsedDecimalInteger = 1000000000000000m;
    private const string OutsideFiniteRange = "Number outside supported finite range";

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

    /// <summary>
    /// Returns whether typed decoding of <paramref name="type"/> on the native route binds <paramref name="key"/> to a declared
    /// member or constructor parameter; <see langword="false"/> when the type cannot be decoded.
    /// </summary>
    internal static bool BindsMember(Type type, string key)
    {
        TypePlan plan;
        try { plan = GetPlan(type); }
        catch (NotSupportedException) { return false; }

        return plan.Kind == PlanKind.Object &&
               (plan.Properties?.ContainsKey(key) == true || plan.ConstructorParameterLookup?.ContainsKey(key) == true ||
                plan.ConstructorMembers?.ContainsKey(key) == true);
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

            var publicProperties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
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
                    if (IsFrameworkObjectType(type))
                    {
                        return TypePlan.Unsupported(type);
                    }

                    return TryBuildConstructorObjectPlan(type, publicProperties, building, out var constructorPlan)
                        ? constructorPlan
                        : TypePlan.Unsupported(type);
                }

                factory = CreateFactory(ctor);
            }

            var extension = ExtensionDataAccessor.Find(type);
            var propertyPlans = new Dictionary<string, PropertyPlan>(StringComparer.Ordinal);
            HashSet<string>? unboundMembers = null;
            foreach (var property in publicProperties)
            {
                if (property.GetIndexParameters().Length != 0 || extension?.IsExtension(property) == true)
                {
                    continue;
                }

                if (!property.CanWrite)
                {
                    if (extension != null)
                    {
                        AddMemberNames(unboundMembers ??= new HashSet<string>(StringComparer.Ordinal), property);
                    }

                    continue;
                }

                var propertyPlan = BuildPlan(property.PropertyType, building);
                if (!propertyPlan.IsSupported)
                {
                    return TypePlan.Unsupported(type);
                }

                var propertyEntry = new PropertyPlan(propertyPlan, CreateSetter(property));
                propertyPlans[property.Name] = propertyEntry;

                var toonName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name;
                if (toonName is { Length: > 0 })
                {
                    propertyPlans[toonName] = propertyEntry;
                }
            }

            return TypePlan.Object(type, factory, propertyPlans).WithExtension(extension, -1, unboundMembers);
        }
        finally
        {
            building.Remove(type);
        }
    }

    private static bool IsFrameworkObjectType(Type type)
    {
        return type.Namespace != null &&
               (type.Namespace == "System" || type.Namespace.StartsWith("System.", StringComparison.Ordinal));
    }

    private static bool TryBuildConstructorObjectPlan(
        Type type,
        PropertyInfo[] properties,
        HashSet<Type> building,
        out TypePlan plan)
    {
        plan = TypePlan.Unsupported(type);
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        ConstructorParameterPlan[]? bestParameters = null;
        Dictionary<string, ConstructorParameterPlan>? bestLookup = null;
        ConstructorInfo? bestConstructor = null;
        bool ambiguous = false;

        for (int i = 0; i < constructors.Length; i++)
        {
            var constructor = constructors[i];
            var parameters = constructor.GetParameters();
            if (parameters.Length == 0)
            {
                continue;
            }

            if (!TryBuildConstructorParameters(constructor, properties, building, out var parameterPlans, out var lookup))
            {
                continue;
            }

            if (bestParameters == null || parameterPlans.Length > bestParameters.Length)
            {
                bestConstructor = constructor;
                bestParameters = parameterPlans;
                bestLookup = lookup;
                ambiguous = false;
            }
            else if (parameterPlans.Length == bestParameters.Length)
            {
                ambiguous = true;
            }
        }

        if (bestConstructor == null || bestParameters == null || bestLookup == null || ambiguous)
        {
            return false;
        }

        var extension = ExtensionDataAccessor.Find(type);
        var constructorMembers = BuildConstructorMemberPlans(properties, bestLookup, extension, building);
        var extensionArgument = -1;
        HashSet<string>? unboundMembers = null;
        if (extension != null)
        {
            extensionArgument = extension.FindConstructorArgument(bestConstructor.GetParameters());
            foreach (var property in properties)
            {
                if (property.GetIndexParameters().Length == 0 && !extension.IsExtension(property) &&
                    !bestLookup.ContainsKey(property.Name) && constructorMembers?.ContainsKey(property.Name) != true)
                {
                    AddMemberNames(unboundMembers ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase), property);
                }
            }
        }

        plan = TypePlan.Object(
            type,
            CreateConstructorFactory(bestConstructor),
            bestParameters,
            bestLookup).WithExtension(extension, extensionArgument, unboundMembers)
            .WithConstructorMembers(constructorMembers);
        return true;
    }

    /// <summary>
    /// Plans the settable members of a constructor-bound type that no constructor parameter covers; they are assigned
    /// after construction. Members whose type cannot be materialized stay unbound, as before.
    /// </summary>
    private static Dictionary<string, PropertyPlan>? BuildConstructorMemberPlans(
        PropertyInfo[] properties,
        Dictionary<string, ConstructorParameterPlan> lookup,
        ExtensionDataAccessor? extension,
        HashSet<Type> building)
    {
        Dictionary<string, PropertyPlan>? members = null;
        foreach (var property in properties)
        {
            if (!property.CanWrite || property.GetIndexParameters().Length != 0 || lookup.ContainsKey(property.Name) ||
                extension?.IsExtension(property) == true)
            {
                continue;
            }

            var propertyPlan = BuildPlan(property.PropertyType, building);
            if (!propertyPlan.IsSupported)
            {
                continue;
            }

            var entry = new PropertyPlan(propertyPlan, CreateSetter(property));
            members ??= new Dictionary<string, PropertyPlan>(StringComparer.OrdinalIgnoreCase);
            members[property.Name] = entry;
            var toonName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name;
            if (toonName is { Length: > 0 })
            {
                members[toonName] = entry;
            }
        }

        return members;
    }

    private static void AddMemberNames(HashSet<string> names, PropertyInfo property)
    {
        names.Add(property.Name);
        var toonName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name;
        if (toonName is { Length: > 0 })
        {
            names.Add(toonName);
        }
    }

    private static bool TryBuildConstructorParameters(
        ConstructorInfo constructor,
        PropertyInfo[] properties,
        HashSet<Type> building,
        out ConstructorParameterPlan[] parameterPlans,
        out Dictionary<string, ConstructorParameterPlan> lookup)
    {
        var parameters = constructor.GetParameters();
        parameterPlans = new ConstructorParameterPlan[parameters.Length];
        lookup = new Dictionary<string, ConstructorParameterPlan>(StringComparer.OrdinalIgnoreCase);
        var extensionArgument = ExtensionDataAccessor.Find(constructor.DeclaringType!)?.FindConstructorArgument(parameters) ?? -1;

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (string.IsNullOrEmpty(parameter.Name))
            {
                return false;
            }

            var parameterPlan = BuildPlan(parameter.ParameterType, building);
            if (!parameterPlan.IsSupported)
            {
                return false;
            }

            var parameterEntry = new ConstructorParameterPlan(
                i,
                parameter.Name!,
                parameterPlan,
                GetParameterDefaultValue(parameter));
            parameterPlans[i] = parameterEntry;
            if (i == extensionArgument)
            {
                continue;
            }

            AddConstructorAlias(lookup, parameter.Name!, parameterEntry);

            var property = FindPropertyForParameter(properties, parameter.Name!);
            if (property == null)
            {
                continue;
            }

            AddConstructorAlias(lookup, property.Name, parameterEntry);
            var toonName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name;
            if (toonName is { Length: > 0 })
            {
                AddConstructorAlias(lookup, toonName, parameterEntry);
            }
        }

        return true;
    }

    private static void AddConstructorAlias(
        Dictionary<string, ConstructorParameterPlan> lookup,
        string alias,
        ConstructorParameterPlan parameter)
    {
        if (alias.Length != 0)
        {
            lookup[alias] = parameter;
        }
    }

    private static PropertyInfo? FindPropertyForParameter(PropertyInfo[] properties, string parameterName)
    {
        for (int i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (property.GetIndexParameters().Length == 0 &&
                string.Equals(property.Name, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return property;
            }
        }

        return null;
    }

    private static object? GetParameterDefaultValue(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue &&
            parameter.DefaultValue != DBNull.Value &&
            parameter.DefaultValue != Type.Missing)
        {
            return parameter.DefaultValue;
        }

        return GetDefaultValue(parameter.ParameterType);
    }

    private static object? GetDefaultValue(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static bool TryBuildDictionaryPlan(Type type, HashSet<Type> building, out TypePlan plan)
    {
        plan = TypePlan.Unsupported(type);
        if (!DictionaryTargetShape.TryResolve(type, out var valueType, out var concreteDictionaryType))
        {
            return false;
        }

        if (valueType == null || concreteDictionaryType == null)
        {
            return true;
        }

        var valuePlan = BuildPlan(valueType, building);
        if (!valuePlan.IsSupported)
        {
            // Recursive value types keep the old behaviour; any other value type the plan cannot build rejects the target.
            return !building.Contains(valueType);
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

            if (plan.BindsScalarTokens)
            {
                value = plan.DefaultValue;
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
                if (node is NativePrimitiveNode { NumberText: { } floatingText } && (plan.Type == typeof(double) || plan.Type == typeof(float)))
                {
                    value = ParseFloatingText(floatingText, plan.Type);
                    return true;
                }

                if (node is NativePrimitiveNode number && IntegerRange(plan.Type) is { } integerRange &&
                    (TryConvertIntegerFromText(number, plan.Type, integerRange, out value) || TryConvertFraction(number, plan.Type, out value)))
                {
                    return true;
                }

                if (plan.BindsScalarTokens && node is NativePrimitiveNode { NumberText: null, Value: bool or string } scalar)
                {
                    value = scalar.Value is bool flag
                        ? NumericUtils.ConvertBoolean(plan.Type, flag)
                        : NumericUtils.ConvertText(plan.Type, (string)scalar.Value);
                    return true;
                }

                if (TryConvertPrimitive(node, plan.Type, out value))
                {
                    if (value is float single && float.IsInfinity(single))
                    {
                        throw ToonFormatException.Syntax(OutsideFiniteRange);
                    }

                    return true;
                }

                return node is NativePrimitiveNode primitive && TryConvertNumberOutsideType(primitive, plan.Type, out value);
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
        if (node is not NativeObjectNode objectNode)
        {
            value = null;
            return false;
        }

        if (plan.Factory == null || plan.Properties == null)
        {
            return TryConvertConstructorObject(objectNode, plan, out value);
        }

        var instance = plan.Factory();
        Dictionary<string, object?>? extensionEntries = null;
        foreach (var kvp in objectNode)
        {
            if (!plan.Properties.TryGetValue(kvp.Key, out var property))
            {
                if (plan.Extension != null)
                {
                    CaptureExtension(plan, kvp.Key, kvp.Value, ref extensionEntries);
                }

                continue;
            }

            if (!TryConvert(kvp.Value, property.Plan, out var propertyValue))
            {
                value = null;
                return false;
            }

            property.Setter(instance, propertyValue);
        }

        if (extensionEntries != null)
        {
            plan.Extension!.Populate(instance, extensionEntries);
        }

        value = instance;
        return true;
    }

    private static void CaptureExtension(TypePlan plan, string key, NativeNode? node, ref Dictionary<string, object?>? entries)
    {
        if (plan.UnboundMembers?.Contains(key) == true)
        {
            return;
        }

        (entries ??= new Dictionary<string, object?>(StringComparer.Ordinal))[key] = ExtensionDataAccessor.ToValue(node);
    }

    private static bool TryConvertConstructorObject(NativeObjectNode objectNode, TypePlan plan, out object? value)
    {
        if (plan.ConstructorFactory == null || plan.ConstructorParameters == null || plan.ConstructorParameterLookup == null)
        {
            value = null;
            return false;
        }

        var arguments = new object?[plan.ConstructorParameters.Length];
        var assigned = new bool[plan.ConstructorParameters.Length];
        Dictionary<string, object?>? extensionEntries = null;
        List<KeyValuePair<PropertyPlan, object?>>? pendingMembers = null;
        foreach (var kvp in objectNode)
        {
            if (!plan.ConstructorParameterLookup.TryGetValue(kvp.Key, out var parameter))
            {
                if (plan.ConstructorMembers != null && plan.ConstructorMembers.TryGetValue(kvp.Key, out var member))
                {
                    if (!TryConvert(kvp.Value, member.Plan, out var memberValue))
                    {
                        value = null;
                        return false;
                    }

                    (pendingMembers ??= new List<KeyValuePair<PropertyPlan, object?>>()).Add(new KeyValuePair<PropertyPlan, object?>(member, memberValue));
                    continue;
                }

                if (plan.Extension != null)
                {
                    CaptureExtension(plan, kvp.Key, kvp.Value, ref extensionEntries);
                }

                continue;
            }

            if (!TryConvert(kvp.Value, parameter.Plan, out var parameterValue))
            {
                value = null;
                return false;
            }

            arguments[parameter.Index] = parameterValue;
            assigned[parameter.Index] = true;
        }

        for (int i = 0; i < plan.ConstructorParameters.Length; i++)
        {
            if (!assigned[i])
            {
                arguments[i] = plan.ConstructorParameters[i].DefaultValue;
            }
        }

        if (extensionEntries != null && plan.ExtensionArgument >= 0)
        {
            arguments[plan.ExtensionArgument] = extensionEntries;
        }

        value = plan.ConstructorFactory(arguments);
        if (pendingMembers != null)
        {
            foreach (var entry in pendingMembers)
            {
                entry.Key.Setter(value, entry.Value);
            }
        }

        if (extensionEntries != null && plan.ExtensionArgument < 0)
        {
            plan.Extension!.Populate(value, extensionEntries);
        }

        return true;
    }

    private static bool TryConvertCollection(NativeNode? node, TypePlan plan, out object? value)
    {
        if (plan.ElementPlan?.Type == typeof(byte) &&
            node is NativePrimitiveNode { Value: string base64Text })
        {
            try
            {
                var bytes = Convert.FromBase64String(base64Text);
                if (plan.Kind == PlanKind.Array && plan.Type == typeof(byte[]))
                {
                    value = bytes;
                    return true;
                }

                var byteList = plan.CreateList!(bytes.Length);
                for (int i = 0; i < bytes.Length; i++)
                {
                    byteList.Add(bytes[i]);
                }

                value = plan.FinalizeList!(byteList);
                return true;
            }
            catch (FormatException)
            {
                value = null;
                return false;
            }
        }

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
        if (plan.ElementPlan == null || plan.CreateDictionary == null)
        {
            value = null;
            return false;
        }

        if (node is NativeArrayNode pairArray)
        {
            return TryConvertDictionaryPairs(pairArray, plan, out value);
        }

        if (node is not NativeObjectNode objectNode)
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

    private static bool TryConvertDictionaryPairs(NativeArrayNode arrayNode, TypePlan plan, out object? value)
    {
        var dictionary = plan.CreateDictionary!();
        foreach (var item in arrayNode)
        {
            if (item is not NativeObjectNode pair ||
                !TryGetObjectValue(pair, "Key", out var keyNode) ||
                !TryGetObjectValue(pair, "Value", out var valueNode) ||
                !TryConvert(keyNode, TypePlan.Primitive(typeof(string)), out var keyValue) ||
                keyValue is not string key ||
                !TryConvert(valueNode, plan.ElementPlan!, out var elementValue))
            {
                value = null;
                return false;
            }

            dictionary[key] = elementValue;
        }

        value = dictionary;
        return true;
    }

    private static bool TryGetObjectValue(NativeObjectNode objectNode, string key, out NativeNode? value)
    {
        foreach (var kvp in objectNode)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
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

            return TryConvertIntegerPrimitive(primitiveValue, int.MinValue, int.MaxValue, static candidate => (int)candidate, out value);
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

            // Text numbers arrive as double, or as a decimal of at most 15 significant digits; past those limits the value is no longer exact.
            if (primitiveValue is double doubleLong &&
                Math.Abs(doubleLong) <= MaxExactDoubleInteger &&
                Math.Abs(doubleLong % 1d) < double.Epsilon)
            {
                value = (long)doubleLong;
                return true;
            }

            if (primitiveValue is decimal decimalLong &&
                Math.Abs(decimalLong) < MaxExactParsedDecimalInteger &&
                decimal.Truncate(decimalLong) == decimalLong)
            {
                value = (long)decimalLong;
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
            // The parsed value may have been rounded through a double; the kept text is the exact number.
            if (primitive.NumberText is { } numberText &&
                decimal.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var exactDecimal))
            {
                value = exactDecimal;
                return true;
            }

            if (primitiveValue is decimal decimalValue)
            {
                value = decimalValue;
                return true;
            }

            if (primitiveValue is double decimalDouble)
            {
                if (double.IsNaN(decimalDouble) || Math.Abs(decimalDouble) >= MaxDecimalAsDouble)
                {
                    throw ToonFormatException.Syntax($"Number {decimalDouble.ToString("R", CultureInfo.InvariantCulture)} is outside the range of System.Decimal");
                }

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
            if (primitiveValue is int or long or ulong or double or decimal)
            {
                var numberText = primitive.NumberText ?? (primitiveValue is double number ? number.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(primitiveValue, CultureInfo.InvariantCulture)!);
                value = NumericUtils.BindEnumNumber(targetType, numberText.AsSpan());
                return true;
            }

            if (primitiveValue is string enumName && Enum.IsDefined(targetType, enumName))
            {
                value = Enum.Parse(targetType, enumName, false);
                return true;
            }

            value = null;
            return false;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Handles a number the primitive conversion refused for a numeric <paramref name="type"/>: a value the type cannot hold
    /// throws the format error the direct route reports, rather than surfacing as an unsupported target type, and a double
    /// target converts a number read as a decimal, and an integer type converts an integral value it can hold that is known
    /// exactly. A number the type could hold but that does not convert exactly, such as a fraction for an integer type or a
    /// text number past the exact double and parsed-decimal limits, and any non-number are still refused.
    /// </summary>
    private static bool TryConvertNumberOutsideType(NativePrimitiveNode primitive, Type type, out object? value)
    {
        value = null;
        var range = IntegerRange(type);
        if ((range == null && type != typeof(double) && type != typeof(float) && type != typeof(decimal)) ||
            !TryGetNumber(primitive, out var number, out var exact))
        {
            return false;
        }

        if (double.IsInfinity(number) || double.IsNaN(number) || (type == typeof(float) && float.IsInfinity((float)number)))
        {
            throw ToonFormatException.Syntax(OutsideFiniteRange);
        }

        if (type == typeof(double) || type == typeof(float))
        {
            value = type == typeof(double) ? (object)number : (float)number;
            return true;
        }

        if (exact is { } inRange && (range == null || (inRange >= range.Value.Min && inRange <= range.Value.Max)))
        {
            // An integral value that fits converts when it is known exactly; text numbers past the exact limits stay refused.
            if (range != null && decimal.Truncate(inRange) == inRange && IsExactNumber(primitive))
            {
                value = Convert.ChangeType(inRange, type, CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        throw ToonFormatException.Syntax($"Number {number.ToString("R", CultureInfo.InvariantCulture)} is outside the range of {type.FullName}");
    }

    /// <summary>
    /// Reads a double or float target from a number's kept source text, rounding once: the parsed value is a double, or a
    /// decimal built from one, and converting it again can round a second time.
    /// </summary>
    private static object ParseFloatingText(string text, Type type)
    {
        if (type == typeof(float))
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) || float.IsInfinity(single))
            {
                throw ToonFormatException.Syntax(OutsideFiniteRange);
            }

            return FloatUtils.NormalizeSignedZero(single);
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsInfinity(number))
        {
            throw ToonFormatException.Syntax(OutsideFiniteRange);
        }

        return FloatUtils.NormalizeSignedZero(number);
    }

    /// <summary>
    /// Reads an integer <paramref name="type"/> from a number's kept source text, before any conversion of the parsed value,
    /// which a double may have rounded (9007199254740993.0 parses as 9007199254740992). A whole number converts exactly or
    /// is outside the type's range; a fraction follows <see cref="TryConvertFraction"/>.
    /// </summary>
    private static bool TryConvertIntegerFromText(NativePrimitiveNode primitive, Type type, (decimal Min, decimal Max) range, out object? value)
    {
        value = null;
        if (primitive.NumberText is not { } text)
        {
            return false;
        }

        var parsed = decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact);
        if (parsed && !NumericUtils.IsIntegralNumberText(text))
        {
            throw ToonFormatException.Syntax(NumericUtils.NotAnInteger(text, type));
        }

        if (parsed && exact >= range.Min && exact <= range.Max)
        {
            value = Convert.ChangeType(exact, type, CultureInfo.InvariantCulture);
            return true;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsInfinity(number))
        {
            throw ToonFormatException.Syntax(OutsideFiniteRange);
        }

        throw ToonFormatException.Syntax($"Number {number.ToString("R", CultureInfo.InvariantCulture)} is outside the range of {type.FullName}");
    }

    /// <summary>
    /// Rejects a number with a fractional part for an integer <paramref name="type"/>, as the direct route and 4.x do. The
    /// fraction is judged from the kept source text when there is one, because the parse can round <c>1e-29</c> to zero.
    /// </summary>
    private static bool TryConvertFraction(NativePrimitiveNode primitive, Type type, out object? value)
    {
        value = null;
        if (!TryGetNumber(primitive, out var number, out var exact) || exact is not { } amount)
        {
            return false;
        }

        var fractional = primitive.NumberText is { } text
            ? !NumericUtils.IsIntegralNumberText(text)
            : decimal.Truncate(amount) != amount;
        if (!fractional)
        {
            return false;
        }

        throw ToonFormatException.Syntax(NumericUtils.NotAnInteger(primitive.NumberText ?? number.ToString("R", CultureInfo.InvariantCulture), type));
    }

    private static bool TryGetNumber(NativePrimitiveNode primitive, out double number, out decimal? exact)
    {
        exact = null;
        switch (primitive.Value)
        {
            case int intValue:
                number = intValue;
                exact = intValue;
                return true;
            case long longValue:
                number = longValue;
                exact = longValue;
                return true;
            case ulong ulongValue:
                number = ulongValue;
                exact = ulongValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                exact = decimalValue;
                return true;
            case double doubleValue:
                number = doubleValue;
                if (primitive.NumberText is { } numberText && decimal.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    exact = parsed;
                }
                else if (!double.IsNaN(doubleValue) && Math.Abs(doubleValue) < MaxDecimalAsDouble)
                {
                    exact = (decimal)doubleValue;
                }

                return true;
            // The legacy parser keeps a number beyond the double range as text.
            case string word when LooksLikeNumber(word) &&
                                  double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var huge) && double.IsInfinity(huge):
                number = huge;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool IsExactNumber(NativePrimitiveNode primitive) => primitive.Value switch
    {
        int or long or ulong => true,
        decimal decimalValue => Math.Abs(decimalValue) < MaxExactParsedDecimalInteger,
        double doubleValue => Math.Abs(doubleValue) <= MaxExactDoubleInteger,
        _ => false
    };

    private static bool LooksLikeNumber(string text)
    {
        var i = text.Length > 0 && text[0] == '-' ? 1 : 0;
        return i < text.Length && text[i] >= '0' && text[i] <= '9';
    }

    private static (decimal Min, decimal Max)? IntegerRange(Type type)
    {
        if (type == typeof(int)) return (int.MinValue, int.MaxValue);
        if (type == typeof(long)) return (long.MinValue, long.MaxValue);
        if (type == typeof(short)) return (short.MinValue, short.MaxValue);
        if (type == typeof(byte)) return (byte.MinValue, byte.MaxValue);
        if (type == typeof(sbyte)) return (sbyte.MinValue, sbyte.MaxValue);
        if (type == typeof(ushort)) return (ushort.MinValue, ushort.MaxValue);
        if (type == typeof(uint)) return (uint.MinValue, uint.MaxValue);
        if (type == typeof(ulong)) return (ulong.MinValue, ulong.MaxValue);
        return null;
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

    private static Func<object?[], object> CreateConstructorFactory(ConstructorInfo constructor)
    {
        var values = Expression.Parameter(typeof(object[]), "values");
        var parameters = constructor.GetParameters();
        var arguments = new Expression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var index = Expression.ArrayIndex(values, Expression.Constant(i));
            arguments[i] = Expression.Convert(index, parameters[i].ParameterType);
        }

        var body = Expression.New(constructor, arguments);
        return Expression.Lambda<Func<object?[], object>>(Expression.Convert(body, typeof(object)), values).Compile();
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
        var declaringType = property.DeclaringType!;
        Expression convertedTarget = declaringType.IsValueType
            ? Expression.Unbox(target, declaringType)
            : Expression.Convert(target, declaringType);
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
    /// Describes a constructor parameter that can be populated from a native object field.
    /// </summary>
    private sealed class ConstructorParameterPlan
    {
        public ConstructorParameterPlan(int index, string name, TypePlan plan, object? defaultValue)
        {
            Index = index;
            Name = name;
            Plan = plan;
            DefaultValue = defaultValue;
        }

        public int Index { get; }
        public string Name { get; }
        public TypePlan Plan { get; }
        public object? DefaultValue { get; }
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

        /// <summary>
        /// Gets whether this is a numeric or enum target, which binds <c>null</c>, <c>true</c>/<c>false</c> and text tokens
        /// as 4.x decoding does.
        /// </summary>
        public bool BindsScalarTokens { get; private set; }

        /// <summary>Gets the value a <c>null</c> token binds to when <see cref="BindsScalarTokens"/> is set.</summary>
        public object? DefaultValue { get; private set; }

        /// <summary>Gets the element or value plan for collections, arrays, and dictionaries.</summary>
        public TypePlan? ElementPlan { get; private set; }

        /// <summary>Gets the factory used to construct object targets.</summary>
        public Func<object>? Factory { get; private set; }

        /// <summary>Gets writable property plans keyed by encoded TOON field name.</summary>
        public Dictionary<string, PropertyPlan>? Properties { get; private set; }

        /// <summary>Gets the compiled constructor factory for constructor-bound object targets.</summary>
        public Func<object?[], object>? ConstructorFactory { get; private set; }

        /// <summary>Gets constructor parameter conversion plans in constructor argument order.</summary>
        public ConstructorParameterPlan[]? ConstructorParameters { get; private set; }

        /// <summary>Gets constructor parameters keyed by TOON field alias.</summary>
        public Dictionary<string, ConstructorParameterPlan>? ConstructorParameterLookup { get; private set; }

        /// <summary>Gets the factory used to create mutable collection instances.</summary>
        public Func<int, IList>? CreateList { get; private set; }

        /// <summary>Gets the converter used to finalize mutable list storage into the target collection type.</summary>
        public Func<IList, object>? FinalizeList { get; private set; }

        /// <summary>Gets the factory used to create dictionary instances.</summary>
        public Func<IDictionary>? CreateDictionary { get; private set; }

        /// <summary>Gets the accessor of the object's extension data property, when it declares one.</summary>
        public ExtensionDataAccessor? Extension { get; private set; }

        /// <summary>Gets the constructor argument index that receives extension data, or -1.</summary>
        public int ExtensionArgument { get; private set; } = -1;

        /// <summary>Gets the names of declared members the object cannot bind, which are never captured as extension data.</summary>
        public HashSet<string>? UnboundMembers { get; private set; }

        /// <summary>Gets settable members of a constructor-bound object that are assigned after construction.</summary>
        public Dictionary<string, PropertyPlan>? ConstructorMembers { get; private set; }

        public TypePlan WithConstructorMembers(Dictionary<string, PropertyPlan>? members)
        {
            ConstructorMembers = members;
            return this;
        }

        public TypePlan WithExtension(ExtensionDataAccessor? extension, int argument, HashSet<string>? unboundMembers)
        {
            Extension = extension;
            ExtensionArgument = argument;
            UnboundMembers = unboundMembers;
            return this;
        }

        public static TypePlan Unsupported(Type type) => new TypePlan(type, PlanKind.Unsupported);

        public static TypePlan Primitive(Type type)
        {
            var bindsScalarTokens = type.IsEnum || NumericUtils.IsNumericType(type);
            return new TypePlan(type, PlanKind.Primitive)
            {
                BindsScalarTokens = bindsScalarTokens,
                DefaultValue = bindsScalarTokens ? Activator.CreateInstance(type) : null
            };
        }

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

        public static TypePlan Object(
            Type type,
            Func<object?[], object> constructorFactory,
            ConstructorParameterPlan[] constructorParameters,
            Dictionary<string, ConstructorParameterPlan> constructorParameterLookup)
        {
            return new TypePlan(type, PlanKind.Object)
            {
                ConstructorFactory = constructorFactory,
                ConstructorParameters = constructorParameters,
                ConstructorParameterLookup = constructorParameterLookup
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
