#nullable enable
namespace DevOp.Toon.Internal.Decode;

internal readonly struct V4SourceLine
{
    internal readonly int Start;
    internal readonly int Length;
    internal readonly int Depth;
    internal readonly int Number;
    internal V4SourceLine(int start, int length, int depth, int number)
    { Start = start; Length = length; Depth = depth; Number = number; }
}
