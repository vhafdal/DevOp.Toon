#nullable enable

using DevOp.Toon.Core;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Immutable-by-convention copy of public encode options with enum values resolved to hot-path primitives.
/// </summary>
internal sealed class ResolvedEncodeOptions
{
    /// <summary>Gets or sets the number of spaces per indentation level.</summary>
    public int Indent { get; set; } = 2;
    /// <summary>Gets or sets the delimiter character used in inline and tabular rows.</summary>
    public char Delimiter { get; set; } = Constants.COMMA;
    /// <summary>Gets or sets the key folding mode.</summary>
    public ToonKeyFolding KeyFolding { get; set; } = ToonKeyFolding.Off;
    /// <summary>Gets or sets the maximum number of path segments to fold.</summary>
    public int FlattenDepth { get; set; } = int.MaxValue;
    /// <summary>Gets or sets the object-array layout strategy.</summary>
    public ToonObjectArrayLayout ObjectArrayLayout { get; set; } = ToonObjectArrayLayout.Auto;
    /// <summary>Gets or sets how CLR byte arrays are encoded.</summary>
    public ToonByteArrayFormat ByteArrayFormat { get; set; } = ToonByteArrayFormat.NumericArray;
    /// <summary>Gets or sets whether all-null and all-empty-string columns are suppressed.</summary>
    public bool IgnoreNullOrEmpty { get; set; } = false;
    /// <summary>Gets or sets whether empty array properties are omitted.</summary>
    public bool ExcludeEmptyArrays { get; set; } = false;
}
