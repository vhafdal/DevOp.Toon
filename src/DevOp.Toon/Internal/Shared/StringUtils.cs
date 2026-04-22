#nullable enable
using System.Text;
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon.Internal.Shared
{
    /// <summary>
    /// String utilities, aligned with TypeScript version shared/string-utils.ts:
    /// - EscapeString: Escapes special characters during encoding
    /// - UnescapeString: Restores escape sequences during decoding
    /// - FindClosingQuote: Finds the position of the matching closing quote, considering escapes
    /// - FindUnquotedChar: Finds the position of the target character not inside quotes
    /// </summary>
    internal static class StringUtils
    {
        /// <summary>
        /// Escapes special characters: backslash, quotes, newlines, carriage returns, tabs.
        /// Equivalent to TS escapeString.
        /// </summary>
        /// <param name="value">The string to escape.</param>
        /// <returns>The original string when no escaping is required; otherwise, an escaped string.</returns>
        internal static string EscapeString(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

            int firstEscapeIndex = FindFirstEscapeIndex(value);
            if (firstEscapeIndex < 0)
                return value;

            var builder = new StringBuilder(value.Length + 8);
            builder.Append(value, 0, firstEscapeIndex);
            AppendEscapedString(builder, value, firstEscapeIndex);
            return builder.ToString();
        }

        /// <summary>
        /// Appends an escaped string to a <see cref="StringBuilder"/>.
        /// </summary>
        /// <param name="builder">The destination builder.</param>
        /// <param name="value">The string to escape and append.</param>
        internal static void AppendEscapedString(StringBuilder builder, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            int firstEscapeIndex = FindFirstEscapeIndex(value);
            if (firstEscapeIndex < 0)
            {
                builder.Append(value);
                return;
            }

            builder.Append(value, 0, firstEscapeIndex);
            AppendEscapedString(builder, value, firstEscapeIndex);
        }

        /// <summary>
        /// Appends an escaped string to a pooled compact writer.
        /// </summary>
        /// <param name="writer">The destination writer.</param>
        /// <param name="value">The string to escape and append.</param>
        internal static void AppendEscapedString(CompactBufferWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            int firstEscapeIndex = FindFirstEscapeIndex(value);
            if (firstEscapeIndex < 0)
            {
                writer.Append(value);
                return;
            }

            writer.Append(value.AsSpan(0, firstEscapeIndex));
            AppendEscapedString(writer, value, firstEscapeIndex);
        }

        private static void AppendEscapedString(StringBuilder builder, string value, int startIndex)
        {
            for (int i = startIndex; i < value.Length; i++)
            {
                switch (value[i])
                {
                    case '\r':
                        if (i + 1 < value.Length && value[i + 1] == '\n')
                        {
                            builder.Append(Constants.BACKSLASH);
                            builder.Append('n');
                            i++;
                        }
                        else
                        {
                            builder.Append(Constants.BACKSLASH);
                            builder.Append('r');
                        }
                        break;
                    case '\n':
                        builder.Append(Constants.BACKSLASH);
                        builder.Append('n');
                        break;
                    case '\t':
                        builder.Append(Constants.BACKSLASH);
                        builder.Append('t');
                        break;
                    case '\\':
                        builder.Append(Constants.BACKSLASH);
                        builder.Append(Constants.BACKSLASH);
                        break;
                    case '"':
                        builder.Append(Constants.BACKSLASH);
                        builder.Append(Constants.DOUBLE_QUOTE);
                        break;
                    default:
                        builder.Append(value[i]);
                        break;
                }
            }
        }

        private static void AppendEscapedString(CompactBufferWriter writer, string value, int startIndex)
        {
            for (int i = startIndex; i < value.Length; i++)
            {
                switch (value[i])
                {
                    case '\r':
                        if (i + 1 < value.Length && value[i + 1] == '\n')
                        {
                            writer.Append(Constants.BACKSLASH);
                            writer.Append('n');
                            i++;
                        }
                        else
                        {
                            writer.Append(Constants.BACKSLASH);
                            writer.Append('r');
                        }
                        break;
                    case '\n':
                        writer.Append(Constants.BACKSLASH);
                        writer.Append('n');
                        break;
                    case '\t':
                        writer.Append(Constants.BACKSLASH);
                        writer.Append('t');
                        break;
                    case '\\':
                        writer.Append(Constants.BACKSLASH);
                        writer.Append(Constants.BACKSLASH);
                        break;
                    case '"':
                        writer.Append(Constants.BACKSLASH);
                        writer.Append(Constants.DOUBLE_QUOTE);
                        break;
                    default:
                        writer.Append(value[i]);
                        break;
                }
            }
        }

        private static int FindFirstEscapeIndex(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                switch (value[i])
                {
                    case '\r':
                    case '\n':
                    case '\t':
                    case '\\':
                    case '"':
                        return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Unescapes the string, supporting \n, \t, \r, \\, \". Invalid sequences throw <see cref="ToonFormatException"/>.
        /// Equivalent to TS unescapeString.
        /// </summary>
        /// <param name="value">The escaped string content without surrounding quotes.</param>
        /// <returns>The unescaped string.</returns>
        /// <exception cref="ToonFormatException">Thrown when an escape sequence is incomplete or unsupported.</exception>
        internal static string UnescapeString(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

            var sb = new StringBuilder(value.Length);
            int i = 0;
            while (i < value.Length)
            {
                var ch = value[i];
                if (ch == Constants.BACKSLASH)
                {
                    if (i + 1 >= value.Length)
                        throw ToonFormatException.Syntax("Invalid escape sequence: backslash at end of string");

                    var next = value[i + 1];
                    switch (next)
                    {
                        case 'n':
                            sb.Append(Constants.NEWLINE);
                            i += 2;
                            continue;
                        case 't':
                            sb.Append(Constants.TAB);
                            i += 2;
                            continue;
                        case 'r':
                            sb.Append(Constants.CARRIAGE_RETURN);
                            i += 2;
                            continue;
                        case '\\':
                            sb.Append(Constants.BACKSLASH);
                            i += 2;
                            continue;
                        case '"':
                            sb.Append(Constants.DOUBLE_QUOTE);
                            i += 2;
                            continue;
                        default:
                            throw ToonFormatException.Syntax($"Invalid escape sequence: \\{next}");
                    }
                }

                sb.Append(ch);
                i++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Unescapes a span of string content, supporting \n, \t, \r, \\, and \".
        /// </summary>
        /// <param name="value">The escaped string content without surrounding quotes.</param>
        /// <returns>The unescaped string.</returns>
        /// <exception cref="ToonFormatException">Thrown when an escape sequence is incomplete or unsupported.</exception>
        internal static string UnescapeString(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty) return string.Empty;

            var sb = new StringBuilder(value.Length);
            int i = 0;
            while (i < value.Length)
            {
                var ch = value[i];
                if (ch == Constants.BACKSLASH)
                {
                    if (i + 1 >= value.Length)
                        throw ToonFormatException.Syntax("Invalid escape sequence: backslash at end of string");

                    var next = value[i + 1];
                    switch (next)
                    {
                        case 'n':
                            sb.Append(Constants.NEWLINE);
                            i += 2;
                            continue;
                        case 't':
                            sb.Append(Constants.TAB);
                            i += 2;
                            continue;
                        case 'r':
                            sb.Append(Constants.CARRIAGE_RETURN);
                            i += 2;
                            continue;
                        case '\\':
                            sb.Append(Constants.BACKSLASH);
                            i += 2;
                            continue;
                        case '"':
                            sb.Append(Constants.DOUBLE_QUOTE);
                            i += 2;
                            continue;
                        default:
                            throw ToonFormatException.Syntax($"Invalid escape sequence: \\{next}");
                    }
                }

                sb.Append(ch);
                i++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Finds the position of the next double quote in the string starting from 'start', considering escapes.
        /// Returns -1 if not found. Equivalent to TS findClosingQuote.
        /// </summary>
        /// <param name="content">The content to search.</param>
        /// <param name="start">The index of the opening quote.</param>
        /// <returns>The index of the closing quote, or -1 when none exists.</returns>
        internal static int FindClosingQuote(string content, int start)
        {
            int i = start + 1;
            while (i < content.Length)
            {
                // Skip the next character when encountering an escape inside quotes
                if (content[i] == Constants.BACKSLASH && i + 1 < content.Length)
                {
                    i += 2;
                    continue;
                }

                if (content[i] == Constants.DOUBLE_QUOTE)
                    return i;

                i++;
            }
            return -1;
        }

        /// <summary>
        /// Finds the position of the next double quote in the span starting from <paramref name="start"/>, considering escapes.
        /// </summary>
        /// <param name="content">The content to search.</param>
        /// <param name="start">The index of the opening quote.</param>
        /// <returns>The index of the closing quote, or -1 when none exists.</returns>
        internal static int FindClosingQuote(ReadOnlySpan<char> content, int start)
        {
            int i = start + 1;
            while (i < content.Length)
            {
                if (content[i] == Constants.BACKSLASH && i + 1 < content.Length)
                {
                    i += 2;
                    continue;
                }

                if (content[i] == Constants.DOUBLE_QUOTE)
                    return i;

                i++;
            }

            return -1;
        }

        /// <summary>
        /// Finds the position of the target character not inside quotes; returns -1 if not found.
        /// Escape sequences inside quotes are skipped. Equivalent to TS findUnquotedChar.
        /// </summary>
        /// <param name="content">The content to search.</param>
        /// <param name="target">The character to find outside quoted text.</param>
        /// <param name="start">The index where scanning starts.</param>
        /// <returns>The index of the first unquoted target character, or -1 when none exists.</returns>
        internal static int FindUnquotedChar(string content, char target, int start = 0)
        {
            bool inQuotes = false;
            int i = start;

            while (i < content.Length)
            {
                if (inQuotes && content[i] == Constants.BACKSLASH && i + 1 < content.Length)
                {
                    // Skip the next character for escape sequences inside quotes
                    i += 2;
                    continue;
                }

                if (content[i] == Constants.DOUBLE_QUOTE)
                {
                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }

                if (!inQuotes && content[i] == target)
                    return i;

                i++;
            }

            return -1;
        }

        /// <summary>
        /// Finds the position of the target character not inside quotes in a span.
        /// </summary>
        /// <param name="content">The content to search.</param>
        /// <param name="target">The character to find outside quoted text.</param>
        /// <param name="start">The index where scanning starts.</param>
        /// <returns>The index of the first unquoted target character, or -1 when none exists.</returns>
        internal static int FindUnquotedChar(ReadOnlySpan<char> content, char target, int start = 0)
        {
            bool inQuotes = false;
            int i = start;

            while (i < content.Length)
            {
                if (inQuotes && content[i] == Constants.BACKSLASH && i + 1 < content.Length)
                {
                    i += 2;
                    continue;
                }

                if (content[i] == Constants.DOUBLE_QUOTE)
                {
                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }

                if (!inQuotes && content[i] == target)
                {
                    return i;
                }

                i++;
            }

            return -1;
        }

        /// <summary>
        /// Generates a quoted string literal, escaping internal characters as necessary.
        /// Note: Whether quotes are needed should be determined by the caller based on ValidationShared rules.
        /// </summary>
        /// <param name="value">The unescaped string value.</param>
        /// <returns>The escaped string wrapped in double quotes.</returns>
        internal static string Quote(string value)
        {
            return $"\"{EscapeString(value)}\"";
        }
    }
}
