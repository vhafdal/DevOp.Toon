namespace DevOp.Toon.Benchmarks;

internal static class ToonBenchmarkProfiles
{
    public static ToonEncodeOptions ResolveEncodeOptions(string mode)
    {
        if (string.Equals(mode, "toon-native", StringComparison.OrdinalIgnoreCase))
        {
            return new ToonEncodeOptions
            {
                Indent = 1,
                KeyFolding = ToonKeyFolding.Safe,
                ObjectArrayLayout = ToonObjectArrayLayout.Auto,
                IgnoreNullOrEmpty = false,
                ExcludeEmptyArrays = false,
                Delimiter = ToonDelimiter.COMMA
            };
        }

        if (string.Equals(mode, "vlink-toon", StringComparison.OrdinalIgnoreCase))
        {
            return new ToonEncodeOptions
            {
                Indent = 1,
                KeyFolding = ToonKeyFolding.Safe,
                ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
                IgnoreNullOrEmpty = true,
                ExcludeEmptyArrays = true,
                Delimiter = ToonDelimiter.COMMA
            };
        }

        if (string.Equals(mode, "readable", StringComparison.OrdinalIgnoreCase))
        {
            return new ToonEncodeOptions
            {
                Indent = 2,
                KeyFolding = ToonKeyFolding.Safe,
                ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
                Delimiter = ToonDelimiter.COMMA
            };
        }

        if (string.Equals(mode, "compact", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "default", StringComparison.OrdinalIgnoreCase))
        {
            return new ToonEncodeOptions
            {
                Indent = 1,
                KeyFolding = ToonKeyFolding.Safe,
                ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
                Delimiter = ToonDelimiter.COMMA
            };
        }

        return new ToonEncodeOptions
        {
            Indent = 1,
            KeyFolding = ToonKeyFolding.Safe,
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
            Delimiter = ToonDelimiter.COMMA
        };
    }

    public static string GetModeName(ToonEncodeOptions options)
    {
        if (options.ObjectArrayLayout == ToonObjectArrayLayout.Auto &&
            !options.IgnoreNullOrEmpty &&
            !options.ExcludeEmptyArrays)
        {
            return "toon-native";
        }

        if (options.ObjectArrayLayout == ToonObjectArrayLayout.Columnar &&
            options.IgnoreNullOrEmpty &&
            options.ExcludeEmptyArrays)
        {
            return "vlink-toon";
        }

        return options.Indent == 1 ? "compact" : "readable";
    }
}
