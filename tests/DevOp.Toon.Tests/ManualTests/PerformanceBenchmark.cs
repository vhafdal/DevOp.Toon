using System;
using System.Diagnostics;
using DevOp.Toon;
using Xunit;
using Xunit.Abstractions;

namespace DevOp.Toon.Tests;

public class PerformanceBenchmark
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmark(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Use benchmarks/DevOp.Toon.Benchmarks for performance measurements.")]
    public void BenchmarkSimpleEncoding()
    {
        var data = new
        {
            users = new[]
            {
                new { id = 1, name = "Alice", role = "admin" },
                new { id = 2, name = "Bob", role = "user" },
                new { id = 3, name = "Charlie", role = "user" }
            }
        };

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            _ = ToonEncoder.Encode(data);
        }
        sw.Stop();

        _output.WriteLine($"Simple encoding: {sw.ElapsedMilliseconds}ms for 10,000 iterations");
        _output.WriteLine($"Average: {sw.Elapsed.Ticks / 100000:F2}Î¼s per encode");

        // Baseline: should complete in reasonable time (< 5 seconds for 10k iterations)
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Encoding took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    [Fact(Skip = "Use benchmarks/DevOp.Toon.Benchmarks for performance measurements.")]
    public void BenchmarkSimpleDecoding()
    {
        var toonString = """
users[3]{id,name,role}:
  1,Alice,admin
  2,Bob,user
  3,Charlie,user
""";

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            _ = ToonDecoder.Decode(toonString);
        }
        sw.Stop();

        _output.WriteLine($"Simple decoding: {sw.ElapsedMilliseconds}ms for 10,000 iterations");
        _output.WriteLine($"Average: {sw.Elapsed.Ticks / 100000:F2}Î¼s per decode");

        // Baseline: should complete in reasonable time (< 5 seconds for 10k iterations)
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Decoding took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    [Fact(Skip = "Use benchmarks/DevOp.Toon.Benchmarks for performance measurements.")]
    public void BenchmarkSection10Encoding()
    {
        var data = new
        {
            items = new[]
            {
                new
                {
                    users = new[]
                    {
                        new { id = 1, name = "Ada" },
                        new { id = 2, name = "Bob" }
                    },
                    status = "active",
                    count = 2
                }
            }
        };

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            _ = ToonEncoder.Encode(data);
        }
        sw.Stop();

        _output.WriteLine($"Section 10 encoding: {sw.ElapsedMilliseconds}ms for 10,000 iterations");
        _output.WriteLine($"Average: {sw.Elapsed.Ticks / 100000:F2}Î¼s per encode");

        // Baseline: should complete in reasonable time
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Section 10 encoding took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    [Fact(Skip = "Use benchmarks/DevOp.Toon.Benchmarks for performance measurements.")]
    public void BenchmarkRoundTrip()
    {
        var data = new
        {
            users = new[]
            {
                new { id = 1, name = "Alice" },
                new { id = 2, name = "Bob" }
            },
            metadata = new { version = 1, timestamp = "2025-11-27" }
        };

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 5000; i++)
        {
            var encoded = ToonEncoder.Encode(data);
            _ = ToonDecoder.Decode(encoded);
        }
        sw.Stop();

        _output.WriteLine($"Round-trip: {sw.ElapsedMilliseconds}ms for 5,000 iterations");
        _output.WriteLine($"Average: {sw.Elapsed.Ticks / 50000:F2}Î¼s per round-trip");

        // Baseline: should complete in reasonable time
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Round-trip took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
    }
}
