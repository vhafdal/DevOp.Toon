#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode
{
    /// <summary>
    /// High-performance typed decoder that materializes CLR objects directly from scanned TOON lines without first building a native node graph.
    /// </summary>
    internal static class DirectMaterializer
    {
        private const string OutsideFiniteRange = "Number outside supported finite range";
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<Type, TypePlan> PlanCache = new Dictionary<Type, TypePlan>();
        private static readonly ConcurrentDictionary<string, ArrayHeaderParseResult> ArrayHeaderCache =
            new ConcurrentDictionary<string, ArrayHeaderParseResult>(StringComparer.Ordinal);

        /// <summary>
        /// Attempts to decode the value at the cursor into the requested CLR type using cached reflection and primitive setter plans.
        /// </summary>
        /// <typeparam name="T">The target CLR type.</typeparam>
        /// <param name="cursor">The cursor positioned at the value to decode.</param>
        /// <param name="options">Resolved decode options.</param>
        /// <param name="value">The decoded value when materialization succeeds.</param>
        /// <returns><see langword="true"/> when the direct path supports and materializes the target type; otherwise, <see langword="false"/>.</returns>
        public static bool TryDecode<T>(LineCursor cursor, ResolvedDecodeOptions options, out T? value)
        {
            var plan = GetPlan(typeof(T));
            if (!plan.IsSupported)
            {
                value = default;
                return false;
            }

            if (!TryReadRootValue(cursor, plan, options, out var boxed))
            {
                value = default;
                return false;
            }

            value = (T?)boxed;
            return true;
        }

        /// <summary>
        /// Returns whether typed legacy decoding of <paramref name="type"/> binds <paramref name="key"/> to a declared member or
        /// constructor parameter; <see langword="false"/> when the direct path cannot decode the type.
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

                if (!type.IsClass || type == typeof(string))
                {
                    return TypePlan.Unsupported(type);
                }

                return BuildObjectPlan(type, building);
            }
            finally
            {
                building.Remove(type);
            }
        }

        private static TypePlan BuildObjectPlan(Type type, HashSet<Type> building)
        {
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor != null)
            {
                var extension = ExtensionDataAccessor.Find(type);
                var writableProperties = new Dictionary<string, PropertyPlan>(StringComparer.Ordinal);
                var aliases = new List<PropertyAlias>(16);
                HashSet<string>? unboundMembers = null;
                for (int i = 0; i < properties.Length; i++)
                {
                    var property = properties[i];
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

                    var propertyEntry = new PropertyPlan(
                        propertyPlan,
                        CreateSetter(property),
                        CreatePrimitiveSetter(property, propertyPlan));
                    var toonName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name;
                    writableProperties[property.Name] = propertyEntry;
                    aliases.Add(new PropertyAlias(property.Name, propertyEntry));
                    if (toonName is { Length: > 0 })
                    {
                        writableProperties[toonName] = propertyEntry;
                        aliases.Add(new PropertyAlias(toonName, propertyEntry));
                    }
                }

                var propertyAliases = aliases.ToArray();
                return TypePlan.Object(type, CreateFactory(ctor), writableProperties, propertyAliases, BuildAliasBuckets(propertyAliases))
                    .WithExtension(extension, -1, unboundMembers);
            }

            if (IsFrameworkObjectType(type))
            {
                return TypePlan.Unsupported(type);
            }

            return TryBuildConstructorObjectPlan(type, properties, building, out var constructorPlan)
                ? constructorPlan
                : TypePlan.Unsupported(type);
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
        /// after construction. Members whose type the direct path cannot build stay unbound, as before.
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

                var entry = new PropertyPlan(propertyPlan, CreateSetter(property), null);
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

        /// <summary>
        /// Parses the array header after a list item's hyphen as a keyed object field, with the key range relative to the
        /// whole line; <see langword="false"/> for a keyless header, which is the item itself.
        /// </summary>
        private static bool TryGetListItemFieldHeader(string content, int itemStart, out ParsedFieldInfo field)
        {
            field = null!;
            var header = Parser.ParseArrayHeaderLine(content.Substring(itemStart), Constants.DEFAULT_DELIMITER_CHAR);
            if (header == null || (header.HasKeyRange ? header.KeyEndExclusive <= header.KeyStart : string.IsNullOrEmpty(header.Header.Key)))
            {
                return false;
            }

            field = ParsedFieldInfo.FromArrayHeader(new ArrayHeaderParseResult
            {
                Header = header.Header,
                InlineValues = header.InlineValues,
                KeyStart = header.HasKeyRange ? header.KeyStart + itemStart : -1,
                KeyEndExclusive = header.HasKeyRange ? header.KeyEndExclusive + itemStart : -1
            });
            return true;
        }

        // A list item's first field sits on the hyphen line, so the rows of an array header there are one level deeper
        // than for an ordinary field at the item's depth.
        private static int ListItemArrayDepth(int baseDepth, int contentStart) => contentStart > 0 ? baseDepth + 1 : baseDepth;

        private static bool TryReadConstructorMemberField(
            TypePlan plan,
            object?[] arguments,
            string key,
            LineCursor cursor,
            string content,
            int baseDepth,
            ResolvedDecodeOptions options,
            ParsedFieldInfo field,
            out bool valueRead)
        {
            if (plan.ConstructorMembers == null || !plan.ConstructorMembers.TryGetValue(key, out var member))
            {
                valueRead = false;
                return false;
            }

            valueRead = TryReadFieldValue(cursor, content, baseDepth, member.Plan, options, field, out var value);
            if (valueRead)
            {
                AddPendingMember(plan, arguments, member, value);
            }

            return true;
        }

        private static void AddPendingMember(TypePlan plan, object?[] arguments, PropertyPlan member, object? value)
        {
            var slot = plan.ConstructorMemberSlot;
            var pending = arguments[slot] as List<KeyValuePair<PropertyPlan, object?>>;
            if (pending == null)
            {
                pending = new List<KeyValuePair<PropertyPlan, object?>>();
                arguments[slot] = pending;
            }

            pending.Add(new KeyValuePair<PropertyPlan, object?>(member, value));
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
            if (alias.Length == 0)
            {
                return;
            }

            lookup[alias] = parameter;
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
                // A recognized container whose values this path cannot build must not be read as a plain object:
                // leave the whole target to the native materializer. Recursive element types keep the old behaviour.
                return !building.Contains(valueType);
            }

            plan = TypePlan.Dictionary(type, valuePlan, CreateDictionaryFactory(concreteDictionaryType));
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
                    return !building.Contains(elementType);
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
                return !building.Contains(elementTypeForList);
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

        private static bool TryReadRootValue(LineCursor cursor, TypePlan plan, ResolvedDecodeOptions options, out object? value)
        {
            var first = cursor.Peek();
            if (first.IsNone)
            {
                value = null;
                return false;
            }

            if (Parser.IsArrayHeaderAfterHyphen(first.Content))
            {
                var headerInfo = GetOrParseArrayHeaderLine(first.Content);
                if (headerInfo != null)
                {
                    cursor.Advance();
                    if (plan.Kind == PlanKind.Dictionary)
                    {
                        return TryReadDictionaryFromHeader(cursor, plan, headerInfo.Header, headerInfo.InlineValues, 0, options, out value);
                    }

                    return TryReadArrayFromHeader(cursor, plan, headerInfo.Header, headerInfo.InlineValues, 0, options, out value);
                }
            }

            if (cursor.Length == 1 && !IsKeyValueLine(first.Content))
            {
                return TryReadPrimitiveLikeValue(plan, first.Content, 0, first.Content.Length, out value);
            }

            if (plan.Kind == PlanKind.Object)
            {
                return TryReadObject(cursor, plan, 0, options, out value);
            }

            if (plan.Kind == PlanKind.Dictionary)
            {
                return TryReadDictionary(cursor, plan, 0, options, out value);
            }

            if (plan.Kind == PlanKind.Any)
            {
                return TryReadUntypedObject(cursor, 0, options, out value);
            }

            value = null;
            return false;
        }

        private static bool IsKeyValueLine(string content)
        {
            if (content.StartsWith("\"", StringComparison.Ordinal))
            {
                var closingQuoteIndex = StringUtils.FindClosingQuote(content, 0);
                if (closingQuoteIndex == -1)
                {
                    return false;
                }

                return content.Substring(closingQuoteIndex + 1).Contains(Constants.COLON);
            }

            return content.Contains(Constants.COLON);
        }

        private static bool TryReadObject(LineCursor cursor, TypePlan plan, int baseDepth, ResolvedDecodeOptions options, out object? value)
        {
            if (plan.Factory == null || plan.Properties == null)
            {
                if (plan.ConstructorFactory != null && plan.ConstructorParameters != null && plan.ConstructorParameterLookup != null)
                {
                    return TryReadConstructorObject(cursor, plan, baseDepth, options, out value);
                }

                value = null;
                return false;
            }

            var instance = plan.Factory();
            Dictionary<string, object?>? extensionEntries = null;
            int? computedDepth = null;

            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < baseDepth)
                {
                    break;
                }

                if (computedDepth == null && line.Depth >= baseDepth)
                {
                    computedDepth = line.Depth;
                }

                if (line.Depth != computedDepth)
                {
                    break;
                }

                cursor.Advance();
                ReadObjectField(instance, cursor, line.Content, computedDepth.Value, plan, ref extensionEntries, options);
            }

            if (extensionEntries != null)
            {
                plan.Extension!.Populate(instance, extensionEntries);
            }

            value = instance;
            return true;
        }

        private static bool TryReadConstructorObject(LineCursor cursor, TypePlan plan, int baseDepth, ResolvedDecodeOptions options, out object? value)
        {
            if (!TryCreateConstructorBuffer(plan, out var arguments, out var assigned))
            {
                value = null;
                return false;
            }

            int? computedDepth = null;

            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < baseDepth)
                {
                    break;
                }

                if (computedDepth == null && line.Depth >= baseDepth)
                {
                    computedDepth = line.Depth;
                }

                if (line.Depth != computedDepth)
                {
                    break;
                }

                cursor.Advance();
                if (!ReadConstructorObjectField(arguments, assigned, cursor, line.Content, computedDepth.Value, plan, options))
                {
                    value = null;
                    return false;
                }
            }

            value = CreateConstructorInstance(plan, arguments, assigned);
            return true;
        }

        private static bool ReadConstructorObjectField(
            object?[] arguments,
            bool[] assigned,
            LineCursor cursor,
            string content,
            int baseDepth,
            TypePlan plan,
            ResolvedDecodeOptions options,
            ParsedFieldInfo? parsedField = null,
            int contentStart = 0)
        {
            var field = parsedField ?? ParseFieldContent(content, contentStart);
            if (field.ArrayHeader != null)
            {
                var arrayDepth = ListItemArrayDepth(baseDepth, contentStart);
                var key = GetArrayHeaderKey(content, field.ArrayHeader);
                if (!TryGetConstructorParameter(plan, key, out var parameter))
                {
                    if (TryReadConstructorMemberField(plan, arguments, key, cursor, content, arrayDepth, options, field, out var memberRead))
                    {
                        if (!memberRead)
                        {
                            SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, arrayDepth, options);
                        }

                        return memberRead;
                    }

                    if (plan.Extension == null || !TryCaptureConstructorExtensionField(plan, arguments, key, cursor, content, contentStart, baseDepth, options))
                    {
                        SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, arrayDepth, options);
                    }

                    return true;
                }

                object? arrayValue;
                var valueRead = parameter.Plan.Kind == PlanKind.Dictionary
                    ? TryReadDictionaryFromHeader(cursor, parameter.Plan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, arrayDepth, options, out arrayValue)
                    : TryReadArrayFromHeader(cursor, parameter.Plan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, arrayDepth, options, out arrayValue);
                if (!valueRead)
                {
                    SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, arrayDepth, options);
                    return false;
                }

                SetConstructorArgument(arguments, assigned, parameter, arrayValue);
                return true;
            }

            var fieldKey = GetFieldKey(content, field);
            if (!TryGetConstructorParameter(plan, fieldKey, out var parameterPlan))
            {
                if (TryReadConstructorMemberField(plan, arguments, fieldKey, cursor, content, baseDepth, options, field, out var memberRead))
                {
                    return memberRead;
                }

                if (plan.Extension != null && TryCaptureConstructorExtensionField(plan, arguments, fieldKey, cursor, content, contentStart, baseDepth, options))
                {
                    return true;
                }

                if (field.ValueStart >= content.Length)
                {
                    SkipValue(cursor, field.ValueStart, baseDepth, options);
                }

                return true;
            }

            if (field.ValueStart >= content.Length)
            {
                var nextLine = cursor.Peek();
                if (!nextLine.IsNone && nextLine.Depth > baseDepth)
                {
                    if (!TryReadNestedValue(cursor, parameterPlan.Plan, baseDepth + 1, options, out var nestedValue))
                    {
                        SkipObject(cursor, baseDepth + 1, options);
                        return false;
                    }

                    SetConstructorArgument(arguments, assigned, parameterPlan, nestedValue);
                    return true;
                }

                if (TryCreateEmptyValue(parameterPlan.Plan, out var emptyValue))
                {
                    SetConstructorArgument(arguments, assigned, parameterPlan, emptyValue);
                }

                return true;
            }

            if (!TryReadPrimitiveLikeValue(parameterPlan.Plan, content, field.ValueStart, content.Length, out var primitiveValue))
            {
                return false;
            }

            SetConstructorArgument(arguments, assigned, parameterPlan, primitiveValue);
            return true;
        }

        private static bool TryReadDictionary(LineCursor cursor, TypePlan plan, int baseDepth, ResolvedDecodeOptions options, out object? value)
        {
            if (plan.CreateDictionary == null || plan.ElementPlan == null)
            {
                value = null;
                return false;
            }

            var dictionary = plan.CreateDictionary();
            int? computedDepth = null;

            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < baseDepth)
                {
                    break;
                }

                if (computedDepth == null && line.Depth >= baseDepth)
                {
                    computedDepth = line.Depth;
                }

                if (line.Depth != computedDepth)
                {
                    break;
                }

                cursor.Advance();
                if (!ReadDictionaryField(dictionary, cursor, line.Content, computedDepth.Value, plan, options))
                {
                    value = null;
                    return false;
                }
            }

            value = dictionary;
            return true;
        }

        private static bool TryReadDictionaryFromHeader(
            LineCursor cursor,
            TypePlan plan,
            ArrayHeaderInfo header,
            string? inlineValues,
            int baseDepth,
            ResolvedDecodeOptions options,
            out object? value)
        {
            if (plan.Kind != PlanKind.Dictionary ||
                plan.CreateDictionary == null ||
                plan.ElementPlan == null ||
                inlineValues != null ||
                header.Fields == null)
            {
                value = null;
                return false;
            }

            var keyIndex = FindFieldIndex(header.Fields, "Key");
            var valueIndex = FindFieldIndex(header.Fields, "Value");
            if (keyIndex < 0)
            {
                value = null;
                return false;
            }

            var dictionary = plan.CreateDictionary();
            var tokenRanges = new Parser.TokenRange[header.Fields.Count];
            var rowDepth = baseDepth + 1;
            int rowCount = 0;
            while (!cursor.AtEnd() && rowCount < header.Length)
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth != rowDepth)
                {
                    break;
                }

                cursor.Advance();
                var contentSpan = line.ContentSpan;
                var contentOffset = line.ContentStart;
                if (!Parser.TryParseDelimitedValueRanges(contentSpan, header.Delimiter, tokenRanges, out var valueCount) ||
                    valueCount != header.Fields.Count)
                {
                    value = null;
                    return false;
                }

                var keyRange = tokenRanges[keyIndex];
                var key = ReadStringValue(line.SourceString, contentOffset + keyRange.Start, contentOffset + keyRange.EndExclusive);
                var followDepth = rowDepth + 1;
                object? itemValue;
                if (valueIndex >= 0)
                {
                    var valueRange = tokenRanges[valueIndex];
                    if (!TryReadPrimitiveLikeValue(plan.ElementPlan, line.SourceString, contentOffset + valueRange.Start, contentOffset + valueRange.EndExclusive, out itemValue, alreadyTrimmed: true))
                    {
                        value = null;
                        return false;
                    }
                }
                else if (!TryReadDictionaryHeaderContinuationValue(cursor, plan.ElementPlan, followDepth, options, out itemValue))
                {
                    value = null;
                    return false;
                }

                dictionary[key] = itemValue;

                while (!cursor.AtEnd())
                {
                    var continuation = cursor.Peek();
                    if (continuation.IsNone || continuation.Depth < followDepth)
                    {
                        break;
                    }

                    if (continuation.Depth == followDepth && !IsListItemLine(continuation.Content))
                    {
                        cursor.Advance();
                        SkipObjectField(cursor, continuation.Content, followDepth, options);
                        continue;
                    }

                    break;
                }

                rowCount++;
            }

            if (rowCount != header.Length)
            {
                value = null;
                return false;
            }

            value = dictionary;
            return true;
        }

        private static bool TryReadDictionaryHeaderContinuationValue(
            LineCursor cursor,
            TypePlan valuePlan,
            int followDepth,
            ResolvedDecodeOptions options,
            out object? value)
        {
            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < followDepth)
                {
                    break;
                }

                if (line.Depth != followDepth || IsListItemLine(line.Content))
                {
                    break;
                }

                cursor.Advance();
                var field = ParseFieldContent(line.Content, 0);
                var fieldKey = field.ArrayHeader != null
                    ? GetArrayHeaderKey(line.Content, field.ArrayHeader)
                    : GetFieldKey(line.Content, field);
                if (!string.Equals(fieldKey, "Value", StringComparison.OrdinalIgnoreCase))
                {
                    SkipObjectField(cursor, line.Content, followDepth, options, field);
                    continue;
                }

                return TryReadFieldValue(cursor, line.Content, followDepth, valuePlan, options, field, out value);
            }

            return TryCreateEmptyValue(valuePlan, out value);
        }

        private static bool TryReadFieldValue(
            LineCursor cursor,
            string content,
            int baseDepth,
            TypePlan plan,
            ResolvedDecodeOptions options,
            ParsedFieldInfo field,
            out object? value)
        {
            if (field.ArrayHeader != null)
            {
                if (plan.Kind == PlanKind.Dictionary)
                {
                    return TryReadDictionaryFromHeader(cursor, plan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options, out value);
                }

                return TryReadArrayFromHeader(cursor, plan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options, out value);
            }

            if (field.ValueStart >= content.Length)
            {
                var nextLine = cursor.Peek();
                if (!nextLine.IsNone && nextLine.Depth > baseDepth)
                {
                    return TryReadNestedValue(cursor, plan, baseDepth + 1, options, out value);
                }

                return TryCreateEmptyValue(plan, out value);
            }

            return TryReadPrimitiveLikeValue(plan, content, field.ValueStart, content.Length, out value);
        }

        private static bool ReadDictionaryField(
            IDictionary dictionary,
            LineCursor cursor,
            string content,
            int baseDepth,
            TypePlan plan,
            ResolvedDecodeOptions options,
            ParsedFieldInfo? parsedField = null,
            int contentStart = 0)
        {
            var valuePlan = plan.ElementPlan!;
            var field = parsedField ?? ParseFieldContent(content, contentStart);
            if (field.ArrayHeader != null)
            {
                var key = GetArrayHeaderKey(content, field.ArrayHeader);
                object? arrayValue;
                var valueRead = valuePlan.Kind == PlanKind.Dictionary
                    ? TryReadDictionaryFromHeader(cursor, valuePlan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options, out arrayValue)
                    : TryReadArrayFromHeader(cursor, valuePlan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options, out arrayValue);
                if (!valueRead)
                {
                    SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options);
                    return false;
                }

                dictionary[key] = arrayValue;
                return true;
            }

            var fieldKey = GetFieldKey(content, field);
            if (field.ValueStart >= content.Length)
            {
                var nextLine = cursor.Peek();
                if (!nextLine.IsNone && nextLine.Depth > baseDepth)
                {
                    if (!TryReadNestedValue(cursor, valuePlan, baseDepth + 1, options, out var nestedValue))
                    {
                        SkipObject(cursor, baseDepth + 1, options);
                        return false;
                    }

                    dictionary[fieldKey] = nestedValue;
                    return true;
                }

                if (TryCreateEmptyValue(valuePlan, out var emptyValue))
                {
                    dictionary[fieldKey] = emptyValue;
                    return true;
                }

                return false;
            }

            if (!TryReadPrimitiveLikeValue(valuePlan, content, field.ValueStart, content.Length, out var primitiveValue))
            {
                return false;
            }

            dictionary[fieldKey] = primitiveValue;
            return true;
        }

        private static void ReadObjectField(
            object instance,
            LineCursor cursor,
            string content,
            int baseDepth,
            TypePlan plan,
            ref Dictionary<string, object?>? extensionEntries,
            ResolvedDecodeOptions options,
            ParsedFieldInfo? parsedField = null,
            int contentStart = 0)
        {
            var field = parsedField ?? ParseFieldContent(content, contentStart);
            if (field.ArrayHeader != null)
            {
                var arrayDepth = ListItemArrayDepth(baseDepth, contentStart);
                if (!TryGetProperty(plan, content, field.ArrayHeader, out var property))
                {
                    if (plan.Extension == null ||
                        !TryCaptureExtensionField(plan, ref extensionEntries, GetArrayHeaderKey(content, field.ArrayHeader), cursor, content, contentStart, baseDepth, options))
                    {
                        SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, arrayDepth, options);
                    }

                    return;
                }

                if (TryReadArrayFromHeader(cursor, property.Plan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, arrayDepth, options, out var arrayValue))
                {
                    property.Setter(instance, arrayValue);
                    return;
                }

                SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, arrayDepth, options);
                return;
            }

            if (!TryGetProperty(plan, content, field, out var propertyPlan))
            {
                if (plan.Extension != null &&
                    TryCaptureExtensionField(plan, ref extensionEntries, GetFieldKey(content, field), cursor, content, contentStart, baseDepth, options))
                {
                    return;
                }

                if (field.ValueStart >= content.Length)
                {
                    SkipValue(cursor, field.ValueStart, baseDepth, options);
                }

                return;
            }

            if (field.ValueStart >= content.Length)
            {
                var nextLine = cursor.Peek();
                if (!nextLine.IsNone && nextLine.Depth > baseDepth)
                {
                    if (TryReadNestedValue(cursor, propertyPlan.Plan, baseDepth + 1, options, out var nestedValue))
                    {
                        propertyPlan.Setter(instance, nestedValue);
                        return;
                    }

                    SkipObject(cursor, baseDepth + 1, options);
                    return;
                }

                if (TryCreateEmptyValue(propertyPlan.Plan, out var emptyValue))
                {
                    propertyPlan.Setter(instance, emptyValue);
                }

                return;
            }

            if (propertyPlan.TrySetPrimitiveLikeValue(instance, content, field.ValueStart, content.Length))
            {
                return;
            }

            if (TryReadPrimitiveLikeValue(propertyPlan.Plan, content, field.ValueStart, content.Length, out var primitiveValue))
            {
                propertyPlan.Setter(instance, primitiveValue);
                return;
            }

            if (field.ValueStart >= content.Length)
            {
                SkipValue(cursor, field.ValueStart, baseDepth, options);
            }
        }

        private static bool TryReadNestedValue(LineCursor cursor, TypePlan plan, int baseDepth, ResolvedDecodeOptions options, out object? value)
        {
            if (plan.Kind == PlanKind.Object)
            {
                return TryReadObject(cursor, plan, baseDepth, options, out value);
            }

            if (plan.Kind == PlanKind.Dictionary)
            {
                return TryReadDictionary(cursor, plan, baseDepth, options, out value);
            }

            if (plan.Kind == PlanKind.Any)
            {
                return TryReadUntypedObject(cursor, baseDepth, options, out value);
            }

            value = null;
            return false;
        }

        private static bool TryReadArrayFromHeader(
            LineCursor cursor,
            TypePlan plan,
            ArrayHeaderInfo header,
            string? inlineValues,
            int baseDepth,
            ResolvedDecodeOptions options,
            out object? value)
        {
            if (plan.Kind != PlanKind.Collection && plan.Kind != PlanKind.Array && plan.Kind != PlanKind.Any)
            {
                value = null;
                return false;
            }

            if (plan.Kind == PlanKind.Any)
            {
                value = ReadUntypedArray(cursor, header, inlineValues, baseDepth, options);
                return true;
            }

            if (plan.CreateList == null || plan.FinalizeCollection == null || plan.ElementPlan == null)
            {
                value = null;
                return false;
            }

            var items = plan.CreateList(Math.Min(header.Length, 4096));

            if (inlineValues != null)
            {
                if (!TryReadInlineArrayValues(items, plan.ElementPlan, header, inlineValues))
                {
                    value = null;
                    return false;
                }

                value = plan.FinalizeCollection(items);
                return true;
            }

            if (header.Fields != null && header.Fields.Count > 0)
            {
                if (!TryReadTabularArray(cursor, items, plan.ElementPlan, header, baseDepth, options))
                {
                    value = null;
                    return false;
                }

                value = plan.FinalizeCollection(items);
                return true;
            }

            if (!TryReadListArray(cursor, items, plan.ElementPlan, header, baseDepth, options))
            {
                value = null;
                return false;
            }

            value = plan.FinalizeCollection(items);
            return true;
        }

        private static bool TryReadInlineArrayValues(IList items, TypePlan elementPlan, ArrayHeaderInfo header, string inlineValues)
        {
            if (string.IsNullOrWhiteSpace(inlineValues))
            {
                return header.Length == 0;
            }

            var tokenRanges = new Parser.TokenRange[Math.Min(header.Length, inlineValues.Length + 1)];
            if (!Parser.TryParseDelimitedValueRanges(inlineValues, header.Delimiter, tokenRanges, out var valueCount) ||
                valueCount != header.Length)
            {
                return false;
            }

            for (int i = 0; i < valueCount; i++)
            {
                var tokenRange = tokenRanges[i];
                if (!TryReadPrimitiveLikeValue(elementPlan, inlineValues, tokenRange.Start, tokenRange.EndExclusive, out var item, alreadyTrimmed: true))
                {
                    return false;
                }

                items.Add(item);
            }

            return true;
        }

        private static bool TryReadTabularArray(
            LineCursor cursor,
            IList items,
            TypePlan elementPlan,
            ArrayHeaderInfo header,
            int baseDepth,
            ResolvedDecodeOptions options)
        {
            if (elementPlan.Kind != PlanKind.Object || header.Fields == null)
            {
                return false;
            }

            if (elementPlan.Factory == null)
            {
                return TryReadConstructorTabularArray(cursor, items, elementPlan, header, baseDepth, options);
            }

            var columnPlans = GetOrCreateColumnPlans(elementPlan, header.Fields);
            var tokenRanges = new Parser.TokenRange[columnPlans.Length];
            var rowDepth = baseDepth + 1;
            int rowCount = 0;
            while (!cursor.AtEnd() && rowCount < header.Length)
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth != rowDepth)
                {
                    break;
                }

                cursor.Advance();
                // Use ContentSpan (zero-alloc) for token splitting; pass SourceString + adjusted
                // offsets to primitive setters so Content is never materialised for numeric rows.
                var contentSpan = line.ContentSpan;
                var contentOffset = line.ContentStart;
                if (!Parser.TryParseDelimitedValueRanges(contentSpan, header.Delimiter, tokenRanges, out var valueCount) ||
                    valueCount != columnPlans.Length)
                {
                    return false;
                }

                var instance = elementPlan.Factory();
                Dictionary<string, object?>? extensionEntries = null;
                for (int i = 0; i < columnPlans.Length; i++)
                {
                    var property = columnPlans[i];
                    if (property == null)
                    {
                        if (elementPlan.Extension != null)
                        {
                            CaptureExtensionCell(elementPlan, ref extensionEntries, header.Fields[i], line.SourceString, contentOffset + tokenRanges[i].Start, contentOffset + tokenRanges[i].EndExclusive);
                        }

                        continue;
                    }

                    var tokenRange = tokenRanges[i];
                    // Ranges from TryParseDelimitedValueRanges are relative to ContentSpan[0];
                    // shift by contentOffset to get absolute positions within SourceString.
                    if (property.TrySetPrimitiveLikeValue(instance, line.SourceString, contentOffset + tokenRange.Start, contentOffset + tokenRange.EndExclusive, alreadyTrimmed: true))
                    {
                        continue;
                    }

                    if (!TryReadPrimitiveLikeValue(property.Plan, line.SourceString, contentOffset + tokenRange.Start, contentOffset + tokenRange.EndExclusive, out var fieldValue, alreadyTrimmed: true))
                    {
                        return false;
                    }

                    property.Setter(instance, fieldValue);
                }

                var followDepth = rowDepth + 1;
                while (!cursor.AtEnd())
                {
                    var continuation = cursor.Peek();
                    if (continuation.IsNone || continuation.Depth < followDepth)
                    {
                        break;
                    }

                    if (continuation.Depth == followDepth && !IsListItemLine(continuation.Content))
                    {
                        cursor.Advance();
                        ReadObjectField(instance, cursor, continuation.Content, followDepth, elementPlan, ref extensionEntries, options);
                        continue;
                    }

                    break;
                }

                if (extensionEntries != null)
                {
                    elementPlan.Extension!.Populate(instance, extensionEntries);
                }

                items.Add(instance);
                rowCount++;
            }

            return rowCount == header.Length;
        }

        private static bool TryReadConstructorTabularArray(
            LineCursor cursor,
            IList items,
            TypePlan elementPlan,
            ArrayHeaderInfo header,
            int baseDepth,
            ResolvedDecodeOptions options)
        {
            if (elementPlan.ConstructorFactory == null || elementPlan.ConstructorParameters == null || header.Fields == null)
            {
                return false;
            }

            var columnPlans = GetOrCreateConstructorColumnPlans(elementPlan, header.Fields);
            var memberColumns = elementPlan.ConstructorMembers == null ? null : GetConstructorMemberColumns(elementPlan.ConstructorMembers, header.Fields);
            var tokenRanges = new Parser.TokenRange[columnPlans.Length];
            var rowDepth = baseDepth + 1;
            int rowCount = 0;
            while (!cursor.AtEnd() && rowCount < header.Length)
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth != rowDepth)
                {
                    break;
                }

                cursor.Advance();
                var contentSpan = line.ContentSpan;
                var contentOffset = line.ContentStart;
                if (!Parser.TryParseDelimitedValueRanges(contentSpan, header.Delimiter, tokenRanges, out var valueCount) ||
                    valueCount != columnPlans.Length)
                {
                    return false;
                }

                if (!TryCreateConstructorBuffer(elementPlan, out var arguments, out var assigned))
                {
                    return false;
                }

                for (int i = 0; i < columnPlans.Length; i++)
                {
                    var parameter = columnPlans[i];
                    if (parameter == null)
                    {
                        if (memberColumns?[i] is { } member)
                        {
                            if (!TryReadPrimitiveLikeValue(member.Plan, line.SourceString, contentOffset + tokenRanges[i].Start, contentOffset + tokenRanges[i].EndExclusive, out var memberValue, alreadyTrimmed: true))
                            {
                                return false;
                            }

                            AddPendingMember(elementPlan, arguments, member, memberValue);
                            continue;
                        }

                        if (elementPlan.Extension != null)
                        {
                            var slot = elementPlan.ConstructorParameters.Length;
                            var extensionEntries = arguments[slot] as Dictionary<string, object?>;
                            CaptureExtensionCell(elementPlan, ref extensionEntries, header.Fields[i], line.SourceString, contentOffset + tokenRanges[i].Start, contentOffset + tokenRanges[i].EndExclusive);
                            arguments[slot] = extensionEntries;
                        }

                        continue;
                    }

                    var tokenRange = tokenRanges[i];
                    if (!TryReadPrimitiveLikeValue(parameter.Plan, line.SourceString, contentOffset + tokenRange.Start, contentOffset + tokenRange.EndExclusive, out var fieldValue, alreadyTrimmed: true))
                    {
                        return false;
                    }

                    SetConstructorArgument(arguments, assigned, parameter, fieldValue);
                }

                var followDepth = rowDepth + 1;
                while (!cursor.AtEnd())
                {
                    var continuation = cursor.Peek();
                    if (continuation.IsNone || continuation.Depth < followDepth)
                    {
                        break;
                    }

                    if (continuation.Depth == followDepth && !IsListItemLine(continuation.Content))
                    {
                        cursor.Advance();
                        if (!ReadConstructorObjectField(arguments, assigned, cursor, continuation.Content, followDepth, elementPlan, options))
                        {
                            return false;
                        }

                        continue;
                    }

                    break;
                }

                items.Add(CreateConstructorInstance(elementPlan, arguments, assigned));
                rowCount++;
            }

            return rowCount == header.Length;
        }

        private static PropertyPlan?[] GetConstructorMemberColumns(Dictionary<string, PropertyPlan> members, List<string> fields)
        {
            var columns = new PropertyPlan?[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                if (members.TryGetValue(fields[i], out var member))
                {
                    columns[i] = member;
                }
            }

            return columns;
        }

        private static PropertyPlan?[] GetOrCreateColumnPlans(TypePlan elementPlan, List<string> fields)
        {
            var cache = elementPlan.ColumnPlanCache;
            if (cache != null && cache.TryGetValue(fields, out var cached))
            {
                return cached;
            }

            var columnPlans = new PropertyPlan?[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                if (TryGetProperty(elementPlan, fields[i], out var property))
                {
                    columnPlans[i] = property;
                }
            }

            cache?.TryAdd(fields, columnPlans);
            return columnPlans;
        }

        private static ConstructorParameterPlan?[] GetOrCreateConstructorColumnPlans(TypePlan elementPlan, List<string> fields)
        {
            var cache = elementPlan.ConstructorColumnPlanCache;
            if (cache != null && cache.TryGetValue(fields, out var cached))
            {
                return cached;
            }

            var columnPlans = new ConstructorParameterPlan?[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                if (TryGetConstructorParameter(elementPlan, fields[i], out var parameter))
                {
                    columnPlans[i] = parameter;
                }
            }

            cache?.TryAdd(fields, columnPlans);
            return columnPlans;
        }

        private static bool TryReadListArray(
            LineCursor cursor,
            IList items,
            TypePlan elementPlan,
            ArrayHeaderInfo header,
            int baseDepth,
            ResolvedDecodeOptions options)
        {
            var itemDepth = baseDepth + 1;
            while (!cursor.AtEnd() && items.Count < header.Length)
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth != itemDepth)
                {
                    break;
                }

                if (!IsListItemLine(line.Content))
                {
                    break;
                }

                if (!TryReadListItem(cursor, elementPlan, itemDepth, options, out var item))
                {
                    return false;
                }

                items.Add(item);
            }

            return items.Count == header.Length;
        }

        private static bool TryReadListItem(LineCursor cursor, TypePlan plan, int baseDepth, ResolvedDecodeOptions options, out object? value)
        {
            var line = cursor.Next();
            if (line.IsNone)
            {
                value = null;
                return false;
            }

            if (line.Content == "-")
            {
                return TryCreateEmptyValue(plan, out value);
            }

            if (!IsListItemWithValue(line.Content))
            {
                value = null;
                return false;
            }

            var itemStart = Constants.LIST_ITEM_PREFIX.Length;
            if (IsWhitespaceOrEmpty(line.Content, itemStart))
            {
                return TryCreateEmptyValue(plan, out value);
            }

            var field = ParseListItemContent(line.Content, itemStart);
            if (field.ArrayHeader != null)
            {
                // A keyed header ("- Tags[2]: a,b") is the first field of an object item, not the item itself.
                if (plan.Kind == PlanKind.Object && TryGetListItemFieldHeader(line.Content, itemStart, out var firstField))
                {
                    return TryReadObjectFromListItem(cursor, plan, line, itemStart, firstField, baseDepth, options, out value);
                }

                if (plan.Kind == PlanKind.Dictionary)
                {
                    return TryReadDictionaryFromHeader(cursor, plan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options, out value);
                }

                return TryReadArrayFromHeader(cursor, plan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options, out value);
            }

            if (field.Kind == ParsedFieldKind.KeyValue)
            {
                if (plan.Kind == PlanKind.Object)
                {
                    return TryReadObjectFromListItem(cursor, plan, line, itemStart, field, baseDepth, options, out value);
                }

                if (plan.Kind == PlanKind.Dictionary)
                {
                    return TryReadDictionaryFromListItem(cursor, plan, line, itemStart, field, baseDepth, options, out value);
                }

                if (plan.Kind == PlanKind.Any)
                {
                    return TryReadUntypedObjectFromListItem(cursor, line, itemStart, field, baseDepth, options, out value);
                }
            }

            return TryReadPrimitiveLikeValue(plan, line.Content, itemStart, line.Content.Length, out value);
        }

        private static bool TryReadObjectFromListItem(
            LineCursor cursor,
            TypePlan plan,
            ParsedLine firstLine,
            int firstFieldStart,
            ParsedFieldInfo firstField,
            int baseDepth,
            ResolvedDecodeOptions options,
            out object? value)
        {
            if (plan.Factory == null)
            {
                if (plan.ConstructorFactory != null && plan.ConstructorParameters != null && plan.ConstructorParameterLookup != null)
                {
                    return TryReadConstructorObjectFromListItem(cursor, plan, firstLine, firstFieldStart, firstField, baseDepth, options, out value);
                }

                value = null;
                return false;
            }

            var instance = plan.Factory();
            Dictionary<string, object?>? extensionEntries = null;
            ReadObjectField(instance, cursor, firstLine.Content, baseDepth, plan, ref extensionEntries, options, firstField, firstFieldStart);

            var followDepth = baseDepth + 1;
            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < followDepth)
                {
                    break;
                }

                if (line.Depth == followDepth && !IsListItemLine(line.Content))
                {
                    cursor.Advance();
                    ReadObjectField(instance, cursor, line.Content, followDepth, plan, ref extensionEntries, options);
                    continue;
                }

                break;
            }

            if (extensionEntries != null)
            {
                plan.Extension!.Populate(instance, extensionEntries);
            }

            value = instance;
            return true;
        }

        private static bool TryReadConstructorObjectFromListItem(
            LineCursor cursor,
            TypePlan plan,
            ParsedLine firstLine,
            int firstFieldStart,
            ParsedFieldInfo firstField,
            int baseDepth,
            ResolvedDecodeOptions options,
            out object? value)
        {
            if (!TryCreateConstructorBuffer(plan, out var arguments, out var assigned))
            {
                value = null;
                return false;
            }

            if (!ReadConstructorObjectField(arguments, assigned, cursor, firstLine.Content, baseDepth, plan, options, firstField, firstFieldStart))
            {
                value = null;
                return false;
            }

            var followDepth = baseDepth + 1;
            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < followDepth)
                {
                    break;
                }

                if (line.Depth == followDepth && !IsListItemLine(line.Content))
                {
                    cursor.Advance();
                    if (!ReadConstructorObjectField(arguments, assigned, cursor, line.Content, followDepth, plan, options))
                    {
                        value = null;
                        return false;
                    }

                    continue;
                }

                break;
            }

            value = CreateConstructorInstance(plan, arguments, assigned);
            return true;
        }

        private static bool TryReadDictionaryFromListItem(
            LineCursor cursor,
            TypePlan plan,
            ParsedLine firstLine,
            int firstFieldStart,
            ParsedFieldInfo firstField,
            int baseDepth,
            ResolvedDecodeOptions options,
            out object? value)
        {
            if (plan.CreateDictionary == null)
            {
                value = null;
                return false;
            }

            var dictionary = plan.CreateDictionary();
            if (!ReadDictionaryField(dictionary, cursor, firstLine.Content, baseDepth, plan, options, firstField, firstFieldStart))
            {
                value = null;
                return false;
            }

            var followDepth = baseDepth + 1;
            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < followDepth)
                {
                    break;
                }

                if (line.Depth == followDepth && !IsListItemLine(line.Content))
                {
                    cursor.Advance();
                    if (!ReadDictionaryField(dictionary, cursor, line.Content, followDepth, plan, options))
                    {
                        value = null;
                        return false;
                    }

                    continue;
                }

                break;
            }

            value = dictionary;
            return true;
        }

        private static bool TryCreateEmptyValue(TypePlan plan, out object? value)
        {
            if (plan.Kind == PlanKind.Object && plan.Factory != null)
            {
                value = plan.Factory();
                return true;
            }

            if (plan.Kind == PlanKind.Object && plan.ConstructorFactory != null)
            {
                value = CreateConstructorInstanceWithDefaults(plan);
                return true;
            }

            if ((plan.Kind == PlanKind.Collection || plan.Kind == PlanKind.Array) &&
                plan.CreateList != null &&
                plan.FinalizeCollection != null)
            {
                value = plan.FinalizeCollection(plan.CreateList(0));
                return true;
            }

            if (plan.Kind == PlanKind.Dictionary && plan.CreateDictionary != null)
            {
                value = plan.CreateDictionary();
                return true;
            }

            if (plan.Kind == PlanKind.Any)
            {
                value = new Dictionary<string, object?>(StringComparer.Ordinal);
                return true;
            }

            if (plan.IsNullable)
            {
                value = null;
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryReadPrimitiveLikeValue(TypePlan plan, string source, int start, int endExclusive, out object? value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive) &&
                (plan.Kind == PlanKind.Any || plan.Kind == PlanKind.Nullable || !plan.Type.IsValueType))
            {
                value = null;
                return true;
            }

            if (plan.Kind == PlanKind.Nullable && plan.UnderlyingPlan != null)
            {
                return TryReadPrimitiveLikeValue(plan.UnderlyingPlan, source, start, endExclusive, out value, alreadyTrimmed: true);
            }

            if (plan.Kind == PlanKind.Primitive)
            {
                return TryConvertPrimitive(plan.Type, source, start, endExclusive, out value, alreadyTrimmed);
            }

            if (plan.Kind == PlanKind.Any)
            {
                return TryConvertUntypedPrimitive(source, start, endExclusive, out value, alreadyTrimmed);
            }

            value = null;
            return false;
        }

        private static bool TryConvertPrimitive(Type type, string source, int start, int endExclusive, out object? value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (TryConvertPrimitiveCore(type, source, start, endExclusive, out value))
            {
                return true;
            }

            if (start >= endExclusive || !IsTextScalarOrBoolean(type))
            {
                return false;
            }

            value = ConvertScalarAsV4(type, source, start, endExclusive);
            return true;
        }

        private static bool IsTextScalarOrBoolean(Type type)
            => type == typeof(bool) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(char)
#if NET6_0_OR_GREATER
               || type == typeof(DateOnly) || type == typeof(TimeOnly)
#endif
            ;

        /// <summary>
        /// Converts a token the legacy parsers rejected for a boolean, date, time, <see cref="Guid"/> or <see cref="char"/>
        /// member as 4.x decoding converts it, instead of leaving the member at its default: <c>null</c> to the default, text
        /// by the invariant parse of the type, a number in the 4.x grammar to a boolean as nonzero; anything else throws a
        /// format error.
        /// </summary>
        private static object? ConvertScalarAsV4(Type type, string source, int start, int endExclusive)
        {
            var token = source.AsSpan(start, endExclusive - start);
            object? decoded;
            if (token.SequenceEqual(Constants.NULL_LITERAL)) decoded = null;
            else if (token.SequenceEqual(Constants.TRUE_LITERAL)) decoded = true;
            else if (token.SequenceEqual(Constants.FALSE_LITERAL)) decoded = false;
            else if (token[0] == Constants.DOUBLE_QUOTE) decoded = Parser.ParseStringLiteral(source, start, endExclusive);
            else if (NumericUtils.IsNumberGrammar(token) && double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && NumericUtils.IsFinite(number)) decoded = number;
            else decoded = token.ToString();
            var value = V4Materialization.Scalar(decoded, type, out var error);
            if (error != null)
            {
                throw ToonFormatException.Syntax(decoded is double ? $"Value {token.ToString()} is not a valid {type.FullName}" : error);
            }

            return value;
        }

        private static bool TryConvertPrimitiveCore(Type type, string source, int start, int endExclusive, out object? value)
        {
            if (start >= endExclusive)
            {
                if (type == typeof(string))
                {
                    value = string.Empty;
                    return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(string))
            {
                value = ReadStringValue(source, start, endExclusive);
                return true;
            }

            var token = source.AsSpan(start, endExclusive - start);

            if (type == typeof(char))
            {
                var text = ReadStringValue(source, start, endExclusive);
                if (text.Length == 1)
                {
                    value = text[0];
                    return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(bool))
            {
                if (token.SequenceEqual(Constants.TRUE_LITERAL))
                {
                    value = true;
                    return true;
                }

                if (token.SequenceEqual(Constants.FALSE_LITERAL))
                {
                    value = false;
                    return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(byte))
            {
#if NETSTANDARD2_0
                if (byte.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteValue))
#else
                if (byte.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteValue))
#endif
                {
                    value = byteValue;
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(sbyte))
            {
#if NETSTANDARD2_0
                if (sbyte.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbyteValue))
#else
                if (sbyte.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbyteValue))
#endif
                {
                    value = sbyteValue;
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(short))
            {
#if NETSTANDARD2_0
                if (short.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue))
#else
                if (short.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue))
#endif
                {
                    value = shortValue;
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(ushort))
            {
#if NETSTANDARD2_0
                if (ushort.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ushortValue))
#else
                if (ushort.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ushortValue))
#endif
                {
                    value = ushortValue;
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(int))
            {
#if NETSTANDARD2_0
                if (int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
#else
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
#endif
                {
                    value = intValue;
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(uint))
            {
#if NETSTANDARD2_0
                if (uint.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue))
#else
                if (uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue))
#endif
                {
                    value = uintValue;
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(long))
            {
#if NETSTANDARD2_0
                if (long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
#else
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
#endif
                {
                    value = longValue;
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(ulong))
            {
#if NETSTANDARD2_0
                if (ulong.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongValue))
#else
                if (ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongValue))
#endif
                {
                    value = ulongValue;
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(double))
            {
#if NETSTANDARD2_0
                if (double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
#else
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
#endif
                {
                    if (!NumericUtils.IsFinite(doubleValue))
                    {
                        throw ToonFormatException.Syntax(IsNumberToken(token) ? OutsideFiniteRange : $"Value {token.ToString()} is not a valid {type.FullName}");
                    }

                    value = FloatUtils.NormalizeSignedZero(doubleValue);
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(float))
            {
#if NETSTANDARD2_0
                if (float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
#else
                if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
#endif
                {
                    if (!NumericUtils.IsFinite(floatValue))
                    {
                        throw ToonFormatException.Syntax(IsNumberToken(token) ? OutsideFiniteRange : $"Value {token.ToString()} is not a valid {type.FullName}");
                    }

                    value = FloatUtils.NormalizeSignedZero(floatValue);
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(decimal))
            {
#if NETSTANDARD2_0
                if (decimal.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
#else
                if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
#endif
                {
                    value = decimalValue;
                    return true;
                }

                return TryConvertNumberOutsideType(type, token, out value);
            }

            if (type == typeof(DateTime))
            {
                if (token.Length > 0 && token[0] == Constants.DOUBLE_QUOTE)
                {
                    var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                    if (DateTime.TryParse(parsed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var quotedDateTimeValue))
                    {
                        value = quotedDateTimeValue;
                        return true;
                    }

                    value = null;
                    return false;
                }

#if NETSTANDARD2_0
                if (DateTime.TryParse(token.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeValue))
#else
                if (DateTime.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeValue))
#endif
                {
                    value = dateTimeValue;
                    return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(DateTimeOffset))
            {
                if (token.Length > 0 && token[0] == Constants.DOUBLE_QUOTE)
                {
                    var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                    if (DateTimeOffset.TryParse(parsed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var quotedDateTimeOffsetValue))
                    {
                        value = quotedDateTimeOffsetValue;
                        return true;
                    }

                    value = null;
                    return false;
                }

#if NETSTANDARD2_0
                if (DateTimeOffset.TryParse(token.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffsetValue))
#else
                if (DateTimeOffset.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffsetValue))
#endif
                {
                    value = dateTimeOffsetValue;
                    return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(TimeSpan))
            {
                if (TryParseTimeSpan(source, start, endExclusive, out var timeSpanValue, alreadyTrimmed: true))
                {
                    value = timeSpanValue;
                    return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(Uri))
            {
                var uriText = ReadStringValue(source, start, endExclusive);
                if (Uri.TryCreate(uriText, UriKind.RelativeOrAbsolute, out var uriValue))
                {
                    value = uriValue;
                    return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(Version))
            {
                var versionText = ReadStringValue(source, start, endExclusive);
                if (Version.TryParse(versionText, out var versionValue))
                {
                    value = versionValue;
                    return true;
                }

                value = null;
                return false;
            }

#if NET6_0_OR_GREATER
            if (type == typeof(DateOnly))
            {
                    if (TryParseDateOnly(source, start, endExclusive, out var dateOnlyValue, alreadyTrimmed: true))
                    {
                        value = dateOnlyValue;
                        return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(TimeOnly))
            {
                    if (TryParseTimeOnly(source, start, endExclusive, out var timeOnlyValue, alreadyTrimmed: true))
                    {
                        value = timeOnlyValue;
                        return true;
                }

                value = null;
                return false;
            }
#endif

            if (type == typeof(Guid))
            {
                if (token.Length > 0 && token[0] == Constants.DOUBLE_QUOTE)
                {
                    var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                    if (Guid.TryParse(parsed, out var quotedGuidValue))
                    {
                        value = quotedGuidValue;
                        return true;
                    }

                    value = null;
                    return false;
                }

#if NETSTANDARD2_0
                if (Guid.TryParse(token.ToString(), out var guidValue))
#else
                if (Guid.TryParse(token, out var guidValue))
#endif
                {
                    value = guidValue;
                    return true;
                }

                value = null;
                return false;
            }

            if (type.IsEnum)
            {
                value = ConvertEnum(type, source, start, endExclusive);
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Converts a token to an enum member as 4.x decoding does: a number by the integer rule of the underlying type, a
        /// name, a list of flag names or a number in text by <see cref="Enum.Parse(Type, string, bool)"/>, <c>true</c> and
        /// <c>false</c> as one and zero, and <c>null</c> to the default. Any other token throws a format error instead of leaving the member at its default.
        /// </summary>
        private static object ConvertEnum(Type enumType, string source, int start, int endExclusive)
        {
            var token = source.AsSpan(start, endExclusive - start);
            if (token.SequenceEqual(Constants.NULL_LITERAL))
            {
                return Activator.CreateInstance(enumType)!;
            }

            if (token.SequenceEqual(Constants.TRUE_LITERAL) || token.SequenceEqual(Constants.FALSE_LITERAL))
            {
                return Enum.ToObject(enumType, token.SequenceEqual(Constants.TRUE_LITERAL) ? 1 : 0);
            }

            if (IsNumberToken(token))
            {
                return NumericUtils.BindEnumNumber(enumType, token);
            }

            var text = token.Length > 0 && token[0] == Constants.DOUBLE_QUOTE ? Parser.ParseStringLiteral(source, start, endExclusive) : token.ToString();
            try
            {
                return Enum.Parse(enumType, text, false);
            }
            catch (OverflowException)
            {
                throw ToonFormatException.Syntax(NumericUtils.OutsideRange(text, enumType));
            }
            catch (ArgumentException)
            {
                throw ToonFormatException.Syntax($"Value {text} is not a valid {enumType.FullName}");
            }
        }

        /// <summary>
        /// Handles a token that did not parse as the numeric <paramref name="type"/> instead of leaving the member at its
        /// default, converting what 4.x decoding converts and otherwise throwing a format error. A number the type cannot hold
        /// throws the error the native and JSON routes report; an integral number in exponent or fraction form that fits an
        /// integer type converts; a fraction is not an integer for any integer type. Any other token converts as 4.x converts it: <c>null</c> to zero, <c>true</c> and <c>false</c> to one
        /// and zero, and text, quoted or not, by the invariant parse of the type.
        /// </summary>
        private static bool TryConvertNumberOutsideType(Type type, ReadOnlySpan<char> token, out object? value)
        {
            value = null;
            if (!IsNumberToken(token))
            {
                value = ConvertNonNumberToken(type, token);
                return true;
            }

            var text = token.ToString();
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsInfinity(number) ||
                type == typeof(double) || type == typeof(float))
            {
                throw ToonFormatException.Syntax(OutsideFiniteRange);
            }

            if (type != typeof(decimal) && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact))
            {
                if (!NumericUtils.IsIntegralNumberText(text))
                {
                    throw ToonFormatException.Syntax(NumericUtils.NotAnInteger(text, type));
                }

                value = NarrowInteger(type, exact);
                if (value != null)
                {
                    return true;
                }
            }

            throw ToonFormatException.Syntax($"Number {number.ToString("R", CultureInfo.InvariantCulture)} is outside the range of {type.FullName}");
        }

        private static object ConvertNonNumberToken(Type type, ReadOnlySpan<char> token)
        {
            if (token.SequenceEqual(Constants.NULL_LITERAL))
            {
                return Activator.CreateInstance(type)!;
            }

            if (token.SequenceEqual(Constants.TRUE_LITERAL) || token.SequenceEqual(Constants.FALSE_LITERAL))
            {
                return Convert.ChangeType(token.SequenceEqual(Constants.TRUE_LITERAL), type, CultureInfo.InvariantCulture);
            }

            var text = token.Length > 0 && token[0] == Constants.DOUBLE_QUOTE ? Parser.ParseStringLiteral(token.ToString()) : token.ToString();
            try
            {
                var converted = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                if ((converted is double number && !NumericUtils.IsFinite(number)) || (converted is float single && !NumericUtils.IsFinite(single)))
                {
                    throw ToonFormatException.Syntax(NumericUtils.IsNumberShaped(text.Trim()) ? NumericUtils.OutsideRange(text, type) : $"Value {text} is not a valid {type.FullName}");
                }

                return converted;
            }
            catch (FormatException)
            {
                throw ToonFormatException.Syntax($"Value {text} is not a valid {type.FullName}");
            }
            catch (OverflowException)
            {
                throw ToonFormatException.Syntax($"Number {text} is outside the range of {type.FullName}");
            }
        }

        private static object? NarrowInteger(Type type, decimal value)
        {
            if (type == typeof(int)) return value >= int.MinValue && value <= int.MaxValue ? (int)value : null;
            if (type == typeof(long)) return value >= long.MinValue && value <= long.MaxValue ? (long)value : null;
            if (type == typeof(short)) return value >= short.MinValue && value <= short.MaxValue ? (short)value : null;
            if (type == typeof(byte)) return value >= byte.MinValue && value <= byte.MaxValue ? (byte)value : null;
            if (type == typeof(sbyte)) return value >= sbyte.MinValue && value <= sbyte.MaxValue ? (sbyte)value : null;
            if (type == typeof(ushort)) return value >= ushort.MinValue && value <= ushort.MaxValue ? (ushort)value : null;
            if (type == typeof(uint)) return value >= uint.MinValue && value <= uint.MaxValue ? (uint)value : null;
            if (type == typeof(ulong)) return value >= ulong.MinValue && value <= ulong.MaxValue ? (ulong)value : null;
            return null;
        }

        /// <summary>
        /// Returns whether a bare token has the shape of a decimal number (<c>-12</c>, <c>1.5</c>, <c>1e400</c>), whatever its
        /// magnitude; quoted strings and words such as <c>Infinity</c> do not.
        /// </summary>
        private static bool IsNumberToken(ReadOnlySpan<char> token)
        {
            var i = token.Length > 0 && token[0] == '-' ? 1 : 0;
            var digits = SkipDigits(token, ref i);
            if (digits == 0)
            {
                return false;
            }

            if (i < token.Length && token[i] == '.')
            {
                i++;
                if (SkipDigits(token, ref i) == 0)
                {
                    return false;
                }
            }

            if (i < token.Length && (token[i] == 'e' || token[i] == 'E'))
            {
                i++;
                if (i < token.Length && (token[i] == '+' || token[i] == '-'))
                {
                    i++;
                }

                if (SkipDigits(token, ref i) == 0)
                {
                    return false;
                }
            }

            return i == token.Length;
        }

        private static int SkipDigits(ReadOnlySpan<char> token, ref int index)
        {
            var start = index;
            while (index < token.Length && token[index] >= '0' && token[index] <= '9')
            {
                index++;
            }

            return index - start;
        }

        private static bool TryConvertUntypedPrimitive(string source, int start, int endExclusive, out object? value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (start >= endExclusive)
            {
                value = string.Empty;
                return true;
            }

            if (IsNullToken(source, start, endExclusive))
            {
                value = null;
                return true;
            }

            if (source[start] == Constants.DOUBLE_QUOTE)
            {
                value = ReadStringValue(source, start, endExclusive);
                return true;
            }

            var token = source.AsSpan(start, endExclusive - start);
            if (token.SequenceEqual(Constants.TRUE_LITERAL))
            {
                value = true;
                return true;
            }

            if (token.SequenceEqual(Constants.FALSE_LITERAL))
            {
                value = false;
                return true;
            }

            if (LiteralUtils.IsNumericLiteral(token) &&
#if NETSTANDARD2_0
                double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
#else
                double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
#endif
            {
                value = number;
                return true;
            }

            value = token.ToString();
            return true;
        }

        private static object? ReadUntypedArray(LineCursor cursor, ArrayHeaderInfo header, string? inlineValues, int baseDepth, ResolvedDecodeOptions options)
        {
            var list = new List<object?>(Math.Min(header.Length, 4096));

            if (inlineValues != null)
            {
                if (!string.IsNullOrWhiteSpace(inlineValues))
                {
                    var tokenRanges = new Parser.TokenRange[Math.Min(header.Length, inlineValues.Length + 1)];
                    if (Parser.TryParseDelimitedValueRanges(inlineValues, header.Delimiter, tokenRanges, out var valueCount))
                    {
                        for (int i = 0; i < valueCount; i++)
                        {
                            var tokenRange = tokenRanges[i];
                            TryConvertUntypedPrimitive(inlineValues, tokenRange.Start, tokenRange.EndExclusive, out var item, alreadyTrimmed: true);
                            list.Add(item);
                        }
                    }
                }

                return list;
            }

            if (header.Fields != null && header.Fields.Count > 0)
            {
                var rowDepth = baseDepth + 1;
                while (!cursor.AtEnd() && list.Count < header.Length)
                {
                    var line = cursor.Peek();
                    if (line.IsNone || line.Depth != rowDepth)
                    {
                        break;
                }

                cursor.Advance();
                var tokenRanges = new Parser.TokenRange[header.Fields.Count];
                var contentSpanU = line.ContentSpan;
                var contentOffsetU = line.ContentStart;
                if (!Parser.TryParseDelimitedValueRanges(contentSpanU, header.Delimiter, tokenRanges, out var valueCount))
                {
                    break;
                }

                var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int i = 0; i < header.Fields.Count && i < valueCount; i++)
                {
                    var tokenRange = tokenRanges[i];
                    TryConvertUntypedPrimitive(line.SourceString, contentOffsetU + tokenRange.Start, contentOffsetU + tokenRange.EndExclusive, out var fieldValue, alreadyTrimmed: true);
                    obj[header.Fields[i]] = fieldValue;
                }

                var followDepth = rowDepth + 1;
                while (!cursor.AtEnd())
                {
                    var continuation = cursor.Peek();
                    if (continuation.IsNone || continuation.Depth < followDepth)
                    {
                        break;
                    }

                    if (continuation.Depth == followDepth && !IsListItemLine(continuation.Content))
                    {
                        cursor.Advance();
                        ReadUntypedField(obj, cursor, continuation.Content, followDepth, options);
                        continue;
                    }

                    break;
                }

                    list.Add(obj);
                }

                return list;
            }

            var itemDepth = baseDepth + 1;
            while (!cursor.AtEnd() && list.Count < header.Length)
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth != itemDepth)
                {
                    break;
                }

                if (!TryReadListItem(cursor, TypePlan.Any(typeof(object)), itemDepth, options, out var item))
                {
                    break;
                }

                list.Add(item);
            }

            return list;
        }

        private static bool TryReadUntypedObject(LineCursor cursor, int baseDepth, ResolvedDecodeOptions options, out object? value)
        {
            var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
            int? computedDepth = null;

            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < baseDepth)
                {
                    break;
                }

                if (computedDepth == null && line.Depth >= baseDepth)
                {
                    computedDepth = line.Depth;
                }

                if (line.Depth != computedDepth)
                {
                    break;
                }

                cursor.Advance();
                ReadUntypedField(obj, cursor, line.Content, computedDepth.Value, options);
            }

            value = obj;
            return true;
        }

        private static void ReadUntypedField(
            Dictionary<string, object?> obj,
            LineCursor cursor,
            string content,
            int baseDepth,
            ResolvedDecodeOptions options,
            ParsedFieldInfo? parsedField = null,
            int contentStart = 0)
        {
            var field = parsedField ?? ParseFieldContent(content, contentStart);
            if (field.ArrayHeader != null)
            {
                obj[GetArrayHeaderKey(content, field.ArrayHeader)] = ReadUntypedArray(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options);
                return;
            }

            if (field.ValueStart >= content.Length)
            {
                var nextLine = cursor.Peek();
                if (!nextLine.IsNone && nextLine.Depth > baseDepth)
                {
                    TryReadUntypedObject(cursor, baseDepth + 1, options, out var nested);
                    obj[GetFieldKey(content, field)] = nested;
                }
                else
                {
                    obj[GetFieldKey(content, field)] = new Dictionary<string, object?>(StringComparer.Ordinal);
                }

                return;
            }

            TryConvertUntypedPrimitive(content, field.ValueStart, content.Length, out var primitive);
            obj[GetFieldKey(content, field)] = primitive;
        }

        private static bool TryReadUntypedObjectFromListItem(
            LineCursor cursor,
            ParsedLine firstLine,
            int firstFieldStart,
            ParsedFieldInfo firstField,
            int baseDepth,
            ResolvedDecodeOptions options,
            out object? value)
        {
            var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
            ReadUntypedField(obj, cursor, firstLine.Content, baseDepth, options, firstField, firstFieldStart);

            var followDepth = baseDepth + 1;
            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < followDepth)
                {
                    break;
                }

                if (line.Depth == followDepth && !IsListItemLine(line.Content))
                {
                    cursor.Advance();
                    ReadUntypedField(obj, cursor, line.Content, followDepth, options);
                    continue;
                }

                break;
            }

            value = obj;
            return true;
        }

        private static void SkipValue(LineCursor cursor, int valueStart, int baseDepth, ResolvedDecodeOptions options)
        {
            if (valueStart >= 0 && TryGetNestedLine(cursor, baseDepth, out _))
            {
                SkipObject(cursor, baseDepth + 1, options);
            }
        }

        private static bool TryGetNestedLine(LineCursor cursor, int baseDepth, out ParsedLine line)
        {
            line = cursor.Peek();
            return !line.IsNone && line.Depth > baseDepth;
        }

        private static void SkipObject(LineCursor cursor, int baseDepth, ResolvedDecodeOptions options)
        {
            int? computedDepth = null;
            while (!cursor.AtEnd())
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth < baseDepth)
                {
                    break;
                }

                if (computedDepth == null && line.Depth >= baseDepth)
                {
                    computedDepth = line.Depth;
                }

                if (line.Depth != computedDepth)
                {
                    break;
                }

                cursor.Advance();
                SkipObjectField(cursor, line.Content, computedDepth.Value, options);
            }
        }

        private static void SkipObjectField(
            LineCursor cursor,
            string content,
            int baseDepth,
            ResolvedDecodeOptions options,
            ParsedFieldInfo? parsedField = null,
            int contentStart = 0)
        {
            var field = parsedField ?? ParseFieldContent(content, contentStart);
            if (field.ArrayHeader != null)
            {
                SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options);
                return;
            }

            SkipValue(cursor, field.ValueStart, baseDepth, options);
        }

        private static void SkipArrayFromHeader(LineCursor cursor, ArrayHeaderInfo header, string? inlineValues, int baseDepth, ResolvedDecodeOptions options)
        {
            if (inlineValues != null)
            {
                return;
            }

            if (header.Fields != null && header.Fields.Count > 0)
            {
                var rowDepth = baseDepth + 1;
                int count = 0;
                while (!cursor.AtEnd() && count < header.Length)
                {
                    var line = cursor.Peek();
                    if (line.IsNone || line.Depth != rowDepth)
                    {
                        break;
                    }

                    cursor.Advance();
                    var followDepth = rowDepth + 1;
                    while (!cursor.AtEnd())
                    {
                        var continuation = cursor.Peek();
                        if (continuation.IsNone || continuation.Depth < followDepth)
                        {
                            break;
                        }

                        if (continuation.Depth == followDepth && !IsListItemLine(continuation.Content))
                        {
                            cursor.Advance();
                            SkipObjectField(cursor, continuation.Content, followDepth, options);
                            continue;
                        }

                        break;
                    }
                    count++;
                }

                return;
            }

            var itemDepth = baseDepth + 1;
            int itemCount = 0;
            while (!cursor.AtEnd() && itemCount < header.Length)
            {
                var line = cursor.Peek();
                if (line.IsNone || line.Depth != itemDepth)
                {
                    break;
                }

                if (!IsListItemLine(line.Content))
                {
                    break;
                }

                SkipListItem(cursor, itemDepth, options);
                itemCount++;
            }
        }

        private static void SkipListItem(LineCursor cursor, int baseDepth, ResolvedDecodeOptions options)
        {
            var line = cursor.Next();
            if (line.IsNone || line.Content == "-")
            {
                return;
            }

            if (!IsListItemWithValue(line.Content))
            {
                return;
            }

            var itemStart = Constants.LIST_ITEM_PREFIX.Length;
            if (IsWhitespaceOrEmpty(line.Content, itemStart))
            {
                return;
            }

            var field = ParseListItemContent(line.Content, itemStart);
            if (field.ArrayHeader != null)
            {
                SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options);
                return;
            }

            if (field.Kind == ParsedFieldKind.KeyValue)
            {
                SkipObjectField(cursor, line.Content, baseDepth, options, field, itemStart);
                SkipObject(cursor, baseDepth + 1, options);
            }
        }

        private static ParsedFieldInfo ParseFieldContent(string content, int start)
        {
            if (TryParseArrayHeader(content, start, false, out var arrayHeader))
            {
                return ParsedFieldInfo.FromArrayHeader(arrayHeader!);
            }

            var keyStart = SkipWhitespace(content, start);
            if (keyStart < content.Length && content[keyStart] != Constants.DOUBLE_QUOTE)
            {
                int colonIndex = keyStart;
                while (colonIndex < content.Length && content[colonIndex] != Constants.COLON)
                {
                    colonIndex++;
                }

                if (colonIndex >= content.Length)
                {
                    throw ToonFormatException.Syntax("Missing colon after key");
                }

                var trimmedKeyEnd = colonIndex;
                while (trimmedKeyEnd > keyStart && char.IsWhiteSpace(content[trimmedKeyEnd - 1]))
                {
                    trimmedKeyEnd--;
                }

                return ParsedFieldInfo.FromKeyRange(keyStart, trimmedKeyEnd, SkipWhitespace(content, colonIndex + 1));
            }

            var keyResult = Parser.ParseKeyToken(content, start);
            return ParsedFieldInfo.FromKeyValue(keyResult.Key, SkipWhitespace(content, keyResult.End));
        }

        private static ParsedFieldInfo ParseListItemContent(string content, int start)
        {
            if (TryParseArrayHeader(content, start, true, out var arrayHeader))
            {
                return ParsedFieldInfo.FromArrayHeader(arrayHeader!);
            }

            if (StringUtils.FindUnquotedChar(content.AsSpan(start), Constants.COLON) != -1)
            {
                var keyStart = SkipWhitespace(content, start);
                if (keyStart < content.Length && content[keyStart] != Constants.DOUBLE_QUOTE)
                {
                    int colonIndex = keyStart;
                    while (colonIndex < content.Length && content[colonIndex] != Constants.COLON)
                    {
                        colonIndex++;
                    }

                    if (colonIndex >= content.Length)
                    {
                        throw ToonFormatException.Syntax("Missing colon after key");
                    }

                    var trimmedKeyEnd = colonIndex;
                    while (trimmedKeyEnd > keyStart && char.IsWhiteSpace(content[trimmedKeyEnd - 1]))
                    {
                        trimmedKeyEnd--;
                    }

                    return ParsedFieldInfo.FromKeyRange(keyStart, trimmedKeyEnd, SkipWhitespace(content, colonIndex + 1));
                }

                var keyResult = Parser.ParseKeyToken(content, start);
                return ParsedFieldInfo.FromKeyValue(keyResult.Key, SkipWhitespace(content, keyResult.End));
            }

            return ParsedFieldInfo.Primitive();
        }

        private static bool TryParseArrayHeader(string content, int start, bool allowKeylessHeader, out ArrayHeaderParseResult? arrayHeader)
        {
            arrayHeader = null;
            var trimmedStart = SkipWhitespace(content, start);
            if (trimmedStart >= content.Length)
            {
                return false;
            }

            if (content[trimmedStart] == Constants.DOUBLE_QUOTE)
            {
                var closingQuoteIndex = StringUtils.FindClosingQuote(content.AsSpan(trimmedStart), 0);
                if (closingQuoteIndex == -1)
                {
                    return false;
                }

                var bracketStart = trimmedStart + closingQuoteIndex + 1;
                if (bracketStart >= content.Length || content[bracketStart] != Constants.OPEN_BRACKET)
                {
                    return false;
                }
            }
            else
            {
                var colonIndex = content.IndexOf(Constants.COLON, trimmedStart);
                if (colonIndex == -1)
                {
                    return false;
                }

                var bracketIndex = content.IndexOf(Constants.OPEN_BRACKET, trimmedStart);
                if (bracketIndex == -1 || bracketIndex > colonIndex)
                {
                    return false;
                }
            }

            arrayHeader = GetOrParseArrayHeaderLine(content);
            return arrayHeader != null && (allowKeylessHeader || arrayHeader.HasKeyRange || arrayHeader.Header.Key != null);
        }

        private static ArrayHeaderParseResult? GetOrParseArrayHeaderLine(string content)
        {
            if (ArrayHeaderCache.TryGetValue(content, out var cached))
            {
                return cached;
            }

            var parsed = Parser.ParseArrayHeaderLine(content, Constants.DEFAULT_DELIMITER_CHAR);
            if (parsed != null)
            {
                ArrayHeaderCache.TryAdd(content, parsed);
            }

            return parsed;
        }

        private static bool TryCaptureExtensionField(
            TypePlan plan,
            ref Dictionary<string, object?>? entries,
            string key,
            LineCursor cursor,
            string content,
            int contentStart,
            int baseDepth,
            ResolvedDecodeOptions options)
        {
            if (plan.UnboundMembers?.Contains(key) == true)
            {
                return false;
            }

            var fieldContent = contentStart == 0 ? content : content.Substring(contentStart);
            var node = NativeDecoders.DecodeFieldValue(fieldContent, cursor, baseDepth, options, isListItemFirstField: contentStart > 0);
            CaptureExtension(key, node, ref entries);
            return true;
        }

        private static bool TryCaptureConstructorExtensionField(
            TypePlan plan,
            object?[] arguments,
            string key,
            LineCursor cursor,
            string content,
            int contentStart,
            int baseDepth,
            ResolvedDecodeOptions options)
        {
            var slot = plan.ConstructorParameters!.Length;
            var entries = arguments[slot] as Dictionary<string, object?>;
            if (!TryCaptureExtensionField(plan, ref entries, key, cursor, content, contentStart, baseDepth, options))
            {
                return false;
            }

            arguments[slot] = entries;
            return true;
        }

        private static void CaptureExtensionCell(TypePlan plan, ref Dictionary<string, object?>? entries, string key, string source, int start, int endExclusive)
        {
            if (plan.UnboundMembers?.Contains(key) == true)
            {
                return;
            }

            CaptureExtension(key, Parser.ParsePrimitiveNativeToken(source.Substring(start, endExclusive - start)), ref entries);
        }

        private static void CaptureExtension(string key, NativeNode? node, ref Dictionary<string, object?>? entries)
        {
            (entries ??= new Dictionary<string, object?>(StringComparer.Ordinal))[key] = ExtensionDataAccessor.ToValue(node);
        }

        private static string GetFieldKey(string content, ParsedFieldInfo field)
        {
            if (field.HasKeyRange)
            {
                return content.Substring(field.KeyStart, field.KeyEndExclusive - field.KeyStart);
            }

            return field.Key!;
        }

        private static string GetArrayHeaderKey(string content, ArrayHeaderParseResult header)
        {
            if (header.HasKeyRange)
            {
                return content.Substring(header.KeyStart, header.KeyEndExclusive - header.KeyStart);
            }

            return header.Header.Key!;
        }

        private static int FindFieldIndex(IReadOnlyList<string> fields, string name)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryGetConstructorParameter(TypePlan plan, string key, out ConstructorParameterPlan parameter)
        {
            if (plan.ConstructorParameterLookup != null &&
                plan.ConstructorParameterLookup.TryGetValue(key, out var resolvedParameter))
            {
                parameter = resolvedParameter;
                return true;
            }

            parameter = null!;
            return false;
        }

        private static bool TryCreateConstructorBuffer(TypePlan plan, out object?[] arguments, out bool[] assigned)
        {
            if (plan.ConstructorParameters == null)
            {
                arguments = Array.Empty<object?>();
                assigned = Array.Empty<bool>();
                return false;
            }

            // An extension-data type keeps its collected entries in one slot past the constructor arguments.
            // Pending values of members assigned after construction take one more slot.
            arguments = new object?[plan.ConstructorParameters.Length + (plan.Extension != null ? 1 : 0) + (plan.ConstructorMembers != null ? 1 : 0)];
            assigned = new bool[plan.ConstructorParameters.Length];
            return true;
        }

        private static void SetConstructorArgument(
            object?[] arguments,
            bool[] assigned,
            ConstructorParameterPlan parameter,
            object? value)
        {
            arguments[parameter.Index] = value;
            assigned[parameter.Index] = true;
        }

        private static object? CreateConstructorInstanceWithDefaults(TypePlan plan)
        {
            if (!TryCreateConstructorBuffer(plan, out var arguments, out var assigned))
            {
                return null;
            }

            return CreateConstructorInstance(plan, arguments, assigned);
        }

        private static object CreateConstructorInstance(TypePlan plan, object?[] arguments, bool[] assigned)
        {
            var parameters = plan.ConstructorParameters!;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!assigned[i])
                {
                    arguments[i] = parameters[i].DefaultValue;
                }
            }

            var extensionEntries = plan.Extension != null ? arguments[parameters.Length] as Dictionary<string, object?> : null;
            if (extensionEntries != null && plan.ExtensionArgument >= 0)
            {
                arguments[plan.ExtensionArgument] = extensionEntries;
            }

            var value = plan.ConstructorFactory!(arguments);
            if (plan.ConstructorMembers != null && arguments[plan.ConstructorMemberSlot] is List<KeyValuePair<PropertyPlan, object?>> pending)
            {
                foreach (var entry in pending)
                {
                    entry.Key.Setter(value, entry.Value);
                }
            }

            if (extensionEntries != null && plan.ExtensionArgument < 0)
            {
                plan.Extension!.Populate(value, extensionEntries);
            }

            return value;
        }

        private static bool TryGetProperty(TypePlan plan, string key, out PropertyPlan property)
        {
            if (plan.Properties is Dictionary<string, PropertyPlan> properties
                && properties.TryGetValue(key, out var resolvedProperty))
            {
                property = resolvedProperty;
                return true;
            }

            property = null!;
            return false;
        }

        private static bool TryGetProperty(TypePlan plan, string content, ParsedFieldInfo field, out PropertyPlan property)
        {
            if (field.HasKeyRange)
            {
                return TryGetProperty(plan, content, field.KeyStart, field.KeyEndExclusive, out property);
            }

            return TryGetProperty(plan, field.Key!, out property);
        }

        private static bool TryGetProperty(TypePlan plan, string content, ArrayHeaderParseResult header, out PropertyPlan property)
        {
            if (header.HasKeyRange)
            {
                return TryGetProperty(plan, content, header.KeyStart, header.KeyEndExclusive, out property);
            }

            return TryGetProperty(plan, header.Header.Key!, out property);
        }

        private static bool TryGetProperty(TypePlan plan, string content, int keyStart, int keyEndExclusive, out PropertyPlan property)
        {
            property = null!;
            var aliases = GetCandidateAliases(plan, content, keyStart, keyEndExclusive);
            if (aliases == null)
            {
                return false;
            }

            var key = content.AsSpan(keyStart, keyEndExclusive - keyStart);
            for (int i = 0; i < aliases.Length; i++)
            {
                if (key.SequenceEqual(aliases[i].Name.AsSpan()))
                {
                    property = aliases[i].Property;
                    return true;
                }
            }

            return false;
        }

        private static PropertyAlias[]? GetCandidateAliases(TypePlan plan, string content, int keyStart, int keyEndExclusive)
        {
            if (keyEndExclusive <= keyStart)
            {
                return null;
            }

            var buckets = plan.PropertyAliasBuckets;
            if (buckets != null && buckets.TryGetValue(CreateAliasBucketKey(content[keyStart], keyEndExclusive - keyStart), out var bucket))
            {
                return bucket;
            }

            return plan.PropertyAliases;
        }

        private static Dictionary<int, PropertyAlias[]> BuildAliasBuckets(PropertyAlias[] aliases)
        {
            var grouped = new Dictionary<int, List<PropertyAlias>>(aliases.Length);
            for (int i = 0; i < aliases.Length; i++)
            {
                var alias = aliases[i];
                if (string.IsNullOrEmpty(alias.Name))
                {
                    continue;
                }

                var bucketKey = CreateAliasBucketKey(alias.Name[0], alias.Name.Length);
                if (!grouped.TryGetValue(bucketKey, out var bucket))
                {
                    bucket = new List<PropertyAlias>(2);
                    grouped[bucketKey] = bucket;
                }

                bucket.Add(alias);
            }

            var result = new Dictionary<int, PropertyAlias[]>(grouped.Count);
            foreach (var pair in grouped)
            {
                result[pair.Key] = pair.Value.ToArray();
            }

            return result;
        }

        private static int CreateAliasBucketKey(char firstChar, int length)
        {
            return (length << 16) ^ firstChar;
        }

        private static int SkipWhitespace(string value, int index)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            return index;
        }

        private static bool IsListItemLine(string content)
        {
            return content.Length != 0 &&
                   content[0] == '-' &&
                   (content.Length == 1 || content[1] == Constants.SPACE);
        }

        private static bool IsListItemWithValue(string content)
        {
            return content.Length >= Constants.LIST_ITEM_PREFIX.Length &&
                   content[0] == '-' &&
                   content[1] == Constants.SPACE;
        }

        private static bool IsWhitespaceOrEmpty(string value, int start)
        {
            return SkipWhitespace(value, start) >= value.Length;
        }

        private static void TrimRange(string value, ref int start, ref int endExclusive)
        {
            while (start < endExclusive && char.IsWhiteSpace(value[start]))
            {
                start++;
            }

            while (endExclusive > start && char.IsWhiteSpace(value[endExclusive - 1]))
            {
                endExclusive--;
            }
        }

        private static void NormalizeRange(string value, ref int start, ref int endExclusive, bool alreadyTrimmed)
        {
            if (!alreadyTrimmed)
            {
                TrimRange(value, ref start, ref endExclusive);
            }
        }

        private static bool TryGetSimpleQuotedContentRange(string source, int start, int endExclusive, out int contentStart, out int contentEndExclusive)
        {
            contentStart = -1;
            contentEndExclusive = -1;

            if (endExclusive - start < 2 ||
                source[start] != Constants.DOUBLE_QUOTE ||
                source[endExclusive - 1] != Constants.DOUBLE_QUOTE)
            {
                return false;
            }

            contentStart = start + 1;
            contentEndExclusive = endExclusive - 1;
            for (int i = contentStart; i < contentEndExclusive; i++)
            {
                if (source[i] == Constants.BACKSLASH || source[i] == Constants.DOUBLE_QUOTE)
                {
                    contentStart = -1;
                    contentEndExclusive = -1;
                    return false;
                }
            }

            return true;
        }

        private static bool IsNullToken(string source, int start, int endExclusive)
        {
            return endExclusive - start == Constants.NULL_LITERAL.Length &&
                   string.CompareOrdinal(source, start, Constants.NULL_LITERAL, 0, Constants.NULL_LITERAL.Length) == 0;
        }

        private static string ReadStringValue(string source, int start, int endExclusive)
        {
            TrimRange(source, ref start, ref endExclusive);
            if (TryGetSimpleQuotedContentRange(source, start, endExclusive, out var contentStart, out var contentEndExclusive))
            {
                return source.Substring(contentStart, contentEndExclusive - contentStart);
            }

            return Parser.ParseStringLiteral(source, start, endExclusive);
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

        private static PrimitiveSetterDelegate? CreatePrimitiveSetter(PropertyInfo property, TypePlan plan)
        {
            var propertyType = property.PropertyType;
            var nullableType = Nullable.GetUnderlyingType(propertyType);

            if (propertyType == typeof(string))
            {
                var setter = CreateTypedSetter<string?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) =>
                {
                    NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
                    if (IsNullToken(source, start, endExclusive))
                    {
                        setter(instance, null);
                        return true;
                    }

                    setter(instance, ReadStringValue(source, start, endExclusive));
                    return true;
                };
            }

            if (propertyType == typeof(bool))
            {
                var setter = CreateTypedSetter<bool>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) =>
                {
                    NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
                    var token = source.AsSpan(start, endExclusive - start);
                    if (token.SequenceEqual(Constants.TRUE_LITERAL))
                    {
                        setter(instance, true);
                        return true;
                    }

                    if (token.SequenceEqual(Constants.FALSE_LITERAL))
                    {
                        setter(instance, false);
                        return true;
                    }

                    return false;
                };
            }

            if (propertyType == typeof(int))
            {
                var setter = CreateTypedSetter<int>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetInt(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(int))
            {
                var setter = CreateTypedSetter<int?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableInt(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (propertyType == typeof(long))
            {
                var setter = CreateTypedSetter<long>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetLong(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(long))
            {
                var setter = CreateTypedSetter<long?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableLong(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (propertyType == typeof(double))
            {
                var setter = CreateTypedSetter<double>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetDouble(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(double))
            {
                var setter = CreateTypedSetter<double?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableDouble(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (propertyType == typeof(float))
            {
                var setter = CreateTypedSetter<float>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetFloat(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(float))
            {
                var setter = CreateTypedSetter<float?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableFloat(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (propertyType == typeof(decimal))
            {
                var setter = CreateTypedSetter<decimal>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetDecimal(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(decimal))
            {
                var setter = CreateTypedSetter<decimal?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableDecimal(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (propertyType == typeof(DateTime))
            {
                var setter = CreateTypedSetter<DateTime>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetDateTime(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(DateTime))
            {
                var setter = CreateTypedSetter<DateTime?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableDateTime(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (propertyType == typeof(DateTimeOffset))
            {
                var setter = CreateTypedSetter<DateTimeOffset>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetDateTimeOffset(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(DateTimeOffset))
            {
                var setter = CreateTypedSetter<DateTimeOffset?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableDateTimeOffset(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

#if NET6_0_OR_GREATER
            if (propertyType == typeof(DateOnly))
            {
                var setter = CreateTypedSetter<DateOnly>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetDateOnly(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(DateOnly))
            {
                var setter = CreateTypedSetter<DateOnly?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableDateOnly(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (propertyType == typeof(TimeOnly))
            {
                var setter = CreateTypedSetter<TimeOnly>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetTimeOnly(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(TimeOnly))
            {
                var setter = CreateTypedSetter<TimeOnly?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableTimeOnly(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }
#endif

            if (propertyType == typeof(Guid))
            {
                var setter = CreateTypedSetter<Guid>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetGuid(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            if (nullableType == typeof(Guid))
            {
                var setter = CreateTypedSetter<Guid?>(property);
                return (instance, source, start, endExclusive, alreadyTrimmed) => TryParseAndSetNullableGuid(instance, source, start, endExclusive, setter, alreadyTrimmed);
            }

            return null;
        }

        private static Action<object, T> CreateTypedSetter<T>(PropertyInfo property)
        {
            var target = Expression.Parameter(typeof(object), "target");
            var value = Expression.Parameter(typeof(T), "value");
            var convertedTarget = Expression.Convert(target, property.DeclaringType!);
            var body = Expression.Assign(Expression.Property(convertedTarget, property), value);
            return Expression.Lambda<Action<object, T>>(body, target, value).Compile();
        }

        private static bool TryParseAndSetInt(object instance, string source, int start, int endExclusive, Action<object, int> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
#if NETSTANDARD2_0
            if (int.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
#else
            if (int.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableInt(object instance, string source, int start, int endExclusive, Action<object, int?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

#if NETSTANDARD2_0
            if (int.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
#else
            if (int.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetLong(object instance, string source, int start, int endExclusive, Action<object, long> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
#if NETSTANDARD2_0
            if (long.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
#else
            if (long.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableLong(object instance, string source, int start, int endExclusive, Action<object, long?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

#if NETSTANDARD2_0
            if (long.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
#else
            if (long.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetDouble(object instance, string source, int start, int endExclusive, Action<object, double> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
#if NETSTANDARD2_0
            if (double.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && NumericUtils.IsFinite(value))
#else
            if (double.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && NumericUtils.IsFinite(value))
#endif
            {
                setter(instance, FloatUtils.NormalizeSignedZero(value));
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableDouble(object instance, string source, int start, int endExclusive, Action<object, double?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

#if NETSTANDARD2_0
            if (double.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && NumericUtils.IsFinite(value))
#else
            if (double.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && NumericUtils.IsFinite(value))
#endif
            {
                setter(instance, FloatUtils.NormalizeSignedZero(value));
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetFloat(object instance, string source, int start, int endExclusive, Action<object, float> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
#if NETSTANDARD2_0
            if (float.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && NumericUtils.IsFinite(value))
#else
            if (float.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && NumericUtils.IsFinite(value))
#endif
            {
                setter(instance, FloatUtils.NormalizeSignedZero(value));
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableFloat(object instance, string source, int start, int endExclusive, Action<object, float?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

#if NETSTANDARD2_0
            if (float.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && NumericUtils.IsFinite(value))
#else
            if (float.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && NumericUtils.IsFinite(value))
#endif
            {
                setter(instance, FloatUtils.NormalizeSignedZero(value));
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetDecimal(object instance, string source, int start, int endExclusive, Action<object, decimal> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
#if NETSTANDARD2_0
            if (decimal.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#else
            if (decimal.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableDecimal(object instance, string source, int start, int endExclusive, Action<object, decimal?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

#if NETSTANDARD2_0
            if (decimal.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#else
            if (decimal.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetDateTime(object instance, string source, int start, int endExclusive, Action<object, DateTime> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (TryParseDateTime(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableDateTime(object instance, string source, int start, int endExclusive, Action<object, DateTime?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

            if (TryParseDateTime(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetDateTimeOffset(object instance, string source, int start, int endExclusive, Action<object, DateTimeOffset> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (TryParseDateTimeOffset(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableDateTimeOffset(object instance, string source, int start, int endExclusive, Action<object, DateTimeOffset?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

            if (TryParseDateTimeOffset(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

#if NET6_0_OR_GREATER
        private static bool TryParseAndSetDateOnly(object instance, string source, int start, int endExclusive, Action<object, DateOnly> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (TryParseDateOnly(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableDateOnly(object instance, string source, int start, int endExclusive, Action<object, DateOnly?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

            if (TryParseDateOnly(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetTimeOnly(object instance, string source, int start, int endExclusive, Action<object, TimeOnly> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (TryParseTimeOnly(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableTimeOnly(object instance, string source, int start, int endExclusive, Action<object, TimeOnly?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

            if (TryParseTimeOnly(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }
#endif

        private static bool TryParseAndSetGuid(object instance, string source, int start, int endExclusive, Action<object, Guid> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (TryParseGuid(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetNullableGuid(object instance, string source, int start, int endExclusive, Action<object, Guid?> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (IsNullToken(source, start, endExclusive))
            {
                setter(instance, null);
                return true;
            }

            if (TryParseGuid(source, start, endExclusive, out var value, alreadyTrimmed: true))
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseDateTime(string source, int start, int endExclusive, out DateTime value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (start < endExclusive && source[start] == Constants.DOUBLE_QUOTE)
            {
                if (TryGetSimpleQuotedContentRange(source, start, endExclusive, out var contentStart, out var contentEndExclusive))
                {
#if NETSTANDARD2_0
                    var simpleToken = source.Substring(contentStart, contentEndExclusive - contentStart);
                    if (DateTime.TryParseExact(simpleToken, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
                    {
                        return true;
                    }

                    return DateTime.TryParse(simpleToken, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
#else
                    if (DateTime.TryParseExact(source.AsSpan(contentStart, contentEndExclusive - contentStart), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
                    {
                        return true;
                    }

                    return DateTime.TryParse(source.AsSpan(contentStart, contentEndExclusive - contentStart), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
#endif
                }

                var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                if (DateTime.TryParseExact(parsed, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
                {
                    return true;
                }

                return DateTime.TryParse(parsed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
            }

#if NETSTANDARD2_0
            var token = source.Substring(start, endExclusive - start);
            if (DateTime.TryParseExact(token, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                return true;
            }

            return DateTime.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
#else
            if (DateTime.TryParseExact(source.AsSpan(start, endExclusive - start), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                return true;
            }

            return DateTime.TryParse(source.AsSpan(start, endExclusive - start), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
#endif
        }

        private static bool TryParseDateTimeOffset(string source, int start, int endExclusive, out DateTimeOffset value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (start < endExclusive && source[start] == Constants.DOUBLE_QUOTE)
            {
                if (TryGetSimpleQuotedContentRange(source, start, endExclusive, out var contentStart, out var contentEndExclusive))
                {
#if NETSTANDARD2_0
                    var simpleToken = source.Substring(contentStart, contentEndExclusive - contentStart);
                    if (DateTimeOffset.TryParseExact(simpleToken, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
                    {
                        return true;
                    }

                    return DateTimeOffset.TryParse(simpleToken, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
#else
                    if (DateTimeOffset.TryParseExact(source.AsSpan(contentStart, contentEndExclusive - contentStart), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
                    {
                        return true;
                    }

                    return DateTimeOffset.TryParse(source.AsSpan(contentStart, contentEndExclusive - contentStart), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
#endif
                }

                var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                if (DateTimeOffset.TryParseExact(parsed, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
                {
                    return true;
                }

                return DateTimeOffset.TryParse(parsed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
            }

#if NETSTANDARD2_0
            var token = source.Substring(start, endExclusive - start);
            if (DateTimeOffset.TryParseExact(token, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                return true;
            }

            return DateTimeOffset.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
#else
            if (DateTimeOffset.TryParseExact(source.AsSpan(start, endExclusive - start), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                return true;
            }

            return DateTimeOffset.TryParse(source.AsSpan(start, endExclusive - start), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
#endif
        }

        private static bool TryParseTimeSpan(string source, int start, int endExclusive, out TimeSpan value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (start < endExclusive && source[start] == Constants.DOUBLE_QUOTE)
            {
                if (TryGetSimpleQuotedContentRange(source, start, endExclusive, out var contentStart, out var contentEndExclusive))
                {
#if NETSTANDARD2_0
                    return TimeSpan.TryParse(source.Substring(contentStart, contentEndExclusive - contentStart), CultureInfo.InvariantCulture, out value);
#else
                    return TimeSpan.TryParse(source.AsSpan(contentStart, contentEndExclusive - contentStart), CultureInfo.InvariantCulture, out value);
#endif
                }

                var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                return TimeSpan.TryParse(parsed, CultureInfo.InvariantCulture, out value);
            }

#if NETSTANDARD2_0
            return TimeSpan.TryParse(source.Substring(start, endExclusive - start), CultureInfo.InvariantCulture, out value);
#else
            return TimeSpan.TryParse(source.AsSpan(start, endExclusive - start), CultureInfo.InvariantCulture, out value);
#endif
        }

#if NET6_0_OR_GREATER
        private static bool TryParseDateOnly(string source, int start, int endExclusive, out DateOnly value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (start < endExclusive && source[start] == Constants.DOUBLE_QUOTE)
            {
                if (TryGetSimpleQuotedContentRange(source, start, endExclusive, out var contentStart, out var contentEndExclusive))
                {
                    return DateOnly.TryParse(source.AsSpan(contentStart, contentEndExclusive - contentStart), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
                }

                var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                return DateOnly.TryParse(parsed, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
            }

            return DateOnly.TryParse(source.AsSpan(start, endExclusive - start), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        private static bool TryParseTimeOnly(string source, int start, int endExclusive, out TimeOnly value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (start < endExclusive && source[start] == Constants.DOUBLE_QUOTE)
            {
                if (TryGetSimpleQuotedContentRange(source, start, endExclusive, out var contentStart, out var contentEndExclusive))
                {
                    return TimeOnly.TryParse(source.AsSpan(contentStart, contentEndExclusive - contentStart), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
                }

                var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                return TimeOnly.TryParse(parsed, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
            }

            return TimeOnly.TryParse(source.AsSpan(start, endExclusive - start), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }
#endif

        private static bool TryParseGuid(string source, int start, int endExclusive, out Guid value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (start < endExclusive && source[start] == Constants.DOUBLE_QUOTE)
            {
                if (TryGetSimpleQuotedContentRange(source, start, endExclusive, out var contentStart, out var contentEndExclusive))
                {
#if NETSTANDARD2_0
                    return Guid.TryParse(source.Substring(contentStart, contentEndExclusive - contentStart), out value);
#else
                    return Guid.TryParse(source.AsSpan(contentStart, contentEndExclusive - contentStart), out value);
#endif
                }

                var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                return Guid.TryParse(parsed, out value);
            }

#if NETSTANDARD2_0
            return Guid.TryParse(source.Substring(start, endExclusive - start), out value);
#else
            return Guid.TryParse(source.AsSpan(start, endExclusive - start), out value);
#endif
        }

        private sealed class PropertyPlan
        {
            /// <summary>
            /// Initializes a property assignment plan.
            /// </summary>
            /// <param name="plan">The type plan for the property value.</param>
            /// <param name="setter">A compiled setter for assigning boxed values.</param>
            /// <param name="primitiveSetter">An optional zero-allocation primitive setter for scalar source ranges.</param>
            public PropertyPlan(TypePlan plan, Action<object, object?> setter, PrimitiveSetterDelegate? primitiveSetter)
            {
                Plan = plan;
                Setter = setter;
                PrimitiveSetter = primitiveSetter;
            }

            /// <summary>Gets the plan used to materialize this property's value.</summary>
            public TypePlan Plan { get; }
            /// <summary>Gets the compiled setter used for object, collection, and fallback scalar assignments.</summary>
            public Action<object, object?> Setter { get; }
            /// <summary>Gets the optimized primitive setter, when the property type can be parsed directly from a source range.</summary>
            public PrimitiveSetterDelegate? PrimitiveSetter { get; }

            /// <summary>
            /// Attempts to parse and assign a primitive value from a source string range.
            /// </summary>
            /// <param name="instance">The target object instance.</param>
            /// <param name="source">The original source string.</param>
            /// <param name="start">The inclusive start index of the primitive token.</param>
            /// <param name="endExclusive">The exclusive end index of the primitive token.</param>
            /// <param name="alreadyTrimmed">Whether the supplied range is already trimmed.</param>
            /// <returns><see langword="true"/> when the primitive setter parsed and assigned the value.</returns>
            public bool TrySetPrimitiveLikeValue(object instance, string source, int start, int endExclusive, bool alreadyTrimmed = false)
            {
                return PrimitiveSetter != null && PrimitiveSetter(instance, source, start, endExclusive, alreadyTrimmed);
            }
        }

        /// <summary>
        /// Assigns a primitive property directly from a token range in the original source string.
        /// </summary>
        private delegate bool PrimitiveSetterDelegate(object instance, string source, int start, int endExclusive, bool alreadyTrimmed);

        /// <summary>
        /// Maps an encoded TOON property name to its property plan.
        /// </summary>
        private readonly struct PropertyAlias
        {
            /// <summary>
            /// Initializes an encoded-name alias for a property.
            /// </summary>
            /// <param name="name">The encoded TOON field name.</param>
            /// <param name="property">The property plan assigned by the alias.</param>
            public PropertyAlias(string name, PropertyPlan property)
            {
                Name = name;
                Property = property;
            }

            /// <summary>Gets the encoded TOON field name.</summary>
            public string Name { get; }
            /// <summary>Gets the property plan assigned by this alias.</summary>
            public PropertyPlan Property { get; }
        }

        /// <summary>
        /// Describes a constructor parameter that can be populated from a TOON field.
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
        /// Classifies the materialization strategy for a target type.
        /// </summary>
        private enum PlanKind
        {
            /// <summary>The type cannot be handled by the direct materializer.</summary>
            Unsupported,
            /// <summary>The type is scalar and can be parsed from a primitive token.</summary>
            Primitive,
            /// <summary>The type is <see cref="Nullable{T}"/> and delegates to an underlying plan.</summary>
            Nullable,
            /// <summary>The type is a writable class with a parameterless constructor.</summary>
            Object,
            /// <summary>The type is a supported collection.</summary>
            Collection,
            /// <summary>The type is an array.</summary>
            Array,
            /// <summary>The type is a string-keyed dictionary.</summary>
            Dictionary,
            /// <summary>The type is <see cref="object"/> and should materialize native values dynamically.</summary>
            Any
        }

        /// <summary>
        /// Classifies a parsed source field before value materialization.
        /// </summary>
        private enum ParsedFieldKind
        {
            /// <summary>The field is a primitive value without an object key.</summary>
            Primitive,
            /// <summary>The field is a key/value pair.</summary>
            KeyValue,
            /// <summary>The field is an array header.</summary>
            ArrayHeader
        }

        /// <summary>
        /// Stores the parsed shape of a field while preserving source ranges for direct primitive assignment.
        /// </summary>
        private sealed class ParsedFieldInfo
        {
            private ParsedFieldInfo(ParsedFieldKind kind)
            {
                Kind = kind;
            }

            /// <summary>Gets the parsed field category.</summary>
            public ParsedFieldKind Kind { get; }
            /// <summary>Gets the materialized key when it was parsed as text.</summary>
            public string? Key { get; private set; }
            /// <summary>Gets the inclusive source start for the key token when available.</summary>
            public int KeyStart { get; private set; } = -1;
            /// <summary>Gets the exclusive source end for the key token when available.</summary>
            public int KeyEndExclusive { get; private set; } = -1;
            /// <summary>Gets whether this field has a source range for the key token.</summary>
            public bool HasKeyRange => KeyStart >= 0;
            /// <summary>Gets the inclusive source start for the primitive value token.</summary>
            public int ValueStart { get; private set; } = -1;
            /// <summary>Gets parsed array header metadata when this field is an array header.</summary>
            public ArrayHeaderParseResult? ArrayHeader { get; private set; }

            public static ParsedFieldInfo Primitive() => new ParsedFieldInfo(ParsedFieldKind.Primitive);

            public static ParsedFieldInfo FromKeyValue(string key, int valueStart)
            {
                return new ParsedFieldInfo(ParsedFieldKind.KeyValue)
                {
                    Key = key,
                    ValueStart = valueStart
                };
            }

            public static ParsedFieldInfo FromKeyRange(int keyStart, int keyEndExclusive, int valueStart)
            {
                return new ParsedFieldInfo(ParsedFieldKind.KeyValue)
                {
                    KeyStart = keyStart,
                    KeyEndExclusive = keyEndExclusive,
                    ValueStart = valueStart
                };
            }

            public static ParsedFieldInfo FromArrayHeader(ArrayHeaderParseResult arrayHeader)
            {
                return new ParsedFieldInfo(ParsedFieldKind.ArrayHeader)
                {
                    ArrayHeader = arrayHeader
                };
            }
        }

        private sealed class TypePlan
        {
            private TypePlan(Type type, PlanKind kind)
            {
                Type = type;
                Kind = kind;
            }

            public Type Type { get; }
            public PlanKind Kind { get; }
            public bool IsSupported => Kind != PlanKind.Unsupported;
            public bool IsNullable => Kind == PlanKind.Nullable;
            public TypePlan? UnderlyingPlan { get; private set; }
            public TypePlan? ElementPlan { get; private set; }
            public Func<object>? Factory { get; private set; }
            public Dictionary<string, PropertyPlan>? Properties { get; private set; }
            public PropertyAlias[]? PropertyAliases { get; private set; }
            public Dictionary<int, PropertyAlias[]>? PropertyAliasBuckets { get; private set; }
            public Func<object?[], object>? ConstructorFactory { get; private set; }
            public ConstructorParameterPlan[]? ConstructorParameters { get; private set; }
            public Dictionary<string, ConstructorParameterPlan>? ConstructorParameterLookup { get; private set; }
            public Func<int, IList>? CreateList { get; private set; }
            public Func<IList, object>? FinalizeCollection { get; private set; }
            public Func<IDictionary>? CreateDictionary { get; private set; }
            public ConcurrentDictionary<IReadOnlyList<string>, PropertyPlan?[]>? ColumnPlanCache { get; private set; }
            public ConcurrentDictionary<IReadOnlyList<string>, ConstructorParameterPlan?[]>? ConstructorColumnPlanCache { get; private set; }
            public ExtensionDataAccessor? Extension { get; private set; }
            public int ExtensionArgument { get; private set; } = -1;
            public HashSet<string>? UnboundMembers { get; private set; }

            public Dictionary<string, PropertyPlan>? ConstructorMembers { get; private set; }

            public int ConstructorMemberSlot => ConstructorParameters!.Length + (Extension != null ? 1 : 0);

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
            public static TypePlan Primitive(Type type) => new TypePlan(type, PlanKind.Primitive);
            public static TypePlan Any(Type type) => new TypePlan(type, PlanKind.Any);

            public static TypePlan Nullable(Type type, TypePlan underlyingPlan)
            {
                return new TypePlan(type, PlanKind.Nullable)
                {
                    UnderlyingPlan = underlyingPlan
                };
            }

            public static TypePlan Object(
                Type type,
                Func<object> factory,
                Dictionary<string, PropertyPlan> properties,
                PropertyAlias[] propertyAliases,
                Dictionary<int, PropertyAlias[]> propertyAliasBuckets)
            {
                return new TypePlan(type, PlanKind.Object)
                {
                    Factory = factory,
                    Properties = properties,
                    PropertyAliases = propertyAliases,
                    PropertyAliasBuckets = propertyAliasBuckets,
                    ColumnPlanCache = new ConcurrentDictionary<IReadOnlyList<string>, PropertyPlan?[]>(FieldListComparer.Instance)
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
                    ConstructorParameterLookup = constructorParameterLookup,
                    ConstructorColumnPlanCache = new ConcurrentDictionary<IReadOnlyList<string>, ConstructorParameterPlan?[]>(FieldListComparer.Instance)
                };
            }

            public static TypePlan Collection(Type type, TypePlan elementPlan, Func<int, IList> createList, Func<IList, object> finalize)
            {
                return new TypePlan(type, PlanKind.Collection)
                {
                    ElementPlan = elementPlan,
                    CreateList = createList,
                    FinalizeCollection = finalize
                };
            }

            public static TypePlan Array(Type type, TypePlan elementPlan, Func<int, IList> createList, Func<IList, object> finalize)
            {
                return new TypePlan(type, PlanKind.Array)
                {
                    ElementPlan = elementPlan,
                    CreateList = createList,
                    FinalizeCollection = finalize
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

        private sealed class FieldListComparer : IEqualityComparer<IReadOnlyList<string>>
        {
            public static FieldListComparer Instance { get; } = new FieldListComparer();

            public bool Equals(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null || x.Count != y.Count)
                {
                    return false;
                }

                for (int i = 0; i < x.Count; i++)
                {
                    if (!string.Equals(x[i], y[i], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(IReadOnlyList<string> fields)
            {
                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + fields.Count;
                    for (int i = 0; i < fields.Count; i++)
                    {
                        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(fields[i]);
                    }

                    return hash;
                }
            }
        }
    }
}
