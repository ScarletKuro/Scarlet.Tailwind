# Scarlet.Tailwind

![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/ScarletKuro/Scarlet.Tailwind/.github/workflows/ci.yml?branch=master&logo=github&style=flat-square)
[![codecov](https://codecov.io/gh/ScarletKuro/Scarlet.Tailwind/graph/badge.svg?token=6WWT9I5PU5)](https://codecov.io/gh/ScarletKuro/Scarlet.Tailwind)
[![GitHub](https://img.shields.io/github/license/ScarletKuro/Scarlet.Tailwind?color=594ae2&logo=github&style=flat-square)](https://github.com/ScarletKuro/Scarlet.Tailwind/blob/master/LICENSE)

[Tailwind CSS](https://tailwindcss.com/) for .NET, as a pinned NuGet dependency or .NET tool rather than
something you install separately.

Compile Tailwind before Blazor, Razor Class Library, and ASP.NET Core static web assets are discovered, or
run the official standalone CLI with `dotnet tailwind`, on Windows, Linux and macOS (x64 and ARM64). No
Node.js, npm, or `node_modules` required.

## Which package do I want?

| | Package | Use it when |
|---|---|---|
| **During a build** | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.MSBuild?color=ff4081&label=Scarlet.Tailwind.MSBuild&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.MSBuild/) | You want `dotnet build` to compile Tailwind into static web assets — Blazor, Razor Class Libraries, ASP.NET Core |
| **On the command line** | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.Cli?color=ff4081&label=Scarlet.Tailwind.Cli&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.Cli/) | You want `dotnet tailwind ...`, including `--watch`, pinned per repository |

Raw CLI invocations and build integration work independently. When both packages are installed,
`dotnet tailwind watch` intentionally bridges them by reading the project's evaluated MSBuild configuration.

### Scarlet.Tailwind.MSBuild — Tailwind during `dotnet build`

```bash
dotnet add package Scarlet.Tailwind.MSBuild
dotnet add package Scarlet.Tailwind.Runtime.windows-x64
```

```xml
<ItemGroup>
  <TailwindBeforeStaticWebAssets Include="Styles/app.css">
    <OutputPath>wwwroot/css/app.css</OutputPath>
  </TailwindBeforeStaticWebAssets>
</ItemGroup>
```

The Tailwind runtime comes either from a platform-specific `Scarlet.Tailwind.Runtime.*` package or from an
on-demand download, whichever suits your build.

📖 **[Full documentation →](src/Scarlet.Tailwind.MSBuild/README.md)** — installation, runtime options,
Tailwind v4 stylesheet configuration, task properties, item metadata, static web assets, incrementality,
cleaning, and `dotnet watch` integration.

### Scarlet.Tailwind.Cli — Tailwind on the command line

Use the CLI independently by passing Tailwind's native arguments directly:

```bash
dotnet new tool-manifest
dotnet tool install Scarlet.Tailwind.Cli
dotnet tailwind --input Styles/app.css --output wwwroot/css/app.css --watch
```

When the project also references `Scarlet.Tailwind.MSBuild`, the two packages work together for a better
watch loop: the project-aware command reuses every evaluated entry point and build setting without repeating
paths on the command line.

```bash
dotnet tailwind watch
```

The tool version identifies the bundled Tailwind version, with an optional fourth component for CLI-only
revisions, so `.config/dotnet-tools.json` pins Tailwind alongside the rest of your tooling.
`Scarlet.Tailwind.Cli` is a pointer package; installing it also pulls a matching
`Scarlet.Tailwind.Cli.*` sub-package for your platform, and that one embeds Tailwind, so it needs no network
at run time.

📖 **[Full documentation →](src/Scarlet.Tailwind.Cli/README.md)** — installing, project-aware watch mode,
argument forwarding, runtime resolution, diagnostics, and environment variables.

## Available Packages

| Package | Contains |
|---------|----------|
| [Scarlet.Tailwind.MSBuild](https://www.nuget.org/packages/Scarlet.Tailwind.MSBuild/) | The MSBuild task. Versioned independently. |
| [Scarlet.Tailwind.Cli](https://www.nuget.org/packages/Scarlet.Tailwind.Cli/) | The `dotnet tailwind` tool. Version = the embedded Tailwind version. |
| [Scarlet.Tailwind.Runtime.windows-x64](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.windows-x64/) | Tailwind for Windows x64, and Windows ARM64 under emulation |
| [Scarlet.Tailwind.Runtime.linux-x64](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.linux-x64/) | Tailwind for Linux x64 |
| [Scarlet.Tailwind.Runtime.linux-arm64](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.linux-arm64/) | Tailwind for Linux ARM64 |
| [Scarlet.Tailwind.Runtime.linux-x64-musl](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.linux-x64-musl/) | Tailwind for Linux x64, musl (Alpine) |
| [Scarlet.Tailwind.Runtime.linux-arm64-musl](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.linux-arm64-musl/) | Tailwind for Linux ARM64, musl (Alpine) |
| [Scarlet.Tailwind.Runtime.darwin-x64](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.darwin-x64/) | Tailwind for macOS x64 |
| [Scarlet.Tailwind.Runtime.darwin-arm64](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.darwin-arm64/) | Tailwind for macOS ARM64 |

The `Scarlet.Tailwind.Runtime.*` packages are consumed by `Scarlet.Tailwind.MSBuild` and can also be
installed directly (see its README); the CLI embeds its own Tailwind and does not use them. Their package
version is the Tailwind version they contain.

`Scarlet.Tailwind.Cli` restores its own per-platform `Scarlet.Tailwind.Cli.*` sub-packages (one per RID,
plus a portable `.any` fallback) automatically — unlike the `Runtime.*` packages, these are a `dotnet tool`
implementation detail, never meant to be installed directly, so they are not listed here.

## Supported Platforms

Windows, Linux and macOS on **x64 or arm64**, including musl-based Linux distributions such as Alpine.
Tailwind does not publish a native Windows ARM64 executable, so Windows ARM64 uses the Windows x64 binary
under emulation. Any other architecture gets an explanatory error rather than a mismatched binary — point
at your own Tailwind with `SCARLET_TAILWIND_PATH` (CLI) or `TailwindRuntimeDirectory` (MSBuild) if you need
one.

## Development

### Building the Package

```bash
dotnet build
```

The first build downloads seven Tailwind binaries into the runtime package directories. They are
gitignored and fetched only once per version.

### Running Tests

Unit tests:

```bash
dotnet test tests/Scarlet.Tailwind.MSBuild.Tests/Scarlet.Tailwind.MSBuild.Tests.csproj
```

Integration tests:

```bash
dotnet test tests/Scarlet.Tailwind.MSBuild.IntegrationTests/Scarlet.Tailwind.MSBuild.IntegrationTests.csproj
```

CLI tests:

```bash
dotnet test tests/Scarlet.Tailwind.Cli.Tests/Scarlet.Tailwind.Cli.Tests.csproj
```

All tests:

```bash
dotnet test
```

End-to-end scenarios pack real packages into a local feed and consume them from a temporary project. The
CLI scenarios additionally take the independently revisioned CLI package version:

```bash
tests/e2e/package-installation/verify.sh "$PWD" 1.0.0-local 4.3.3
tests/e2e/cli-tool/verify.sh            "$PWD" 1.0.0-local 4.3.3 4.3.3.1
tests/e2e/hot-reload/verify.sh          "$PWD" 1.0.0-local 4.3.3 4.3.3.1
```

### Creating a Package

```bash
dotnet pack src/Scarlet.Tailwind.MSBuild/Scarlet.Tailwind.MSBuild.csproj
```

The CLI packs into ten packages at once — eight runtime identifiers, a portable fallback, and a top-level
pointer package:

```bash
dotnet pack src/Scarlet.Tailwind.Cli/Scarlet.Tailwind.Cli.csproj
```

`src/Scarlet.Tailwind.Core` is a shared library used by both shipping packages. It is deliberately not
published: the MSBuild package packs the assembly into its `tools/` folder, and the CLI carries it in its
publish output.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full development workflow.

## Requirements

- .NET / .NET Core (no .NET Framework support)
- Supported on Windows, Linux, and macOS

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

### Bundled Software Licenses

This package distributes the official Tailwind CSS standalone executable:

- **Tailwind CSS**: MIT License - Copyright (c) Tailwind Labs, Inc.

See [LICENSE-3RD-PARTY.txt](LICENSE-3RD-PARTY.txt) for bundled third-party license notices.

## Credits

- Built by [ScarletKuro](https://github.com/ScarletKuro)
- Uses [Tailwind CSS](https://tailwindcss.com/) - a utility-first CSS framework

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build, test, and submit a
Pull Request.
