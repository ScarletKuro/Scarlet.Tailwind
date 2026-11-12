using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Makes Windows terminate every process this tool starts when the tool itself terminates, however it does.
/// </summary>
/// <remarks>
/// <para>
/// Windows has no parent-death signal. A Ctrl+C or a closed console reaches Tailwind directly, but
/// <c>TerminateProcess</c> - Task Manager, <c>Stop-Process -Force</c>, <c>taskkill /F</c> without <c>/T</c>,
/// an IDE's stop button - ends this process without running any managed code, so
/// <see cref="ProcessSignalBridge"/> never gets the chance to stop Tailwind and it is orphaned. For
/// <c>dotnet tailwind watch</c> that is worse than a leak: the session lock dies with this process, so the next
/// session starts a second watcher beside the orphan, and if a setting changed in between the two overwrite
/// each other with different CSS.
/// </para>
/// <para>
/// The fix is a job object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> holding this process. Children
/// join their parent's job when they are created, so Tailwind is covered from its first instruction with no
/// window between starting it and assigning it, and so is anything Tailwind starts. The only handle to the job
/// is this process's, and the kernel closes it however the process ends, which terminates the whole job.
/// </para>
/// <para>
/// Call it immediately before starting Tailwind and not earlier: <c>dotnet tailwind watch</c> first runs
/// <c>dotnet msbuild</c>, whose worker nodes and build servers outlive it on purpose so other builds can
/// reuse them. Started from inside the job, they would be killed when this tool exits, possibly in the
/// middle of someone else's build.
/// </para>
/// <para>
/// Best effort: any failure leaves the previous behaviour in place rather than stopping Tailwind from running.
/// Nested jobs are supported since Windows 8, so this works even when an IDE or CI agent already put this
/// process in a job.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
internal static partial class WindowsJobObject
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    // GetCurrentProcess() returns this constant pseudo-handle; it needs no closing.
    private static readonly nint CurrentProcessPseudoHandle = -1;

    private static readonly Lock Gate = new();

    // Deliberately never closed: closing it is what kills the job, and that must happen only when this
    // process ends.
    private static nint s_job;

    /// <summary>
    /// Ensures processes started from now on are terminated together with this process.
    /// </summary>
    /// <returns><see langword="true"/> when this process is in a kill-on-close job.</returns>
    public static bool KillChildrenOnExit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        lock (Gate)
        {
            if (s_job != 0)
            {
                return true;
            }

            try
            {
                var job = CreateJobObjectW(0, null);
                if (job == 0)
                {
                    return false;
                }

                var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    }
                };

                if (!SetInformationJobObject(
                        job,
                        JobObjectExtendedLimitInformation,
                        ref limits,
                        (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>())
                    || !AssignProcessToJobObject(job, CurrentProcessPseudoHandle))
                {
                    CloseHandle(job);
                    return false;
                }

                s_job = job;
                return true;
            }
            catch (Exception)
            {
                // Missing export or marshalling failure: run Tailwind without the guarantee rather than not at all.
                return false;
            }
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateJobObjectW(nint jobAttributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        nint job,
        int jobObjectInformationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION jobObjectInformation,
        uint jobObjectInformationLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
