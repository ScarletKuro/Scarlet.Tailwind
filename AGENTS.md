# AGENTS.md

Guidance for anyone — human or AI agent — changing this repository.

## What this is

An MSBuild task package and a .NET tool that run the Tailwind CSS standalone CLI during .NET builds. Three
shipping surfaces: `Scarlet.Tailwind.MSBuild`, `Scarlet.Tailwind.Cli`, and seven
`Scarlet.Tailwind.Runtime.*` packages.

`SCARLET_TAILWIND_SPEC.md` records the design decisions and, more usefully, the ones that were rejected and
why.

## Naming hygiene — read this before any bulk rename

This repository began as an architectural adaptation, so bulk renames deserve special care:

- Tailwind's **asset vocabulary is not the package vocabulary.** Upstream publishes `tailwindcss-macos-x64`;
  the package is `Scarlet.Tailwind.Runtime.darwin-x64`; the RID is `osx-x64`. All three are correct in their
  own place, and a rename that unifies them breaks the download URL. `PlatformInfo` keeps them separate on
  purpose.
- The executable is `tailwindcss`, not `tailwind`. Using the package name as the executable name still
  compiles but fails at run time.
- Tailwind's release tags are `v4.3.3`. The version-bump workflow parses that prefix, so an incorrect regex
  remains invisible until a release day.

`PlatformTests` pins every one of these values literally, which is the point of it.

## Understanding Tailwind v4

**Tailwind v4 is not v3 with a new version number, and most of what is on the internet is about v3.**

The facts this package depends on, all verified against 4.3.3:

- **The stylesheet is the configuration.** No `tailwind.config.js` auto-detection, no `content` array, no
  `--config` flag, no `--content` flag. `@import "tailwindcss"`, `@theme`, `@source` and `@config` are the
  whole surface.
- **Unrecognised flags are silently discarded.** `tailwindcss --version` does not error — it ignores the
  flag, reads stdin and prints a stylesheet. There is no `--version`; the banner in `--help` is where the
  version lives.
- **Repeated flags collapse to the last one.** `--input a.css --input b.css` compiles `b.css`, silently. One
  invocation is one input and one output, permanently, which is why the task runs one process per entry
  point and why `TailwindAdditionalArguments` can override what the task rendered.
- **`--map` has two behaviours.** Bare `--map` inlines; `--map <path>` writes a file. That changes how many
  files the build produces, so it changes the manifest, static web assets and clean.
- **`--cwd` also resolves `--input`, `--output` and `--map`.** The task therefore passes all three as
  absolute paths, so `--cwd` only affects scanning.
- **The scanner's built-in ignore list has no `bin` or `obj`.** Every entry is from the JavaScript or Python
  ecosystem. In a .NET project `.gitignore` usually covers it; when it does not, `obj` feeds the scanner
  last build's generated Razor sources and deleted classes never leave the CSS.

Check any new claim against the binary rather than the docs. `src/Scarlet.Tailwind.Runtime.*/tailwindcss` is
right there after a build.

## Project structure

```text
build/TailwindRuntime.targets      Shared copy/pack logic for the runtime packages
src/
  Scarlet.Tailwind.Core/           netstandard2.0, IsPackable=false, shared by the task and the CLI
    Platform.cs, PlatformInfo.cs   The eight host platforms and their four names each
    TailwindRuntimeResolver.cs     Host detection, pack selection, path resolution
    TailwindRuntimePack.cs         The @(TailwindRuntimePack) item contract
    TailwindDownloader.cs          Download, verify, stage, publish, mark
    TailwindCommandLine.cs         Argument quoting (netstandard2.0 has no ArgumentList)
  Scarlet.Tailwind.MSBuild/        TailwindCompileTask plus three copies of the targets
  Scarlet.Tailwind.Cli/            dotnet-tailwind, ten packages from one pack
  Scarlet.Tailwind.Runtime.*/      Seven asset-only packages
tools/download-tailwind.{sh,ps1}   Stage a runtime binary during the runtime packages' build
```

## Build and test

```bash
dotnet build                      # first run downloads ~700 MB of Tailwind binaries
dotnet test                       # all three test projects
dotnet pack --configuration Release --output ./packages
```

Individual suites:

```bash
dotnet test tests/Scarlet.Tailwind.MSBuild.Tests                # fast, no network, no Tailwind
dotnet test tests/Scarlet.Tailwind.MSBuild.IntegrationTests     # real builds and a real Tailwind; slow
dotnet test tests/Scarlet.Tailwind.Cli.Tests                    # fast
```

**Run the suite in Release as well as Debug before claiming it passes.** Several properties resolve from
`$(Configuration)`, so a Debug-only run cannot see a Configuration-driven bug. This has caused real
release-only regressions.

The e2e scenarios need packed packages and are documented in [`tests/e2e/README.md`](tests/e2e/README.md).

## The runtime discovery contract

**This is the extension point. Read it before adding a platform.**

Tailwind runs on the build host, not on the project's target RID, so NuGet's RID-graph resolution does not
apply. Each runtime package's `build/*.props` contributes one `@(TailwindRuntimePack)` item, and
`TailwindRuntimeResolver.SelectPacks` picks the best match.

| Metadata | Meaning |
|---|---|
| `Rid` | The runtime identifier the pack serves. Required. |
| `RuntimesPath` | Directory containing `<rid>/native/<executable>`. Required. |
| `NativeRid` | The subdirectory the executable is stored under. Defaults to `Rid`. |
| `Variant` | Diagnostic only. |
| `Priority` | Higher wins. Ties break by pack id, so the result never depends on import order. |

Consequences worth knowing:

- **Adding a runtime identifier needs no change to `Scarlet.Tailwind.MSBuild`.** A package that emits the
  item is enough.
- **`NativeRid` exists for exactly one case**: the `windows-x64` package emits a second item serving
  `win-arm64` out of the same `win-x64` binary, at `Priority -100`. Without `NativeRid` the resolver would
  look under `win-arm64/native/`, which that package does not ship. When a native Windows ARM64 build
  appears, a package for it at the default priority wins with no code change.

## Things that will bite you

**The task's dependencies must be hand-packed.** MSBuild resolves a task's dependencies from the folder the
task assembly lives in, so every assembly `Scarlet.Tailwind.MSBuild` references must also appear as a
`<None ... PackagePath="tools/netstandard2.0/" />` item. Miss one and the task fails to load in every
consumer build — while the in-process integration tests stay green, because they resolve through project
references. `TaskPackagingTests` catches it in milliseconds; the `package-installation` e2e catches it for
real.

**XML comments cannot contain `--`.** Writing `dotnet pack --no-build` in a comment inside a `.targets` file
makes it invalid XML. This was caught here only because the targets-comparison test parses all three copies;
the development copy happened not to have the comment, so the samples built fine while the two packed copies
were broken.

**There are three copies of the targets** — `build/`, `buildMultiTargeting/`, and the development copy at the
project root — and they are hand-maintained. `TailwindTargetsTests` compares all three and normalises the one
allowed difference (the development copy's extra `ResolveProjectReferences` dependency).

**An unwired task parameter is invisible.** It falls back to its C# default, which is usually what the tests
happen to exercise, so everything passes and the property silently does nothing for consumers.
`Targets_ShouldWireEveryCompileTaskInput` derives the expected list by reflection for that reason. If you add
a task parameter, the test tells you to wire it; do not add it to an allow-list.

**`--watch` must never reach the task.** Tailwind's watch mode does not exit, so with the default
`TailwindTimeoutMilliseconds` of `0` the build waits forever. It is documented as forbidden in
`TailwindAdditionalArguments`. Note that it exits immediately without a TTY, which makes it useless for
testing timeouts — use a tiny `TailwindTimeoutMilliseconds` instead.

**The CLI version is `$(TailwindVersion).$(TailwindCliRevision)`.** NuGet drops a trailing zero, so revision
0 publishes as plain `4.3.3`. Bump the revision for a CLI-only fix and reset it when `TailwindVersion` moves;
a re-release at an unchanged version is silently dropped by the deploy push, which skips duplicates.

**CLI push order is load-bearing.** Every RID package must reach the feed before the pointer package. A
`*.nupkg` glob gets this backwards, because `.` sorts before any letter, which is why `deploy.yml` pushes the
pointer explicitly last.

**Do not set `PublishTrimmed`/`PublishSingleFile`/`PublishAot` on the CLI.** Each implies `SelfContained`,
adding ~70 MB of .NET runtime on top of an 80-112 MB Tailwind in every RID package, for no benefit — a dotnet
tool already needs a .NET install.

**The embedded binary must be chmod'd at run time.** NuGet packages carry no Unix permission bits, so it is
extracted `0644` and fails with `EACCES` on first use on Linux and macOS.

## Common failure modes

| Symptom | Cause |
|---|---|
| `Could not load file or assembly 'Scarlet.Tailwind.Core'` in a consumer build | A dependency missing from the `tools/netstandard2.0/` pack list |
| `An XML comment cannot contain '--'` | A flag written literally in a `.targets` comment |
| Tailwind reports a different version than `$(TailwindVersion)` | A stale staged binary. Delete the `<exe>.version` markers and rebuild; `TailwindBinaryVersionTests` guards it |
| CSS contains classes that were deleted | The scanner is reading `obj`. See *Writing your entry stylesheet* in the MSBuild README |
| A build hangs indefinitely | `--watch` reached Tailwind through `TailwindAdditionalArguments` |
| e2e script fails on Linux with a bad interpreter | CRLF line endings. `.gitattributes` forces `*.sh` to LF |
| A `grep` against build output never matches | The build ran at `minimal` verbosity, which drops `High`-importance messages. Pass `--verbosity normal` |

## Before you claim it works

- [ ] `dotnet build` succeeds with **zero warnings**
- [ ] `dotnet test` passes in **both Debug and Release**
- [ ] `dotnet pack` succeeds and `tools/netstandard2.0/` contains the full dependency closure
- [ ] Both samples build and produce `wwwroot/css/app.css`, and `dotnet clean` removes it
- [ ] `bash -n` passes on every e2e script
- [ ] `git status` shows no unexpected files — the runtime binaries are gitignored and must stay that way
