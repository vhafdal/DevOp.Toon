#nullable enable

using DevOp.Toon.Core;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Immutable-by-convention copy of public encode options with enum values resolved to hot-path primitives.
/// </summary>
internal sealed class ResolvedEncodeOptions
{
    /// <summary>Gets or sets the TOON protocol version.</summary>
    public ToonProtocolVersion ProtocolVersion { get; set; } = ToonProtocolVersion.V4_4;
    /// <summary>Gets or sets the number of spaces per indentation level.</summary>
    public int Indent { get; set; } = 1;
    /// <summary>Gets or sets the delimiter character used in inline and tabular rows.</summary>
    public char Delimiter { get; set; } = Constants.COMMA;
    /// <summary>Gets or sets the key folding mode.</summary>
    public ToonKeyFolding KeyFolding { get; set; } = ToonKeyFolding.Off;
    /// <summary>Gets or sets the maximum number of path segments to fold.</summary>
    public int FlattenDepth { get; set; } = int.MaxValue;
    /// <summary>Gets or sets the object-array layout strategy.</summary>
    public ToonObjectArrayLayout ObjectArrayLayout { get; set; } = ToonObjectArrayLayout.Auto;
    /// <summary>Gets or sets how CLR byte arrays are encoded.</summary>
    public ToonByteArrayFormat ByteArrayFormat { get; set; } = ToonByteArrayFormat.Base64String;
    /// <summary>Gets or sets whether all-null and all-empty-string columns are suppressed.</summary>
    public bool IgnoreNullOrEmpty { get; set; } = false;
    /// <summary>Gets or sets whether empty array properties are omitted.</summary>
    public bool ExcludeEmptyArrays { get; set; } = false;
    /// <summary>Gets or sets whether CustomV4_4 tables write row-invariant columns as header constants.</summary>
    public bool ConstantColumns { get; set; } = false;
    /// <summary>Gets whether annotation keys such as <c>@id</c> and <c>Raven-Clr-Type</c> are written unquoted.</summary>
    public bool AnnotationKeys => ProtocolVersion == ToonProtocolVersion.CustomV4_4;
    /// <summary>Gets whether the options select an official 4.x or custom 4.4 profile.</summary>
    public bool IsV4 => IsV4Protocol(ProtocolVersion);

    internal static bool IsV4Protocol(ToonProtocolVersion version)
        => version >= ToonProtocolVersion.V4_0 && version <= ToonProtocolVersion.V4_4 || version == ToonProtocolVersion.CustomV4_4;
}
