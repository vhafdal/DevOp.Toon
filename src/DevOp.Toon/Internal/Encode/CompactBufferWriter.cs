#nullable enable
using System;
using System.Buffers;

namespace DevOp.Toon.Internal.Encode;

internal sealed class CompactBufferWriter : IDisposable
{
    private char[] _buffer;
    private int _position;
    private readonly int _indentSize;
    private readonly string _newLine;
    private bool _disposed;

    public CompactBufferWriter(int indentSize, int initialCapacity = 1024)
    {
        _indentSize = indentSize;
        _newLine = Environment.NewLine;
        _buffer = ArrayPool<char>.Shared.Rent(Math.Max(initialCapacity, 256));
    }

    public void Push(int depth, string content)
    {
        StartLine(depth);
        Append(content);
    }

    public void Push(int depth, Action<CompactBufferWriter> contentWriter)
    {
        StartLine(depth);
        contentWriter(this);
    }

    public void PushListItem(int depth, Action<CompactBufferWriter> contentWriter)
    {
        Push(depth, writer =>
        {
            writer.Append(Constants.LIST_ITEM_PREFIX);
            contentWriter(writer);
        });
    }

    public void PushRawBlock(string content)
    {
        if (_position > 0)
        {
            Append(_newLine);
        }

        Append(content);
    }

    public void Append(char value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

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

    public override string ToString()
    {
        return new string(_buffer, 0, _position);
    }

    /// <summary>
    /// Returns the underlying char buffer and valid character count for direct byte conversion.
    /// The caller must not use the buffer after calling Dispose().
    /// </summary>
    internal (char[] buffer, int length) GetCharBuffer() => (_buffer, _position);

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
