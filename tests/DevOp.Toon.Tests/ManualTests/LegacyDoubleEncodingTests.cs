using System.Globalization;
using System.Text;
using Xunit.Abstractions;

namespace DevOp.Toon.Tests;

/// <summary>
/// The legacy profiles write a double with 16 significant digits, which loses the last bit of many values. They now keep that
/// text whenever it reads back as the same double and write the shortest round-trip digits otherwise, always without an
/// exponent. Floats keep their 16-digit text, which already reads back as the same float.
/// </summary>
public sealed class LegacyDoubleEncodingTests
{
    private readonly ITestOutputHelper output;

    public LegacyDoubleEncodingTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    public static TheoryData<ToonProtocolVersion, double, string> LossyCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, double, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            {
                data.Add(version, -296.33000000000004, "-296.33000000000004");
                data.Add(version, 0.1 + 0.2, "0.30000000000000004");
                data.Add(version, 4.35 * 100, "434.99999999999994");
                data.Add(version, 1234567890123456.7, "1234567890123456.8");
                data.Add(version, 1.2345678901234566e-7, "0.00000012345678901234566");
                data.Add(version, -9.999999999999999e-6, "-0.000009999999999999999");
            }

            return data;
        }
    }

    public static TheoryData<ToonProtocolVersion, double, string> UnchangedCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, double, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            {
                data.Add(version, -296.33, "-296.33");
                data.Add(version, 0.1, "0.1");
                data.Add(version, 1e-7, "0.0000001");
                data.Add(version, 1e21, "1000000000000000000000");
                data.Add(version, 123456789012.5, "123456789012.5");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LossyCases))]
    public void Encode_LegacyDoubleThatSixteenDigitsLose_WritesRoundTripDigits(ToonProtocolVersion version, double value, string expected)
    {
        foreach (var (route, text) in EncodeOnEveryRoute(version, value))
        {
            Assert.True(expected == text, $"{route}: wrote {text} instead of {expected}");
        }

        AssertDecodesBack(version, value);
    }

    [Theory]
    [MemberData(nameof(UnchangedCases))]
    public void Encode_LegacyDoubleThatSixteenDigitsKeep_WritesSameText(ToonProtocolVersion version, double value, string expected)
    {
        foreach (var (route, text) in EncodeOnEveryRoute(version, value))
        {
            Assert.True(expected == text, $"{route}: wrote {text} instead of {expected}");
        }

        AssertDecodesBack(version, value);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Encode_LegacyFloat_KeepsSixteenDigitText(ToonProtocolVersion version)
    {
        var options = new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 };
        var rows = new List<FloatRow> { new() { Id = 1, V = 0.1f }, new() { Id = 2, V = 3.4028235e38f } };

        var native = ToonEncoder.Encode(new Dictionary<string, object?> { ["V"] = new[] { 0.1f, 3.4028235e38f } }, options);
        var fast = ToonEncoder.Encode(rows, options);
        var compact = Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(rows, options));

        Assert.Equal("V[2]: 0.1000000014901161,340282346638528859811704183484516925440", native);
        Assert.Equal("[2]{Id,V}:\n  1,0.1000000014901161\n  2,340282346638528859811704183484516925440", fast);
        Assert.Equal(fast, compact);
        var decoded = ToonDecoder.Decode<List<FloatRow>>(fast, new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 })!;
        Assert.Equal(0.1f, decoded[0].V);
        Assert.Equal(3.4028235e38f, decoded[1].V);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Encode_V4Double_Unchanged(ToonProtocolVersion version)
    {
        var options = new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 };

        Assert.Equal("V: 0.30000000000000004", ToonEncoder.Encode(new DoubleRow { Id = 1, V = 0.1 + 0.2 }, options).Split('\n')[1]);
        Assert.Equal("V: 0.1", ToonEncoder.Encode(new FloatRow { Id = 1, V = 0.1f }, options).Split('\n')[1]);
        Assert.Equal("V: 1.2345678901234566e-7", ToonEncoder.Encode(new DoubleRow { Id = 1, V = 1.2345678901234566e-7 }, options).Split('\n')[1]);
    }

    /// <summary>
    /// Every double that the 16-digit form already read back exactly keeps that text byte for byte on every legacy route,
    /// and every other double now reads back exactly.
    /// </summary>
    [Fact]
    public void Encode_LegacyDoubleSweep_ChangesOnlyValuesSixteenDigitsLose()
    {
        var values = SweepValues();
        var options = new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.CustomLegacy, Indent = 2 };
        var rows = values.Select((v, i) => new DoubleRow { Id = i, V = v }).ToList();

        var native = ToonEncoder.Encode(new Dictionary<string, object?> { ["V"] = values }, options);
        var nativeTokens = native.Substring(native.IndexOf(": ", StringComparison.Ordinal) + 2).Split(',');
        var fastTokens = RowTokens(ToonEncoder.Encode(rows, options));
        var compactTokens = RowTokens(Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(rows, options)));
        Assert.Equal(values.Length, nativeTokens.Length);
        Assert.Equal(values.Length, fastTokens.Length);
        Assert.Equal(values.Length, compactTokens.Length);

        int kept = 0, changed = 0;
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var previous = PreviousFormat(value);
            var previousReadsBack = double.Parse(previous, NumberStyles.Float, CultureInfo.InvariantCulture) == value;
            foreach (var (route, text) in new[] { ("native", nativeTokens[i]), ("fast", fastTokens[i]), ("compact", compactTokens[i]) })
            {
                Assert.True(text.IndexOf('E') < 0 && text.IndexOf('e') < 0, $"{route}: {value:R} written with an exponent: {text}");
                Assert.True(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(value), $"{route}: {value:R} written as {text}");
                if (previousReadsBack)
                {
                    Assert.True(previous == text, $"{route}: {value:R} written as {text} instead of {previous}");
                }
            }

            if (previousReadsBack) kept++;
            else changed++;
        }

        output.WriteLine($"Sweep: {values.Length} doubles, {kept} byte-identical (16 digits read back), {changed} rewritten (16 digits lost bits).");
        Assert.True(changed > 0 && kept > 0);
    }

    private static IEnumerable<(string Route, string Text)> EncodeOnEveryRoute(ToonProtocolVersion version, double value)
    {
        var options = new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 };
        var rows = new List<DoubleRow> { new() { Id = 1, V = value } };
        var native = ToonEncoder.Encode(new Dictionary<string, object?> { ["V"] = value }, options);
        yield return ("native", native.Substring("V: ".Length));
        yield return ("native array", ToonEncoder.Encode(new Dictionary<string, object?> { ["V"] = new[] { value } }, options).Substring("V[1]: ".Length));
        yield return ("fast object", ToonEncoder.Encode(new DoubleRow { Id = 1, V = value }, options).Split('\n')[1].Substring("V: ".Length));
        yield return ("fast table", RowTokens(ToonEncoder.Encode(rows, options))[0]);
        yield return ("compact table", RowTokens(Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(rows, options)))[0]);
        yield return ("json", ToonEncoder.Json2Toon("{\"V\":" + value.ToString("R", CultureInfo.InvariantCulture) + "}", options).Substring("V: ".Length));
    }

    private static void AssertDecodesBack(ToonProtocolVersion version, double value)
    {
        var encodeOptions = new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 };
        var decodeOptions = new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 };
        var rows = new List<DoubleRow> { new() { Id = 1, V = value } };

        Assert.Equal(value, ToonDecoder.Decode<DoubleRow>(ToonEncoder.Encode(new DoubleRow { Id = 1, V = value }, encodeOptions), decodeOptions)!.V);
        Assert.Equal(value, ToonDecoder.Decode<List<DoubleRow>>(ToonEncoder.Encode(rows, encodeOptions), decodeOptions)![0].V);
    }

    private static string[] RowTokens(string text) =>
        text.Split('\n').Skip(1).Select(static line => line.Trim().Split(',')[1]).ToArray();

    // The legacy formatting before this change: 16 significant digits, and fixed notation where that form had an exponent.
    private static string PreviousFormat(double value)
    {
        if (value == 0.0)
            return "0";

        var text = value.ToString("G16", CultureInfo.InvariantCulture);
        if (text.IndexOf('E') < 0)
            return text;

        var absValue = Math.Abs(value);
        var decimalPlaces = absValue < 1.0 && absValue > 0.0 ? Math.Max(0, -(int)Math.Floor(Math.Log10(absValue)) + 15) : 15;
        text = value.ToString("F" + decimalPlaces, CultureInfo.InvariantCulture);
        if (text.Contains('.'))
        {
            text = text.TrimEnd('0');
            if (text.EndsWith(".", StringComparison.Ordinal))
                text = text.TrimEnd('.');
        }

        return text;
    }

    private static double[] SweepValues()
    {
        var random = new Random(20261112);
        var values = new List<double>();
        Span<byte> bits = stackalloc byte[8];
        while (values.Count < 40_000)
        {
            random.NextBytes(bits);
            var value = BitConverter.ToDouble(bits);
            if (double.IsFinite(value) && value != 0.0) values.Add(value);
        }

        for (var i = 0; i < 40_000; i++)
        {
            var scale = Math.Pow(10, random.Next(0, 7));
            var a = Math.Round(random.NextDouble() * 100_000 * scale) / scale;
            var b = Math.Round(random.NextDouble() * 1_000) / 100;
            values.Add(a);
            values.Add(i % 3 == 0 ? a + b : i % 3 == 1 ? a * b : a - b);
        }

        for (var i = 0; i < 20_000; i++)
        {
            values.Add((double)random.NextInt64(1, 1L << 53));
            values.Add((double)random.NextInt64(long.MaxValue / 4, long.MaxValue) * (i % 2 == 0 ? 1 : 1000));
            values.Add(random.NextDouble() * Math.Pow(10, random.Next(-30, -4)));
            values.Add(random.NextDouble() * Math.Pow(10, random.Next(14, 26)));
            values.Add(-random.NextDouble() * Math.Pow(10, random.Next(-8, 16)));
        }

        return values.Where(static v => v != 0.0).ToArray();
    }

    public sealed class DoubleRow
    {
        public int Id { get; set; }

        public double V { get; set; }
    }

    public sealed class FloatRow
    {
        public int Id { get; set; }

        public float V { get; set; }
    }
}
