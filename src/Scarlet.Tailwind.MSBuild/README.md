# Scarlet.Tailwind.MSBuild

Compile [Tailwind CSS](https://tailwindcss.com) during `dotnet build`, before ASP.NET Core, Blazor and Razor
Class Library static web assets are discovered. No Node.js, no npm, no `node_modules`.

Scarlet.Tailwind.MSBuild is intentionally explicit: it compiles only
`@(TailwindBeforeStaticWebAssets)` items. There is no automatic `Styles/` convention, no
`tailwind.config.js` auto-detection, and no JSON configuration dialect.

```xml
<PackageReference Include="Scarlet.Tailwind.MSBuild" Version="1.0.0" PrivateAssets="all" />
<PackageReference Include="Scarlet.Tailwind.Runtime.windows-x64" Version="4.3.3" PrivateAssets="all" />

<ItemGroup>
  <TailwindBeforeStaticWebAssets Include="Styles/app.css">
    <OutputPath>wwwroot/css/app.css</OutputPath>
  </TailwindBeforeStaticWebAssets>
</ItemGroup>
```

## Table of Contents

- [Installation](#installation)
- [Runtime Options](#runtime-options)
  - [Option 1: Runtime Download](#option-1-runtime-download)
  - [Option 2: Platform-Specific Runtime Packages](#option-2-platform-specific-runtime-packages)
  - [Option 3: Conditional Package References](#option-3-conditional-package-references)
  - [How the Runtime Is Discovered](#how-the-runtime-is-discovered)
- [Compile Before Static Web Assets](#compile-before-static-web-assets)
- [Writing Your Entry Stylesheet](#writing-your-entry-stylesheet)
- [Properties](#properties)
- [Item Metadata](#item-metadata)
- [Task Parameters](#task-parameters)
  - [Output Parameters](#output-parameters)
- [Incrementality](#incrementality)
- [Cleaning](#cleaning)
- [dotnet watch Integration](#dotnet-watch-integration)
- [Supported Platforms](#supported-platforms)
- [Links](#links)
- [License](#license)

## Installation

```bash
dotnet add package Scarlet.Tailwind.MSBuild
```

`PrivateAssets="all"` is recommended: this is a build-time dependency and should not flow to projects that
consume your package.

> **Note:** The base package does not contain Tailwind. Choose one of the runtime options below.

## Runtime Options

After installing `Scarlet.Tailwind.MSBuild`, provide the Tailwind executable through an on-demand download
or a runtime package.

### Option 1: Runtime Download

No runtime package at all — the build fetches Tailwind on first use and caches it:

```xml
<PropertyGroup>
  <TailwindRuntimeDownload>true</TailwindRuntimeDownload>
  <TailwindVersionDownload>4.3.3</TailwindVersionDownload>
  <TailwindRuntimeDirectory>$(MSBuildProjectDirectory)/runtimes</TailwindRuntimeDirectory>
</PropertyGroup>
```

`TailwindRuntimeDirectory` is required here — it is where the download lands. The download is
checksum-verified against the `sha256sums.txt` GitHub publishes with each release and published atomically.
Point several projects at one shared directory and they coordinate through a named mutex instead of racing
over the executable. Leaving `TailwindVersionDownload` empty resolves the latest GitHub release and
re-checks it on every build.

### Option 2: Platform-Specific Runtime Packages

Install the runtime package matching the **build host**, not the project's target RID:

```bash
# Windows x64 and Windows ARM64 under emulation
dotnet add package Scarlet.Tailwind.Runtime.windows-x64

# Linux x64 / ARM64 (glibc)
dotnet add package Scarlet.Tailwind.Runtime.linux-x64
dotnet add package Scarlet.Tailwind.Runtime.linux-arm64

# Linux x64 / ARM64 (musl, for example Alpine)
dotnet add package Scarlet.Tailwind.Runtime.linux-x64-musl
dotnet add package Scarlet.Tailwind.Runtime.linux-arm64-musl

# macOS x64 / ARM64
dotnet add package Scarlet.Tailwind.Runtime.darwin-x64
dotnet add package Scarlet.Tailwind.Runtime.darwin-arm64
```

The runtime packages are versioned independently from `Scarlet.Tailwind.MSBuild`; their package version is
the Tailwind CSS version they contain.

**Available Runtime Packages:**

| Build host | Tailwind release asset | Package name | Package version |
| --- | --- | --- | --- |
| Windows x64 | `tailwindcss-windows-x64.exe` | `Scarlet.Tailwind.Runtime.windows-x64` | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.Runtime.windows-x64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.windows-x64/) |
| Windows ARM64 | `tailwindcss-windows-x64.exe` under emulation | `Scarlet.Tailwind.Runtime.windows-x64` | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.Runtime.windows-x64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.windows-x64/) |
| Linux x64 | `tailwindcss-linux-x64` | `Scarlet.Tailwind.Runtime.linux-x64` | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.Runtime.linux-x64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.linux-x64/) |
| Linux ARM64 | `tailwindcss-linux-arm64` | `Scarlet.Tailwind.Runtime.linux-arm64` | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.Runtime.linux-arm64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.linux-arm64/) |
| Linux x64, musl | `tailwindcss-linux-x64-musl` | `Scarlet.Tailwind.Runtime.linux-x64-musl` | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.Runtime.linux-x64-musl?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.linux-x64-musl/) |
| Linux ARM64, musl | `tailwindcss-linux-arm64-musl` | `Scarlet.Tailwind.Runtime.linux-arm64-musl` | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.Runtime.linux-arm64-musl?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.linux-arm64-musl/) |
| macOS x64 | `tailwindcss-macos-x64` | `Scarlet.Tailwind.Runtime.darwin-x64` | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.Runtime.darwin-x64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.darwin-x64/) |
| macOS ARM64 | `tailwindcss-macos-arm64` | `Scarlet.Tailwind.Runtime.darwin-arm64` | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Tailwind.Runtime.darwin-arm64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Tailwind.Runtime.darwin-arm64/) |

Tailwind publishes no native Windows ARM64 executable, so the Windows x64 package deliberately serves
both `win-x64` and `win-arm64`.

### Option 3: Conditional Package References

Each runtime package contains a complete Tailwind executable, so referencing every package is wasteful.
Detect the host and reference only what it needs — this is what a repository building on several platforms
wants in `Directory.Build.props`:

```xml
<!-- Detect the build host -->
<PropertyGroup>
  <IsWindows Condition="'$(OS)' == 'Windows_NT'">true</IsWindows>
  <IsLinux Condition="Exists('/proc')">true</IsLinux>
  <IsMacOS Condition="Exists('/System/Library/CoreServices/SystemVersion.plist')">true</IsMacOS>
  <IsMusl Condition="Exists('/lib/ld-musl-x86_64.so.1') OR Exists('/lib/ld-musl-aarch64.so.1')">true</IsMusl>
  <IsX64 Condition="'$(PROCESSOR_ARCHITECTURE)' == 'AMD64' OR '$(PROCESSOR_IDENTIFIER)' == 'AMD64'">true</IsX64>
  <IsARM64 Condition="'$(PROCESSOR_ARCHITECTURE)' == 'ARM64' OR '$(PROCESSOR_IDENTIFIER)' == 'ARM64'">true</IsARM64>
</PropertyGroup>

<!-- Tailwind publishes only an x64 Windows executable; Windows ARM64 runs it under emulation. -->
<ItemGroup Condition="'$(IsWindows)' == 'true'">
  <PackageReference Include="Scarlet.Tailwind.Runtime.windows-x64" Version="4.3.3" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsX64)' == 'true' AND '$(IsMusl)' != 'true'">
  <PackageReference Include="Scarlet.Tailwind.Runtime.linux-x64" Version="4.3.3" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsARM64)' == 'true' AND '$(IsMusl)' != 'true'">
  <PackageReference Include="Scarlet.Tailwind.Runtime.linux-arm64" Version="4.3.3" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsX64)' == 'true' AND '$(IsMusl)' == 'true'">
  <PackageReference Include="Scarlet.Tailwind.Runtime.linux-x64-musl" Version="4.3.3" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsARM64)' == 'true' AND '$(IsMusl)' == 'true'">
  <PackageReference Include="Scarlet.Tailwind.Runtime.linux-arm64-musl" Version="4.3.3" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsMacOS)' == 'true' AND '$(IsX64)' == 'true'">
  <PackageReference Include="Scarlet.Tailwind.Runtime.darwin-x64" Version="4.3.3" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsMacOS)' == 'true' AND '$(IsARM64)' == 'true'">
  <PackageReference Include="Scarlet.Tailwind.Runtime.darwin-arm64" Version="4.3.3" PrivateAssets="all" />
</ItemGroup>
```

`IsMusl` separates Alpine from glibc distributions; without it a musl host restores a glibc executable and
fails at launch rather than at restore.

### How the Runtime Is Discovered

Tailwind runs on the machine performing the build, not on the project's target runtime identifier, so NuGet's
RID-graph resolution is the wrong mechanism. Instead each runtime package contributes a
`@(TailwindRuntimePack)` item, and the task picks the best match for the build host.

| Metadata | Meaning |
|---|---|
| `Rid` | The runtime identifier this pack serves. Required. |
| `RuntimesPath` | Directory containing `<rid>/native/<executable>`. Required. |
| `NativeRid` | The subdirectory the executable is really stored under. Defaults to `Rid`. |
| `Variant` | Diagnostic only. |
| `Priority` | Higher wins when several packs serve the same RID. Defaults to `0`. |

You normally never author this item. You would if you want the build to use a Tailwind executable you
supply yourself — a pre-release or a locally built one — without waiting for a runtime package:

```xml
<ItemGroup>
  <TailwindRuntimePack Include="MyCompany.Tailwind.linux-x64-custom">
    <Rid>linux-x64</Rid>
    <RuntimesPath>$(MSBuildProjectDirectory)/tailwind/runtimes</RuntimesPath>
    <Priority>100</Priority>
  </TailwindRuntimePack>
</ItemGroup>
```

The practical consequences:

- **A new runtime identifier needs no change to this package.** Publish a package that emits the item.
- **You can point the build at your own Tailwind** by declaring the item yourself.
- `TailwindRuntimeDirectory` overrides packs entirely; `TailwindRuntimeDownload=true` bypasses them.

Resolution order is `TailwindRuntimeDownload`, then `TailwindRuntimeDirectory`, then the packs.

A pack that omits `Rid` or `RuntimesPath` is skipped with a build warning naming it, and a non-numeric
`Priority` warns and falls back to `0`. If a candidate executable is missing, the resolver tries the next
pack for that RID.

> **Note:** Runtime packages are development dependencies, so their props apply to the project referencing
> them directly. They do not flow to projects that reference *that* project — add the `PackageReference`
> in each project that compiles Tailwind, or in a shared `Directory.Build.props`.

## Compile Before Static Web Assets

Generating CSS is easy; getting it into the pipeline is the part that goes wrong. Scarlet.Tailwind runs
before `ResolveProjectStaticWebAssets`, so the generated stylesheet is a real static web asset:

- **Razor Class Libraries pack it under `staticwebassets/`**, so a consuming app serves it at
  `_content/<PackageId>/css/app.css`. A plain `Content` item packs it under `content/`, where nothing will
  ever serve it.
- **Publishing fingerprints it**, so it gets a content-hashed route and immutable caching.
- **`dotnet clean` removes it**, because the task records what it generated.

A `BeforeTargets="Build"` exec that shells out to Tailwind gets none of that.

```xml
<ItemGroup>
  <TailwindBeforeStaticWebAssets Include="Styles/app.css">
    <OutputPath>wwwroot/css/app.css</OutputPath>
  </TailwindBeforeStaticWebAssets>
</ItemGroup>
```

## Writing Your Entry Stylesheet

**Read this section even if you know Tailwind.** In v4 the stylesheet *is* the configuration, and what ends
up in your CSS depends on what Tailwind finds on disk — which in a .NET project is not what upstream's
documentation assumes.

A complete entry stylesheet is one line:

```css
@import "tailwindcss";
```

There is no `tailwind.config.js` to create, no `content` array, and no `@tailwind base/components/utilities`
triple. Tailwind v4 finds your classes by scanning, automatically.

### Exclude `bin` and `obj`

This is the recommended starting point for a .NET project:

```css
@import "tailwindcss";

@source not "./bin";
@source not "./obj";
```

Tailwind's built-in ignore list covers `node_modules`, `.git`, `.venv` and friends — **every entry is from
the JavaScript or Python ecosystem. `bin` and `obj` are not on it.**

That matters more than it sounds. `obj` holds the Razor compiler's generated `*.g.cs` files, which contain
the class names from *previous* builds. A class you deleted from a `.razor` file this morning is still
present in yesterday's generated source, so Tailwind keeps emitting CSS for it. The only symptom is a
stylesheet that will not shrink, and nothing points at the scanner.

### What `.gitignore` Already Does for You

The scanner honours `.gitignore` even when there is no `.git` directory, so the standard dotnet `.gitignore`
— which excludes `bin/` and `obj/` — already covers most projects. The two lines above are insurance for the
cases it does not: a project with no `.gitignore`, one whose ignore rules do not cover these directories, or
a source tree consumed from somewhere the ignore file does not apply.

They cost nothing when `.gitignore` has already done the job.

### Coming from v3? The `content` Array Translates Directly

If you would rather enumerate sources than exclude them, `source(none)` turns off automatic detection:

```css
@import "tailwindcss" source(none);
@source "./**/*.{cshtml,razor}";
```

Brace expansion works, so a v3 `content: ["./**/*.{cshtml,razor}"]` maps one to one. This is stricter and
faster, at the cost of silently missing classes in a file type nobody remembered to list — a `.cs` file that
builds a class string, say. Prefer exclusion as the default and treat `source(none)` as a deliberate opt-in.

### Scanning Outside the Project

```css
@import "tailwindcss";
@source "../SharedComponents";
```

Or move the base entirely, which is useful in a monorepo:

```css
@import "tailwindcss" source("../src");
```

### Theme and Legacy Config

Customise through `@theme`:

```css
@import "tailwindcss";

@theme {
  --color-brand: oklch(0.55 0.2 264);
}
```

A legacy `tailwind.config.js` is **no longer auto-detected**. Load it explicitly, and note that
`corePlugins`, `safelist` and `separator` are unsupported in v4:

```css
@import "tailwindcss";
@config "../../tailwind.config.js";
```

### Why These Paths Always Mean the Same Thing

`Scarlet.Tailwind` always passes `--cwd`, defaulting to `$(MSBuildProjectDirectory)`. Without it, the scan
root would be wherever the build happened to be invoked from, and `dotnet build` in the project folder would
produce different CSS from `dotnet build src/App`. Relative `@source` paths are therefore always relative to
your project directory. Change it with [`TailwindCwd`](#properties) if you need to.

## Properties

| Property | Default | Meaning |
|---|---|---|
| `TailwindEnabled` | `true` | Set to `false` to skip Tailwind entirely |
| `TailwindMinify` | `Auto` | `Auto`, `true`, `false`. Renders `--minify` |
| `TailwindOptimize` | `false` | Renders `--optimize`. Ignored when minifying |
| `TailwindMap` | `Auto` | `Auto`, `true`, `false`, or a path. `true` inlines, a path writes an external map |
| `TailwindSilent` | `false` | Renders `--silent`, suppressing Tailwind's non-error output |
| `TailwindCwd` | project directory | Working directory for source detection |
| `TailwindAdditionalArguments` | empty | Extra arguments for future Tailwind options; task-owned paths, maps and watch flags are rejected |
| `TailwindRuntimeDirectory` | empty | Explicit runtime directory containing `<rid>/native/<executable>` |
| `TailwindRuntimeDownload` | `false` | Download the runtime instead of using runtime packs |
| `TailwindVersionDownload` | empty | Version to download. Empty means latest |
| `TailwindDownloadMutexTimeoutSeconds` | `300` | Timeout for cross-process download coordination |
| `TailwindStampDirectory` | `$(IntermediateOutputPath)\Scarlet.Tailwind` | Settings stamp and manifest directory |
| `TailwindTimeoutMilliseconds` | `0` | Maximum time per invocation. `0` waits indefinitely |

`Auto` resolves from `$(Configuration)`:

| Property | Debug | Release |
|---|---|---|
| `TailwindMinify` | `false` | `true` |
| `TailwindMap` | `true` (inline) | `false` |

`TailwindMap` defaulting to *inline* rather than to a file is deliberate: one output file means one static
web asset to fingerprint and one file to clean. The larger dev-time stylesheet never ships, because Release
emits no map at all. Set a path when you want an external `.css.map`; the task then tracks both files.

`--minify` already implies `--optimize` — Tailwind describes it as "Optimize and minify the output" — so when
both are set the task renders `--minify` only. `TailwindOptimize` on its own covers the narrower case of
wanting the optimiser's transforms with readable output.

`TailwindAdditionalArguments` cannot contain `--input`/`-i`, `--output`/`-o`, `--cwd`, or `--map`: those
values determine the generated-file manifest and must come from the item and its metadata. `--watch`/`-w`
and `--poll` are also rejected because a watcher must never be started by a build. See
[dotnet watch integration](#dotnet-watch-integration).

## Item Metadata

Every compile setting can be overridden per entry point:

```xml
<TailwindBeforeStaticWebAssets Include="Styles/app.css">
  <OutputPath>wwwroot/css/app.css</OutputPath>
  <Minify>true</Minify>
  <Optimize>false</Optimize>
  <Map>wwwroot/css/app.css.map</Map>
  <Silent>true</Silent>
  <Cwd>..</Cwd>
  <AdditionalArguments></AdditionalArguments>
</TailwindBeforeStaticWebAssets>
```

`OutputPath` is required. `Minify`, `Optimize`, `Map`, `Silent` and `Cwd` override the corresponding
property; `AdditionalArguments` is concatenated, global first then item.

Multiple entry points are supported and each runs in its own Tailwind process — one invocation compiles
exactly one input to one output, so there is no batching to be had.

## Task Parameters

`RunTailwindBeforeStaticWebAssets` sets these from the properties above. Call `TailwindCompileTask`
directly only if you need a compile outside that target.

| Parameter | Required | Description | Default |
| --- | --- | --- | --- |
| `Compilations` | Yes | The items to compile, normally `@(TailwindBeforeStaticWebAssets)` | - |
| `ProjectDirectory` | Yes | Directory that relative input, output and stamp paths resolve against, normally `$(MSBuildProjectDirectory)` | - |
| `Configuration` | No | Drives the `Auto` defaults for `Minify` and `Map` | `Debug` |
| `Minify` | No | `Auto`, `true`, or `false`; adds `--minify` when enabled | `Auto` |
| `Optimize` | No | Adds `--optimize` when enabled and not minifying | `false` |
| `Map` | No | `Auto`, `true`, `false`, or an external source-map path | `Auto` |
| `Silent` | No | Adds `--silent` | `false` |
| `Cwd` | No | Tailwind scan root, resolved against `ProjectDirectory` | project directory |
| `AdditionalArguments` | No | Extra Tailwind arguments, subject to the restrictions above | empty |
| `StampDirectory` | No | Directory for the settings stamp and generated-file manifest | `obj/Scarlet.Tailwind` |
| `RuntimeDirectory` | No | Explicit runtime directory. Overrides `RuntimePacks`; required when `TailwindRuntimeDownload` is true | null |
| `RuntimePacks` | No | Runtimes available to the build, normally `@(TailwindRuntimePack)`. See [How the Runtime Is Discovered](#how-the-runtime-is-discovered) | empty |
| `TailwindRuntimeDownload` | No | Download Tailwind instead of using runtime packs | `false` |
| `TailwindVersionDownload` | No | Version to download. Empty resolves the latest GitHub release | empty |
| `DownloadMutexTimeoutSeconds` | No | Seconds to wait when another process holds the download mutex | `300` |
| `TimeoutMilliseconds` | No | Maximum time per Tailwind invocation before it is killed. `0` waits indefinitely | `0` |

### Output Parameters

| Parameter | Description |
| --- | --- |
| `GeneratedFiles` | Every generated CSS and external source-map file. Each carries `RelativePath` metadata used to add it to `@(Content)` and `@(FileWrites)` |
| `RemovedFiles` | Previously generated files that are no longer produced, removed from `@(Content)` and `@(None)` so stale assets are not served |

`RemovedFiles` is what stops a stale `app.css.map` being served after you turn maps off: the targets drop it
from `@(Content)` and the task deletes it.

## Incrementality

Tailwind is fast and knows its own inputs — including every file it scans, which MSBuild cannot enumerate —
so the target has no `Inputs`/`Outputs` and runs on every build. Trying to out-guess the scanner is how a
wrapper ends up serving stale CSS.

What the task does track is a **settings stamp**. Change a property, and the previous outputs are deleted
before the rebuild rather than being overwritten in place.

## Cleaning

`dotnet clean` removes the generated CSS, any external source map, the manifest and the settings stamp. The
task writes a manifest of what it generated, and `TailwindClean` reads it — so cleaning removes exactly what
was produced, including outputs whose names came from properties.

## dotnet watch Integration

Add a `Watch` item so `dotnet watch` re-runs the build when your sources change:

```xml
<ItemGroup>
  <Watch Include="Styles\**\*.css" />
  <Watch Include="**\*.razor" />
  <Watch Include="**\*.cshtml" />
</ItemGroup>
```

Watch the files Tailwind *scans*, not just the stylesheet. Adding a class to a `.razor` file has to trigger a
rebuild, or the new class never reaches your CSS.

> Tailwind watch flags are rejected by the MSBuild task because watch mode does not exit. If you want a
> true watcher, run
> [`dotnet tailwind --watch`](https://www.nuget.org/packages/Scarlet.Tailwind.Cli/) as a separate process, or
> let `dotnet watch` drive the build as above.

## Supported Platforms

| Package | Host |
|---|---|
| `Scarlet.Tailwind.Runtime.windows-x64` | Windows x64, and Windows ARM64 under emulation |
| `Scarlet.Tailwind.Runtime.linux-x64` | Linux x64 (glibc) |
| `Scarlet.Tailwind.Runtime.linux-arm64` | Linux ARM64 (glibc) |
| `Scarlet.Tailwind.Runtime.linux-x64-musl` | Linux x64 (musl, e.g. Alpine) |
| `Scarlet.Tailwind.Runtime.linux-arm64-musl` | Linux ARM64 (musl, e.g. Alpine) |
| `Scarlet.Tailwind.Runtime.darwin-x64` | macOS x64 |
| `Scarlet.Tailwind.Runtime.darwin-arm64` | macOS ARM64 |

Tailwind publishes no native ARM64 build for Windows. The `windows-x64` package serves `win-arm64` as well,
with the x64 binary that Windows on ARM runs under emulation. If a native build appears later, a package
providing it wins automatically — no change here, and nothing to migrate.

Requires the .NET SDK. The task targets `netstandard2.0`, so it loads in both `dotnet build` and Visual
Studio's MSBuild.

## Links

- [Source and full documentation](https://github.com/ScarletKuro/Scarlet.Tailwind)
- [`Scarlet.Tailwind.Cli`](https://www.nuget.org/packages/Scarlet.Tailwind.Cli/) — the same Tailwind as a
  `dotnet tailwind` command-line tool
- [Tailwind CSS documentation](https://tailwindcss.com/docs)

## License

Scarlet.Tailwind is MIT licensed. Tailwind CSS is distributed under its own MIT license; see
`LICENSE-3RD-PARTY.txt` in the package.
