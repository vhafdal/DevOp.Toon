#nullable enable
using System;
using System.Text;

namespace DevOp.Toon
{
    /// <summary>
    /// Exception thrown when TOON format parsing or encoding fails.
    /// </summary>
    public sealed class ToonFormatException : Exception
    {
        /// <summary>
        /// Gets the category of TOON format error that occurred.
        /// </summary>
        public ToonErrorKind Kind { get; }

        /// <summary>
        /// Gets the 1-based line number where the error occurred, when available.
        /// </summary>
        public int? LineNumber { get; }

        /// <summary>
        /// Gets the 1-based column number where the error occurred, when available.
        /// </summary>
        public int? ColumnNumber { get; }

        /// <summary>
        /// Gets the original line text where the error occurred, when available.
        /// </summary>
        public string? SourceLine { get; }

        /// <summary>
        /// Gets the parsed indentation depth associated with the error, when available.
        /// </summary>
        public int? Depth { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToonFormatException"/> class with structured location details.
        /// </summary>
        /// <param name="kind">The category of format error.</param>
        /// <param name="message">The human-readable error message.</param>
        /// <param name="lineNumber">The 1-based line number where the error occurred, when known.</param>
        /// <param name="columnNumber">The 1-based column number where the error occurred, when known.</param>
        /// <param name="sourceLine">The source line associated with the error, when known.</param>
        /// <param name="depth">The parsed indentation depth associated with the error, when known.</param>
        /// <param name="inner">The exception that caused this error, when available.</param>
        public ToonFormatException(
            ToonErrorKind kind,
            string message,
            int? lineNumber = null,
            int? columnNumber = null,
            string? sourceLine = null,
            int? depth = null,
            Exception? inner = null)
            : base(BuildMessage(kind, message, lineNumber, columnNumber, sourceLine), inner)
        {
            Kind = kind;
            LineNumber = lineNumber;
            ColumnNumber = columnNumber;
            SourceLine = sourceLine;
            Depth = depth;
        }

        /// <summary>
        /// Creates an exception for invalid TOON syntax, such as malformed keys, literals, or structural tokens.
        /// </summary>
        /// <param name="message">The human-readable error message.</param>
        /// <param name="lineNumber">The 1-based line number where the error occurred, when known.</param>
        /// <param name="columnNumber">The 1-based column number where the error occurred, when known.</param>
        /// <param name="sourceLine">The source line associated with the error, when known.</param>
        /// <param name="depth">The parsed indentation depth associated with the error, when known.</param>
        /// <param name="inner">The exception that caused this error, when available.</param>
        /// <returns>A syntax-classified TOON format exception.</returns>
        public static ToonFormatException Syntax(
            string message,
            int? lineNumber = null,
            int? columnNumber = null,
            string? sourceLine = null,
            int? depth = null,
            Exception? inner = null)
            => new(ToonErrorKind.Syntax, message, lineNumber, columnNumber, sourceLine, depth, inner);

        /// <summary>
        /// Creates an exception for range errors, such as declared array counts that do not match parsed items.
        /// </summary>
        /// <param name="message">The human-readable error message.</param>
        /// <param name="lineNumber">The 1-based line number where the error occurred, when known.</param>
        /// <param name="columnNumber">The 1-based column number where the error occurred, when known.</param>
        /// <param name="sourceLine">The source line associated with the error, when known.</param>
        /// <param name="depth">The parsed indentation depth associated with the error, when known.</param>
        /// <param name="inner">The exception that caused this error, when available.</param>
        /// <returns>A range-classified TOON format exception.</returns>
        public static ToonFormatException Range(
            string message,
            int? lineNumber = null,
            int? columnNumber = null,
            string? sourceLine = null,
            int? depth = null,
            Exception? inner = null)
            => new(ToonErrorKind.Range, message, lineNumber, columnNumber, sourceLine, depth, inner);

        /// <summary>
        /// Creates an exception for strict-mode validation failures, such as unexpected extra lines or structural rule violations.
        /// </summary>
        /// <param name="message">The human-readable error message.</param>
        /// <param name="lineNumber">The 1-based line number where the error occurred, when known.</param>
        /// <param name="columnNumber">The 1-based column number where the error occurred, when known.</param>
        /// <param name="sourceLine">The source line associated with the error, when known.</param>
        /// <param name="depth">The parsed indentation depth associated with the error, when known.</param>
        /// <param name="inner">The exception that caused this error, when available.</param>
        /// <returns>A validation-classified TOON format exception.</returns>
        public static ToonFormatException Validation(
            string message,
            int? lineNumber = null,
            int? columnNumber = null,
            string? sourceLine = null,
            int? depth = null,
            Exception? inner = null)
            => new(ToonErrorKind.Validation, message, lineNumber, columnNumber, sourceLine, depth, inner);

        /// <summary>
        /// Creates an exception for indentation errors, such as tab indentation or widths that are not multiples of the configured indent.
        /// </summary>
        /// <param name="message">The human-readable error message.</param>
        /// <param name="lineNumber">The 1-based line number where the error occurred, when known.</param>
        /// <param name="columnNumber">The 1-based column number where the error occurred, when known.</param>
        /// <param name="sourceLine">The source line associated with the error, when known.</param>
        /// <param name="depth">The parsed indentation depth associated with the error, when known.</param>
        /// <param name="inner">The exception that caused this error, when available.</param>
        /// <returns>An indentation-classified TOON format exception.</returns>
        public static ToonFormatException Indentation(
            string message,
            int? lineNumber = null,
            int? columnNumber = null,
            string? sourceLine = null,
            int? depth = null,
            Exception? inner = null)
            => new(ToonErrorKind.Indentation, message, lineNumber, columnNumber, sourceLine, depth, inner);

        /// <summary>
        /// Creates an exception for delimiter-related errors, such as unsupported delimiters or malformed delimited rows.
        /// </summary>
        /// <param name="message">The human-readable error message.</param>
        /// <param name="lineNumber">The 1-based line number where the error occurred, when known.</param>
        /// <param name="columnNumber">The 1-based column number where the error occurred, when known.</param>
        /// <param name="sourceLine">The source line associated with the error, when known.</param>
        /// <param name="depth">The parsed indentation depth associated with the error, when known.</param>
        /// <param name="inner">The exception that caused this error, when available.</param>
        /// <returns>A delimiter-classified TOON format exception.</returns>
        public static ToonFormatException Delimiter(
            string message,
            int? lineNumber = null,
            int? columnNumber = null,
            string? sourceLine = null,
            int? depth = null,
            Exception? inner = null)
            => new(ToonErrorKind.Delimiter, message, lineNumber, columnNumber, sourceLine, depth, inner);

        private static string BuildMessage(
            ToonErrorKind kind,
            string message,
            int? lineNumber,
            int? columnNumber,
            string? sourceLine)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(kind).Append("] ").Append(message);

            if (lineNumber is not null)
                sb.Append(" (Line ").Append(lineNumber.Value).Append(')');
            if (columnNumber is not null)
                sb.Append(" (Column ").Append(columnNumber.Value).Append(')');

            if (!string.IsNullOrEmpty(sourceLine))
            {
                sb.AppendLine();
                sb.Append("  > ").Append(sourceLine);

                if (columnNumber is not null && columnNumber.Value > 0)
                {
                    sb.AppendLine();
                    sb.Append("    ");
                    // Caret pointing to the column position
                    var caretPos = Math.Max(1, columnNumber.Value);
                    sb.Append(new string(' ', caretPos - 1)).Append('^');
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>TOON error type classification.</summary>
    public enum ToonErrorKind
    {
        /// <summary>Syntax error: illegal tokens or structures encountered during scanning/parsing phase.</summary>
        Syntax,
        /// <summary>Range error: count mismatches (e.g., [N] does not match actual item count).</summary>
        Range,
        /// <summary>Validation error: structural/rule validation failure in strict mode (e.g., extra lines, empty lines).</summary>
        Validation,
        /// <summary>Indentation error: indentation is not a multiple of Indent or contains TAB.</summary>
        Indentation,
        /// <summary>Delimiter error: fields/values contain disallowed delimiters or delimiter inference failed.</summary>
        Delimiter,
        /// <summary>Unknown error: unclassified exceptions.</summary>
        Unknown
    }
}
