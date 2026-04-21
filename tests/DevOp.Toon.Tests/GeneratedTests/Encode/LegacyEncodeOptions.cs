using DevOp.Toon;

namespace DevOp.Toon.Tests.Encode;

internal sealed class LegacyToonEncodeOptions : ToonEncodeOptions
{
    public LegacyToonEncodeOptions()
    {
        Indent = 2;
        Delimiter = ToonDelimiter.COMMA;
        KeyFolding = ToonKeyFolding.Off;
        ObjectArrayLayout = ToonObjectArrayLayout.Auto;
        IgnoreNullOrEmpty = true;
        ExcludeEmptyArrays = true;
    }
}

internal sealed class LegacyDefaultToonEncodeOptions : ToonEncodeOptions
{
    public LegacyDefaultToonEncodeOptions()
    {
        Indent = 2;
        Delimiter = ToonDelimiter.COMMA;
        KeyFolding = ToonKeyFolding.Safe;
        ObjectArrayLayout = ToonObjectArrayLayout.Columnar;
        IgnoreNullOrEmpty = true;
        ExcludeEmptyArrays = true;
    }
}
