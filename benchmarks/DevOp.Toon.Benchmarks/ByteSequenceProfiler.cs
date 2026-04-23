using System.Text;
using System.Diagnostics;

namespace DevOp.Toon.Benchmarks;

internal static class ByteSequenceProfiler
{
    public static bool TryRun(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--profile-byte-sequences", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var count = TryReadInt(args, "--count") ?? 16_384;
        var iterations = TryReadInt(args, "--iterations") ?? 12;
        Run(count, iterations);
        return true;
    }

    private static void Run(int count, int iterations)
    {
        var data = CreateData(count);
        var list = data.ToList();
        IEnumerable<byte> enumerable = Enumerate(data);

        var base64EncodeOptions = new ToonEncodeOptions
        {
            ByteArrayFormat = ToonByteArrayFormat.Base64String
        };

        var numericEncodeOptions = new ToonEncodeOptions
        {
            ByteArrayFormat = ToonByteArrayFormat.NumericArray
        };

        var numericText = ToonEncoder.Encode(data, numericEncodeOptions);
        var base64Text = ToonEncoder.Encode(data, base64EncodeOptions);

        // Warm up JIT and caches.
        _ = ToonEncoder.Encode(data, base64EncodeOptions);
        _ = ToonEncoder.Encode(list, base64EncodeOptions);
        _ = ToonEncoder.Encode(enumerable, base64EncodeOptions);
        _ = ToonDecoder.Decode<byte[]>(base64Text);
        _ = ToonDecoder.Decode<List<byte>>(base64Text);
        _ = ToonDecoder.Decode<IEnumerable<byte>>(base64Text);
        _ = ToonDecoder.Decode<byte[]>(numericText);
        _ = ToonDecoder.Decode<List<byte>>(numericText);
        _ = ToonDecoder.Decode<IEnumerable<byte>>(numericText);

        var byteArrayBase64Encode = Measure(iterations, () => ToonEncoder.Encode(data, base64EncodeOptions));
        var byteArrayNumericEncode = Measure(iterations, () => ToonEncoder.Encode(data, numericEncodeOptions));
        var listBase64Encode = Measure(iterations, () => ToonEncoder.Encode(list, base64EncodeOptions));
        var listNumericEncode = Measure(iterations, () => ToonEncoder.Encode(list, numericEncodeOptions));
        var enumerableBase64Encode = Measure(iterations, () => ToonEncoder.Encode(Enumerate(data), base64EncodeOptions));
        var enumerableNumericEncode = Measure(iterations, () => ToonEncoder.Encode(Enumerate(data), numericEncodeOptions));

        var byteArrayBase64Decode = Measure(iterations, () => ToonDecoder.Decode<byte[]>(base64Text)!);
        var listBase64Decode = Measure(iterations, () => ToonDecoder.Decode<List<byte>>(base64Text)!);
        var enumerableBase64Decode = Measure(iterations, () => ToonDecoder.Decode<IEnumerable<byte>>(base64Text)!.ToArray());
        var byteArrayNumericDecode = Measure(iterations, () => ToonDecoder.Decode<byte[]>(numericText)!);
        var listNumericDecode = Measure(iterations, () => ToonDecoder.Decode<List<byte>>(numericText)!);
        var enumerableNumericDecode = Measure(iterations, () => ToonDecoder.Decode<IEnumerable<byte>>(numericText)!.ToArray());

        Console.WriteLine("Byte sequence profile");
        Console.WriteLine($"Count                : {count:N0} bytes");
        Console.WriteLine($"Iterations           : {iterations}");
        Console.WriteLine($"Numeric output bytes : {Encoding.UTF8.GetByteCount(numericText):N0}");
        Console.WriteLine($"Base64 output bytes  : {Encoding.UTF8.GetByteCount(base64Text):N0}");
        Console.WriteLine();
        Console.WriteLine("Encode timings");
        Console.WriteLine("-------------------------------");
        WriteMeasurement("byte[] base64", byteArrayBase64Encode);
        WriteMeasurement("byte[] numeric", byteArrayNumericEncode);
        WriteMeasurement("List<byte> base64", listBase64Encode);
        WriteMeasurement("List<byte> numeric", listNumericEncode);
        WriteMeasurement("IEnum<byte> base64", enumerableBase64Encode);
        WriteMeasurement("IEnum<byte> numeric", enumerableNumericEncode);
        Console.WriteLine();
        Console.WriteLine("Decode timings");
        Console.WriteLine("-------------------------------");
        WriteMeasurement("byte[] <- base64", byteArrayBase64Decode);
        WriteMeasurement("List<byte> <- base64", listBase64Decode);
        WriteMeasurement("IEnum<byte> <- base64", enumerableBase64Decode);
        WriteMeasurement("byte[] <- numeric", byteArrayNumericDecode);
        WriteMeasurement("List<byte> <- numeric", listNumericDecode);
        WriteMeasurement("IEnum<byte> <- numeric", enumerableNumericDecode);
    }

    private static byte[] CreateData(int count)
    {
        var data = new byte[count];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 31 + 17) % 256);
        }

        return data;
    }

    private static IEnumerable<byte> Enumerate(byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            yield return data[i];
        }
    }

    private static List<MeasurementResult> Measure(int iterations, Func<string> action)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var result = action();
            var elapsed = Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore, Encoding.UTF8.GetByteCount(result)));
        }

        return results;
    }

    private static List<MeasurementResult> Measure<T>(int iterations, Func<T> action)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            _ = action();
            var elapsed = Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore, 0));
        }

        return results;
    }

    private static void WriteMeasurement(string name, List<MeasurementResult> results)
    {
        var averageMs = results.Average(static result => result.Elapsed.TotalMilliseconds);
        var minMs = results.Min(static result => result.Elapsed.TotalMilliseconds);
        var maxMs = results.Max(static result => result.Elapsed.TotalMilliseconds);
        var averageAllocatedKb = results.Average(static result => result.AllocatedBytes / 1024d);
        var averageBytes = results.Average(static result => result.OutputBytes);

        Console.WriteLine(
            $"{name,-20} avg {averageMs,8:F3} ms   min {minMs,8:F3} ms   max {maxMs,8:F3} ms   alloc {averageAllocatedKb,10:F2} KB   bytes {averageBytes,10:F0}");
    }

    private static int? TryReadInt(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(args[i + 1], out var value))
            {
                return value;
            }
        }

        return null;
    }

    private readonly record struct MeasurementResult(TimeSpan Elapsed, long AllocatedBytes, int OutputBytes);
}
