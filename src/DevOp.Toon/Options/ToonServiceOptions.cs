#nullable enable
using DevOp.Toon.Core;

namespace DevOp.Toon;

/// <summary>
/// Combined service options for TOON encoding and decoding.
/// </summary>
public sealed class ToonServiceOptions
{
    /// <summary>
    /// Gets the encoder options used by the service.
    /// </summary>
    public ToonEncodeOptions Encode { get; } = new();

    /// <summary>
    /// Gets the decoder options used by the service.
    /// </summary>
    public ToonDecodeOptions Decode { get; } = new();

    /// <summary>
    /// Shared indentation width applied to both encode and decode defaults.
    /// </summary>
    public int Indent
    {
        get => Encode.Indent;
        set
        {
            Encode.Indent = value;
            Decode.Indent = value;
        }
    }

    /// <summary>
    /// Encoder delimiter setting.
    /// </summary>
    public ToonDelimiter Delimiter
    {
        get => Encode.Delimiter;
        set => Encode.Delimiter = value;
    }

    /// <summary>
    /// Encoder key folding setting.
    /// </summary>
    public ToonKeyFolding KeyFolding
    {
        get => Encode.KeyFolding;
        set => Encode.KeyFolding = value;
    }

    /// <summary>
    /// Encoder flatten depth setting.
    /// </summary>
    public int? FlattenDepth
    {
        get => Encode.FlattenDepth;
        set => Encode.FlattenDepth = value;
    }

    /// <summary>
    /// Object array layout setting.
    /// </summary>
    public ToonObjectArrayLayout ObjectArrayLayout
    {
        get => Encode.ObjectArrayLayout;
        set => Encode.ObjectArrayLayout = value;
    }

    /// <summary>
    /// Decoder strict mode setting.
    /// </summary>
    public bool Strict
    {
        get => Decode.Strict;
        set => Decode.Strict = value;
    }

    /// <summary>
    /// Decoder path expansion setting.
    /// </summary>
    public ToonPathExpansion ExpandPaths
    {
        get => Decode.ExpandPaths;
        set => Decode.ExpandPaths = value;
    }

    internal ToonEncodeOptions CreateEncodeOptions()
    {
        return new ToonEncodeOptions
        {
            Indent = Encode.Indent,
            Delimiter = Encode.Delimiter,
            KeyFolding = Encode.KeyFolding,
            FlattenDepth = Encode.FlattenDepth,
            ObjectArrayLayout = Encode.ObjectArrayLayout,
            IgnoreNullOrEmpty = Encode.IgnoreNullOrEmpty,
            ExcludeEmptyArrays = Encode.ExcludeEmptyArrays
        };
    }

    internal ToonDecodeOptions CreateDecodeOptions()
    {
        return new ToonDecodeOptions
        {
            Indent = Decode.Indent,
            Strict = Decode.Strict,
            ExpandPaths = Decode.ExpandPaths,
            ObjectArrayLayout = Decode.ObjectArrayLayout
        };
    }
}
