#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Encode;

internal static class NativeEncoders
{
    public static string EncodeValue(NativeNode? value, ResolvedEncodeOptions options)
    {
        if (NativeNormalize.IsPrimitive(value))
            return NativePrimitives.EncodePrimitive(value, options.Delimiter);

        var writer = new LineWriter(options.Indent);
        if (value is NativeArrayNode arrayNode)
            EncodeArray(null, arrayNode, writer, 0, options);
        else if (value is NativeObjectNode objectNode)
            EncodeObject(objectNode, writer, 0, options);

        return writer.ToString();
    }

    private static void EncodeObject(NativeObjectNode value, LineWriter writer, int depth, ResolvedEncodeOptions options, IReadOnlyCollection<string>? rootLiteralKeys = null, string? pathPrefix = null, int? remainingDepth = null)
    {
        var keys = new string[value.Count];
        int keysIndex = 0;
        foreach (var kvp in value)
            keys[keysIndex++] = kvp.Key;

        if (depth == 0 && rootLiteralKeys == null)
        {
            HashSet<string>? literalKeys = null;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].Contains('.'))
                {
                    literalKeys ??= new HashSet<string>(StringComparer.Ordinal);
                    literalKeys.Add(keys[i]);
                }
            }

            rootLiteralKeys = (IReadOnlyCollection<string>?)literalKeys ?? Array.Empty<string>();
        }

        var effectiveFlattenDepth = remainingDepth ?? options.FlattenDepth;

        foreach (var kvp in value)
        {
            EncodeKeyValuePair(kvp.Key, kvp.Value, writer, depth, options, keys, rootLiteralKeys, pathPrefix, effectiveFlattenDepth);
        }
    }

    private static void EncodeKeyValuePair(string key, NativeNode? value, LineWriter writer, int depth, ResolvedEncodeOptions options, IReadOnlyCollection<string>? siblings = null, IReadOnlyCollection<string>? rootLiteralKeys = null, string? pathPrefix = null, int? flattenDepth = null)
    {
        var currentPath = pathPrefix != null ? $"{pathPrefix}{Constants.DOT}{key}" : key;
        var effectiveFlattenDepth = flattenDepth ?? options.FlattenDepth;

        if (options.KeyFolding == ToonKeyFolding.Safe && siblings != null)
        {
            var foldResult = NativeFolding.TryFoldKeyChain(key, value, siblings, options, rootLiteralKeys, pathPrefix, effectiveFlattenDepth);
            if (foldResult != null)
            {
                var encodedFoldedKey = NativePrimitives.EncodeKey(foldResult.FoldedKey);
                if (foldResult.Remainder == null)
                {
                    if (NativeNormalize.IsPrimitive(foldResult.LeafValue))
                    {
                        writer.Push(depth, $"{encodedFoldedKey}: {NativePrimitives.EncodePrimitive(foldResult.LeafValue, options.Delimiter)}");
                        return;
                    }

                    if (foldResult.LeafValue is NativeArrayNode leafArray)
                    {
                        EncodeArray(foldResult.FoldedKey, leafArray, writer, depth, options);
                        return;
                    }

                    if (NativeNormalize.IsEmptyObject(foldResult.LeafValue))
                    {
                        writer.Push(depth, $"{encodedFoldedKey}:");
                        return;
                    }
                }

                if (foldResult.Remainder is NativeObjectNode remainderObject)
                {
                    writer.Push(depth, $"{encodedFoldedKey}:");
                    var foldedPath = pathPrefix != null ? $"{pathPrefix}{Constants.DOT}{foldResult.FoldedKey}" : foldResult.FoldedKey;
                    EncodeObject(remainderObject, writer, depth + 1, options, rootLiteralKeys, foldedPath, effectiveFlattenDepth - foldResult.SegmentCount);
                    return;
                }
            }
        }

        var encodedKey = NativePrimitives.EncodeKey(key);
        if (NativeNormalize.IsPrimitive(value))
        {
            if (options.IgnoreNullOrEmpty && (value is null || value is NativePrimitiveNode { Value: null or "" }))
                return;
            writer.Push(depth, builder =>
            {
                builder.Append(encodedKey);
                builder.Append(Constants.COLON);
                builder.Append(Constants.SPACE);
                NativePrimitives.AppendPrimitive(builder, value, options.Delimiter);
            });
        }
        else if (value is NativeArrayNode arrayNode)
        {
            if (options.ExcludeEmptyArrays && arrayNode.Count == 0)
                return;
            EncodeArray(key, arrayNode, writer, depth, options);
        }
        else if (value is NativeObjectNode objectNode)
        {
            writer.Push(depth, builder =>
            {
                builder.Append(encodedKey);
                builder.Append(Constants.COLON);
            });
            if (!NativeNormalize.IsEmptyObject(objectNode))
                EncodeObject(objectNode, writer, depth + 1, options, rootLiteralKeys, currentPath, effectiveFlattenDepth);
        }
    }

    internal static void EncodeNamedValue(string key, object? rawValue, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        EncodeKeyValuePair(key, NativeNormalize.Normalize(rawValue), writer, depth, options);
    }

    internal static string EncodeNamedValue(string key, object? rawValue, int depth, ResolvedEncodeOptions options)
    {
        var writer = new LineWriter(options.Indent);
        EncodeNamedValue(key, rawValue, writer, depth, options);
        return writer.ToString();
    }

    private static void EncodeArray(string? key, NativeArrayNode value, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (value.Count == 0)
        {
            writer.Push(depth, builder => NativePrimitives.AppendHeader(builder, 0, key, null, options.Delimiter));
            return;
        }

        if (NativeNormalize.IsArrayOfPrimitives(value))
        {
            writer.Push(depth, builder => AppendInlineArrayLine(builder, value, options.Delimiter, key));
            return;
        }

        if (NativeNormalize.IsArrayOfArrays(value))
        {
            var primitiveArrays = value.All(item => item is NativeArrayNode array && NativeNormalize.IsArrayOfPrimitives(array));
            if (primitiveArrays)
            {
                var arrays = new List<NativeArrayNode>(value.Count);
                foreach (var item in value)
                {
                    arrays.Add((NativeArrayNode)item!);
                }

                EncodeArrayOfArraysAsListItems(key, arrays, writer, depth, options);
                return;
            }
        }

        if (NativeNormalize.IsArrayOfObjects(value))
        {
            var objects = new List<NativeObjectNode>(value.Count);
            foreach (var item in value)
            {
                objects.Add((NativeObjectNode)item!);
            }

            var header = ExtractTabularHeader(objects);
            if (header != null)
            {
                if (options.IgnoreNullOrEmpty)
                    header = FilterNullOrEmptyColumns(objects, header);
                EncodeArrayOfObjectsAsTabular(key, objects, header, writer, depth, options);
            }
            else if (options.ObjectArrayLayout == ToonObjectArrayLayout.Columnar &&
                     TryExtractColumnarHeader(objects, out var hybridHeader))
            {
                if (options.IgnoreNullOrEmpty)
                    hybridHeader = FilterNullOrEmptyColumns(objects, hybridHeader);
                EncodeArrayOfObjectsAsColumnar(key, objects, hybridHeader, writer, depth, options);
            }
            else
            {
                EncodeMixedArrayAsListItems(key, value, writer, depth, options);
            }

            return;
        }

        EncodeMixedArrayAsListItems(key, value, writer, depth, options);
    }

    private static void EncodeArrayOfArraysAsListItems(string? prefix, IReadOnlyList<NativeArrayNode> values, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        writer.Push(depth, builder => NativePrimitives.AppendHeader(builder, values.Count, prefix, null, options.Delimiter));
        foreach (var array in values)
        {
            writer.PushListItem(depth + 1, builder => AppendInlineArrayLine(builder, array, options.Delimiter, null));
        }
    }

    private static string EncodeInlineArrayLine(NativeArrayNode values, char delimiter, string? prefix = null)
    {
        var builder = new StringBuilder();
        AppendInlineArrayLine(builder, values, delimiter, prefix);
        return builder.ToString();
    }

    private static void AppendInlineArrayLine(StringBuilder builder, NativeArrayNode values, char delimiter, string? prefix = null)
    {
        NativePrimitives.AppendHeader(builder, values.Count, prefix, null, delimiter);
        if (values.Count == 0)
        {
            return;
        }

        builder.Append(Constants.SPACE);
        NativePrimitives.AppendJoinedPrimitives(builder, values, delimiter);
    }

    private static void EncodeArrayOfObjectsAsTabular(string? prefix, IReadOnlyList<NativeObjectNode> rows, IReadOnlyList<string> header, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        writer.Push(depth, builder => NativePrimitives.AppendHeader(builder, rows.Count, prefix, header, options.Delimiter));
        WriteTabularRows(rows, header, writer, depth + 1, options);
    }

    private static void EncodeArrayOfObjectsAsColumnar(string? prefix, IReadOnlyList<NativeObjectNode> rows, IReadOnlyList<string> header, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        writer.Push(depth, builder => NativePrimitives.AppendHeader(builder, rows.Count, prefix, header, options.Delimiter));
        var headerKeys = new HashSet<string>(header, StringComparer.Ordinal);

        foreach (var row in rows)
        {
            writer.Push(depth + 1, builder => AppendPrimitiveRow(builder, row, header, options.Delimiter));

            foreach (var kvp in row)
            {
                if (headerKeys.Contains(kvp.Key))
                    continue;

                EncodeKeyValuePair(kvp.Key, kvp.Value, writer, depth + 2, options);
            }
        }
    }

    private static IReadOnlyList<string> FilterNullOrEmptyColumns(IReadOnlyList<NativeObjectNode> rows, IReadOnlyList<string> header)
    {
        var active = new List<string>(header.Count);
        for (int ci = 0; ci < header.Count; ci++)
        {
            var col = header[ci];
            bool allNullOrEmpty = true;
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var node = rows[ri][col];
                if (node is NativePrimitiveNode p)
                {
                    if (p.Value is not null && !(p.Value is string s && s.Length == 0))
                    {
                        allNullOrEmpty = false;
                        break;
                    }
                }
                else if (node is not null)
                {
                    allNullOrEmpty = false;
                    break;
                }
            }
            if (!allNullOrEmpty)
                active.Add(col);
        }
        return active;
    }

    private static IReadOnlyList<string>? ExtractTabularHeader(IReadOnlyList<NativeObjectNode> rows)
    {
        if (rows.Count == 0)
            return null;

        var firstKeys = new List<string>(rows[0].Count);
        foreach (var kvp in rows[0])
        {
            firstKeys.Add(kvp.Key);
        }

        if (firstKeys.Count == 0)
            return null;

        return IsTabularArray(rows, firstKeys) ? firstKeys : null;
    }

    private static bool TryExtractColumnarHeader(IReadOnlyList<NativeObjectNode> rows, out IReadOnlyList<string> header)
    {
        header = Array.Empty<string>();
        if (rows.Count == 0)
            return false;

        var candidateKeys = new List<string>(rows[0].Count);
        foreach (var kvp in rows[0])
        {
            candidateKeys.Add(kvp.Key);
        }

        if (candidateKeys.Count == 0)
            return false;

        var sharedPrimitiveKeys = new List<string>();
        foreach (var key in candidateKeys)
        {
            var isSharedPrimitive = true;
            foreach (var row in rows)
            {
                if (!row.ContainsKey(key) || !NativeNormalize.IsPrimitive(row[key]))
                {
                    isSharedPrimitive = false;
                    break;
                }
            }

            if (isSharedPrimitive)
                sharedPrimitiveKeys.Add(key);
        }

        if (sharedPrimitiveKeys.Count == 0)
            return false;

        var sharedPrimitiveKeySet = new HashSet<string>(sharedPrimitiveKeys, StringComparer.Ordinal);
        var hasNonHeaderValues = false;
        foreach (var row in rows)
        {
            foreach (var kvp in row)
            {
                if (!sharedPrimitiveKeySet.Contains(kvp.Key))
                {
                    hasNonHeaderValues = true;
                    break;
                }
            }

            if (hasNonHeaderValues)
            {
                break;
            }
        }

        if (!hasNonHeaderValues)
        {
            return false;
        }

        header = sharedPrimitiveKeys;
        return true;
    }

    private static bool IsTabularArray(IReadOnlyList<NativeObjectNode> rows, IReadOnlyList<string> header)
    {
        foreach (var row in rows)
        {
            if (row.Count != header.Count)
                return false;

            foreach (var key in header)
            {
                if (!row.ContainsKey(key) || !NativeNormalize.IsPrimitive(row[key]))
                    return false;
            }
        }

        return true;
    }

    private static void WriteTabularRows(IReadOnlyList<NativeObjectNode> rows, IReadOnlyList<string> header, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        foreach (var row in rows)
        {
            writer.Push(depth, builder => AppendPrimitiveRow(builder, row, header, options.Delimiter));
        }
    }

    private static string EncodePrimitiveRow(NativeObjectNode row, IReadOnlyList<string> header, char delimiter)
    {
        var builder = new StringBuilder();
        AppendPrimitiveRow(builder, row, header, delimiter);
        return builder.ToString();
    }

    private static void AppendPrimitiveRow(StringBuilder builder, NativeObjectNode row, IReadOnlyList<string> header, char delimiter)
    {
        for (int i = 0; i < header.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(delimiter);
            }

            NativePrimitives.AppendPrimitive(builder, row[header[i]], delimiter);
        }
    }

    private static void EncodeMixedArrayAsListItems(string? prefix, NativeArrayNode items, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        writer.Push(depth, builder => NativePrimitives.AppendHeader(builder, items.Count, prefix, null, options.Delimiter));
        foreach (var item in items)
        {
            EncodeListItemValue(item, writer, depth + 1, options);
        }
    }

    private static void EncodeObjectAsListItem(NativeObjectNode obj, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        var keys = new List<string>(obj.Count);
        foreach (var kvp in obj)
        {
            keys.Add(kvp.Key);
        }

        if (keys.Count == 0)
        {
            writer.Push(depth, Constants.LIST_ITEM_MARKER.ToString());
            return;
        }

        var firstKey = keys[0];
        var firstValue = obj[firstKey];
        var encodedKey = NativePrimitives.EncodeKey(firstKey);

        if (NativeNormalize.IsPrimitive(firstValue))
        {
            writer.PushListItem(depth, builder =>
            {
                builder.Append(encodedKey);
                builder.Append(Constants.COLON);
                builder.Append(Constants.SPACE);
                NativePrimitives.AppendPrimitive(builder, firstValue, options.Delimiter);
            });
        }
        else if (firstValue is NativeArrayNode arrayNode)
        {
            if (NativeNormalize.IsArrayOfPrimitives(arrayNode))
            {
                writer.PushListItem(depth, builder => AppendInlineArrayLine(builder, arrayNode, options.Delimiter, firstKey));
            }
            else if (NativeNormalize.IsArrayOfObjects(arrayNode))
            {
                var objects = new List<NativeObjectNode>(arrayNode.Count);
                foreach (var item in arrayNode)
                {
                    objects.Add((NativeObjectNode)item!);
                }

                var header = ExtractTabularHeader(objects);
                if (header != null)
                {
                    if (options.IgnoreNullOrEmpty)
                        header = FilterNullOrEmptyColumns(objects, header);
                    writer.PushListItem(depth, builder => NativePrimitives.AppendHeader(builder, arrayNode.Count, firstKey, header, options.Delimiter));
                    WriteTabularRows(objects, header, writer, depth + 2, options);
                }
                else if (options.ObjectArrayLayout == ToonObjectArrayLayout.Columnar &&
                         TryExtractColumnarHeader(objects, out var hybridHeader))
                {
                    if (options.IgnoreNullOrEmpty)
                        hybridHeader = FilterNullOrEmptyColumns(objects, hybridHeader);
                    writer.PushListItem(depth, builder => NativePrimitives.AppendHeader(builder, arrayNode.Count, firstKey, hybridHeader, options.Delimiter));
                    var hybridHeaderKeys = new HashSet<string>(hybridHeader, StringComparer.Ordinal);
                    foreach (var row in objects)
                    {
                        writer.Push(depth + 2, builder => AppendPrimitiveRow(builder, row, hybridHeader, options.Delimiter));
                        foreach (var kvp in row)
                        {
                            if (hybridHeaderKeys.Contains(kvp.Key))
                                continue;

                            EncodeKeyValuePair(kvp.Key, kvp.Value, writer, depth + 3, options);
                        }
                    }
                }
                else
                {
                    writer.PushListItem(depth, builder => NativePrimitives.AppendHeader(builder, arrayNode.Count, firstKey, null, options.Delimiter));
                    foreach (var item in arrayNode)
                    {
                        EncodeObjectAsListItem((NativeObjectNode)item!, writer, depth + 2, options);
                    }
                }
            }
            else
            {
                writer.PushListItem(depth, builder => NativePrimitives.AppendHeader(builder, arrayNode.Count, firstKey, null, options.Delimiter));
                foreach (var item in arrayNode)
                {
                    EncodeListItemValue(item, writer, depth + 2, options);
                }
            }
        }
        else if (firstValue is NativeObjectNode nestedObject)
        {
            writer.PushListItem(depth, builder =>
            {
                builder.Append(encodedKey);
                builder.Append(Constants.COLON);
            });
            if (!NativeNormalize.IsEmptyObject(nestedObject))
                EncodeObject(nestedObject, writer, depth + 2, options);
        }

        for (int i = 1; i < keys.Count; i++)
        {
            EncodeKeyValuePair(keys[i], obj[keys[i]], writer, depth + 1, options);
        }
    }

    private static void EncodeListItemValue(NativeNode? value, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (NativeNormalize.IsPrimitive(value))
        {
            writer.PushListItem(depth, builder => NativePrimitives.AppendPrimitive(builder, value, options.Delimiter));
            return;
        }

        if (value is NativeArrayNode arrayNode && NativeNormalize.IsArrayOfPrimitives(arrayNode))
        {
            writer.PushListItem(depth, builder => AppendInlineArrayLine(builder, arrayNode, options.Delimiter, null));
            return;
        }

        if (value is NativeObjectNode objectNode)
        {
            EncodeObjectAsListItem(objectNode, writer, depth, options);
            return;
        }

        if (value is NativeArrayNode complexArray)
        {
            if (NativeNormalize.IsArrayOfObjects(complexArray))
            {
                var objects = new List<NativeObjectNode>(complexArray.Count);
                foreach (var item in complexArray)
                {
                    objects.Add((NativeObjectNode)item!);
                }

                var header = ExtractTabularHeader(objects);
                if (header != null)
                {
                    if (options.IgnoreNullOrEmpty)
                        header = FilterNullOrEmptyColumns(objects, header);
                    writer.PushListItem(depth, builder => NativePrimitives.AppendHeader(builder, complexArray.Count, null, header, options.Delimiter));
                    WriteTabularRows(objects, header, writer, depth + 2, options);
                    return;
                }
                if (options.ObjectArrayLayout == ToonObjectArrayLayout.Columnar &&
                    TryExtractColumnarHeader(objects, out var hybridHeader))
                {
                    if (options.IgnoreNullOrEmpty)
                        hybridHeader = FilterNullOrEmptyColumns(objects, hybridHeader);
                    writer.PushListItem(depth, builder => NativePrimitives.AppendHeader(builder, complexArray.Count, null, hybridHeader, options.Delimiter));
                    var hybridHeaderKeys = new HashSet<string>(hybridHeader, StringComparer.Ordinal);
                    foreach (var row in objects)
                    {
                        writer.Push(depth + 2, builder => AppendPrimitiveRow(builder, row, hybridHeader, options.Delimiter));
                        foreach (var kvp in row)
                        {
                            if (hybridHeaderKeys.Contains(kvp.Key))
                                continue;

                            EncodeKeyValuePair(kvp.Key, kvp.Value, writer, depth + 3, options);
                        }
                    }
                    return;
                }
            }

            writer.PushListItem(depth, builder => NativePrimitives.AppendHeader(builder, complexArray.Count, null, null, options.Delimiter));
            foreach (var item in complexArray)
            {
                EncodeListItemValue(item, writer, depth + 1, options);
            }
        }
    }
}

internal static class NativePrimitives
{
    private const int StringLiteralCacheMaxLength = 128;
    private static readonly ConcurrentDictionary<string, string> EncodedKeyCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> EncodedCommaStringLiteralCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> EncodedTabStringLiteralCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> EncodedPipeStringLiteralCache = new(StringComparer.Ordinal);

    private static string FormatNumber(double value)
    {
        if (value == 0.0)
            return "0";

        Span<char> initialBuffer = stackalloc char[64];
        var gFormat = FormatToString(value, "G16", initialBuffer);
        if (!gFormat.Contains('E') && !gFormat.Contains('e'))
            return gFormat;

        var absValue = Math.Abs(value);
        int decimalPlaces = absValue < 1.0 && absValue > 0.0
            ? Math.Max(0, -(int)Math.Floor(Math.Log10(absValue)) + 15)
            : 15;
        var result = FormatToString(value, "F" + decimalPlaces, initialBuffer);
        if (result.Contains('.'))
        {
            result = result.TrimEnd('0');
            if (result.EndsWith(Constants.DOT.ToString()))
                result = result.TrimEnd(Constants.DOT);
        }

        return result;
    }

    public static string EncodePrimitive(NativeNode? value, char delimiter = Constants.COMMA)
    {
        var builder = new StringBuilder();
        AppendPrimitive(builder, value, delimiter);
        return builder.ToString();
    }

    public static void AppendPrimitive(StringBuilder builder, NativeNode? value, char delimiter = Constants.COMMA)
    {
        if (value is not NativePrimitiveNode primitive)
        {
            builder.Append(Constants.NULL_LITERAL);
            return;
        }

        AppendPrimitiveRaw(builder, primitive.Value, delimiter);
    }

    public static void AppendPrimitiveRaw(StringBuilder builder, object? value, char delimiter = Constants.COMMA)
    {
        switch (value)
        {
            case null:
                builder.Append(Constants.NULL_LITERAL);
                return;
            case bool boolValue:
                builder.Append(boolValue ? Constants.TRUE_LITERAL : Constants.FALSE_LITERAL);
                return;
            case int intValue:
                AppendInvariant(builder, intValue);
                return;
            case long longValue:
                AppendInvariant(builder, longValue);
                return;
            case byte byteValue:
                AppendInvariant(builder, byteValue);
                return;
            case sbyte sbyteValue:
                AppendInvariant(builder, sbyteValue);
                return;
            case short shortValue:
                AppendInvariant(builder, shortValue);
                return;
            case ushort ushortValue:
                AppendInvariant(builder, ushortValue);
                return;
            case uint uintValue:
                AppendInvariant(builder, uintValue);
                return;
            case ulong ulongValue:
                AppendInvariant(builder, ulongValue);
                return;
            case decimal decimalValue:
                AppendInvariant(builder, decimalValue);
                return;
            case float floatValue:
                var normalizedFloat = FloatUtils.NormalizeSignedZero(floatValue);
                if (!NumericUtils.IsFinite(normalizedFloat))
                {
                    builder.Append(Constants.NULL_LITERAL);
                    return;
                }

                AppendDouble(builder, normalizedFloat);
                return;
            case double doubleValue:
                var normalizedDouble = FloatUtils.NormalizeSignedZero(doubleValue);
                if (!NumericUtils.IsFinite(normalizedDouble))
                {
                    builder.Append(Constants.NULL_LITERAL);
                    return;
                }

                AppendDouble(builder, normalizedDouble);
                return;
            case string stringValue:
                AppendStringLiteral(builder, stringValue, delimiter);
                return;
            case DateTime dateTime:
                AppendDateTimeRaw(builder, dateTime);
                return;
            case DateTimeOffset dateTimeOffset:
                AppendDateTimeRaw(builder, dateTimeOffset);
                return;
            case DeferredDateTimeValue dateTimeValue:
                AppendDateTimeRaw(builder, dateTimeValue.Value);
                return;
            case DeferredDateTimeOffsetValue dateTimeOffsetValue:
                AppendDateTimeRaw(builder, dateTimeOffsetValue.Value);
                return;
#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                AppendStringLiteral(builder, FormatRoundTrip(dateOnly), delimiter);
                return;
            case TimeOnly timeOnly:
                AppendStringLiteral(builder, FormatRoundTrip(timeOnly), delimiter);
                return;
            case DeferredDateOnlyValue dateOnlyValue:
                AppendStringLiteral(builder, FormatRoundTrip(dateOnlyValue.Value), delimiter);
                return;
            case DeferredTimeOnlyValue timeOnlyValue:
                AppendStringLiteral(builder, FormatRoundTrip(timeOnlyValue.Value), delimiter);
                return;
#endif
            default:
                AppendStringLiteral(builder, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, delimiter);
                return;
        }
    }

    public static void AppendPrimitive(CompactBufferWriter writer, NativeNode? value, char delimiter = Constants.COMMA)
    {
        if (value is not NativePrimitiveNode primitive)
        {
            writer.Append(Constants.NULL_LITERAL);
            return;
        }

        AppendPrimitiveRaw(writer, primitive.Value, delimiter);
    }

    public static void AppendPrimitiveRaw(CompactBufferWriter writer, object? value, char delimiter = Constants.COMMA)
    {
        switch (value)
        {
            case null:
                writer.Append(Constants.NULL_LITERAL);
                return;
            case bool boolValue:
                writer.Append(boolValue ? Constants.TRUE_LITERAL : Constants.FALSE_LITERAL);
                return;
            case int intValue:
                AppendInvariant(writer, intValue);
                return;
            case long longValue:
                AppendInvariant(writer, longValue);
                return;
            case byte byteValue:
                AppendInvariant(writer, byteValue);
                return;
            case sbyte sbyteValue:
                AppendInvariant(writer, sbyteValue);
                return;
            case short shortValue:
                AppendInvariant(writer, shortValue);
                return;
            case ushort ushortValue:
                AppendInvariant(writer, ushortValue);
                return;
            case uint uintValue:
                AppendInvariant(writer, uintValue);
                return;
            case ulong ulongValue:
                AppendInvariant(writer, ulongValue);
                return;
            case decimal decimalValue:
                AppendInvariant(writer, decimalValue);
                return;
            case float floatValue:
                var normalizedFloat = FloatUtils.NormalizeSignedZero(floatValue);
                if (!NumericUtils.IsFinite(normalizedFloat))
                {
                    writer.Append(Constants.NULL_LITERAL);
                    return;
                }

                AppendDouble(writer, normalizedFloat);
                return;
            case double doubleValue:
                var normalizedDouble = FloatUtils.NormalizeSignedZero(doubleValue);
                if (!NumericUtils.IsFinite(normalizedDouble))
                {
                    writer.Append(Constants.NULL_LITERAL);
                    return;
                }

                AppendDouble(writer, normalizedDouble);
                return;
            case string stringValue:
                AppendStringLiteral(writer, stringValue, delimiter);
                return;
            case DateTime dateTime:
                AppendDateTimeRaw(writer, dateTime);
                return;
            case DateTimeOffset dateTimeOffset:
                AppendDateTimeRaw(writer, dateTimeOffset);
                return;
            case DeferredDateTimeValue dateTimeValue:
                AppendDateTimeRaw(writer, dateTimeValue.Value);
                return;
            case DeferredDateTimeOffsetValue dateTimeOffsetValue:
                AppendDateTimeRaw(writer, dateTimeOffsetValue.Value);
                return;
#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                AppendStringLiteral(writer, FormatRoundTrip(dateOnly), delimiter);
                return;
            case TimeOnly timeOnly:
                AppendStringLiteral(writer, FormatRoundTrip(timeOnly), delimiter);
                return;
            case DeferredDateOnlyValue dateOnlyValue:
                AppendStringLiteral(writer, FormatRoundTrip(dateOnlyValue.Value), delimiter);
                return;
            case DeferredTimeOnlyValue timeOnlyValue:
                AppendStringLiteral(writer, FormatRoundTrip(timeOnlyValue.Value), delimiter);
                return;
#endif
            default:
                AppendStringLiteral(writer, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, delimiter);
                return;
        }
    }

    public static string EncodeStringLiteral(string value, char delimiter = Constants.COMMA)
    {
        var builder = new StringBuilder(value.Length + 2);
        AppendStringLiteral(builder, value, delimiter);
        return builder.ToString();
    }

    public static void AppendStringLiteral(StringBuilder builder, string value, char delimiter = Constants.COMMA)
    {
        if (TryAppendCachedStringLiteral(builder, value, delimiter))
        {
            return;
        }

        if (ValidationShared.IsSafeUnquoted(value, Constants.FromDelimiterChar(delimiter)))
        {
            builder.Append(value);
            return;
        }

        builder.Append(Constants.DOUBLE_QUOTE);
        StringUtils.AppendEscapedString(builder, value);
        builder.Append(Constants.DOUBLE_QUOTE);
    }

    public static void AppendStringLiteral(CompactBufferWriter writer, string value, char delimiter = Constants.COMMA)
    {
        if (TryAppendCachedStringLiteral(writer, value, delimiter))
        {
            return;
        }

        if (ValidationShared.IsSafeUnquoted(value, Constants.FromDelimiterChar(delimiter)))
        {
            writer.Append(value);
            return;
        }

        writer.Append(Constants.DOUBLE_QUOTE);
        StringUtils.AppendEscapedString(writer, value);
        writer.Append(Constants.DOUBLE_QUOTE);
    }

    public static string EncodeKey(string key) => EncodedKeyCache.GetOrAdd(key, static currentKey =>
    {
        if (ValidationShared.IsValidUnquotedKey(currentKey))
            return currentKey;

        var builder = new StringBuilder(currentKey.Length + 2);
        builder.Append(Constants.DOUBLE_QUOTE);
        StringUtils.AppendEscapedString(builder, currentKey);
        builder.Append(Constants.DOUBLE_QUOTE);
        return builder.ToString();
    });

    private static bool TryAppendCachedStringLiteral(StringBuilder builder, string value, char delimiter)
    {
        if (value.Length > StringLiteralCacheMaxLength)
        {
            return false;
        }

        builder.Append(GetStringLiteralCache(delimiter).GetOrAdd(value, currentValue => EncodeStringLiteralUncached(currentValue, delimiter)));
        return true;
    }

    private static bool TryAppendCachedStringLiteral(CompactBufferWriter writer, string value, char delimiter)
    {
        if (value.Length > StringLiteralCacheMaxLength)
        {
            return false;
        }

        writer.Append(GetStringLiteralCache(delimiter).GetOrAdd(value, currentValue => EncodeStringLiteralUncached(currentValue, delimiter)));
        return true;
    }

    private static string EncodeStringLiteralUncached(string value, char delimiter)
    {
        if (ValidationShared.IsSafeUnquoted(value, Constants.FromDelimiterChar(delimiter)))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append(Constants.DOUBLE_QUOTE);
        StringUtils.AppendEscapedString(builder, value);
        builder.Append(Constants.DOUBLE_QUOTE);
        return builder.ToString();
    }

    private static ConcurrentDictionary<string, string> GetStringLiteralCache(char delimiter)
    {
        return delimiter switch
        {
            Constants.COMMA => EncodedCommaStringLiteralCache,
            Constants.TAB => EncodedTabStringLiteralCache,
            Constants.PIPE => EncodedPipeStringLiteralCache,
            _ => EncodedCommaStringLiteralCache
        };
    }

    public static string EncodeAndJoinPrimitives(IEnumerable<NativeNode?> values, char delimiter = Constants.COMMA)
    {
        var builder = new StringBuilder();
        AppendJoinedPrimitives(builder, values, delimiter);
        return builder.ToString();
    }

    public static void AppendJoinedPrimitives(StringBuilder builder, IEnumerable<NativeNode?> values, char delimiter = Constants.COMMA)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(delimiter);
            }

            AppendPrimitive(builder, value, delimiter);
            first = false;
        }
    }

    public static void AppendJoinedPrimitives(CompactBufferWriter writer, IEnumerable<NativeNode?> values, char delimiter = Constants.COMMA)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                writer.Append(delimiter);
            }

            AppendPrimitive(writer, value, delimiter);
            first = false;
        }
    }

    public static string FormatHeader(int length, string? key = null, IReadOnlyList<string>? fields = null, char? delimiter = null)
    {
        var builder = new StringBuilder();
        AppendHeader(builder, length, key, fields, delimiter);
        return builder.ToString();
    }

    public static void AppendHeader(StringBuilder builder, int length, string? key = null, IReadOnlyList<string>? fields = null, char? delimiter = null)
        => AppendHeader(builder, length, null, key, fields, delimiter);

    public static void AppendHeader(StringBuilder builder, int length, string? encodedKey, string? key, IReadOnlyList<string>? fields = null, char? delimiter = null)
    {
        var delimiterChar = delimiter ?? Constants.DEFAULT_DELIMITER_CHAR;
        if (!string.IsNullOrEmpty(encodedKey))
        {
            builder.Append(encodedKey);
        }
        else if (key is { Length: > 0 } actualKey)
        {
            builder.Append(EncodeKey(actualKey));
        }

        builder.Append(Constants.OPEN_BRACKET);
        builder.Append(length);
        if (delimiterChar != Constants.DEFAULT_DELIMITER_CHAR)
        {
            builder.Append(delimiterChar);
        }

        builder.Append(Constants.CLOSE_BRACKET);

        if (fields != null && fields.Count > 0)
        {
            builder.Append(Constants.OPEN_BRACE);
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(delimiterChar);
                }

                builder.Append(EncodeKey(fields[i]));
            }

            builder.Append(Constants.CLOSE_BRACE);
        }

        builder.Append(Constants.COLON);
    }

    public static void AppendHeader(CompactBufferWriter writer, int length, string? key = null, IReadOnlyList<string>? fields = null, char? delimiter = null)
        => AppendHeader(writer, length, null, key, fields, delimiter);

    public static void AppendHeader(CompactBufferWriter writer, int length, string? encodedKey, string? key, IReadOnlyList<string>? fields = null, char? delimiter = null)
    {
        var delimiterChar = delimiter ?? Constants.DEFAULT_DELIMITER_CHAR;
        if (!string.IsNullOrEmpty(encodedKey))
        {
            writer.Append(encodedKey);
        }
        else if (key is { Length: > 0 } actualKey)
        {
            writer.Append(EncodeKey(actualKey));
        }

        writer.Append(Constants.OPEN_BRACKET);
        AppendInvariant(writer, length);
        if (delimiterChar != Constants.DEFAULT_DELIMITER_CHAR)
        {
            writer.Append(delimiterChar);
        }

        writer.Append(Constants.CLOSE_BRACKET);

        if (fields != null && fields.Count > 0)
        {
            writer.Append(Constants.OPEN_BRACE);
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    writer.Append(delimiterChar);
                }

                writer.Append(EncodeKey(fields[i]));
            }

            writer.Append(Constants.CLOSE_BRACE);
        }

        writer.Append(Constants.COLON);
    }

    internal static void AppendInvariant(StringBuilder builder, int value)
#if NETSTANDARD2_0
        => builder.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(builder, static (Span<char> buffer, out int written, int current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    internal static void AppendInvariant(StringBuilder builder, long value)
#if NETSTANDARD2_0
        => builder.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(builder, static (Span<char> buffer, out int written, long current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(StringBuilder builder, byte value)
#if NETSTANDARD2_0
        => builder.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(builder, static (Span<char> buffer, out int written, byte current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(StringBuilder builder, sbyte value)
#if NETSTANDARD2_0
        => builder.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(builder, static (Span<char> buffer, out int written, sbyte current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(StringBuilder builder, short value)
#if NETSTANDARD2_0
        => builder.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(builder, static (Span<char> buffer, out int written, short current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(StringBuilder builder, ushort value)
#if NETSTANDARD2_0
        => builder.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(builder, static (Span<char> buffer, out int written, ushort current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(StringBuilder builder, uint value)
#if NETSTANDARD2_0
        => builder.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(builder, static (Span<char> buffer, out int written, uint current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(StringBuilder builder, ulong value)
#if NETSTANDARD2_0
        => builder.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(builder, static (Span<char> buffer, out int written, ulong current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    internal static void AppendInvariant(StringBuilder builder, decimal value)
#if NETSTANDARD2_0
        => builder.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(builder, static (Span<char> buffer, out int written, decimal current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    internal static void AppendInvariant(CompactBufferWriter writer, int value)
#if NETSTANDARD2_0
        => writer.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(writer, static (Span<char> buffer, out int written, int current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    internal static void AppendInvariant(CompactBufferWriter writer, long value)
#if NETSTANDARD2_0
        => writer.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(writer, static (Span<char> buffer, out int written, long current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(CompactBufferWriter writer, byte value)
#if NETSTANDARD2_0
        => writer.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(writer, static (Span<char> buffer, out int written, byte current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(CompactBufferWriter writer, sbyte value)
#if NETSTANDARD2_0
        => writer.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(writer, static (Span<char> buffer, out int written, sbyte current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(CompactBufferWriter writer, short value)
#if NETSTANDARD2_0
        => writer.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(writer, static (Span<char> buffer, out int written, short current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(CompactBufferWriter writer, ushort value)
#if NETSTANDARD2_0
        => writer.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(writer, static (Span<char> buffer, out int written, ushort current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(CompactBufferWriter writer, uint value)
#if NETSTANDARD2_0
        => writer.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(writer, static (Span<char> buffer, out int written, uint current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    private static void AppendInvariant(CompactBufferWriter writer, ulong value)
#if NETSTANDARD2_0
        => writer.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(writer, static (Span<char> buffer, out int written, ulong current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    internal static void AppendInvariant(CompactBufferWriter writer, decimal value)
#if NETSTANDARD2_0
        => writer.Append(value.ToString(CultureInfo.InvariantCulture));
#else
        => AppendFormatted(writer, static (Span<char> buffer, out int written, decimal current) => current.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture), value);
#endif

    /// <summary>
    /// Appends a double to the builder without allocating a string for the common case (no exponent notation).
    /// Falls back to FormatNumber only for values requiring exponent notation, which is rare for real-world data.
    /// </summary>
    internal static void AppendDouble(StringBuilder builder, double value)
    {
        if (value == 0.0)
        {
            builder.Append('0');
            return;
        }

#if NETSTANDARD2_0
        builder.Append(FormatNumber(value));
#else
        Span<char> buffer = stackalloc char[64];
        if (value.TryFormat(buffer, out var written, "G16", CultureInfo.InvariantCulture))
        {
            var span = buffer.Slice(0, written);
            if (span.IndexOf('E') < 0 && span.IndexOf('e') < 0)
            {
                builder.Append(span);
                return;
            }
        }

        // Exponent notation (very small or very large numbers) — fallback to string allocation
        builder.Append(FormatNumber(value));
#endif
    }

    /// <summary>
    /// Appends a double to the writer without allocating a string for the common case (no exponent notation).
    /// Falls back to FormatNumber only for values requiring exponent notation, which is rare for real-world data.
    /// </summary>
    internal static void AppendDouble(CompactBufferWriter writer, double value)
    {
        if (value == 0.0)
        {
            writer.Append('0');
            return;
        }

#if NETSTANDARD2_0
        writer.Append(FormatNumber(value));
#else
        Span<char> buffer = stackalloc char[64];
        if (value.TryFormat(buffer, out var written, "G16", CultureInfo.InvariantCulture))
        {
            var span = buffer.Slice(0, written);
            if (span.IndexOf('E') < 0 && span.IndexOf('e') < 0)
            {
                writer.Append(span);
                return;
            }
        }

        // Exponent notation (very small or very large numbers) — fallback to string allocation
        writer.Append(FormatNumber(value));
#endif
    }

#if NETSTANDARD2_0
    private static void AppendFormatted<T>(StringBuilder builder, Func<T, string> formatter, T value)
        => builder.Append(formatter(value));

    private static void AppendFormatted<T>(CompactBufferWriter writer, Func<T, string> formatter, T value)
        => writer.Append(formatter(value));
#else
    private delegate bool TryFormatDelegate<T>(Span<char> buffer, out int written, T value);

    private static void AppendFormatted<T>(StringBuilder builder, TryFormatDelegate<T> formatter, T value)
    {
        Span<char> buffer = stackalloc char[64];
        if (formatter(buffer, out var written, value))
        {
            builder.Append(buffer[..written]);
            return;
        }

        var rented = ArrayPool<char>.Shared.Rent(128);
        try
        {
            if (formatter(rented, out written, value))
            {
                builder.Append(rented, 0, written);
                return;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }

        builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static void AppendFormatted<T>(CompactBufferWriter writer, TryFormatDelegate<T> formatter, T value)
    {
        Span<char> buffer = stackalloc char[64];
        if (formatter(buffer, out var written, value))
        {
            writer.Append(buffer[..written]);
            return;
        }

        var rented = ArrayPool<char>.Shared.Rent(128);
        try
        {
            if (formatter(rented, out written, value))
            {
                writer.Append(rented.AsSpan(0, written));
                return;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }

        writer.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
    }
#endif

    /// <summary>
    /// Appends a DateTime as a quoted ISO 8601 "O" string directly into the builder without an intermediate string allocation.
    /// DateTime "O" format never contains characters requiring TOON escaping, so quoting is straightforward.
    /// </summary>
    internal static void AppendDateTimeRaw(StringBuilder builder, DateTime value)
    {
        builder.Append(Constants.DOUBLE_QUOTE);
        builder.Append(value.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(Constants.DOUBLE_QUOTE);
    }

    /// <summary>
    /// Appends a DateTimeOffset as a quoted ISO 8601 "O" string directly into the builder.
    /// </summary>
    internal static void AppendDateTimeRaw(StringBuilder builder, DateTimeOffset value)
    {
        builder.Append(Constants.DOUBLE_QUOTE);
        builder.Append(value.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(Constants.DOUBLE_QUOTE);
    }

    /// <summary>
    /// Appends a DateTime as a quoted ISO 8601 "O" string directly into the writer without an intermediate string allocation.
    /// </summary>
    internal static void AppendDateTimeRaw(CompactBufferWriter writer, DateTime value)
    {
        writer.Append(Constants.DOUBLE_QUOTE);
        writer.Append(value.ToString("O", CultureInfo.InvariantCulture));
        writer.Append(Constants.DOUBLE_QUOTE);
    }

    /// <summary>
    /// Appends a DateTimeOffset as a quoted ISO 8601 "O" string directly into the writer.
    /// </summary>
    internal static void AppendDateTimeRaw(CompactBufferWriter writer, DateTimeOffset value)
    {
        writer.Append(Constants.DOUBLE_QUOTE);
        writer.Append(value.ToString("O", CultureInfo.InvariantCulture));
        writer.Append(Constants.DOUBLE_QUOTE);
    }

    private static string FormatToString(double value, string format, Span<char> initialBuffer)
    {
#if NETSTANDARD2_0
        return value.ToString(format, CultureInfo.InvariantCulture);
#else
        if (value.TryFormat(initialBuffer, out var written, format, CultureInfo.InvariantCulture))
        {
            return initialBuffer.Slice(0, written).ToString();
        }

        var rented = ArrayPool<char>.Shared.Rent(384);
        try
        {
            if (value.TryFormat(rented, out written, format, CultureInfo.InvariantCulture))
            {
                return new string(rented, 0, written);
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }

        return value.ToString(format, CultureInfo.InvariantCulture);
#endif
    }

    private static string FormatRoundTrip(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string FormatRoundTrip(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

#if NET6_0_OR_GREATER
    private static string FormatRoundTrip(DateOnly value)
    {
        Span<char> buffer = stackalloc char[32];
        return value.TryFormat(buffer, out var written, "O", null)
            ? new string(buffer[..written])
            : value.ToString("O");
    }

    private static string FormatRoundTrip(TimeOnly value)
    {
        Span<char> buffer = stackalloc char[64];
        return value.TryFormat(buffer, out var written, "O", null)
            ? new string(buffer[..written])
            : value.ToString("O");
    }
#endif
}

internal static class NativeFolding
{
    internal sealed class KeyChain
    {
        public IReadOnlyCollection<string> Segments { get; set; } = Array.Empty<string>();
        public NativeNode? Tail { get; set; }
        public NativeNode LeafValue { get; set; } = new NativePrimitiveNode(null);
    }

    internal sealed class FoldResult
    {
        public string FoldedKey { get; set; } = string.Empty;
        public NativeNode LeafValue { get; set; } = new NativePrimitiveNode(null);
        public NativeNode? Remainder { get; set; }
        public int SegmentCount { get; set; }
    }

    public static FoldResult? TryFoldKeyChain(string key, NativeNode? value, IReadOnlyCollection<string> siblings, ResolvedEncodeOptions options, IReadOnlyCollection<string>? rootLiteralKeys = null, string? pathPrefix = null, int? flattenDepth = null)
    {
        if (options.KeyFolding != ToonKeyFolding.Safe || value is not NativeObjectNode)
            return null;

        var effectiveFlattenDepth = flattenDepth ?? options.FlattenDepth;
        var keyChain = CollectSingleKeyChain(key, value, effectiveFlattenDepth);
        var segments = keyChain.Segments;
        var tail = keyChain.Tail;
        var leafValue = keyChain.LeafValue;

        if (segments.Count < 2 || !segments.All(ValidationShared.IsIdentifierSegment))
            return null;

        var foldedKey = string.Join(Constants.DOT.ToString(), segments);
        var absolutePath = pathPrefix != null ? $"{pathPrefix}{Constants.DOT}{foldedKey}" : foldedKey;
        if (siblings.Contains(foldedKey) || (rootLiteralKeys != null && rootLiteralKeys.Contains(absolutePath)))
            return null;

        return new FoldResult
        {
            FoldedKey = foldedKey,
            Remainder = tail,
            LeafValue = leafValue,
            SegmentCount = segments.Count
        };
    }

    private static KeyChain CollectSingleKeyChain(string startKey, NativeNode? startValue, int maxDepth)
    {
        var segments = new List<string> { startKey };
        var currentValue = startValue;

        while (segments.Count < maxDepth)
        {
            if (currentValue is not NativeObjectNode objectNode)
                break;

            if (objectNode.Count != 1)
                break;

            var next = objectNode.First();
            segments.Add(next.Key);
            currentValue = next.Value;
        }

        if (currentValue is not NativeObjectNode || NativeNormalize.IsEmptyObject(currentValue))
        {
            return new KeyChain
            {
                Segments = segments,
                Tail = null,
                LeafValue = currentValue ?? new NativePrimitiveNode(null)
            };
        }

        return new KeyChain
        {
            Segments = segments,
            Tail = currentValue,
            LeafValue = currentValue
        };
    }
}
