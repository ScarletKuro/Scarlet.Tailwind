using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Build.Framework;
using Scarlet.Tailwind.Core;

namespace Scarlet.Tailwind.MSBuild;

/// <summary>
/// Builds <see cref="TailwindRuntimePack"/> instances from MSBuild items.
/// </summary>
/// <remarks>
/// This is the only piece of the pack contract that knows about MSBuild, which is why it lives here rather
/// than beside <see cref="TailwindRuntimePack"/> in Scarlet.Tailwind.Core - the CLI shares the pack type but has no
/// <see cref="ITaskItem"/> to translate.
/// </remarks>
public static class TailwindRuntimePackFactory
{
    /// <summary>
    /// Builds the list of packs described by the given MSBuild items, dropping malformed and duplicate entries.
    /// </summary>
    /// <param name="items">The <c>TailwindRuntimePack</c> items. The sequence, and any entry in it, may be <see langword="null"/>.</param>
    /// <param name="onInvalidItem">Invoked with a human readable reason for every item that had to be dropped.</param>
    /// <returns>The valid, de-duplicated packs in declaration order.</returns>
    public static IReadOnlyList<TailwindRuntimePack> FromTaskItems(IEnumerable<ITaskItem?>? items, Action<string>? onInvalidItem = null)
    {
        if (items is null)
        {
            return Array.Empty<TailwindRuntimePack>();
        }

        var packs = new List<TailwindRuntimePack>();

        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var id = item.ItemSpec?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                onInvalidItem?.Invoke($"A {TailwindRuntimePack.ItemName} item without an identity was ignored.");
                continue;
            }

            var rid = item.GetMetadata(TailwindRuntimePack.RidMetadataName)?.Trim();
            if (string.IsNullOrEmpty(rid))
            {
                onInvalidItem?.Invoke($"{TailwindRuntimePack.ItemName} \"{id}\" was ignored because it does not set the \"{TailwindRuntimePack.RidMetadataName}\" metadata.");
                continue;
            }

            var runtimesPath = item.GetMetadata(TailwindRuntimePack.RuntimesPathMetadataName)?.Trim();
            if (string.IsNullOrEmpty(runtimesPath))
            {
                onInvalidItem?.Invoke($"{TailwindRuntimePack.ItemName} \"{id}\" was ignored because it does not set the \"{TailwindRuntimePack.RuntimesPathMetadataName}\" metadata.");
                continue;
            }

            var variant = item.GetMetadata(TailwindRuntimePack.VariantMetadataName);
            var priorityText = item.GetMetadata(TailwindRuntimePack.PriorityMetadataName)?.Trim();
            var priority = 0;

            if (!string.IsNullOrEmpty(priorityText)
                && !int.TryParse(priorityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out priority))
            {
                onInvalidItem?.Invoke($"{TailwindRuntimePack.ItemName} \"{id}\" has a non-numeric \"{TailwindRuntimePack.PriorityMetadataName}\" metadata (\"{priorityText}\"); 0 was used instead.");
                priority = 0;
            }

            // Optional, and empty for every pack but the emulated win-arm64 one; the pack type falls back to
            // Rid on its own, so nothing needs a default here.
            var nativeRid = item.GetMetadata(TailwindRuntimePack.NativeRidMetadataName);

            packs.Add(new TailwindRuntimePack(id!, rid!, runtimesPath!, variant, priority, nativeRid));
        }

        return TailwindRuntimePack.Deduplicate(packs);
    }
}
