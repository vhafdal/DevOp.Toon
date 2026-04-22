#nullable enable
using System;
using System.Collections.Concurrent;
using DevOp.Toon.Core;
using DevOp.Toon;

namespace DevOp.Toon.Internal.Shared
{
    /// <summary>
    /// Validation utilities aligned with TypeScript version shared/validation.ts:
    /// - IsValidUnquotedKey: Whether the key name can be without quotes
    /// - IsSafeUnquoted: Whether the string value can be without quotes
    /// - IsBooleanOrNullLiteral: Whether it is true/false/null
    /// - IsNumericLike: Whether it looks like numeric text (including leading zero integers)
    /// </summary>
    internal static class ValidationShared
    {
        private const int SafeStringCacheMaxLength = 128;
        private static readonly ConcurrentDictionary<string, bool> ValidUnquotedKeyCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, bool> IdentifierSegmentCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, bool> SafeUnquotedCommaCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, bool> SafeUnquotedTabCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, bool> SafeUnquotedPipeCache = new(StringComparer.Ordinal);

        /// <summary>Whether the key name can be without quotes.</summary>
        /// <param name="key">The key to validate.</param>
        /// <returns><see langword="true"/> when the key can be emitted without quotes.</returns>
        internal static bool IsValidUnquotedKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            return ValidUnquotedKeyCache.GetOrAdd(key, static currentKey =>
            {
                if (!IsKeyStart(currentKey[0]))
                    return false;

                for (int i = 1; i < currentKey.Length; i++)
                {
                    var current = currentKey[i];
                    if (current == Constants.DOT)
                        continue;

                    if (!IsKeyPart(current))
                        return false;
                }

                return true;
            });
        }

        /// <summary>
        /// Returns whether a dotted-path segment is a valid identifier for safe key folding or path expansion.
        /// </summary>
        /// <param name="key">The path segment to validate.</param>
        /// <returns><see langword="true"/> when the segment starts and continues with valid identifier characters.</returns>
        internal static bool IsIdentifierSegment(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            return IdentifierSegmentCache.GetOrAdd(key, static currentKey =>
            {
                if (!IsKeyStart(currentKey[0]))
                    return false;

                for (int i = 1; i < currentKey.Length; i++)
                {
                    if (!IsKeyPart(currentKey[i]))
                        return false;
                }

                return true;
            });
        }

        /// <summary>Whether the string value can be safely without quotes.</summary>
        /// <param name="value">The string value to inspect.</param>
        /// <param name="delimiter">The active delimiter that would make the value ambiguous if present.</param>
        /// <returns><see langword="true"/> when the value can be emitted without quotes.</returns>
        internal static bool IsSafeUnquoted(string value, ToonDelimiter delimiter = Constants.DEFAULT_DELIMITER_ENUM)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            if (value.Length <= SafeStringCacheMaxLength)
            {
                return GetSafeUnquotedCache(delimiter).GetOrAdd(value, currentValue => IsSafeUnquotedCore(currentValue, delimiter));
            }

            return IsSafeUnquotedCore(value, delimiter);
        }

        private static bool IsSafeUnquotedCore(string value, ToonDelimiter delimiter)
        {
            if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1]))
                return false;

            if (LiteralUtils.IsBooleanOrNullLiteral(value) || IsNumericLike(value))
                return false;

            var delimiterChar = Constants.ToDelimiterChar(delimiter);

            if (value[0] == Constants.LIST_ITEM_MARKER)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (current == Constants.COLON
                    || current == Constants.DOUBLE_QUOTE
                    || current == Constants.BACKSLASH
                    || current == delimiterChar)
                {
                    return false;
                }

                switch (current)
                {
                    case Constants.OPEN_BRACKET:
                    case Constants.CLOSE_BRACKET:
                    case Constants.OPEN_BRACE:
                    case Constants.CLOSE_BRACE:
                    case Constants.NEWLINE:
                    case Constants.CARRIAGE_RETURN:
                    case Constants.TAB:
                        return false;
                }
            }

            return true;
        }

        private static ConcurrentDictionary<string, bool> GetSafeUnquotedCache(ToonDelimiter delimiter)
        {
            return delimiter switch
            {
                ToonDelimiter.COMMA => SafeUnquotedCommaCache,
                ToonDelimiter.TAB => SafeUnquotedTabCache,
                ToonDelimiter.PIPE => SafeUnquotedPipeCache,
                _ => SafeUnquotedCommaCache
            };
        }

        private static bool IsNumericLike(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            int index = 0;
            if (value[0] == '-')
            {
                if (value.Length == 1)
                    return false;

                index = 1;
            }

            int integerStart = index;
            int integerDigits = 0;
            while (index < value.Length && char.IsDigit(value[index]))
            {
                index++;
                integerDigits++;
            }

            if (integerDigits == 0)
                return false;

            if (index == value.Length)
            {
                return integerDigits > 1 && value[integerStart] == '0'
                    || integerDigits > 0;
            }

            if (value[index] == Constants.DOT)
            {
                index++;
                int fractionalDigits = 0;
                while (index < value.Length && char.IsDigit(value[index]))
                {
                    index++;
                    fractionalDigits++;
                }

                if (fractionalDigits == 0)
                    return false;
            }

            if (index < value.Length && (value[index] == 'e' || value[index] == 'E'))
            {
                index++;
                if (index < value.Length && (value[index] == '+' || value[index] == '-'))
                {
                    index++;
                }

                int exponentDigits = 0;
                while (index < value.Length && char.IsDigit(value[index]))
                {
                    index++;
                    exponentDigits++;
                }

                if (exponentDigits == 0)
                    return false;
            }

            return index == value.Length;
        }

        private static bool IsKeyStart(char value)
        {
            return value == '_' || char.IsLetter(value);
        }

        private static bool IsKeyPart(char value)
        {
            return value == '_' || char.IsLetterOrDigit(value);
        }
    }
}
