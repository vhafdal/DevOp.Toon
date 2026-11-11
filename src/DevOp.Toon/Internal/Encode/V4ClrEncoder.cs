#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Encode;

/// <summary>Emits indexable CLR tables using cached accessors without a native value graph.</summary>
internal static class V4ClrEncoder
{
    private static readonly ConcurrentDictionary<Type, Plan> Plans = new();

    internal static bool TryEncode(object? value, ResolvedEncodeOptions options, out string text)
    {
        text = string.Empty;
        if (!TryEncodeToBuffer(value, options, out var writer)) return false;
        using (writer) text = writer.ToString();
        return true;
    }

    internal static bool TryEncodeToBuffer(object? value, ResolvedEncodeOptions options, out CompactBufferWriter writer)
    {
        writer = null!;
        if (value == null || NativeNormalize.IsPrimitiveOrNull(value) || DictionaryEntries.IsDictionaryType(value.GetType()) || value is not IList && !NativeNormalize.IsPlainObjectType(value.GetType()) || value is IList { Count: 0 } || options.ByteArrayFormat == ToonByteArrayFormat.Base64String && value is IEnumerable<byte>) return false;
        writer = new CompactBufferWriter(options.Indent, newLine: "\n");
        try
        {
            if (TryWrite(writer, value, null, 0, options)) return true;
            writer.Dispose();
            writer = null!;
            return false;
        }
        catch { writer.Dispose(); throw; }
    }

    private static bool TryWrite(CompactBufferWriter writer, object? value, PropertyKey? key, int depth, ResolvedEncodeOptions options, bool firstListField = false)
    {
        if (NativeNormalize.IsPrimitiveOrNull(value)) return false;
        if (options.ByteArrayFormat == ToonByteArrayFormat.Base64String && value is IEnumerable<byte>) return false;
        if (value != null && DictionaryEntries.IsDictionaryType(value.GetType())) return false;
        if (value != null && value is not IList && NativeNormalize.IsPlainObjectType(value.GetType()))
        {
            var objectPlan = Plans.GetOrAdd(value.GetType(), static t => new Plan(t));
            if (!objectPlan.Eligible) return false;
            var potentialKeyed = objectPlan.Properties.Count >= 2;
            foreach (var property in objectPlan.Properties)
                potentialKeyed &= !Plan.Scalar(Nullable.GetUnderlyingType(property.Info.PropertyType) ?? property.Info.PropertyType) && NativeNormalize.IsPlainObjectType(property.Info.PropertyType);
            if (potentialKeyed) return false;
            if (key != null)
            {
                V4Encoder.Start(writer, firstListField ? depth - 1 : depth, firstListField, options);
                key.Write(writer, options.AnnotationKeys); writer.Append(':');
                depth++;
            }
            for (var propertyIndex = 0; propertyIndex < objectPlan.Properties.Count; propertyIndex++)
            {
                var property = objectPlan.Properties[propertyIndex];
                var column = objectPlan.PropertyColumns[propertyIndex];
                if (column != null && column.Nested == null)
                {
                    V4Encoder.Start(writer, depth, false, options);
                    objectPlan.PropertyKeys[propertyIndex].Write(writer, options.AnnotationKeys); writer.Append(": ");
                    column.Emit(writer, value, options.Delimiter);
                }
                else
                {
                    var raw = property.Getter(value);
                    if (options.ExcludeEmptyArrays && raw is ICollection { Count: 0 }) continue;
                    WriteNamedValue(writer, raw, objectPlan.PropertyKeys[propertyIndex], depth, options);
                }
            }
            return true;
        }
        if (value is not IList rows) return false;
        if (rows.Count == 0 || PrimitiveRows(rows, options))
        {
            WritePrimitiveArray(writer, rows, key, depth, firstListField, false, options);
            return true;
        }
        for (var i = 0; i < rows.Count; i++)
            if (NativeNormalize.IsPrimitiveOrNull(rows[i]) || rows[i] is IList)
            {
                WriteListArray(writer, rows, key, depth, firstListField, options);
                return true;
            }
        var type = rows[0]?.GetType();
        if (type == null || !NativeNormalize.IsPlainObjectType(type)) return false;
        var plan = Plans.GetOrAdd(type, static t => new Plan(t));
        if (!plan.Eligible) return false;
        for (var i = 0; i < rows.Count; i++) if (rows[i]!.GetType() != type) return false;
        var custom = options.ProtocolVersion == ToonProtocolVersion.CustomV4_4;
        var tabular = plan.Columns.Length > 0 && (custom || plan.Spills.Length == 0);
        if (tabular)
            for (var i = 0; i < rows.Count; i++)
                if (!plan.Validate(rows[i]!)) { tabular = false; break; }
        if (!tabular)
        {
            // A declared complex property can hold only nulls and still qualify as a primitive column.
            // Only observed array values prove that official tabular detection must fail.
            if (custom || !HasArrayColumn(rows, plan, options)) return false;
            WriteListArray(writer, rows, key, depth, firstListField, options);
            return true;
        }
        var tablePlan = plan!;
        bool[]? suppressed = null;
        if (options.IgnoreNullOrEmpty)
        {
            suppressed = new bool[tablePlan.Columns.Length];
            for (var c = 0; c < tablePlan.Columns.Length; c++)
            {
                if (tablePlan.Columns[c].Nested != null) continue;
                var empty = true;
                for (var r = 0; r < rows.Count; r++)
                {
                    var item = tablePlan.Columns[c].Property.Getter(rows[r]!);
                    if (item != null && item is not "") { empty = false; break; }
                }
                suppressed[c] = empty;
            }
            var all = true;
            foreach (var flag in suppressed) all &= flag;
            if (all) return false;
        }
        bool[]? constants = null;
        if (options.ConstantColumns && rows.Count >= ConstantColumnTokens.MinimumRows)
            constants = MarkConstants(rows, tablePlan, suppressed);
        V4Encoder.Start(writer, firstListField ? depth - 1 : depth, firstListField, options);
        if (key != null) key.Write(writer, options.AnnotationKeys);
        writer.Append('['); writer.Append(rows.Count);
        if (options.Delimiter != ',') writer.Append(options.Delimiter);
        writer.Append(']');
        var headerLeaf = 0;
        Fields(writer, tablePlan, rows[0]!, options.Delimiter, suppressed, constants, ref headerLeaf, options.AnnotationKeys);
        writer.Append(':');
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]!;
            V4Encoder.Start(writer, depth + 1, false, options);
            var first = true;
            var leaf = 0;
            Cells(writer, row, tablePlan, options.Delimiter, ref first, suppressed, constants, ref leaf);
            if (custom)
            {
                for (var spillIndex = 0; spillIndex < tablePlan.Spills.Length; spillIndex++)
                {
                    var spill = tablePlan.Spills[spillIndex];
                    var raw = spill.Getter(row);
                    if (options.ExcludeEmptyArrays && raw is ICollection { Count: 0 }) continue;
                    WriteNamedValue(writer, raw, tablePlan.SpillKeys[spillIndex], depth + 2, options);
                }
            }
        }
        return true;
    }

    private static void WriteNamedValue(CompactBufferWriter writer, object? value, PropertyKey key, int depth, ResolvedEncodeOptions options, bool firstListField = false)
    {
        if (IsNormalizedPrimitive(value, options))
        {
            V4Encoder.Start(writer, firstListField ? depth - 1 : depth, firstListField, options);
            key.Write(writer, options.AnnotationKeys); writer.Append(": ");
            WritePrimitive(writer, value, options);
            return;
        }
        if (TryWrite(writer, value, key, depth, options, firstListField)) return;
        var node = NativeNormalize.Normalize(value, options);
        if (firstListField)
        {
            var wrapper = new NativeObjectNode();
            wrapper[key.Name] = node;
            V4Encoder.WriteValue(writer, wrapper, null, depth - 1, true, options);
        }
        else V4Encoder.WriteValue(writer, node, key.Name, depth, false, options);
    }

    private static bool HasArrayColumn(IList rows, Plan plan, ResolvedEncodeOptions options)
    {
        for (var row = 0; row < rows.Count; row++)
            foreach (var property in plan.Properties)
            {
                if (property.EnumerableElementType == null) continue;
                var value = property.Getter(rows[row]!);
                if (value is IEnumerable && value is not string && !DictionaryEntries.IsDictionaryType(value.GetType()) &&
                    !(options.ByteArrayFormat == ToonByteArrayFormat.Base64String && value is IEnumerable<byte>)) return true;
            }
        return false;
    }

    private static bool IsNormalizedPrimitive(object? value, ResolvedEncodeOptions options)
        => NativeNormalize.IsPrimitiveOrNull(value) || options.ByteArrayFormat == ToonByteArrayFormat.Base64String && value is IEnumerable<byte>;

    private static void WritePrimitive(CompactBufferWriter writer, object? value, ResolvedEncodeOptions options)
    {
        if (options.ByteArrayFormat == ToonByteArrayFormat.Base64String && value is IEnumerable<byte> && ByteSequenceText.TryToBase64String(value, out var base64))
            V4Encoder.PrimitiveRaw(writer, base64, options.Delimiter, false);
        else V4Encoder.PrimitiveRaw(writer, value, options.Delimiter, false);
    }

    private static bool PrimitiveRows(IList rows, ResolvedEncodeOptions options)
    {
        for (var i = 0; i < rows.Count; i++) if (!IsNormalizedPrimitive(rows[i], options)) return false;
        return true;
    }

    private static void ArrayHeader(CompactBufferWriter writer, PropertyKey? key, int count, ResolvedEncodeOptions options)
    {
        if (key != null) key.Write(writer, options.AnnotationKeys);
        writer.Append('['); writer.Append(count);
        if (options.Delimiter != ',') writer.Append(options.Delimiter);
        writer.Append("]:");
    }

    private static void WritePrimitiveArray(CompactBufferWriter writer, IList rows, PropertyKey? key, int depth, bool firstListField, bool anonymousListItem, ResolvedEncodeOptions options)
    {
        V4Encoder.Start(writer, firstListField ? depth - 1 : depth, firstListField || anonymousListItem, options);
        if (rows.Count == 0 && !anonymousListItem)
        {
            if (key != null) { key.Write(writer, options.AnnotationKeys); writer.Append(": "); }
            writer.Append("[]");
            return;
        }
        ArrayHeader(writer, key, rows.Count, options);
        if (rows.Count > 0) writer.Append(' ');
        for (var i = 0; i < rows.Count; i++)
        {
            if (i != 0) writer.Append(options.Delimiter);
            WritePrimitive(writer, rows[i], options);
        }
    }

    private static void WriteListArray(CompactBufferWriter writer, IList rows, PropertyKey? key, int depth, bool firstListField, ResolvedEncodeOptions options)
    {
        V4Encoder.Start(writer, firstListField ? depth - 1 : depth, firstListField, options);
        ArrayHeader(writer, key, rows.Count, options);
        for (var i = 0; i < rows.Count; i++) WriteListItem(writer, rows[i], depth + 1, options);
    }

    private static void WriteListItem(CompactBufferWriter writer, object? value, int depth, ResolvedEncodeOptions options)
    {
        if (IsNormalizedPrimitive(value, options))
        {
            V4Encoder.Start(writer, depth, true, options);
            WritePrimitive(writer, value, options);
            return;
        }
        if (value is IList rows)
        {
            if (PrimitiveRows(rows, options)) WritePrimitiveArray(writer, rows, null, depth, false, true, options);
            else
            {
                V4Encoder.Start(writer, depth, true, options);
                ArrayHeader(writer, null, rows.Count, options);
                for (var i = 0; i < rows.Count; i++) WriteListItem(writer, rows[i], depth + 1, options);
            }
            return;
        }
        if (value != null && NativeNormalize.IsPlainObjectType(value.GetType()))
        {
            var plan = Plans.GetOrAdd(value.GetType(), static t => new Plan(t));
            if (plan.Eligible)
            {
                WriteObjectListItem(writer, value, plan, depth, options);
                return;
            }
        }
        V4Encoder.WriteValue(writer, NativeNormalize.Normalize(value, options), null, depth, true, options);
    }

    private static void WriteObjectListItem(CompactBufferWriter writer, object value, Plan plan, int depth, ResolvedEncodeOptions options)
    {
        var first = true;
        for (var i = 0; i < plan.Properties.Count; i++)
        {
            var property = plan.Properties[i];
            var column = plan.PropertyColumns[i];
            if (column != null && column.Nested == null)
            {
                V4Encoder.Start(writer, first ? depth : depth + 1, first, options);
                plan.PropertyKeys[i].Write(writer, options.AnnotationKeys); writer.Append(": ");
                column.Emit(writer, value, options.Delimiter);
            }
            else
            {
                var raw = property.Getter(value);
                if (options.ExcludeEmptyArrays && raw is ICollection { Count: 0 }) continue;
                WriteNamedValue(writer, raw, plan.PropertyKeys[i], depth + 1, options, first);
            }
            first = false;
        }
        if (first) { V4Encoder.Start(writer, depth, false, options); writer.Append('-'); }
    }

    /// <summary>
    /// Returns, per leaf column in depth-first header order, whether every row writes the same token. The mask is
    /// local to one table write: plans are cached per type and shared by every option set.
    /// </summary>
    private static bool[] MarkConstants(IList rows, Plan plan, bool[]? suppressed)
    {
        var constants = new bool[plan.LeafCount];
        var leaf = 0;
        var firstWritten = -1;
        var cells = 0;
        MarkConstants(rows, plan, suppressed, constants, ref leaf, ref firstWritten, ref cells);
        if (cells == 0 && firstWritten >= 0) constants[firstWritten] = false;
        return constants;
    }

    private static void MarkConstants(IList rows, Plan plan, bool[]? suppressed, bool[] constants, ref int leaf, ref int firstWritten, ref int cells)
    {
        for (var i = 0; i < plan.Columns.Length; i++)
        {
            var column = plan.Columns[i];
            if (column.Nested != null)
            {
                var children = new object[rows.Count];
                for (var r = 0; r < rows.Count; r++) children[r] = column.Property.Getter(rows[r]!)!;
                MarkConstants(children, column.Nested, null, constants, ref leaf, ref firstWritten, ref cells);
                continue;
            }
            var index = leaf++;
            if (suppressed != null && suppressed[i]) continue;
            if (firstWritten < 0) firstWritten = index;
            var compare = column.SameToken.Value;
            var same = compare != null;
            var first = rows[0]!;
            for (var r = 1; same && r < rows.Count; r++) same = compare!(first, rows[r]!);
            constants[index] = same;
            if (!same) cells++;
        }
    }

    private static void Fields(CompactBufferWriter writer, Plan plan, object? sample, char delimiter, bool[]? suppressed, bool[]? constants, ref int leaf, bool annotationKeys)
    {
        writer.Append('{');
        var first = true;
        for (var i = 0; i < plan.Columns.Length; i++)
        {
            var column = plan.Columns[i];
            if (suppressed != null && suppressed[i]) { leaf++; continue; }
            if (!first) writer.Append(delimiter);
            column.Key.Write(writer, annotationKeys);
            if (column.Nested != null) Fields(writer, column.Nested, constants != null ? column.Property.Getter(sample!) : null, delimiter, null, constants, ref leaf, annotationKeys);
            else if (constants != null && constants[leaf++])
            {
                writer.Append('=');
                ConstantColumnTokens.WriteHeaderValue(writer, (column, sample!, delimiter), static (target, cell) => cell.column.Emit(target, cell.Item2, cell.delimiter));
            }
            first = false;
        }
        writer.Append('}');
    }

    private static void Cells(CompactBufferWriter writer, object row, Plan plan, char delimiter, ref bool first, bool[]? suppressed, bool[]? constants, ref int leaf)
    {
        for (var i = 0; i < plan.Columns.Length; i++)
        {
            var column = plan.Columns[i];
            if (column.Nested != null) { Cells(writer, column.Property.Getter(row)!, column.Nested, delimiter, ref first, null, constants, ref leaf); continue; }
            var index = leaf++;
            if (suppressed != null && suppressed[i] || constants != null && constants[index]) continue;
            if (!first) writer.Append(delimiter);
            column.Emit(writer, row, delimiter);
            first = false;
        }
    }

    private sealed class PropertyKey
    {
        internal readonly string Name;
        private readonly string? encoded;
        private readonly string? annotationEncoded;

        internal PropertyKey(string name)
        {
            Name = name;
            encoded = Encode(name, false);
            annotationEncoded = V4Encoder.IsPlainKey(name, true) ? name : encoded;
        }

        private static string? Encode(string name, bool annotationKeys)
        {
            if (V4Encoder.IsPlainKey(name, annotationKeys)) return name;
            try
            {
                using var writer = new CompactBufferWriter(2, newLine: "\n");
                V4Encoder.Key(writer, name, annotationKeys);
                return writer.ToString();
            }
            catch (ArgumentException)
            {
                // Invalid aliases must fail only when their property is emitted, after omissions.
                return null;
            }
        }

        internal void Write(CompactBufferWriter writer, bool annotationKeys)
        {
            var text = annotationKeys ? annotationEncoded : encoded;
            if (text != null) writer.Append(text);
            else V4Encoder.Key(writer, Name, annotationKeys);
        }
    }

    private sealed class Column
    {
        internal NativeNormalize.PropertyMetadata Property;
        internal PropertyKey Key = null!;
        internal Plan? Nested;
        internal Action<CompactBufferWriter, object, char> Emit = null!;
        /// <summary>Compares the column of two rows by written token, or yields null when the type is never constant.</summary>
        /// <remarks>Compiled on first use so encoding with constant columns off never pays for it.</remarks>
        internal Lazy<Func<object, object, bool>?> SameToken = null!;
    }

    private static Func<object, object, bool>? CompileTokenComparer(NativeNormalize.PropertyMetadata property)
    {
        var type = property.Info.PropertyType;
        if (!ConstantColumnTokens.Supported(type)) return null;
        var left = Expression.Parameter(typeof(object), "left");
        var right = Expression.Parameter(typeof(object), "right");
        var declaring = property.Info.DeclaringType!;
        var leftValue = Expression.Property(Expression.Convert(left, declaring), property.Info);
        var rightValue = Expression.Property(Expression.Convert(right, declaring), property.Info);
        MethodInfo method;
        if (type == typeof(DateTime) || type == typeof(DateTime?) || type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?) || type == typeof(Uri))
            method = typeof(V4ClrEncoder).GetMethod(nameof(SameToken), BindingFlags.Static | BindingFlags.NonPublic, null, new[] { type, type }, null)!;
        else method = typeof(V4ClrEncoder).GetMethod(nameof(SameDefault), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type);
        return Expression.Lambda<Func<object, object, bool>>(Expression.Call(method, leftValue, rightValue), left, right).Compile();
    }

    private static bool SameDefault<T>(T left, T right) => EqualityComparer<T>.Default.Equals(left, right);
    private static bool SameToken(DateTime left, DateTime right) => ConstantColumnTokens.Same(left, right);
    private static bool SameToken(DateTime? left, DateTime? right) => left.HasValue ? right.HasValue && ConstantColumnTokens.Same(left.Value, right.Value) : !right.HasValue;
    private static bool SameToken(DateTimeOffset left, DateTimeOffset right) => left.EqualsExact(right);
    private static bool SameToken(DateTimeOffset? left, DateTimeOffset? right) => left.HasValue ? right.HasValue && left.Value.EqualsExact(right.Value) : !right.HasValue;
    private static bool SameToken(Uri? left, Uri? right) => left == null ? right == null : right != null && string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);

    private static Action<CompactBufferWriter, object, char> CompileEmitter(NativeNormalize.PropertyMetadata property)
    {
        var writer = Expression.Parameter(typeof(CompactBufferWriter), "writer");
        var row = Expression.Parameter(typeof(object), "row");
        var delimiter = Expression.Parameter(typeof(char), "delimiter");
        var propertyValue = Expression.Property(Expression.Convert(row, property.Info.DeclaringType!), property.Info);
        var nullable = Nullable.GetUnderlyingType(propertyValue.Type);
        var captured = nullable == null ? null : Expression.Variable(propertyValue.Type, "value");
        Expression value = captured ?? (Expression)propertyValue;
        var scalar = nullable == null ? (Expression)value : Expression.Property(value, "Value");
        var type = nullable ?? value.Type;
        Expression body;
        if (type.IsEnum) { type = Enum.GetUnderlyingType(type); scalar = Expression.Convert(scalar, type); }
        if (type == typeof(double) || type == typeof(float))
            body = Expression.Call(typeof(V4Encoder).GetMethod(type == typeof(double) ? "Number" : "Float", BindingFlags.Static | BindingFlags.NonPublic)!, writer, scalar);
        else if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            body = Expression.Call(typeof(NativePrimitives).GetMethod("AppendDateTimeRaw", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(CompactBufferWriter), type }, null)!, writer, scalar);
        else if (type == typeof(bool))
            body = Expression.Call(writer, typeof(CompactBufferWriter).GetMethod("Append", new[] { typeof(string) })!, Expression.Condition(scalar, Expression.Constant("true"), Expression.Constant("false")));
        else if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(decimal))
            body = Expression.Call(typeof(V4ClrEncoder).GetMethod(nameof(Integral), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type), writer, scalar);
        else
            body = Expression.Call(typeof(V4Encoder).GetMethod("PrimitiveRaw", BindingFlags.Static | BindingFlags.NonPublic)!, writer, Expression.Convert(scalar, typeof(object)), delimiter, Expression.Constant(false));
        if (nullable != null)
        {
            body = Expression.IfThenElse(Expression.Property(value, "HasValue"), body, Expression.Call(writer, typeof(CompactBufferWriter).GetMethod("Append", new[] { typeof(string) })!, Expression.Constant("null")));
            body = Expression.Block(new[] { captured! }, Expression.Assign(captured!, propertyValue), body);
        }
        return Expression.Lambda<Action<CompactBufferWriter, object, char>>(body, writer, row, delimiter).Compile();
    }

#if NETSTANDARD2_0
    private static void Integral<T>(CompactBufferWriter writer, T value) where T : IFormattable
        => writer.Append(value.ToString(typeof(T) == typeof(decimal) ? "0.############################" : null, CultureInfo.InvariantCulture));
#else
    private static void Integral<T>(CompactBufferWriter writer, T value) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[64];
        ReadOnlySpan<char> format = typeof(T) == typeof(decimal) ? "0.############################" : default;
        if (value.TryFormat(buffer, out var count, format, CultureInfo.InvariantCulture)) writer.Append(buffer.Slice(0, count));
        else writer.Append(value.ToString(null, CultureInfo.InvariantCulture));
    }
#endif

    private sealed class Plan
    {
        internal readonly IReadOnlyList<NativeNormalize.PropertyMetadata> Properties;
        internal readonly Column[] Columns;
        private readonly Column[] nestedColumns;
        internal readonly Column?[] PropertyColumns;
        internal readonly PropertyKey[] PropertyKeys;
        internal readonly PropertyKey[] SpillKeys;
        internal readonly NativeNormalize.PropertyMetadata[] Spills;
        internal readonly bool Eligible;
        /// <summary>Gets the number of leaf columns, nested groups included.</summary>
        internal readonly int LeafCount;
        internal Plan(Type type) : this(type, new HashSet<Type>()) { }
        private Plan(Type type, HashSet<Type> active)
        {
            Properties = NativeNormalize.GetCachedProperties(type);
            var columns = new List<Column>();
            var spills = new List<NativeNormalize.PropertyMetadata>();
            var spillKeys = new List<PropertyKey>();
            PropertyKeys = new PropertyKey[Properties.Count];
            for (var i = 0; i < Properties.Count; i++) PropertyKeys[i] = new PropertyKey(Properties[i].Name);
            Eligible = ExtensionDataMetadata.Find(type) == null && active.Add(type);
            if (Eligible)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (var propertyIndex = 0; propertyIndex < Properties.Count; propertyIndex++)
                {
                    var property = Properties[propertyIndex];
                    var key = PropertyKeys[propertyIndex];
                    if (!names.Add(property.Name)) { Eligible = false; break; }
                    var target = Nullable.GetUnderlyingType(property.Info.PropertyType) ?? property.Info.PropertyType;
                    if (Scalar(target)) columns.Add(new Column { Property = property, Key = key, Emit = CompileEmitter(property), SameToken = new Lazy<Func<object, object, bool>?>(() => CompileTokenComparer(property), System.Threading.LazyThreadSafetyMode.PublicationOnly) });
                    else if (NativeNormalize.IsPlainObjectType(target) && !active.Contains(target))
                    {
                        var child = new Plan(target, active);
                        if (child.Eligible && child.Columns.Length > 0 && child.Spills.Length == 0)
                            columns.Add(new Column { Property = property, Key = key, Nested = child });
                        else { spills.Add(property); spillKeys.Add(key); }
                    }
                    else { spills.Add(property); spillKeys.Add(key); }
                }
                active.Remove(type);
            }
            Columns = columns.ToArray();
            foreach (var column in Columns) LeafCount += column.Nested?.LeafCount ?? 1;
            nestedColumns = Columns.Where(column => column.Nested != null).ToArray();
            PropertyColumns = new Column?[Properties.Count];
            for (var i = 0; i < Properties.Count; i++)
                foreach (var column in Columns)
                    if (column.Property.Name == Properties[i].Name) { PropertyColumns[i] = column; break; }
            Spills = spills.ToArray();
            SpillKeys = spillKeys.ToArray();
        }
        internal bool Validate(object row)
        {
            foreach (var column in nestedColumns)
            {
                var child = column.Property.Getter(row);
                if (child == null || child.GetType() != column.Property.Info.PropertyType || !column.Nested!.Validate(child)) return false;
            }
            return true;
        }
        internal static bool Scalar(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(TimeSpan)
            || type == typeof(Uri) || type == typeof(Version)
#if NET6_0_OR_GREATER
            || type == typeof(DateOnly) || type == typeof(TimeOnly)
#endif
            ;
    }
}
