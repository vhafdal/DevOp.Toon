#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode
{
    /// <summary>
    /// Information about an array header.
    /// </summary>
    internal class ArrayHeaderInfo
    {
        public string? Key { get; set; }
        public int Length { get; set; }
        public char Delimiter { get; set; }
        public List<string>? Fields { get; set; }
    }

    /// <summary>
    /// Result of parsing an array header line.
    /// </summary>
    internal class ArrayHeaderParseResult
    {
        public ArrayHeaderInfo Header { get; set; } = null!;
        public int KeyStart { get; set; } = -1;
        public int KeyEndExclusive { get; set; } = -1;
        public bool HasKeyRange => KeyStart >= 0;
        public string? InlineValues { get; set; }
    }

    /// <summary>
    /// Parsing utilities for TOON format tokens, headers, and values.
    /// Aligned with TypeScript decode/parser.ts
    /// </summary>
    internal static class Parser
    {
        internal readonly struct TokenRange
        {
            public TokenRange(int start, int endExclusive)
            {
                Start = start;
                EndExclusive = endExclusive;
            }

            public int Start { get; }
            public int EndExclusive { get; }
        }

        // #region Array header parsing

        /// <summary>
        /// Parses an array header line like "key[3]:" or "users[#2,]{name,age}:".
        /// </summary>
        public static ArrayHeaderParseResult? ParseArrayHeaderLine(string content, char defaultDelimiter)
        {
            var trimmedStart = FindTrimmedStart(content, 0, content.Length);
            if (trimmedStart >= content.Length)
            {
                return null;
            }

            int bracketStart;
            if (content[trimmedStart] == Constants.DOUBLE_QUOTE)
            {
                var trimmed = content.AsSpan(trimmedStart);
                var closingQuoteIndex = StringUtils.FindClosingQuote(trimmed, 0);
                if (closingQuoteIndex == -1)
                {
                    return null;
                }

                bracketStart = trimmedStart + closingQuoteIndex + 1;
                if (bracketStart >= content.Length || content[bracketStart] != Constants.OPEN_BRACKET)
                {
                    return null;
                }
            }
            else
            {
                bracketStart = content.IndexOf(Constants.OPEN_BRACKET, trimmedStart);
            }

            if (bracketStart == -1)
                return null;

            var bracketEnd = content.IndexOf(Constants.CLOSE_BRACKET, bracketStart);
            if (bracketEnd == -1)
                return null;

            var colonIndex = FindHeaderColonIndex(content, bracketEnd + 1);
            if (colonIndex == -1)
                return null;

            int braceStart = -1;
            int braceEnd = -1;
            if (colonIndex > bracketEnd + 1)
            {
                braceStart = content.IndexOf(Constants.OPEN_BRACE, bracketEnd + 1);
                if (braceStart != -1 && braceStart < colonIndex)
                {
                    braceEnd = content.IndexOf(Constants.CLOSE_BRACE, braceStart + 1);
                    if (braceEnd == -1 || braceEnd >= colonIndex)
                    {
                        return null;
                    }
                }
                else
                {
                    braceStart = -1;
                }
            }

            string? key = null;
            int keyStart = -1;
            int keyEndExclusive = -1;
            if (bracketStart > trimmedStart)
            {
                keyStart = FindTrimmedStart(content, trimmedStart, bracketStart);
                keyEndExclusive = FindTrimmedEnd(content, keyStart, bracketStart);
                key = ParseKeyText(content, keyStart, keyEndExclusive);
            }

            // Try to parse bracket segment
            BracketSegmentResult parsedBracket;
            try
            {
                parsedBracket = ParseBracketSegment(content.AsSpan(bracketStart + 1, bracketEnd - bracketStart - 1), defaultDelimiter);
            }
            catch
            {
                return null;
            }

            List<string>? fields = null;
            if (braceStart != -1)
            {
                fields = ParseDelimitedValues(content.AsSpan(braceStart + 1, braceEnd - braceStart - 1), parsedBracket.Delimiter)
                    .Select(ParseStringLiteral)
                    .ToList();
            }

            var inlineValueStart = FindTrimmedStart(content, colonIndex + 1, content.Length);
            string? inlineValues = inlineValueStart < content.Length
                ? content.Substring(inlineValueStart, FindTrimmedEnd(content, inlineValueStart, content.Length) - inlineValueStart)
                : null;

            return new ArrayHeaderParseResult
            {
                Header = new ArrayHeaderInfo
                {
                    Key = key,
                    Length = parsedBracket.Length,
                    Delimiter = parsedBracket.Delimiter,
                    Fields = fields,
                },
                KeyStart = keyStart,
                KeyEndExclusive = keyEndExclusive,
                InlineValues = inlineValues
            };
        }

        private class BracketSegmentResult
        {
            public int Length { get; set; }
            public char Delimiter { get; set; }
        }

        private static int FindHeaderColonIndex(string content, int start)
        {
            bool inQuotes = false;
            bool inBraces = false;

            for (int i = start; i < content.Length; i++)
            {
                char ch = content[i];

                if (ch == Constants.BACKSLASH && inQuotes && i + 1 < content.Length)
                {
                    i++;
                    continue;
                }

                if (ch == Constants.DOUBLE_QUOTE)
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes)
                {
                    if (ch == Constants.OPEN_BRACE)
                    {
                        inBraces = true;
                        continue;
                    }

                    if (ch == Constants.CLOSE_BRACE)
                    {
                        inBraces = false;
                        continue;
                    }

                    if (ch == Constants.COLON && !inBraces)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static BracketSegmentResult ParseBracketSegment(string seg, char defaultDelimiter)
        {
            return ParseBracketSegment(seg.AsSpan(), defaultDelimiter);
        }

        private static BracketSegmentResult ParseBracketSegment(ReadOnlySpan<char> seg, char defaultDelimiter)
        {
            var content = seg;

            // Check for delimiter suffix
            char delimiter = defaultDelimiter;
            if (!content.IsEmpty && content[content.Length - 1] == Constants.TAB)
            {
                delimiter = Constants.TAB;
                content = content.Slice(0, content.Length - 1);
            }
            else if (!content.IsEmpty && content[content.Length - 1] == Constants.PIPE)
            {
                delimiter = Constants.PIPE;
                content = content.Slice(0, content.Length - 1);
            }

            content = TrimSpan(content);

#if NETSTANDARD2_0
            if (!int.TryParse(content.ToString(), out var length))
#else
            if (!int.TryParse(content, out var length))
#endif
            {
                throw new FormatException($"Invalid array length: {seg.ToString()}");
            }

            return new BracketSegmentResult
            {
                Length = length,
                Delimiter = delimiter,
            };
        }

        // #endregion

        // #region Delimited value parsing

        /// <summary>
        /// Parses a delimiter-separated string into individual values, respecting quotes.
        /// </summary>
        public static List<string> ParseDelimitedValues(string input, char delimiter)
        {
            return ParseDelimitedValues(input.AsSpan(), delimiter);
        }

        public static List<string> ParseDelimitedValues(ReadOnlySpan<char> input, char delimiter)
        {
            var values = new List<string>(16);
            int tokenStart = 0;
            bool inQuotes = false;
            bool needsSlowPath = false;

            for (int i = 0; i < input.Length; i++)
            {
                char ch = input[i];

                if (ch == Constants.BACKSLASH && inQuotes && i + 1 < input.Length)
                {
                    needsSlowPath = true;
                    i++;
                    continue;
                }

                if (ch == Constants.DOUBLE_QUOTE)
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (ch == delimiter && !inQuotes)
                {
                    values.Add(TrimmedSliceToString(input, tokenStart, i));
                    tokenStart = i + 1;
                }
            }

            if (!needsSlowPath)
            {
                if (tokenStart < input.Length || values.Count > 0)
                {
                    values.Add(TrimmedSliceToString(input, tokenStart, input.Length));
                }

                return values;
            }

            return ParseDelimitedValuesSlow(input.ToString(), delimiter);
        }

        public static bool TryParseDelimitedValueRanges(string input, char delimiter, TokenRange[] destination, out int count)
        {
            return TryParseDelimitedValueRanges(input.AsSpan(), delimiter, destination, out count);
        }

        /// <summary>
        /// Span overload — avoids materialising a Content string in hot tabular-decode loops.
        /// Returned <see cref="TokenRange"/> indices are relative to the start of <paramref name="input"/>.
        /// </summary>
        public static bool TryParseDelimitedValueRanges(ReadOnlySpan<char> input, char delimiter, TokenRange[] destination, out int count)
        {
            count = 0;
            int tokenStart = 0;
            bool inQuotes = false;

            for (int i = 0; i < input.Length; i++)
            {
                char ch = input[i];

                if (ch == Constants.BACKSLASH && inQuotes && i + 1 < input.Length)
                {
                    i++;
                    continue;
                }

                if (ch == Constants.DOUBLE_QUOTE)
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (ch == delimiter && !inQuotes)
                {
                    if (count >= destination.Length)
                    {
                        return false;
                    }

                    destination[count++] = CreateTrimmedRange(input, tokenStart, i);
                    tokenStart = i + 1;
                }
            }

            if (tokenStart < input.Length || count > 0)
            {
                if (count >= destination.Length)
                {
                    return false;
                }

                destination[count++] = CreateTrimmedRange(input, tokenStart, input.Length);
            }

            return true;
        }

        private static List<string> ParseDelimitedValuesSlow(string input, char delimiter)
        {
            var values = new List<string>(16);
            var current = new System.Text.StringBuilder(input.Length);
            bool inQuotes = false;

            for (int i = 0; i < input.Length; i++)
            {
                char ch = input[i];

                if (ch == Constants.BACKSLASH && inQuotes && i + 1 < input.Length)
                {
                    current.Append(ch);
                    current.Append(input[i + 1]);
                    i++;
                    continue;
                }

                if (ch == Constants.DOUBLE_QUOTE)
                {
                    inQuotes = !inQuotes;
                    current.Append(ch);
                    continue;
                }

                if (ch == delimiter && !inQuotes)
                {
                    values.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0 || values.Count > 0)
            {
                values.Add(current.ToString().Trim());
            }

            return values;
        }

        private static string TrimmedSubstring(string input, int start, int endExclusive)
        {
            return TrimmedSliceToString(input.AsSpan(), start, endExclusive);
        }

        private static TokenRange CreateTrimmedRange(string input, int start, int endExclusive)
        {
            start = FindTrimmedStart(input, start, endExclusive);
            endExclusive = FindTrimmedEnd(input, start, endExclusive);
            return new TokenRange(start, endExclusive);
        }

        private static TokenRange CreateTrimmedRange(ReadOnlySpan<char> input, int start, int endExclusive)
        {
            start = FindTrimmedStart(input, start, endExclusive);
            endExclusive = FindTrimmedEnd(input, start, endExclusive);
            return new TokenRange(start, endExclusive);
        }

        private static string TrimmedSliceToString(ReadOnlySpan<char> input, int start, int endExclusive)
        {
            start = FindTrimmedStart(input, start, endExclusive);
            endExclusive = FindTrimmedEnd(input, start, endExclusive);
            return input.Slice(start, endExclusive - start).ToString();
        }

        // #endregion

        // #region Primitive and key parsing

        public static NativeNode? ParsePrimitiveNativeToken(string token)
        {
            var trimmed = token.Trim();

            if (string.IsNullOrEmpty(trimmed))
                return new NativePrimitiveNode(string.Empty);

            if (trimmed[0] == Constants.DOUBLE_QUOTE)
            {
                return new NativePrimitiveNode(ParseStringLiteral(trimmed));
            }

            if (LiteralUtils.IsBooleanOrNullLiteral(trimmed))
            {
                if (trimmed == Constants.TRUE_LITERAL)
                    return new NativePrimitiveNode(true);
                if (trimmed == Constants.FALSE_LITERAL)
                    return new NativePrimitiveNode(false);
                if (trimmed == Constants.NULL_LITERAL)
                    return new NativePrimitiveNode(null);
            }

            if (LiteralUtils.IsNumericLiteral(trimmed))
            {
                var parsedNumber = double.Parse(trimmed, CultureInfo.InvariantCulture);
                parsedNumber = FloatUtils.NormalizeSignedZero(parsedNumber);
                if (parsedNumber < 1e-6 || parsedNumber > 1e6)
                {
                    if (NumericUtils.TryEmitCanonicalDecimalForm(parsedNumber, out var decimalValue))
                    {
                        return new NativePrimitiveNode(decimalValue);
                    }
                }

                return new NativePrimitiveNode(parsedNumber);
            }

            return new NativePrimitiveNode(trimmed);
        }

        /// <summary>
        /// Parses a string literal, handling quotes and escape sequences.
        /// </summary>
        public static string ParseStringLiteral(string token)
        {
            var trimmedToken = token.Trim();

            if (trimmedToken.StartsWith(Constants.DOUBLE_QUOTE.ToString()))
            {
                // Find the closing quote, accounting for escaped quotes
                var closingQuoteIndex = StringUtils.FindClosingQuote(trimmedToken, 0);

                if (closingQuoteIndex == -1)
                {
                    throw ToonFormatException.Syntax("Unterminated string: missing closing quote");
                }

                if (closingQuoteIndex != trimmedToken.Length - 1)
                {
                    throw ToonFormatException.Syntax("Unexpected characters after closing quote");
                }

                var content = trimmedToken.Substring(1, closingQuoteIndex - 1);
                return StringUtils.UnescapeString(content);
            }

            return trimmedToken;
        }

        public static string ParseStringLiteral(string token, int start, int endExclusive)
        {
            while (start < endExclusive && char.IsWhiteSpace(token[start]))
            {
                start++;
            }

            while (endExclusive > start && char.IsWhiteSpace(token[endExclusive - 1]))
            {
                endExclusive--;
            }

            if (start >= endExclusive)
            {
                return string.Empty;
            }

            if (token[start] == Constants.DOUBLE_QUOTE)
            {
                var trimmed = token.AsSpan(start, endExclusive - start);
                var closingQuoteIndex = StringUtils.FindClosingQuote(trimmed, 0);

                if (closingQuoteIndex == -1)
                {
                    throw ToonFormatException.Syntax("Unterminated string: missing closing quote");
                }

                if (closingQuoteIndex != trimmed.Length - 1)
                {
                    throw ToonFormatException.Syntax("Unexpected characters after closing quote");
                }

                return StringUtils.UnescapeString(trimmed.Slice(1, closingQuoteIndex - 1));
            }

            return token.Substring(start, endExclusive - start);
        }

        public class KeyParseResult
        {
            public string Key { get; set; } = string.Empty;
            public int End { get; set; }
            public bool WasQuoted { get; set; }
        }

        public static KeyParseResult ParseUnquotedKey(string content, int start)
        {
            int end = start;
            while (end < content.Length && content[end] != Constants.COLON)
            {
                end++;
            }

            // Validate that a colon was found
            if (end >= content.Length || content[end] != Constants.COLON)
            {
                throw ToonFormatException.Syntax("Missing colon after key");
            }

            var keyStart = FindTrimmedStart(content, start, end);
            var keyEnd = FindTrimmedEnd(content, keyStart, end);
            var key = content.Substring(keyStart, keyEnd - keyStart);

            // Skip the colon
            end++;

            return new KeyParseResult { Key = key, End = end, WasQuoted = false };
        }

        public static KeyParseResult ParseQuotedKey(string content, int start)
        {
            // Find the closing quote, accounting for escaped quotes
            var closingQuoteIndex = StringUtils.FindClosingQuote(content.AsSpan(), start);

            if (closingQuoteIndex == -1)
            {
                throw ToonFormatException.Syntax("Unterminated quoted key");
            }

            // Extract and unescape the key content
            var key = StringUtils.UnescapeString(content.AsSpan(start + 1, closingQuoteIndex - start - 1));
            int end = closingQuoteIndex + 1;

            // Validate and skip colon after quoted key
            if (end >= content.Length || content[end] != Constants.COLON)
            {
                throw ToonFormatException.Syntax("Missing colon after key");
            }

            end++;

            return new KeyParseResult { Key = key, End = end, WasQuoted = true };
        }

        /// <summary>
        /// Parses a key token (quoted or unquoted) and returns the key and position after colon.
        /// </summary>
        public static KeyParseResult ParseKeyToken(string content, int start)
        {
            if (content[start] == Constants.DOUBLE_QUOTE)
            {
                return ParseQuotedKey(content, start);
            }
            else
            {
                return ParseUnquotedKey(content, start);
            }
        }

        // #endregion

        // #region Array content detection helpers

        /// <summary>
        /// Checks if content after hyphen starts with an array header.
        /// </summary>
        public static bool IsArrayHeaderAfterHyphen(string content)
        {
            var trimmed = content.AsSpan().Trim();
            return !trimmed.IsEmpty
                   && trimmed[0] == Constants.OPEN_BRACKET
                   && StringUtils.FindUnquotedChar(trimmed, Constants.COLON) != -1;
        }

        /// <summary>
        /// Checks if content after hyphen contains a key-value pair (has a colon).
        /// </summary>
        public static bool IsObjectFirstFieldAfterHyphen(string content)
        {
            return StringUtils.FindUnquotedChar(content, Constants.COLON) != -1;
        }

        private static string ParseKeyText(string content, int start, int endExclusive)
        {
            start = FindTrimmedStart(content, start, endExclusive);
            endExclusive = FindTrimmedEnd(content, start, endExclusive);
            if (start >= endExclusive)
            {
                return string.Empty;
            }

            return content[start] == Constants.DOUBLE_QUOTE
                ? ParseStringLiteral(content, start, endExclusive)
                : content.Substring(start, endExclusive - start);
        }

        private static int FindTrimmedStart(string input, int start, int endExclusive)
        {
            return FindTrimmedStart(input.AsSpan(), start, endExclusive);
        }

        private static int FindTrimmedStart(ReadOnlySpan<char> input, int start, int endExclusive)
        {
            while (start < endExclusive && char.IsWhiteSpace(input[start]))
            {
                start++;
            }

            return start;
        }

        private static int FindTrimmedEnd(string input, int start, int endExclusive)
        {
            return FindTrimmedEnd(input.AsSpan(), start, endExclusive);
        }

        private static int FindTrimmedEnd(ReadOnlySpan<char> input, int start, int endExclusive)
        {
            while (endExclusive > start && char.IsWhiteSpace(input[endExclusive - 1]))
            {
                endExclusive--;
            }

            return endExclusive;
        }

        private static ReadOnlySpan<char> TrimSpan(ReadOnlySpan<char> input)
        {
            var start = FindTrimmedStart(input, 0, input.Length);
            var end = FindTrimmedEnd(input, start, input.Length);
            return input.Slice(start, end - start);
        }

        // #endregion
    }
}
