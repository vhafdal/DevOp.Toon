#nullable enable
using DevOp.Toon.Core;
using DevOp.Toon.Internal.Decode;

namespace DevOp.Toon;

/// <summary>
/// Options that control how TOON text is parsed and materialized.
/// </summary>
public class ToonDecodeOptions
{
    private ResolvedDecodeOptions? cachedResolvedOptions;

    internal ResolvedDecodeOptions GetResolvedOptions()
    {
        var cached = cachedResolvedOptions;
        if (cached != null && cached.ProtocolVersion == ProtocolVersion && cached.Indent == Indent && cached.Strict == Strict && cached.ExpandPaths == ExpandPaths && cached.ObjectArrayLayout == ObjectArrayLayout) return cached;
        return cachedResolvedOptions = new ResolvedDecodeOptions
        {
            ProtocolVersion = ProtocolVersion,
            Indent = Indent,
            Strict = Strict,
            ExpandPaths = ExpandPaths,
            ObjectArrayLayout = ObjectArrayLayout
        };
    }

    /// <summary>
    /// Gets or sets the TOON protocol version to expect during decoding.
    /// </summary>
    /// <remarks>The default value is <see cref="ToonProtocolVersion.Auto"/>.</remarks>
    public ToonProtocolVersion ProtocolVersion { get; set; } = ToonProtocolVersion.Auto;

    /// <summary>
    /// Gets or sets the number of spaces that represent one indentation level.
    /// </summary>
    /// <remarks>The default value is 2. Decoding throws when this value is less than or equal to zero.</remarks>
    public int Indent { get; set; } = 2;

    /// <summary>
    /// Gets or sets whether the decoder enforces strict structural validation.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="true"/>. Strict mode validates declared array lengths,
    /// tabular row counts, indentation, and other structural rules.
    /// </remarks>
    public bool Strict { get; set; } = true;

    /// <summary>
    /// Gets or sets how dotted object keys are handled during decoding.
    /// </summary>
    /// <remarks>
    /// The default value is <see cref="ToonPathExpansion.Off"/>, which treats dotted keys as literal property names.
    /// Use <see cref="ToonPathExpansion.Safe"/> to expand eligible unquoted dotted keys into nested objects.
    /// </remarks>
    public ToonPathExpansion ExpandPaths { get; set; } = ToonPathExpansion.Off;

    /// <summary>
    /// Gets or sets the expected object-array layout for decoded payloads.
    /// </summary>
    /// <remarks>
    /// The default value is <see cref="ToonObjectArrayLayout.Auto"/>. The decoder can read both classic and
    /// columnar object arrays; this property is also populated by option detection to describe the detected layout.
    /// </remarks>
    public ToonObjectArrayLayout ObjectArrayLayout { get; set; } = ToonObjectArrayLayout.Auto;

    /// <summary>
    /// Creates a shallow clone of the current options instance.
    /// </summary>
    /// <returns>A new <see cref="ToonDecodeOptions"/> instance with identical settings.</returns>
    public ToonDecodeOptions Clone()
    {
        return new ToonDecodeOptions
        {
            ProtocolVersion = ProtocolVersion,
            Indent = Indent,
            Strict = Strict,
            ExpandPaths = ExpandPaths,
            ObjectArrayLayout = ObjectArrayLayout
        };
    }
}
