using System.Diagnostics;
using System.Text.Json;

namespace DevOp.Toon.Benchmarks;

internal static class KanShapeProfiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private static readonly ToonEncodeOptions EncodeOptions = new()
    {
        KeyFolding = ToonKeyFolding.Off,
        Delimiter = ToonDelimiter.COMMA,
        Indent = 1,
        ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
        ExcludeEmptyArrays = true,
        IgnoreNullOrEmpty = true
    };

    public static bool TryRun(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--profile-kan-shape", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var customerCount = TryReadInt(args, "--count") ?? 1_000;
        var iterations = TryReadInt(args, "--iterations") ?? 8;
        Run(customerCount, iterations);
        return true;
    }

    private static void Run(int customerCount, int iterations)
    {
        var customers = CreateCustomers(customerCount);
        var batch = CreateBatch(customers);

        var listJsonText = JsonSerializer.Serialize(customers, JsonOptions);
        var listToonText = ToonEncoder.Encode(customers, EncodeOptions);
        var batchJsonText = JsonSerializer.Serialize(batch, JsonOptions);
        var batchToonText = ToonEncoder.Encode(batch, EncodeOptions);

        var listDecodeOptions = CreateProfilingDecodeOptions(listToonText);
        var batchDecodeOptions = CreateProfilingDecodeOptions(batchToonText);

        var listHarness = DecodeReflectionHarness<List<KanCustomerDto>>.Create(listDecodeOptions);
        var arrayHarness = DecodeReflectionHarness<KanCustomerDto[]>.Create(listDecodeOptions);
        var readOnlyListHarness = DecodeReflectionHarness<IReadOnlyList<KanCustomerDto>>.Create(listDecodeOptions);
        var batchHarness = DecodeReflectionHarness<KanCustomerBatch>.Create(batchDecodeOptions);

        ValidateCustomers(JsonSerializer.Deserialize<List<KanCustomerDto>>(listJsonText, JsonOptions), customerCount);
        ValidateCustomers(ToonDecoder.Decode<List<KanCustomerDto>>(listToonText, listDecodeOptions), customerCount);
        ValidateCustomers(ToonDecoder.Decode<KanCustomerDto[]>(listToonText, listDecodeOptions), customerCount);
        ValidateCustomers(ToonDecoder.Decode<IReadOnlyList<KanCustomerDto>>(listToonText, listDecodeOptions), customerCount);
        ValidateBatch(JsonSerializer.Deserialize<KanCustomerBatch>(batchJsonText, JsonOptions), customerCount);
        ValidateBatch(ToonDecoder.Decode<KanCustomerBatch>(batchToonText, batchDecodeOptions), customerCount);

        var warmListScan = listHarness.Scan(listToonText);
        listHarness.DirectDecode(warmListScan);
        arrayHarness.DirectDecode(warmListScan);
        readOnlyListHarness.DirectDecode(warmListScan);
        listHarness.TypedDecode(listToonText);

        var warmBatchScan = batchHarness.Scan(batchToonText);
        batchHarness.DirectDecode(warmBatchScan);
        batchHarness.TypedDecode(batchToonText);

        var jsonListMeasurements = Measure(iterations, () => JsonSerializer.Deserialize<List<KanCustomerDto>>(listJsonText, JsonOptions));
        var publicListMeasurements = Measure(iterations, () => ToonDecoder.Decode<List<KanCustomerDto>>(listToonText, listDecodeOptions));
        var publicArrayMeasurements = Measure(iterations, () => ToonDecoder.Decode<KanCustomerDto[]>(listToonText, listDecodeOptions));
        var publicReadOnlyListMeasurements = Measure(iterations, () => ToonDecoder.Decode<IReadOnlyList<KanCustomerDto>>(listToonText, listDecodeOptions));
        var scanListMeasurements = Measure(iterations, () => listHarness.Scan(listToonText));

        bool directListSucceeded = true;
        var directListMeasurements = Measure(iterations, () =>
        {
            var scanResult = listHarness.Scan(listToonText);
            directListSucceeded &= listHarness.DirectDecode(scanResult);
        });

        bool directArraySucceeded = true;
        var directArrayMeasurements = Measure(iterations, () =>
        {
            var scanResult = arrayHarness.Scan(listToonText);
            directArraySucceeded &= arrayHarness.DirectDecode(scanResult);
        });

        bool directReadOnlyListSucceeded = true;
        var directReadOnlyListMeasurements = Measure(iterations, () =>
        {
            var scanResult = readOnlyListHarness.Scan(listToonText);
            directReadOnlyListSucceeded &= readOnlyListHarness.DirectDecode(scanResult);
        });

        var typedListMeasurements = Measure(iterations, () => listHarness.TypedDecode(listToonText));

        var jsonBatchMeasurements = Measure(iterations, () => JsonSerializer.Deserialize<KanCustomerBatch>(batchJsonText, JsonOptions));
        var publicBatchMeasurements = Measure(iterations, () => ToonDecoder.Decode<KanCustomerBatch>(batchToonText, batchDecodeOptions));
        var scanBatchMeasurements = Measure(iterations, () => batchHarness.Scan(batchToonText));

        bool directBatchSucceeded = true;
        var directBatchMeasurements = Measure(iterations, () =>
        {
            var scanResult = batchHarness.Scan(batchToonText);
            directBatchSucceeded &= batchHarness.DirectDecode(scanResult);
        });

        var typedBatchMeasurements = Measure(iterations, () => batchHarness.TypedDecode(batchToonText));

        Console.WriteLine("KAN typed decode profile");
        Console.WriteLine($"Customers  : {customerCount}");
        Console.WriteLine($"Iterations : {iterations}");
        Console.WriteLine($"List TOON  : {listToonText.Length:N0} chars");
        Console.WriteLine($"List JSON  : {listJsonText.Length:N0} chars");
        Console.WriteLine($"Batch TOON : {batchToonText.Length:N0} chars");
        Console.WriteLine($"Batch JSON : {batchJsonText.Length:N0} chars");
        Console.WriteLine($"List decode indent  : {listDecodeOptions.Indent}");
        Console.WriteLine($"Batch decode indent : {batchDecodeOptions.Indent}");
        Console.WriteLine();
        Console.WriteLine("Root collection timings");
        Console.WriteLine("-------------------------------");
        WriteMeasurement("JSON List<T>", jsonListMeasurements);
        WriteMeasurement("TOON public List<T>", publicListMeasurements);
        WriteMeasurement("TOON public T[]", publicArrayMeasurements);
        WriteMeasurement("TOON public IReadOnlyList", publicReadOnlyListMeasurements);
        WriteMeasurement("TOON scan only", scanListMeasurements);
        WriteMeasurement("TOON direct List<T>", directListMeasurements);
        WriteMeasurement("TOON direct T[]", directArrayMeasurements);
        WriteMeasurement("TOON direct IReadOnlyList", directReadOnlyListMeasurements);
        WriteMeasurement("TOON typed internal", typedListMeasurements);
        Console.WriteLine($"Direct List<T> succeeded on all iterations: {directListSucceeded}");
        Console.WriteLine($"Direct T[] succeeded on all iterations: {directArraySucceeded}");
        Console.WriteLine($"Direct IReadOnlyList<T> succeeded on all iterations: {directReadOnlyListSucceeded}");
        Console.WriteLine();
        Console.WriteLine("Constructor object and dictionary timings");
        Console.WriteLine("-------------------------------");
        WriteMeasurement("JSON batch", jsonBatchMeasurements);
        WriteMeasurement("TOON public batch", publicBatchMeasurements);
        WriteMeasurement("TOON scan only", scanBatchMeasurements);
        WriteMeasurement("TOON direct batch", directBatchMeasurements);
        WriteMeasurement("TOON typed internal", typedBatchMeasurements);
        Console.WriteLine($"Direct batch succeeded on all iterations: {directBatchSucceeded}");
    }

    private static List<KanCustomerDto> CreateCustomers(int customerCount)
    {
        var customers = new List<KanCustomerDto>(customerCount);
        for (int i = 0; i < customerCount; i++)
        {
            var id = $"C-{i + 1:000000}";
            var billingAddress = new KanAddressDto(
                $"City-{i % 31:00}",
                Country: i % 3 == 0 ? "Iceland" : "Norway",
                PostalCode: $"{100 + (i % 900):000}");
            var shippingAddress = new KanAddressDto(
                $"ShipCity-{i % 17:00}",
                Country: i % 2 == 0 ? "Iceland" : "Denmark",
                PostalCode: $"{200 + (i % 800):000}");

            customers.Add(new KanCustomerDto(
                id,
                $"Customer {i + 1:000000}",
                new[] { billingAddress, shippingAddress },
                new[] { "Primary", $"Segment-{i % 5}", $"Tier-{i % 3}" },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["currency"] = i % 4 == 0 ? "EUR" : "ISK",
                    ["size"] = i % 2 == 0 ? "medium" : "large",
                    ["region"] = $"R{i % 7}"
                },
                new Dictionary<string, KanAddressDto>(StringComparer.Ordinal)
                {
                    ["billing"] = billingAddress,
                    ["shipping"] = shippingAddress
                }));
        }

        return customers;
    }

    private static KanCustomerBatch CreateBatch(IReadOnlyList<KanCustomerDto> customers)
    {
        var primaryAddresses = new Dictionary<string, KanAddressDto>(StringComparer.Ordinal);
        for (int i = 0; i < customers.Count; i++)
        {
            primaryAddresses[customers[i].Id] = customers[i].Addresses[0];
        }

        return new KanCustomerBatch(
            customers,
            primaryAddresses,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "kan-profile",
                ["schema"] = "constructor-record-dictionaries"
            });
    }

    private static void ValidateCustomers(IReadOnlyList<KanCustomerDto>? customers, int expectedCount)
    {
        if (customers == null || customers.Count != expectedCount)
        {
            throw new InvalidOperationException($"Expected {expectedCount} customers, but decoded {customers?.Count ?? 0}.");
        }

        var first = customers[0];
        if (first.Id != "C-000001" ||
            first.Addresses.Count != 2 ||
            first.Aliases.Count != 3 ||
            first.DefaultDimensions["currency"].Length == 0 ||
            first.AddressBook["billing"].City.Length == 0)
        {
            throw new InvalidOperationException("The KAN profile decoded invalid customer data.");
        }
    }

    private static void ValidateBatch(KanCustomerBatch? batch, int expectedCount)
    {
        if (batch == null ||
            batch.Customers.Count != expectedCount ||
            batch.PrimaryAddresses.Count != expectedCount ||
            batch.DefaultDimensions["source"] != "kan-profile")
        {
            throw new InvalidOperationException("The KAN profile decoded invalid batch data.");
        }
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
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore));
        }

        return results;
    }

    private static List<MeasurementResult> Measure(int iterations, Action action)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            action();
            var elapsed = Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore));
        }

        return results;
    }

    private static void WriteMeasurement(string name, List<MeasurementResult> results)
    {
        var averageMs = results.Average(static result => result.Elapsed.TotalMilliseconds);
        var minMs = results.Min(static result => result.Elapsed.TotalMilliseconds);
        var maxMs = results.Max(static result => result.Elapsed.TotalMilliseconds);
        var averageAllocatedKb = results.Average(static result => result.AllocatedBytes / 1024d);
        Console.WriteLine(
            $"{name,-26} avg {averageMs,8:F3} ms   min {minMs,8:F3} ms   max {maxMs,8:F3} ms   alloc {averageAllocatedKb,10:F2} KB");
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

    private static ToonDecodeOptions CreateProfilingDecodeOptions(string toonText)
    {
        var defaults = ToonDecoder.DefaultOptions;
        return ToonDecoder.DetectOptions(toonText, new ToonDecodeOptions
        {
            ExpandPaths = defaults.ExpandPaths,
            Strict = defaults.Strict,
            Indent = defaults.Indent > 0 ? defaults.Indent : 2
        });
    }

    private readonly record struct MeasurementResult(TimeSpan Elapsed, long AllocatedBytes);

    private sealed record KanCustomerBatch(
        IReadOnlyList<KanCustomerDto> Customers,
        IReadOnlyDictionary<string, KanAddressDto> PrimaryAddresses,
        Dictionary<string, string> DefaultDimensions);

    private sealed record KanCustomerDto(
        string Id,
        string Name,
        IReadOnlyList<KanAddressDto> Addresses,
        IReadOnlyList<string> Aliases,
        Dictionary<string, string> DefaultDimensions,
        IReadOnlyDictionary<string, KanAddressDto> AddressBook);

    private sealed record KanAddressDto(
        string City,
        string Country,
        string PostalCode);
}
