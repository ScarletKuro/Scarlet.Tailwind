# Scarlet.Tailwind.Cli

Run the [Tailwind CSS](https://tailwindcss.com) standalone CLI as a .NET tool. No Node.js, no npm, no
`node_modules`.

```bash
dotnet tailwind --input Styles/app.css --output wwwroot/css/app.css
dotnet tailwind --input Styles/app.css --output wwwroot/css/app.css --watch
dotnet tailwind --help
```

Every argument is forwarded to Tailwind verbatim, so anything valid after `tailwindcss` is valid after
`dotnet tailwind`.

## Why not just download the standalone CLI?

You still can — nothing here stops you, and for a solo project with no CI this buys you little. It earns
its keep once a repository has more than one contributor or a CI pipeline, because it makes Tailwind pinned
and installed the same way as the rest of your .NET tooling, instead of a separate step with its own
installer:

- **Pinned like everything else.** The Tailwind version lives in `.config/dotnet-tools.json` next to
  `dotnet-ef`, `dotnet-format`, and friends. `dotnet tool restore` brings it down with the repository —
  no "works on my machine because I have 4.3 and CI has 4.1."
- **Hash-verified.** NuGet checks the package against what was published, unlike a `curl | sh` installer.
- **No network at run time**, for the platform-specific package — it **contains the Tailwind binary**, so
  there's no download on first use, no dependency on github.com being reachable, and no chance of a CI
  agent quietly picking up a different Tailwind than your laptop did.

## Install

Per repository (recommended — this is the part that pins):

```bash
dotnet new tool-manifest      # once per repository
dotnet tool install Scarlet.Tailwind.Cli
dotnet tailwind --help
```

Commit `.config/dotnet-tools.json` and every contributor and CI agent gets the same Tailwind from
`dotnet tool restore`.

Globally:

```bash
dotnet tool install -g Scarlet.Tailwind.Cli
```

Or once, without installing anything (.NET 10 SDK):

```bash
dnx Scarlet.Tailwind.Cli -- --input app.css --output out.css
```

> **The package version is the Tailwind version.** `Scarlet.Tailwind.Cli` 4.3.3 contains Tailwind CSS
> 4.3.3, the same as the `Scarlet.Tailwind.Runtime.*` packages.

To move to a newer Tailwind, update the package like any other .NET tool — no separate upgrade command
needed:

```bash
dotnet tool update Scarlet.Tailwind.Cli      # local: also bumps .config/dotnet-tools.json
dotnet tool update -g Scarlet.Tailwind.Cli   # global
```

## How it finds Tailwind

`Scarlet.Tailwind.Cli` is a pointer package: it owns the `dotnet-tailwind` command but carries no Tailwind
binary itself. Installing it makes `dotnet tool install`/`dotnet tool restore` also pull one matching
sub-package for your machine's RID — `Scarlet.Tailwind.Cli.win-x64`, `Scarlet.Tailwind.Cli.linux-arm64`,
and so on — and *that* package embeds the actual Tailwind binary. This is automatic; you never name a
sub-package yourself, and running `dotnet add package Scarlet.Tailwind.Cli.<rid>` on one directly installs
nothing usable — it carries no library assets, only a tool payload NuGet places when
`Scarlet.Tailwind.Cli` asks for it.

**Supported platforms are Windows, Linux and macOS on x64 or arm64**, including musl-based Linux
distributions such as Alpine — one sub-package per combination, eight in total. Windows on ARM64 gets the
x64 binary, which it runs under emulation, because Tailwind publishes no native ARM64 build for Windows.
Hosts outside that matrix restore `Scarlet.Tailwind.Cli.any` instead: a portable fallback with no embedded
binary, so it downloads Tailwind on first use and caches it per user rather than shipping a mismatched one.
Any other architecture gets an explanatory error rather than a mismatched binary — download the standalone
CLI yourself and point at it with `SCARLET_TAILWIND_PATH` if you need one.

Resolution order:

1. `SCARLET_TAILWIND_PATH`, if set — errors if it points at nothing, rather than quietly falling back
2. the Tailwind embedded in the installed package
3. a previously downloaded Tailwind in the per-user cache
4. a download

To see what it chose and why:

```bash
dotnet tailwind --scarlet-info
dotnet tailwind --scarlet-info --json
```

That is the only argument the tool reserves for itself, it is recognised only as the *first* argument, and
`SCARLET_TAILWIND_PASSTHROUGH=1` disables even that. It never downloads anything — it reports the URL it
*would* use.

## Configuration

| Variable | Effect |
|----------|--------|
| `SCARLET_TAILWIND_PATH` | Use this Tailwind executable. Highest precedence. |
| `SCARLET_TAILWIND_VERSION` | Resolve a different Tailwind version, or `latest`. Bypasses the embedded binary. |
| `SCARLET_TAILWIND_CACHE` | Override the download cache root. |
| `SCARLET_TAILWIND_NO_EMBEDDED` | Ignore the embedded binary. |
| `SCARLET_TAILWIND_DIAGNOSTICS` | Print the resolved Tailwind path to stderr before running. |
| `SCARLET_TAILWIND_PASSTHROUGH` | Disable `--scarlet-info` so every argument reaches Tailwind. |
| `SCARLET_TAILWIND_DOWNLOAD_TIMEOUT` | Seconds to wait for a concurrent download. Defaults to 300. |

Configuration is environment variables rather than command-line flags on purpose: every argument belongs
to Tailwind, so a flag Tailwind adds in future keeps working without a release of this package.

`SCARLET_TAILWIND_VERSION` changes which **Tailwind binary** gets downloaded and run — it never changes
which **NuGet package** is installed; that's decided once, at `dotnet tool install` time (see
[How it finds Tailwind](#how-it-finds-tailwind)). Setting it to anything other than the version baked into
the installed package skips the embedded binary and downloads the requested one into the per-user cache,
scoped by version (`<cache>/runtimes/<version>/`), so later runs with the same value reuse it instead of
re-downloading.

`latest` is re-resolved against GitHub on every run. The cache keeps the resolved version in a marker: when
GitHub still points at that version the existing binary is reused, and when the release moves the binary
and marker are replaced.

## Running Tailwind during a build instead

If you want Tailwind to run as part of `dotnet build`, before ASP.NET Core, Blazor and Razor Class Library
static web assets are discovered, use
[`Scarlet.Tailwind.MSBuild`](https://www.nuget.org/packages/Scarlet.Tailwind.MSBuild/) instead. The two are
independent; this tool is for the command line.

`--watch` is the one thing this tool does that the MSBuild task deliberately will not: a watch never exits,
so a build that started one would hang instead of finishing. Run it here, in its own terminal, alongside
`dotnet watch`.

## Links

- [Source and full documentation](https://github.com/ScarletKuro/Scarlet.Tailwind)
- [Tailwind CSS documentation](https://tailwindcss.com/docs)

Licensed under MIT. Tailwind CSS itself is licensed separately; see `LICENSE-3RD-PARTY.txt` in the package.
