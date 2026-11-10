# Contributing to DevOp.Toon

Thanks for your interest in contributing. Bug reports, fixes, features and documentation
improvements are welcome. [`AGENTS.md`](AGENTS.md) is the detailed guide to the repository's
structure, commands, conventions and commit workflow; this page is the short version.

## Setup

- Install the [.NET 10 SDK](https://dotnet.microsoft.com/download). It builds every target
  (`netstandard2.0`, `net8.0`, `net9.0`, `net10.0`); running the tests on .NET 8 and 9 also needs
  those runtimes.
- Clone the repository. Packages restore from nuget.org; nothing private is needed.

## Build and test

```bash
dotnet build tests/DevOp.Toon.Tests/DevOp.Toon.Tests.csproj
dotnet test tests/DevOp.Toon.Tests/DevOp.Toon.Tests.csproj
```

`dotnet build DevOp.Toon.slnx` builds every project in the repository.

If you change encoding or decoding behaviour, refresh the pinned spec fixtures and commit the result:

```bash
./specgen.sh     # or ./specgen.ps1 on Windows
```

## Coverage

Keep line coverage on `DevOp.Toon` at **85% or higher**:

```bash
dotnet test tests/DevOp.Toon.Tests/DevOp.Toon.Tests.csproj --collect:"XPlat Code Coverage"
```

## Pull requests

- Keep each pull request focused on one change and follow the existing code style.
- Add tests: a bug fix includes a test that fails without the fix.
- Use [Conventional Commit](https://www.conventionalcommits.org/) messages (`feat:`, `fix:`,
  `perf:`, `docs:`, `chore:` …) with a body explaining why.
- If the change affects package consumers, update `PackageReleaseNotes` in
  `src/DevOp.Toon/DevOp.Toon.csproj` using the format in `AGENTS.md`. Internal, test-only and
  tooling changes do not need release notes.
- Update `README.md`, `src/DevOp.Toon/README.md` or `Documentation/` when usage changes.
- Do not commit machine-specific paths, local settings or secrets.

## License and CLA

The library is MIT licensed ([`src/DevOp.Toon/LICENSE.md`](src/DevOp.Toon/LICENSE.md)).
Contributions are accepted under the Contributor License Agreement in
[`src/DevOp.Toon/CLA.md`](src/DevOp.Toon/CLA.md); see
[`src/DevOp.Toon/CONTRIBUTOR.md`](src/DevOp.Toon/CONTRIBUTOR.md) for what it means.

Report issues through GitHub Issues with clear reproduction steps.
