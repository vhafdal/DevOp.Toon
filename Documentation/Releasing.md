# Releasing the TOON package family

These repositories currently derive package versions from `<Version>` in each packable project,
not MinVer. Jenkins reads that value and packs the same project. Annotated `v<Version>` tags
identify immutable release source; they do not calculate the version.

Publish in dependency order:

1. `DevOp.Toon.Core`
2. `DevOp.Toon`
3. `DevOp.Toon.API` and `DevOp.Toon.Client`

Update the affected project version, consumer dependency references, package release notes and
usage documentation together. Validate the source changes before merging. Keep raw validation
logs and benchmark artifacts in ignored local evidence directories; pinned specification fixtures
remain in Git so clean checkouts can execute the conformance suite.

Jenkins jobs under the `Toon` folder accept `PUSH_NUGET` and `GIT_REF`. Run a nonpublishing build
after introducing pipeline parameter changes, then read the live job parameters before triggering
a release. For publication, select the annotated `v<Version>` explicitly and set
`PUSH_NUGET=true`. The pipeline verifies the selected tag, package version and main-branch
ancestry, executes actual test projects where present, and packs from the selected source.

Wait for each upstream package to be visible on NuGet.org before restoring dependent Release
builds. Release builds use NuGet dependencies; local project substitutions are Debug-only.
For prepublication validation, an isolated local package source can supply the newly packed
upstream packages without changing production project references.

A release is complete only after terminal Jenkins success, source-commit verification, public
package registration/index visibility, and a clean consumer restore and execution. Publishing
these packages does not deploy downstream applications.
