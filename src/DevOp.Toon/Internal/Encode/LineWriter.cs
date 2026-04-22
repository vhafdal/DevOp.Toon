#nullable enable
using System;
using System.Text;

namespace DevOp.Toon.Internal.Encode
{
    /// <summary>
    /// Helper class for building indented lines of TOON output.
    /// Aligned with TypeScript encode/writer.ts
    /// </summary>
    internal class LineWriter
    {
        private readonly StringBuilder _builder = new();
        private readonly string[] _indentCache = new string[8];
        private readonly string _indentationString;

        /// <summary>
        /// Creates a new LineWriter with the specified indentation size.
        /// </summary>
        /// <param name="indentSize">Number of spaces per indentation level.</param>
        public LineWriter(int indentSize)
        {
            _indentationString = new string(' ', indentSize);
        }

        /// <summary>
        /// Pushes a new line with the specified depth and content.
        /// </summary>
        /// <param name="depth">Indentation depth level.</param>
        /// <param name="content">The content of the line.</param>
        public void Push(int depth, string content)
        {
            if (_builder.Length > 0)
            {
                _builder.Append(Environment.NewLine);
            }

            _builder.Append(GetIndent(depth));
            _builder.Append(content);
        }

        /// <summary>
        /// Pushes a new line and lets the callback append content directly to the backing builder.
        /// </summary>
        /// <param name="depth">Indentation depth level.</param>
        /// <param name="contentWriter">Callback that appends content after indentation.</param>
        public void Push(int depth, Action<StringBuilder> contentWriter)
        {
            if (_builder.Length > 0)
            {
                _builder.Append(Environment.NewLine);
            }

            _builder.Append(GetIndent(depth));
            contentWriter(_builder);
        }

        /// <summary>
        /// Starts a new line at the given depth and returns the underlying StringBuilder for direct zero-allocation writes.
        /// </summary>
        /// <param name="depth">Indentation depth level.</param>
        /// <returns>The underlying builder positioned after indentation for the new line.</returns>
        public StringBuilder BeginLine(int depth)
        {
            if (_builder.Length > 0)
            {
                _builder.Append(Environment.NewLine);
            }

            _builder.Append(GetIndent(depth));
            return _builder;
        }

        /// <summary>
        /// Pushes a list item (prefixed with "- ") at the specified depth.
        /// </summary>
        /// <param name="depth">Indentation depth level.</param>
        /// <param name="content">The content after the list item marker.</param>
        public void PushListItem(int depth, string content)
        {
            Push(depth, Constants.LIST_ITEM_PREFIX + content);
        }

        /// <summary>
        /// Pushes a list-item line and lets the callback append content after the list marker.
        /// </summary>
        /// <param name="depth">Indentation depth level.</param>
        /// <param name="contentWriter">Callback that appends content after <c>- </c>.</param>
        public void PushListItem(int depth, Action<StringBuilder> contentWriter)
        {
            Push(depth, builder =>
            {
                builder.Append(Constants.LIST_ITEM_PREFIX);
                contentWriter(builder);
            });
        }

        /// <summary>
        /// Returns the complete output as a single string with newlines.
        /// </summary>
        /// <returns>The accumulated TOON text.</returns>
        public override string ToString()
        {
            return _builder.ToString();
        }

        private string GetIndent(int depth)
        {
            if (depth <= 0 || _indentationString.Length == 0)
            {
                return string.Empty;
            }

            if (depth < _indentCache.Length)
            {
                var cached = _indentCache[depth];
                if (cached is not null)
                {
                    return cached;
                }
            }

            var sb = new StringBuilder(_indentationString.Length * depth);
            for (int i = 0; i < depth; i++)
            {
                sb.Append(_indentationString);
            }

            var indent = sb.ToString();
            if (depth < _indentCache.Length)
            {
                _indentCache[depth] = indent;
            }

            return indent;
        }
    }
}
