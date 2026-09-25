namespace Scarlet.Tailwind.Core;

/// <summary>
/// Per-platform names: the .NET runtime identifier, the release asset to download, the runtime package
/// that carries it, and the executable's name on disk.
/// </summary>
/// <remarks>
/// There is deliberately no directory name here. Tailwind publishes bare executables rather than archives,
/// so nothing is ever extracted and there is no folder inside a download to name - the layout is simply
/// <c>&lt;rid&gt;/native/&lt;executable&gt;</c>.
/// </remarks>
public sealed class PlatformInfo
{
    /// <summary>The .NET runtime identifier this platform corresponds to.</summary>
    public string Rid { get; }

    /// <summary>
    /// The release asset filename, exactly as Tailwind publishes it.
    /// </summary>
    /// <remarks>
    /// Tailwind's own vocabulary, not .NET's: it says <c>macos</c> where a RID says <c>osx</c>, and
    /// <c>windows</c> where a RID says <c>win</c>. The two are kept distinct on purpose, because this value
    /// is concatenated into a download URL and must match upstream exactly.
    /// </remarks>
    public string DownloadName { get; }

    /// <summary>The runtime package that ships the executable for this platform.</summary>
    public string PackageName { get; }

    /// <summary>The executable's filename on disk.</summary>
    public string ExecutableName { get; }

    /// <summary>
    /// The runtime identifier a runtime package stores this platform's executable under, which is
    /// <see cref="Rid"/> unless upstream publishes no build for this platform and another one's binary
    /// serves it.
    /// </summary>
    /// <remarks>
    /// The counterpart of the <c>NativeRid</c> metadata on a runtime pack, and it exists for the same single
    /// case: Windows ARM64 runs the x64 build under emulation, so
    /// <see cref="TailwindRuntimeResolver.GetRuntimePackageName"/> returns the <c>windows-x64</c> package and
    /// that package stages its binary under <c>win-x64</c>.
    ///
    /// Deliberately data rather than a branch that names the platform. Nothing in the resolver asks whether
    /// it is running on Windows ARM64; it asks whether this platform stores its executable somewhere other
    /// than under its own identifier, and every other platform answers no.
    /// </remarks>
    public string NativeRid { get; }

    public PlatformInfo(
        string rid,
        string downloadName,
        string packageName,
        string executableName,
        string? nativeRid = null)
    {
        Rid = rid;
        DownloadName = downloadName;
        PackageName = packageName;
        ExecutableName = executableName;
        NativeRid = string.IsNullOrWhiteSpace(nativeRid) ? rid : nativeRid!;
    }
}
