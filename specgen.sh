# generate spec
GH_REPO="https://github.com/toon-format/spec.git"
OUT_DIR="./tests/DevOp.Toon.Tests/GeneratedTests"

# build and execute spec generator
dotnet build tests/DevOp.Toon.SpecGenerator

dotnet run --project tests/DevOp.Toon.SpecGenerator -- --url="$GH_REPO" --output="$OUT_DIR" --branch="v3.0.0" --loglevel="Information"