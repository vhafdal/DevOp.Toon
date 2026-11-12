namespace DevOp.Toon.Tests;

public sealed class LegacyNativeScalarBindingTests
{
    public static TheoryData<ToonProtocolVersion, string, string> AcceptedTokenCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, string, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            {
                data.Add(version, "Int", "\"12\"");
                data.Add(version, "NInt", "\"-7\"");
                data.Add(version, "Dbl", "\"1.5\"");
                data.Add(version, "Flt", "\"2.5\"");
                data.Add(version, "Dec", "\"0.1234567890123456789012345678\"");
                data.Add(version, "ULng", "\"18446744073709551615\"");
                data.Add(version, "Int", "true");
                data.Add(version, "Byt", "true");
                data.Add(version, "Dec", "false");
                data.Add(version, "Int", "null");
                data.Add(version, "Lng", "null");
                data.Add(version, "Dbl", "null");
                data.Add(version, "Level", "High");
                data.Add(version, "Level", "\"High\"");
                data.Add(version, "Level", "\"2\"");
                data.Add(version, "Level", "true");
                data.Add(version, "Level", "null");
                data.Add(version, "Flags", "\"A, B\"");
                data.Add(version, "NLevel", "Low");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AcceptedTokenCases))]
    public void Decode_LegacyNativeRouteNonNumberToken_BindsAsV4Does(ToonProtocolVersion version, string member, string token)
    {
        var property = typeof(Scalars).GetProperty(member)!;
        var expected = property.GetValue(ToonDecoder.Decode<Scalars>($"{member}: {token}", Options(ToonProtocolVersion.V4_4)));
        var native = Options(version, ToonPathExpansion.Safe);

        Assert.Equal(expected, property.GetValue(ToonDecoder.Decode<Scalars>($"{member}: {token}", Options(version))));
        Assert.Equal(expected, property.GetValue(ToonDecoder.Decode<Scalars>($"{member}: {token}\nz.q: 1", native)));
        Assert.Equal(expected, property.GetValue(ToonDecoder.Decode<List<Scalars>>($"[1]:\n  - {member}: {token}\n    z.q: 1", native)![0]));
        Assert.Equal(expected, property.GetValue(ToonDecoder.Decode<ScalarsRecord>($"{member}: {token}\nz.q: 1", native)!.ToScalars()));
        Assert.Equal(expected, property.GetValue(new ToonService().Deserialize<Scalars>($"{{\"{member}\":{ToJson(token)}}}", "application/json")));
    }

    public static TheoryData<ToonProtocolVersion, string, string, string> RejectedTokenCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, string, string, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            {
                data.Add(version, "Int", "abc", "Value abc is not a valid System.Int32");
                data.Add(version, "Int", "\"1.5\"", "Value 1.5 is not a valid System.Int32");
                data.Add(version, "Byt", "\"300\"", "Number 300 is outside the range of System.Byte");
                data.Add(version, "Dec", "abc", "Value abc is not a valid System.Decimal");
                data.Add(version, "Dbl", "Infinity", "Value Infinity is not a valid System.Double");
                data.Add(version, "Flt", "\"NaN\"", "Value NaN is not a valid System.Single");
                data.Add(version, "Level", "Bogus", "Value Bogus is not a valid DevOp.Toon.Tests.LegacyNativeScalarBindingTests+Level");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RejectedTokenCases))]
    public void Decode_LegacyNativeRouteTokenNotValidForMember_ThrowsFormatExceptionAsV4Does(ToonProtocolVersion version, string member, string token, string message)
    {
        var native = Options(version, ToonPathExpansion.Safe);

        AssertFormatError(message, () => ToonDecoder.Decode<Scalars>($"{member}: {token}", Options(ToonProtocolVersion.V4_4)));
        AssertFormatError(message, () => ToonDecoder.Decode<Scalars>($"{member}: {token}", Options(version)));
        AssertFormatError(message, () => ToonDecoder.Decode<Scalars>($"{member}: {token}\nz.q: 1", native));
        AssertFormatError(message, () => ToonDecoder.Decode<List<Scalars>>($"[1]:\n  - {member}: {token}\n    z.q: 1", native));
        AssertFormatError(message, () => new ToonService().Deserialize<Scalars>($"{{\"{member}\":{ToJson(token)}}}", "application/json"));
    }

    private static string ToJson(string token) =>
        token is "true" or "false" or "null" || token.StartsWith('"') ? token : $"\"{token}\"";

    private static void AssertFormatError(string message, Func<object?> decode)
    {
        var error = Assert.Throws<ToonFormatException>(decode);

        Assert.Equal(ToonErrorKind.Syntax, error.Kind);
        Assert.Contains(message, error.Message);
    }

    private static ToonDecodeOptions Options(ToonProtocolVersion version, ToonPathExpansion expandPaths = ToonPathExpansion.Off) =>
        new() { ProtocolVersion = version, Indent = 2, ExpandPaths = expandPaths };

    public enum Level
    {
        None,
        Low,
        High
    }

    [Flags]
    public enum Options4
    {
        None = 0,
        A = 1,
        B = 2
    }

    public sealed class Scalars
    {
        public int Int { get; set; }
        public int? NInt { get; set; }
        public long Lng { get; set; }
        public ulong ULng { get; set; }
        public byte Byt { get; set; }
        public double Dbl { get; set; }
        public float Flt { get; set; }
        public decimal Dec { get; set; }
        public Level Level { get; set; }
        public Level? NLevel { get; set; }
        public Options4 Flags { get; set; }
    }

    public sealed record ScalarsRecord(
        int Int = 0,
        int? NInt = null,
        long Lng = 0,
        ulong ULng = 0,
        byte Byt = 0,
        double Dbl = 0,
        float Flt = 0,
        decimal Dec = 0,
        Level Level = Level.None,
        Level? NLevel = null,
        Options4 Flags = Options4.None)
    {
        public Scalars ToScalars() => new()
        {
            Int = Int, NInt = NInt, Lng = Lng, ULng = ULng, Byt = Byt, Dbl = Dbl, Flt = Flt, Dec = Dec, Level = Level, NLevel = NLevel, Flags = Flags
        };
    }
}
