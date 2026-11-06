using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using DevOp.Toon.SpecGenerator.Types;
using DevOp.Toon.SpecGenerator.Util;
using DevOp.Toon.SpecGenerator.Extensions;

namespace DevOp.Toon.SpecGenerator;

internal class SpecGenerator(ILogger<SpecGenerator> logger)
{
    public void GenerateSpecs(SpecGeneratorOptions options)
    {
        var toonSpecDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        logger.LogInformation("Cloning Toon Format spec repository to {ToonSpecDir}", toonSpecDir);

        try
        {
            // Clean up test directory before generating new files
            if (!options.Snapshot) CleanTestDirectory(options.AbsoluteOutputPath);

            logger.LogDebug("Cloning repository {RepoUrl} to {CloneDirectory}", options.SpecRepoUrl, toonSpecDir);

            GitTool.CloneRepository(options.SpecRepoUrl, toonSpecDir,
                branch: options.Branch, depth: 1, logger: logger);

            if (options.Snapshot)
            {
                CopySnapshot(toonSpecDir, options);
                return;
            }

            var testsToIgnore = GenerateTestsToIgnore(options.AbsoluteSpecIgnorePath);

            GenerateEncodeFixtures(
                toonSpecDir,
                Path.Combine(options.AbsoluteOutputPath, "Encode"),
                testsToIgnore
            );

            GenerateDecodeFixtures(
                toonSpecDir,
                Path.Combine(options.AbsoluteOutputPath, "Decode"),
                testsToIgnore
            );
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed generating specs");

            throw;
        }
        finally
        {
            logger.LogDebug("Removing temp clone directory {CloneDirectory}", toonSpecDir);

            // delete folder recursively
            TryDeleteDirectory(toonSpecDir);
        }

        logger.LogInformation("Spec generation completed.");
    }

    private void CopySnapshot(string source, SpecGeneratorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Branch)) throw new ArgumentException("A pinned tag is required for snapshots.");
        var target = Path.Combine(options.AbsoluteOutputPath, options.Branch);
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = source,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        process.StartInfo.ArgumentList.Add("rev-parse");
        process.StartInfo.ArgumentList.Add("HEAD");
        process.Start();
        var commit = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        var expected = options.Branch switch
        {
            "v3.0.0" => "c09f73b267323190f61de5b91563fa579b3b7c5e",
            "v4.0.0" => "c31beab79abf62f2bac16449ff1ce901d0b42ccd",
            "v4.1.3" => "a6b801a3326980ab2cb615b0ffe44457e091f7b6",
            "v4.2.1" => "43d8c2933f64f07101d7e9369d3c9c332b944efe",
            "v4.3.0" => "6d5633b8d721bd2314eb937c22596632fa1c5952",
            "v4.4.0" => "d09c3d33e37389345a74bd96981eb96dd9be6c5e",
            _ => throw new ArgumentException("Snapshot generation requires one of the six supported version tags.")
        };
        if (!string.Equals(commit, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Tag {options.Branch} does not match its pinned commit {expected}.");
        CopySnapshotDirectory(Path.Combine(source, "tests", "fixtures"), target);
        var license = Directory.GetFiles(source, "LICENSE*").FirstOrDefault();
        if (license != null) File.Copy(license, Path.Combine(options.AbsoluteOutputPath, "LICENSE"), true);
        File.WriteAllText(Path.Combine(target, "source.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            repository = options.SpecRepoUrl,
            tag = options.Branch,
            commit
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        logger.LogInformation("Copied fixtures for {Tag} at {Commit}", options.Branch, commit);
    }

    private static void CopySnapshotDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(source)) CopySnapshotDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private void CleanTestDirectory(string testDirectory)
    {
        if (!Directory.Exists(testDirectory))
        {
            logger.LogDebug("Test directory {TestDirectory} does not exist, skipping cleanup", testDirectory);
            return;
        }

        logger.LogInformation("Cleaning test directory {TestDirectory}", testDirectory);

        // Delete all subdirectories
        foreach (var dir in Directory.GetDirectories(testDirectory))
        {
            try
            {
                Directory.Delete(dir, true);
                logger.LogDebug("Deleted directory {Directory}", dir);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete directory {Directory}", dir);
            }
        }

        // Delete all files except .csproj files
        foreach (var file in Directory.GetFiles(testDirectory))
        {
            if (Path.GetExtension(file).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("Preserving project file {File}", file);
                continue;
            }

            try
            {
                File.Delete(file);
                logger.LogDebug("Deleted file {File}", file);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete file {File}", file);
            }
        }

        logger.LogInformation("Test directory cleanup completed");
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        // Create destination directory if it doesn't exist
        Directory.CreateDirectory(destDir);

        // Copy all files
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(destDir, fileName);

            try
            {
                File.Copy(file, destFile, overwrite: true);
                logger.LogDebug("Copied file {SourceFile} to {DestFile}", file, destFile);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to copy file {SourceFile} to {DestFile}", file, destFile);
            }
        }

        // Copy all subdirectories recursively
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var subDirName = Path.GetFileName(subDir);
            var destSubDir = Path.Combine(destDir, subDirName);
            CopyDirectory(subDir, destSubDir);
        }
    }

    private void GenerateEncodeFixtures(string specDir, string outputDir, IEnumerable<string> ignores)
    {
        var encodeFixtures = LoadEncodeFixtures(specDir);

        foreach (var fixture in encodeFixtures)
        {
            fixture.Tests = fixture.Tests.Where(t => !ignores.Contains(t.Name));

            // Process each encode fixture as needed
            var writer = new FixtureWriter<EncodeTestCase, JsonNode, string>(fixture, outputDir);

            writer.WriteFile();
        }
    }

    private void GenerateDecodeFixtures(string specDir, string outputDir, IEnumerable<string> ignores)
    {
        var decodeFixtures = LoadDecodeFixtures(specDir);

        foreach (var fixture in decodeFixtures)
        {
            fixture.Tests = fixture.Tests.Where(t => !ignores.Contains(t.Name));

            // Process each decode fixture as needed
            var writer = new FixtureWriter<DecodeTestCase, string, JsonNode>(fixture, outputDir);

            writer.WriteFile();
        }
    }

    private IEnumerable<Fixtures<EncodeTestCase, JsonNode, string>> LoadEncodeFixtures(string specDir)
    {
        return LoadFixtures<EncodeTestCase, JsonNode, string>(specDir, "encode");
    }

    private IEnumerable<Fixtures<DecodeTestCase, string, JsonNode>> LoadDecodeFixtures(string specDir)
    {
        return LoadFixtures<DecodeTestCase, string, JsonNode>(specDir, "decode");
    }

    private IEnumerable<string> GenerateTestsToIgnore(string specIgnorePath)
    {
        const string specIgnoreFileName = ".specignore";
        var specIgnoreFileAbsolutePath = !specIgnorePath.EndsWith(specIgnoreFileName) ?
                                            Path.Combine(specIgnorePath, specIgnoreFileName) : specIgnorePath;

        if (!File.Exists(specIgnoreFileAbsolutePath))
        {
            logger.LogDebug("No spec ignore file found at path {Path}", specIgnoreFileAbsolutePath);

            return Array.Empty<string>();
        }

        // filter comments and empty lines
        var testNames = File.ReadAllLines(specIgnoreFileAbsolutePath)
            .Where(i => !string.IsNullOrWhiteSpace(i) && !i.StartsWith('#'));

        var set = new HashSet<string>(testNames, StringComparer.OrdinalIgnoreCase);

        logger.LogDebug("Found {Count} tests to ignore", set.Count);

        return set;
    }

    private static IEnumerable<Fixtures<TTestCase, TIn, TOut>> LoadFixtures<TTestCase, TIn, TOut>(string specDir, string testType)
        where TTestCase : ITestCase<TIn, TOut>
    {
        var fixturesPath = Path.Combine(specDir, "tests", "fixtures", testType);

        foreach (var testFixture in Directory.GetFiles(fixturesPath, "*.json"))
        {
            var fixtureFileName = Path.GetFileName(testFixture);

            var fixtureContent = File.ReadAllText(testFixture);
            var fixture = SpecSerializer.Deserialize<Fixtures<TTestCase, TIn, TOut>>(fixtureContent) ?? throw new InvalidOperationException($"Failed to deserialize fixture file: {testFixture}");

            fixture.FileName = FixtureNameToCSharpFileName(fixtureFileName);

            yield return fixture;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        int i = 0;
        do
        {
            if (Directory.Exists(path))
            {
                try
                {
                    Directory.Delete(path, true);

                    break;
                }
                catch (Exception) { i++; }
            }
        }
        while (i < 3);
    }

    private static string FixtureNameToCSharpFileName(string fixtureName)
    {
        return Path.ChangeExtension(fixtureName.ToPascalCase(), ".cs");
    }
}
