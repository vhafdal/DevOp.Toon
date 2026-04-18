#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DevOp.Toon.Internal.Decode
{
    /// <summary>
    /// Represents a parsed line with its raw content, indentation, depth, and line number.
    /// Stored as a value type — lines are held inline in a List&lt;ParsedLine&gt; with no per-line heap allocation.
    /// Content is stored as a range into the original source string; the string is materialised on demand.
    /// Use <see cref="IsNone"/> to test for the sentinel (default) value.
    /// </summary>
    internal readonly struct ParsedLine
    {
        private readonly string? _source;
        private readonly int _contentStart;
        private readonly int _contentLength;

        public ParsedLine(string source, int contentStart, int contentLength, int indent, int depth, int lineNumber)
        {
            _source = source;
            _contentStart = contentStart;
            _contentLength = contentLength;
            Indent = indent;
            Depth = depth;
            LineNumber = lineNumber;
        }

        public int Indent { get; }
        public int Depth { get; }
        public int LineNumber { get; }

        /// <summary>True when this is the sentinel (default) value returned instead of null.</summary>
        public bool IsNone => _source is null;

        /// <summary>
        /// The original source string. Allows callers to pass source + adjusted offsets to
        /// primitive setters without materialising a Content substring.
        /// </summary>
        internal string SourceString => _source!;

        /// <summary>
        /// Absolute index of the first content character within <see cref="SourceString"/>.
        /// </summary>
        internal int ContentStart => _contentStart;

        /// <summary>
        /// Zero-allocation span over the content characters.
        /// </summary>
        public ReadOnlySpan<char> ContentSpan => _source.AsSpan(_contentStart, _contentLength);

        /// <summary>
        /// Materialised content string. Prefer <see cref="ContentSpan"/> for hot-path character iteration.
        /// </summary>
        public string Content => _source!.Substring(_contentStart, _contentLength);
    }

    /// <summary>
    /// Information about a blank line in the source.
    /// </summary>
    internal struct BlankLineInfo
    {
        public int LineNumber { get; set; }
        public int Indent { get; set; }
        public int Depth { get; set; }
    }

    /// <summary>
    /// Result of scanning source text into parsed lines.
    /// </summary>
    internal class ScanResult
    {
        public List<ParsedLine> Lines { get; set; } = new();
        public List<BlankLineInfo> BlankLines { get; set; } = new();
    }

    /// <summary>
    /// Cursor for navigating through parsed lines during decoding.
    /// Aligned with TypeScript decode/scanner.ts LineCursor
    /// </summary>
    internal class LineCursor
    {
        private readonly List<ParsedLine> _lines;
        private readonly List<BlankLineInfo> _blankLines;
        private int _index;

        public LineCursor(List<ParsedLine> lines, List<BlankLineInfo> blankLines)
        {
            _lines = lines;
            _blankLines = blankLines;
            _index = 0;
        }

        public List<BlankLineInfo> GetBlankLines() => _blankLines;

        public ParsedLine Peek()
        {
            return _index < _lines.Count ? _lines[_index] : default;
        }

        public ParsedLine Next()
        {
            return _index < _lines.Count ? _lines[_index++] : default;
        }

        public ParsedLine Current()
        {
            return _index > 0 ? _lines[_index - 1] : default;
        }

        public void Advance()
        {
            _index++;
        }

        public bool AtEnd()
        {
            return _index >= _lines.Count;
        }

        public int Length => _lines.Count;

        public ParsedLine PeekAtDepth(int targetDepth)
        {
            var line = Peek();
            if (line.IsNone || line.Depth < targetDepth)
                return default;
            if (line.Depth == targetDepth)
                return line;
            return default;
        }

        public bool HasMoreAtDepth(int targetDepth)
        {
            return !PeekAtDepth(targetDepth).IsNone;
        }
    }

    /// <summary>
    /// Scanner utilities for parsing source text into structured lines.
    /// Aligned with TypeScript decode/scanner.ts
    /// </summary>
    internal static class Scanner
    {
        /// <summary>
        /// Parses source text into a list of structured lines with depth information.
        /// </summary>
        public static ScanResult ToParsedLines(string source, int indentSize, bool strict)
        {
            int estimatedLines = 1;

            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == '\n')
                    estimatedLines++;
            }
            var parsed = new List<ParsedLine>(estimatedLines);
            var blankLines = new List<BlankLineInfo>(Math.Max(4, estimatedLines / 4));
            if (string.IsNullOrWhiteSpace(source))
            {
                return new ScanResult { Lines = parsed, BlankLines = blankLines };
            }

            // Scan using absolute position tracking so we can store source-relative offsets
            // in ParsedLine without copying characters into a new string at scan time.
            ReadOnlySpan<char> remaining = source.AsSpan();
            int absolutePos = 0;
            int lineNumber = 0;

            while (!remaining.IsEmpty)
            {
                lineNumber++;
                int lineAbsoluteStart = absolutePos;

                // Find end of this line
                int newlineIdx = remaining.IndexOf('\n');
                int lineLength;
                if (newlineIdx >= 0)
                {
                    lineLength = newlineIdx;          // excludes the '\n'
                    absolutePos += newlineIdx + 1;    // advance past '\n'
                    remaining = remaining.Slice(newlineIdx + 1);
                }
                else
                {
                    lineLength = remaining.Length;
                    absolutePos += remaining.Length;
                    remaining = ReadOnlySpan<char>.Empty;
                }

                // Strip trailing carriage return
                if (lineLength > 0 && source[lineAbsoluteStart + lineLength - 1] == '\r')
                    lineLength--;

                // Count leading spaces (indent)
                int indent = 0;
                while (indent < lineLength && source[lineAbsoluteStart + indent] == Constants.SPACE)
                    indent++;

                int contentStart = lineAbsoluteStart + indent;
                int contentLength = lineLength - indent;

                // Blank / all-whitespace line
                ReadOnlySpan<char> contentSpan = source.AsSpan(contentStart, contentLength);
                if (contentSpan.IsWhiteSpace())
                {
                    var depth = ComputeDepthFromIndent(indent, indentSize);
                    blankLines.Add(new BlankLineInfo
                    {
                        LineNumber = lineNumber,
                        Indent = indent,
                        Depth = depth
                    });
                    continue;
                }

                var lineDepth = ComputeDepthFromIndent(indent, indentSize);

                if (strict)
                {
                    // Scan ALL leading whitespace (spaces + tabs) to detect any tabs.
                    // (indent only counted spaces; a leading tab makes contentStart == lineAbsoluteStart
                    // so the range loop would miss it.)
                    int wsEnd = lineAbsoluteStart;
                    while (wsEnd < lineAbsoluteStart + lineLength &&
                           (source[wsEnd] == Constants.SPACE || source[wsEnd] == Constants.TAB))
                    {
                        wsEnd++;
                    }
                    for (int j = lineAbsoluteStart; j < wsEnd; j++)
                    {
                        if (source[j] == Constants.TAB)
                        {
                            throw ToonFormatException.Syntax(
                                $"Line {lineNumber}: Tabs are not allowed in indentation in strict mode");
                        }
                    }
                    if (indent > 0 && indent % indentSize != 0)
                    {
                        throw ToonFormatException.Syntax(
                            $"Line {lineNumber}: Indentation must be exact multiple of {indentSize}, but found {indent} spaces");
                    }
                }

                // Store source + range — no substring allocation at scan time.
                parsed.Add(new ParsedLine(source, contentStart, contentLength, indent, lineDepth, lineNumber));
            }
            return new ScanResult { Lines = parsed, BlankLines = blankLines };
        }

        private static bool IsWhiteSpace(this ReadOnlySpan<char> span)
        {
            for (int i = 0; i < span.Length; i++)
            {
                if (!char.IsWhiteSpace(span[i]))
                    return false;
            }
            return true;
        }

        private static int ComputeDepthFromIndent(int indentSpaces, int indentSize)
        {
            return indentSpaces / indentSize;
        }
    }
}
