#!/bin/sh
set -eu

validator_dir=$(mktemp -d "${TMPDIR:-/tmp}/toon-release-validator.XXXXXXXX")
trap 'rm -rf "$validator_dir"' EXIT HUP INT TERM
cat > "$validator_dir/ReleaseValidator.csproj" <<'PROJECT'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
PROJECT
cat > "$validator_dir/Program.cs" <<'PROGRAM'
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length == 0) throw new ArgumentException("Specify source, tests or package validation.");
switch (args[0])
{
    case "source" when args.Length == 4:
        ValidateSource(args[1], args[2], bool.Parse(args[3]));
        break;
    case "tests" when args.Length == 3:
        var files = Directory.GetFiles(args[1], "*.trx", SearchOption.AllDirectories);
        if (files.Length == 0) throw new InvalidOperationException("No TRX results: testing a library is not a test gate.");
        var passed = 0;
        foreach (var file in files)
        {
            var counters = XDocument.Load(file).Descendants().Single(element => element.Name.LocalName == "Counters");
            var results = XDocument.Load(file).Descendants().Where(element => element.Name.LocalName == "UnitTestResult").ToArray();
            if ((int?)counters.Attribute("failed") != 0 || results.Any(result => (string?)result.Attribute("outcome") is not ("Passed" or "NotExecuted")))
                throw new InvalidOperationException("TRX contains failed, aborted or inconclusive tests.");
            passed += (int?)counters.Attribute("passed") ?? 0;
        }
        if (passed < int.Parse(args[2], CultureInfo.InvariantCulture)) throw new InvalidOperationException($"Only {passed} passing tests; expected at least {args[2]}.");
        Console.WriteLine($"Verified {passed} passing tests in {files.Length} TRX file(s).");
        break;
    case "package" when args.Length == 5:
        var expected = ValidateSource(args[1], args[3], bool.Parse(args[4]));
        var packages = Directory.GetFiles(args[2], "*.nupkg", SearchOption.TopDirectoryOnly);
        if (packages.Length != 1) throw new InvalidOperationException("Expected exactly one freshly packed nupkg.");
        var package = packages[0];
        if (Path.GetFileName(package) != $"{expected.Id}.{expected.Version}.nupkg") throw new InvalidOperationException("Unexpected package filename.");
        using (var archive = ZipFile.OpenRead(package))
        {
            var entry = archive.Entries.Single(item => item.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var stream = entry.Open();
            var metadata = XDocument.Load(stream).Descendants().Single(element => element.Name.LocalName == "metadata");
            string Field(string name) => metadata.Elements().Single(element => element.Name.LocalName == name).Value;
            if (Field("id") != expected.Id || Field("version") != expected.Version) throw new InvalidOperationException("Packed id/version does not match the project.");
            var repository = metadata.Elements().Single(element => element.Name.LocalName == "repository");
            if ((string?)repository.Attribute("commit") != expected.Commit) throw new InvalidOperationException("Packed RepositoryCommit does not match checked-out HEAD.");
        }
        Console.WriteLine($"Verified package {expected.Id} {expected.Version}; HEAD={expected.Commit}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package)))}");
        break;
    default:
        throw new ArgumentException("Invalid validator arguments.");
}

static (string Id, string Version, string Commit) ValidateSource(string project, string requested, bool publishing)
{
    var document = XDocument.Load(project);
    string Field(string name) => document.Root!.Elements().Where(element => element.Name.LocalName == "PropertyGroup")
        .SelectMany(element => element.Elements()).Single(element => element.Name.LocalName == name).Value.Trim();
    var id = Field("PackageId");
    var version = Field("Version");
    if (version.Contains("$(", StringComparison.Ordinal)) throw new InvalidOperationException("Expected a literal csproj package version.");
    var stableTag = Regex.IsMatch(requested, @"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$");
    if (requested != "main" && !stableTag) throw new InvalidOperationException("GIT_REF must be main or a stable vMAJOR.MINOR.PATCH tag.");
    if (publishing && (!stableTag || requested != "v" + version)) throw new InvalidOperationException("Publishing requires the stable version tag matching the csproj Version.");
    var head = Git("rev-parse", "HEAD^{commit}");
    var main = Git("rev-parse", "refs/remotes/origin/main^{commit}");
    if (requested == "main")
    {
        if (head != main) throw new InvalidOperationException("HEAD does not match fetched origin/main.");
    }
    else
    {
        if (requested != "v" + version) throw new InvalidOperationException("Tag/project version mismatch.");
        var tag = "refs/tags/" + requested;
        if (Git("cat-file", "-t", tag) != "tag") throw new InvalidOperationException("Release tags must be annotated.");
        if (Git("rev-parse", tag + "^{commit}") != head) throw new InvalidOperationException("Requested tag does not point to HEAD.");
        Git("merge-base", "--is-ancestor", head, "refs/remotes/origin/main");
    }
    Git("diff", "--quiet", "HEAD", "--");
    Console.WriteLine($"Verified source {id} {version}; GIT_REF={requested}; HEAD={head}; origin/main={main}; publishing={publishing}");
    return (id, version, head);
}

static string Git(params string[] arguments)
{
    var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    start.ArgumentList.Add("-c");
    start.ArgumentList.Add("safe.directory=" + Directory.GetCurrentDirectory());
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to run git.");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"Git validation failed ({arguments[0]}): {error.Trim()}");
    return output.Trim();
}
PROGRAM
dotnet run --project "$validator_dir/ReleaseValidator.csproj" --configuration Release --verbosity quiet -- "$@"
