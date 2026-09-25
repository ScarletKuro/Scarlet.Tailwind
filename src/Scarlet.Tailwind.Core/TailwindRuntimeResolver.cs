using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Scarlet.Tailwind.Core.Providers;

namespace Scarlet.Tailwind.Core;

/// <summary>
/// Helper class for detecting and resolving Tailwind runtime paths.
/// </summary>
public static class TailwindRuntimeResolver
{
    private static readonly IReadOnlyDictionary<Platform, PlatformInfo> PlatformMap =
        new Dictionary<Platform, PlatformInfo>
        {
            [Platform.WindowsX64] = new(
                rid: "win-x64",
                downloadName: "tailwindcss-windows-x64.exe",
                packageName: "Scarlet.Tailwind.Runtime.windows-x64",
                executableName: "tailwindcss.exe"
            ),
            // Tailwind publishes no ARM64 build for Windows, and Windows 11 on ARM emulates x64. This entry
            // therefore points at the x64 asset and the x64 package while keeping its own truthful Rid, so
            // diagnostics never claim the host is something it is not. Pack selection handles the rest: the
            // windows-x64 package emits a second, lower-priority item carrying Rid win-arm64.
            [Platform.WindowsArm64] = new(
                rid: "win-arm64",
                downloadName: "tailwindcss-windows-x64.exe",
                packageName: "Scarlet.Tailwind.Runtime.windows-x64",
                executableName: "tailwindcss.exe",
                // That package stages under win-x64, so a directory it populated has no win-arm64 folder.
                // The downloader, by contrast, writes under win-arm64. Both layouts have to resolve.
                nativeRid: "win-x64"
            ),
            [Platform.LinuxX64] = new(
                rid: "linux-x64",
                downloadName: "tailwindcss-linux-x64",
                packageName: "Scarlet.Tailwind.Runtime.linux-x64",
                executableName: "tailwindcss"
            ),
            [Platform.LinuxArm64] = new(
                rid: "linux-arm64",
                downloadName: "tailwindcss-linux-arm64",
                packageName: "Scarlet.Tailwind.Runtime.linux-arm64",
                executableName: "tailwindcss"
            ),
            [Platform.MacOsX64] = new(
                rid: "osx-x64",
                downloadName: "tailwindcss-macos-x64",
                packageName: "Scarlet.Tailwind.Runtime.darwin-x64",
                executableName: "tailwindcss"
            ),
            [Platform.MacOsArm64] = new(
                rid: "osx-arm64",
                downloadName: "tailwindcss-macos-arm64",
                packageName: "Scarlet.Tailwind.Runtime.darwin-arm64",
                executableName: "tailwindcss"
            ),
            [Platform.LinuxMuslX64] = new(
                rid: "linux-musl-x64",
                downloadName: "tailwindcss-linux-x64-musl",
                packageName: "Scarlet.Tailwind.Runtime.linux-x64-musl",
                executableName: "tailwindcss"
            ),
            [Platform.LinuxMuslArm64] = new(
                rid: "linux-musl-arm64",
                downloadName: "tailwindcss-linux-arm64-musl",
                packageName: "Scarlet.Tailwind.Runtime.linux-arm64-musl",
                executableName: "tailwindcss"
            )
        };


    /// <summary>
    /// Gets the current platform.
    /// </summary>
    public static Platform GetCurrentPlatform()
    {
        var osPlatform = DetectOSPlatform(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX));

        return GetPlatform(
            osPlatform,
            RuntimeInformation.ProcessArchitecture,
            IsMuslLibc(),
            RuntimeInformation.OSDescription);
    }

    /// <summary>
    /// Maps the three OS checks .NET exposes to an <see cref="OSPlatform"/>.
    /// </summary>
    /// <returns><see langword="default"/> when none of the three match.</returns>
    /// <remarks>
    /// Split out from <see cref="GetCurrentPlatform"/> so the "none of the three matched" case can be
    /// tested: no real CI host can produce it, since every runner is Windows, Linux, or macOS.
    /// </remarks>
    internal static OSPlatform DetectOSPlatform(bool isWindows, bool isLinux, bool isOSX)
    {
        return isWindows ? OSPlatform.Windows
            : isLinux ? OSPlatform.Linux
            : isOSX ? OSPlatform.OSX
            : default;
    }

    /// <summary>
    /// Maps a host description to the Tailwind build that serves it.
    /// </summary>
    /// <param name="osPlatform">The host operating system.</param>
    /// <param name="architecture">The process architecture.</param>
    /// <param name="isMuslLibc">Whether the host uses musl rather than glibc.</param>
    /// <param name="osDescription">Description of the host, used only in error messages.</param>
    /// <returns>The platform to resolve a Tailwind build for.</returns>
    /// <exception cref="PlatformNotSupportedException">No Tailwind build covers this host.</exception>
    /// <remarks>
    /// Split out from <see cref="GetCurrentPlatform"/> so the unsupported combinations can be tested: they
    /// are the ones nobody can reproduce on a normal development machine, and they used to fail silently.
    /// </remarks>
    internal static Platform GetPlatform(
        OSPlatform osPlatform,
        Architecture architecture,
        bool isMuslLibc,
        string osDescription)
    {
        // x86 is folded into x64 rather than rejected. Tailwind ships no 32-bit build, but a 32-bit *host
        // process* on a 64-bit OS - an older MSBuild, for instance - can happily start the x64 binary,
        // and that has always worked.
        var isX64 = architecture is Architecture.X64 or Architecture.X86;
        var isArm64 = architecture == Architecture.Arm64;

        if (!isX64 && !isArm64)
        {
            throw new PlatformNotSupportedException(
                $"Tailwind does not publish a build for {architecture} ({osDescription}). "
                + "Supported architectures are x64 and arm64.");
        }

        if (osPlatform == OSPlatform.Windows)
        {
            return isArm64 ? Platform.WindowsArm64 : Platform.WindowsX64;
        }

        if (osPlatform == OSPlatform.Linux)
        {
            if (isMuslLibc)
            {
                return isArm64 ? Platform.LinuxMuslArm64 : Platform.LinuxMuslX64;
            }

            return isArm64 ? Platform.LinuxArm64 : Platform.LinuxX64;
        }

        if (osPlatform == OSPlatform.OSX)
        {
            return isArm64 ? Platform.MacOsArm64 : Platform.MacOsX64;
        }

        throw new PlatformNotSupportedException($"Unsupported platform: {osDescription}");
    }

    /// <summary>
    /// Directories the musl dynamic loader has been observed in, across the distributions that ship it.
    /// </summary>
    /// <remarks>
    /// Alpine (the only musl distribution covered by CI) always uses <c>/lib</c>. Other musl distros, such
    /// as Void Linux, install it under <c>/usr/lib</c> or <c>/lib64</c> instead; those are only a
    /// best-effort widening, not something a test host can verify, since no CI runner uses them.
    /// </remarks>
    private static readonly string[] MuslLoaderDirectories = { "/lib", "/lib64", "/usr/lib" };

    /// <summary>
    /// Detects a musl-based Linux distribution, such as Alpine.
    /// </summary>
    /// <returns><see langword="true"/> when the musl dynamic loader is present.</returns>
    /// <remarks>
    /// Probing for the loader keeps this working on netstandard2.0, where
    /// <c>RuntimeInformation.RuntimeIdentifier</c> is unavailable. A distro whose loader lives somewhere
    /// none of <see cref="MuslLoaderDirectories"/> covers falls through to the glibc build, which then
    /// fails to start with an ELF interpreter error rather than a clear "unsupported platform" message.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static bool IsMuslLibc()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return false;
        }

        try
        {
            foreach (var directory in MuslLoaderDirectories)
            {
                if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, "ld-musl-*.so.1").Any())
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            // An unreadable directory is not a reason to fail; assume glibc and let the binary speak for itself.
            return false;
        }
    }

    /// <summary>
    /// Gets the runtime identifier (RID) for the specified platform.
    /// </summary>
    public static string GetRuntimeIdentifier(Platform platform) => GetInfo(platform).Rid;

    /// <summary>
    /// Gets the Tailwind executable name for the specified platform.
    /// </summary>
    public static string GetExecutableName(Platform platform) => GetInfo(platform).ExecutableName;

    /// <summary>
    /// Gets the runtime package name for the specified platform.
    /// </summary>
    public static string GetRuntimePackageName(Platform platform) => GetInfo(platform).PackageName;

    /// <summary>
    /// Gets the GitHub release download archive name for the specified platform.
    /// </summary>
    public static string GetDownloadName(Platform platform) => GetInfo(platform).DownloadName;

    /// <summary>
    /// Gets the full path the Tailwind executable is expected at inside a runtimes directory.
    /// </summary>
    /// <param name="runtimesPath">Directory containing <c>&lt;rid&gt;/native/&lt;executable&gt;</c>.</param>
    /// <param name="platform">The platform to build the path for.</param>
    /// <returns>The full path to the Tailwind executable. The file is not required to exist.</returns>
    public static string GetExecutablePath(string runtimesPath, Platform platform)
    {
        var info = GetInfo(platform);

        return GetExecutablePath(runtimesPath, info.Rid, info.ExecutableName);
    }

    /// <summary>
    /// Builds the <c>&lt;runtimesPath&gt;/&lt;rid&gt;/native/&lt;executable&gt;</c> path for an explicit RID
    /// subdirectory.
    /// </summary>
    /// <remarks>
    /// Separate from the <see cref="Platform"/> overload because a pack does not have to store its executable
    /// under the RID it serves - see <see cref="TailwindRuntimePack.NativeRid"/>.
    /// </remarks>
    internal static string GetExecutablePath(string runtimesPath, string rid, string executableName)
    {
        return Path.GetFullPath(Path.Combine(runtimesPath, rid, "native", executableName));
    }

    /// <summary>
    /// Selects the runtime packs that can serve the given platform, best candidate first.
    /// </summary>
    /// <param name="packs">All packs contributed to the build. May be <see langword="null"/>.</param>
    /// <param name="platform">The platform that has to be served.</param>
    /// <returns>
    /// The matching packs ordered by descending <see cref="TailwindRuntimePack.Priority"/>, then by pack id and path so
    /// that the outcome does not depend on the order NuGet happened to import the runtime packages in.
    /// </returns>
    public static IReadOnlyList<TailwindRuntimePack> SelectPacks(IEnumerable<TailwindRuntimePack>? packs, Platform platform)
    {
        if (packs is null)
        {
            return Array.Empty<TailwindRuntimePack>();
        }

        var rid = GetRuntimeIdentifier(platform);
        var matches = new List<TailwindRuntimePack>();

        foreach (var pack in packs)
        {
            if (string.Equals(pack.Rid, rid, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(pack);
            }
        }

        matches.Sort(static (left, right) =>
        {
            var byPriority = right.Priority.CompareTo(left.Priority);
            if (byPriority != 0)
            {
                return byPriority;
            }

            var byId = string.CompareOrdinal(left.Id, right.Id);

            return byId != 0 ? byId : string.CompareOrdinal(left.RuntimesPath, right.RuntimesPath);
        });

        return matches;
    }

    /// <summary>
    /// Resolves the full path to the Tailwind executable.
    /// </summary>
    /// <param name="fileSystem">File system abstraction.</param>
    /// <param name="chmodProvider">Provider for setting executable permissions.</param>
    /// <param name="platform">Target platform.</param>
    /// <param name="runtimeDirectory">Optional explicit runtime directory. When set, it wins over <paramref name="runtimePacks"/>.</param>
    /// <param name="runtimePacks">Runtime packs contributed by the referenced runtime packages.</param>
    /// <param name="log">Optional sink for diagnostic messages about the selection.</param>
    /// <returns>Full path to the Tailwind executable.</returns>
    /// <exception cref="FileNotFoundException">No usable Tailwind executable could be found.</exception>
    public static string ResolveTailwindExecutable(
        IFileSystem fileSystem,
        IChmodProvider chmodProvider,
        Platform platform,
        string? runtimeDirectory = null,
        IReadOnlyList<TailwindRuntimePack>? runtimePacks = null,
        Action<string>? log = null)
    {
        // An explicit directory is a deliberate override, so it is never second-guessed against the packs.
        if (!string.IsNullOrEmpty(runtimeDirectory))
        {
            return ResolveFromDirectory(fileSystem, chmodProvider, platform, runtimeDirectory!);
        }

        var candidates = SelectPacks(runtimePacks, platform);
        var searched = new List<string>();

        foreach (var candidate in candidates)
        {
            // NativeRid, not the host's RID: a pack may serve a RID it does not store under.
            var candidatePath = GetExecutablePath(candidate.RuntimesPath, candidate.NativeRid, GetExecutableName(platform));

            if (fileSystem.File.Exists(candidatePath))
            {
                if (candidates.Count > 1)
                {
                    log?.Invoke($"Selected Tailwind runtime pack {candidate} out of {candidates.Count} candidates for {GetRuntimeIdentifier(platform)}.");
                }
                else
                {
                    log?.Invoke($"Using Tailwind runtime pack {candidate}.");
                }

                chmodProvider.EnsureExecutablePermissions(candidatePath);

                return candidatePath;
            }

            searched.Add(candidatePath);
        }

        throw new FileNotFoundException(candidates.Count > 0
            ? BuildIncompletePackMessage(platform, candidates, searched)
            : BuildMissingPackMessage(platform, runtimePacks));
    }

    /// <summary>
    /// Resolves the Tailwind executable inside an explicitly configured runtimes directory.
    /// </summary>
    /// <remarks>
    /// Probes <see cref="PlatformInfo.Rid"/> first and then <see cref="PlatformInfo.NativeRid"/>, which
    /// differ only for a platform whose executable is stored under another platform's identifier. Both are
    /// needed because a directory can be populated two ways that disagree: the downloader writes under the
    /// host's own RID, while a runtime package stages under the RID it ships for. On Windows ARM64 those are
    /// <c>win-arm64</c> and <c>win-x64</c> respectively, so probing only one of them finds Tailwind in one
    /// case and not the other.
    ///
    /// This is the same fallback the pack contract expresses through its <c>NativeRid</c> metadata. An
    /// explicit directory bypasses pack selection entirely, so without it the emulated platform works when
    /// discovered through packs and fails when pointed at directly - which is exactly what the samples and
    /// integration tests do.
    /// </remarks>
    private static string ResolveFromDirectory(
        IFileSystem fileSystem,
        IChmodProvider chmodProvider,
        Platform platform,
        string runtimeDirectory)
    {
        var info = GetInfo(platform);
        var searched = new List<string>();

        foreach (var rid in info.NativeRid == info.Rid ? new[] { info.Rid } : new[] { info.Rid, info.NativeRid })
        {
            var candidatePath = GetExecutablePath(runtimeDirectory, rid, info.ExecutableName);

            if (fileSystem.File.Exists(candidatePath))
            {
                chmodProvider.EnsureExecutablePermissions(candidatePath);

                return candidatePath;
            }

            searched.Add(candidatePath);
        }

        throw new FileNotFoundException(
            $"Tailwind executable not found at: {string.Join("\n  ", searched)}\n\n" +
            $"TailwindRuntimeDirectory points at '{runtimeDirectory}', which does not contain a Tailwind build for {info.Rid}.\n" +
            $"Either clear that property and reference the {GetRuntimePackageName(platform)} package, or make sure the directory " +
            $"contains '{info.Rid}/native/{info.ExecutableName}'.");
    }

    /// <summary>
    /// Builds the error shown when no runtime pack targets the build host.
    /// </summary>
    private static string BuildMissingPackMessage(Platform platform, IEnumerable<TailwindRuntimePack>? allPacks)
    {
        var runtimePackageName = GetRuntimePackageName(platform);
        var rid = GetRuntimeIdentifier(platform);

        var message = new StringBuilder();
        message.Append("Tailwind runtime package not found.\n\n");
        message.Append($"No Tailwind runtime is available for this build host ({rid}).\n\n");
        message.Append("Add the matching runtime package to your project:\n");
        message.Append($"  <PackageReference Include=\"{runtimePackageName}\" Version=\"<tailwind-version>\" PrivateAssets=\"all\" />\n\n");
        message.Append("...or let the build download Tailwind on demand:\n");
        message.Append("  <PropertyGroup>\n");
        message.Append("    <TailwindRuntimeDownload>true</TailwindRuntimeDownload>\n");
        message.Append("    <TailwindRuntimeDirectory>$(MSBuildProjectDirectory)/runtimes</TailwindRuntimeDirectory>\n");
        message.Append("  </PropertyGroup>\n\n");
        message.Append(DescribeVisiblePacks(allPacks));

        return message.ToString();
    }

    /// <summary>
    /// Builds the error shown when a matching runtime pack is referenced but its binary is missing.
    /// </summary>
    private static string BuildIncompletePackMessage(
        Platform platform,
        IReadOnlyList<TailwindRuntimePack> candidates,
        IReadOnlyList<string> searched)
    {
        var message = new StringBuilder();
        message.Append($"Tailwind executable not found at: {searched[0]}\n\n");
        message.Append(candidates.Count == 1
            ? $"The runtime pack {candidates[0]} is referenced but its Tailwind executable is missing.\n"
            : $"{candidates.Count} runtime packs target {GetRuntimeIdentifier(platform)} but none of them contains a Tailwind executable.\n");
        message.Append("Try clearing the NuGet cache for the runtime package and rebuilding.\n\n");
        message.Append("Locations searched:\n");

        foreach (var path in searched)
        {
            message.Append($"  - {path}\n");
        }

        return message.ToString();
    }

    /// <summary>
    /// Renders the packs the build can see, which is the fastest way to spot a host/pack mismatch.
    /// </summary>
    private static string DescribeVisiblePacks(IEnumerable<TailwindRuntimePack>? allPacks)
    {
        var message = new StringBuilder("Runtime packs visible to this project:");
        var any = false;

        if (allPacks is not null)
        {
            foreach (var pack in allPacks)
            {
                message.Append($"\n  - {pack}");
                any = true;
            }
        }

        if (!any)
        {
            message.Append(" (none)");
        }

        return message.ToString();
    }

    private static PlatformInfo GetInfo(Platform platform)
    {
        return PlatformMap.TryGetValue(platform, out var info)
            ? info
            : throw new ArgumentException($"Unknown platform: {platform}", nameof(platform));
    }
}
