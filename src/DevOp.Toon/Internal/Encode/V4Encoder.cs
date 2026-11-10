#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DevOp.Toon.Internal.Encode;

/// <summary>Writes the v4 grammar without introducing an intermediate text representation.</summary>
internal static class V4Encoder
{
    internal static string Encode(NativeNode? value, ResolvedEncodeOptions options)
    {
        using var writer = new CompactBufferWriter(options.Indent, newLine: "\n");
        WriteValue(writer, value, null, 0, false, options);
        return writer.ToString();
    }

    internal static void WriteValue(CompactBufferWriter writer, NativeNode? value, string? key, int depth, bool listItem, ResolvedEncodeOptions options)
    {
        if (value is NativeArrayNode array)
        {
            WriteArray(writer, array, key, depth, listItem, options);
            return;
        }
        if (value is NativeObjectNode obj)
        {
            if (!listItem && TryKeyedRows(obj, out var rows, out var entryKeys) && TryColumns(rows, options, out var columns))
            {
                Start(writer, depth, false, options);
                Header(writer, key, rows.Count, true, columns, options);
                WriteRows(writer, rows, entryKeys, columns, depth + 1, options);
                return;
            }
            if (listItem)
            {
                if (obj.Count == 0)
                {
                    Start(writer, depth, false, options);
                    writer.Append('-');
                    return;
                }
                var first = true;
                foreach (var pair in obj)
                {
                    if (Omit(pair.Value, options)) continue;
                    if (first)
                    {
                        WriteFirstField(writer, pair.Key, pair.Value, depth, options);
                        first = false;
                    }
                    else WriteValue(writer, pair.Value, pair.Key, depth + 1, false, options);
                }
                if (first) { Start(writer, depth, false, options); writer.Append('-'); }
                return;
            }
            if (key != null)
            {
                Start(writer, depth, false, options);
                Key(writer, key, options.AnnotationKeys);
                writer.Append(':');
                depth++;
            }
            foreach (var pair in obj)
                if (!Omit(pair.Value, options)) WriteValue(writer, pair.Value, pair.Key, depth, false, options);
            return;
        }
        Start(writer, depth, listItem, options);
        if (key != null) { Key(writer, key, options.AnnotationKeys); writer.Append(": "); }
        Primitive(writer, value, options.Delimiter, key == null && !listItem);
    }

    private static void WriteFirstField(CompactBufferWriter writer, string key, NativeNode? value, int depth, ResolvedEncodeOptions options)
    {
        Start(writer, depth, true, options);
        if (value is NativeArrayNode array)
        {
            WriteArrayBody(writer, array, key, depth + 1, false, options);
        }
        else if (value is NativeObjectNode obj)
        {
            if (TryKeyedRows(obj, out var rows, out var keys) && TryColumns(rows, options, out var columns))
            {
                Header(writer, key, rows.Count, true, columns, options);
                WriteRows(writer, rows, keys, columns, depth + 2, options);
            }
            else
            {
                Key(writer, key, options.AnnotationKeys); writer.Append(':');
                foreach (var pair in obj)
                    if (!Omit(pair.Value, options)) WriteValue(writer, pair.Value, pair.Key, depth + 2, false, options);
            }
        }
        else
        {
            Key(writer, key, options.AnnotationKeys); writer.Append(": "); Primitive(writer, value, options.Delimiter, false);
        }
    }

    private static void WriteArray(CompactBufferWriter writer, NativeArrayNode array, string? key, int depth, bool listItem, ResolvedEncodeOptions options)
    {
        Start(writer, depth, listItem, options);
        WriteArrayBody(writer, array, key, depth, listItem, options);
    }

    private static void WriteArrayBody(CompactBufferWriter writer, NativeArrayNode array, string? key, int depth, bool listItem, ResolvedEncodeOptions options)
    {
        if (array.Count == 0 && !listItem)
        {
            if (key != null) { Key(writer, key, options.AnnotationKeys); writer.Append(": "); }
            writer.Append("[]");
            return;
        }
        if (AllPrimitive(array))
        {
            Header(writer, key, array.Count, false, null, options);
            if (array.Count > 0) writer.Append(' ');
            for (var i = 0; i < array.Count; i++)
            {
                if (i != 0) writer.Append(options.Delimiter);
                Primitive(writer, array[i], options.Delimiter, false);
            }
            return;
        }
        if (!listItem && TryArrayRows(array, out var rows) && TryColumns(rows, options, out var columns))
        {
            Header(writer, key, rows.Count, false, columns, options);
            WriteRows(writer, rows, null, columns, depth + 1, options);
            return;
        }
        Header(writer, key, array.Count, false, null, options);
        foreach (var item in array) WriteValue(writer, item, null, depth + 1, true, options);
    }

    internal static void Start(CompactBufferWriter writer, int depth, bool listItem, ResolvedEncodeOptions options)
    {
        writer.StartLine(depth);
        if (listItem) writer.Append("- ");
    }

    private sealed class Column
    {
        internal string Name = string.Empty;
        internal List<Column>? Children;
    }

    private static bool TryArrayRows(NativeArrayNode array, out List<NativeObjectNode> rows)
    {
        rows = null!;
        foreach (var item in array)
            if (item is not NativeObjectNode { Count: > 0 }) return false;
        rows = new List<NativeObjectNode>(array.Count);
        foreach (var item in array) rows.Add((NativeObjectNode)item!);
        return rows.Count > 0;
    }

    private static bool TryKeyedRows(NativeObjectNode obj, out List<NativeObjectNode> rows, out List<string> keys)
    {
        rows = null!;
        keys = null!;
        if (obj.Count < 2) return false;
        foreach (var pair in obj)
            if (pair.Value is not NativeObjectNode { Count: > 0 }) return false;
        rows = new List<NativeObjectNode>(obj.Count);
        keys = new List<string>(obj.Count);
        foreach (var pair in obj)
        {
            rows.Add((NativeObjectNode)pair.Value!); keys.Add(pair.Key);
        }
        return true;
    }

    private static bool TryColumns(IReadOnlyList<NativeObjectNode> rows, ResolvedEncodeOptions options, out List<Column> columns)
    {
        var custom = options.ProtocolVersion == ToonProtocolVersion.CustomV4_4;
        columns = new List<Column>();
        foreach (var pair in rows[0])
        {
            var present = true;
            var primitives = true;
            var objects = true;
            var allEmpty = true;
            List<NativeObjectNode>? children = null;
            foreach (var row in rows)
            {
                if (!row.ContainsKey(pair.Key)) { present = false; break; }
                var item = row[pair.Key];
                primitives &= item is null or NativePrimitiveNode;
                if (item is NativeObjectNode child && child.Count > 0) (children ??= new List<NativeObjectNode>()).Add(child);
                else objects = false;
                allEmpty &= item == null || item is NativePrimitiveNode { Value: null or "" };
            }
            if (!present) { if (!custom) return false; continue; }
            if (primitives)
            {
                if (!(options.IgnoreNullOrEmpty && allEmpty)) columns.Add(new Column { Name = pair.Key });
                continue;
            }
            if (objects && children != null && SameKeys(children) && TryUniformColumns(children, options, out var nested))
            {
                columns.Add(new Column { Name = pair.Key, Children = nested });
                continue;
            }
            if (!custom) return false;
        }
        if (!custom && !SameKeys(rows)) return false;
        return columns.Count > 0;
    }

    private static bool TryUniformColumns(IReadOnlyList<NativeObjectNode> rows, ResolvedEncodeOptions options, out List<Column> columns)
    {
        columns = new List<Column>();
        foreach (var pair in rows[0])
        {
            var primitive = true;
            List<NativeObjectNode>? children = null;
            foreach (var row in rows)
            {
                var value = row[pair.Key];
                primitive &= value is null or NativePrimitiveNode;
                if (value is NativeObjectNode child && child.Count > 0) (children ??= new List<NativeObjectNode>()).Add(child);
            }
            if (primitive) columns.Add(new Column { Name = pair.Key });
            else if (children != null && children.Count == rows.Count && SameKeys(children) && TryUniformColumns(children, options, out var nested))
                columns.Add(new Column { Name = pair.Key, Children = nested });
            else return false;
        }
        return columns.Count > 0;
    }

    private static bool SameKeys(IReadOnlyList<NativeObjectNode> rows)
    {
        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].Count != rows[0].Count) return false;
            foreach (var pair in rows[0]) if (!rows[i].ContainsKey(pair.Key)) return false;
        }
        return true;
    }

    private static void WriteRows(CompactBufferWriter writer, IReadOnlyList<NativeObjectNode> rows, IReadOnlyList<string>? keys, List<Column> columns, int depth, ResolvedEncodeOptions options)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            Start(writer, depth, false, options);
            if (keys != null) { Key(writer, keys[i], options.AnnotationKeys); writer.Append(": "); }
            var first = true;
            Cells(writer, rows[i], columns, options.Delimiter, ref first);
            if (options.ProtocolVersion != ToonProtocolVersion.CustomV4_4) continue;
            foreach (var pair in rows[i])
            {
                var headerField = false;
                foreach (var column in columns) if (column.Name == pair.Key) { headerField = true; break; }
                if (!headerField && !Omit(pair.Value, options) && !(options.IgnoreNullOrEmpty && Suppressed(rows, pair.Key)))
                    WriteValue(writer, pair.Value, pair.Key, depth + 1, false, options);
            }
        }
    }

    private static bool Suppressed(IReadOnlyList<NativeObjectNode> rows, string key)
    {
        foreach (var row in rows)
            if (!row.ContainsKey(key) || row[key] is not null and not NativePrimitiveNode { Value: null or "" }) return false;
        return true;
    }

    private static void Cells(CompactBufferWriter writer, NativeObjectNode row, List<Column> columns, char delimiter, ref bool first)
    {
        foreach (var column in columns)
        {
            if (column.Children != null) Cells(writer, (NativeObjectNode)row[column.Name]!, column.Children, delimiter, ref first);
            else
            {
                if (!first) writer.Append(delimiter);
                Primitive(writer, row[column.Name], delimiter, false);
                first = false;
            }
        }
    }

    private static void Header(CompactBufferWriter writer, string? key, int count, bool keyed, List<Column>? columns, ResolvedEncodeOptions options)
    {
        var delimiter = options.Delimiter;
        if (key != null) Key(writer, key, options.AnnotationKeys);
        writer.Append('['); writer.Append(count);
        if (keyed) writer.Append(':');
        if (delimiter != ',') writer.Append(delimiter);
        writer.Append(']');
        if (columns != null) Fields(writer, columns, delimiter, options.AnnotationKeys);
        writer.Append(':');
    }

    private static void Fields(CompactBufferWriter writer, List<Column> columns, char delimiter, bool annotationKeys)
    {
        writer.Append('{');
        for (var i = 0; i < columns.Count; i++)
        {
            if (i != 0) writer.Append(delimiter);
            Key(writer, columns[i].Name, annotationKeys);
            if (columns[i].Children != null) Fields(writer, columns[i].Children!, delimiter, annotationKeys);
        }
        writer.Append('}');
    }

    private static bool Omit(NativeNode? value, ResolvedEncodeOptions options) => options.ExcludeEmptyArrays && value is NativeArrayNode { Count: 0 };
    private static bool AllPrimitive(NativeArrayNode array)
    {
        foreach (var item in array) if (item is NativeObjectNode or NativeArrayNode) return false;
        return true;
    }

    internal static void Key(CompactBufferWriter writer, string key, bool annotationKeys)
    {
        if (IsPlainKey(key, annotationKeys)) writer.Append(key); else Quote(writer, key);
    }

    /// <summary>
    /// Official keys match <c>[A-Za-z_][A-Za-z0-9_.]*</c>. Custom 4.4 also writes annotation keys
    /// (<c>@id</c>, <c>$ref</c>, <c>@odata.context</c>, <c>Raven-Clr-Type</c>) unquoted: <c>@</c> and
    /// <c>$</c> anywhere, <c>-</c> anywhere but first, because a leading <c>-</c> would read as a list item.
    /// </summary>
    internal static bool IsPlainKey(string key, bool annotationKeys)
    {
        if (key.Length == 0) return false;
        var start = key[0];
        if (!(AsciiLetter(start) || start == '_' || annotationKeys && start is '@' or '$')) return false;
        foreach (var c in key)
        {
            if (AsciiLetter(c) || c is >= '0' and <= '9' or '_' or '.') continue;
            if (annotationKeys && c is '@' or '$' or '-') continue;
            return false;
        }
        return true;
    }
    private static bool AsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static void Primitive(CompactBufferWriter writer, NativeNode? node, char delimiter, bool root)
    {
        PrimitiveRaw(writer, (node as NativePrimitiveNode)?.Value, delimiter, root);
    }

    internal static void PrimitiveRaw(CompactBufferWriter writer, object? value, char delimiter, bool root)
    {
        if (value == null) { writer.Append("null"); return; }
        if (value is string text) { String(writer, text, delimiter, root); return; }
        if (value is char character) { String(writer, character.ToString(), delimiter, root); return; }
        if (value is Uri uri) { String(writer, uri.ToString(), delimiter, root); return; }
        if (value is bool boolean) { writer.Append(boolean ? "true" : "false"); return; }
        if (value is double d) { Number(writer, d); return; }
        if (value is float f) { Float(writer, f); return; }
        if (value is decimal dec)
        {
#if NETSTANDARD2_0
            writer.Append(dec.ToString("0.############################", CultureInfo.InvariantCulture));
#else
            Span<char> buffer = stackalloc char[64];
            if (dec.TryFormat(buffer, out var count, "0.############################", CultureInfo.InvariantCulture)) writer.Append(buffer.Slice(0, count));
            else writer.Append(dec.ToString("0.############################", CultureInfo.InvariantCulture));
#endif
            return;
        }
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
        { NativePrimitives.AppendPrimitiveRaw(writer, value, delimiter); return; }
        if (value is DeferredDateTimeValue dt) { String(writer, dt.Value.ToString("O", CultureInfo.InvariantCulture), delimiter, root); return; }
        if (value is DeferredDateTimeOffsetValue dto) { String(writer, dto.Value.ToString("O", CultureInfo.InvariantCulture), delimiter, root); return; }
        NativePrimitives.AppendPrimitiveRaw(writer, value, delimiter);
    }

    internal static void Number(CompactBufferWriter writer, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) { writer.Append("null"); return; }
        if (value == 0) { writer.Append('0'); return; }
#if !NETSTANDARD2_0
        Span<char> digitsBuffer = stackalloc char[32];
        if (value.TryFormat(digitsBuffer, out var written, "R", CultureInfo.InvariantCulture))
        {
            var candidate = digitsBuffer.Slice(0, written);
            if (candidate.IndexOf('E') < 0 && candidate.IndexOf('e') < 0) { writer.Append(candidate); return; }
        }
#endif
        var text = value.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        var e = text.IndexOf('e');
        if (e >= 0 && Math.Abs(value) >= 1e-6 && Math.Abs(value) < 1e21)
        {
            var exponent = int.Parse(text.Substring(e + 1), CultureInfo.InvariantCulture);
            var mantissa = text.Substring(0, e);
            var negative = mantissa[0] == '-';
            if (negative) mantissa = mantissa.Substring(1);
            var dot = mantissa.IndexOf('.');
            var point = (dot >= 0 ? dot : mantissa.Length) + exponent;
            var digits = mantissa.Replace(".", "");
            if (negative) writer.Append('-');
            if (point <= 0) { writer.Append("0."); writer.Append(new string('0', -point)); writer.Append(digits); }
            else if (point >= digits.Length) { writer.Append(digits); writer.Append(new string('0', point - digits.Length)); }
            else { writer.Append(digits.Substring(0, point)); writer.Append('.'); writer.Append(digits.Substring(point)); }
            return;
        }
        if (e >= 0)
        {
            writer.Append(text.Substring(0, e)); writer.Append('e');
            var exponent = int.Parse(text.Substring(e + 1), CultureInfo.InvariantCulture);
            if (exponent >= 0) writer.Append('+');
            writer.Append(exponent);
        }
        else writer.Append(text);
    }

    internal static void Float(CompactBufferWriter writer, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) { writer.Append("null"); return; }
        if (value == 0) { writer.Append('0'); return; }
#if !NETSTANDARD2_0
        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var count, "R", CultureInfo.InvariantCulture))
        {
            var text = buffer.Slice(0, count);
            if (text.IndexOf('E') < 0 && text.IndexOf('e') < 0) { writer.Append(text); return; }
        }
#endif
        Number(writer, double.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
    }

    private static bool IsNumericLike(string text)
    {
        var i = 0;
        if (i < text.Length && text[i] is '+' or '-') i++;
        var first = i;
        while (i < text.Length && text[i] is >= '0' and <= '9') i++;
        if (i == first) return false;
        if (i < text.Length && text[i] == '.')
        {
            first = ++i;
            while (i < text.Length && text[i] is >= '0' and <= '9') i++;
            if (i == first) return false;
        }
        if (i < text.Length && text[i] is 'e' or 'E')
        {
            i++;
            if (i < text.Length && text[i] is '+' or '-') i++;
            first = i;
            while (i < text.Length && text[i] is >= '0' and <= '9') i++;
            if (i == first) return false;
        }
        return i == text.Length;
    }

    private static void String(CompactBufferWriter writer, string text, char delimiter, bool root)
    {
        ValidateUnicode(text);
        var quote = text.Length == 0 || text == "true" || text == "false" || text == "null" || IsNumericLike(text);
        if (text.Length > 0) quote |= text[0] is ' ' or '\t' or '-' or '#' || text[text.Length - 1] is ' ' or '\t' || root && text[0] == '\ufeff';
        foreach (var c in text) quote |= c < ' ' || c == delimiter || c is ':' or '"' or '\\' or '[' or ']' or '{' or '}';
        if (quote) Quote(writer, text); else writer.Append(text);
    }

    private static void Quote(CompactBufferWriter writer, string text)
    {
        ValidateUnicode(text);
        writer.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': writer.Append("\\\\"); break;
                case '"': writer.Append("\\\""); break;
                case '\n': writer.Append("\\n"); break;
                case '\r': writer.Append("\\r"); break;
                case '\t': writer.Append("\\t"); break;
                default:
                    if (c < ' ') { writer.Append("\\u"); writer.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                    else writer.Append(c);
                    break;
            }
        }
        writer.Append('"');
    }

    private static void ValidateUnicode(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i])) throw new ArgumentException("TOON strings cannot contain unpaired surrogates.");
            }
            else if (char.IsLowSurrogate(text[i])) throw new ArgumentException("TOON strings cannot contain unpaired surrogates.");
        }
    }
}
