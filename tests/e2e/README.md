# End-to-End (E2E) Tests

This directory contains end-to-end tests that validate the complete Scarlet.Tailwind package installation and
execution flow.

The tests intentionally exercise packed NuGet packages from a local feed instead of project references. That
is what catches the expensive failures: missing task dependencies in `tools/netstandard2.0/`, broken `build/`
or `buildMultiTargeting/` imports, runtime package layout mistakes, static web asset timing regressions, and
CLI pointer/RID package issues.

None of those are reachable from the in-process test projects, which resolve everything through
`ProjectReference` and therefore never open a nupkg.

## Directory Structure

```text
tests/e2e/
├── package-installation/
│   ├── verify.sh                    # Package installation and static web assets E2E test
│   └── templates/                   # Template files for the package install test
├── monorepo-download/
│   ├── verify.sh                    # Shared runtime download E2E test
│   └── templates/                   # Template files for the monorepo download test
├── multi-tfm/
│   ├── verify.sh                    # Multi-target framework Razor Class Library E2E test
│   └── templates/                   # Template files for the multi-TFM test
├── incremental/
│   ├── verify.sh                    # Rescan, setting-change and clean E2E test
│   └── templates/                   # Template files for the incremental test
├── hot-reload/
│   ├── verify.sh                    # Blazor and project-aware Tailwind watcher E2E test
│   └── templates/                   # Minimal Blazor Web App and NuGet configuration
├── cli-tool/
│   ├── verify.sh                    # dotnet tailwind .NET tool E2E test
│   └── templates/                   # Template files for the CLI tool test
└── README.md
```

## What Each Scenario Proves

| Scenario | The claim it tests |
|---|---|
| `package-installation` | The packed task loads in a real consumer build, compiles, packs the CSS under `staticwebassets/`, and cleans up after itself |
| `multi-tfm` | A three-TFM Razor Class Library runs Tailwind once, packs the CSS under `staticwebassets/` exactly once, and is not re-run by `pack --no-build` |
| `incremental` | A class added to a `.razor` file reaches the next build's CSS, and changing a setting discards the previous output |
| `hot-reload` | `dotnet tailwind watch` discovers the packed MSBuild configuration, then Blazor Hot Reload and Tailwind update component markup and generated CSS without restarting the app |
| `monorepo-download` | Four projects sharing one runtime directory coordinate a single download instead of racing |
| `cli-tool` | Raw `dotnet tailwind --input ... --output ...` passthrough runs the Tailwind embedded in its RID package, with no MSBuild project and no download |

The two CLI scenarios intentionally exercise different contracts. `cli-tool` supplies Tailwind's native
arguments directly and proves the tool works on its own. `hot-reload` supplies only `watch --project ...`;
the CLI must ask the packed MSBuild target for its input, output, working directory, runtime and remaining
arguments before starting the native watcher.

Tailwind has no compiler-managed incremental flag and cannot tell MSBuild which files it scanned, so the
target has no `Inputs`/`Outputs`. The claim worth testing is the positive one: changing a file Tailwind
*scans* — not the stylesheet — still changes the CSS.

## Running Locally

Most scripts take three arguments:

```bash
./verify.sh <workspace-path> <package-version> <runtime-version>
```

The `cli-tool` and `hot-reload` scenarios also install the CLI and therefore take its package version as a
fourth argument:

```bash
./verify.sh <workspace-path> <package-version> <runtime-version> <cli-version>
```

- `<workspace-path>` — a directory containing a `packages/` folder with the packed `.nupkg` files
- `<package-version>` — the `Scarlet.Tailwind.MSBuild` version to install
- `<runtime-version>` — the `Scarlet.Tailwind.Runtime.*` version, which is the upstream Tailwind version
- `<cli-version>` — the `Scarlet.Tailwind.Cli` package version; this includes `TailwindCliRevision` and can
  therefore differ from the runtime version

So, from the repository root:

```bash
mkdir -p /tmp/e2e/packages
dotnet pack --configuration Release --output /tmp/e2e/packages
tests/e2e/package-installation/verify.sh /tmp/e2e 1.0.0 4.3.3
tests/e2e/cli-tool/verify.sh /tmp/e2e 1.0.0 4.3.3 4.3.3.1
tests/e2e/hot-reload/verify.sh /tmp/e2e 1.0.0 4.3.3 4.3.3.1
```

Each script creates its own directory under `/tmp`, uses a private `NUGET_PACKAGES` cache so a previously
restored copy of the same version cannot satisfy the restore, and removes the directory afterwards unless
`CI` is set.

## Conventions

These scripts are written to a shared style, and it is worth keeping:

- **Accumulate failures, do not exit on the first one.** `FAILED=1` and a final check, so one run reports
  everything that is broken rather than the first thing.
- **`fatal` is for setup only** — a missing runtime package, a failed `dotnet new` — where the remaining
  assertions could not run meaningfully anyway.
- **Every check prints `✓` or `✗`**, including setup steps, so the log reads as a checklist.
- **Pin `--configuration` whenever a scenario both builds and packs.** `dotnet build` defaults to Debug and
  `dotnet pack` defaults to Release, so a `pack --no-build` after a plain `build` looks for output that was
  never produced and dies with `Manifest file at obj/Release/.../staticwebassets.build.json not found`.
- **Assertions that grep build output need `--verbosity normal`.** The task logs at `High` importance, which
  `minimal` drops, so a grep against a `minimal` build silently matches nothing and can never fail. This has
  caused real regressions before.

`.gitattributes` forces `*.sh` to LF endings. Without that, a checkout on Windows gives these files CRLF and
they fail on Linux with a bad-interpreter error.
