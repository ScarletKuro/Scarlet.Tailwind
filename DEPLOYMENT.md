# Deployment Guide

This document describes how to deploy the Scarlet.Tailwind NuGet packages.

## Overview

The project publishes 18 NuGet packages:

1. `Scarlet.Tailwind.MSBuild` - Main MSBuild task package
2. `Scarlet.Tailwind.Runtime.windows-x64` - Windows x64 runtime, and Windows ARM64 under emulation
3. `Scarlet.Tailwind.Runtime.linux-x64` - Linux x64 runtime
4. `Scarlet.Tailwind.Runtime.linux-arm64` - Linux ARM64 runtime
5. `Scarlet.Tailwind.Runtime.linux-x64-musl` - Linux x64 runtime, musl (Alpine)
6. `Scarlet.Tailwind.Runtime.linux-arm64-musl` - Linux ARM64 runtime, musl (Alpine)
7. `Scarlet.Tailwind.Runtime.darwin-x64` - macOS x64 runtime
8. `Scarlet.Tailwind.Runtime.darwin-arm64` - macOS ARM64 runtime
9. `Scarlet.Tailwind.Cli` - the `dotnet tailwind` tool, which is itself **ten** packages: one per runtime
   identifier (eight), a portable `any` fallback, and a top-level pointer package. A single
   `dotnet pack` produces all of them.

**Seven runtime packages, not eight.** Tailwind publishes no native ARM64 build for Windows, so there is no
`Scarlet.Tailwind.Runtime.windows-arm64`: the `windows-x64` package serves `win-arm64` as well, through a
second lower-priority `@(TailwindRuntimePack)` item pointing at the same binary.

The CLI is the exception — it *does* ship a `win-arm64` package, carrying the x64 binary. That is not an
inconsistency: `dotnet tool` RID selection is NuGet's, not ours, and without its own package a `win-arm64`
host would fall through to `any` and download on first use.

`Scarlet.Tailwind.Core` is a shared library used by both shipping packages. It is deliberately **not**
published: `Scarlet.Tailwind.MSBuild` packs the assembly into its `tools/netstandard2.0/` folder and
`Scarlet.Tailwind.Cli` carries it in its publish output.

## Prerequisites

`deploy.yml` authenticates to NuGet.org via [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC), not a long-lived API key. Before deploying, ensure you have:

1. A Trusted Publishing policy configured on NuGet.org, scoped to this repository and the `deploy.yml` workflow
2. The NuGet.org username that owns that policy, stored as a GitHub secret named `NUGET_USER`

### Setting up Trusted Publishing

1. Go to [NuGet.org](https://www.nuget.org) and sign in
2. Go to your account settings → Trusted Publishing
3. Add a new policy for the Scarlet.Tailwind.* package pattern, scoped to:
   - Repository owner: `ScarletKuro` (or your fork's owner)
   - Repository: `Scarlet.Tailwind`
   - Workflow file: `.github/workflows/deploy.yml`
4. In your GitHub repository:
   - Go to Settings → Secrets and variables → Actions
   - Click "New repository secret"
   - Name: `NUGET_USER`
   - Value: your NuGet.org username
   - Click "Add secret"

At run time, the `NuGet/login@v1` step in `deploy.yml` exchanges the job's OIDC token (granted by the
workflow's `id-token: write` permission) plus `NUGET_USER` for a short-lived API key
(`steps.login.outputs.NUGET_API_KEY`) that `dotnet nuget push` uses. Nothing long-lived is stored as a
secret, and there is no `NUGET_KEY` to rotate or leak.

Packages are also mirrored to GitHub Packages using the workflow's own `GITHUB_TOKEN` (`packages: write`)
— no extra setup needed for that.

## Automatic Tailwind Version Bump

`.github/workflows/tailwind-version-bump.yml` runs monthly (and on manual dispatch). It checks the
latest stable SemVer `tailwindlabs/tailwindcss` release against `TailwindVersion` in
`Directory.Build.props`; if newer,
it commits the bump (resetting `TailwindCliRevision` to 0) directly to `master` and dispatches `deploy.yml`
with `release_target: runtime-and-cli`. That deploy run still runs the full test matrix first, so a Tailwind
release that breaks something here fails the workflow instead of publishing.

## Deployment Process

The deployment is automated through GitHub Actions. A SemVer 2.0 tag publishes every package family;
manual runs can publish only MSBuild, the runtime packages and CLI, or the CLI.

### Supported Version Formats

The workflow supports SemVer 2.0 version formats, including:
- Release versions: `1.0.0`, `2.1.3`, `3.0.0`
- Pre-release versions: `1.0.0-preview.1`, `1.0.0-rc.1`, `2.0.0-beta.2`
- Build metadata: `1.0.0-preview.1+build.123`

### Steps to Deploy

1. **Ensure all changes are committed and pushed to the main development branch**
   ```bash
   # Switch to your main branch (master, main, etc.)
   git checkout master
   git pull origin master
   ```

2. **Create and push a version tag**
   ```bash
   # For a release version
   git tag 1.0.0
   git push origin 1.0.0

   # For a pre-release version
   git tag 1.0.0-preview.1
   git push origin 1.0.0-preview.1

   # For an RC version
   git tag 1.0.0-rc.1
   git push origin 1.0.0-rc.1
   ```

3. **Monitor the deployment**
   - Go to the "Actions" tab in your GitHub repository
   - Find the "Deploy to NuGet" workflow run
   - Monitor the progress through these stages:
     - Checkout code
     - Setup .NET
     - Extract version from tag
     - Restore dependencies
     - Build projects with version
     - Pack NuGet packages
     - Push to NuGet.org
     - Run the applicable package-installation and CLI end-to-end scenarios

4. **Verify the deployment**
   - Check [NuGet.org](https://www.nuget.org/packages/Scarlet.Tailwind.MSBuild/) for the new package version
   - It may take a few minutes for the package to appear in search results
   - Before publishing, the workflow installs the locally packed packages and confirms they work correctly

## What the Workflow Does

The deployment workflow (`.github/workflows/deploy.yml`) performs the following steps:

1. **Runs tests on all supported host families**:
   - Restores dependencies
   - Builds the solution
   - Runs all tests with code coverage
   - Exercises x64 and ARM64 hosts on Windows, Linux, and macOS, plus x64 and ARM64 musl containers
   - Only proceeds to deployment if all tests pass

2. **Extracts the version** from the Git tag (e.g., `1.0.0` from `refs/tags/1.0.0`)

3. **Builds all projects** with the specified version:
   ```bash
   dotnet build --configuration Release /p:Version=<version>
   ```

4. **Packs the main MSBuild package**:
   - Creates both `.nupkg` (main package) and `.snupkg` (symbols package)
   - Includes the MSBuild task DLL and build files

5. **Packs all runtime packages**:
   - Downloads Tailwind binaries for each platform if not already cached
   - Packages the runtime binaries in platform-specific packages
   - Each runtime package contains the Tailwind executable for its target platform
   - All packages are marked as `DevelopmentDependency=True` to prevent transitive dependencies

6. **Verifies the packages before publication**:
   - Installs the newly packed packages from local build output
   - Compiles a real Tailwind stylesheet and verifies the generated static web asset
   - Installs and runs the packed CLI when that package family is selected
   - Fails the workflow before any immutable NuGet package is published if verification fails

7. **Pushes to NuGet.org and GitHub Packages**:
   - Logs in to NuGet.org via Trusted Publishing (OIDC), exchanging the job's ID token for a short-lived API key
   - Uploads all `.nupkg` files (and `.snupkg` symbol files) to NuGet.org
   - Uploads all `.nupkg` files to GitHub Packages using `GITHUB_TOKEN`
   - Uses `--skip-duplicate` on both feeds to avoid errors if the version already exists

**Note:** The workflow will only proceed to packaging and deployment if all tests pass on all platforms (Ubuntu, Windows, macOS). This ensures that only tested and verified code is deployed to NuGet.org.

## Troubleshooting

### Tag push doesn't trigger the workflow

- Verify the tag follows SemVer format: `X.Y.Z` or `X.Y.Z-prerelease`
- Check the workflow file for correct tag pattern matching
- Ensure the workflow file is on the branch that receives the tag push

### Build fails during packaging

- Check that all runtime binaries were downloaded successfully
- Verify the `TailwindVersion` in `Directory.Build.props` is valid
- Check for network issues downloading Tailwind binaries from GitHub releases

### Push to NuGet fails

- Verify the `NUGET_USER` secret is set and matches the username on the Trusted Publishing policy
- Verify the Trusted Publishing policy on NuGet.org is still scoped to this repo and `deploy.yml`
- Check that the `deploy` job still has `id-token: write` permission - without it, `NuGet/login@v1` cannot mint an API key
- Ensure you're not trying to push a version that already exists (unless using --skip-duplicate)
- Check NuGet.org service status

### Verification fails

- Check the workflow logs for specific error messages
- The verification step tests that:
  - Packages can be installed from local source
  - TailwindCompileTask can find and execute the Tailwind runtime
  - Tailwind can compile a real stylesheet successfully

## Manual Deployment

If you need to deploy manually (not recommended), follow these steps. Trusted Publishing only works from
the OIDC-enabled CI job, so a manual push needs a classic NuGet.org API key instead (Account settings →
API Keys, "Push" permission for the Scarlet.Tailwind.* packages) - substitute it for `YOUR_API_KEY` below.

```bash
# Set the version
VERSION="1.0.0"

# Restore and build
dotnet restore
dotnet build --configuration Release /p:Version=$VERSION

# Pack all packages
dotnet pack src/Scarlet.Tailwind.MSBuild/Scarlet.Tailwind.MSBuild.csproj --configuration Release --output ./packages /p:Version=$VERSION
dotnet pack src/Scarlet.Tailwind.Runtime.windows-x64/Scarlet.Tailwind.Runtime.windows-x64.csproj --configuration Release --output ./packages
dotnet pack src/Scarlet.Tailwind.Runtime.linux-x64/Scarlet.Tailwind.Runtime.linux-x64.csproj --configuration Release --output ./packages
dotnet pack src/Scarlet.Tailwind.Runtime.linux-arm64/Scarlet.Tailwind.Runtime.linux-arm64.csproj --configuration Release --output ./packages
dotnet pack src/Scarlet.Tailwind.Runtime.linux-x64-musl/Scarlet.Tailwind.Runtime.linux-x64-musl.csproj --configuration Release --output ./packages
dotnet pack src/Scarlet.Tailwind.Runtime.linux-arm64-musl/Scarlet.Tailwind.Runtime.linux-arm64-musl.csproj --configuration Release --output ./packages
dotnet pack src/Scarlet.Tailwind.Runtime.darwin-x64/Scarlet.Tailwind.Runtime.darwin-x64.csproj --configuration Release --output ./packages
dotnet pack src/Scarlet.Tailwind.Runtime.darwin-arm64/Scarlet.Tailwind.Runtime.darwin-arm64.csproj --configuration Release --output ./packages

# Pack the dotnet tool. No /p:Version - it versions from TailwindVersion, like the runtime packages.
# This one command produces ten packages: eight RID-specific, one portable "any", one pointer.
dotnet pack src/Scarlet.Tailwind.Cli/Scarlet.Tailwind.Cli.csproj --configuration Release --output ./packages

# Push to NuGet (requires API key).
#
# The pointer package MUST go last. The .NET CLI resolves a tool's RID-specific package from the version
# of the pointer package, so if the pointer is live before its RID packages, every install in that window
# fails. A "*.nupkg" glob gets this exactly backwards: '.' sorts before any letter, so
# Scarlet.Tailwind.Cli.<version>.nupkg would be pushed ahead of Scarlet.Tailwind.Cli.<rid>.<version>.nupkg.
TAILWIND_VERSION=$(sed -n 's/.*<TailwindVersion>\([^<]*\)<\/TailwindVersion>.*/\1/p' Directory.Build.props)
TAILWIND_CLI_REVISION=$(sed -n 's/.*<TailwindCliRevision>\([^<]*\)<\/TailwindCliRevision>.*/\1/p' Directory.Build.props)
if [ "$TAILWIND_CLI_REVISION" = "0" ]; then
  CLI_VERSION="$TAILWIND_VERSION"
else
  CLI_VERSION="$TAILWIND_VERSION.$TAILWIND_CLI_REVISION"
fi
CLI_POINTER="./packages/Scarlet.Tailwind.Cli.${CLI_VERSION}.nupkg"

for package in ./packages/*.nupkg; do
  [ "$package" = "$CLI_POINTER" ] && continue
  dotnet nuget push "$package" --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
done

dotnet nuget push "$CLI_POINTER" --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## Version Strategy

Recommended version strategy:
- **Major version (X.0.0)**: Breaking changes, major new features
- **Minor version (0.X.0)**: New features, non-breaking changes
- **Patch version (0.0.X)**: Bug fixes, minor improvements
- **Pre-release (-preview.X)**: Early access, testing
- **Release candidate (-rc.X)**: Final testing before release

## Notes

- `Scarlet.Tailwind.MSBuild` versions from the git tag. `Scarlet.Tailwind.Runtime.*` and `Scarlet.Tailwind.Cli` version
  from `$(TailwindVersion)` in `Directory.Build.props`, so their version *is* the Tailwind version they contain
- **The `Scarlet.Tailwind.Cli` pointer package must be pushed last**, after all nine of its sub-packages. The
  .NET CLI resolves the RID-specific package from the pointer's version, so publishing the pointer first
  makes every install fail until the rest land. `deploy.yml` pushes file-by-file for this reason rather
  than using a `*.nupkg` glob
- A CLI-only fix cannot be released at an unchanged `TailwindVersion`: `--skip-duplicate` would drop it
  silently. Bump `<TailwindCliRevision>` in `Directory.Build.props` (0 → 1, giving `4.3.3.1`) to ship one, and
  reset it to 0 the next time `TailwindVersion` changes. Revision 0 publishes as plain `4.3.3`, because NuGet
  normalises a trailing zero away
- Symbol packages (`.snupkg`) are uploaded where produced. The CLI deliberately does not create one because
  a .NET tool has no `lib/` or `ref/` assembly for NuGet.org to associate with it
- The workflow uses `--skip-duplicate` to allow re-running failed deployments
- Runtime binaries are downloaded on-demand during build if not present
- The verification steps install the packed packages from a local feed and exercise runtime-package,
  download, static-web-asset, multi-targeting, cleaning, and CLI embedding behavior
