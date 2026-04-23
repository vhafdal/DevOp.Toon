#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon;

/// <summary>
/// Default service implementation for TOON encode/decode operations.
/// </summary>
public sealed class ToonService : IToonService
{
    private readonly ToonEncodeOptions encodeOptions;
    private readonly ResolvedEncodeOptions resolvedEncodeOptions;
    private readonly ToonDecodeOptions decodeOptions;

    /// <summary>
    /// Creates a service instance with default TOON service options.
    /// </summary>
    public ToonService()
        : this(new ToonServiceOptions())
    {
    }

    /// <summary>
    /// Creates a service instance with the supplied TOON service options.
    /// </summary>
    public ToonService(ToonServiceOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        encodeOptions = options.CreateEncodeOptions();
        resolvedEncodeOptions = encodeOptions.GetResolvedOptions();
        decodeOptions = options.CreateDecodeOptions();
    }

    /// <summary>
    /// Encodes a value to TOON using the service's default encode options.
    /// </summary>
    /// <typeparam name="T">The value type to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <returns>The encoded TOON payload.</returns>
    public string Encode<T>(T data)
    {
        return ToonEncoder.EncodeResolved(data, resolvedEncodeOptions);
    }

    /// <summary>
    /// Encodes a value to TOON using the supplied encode options.
    /// </summary>
    /// <typeparam name="T">The value type to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">The encode options to apply.</param>
    /// <returns>The encoded TOON payload.</returns>
    public string Encode<T>(T data, ToonEncodeOptions options)
    {
        return ToonEncoder.Encode(data, options);
    }

    /// <summary>
    /// Encodes a value to TOON asynchronously using the service's default encode options.
    /// </summary>
    /// <typeparam name="T">The value type to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task containing the encoded TOON payload.</returns>
    public Task<string> EncodeAsync<T>(T data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ToonEncoder.EncodeResolved(data, resolvedEncodeOptions));
    }

    /// <summary>
    /// Encodes a value to TOON asynchronously using the supplied encode options.
    /// </summary>
    /// <typeparam name="T">The value type to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">The encode options to apply.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task containing the encoded TOON payload.</returns>
    public Task<string> EncodeAsync<T>(T data, ToonEncodeOptions options, CancellationToken cancellationToken = default)
    {
        return ToonEncoder.EncodeAsync(data, options, cancellationToken);
    }

    /// <summary>
    /// Converts a JSON payload to TOON using the service's default encode options.
    /// </summary>
    /// <param name="json">The JSON payload to convert.</param>
    /// <returns>The encoded TOON payload.</returns>
    public string Json2Toon(string json)
    {
        return ToonEncoder.Json2Toon(json, encodeOptions);
    }

    /// <summary>
    /// Converts a JSON payload to TOON using the supplied encode options.
    /// </summary>
    /// <param name="json">The JSON payload to convert.</param>
    /// <param name="options">The encode options to apply.</param>
    /// <returns>The encoded TOON payload.</returns>
    public string Json2Toon(string json, ToonEncodeOptions options)
    {
        return ToonEncoder.Json2Toon(json, options);
    }

    /// <summary>
    /// Decodes a TOON payload to the DOM representation using the service's default decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <returns>The decoded node graph.</returns>
    public ToonNode? Decode(string toonString)
    {
        return ToonDecoder.Decode(toonString, DetectDecodeOptions(toonString));
    }

    /// <summary>
    /// Decodes a TOON payload to the DOM representation using the supplied decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="options">The decode options to apply.</param>
    /// <returns>The decoded node graph.</returns>
    public ToonNode? Decode(string toonString, ToonDecodeOptions options)
    {
        return ToonDecoder.Decode(toonString, options);
    }

    /// <summary>
    /// Decodes a TOON payload to the specified CLR type using the service's default decode options.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <returns>The decoded value.</returns>
    public T? Decode<T>(string toonString)
    {
        return ToonDecoder.Decode<T>(toonString, DetectDecodeOptions(toonString));
    }

    /// <summary>
    /// Decodes a TOON payload to the specified CLR type using the supplied decode options.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="options">The decode options to apply.</param>
    /// <returns>The decoded value.</returns>
    public T? Decode<T>(string toonString, ToonDecodeOptions options)
    {
        return ToonDecoder.Decode<T>(toonString, options);
    }

    /// <summary>
    /// Decodes a TOON payload to the DOM representation asynchronously using the service's default decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task containing the decoded node graph.</returns>
    public Task<ToonNode?> DecodeAsync(string toonString, CancellationToken cancellationToken = default)
    {
        return ToonDecoder.DecodeAsync(toonString, DetectDecodeOptions(toonString), cancellationToken);
    }

    /// <summary>
    /// Decodes a TOON payload to the DOM representation asynchronously using the supplied decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="options">The decode options to apply.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task containing the decoded node graph.</returns>
    public Task<ToonNode?> DecodeAsync(string toonString, ToonDecodeOptions options, CancellationToken cancellationToken = default)
    {
        return ToonDecoder.DecodeAsync(toonString, options, cancellationToken);
    }

    /// <summary>
    /// Decodes a TOON payload to the specified CLR type asynchronously using the service's default decode options.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task containing the decoded value.</returns>
    public Task<T?> DecodeAsync<T>(string toonString, CancellationToken cancellationToken = default)
    {
        return ToonDecoder.DecodeAsync<T>(toonString, DetectDecodeOptions(toonString), cancellationToken);
    }

    /// <summary>
    /// Decodes a TOON payload to the specified CLR type asynchronously using the supplied decode options.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="options">The decode options to apply.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task containing the decoded value.</returns>
    public Task<T?> DecodeAsync<T>(string toonString, ToonDecodeOptions options, CancellationToken cancellationToken = default)
    {
        return ToonDecoder.DecodeAsync<T>(toonString, options, cancellationToken);
    }

    /// <summary>
    /// Detects decode options from the supplied TOON payload.
    /// </summary>
    /// <param name="toonString">The TOON payload to inspect.</param>
    /// <returns>The detected decode options.</returns>
    public ToonDecodeOptions DetectDecodeOptions(string toonString)
    {
        return ToonDecoder.DetectOptions(toonString, CloneDecodeOptions(decodeOptions));
    }

    /// <summary>
    /// Converts a TOON payload to JSON using the service's default decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to convert.</param>
    /// <returns>The converted JSON payload.</returns>
    public string Toon2Json(string toonString)
    {
        return ToonDecoder.Toon2Json(toonString, DetectDecodeOptions(toonString));
    }

    /// <summary>
    /// Converts a TOON payload to JSON using the supplied decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to convert.</param>
    /// <param name="options">The decode options to apply.</param>
    /// <returns>The converted JSON payload.</returns>
    public string Toon2Json(string toonString, ToonDecodeOptions options)
    {
        return ToonDecoder.Toon2Json(toonString, options);
    }

    /// <summary>
    /// Deserializes a payload body into the specified CLR type using the content type to select the decoder.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="body">The raw payload string.</param>
    /// <param name="contentType">The media type of the payload.</param>
    /// <returns>The deserialized value.</returns>
    public T? Deserialize<T>(string body, string contentType)
    {
        if (IsToonContentType(contentType))
            return Decode<T>(body);

        if (IsJsonContentType(contentType))
            return Decode<T>(Json2Toon(body));

        throw new NotSupportedException(
            $"Content type '{contentType}' is not supported by Deserialize<T>. " +
            "Use a TOON (application/toon, text/toon) or JSON (application/json, text/json) content type.");
    }

    /// <summary>
    /// Deserializes a payload body into the specified CLR type using the content type to select the decoder,
    /// applying the provided decode options for the TOON decode step.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="body">The raw payload string.</param>
    /// <param name="contentType">The media type of the payload.</param>
    /// <param name="options">The decode options to apply when decoding the TOON representation.</param>
    /// <returns>The deserialized value.</returns>
    public T? Deserialize<T>(string body, string contentType, ToonDecodeOptions options)
    {
        if (IsToonContentType(contentType))
            return Decode<T>(body, options);

        if (IsJsonContentType(contentType))
            return Decode<T>(Json2Toon(body), options);

        throw new NotSupportedException(
            $"Content type '{contentType}' is not supported by Deserialize<T>. " +
            "Use a TOON (application/toon, text/toon) or JSON (application/json, text/json) content type.");
    }

    private static bool IsToonContentType(string contentType) =>
        contentType.Equals(ToonMediaTypes.Application, StringComparison.OrdinalIgnoreCase)
        || contentType.Equals(ToonMediaTypes.Text, StringComparison.OrdinalIgnoreCase)
        || contentType.EndsWith("+toon", StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonContentType(string contentType) =>
        contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
        || contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

    private static ToonDecodeOptions CloneDecodeOptions(ToonDecodeOptions options)
    {
        return new ToonDecodeOptions
        {
            Indent = options.Indent,
            Strict = options.Strict,
            ExpandPaths = options.ExpandPaths,
            ObjectArrayLayout = options.ObjectArrayLayout
        };
    }
}
