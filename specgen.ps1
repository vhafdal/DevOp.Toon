$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$project = 'tests/DevOp.Toon.SpecGenerator/DevOp.Toon.SpecGenerator.csproj'
$repository = if ($env:TOON_SPEC_REPOSITORY) { $env:TOON_SPEC_REPOSITORY } else { 'https://github.com/toon-format/spec.git' }
$output = 'tests/DevOp.Toon.Tests/ProtocolFixtures'
if (-not (Test-Path 'tests/DevOp.Toon.SpecGenerator/obj/project.assets.json')) {
    devop-build-slot dotnet restore $project
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
devop-build-slot dotnet build $project --no-restore -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($tag in 'v3.0.0', 'v4.0.0', 'v4.1.3', 'v4.2.1', 'v4.3.0', 'v4.4.0') {
    devop-build-slot dotnet run --project $project --framework net10.0 --no-build --no-restore -- --url $repository --output $output --branch $tag --snapshot --loglevel Information
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
