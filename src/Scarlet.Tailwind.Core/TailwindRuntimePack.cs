using System;
using System.Collections.Generic;
using System.IO;

namespace Scarlet.Tailwind.Core;

/// <summary>
/// A Tailwind runtime pack contributed to the build through the <c>TailwindRuntimePack</c> MSBuild item.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Scarlet.Tailwind.Runtime.*</c> package declares one of these in its <c>build/*.props</c>:
/// </para>
/// <code>
/// &lt;ItemGroup&gt;
///   &lt;TailwindRuntimePack Include="Scarlet.Tailwind.Runtime.darwin-aarch64"&gt;
///     &lt;Rid&gt;osx-arm64&lt;/Rid&gt;
///     &lt;RuntimesPath&gt;$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)..', 'runtimes'))&lt;/RuntimesPath&gt;
///     &lt;Variant&gt;baseline&lt;/Variant&gt;
///     &lt;Priority&gt;0&lt;/Priority&gt;
///   &lt;/TailwindRuntimePack&gt;
/// &lt;/ItemGroup&gt;
/// </code>
/// <para>
/// The contract is an item rather than one property per runtime identifier so that a new identifier - a musl
/// build, an emulated build, or a locally compiled Tailwind - can be added without any change to
/// <c>Scarlet.Tailwind.MSBuild</c> itself. A property set can hold only one path per RID, which makes two
/// packages serving the same RID silently fight over it, last import winning; items are additive and settle
/// that case through <see cref="Priority"/>. It is not a hypothetical here - the <c>windows-x64</c> package
/// deliberately contributes a second, lower-priority pack for <c>win-arm64</c>.
/// </para>
/// </remarks>
public sealed class TailwindRuntimePack
{
    /// <summary>Name of the MSBuild item that carries runtime packs.</summary>
    public const string ItemName = "TailwindRuntimePack";

    /// <summary>Metadata holding the .NET runtime identifier the pack provides (for example <c>osx-arm64</c>). Required.</summary>
    public const string RidMetadataName = "Rid";

    /// <summary>Metadata holding the directory that contains <c>&lt;rid&gt;/native/&lt;executable&gt;</c>. Required.</summary>
    public const string RuntimesPathMetadataName = "RuntimesPath";

    /// <summary>Metadata holding the Tailwind build variant (for example <c>baseline</c>). Informational only.</summary>
    public const string VariantMetadataName = "Variant";

    /// <summary>Metadata holding the selection priority. Higher wins, defaults to <c>0</c>.</summary>
    public const string PriorityMetadataName = "Priority";

    /// <summary>
    /// Metadata holding the RID subdirectory the executable is actually stored under, when that differs from
    /// <see cref="RidMetadataName"/>. Optional; defaults to the pack's RID.
    /// </summary>
    /// <remarks>
    /// Separates the RID a pack <em>serves</em> from the one it <em>stores under</em>. That is what lets a
    /// single package cover a host upstream publishes no binary for, without shipping the same ~110 MB binary
    /// twice under two directory names. The <c>windows-x64</c> package is the only user: its second item
    /// serves <c>win-arm64</c> and sets this to <c>win-x64</c>, because Windows on ARM runs the x64 build
    /// under emulation.
    /// </remarks>
    public const string NativeRidMetadataName = "NativeRid";

    /// <summary>
    /// Initializes a new instance of the <see cref="TailwindRuntimePack"/> class.
    /// </summary>
    /// <param name="id">Identifier of the pack, normally the runtime package id.</param>
    /// <param name="rid">The .NET runtime identifier the pack provides.</param>
    /// <param name="runtimesPath">Directory containing <c>&lt;rid&gt;/native/&lt;executable&gt;</c>.</param>
    /// <param name="variant">Optional Tailwind build variant, used for diagnostics only.</param>
    /// <param name="priority">Selection priority. Higher wins when several packs provide the same RID.</param>
    /// <param name="nativeRid">RID subdirectory the executable is stored under. Defaults to <paramref name="rid"/>.</param>
    public TailwindRuntimePack(
        string id,
        string rid,
        string runtimesPath,
        string? variant = null,
        int priority = 0,
        string? nativeRid = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Pack id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(rid))
        {
            throw new ArgumentException("Pack RID must not be empty.", nameof(rid));
        }

        if (string.IsNullOrWhiteSpace(runtimesPath))
        {
            throw new ArgumentException("Pack runtimes path must not be empty.", nameof(runtimesPath));
        }

        Id = id.Trim();
        Rid = rid.Trim();
        RuntimesPath = runtimesPath.Trim();
        Variant = string.IsNullOrWhiteSpace(variant) ? null : variant!.Trim();
        Priority = priority;
        NativeRid = string.IsNullOrWhiteSpace(nativeRid) ? Rid : nativeRid!.Trim();
    }

    /// <summary>Identifier of the pack, normally the runtime package id.</summary>
    public string Id { get; }

    /// <summary>The .NET runtime identifier the pack provides.</summary>
    public string Rid { get; }

    /// <summary>Directory containing <c>&lt;rid&gt;/native/&lt;executable&gt;</c>.</summary>
    public string RuntimesPath { get; }

    /// <summary>Tailwind build variant, used for diagnostics only.</summary>
    public string? Variant { get; }

    /// <summary>Selection priority. Higher wins when several packs provide the same RID.</summary>
    public int Priority { get; }

    /// <summary>
    /// RID subdirectory the executable is stored under, which equals <see cref="Rid"/> unless the pack serves
    /// a RID other than the one it ships for. See <see cref="NativeRidMetadataName"/>.
    /// </summary>
    public string NativeRid { get; }

    /// <summary>
    /// Removes packs that resolve to the same RID and directory.
    /// </summary>
    /// <remarks>
    /// The same pack can legitimately reach the task twice - items are additive, and a package's props can be
    /// imported from more than one build folder - so duplicates are expected rather than exceptional.
    /// </remarks>
    /// <param name="packs">The packs to de-duplicate.</param>
    /// <returns>The distinct packs, first occurrence wins.</returns>
    public static IReadOnlyList<TailwindRuntimePack> Deduplicate(IEnumerable<TailwindRuntimePack> packs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TailwindRuntimePack>();

        foreach (var pack in packs)
        {
            if (seen.Add($"{pack.Rid}|{pack.NativeRid}|{NormalizePath(pack.RuntimesPath)}"))
            {
                result.Add(pack);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a short description of the pack for build logs and error messages.
    /// </summary>
    /// <returns>For example <c>Scarlet.Tailwind.Runtime.darwin-aarch64 (osx-arm64, baseline)</c>.</returns>
    public override string ToString()
    {
        return Variant is null
            ? $"{Id} ({Rid})"
            : $"{Id} ({Rid}, {Variant})";
    }

    /// <summary>
    /// Canonicalizes a directory for comparison purposes, tolerating paths the OS cannot resolve.
    /// </summary>
    /// <param name="path">The path to canonicalize.</param>
    /// <returns>The full path without a trailing separator, or the trimmed input if it cannot be canonicalized.</returns>
    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            // An unresolvable path is still a usable key, it just cannot be compared structurally.
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
