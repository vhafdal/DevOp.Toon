#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DevOp.Toon.Core;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;
using Line = DevOp.Toon.Internal.Decode.V4SourceLine;

namespace DevOp.Toon.Internal.Decode;

internal static class V4Decoder
{
    internal static NativeNode Decode(string source, ToonDecodeOptions options, List<V4SourceLine>? scannedLines = null)
        => (NativeNode)new Reader(source, options, null, scannedLines).Read()!;

    internal static T? Decode<T>(string source, ToonDecodeOptions options, List<V4SourceLine>? scannedLines = null)
        => (T?)new Reader(source, options, typeof(T), scannedLines).Read();

    internal static bool IsNumberToken(ReadOnlySpan<char> token) => Reader.NumberGrammar(token);

    private readonly struct SchemaKey : IEquatable<SchemaKey>
    {
        private readonly string text;
        private readonly int start;
        internal readonly int Length;
        private readonly char delimiter;
        private readonly bool allowDuplicates;
        private readonly int hash;

        internal SchemaKey(string text, int start, int length, char delimiter, bool allowDuplicates)
        {
            this.text = text; this.start = start; Length = length;
            this.delimiter = delimiter; this.allowDuplicates = allowDuplicates;
            unchecked
            {
                var value = 2166136261u;
                for (var i = start; i < start + length; i++) value = (value ^ text[i]) * 16777619u;
                hash = (int)value ^ delimiter ^ (allowDuplicates ? int.MinValue : 0);
            }
        }
        internal SchemaKey Copy() => new SchemaKey(text.Substring(start, Length), 0, Length, delimiter, allowDuplicates);
        public bool Equals(SchemaKey other) => hash == other.hash && Length == other.Length && delimiter == other.delimiter && allowDuplicates == other.allowDuplicates &&
            text.AsSpan(start, Length).SequenceEqual(other.text.AsSpan(other.start, other.Length));
        public override bool Equals(object? value) => value is SchemaKey other && Equals(other);
        public override int GetHashCode() => hash;
    }

    private sealed class HeaderGrammarException : Exception
    {
        internal HeaderGrammarException(string message) : base(message) { }
    }

    private sealed class Field
    {
        internal string Name = string.Empty;
        internal string? Constant;
        internal List<Field>? Children;
        internal Type? BoundType;
        internal V4Materialization.Member? BoundMember;
    }

    private sealed class Header
    {
        internal string? Key;
        internal string Count = string.Empty;
        internal bool Keyed;
        internal char Delimiter = ',';
        internal List<Field>? Fields;
        internal string Inline = string.Empty;
    }

    private sealed class Reader
    {
        private readonly string source;
        private readonly Type? rootType;
        private readonly ToonDecodeOptions options;
        private readonly bool custom;
        private readonly int version;
        private readonly List<Line> lines;
        private readonly List<int> blanks = new();
        private int index;
        private int recursion;
        private SchemaKey? firstSchema;
        private List<Field>? firstFields;
        private Dictionary<SchemaKey, List<Field>>? schemas;
        private int cachedSchemaCharacters;
        private Dictionary<SchemaKey, Header>? headers;
        private int cachedHeaderCharacters;

        internal Reader(string source, ToonDecodeOptions options, Type? rootType, List<Line>? scannedLines)
        {
            lines = scannedLines ?? new List<Line>();
            this.rootType = rootType;
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Indent <= 0) throw new ArgumentOutOfRangeException(nameof(options.Indent));
            custom = options.ProtocolVersion == ToonProtocolVersion.CustomV4_4;
            version = custom ? 404 : (int)options.ProtocolVersion;
            if (scannedLines == null) Scan();
            else NormalizeScan();
        }

        private void NormalizeScan()
        {
            var write = 0;
            for (var read = 0; read < lines.Count; read++)
            {
                var raw = lines[read];
                var content = raw.Start;
                var end = raw.Start + raw.Length;
                var spaces = 0; var tabs = 0;
                while (content < end && (source[content] == ' ' || source[content] == '\t'))
                { if (source[content++] == ' ') spaces++; else tabs++; }
                if (version >= 401) while (end > content && source[end - 1] == ' ') end--;
                if (content == end)
                {
                    if (tabs > 0 && options.Strict && version >= 402) throw ToonFormatException.Indentation("Tab indentation", raw.Number);
                    blanks.Add(raw.Number); continue;
                }
                if (options.Strict && (tabs > 0 || spaces % options.Indent != 0)) throw ToonFormatException.Indentation("Invalid indentation", raw.Number);
                lines[write++] = new Line(content, end - content, tabs + spaces / options.Indent, raw.Number);
            }
            if (write < lines.Count) lines.RemoveRange(write, lines.Count - write);
        }

        private void Scan()
        {
            var start = version >= 401 && source.Length > 0 && source[0] == '\ufeff' ? 1 : 0;
            var number = 0;
            while (start < source.Length)
            {
                number++;
                var end = source.IndexOf('\n', start);
                var next = end < 0 ? source.Length : end + 1;
                if (end < 0) end = source.Length;
                if (end > start && source[end - 1] == '\r') end--;
                var content = start;
                while (content < end && source[content] == ' ') content++;
                if (content < end && source[content] == '#') { start = next; continue; }
                var spaces = 0;
                var tabs = 0;
                content = start;
                while (content < end && (source[content] == ' ' || source[content] == '\t'))
                { if (source[content++] == ' ') spaces++; else tabs++; }
                if (version >= 401) while (end > content && source[end - 1] == ' ') end--;
                if (content == end)
                {
                    if (tabs > 0 && options.Strict && version >= 402)
                        throw ToonFormatException.Indentation("Tab indentation", number);
                    blanks.Add(number); start = next; continue;
                }
                if (options.Strict && (tabs > 0 || spaces % options.Indent != 0))
                    throw ToonFormatException.Indentation("Invalid indentation", number);
                lines.Add(new Line(content, end - content, tabs + spaces / options.Indent, number));
                start = next;
            }
        }

        private ReadOnlySpan<char> Text(Line line) => source.AsSpan(line.Start, line.Length);
        private string Content(Line line) => source.Substring(line.Start, line.Length);
        private ToonFormatException Error(string message) => ToonFormatException.Syntax(message,
            index < lines.Count ? lines[index].Number : lines.Count > 0 ? lines[lines.Count - 1].Number : 1);

        internal object? Read()
        {
            if (lines.Count == 0) return new V4Materialization.ObjectBuilder(rootType).Finish();
            if (lines[0].Depth != 0 && version >= 402) throw Error("Indented root");
            var rootDepth = lines[0].Depth;
            var first = Content(lines[0]);
            var header = TryHeader(first);
            object? result;
            if (header != null && header.Key == null)
            { index++; result = ReadHeader(header, rootDepth, rootType); }
            else if (first == "[]") { index++; result = new V4Materialization.ArrayBuilder(rootType).Finish(); }
            else if (lines.Count == 1 && Find(first.AsSpan(), ':') < 0)
            { index++; result = Primitive(first, false, rootType); }
            else result = ReadObject(rootDepth, rootType);
            if (index != lines.Count) throw Error("Trailing content or scalar outside root");
            return result;
        }

        private object? ReadObject(int depth, Type? targetType, V4Materialization.ObjectBuilder? target = null, bool spill = false, int? minimumDepth = null)
        {
            if (++recursion > 256) throw Error("Maximum nesting depth 256 exceeded");
            try
            {
                var obj = target ?? new V4Materialization.ObjectBuilder(targetType);
                while (index < lines.Count && lines[index].Depth >= (minimumDepth ?? depth))
                {
                    if (lines[index].Depth != depth)
                    {
                        if (!options.Strict && version < 404) { SkipOrphanLine(); continue; }
                        throw Error("Over-indented field");
                    }
                    var line = lines[index++];
                    ReadField(ref obj, Text(line), depth, spill, line.Start);
                }
                return obj.Finish();
            }
            finally { recursion--; }
        }

        private void SkipOrphanLine()
        {
            var text = Text(lines[index]);
            var listMarker = text.Length > 0 && text[0] == '-' && (text.Length == 1 || text[1] == ' ');
            if (version >= 401 && Find(text, ':') < 0 && (version >= 402 || !listMarker))
                throw Error("Scalar outside root");
            index++;
        }

        private void Set(ref V4Materialization.ObjectBuilder obj, string key, object? value, bool rejectExisting = false)
        {
            if (obj.Contains(key) && (options.Strict || rejectExisting)) throw Error("Duplicate object key");
            obj.Set(key, value);
        }

        private void ReadField(ref V4Materialization.ObjectBuilder obj, ReadOnlySpan<char> text, int depth, bool spill = false, int sourceStart = -1)
        {
            var colon = Find(text, ':');
            var bracket = colon < 0 ? -1 : Find(text.Slice(0, colon), '[');
            var header = bracket < 0 ? null : ReadCachedHeader(text, sourceStart);
            if (header != null)
            {
                if (header.Key == null) throw Error("Keyless header outside root or array item");
                Set(ref obj, header.Key, ReadHeader(header, depth, obj.FieldType(header.Key)), spill);
                return;
            }
            if (colon < 0) throw Error("Scalar outside root");
            var key = Key(text.Slice(0, colon).ToString());
            var rest = TrimSpan(text.Slice(colon + 1));
            object? value;
            if (rest.Length == 0)
            {
                if (index < lines.Count && lines[index].Depth > depth)
                    value = ReadObject(ChildDepth(depth + 1), obj.FieldType(key), minimumDepth: depth + 1);
                else value = new V4Materialization.ObjectBuilder(obj.FieldType(key)).Finish();
            }
            else
            {
                if (TrySetPrimitive(ref obj, key, rest, spill)) return;
                value = Primitive(rest, true, obj.FieldType(key));
            }
            Set(ref obj, key, value, spill);
        }

        private int ChildDepth(int expected)
        {
            if (index >= lines.Count || lines[index].Depth <= expected) return expected;
            if (options.Strict) throw Error("Skipped indentation level");
            return lines[index].Depth;
        }

        private object? ReadHeader(Header header, int depth, Type? targetType)
        {
            if (++recursion > 256) throw Error("Maximum nesting depth 256 exceeded");
            try
            {
                var begin = index;
                var count = 0;
                object? result;
                if (header.Fields != null)
                {
                    var keyed = new V4Materialization.ObjectBuilder(header.Keyed ? targetType : typeof(void));
                    var array = new V4Materialization.ArrayBuilder(header.Keyed ? typeof(void) : targetType);
                    var rowDepth = ChildDepth(depth + 1);
                    while (index < lines.Count && lines[index].Depth > depth)
                    {
                        if (lines[index].Depth != rowDepth)
                        {
                            if (!options.Strict && version < 404) { SkipOrphanLine(); continue; }
                            throw Error("Over-indented row");
                        }
                        var text = Text(lines[index]);
                        var delimiter = Find(text, header.Delimiter);
                        var colon = Find(!header.Keyed && delimiter >= 0 ? text.Slice(0, delimiter) : text, ':');
                        if (!header.Keyed && colon >= 0 && (delimiter < 0 || colon < delimiter)) break;
                        index++;
                        string? key = null;
                        if (header.Keyed)
                        {
                            if (colon < 0)
                            {
                                if (!options.Strict && version < 404) continue;
                                throw Error("Entry row requires colon");
                            }
                            key = String(TrimSpan(text.Slice(0, colon)));
                            text = text.Slice(colon + 1);
                        }
                        var cell = 0;
                        var cells = text;
                        var emptyCells = header.Keyed && TrimSpan(text).Length == 0;
                        var rowType = header.Keyed ? keyed.FieldType(key!) : array.ElementType;
                        var row = ReadFields(header.Fields, cells, header.Delimiter, ref cell, rowType, emptyCells);
                        if (cell <= cells.Length && (options.Strict || version >= 404)) throw Error("Row width mismatch");
                        object? rowValue;
                        if (index < lines.Count && lines[index].Depth > rowDepth)
                        {
                            if (!custom)
                            {
                                if (options.Strict || version >= 404) throw Error("Row spill requires custom protocol");
                                while (index < lines.Count && lines[index].Depth > rowDepth) SkipOrphanLine();
                                rowValue = row.Finish();
                            }
                            else rowValue = ReadObject(rowDepth + 1, rowType, row, spill: true);
                        }
                        else rowValue = row.Finish();
                        if (header.Keyed) Set(ref keyed, key!, rowValue); else array.Add(rowValue);
                        count++;
                    }
                    result = header.Keyed ? keyed.Finish() : array.Finish();
                }
                else if (header.Inline.Length > 0)
                {
                    var array = new V4Materialization.ArrayBuilder(targetType);
                    foreach (var value in Split(header.Inline, header.Delimiter)) array.Add(Primitive(value, false, array.ElementType));
                    count = array.Count; result = array.Finish();
                }
                else
                {
                    var array = new V4Materialization.ArrayBuilder(targetType);
                    var itemDepth = ChildDepth(depth + 1);
                    while (index < lines.Count && lines[index].Depth > depth)
                    {
                        if (lines[index].Depth != itemDepth)
                        {
                            if (!options.Strict && version < 404) { SkipOrphanLine(); continue; }
                            throw Error("Over-indented item");
                        }
                        var text = Content(lines[index]);
                        if (text.Length == 0 || text[0] != '-' || (text.Length > 1 && text[1] != ' '))
                        {
                            if (!options.Strict && version < 404) { SkipOrphanLine(); continue; }
                            break;
                        }
                        index++;
                        var rest = text.Substring(1).TrimStart(' ');
                        if (rest.Length == 0) array.Add(new V4Materialization.ObjectBuilder(array.ElementType).Finish());
                        else
                        {
                            var inner = TryHeader(rest);
                            if (inner != null && inner.Key == null)
                            {
                                if (inner.Fields != null) throw Error("Keyless fields-bearing header in list item");
                                array.Add(ReadHeader(inner, itemDepth, array.ElementType));
                            }
                            else if (inner != null || Find(rest.AsSpan(), ':') >= 0)
                            {
                                var obj = new V4Materialization.ObjectBuilder(array.ElementType);
                                ReadField(ref obj, rest, itemDepth + 1);
                                object? objectValue;
                                if (index < lines.Count && lines[index].Depth > itemDepth)
                                {
                                    if (lines[index].Depth != itemDepth + 1 && (options.Strict || version >= 404)) throw Error("Over-indented list object");
                                    objectValue = ReadObject(itemDepth + 1, array.ElementType, obj);
                                }
                                else objectValue = obj.Finish();
                                array.Add(objectValue);
                            }
                            else array.Add(Primitive(rest, true, array.ElementType));
                        }
                        count++;
                    }
                    result = array.Finish();
                }
                if (options.Strict && header.Count != count.ToString(CultureInfo.InvariantCulture)) throw Error("Declared count mismatch");
                if (options.Strict && begin < index)
                {
                    var firstLine = lines[begin].Number;
                    var lastLine = lines[index - 1].Number;
                    foreach (var blank in blanks) if (blank > firstLine && blank < lastLine) throw Error("Blank line in header span");
                }
                return result;
            }
            finally { recursion--; }
        }

        private V4Materialization.ObjectBuilder ReadFields(List<Field> fields, ReadOnlySpan<char> cells, char delimiter, ref int cell, Type? targetType, bool emptyCells = false)
        {
            if (++recursion > 256) throw Error("Maximum nesting depth 256 exceeded");
            try
            {
                var row = new V4Materialization.ObjectBuilder(targetType);
                foreach (var field in fields)
                {
                    if (field.BoundType != targetType)
                    {
                        field.BoundType = targetType;
                        field.BoundMember = row.FindMember(field.Name);
                    }
                    var member = field.BoundMember;
                    object? value;
                    if (field.Children != null) value = ReadFields(field.Children, cells, delimiter, ref cell, member?.Type ?? row.FieldType(field.Name), emptyCells).Finish();
                    else
                    {
                        ReadOnlySpan<char> token;
                        if (field.Constant != null) token = field.Constant.AsSpan();
                        else
                        {
                            if (emptyCells || cell > cells.Length)
                            {
                                if (!options.Strict && version < 404) continue;
                                throw Error("Row width mismatch");
                            }
                            var end = Find(cells.Slice(cell), delimiter);
                            if (end < 0) end = cells.Length - cell;
                            token = cells.Slice(cell, end);
                            cell += end + 1;
                        }
                        if (TrySetPrimitive(ref row, field.Name, token, boundMember: member)) continue;
                        value = Primitive(token, false, member?.Type ?? row.FieldType(field.Name));
                    }
                    row.Set(field.Name, value, member);
                }
                return row;
            }
            finally { recursion--; }
        }

        private bool TrySetPrimitive(ref V4Materialization.ObjectBuilder obj, string name, ReadOnlySpan<char> token, bool rejectExisting = false, V4Materialization.Member? boundMember = null)
        {
            token = TrimSpan(token);
            if (!token.SequenceEqual("null") && !token.SequenceEqual("true") && !token.SequenceEqual("false") && !NumberGrammar(token))
                return obj.TrySetText(name, token, options.Strict || rejectExisting, index > 0 ? lines[index - 1].Number : (int?)null, boundMember);
            if ((options.Strict || rejectExisting) && obj.Contains(name, boundMember)) throw Error("Duplicate object key");
            return obj.TrySetPrimitive(name, token, boundMember);
        }

        private Header? TryHeader(string text)
        {
            var colon = Find(text.AsSpan(), ':');
            var bracket = Find(text.AsSpan(), '[');
            if (colon < 0 || bracket < 0 || bracket > colon) return null;
            var end = text.IndexOf(']', bracket + 1);
            if (end < 0) return MalformedHeader();
            var header = new Header();
            if (bracket > 0)
            {
                if (version >= 401 && (text[bracket - 1] == ' ' || text[bracket - 1] == '\t')) return MalformedHeader();
                header.Key = Key(text.Substring(0, bracket));
            }
            var p = bracket + 1;
            var countStart = p;
            while (p < end && text[p] >= '0' && text[p] <= '9') p++;
            if (p == countStart || (p - countStart > 1 && text[countStart] == '0')) return MalformedHeader();
            header.Count = text.Substring(countStart, p - countStart);
            if (p < end && text[p] == ':') { header.Keyed = true; p++; }
            if (p < end && (text[p] == '|' || text[p] == '\t')) header.Delimiter = text[p++];
            if (p != end) return MalformedHeader();
            p++;
            if (p < text.Length && text[p] == '{')
            {
                try { header.Fields = ReadCachedFields(text, ref p, header.Delimiter, version == 400 && header.Count == "0"); }
                catch (HeaderGrammarException error)
                {
                    if (!options.Strict && version < 404) return null;
                    throw Error(error.Message);
                }
            }
            if (p >= text.Length && version < 403) return null;
            if (p >= text.Length || text[p++] != ':' || (header.Keyed && header.Fields == null)) return MalformedHeader();
            header.Inline = Trim(text.Substring(p));
            if (header.Fields != null && header.Inline.Length > 0) return MalformedHeader();
            return header;
        }

        private Header? ReadCachedHeader(ReadOnlySpan<char> text, int sourceStart)
        {
            if (sourceStart < 0 || text.Length > 4096) return TryHeader(text.ToString());
            var key = new SchemaKey(source, sourceStart, text.Length, '\0', false);
            if (headers != null && headers.TryGetValue(key, out var cached)) return cached;
            var header = TryHeader(text.ToString());
            if (header != null && header.Inline.Length == 0 && (headers?.Count ?? 0) < 256 && cachedHeaderCharacters + text.Length <= 32768)
            {
                (headers ??= new Dictionary<SchemaKey, Header>()).Add(key, header);
                cachedHeaderCharacters += text.Length;
            }
            return header;
        }

        private Header? MalformedHeader()
        {
            if (!options.Strict && version < 404) return null;
            throw Error("Malformed header");
        }

        private List<Field> ReadCachedFields(string text, ref int position, char delimiter, bool allowDuplicates)
        {
            var start = position;
            var end = -1;
            var depth = 0;
            var quoted = false;
            for (var i = start; i < text.Length; i++)
            {
                var ch = text[i];
                if (quoted && ch == '\\') { i++; continue; }
                if (ch == '\"') { quoted = !quoted; continue; }
                if (quoted) continue;
                if (ch == '{') depth++;
                if (ch == '}' && --depth == 0) { end = i; break; }
            }
            if (end < 0 || end - start + 1 > 4096) return ParseHeaderFields(text, ref position, delimiter, allowDuplicates);
            var key = new SchemaKey(text, start, end - start + 1, delimiter, allowDuplicates);
            if (firstSchema.HasValue && firstSchema.Value.Equals(key))
            { position = end + 1; return firstFields!; }
            if (schemas != null && schemas.TryGetValue(key, out var cached))
            { position = end + 1; return cached; }
            var fields = ParseHeaderFields(text, ref position, delimiter, allowDuplicates);
            if (!firstSchema.HasValue)
            {
                firstSchema = key.Copy(); firstFields = fields;
                cachedSchemaCharacters = key.Length;
            }
            else if ((schemas?.Count ?? 1) < 64 && cachedSchemaCharacters + key.Length <= 32768)
            {
                if (schemas == null) schemas = new Dictionary<SchemaKey, List<Field>> { [firstSchema.Value] = firstFields! };
                schemas[key.Copy()] = fields;
                cachedSchemaCharacters += key.Length;
            }
            return fields;
        }

        private List<Field> ParseHeaderFields(string text, ref int position, char delimiter, bool allowDuplicates)
        {
            var fields = ParseFields(text, ref position, delimiter, allowDuplicates);
            // Without a cell every row line would be blank, so the rows could not be counted or told apart.
            if (custom && !HasCell(fields)) throw new HeaderGrammarException("Header requires at least one cell column");
            return fields;
        }

        private static bool HasCell(List<Field> fields)
        {
            foreach (var field in fields)
                if (field.Children != null ? HasCell(field.Children) : field.Constant == null) return true;
            return false;
        }

        private List<Field> ParseFields(string text, ref int p, char delimiter, bool allowDuplicates = false)
        {
            if (++recursion > 256) throw Error("Maximum nesting depth 256 exceeded");
            try
            {
                p++;
                var result = new List<Field>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string>? constants = null;
                while (p < text.Length)
                {
                    var start = p;
                    var quoted = false;
                    var equals = -1;
                    while (p < text.Length)
                    {
                        var ch = text[p];
                        if (quoted && ch == '\\') { p += 2; continue; }
                        if (ch == '"') quoted = !quoted;
                        if (!quoted && (ch == '{' || ch == '}' || ch == delimiter)) break;
                        if (!quoted && (ch == ',' || ch == '|' || ch == '\t') && ch != delimiter) throw new HeaderGrammarException("Wrong field delimiter");
                        if (custom && !quoted && ch == '=')
                        {
                            if (equals >= 0) throw new HeaderGrammarException("Unquoted '=' in constant value");
                            equals = p;
                        }
                        p++;
                    }
                    if (p >= text.Length) throw new HeaderGrammarException("Unclosed field list");
                    var raw = text.Substring(start, (equals >= 0 ? equals : p) - start);
                    var constant = equals >= 0 ? ReadConstant(text, equals + 1, p) : null;
                    var name = Key(raw);
                    var emptyToken = TrimSpan(raw.AsSpan()).IsEmpty;
                    var emptyGroup = result.Count == 0 && text[p] == '}';
                    var spacedGroup = text[p] == '{' && raw.Length > 0 && (raw[raw.Length - 1] == ' ' || raw[raw.Length - 1] == '\t');
                    if (emptyToken && (version >= 402 || emptyGroup) || version >= 402 && spacedGroup)
                        throw new HeaderGrammarException("Empty or space-separated field group");
                    var duplicate = !names.Add(name);
                    // A constant is never resolved by "last field wins": that would drop it or every cell silently.
                    if (duplicate && (options.Strict && !allowDuplicates || constant != null || constants?.Contains(name) == true))
                        throw new HeaderGrammarException("Duplicate header field");
                    if (constant != null) (constants ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
                    var field = new Field { Name = name, Constant = constant };
                    if (text[p] == '{')
                    {
                        field.Children = ParseFields(text, ref p, delimiter, allowDuplicates);
                        while (p < text.Length && text[p] == ' ') p++;
                    }
                    result.Add(field);
                    if (p >= text.Length) throw new HeaderGrammarException("Unclosed field list");
                    if (text[p] == '}') { p++; return result; }
                    if (text[p++] != delimiter) throw new HeaderGrammarException("Invalid field separator");
                }
                throw new HeaderGrammarException("Unclosed field list");
            }
            finally { recursion--; }
        }

        private string ReadConstant(string text, int start, int end)
        {
            if (text[end] == '{') throw new HeaderGrammarException("Constant on field group");
            var value = TrimSpan(text.AsSpan(start, end - start));
            if (value.IsEmpty) throw new HeaderGrammarException("Empty constant value");
            if (value[0] == '"')
            {
                try { String(value); }
                catch (ToonFormatException) { throw new HeaderGrammarException("Malformed constant value"); }
            }
            return value.ToString();
        }

        private string Key(string token)
        {
            var trimmed = TrimSpan(token.AsSpan());
            if (trimmed.Length == token.Length && (token.Length == 0 || token[0] != '"'))
            { ValidateUnicode(trimmed); return token; }
            return String(trimmed);
        }
        private string Trim(string token) => version >= 400 ? token.Trim(' ') : token.Trim();

        private ReadOnlySpan<char> TrimSpan(ReadOnlySpan<char> token)
        {
            var begin = 0; var end = token.Length;
            while (begin < end && token[begin] == ' ') begin++;
            while (end > begin && token[end - 1] == ' ') end--;
            return token.Slice(begin, end - begin);
        }

        private object? Primitive(ReadOnlySpan<char> token, bool allowEmptyArray, Type? targetType)
        {
            token = TrimSpan(token);
            string? error;
            object? value;
            if (token.Length > 0 && token[0] == '"') value = V4Materialization.Scalar(String(token), targetType, out error);
            else if (allowEmptyArray && token.SequenceEqual("[]")) return new V4Materialization.ArrayBuilder(targetType).Finish();
            else if (token.SequenceEqual("null")) value = V4Materialization.Scalar(null, targetType, out error);
            else if (token.SequenceEqual("true")) value = V4Materialization.Scalar(true, targetType, out error);
            else if (token.SequenceEqual("false")) value = V4Materialization.Scalar(false, targetType, out error);
            else if (NumberGrammar(token))
            {
#if NETSTANDARD2_0
                var parsed = double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number);
#else
                var parsed = double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number);
#endif
                if (!parsed || double.IsInfinity(number) || double.IsNaN(number))
                {
                    if (options.Strict || version >= 404) throw Error("Number outside supported finite range");
                    value = V4Materialization.Scalar(targetType == typeof(void) ? null : token.ToString(), targetType, out error);
                }
                else value = V4Materialization.Number(token, number == 0 ? 0d : number, targetType, out error);
            }
            else
            {
                ValidateUnicode(token);
                value = V4Materialization.Scalar(targetType == typeof(void) ? null : token.ToString(), targetType, out error);
            }
            if (error != null) throw BindingError(error);
            return value;
        }

        // Every caller has consumed the token's line, so it is the previous one.
        private ToonFormatException BindingError(string message) => ToonFormatException.Syntax(message, lines[index - 1].Number);

        private string String(ReadOnlySpan<char> token)
        {
            if (token.Length == 0 || token[0] != '"') { ValidateUnicode(token); return token.ToString(); }
            if (token.Length >= 2 && token[token.Length - 1] == '"')
            {
                var inner = token.Slice(1, token.Length - 2);
                if (inner.IndexOf('\\') < 0 && inner.IndexOf('"') < 0)
                { ValidateUnicode(inner); return inner.ToString(); }
            }
            var builder = new StringBuilder(Math.Max(0, token.Length - 2));
            for (var p = 1; p < token.Length; p++)
            {
                var ch = token[p];
                if (ch == '"')
                {
                    if (p != token.Length - 1) throw Error("Characters after closing quote");
                    var value = builder.ToString(); ValidateUnicode(value); return value;
                }
                if (ch != '\\') { builder.Append(ch); continue; }
                if (++p >= token.Length) throw Error("Unterminated escape");
                switch (token[p])
                {
                    case '\\': builder.Append('\\'); break;
                    case '"': builder.Append('"'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (p + 4 >= token.Length) throw Error("Incomplete Unicode escape");
                        var value = 0;
                        for (var k = 0; k < 4; k++)
                        {
                            var digit = token[++p];
                            var hex = digit >= '0' && digit <= '9' ? digit - '0' : digit >= 'a' && digit <= 'f' ? digit - 'a' + 10 : digit >= 'A' && digit <= 'F' ? digit - 'A' + 10 : -1;
                            if (hex < 0) throw Error("Invalid Unicode escape");
                            value = value * 16 + hex;
                        }
                        if (value >= 0xd800 && value <= 0xdfff) throw Error("Surrogate escape is not a Unicode scalar");
                        builder.Append((char)value); break;
                    default: throw Error("Invalid escape");
                }
            }
            throw Error("Unterminated quoted string");
        }

        private void ValidateUnicode(ReadOnlySpan<char> text)
        {
            for (var p = 0; p < text.Length; p++)
            {
                if (!char.IsSurrogate(text[p])) continue;
                if (!char.IsHighSurrogate(text[p]) || p + 1 >= text.Length || !char.IsLowSurrogate(text[++p])) throw Error("Unpaired surrogate");
            }
        }

        internal static bool NumberGrammar(ReadOnlySpan<char> text) => NumericUtils.IsNumberGrammar(text);

        private List<string> Split(string text, char delimiter)
        {
            var result = new List<string>();
            var start = 0;
            var quoted = false;
            for (var p = 0; p < text.Length; p++)
            {
                if (quoted && text[p] == '\\') { p++; continue; }
                if (text[p] == '"') quoted = !quoted;
                if (!quoted && text[p] == delimiter) { result.Add(Trim(text.Substring(start, p - start))); start = p + 1; }
            }
            result.Add(Trim(text.Substring(start)));
            return result;
        }

        private static int Find(ReadOnlySpan<char> text, char target)
        {
            var first = text.IndexOfAny('"', target);
            if (first < 0 || text[first] == target) return first;
            var quoted = false;
            for (var p = first; p < text.Length; p++)
            {
                if (quoted && text[p] == '\\') { p++; continue; }
                if (text[p] == '"') quoted = !quoted;
                if (!quoted && text[p] == target) return p;
            }
            return -1;
        }
    }
}
