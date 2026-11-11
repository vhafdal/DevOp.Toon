#nullable enable
using System;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Shared rules for CustomV4_4 constant columns: when two cell values are guaranteed to write the same token,
/// and how a cell token is written as a header value.
/// </summary>
internal static class ConstantColumnTokens
{
    /// <summary>The fewest rows a table needs before any column is written as a header constant.</summary>
    internal const int MinimumRows = 3;

    /// <summary>
    /// Returns whether <paramref name="left"/> and <paramref name="right"/> always encode to the same cell token.
    /// </summary>
    /// <remarks>
    /// Values must share a runtime type, so <c>1</c>, <c>1.0</c> and <c>"1"</c> never match even where two of them
    /// would print alike. CLR equality is not enough where it ignores part of the written text: a
    /// <see cref="DateTime"/> kind, a <see cref="DateTimeOffset"/> offset or a <see cref="Uri"/> fragment.
    /// Types without a rule here are never constant.
    /// </remarks>
    internal static bool SameToken(object? left, object? right)
    {
        if (left == null || right == null) return left == null && right == null;
        if (left.GetType() != right.GetType()) return false;
        switch (left)
        {
            case string text: return string.Equals(text, (string)right, StringComparison.Ordinal);
            case DateTime value: return Same(value, (DateTime)right);
            case DeferredDateTimeValue value: return Same(value.Value, ((DeferredDateTimeValue)right).Value);
            case DateTimeOffset value: return value.EqualsExact((DateTimeOffset)right);
            case DeferredDateTimeOffsetValue value: return value.Value.EqualsExact(((DeferredDateTimeOffsetValue)right).Value);
            case Uri value: return string.Equals(value.ToString(), right.ToString(), StringComparison.Ordinal);
            case bool or char or byte or sbyte or short or ushort or int or uint or long or ulong or decimal or double or float
                or Guid or TimeSpan or Version or Enum:
                return left.Equals(right);
#if NET6_0_OR_GREATER
            case DateOnly or TimeOnly: return left.Equals(right);
            case DeferredDateOnlyValue value: return value.Value == ((DeferredDateOnlyValue)right).Value;
            case DeferredTimeOnlyValue value: return value.Value == ((DeferredTimeOnlyValue)right).Value;
#endif
            default: return false;
        }
    }

    /// <summary>Returns whether a value of <paramref name="type"/> can be compared by <see cref="SameToken"/>.</summary>
    internal static bool Supported(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum || type == typeof(string) || type == typeof(bool) || type == typeof(char)
            || type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
            || type == typeof(decimal) || type == typeof(double) || type == typeof(float)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(TimeSpan)
            || type == typeof(Uri) || type == typeof(Version)
#if NET6_0_OR_GREATER
            || type == typeof(DateOnly) || type == typeof(TimeOnly)
#endif
            ;
    }

    internal static bool Same(DateTime left, DateTime right) => left.Ticks == right.Ticks && left.Kind == right.Kind;

    /// <summary>
    /// Writes the cell token rendered by <paramref name="writeCell"/> as a header value. A header value needs the
    /// cell quoting plus quotes around any <c>=</c>, field delimiter or header punctuation, so an unquoted cell
    /// token containing one is re-quoted; it is always a string there, because no literal or number contains them.
    /// </summary>
    internal static void WriteHeaderValue<TState>(CompactBufferWriter writer, TState state, Action<CompactBufferWriter, TState> writeCell)
    {
        var start = writer.Length;
        writeCell(writer, state);
        var (buffer, end) = writer.GetCharBuffer();
        if (end == start || buffer[start] == '"' || buffer.AsSpan(start, end - start).IndexOfAny(HeaderSpecial.AsSpan()) < 0) return;
        var token = new string(buffer, start, end - start);
        writer.Truncate(start);
        V4Encoder.Quote(writer, token);
    }

    private static readonly char[] HeaderSpecial = { '=', ',', '|', '\t', '{', '}', ':', '[', ']', '"', '\\' };
}
