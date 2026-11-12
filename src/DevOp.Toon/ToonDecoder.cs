#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOp.Toon.Core;
using DevOp.Toon;
using DevOp.Toon.Internal.Decode;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon;

/// <summary>
/// Decodes TOON-formatted strings into data structures.
/// </summary>
public static class ToonDecoder
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding ReplacingUtf8 = new UTF8Encoding(false, false);
    private static ToonDecodeOptions defaultOptions = new ToonDecodeOptions();

    /// <summary>
    /// Gets or sets the default decode options used by overloads that do not accept an explicit options argument.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
    public static ToonDecodeOptions DefaultOptions
    {
        get => defaultOptions;
        set => defaultOptions = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Decodes a TOON-formatted string into a ToonNode with default options.
    /// </summary>
    /// <param name="toonString">The TOON-formatted string to decode.</param>
    /// <returns>The decoded ToonNode object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when toonString is null.</exception>
    /// <exception cref="ToonFormatException">Thrown when the TOON format is invalid.</exception>
    public static ToonNode? Decode(string toonString)
    {
        return Decode(toonString, DefaultOptions);
    }

    /// <summary>
    /// Decodes a TOON-formatted string into the specified type with default options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="toonString">The TOON-formatted string to decode.</param>
    /// <returns>The deserialized value of type T.</returns>
    public static T? Decode<T>(string toonString)
    {
        return Decode<T>(toonString, DefaultOptions);
    }

    /// <summary>
    /// Converts TOON source text to minified JSON using the default decode options.
    /// </summary>
    /// <param name="toonString">The TOON-formatted string to convert.</param>
    /// <returns>A minified JSON string representing the TOON input.</returns>
    /// <exception cref="ArgumentNullException">Thrown when toonString is null.</exception>
    /// <exception cref="ToonFormatException">Thrown when the TOON format is invalid.</exception>
    public static string Toon2Json(string toonString)
    {
        return Toon2Json(toonString, DefaultOptions);
    }

    /// <summary>
    /// Converts TOON source text to minified JSON using custom decode options.
    /// </summary>
    /// <param name="toonString">The TOON-formatted string to convert.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <returns>A minified JSON string representing the TOON input.</returns>
    /// <exception cref="ArgumentNullException">Thrown when toonString or options is null.</exception>
    /// <exception cref="ToonFormatException">Thrown when the TOON format is invalid.</exception>
    public static string Toon2Json(string toonString, ToonDecodeOptions? options)
    {
        return JsonTextConverter.Write(DecodeNative(toonString, options));
    }

    /// <summary>
    /// Detects likely decode options from TOON source text.
    /// </summary>
    /// <param name="toonString">The TOON-formatted string to inspect.</param>
    /// <returns>A new options instance seeded from <see cref="DefaultOptions"/> and adjusted from the input text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toonString"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The default indentation width is not positive.</exception>
    public static ToonDecodeOptions DetectOptions(string toonString)
    {
        return DetectOptions(toonString, DefaultOptions);
    }

    /// <summary>
    /// Detects likely decode options from TOON source text using a fallback options template.
    /// </summary>
    /// <param name="toonString">The TOON-formatted string to inspect.</param>
    /// <param name="fallbackOptions">Fallback options used for settings that cannot be inferred from source text.</param>
    /// <returns>A new options instance based on the fallback options and adjusted from the input text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toonString"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The fallback indentation width is not positive.</exception>
    public static ToonDecodeOptions DetectOptions(string toonString, ToonDecodeOptions? fallbackOptions)
    {
        return ToonDecoderOptionsDetector.Detect(toonString, fallbackOptions);
    }

    /// <summary>
    /// Decodes a TOON-formatted string into a ToonNode with custom options.
    /// </summary>
    /// <param name="toonString">The TOON-formatted string to decode.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <returns>The decoded ToonNode object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when toonString or options is null.</exception>
    /// <exception cref="ToonFormatException">Thrown when the TOON format is invalid.</exception>
    public static ToonNode? Decode(string toonString, ToonDecodeOptions? options)
    {
        return ToonNodeConverter.ToToonNode(DecodeNative(toonString, options));
    }

    private static NativeNode? DecodeNative(string toonString, ToonDecodeOptions? options)
    {
        if (toonString == null)
            throw new ArgumentNullException(nameof(toonString));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ValidateIndent(options.Indent);
        options = ResolveProtocol(toonString, options, out var scannedLines);
        if (IsV4(options.ProtocolVersion)) return V4Decoder.Decode(toonString, options, scannedLines);


        // Resolve options
        var resolvedOptions = options.GetResolvedOptions();

        // Scan the source text into structured lines
        var scanResult = Scanner.ToParsedLines(toonString, resolvedOptions.Indent, resolvedOptions.Strict);

        // Handle empty input
        if (scanResult.Lines.Count == 0)
        {
            return new NativeObjectNode();
        }

        // Create cursor and decode
        var cursor = new LineCursor(scanResult.Lines, scanResult.BlankLines);

        var nativeResult = NativeDecoders.DecodeValueFromLines(cursor, resolvedOptions);

        // Apply path expansion before converting to the public TOON node model.
        if (ShouldApplyPathExpansion(scanResult, resolvedOptions.ExpandPaths))
        {
            nativeResult = NativePathExpansion.ExpandPaths(nativeResult, resolvedOptions.Strict);
        }

        return nativeResult;
    }

    /// <summary>
    /// Decodes a TOON-formatted string into the specified type with custom options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="toonString">The TOON-formatted string to decode.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <returns>The deserialized value of type T.</returns>
    /// <exception cref="ArgumentNullException">Thrown when toonString is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when options specify a non-positive indentation width.</exception>
    /// <exception cref="ToonFormatException">Thrown when the TOON format is invalid.</exception>
    /// <exception cref="NotSupportedException">Thrown when the requested target type cannot be materialized from TOON data.</exception>
    public static T? Decode<T>(string toonString, ToonDecodeOptions? options)
    {
        var resolvedOptions = options ?? new ToonDecodeOptions();

        ValidateIndent(resolvedOptions.Indent);
        resolvedOptions = ResolveProtocol(toonString, resolvedOptions, out var scannedLines);
        if (IsV4(resolvedOptions.ProtocolVersion))
        {
            if (typeof(ToonNode).IsAssignableFrom(typeof(T))) return (T?)(object?)ToonNodeConverter.ToToonNode(V4Decoder.Decode(toonString, resolvedOptions, scannedLines));
            return V4Decoder.Decode<T>(toonString, resolvedOptions, scannedLines);
        }

        if (typeof(ToonNode).IsAssignableFrom(typeof(T)))
        {
            var node = Decode(toonString, resolvedOptions);
            return (T?)(object?)node;
        }

        var decodeOptions = resolvedOptions.GetResolvedOptions();

        var scanResult = Scanner.ToParsedLines(toonString, decodeOptions.Indent, decodeOptions.Strict);
        var shouldExpandPaths = ShouldApplyPathExpansion(scanResult, decodeOptions.ExpandPaths);

        if (!shouldExpandPaths && !RequiresNativeByteSequenceMaterialization(typeof(T)))
        {
            return TypedDecoder.Decode<T>(scanResult, decodeOptions);
        }

        if (scanResult.Lines.Count == 0)
        {
            if (NativeTypedMaterializer.TryConvert<T>(new NativeObjectNode(), out var emptyValue))
            {
                return emptyValue;
            }

            throw CreateUnsupportedTargetTypeException(typeof(T));
        }

        var cursor = new LineCursor(scanResult.Lines, scanResult.BlankLines);

        var nativeResult = NativeDecoders.DecodeValueFromLines(cursor, decodeOptions);
        if (shouldExpandPaths)
        {
            nativeResult = NativePathExpansion.ExpandPaths(nativeResult, decodeOptions.Strict);
        }

        if (NativeTypedMaterializer.TryConvert<T>(nativeResult, out var typedValue))
        {
            return typedValue;
        }

        throw CreateUnsupportedTargetTypeException(typeof(T));
    }

    /// <summary>Inspects syntax and reports whether an explicit version is needed.</summary>
    public static ToonVersionDetectionResult DetectVersion(string toonString)
    {
        var result = V4ProtocolDetector.Detect(toonString, fallbackIndent: DefaultOptions.Indent);
        return new ToonVersionDetectionResult(result.Version, result.IsUnique, result.RequiresHint);
    }

    private static Encoding DecodeEncoding(ToonDecodeOptions? options)
    {
        var replaceInvalid = options?.ProtocolVersion is ToonProtocolVersion.CustomLegacy or ToonProtocolVersion.V3_0
            || options?.Strict == false && options.ProtocolVersion is ToonProtocolVersion.V4_2 or ToonProtocolVersion.V4_3;
        return replaceInvalid ? ReplacingUtf8 : StrictUtf8;
    }

    private static bool IsV4(ToonProtocolVersion version) => version >= ToonProtocolVersion.V4_0 && version <= ToonProtocolVersion.V4_4 || version == ToonProtocolVersion.CustomV4_4;

    private static ToonDecodeOptions ResolveProtocol(string source, ToonDecodeOptions options, out List<V4SourceLine>? scannedLines)
    {
        scannedLines = null;
        if (options.ProtocolVersion is not (ToonProtocolVersion.Auto or ToonProtocolVersion.V3_0 or ToonProtocolVersion.V4_0 or ToonProtocolVersion.V4_1 or ToonProtocolVersion.V4_2 or ToonProtocolVersion.V4_3 or ToonProtocolVersion.V4_4 or ToonProtocolVersion.CustomLegacy or ToonProtocolVersion.CustomV4_4)) throw new ArgumentOutOfRangeException(nameof(options.ProtocolVersion));
        if (options.ProtocolVersion == ToonProtocolVersion.Auto)
        {
            var detected = V4ProtocolDetector.Detect(source, collectLines: true, fallbackIndent: options.Indent);
            scannedLines = detected.Lines;
            if (detected.RequiresHint) throw ToonFormatException.Syntax("The payload has version-dependent meanings. Supply ProtocolVersion explicitly.");
            options = options.Clone();
            options.ProtocolVersion = detected.Version;
            if (detected.Indent.HasValue) options.Indent = detected.Indent.Value;
        }
        if (options.ProtocolVersion is >= ToonProtocolVersion.V4_0 and <= ToonProtocolVersion.V4_4 && options.ObjectArrayLayout == ToonObjectArrayLayout.Columnar) throw new ArgumentException("Columnar spill layout is not supported by an official protocol version.", nameof(options));
        if (IsV4(options.ProtocolVersion) && options.ExpandPaths != ToonPathExpansion.Off) throw new ArgumentException("Path expansion is not supported by this protocol version.", nameof(options));
        return options;
    }

    private static NotSupportedException CreateUnsupportedTargetTypeException(Type targetType)
    {
        return new NotSupportedException($"TOON typed decode does not support target type '{targetType.FullName}'.");
    }

    private static void ValidateIndent(int indent)
    {
        if (indent <= 0)
            throw new ArgumentOutOfRangeException(nameof(indent), indent, "Indent must be greater than 0.");
    }

    private static bool ShouldApplyPathExpansion(ScanResult scanResult, ToonPathExpansion expandPaths)
    {
        if (expandPaths != ToonPathExpansion.Safe || scanResult.Lines.Count == 0)
        {
            return false;
        }

        if (scanResult.Lines.Count == 1 && !IsKeyValueLine(scanResult.Lines[0].Content))
        {
            return false;
        }

        for (int i = 0; i < scanResult.Lines.Count; i++)
        {
            var content = scanResult.Lines[i].Content;
            if (content.IndexOf(Constants.DOT) < 0)
            {
                continue;
            }

            if (content.StartsWith(Constants.LIST_ITEM_PREFIX, StringComparison.Ordinal))
            {
                content = content.Substring(Constants.LIST_ITEM_PREFIX.Length);
            }

            if (HasExpandableKey(content))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasExpandableKey(string content)
    {
        if (content.Length == 0 || StringUtils.FindUnquotedChar(content, Constants.COLON) < 0)
        {
            return false;
        }

        var arrayHeader = Parser.ParseArrayHeaderLine(content, Constants.DEFAULT_DELIMITER_CHAR);
        if (arrayHeader != null)
        {
            var header = arrayHeader.Header;
            if (header.Key != null &&
                !(arrayHeader.HasKeyRange && content[arrayHeader.KeyStart] == Constants.DOUBLE_QUOTE) &&
                IsExpandablePathKey(header.Key))
            {
                return true;
            }

            if (header.Fields != null)
            {
                foreach (var field in header.Fields)
                {
                    if (header.QuotedDottedFields?.Contains(field) != true && IsExpandablePathKey(field))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        try
        {
            var keyResult = Parser.ParseKeyToken(content, 0);
            return !keyResult.WasQuoted && IsExpandablePathKey(keyResult.Key);
        }
        catch (ToonFormatException)
        {
            return false;
        }
    }

    private static bool IsExpandablePathKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.IndexOf(Constants.DOT) < 0)
        {
            return false;
        }

        var segments = key.Split(Constants.DOT);
        for (int i = 0; i < segments.Length; i++)
        {
            if (!ValidationShared.IsIdentifierSegment(segments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RequiresNativeByteSequenceMaterialization(Type type)
    {
        return RequiresNativeByteSequenceMaterialization(type, new HashSet<Type>());
    }

    private static bool RequiresNativeByteSequenceMaterialization(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (type == typeof(byte[]) || IsByteSequenceCollectionType(type))
        {
            return true;
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            return elementType is not null && RequiresNativeByteSequenceMaterialization(elementType, visited);
        }

        if ((!type.IsClass && !type.IsValueType) || type == typeof(string))
        {
            return false;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (!property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (RequiresNativeByteSequenceMaterialization(property.PropertyType, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsByteSequenceCollectionType(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var genericDefinition = type.GetGenericTypeDefinition();
        return (genericDefinition == typeof(List<>) ||
                genericDefinition == typeof(IList<>) ||
                genericDefinition == typeof(ICollection<>) ||
                genericDefinition == typeof(IEnumerable<>) ||
                genericDefinition == typeof(IReadOnlyList<>)) &&
               type.GetGenericArguments()[0] == typeof(byte);
    }

    private static bool IsKeyValueLine(string content)
    {
        if (content.StartsWith("\"", StringComparison.Ordinal))
        {
            var closingQuoteIndex = StringUtils.FindClosingQuote(content, 0);
            if (closingQuoteIndex == -1)
            {
                return false;
            }

            return content.Substring(closingQuoteIndex + 1).Contains(Constants.COLON);
        }

        return content.Contains(Constants.COLON);
    }

    /// <summary>
    /// Decodes TOON data from a UTF-8 byte array into a ToonNode with default options.
    /// </summary>
    /// <param name="utf8Bytes">UTF-8 encoded TOON text.</param>
    /// <returns>The decoded ToonNode object.</returns>
    public static ToonNode? Decode(byte[] utf8Bytes)
    {
        if (utf8Bytes == null)
            throw new ArgumentNullException(nameof(utf8Bytes));
        return Decode(DecodeEncoding(DefaultOptions).GetString(utf8Bytes));
    }

    /// <summary>
    /// Decodes TOON data from a UTF-8 byte array into a ToonNode with custom options.
    /// </summary>
    /// <param name="utf8Bytes">UTF-8 encoded TOON text.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <returns>The decoded ToonNode object.</returns>
    public static ToonNode? Decode(byte[] utf8Bytes, ToonDecodeOptions? options)
    {
        if (utf8Bytes == null)
            throw new ArgumentNullException(nameof(utf8Bytes));
        var text = DecodeEncoding(options ?? DefaultOptions).GetString(utf8Bytes);
        return Decode(text, options ?? new ToonDecodeOptions());
    }

    /// <summary>
    /// Decodes TOON data from a UTF-8 byte array into the specified type with default options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="utf8Bytes">UTF-8 encoded TOON text.</param>
    /// <returns>The deserialized value of type T.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Bytes"/> is <see langword="null"/>.</exception>
    public static T? Decode<T>(byte[] utf8Bytes)
    {
        if (utf8Bytes == null)
            throw new ArgumentNullException(nameof(utf8Bytes));
        return Decode<T>(DecodeEncoding(DefaultOptions).GetString(utf8Bytes));
    }

    /// <summary>
    /// Decodes TOON data from a UTF-8 byte array into the specified type with custom options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="utf8Bytes">UTF-8 encoded TOON text.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <returns>The deserialized value of type T.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Bytes"/> is <see langword="null"/>.</exception>
    public static T? Decode<T>(byte[] utf8Bytes, ToonDecodeOptions? options)
    {
        if (utf8Bytes == null)
            throw new ArgumentNullException(nameof(utf8Bytes));
        var text = DecodeEncoding(options ?? DefaultOptions).GetString(utf8Bytes);
        return Decode<T>(text, options ?? new ToonDecodeOptions());
    }

    /// <summary>
    /// Decodes TOON data from a stream (UTF-8) into a ToonNode with default options.
    /// </summary>
    /// <param name="stream">The input stream to read from.</param>
    /// <returns>The decoded ToonNode object.</returns>
    public static ToonNode? Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(stream, DecodeEncoding(DefaultOptions), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        return Decode(reader.ReadToEnd());
    }

    /// <summary>
    /// Decodes TOON data from a stream (UTF-8) into a ToonNode with custom options.
    /// </summary>
    /// <param name="stream">The input stream to read from.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <returns>The decoded ToonNode object.</returns>
    public static ToonNode? Decode(Stream stream, ToonDecodeOptions? options)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(stream, DecodeEncoding(options ?? DefaultOptions), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var text = reader.ReadToEnd();

        return Decode(text, options ?? new ToonDecodeOptions());
    }

    /// <summary>
    /// Decodes TOON data from a stream (UTF-8) into the specified type with default options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="stream">The input stream to read from.</param>
    /// <returns>The deserialized value of type T.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public static T? Decode<T>(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(stream, DecodeEncoding(DefaultOptions), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        return Decode<T>(reader.ReadToEnd());
    }

    /// <summary>
    /// Decodes TOON data from a stream (UTF-8) into the specified type with custom options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="stream">The input stream to read from.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <returns>The deserialized value of type T.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public static T? Decode<T>(Stream stream, ToonDecodeOptions? options)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(stream, DecodeEncoding(options ?? DefaultOptions), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var text = reader.ReadToEnd();

        return Decode<T>(text, options ?? new ToonDecodeOptions());
    }

    #region Async Methods

    /// <summary>
    /// Asynchronously decodes a TOON-formatted string into a ToonNode with default options.
    /// </summary>
    /// <param name="toonString">The TOON-formatted string to decode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the decoded ToonNode.</returns>
    /// <exception cref="ArgumentNullException">Thrown when toonString is null.</exception>
    /// <exception cref="ToonFormatException">Thrown when the TOON format is invalid.</exception>
    public static Task<ToonNode?> DecodeAsync(string toonString, CancellationToken cancellationToken = default)
    {
        return DecodeAsync(toonString, DefaultOptions, cancellationToken);
    }

    /// <summary>
    /// Asynchronously decodes a TOON-formatted string into a ToonNode with custom options.
    /// </summary>
    /// <param name="toonString">The TOON-formatted string to decode.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the decoded ToonNode.</returns>
    /// <exception cref="ArgumentNullException">Thrown when toonString or options is null.</exception>
    /// <exception cref="ToonFormatException">Thrown when the TOON format is invalid.</exception>
    public static Task<ToonNode?> DecodeAsync(string toonString, ToonDecodeOptions? options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Decode(toonString, options);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Asynchronously decodes a TOON-formatted string into the specified type with default options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="toonString">The TOON-formatted string to decode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized value.</returns>
    public static Task<T?> DecodeAsync<T>(string toonString, CancellationToken cancellationToken = default)
    {
        return DecodeAsync<T>(toonString, DefaultOptions, cancellationToken);
    }

    /// <summary>
    /// Asynchronously decodes a TOON-formatted string into the specified type with custom options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="toonString">The TOON-formatted string to decode.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized value.</returns>
    public static Task<T?> DecodeAsync<T>(string toonString, ToonDecodeOptions? options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Decode<T>(toonString, options);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Asynchronously decodes TOON data from a stream (UTF-8) into a ToonNode with default options.
    /// </summary>
    /// <param name="stream">The input stream to read from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the decoded ToonNode.</returns>
    public static Task<ToonNode?> DecodeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return DecodeAsync(stream, null, cancellationToken);
    }

    /// <summary>
    /// Asynchronously decodes TOON data from a stream (UTF-8) into a ToonNode with custom options.
    /// </summary>
    /// <param name="stream">The input stream to read from.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the decoded ToonNode.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static async Task<ToonNode?> DecodeAsync(Stream stream, ToonDecodeOptions? options, CancellationToken cancellationToken = default)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(stream, DecodeEncoding(options ?? DefaultOptions), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        return options is null
            ? await DecodeAsync(text, cancellationToken).ConfigureAwait(false)
            : await DecodeAsync(text, options, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously decodes TOON data from a stream (UTF-8) into the specified type with default options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="stream">The input stream to read from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized value.</returns>
    public static Task<T?> DecodeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        return DecodeAsync<T>(stream, null, cancellationToken);
    }

    /// <summary>
    /// Asynchronously decodes TOON data from a stream (UTF-8) into the specified type with custom options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="stream">The input stream to read from.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static async Task<T?> DecodeAsync<T>(Stream stream, ToonDecodeOptions? options, CancellationToken cancellationToken = default)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(stream, DecodeEncoding(options ?? DefaultOptions), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        return options is null
            ? await DecodeAsync<T>(text, cancellationToken).ConfigureAwait(false)
            : await DecodeAsync<T>(text, options, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    #endregion
}
