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

        var options = Clone(fallbackOptions ?? new ToonDecodeOptions());
        var detectedIndent = DetectIndent(toonString);
        if (detectedIndent.HasValue)
        {
            options.Indent = detectedIndent.Value;
        }

        if (DetectColumnarObjectArrays(toonString, options.Indent))
        {
            options.ObjectArrayLayout = ToonObjectArrayLayout.Columnar;
        }

        return options;
    }

    private static int? DetectIndent(string toonString)
    {
        ReadOnlySpan<char> span = toonString.AsSpan();
        int currentIndent = 0;
        int? gcd = null;
        var inLeadingWhitespace = true;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];

            if (inLeadingWhitespace)
            {
                if (ch == ' ')
                {
                    currentIndent++;
                    continue;
                }

                if (ch == '\t')
                {
                    currentIndent = 0;
                    inLeadingWhitespace = false;
                    continue;
                }

                if (ch == '\r')
                {
                    continue;
                }

                if (ch == '\n')
                {
                    currentIndent = 0;
                    inLeadingWhitespace = true;
                    continue;
                }

                if (currentIndent > 0)
                {
                    gcd = gcd.HasValue ? GreatestCommonDivisor(gcd.Value, currentIndent) : currentIndent;
                }

                inLeadingWhitespace = false;
                continue;
            }

            if (ch == '\n')
            {
                currentIndent = 0;
                inLeadingWhitespace = true;
            }
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
        return new ToonDecodeOptions
        {
            Indent = options.Indent,
            Strict = options.Strict,
            ExpandPaths = options.ExpandPaths,
            ObjectArrayLayout = options.ObjectArrayLayout
        };
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
