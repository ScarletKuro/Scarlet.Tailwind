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
- Support `dotnet clean` for generated CSS, source maps, and manifests.
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
- Do not expose a `TailwindConfigFile` property, and do not inspect the project for stylesheet or
  configuration mistakes. Tailwind owns its own configuration semantics; this package runs it. See
  *Configuration Files*.
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

**Unrecognised flags are silently discarded.** `packages/@tailwindcss-cli/src/utils/args.ts` iterates the
declared options and never reads anything else, so an unknown flag produces no error, no warning and no
effect. This matters more than it sounds; see *Configuration Files*.

**Repeated flags collapse to the last one, and there is no multi-input support.** The same parser does
`if (key !== '_' && Array.isArray(value)) value = value[value.length - 1]`, so
`--input a.css --input b.css` compiles only `b.css` without complaint. `--input` and `--output` are
declared `type: 'string'` and consumed as scalars throughout the build command — one `path.resolve`, one
existence check, one `fs.readFile`. One invocation is therefore one input and one output, permanently, so
the task's process-per-entry-point shape is forced rather than chosen, and there is no batching to be had.

The silent last-wins rule makes task-owned flags unsafe in `AdditionalArguments`. Overriding `--input`,
`--output`, `--cwd`, or `--map` would make the generated-file manifest and Clean describe different files
from the ones Tailwind actually used. The task must reject those flags and direct the user to the item and
its metadata instead.

**`--map` has two behaviours, not one.** Bare `--map` inlines the source map into the CSS; `--map <path>`
writes an external map file at that path, resolved against `--cwd`. The CLI even rejects `--map -`
explicitly with "Use --map without a value to inline the source map." This changes what the task produces:
inline means one output file, external means two, so the generated-files manifest and the static web asset
recovery both depend on which form was used. `TailwindMap` must therefore accept a path as well as a
boolean.

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

### Versioning

Two independent version lines, exactly as in both siblings.

`Directory.Build.props` holds `$(TailwindVersion)` — the pinned Tailwind release, and the single source of
truth for every runtime package and the CLI — alongside `$(TailwindCliRevision)`, which allows a CLI-only
fix without moving the Tailwind version. The CLI package version is `$(TailwindVersion).$(TailwindCliRevision)`.

Two mechanical details are easy to get wrong and expensive to debug. **NuGet normalises a trailing zero**,
so revision `0` publishes as plain `4.3.3` rather than `4.3.3.0`; the deploy workflow has to recompute that
same normalisation to locate the pointer package's filename. And **the revision resets to 0 whenever
`$(TailwindVersion)` moves**, because a re-release at an unchanged version is silently dropped by a push
that skips duplicates.

The MSBuild package is versioned independently, from the git tag, because its release cadence has nothing
to do with Tailwind's — a targets fix should not require a Tailwind bump, and a Tailwind bump should not
force a task release.

This also answers a question that looks open but is not: there is no "version range" to decide. A runtime
package's version *is* the Tailwind version it carries, so a consumer who wants floating behaviour writes
it in their own `PackageReference` — `Version="4.*"` — which is their call, not this package's.

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
path so the outcome never depends on the order NuGet happened to import packages in. `NativeRid` is
optional and defaults to `Rid`; only the emulated `win-arm64` item sets it (see *Windows ARM64*). Items
should be de-duplicated on `Rid` plus `NativeRid` plus normalised `RuntimesPath`, because a package's props
can be imported from more than one build folder.

Precedence: an explicit `TailwindRuntimeDirectory` wins over every pack and is never second-guessed;
`TailwindRuntimeDownload=true` bypasses pack resolution entirely; otherwise the first candidate pack whose
executable exists on disk is selected.

**There is no legacy property contract.** Nothing predates this package, so runtime discovery starts and
stays with the item. New runtime identifiers need a package that emits the item and no change to
`Scarlet.Tailwind.MSBuild` at all.

### Windows ARM64

Tailwind publishes no ARM64 binary for Windows, and Windows 11 on ARM runs x64 executables under
emulation. The mechanism for covering that gap is the pack contract itself: the `windows-x64` package
emits **two** items.

```xml
<TailwindRuntimePack Include="Scarlet.Tailwind.Runtime.windows-x64">
  <Rid>win-x64</Rid>
  <RuntimesPath>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)..', 'runtimes'))</RuntimesPath>
  <Variant>default</Variant>
  <Priority>0</Priority>
</TailwindRuntimePack>

<TailwindRuntimePack Include="Scarlet.Tailwind.Runtime.windows-x64 (win-arm64)">
  <Rid>win-arm64</Rid>
  <NativeRid>win-x64</NativeRid>
  <RuntimesPath>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)..', 'runtimes'))</RuntimesPath>
  <Variant>x64-emulated</Variant>
  <Priority>-100</Priority>
</TailwindRuntimePack>
```

Both point at the same `win-x64/native/tailwindcss.exe`. No second package and no duplicated binary.

`NativeRid` is what makes that possible, and it is the one piece of the contract this platform adds.
`RuntimesPath` names a directory laid out as `<rid>/native/<executable>`, so a pack serving `win-arm64`
would otherwise be looked for under `win-arm64/native/` — a directory the `windows-x64` package does not
ship and should not have to, since shipping it would mean the same 110 MB binary twice in one nupkg.
`NativeRid` separates the RID a pack **serves** from the one it **stores under**; it is optional, defaults
to `Rid`, and every other package omits it. It is also the honest shape of the thing: the emulated pack
really does serve one RID out of another's directory.

**`win-arm64` is then an ordinary RID and gets no special treatment anywhere else.** It resolves through
the same `SelectPacks` path as every other host, and the resolver's ordinary pack-selection line reports
the `x64-emulated` variant because `Variant` is part of a pack's description — not because emulation is
being singled out. There is no dedicated message, no extra log line, no property to refuse emulation, and
no branch in the resolver that names this platform.

That is a decision, not an omission. The alternative — a message on every build, or a
`TailwindAllowEmulatedRuntime` switch — would treat a working configuration as a problem to be announced,
and would mean maintaining a special case for a gap that is Tailwind's to close. Anyone who genuinely
objects to emulation already has two answers that cost this package nothing: point
`TailwindRuntimeDirectory` at their own binary, or do not reference the `windows-x64` package at all, which
produces the normal "no pack matches this host" error.

The decisive property is what happens when Tailwind ships a native build. Someone publishes
`Scarlet.Tailwind.Runtime.windows-arm64` at `Priority 0`; both items now serve `win-arm64`; priority
selects the native one; the emulated item becomes dead weight. **No code change, no resolver change, no
breaking change** — which is the property the contract exists to provide.

Two alternatives were considered and rejected.

**A delimited `<Rid>win-x64;win-arm64</Rid>`** would turn `Rid` from a scalar into a list, changing
parsing, the `Rid|RuntimesPath` de-duplication key, selection, and every error message. That is a permanent
complication to the core contract in exchange for one platform's temporary quirk, and `Priority` already
expresses the same thing.

**Folding win-arm64 into win-x64 inside `GetPlatform()`** is simpler and has apparent sibling precedent —
`Scarlet.Bun` folds x86 into x64. The precedent does not transfer. x86 there is a *process* architecture
quirk, a 32-bit MSBuild on a 64-bit OS, where the host really is x64. Here the host really is ARM64, so the
enum would be reporting something false, and that falsehood escapes into `--scarlet-info`, which exists to
be trusted. Un-folding later would also be a code change rather than a packaging one.

Teams that prefer strictness over emulation can point `TailwindRuntimeDirectory` at their own binary.

**The CLI answers this differently, deliberately.** `dotnet tool` RID selection uses NuGet's RID graph,
which we do not control, and `win-arm64` falls back to `win` and then `any` — never to `win-x64`, because
the architecture differs. Without a RID package an ARM64 developer would therefore install the portable
`any` package and download ~112 MB on first use, contradicting the embedded packages' promise of needing no
network. `Scarlet.Tailwind.Cli` should therefore ship an eighth RID package, `win-arm64`, carrying the x64
binary, with a package description saying so in as many words. The asymmetry with the runtime packages is
principled: we own MSBuild resolution and can express the fallback declaratively, we do not own tool
resolution and have to satisfy it with a package. It is also the safer direction, since adding a RID package
later is additive while removing one is breaking.

When no pack matches the host, the error should include a copy-pasteable `<PackageReference>` for the right
package, mention `TailwindRuntimeDownload` and `TailwindRuntimeDirectory` as alternatives, and list the
packs that were visible. When packs matched but no executable was found, the error should list every path
searched. These are two genuinely different failures and should not share a message.

### Host detection

Detecting the host platform should be split out from reading it, as in both siblings, so unsupported
combinations are testable without running on them.

`RuntimeInformation.RuntimeIdentifier` is unavailable on `netstandard2.0`, so **musl is detected by probing
`/lib`, `/lib64` and `/usr/lib` for `ld-musl-*.so.1`**. Only Alpine is realistically verified in CI; the
other directories are best-effort widening. The probe is a filesystem walk, so the result should be
computed once per task invocation and reused rather than recomputed per entry point.

**x86 folds into x64.** Tailwind ships no 32-bit build, but a 32-bit *host process* on a 64-bit OS — an
older MSBuild, for instance — can start the x64 binary perfectly well. This is the fold that is truthful,
and the contrast with *Windows ARM64* is the point: there the host really is a different architecture, here
only the process is.

**Anything else throws `PlatformNotSupportedException` naming the architecture**, rather than resolving a
mismatched binary. This is the error the CLI's `any` package exists to deliver — see *CLI API* — and on the
MSBuild side it should arrive as a task error, never as a stack trace.

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
| `TailwindMap` | `Auto` | `Auto`, `true`, `false`, or a path. `true` inlines the map, a path writes it there |
| `TailwindSilent` | `false` | Renders `--silent`, suppressing Tailwind's non-error output |
| `TailwindCwd` | empty | Working directory for source detection. Empty means `$(MSBuildProjectDirectory)` |
| `TailwindAdditionalArguments` | empty | Extra arguments for future Tailwind options; task-owned paths, maps and watch flags are rejected |
| `TailwindRuntimeDirectory` | empty | Explicit runtime directory containing `<rid>/native/<executable>` |
| `TailwindRuntimeDownload` | `false` | Download the runtime instead of using runtime packs |
| `TailwindVersionDownload` | empty | Version to download. Empty resolves the latest GitHub release |
| `TailwindDownloadMutexTimeoutSeconds` | `300` | Timeout for cross-process download coordination |
| `TailwindManifestDirectory` | empty | Generated-files manifest directory. Empty means `$(IntermediateOutputPath)\Scarlet.Tailwind` |
| `TailwindTimeoutMilliseconds` | `0` | Maximum time per invocation. `0` waits indefinitely |

`Auto` resolves from `$(Configuration)`:

| Property | Debug default | Release default |
| --- | --- | --- |
| `TailwindMinify` | `false` | `true` |
| `TailwindMap` | `true`, meaning inline | `false` |

`Auto` resolving to `true` rather than to a path is deliberate: an inline map keeps the output to a single
file, so the generated-files manifest has one entry, the static web asset pipeline has one asset to
fingerprint, and `Clean` has one file to remove. The cost is a larger dev-time stylesheet, which does not
ship because Release defaults to no map at all. A project that wants an external `.css.map` — to keep the
served CSS small, or to match what `Scarlet.Sass` produces — sets the path form explicitly, and the task
must then record **both** files in `GeneratedFiles` and the manifest.

`--minify` already implies optimisation — the CLI describes it as "Optimize and minify the output", and
both flags enter the same branch, with `--minify` additionally setting `minify: true` inside the optimiser.
`TailwindOptimize` therefore covers the narrower case of wanting the optimiser's transforms (vendor
prefixing, syntax lowering, dead-rule removal) with readable output, which is useful when debugging a
production-shaped stylesheet or when a downstream bundler will do the minifying. When both are true the
task should render `--minify` only, since it is a superset, and should say so in the property documentation
rather than passing both and relying on Tailwind's precedence.

**The rule for what gets a property: if Tailwind's CLI has the flag, this package exposes it.** The
alternative — guessing which flags users will want — is how a wrapper ends up with an arbitrary subset and
a stream of "why can't I set X" issues, and `TailwindAdditionalArguments` is a worse answer for anything
Tailwind supports first-class. Every flag in *Upstream Facts* therefore has a home:

| Flag | How it is exposed |
| --- | --- |
| `--input` | the item's `Include` |
| `--output` | `OutputPath` metadata |
| `--minify` | `TailwindMinify` / `Minify` |
| `--optimize` | `TailwindOptimize` / `Optimize` |
| `--map` | `TailwindMap` / `Map` |
| `--silent` | `TailwindSilent` / `Silent` |
| `--cwd` | `TailwindCwd` / `Cwd` |
| `--watch` | **rejected by the MSBuild task** |
| `--poll` | **rejected by the MSBuild task** |

The two exclusions are the only ones, and they share a reason rather than being a judgement call:
Tailwind's watch mode never exits, so a build that invoked it would hang instead of completing, and
`--poll` is valid only alongside `--watch`. Neither may reach Tailwind through `AdditionalArguments`.
Watching is `dotnet watch`'s job — see *Competitor Analysis*.

If Tailwind adds a flag, the corresponding property is a one-line addition plus its wiring test, and the
table above is the checklist that makes the omission obvious.

## Item Metadata

`TailwindBeforeStaticWebAssets` supports the same compile settings as the global properties, applied per
entry point:

```xml
<TailwindBeforeStaticWebAssets Include="Styles/app.css">
  <OutputPath>wwwroot/css/app.css</OutputPath>
  <Minify>true</Minify>
  <Optimize>false</Optimize>
  <Map>false</Map>
  <Silent>true</Silent>
  <Cwd>..</Cwd>
  <AdditionalArguments></AdditionalArguments>
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

**`--cwd` also resolves `--input`, `--output` and `--map`, so the task must pass all three as absolute
paths.** Verified against 4.3.3: `--cwd=sub --input=sub/app.css` fails with
``Specified input file `.\sub\sub\app.css` does not exist`` — the relative input is resolved a second time
against the new working directory. Passing absolute paths removes the interaction entirely, leaves `--cwd`
doing only the one job it is there for, and means the paths the task records in `GeneratedFiles` and the
manifest are by construction the paths Tailwind wrote.

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

### How v4 decides what to scan, and why `bin`/`obj` matter

There is no file-type filter in v4. The v3 `content: ["./**/*.{cshtml,razor}"]` array is gone, and the
scanner does not look for Razor or cshtml specifically — it walks the tree from the working directory and
treats **every** surviving file as plain text, extracting anything that looks like a class name. What
narrows it is exclusion, not inclusion.

The exclusions come from three places. The `.gitignore` files that apply to the tree; the documented
categories (CSS files, lock files, binary extensions); and a hardcoded directory list in
`crates/oxide/src/scanner/fixtures/ignored-content-dirs.txt`, which at the time of writing is exactly:

```text
.git  .hg  .jj  .next  .parcel-cache  .pnpm-store  .svelte-kit  .svn
.turbo  .venv  .vercel  .yarn  __pycache__  node_modules  venv
```

**Every entry is from the JavaScript or Python ecosystem. `bin` and `obj` are not there.** In a .NET
project they are excluded by `.gitignore` alone. The scanner sets `require_git(false)`, so a `.gitignore`
is honoured even with no `.git` directory present — which means the standard dotnet `.gitignore` does the
job for most projects, and a project without one, or with one that does not cover `bin`/`obj`, gets both
directories scanned.

That is not merely slow, it is wrong in a specific and hard-to-debug way. `obj` holds the Razor compiler's
generated `*.g.cs` files, which contain the class names from **previous** builds. A class deleted from a
`.razor` file this morning is still present in yesterday's generated source, so Tailwind keeps emitting CSS
for it, and the only symptom is a stylesheet that will not shrink. `bin` compounds it by holding copies of
content files, so the same markup is scanned twice.

The spec's position: `Scarlet.Tailwind` must not paper over this by rewriting the user's stylesheet, but it
must not leave users to discover it either. Both samples and the README should show the exclusion
explicitly, as the recommended starting point for a .NET project:

```css
@import "tailwindcss";

@source not "./bin";
@source not "./obj";
```

For projects that would rather enumerate sources than exclude them, `source(none)` turns off detection
entirely and the v3 `content` array translates directly. Brace expansion is supported in source patterns —
Tailwind's own glob tests cover `a-{b,c}-d-{e,f}-g/*.html` — so the v3 example maps one-to-one:

```css
@import "tailwindcss" source(none);
@source "./**/*.{cshtml,razor}";
```

This is stricter and faster, at the cost of silently missing classes that appear in a file type nobody
remembered to list — a `.cs` file building a class string, say. The README should present exclusion as the
default and `source(none)` as the deliberate opt-in.

Keeping all three in the stylesheet means one place to look, and it means a `dotnet build` and a bare
`npx @tailwindcss/cli -i Styles/app.css -o out.css` produce the same result.

## Configuration Files

**There should be no `TailwindConfigFile` property.** This is worth stating as a decision rather than an
omission, because the most widely used competitor has one and it is the single most instructive mistake in
the field.

Tailwind v4's CLI accepts no `--config` or `-c` flag — the full option set is the nine flags listed in
*Upstream Facts*. Worse, its argument parser **silently discards unrecognised flags** rather than
rejecting them: `packages/@tailwindcss-cli/src/utils/args.ts` iterates the declared options and simply
never reads anything else. So passing `-c tailwind.config.js` to a v4 binary does not fail. It does
nothing, and the build produces CSS as though no configuration existed.

`tailwind-dotnet` emits `-c "$(_TailwindTailwindConfigFile)"` unconditionally, defaulting to
`tailwind.config.js`, with no guard on the Tailwind major version, while advertising support for both v3
and v4. On v3 that works. On v4 the user has a property that appears to be respected, a config file that
appears to be wired up, and a silently ignored flag in between. Adding the same property to
`Scarlet.Tailwind` would reproduce that failure exactly, because there is no correct value to pass it to.

The supported path is Tailwind's own, from inside the entry stylesheet:

```css
@import "tailwindcss";
@config "../../tailwind.config.js";
```

**The task should not inspect the project for orphaned config files either.** An earlier draft proposed
detecting a `tailwind.config.js` that no `@config` directive references and reporting it, on the grounds
that v4 silently ignores such a file. That was scope creep, and the reasoning behind it was a false
analogy.

The `bin`/`obj` problem in *Working Directory and Source Detection* is .NET-specific: Tailwind's ignore
list is drawn from the JavaScript and Python ecosystems, it cannot reasonably be expected to know about the
.NET toolchain, and this package is the only thing positioned to warn. An orphaned `tailwind.config.js` is
not .NET-specific in any way — a React project hits exactly the same silence — so it belongs to Tailwind,
not to a build integration. Note also that the answer for `bin`/`obj` is documentation, not a code
diagnostic; the consistent answer here is the same.

Three further reasons hold independently. A file existing is not evidence of intent: it may serve an editor
plugin, a linter, or a neighbouring project. A broken migration and a finished one are externally
identical, so any detection is guessing at intent from file contents — the kind of cleverness that
generates its own bug reports. And the concern expires when v3 migrations do, whereas a per-build probe
would not.

The siblings are thin in exactly this way and should stay that way: `Scarlet.Sass` does not lint stylesheets
or comment on unreferenced partials, and `Scarlet.Bun` does not inspect `package.json`.

The lever that does belong to this package is the README. *Writing Your Entry Stylesheet* must explain
`@config`, mark it legacy, and carry the caveat that `corePlugins`, `safelist` and `separator` are
unsupported under v4 even through `@config`. That reaches the reader before they hit the problem, costs
nothing at build time, and produces no false positives.

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

A generated-files manifest should still be written for a job unrelated to skipping:

- `<manifestDirectory>/Tailwind.generated.txt` records every file the task produced, one absolute path per
  line. It drives stale-output removal when an entry point is renamed or removed, and it is what `Clean`
  reads.

The manifest should be written only after every invocation exits successfully, so a failed build does not
record outputs it did not produce.

There is deliberately no settings stamp. Tailwind always recompiles and overwrites its explicit output, so
deleting that output when settings change does not force any work that would otherwise be skipped. This is
different from Sass, whose settings stamp is required to force its internal `--update` check to regenerate.

If a team later wants opt-in skipping, `Scarlet.Bun`'s `Inputs`/`Outputs`/`StampFile` scheme is the model —
it is opt-in precisely because Bun also cannot know its own inputs. That is a deliberate v2 question, not a
v1 omission.

## Cleaning

```xml
<Target Name="TailwindClean" BeforeTargets="CoreClean;Clean" DependsOnTargets="_TailwindResolveManifestDirectory">
  <ReadLinesFromFile File="$(_TailwindManifestDirectory)/Tailwind.generated.txt"
                     Condition="Exists('$(_TailwindManifestDirectory)/Tailwind.generated.txt')">
    <Output TaskParameter="Lines" ItemName="_TailwindFilesToClean" />
  </ReadLinesFromFile>
  <Delete Files="@(_TailwindFilesToClean)" Condition="'@(_TailwindFilesToClean)' != ''" />
  <Delete Files="$(_TailwindManifestDirectory)/Tailwind.generated.txt" />
</Target>
```

Manifest-driven, never a blanket `wwwroot` delete — the output directory usually contains hand-authored
files too. Both hooks are required: `CoreClean` finds the manifest written by a single-target build, while
the outer `Clean` target finds the manifest written by a multi-target build. The other hook sees no manifest
and is harmless.

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
`dotnet tailwind`. With `RuntimeIdentifiers` covering eight RIDs plus `any`, one `dotnet pack` produces ten
packages: eight RID-specific ones with the binary embedded, a portable `any` package that downloads on
first use, and the top-level pointer package.

The eight RIDs are the seven Tailwind publishes for, plus `win-arm64` carrying the x64 binary — see
*Windows ARM64* for why the CLI needs a package where the runtime side needs only an item. The RID-to-asset
map therefore has one entry that is not one-to-one, and `TailwindRidMapTests` should assert that
deliberately rather than treating it as a discrepancy to fix.

**The `any` package is not optional, and its job is diagnostics as much as portability.** It embeds
nothing, so it costs nothing to ship, and NuGet's RID graph terminates at `any` — which means it is what
every RID we did not enumerate resolves to. Two things follow.

An architecture Tailwind genuinely does not support, such as 32-bit ARM, installs successfully and then
fails at run time with **our** message: `GetPlatform` throws naming the architecture and pointing at
`SCARLET_TAILWIND_PATH`. Without `any`, the same user gets a NuGet install failure saying the package does
not support their RID — technically accurate, but it blames packaging for what is really an upstream
platform gap, and it gives them nothing to do about it.

A RID that is merely unusual, rather than unsupported, works: `any` resolves the platform, downloads the
right asset, and caches it. That is the portability half.

The first-use download is real and should be documented, but it is the cost of the fallback path, not a
reason to remove the fallback — the package itself adds no bytes to anyone's restore.

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
| `SCARLET_TAILWIND_CACHE` | Cache root override |
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
    public string Silent { get; set; } = "false";
    public string Cwd { get; set; } = string.Empty;
    public string AdditionalArguments { get; set; } = string.Empty;
    public string ManifestDirectory { get; set; } = string.Empty;
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
<Target Name="_TailwindResolveManifestDirectory">
  <PropertyGroup>
    <_TailwindManifestDirectory Condition="'$(TailwindManifestDirectory)' != ''">$(TailwindManifestDirectory)</_TailwindManifestDirectory>
    <_TailwindManifestDirectory Condition="'$(_TailwindManifestDirectory)' == '' AND '$(IntermediateOutputPath)' != ''">$([System.IO.Path]::GetFullPath($([System.IO.Path]::Combine('$(MSBuildProjectDirectory)', '$(IntermediateOutputPath)', 'Scarlet.Tailwind'))))</_TailwindManifestDirectory>
  </PropertyGroup>
</Target>

<Target Name="RunTailwindBeforeStaticWebAssets"
        BeforeTargets="DispatchToInnerBuilds;ResolveProjectStaticWebAssets;PreBuildEvent"
        DependsOnTargets="_TailwindResolveManifestDirectory"
        Condition="'$(TailwindEnabled)' == 'true' AND '@(TailwindBeforeStaticWebAssets)' != '' AND '$(DesignTimeBuild)' != 'true' AND '$(NoBuild)' != 'true' AND ('$(TargetFrameworks)' == '' OR '$(TargetFramework)' == '')">
  <TailwindCompileTask Compilations="@(TailwindBeforeStaticWebAssets)"
                       ProjectDirectory="$(MSBuildProjectDirectory)"
                       Configuration="$(Configuration)"
                       ... />
</Target>
```

Four parts of that condition are load-bearing and should each be commented in the shipped file:

- `_TailwindResolveManifestDirectory` must be a **target**, not a file-scope `PropertyGroup`, because
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
`Silent`, `AdditionalArguments`, `ManifestDirectory`, `RuntimeDirectory`, `TailwindRuntimeDownload`,
`TailwindVersionDownload`, `DownloadMutexTimeoutSeconds`, `TimeoutMilliseconds`, `RuntimePacks`.

And every item metadata value: `OutputPath`, `Minify`, `Optimize`, `Map`, `Silent`, `Cwd`,
`AdditionalArguments`.

**Behavioural tests that must exist:**

- Every setting translates into the expected command-line flag, asserted against the logged command.
- `--cwd` defaults to the project directory, and an item's `Cwd` overrides it.
- `--minify` and `--optimize` together render `--minify` only.
- `TailwindMap=true` renders bare `--map` and yields one entry in `GeneratedFiles`; a path value renders
  `--map <path>` and yields two, with the map file present on disk and removed by `Clean`.
- Debug and Release produce different defaults, asserted through a real `dotnet build` in both — noting
  that a Configuration-driven assertion only bites in the configuration the suite does not default to.
- A missing `OutputPath` fails with a message naming the item.
- Checksum parsing accepts the `./` prefix, rejects a mismatch, and reports a missing entry distinctly.
- An interrupted download (marker absent, binary present) is not treated as a usable cache entry.
- The download mutex: held past the timeout throws; abandoned by a terminated thread is treated as
  acquisition; a runtime published by another process while waiting is used rather than re-downloaded.
- An unsupported architecture throws `PlatformNotSupportedException` naming it, and x86 folds into x64
  rather than throwing.
- A win-arm64 host selects the emulated pack, and a pack for the same RID at a higher priority beats it.
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
pair guarded by an XML comparison test; the `_*ResolveManifestDirectory` target-not-PropertyGroup trick; the
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

## Competitor Analysis

The .NET Tailwind ecosystem is crowded but thin: most packages on nuget.org matching `tailwindcss` are
either abandoned, tool-only, or wrappers that assume Node.js. The four below are the ones worth reasoning
about. None of them ships unit, integration and e2e tests, which is the gap `Scarlet.Tailwind` exists to
close.

### `kallebysantos/tailwind-dotnet`

The most complete competitor and the current incumbent. Packages: `Tailwind.Hosting.Build` (MSBuild),
`Tailwind.Hosting` (an `IHostingStartup` for hot reload), `Tailwind.Hosting.Cli`. Downloads the standalone
binary, no Node.js, claims v3 and v4 support. Properties: `TailwindVersion`, `TailwindInputCssFile`,
`TailwindOutputCssFile`, `TailwindConfigFile`, `TailwindWatch`, `TailwindMinifyOnPublish`,
`TailwindExcludeInputFileOnPublish`.

**Strong points.** No Node.js. Sensible default paths. A genuine attempt at a dev-time watch story.
Supports WebForms and Blazor Hybrid examples, which is broader than most.

**Weak points.** No test project of any kind. `-c` is passed unconditionally and is a silent no-op on v4
(see *Configuration Files*). The download has no cross-process coordination, so a monorepo building several
projects in parallel has them fight over one download — the problem PR #26 was opened to fix in April 2026
with a mutex and a configurable download location, still unmerged and unreviewed at the time of writing.
That PR's non-acceptance is the proximate reason for building this package. The `IHostingStartup` watch
mechanism spawns a `tailwind --watch` child from inside the running application, which on Windows leaves
zombie processes: the watcher's lifetime is tied to a host that `dotnet watch` kills and restarts on every
rebuild, and Windows has no process-group teardown by default, so each cycle can leak another watcher.

**Borrow.** The property naming instinct is good — `TailwindInputCssFile`/`TailwindOutputCssFile` read
well, even though this spec chooses items over properties. The decision to bundle no Node.js is correct
and non-negotiable.

**Do not borrow.** `TailwindConfigFile`. The `IHostingStartup` watcher. Downloading without a mutex.

### `tailwindcss.msbuild` and `tailwindcss.cli` (duaneedwards)

Version 0.0.3, published March 2022, ~4.3K downloads. Requires the Tailwind CLI to already be installed
locally — it integrates rather than acquires. Predates Tailwind v4 by two major versions and has had no
update since.

**Borrow.** Nothing. Listed here only so the spec records that the obvious package name is taken by
something abandoned, which matters for discoverability and naming.

### `rozumak/tailwindcss-dotnet`

A `dotnet tool` only, no MSBuild integration. Downloads platform-specific executables on first use and
exposes `install`, `build`, `watch` and `exec`. Documents v3 throughout, with a `--tailwindcss` flag to
override the CLI version; v4 support is not claimed. Last meaningful activity roughly a year ago.

**Borrow.** The `exec` escape hatch is a good idea and close to what `Scarlet.Tailwind.Cli` does by
default — forward everything verbatim rather than modelling subcommands. Their `install` scaffolding
command is the opposite approach to this spec's, which deliberately generates nothing.

### `dotnetdev-kr/DotnetDevKR.TailwindCSS`

MSBuild integration for v4+. Bundles the standalone executables **inside the NuGet package itself** and
picks one at build time by detecting OS and architecture. Properties: `InputFilename`, `OutputFilename`,
`IsMinify`, `DebugMode`, `ProjectDir`. Its `DotnetDevKR.TailwindCSS.WebTest` is a Blazor WebAssembly
sample, not a test suite.

**Strong point.** Explicit v4 targeting, which most of the field lacks.

**Weak point.** Shipping every platform's binary in one package means every consumer downloads roughly
700 MB of binaries to use one of them. This is precisely the problem the per-RID runtime package split
solves, and it is the clearest vindication of the sibling architecture: `Scarlet.Tailwind` ships seven
small packages plus a discovery contract, so a consumer restores one ~110 MB binary, or none at all if they
use download mode.

**Borrow.** `DebugMode` driving source maps is the same instinct as this spec's `Configuration`-driven
`Auto` defaults, though tying it to the SDK's own `$(Configuration)` is better than a bespoke flag.

### Why Scarlet.Tailwind makes sense

Three gaps are common to all of them, and each is something the sibling platform already solves:

1. **No cross-process download coordination.** Every competitor that downloads does so without a mutex, so
   parallel or monorepo builds race. `Scarlet.Bun` and `Scarlet.Sass` both solve this with a named global
   mutex plus atomic publication, and the design carries over unchanged.
2. **No test coverage worth the name.** None of the four ships unit, integration or e2e tests. The siblings'
   experience is that the failures that matter — an unwired MSBuild attribute, a stale cache entry, a
   half-written binary, output lost on timeout — are invisible without them.
3. **No static web assets integration.** None of them runs before `ResolveProjectStaticWebAssets`, so
   generated CSS does not get fingerprinted, does not reach `staticwebassets/` when an RCL is packed, and
   is not removed by `dotnet clean`. For Blazor and Razor Class Libraries that is the whole point.

A fourth gap is narrower but decisive for the incumbent's users: **watching should not be a hosted
service.** `Scarlet.Tailwind` should have no runtime package, no `IHostingStartup`, and no long-lived child
process owned by the application. The supported dev loop is a `Watch` item plus `dotnet watch`, exactly as
documented for both siblings:

```xml
<ItemGroup>
  <Watch Include="Styles\**\*.css" />
</ItemGroup>
```

`dotnet watch` then triggers an ordinary build, which runs the Tailwind target that was always going to
run. Nothing owns a background process, so nothing can leak one. The honest cost is a full MSBuild build
per save rather than Tailwind's own incremental watch; for anyone who needs that, running
`dotnet tailwind --watch` as a separate foreground process is the documented escape hatch, and there the
process is the user's to manage rather than something a web host spawned invisibly.

## Documentation Requirements

- `README.md` at the repository root routing to the per-package readmes.
- `src/Scarlet.Tailwind.MSBuild/README.md` with a table of contents, install, the two runtime-acquisition
  options plus conditional package references, *How the Runtime Is Discovered*, **Writing Your Entry
  Stylesheet**, properties, item metadata, task and output parameters, incrementality, `dotnet watch`
  integration, supported platforms, links.
- `src/Scarlet.Tailwind.Cli/README.md` and a separate RID-package readme.
- `AGENTS.md` covering build and test commands, the runtime discovery contract, common failure modes, and a
  naming-hygiene note — the siblings were both created by find-and-replace from an earlier repo and both
  shipped scars from it.
- `DEPLOYMENT.md` documenting version derivation and the pointer-last push rule as runnable commands.

The `dotnet watch` section must explain that `--watch`, `-w`, and `--poll` are rejected in
`TailwindAdditionalArguments`. Tailwind's watch mode never exits, so the build would hang rather than
finish. The supported approach is a `Watch` item pointing at the sources, or a separately managed
`dotnet tailwind --watch` process.

### Writing Your Entry Stylesheet

This section is required, must appear before the property tables, and is the one piece of documentation
this package is uniquely positioned to provide.

`Scarlet.Sass` needs nothing like it: Dart Sass compiles the files you name, so there is nothing to explain
beyond the item. Tailwind is the opposite — the stylesheet is the configuration, and what ends up in the
output depends on what the scanner finds on disk. That makes it the consumer's business, not an
implementation detail.

Crucially, **upstream documentation will not tell a .NET developer any of this.** Tailwind's docs are
written for JavaScript projects, where `node_modules` is in the built-in ignore list and the equivalent
problem does not arise. Nothing on tailwindcss.com mentions `bin` or `obj`, and the failure mode is silent:
CSS that quietly refuses to shrink because deleted classes are still being found in stale generated
sources. A developer hitting it has no reason to suspect the scanner and every reason to suspect this
package.

The section must cover, in this order:

1. **A minimal working entry stylesheet**, since `@import "tailwindcss"` is the whole of it and a reader
   arriving from v3 will be looking for a config file that no longer exists.
2. **The `bin`/`obj` exclusion, with the reason.** Show the two `@source not` lines, then explain that
   Tailwind's built-in ignore list covers `node_modules` and `.git` but nothing from the .NET toolchain, so
   `obj` — which holds the Razor compiler's generated `*.g.cs` files from previous builds — is otherwise
   fair game. State the symptom plainly: classes you deleted keep appearing in the output.
3. **What `.gitignore` does and does not save you from**, since most projects are already covered and
   should understand why, rather than copying lines they do not need.
4. **The v3 `content` array migration**, mapped directly onto `source(none)` plus `@source`, with brace
   expansion shown, and framed as the stricter opt-in rather than the default.
5. **`@theme` and `@config`**, briefly, with `@config` marked legacy and carrying the `corePlugins` /
   `safelist` / `separator` caveat.
6. **A pointer to `--cwd`**, explaining that `Scarlet.Tailwind` pins the scan root to the project directory
   so these paths mean the same thing however the build was invoked.

Both samples must use the recommended stylesheet verbatim, so the documented advice is also the tested
path rather than prose nobody executes.

## Recommended V1

1. `Scarlet.Tailwind.Core` targeting `netstandard2.0`: platform map for the seven v4 platforms, runtime
   resolver with the win-arm64 fallback, downloader with staged publish and checksum verification, chmod
   provider, process-start retry.
2. `Scarlet.Tailwind.MSBuild`: `TailwindCompileTask`, three targets copies, props defaults, the manifest and
   generated-files manifest, static web assets recovery, `TailwindClean`.
3. Seven runtime packages, asset-only, versioned by Tailwind version, each emitting a `TailwindRuntimePack`
   (the windows-x64 package emitting two, per *Windows ARM64*).
4. `Scarlet.Tailwind.Cli` packing to ten packages with verbatim argument forwarding and `--scarlet-info`.
5. Two samples: one using runtime packages, one using download.
6. Unit, contract, packaging, integration and CLI test projects, with the reflection-derived wiring test
   from *Testing Requirements* present from the first commit rather than retrofitted.
7. Five e2e scenarios against packed nupkgs.
8. CI across Windows, Linux, macOS, ARM64 and musl, plus a monthly Tailwind version bump workflow that
   refuses drafts and prereleases.
9. Deploy workflow with tag-derived versioning and pointer-last push ordering.
