#nullable enable
using System;
using DevOp.Toon.Internal.Decode;

namespace DevOp.Toon;

/// <summary>
/// Detects likely decode options from TOON source text.
/// </summary>
public static class ToonDecoderOptionsDetector
{
    /// <summary>
    /// Detects likely decode options from TOON source text, preserving fallback values for settings that cannot be inferred.
    /// </summary>
    public static ToonDecodeOptions Detect(string toonString, ToonDecodeOptions? fallbackOptions = null)
    {
        if (toonString == null)
            throw new ArgumentNullException(nameof(toonString));

        var options = (fallbackOptions ?? new ToonDecodeOptions()).Clone();
        if (options.Indent <= 0) throw new ArgumentOutOfRangeException(nameof(fallbackOptions), options.Indent, "Indent must be greater than 0.");
        if (options.ProtocolVersion == DevOp.Toon.Core.ToonProtocolVersion.Auto)
        {
            var detection = V4ProtocolDetector.Detect(toonString, fallbackIndent: options.Indent);
            if (detection.Indent.HasValue) options.Indent = detection.Indent.Value;
            if (detection.RequiresHint) throw ToonFormatException.Syntax("The payload has version-dependent meanings. Supply ProtocolVersion explicitly.");
            options.ProtocolVersion = detection.Version;
        }
        else
        {
            var comments = options.ProtocolVersion is >= DevOp.Toon.Core.ToonProtocolVersion.V4_0 and <= DevOp.Toon.Core.ToonProtocolVersion.V4_4 or DevOp.Toon.Core.ToonProtocolVersion.CustomV4_4;
            var indent = DetectIndent(toonString, comments);
            if (indent.HasValue) options.Indent = indent.Value;
        }

        if (options.ProtocolVersion is DevOp.Toon.Core.ToonProtocolVersion.CustomLegacy or DevOp.Toon.Core.ToonProtocolVersion.CustomV4_4)
            options.ObjectArrayLayout = DevOp.Toon.Core.ToonObjectArrayLayout.Columnar;
        return options;
    }

    internal static int? DetectIndent(string toonString, bool ignoreComments)
    {
        var gcd = 0;
        for (var start = 0; start < toonString.Length;)
        {
            var newline = toonString.IndexOf('\n', start);
            if (newline < 0) newline = toonString.Length;
            var end = newline > start && toonString[newline - 1] == '\r' ? newline - 1 : newline;
            var position = start;
            while (position < end && toonString[position] == ' ') position++;
            var indent = position - start;
            var content = position;
            while (content < end && (toonString[content] == ' ' || toonString[content] == '\t')) content++;
            if (content < end && toonString[position] != '\t' && (!ignoreComments || toonString[position] != '#') && indent > 0)
                gcd = GreatestCommonDivisor(gcd, indent);
            start = newline + 1;
        }

        return gcd > 0 ? gcd : null;
    }

    private static bool DetectColumnarObjectArrays(string toonString, int indent)
    {
        var scanResult = Scanner.ToParsedLines(toonString, indent, strict: false);
        var lines = scanResult.Lines;

        for (var i = 0; i < lines.Count; i++)
        {
            var headerInfo = Parser.ParseArrayHeaderLine(lines[i].Content, Constants.DEFAULT_DELIMITER_CHAR);
            if (headerInfo?.Header.Fields == null || headerInfo.Header.Fields.Count == 0)
            {
                continue;
            }

            var rowDepth = lines[i].Depth + 1;
            var cursor = i + 1;
            while (cursor < lines.Count && lines[cursor].Depth >= rowDepth)
            {
                if (lines[cursor].Depth != rowDepth)
                {
                    cursor++;
                    continue;
                }

                cursor++;
                if (cursor < lines.Count && lines[cursor].Depth > rowDepth)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ToonDecodeOptions Clone(ToonDecodeOptions options)
    {
        return options.Clone();
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }

        return Math.Abs(left);
    }
}
