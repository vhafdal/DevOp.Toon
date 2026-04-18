# Build Configuration

This repository supports two ways of resolving `DevOp.Toon.Core`:

- `Debug` builds can use a local `DevOp.Toon.Core` project reference
- `Release` builds always use the published NuGet package

The goal is to let each developer work against a local checkout of `DevOp.Toon.Core` without hardcoding machine-specific paths into the repo.

## Default Behavior

By default, `DevOp.Toon` resolves `DevOp.Toon.Core` from NuGet:

- package: `DevOp.Toon.Core`
- version: `0.1.0`

This is always the behavior for `Release` builds.

## Debug Local Override

For `Debug` builds, the project can switch to a local `DevOp.Toon.Core` project reference if either of these is provided:

- MSBuild property: `ToonCoreProjectPath`
- environment variable: `DEVOP_TOON_CORE_CSPROJ`

The value must point to the local `DevOp.Toon.Core.csproj` file.

Example:

```bash
export DEVOP_TOON_CORE_CSPROJ=/home/valdi/Projects/DevOp.Toon.Core/DevOp.Toon.Core.csproj
dotnet build -c Debug
```

Or per command:

```bash
dotnet build -c Debug -p:ToonCoreProjectPath=/home/valdi/Projects/DevOp.Toon.Core/DevOp.Toon.Core.csproj
```

## Environment Variable Setup

### Bash / Zsh

For the current shell session:

```bash
export DEVOP_TOON_CORE_CSPROJ=/home/valdi/Projects/DevOp.Toon.Core/DevOp.Toon.Core.csproj
export DEVOP_TOON_CSPROJ=/home/valdi/Projects/DevOp.Toon/src/DevOp.Toon/DevOp.Toon.csproj
export DEVOP_TOON_API_CSPROJ=/home/valdi/Projects/DevOp.Toon.API/src/DevOp.Toon.API/DevOp.Toon.API.csproj
```

Persist for future shell sessions:

```bash
echo 'export DEVOP_TOON_CORE_CSPROJ=/home/valdi/Projects/DevOp.Toon.Core/DevOp.Toon.Core.csproj' >> ~/.bashrc
echo 'export DEVOP_TOON_CSPROJ=/home/valdi/Projects/DevOp.Toon/src/DevOp.Toon/DevOp.Toon.csproj' >> ~/.bashrc
echo 'export DEVOP_TOON_API_CSPROJ=/home/valdi/Projects/DevOp.Toon.API/src/DevOp.Toon.API/DevOp.Toon.API.csproj' >> ~/.bashrc
source ~/.bashrc
```

### PowerShell

For the current PowerShell session:

```powershell
$env:DEVOP_TOON_CORE_CSPROJ = "C:\Projects\Nuget\Toon\DevOp.Toon.Core\DevOp.Toon.Core.csproj"
$env:DEVOP_TOON_CSPROJ = "C:\Projects\Nuget\Toon\DevOp.Toon\src\DevOp.Toon\DevOp.Toon.csproj"
$env:DEVOP_TOON_API_CSPROJ = "C:\Projects\Nuget\Toon\DevOp.Toon.API\src\DevOp.Toon.API\DevOp.Toon.API.csproj"
```

Persist as user environment variables for future shells and IDE launches:

```powershell
[Environment]::SetEnvironmentVariable("DEVOP_TOON_CORE_CSPROJ", "C:\Projects\Nuget\Toon\DevOp.Toon.Core\DevOp.Toon.Core.csproj", "User")
[Environment]::SetEnvironmentVariable("DEVOP_TOON_CSPROJ", "C:\Projects\Nuget\Toon\DevOp.Toon\src\DevOp.Toon\DevOp.Toon.csproj", "User")
[Environment]::SetEnvironmentVariable("DEVOP_TOON_API_CSPROJ", "C:\Projects\Nuget\Toon\DevOp.Toon.API\src\DevOp.Toon.API\DevOp.Toon.API.csproj", "User")
```

Restart PowerShell, Rider, or Visual Studio after setting them so new processes pick up the values.

### Rider And Other GUI Apps On Linux

PowerShell profile values only apply to PowerShell sessions. They do not automatically flow into:

- Rider's `sh` terminal
- Rider's design-time project model
- other GUI applications started from the desktop

If Rider should pick up these variables too, use one of these approaches:

1. Put the exports in `~/.profile`, then log out and back in
2. Launch Rider from a shell that already has the variables set
3. Pass the path explicitly with MSBuild properties during build

For login/session-wide Linux setup:

```bash
echo 'export DEVOP_TOON_CORE_CSPROJ=/home/valdi/Projects/DevOp.Toon.Core/DevOp.Toon.Core.csproj' >> ~/.profile
echo 'export DEVOP_TOON_CSPROJ=/home/valdi/Projects/DevOp.Toon/src/DevOp.Toon/DevOp.Toon.csproj' >> ~/.profile
echo 'export DEVOP_TOON_API_CSPROJ=/home/valdi/Projects/DevOp.Toon.API/src/DevOp.Toon.API/DevOp.Toon.API.csproj' >> ~/.profile
```

## API Test Host Override

`src/DevOp.Toon.API.TestHost` can also switch between a local `DevOp.Toon.API` checkout and the published package:

- `Debug` can use a local formatter project
- `Release` uses the `DevOp.Toon.API` NuGet package

For `Debug`, provide either:

- MSBuild property: `ToonApiProjectPath`
- environment variable: `DEVOP_TOON_API_CSPROJ`

Example:

```bash
export DEVOP_TOON_API_CSPROJ=/home/valdi/Projects/DevOp.Toon.API/src/DevOp.Toon.API/DevOp.Toon.API.csproj
dotnet build -c Debug src/DevOp.Toon.API.TestHost/DevOp.Toon.API.TestHost.csproj
```

## Resolution Rules

The build resolves `DevOp.Toon.Core` in this order:

1. If `ToonCoreProjectPath` is set, use that value
2. Otherwise, if `DEVOP_TOON_CORE_CSPROJ` is set, use that value
3. If the path exists and the build configuration is `Debug`, use a `ProjectReference`
4. Otherwise, use the NuGet package reference

## Practical Guidance

- Use the environment variable if you regularly work on both repos locally
- Use the MSBuild property for one-off builds
- Do not rely on the local override for `Release` packaging or CI
- Keep `Release` builds on NuGet so package outputs stay reproducible

## Example Local Setup

If your local repos are:

- `/home/valdi/Projects/DevOp.Toon`
- `/home/valdi/Projects/DevOp.Toon.Core`

then use:

```bash
export DEVOP_TOON_CORE_CSPROJ=/home/valdi/Projects/DevOp.Toon.Core/DevOp.Toon.Core.csproj
```

After that:

```bash
dotnet build -c Debug
```

will use the local `DevOp.Toon.Core` project, while:

```bash
dotnet build -c Release
```

will still use NuGet.
