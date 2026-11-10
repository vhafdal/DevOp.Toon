#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
project="tests/DevOp.Toon.SpecGenerator/DevOp.Toon.SpecGenerator.csproj"
repository="${TOON_SPEC_REPOSITORY:-https://github.com/toon-format/spec.git}"
output="tests/DevOp.Toon.Tests/ProtocolFixtures"
# devop-build-slot is an optional machine-local build throttle; plain dotnet is used without it.
run() { if command -v devop-build-slot >/dev/null 2>&1; then devop-build-slot "$@"; else "$@"; fi; }
if [[ ! -f tests/DevOp.Toon.SpecGenerator/obj/project.assets.json ]]; then
    run dotnet restore "$project"
fi
run dotnet build "$project" --no-restore -v minimal
for tag in v3.0.0 v4.0.0 v4.1.3 v4.2.1 v4.3.0 v4.4.0; do
    run dotnet run --project "$project" --framework net10.0 --no-build --no-restore -- \
        --url "$repository" --output "$output" --branch "$tag" --snapshot --loglevel Information
done
