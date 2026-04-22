#nullable enable
using System;
using System.Collections.Generic;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode;

/// <summary>
/// Converts scanned TOON lines into the internal native node graph, including objects, list arrays, inline arrays, and tabular arrays.
/// </summary>
internal static class NativeDecoders
{
    /// <summary>
    /// Decodes the next complete value from the current cursor position.
    /// </summary>
    /// <param name="cursor">The cursor positioned at the first line of the value.</param>
    /// <param name="options">Resolved decode options controlling indentation, strictness, and object-array layout.</param>
    /// <param name="quotedKeys">Optional set populated with quoted root keys so path expansion can distinguish literal dotted keys.</param>
    /// <returns>The decoded native node.</returns>
    public static NativeNode? DecodeValueFromLines(LineCursor cursor, ResolvedDecodeOptions options, HashSet<string>? quotedKeys = null)
    {
        var first = cursor.Peek();
        if (first.IsNone)
            throw ToonFormatException.Syntax("No content to decode");

        if (Parser.IsArrayHeaderAfterHyphen(first.Content))
        {
            var headerInfo = Parser.ParseArrayHeaderLine(first.Content, Constants.DEFAULT_DELIMITER_CHAR);
            if (headerInfo != null)
            {
                cursor.Advance();
                return DecodeArrayFromHeader(headerInfo.Header, headerInfo.InlineValues, cursor, 0, options);
            }
        }

        if (cursor.Length == 1 && !IsKeyValueLine(first))
            return Parser.ParsePrimitiveNativeToken(first.Content.Trim());

        return DecodeObject(cursor, 0, options, quotedKeys);
    }

    private static bool IsKeyValueLine(ParsedLine line)
    {
        var content = line.Content;
        if (content.StartsWith("\"", StringComparison.Ordinal))
        {
            var closingQuoteIndex = StringUtils.FindClosingQuote(content, 0);
            if (closingQuoteIndex == -1)
                return false;

            return content.Substring(closingQuoteIndex + 1).Contains(Constants.COLON);
        }

        return content.Contains(Constants.COLON);
    }

    private static NativeObjectNode DecodeObject(LineCursor cursor, int baseDepth, ResolvedDecodeOptions options, HashSet<string>? quotedKeys = null)
    {
        var obj = new NativeObjectNode();
        int? computedDepth = null;

        while (!cursor.AtEnd())
        {
            var line = cursor.Peek();
            if (line.IsNone || line.Depth < baseDepth)
                break;

            if (computedDepth == null && line.Depth >= baseDepth)
                computedDepth = line.Depth;

            if (line.Depth == computedDepth)
            {
                var (key, value, wasQuoted) = DecodeKeyValuePair(line, cursor, computedDepth.Value, options);
                obj[key] = value;

                if (wasQuoted && quotedKeys != null && baseDepth == 0)
                    quotedKeys.Add(key);
            }
            else
            {
                break;
            }
        }

        return obj;
    }

    private sealed class KeyValueDecodeResult
    {
        /// <summary>The decoded object key.</summary>
        public string Key { get; set; } = string.Empty;
        /// <summary>The decoded value for the key.</summary>
        public NativeNode? Value { get; set; }
        /// <summary>The depth at which child lines for this value are expected.</summary>
        public int FollowDepth { get; set; }
        /// <summary>Whether the key token was explicitly quoted in source.</summary>
        public bool WasQuoted { get; set; }
    }

    private static KeyValueDecodeResult DecodeKeyValue(string content, LineCursor cursor, int baseDepth, ResolvedDecodeOptions options, bool isListItemFirstField = false)
    {
        var arrayHeader = Parser.ParseArrayHeaderLine(content, Constants.DEFAULT_DELIMITER_CHAR);
        if (arrayHeader != null && arrayHeader.Header.Key != null)
        {
            var effectiveDepth = isListItemFirstField ? baseDepth + 1 : baseDepth;
            var value = DecodeArrayFromHeader(arrayHeader.Header, arrayHeader.InlineValues, cursor, effectiveDepth, options);
            return new KeyValueDecodeResult
            {
                Key = arrayHeader.Header.Key,
                Value = value,
                FollowDepth = baseDepth + 1,
                WasQuoted = false
            };
        }

        var keyResult = Parser.ParseKeyToken(content, 0);
        var rest = content.Substring(keyResult.End).Trim();

        if (string.IsNullOrEmpty(rest))
        {
            var nextLine = cursor.Peek();
            if (!nextLine.IsNone && nextLine.Depth > baseDepth)
            {
                var nested = DecodeObject(cursor, baseDepth + 1, options);
                return new KeyValueDecodeResult { Key = keyResult.Key, Value = nested, FollowDepth = baseDepth + 1, WasQuoted = keyResult.WasQuoted };
            }

            return new KeyValueDecodeResult { Key = keyResult.Key, Value = new NativeObjectNode(), FollowDepth = baseDepth + 1, WasQuoted = keyResult.WasQuoted };
        }

        var primitiveValue = Parser.ParsePrimitiveNativeToken(rest);
        return new KeyValueDecodeResult { Key = keyResult.Key, Value = primitiveValue, FollowDepth = baseDepth + 1, WasQuoted = keyResult.WasQuoted };
    }

    private static (string key, NativeNode? value, bool wasQuoted) DecodeKeyValuePair(ParsedLine line, LineCursor cursor, int baseDepth, ResolvedDecodeOptions options)
    {
        cursor.Advance();
        var result = DecodeKeyValue(line.Content, cursor, baseDepth, options);
        return (result.Key, result.Value, result.WasQuoted);
    }

    private static NativeNode DecodeArrayFromHeader(ArrayHeaderInfo header, string? inlineValues, LineCursor cursor, int baseDepth, ResolvedDecodeOptions options)
    {
        if (inlineValues != null)
        {
            return CreateArrayNode(DecodeInlinePrimitiveArray(header, inlineValues, options));
        }

        if (header.Fields != null && header.Fields.Count > 0)
        {
            return CreateArrayNode(DecodeTabularArray(header, cursor, baseDepth, options));
        }

        return CreateArrayNode(DecodeListArray(header, cursor, baseDepth, options));
    }

    private static NativeArrayNode CreateArrayNode<TNode>(IReadOnlyCollection<TNode> items) where TNode : NativeNode?
    {
        var array = new NativeArrayNode();
        foreach (var item in items)
        {
            array.Add(item);
        }

        return array;
    }

    private static List<NativeNode?> DecodeInlinePrimitiveArray(ArrayHeaderInfo header, string inlineValues, ResolvedDecodeOptions options)
    {
        if (string.IsNullOrWhiteSpace(inlineValues))
        {
            Validation.AssertExpectedCount(0, header.Length, "inline array items", options);
            return new List<NativeNode?>();
        }

        var values = Parser.ParseDelimitedValues(inlineValues, header.Delimiter);
        var primitives = new List<NativeNode?>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            primitives.Add(Parser.ParsePrimitiveNativeToken(values[i]));
        }

        Validation.AssertExpectedCount(primitives.Count, header.Length, "inline array items", options);
        return primitives;
    }

    private static List<NativeNode?> DecodeListArray(ArrayHeaderInfo header, LineCursor cursor, int baseDepth, ResolvedDecodeOptions options)
    {
        var items = new List<NativeNode?>(header.Length > 0 ? header.Length : 4);
        var itemDepth = baseDepth + 1;
        int? startLine = null;
        int? endLine = null;

        while (!cursor.AtEnd() && items.Count < header.Length)
        {
            var line = cursor.Peek();
            if (line.IsNone || line.Depth < itemDepth)
                break;

            var isListItem = line.Content.StartsWith(Constants.LIST_ITEM_PREFIX, StringComparison.Ordinal) || line.Content == "-";
            if (line.Depth == itemDepth && isListItem)
            {
                if (startLine == null)
                    startLine = line.LineNumber;
                endLine = line.LineNumber;

                var item = DecodeListItem(cursor, itemDepth, options);
                items.Add(item);

                var currentLine = cursor.Current();
                if (!currentLine.IsNone)
                    endLine = currentLine.LineNumber;
            }
            else
            {
                break;
            }
        }

        Validation.AssertExpectedCount(items.Count, header.Length, "list array items", options);

        if (options.Strict && startLine != null && endLine != null)
        {
            Validation.ValidateNoBlankLinesInRange(startLine.Value, endLine.Value, cursor.GetBlankLines(), options.Strict, "list array");
            Validation.ValidateNoExtraListItems(cursor, itemDepth, header.Length);
        }

        return items;
    }

    private static List<NativeObjectNode> DecodeTabularArray(ArrayHeaderInfo header, LineCursor cursor, int baseDepth, ResolvedDecodeOptions options)
    {
        var objects = new List<NativeObjectNode>(header.Length > 0 ? header.Length : 4);
        var rowDepth = baseDepth + 1;
        int? startLine = null;
        int? endLine = null;

        while (!cursor.AtEnd() && objects.Count < header.Length)
        {
            var line = cursor.Peek();
            if (line.IsNone || line.Depth < rowDepth)
                break;

            if (line.Depth == rowDepth)
            {
                if (startLine == null)
                    startLine = line.LineNumber;
                endLine = line.LineNumber;

                cursor.Advance();
                var values = Parser.ParseDelimitedValues(line.Content, header.Delimiter);
                Validation.AssertExpectedCount(values.Count, header.Fields!.Count, "tabular row values", options);

                var obj = new NativeObjectNode();
                for (int i = 0; i < header.Fields.Count; i++)
                {
                    obj[header.Fields[i]] = Parser.ParsePrimitiveNativeToken(values[i]);
                }

                var followDepth = rowDepth + 1;
                while (!cursor.AtEnd())
                {
                    var continuation = cursor.Peek();
                    if (continuation.IsNone || continuation.Depth < followDepth)
                        break;

                    if (continuation.Depth == followDepth && !continuation.Content.StartsWith(Constants.LIST_ITEM_PREFIX, StringComparison.Ordinal))
                    {
                        var (key, value, _) = DecodeKeyValuePair(continuation, cursor, followDepth, options);
                        obj[key] = value;
                        continue;
                    }

                    break;
                }

                objects.Add(obj);
            }
            else
            {
                break;
            }
        }

        Validation.AssertExpectedCount(objects.Count, header.Length, "tabular rows", options);

        if (options.Strict && startLine != null && endLine != null)
        {
            Validation.ValidateNoBlankLinesInRange(startLine.Value, endLine.Value, cursor.GetBlankLines(), options.Strict, "tabular array");
            Validation.ValidateNoExtraTabularRows(cursor, rowDepth, header);
        }

        return objects;
    }

    private static NativeNode? DecodeListItem(LineCursor cursor, int baseDepth, ResolvedDecodeOptions options)
    {
        var line = cursor.Next();
        if (line.IsNone)
            throw ToonFormatException.Syntax("Expected list item");

        string afterHyphen;
        if (line.Content == "-")
            return new NativeObjectNode();

        if (line.Content.StartsWith(Constants.LIST_ITEM_PREFIX, StringComparison.Ordinal))
            afterHyphen = line.Content.Substring(Constants.LIST_ITEM_PREFIX.Length);
        else
            throw ToonFormatException.Syntax($"Expected list item to start with \"{Constants.LIST_ITEM_PREFIX}\"");

        if (string.IsNullOrWhiteSpace(afterHyphen))
            return new NativeObjectNode();

        if (Parser.IsArrayHeaderAfterHyphen(afterHyphen))
        {
            var arrayHeader = Parser.ParseArrayHeaderLine(afterHyphen, Constants.DEFAULT_DELIMITER_CHAR);
            if (arrayHeader != null)
                return DecodeArrayFromHeader(arrayHeader.Header, arrayHeader.InlineValues, cursor, baseDepth, options);
        }

        if (Parser.IsObjectFirstFieldAfterHyphen(afterHyphen))
            return DecodeObjectFromListItem(line, cursor, baseDepth, options);

        return Parser.ParsePrimitiveNativeToken(afterHyphen);
    }

    private static NativeObjectNode DecodeObjectFromListItem(ParsedLine firstLine, LineCursor cursor, int baseDepth, ResolvedDecodeOptions options)
    {
        var afterHyphen = firstLine.Content.Substring(Constants.LIST_ITEM_PREFIX.Length);
        var firstField = DecodeKeyValue(afterHyphen, cursor, baseDepth, options, isListItemFirstField: true);

        var obj = new NativeObjectNode
        {
            [firstField.Key] = firstField.Value
        };

        while (!cursor.AtEnd())
        {
            var line = cursor.Peek();
            if (line.IsNone || line.Depth < firstField.FollowDepth)
                break;

            if (line.Depth == firstField.FollowDepth && !line.Content.StartsWith(Constants.LIST_ITEM_PREFIX, StringComparison.Ordinal))
            {
                var (key, value, _) = DecodeKeyValuePair(line, cursor, firstField.FollowDepth, options);
                obj[key] = value;
            }
            else
            {
                break;
            }
        }

        return obj;
    }
}
