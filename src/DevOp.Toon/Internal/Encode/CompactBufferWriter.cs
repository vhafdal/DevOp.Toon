#nullable enable
using System;
using System.Buffers;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Pooled character writer optimized for compact TOON output and direct UTF-8 conversion without creating intermediate strings.
/// </summary>
internal sealed class CompactBufferWriter : IDisposable
{
    private char[] _buffer;
    private int _position;
    private readonly int _indentSize;
    private readonly string _newLine;
    private bool _disposed;

    /// <summary>
    /// Initializes a pooled writer with the requested indentation size and initial character capacity.
    /// </summary>
    /// <param name="indentSize">The number of spaces written for each indentation depth.</param>
    /// <param name="initialCapacity">The initial rented character capacity.</param>
    public CompactBufferWriter(int indentSize, int initialCapacity = 1024)
    {
        _indentSize = indentSize;
        _newLine = Environment.NewLine;
        _buffer = ArrayPool<char>.Shared.Rent(Math.Max(initialCapacity, 256));
    }

    /// <summary>
    /// Starts a new TOON line at the specified depth and appends content.
    /// </summary>
    /// <param name="depth">The indentation depth to write.</param>
    /// <param name="content">The line content to append after indentation.</param>
    public void Push(int depth, string content)
    {
        StartLine(depth);
        Append(content);
    }

    /// <summary>
    /// Starts a new TOON line at the specified depth and lets the callback append line content directly.
    /// </summary>
    /// <param name="depth">The indentation depth to write.</param>
    /// <param name="contentWriter">The callback that appends content for the line.</param>
    public void Push(int depth, Action<CompactBufferWriter> contentWriter)
    {
        StartLine(depth);
        contentWriter(this);
    }

    /// <summary>
    /// Starts a new list-item line and lets the callback append content after the list marker.
    /// </summary>
    /// <param name="depth">The indentation depth to write.</param>
    /// <param name="contentWriter">The callback that appends content after <c>- </c>.</param>
    public void PushListItem(int depth, Action<CompactBufferWriter> contentWriter)
    {
        Push(depth, writer =>
        {
            writer.Append(Constants.LIST_ITEM_PREFIX);
            contentWriter(writer);
        });
    }

    /// <summary>
    /// Appends a preformatted block, inserting a line break first when the buffer already contains content.
    /// </summary>
    /// <param name="content">The raw TOON block to append.</param>
    public void PushRawBlock(string content)
    {
        if (_position > 0)
        {
            Append(_newLine);
        }

        Append(content);
    }

    /// <summary>
    /// Appends a single character to the buffer.
    /// </summary>
    /// <param name="value">The character to append.</param>
    public void Append(char value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    /// <summary>
    /// Appends a string when it is non-null and non-empty.
    /// </summary>
    /// <param name="value">The string to append.</param>
    public void Append(string? value)
    {
        if (value is not { Length: > 0 } text)
        {
            return;
        }

        EnsureCapacity(text.Length);
        text.AsSpan().CopyTo(_buffer.AsSpan(_position));
        _position += text.Length;
    }

    /// <summary>
    /// Appends a span of characters directly into the pooled buffer.
    /// </summary>
    /// <param name="value">The characters to append.</param>
    public void Append(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_position));
        _position += value.Length;
    }

    /// <summary>
    /// Materializes the buffered characters as a string.
    /// </summary>
    /// <returns>The current buffer contents.</returns>
    public override string ToString()
    {
        return new string(_buffer, 0, _position);
    }

    /// <summary>
    /// Returns the underlying char buffer and valid character count for direct byte conversion.
    /// The caller must not use the buffer after calling Dispose().
    /// </summary>
    internal (char[] buffer, int length) GetCharBuffer() => (_buffer, _position);

    /// <summary>
    /// Appends an integer using invariant-culture formatting.
    /// </summary>
    /// <param name="value">The integer value to append.</param>
    public void Append(int value)
    {
#if NETSTANDARD2_0
        Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
#else
        Span<char> buffer = stackalloc char[12];
        if (value.TryFormat(buffer, out var written, default, System.Globalization.CultureInfo.InvariantCulture))
            Append(buffer.Slice(0, written));
        else
            Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
#endif
    }

    /// <summary>
    /// Returns the rented buffer to the shared array pool and clears writer state.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = Array.Empty<char>();
        _position = 0;
        _disposed = true;
    }

    /// <summary>
    /// Starts a line by appending a newline when needed and writing indentation spaces.
    /// </summary>
    /// <param name="depth">The indentation depth to write.</param>
    internal void StartLine(int depth)
    {
        if (_position > 0)
        {
            Append(_newLine);
        }

        if (depth <= 0 || _indentSize <= 0)
        {
            return;
        }

        int count = depth * _indentSize;
        EnsureCapacity(count);
        _buffer.AsSpan(_position, count).Fill(Constants.SPACE);
        _position += count;
    }

    private void EnsureCapacity(int additionalCapacity)
    {
        int required = _position + additionalCapacity;
        if (required <= _buffer.Length)
        {
            return;
        }

        int newSize = Math.Max(required, _buffer.Length * 2);
        var newBuffer = ArrayPool<char>.Shared.Rent(newSize);
        _buffer.AsSpan(0, _position).CopyTo(newBuffer);
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }
}
