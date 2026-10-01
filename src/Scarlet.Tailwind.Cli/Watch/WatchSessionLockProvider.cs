using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Scarlet.Tailwind.Cli.Watch;

/// <summary>
/// Holds OS-backed lifetime locks for every file a watch session can generate.
/// </summary>
internal sealed class WatchSessionLockProvider : IWatchSessionLockProvider
{
    private readonly string _cacheRoot;

    public WatchSessionLockProvider(string cacheRoot) => _cacheRoot = cacheRoot;

    public bool TryAcquire(IReadOnlyList<TailwindWatchInvocation> invocations, [NotNullWhen(true)] out IDisposable? lease)
    {
        lease = null;
        var streams = new List<FileStream>();

        try
        {
            var lockDirectory = Path.Combine(_cacheRoot, "watch");
            Directory.CreateDirectory(lockDirectory);

            var outputs = invocations
                .SelectMany(static invocation => invocation.GeneratedPaths.Select(
                    path => NormalizePath(path, invocation.WorkingDirectory, OperatingSystem.IsWindows())))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static path => path, StringComparer.Ordinal);

            foreach (var output in outputs)
            {
                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(output)));
                var lockPath = Path.Combine(lockDirectory, hash + ".lock");

                FileStream stream;
                try
                {
                    // FileShare.Read maps to a shared flock on Unix, so it does not exclude another watcher.
                    // The lock must be exclusive on every platform; metadata remains readable after release.
                    stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    DisposeAll(streams);
                    return false;
                }

                streams.Add(stream);
                stream.SetLength(0);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
                writer.WriteLine($"pid={Process.GetCurrentProcess().Id}");
                writer.WriteLine($"started={DateTimeOffset.UtcNow:O}");
                writer.WriteLine($"output={output}");
                writer.Flush();
                stream.Position = 0;
            }

            lease = new LockSet(streams);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or NotSupportedException
            or ArgumentException)
        {
            DisposeAll(streams);
            throw new TailwindWatchException(
                $"could not create watch-session locks under '{_cacheRoot}': {exception.Message}");
        }
    }

    internal static string NormalizePath(string path, string workingDirectory, bool isWindows)
    {
        var fullPath = Path.GetFullPath(path, workingDirectory);

        return isWindows
            ? fullPath.ToUpperInvariant()
            : fullPath;
    }

    private static void DisposeAll(IEnumerable<FileStream> streams)
    {
        foreach (var stream in streams.Reverse())
        {
            stream.Dispose();
        }
    }

    private sealed class LockSet(IReadOnlyList<FileStream> streams) : IDisposable
    {
        public void Dispose() => DisposeAll(streams);
    }
}
