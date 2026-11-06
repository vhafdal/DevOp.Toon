# Jenkins package release gate

The project file remains the version source. Jenkins `GIT_REF=main` validates and packs without
publishing. Publishing requires `PUSH_NUGET=true` and an annotated stable `v<Version>` tag already
contained in fetched `origin/main`. The Git plugin reuses the job's configured URL/credentials and
fetches main plus tags; no shell-based unauthenticated fetch is used.

The checkout is clean, packages are built in Release, and pack explicitly embeds `RepositoryCommit`.
The validator checks source/tag/commit ancestry, actual passing TRX tests where a test project
exists, and exactly one package whose filename, nuspec id/version and repository commit match.
NuGet push does not skip duplicates: an existing version fails rather than hiding a collision.
Core has no standalone test project; its contracts are exercised by the runtime package suite.

`validate-release.sh` requires only the .NET 10 SDK, Git and a POSIX shell. Its package-free C#
validator is generated in a temporary directory outside the checkout (important for Core's root
`**/*.cs` compile glob), then removed. It needs neither Python nor unzip. No API key is handled
by the validator. Run it through `devop-build-slot` when validating locally:

```bash
devop-build-slot sh tools/validate-release.sh tests /absolute/test-results 1
devop-build-slot sh tools/validate-release.sh source src/DevOp.Toon/DevOp.Toon.csproj v0.3.0 true
devop-build-slot sh tools/validate-release.sh package src/DevOp.Toon/DevOp.Toon.csproj nupkg v0.3.0 true
```

Source/package validation intentionally rejects dirty tracked files, mismatched tags, unannotated
tags and tags outside main. Use the appropriate project path/version in each repository. The
Jenkins job must first load this pipeline on main with publishing disabled to register `GIT_REF`;
inspect its live parameters before triggering a tagged publication. Archive the package, TRX files
and `release-proof.txt`, and verify terminal Jenkins success plus the public feed afterward.
