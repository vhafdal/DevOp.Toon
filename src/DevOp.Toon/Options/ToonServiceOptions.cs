#nullable enable
using DevOp.Toon.Core;

namespace DevOp.Toon;

/// <summary>
/// Combined encode and decode options used by <see cref="ToonService"/> and dependency injection registration.
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
    /// Gets or sets the indentation width applied to both <see cref="Encode"/> and <see cref="Decode"/> defaults.
    /// </summary>
    /// <remarks>Use this convenience property when the service should read and write the same indentation width.</remarks>
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
    /// Gets or sets the delimiter used by the service's default encoder options.
    /// </summary>
    public ToonDelimiter Delimiter
    {
        get => Encode.Delimiter;
        set => Encode.Delimiter = value;
    }

    /// <summary>
    /// Gets or sets the key folding mode used by the service's default encoder options.
    /// </summary>
    public ToonKeyFolding KeyFolding
    {
        get => Encode.KeyFolding;
        set => Encode.KeyFolding = value;
    }

    /// <summary>
    /// Gets or sets the maximum folded path depth used by the service's default encoder options.
    /// </summary>
    public int? FlattenDepth
    {
        get => Encode.FlattenDepth;
        set => Encode.FlattenDepth = value;
    }

    /// <summary>
    /// Gets or sets the object-array layout used by the service's default encoder options.
    /// </summary>
    public ToonObjectArrayLayout ObjectArrayLayout
    {
        get => Encode.ObjectArrayLayout;
        set => Encode.ObjectArrayLayout = value;
    }

    /// <summary>
    /// Gets or sets whether the service's default decoder options enforce strict structural validation.
    /// </summary>
    public bool Strict
    {
        get => Decode.Strict;
        set => Decode.Strict = value;
    }

    /// <summary>
    /// Gets or sets how the service's default decoder options handle dotted object keys.
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
