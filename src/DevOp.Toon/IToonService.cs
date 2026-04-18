#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace DevOp.Toon;

/// <summary>
/// Service abstraction over TOON encode/decode operations.
/// </summary>
public interface IToonService
{
    /// <summary>
    /// Encodes a value using the service's configured default encode options.
    /// </summary>
    string Encode<T>(T data);

    /// <summary>
    /// Encodes a value using the provided encode options.
    /// </summary>
    string Encode<T>(T data, ToonEncodeOptions options);

    /// <summary>
    /// Encodes a value asynchronously using the service's configured default encode options.
    /// </summary>
    Task<string> EncodeAsync<T>(T data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encodes a value asynchronously using the provided encode options.
    /// </summary>
    Task<string> EncodeAsync<T>(T data, ToonEncodeOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts JSON text to TOON using the service's configured default encode options.
    /// </summary>
    string Json2Toon(string json);

    /// <summary>
    /// Converts JSON text to TOON using the provided encode options.
    /// </summary>
    string Json2Toon(string json, ToonEncodeOptions options);

    /// <summary>
    /// Decodes TOON text into a <see cref="ToonNode"/> using the service's configured default decode options.
    /// </summary>
    ToonNode? Decode(string toonString);

    /// <summary>
    /// Decodes TOON text into a <see cref="ToonNode"/> using the provided decode options.
    /// </summary>
    ToonNode? Decode(string toonString, ToonDecodeOptions options);

    /// <summary>
    /// Decodes TOON text into the requested target type using the service's configured default decode options.
    /// </summary>
    T? Decode<T>(string toonString);

    /// <summary>
    /// Decodes TOON text into the requested target type using the provided decode options.
    /// </summary>
    T? Decode<T>(string toonString, ToonDecodeOptions options);

    /// <summary>
    /// Decodes TOON text asynchronously into a <see cref="ToonNode"/> using the service's configured default decode options.
    /// </summary>
    Task<ToonNode?> DecodeAsync(string toonString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decodes TOON text asynchronously into a <see cref="ToonNode"/> using the provided decode options.
    /// </summary>
    Task<ToonNode?> DecodeAsync(string toonString, ToonDecodeOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decodes TOON text asynchronously into the requested target type using the service's configured default decode options.
    /// </summary>
    Task<T?> DecodeAsync<T>(string toonString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decodes TOON text asynchronously into the requested target type using the provided decode options.
    /// </summary>
    Task<T?> DecodeAsync<T>(string toonString, ToonDecodeOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects likely decode options from the supplied TOON text.
    /// </summary>
    ToonDecodeOptions DetectDecodeOptions(string toonString);

    /// <summary>
    /// Converts TOON text to minified JSON using the service's configured default decode options.
    /// </summary>
    string Toon2Json(string toonString);

    /// <summary>
    /// Converts TOON text to minified JSON using the provided decode options.
    /// </summary>
    string Toon2Json(string toonString, ToonDecodeOptions options);
}
