#nullable enable
using System;
using System.Collections.Generic;
using DevOp.Toon.Core;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode;

internal static class V4ProtocolDetector
{
    internal readonly struct Detection
    {
        internal Detection(ToonProtocolVersion version, bool requiresHint, bool unique, int? indent, List<V4SourceLine>? lines)
        { Version = version; RequiresHint = requiresHint; IsUnique = unique; Indent = indent; Lines = lines; }
        internal ToonProtocolVersion Version { get; }
        internal bool RequiresHint { get; }
        internal bool IsUnique { get; }
        internal int? Indent { get; }
        internal List<V4SourceLine>? Lines { get; }
    }

    internal static Detection Detect(string source, bool collectLines = false, int fallbackIndent = 2)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (fallbackIndent <= 0) throw new ArgumentOutOfRangeException(nameof(fallbackIndent), fallbackIndent, "Indent must be greater than 0.");
        var collected = collectLines ? new List<V4SourceLine>() : null;
        var official = false;
        var spill = false;
        var legacyConflict = false;
        var minorConflict = source.Length > 0 && source[0] == '\ufeff';
        // A later non-strict depth jump can reveal a smaller unit for a hyphen-carried header.
        var gcd = ToonDecoderOptionsDetector.DetectIndent(source, ignoreComments: true) ?? 0;
        var indentUnit = gcd > 0 ? gcd : fallbackIndent;
        var tableIndent = -1;
        var tableRowIndent = -1;
        var tableDelimiter = ',';
        var tableKeyed = false;
        List<(int Indent, int RowIndent, char Delimiter, bool Keyed)>? parentTables = null;
        var previousIndent = -1;
        var previousWasRow = false;
        var number = 0;
        for (var start = 0; start < source.Length;)
        {
            number++;
            var newline = source.IndexOf('\n', start);
            if (newline < 0) newline = source.Length;
            var end = newline > start && source[newline - 1] == '\r' ? newline - 1 : newline;
            var pos = start;
            while (pos < end && source[pos] == ' ') pos++;
            var spaces = pos - start;
            if (pos < end && source[pos] == '#')
            { legacyConflict = true; start = newline + 1; continue; }
            collected?.Add(new V4SourceLine(start, end - start, 0, number));
            var tabs = 0;
            while (pos < end && (source[pos] == ' ' || source[pos] == '\t'))
            { if (source[pos++] == ' ') spaces++; else tabs++; }
            if (pos == end) { start = newline + 1; continue; }
            if (tabs > 0) legacyConflict = true;
            var indent = spaces / indentUnit + tabs;
            var text = source.AsSpan(pos, end - pos);
            var colon = Find(text, ':');
            while (tableIndent >= 0 && indent <= tableIndent)
            {
                if (parentTables == null || parentTables.Count == 0)
                { tableIndent = -1; tableRowIndent = -1; break; }
                var parent = parentTables[parentTables.Count - 1];
                parentTables.RemoveAt(parentTables.Count - 1);
                tableIndent = parent.Indent;
                tableRowIndent = parent.RowIndent;
                tableDelimiter = parent.Delimiter;
                tableKeyed = parent.Keyed;
            }
            if (tableIndent >= 0 && tableRowIndent < 0) tableRowIndent = indent;
            var delimiterIndex = Find(text, tableDelimiter);
            var row = tableIndent >= 0 && indent == tableRowIndent &&
                (tableKeyed || colon < 0 || delimiterIndex >= 0 && delimiterIndex < colon);
            if (tableIndent >= 0 && previousWasRow && indent > previousIndent) spill = true;
            var bracket = row || colon < 0 ? -1 : Find(text.Slice(0, colon), '[');
            var header = bracket >= 0;
            var delimiter = row ? tableDelimiter : ',';
            var keyed = false;
            var hasFields = false;
            var headerColon = -1;
            if (header)
            {
                var close = text.Slice(bracket + 1).IndexOf(']');
                if (close >= 0)
                {
                    close += bracket + 1;
                    if (close > bracket + 1 && (text[close - 1] == '\t' || text[close - 1] == '|')) delimiter = text[close - 1];
                    keyed = text.Slice(bracket + 1, close - bracket - 1).IndexOf(':') >= 0;
                    official |= keyed;
                    hasFields = close + 1 < text.Length && text[close + 1] == '{';
                    var braces = 0;
                    var quoted = false;
                    for (var p = close + 1; p < text.Length; p++)
                    {
                        var ch = text[p];
                        if (quoted && ch == '\\') { p++; continue; }
                        if (ch == '"') { quoted = !quoted; continue; }
                        if (quoted) continue;
                        if (ch == '{') { braces++; if (braces > 1) official = true; }
                        if (ch == '}') braces--;
                        if (ch == ':' && braces == 0) { headerColon = p; break; }
                    }
                }
            }
            var splitValues = row || header;
            var valueStart = row ? tableKeyed && colon >= 0 ? colon + 1 : 0 : header ? headerColon >= 0 ? headerColon + 1 : text.Length : colon >= 0 ? colon + 1 : 0;
            if (!row && !header && colon < 0 && text.Length > 1 && text[0] == '-' && text[1] == ' ')
            { valueStart = 1; while (valueStart < text.Length && text[valueStart] == ' ') valueStart++; }
            var trailingWhitespace = text.Length;
            while (trailingWhitespace > 0 && char.IsWhiteSpace(text[trailingWhitespace - 1])) trailingWhitespace--;
            var keyWhitespace = colon;
            while (keyWhitespace > 0 && char.IsWhiteSpace(text[keyWhitespace - 1])) keyWhitespace--;
            var quote = false;
            var atTokenStart = true;
            var whitespaceRunEnd = -1;
            var whitespaceEndsToken = false;
            for (var p = 0; p < text.Length; p++)
            {
                var ch = text[p];
                if (quote && ch == '\\') { p++; continue; }
                if (ch == '"')
                {
                    if (!quote && p >= valueStart && !atTokenStart) minorConflict = true;
                    quote = !quote; atTokenStart = false; continue;
                }
                if (quote) continue;
                if (ch == '\t' && !(splitValues && delimiter == '\t') &&
                    (p == 0 || p >= trailingWhitespace || p >= valueStart && atTokenStart || (!row || tableKeyed) && colon >= 0 && p >= keyWhitespace && p < colon))
                    legacyConflict = true;
                if (ch == '\ufeff') legacyConflict = true;
                if (char.IsWhiteSpace(ch) && ch != ' ' && ch != '\t')
                {
                    if (p >= whitespaceRunEnd)
                    {
                        whitespaceRunEnd = p;
                        while (whitespaceRunEnd < text.Length && char.IsWhiteSpace(text[whitespaceRunEnd]) && !(splitValues && text[whitespaceRunEnd] == delimiter)) whitespaceRunEnd++;
                        whitespaceEndsToken = whitespaceRunEnd == text.Length || splitValues && text[whitespaceRunEnd] == delimiter;
                    }
                    if (p == 0 || p >= valueStart && atTokenStart || whitespaceEndsToken || (!row || tableKeyed) && colon >= 0 && p >= keyWhitespace && p < colon)
                        legacyConflict = true;
                }
                if (p >= valueStart && atTokenStart && (ch == '+' || ch == '.' || ch == '-' || ch >= '0' && ch <= '9'))
                {
                    var tokenEnd = text.Length;
                    if (splitValues)
                    {
                        var relative = Find(text.Slice(p), delimiter);
                        if (relative >= 0) tokenEnd = p + relative;
                    }
                    while (tokenEnd > p && text[tokenEnd - 1] == ' ') tokenEnd--;
                    var token = text.Slice(p, tokenEnd - p);
                    if (HasNumericConflict(token)) legacyConflict = true;
                }
                atTokenStart = p < valueStart || splitValues && ch == delimiter || atTokenStart && ch == ' ';
            }
            previousWasRow = row;
            previousIndent = indent;
            if (header && hasFields)
            {
                if (tableIndent >= 0)
                    (parentTables ??= new List<(int, int, char, bool)>()).Add((tableIndent, tableRowIndent, tableDelimiter, tableKeyed));
                var keyStart = 1;
                while (keyStart < bracket && text[keyStart] == ' ') keyStart++;
                var namedHyphenHeader = text.Length > 1 && text[0] == '-' && text[1] == ' ' && keyStart < bracket;
                tableIndent = indent + (namedHyphenHeader ? 1 : 0);
                tableRowIndent = -1; tableDelimiter = delimiter; tableKeyed = keyed; previousWasRow = false;
            }
            start = newline + 1;
        }
        var version = spill ? ToonProtocolVersion.CustomV4_4 : ToonProtocolVersion.V4_4;
        return new Detection(version, minorConflict || legacyConflict && !official, official && spill, gcd > 0 ? gcd : null, collected);
    }

    private static bool HasNumericConflict(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty) return false;
        var suspicious = token[0] == '+' || token[0] == '.' || token[token.Length - 1] == '.' || token.Length > 308 ||
            token.IndexOf('e') >= 0 || token.IndexOf('E') >= 0 ||
            token[0] == '-' && token.Length > 1 && (token[1] == '.' || token[1] == '0' && token.Length > 2 && token[2] >= '0' && token[2] <= '9');
        return suspicious && LiteralUtils.IsNumericLiteral(token) != V4Decoder.IsNumberToken(token);
    }

    private static int Find(ReadOnlySpan<char> text, char character)
    {
        var first = text.IndexOfAny('"', character);
        if (first < 0 || text[first] != '"') return first;
        var quote = false;
        for (var p = first; p < text.Length; p++)
        {
            if (quote && text[p] == '\\') { p++; continue; }
            if (text[p] == '"') { quote = !quote; continue; }
            if (!quote && text[p] == character) return p;
        }
        return -1;
    }

}
