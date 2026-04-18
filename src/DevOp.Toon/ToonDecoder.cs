#nullable enable
using System;
using System.IO;
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

        // Resolve options
        var resolvedOptions = new ResolvedDecodeOptions
        {
            Indent = options.Indent,
            Strict = options.Strict,
            ExpandPaths = options.ExpandPaths,
            ObjectArrayLayout = options.ObjectArrayLayout
        };

        // Scan the source text into structured lines
        var scanResult = Scanner.ToParsedLines(toonString, resolvedOptions.Indent, resolvedOptions.Strict);

        // Handle empty input
        if (scanResult.Lines.Count == 0)
        {
            return new NativeObjectNode();
        }

        // Create cursor and decode
        var cursor = new LineCursor(scanResult.Lines, scanResult.BlankLines);

        var shouldExpandPaths = ShouldApplyRootPathExpansion(scanResult, resolvedOptions.ExpandPaths);

        // Track quoted keys only when root-level path expansion can actually apply.
        HashSet<string>? quotedKeys = null;
        if (shouldExpandPaths)
        {
            quotedKeys = new HashSet<string>();
        }

        var nativeResult = NativeDecoders.DecodeValueFromLines(cursor, resolvedOptions, quotedKeys);

        // Apply path expansion before converting to the public TOON node model.
        if (shouldExpandPaths && nativeResult is NativeObjectNode nativeObject)
        {
            nativeResult = NativePathExpansion.ExpandPaths(nativeObject, resolvedOptions.Strict, quotedKeys);
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
    public static T? Decode<T>(string toonString, ToonDecodeOptions? options)
    {
        var resolvedOptions = options ?? new ToonDecodeOptions();

        ValidateIndent(resolvedOptions.Indent);

        if (typeof(ToonNode).IsAssignableFrom(typeof(T)))
        {
            var node = Decode(toonString, resolvedOptions);
            return (T?)(object?)node;
        }

        var decodeOptions = new ResolvedDecodeOptions
        {
            Indent = resolvedOptions.Indent,
            Strict = resolvedOptions.Strict,
            ExpandPaths = resolvedOptions.ExpandPaths,
            ObjectArrayLayout = resolvedOptions.ObjectArrayLayout
        };

        var scanResult = Scanner.ToParsedLines(toonString, decodeOptions.Indent, decodeOptions.Strict);
        var shouldExpandPaths = ShouldApplyRootPathExpansion(scanResult, decodeOptions.ExpandPaths);

        if (!shouldExpandPaths)
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
        var quotedKeys = new HashSet<string>();

        var nativeResult = NativeDecoders.DecodeValueFromLines(cursor, decodeOptions, quotedKeys);
        if (nativeResult is NativeObjectNode nativeObject)
        {
            nativeResult = NativePathExpansion.ExpandPaths(nativeObject, decodeOptions.Strict, quotedKeys);
        }

        if (NativeTypedMaterializer.TryConvert<T>(nativeResult, out var typedValue))
        {
            return typedValue;
        }

        throw CreateUnsupportedTargetTypeException(typeof(T));
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

    private static bool ShouldApplyRootPathExpansion(ScanResult scanResult, ToonPathExpansion expandPaths)
    {
        if (expandPaths != ToonPathExpansion.Safe || scanResult.Lines.Count == 0)
        {
            return false;
        }

        var firstLine = scanResult.Lines[0];
        if (Parser.IsArrayHeaderAfterHyphen(firstLine.Content))
        {
            return false;
        }

        if (scanResult.Lines.Count == 1 && !IsKeyValueLine(firstLine.Content))
        {
            return false;
        }

        var rootDepth = firstLine.Depth;
        for (int i = 0; i < scanResult.Lines.Count; i++)
        {
            var line = scanResult.Lines[i];
            if (line.Depth != rootDepth)
            {
                continue;
            }

            if (TryGetRootObjectKey(line.Content, out var key, out var wasQuoted)
                && !wasQuoted
                && IsExpandablePathKey(key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetRootObjectKey(string content, out string key, out bool wasQuoted)
    {
        var arrayHeader = Parser.ParseArrayHeaderLine(content, Constants.DEFAULT_DELIMITER_CHAR);
        if (arrayHeader?.Header.Key != null)
        {
            key = arrayHeader.Header.Key;
            wasQuoted = arrayHeader.HasKeyRange && content[arrayHeader.KeyStart] == Constants.DOUBLE_QUOTE;
            return true;
        }

        try
        {
            var keyResult = Parser.ParseKeyToken(content, 0);
            key = keyResult.Key;
            wasQuoted = keyResult.WasQuoted;
            return true;
        }
        catch (ToonFormatException)
        {
            key = string.Empty;
            wasQuoted = false;
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
        return Decode(utf8Bytes, DefaultOptions);
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
        var text = Encoding.UTF8.GetString(utf8Bytes);
        return Decode(text, options ?? new ToonDecodeOptions());
    }

    /// <summary>
    /// Decodes TOON data from a UTF-8 byte array into the specified type with default options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="utf8Bytes">UTF-8 encoded TOON text.</param>
    public static T? Decode<T>(byte[] utf8Bytes)
    {
        return Decode<T>(utf8Bytes, DefaultOptions);
    }

    /// <summary>
    /// Decodes TOON data from a UTF-8 byte array into the specified type with custom options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="utf8Bytes">UTF-8 encoded TOON text.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    public static T? Decode<T>(byte[] utf8Bytes, ToonDecodeOptions? options)
    {
        if (utf8Bytes == null)
            throw new ArgumentNullException(nameof(utf8Bytes));
        var text = Encoding.UTF8.GetString(utf8Bytes);
        return Decode<T>(text, options ?? new ToonDecodeOptions());
    }

    /// <summary>
    /// Decodes TOON data from a stream (UTF-8) into a ToonNode with default options.
    /// </summary>
    /// <param name="stream">The input stream to read from.</param>
    /// <returns>The decoded ToonNode object.</returns>
    public static ToonNode? Decode(Stream stream)
    {
        return Decode(stream, DefaultOptions);
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

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var text = reader.ReadToEnd();

        return Decode(text, options ?? new ToonDecodeOptions());
    }

    /// <summary>
    /// Decodes TOON data from a stream (UTF-8) into the specified type with default options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="stream">The input stream to read from.</param>
    public static T? Decode<T>(Stream stream)
    {
        return Decode<T>(stream, DefaultOptions);
    }

    /// <summary>
    /// Decodes TOON data from a stream (UTF-8) into the specified type with custom options.
    /// </summary>
    /// <typeparam name="T">Target type to deserialize into.</typeparam>
    /// <param name="stream">The input stream to read from.</param>
    /// <param name="options">Decoding options to customize parsing behavior.</param>
    public static T? Decode<T>(Stream stream, ToonDecodeOptions? options)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
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
        return DecodeAsync(stream, DefaultOptions, cancellationToken);
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

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        return await DecodeAsync(text, options ?? new ToonDecodeOptions(), cancellationToken: cancellationToken);
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
        return DecodeAsync<T>(stream, DefaultOptions, cancellationToken);
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

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        return await DecodeAsync<T>(text, options ?? new ToonDecodeOptions(), cancellationToken: cancellationToken);
    }

    #endregion
}
