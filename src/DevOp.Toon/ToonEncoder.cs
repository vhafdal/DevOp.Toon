#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOp.Toon.Core;
using DevOp.Toon;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon;

/// <summary>
/// Encodes data structures into TOON format.
/// </summary>
public static class ToonEncoder
{
    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

    private static ToonEncodeOptions defaultOptions = new ToonEncodeOptions()
    {
        Indent = 2,
        KeyFolding = ToonKeyFolding.Safe,
        ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
        Delimiter = ToonDelimiter.COMMA
    };

    /// <summary>
    /// Gets or sets the default encode options used by overloads that do not accept an explicit options argument.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
    public static ToonEncodeOptions DefaultOptions
    {
        get => defaultOptions;
        set => defaultOptions = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Encodes the specified object into TOON format with default options.
    /// </summary>
    /// <param name="data">The object to encode.</param>
    /// <returns>A TOON-formatted string representation of the object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string Encode(object? data)
    {
        return Encode(data, DefaultOptions);
    }

    /// <summary>
    /// Encodes the specified value into TOON format with default options (generic overload).
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <returns>A TOON-formatted string representation of the value.</returns>
    public static string Encode<T>(T data)
    {
        return Encode(data, DefaultOptions);
    }

    /// <summary>
    /// Encodes the specified object into TOON format with custom options.
    /// </summary>
    /// <param name="data">The object to encode.</param>
    /// <param name="options">Encoding options to customize the output format.</param>
    /// <returns>A TOON-formatted string representation of the object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string Encode(object? data, ToonEncodeOptions? options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ValidateIndent(options.Indent);

        var resolvedOptions = ResolveOptions(options);
        return EncodeResolved(data, resolvedOptions);
    }

    /// <summary>
    /// Encodes the specified value into TOON format with custom options (generic overload).
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">Encoding options to customize the output format.</param>
    /// <returns>A TOON-formatted string representation of the value.</returns>
    public static string Encode<T>(T data, ToonEncodeOptions? options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ValidateIndent(options.Indent);

        var resolvedOptions = ResolveOptions(options);
        return EncodeResolved(data, resolvedOptions);
    }

    /// <summary>
    /// Converts a JSON string to TOON using the default encode options.
    /// </summary>
    /// <param name="json">The JSON string to convert.</param>
    /// <returns>A TOON-formatted string representation of the JSON input.</returns>
    /// <exception cref="ArgumentNullException">Thrown when json is null.</exception>
    /// <exception cref="FormatException">Thrown when the JSON input is invalid.</exception>
    public static string Json2Toon(string json)
    {
        return Json2Toon(json, DefaultOptions);
    }

    /// <summary>
    /// Converts a JSON string to TOON using custom encode options.
    /// </summary>
    /// <param name="json">The JSON string to convert.</param>
    /// <param name="options">Encoding options to customize the TOON output.</param>
    /// <returns>A TOON-formatted string representation of the JSON input.</returns>
    /// <exception cref="ArgumentNullException">Thrown when json or options is null.</exception>
    /// <exception cref="FormatException">Thrown when the JSON input is invalid.</exception>
    public static string Json2Toon(string json, ToonEncodeOptions? options)
    {
        if (json == null)
            throw new ArgumentNullException(nameof(json));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ValidateIndent(options.Indent);

        var resolvedOptions = ResolveOptions(options);
        var node = ToonNode.Parse(json);
        var nativeNode = ToonNodeConverter.ToNativeNode(node);
        return NativeEncoders.EncodeValue(nativeNode, resolvedOptions);
    }

    internal static string EncodeResolved(object? data, ResolvedEncodeOptions resolvedOptions)
    {
        if (ClrObjectArrayFastEncoder.TryEncode(data, resolvedOptions, out var fastEncoded))
        {
            return fastEncoded;
        }

        var normalized = NativeNormalize.Normalize(data);
        return NativeEncoders.EncodeValue(normalized, resolvedOptions);
    }

    internal static string EncodeResolved<T>(T data, ResolvedEncodeOptions resolvedOptions)
    {
        if (ClrObjectArrayFastEncoder.TryEncode(data, resolvedOptions, out var fastEncoded))
        {
            return fastEncoded;
        }

        var normalized = NativeNormalize.Normalize<T>(data);
        return NativeEncoders.EncodeValue(normalized, resolvedOptions);
    }

    /// <summary>
    /// Encodes the specified object into UTF-8 bytes with default options.
    /// </summary>
    /// <param name="data">The object to encode.</param>
    /// <returns>UTF-8 encoded TOON bytes.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static byte[] EncodeToBytes(object? data)
    {
        return EncodeToBytes(data, DefaultOptions);
    }

    /// <summary>
    /// Encodes the specified object into UTF-8 bytes with custom options.
    /// </summary>
    /// <param name="data">The object to encode.</param>
    /// <param name="options">Encoding options to customize the output format.</param>
    /// <returns>UTF-8 encoded TOON bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static byte[] EncodeToBytes(object? data, ToonEncodeOptions? options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ValidateIndent(options.Indent);
        var resolvedOptions = ResolveOptions(options);
        return EncodeToBytesResolved(data, resolvedOptions);
    }

    /// <summary>
    /// Encodes the specified value into UTF-8 bytes with default options (generic overload).
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <returns>UTF-8 encoded TOON bytes.</returns>
    public static byte[] EncodeToBytes<T>(T data)
    {
        return EncodeToBytes(data, DefaultOptions);
    }

    /// <summary>
    /// Encodes the specified value into UTF-8 bytes with custom options (generic overload).
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">Encoding options to customize the output format.</param>
    /// <returns>UTF-8 encoded TOON bytes.</returns>
    public static byte[] EncodeToBytes<T>(T data, ToonEncodeOptions? options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ValidateIndent(options.Indent);
        var resolvedOptions = ResolveOptions(options);
        return EncodeToBytesResolved(data, resolvedOptions);
    }

    private static void ValidateIndent(int indent)
    {
        if (indent <= 0)
            throw new ArgumentOutOfRangeException(nameof(indent), indent, "Indent must be greater than 0.");
    }

    /// <summary>
    /// Encodes the specified object and writes UTF-8 bytes to the destination stream using default options.
    /// </summary>
    /// <param name="data">The object to encode.</param>
    /// <param name="destination">The destination stream to write to. The stream is not disposed.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void EncodeToStream(object? data, Stream destination)
    {
        EncodeToStream(data, destination, DefaultOptions);
    }

    /// <summary>
    /// Encodes the specified object and writes UTF-8 bytes to the destination stream using custom options.
    /// </summary>
    /// <param name="data">The object to encode.</param>
    /// <param name="destination">The destination stream to write to. The stream is not disposed.</param>
    /// <param name="options">Encoding options to customize the output format.</param>
    /// <exception cref="ArgumentNullException">Thrown when destination or options is null.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void EncodeToStream(object? data, Stream destination, ToonEncodeOptions? options)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ValidateIndent(options.Indent);
        EncodeToStreamResolved(data, destination, ResolveOptions(options));
    }

    /// <summary>
    /// Encodes the specified value and writes UTF-8 bytes to the destination stream using default options (generic overload).
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="destination">The destination stream to write to. The stream is not disposed.</param>
    public static void EncodeToStream<T>(T data, Stream destination)
    {
        EncodeToStream(data, destination, DefaultOptions);
    }

    /// <summary>
    /// Encodes the specified value and writes UTF-8 bytes to the destination stream using custom options (generic overload).
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="destination">The destination stream to write to. The stream is not disposed.</param>
    /// <param name="options">Encoding options to customize the output format.</param>
    public static void EncodeToStream<T>(T data, Stream destination, ToonEncodeOptions? options)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ValidateIndent(options.Indent);
        EncodeToStreamResolved(data, destination, ResolveOptions(options));
    }

    #region Async Methods

    /// <summary>
    /// Asynchronously encodes the specified value into TOON format with default options.
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the TOON-formatted string.</returns>
    public static Task<string> EncodeAsync<T>(T data, CancellationToken cancellationToken = default)
    {
        return EncodeAsync(data, DefaultOptions, cancellationToken);
    }

    /// <summary>
    /// Asynchronously encodes the specified value into TOON format with custom options.
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">Encoding options to customize the output format.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the TOON-formatted string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public static Task<string> EncodeAsync<T>(T data, ToonEncodeOptions? options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Encode(data, options);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Asynchronously encodes the specified value into UTF-8 bytes with default options.
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the UTF-8 encoded TOON bytes.</returns>
    public static Task<byte[]> EncodeToBytesAsync<T>(T data, CancellationToken cancellationToken = default)
    {
        return EncodeToBytesAsync(data, DefaultOptions, cancellationToken);
    }

    /// <summary>
    /// Asynchronously encodes the specified value into UTF-8 bytes with custom options.
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">Encoding options to customize the output format.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the UTF-8 encoded TOON bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public static Task<byte[]> EncodeToBytesAsync<T>(T data, ToonEncodeOptions? options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = EncodeToBytes(data, options);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Asynchronously encodes the specified value and writes UTF-8 bytes to the destination stream using default options.
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="destination">The destination stream to write to. The stream is not disposed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    public static Task EncodeToStreamAsync<T>(T data, Stream destination, CancellationToken cancellationToken = default)
    {
        return EncodeToStreamAsync(data, destination, DefaultOptions, cancellationToken);
    }

    /// <summary>
    /// Asynchronously encodes the specified value and writes UTF-8 bytes to the destination stream using custom options.
    /// </summary>
    /// <typeparam name="T">Type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="destination">The destination stream to write to. The stream is not disposed.</param>
    /// <param name="options">Encoding options to customize the output format.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when destination or options is null.</exception>
    public static async Task EncodeToStreamAsync<T>(T data, Stream destination, ToonEncodeOptions? options, CancellationToken cancellationToken = default)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ValidateIndent(options.Indent);
        cancellationToken.ThrowIfCancellationRequested();
        await EncodeToStreamResolvedAsync(data, destination, ResolveOptions(options), cancellationToken).ConfigureAwait(false);
    }

    private static ResolvedEncodeOptions ResolveOptions(ToonEncodeOptions options)
    {
        return options.GetResolvedOptions();
    }

    private static bool TryEncodeToBytesFast(object? data, ResolvedEncodeOptions options, out byte[] bytes)
    {
        if (!ClrObjectArrayFastEncoder.TryEncodeToCompactBuffer(data, options, out var compactWriter))
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        var (charBuffer, charCount) = compactWriter.GetCharBuffer();
        bytes = Utf8WithoutBom.GetBytes(charBuffer, 0, charCount);
        compactWriter.Dispose();
        return true;
    }

    internal static byte[] EncodeToBytesResolved(object? data, ResolvedEncodeOptions resolvedOptions)
    {
        if (TryEncodeToBytesFast(data, resolvedOptions, out var fastBytes))
        {
            return fastBytes;
        }

        var text = EncodeResolved(data, resolvedOptions);
        return Utf8WithoutBom.GetBytes(text);
    }

    internal static void EncodeToStreamResolved(object? data, Stream destination, ResolvedEncodeOptions resolvedOptions)
    {
        // Large buffer avoids frequent StreamWriter flushes for a ~1.5 MB output.
        using var writer = new StreamWriter(destination, Utf8WithoutBom, 4096, leaveOpen: true);
        if (ClrObjectArrayFastEncoder.TryWriteToTextWriter(data, resolvedOptions, writer))
        {
            writer.Flush();
            return;
        }

        // Fallback: exotic types that the fast path doesn't handle.
        var text = NativeEncoders.EncodeValue(NativeNormalize.Normalize(data), resolvedOptions);
        writer.Write(text);
        writer.Flush();
    }

    internal static async Task EncodeToStreamResolvedAsync(object? data, Stream destination, ResolvedEncodeOptions resolvedOptions, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var writer = new StreamWriter(destination, Utf8WithoutBom, 4096, leaveOpen: true);
        if (await ClrObjectArrayFastEncoder.TryWriteToTextWriterAsync(data, resolvedOptions, writer, cancellationToken).ConfigureAwait(false))
        {
#if NET8_0_OR_GREATER
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
#else
            await writer.FlushAsync().ConfigureAwait(false);
#endif
            return;
        }

        // Fallback: exotic types that the fast path doesn't handle.
        var text = NativeEncoders.EncodeValue(NativeNormalize.Normalize(data), resolvedOptions);
        await writer.WriteAsync(text).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    #endregion
}
