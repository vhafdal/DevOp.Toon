#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode
{
    /// <summary>
    /// High-performance typed decoder that materializes CLR objects directly from scanned TOON lines without first building a native node graph.
    /// </summary>
    internal static class DirectMaterializer
    {
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

                if (TryBuildCollectionPlan(type, building, out var collectionPlan))
                {
                    return collectionPlan;
                }

                if (!type.IsClass || type == typeof(string))
                {
                    return TypePlan.Unsupported(type);
                }

                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    return TypePlan.Unsupported(type);
                }

                var properties = new Dictionary<string, PropertyPlan>(StringComparer.Ordinal);
                var aliases = new List<PropertyAlias>(16);
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

                    var propertyEntry = new PropertyPlan(
                        propertyPlan,
                        CreateSetter(property),
                        CreatePrimitiveSetter(property, propertyPlan));
                    var toonName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name;
                    properties[property.Name] = propertyEntry;
                    aliases.Add(new PropertyAlias(property.Name, propertyEntry));
                    if (toonName is { Length: > 0 })
                    {
                        properties[toonName] = propertyEntry;
                        aliases.Add(new PropertyAlias(toonName, propertyEntry));
                    }
                }

                var propertyAliases = aliases.ToArray();
                return TypePlan.Object(type, CreateFactory(ctor), properties, propertyAliases, BuildAliasBuckets(propertyAliases));
            }
            finally
            {
                building.Remove(type);
            }
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
                value = null;
                return false;
            }

            var instance = plan.Factory();
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
                ReadObjectField(instance, cursor, line.Content, computedDepth.Value, plan, options);
            }

            value = instance;
            return true;
        }

        private static void ReadObjectField(
            object instance,
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
                if (!TryGetProperty(plan, content, field.ArrayHeader, out var property))
                {
                    SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options);
                    return;
                }

                if (TryReadArrayFromHeader(cursor, property.Plan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options, out var arrayValue))
                {
                    property.Setter(instance, arrayValue);
                    return;
                }

                SkipArrayFromHeader(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options);
                return;
            }

            if (!TryGetProperty(plan, content, field, out var propertyPlan))
            {
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

            var items = plan.CreateList(header.Length);

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

            var tokenRanges = new Parser.TokenRange[header.Length];
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
            if (elementPlan.Kind != PlanKind.Object || elementPlan.Factory == null || header.Fields == null)
            {
                return false;
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
                for (int i = 0; i < columnPlans.Length; i++)
                {
                    var property = columnPlans[i];
                    if (property == null)
                    {
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
                        ReadObjectField(instance, cursor, continuation.Content, followDepth, elementPlan, options);
                        continue;
                    }

                    break;
                }

                items.Add(instance);
                rowCount++;
            }

            return rowCount == header.Length;
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
                return TryReadArrayFromHeader(cursor, plan, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options, out value);
            }

            if (field.Kind == ParsedFieldKind.KeyValue)
            {
                if (plan.Kind == PlanKind.Object)
                {
                    return TryReadObjectFromListItem(cursor, plan, line, itemStart, field, baseDepth, options, out value);
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
                value = null;
                return false;
            }

            var instance = plan.Factory();
            ReadObjectField(instance, cursor, firstLine.Content, baseDepth, plan, options, firstField, firstFieldStart);

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
                    ReadObjectField(instance, cursor, line.Content, followDepth, plan, options);
                    continue;
                }

                break;
            }

            value = instance;
            return true;
        }

        private static bool TryCreateEmptyValue(TypePlan plan, out object? value)
        {
            if (plan.Kind == PlanKind.Object && plan.Factory != null)
            {
                value = plan.Factory();
                return true;
            }

            if ((plan.Kind == PlanKind.Collection || plan.Kind == PlanKind.Array) &&
                plan.CreateList != null &&
                plan.FinalizeCollection != null)
            {
                value = plan.FinalizeCollection(plan.CreateList(0));
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

                value = null;
                return false;
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

                value = null;
                return false;
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

                value = null;
                return false;
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

                value = null;
                return false;
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

                value = null;
                return false;
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

                value = null;
                return false;
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

                value = null;
                return false;
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

                value = null;
                return false;
            }

            if (type == typeof(double))
            {
#if NETSTANDARD2_0
                if (double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
#else
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
#endif
                {
                    value = doubleValue;
                    return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(float))
            {
#if NETSTANDARD2_0
                if (float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
#else
                if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
#endif
                {
                    value = floatValue;
                    return true;
                }

                value = null;
                return false;
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

                value = null;
                return false;
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
                if (TryConvertEnum(type, source, start, endExclusive, out value, alreadyTrimmed: true))
                {
                    return true;
                }

                value = null;
                return false;
            }

            value = null;
            return false;
        }

        private static bool TryConvertEnum(Type enumType, string source, int start, int endExclusive, out object? value, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
            if (start >= endExclusive)
            {
                value = null;
                return false;
            }

            if (source[start] == Constants.DOUBLE_QUOTE)
            {
                var parsed = Parser.ParseStringLiteral(source, start, endExclusive);
                if (Enum.IsDefined(enumType, parsed))
                {
                    value = Enum.Parse(enumType, parsed, false);
                    return true;
                }

                value = null;
                return false;
            }

            var token = source.AsSpan(start, endExclusive - start);
            var underlyingType = Enum.GetUnderlyingType(enumType);
            if (underlyingType == typeof(ulong) || underlyingType == typeof(uint) || underlyingType == typeof(ushort) || underlyingType == typeof(byte))
            {
#if NETSTANDARD2_0
                if (ulong.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedValue))
#else
                if (ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedValue))
#endif
                {
                    value = Enum.ToObject(enumType, unsignedValue);
                    return true;
                }
            }
            else
            {
#if NETSTANDARD2_0
                if (long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedValue))
#else
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedValue))
#endif
                {
                    value = Enum.ToObject(enumType, signedValue);
                    return true;
                }
            }

            var enumName = token.ToString();
            if (Enum.IsDefined(enumType, enumName))
            {
                value = Enum.Parse(enumType, enumName, false);
                return true;
            }

            value = null;
            return false;
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
            var list = new List<object?>(header.Length);

            if (inlineValues != null)
            {
                if (!string.IsNullOrWhiteSpace(inlineValues))
                {
                    var tokenRanges = new Parser.TokenRange[header.Length];
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
                obj[field.ArrayHeader.Header.Key!] = ReadUntypedArray(cursor, field.ArrayHeader.Header, field.ArrayHeader.InlineValues, baseDepth, options);
                return;
            }

            if (field.ValueStart >= content.Length)
            {
                var nextLine = cursor.Peek();
                if (!nextLine.IsNone && nextLine.Depth > baseDepth)
                {
                    TryReadUntypedObject(cursor, baseDepth + 1, options, out var nested);
                    obj[field.Key!] = nested;
                }
                else
                {
                    obj[field.Key!] = new Dictionary<string, object?>(StringComparer.Ordinal);
                }

                return;
            }

            TryConvertUntypedPrimitive(content, field.ValueStart, content.Length, out var primitive);
            obj[field.Key!] = primitive;
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
            if (double.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#else
            if (double.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
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
            if (double.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#else
            if (double.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
                return true;
            }

            return false;
        }

        private static bool TryParseAndSetFloat(object instance, string source, int start, int endExclusive, Action<object, float> setter, bool alreadyTrimmed = false)
        {
            NormalizeRange(source, ref start, ref endExclusive, alreadyTrimmed);
#if NETSTANDARD2_0
            if (float.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#else
            if (float.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
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
            if (float.TryParse(source.Substring(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#else
            if (float.TryParse(source.AsSpan(start, endExclusive - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
#endif
            {
                setter(instance, value);
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
            public Func<int, IList>? CreateList { get; private set; }
            public Func<IList, object>? FinalizeCollection { get; private set; }
            public ConcurrentDictionary<IReadOnlyList<string>, PropertyPlan?[]>? ColumnPlanCache { get; private set; }

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
