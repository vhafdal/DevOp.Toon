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
    /// <typeparam name="T">The CLR type of the value to encode.</typeparam>
    /// <param name="data">The value to encode. Public readable properties are emitted as TOON object fields.</param>
    /// <returns>A TOON-formatted string.</returns>
    string Encode<T>(T data);

    /// <summary>
    /// Encodes a value using the provided encode options.
    /// </summary>
    /// <typeparam name="T">The CLR type of the value to encode.</typeparam>
    /// <param name="data">The value to encode. Public readable properties are emitted as TOON object fields.</param>
    /// <param name="options">The encode options to apply for this operation.</param>
    /// <returns>A TOON-formatted string.</returns>
    string Encode<T>(T data, ToonEncodeOptions options);

    /// <summary>
    /// Encodes a value asynchronously using the service's configured default encode options.
    /// </summary>
    /// <typeparam name="T">The CLR type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before encoding starts.</param>
    /// <returns>A task whose result is a TOON-formatted string.</returns>
    Task<string> EncodeAsync<T>(T data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encodes a value asynchronously using the provided encode options.
    /// </summary>
    /// <typeparam name="T">The CLR type of the value to encode.</typeparam>
    /// <param name="data">The value to encode.</param>
    /// <param name="options">The encode options to apply for this operation.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before encoding starts.</param>
    /// <returns>A task whose result is a TOON-formatted string.</returns>
    Task<string> EncodeAsync<T>(T data, ToonEncodeOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts JSON text to TOON using the service's configured default encode options.
    /// </summary>
    /// <param name="json">The JSON payload to parse and encode.</param>
    /// <returns>A TOON-formatted string representing the JSON input.</returns>
    string Json2Toon(string json);

    /// <summary>
    /// Converts JSON text to TOON using the provided encode options.
    /// </summary>
    /// <param name="json">The JSON payload to parse and encode.</param>
    /// <param name="options">The encode options to apply for this operation.</param>
    /// <returns>A TOON-formatted string representing the JSON input.</returns>
    string Json2Toon(string json, ToonEncodeOptions options);

    /// <summary>
    /// Decodes TOON text into a <see cref="ToonNode"/> using the service's configured default decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <returns>The decoded node graph, or <see langword="null"/> when the payload represents a null value.</returns>
    ToonNode? Decode(string toonString);

    /// <summary>
    /// Decodes TOON text into a <see cref="ToonNode"/> using the provided decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="options">The decode options to apply for this operation.</param>
    /// <returns>The decoded node graph, or <see langword="null"/> when the payload represents a null value.</returns>
    ToonNode? Decode(string toonString, ToonDecodeOptions options);

    /// <summary>
    /// Decodes TOON text into the requested target type using the service's configured default decode options.
    /// </summary>
    /// <typeparam name="T">The CLR type to materialize from the TOON payload.</typeparam>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <returns>The decoded value, or <see langword="default"/> when the payload represents a null value.</returns>
    T? Decode<T>(string toonString);

    /// <summary>
    /// Decodes TOON text into the requested target type using the provided decode options.
    /// </summary>
    /// <typeparam name="T">The CLR type to materialize from the TOON payload.</typeparam>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="options">The decode options to apply for this operation.</param>
    /// <returns>The decoded value, or <see langword="default"/> when the payload represents a null value.</returns>
    T? Decode<T>(string toonString, ToonDecodeOptions options);

    /// <summary>
    /// Decodes TOON text asynchronously into a <see cref="ToonNode"/> using the service's configured default decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before decoding starts.</param>
    /// <returns>A task whose result is the decoded node graph.</returns>
    Task<ToonNode?> DecodeAsync(string toonString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decodes TOON text asynchronously into a <see cref="ToonNode"/> using the provided decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="options">The decode options to apply for this operation.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before decoding starts.</param>
    /// <returns>A task whose result is the decoded node graph.</returns>
    Task<ToonNode?> DecodeAsync(string toonString, ToonDecodeOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decodes TOON text asynchronously into the requested target type using the service's configured default decode options.
    /// </summary>
    /// <typeparam name="T">The CLR type to materialize from the TOON payload.</typeparam>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before decoding starts.</param>
    /// <returns>A task whose result is the decoded value.</returns>
    Task<T?> DecodeAsync<T>(string toonString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decodes TOON text asynchronously into the requested target type using the provided decode options.
    /// </summary>
    /// <typeparam name="T">The CLR type to materialize from the TOON payload.</typeparam>
    /// <param name="toonString">The TOON payload to decode.</param>
    /// <param name="options">The decode options to apply for this operation.</param>
    /// <param name="cancellationToken">A token that can cancel the operation before decoding starts.</param>
    /// <returns>A task whose result is the decoded value.</returns>
    Task<T?> DecodeAsync<T>(string toonString, ToonDecodeOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects likely decode options from the supplied TOON text.
    /// </summary>
    /// <param name="toonString">The TOON payload to inspect.</param>
    /// <returns>A new decode options instance with detected indentation and object-array layout settings.</returns>
    ToonDecodeOptions DetectDecodeOptions(string toonString);

    /// <summary>
    /// Converts TOON text to minified JSON using the service's configured default decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode and write as JSON.</param>
    /// <returns>A minified JSON string.</returns>
    string Toon2Json(string toonString);

    /// <summary>
    /// Converts TOON text to minified JSON using the provided decode options.
    /// </summary>
    /// <param name="toonString">The TOON payload to decode and write as JSON.</param>
    /// <param name="options">The decode options to apply for this operation.</param>
    /// <returns>A minified JSON string.</returns>
    string Toon2Json(string toonString, ToonDecodeOptions options);
}
