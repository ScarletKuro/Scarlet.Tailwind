using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// The few libc calls needed to forward signals to Tailwind correctly.
/// </summary>
/// <remarks>
/// Uses <c>LibraryImport</c> rather than <c>DllImport</c>: the marshalling stubs are source-generated at
/// compile time instead of emitted by the runtime, which is both faster to start and AOT-safe should the
/// tool ever be published with PublishAot. <c>UnixChmodProvider</c> in Scarlet.Tailwind.Core still uses
/// <c>DllImport</c> because it targets netstandard2.0, where neither the attribute nor the generator exists.
/// </remarks>
[ExcludeFromCodeCoverage]
internal static partial class PosixInterop
{
    /// <summary>Hangup.</summary>
    public const int SIGHUP = 1;

    /// <summary>Interrupt, what Ctrl+C sends.</summary>
    public const int SIGINT = 2;

    /// <summary>Termination request.</summary>
    public const int SIGTERM = 15;

    /// <summary>
    /// Sends a signal to a process, ignoring any failure.
    /// </summary>
    /// <param name="processId">The target process.</param>
    /// <param name="signal">The signal number.</param>
    public static void Send(int processId, int signal)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = kill(processId, signal);
        }
        catch (Exception)
        {
            // The child may have exited between the check and the call; nothing useful to do either way.
        }
    }

    /// <summary>
    /// Whether the child is in this process's group, meaning the terminal already delivers Ctrl+C to it.
    /// </summary>
    /// <param name="processId">The child process id.</param>
    /// <returns><see langword="true"/> when a second, explicit signal would be a duplicate.</returns>
    /// <remarks>
    /// .NET does not call <c>setsid</c> when starting a child, so it normally inherits our process group and
    /// receives terminal signals directly. Checking rather than assuming means the behaviour stays correct
    /// if that ever changes: if the groups differ, the caller forwards the signal explicitly instead.
    /// </remarks>
    public static bool SharesProcessGroup(int processId)
    {
        if (OperatingSystem.IsWindows())
        {
            // A console child shares the console control group, so Ctrl+C reaches it without our help.
            return true;
        }

        try
        {
            var childGroup = getpgid(processId);
            var ownGroup = getpgid(0);

            return childGroup != -1 && childGroup == ownGroup;
        }
        catch (Exception)
        {
            // Assume shared: sending a duplicate SIGINT is worse than relying on the terminal.
            return true;
        }
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int sig);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int getpgid(int pid);
}
