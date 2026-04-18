#if NET8_0_OR_GREATER || NET9_0_OR_GREATER || NET10_0_OR_GREATER
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class DateOnlyTimeOnlyTests
{
    [Fact]
    public void Encode_ArrayOfObjects_WithDateOnly_KeepsDateAsScalarValue()
    {
        var forecasts = new[]
        {
            new WeatherForecastRow
            {
                TemperatureC = 49,
                Summary = "Bracing",
                Date = new DateOnly(2026, 3, 25)
            },
            new WeatherForecastRow
            {
                TemperatureC = -5,
                Summary = "Scorching",
                Date = new DateOnly(2026, 3, 26)
            }
        };

        var result = ToonEncoder.Encode(forecasts, new ToonEncodeOptions
        {
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar
        });

        Assert.Contains("{TemperatureC,Summary,Date}", result);
        Assert.Contains("2026-03-25", result);
        Assert.Contains("2026-03-26", result);
        Assert.DoesNotContain("Year:", result);
        Assert.DoesNotContain("Month:", result);
        Assert.DoesNotContain("DayOfYear:", result);
    }

    [Fact]
    public void Decode_TypedObject_WithDateOnlyAndTimeOnly_RoundTrips()
    {
        var original = new DateAndTimeEnvelope
        {
            Date = new DateOnly(2026, 3, 25),
            Time = new TimeOnly(14, 30, 15, 123),
            OptionalDate = new DateOnly(2026, 3, 26),
            OptionalTime = new TimeOnly(9, 45, 0)
        };

        var encoded = ToonEncoder.Encode(original);
        var decoded = ToonDecoder.Decode<DateAndTimeEnvelope>(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(original.Date, decoded.Date);
        Assert.Equal(original.Time, decoded.Time);
        Assert.Equal(original.OptionalDate, decoded.OptionalDate);
        Assert.Equal(original.OptionalTime, decoded.OptionalTime);
    }

    private sealed class WeatherForecastRow
    {
        public int TemperatureC { get; set; }

        public string Summary { get; set; } = string.Empty;

        public DateOnly Date { get; set; }
    }

    private sealed class DateAndTimeEnvelope
    {
        public DateOnly Date { get; set; }

        public TimeOnly Time { get; set; }

        public DateOnly? OptionalDate { get; set; }

        public TimeOnly? OptionalTime { get; set; }
    }
}
#endif
