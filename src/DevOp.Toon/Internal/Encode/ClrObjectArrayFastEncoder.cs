#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Fast path encoder for typed CLR root objects and homogeneous CLR object arrays using cached reflection metadata and compiled field writers.
/// </summary>
internal static class ClrObjectArrayFastEncoder
{
    /// <summary>
    /// Emits a nested property directly to the line writer.
    /// </summary>
    private delegate void LineWriterPropertyEmitter(object instance, LineWriter writer, int depth, ResolvedEncodeOptions options);

    /// <summary>
    /// Emits a nested property directly to the pooled compact writer.
    /// </summary>
    private delegate void CompactWriterPropertyEmitter(object instance, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options);

    private static readonly ConcurrentDictionary<Type, RowLayoutMetadata> RowLayoutCache = new();
    private static readonly ConcurrentDictionary<Type, ObjectEmitters> PlainObjectEmitterCache = new();

    /// <summary>
    /// Attempts to encode a supported CLR object or homogeneous CLR object array directly to a string.
    /// </summary>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">Resolved encode options.</param>
    /// <param name="encoded">The encoded TOON text when the fast path succeeds.</param>
    /// <returns><see langword="true"/> when the value was handled by the fast path; otherwise, <see langword="false"/>.</returns>
    public static bool TryEncode(object? data, ResolvedEncodeOptions options, out string encoded)
    {
        if (!TryEncodeToCompactBuffer(data, options, out var compactWriter))
        {
            encoded = string.Empty;
            return false;
        }

        using (compactWriter)
        {
            var (buffer, length) = compactWriter.GetCharBuffer();
            encoded = new string(buffer, 0, length);
        }

        return true;
    }

    /// <summary>
    /// Encodes data to a pooled <see cref="CompactBufferWriter"/> so callers can avoid intermediate string allocation.
    /// </summary>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">Resolved encode options.</param>
    /// <param name="compactWriter">The rented writer containing encoded TOON when the fast path succeeds. The caller must dispose it.</param>
    /// <returns><see langword="true"/> when the value was handled by the fast path; otherwise, <see langword="false"/>.</returns>
    internal static bool TryEncodeToCompactBuffer(object? data, ResolvedEncodeOptions options, out CompactBufferWriter compactWriter)
    {
        compactWriter = null!;

        if (!TryCollectRows(data, out var rows, out var properties, out var elementType))
        {
            // Root plain object path
            if (data is not null and not string && NativeNormalize.IsPlainObjectType(data.GetType()))
            {
                return TryEncodeRootClrObjectToCompactBuffer(data, options, out compactWriter);
            }

            return false;
        }

        var rowLayout = GetRowLayout(elementType!, properties);
        var headerProperties = rowLayout.HeaderProperties;
        if (headerProperties.Length == 0 || rowLayout.HasExtensionEntries(rows) || rowLayout.HasBase64ByteColumns(options))
        {
            return false;
        }

        if (options.KeyFolding != ToonKeyFolding.Off)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (ContainsFoldCandidate(rows[i], options, 0))
                {
                    return false;
                }
            }
        }

        var isFullyTabular = headerProperties.Length == rowLayout.MemberCount;
        if (!isFullyTabular && (options.ObjectArrayLayout != ToonObjectArrayLayout.Columnar || rowLayout.HasScalarColumnMember(rows, options)))
        {
            return false;
        }

        // Build column suppression mask when IgnoreNullOrEmpty is enabled.
        // A column is suppressed when every row has null or empty-string for that field.
        bool[]? suppress = null;
        string[]? activeHeaderNames = null;
        if (options.IgnoreNullOrEmpty)
        {
            suppress = BuildSuppressionMask(rows, headerProperties);
            activeHeaderNames = BuildActiveHeaderNames(rowLayout.HeaderNames, suppress);
        }

        var writer = new CompactBufferWriter(options.Indent);
        try
        {
            writer.StartLine(0);
            if (activeHeaderNames != null)
                NativePrimitives.AppendHeader(writer, rows.Count, null, null, activeHeaderNames, options.Delimiter);
            else
                AppendCachedHeader(writer, rows.Count, null, null, rowLayout, options.Delimiter);

            var nonHeaderEmitters = isFullyTabular
                ? Array.Empty<CompactWriterPropertyEmitter>()
                : rowLayout.CompactNonHeaderEmitters;
            if (suppress != null)
            {
                for (int ri = 0; ri < rows.Count; ri++)
                {
                    var row = rows[ri]!;
                    writer.StartLine(1);
                    rowLayout.AppendHeaderRow(writer, row, options.Delimiter, suppress);
                    for (int i = 0; i < nonHeaderEmitters.Length; i++)
                    {
                        nonHeaderEmitters[i](row, writer, 2, options);
                    }
                }
            }
            else
            {
                for (int ri = 0; ri < rows.Count; ri++)
                {
                    var row = rows[ri]!;
                    writer.StartLine(1);
                    rowLayout.AppendHeaderRow(writer, row, options.Delimiter);
                    for (int i = 0; i < nonHeaderEmitters.Length; i++)
                    {
                        nonHeaderEmitters[i](row, writer, 2, options);
                    }
                }
            }
        }
        catch
        {
            writer.Dispose();
            throw;
        }

        compactWriter = writer;
        return true;
    }

    /// <summary>
    /// Attempts to encode data through the fast path and write the result to a text writer.
    /// </summary>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">Resolved encode options.</param>
    /// <param name="writer">The destination writer.</param>
    /// <returns><see langword="true"/> when the value was handled by the fast path; otherwise, <see langword="false"/>.</returns>
    public static bool TryWriteToTextWriter(object? data, ResolvedEncodeOptions options, TextWriter writer)
    {
        if (!TryEncodeToCompactBuffer(data, options, out var compactWriter))
        {
            return false;
        }

        using (compactWriter)
        {
            var (buffer, length) = compactWriter.GetCharBuffer();
            writer.Write(buffer, 0, length);
        }

        return true;
    }

    /// <summary>
    /// Attempts to encode data through the fast path and asynchronously write the result to a text writer.
    /// </summary>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">Resolved encode options.</param>
    /// <param name="writer">The destination writer.</param>
    /// <param name="cancellationToken">A token that can cancel before the write starts.</param>
    /// <returns><see langword="true"/> when the value was handled by the fast path; otherwise, <see langword="false"/>.</returns>
    public static async Task<bool> TryWriteToTextWriterAsync(object? data, ResolvedEncodeOptions options, TextWriter writer, CancellationToken cancellationToken)
    {
        if (!TryEncodeToCompactBuffer(data, options, out var compactWriter))
        {
            return false;
        }

        using (compactWriter)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (buffer, length) = compactWriter.GetCharBuffer();
            await writer.WriteAsync(buffer, 0, length).ConfigureAwait(false);
        }

        return true;
    }

    private static bool TryEncodeRootClrObjectToCompactBuffer(object data, ResolvedEncodeOptions options, out CompactBufferWriter compactWriter)
    {
        var properties = NativeNormalize.GetCachedProperties(data.GetType());
        if (properties.Count == 0)
        {
            compactWriter = null!;
            return false;
        }

        if (options.KeyFolding != ToonKeyFolding.Off && (HasKeyFoldableProperty(properties, data) || ContainsFoldCandidate(data, options, 0)))
        {
            compactWriter = null!;
            return false;
        }

        var objectEmitters = GetPlainObjectEmitters(data.GetType());
        if (objectEmitters.HasExtensionEntries(data))
        {
            compactWriter = null!;
            return false;
        }

        var writer = new CompactBufferWriter(options.Indent);
        try
        {
            var emitters = objectEmitters.Emitters;
            for (int i = 0; i < emitters.Length; i++)
            {
                emitters[i](data, writer, 0, options);
            }
        }
        catch
        {
            writer.Dispose();
            throw;
        }

        compactWriter = writer;
        return true;
    }

    /// <summary>
    /// Returns true if any property is a plain object with exactly one child property,
    /// meaning key folding could collapse it into a dotted key path (e.g. a.b.c: v),
    /// or holds extension data entries that could fold.
    /// </summary>
    private static bool HasKeyFoldableProperty(IReadOnlyList<NativeNormalize.PropertyMetadata> properties, object data)
    {
        for (int i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            if (property.NestedObjectProperties is { Length: 1 })
            {
                return true;
            }

            if (property.ValueDeclaresExtensionData &&
                property.Getter(data) is { } value &&
                GetPlainObjectEmitters(value.GetType()).HasExtensionEntries(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether key folding on the slow path could fold a key anywhere inside <paramref name="value"/>: some member,
    /// dictionary entry or list item member holds an object with exactly one member. The fast path does not fold below the
    /// root, so such values are encoded through normalization. Only called when key folding is enabled.
    /// </summary>
    private static bool ContainsFoldCandidate(object? value, ResolvedEncodeOptions options, int depth)
    {
        if (options.FlattenDepth < 2 || value is null || value is string || NativeNormalize.IsPrimitiveOrNull(value) || value is IEnumerable<byte>)
        {
            return false;
        }

        if (depth >= 64)
        {
            return true;
        }

        if (DictionaryEntries.Get(value) is { } entries)
        {
            foreach (var entry in entries)
            {
                if (IsSingleMemberObject(entry.Value) || ContainsFoldCandidate(entry.Value, options, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (value is IEnumerable sequence)
        {
            // Probing a lazy sequence would consume it before the encoder writes it; the slow path enumerates it once.
            if (sequence is not ICollection)
            {
                return true;
            }

            foreach (var item in sequence)
            {
                if (ContainsFoldCandidate(item, options, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (!NativeNormalize.IsPlainObjectType(value.GetType()))
        {
            return false;
        }

        if (GetPlainObjectEmitters(value.GetType()).HasExtensionEntries(value))
        {
            return true;
        }

        var properties = NativeNormalize.GetCachedProperties(value.GetType());
        for (int i = 0; i < properties.Count; i++)
        {
            var member = properties[i].Getter(value);
            if (IsSingleMemberObject(member) || ContainsFoldCandidate(member, options, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSingleMemberObject(object? value)
    {
        if (value is null || value is string || NativeNormalize.IsPrimitiveOrNull(value))
        {
            return false;
        }

        if (DictionaryEntries.Get(value) is { } entries)
        {
            var count = 0;
            foreach (var _ in entries)
            {
                if (++count > 1)
                {
                    return false;
                }
            }

            return count == 1;
        }

        return value is not IEnumerable && NativeNormalize.IsPlainObjectType(value.GetType()) &&
               NativeNormalize.GetCachedProperties(value.GetType()).Count == 1;
    }

    private static bool TryCollectRows(object? data, out List<object> rows, out IReadOnlyList<NativeNormalize.PropertyMetadata> properties, out Type? elementType)
    {
        properties = Array.Empty<NativeNormalize.PropertyMetadata>();
        elementType = null;

        // A dictionary is an object, not a table of its KeyValuePair entries.
        if (data is null or string || data is not IEnumerable enumerable || DictionaryEntries.IsDictionaryType(data.GetType()))
        {
            rows = new List<object>();
            return false;
        }

        var capacity = (enumerable as ICollection)?.Count ?? 0;
        rows = new List<object>(capacity);

        // Fast path: trust the declared element type for typed arrays and generic lists.
        // Avoids per-element GetType() + IsPlainObjectType() on every item.
        var declaredElementType = GetDeclaredElementType(enumerable);
        if (declaredElementType is not null && NativeNormalize.IsPlainObjectType(declaredElementType))
        {
            properties = NativeNormalize.GetCachedProperties(declaredElementType);
            if (properties.Count == 0)
            {
                return false;
            }

            elementType = declaredElementType;
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    return false;
                }

                rows.Add(item);
            }

            return rows.Count > 0;
        }

        // Slow path: runtime type check per element (handles IEnumerable<object>, non-generic, mixed)
        foreach (var item in enumerable)
        {
            if (item is null)
            {
                return false;
            }

            var runtimeType = item.GetType();
            if (!NativeNormalize.IsPlainObjectType(runtimeType))
            {
                return false;
            }

            if (elementType is null)
            {
                elementType = runtimeType;
                properties = NativeNormalize.GetCachedProperties(runtimeType);
                if (properties.Count == 0)
                {
                    return false;
                }
            }
            else if (runtimeType != elementType)
            {
                return false;
            }

            rows.Add(item);
        }

        return rows.Count > 0;
    }

    private static Type? GetDeclaredElementType(IEnumerable enumerable)
    {
        var type = enumerable.GetType();
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            return type.GetGenericArguments()[0];
        }

        return null;
    }

    // Typed field writer delegates — one per header property, avoid boxing value types.
    private delegate void StringBuilderFieldWriter(object row, StringBuilder builder, char delimiter);
    private delegate void CompactFieldWriter(object row, CompactBufferWriter writer, char delimiter);

    private static void EncodeClrNamedValue(string key, object? rawValue, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (TryEncodeByteSequenceAsBase64(NativePrimitives.EncodeKey(key), rawValue, writer, depth, options))
        {
            return;
        }

        if (NativeNormalize.IsPrimitiveOrNull(rawValue))
        {
            if (options.IgnoreNullOrEmpty && IsNullOrEmptyScalar(rawValue))
                return;
            var sb = writer.BeginLine(depth);
            sb.Append(NativePrimitives.EncodeKey(key));
            sb.Append(Constants.COLON);
            sb.Append(Constants.SPACE);
            NativePrimitives.AppendPrimitiveRaw(sb, rawValue, options.Delimiter);
            return;
        }

        if (DictionaryEntries.Get(rawValue) is { } dictionary)
        {
            EncodeDictionary(key, dictionary, writer, depth, options);
            return;
        }

        if (rawValue is IEnumerable enumerable and not string)
        {
            if (TryEncodeClrEnumerable(NativePrimitives.EncodeKey(key), key, enumerable, writer, depth, options))
            {
                return;
            }
        }

        if (rawValue is not null && NativeNormalize.IsPlainObjectType(rawValue.GetType()))
        {
            EncodeClrObject(key, rawValue, writer, depth, options);
            return;
        }

        NativeEncoders.EncodeNamedValue(key, rawValue, writer, depth, options);
    }

    private static void EncodeClrNamedValue(NativeNormalize.PropertyMetadata property, object instance, LineWriter writer, int depth, ResolvedEncodeOptions options)
        => EncodeClrPropertyValue(property, property.Getter(instance), writer, depth, options);

    private static void EncodeClrPropertyValue(NativeNormalize.PropertyMetadata property, object? rawValue, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (TryEncodeByteSequenceAsBase64(property.EncodedName, rawValue, writer, depth, options))
        {
            return;
        }

        if (NativeNormalize.IsPrimitiveOrNull(rawValue))
        {
            if (options.IgnoreNullOrEmpty && IsNullOrEmptyScalar(rawValue))
                return;
            var sb = writer.BeginLine(depth);
            sb.Append(property.EncodedName);
            sb.Append(Constants.COLON);
            sb.Append(Constants.SPACE);
            NativePrimitives.AppendPrimitiveRaw(sb, rawValue, options.Delimiter);
            return;
        }

        if (DictionaryEntries.Get(rawValue) is { } dictionary)
        {
            EncodeDictionary(property.EncodedName, property.Name, dictionary, writer, depth, options);
            return;
        }

        if (rawValue is IEnumerable enumerable and not string)
        {
            if (TryEncodeClrEnumerable(property, enumerable, writer, depth, options))
            {
                return;
            }
        }

        if (rawValue is not null && NativeNormalize.IsPlainObjectType(rawValue.GetType()))
        {
            if (TryEncodeCachedClrObject(property, rawValue, writer, depth, options))
            {
                return;
            }

            EncodeClrObject(property.EncodedName, property.Name, rawValue, writer, depth, options);
            return;
        }

        NativeEncoders.EncodeNamedValue(property.Name, rawValue, writer, depth, options);
    }

    private static void EncodeClrNamedValue(string key, object? rawValue, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (TryEncodeByteSequenceAsBase64(NativePrimitives.EncodeKey(key), rawValue, writer, depth, options))
        {
            return;
        }

        if (NativeNormalize.IsPrimitiveOrNull(rawValue))
        {
            if (options.IgnoreNullOrEmpty && IsNullOrEmptyScalar(rawValue))
                return;
            writer.StartLine(depth);
            writer.Append(NativePrimitives.EncodeKey(key));
            writer.Append(Constants.COLON);
            writer.Append(Constants.SPACE);
            NativePrimitives.AppendPrimitiveRaw(writer, rawValue, options.Delimiter);
            return;
        }

        if (DictionaryEntries.Get(rawValue) is { } dictionary)
        {
            EncodeDictionary(key, dictionary, writer, depth, options);
            return;
        }

        if (rawValue is IEnumerable enumerable and not string && TryEncodeClrEnumerable(NativePrimitives.EncodeKey(key), key, enumerable, writer, depth, options))
        {
            return;
        }

        if (rawValue is not null && NativeNormalize.IsPlainObjectType(rawValue.GetType()))
        {
            EncodeClrObject(key, rawValue, writer, depth, options);
            return;
        }

        writer.PushRawBlock(NativeEncoders.EncodeNamedValue(key, rawValue, depth, options));
    }

    private static void EncodeClrNamedValue(NativeNormalize.PropertyMetadata property, object instance, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
        => EncodeClrPropertyValue(property, property.Getter(instance), writer, depth, options);

    private static void EncodeClrPropertyValue(NativeNormalize.PropertyMetadata property, object? rawValue, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (TryEncodeByteSequenceAsBase64(property.EncodedName, rawValue, writer, depth, options))
        {
            return;
        }

        if (NativeNormalize.IsPrimitiveOrNull(rawValue))
        {
            if (options.IgnoreNullOrEmpty && IsNullOrEmptyScalar(rawValue))
                return;
            writer.StartLine(depth);
            writer.Append(property.EncodedName);
            writer.Append(Constants.COLON);
            writer.Append(Constants.SPACE);
            NativePrimitives.AppendPrimitiveRaw(writer, rawValue, options.Delimiter);
            return;
        }

        if (DictionaryEntries.Get(rawValue) is { } dictionary)
        {
            EncodeDictionary(property.EncodedName, property.Name, dictionary, writer, depth, options);
            return;
        }

        if (rawValue is IEnumerable enumerable and not string && TryEncodeClrEnumerable(property, enumerable, writer, depth, options))
        {
            return;
        }

        if (rawValue is not null && NativeNormalize.IsPlainObjectType(rawValue.GetType()))
        {
            if (TryEncodeCachedClrObject(property, rawValue, writer, depth, options))
            {
                return;
            }

            EncodeClrObject(property.EncodedName, property.Name, rawValue, writer, depth, options);
            return;
        }

        writer.PushRawBlock(NativeEncoders.EncodeNamedValue(property.Name, rawValue, depth, options));
    }

    private static void EncodeClrObject(string key, object value, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        EncodeClrObject(NativePrimitives.EncodeKey(key), key, value, writer, depth, options);
    }

    private static void EncodeClrObject(string encodedKey, string key, object value, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (GetPlainObjectEmitters(value.GetType()).ExtensionGetter != null)
        {
            NativeEncoders.EncodeExtensionDataValue(key, value, writer, depth, options);
            return;
        }

        var properties = NativeNormalize.GetCachedProperties(value.GetType());
        EncodeClrObject(encodedKey, properties, value, writer, depth, options);
    }

    private static void EncodeClrObject(string encodedKey, IReadOnlyList<NativeNormalize.PropertyMetadata> properties, object value, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        var sb = writer.BeginLine(depth);
        sb.Append(encodedKey);
        sb.Append(Constants.COLON);
        for (int i = 0; i < properties.Count; i++)
        {
            EncodeClrNamedValue(properties[i], value, writer, depth + 1, options);
        }
    }

    private static void EncodeClrObject(string key, object value, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        EncodeClrObject(NativePrimitives.EncodeKey(key), key, value, writer, depth, options);
    }

    private static void EncodeClrObject(string encodedKey, string key, object value, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        var properties = NativeNormalize.GetCachedProperties(value.GetType());
        EncodeClrObject(encodedKey, key, properties, value, writer, depth, options);
    }

    private static ObjectEmitters GetPlainObjectEmitters(Type type)
        => PlainObjectEmitterCache.GetOrAdd(type, static t => BuildPlainObjectEmitters(t));

    private static ObjectEmitters BuildPlainObjectEmitters(Type type)
    {
        var extension = ExtensionDataMetadata.Find(type);
        var properties = NativeNormalize.GetCachedProperties(type);
        var emitters = new List<CompactWriterPropertyEmitter>(properties.Count);
        Func<object, object?>? extensionGetter = null;
        for (int i = 0; i < properties.Count; i++)
        {
            if (extension != null && ExtensionDataMetadata.IsExtension(properties[i].Info, extension))
            {
                extensionGetter = properties[i].Getter;
                continue;
            }

            emitters.Add(BuildPlainObjectPropertyEmitter(properties[i]));
        }
        return new ObjectEmitters(emitters.ToArray(), extensionGetter);
    }

    private static CompactWriterPropertyEmitter BuildPlainObjectPropertyEmitter(NativeNormalize.PropertyMetadata property)
    {
        if (property.EnumerableElementType == typeof(byte))
        {
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (TryEncodeByteSequenceAsBase64(property.EncodedName, rawValue, writer, depth, options))
                {
                    return;
                }

                EncodeClrPropertyValue(property, rawValue, writer, depth, options);
            };
        }

        // Primitive: use typed field writer to avoid boxing value types (int, bool, decimal, etc.)
        if (NativeNormalize.IsPrimitiveClrType(property.Info.PropertyType))
        {
            var (_, compactFieldWriter) = BuildTypedFieldWriter(property);
            var encodedName = property.EncodedName;
            var isNullOrEmpty = CompileNullOrEmptyCheck(property.Info);
            if (isNullOrEmpty is null)
            {
                return (instance, writer, depth, options) =>
                {
                    writer.StartLine(depth);
                    writer.Append(encodedName);
                    writer.Append(Constants.COLON);
                    writer.Append(Constants.SPACE);
                    compactFieldWriter(instance, writer, options.Delimiter);
                };
            }

            return (instance, writer, depth, options) =>
            {
                if (options.IgnoreNullOrEmpty && isNullOrEmpty(instance))
                {
                    return;
                }

                writer.StartLine(depth);
                writer.Append(encodedName);
                writer.Append(Constants.COLON);
                writer.Append(Constants.SPACE);
                compactFieldWriter(instance, writer, options.Delimiter);
            };
        }

        // Dictionary
        if (DictionaryEntries.IsDictionaryType(property.Info.PropertyType))
        {
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (DictionaryEntries.Get(rawValue) is { } dictionary)
                {
                    EncodeDictionary(property.EncodedName, property.Name, dictionary, writer, depth, options);
                    return;
                }
                EncodeClrPropertyValue(property, rawValue, writer, depth, options);
            };
        }

        // Enumerable (array, list, etc.)
        if (property.EnumerableElementType is not null && property.Info.PropertyType != typeof(string))
        {
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (rawValue is IEnumerable enumerable and not string
                    && TryEncodeClrEnumerable(property, enumerable, writer, depth, options))
                {
                    return;
                }
                EncodeClrPropertyValue(property, rawValue, writer, depth, options);
            };
        }

        // Nested plain object with cached properties — pre-bake emitters and encoded key to skip
        // GetType() + ConcurrentDictionary lookup on every encode call.
        if (property.NestedObjectProperties is not null && !property.ValueDeclaresExtensionData)
        {
            var nestedType = property.Info.PropertyType;
            var prebuiltEmitters = GetPlainObjectEmitters(nestedType).Emitters;
            var encodedName = property.EncodedName;
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (rawValue is null || rawValue.GetType() != nestedType)
                {
                    EncodeClrPropertyValue(property, rawValue, writer, depth, options);
                    return;
                }
                writer.StartLine(depth);
                writer.Append(encodedName);
                writer.Append(Constants.COLON);
                for (int i = 0; i < prebuiltEmitters.Length; i++)
                {
                    prebuiltEmitters[i](rawValue, writer, depth + 1, options);
                }
            };
        }

        // Fallback
        return (instance, writer, depth, options) => EncodeClrPropertyValue(property, property.Getter(instance), writer, depth, options);
    }

    private static void EncodeClrObject(string encodedKey, string key, IReadOnlyList<NativeNormalize.PropertyMetadata> properties, object value, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        var objectEmitters = GetPlainObjectEmitters(value.GetType());
        if (objectEmitters.HasExtensionEntries(value))
        {
            writer.PushRawBlock(NativeEncoders.EncodeExtensionDataValue(key, value, depth, options));
            return;
        }

        writer.StartLine(depth);
        writer.Append(encodedKey);
        writer.Append(Constants.COLON);
        var emitters = objectEmitters.Emitters;
        for (int i = 0; i < emitters.Length; i++)
        {
            emitters[i](value, writer, depth + 1, options);
        }
    }

    private static void EncodeDictionary(string key, IEnumerable<DictionaryEntry> dictionary, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        EncodeDictionary(NativePrimitives.EncodeKey(key), key, dictionary, writer, depth, options);
    }

    private static void EncodeDictionary(string encodedKey, string key, IEnumerable<DictionaryEntry> dictionary, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        var sb = writer.BeginLine(depth);
        sb.Append(encodedKey);
        sb.Append(Constants.COLON);
        foreach (var entry in dictionary)
        {
            EncodeClrNamedValue(entry.Key?.ToString() ?? string.Empty, entry.Value, writer, depth + 1, options);
        }
    }

    private static void EncodeDictionary(string key, IEnumerable<DictionaryEntry> dictionary, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        EncodeDictionary(NativePrimitives.EncodeKey(key), key, dictionary, writer, depth, options);
    }

    private static void EncodeDictionary(string encodedKey, string key, IEnumerable<DictionaryEntry> dictionary, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        writer.StartLine(depth);
        writer.Append(encodedKey);
        writer.Append(Constants.COLON);
        foreach (var entry in dictionary)
        {
            EncodeClrNamedValue(entry.Key?.ToString() ?? string.Empty, entry.Value, writer, depth + 1, options);
        }
    }

    private static bool TryEncodeClrEnumerable(string? key, IEnumerable enumerable, LineWriter writer, int depth, ResolvedEncodeOptions options)
        => TryEncodeClrEnumerable(null, key, enumerable, writer, depth, options);

    private static bool TryEncodeClrEnumerable(NativeNormalize.PropertyMetadata property, IEnumerable enumerable, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (DictionaryEntries.IsDictionaryType(enumerable.GetType()))
        {
            return false;
        }

        if (TryWriteEmptyEnumerableHeader(property, enumerable, writer, depth, options))
        {
            return true;
        }

        return TryEncodeClrEnumerable(property.EncodedName, property.Name, property.EnumerableElementType, enumerable, writer, depth, options);
    }

    private static bool TryEncodeClrEnumerable(string? encodedKey, string? key, IEnumerable enumerable, LineWriter writer, int depth, ResolvedEncodeOptions options)
        => TryEncodeClrEnumerable(encodedKey, key, null, enumerable, writer, depth, options);

    private static bool TryEncodeClrEnumerable(string? encodedKey, string? key, Type? declaredElementType, IEnumerable enumerable, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (DictionaryEntries.IsDictionaryType(enumerable.GetType()))
        {
            return false;
        }

        if (TryWriteEmptyEnumerableHeader(enumerable, encodedKey, key, writer, depth, options))
        {
            return true;
        }

        var items = GetEnumerableItems(enumerable);

        if (items.Count == 0)
        {
            if (options.ExcludeEmptyArrays)
                return true;
            NativePrimitives.AppendHeader(writer.BeginLine(depth), 0, encodedKey, key, null, options.Delimiter);
            return true;
        }

        if (TryEncodePrimitiveArray(encodedKey, key, items, writer, depth, options))
        {
            return true;
        }

        if (TryCollectRows(enumerable, items, declaredElementType, out var properties, out var elementType))
        {
            var rowLayout = GetRowLayout(elementType!, properties);
            if (rowLayout.HasBase64ByteColumns(options))
            {
                return false;
            }

            if (rowLayout.HasExtensionEntries(items))
            {
                if (key is null)
                {
                    return false;
                }

                NativeEncoders.EncodeExtensionDataValue(key, enumerable, writer, depth, options);
                return true;
            }

            var headerProperties = rowLayout.HeaderProperties;
            if (headerProperties.Length == rowLayout.MemberCount)
            {
                WriteClrObjectRows(encodedKey, key, items, rowLayout, writer, depth, options, columnar: false);
                return true;
            }

            if (headerProperties.Length > 0 && options.ObjectArrayLayout == ToonObjectArrayLayout.Columnar &&
                !rowLayout.HasScalarColumnMember(items, options))
            {
                WriteClrObjectRows(encodedKey, key, items, rowLayout, writer, depth, options, columnar: true);
                return true;
            }
        }

        return false;
    }

    private static bool TryEncodeClrEnumerable(string? key, IEnumerable enumerable, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
        => TryEncodeClrEnumerable(null, key, enumerable, writer, depth, options);

    private static bool TryEncodeClrEnumerable(NativeNormalize.PropertyMetadata property, IEnumerable enumerable, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (DictionaryEntries.IsDictionaryType(enumerable.GetType()))
        {
            return false;
        }

        if (TryWriteEmptyEnumerableHeader(property, enumerable, writer, depth, options))
        {
            return true;
        }

        return TryEncodeClrEnumerable(property.EncodedName, property.Name, property.EnumerableElementType, enumerable, writer, depth, options);
    }

    private static bool TryEncodeClrEnumerable(string? encodedKey, string? key, IEnumerable enumerable, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
        => TryEncodeClrEnumerable(encodedKey, key, null, enumerable, writer, depth, options);

    private static bool TryEncodeClrEnumerable(string? encodedKey, string? key, Type? declaredElementType, IEnumerable enumerable, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (DictionaryEntries.IsDictionaryType(enumerable.GetType()))
        {
            return false;
        }

        if (TryWriteEmptyEnumerableHeader(enumerable, encodedKey, key, writer, depth, options))
        {
            return true;
        }

        var items = GetEnumerableItems(enumerable);

        if (items.Count == 0)
        {
            if (options.ExcludeEmptyArrays)
                return true;
            writer.StartLine(depth);
            NativePrimitives.AppendHeader(writer, 0, encodedKey, key, null, options.Delimiter);
            return true;
        }

        if (TryEncodePrimitiveArray(encodedKey, key, items, writer, depth, options))
        {
            return true;
        }

        if (TryCollectRows(enumerable, items, declaredElementType, out var properties, out var elementType))
        {
            var rowLayout = GetRowLayout(elementType!, properties);
            if (rowLayout.HasBase64ByteColumns(options))
            {
                return false;
            }

            if (rowLayout.HasExtensionEntries(items))
            {
                if (key is null)
                {
                    return false;
                }

                writer.PushRawBlock(NativeEncoders.EncodeExtensionDataValue(key, enumerable, depth, options));
                return true;
            }

            var headerProperties = rowLayout.HeaderProperties;
            if (headerProperties.Length == rowLayout.MemberCount)
            {
                WriteClrObjectRows(encodedKey, key, items, rowLayout, writer, depth, options, columnar: false);
                return true;
            }

            if (headerProperties.Length > 0 && options.ObjectArrayLayout == ToonObjectArrayLayout.Columnar &&
                !rowLayout.HasScalarColumnMember(items, options))
            {
                WriteClrObjectRows(encodedKey, key, items, rowLayout, writer, depth, options, columnar: true);
                return true;
            }
        }

        return false;
    }

    private static bool TryCollectRows(
        IList items,
        out IReadOnlyList<NativeNormalize.PropertyMetadata> properties,
        out Type? elementType)
        => TryCollectRows(null, items, null, out properties, out elementType);

    private static bool TryWriteEmptyEnumerableHeader(IEnumerable enumerable, string? encodedKey, string? key, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (enumerable is not ICollection collection || collection.Count != 0)
        {
            return false;
        }

        if (options.ExcludeEmptyArrays)
            return true;

        NativePrimitives.AppendHeader(writer.BeginLine(depth), 0, encodedKey, key, null, options.Delimiter);
        return true;
    }

    private static bool TryWriteEmptyEnumerableHeader(NativeNormalize.PropertyMetadata property, IEnumerable enumerable, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (enumerable is not ICollection collection || collection.Count != 0)
        {
            return false;
        }

        if (options.ExcludeEmptyArrays)
            return true;

        if (options.Delimiter == Constants.DEFAULT_DELIMITER_CHAR && property.CachedEmptyEnumerableHeader is not null)
        {
            writer.Push(depth, property.CachedEmptyEnumerableHeader);
            return true;
        }

        NativePrimitives.AppendHeader(writer.BeginLine(depth), 0, property.EncodedName, property.Name, null, options.Delimiter);
        return true;
    }

    private static bool TryWriteEmptyEnumerableHeader(IEnumerable enumerable, string? encodedKey, string? key, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (enumerable is not ICollection collection || collection.Count != 0)
        {
            return false;
        }

        if (options.ExcludeEmptyArrays)
            return true;

        writer.StartLine(depth);
        NativePrimitives.AppendHeader(writer, 0, encodedKey, key, null, options.Delimiter);
        return true;
    }

    private static bool TryWriteEmptyEnumerableHeader(NativeNormalize.PropertyMetadata property, IEnumerable enumerable, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (enumerable is not ICollection collection || collection.Count != 0)
        {
            return false;
        }

        if (options.ExcludeEmptyArrays)
            return true;

        if (options.Delimiter == Constants.DEFAULT_DELIMITER_CHAR && property.CachedEmptyEnumerableHeader is not null)
        {
            writer.Push(depth, property.CachedEmptyEnumerableHeader);
            return true;
        }

        writer.StartLine(depth);
        NativePrimitives.AppendHeader(writer, 0, property.EncodedName, property.Name, null, options.Delimiter);
        return true;
    }

    private static bool TryCollectRows(
        IEnumerable? enumerable,
        IList items,
        Type? declaredElementType,
        out IReadOnlyList<NativeNormalize.PropertyMetadata> properties,
        out Type? elementType)
    {
        properties = Array.Empty<NativeNormalize.PropertyMetadata>();
        elementType = declaredElementType;

        if (declaredElementType is not null)
        {
            if (!NativeNormalize.IsPlainObjectType(declaredElementType))
            {
                elementType = null;
                return false;
            }

            properties = NativeNormalize.GetCachedProperties(declaredElementType);
            if (properties.Count == 0)
            {
                elementType = null;
                return false;
            }
        }

        var trustDeclaredElementType = declaredElementType is not null && CanTrustDeclaredElementType(enumerable, declaredElementType);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                return false;
            }

            if (trustDeclaredElementType)
            {
                continue;
            }

            var runtimeType = item.GetType();
            if (!NativeNormalize.IsPlainObjectType(runtimeType))
            {
                return false;
            }

            if (elementType is null)
            {
                elementType = runtimeType;
                properties = NativeNormalize.GetCachedProperties(runtimeType);
                if (properties.Count == 0)
                {
                    return false;
                }
            }
            else if (runtimeType != elementType)
            {
                return false;
            }
        }

        return items.Count > 0;
    }

    // Cache of element type → expected List<T> type to avoid GetGenericArguments() allocation on every call.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Type> ExpectedListTypeCache = new();

    private static bool CanTrustDeclaredElementType(IEnumerable? enumerable, Type declaredElementType)
    {
        if (enumerable is null)
        {
            return false;
        }

        var enumerableType = enumerable.GetType();
        if (enumerableType.IsArray)
        {
            return enumerableType.GetElementType() == declaredElementType;
        }

        if (!enumerableType.IsGenericType)
        {
            return false;
        }

        // Compare the concrete type directly instead of calling GetGenericArguments() per call.
        // GetGenericArguments() allocates a new Type[] on every invocation.
        var expectedListType = ExpectedListTypeCache.GetOrAdd(
            declaredElementType,
            static t => typeof(List<>).MakeGenericType(t));
        return enumerableType == expectedListType;
    }



    private static bool TryEncodePrimitiveArray(string? key, IList items, LineWriter writer, int depth, ResolvedEncodeOptions options)
        => TryEncodePrimitiveArray(null, key, items, writer, depth, options);

    private static bool TryEncodePrimitiveArray(string? encodedKey, string? key, IList items, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (!NativeNormalize.IsPrimitiveOrNull(items[i]))
            {
                return false;
            }
        }
        var sb = writer.BeginLine(depth);
        NativePrimitives.AppendHeader(sb, items.Count, encodedKey, key, null, options.Delimiter);
        sb.Append(Constants.SPACE);
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(options.Delimiter);
            }

            NativePrimitives.AppendPrimitiveRaw(sb, items[i], options.Delimiter);
        }
        return true;
    }

    private static bool TryEncodePrimitiveArray(string? key, IList items, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
        => TryEncodePrimitiveArray(null, key, items, writer, depth, options);

    private static bool TryEncodePrimitiveArray(string? encodedKey, string? key, IList items, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (!NativeNormalize.IsPrimitiveOrNull(items[i]))
            {
                return false;
            }
        }

        writer.StartLine(depth);
        NativePrimitives.AppendHeader(writer, items.Count, encodedKey, key, null, options.Delimiter);
        writer.Append(Constants.SPACE);
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                writer.Append(options.Delimiter);
            }

            NativePrimitives.AppendPrimitiveRaw(writer, items[i], options.Delimiter);
        }
        return true;
    }

    private static void WriteClrObjectRows(
        string? key,
        IList rows,
        RowLayoutMetadata rowLayout,
        LineWriter writer,
        int depth,
        ResolvedEncodeOptions options,
        bool columnar)
        => WriteClrObjectRows(null, key, rows, rowLayout, writer, depth, options, columnar);

    private static void WriteClrObjectRows(
        string? encodedKey,
        string? key,
        IList rows,
        RowLayoutMetadata rowLayout,
        LineWriter writer,
        int depth,
        ResolvedEncodeOptions options,
        bool columnar)
    {
        bool[]? suppress = null;
        string[]? activeHeaderNames = null;
        if (options.IgnoreNullOrEmpty)
        {
            suppress = BuildSuppressionMask(rows, rowLayout.HeaderProperties);
            activeHeaderNames = BuildActiveHeaderNames(rowLayout.HeaderNames, suppress);
        }

        if (activeHeaderNames != null)
            NativePrimitives.AppendHeader(writer.BeginLine(depth), rows.Count, encodedKey, key, activeHeaderNames, options.Delimiter);
        else
            AppendCachedHeader(writer.BeginLine(depth), rows.Count, encodedKey, key, rowLayout, options.Delimiter);

        var nonHeaderEmitters = columnar
            ? rowLayout.LineWriterNonHeaderEmitters
            : Array.Empty<LineWriterPropertyEmitter>();
        if (suppress != null)
        {
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var row = rows[ri]!;
                rowLayout.AppendHeaderRow(writer.BeginLine(depth + 1), row, options.Delimiter, suppress);
                for (int i = 0; i < nonHeaderEmitters.Length; i++)
                {
                    nonHeaderEmitters[i](row, writer, depth + 2, options);
                }
            }
        }
        else
        {
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var row = rows[ri]!;
                rowLayout.AppendHeaderRow(writer.BeginLine(depth + 1), row, options.Delimiter);
                for (int i = 0; i < nonHeaderEmitters.Length; i++)
                {
                    nonHeaderEmitters[i](row, writer, depth + 2, options);
                }
            }
        }
    }

    private static void WriteClrObjectRows(
        string? key,
        IList rows,
        RowLayoutMetadata rowLayout,
        CompactBufferWriter writer,
        int depth,
        ResolvedEncodeOptions options,
        bool columnar)
        => WriteClrObjectRows(null, key, rows, rowLayout, writer, depth, options, columnar);

    private static void WriteClrObjectRows(
        string? encodedKey,
        string? key,
        IList rows,
        RowLayoutMetadata rowLayout,
        CompactBufferWriter writer,
        int depth,
        ResolvedEncodeOptions options,
        bool columnar)
    {
        bool[]? suppress = null;
        string[]? activeHeaderNames = null;
        if (options.IgnoreNullOrEmpty)
        {
            suppress = BuildSuppressionMask(rows, rowLayout.HeaderProperties);
            activeHeaderNames = BuildActiveHeaderNames(rowLayout.HeaderNames, suppress);
        }

        writer.StartLine(depth);
        NativePrimitives.AppendHeader(writer, rows.Count, encodedKey, key, activeHeaderNames ?? rowLayout.HeaderNames, options.Delimiter);
        var nonHeaderEmitters = columnar
            ? rowLayout.CompactNonHeaderEmitters
            : Array.Empty<CompactWriterPropertyEmitter>();
        if (suppress != null)
        {
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var row = rows[ri]!;
                writer.StartLine(depth + 1);
                rowLayout.AppendHeaderRow(writer, row, options.Delimiter, suppress);
                for (int i = 0; i < nonHeaderEmitters.Length; i++)
                {
                    nonHeaderEmitters[i](row, writer, depth + 2, options);
                }
            }
        }
        else
        {
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var row = rows[ri]!;
                writer.StartLine(depth + 1);
                rowLayout.AppendHeaderRow(writer, row, options.Delimiter);
                for (int i = 0; i < nonHeaderEmitters.Length; i++)
                {
                    nonHeaderEmitters[i](row, writer, depth + 2, options);
                }
            }
        }
    }

    private static IList GetEnumerableItems(IEnumerable enumerable)
    {
        if (enumerable is IList list)
        {
            return list;
        }

        var items = new List<object?>();
        foreach (var item in enumerable)
        {
            items.Add(item);
        }

        return items;
    }

    private static bool ShouldUseBufferedWriter(ResolvedEncodeOptions options) => false;

    private static RowLayoutMetadata GetRowLayout(Type elementType, IReadOnlyList<NativeNormalize.PropertyMetadata> properties)
    {
        return RowLayoutCache.GetOrAdd(elementType, type => BuildRowLayout(properties, ExtensionDataMetadata.Find(type)));
    }

    private static RowLayoutMetadata BuildRowLayout(IReadOnlyList<NativeNormalize.PropertyMetadata> properties, PropertyInfo? extension)
    {
        Func<object, object?>? extensionGetter = null;
        if (extension != null)
        {
            var members = new List<NativeNormalize.PropertyMetadata>(properties.Count);
            for (int i = 0; i < properties.Count; i++)
            {
                if (ExtensionDataMetadata.IsExtension(properties[i].Info, extension))
                {
                    extensionGetter = properties[i].Getter;
                    continue;
                }

                members.Add(properties[i]);
            }

            properties = members;
        }

        var header = new List<NativeNormalize.PropertyMetadata>(properties.Count);
        for (int i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            if (!NativeNormalize.IsPrimitiveClrType(property.Info.PropertyType))
            {
                continue;
            }

            header.Add(property);
        }

        var headerProperties = header.ToArray();
        var headerNames = new string[headerProperties.Length];
        for (int i = 0; i < headerProperties.Length; i++)
        {
            headerNames[i] = headerProperties[i].Name;
        }

        var nonHeaderProperties = GetNonHeaderProperties(properties, header);

        var (sbFieldWriters, compactFieldWriters) = BuildTypedFieldWriters(headerProperties);

        return new RowLayoutMetadata(
            headerProperties,
            headerNames,
            BuildCachedHeaderFieldBlock(headerProperties),
            sbFieldWriters,
            compactFieldWriters,
            nonHeaderProperties,
            BuildLineWriterNonHeaderEmitters(nonHeaderProperties),
            BuildCompactNonHeaderEmitters(nonHeaderProperties),
            properties.Count,
            extensionGetter);
    }

    private static void WriteBufferedRows(
        IReadOnlyList<object> rows,
        IReadOnlyList<NativeNormalize.PropertyMetadata> properties,
        IReadOnlyList<NativeNormalize.PropertyMetadata> headerProperties,
        CompactBufferWriter writer,
        ResolvedEncodeOptions options,
        bool isFullyTabular)
    {
        var rowLayout = BuildRowLayout(properties, extension: null);
        writer.StartLine(0);
        NativePrimitives.AppendHeader(writer, rows.Count, null, rowLayout.HeaderNames, options.Delimiter);
        var nonHeaderEmitters = isFullyTabular
            ? Array.Empty<CompactWriterPropertyEmitter>()
            : rowLayout.CompactNonHeaderEmitters;
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var row = rows[ri];
            writer.StartLine(1);
            rowLayout.AppendHeaderRow(writer, row, options.Delimiter);
            for (int i = 0; i < nonHeaderEmitters.Length; i++)
            {
                nonHeaderEmitters[i](row, writer, 2, options);
            }
        }
    }

    private static LineWriterPropertyEmitter[] BuildLineWriterNonHeaderEmitters(
        IReadOnlyList<NativeNormalize.PropertyMetadata> nonHeaderProperties)
    {
        var emitters = new LineWriterPropertyEmitter[nonHeaderProperties.Count];
        for (int i = 0; i < nonHeaderProperties.Count; i++)
        {
            emitters[i] = BuildLineWriterNonHeaderEmitter(nonHeaderProperties[i]);
        }

        return emitters;
    }

    private static bool TryEncodeByteSequenceAsBase64(string encodedKey, object? rawValue, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (options.ByteArrayFormat != ToonByteArrayFormat.Base64String ||
            !ByteSequenceText.TryToBase64String(rawValue, out var base64))
        {
            return false;
        }

        if (options.IgnoreNullOrEmpty && string.IsNullOrEmpty(base64))
        {
            return true;
        }

        var sb = writer.BeginLine(depth);
        sb.Append(encodedKey);
        sb.Append(Constants.COLON);
        sb.Append(Constants.SPACE);
        NativePrimitives.AppendPrimitiveRaw(sb, base64, options.Delimiter);
        return true;
    }

    private static bool TryEncodeByteSequenceAsBase64(string encodedKey, object? rawValue, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        if (options.ByteArrayFormat != ToonByteArrayFormat.Base64String ||
            !ByteSequenceText.TryToBase64String(rawValue, out var base64))
        {
            return false;
        }

        if (options.IgnoreNullOrEmpty && string.IsNullOrEmpty(base64))
        {
            return true;
        }

        writer.StartLine(depth);
        writer.Append(encodedKey);
        writer.Append(Constants.COLON);
        writer.Append(Constants.SPACE);
        NativePrimitives.AppendPrimitiveRaw(writer, base64, options.Delimiter);
        return true;
    }

    private static CompactWriterPropertyEmitter[] BuildCompactNonHeaderEmitters(
        IReadOnlyList<NativeNormalize.PropertyMetadata> nonHeaderProperties)
    {
        var emitters = new CompactWriterPropertyEmitter[nonHeaderProperties.Count];
        for (int i = 0; i < nonHeaderProperties.Count; i++)
        {
            emitters[i] = BuildCompactNonHeaderEmitter(nonHeaderProperties[i]);
        }

        return emitters;
    }

    private static LineWriterPropertyEmitter BuildLineWriterNonHeaderEmitter(NativeNormalize.PropertyMetadata property)
    {
        if (DictionaryEntries.IsDictionaryType(property.Info.PropertyType))
        {
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (DictionaryEntries.Get(rawValue) is { } dictionary)
                {
                    EncodeDictionary(property.EncodedName, property.Name, dictionary, writer, depth, options);
                    return;
                }

                EncodeClrPropertyValue(property, rawValue, writer, depth, options);
            };
        }

        if (property.EnumerableElementType is not null && property.Info.PropertyType != typeof(string))
        {
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (rawValue is IEnumerable enumerable and not string
                    && TryEncodeClrEnumerable(property, enumerable, writer, depth, options))
                {
                    return;
                }

                EncodeClrPropertyValue(property, rawValue, writer, depth, options);
            };
        }

        if (property.NestedObjectProperties is not null)
        {
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (rawValue is not null && TryEncodeCachedClrObject(property, rawValue, writer, depth, options))
                {
                    return;
                }

                EncodeClrPropertyValue(property, rawValue, writer, depth, options);
            };
        }

        return (instance, writer, depth, options) => EncodeClrPropertyValue(property, property.Getter(instance), writer, depth, options);
    }

    private static CompactWriterPropertyEmitter BuildCompactNonHeaderEmitter(NativeNormalize.PropertyMetadata property)
    {
        if (DictionaryEntries.IsDictionaryType(property.Info.PropertyType))
        {
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (DictionaryEntries.Get(rawValue) is { } dictionary)
                {
                    EncodeDictionary(property.EncodedName, property.Name, dictionary, writer, depth, options);
                    return;
                }

                EncodeClrPropertyValue(property, rawValue, writer, depth, options);
            };
        }

        if (property.EnumerableElementType is not null && property.Info.PropertyType != typeof(string))
        {
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (rawValue is IEnumerable enumerable and not string
                    && TryEncodeClrEnumerable(property, enumerable, writer, depth, options))
                {
                    return;
                }

                EncodeClrPropertyValue(property, rawValue, writer, depth, options);
            };
        }

        if (property.NestedObjectProperties is not null && !property.ValueDeclaresExtensionData)
        {
            var nestedType = property.Info.PropertyType;
            var prebuiltEmitters = GetPlainObjectEmitters(nestedType).Emitters;
            var encodedName = property.EncodedName;
            return (instance, writer, depth, options) =>
            {
                var rawValue = property.Getter(instance);
                if (rawValue is null || rawValue.GetType() != nestedType)
                {
                    EncodeClrPropertyValue(property, rawValue, writer, depth, options);
                    return;
                }
                writer.StartLine(depth);
                writer.Append(encodedName);
                writer.Append(Constants.COLON);
                for (int i = 0; i < prebuiltEmitters.Length; i++)
                {
                    prebuiltEmitters[i](rawValue, writer, depth + 1, options);
                }
            };
        }

        return (instance, writer, depth, options) => EncodeClrPropertyValue(property, property.Getter(instance), writer, depth, options);
    }

    private static NativeNormalize.PropertyMetadata[] GetNonHeaderProperties(
        IReadOnlyList<NativeNormalize.PropertyMetadata> properties,
        IReadOnlyList<NativeNormalize.PropertyMetadata> headerProperties)
    {
        if (headerProperties.Count == 0 || headerProperties.Count == properties.Count)
        {
            return Array.Empty<NativeNormalize.PropertyMetadata>();
        }

        var headerNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < headerProperties.Count; i++)
        {
            headerNames.Add(headerProperties[i].Name);
        }

        var nonHeaderProperties = new List<NativeNormalize.PropertyMetadata>(properties.Count - headerProperties.Count);
        for (int i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            if (!headerNames.Contains(property.Name))
            {
                nonHeaderProperties.Add(property);
            }
        }

        return nonHeaderProperties.ToArray();
    }

    private static string? BuildCachedHeaderFieldBlock(IReadOnlyList<NativeNormalize.PropertyMetadata> headerProperties)
    {
        if (headerProperties.Count == 0)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder();
        builder.Append(Constants.OPEN_BRACE);
        for (int i = 0; i < headerProperties.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(Constants.DEFAULT_DELIMITER_CHAR);
            }

            builder.Append(headerProperties[i].EncodedName);
        }

        builder.Append(Constants.CLOSE_BRACE);
        return builder.ToString();
    }

    /// <summary>
    /// Builds per-field typed writer delegates that read properties without boxing value types.
    /// Each compiled delegate uses a typed Func&lt;object, T&gt; getter (where T is the actual property type)
    /// so that value types like double? or int? stay on the stack and never allocate a heap box.
    /// </summary>
    private static (StringBuilderFieldWriter[] sbWriters, CompactFieldWriter[] compactWriters)
        BuildTypedFieldWriters(IReadOnlyList<NativeNormalize.PropertyMetadata> headerProperties)
    {
        var sbWriters = new StringBuilderFieldWriter[headerProperties.Count];
        var compactWriters = new CompactFieldWriter[headerProperties.Count];
        for (int i = 0; i < headerProperties.Count; i++)
        {
            (sbWriters[i], compactWriters[i]) = BuildTypedFieldWriter(headerProperties[i]);
        }

        return (sbWriters, compactWriters);
    }

    private static (StringBuilderFieldWriter sb, CompactFieldWriter compact)
        BuildTypedFieldWriter(NativeNormalize.PropertyMetadata property)
    {
        var propType = property.Info.PropertyType;
        var underlying = Nullable.GetUnderlyingType(propType) ?? propType;

        // double / float — most common value type in real-world data
        if (underlying == typeof(double) || underlying == typeof(float))
        {
            var getter = CompileNullableValueGetter<double>(property.Info);
            return (
                (row, sb, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) { sb.Append(Constants.NULL_LITERAL); return; }
                    var n = FloatUtils.NormalizeSignedZero(v.Value);
                    if (!NumericUtils.IsFinite(n)) { sb.Append(Constants.NULL_LITERAL); return; }
                    NativePrimitives.AppendDouble(sb, n);
                },
                (row, writer, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) { writer.Append(Constants.NULL_LITERAL); return; }
                    var n = FloatUtils.NormalizeSignedZero(v.Value);
                    if (!NumericUtils.IsFinite(n)) { writer.Append(Constants.NULL_LITERAL); return; }
                    NativePrimitives.AppendDouble(writer, n);
                }
            );
        }

        // int — second most common in product models
        if (underlying == typeof(int))
        {
            var getter = CompileNullableValueGetter<int>(property.Info);
            return (
                (row, sb, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) sb.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendInvariant(sb, v.Value);
                },
                (row, writer, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) writer.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendInvariant(writer, v.Value);
                }
            );
        }

        // long
        if (underlying == typeof(long))
        {
            var getter = CompileNullableValueGetter<long>(property.Info);
            return (
                (row, sb, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) sb.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendInvariant(sb, v.Value);
                },
                (row, writer, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) writer.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendInvariant(writer, v.Value);
                }
            );
        }

        // bool
        if (underlying == typeof(bool))
        {
            var getter = CompileNullableValueGetter<bool>(property.Info);
            return (
                (row, sb, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) sb.Append(Constants.NULL_LITERAL);
                    else sb.Append(v.Value ? Constants.TRUE_LITERAL : Constants.FALSE_LITERAL);
                },
                (row, writer, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) writer.Append(Constants.NULL_LITERAL);
                    else writer.Append(v.Value ? Constants.TRUE_LITERAL : Constants.FALSE_LITERAL);
                }
            );
        }

        // DateTime
        if (underlying == typeof(DateTime))
        {
            var getter = CompileNullableValueGetter<DateTime>(property.Info);
            return (
                (row, sb, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) sb.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendDateTimeRaw(sb, v.Value);
                },
                (row, writer, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) writer.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendDateTimeRaw(writer, v.Value);
                }
            );
        }

        // DateTimeOffset
        if (underlying == typeof(DateTimeOffset))
        {
            var getter = CompileNullableValueGetter<DateTimeOffset>(property.Info);
            return (
                (row, sb, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) sb.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendDateTimeRaw(sb, v.Value);
                },
                (row, writer, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) writer.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendDateTimeRaw(writer, v.Value);
                }
            );
        }

        // string — reference type, no boxing concern; use existing literal path
        if (propType == typeof(string))
        {
            var getter = CompileReferenceGetter<string>(property.Info);
            return (
                (row, sb, delim) =>
                {
                    var v = getter(row);
                    if (v is null) sb.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendStringLiteral(sb, v, delim);
                },
                (row, writer, delim) =>
                {
                    var v = getter(row);
                    if (v is null) writer.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendStringLiteral(writer, v, delim);
                }
            );
        }

        // decimal — common in financial/price fields
        if (underlying == typeof(decimal))
        {
            var getter = CompileNullableValueGetter<decimal>(property.Info);
            return (
                (row, sb, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) sb.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendInvariant(sb, v.Value);
                },
                (row, writer, _) =>
                {
                    var v = getter(row);
                    if (!v.HasValue) writer.Append(Constants.NULL_LITERAL);
                    else NativePrimitives.AppendInvariant(writer, v.Value);
                }
            );
        }

        // Fallback for all other types (byte, sbyte, short, ushort, uint, ulong, DateOnly, TimeOnly, etc.)
        // Uses existing boxing path — correct but allocates. Rare in practice.
        var fallbackGetter = property.Getter;
        return (
            (row, sb, delim) => NativePrimitives.AppendPrimitiveRaw(sb, fallbackGetter(row), delim),
            (row, writer, delim) => NativePrimitives.AppendPrimitiveRaw(writer, fallbackGetter(row), delim)
        );
    }

    /// <summary>
    /// Compiles a typed getter Func&lt;object, T?&gt; for a nullable or non-nullable value-type property.
    /// Returning T? (a value type) avoids boxing the value on the heap — the nullable stays on the stack.
    /// For non-nullable properties the return is always HasValue=true.
    /// </summary>
    private static Func<object, T?> CompileNullableValueGetter<T>(PropertyInfo property) where T : struct
    {
        var rowParam = Expression.Parameter(typeof(object), "row");
        var typedRow = Expression.Convert(rowParam, property.DeclaringType!);
        var getProp = Expression.Property(typedRow, property);

        // If the property is already T?, use it directly; if it's T (non-nullable), convert to T?
        Expression resultExpr = property.PropertyType == typeof(T?)
            ? (Expression)getProp
            : Expression.Convert(getProp, typeof(T?));

        return Expression.Lambda<Func<object, T?>>(resultExpr, rowParam).Compile();
    }

    /// <summary>
    /// Returns whether a scalar is one that normalization turns into null or an empty string, which
    /// <see cref="ResolvedEncodeOptions.IgnoreNullOrEmpty"/> omits: null, an empty string, or a non-finite double or float.
    /// </summary>
    private static bool IsNullOrEmptyScalar(object? value) => value switch
    {
        null => true,
        string text => text.Length == 0,
        double number => !NumericUtils.IsFinite(number),
        float number => !NumericUtils.IsFinite(number),
        _ => false
    };

    /// <summary>
    /// Compiles a check of whether a scalar property holds a value <see cref="IsNullOrEmptyScalar"/> accepts, without
    /// boxing. Returns <see langword="null"/> for a type that can never hold one, such as <see cref="int"/>.
    /// </summary>
    private static Func<object, bool>? CompileNullOrEmptyCheck(PropertyInfo property)
    {
        var type = property.PropertyType;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(double) || underlying == typeof(float))
        {
            var number = CompileNullableValueGetter<double>(property);
            return row => number(row) is not { } value || !NumericUtils.IsFinite(value);
        }

        if (type == typeof(string))
        {
            var text = CompileReferenceGetter<string>(property);
            return row => string.IsNullOrEmpty(text(row));
        }

        if (type.IsValueType && underlying == type)
        {
            return null;
        }

        var rowParam = Expression.Parameter(typeof(object), "row");
        var getProp = Expression.Property(Expression.Convert(rowParam, property.DeclaringType!), property);
        var isNull = Expression.Equal(getProp, Expression.Constant(null, type));
        return Expression.Lambda<Func<object, bool>>(isNull, rowParam).Compile();
    }

    /// <summary>
    /// Compiles a typed getter Func&lt;object, T?&gt; for a reference-type property.
    /// Reference types never box — this just eliminates the untyped object? return.
    /// </summary>
    private static Func<object, T?> CompileReferenceGetter<T>(PropertyInfo property) where T : class
    {
        var rowParam = Expression.Parameter(typeof(object), "row");
        var typedRow = Expression.Convert(rowParam, property.DeclaringType!);
        var getProp = Expression.Property(typedRow, property);
        var castResult = Expression.Convert(getProp, typeof(T));
        return Expression.Lambda<Func<object, T?>>(castResult, rowParam).Compile();
    }


    private static void AppendCachedHeader(
        System.Text.StringBuilder builder,
        int length,
        string? encodedKey,
        string? key,
        RowLayoutMetadata rowLayout,
        char delimiter)
    {
        if (delimiter != Constants.DEFAULT_DELIMITER_CHAR || string.IsNullOrEmpty(rowLayout.CachedHeaderFieldBlock))
        {
            NativePrimitives.AppendHeader(builder, length, encodedKey, key, rowLayout.HeaderNames, delimiter);
            return;
        }

        if (!string.IsNullOrEmpty(encodedKey))
        {
            builder.Append(encodedKey);
        }
        else if (key is { Length: > 0 } actualKey)
        {
            builder.Append(NativePrimitives.EncodeKey(actualKey));
        }

        builder.Append(Constants.OPEN_BRACKET);
        builder.Append(length);
        builder.Append(Constants.CLOSE_BRACKET);
        builder.Append(rowLayout.CachedHeaderFieldBlock);
        builder.Append(Constants.COLON);
    }

    private static void AppendCachedHeader(
        CompactBufferWriter writer,
        int length,
        string? encodedKey,
        string? key,
        RowLayoutMetadata rowLayout,
        char delimiter)
    {
        if (delimiter != Constants.DEFAULT_DELIMITER_CHAR || string.IsNullOrEmpty(rowLayout.CachedHeaderFieldBlock))
        {
            NativePrimitives.AppendHeader(writer, length, encodedKey, key, rowLayout.HeaderNames, delimiter);
            return;
        }

        if (!string.IsNullOrEmpty(encodedKey))
        {
            writer.Append(encodedKey);
        }
        else if (key is { Length: > 0 } actualKey)
        {
            writer.Append(NativePrimitives.EncodeKey(actualKey));
        }

        writer.Append(Constants.OPEN_BRACKET);
        writer.Append(length);
        writer.Append(Constants.CLOSE_BRACKET);
        writer.Append(rowLayout.CachedHeaderFieldBlock);
        writer.Append(Constants.COLON);
    }

    private static bool TryEncodeCachedClrObject(NativeNormalize.PropertyMetadata property, object value, LineWriter writer, int depth, ResolvedEncodeOptions options)
    {
        var cachedProperties = property.NestedObjectProperties;
        if (cachedProperties is null || value.GetType() != property.Info.PropertyType || property.ValueDeclaresExtensionData)
        {
            return false;
        }

        EncodeClrObject(property.EncodedName, cachedProperties, value, writer, depth, options);
        return true;
    }

    private static bool TryEncodeCachedClrObject(NativeNormalize.PropertyMetadata property, object value, CompactBufferWriter writer, int depth, ResolvedEncodeOptions options)
    {
        var cachedProperties = property.NestedObjectProperties;
        if (cachedProperties is null || value.GetType() != property.Info.PropertyType)
        {
            return false;
        }

        EncodeClrObject(property.EncodedName, property.Name, cachedProperties, value, writer, depth, options);
        return true;
    }

    private static bool[] BuildSuppressionMask(IList rows, NativeNormalize.PropertyMetadata[] headerProperties)
    {
        var suppress = new bool[headerProperties.Length];
        for (int ci = 0; ci < headerProperties.Length; ci++)
        {
            var getter = headerProperties[ci].Getter;
            bool allNullOrEmpty = true;
            for (int ri = 0; ri < rows.Count; ri++)
            {
                if (!IsNullOrEmptyScalar(getter(rows[ri]!)))
                {
                    allNullOrEmpty = false;
                    break;
                }
            }
            suppress[ci] = allNullOrEmpty;
        }
        return suppress;
    }

    private static string[]? BuildActiveHeaderNames(string[] headerNames, bool[] suppress)
    {
        bool anySuppressed = false;
        for (int i = 0; i < suppress.Length; i++)
        {
            if (suppress[i]) { anySuppressed = true; break; }
        }
        if (!anySuppressed)
            return null;

        var active = new List<string>(headerNames.Length);
        for (int i = 0; i < headerNames.Length; i++)
        {
            if (!suppress[i])
                active.Add(headerNames[i]);
        }
        return active.ToArray();
    }

    /// <summary>
    /// Cached row layout and compiled field writers for one CLR object-array element type.
    /// </summary>
    /// <summary>
    /// Cached compact-writer emitters for a plain object type, without its extension data property.
    /// </summary>
    private sealed class ObjectEmitters
    {
        /// <summary>
        /// Initializes the emitters of a plain object type.
        /// </summary>
        /// <param name="emitters">Member emitters in declaration order.</param>
        /// <param name="extensionGetter">The getter of the type's extension data property, when it declares one.</param>
        public ObjectEmitters(CompactWriterPropertyEmitter[] emitters, Func<object, object?>? extensionGetter)
        {
            Emitters = emitters;
            ExtensionGetter = extensionGetter;
        }

        /// <summary>Gets the member emitters in declaration order.</summary>
        public CompactWriterPropertyEmitter[] Emitters { get; }

        /// <summary>Gets the getter of the type's extension data property, when it declares one.</summary>
        public Func<object, object?>? ExtensionGetter { get; }

        /// <summary>
        /// Returns whether <paramref name="value"/> carries extension data entries, which the member emitters do not write.
        /// </summary>
        public bool HasExtensionEntries(object value)
            => ExtensionGetter != null && ExtensionGetter(value) is IDictionary<string, object?> { Count: > 0 };
    }

    private readonly struct RowLayoutMetadata
    {
        /// <summary>
        /// Initializes cached row layout metadata.
        /// </summary>
        /// <param name="headerProperties">Scalar properties written into the row header.</param>
        /// <param name="headerNames">Encoded field names for header properties.</param>
        /// <param name="cachedHeaderFieldBlock">Pre-joined encoded header fields when available.</param>
        /// <param name="stringBuilderFieldWriters">Compiled field writers for string builder output.</param>
        /// <param name="compactFieldWriters">Compiled field writers for compact writer output.</param>
        /// <param name="nonHeaderProperties">Properties emitted as nested spill fields.</param>
        /// <param name="lineWriterNonHeaderEmitters">Compiled spill emitters for line writer output.</param>
        /// <param name="compactNonHeaderEmitters">Compiled spill emitters for compact writer output.</param>
        /// <param name="memberCount">The number of encoded member properties, excluding an extension data property.</param>
        /// <param name="extensionGetter">The getter of the row type's extension data property, when it declares one.</param>
        public RowLayoutMetadata(
            NativeNormalize.PropertyMetadata[] headerProperties,
            string[] headerNames,
            string? cachedHeaderFieldBlock,
            StringBuilderFieldWriter[] stringBuilderFieldWriters,
            CompactFieldWriter[] compactFieldWriters,
            NativeNormalize.PropertyMetadata[] nonHeaderProperties,
            LineWriterPropertyEmitter[] lineWriterNonHeaderEmitters,
            CompactWriterPropertyEmitter[] compactNonHeaderEmitters,
            int memberCount,
            Func<object, object?>? extensionGetter)
        {
            HeaderProperties = headerProperties;
            HeaderNames = headerNames;
            CachedHeaderFieldBlock = cachedHeaderFieldBlock;
            StringBuilderFieldWriters = stringBuilderFieldWriters;
            CompactFieldWriters = compactFieldWriters;
            NonHeaderProperties = nonHeaderProperties;
            LineWriterNonHeaderEmitters = lineWriterNonHeaderEmitters;
            CompactNonHeaderEmitters = compactNonHeaderEmitters;
            MemberCount = memberCount;
            ExtensionGetter = extensionGetter;
            polymorphicMembers = new bool[nonHeaderProperties.Length];
            for (int i = 0; i < nonHeaderProperties.Length; i++)
            {
                if (nonHeaderProperties[i].EnumerableElementType == typeof(byte))
                {
                    HasByteSequenceMember = true;
                }

                var type = nonHeaderProperties[i].Info.PropertyType;
                polymorphicMembers[i] = type == typeof(object) || type.IsInterface || type.IsAbstract;
            }
        }

        // Non-header members whose declared type can hold a scalar or a byte sequence at runtime.
        private readonly bool[] polymorphicMembers;

        /// <summary>
        /// Returns whether some non-header member holds a scalar (null included, and a byte sequence written as base64) in
        /// every row. The slow path makes such a member a table column, which these cached layouts cannot, so the rows must be
        /// normalized instead. A member of a concrete object or collection type can only be null, which matters only when
        /// <see cref="ResolvedEncodeOptions.IgnoreNullOrEmpty"/> does not drop both forms.
        /// </summary>
        public bool HasScalarColumnMember(IList rows, ResolvedEncodeOptions options)
        {
            for (int m = 0; m < NonHeaderProperties.Length; m++)
            {
                if (!polymorphicMembers[m] && options.IgnoreNullOrEmpty)
                {
                    continue;
                }

                var getter = NonHeaderProperties[m].Getter;
                var scalarInEveryRow = true;
                for (int r = 0; r < rows.Count; r++)
                {
                    if (IsStructuredValue(getter(rows[r]!), options))
                    {
                        scalarInEveryRow = false;
                        break;
                    }
                }

                if (scalarInEveryRow)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStructuredValue(object? value, ResolvedEncodeOptions options)
        {
            if (NativeNormalize.IsPrimitiveOrNull(value))
            {
                return false;
            }

            if (value is IEnumerable<byte>)
            {
                return options.ByteArrayFormat != ToonByteArrayFormat.Base64String;
            }

            return value is IEnumerable || NativeNormalize.IsPlainObjectType(value!.GetType());
        }

        /// <summary>Gets whether a non-header member is a byte sequence, which base64 output turns into a scalar column.</summary>
        public bool HasByteSequenceMember { get; }

        /// <summary>
        /// Returns whether the options write the row type's byte sequences as base64 scalars: the slow path then makes them
        /// table columns, which these cached layouts cannot, so the rows must be normalized instead.
        /// </summary>
        public bool HasBase64ByteColumns(ResolvedEncodeOptions options)
            => HasByteSequenceMember && options.ByteArrayFormat == ToonByteArrayFormat.Base64String;

        /// <summary>Gets the number of encoded member properties, excluding an extension data property.</summary>
        public int MemberCount { get; }

        /// <summary>Gets the getter of the row type's extension data property, when it declares one.</summary>
        public Func<object, object?>? ExtensionGetter { get; }

        /// <summary>
        /// Returns whether any row carries extension data entries, which the fast path does not write: such rows are encoded
        /// through normalization so the entries become sibling keys.
        /// </summary>
        public bool HasExtensionEntries(IList rows)
        {
            if (ExtensionGetter == null)
            {
                return false;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] is { } row && ExtensionGetter(row) is IDictionary<string, object?> { Count: > 0 })
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Gets scalar properties written into the row header.</summary>
        public NativeNormalize.PropertyMetadata[] HeaderProperties { get; }

        /// <summary>Gets encoded field names for header properties.</summary>
        public string[] HeaderNames { get; }

        /// <summary>Gets a pre-joined encoded header field block when no column suppression is active.</summary>
        public string? CachedHeaderFieldBlock { get; }

        /// <summary>Gets compiled scalar field writers for <see cref="StringBuilder"/> output.</summary>
        public StringBuilderFieldWriter[] StringBuilderFieldWriters { get; }

        /// <summary>Gets compiled scalar field writers for <see cref="CompactBufferWriter"/> output.</summary>
        public CompactFieldWriter[] CompactFieldWriters { get; }

        /// <summary>Gets properties emitted as nested spill fields below the row.</summary>
        public NativeNormalize.PropertyMetadata[] NonHeaderProperties { get; }

        /// <summary>Gets compiled spill emitters for line writer output.</summary>
        public LineWriterPropertyEmitter[] LineWriterNonHeaderEmitters { get; }

        /// <summary>Gets compiled spill emitters for compact writer output.</summary>
        public CompactWriterPropertyEmitter[] CompactNonHeaderEmitters { get; }

        public void AppendHeaderRow(StringBuilder builder, object row, char delimiter)
        {
            var writers = StringBuilderFieldWriters;
            for (int i = 0; i < writers.Length; i++)
            {
                if (i > 0) builder.Append(delimiter);
                writers[i](row, builder, delimiter);
            }
        }

        public void AppendHeaderRow(StringBuilder builder, object row, char delimiter, bool[] suppress)
        {
            var writers = StringBuilderFieldWriters;
            bool first = true;
            for (int i = 0; i < writers.Length; i++)
            {
                if (suppress[i]) continue;
                if (!first) builder.Append(delimiter);
                first = false;
                writers[i](row, builder, delimiter);
            }
        }

        public void AppendHeaderRow(CompactBufferWriter writer, object row, char delimiter)
        {
            var writers = CompactFieldWriters;
            for (int i = 0; i < writers.Length; i++)
            {
                if (i > 0) writer.Append(delimiter);
                writers[i](row, writer, delimiter);
            }
        }

        public void AppendHeaderRow(CompactBufferWriter writer, object row, char delimiter, bool[] suppress)
        {
            var writers = CompactFieldWriters;
            bool first = true;
            for (int i = 0; i < writers.Length; i++)
            {
                if (suppress[i]) continue;
                if (!first) writer.Append(delimiter);
                first = false;
                writers[i](row, writer, delimiter);
            }
        }
    }

}
