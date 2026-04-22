#nullable enable
using System;
using System.Globalization;
using System.Text;
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon.Internal.Shared;

/// <summary>
/// Writes the internal native node graph as minified JSON for <c>Toon2Json</c> conversions.
/// </summary>
internal static class JsonTextConverter
{
    /// <summary>
    /// Serializes a native node graph to minified JSON text.
    /// </summary>
    /// <param name="node">The native node graph to write.</param>
    /// <returns>A minified JSON representation of the node graph.</returns>
    public static string Write(NativeNode? node)
    {
        var builder = new StringBuilder(256);
        WriteNode(node, builder);
        return builder.ToString();
    }

    private static void WriteNode(NativeNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                return;
            case NativePrimitiveNode primitive:
                WritePrimitive(primitive.Value, builder);
                return;
            case NativeArrayNode array:
                WriteArray(array, builder);
                return;
            case NativeObjectNode obj:
                WriteObject(obj, builder);
                return;
            default:
                builder.Append("null");
                return;
        }
    }

    private static void WriteObject(NativeObjectNode obj, StringBuilder builder)
    {
        builder.Append('{');

        var isFirst = true;
        foreach (var property in obj)
        {
            if (!isFirst)
            {
                builder.Append(',');
            }

            isFirst = false;
            WriteEscapedString(property.Key, builder);
            builder.Append(':');
            WriteNode(property.Value, builder);
        }

        builder.Append('}');
    }

    private static void WriteArray(NativeArrayNode array, StringBuilder builder)
    {
        builder.Append('[');

        var isFirst = true;
        foreach (var item in array)
        {
            if (!isFirst)
            {
                builder.Append(',');
            }

            isFirst = false;
            WriteNode(item, builder);
        }

        builder.Append(']');
    }

    private static void WritePrimitive(object? value, StringBuilder builder)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                return;
            case string text:
                WriteEscapedString(text, builder);
                return;
            case bool boolValue:
                builder.Append(boolValue ? "true" : "false");
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            case double doubleValue:
                builder.Append(NumericUtils.IsFinite(doubleValue)
                    ? Convert.ToString(doubleValue, CultureInfo.InvariantCulture)
                    : "null");
                return;
            case float floatValue:
                builder.Append(NumericUtils.IsFinite(floatValue)
                    ? Convert.ToString(floatValue, CultureInfo.InvariantCulture)
                    : "null");
                return;
            case DeferredDateTimeValue dateTimeValue:
                WriteEscapedString(dateTimeValue.Value.Kind == DateTimeKind.Unspecified
                    ? dateTimeValue.Value.ToString("O", CultureInfo.InvariantCulture)
                    : dateTimeValue.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), builder);
                return;
            case DeferredDateTimeOffsetValue dateTimeOffsetValue:
                WriteEscapedString(dateTimeOffsetValue.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), builder);
                return;
#if NET6_0_OR_GREATER
            case DeferredDateOnlyValue dateOnlyValue:
                WriteEscapedString(dateOnlyValue.Value.ToString("O", CultureInfo.InvariantCulture), builder);
                return;
            case DeferredTimeOnlyValue timeOnlyValue:
                WriteEscapedString(timeOnlyValue.Value.ToString("O", CultureInfo.InvariantCulture), builder);
                return;
#endif
            default:
                WriteEscapedString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, builder);
                return;
        }
    }

    private static void WriteEscapedString(string value, StringBuilder builder)
    {
        builder.Append('"');

        for (int i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (ch < 32)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
