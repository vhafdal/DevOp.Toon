using DevOp.Toon.Core;

namespace DevOp.Toon.Tests.Decode;

internal sealed class LegacyToonDecodeOptions : ToonDecodeOptions
{
    public LegacyToonDecodeOptions()
    {
        ProtocolVersion = ToonProtocolVersion.CustomLegacy;
        Indent = 2;
    }
}
