using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class BclScalarRoundTripTests
{
    [Fact]
    public void EncodeDecode_TypedColumnarRows_WithBroadBclScalars_RoundTrips()
    {
        var rows = new List<BclScalarRow>
        {
            new()
            {
                Id = 1,
                NullableString = "alpha",
                NullableInt = 42,
                NullableByte = 250,
                NullableSByte = -12,
                NullableShort = -1234,
                NullableUShort = 54321,
                NullableUInt = 4_000_000_000,
                NullableULong = 9_000_000_000_000_000_000,
                NullableLong = -9_000_000_000,
                NullableBool = true,
                NullableFloat = 1.25f,
                NullableDouble = 2.5,
                NullableDecimal = 1234.5678m,
                NullableChar = 'Z',
                NullableTimeSpan = TimeSpan.FromDays(1) + TimeSpan.FromSeconds(2.5),
                NullableGuid = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                NullableUri = new Uri("https://example.com/path?q=1"),
                NullableVersion = new Version(1, 2, 3, 4),
                NullableState = BclScalarState.Ready
            },
            new()
            {
                Id = 2
            }
        };

        var encoded = ToonEncoder.Encode(rows, new ToonEncodeOptions
        {
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
            IgnoreNullOrEmpty = false
        });

        Assert.Contains("NullableByte", encoded);
        Assert.Contains("NullableTimeSpan", encoded);
        Assert.Contains("NullableUri", encoded);
        Assert.Contains("NullableVersion", encoded);
        Assert.DoesNotContain("NullableState:", encoded);

        var decoded = ToonDecoder.Decode<List<BclScalarRow>>(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Count);

        var first = decoded[0];
        Assert.Equal("alpha", first.NullableString);
        Assert.Equal(42, first.NullableInt);
        Assert.Equal((byte)250, first.NullableByte);
        Assert.Equal((sbyte)-12, first.NullableSByte);
        Assert.Equal((short)-1234, first.NullableShort);
        Assert.Equal((ushort)54321, first.NullableUShort);
        Assert.Equal(4_000_000_000u, first.NullableUInt);
        Assert.Equal(9_000_000_000_000_000_000ul, first.NullableULong);
        Assert.Equal(-9_000_000_000L, first.NullableLong);
        Assert.True(first.NullableBool);
        Assert.Equal(1.25f, first.NullableFloat);
        Assert.Equal(2.5, first.NullableDouble);
        Assert.Equal(1234.5678m, first.NullableDecimal);
        Assert.Equal('Z', first.NullableChar);
        Assert.Equal(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(2.5), first.NullableTimeSpan);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), first.NullableGuid);
        Assert.Equal(new Uri("https://example.com/path?q=1"), first.NullableUri);
        Assert.Equal(new Version(1, 2, 3, 4), first.NullableVersion);
        Assert.Equal(BclScalarState.Ready, first.NullableState);

        var second = decoded[1];
        Assert.Null(second.NullableString);
        Assert.Null(second.NullableInt);
        Assert.Null(second.NullableByte);
        Assert.Null(second.NullableSByte);
        Assert.Null(second.NullableShort);
        Assert.Null(second.NullableUShort);
        Assert.Null(second.NullableUInt);
        Assert.Null(second.NullableULong);
        Assert.Null(second.NullableLong);
        Assert.Null(second.NullableBool);
        Assert.Null(second.NullableFloat);
        Assert.Null(second.NullableDouble);
        Assert.Null(second.NullableDecimal);
        Assert.Null(second.NullableChar);
        Assert.Null(second.NullableTimeSpan);
        Assert.Null(second.NullableGuid);
        Assert.Null(second.NullableUri);
        Assert.Null(second.NullableVersion);
        Assert.Null(second.NullableState);
    }

    private sealed class BclScalarRow
    {
        public int Id { get; set; }
        public string? NullableString { get; set; }
        public int? NullableInt { get; set; }
        public byte? NullableByte { get; set; }
        public sbyte? NullableSByte { get; set; }
        public short? NullableShort { get; set; }
        public ushort? NullableUShort { get; set; }
        public uint? NullableUInt { get; set; }
        public ulong? NullableULong { get; set; }
        public long? NullableLong { get; set; }
        public bool? NullableBool { get; set; }
        public float? NullableFloat { get; set; }
        public double? NullableDouble { get; set; }
        public decimal? NullableDecimal { get; set; }
        public char? NullableChar { get; set; }
        public TimeSpan? NullableTimeSpan { get; set; }
        public Guid? NullableGuid { get; set; }
        public Uri? NullableUri { get; set; }
        public Version? NullableVersion { get; set; }
        public BclScalarState? NullableState { get; set; }
    }

    private enum BclScalarState
    {
        Unknown = 0,
        Ready = 2
    }
}
