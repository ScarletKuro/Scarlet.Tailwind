# Scarlet.Tailwind Specification

Status: draft  
Audience: package maintainer, future contributors, AI agents  
Scope: MSBuild and CLI integration for the Tailwind CSS standalone CLI in .NET projects

## Summary

`Scarlet.Tailwind` should provide pinned Tailwind CSS execution for .NET builds and repository-local
command line use, with the same build correctness priorities as `Scarlet.Bun` and `Scarlet.Sass`:
multi-targeting, Razor Class Library support, static web assets, deterministic runtime resolution, strong
tests, and clear diagnostics.

The package should not start as a general CSS pipeline. It should not register ASP.NET Core services, wrap
PostCSS, or require Node.js. The primary API should be MSBuild properties and items, plus a thin
`dotnet tailwind` CLI that forwards Tailwind arguments verbatim.

Tailwind sits between its two siblings, and the design should say so plainly rather than pick one to copy.
Tailwind ships a **single executable**, like Bun, so runtime acquisition follows Bun. But the task knows
exactly which files it wrote — one input stylesheet produces one output stylesheet — so the build
integration follows Sass.

## Goals

- Compile a Tailwind entry stylesheet during `dotnet build`.
- Work in ASP.NET Core apps, Blazor apps, and Razor Class Libraries.
- Work correctly in single-TFM and multi-TFM projects.
- Run Tailwind before static web asset discovery so generated `wwwroot` files are discovered by the SDK.
- Package generated RCL assets under `staticwebassets/` when the project is packed.
- Support `dotnet pack --no-build` after a previous build without re-running Tailwind.
- Support `dotnet clean` for generated CSS, source maps, manifests, and stamp files.
- Provide pinned Tailwind binaries through platform-specific NuGet runtime packages.
- Provide optional download-on-demand mode for environments that prefer not to reference runtime packages.
- Verify downloaded binaries against the checksums Tailwind publishes.
- Provide a repository-pinned .NET tool for command-line use.
- Keep configuration where Tailwind itself keeps it, which in v4 means inside the CSS.
- Make all important behavior testable with unit, integration, and e2e tests.

## Non-Goals

- Do not require or invoke Node.js, npm, or PostCSS.
- Do not provide an ASP.NET Core middleware or runtime compiler.
- Do not invent a `tailwind.json`, `appsettings.json`, or any Scarlet-specific configuration convention.
  Tailwind v4 configures itself from the CSS entry point; Scarlet should not add a second place to look.
- Do not expose a `Content` or source-globbing property. See *Working Directory and Source Detection*.
- Do not support Tailwind v3 in v1. See *Upstream Facts*.
- Do not attempt MSBuild-level incremental skipping in v1. See *Incremental Build Strategy*.

## Upstream Facts

Neither sibling needed a section like this, because Dart Sass and Bun distribute in ways that matched
expectations. Tailwind does not, and three of the findings below change the design. Each was confirmed
against the GitHub release API and the Tailwind documentation, not assumed, and each should be re-confirmed
when the pinned version moves.

**Release assets are raw executables, not archives.** Release `v4.3.3` publishes exactly eight files:

```text
sha256sums.txt
tailwindcss-linux-arm64
tailwindcss-linux-arm64-musl
tailwindcss-linux-x64
tailwindcss-linux-x64-musl
tailwindcss-macos-arm64
tailwindcss-macos-x64
tailwindcss-windows-x64.exe
```

There is nothing to unpack. `Scarlet.Tailwind.Core` therefore needs no `IZipArchiveProvider`, no
`ITarArchiveProvider`, no `SharpZipLib` dependency, and none of the archive-entry path containment logic
that `Scarlet.Sass` requires. This is a genuine simplification and should not be re-introduced by copying a
sibling's downloader wholesale.

**There is no Windows ARM64 build.** The asset set above has been identical across `v4.2.3` through
`v4.3.3`. This is not an oversight in one release. See *Runtime Discovery Contract* for the consequence.

**The asset set is not stable across majors.** Tailwind `v3.4.17` shipped `tailwindcss-windows-arm64.exe`
and `tailwindcss-linux-armv7`, and shipped no musl builds at all. v4 dropped both and added
`tailwindcss-linux-x64-musl` and `tailwindcss-linux-arm64-musl`. A platform map encodes asset names, so a
map built for v4 cannot serve v3. v1 targets v4 only, and `TailwindVersionDownload` pointing at a v3
version should fail with a message naming the version and the missing asset rather than surfacing a raw
404.

**Checksums are published, with a format quirk.** `sha256sums.txt` contains one line per binary:

```text
55fd0b241214eff3de1e8ee4f22796662f2d2e7a49bcfca7477cfd0bac398195  ./tailwindcss-linux-arm64
```

Sixty-four hex characters, **two** spaces, and a `./` prefix on the filename. `Scarlet.Bun` parses its own
`SHASUMS256.txt` by splitting on whitespace and comparing the last field to the asset name ordinally; that
comparison fails against `./tailwindcss-linux-arm64`. The parser must strip a leading `./` before
comparing. This is the kind of detail that produces a confusing "no checksum entry for …" error months
later, so it deserves a test of its own.

**GitHub's release redirect has the same two hops Bun documents.** Requesting
`releases/latest/download/sha256sums.txt` returns a 302 to
`releases/download/v4.3.3/sha256sums.txt` — which carries the version — and that in turn redirects to a
signed `release-assets.githubusercontent.com` blob URL, which does not. A client that follows redirects
automatically only ever observes the second hop. Bun's rule therefore transfers verbatim: **build both the
binary URL and the checksums URL from a known version string, never from a followed response URI**, and
resolve "latest" with a dedicated non-redirecting request that reads the first `Location` header.

**The CLI surface is small.** From `packages/@tailwindcss-cli/src/commands/build/index.ts`:

| Flag | Short | Type | Default |
| --- | --- | --- | --- |
| `--input` | `-i` | string | — |
| `--output` | `-o` | string | `-` (stdout) |
| `--watch` | `-w` | boolean or string | — |
| `--poll` | — | boolean or number | `false` |
| `--minify` | `-m` | boolean | — |
| `--optimize` | — | boolean | — |
| `--cwd` | — | string | `.` |
| `--map` | — | boolean or string | `false` |
| `--silent` | — | boolean | — |

That is the entire set the task needs to render.

**Configuration lives in the CSS, not on the command line.** Tailwind v4 has no `--content` flag and no
`--config` flag. Source detection is automatic, narrowed by `@source` directives; theme values come from
`@theme { … }`; and a legacy `tailwind.config.js` is **no longer auto-detected** — it must be loaded
explicitly with `@config "../../tailwind.config.js"` from inside the entry stylesheet, with `corePlugins`,
`safelist` and `separator` unsupported. The practical effect is that Scarlet.Tailwind needs no
configuration surface at all. `Scarlet.Sass` had to argue itself out of inventing one; here there is
nothing to invent.

## Package Layout

| Package | Purpose | Versioning |
| --- | --- | --- |
| `Scarlet.Tailwind.MSBuild` | MSBuild task, props, and targets | Scarlet-controlled package version |
| `Scarlet.Tailwind.Cli` | `dotnet tailwind` tool pointer package | Tailwind version, optionally with Scarlet revision |
| `Scarlet.Tailwind.Runtime.windows-x64` | Tailwind for Windows x64 | Tailwind version |
| `Scarlet.Tailwind.Runtime.linux-x64` | Tailwind for Linux x64 glibc | Tailwind version |
| `Scarlet.Tailwind.Runtime.linux-arm64` | Tailwind for Linux ARM64 glibc | Tailwind version |
| `Scarlet.Tailwind.Runtime.linux-x64-musl` | Tailwind for Linux x64 musl | Tailwind version |
| `Scarlet.Tailwind.Runtime.linux-arm64-musl` | Tailwind for Linux ARM64 musl | Tailwind version |
| `Scarlet.Tailwind.Runtime.darwin-x64` | Tailwind for macOS x64 | Tailwind version |
| `Scarlet.Tailwind.Runtime.darwin-arm64` | Tailwind for macOS ARM64 | Tailwind version |

Seven runtime packages, not eight. Package names keep the siblings' vocabulary (`windows`, `darwin`) even
though Tailwind's own assets say `macos`; the two vocabularies stay distinct in the platform map exactly as
they do in `Scarlet.Bun`.

`Scarlet.Tailwind.Core` should be `netstandard2.0` with `IsPackable=false`, holding platform detection,
runtime resolution, the downloader, chmod, and process-start retry. Both shipping packages should carry the
assembly rather than depend on it: `Scarlet.Tailwind.MSBuild` packs it into `tools/netstandard2.0/`, and
the CLI gets it through publish output.

**The packing rule is the one that is expensive to discover.** MSBuild resolves a task's dependencies from
the folder the task assembly lives in, so every assembly `Scarlet.Tailwind.MSBuild` references must also
appear as a `<None … PackagePath="tools/netstandard2.0/" />` item. Miss one and the task fails to load in
every consumer build, while in-process integration tests stay green because they resolve through project
references. A `TaskPackagingTests` fixture should assert the closure, and the `package-installation` e2e
should catch it for real. Note that Tailwind's lack of archive handling makes this closure smaller than
Sass's — there is no SharpZipLib to forget.

On-disk runtime layout, matching Bun's single-file shape:

```text
runtimes/
  <rid>/                       # win-x64, linux-x64, linux-arm64, linux-musl-x64,
    native/                    # linux-musl-arm64, osx-x64, osx-arm64
      tailwindcss | tailwindcss.exe
      tailwindcss.version      # marker written after a successful publish
```

## Runtime Discovery Contract

Tailwind runs on the machine performing the build, not on the project's target RID, so NuGet's RID-graph
resolution is the wrong mechanism — it resolves against `$(RuntimeIdentifier)`. Each runtime package's
`build/*.props` should contribute one item:

```xml
<TailwindRuntimePack Include="Scarlet.Tailwind.Runtime.linux-x64">
  <Rid>linux-x64</Rid>
  <RuntimesPath>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)..', 'runtimes'))</RuntimesPath>
  <Variant>default</Variant>
  <Priority>0</Priority>
</TailwindRuntimePack>
```

`Rid` and `RuntimesPath` are required; an item missing either should be skipped with a warning naming the
pack. `Variant` is diagnostic only. `Priority` defaults to `0`, higher wins, and ties break by pack id then
path so the outcome never depends on the order NuGet happened to import packages in. Items should be
de-duplicated on `Rid` plus normalised `RuntimesPath`, because a package's props can be imported from more
than one build folder.

Precedence: an explicit `TailwindRuntimeDirectory` wins over every pack and is never second-guessed;
`TailwindRuntimeDownload=true` bypasses pack resolution entirely; otherwise the first candidate pack whose
executable exists on disk is selected.

**There is no legacy property contract.** Nothing predates this package, so runtime discovery starts and
stays with the item. New runtime identifiers need a package that emits the item and no change to
`Scarlet.Tailwind.MSBuild` at all.

**Windows ARM64 resolves to the Windows x64 pack.** Tailwind publishes no ARM64 binary for Windows, and
Windows 11 on ARM runs x64 executables under emulation. A win-arm64 host should therefore select the
`windows-x64` pack and log, at normal importance, that it did so and why. Two consequences to document: the
emulated binary is slower than a native one would be, and if Tailwind later ships `windows-arm64`, adding
the package is enough — the fallback becomes dead weight rather than a breaking change. Teams that prefer
strictness can point `TailwindRuntimeDirectory` at their own binary.

When no pack matches the host, the error should include a copy-pasteable `<PackageReference>` for the right
package, mention `TailwindRuntimeDownload` and `TailwindRuntimeDirectory` as alternatives, and list the
packs that were visible. When packs matched but no executable was found, the error should list every path
searched. These are two genuinely different failures and should not share a message.

## MSBuild API

### Minimal usage

```xml
<ItemGroup>
  <TailwindBeforeStaticWebAssets Include="Styles/app.css">
    <OutputPath>wwwroot/css/app.css</OutputPath>
  </TailwindBeforeStaticWebAssets>
</ItemGroup>
```

Where `Styles/app.css` is an ordinary Tailwind entry point:

```css
@import "tailwindcss";

@theme {
  --color-brand-500: oklch(0.72 0.11 178);
}
```

The item name is deliberately specific. Most users want Tailwind output to become Blazor or Razor static
web assets, and naming the item after that intent keeps the common case obvious.

### Multiple entry points

```xml
<ItemGroup>
  <TailwindBeforeStaticWebAssets Include="Styles/app.css">
    <OutputPath>wwwroot/css/app.css</OutputPath>
  </TailwindBeforeStaticWebAssets>
  <TailwindBeforeStaticWebAssets Include="Styles/admin.css">
    <OutputPath>wwwroot/css/admin.css</OutputPath>
    <Minify>true</Minify>
  </TailwindBeforeStaticWebAssets>
</ItemGroup>
```

Each item is one input stylesheet and one output stylesheet. There is no directory mode: `Scarlet.Sass` has
one because Sass compiles many entry points out of a folder, whereas a Tailwind project has a small number
of deliberately chosen roots. Inferring which `.css` files under a directory are Tailwind entry points
would mean guessing, and guessing wrong means compiling a plain stylesheet through Tailwind.

There should be no `$(TailwindInput)`/`$(TailwindOutput)` property pair. It is tempting because most
projects have exactly one entry point, but it would be a second code path, would raise a "which one wins"
question when both are set, and would diverge from the item contract both siblings already share.

## Properties

| Property | Default | Meaning |
| --- | --- | --- |
| `TailwindEnabled` | `true` | Enables `RunTailwindBeforeStaticWebAssets` |
| `TailwindMinify` | `Auto` | `Auto`, `true`, or `false`. Renders `--minify` |
| `TailwindOptimize` | `false` | Renders `--optimize`. Ignored when minifying |
| `TailwindMap` | `Auto` | `Auto`, `true`, or `false`. Renders `--map` |
| `TailwindCwd` | empty | Working directory for source detection. Empty means `$(MSBuildProjectDirectory)` |
| `TailwindAdditionalArguments` | empty | Raw arguments appended verbatim |
| `TailwindRuntimeDirectory` | empty | Explicit runtime directory containing `<rid>/native/<executable>` |
| `TailwindRuntimeDownload` | `false` | Download the runtime instead of using runtime packs |
| `TailwindVersionDownload` | empty | Version to download. Empty means the pinned version |
| `TailwindDownloadMutexTimeoutSeconds` | `300` | Timeout for cross-process download coordination |
| `TailwindStampDirectory` | empty | Settings stamp and manifest directory. Empty means `$(IntermediateOutputPath)\Scarlet.Tailwind` |
| `TailwindTimeoutMilliseconds` | `0` | Maximum time per invocation. `0` waits indefinitely |

`Auto` resolves from `$(Configuration)`:

| Property | Debug default | Release default |
| --- | --- | --- |
| `TailwindMinify` | `false` | `true` |
| `TailwindMap` | `true` | `false` |

`--minify` already implies optimisation, so `TailwindOptimize` exists for the narrower case of wanting
optimised but readable output. When both are true the task should render `--minify` only, and should say so
in the property documentation rather than passing both and relying on Tailwind's precedence.

## Item Metadata

`TailwindBeforeStaticWebAssets` supports the same compile settings as the global properties, applied per
entry point:

```xml
<TailwindBeforeStaticWebAssets Include="Styles/app.css">
  <OutputPath>wwwroot/css/app.css</OutputPath>
  <Minify>true</Minify>
  <Optimize>false</Optimize>
  <Map>false</Map>
  <Cwd>..</Cwd>
  <AdditionalArguments>--silent</AdditionalArguments>
</TailwindBeforeStaticWebAssets>
```

`OutputPath` is required; an item without it should fail with a message naming the item. `Minify`,
`Optimize`, `Map` and `Cwd` override the corresponding property. `AdditionalArguments` is concatenated,
global first then item, matching how `Scarlet.Sass` treats its list-valued settings.

## Working Directory and Source Detection

This is the most consequential defaulting decision in the design and the one most likely to be got wrong.

Tailwind v4 scans for class names automatically, relative to its working directory, ignoring `.gitignore`d
paths, `node_modules`, binary files, CSS files and lockfiles. There is no `--content` flag to pin that
down. If the task simply inherits whatever working directory MSBuild happens to have, then which files get
scanned depends on where the build was invoked from — `dotnet build` in the project folder and
`dotnet build src/App` would produce different CSS from identical sources.

The task should therefore always pass `--cwd`, defaulting to `$(MSBuildProjectDirectory)`. That makes
scanning depend on the project, which is the only stable answer, and it matches what a developer running
`npx @tailwindcss/cli` inside their project would get.

Projects that need to scan outside the project directory should use Tailwind's own mechanisms rather than
an MSBuild property:

```css
@import "tailwindcss";
@source "../SharedComponents";
```

And a monorepo whose build runs from the repository root can set the base explicitly:

```css
@import "tailwindcss" source("../src");
```

Legacy JavaScript configuration is loaded the same way, from the CSS:

```css
@import "tailwindcss";
@config "../../tailwind.config.js";
```

Keeping all three in the stylesheet means one place to look, and it means a `dotnet build` and a bare
`npx @tailwindcss/cli -i Styles/app.css -o out.css` produce the same result.

## Static Web Assets

The target should run **before** static web asset discovery rather than contributing `@(StaticWebAsset)`
items itself. The SDK's `ResolveCoreStaticWebAssets` handles assets that exist on disk and are part of the
project; `ResolveStaticWebAssetsInputs` handles build-generated ones and expects hand-authored items
carrying `SourceId`, `SourceType`, `ContentRoot`, `BasePath`, `RelativePath`, `AssetKind`, `AssetMode`,
`AssetRole`, `Fingerprint` and `Integrity`. Writing the files before discovery means they qualify for the
first stage and the SDK derives all of that itself, fingerprinting and endpoints included.

Recovery of the generated files into `@(Content)` should use a project-relative glob:

```xml
<ItemGroup>
  <Content Remove="@(_TailwindRemovedFiles)" />
  <None Remove="@(_TailwindRemovedFiles)" />
  <_TailwindGeneratedContent Include="%(_TailwindGeneratedFiles.RelativePath)" />
  <_TailwindNewContent Include="@(_TailwindGeneratedContent)" Exclude="@(Content)" />
  <Content Include="@(_TailwindNewContent)" CopyToPublishDirectory="PreserveNewest" />
  <FileWrites Include="@(_TailwindGeneratedFiles)" />
</ItemGroup>
```

The path must stay project-relative: the SDK matches `"wwwroot/**"`, and an absolute path silently falls
through to plain content instead of becoming a static web asset. `Exclude="@(Content)"` avoids handing
other consumers of `@(Content)` the same file twice on incremental builds. Only the project's own `wwwroot`
can be recovered this way, because the SDK hardcodes `ContentRoot="$(MSBuildProjectDirectory)\wwwroot\"`.

The task should compute a `RelativePath` metadata value itself rather than leaving it to the targets, since
`$([System.IO.Path]::GetRelativePath(…))` does not exist in full-framework MSBuild.

## Incremental Build Strategy

**Tailwind runs on every build. The target declares no `Inputs` or `Outputs`.**

Dart Sass has `--update`, which lets `Scarlet.Sass` invoke it unconditionally and let the compiler decide
whether to do work. Tailwind has no equivalent: every invocation is a full rebuild. That argues for
MSBuild-level skipping — and the argument fails, because Tailwind's inputs are *every file it scans* for
class names, discovered automatically. Declaring `Inputs` accurately is not possible without reimplementing
Tailwind's detection, and under-declaring produces the worst outcome available: a build that silently ships
CSS missing the classes someone just added.

Always running is therefore the correct default, and the cost should be stated honestly in the README: a
Tailwind invocation per build, typically a few hundred milliseconds.

A settings stamp and a generated-files manifest should still be written, for two jobs that are not
skipping:

- `<stampDirectory>/Tailwind.settings.stamp` records configuration, resolved settings per entry, runtime
  selection and pack list. When it changes, expected outputs are deleted before the run so nothing stale
  survives a settings change.
- `<stampDirectory>/Tailwind.generated.txt` records every file the task produced, one absolute path per
  line. It drives stale-output removal when an entry point is renamed or removed, and it is what `Clean`
  reads.

Both should be written only after a zero exit code, so a failed build leaves no success stamp.

If a team later wants opt-in skipping, `Scarlet.Bun`'s `Inputs`/`Outputs`/`StampFile` scheme is the model —
it is opt-in precisely because Bun also cannot know its own inputs. That is a deliberate v2 question, not a
v1 omission.

## Cleaning

```xml
<Target Name="TailwindClean" BeforeTargets="CoreClean" DependsOnTargets="_TailwindResolveStampDirectory">
  <ReadLinesFromFile File="$(_TailwindStampDirectory)/Tailwind.generated.txt"
                     Condition="Exists('$(_TailwindStampDirectory)/Tailwind.generated.txt')">
    <Output TaskParameter="Lines" ItemName="_TailwindFilesToClean" />
  </ReadLinesFromFile>
  <Delete Files="@(_TailwindFilesToClean)" Condition="'@(_TailwindFilesToClean)' != ''" />
  <Delete Files="$(_TailwindStampDirectory)/Tailwind.generated.txt;$(_TailwindStampDirectory)/Tailwind.settings.stamp" />
</Target>
```

Manifest-driven, never a blanket `wwwroot` delete — the output directory usually contains hand-authored
files too.

## Download and Verification

`TailwindDownloader` should follow `Scarlet.Bun`'s design, minus all archive handling.

**Publication is atomic.** The binary is downloaded to a hidden staged sibling in the destination
directory (`.tailwindcss.<guid>.tmp`), made executable there, and only then moved into place with
`File.Move` — a same-volume rename. A concurrent or interrupted build therefore never observes a
half-written or non-executable `tailwindcss`. Any pre-existing file at the destination is deleted first,
and that delete must **not** swallow failures: keeping a stale binary while writing a fresh version marker
would make the marker lie about what is on disk. A lost race, seen as `IOException` with the destination
now existing, should be treated as success — another caller published first.

**Hashing happens during the download pass** via `CryptoStream`, not by reading the ~110 MB binary back off
disk afterwards. Verification happens before the staged file is published.

**Both URLs are built from the version string:**

```text
pinned:  https://github.com/tailwindlabs/tailwindcss/releases/download/v{version}/{asset}
         https://github.com/tailwindlabs/tailwindcss/releases/download/v{version}/sha256sums.txt
latest:  https://github.com/tailwindlabs/tailwindcss/releases/latest/download/{asset}
         https://github.com/tailwindlabs/tailwindcss/releases/latest/download/sha256sums.txt
```

Never from a followed response URI, for the redirect reason given in *Upstream Facts*. Resolving "latest"
to a concrete version should use a dedicated request with `AllowAutoRedirect = false` that reads the tag out
of the first `Location` header.

**Checksum parsing must strip the `./` prefix.** Split each line on whitespace, take the last field, trim a
leading `./`, and compare ordinally against the asset name. A missing entry and a mismatch are different
errors and should read differently.

**Cross-process coordination** uses a named global mutex keyed on the SHA-256 of the uppercased full
executable path, so the scope is the exact cache entry. `AbandonedMutexException` counts as acquisition — a
build killed mid-download must not poison every later build on that machine. After acquiring, re-check the
cache, because another process may have finished while this one waited. `DownloadRuntimeAsync` should be
`Task.Run` over the synchronous method rather than a genuine `async` method, because `ReleaseMutex` is
thread-affine and an `await` continuation resuming on a different pool thread throws instead of releasing.

**A version marker** (`<executable>.version`) is written after a successful publish and is what makes a
cache entry trustworthy. Every reader of the download cache must require it, including readers that never
take the mutex — a binary with no marker beside it is an interrupted download, not a usable runtime.

## CLI API

`Scarlet.Tailwind.Cli` should pack as a .NET tool with `ToolCommandName=dotnet-tailwind`, invoked as
`dotnet tailwind`. With `RuntimeIdentifiers` covering the seven supported RIDs plus `any`, one `dotnet pack`
produces nine packages: seven RID-specific ones with the binary embedded, a portable `any` package that
downloads on first use, and the top-level pointer package.

Arguments are forwarded verbatim using `ArgumentList`, never a concatenated string. Standard streams are
inherited rather than redirected, so `isatty` holds and colours and piping behave exactly as a direct
invocation. Tailwind's exit code is returned unchanged.

`--scarlet-info` is the single reserved token, honoured only as the first argument, with
`SCARLET_TAILWIND_PASSTHROUGH=1` as a permanent opt-out — so `dotnet tailwind --watch --scarlet-info` still
reaches Tailwind. It must resolve with downloads disabled: asking the tool what it would do must never
itself fetch 110 MB.

| Variable | Meaning |
| --- | --- |
| `SCARLET_TAILWIND_PATH` | Explicit executable path; honoured or fails, never silently falls back |
| `SCARLET_TAILWIND_VERSION` | Version to resolve; `latest` is a recognised token |
| `SCARLET_TAILWIND_CACHE_DIR` | Cache root override |
| `SCARLET_TAILWIND_NO_EMBEDDED` | Ignore the embedded runtime |
| `SCARLET_TAILWIND_PASSTHROUGH` | Permanently disables `--scarlet-info` |
| `SCARLET_TAILWIND_DIAGNOSTICS` | Report the resolved binary on stderr before running |
| `SCARLET_TAILWIND_DOWNLOAD_TIMEOUT` | Mutex wait seconds, default 300 |

Resolution order is explicit path, then embedded runtime, then download cache (marker required), then
download. The runtime directory should be version-scoped (`<cacheRoot>/runtimes/<version>`) so switching
versions cannot serve the wrong binary.

Exit codes follow shell convention: Tailwind's own code when Tailwind ran, otherwise `0` success,
`64` usage error, `126` not executable, `127` not found.

## MSBuild Task Shape

```csharp
public sealed class TailwindCompileTask : Microsoft.Build.Utilities.Task
{
    [Required] public ITaskItem[] Compilations { get; set; } = Array.Empty<ITaskItem>();
    [Required] public string ProjectDirectory { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Debug";
    public string Minify { get; set; } = "Auto";
    public string Optimize { get; set; } = "false";
    public string Map { get; set; } = "Auto";
    public string Cwd { get; set; } = string.Empty;
    public string AdditionalArguments { get; set; } = string.Empty;
    public string StampDirectory { get; set; } = string.Empty;
    public string? RuntimeDirectory { get; set; }
    public bool TailwindRuntimeDownload { get; set; }
    public string? TailwindVersionDownload { get; set; }
    public int DownloadMutexTimeoutSeconds { get; set; } = 300;
    public int TimeoutMilliseconds { get; set; }
    public ITaskItem[]? RuntimePacks { get; set; }

    [Output] public ITaskItem[] GeneratedFiles { get; private set; } = Array.Empty<ITaskItem>();
    [Output] public ITaskItem[] RemovedFiles { get; private set; } = Array.Empty<ITaskItem>();
}
```

`GeneratedFiles` items carry `RelativePath` metadata. One Tailwind process per entry point: unlike Sass,
there is no batching win, because each invocation has exactly one input and one output.

Process handling should be lifted from the siblings rather than rewritten. `OutputCollector` keeps the
captured text, its bounded tail and the truncation flag behind one lock, because writes arrive on
`Process` reader callbacks while the build thread reads. `TaskLifetimeGate` makes "is the task still
running" and "log this" atomic with respect to task completion — MSBuild asserts its task is active and
throws rather than dropping a late event, and `AsyncStreamReader` rethrows that on a thread-pool thread,
where unhandled means a dead build. The drain events must be declared at method scope, not inside the
`try`, so they are not disposed before the gate closes. Neither stream handler may touch the `Process`
object. `ProcessStartRetry` handles the ETXTBSY window where a just-written executable is briefly busy.

## Build Target Sketch

```xml
<Target Name="_TailwindResolveStampDirectory">
  <PropertyGroup>
    <_TailwindStampDirectory Condition="'$(TailwindStampDirectory)' != ''">$(TailwindStampDirectory)</_TailwindStampDirectory>
    <_TailwindStampDirectory Condition="'$(_TailwindStampDirectory)' == '' AND '$(IntermediateOutputPath)' != ''">$([System.IO.Path]::GetFullPath($([System.IO.Path]::Combine('$(MSBuildProjectDirectory)', '$(IntermediateOutputPath)', 'Scarlet.Tailwind'))))</_TailwindStampDirectory>
  </PropertyGroup>
</Target>

<Target Name="RunTailwindBeforeStaticWebAssets"
        BeforeTargets="DispatchToInnerBuilds;ResolveProjectStaticWebAssets;PreBuildEvent"
        DependsOnTargets="_TailwindResolveStampDirectory"
        Condition="'$(TailwindEnabled)' == 'true' AND '@(TailwindBeforeStaticWebAssets)' != '' AND '$(DesignTimeBuild)' != 'true' AND '$(NoBuild)' != 'true' AND ('$(TargetFrameworks)' == '' OR '$(TargetFramework)' == '')">
  <TailwindCompileTask Compilations="@(TailwindBeforeStaticWebAssets)"
                       ProjectDirectory="$(MSBuildProjectDirectory)"
                       Configuration="$(Configuration)"
                       ... />
</Target>
```

Four parts of that condition are load-bearing and should each be commented in the shipped file:

- `_TailwindResolveStampDirectory` must be a **target**, not a file-scope `PropertyGroup`, because
  `$(IntermediateOutputPath)` is not set when the file is imported, only by the time a target runs. Both
  entry points depend on it rather than duplicating it, so they cannot answer differently.
- The `TargetFrameworks`/`TargetFramework` clause runs the target once in a multi-TFM outer build and skips
  inner builds. Without it, a three-TFM RCL runs Tailwind three times over the same `wwwroot`.
- `ResolveProjectStaticWebAssets` is named explicitly because it hooks `AssignTargetPaths`, which can
  precede `PreBuildEvent`.
- The `NoBuild` guard matters because a `BeforeTargets` hook still fires when the target it hooks is skipped
  by its own condition, and `ResolveProjectStaticWebAssets` carries `Condition="'$(NoBuild)' != 'true'"`.
  Without the guard, `dotnet pack --no-build` re-runs Tailwind and rewrites `wwwroot` after the integrity
  manifest was computed, so the package ships bytes that do not match their own hashes.

Three copies of the targets are needed — `build/`, `buildMultiTargeting/`, and a development copy imported
by the samples and integration tests — and a `TailwindTargetsTests` fixture should assert they declare the
same targets and invoke the task identically, normalising the one allowed difference in the development
copy's `DependsOnTargets`.

## Testing Requirements

The siblings' experience is that behaviour gets tested and *wiring* does not, so this section is
deliberately specific.

**Every task input must have a test that fails when its attribute is deleted from the targets.** The trap
is that a parameter whose C# default agrees with what the tests build is invisible when unwired: in
`Scarlet.Sass`, `Configuration` defaults to `"Debug"` and the suite built Debug, so deleting
`Configuration="$(Configuration)"` changed nothing observable and no test noticed. The robust form is a
single test deriving the expected attribute set by reflection over the task's public settable properties
and comparing it against each targets copy, so a new parameter cannot be added without being wired.

Name every one: `Compilations`, `ProjectDirectory`, `Configuration`, `Minify`, `Optimize`, `Map`, `Cwd`,
`AdditionalArguments`, `StampDirectory`, `RuntimeDirectory`, `TailwindRuntimeDownload`,
`TailwindVersionDownload`, `DownloadMutexTimeoutSeconds`, `TimeoutMilliseconds`, `RuntimePacks`.

And every item metadata value: `OutputPath`, `Minify`, `Optimize`, `Map`, `Cwd`, `AdditionalArguments`.

**Behavioural tests that must exist:**

- Every setting translates into the expected command-line flag, asserted against the logged command.
- `--cwd` defaults to the project directory, and an item's `Cwd` overrides it.
- `--minify` and `--optimize` together render `--minify` only.
- Debug and Release produce different defaults, asserted through a real `dotnet build` in both — noting
  that a Configuration-driven assertion only bites in the configuration the suite does not default to.
- A missing `OutputPath` fails with a message naming the item.
- Checksum parsing accepts the `./` prefix, rejects a mismatch, and reports a missing entry distinctly.
- An interrupted download (marker absent, binary present) is not treated as a usable cache entry.
- The download mutex: held past the timeout throws; abandoned by a terminated thread is treated as
  acquisition; a runtime published by another process while waiting is used rather than re-downloaded.
- A win-arm64 host selects the windows-x64 pack and logs the fallback.
- Pinning a v3 version fails with a message naming the version and asset.

**e2e scenarios**, mirroring the siblings' five, each consuming packed nupkgs from a local feed rather than
project references: `package-installation`, `multi-tfm` (Tailwind runs exactly once in the outer build and
the CSS is packed under `staticwebassets/`), `incremental` (a changed class in a `.razor` file appears in
the next build's CSS — the positive case that matters most given no MSBuild-level skipping),
`monorepo-download` (two apps sharing one `TailwindRuntimeDirectory`, exercising the mutex and atomic
publish), and `cli-tool`.

E2E scripts should accumulate failures rather than exiting on the first one, and print a `✓` or `✗` for
every check including setup steps — a run behind a full package build is expensive enough that it should
report everything broken, not just the first thing.

## Reuse From Scarlet.Bun and Scarlet.Sass

**Reuse as-is.** These are tool-independent, and several encode fixes that were expensive to find:
`TaskLifetimeGate` and `OutputCollector` including the method-scope drain-event declarations;
`ProcessStartRetry`; the `*RuntimePack` item contract and its selection ordering; the Core/MSBuild/Cli split
with the `tools/netstandard2.0/` packing rule and `TaskPackagingTests`; the packaged/development targets
pair guarded by an XML comparison test; the `_*ResolveStampDirectory` target-not-PropertyGroup trick; the
`NoBuild` guard; the run-before-discovery static web assets strategy with a project-relative `Content`
glob; the CLI's pointer-last push ordering; the shared test helpers (`RepositoryRoot`, `DotnetCli`,
`TempWorkspace`).

**Adapt.** The downloader takes Bun's staging, checksum and mutex design, minus every trace of archive
handling, plus the `./` prefix fix. The manifest and `Clean` target take Sass's design, with one output
pair per item instead of a directory walk. The README skeleton takes Sass's section list, with runtime
options, discovery, properties, metadata, task parameters, incrementality and `dotnet watch`.

**Do not reuse.** Any zip or tar provider, or the archive path-containment logic — there is no archive.
Bun's `Inputs`/`Outputs`/`StampFile` incrementality — it solves a problem Tailwind's task does not have, and
half-applying it would invite exactly the stale-CSS failure this design avoids. Sass's
directory-to-directory mode. Any legacy `*Runtime_<rid>` property contract.

## Documentation Requirements

- `README.md` at the repository root routing to the per-package readmes.
- `src/Scarlet.Tailwind.MSBuild/README.md` with a table of contents, install, the two runtime-acquisition
  options plus conditional package references, *How the Runtime Is Discovered*, properties, item metadata,
  task and output parameters, incrementality, `dotnet watch` integration, supported platforms, links.
- `src/Scarlet.Tailwind.Cli/README.md` and a separate RID-package readme.
- `AGENTS.md` covering build and test commands, the runtime discovery contract, common failure modes, and a
  naming-hygiene note — the siblings were both created by find-and-replace from an earlier repo and both
  shipped scars from it.
- `DEPLOYMENT.md` documenting version derivation and the pointer-last push rule as runnable commands.

The `dotnet watch` section must carry a warning the siblings do not need: **`--watch` must never be put in
`TailwindAdditionalArguments`.** Tailwind's watch mode never exits, so the build would hang rather than
finish, and with the default `TailwindTimeoutMilliseconds` of `0` it would wait indefinitely. The supported
approach is a `Watch` item pointing at the sources, or a separately managed `dotnet tailwind --watch`
process.

## Open Questions

- Should `TailwindOptimize` exist at all in v1, or wait for someone to ask for optimised-but-readable output?
- Should the win-arm64 fallback be silent, a normal-importance message, or opt-out-able via a property?
- Should the CLI ship an `any` portable package in v1, given the binary is ~110 MB and the download is
  correspondingly slow on first use?
- Is one Tailwind process per entry point acceptable, or should multiple entries with identical settings be
  worth batching if Tailwind ever grows multi-input support?
- Should the task detect a `tailwind.config.js` with no `@config` directive referencing it and warn? It is
  a silent no-op in v4 and an easy migration mistake.
- Should `Scarlet.Tailwind` ever pin a Tailwind version *range* rather than an exact version, given the
  runtime packages are versioned by Tailwind version?

## Recommended V1

1. `Scarlet.Tailwind.Core` targeting `netstandard2.0`: platform map for the seven v4 platforms, runtime
   resolver with the win-arm64 fallback, downloader with staged publish and checksum verification, chmod
   provider, process-start retry.
2. `Scarlet.Tailwind.MSBuild`: `TailwindCompileTask`, three targets copies, props defaults, the manifest and
   stamp scheme, static web assets recovery, `TailwindClean`.
3. Seven runtime packages, asset-only, versioned by Tailwind version, each emitting a `TailwindRuntimePack`.
4. `Scarlet.Tailwind.Cli` packing to nine packages with verbatim argument forwarding and `--scarlet-info`.
5. Two samples: one using runtime packages, one using download.
6. Unit, contract, packaging, integration and CLI test projects, with the reflection-derived wiring test
   from *Testing Requirements* present from the first commit rather than retrofitted.
7. Five e2e scenarios against packed nupkgs.
8. CI across Windows, Linux, macOS, ARM64 and musl, plus a monthly Tailwind version bump workflow that
   refuses drafts and prereleases.
9. Deploy workflow with tag-derived versioning and pointer-last push ordering.
