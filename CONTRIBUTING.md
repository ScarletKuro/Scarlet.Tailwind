# Contributing to Scarlet.Tailwind

Thanks for considering a contribution. This document covers the human side of contributing - getting set
up, running things locally, and what a PR is expected to look like. For the deep technical details (project
structure, the runtime discovery contract, gotchas around packaging/versioning), see [AGENTS.md](AGENTS.md)
- it's written for AI agents but is equally useful background reading for people.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Nothing else. You do not need Tailwind installed - the runtime packages and the CLI download it on demand, and
  the test suite exercises real Tailwind execution using whatever gets downloaded during the build.

## Getting Started

```bash
git clone https://github.com/ScarletKuro/Scarlet.Tailwind.git
cd Scarlet.Tailwind
dotnet build
dotnet test
```

If `dotnet build` and `dotnet test` both succeed, you have a working baseline to branch from.

## Making Changes

1. Create a branch off `master`.
2. Make your change. If you're touching the runtime discovery contract, the CLI's argument forwarding, or
   MSBuild task packaging, read the relevant section of [AGENTS.md](AGENTS.md) first - each of those has a
   subtle failure mode that isn't obvious from the code alone.
3. Add or update tests. Unit tests live in `tests/Scarlet.Tailwind.MSBuild.Tests` and
   `tests/Scarlet.Tailwind.Cli.Tests`; cross-process integration tests that run real Tailwind live in
   `tests/Scarlet.Tailwind.MSBuild.IntegrationTests`; full package-install scenarios live under `tests/e2e/`.
4. Run the full suite before opening a PR:
   ```bash
   dotnet build
   dotnet test
   dotnet pack src/Scarlet.Tailwind.MSBuild/Scarlet.Tailwind.MSBuild.csproj
   ```
   AGENTS.md has a more detailed verification checklist if your change touches packaging or runtime
   resolution.

## Commit Messages

This repo prefixes commits with the component they touch, followed by an imperative summary:

```
Scarlet.Tailwind.Cli: Fix --json report
Scarlet.Tailwind.MSBuild: Make Diagnostics report more consistent
```

For changes that don't belong to a single component (docs, CI, repo-wide tooling), a plain imperative
summary is fine (`Improve docs`, `Add automation`).

## Adding Support for a New Platform

Runtime discovery is designed so this needs no change to `Scarlet.Tailwind.MSBuild` itself - see "Runtime
Discovery Contract" in AGENTS.md for the item contract a new `Scarlet.Tailwind.Runtime.*` package needs to
provide.

## CI

Every push runs the full matrix (Windows, Linux, macOS, both x64 and arm64, plus a musl/Alpine container
leg) via `.github/workflows/ci.yml` - unit tests, integration tests, packaging, and the `tests/e2e/`
scenarios. A PR is expected to pass all of it. Publishing to NuGet.org only happens from a pushed version
tag (see [DEPLOYMENT.md](DEPLOYMENT.md)), never from a branch or PR, so there's no risk of a PR publishing
anything.

## Reporting Bugs / Requesting Features

Open a [GitHub issue](https://github.com/ScarletKuro/Scarlet.Tailwind/issues). Include your OS/architecture,
the `Scarlet.Tailwind.*` package version(s) involved, and, for build failures, the MSBuild or `dotnet tailwind`
output.

## License

By contributing, you agree that your contributions will be licensed under the project's
[MIT License](LICENSE).
