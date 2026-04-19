using System.IO;

namespace DevOp.Toon.Benchmarks;

internal static class BenchmarkDataPaths
{
    public static readonly string BaseDirectory = ResolveBaseDirectory();

    public static string ProductsJsonPath => Path.Combine(BaseDirectory, "TestData", "prods.json");

    public static string ProductsToonPath => Path.Combine(BaseDirectory, "TestData", "prods.toon");

    private static string ResolveBaseDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "TestData")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the benchmark TestData directory starting from '{AppContext.BaseDirectory}'.");
    }
}
