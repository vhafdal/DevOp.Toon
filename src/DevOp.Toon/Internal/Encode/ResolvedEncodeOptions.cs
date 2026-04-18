#nullable enable

using DevOp.Toon.Core;

namespace DevOp.Toon.Internal.Encode;

internal sealed class ResolvedEncodeOptions
{
    public int Indent { get; set; } = 2;
    public char Delimiter { get; set; } = Constants.COMMA;
    public ToonKeyFolding KeyFolding { get; set; } = ToonKeyFolding.Off;
    public int FlattenDepth { get; set; } = int.MaxValue;
    public ToonObjectArrayLayout ObjectArrayLayout { get; set; } = ToonObjectArrayLayout.Auto;
    public bool IgnoreNullOrEmpty { get; set; } = false;
    public bool ExcludeEmptyArrays { get; set; } = false;
}
