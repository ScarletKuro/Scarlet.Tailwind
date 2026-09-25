using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;

namespace Scarlet.Tailwind.Core;

/// <summary>
/// Starts a <see cref="Process"/>, retrying a handful of times on Linux/macOS if the kernel reports the
/// executable as busy (ETXTBSY).
/// </summary>
/// <remarks>
/// Shared by <c>Scarlet.Tailwind.MSBuild.TailwindCompileTask</c> and <c>Scarlet.Tailwind.Cli.ProcessLauncher</c>:
/// both exec a Tailwind binary that may have just been downloaded, or just been run, moments earlier - a
/// project with two entry points compiles each in its own process against the same cached binary - and a
/// grandchild process the earlier run spawned can still hold the executable open even though that run has
/// already exited.
/// </remarks>
[ExcludeFromCodeCoverage]
public static class ProcessStartRetry
{
    /// <summary>
    /// Linux errno for "Text file busy", surfaced by <see cref="Win32Exception.NativeErrorCode"/> when
    /// <see cref="Process.Start()"/> fails on Unix.
    /// </summary>
    private const int TextFileBusyErrorCode = 26;

    private const int MaxAttempts = 5;

    private const int BaseDelayMilliseconds = 25;

    /// <summary>
    /// Starts <paramref name="process"/>, retrying on ETXTBSY as described in <see cref="ProcessStartRetry"/>.
    /// </summary>
    /// <param name="process">The process to start. Its <see cref="Process.StartInfo"/> must already be set.</param>
    /// <param name="log">Where retry attempts are reported.</param>
    public static void Start(Process process, ITailwindLogger log)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                process.Start();
                return;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == TextFileBusyErrorCode
                && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && attempt < MaxAttempts)
            {
                var delayMilliseconds = BaseDelayMilliseconds * (1 << (attempt - 1));
                log.LogMessage(
                    $"'{process.StartInfo.FileName}' was busy (ETXTBSY); retrying in {delayMilliseconds}ms (attempt {attempt}/{MaxAttempts}).");
                Thread.Sleep(delayMilliseconds);
            }
        }
    }
}
