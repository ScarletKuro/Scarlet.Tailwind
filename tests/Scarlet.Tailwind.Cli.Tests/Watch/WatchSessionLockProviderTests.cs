using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Scarlet.Tailwind.Cli.Watch;

namespace Scarlet.Tailwind.Cli.Tests.Watch;

public class WatchSessionLockProviderTests
{
    [Fact]
    public void TryAcquire_WhenAnOutputIsAlreadyLocked_ShouldRejectTheOverlappingSession()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "scarlet-tailwind-lock-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var provider = new WatchSessionLockProvider(cacheRoot);
            var firstInvocation = CreateInvocation("app.css");
            var overlappingInvocation = CreateInvocation("app.css");

            var acquiredFirst = provider.TryAcquire([firstInvocation], out var first);
            using var firstLease = first;
            var acquiredOverlapping = provider.TryAcquire([overlappingInvocation], out var overlapping);

            Assert.True(acquiredFirst);
            Assert.NotNull(firstLease);
            Assert.False(acquiredOverlapping);
            Assert.Null(overlapping);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryAcquire_WhenLockFileAlreadyExistsButIsUnlocked_ShouldAllowAnotherSession()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "scarlet-tailwind-lock-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var provider = new WatchSessionLockProvider(cacheRoot);
            var invocation = CreateInvocation("app.css");

            var acquiredFirst = provider.TryAcquire([invocation], out var first);
            Assert.True(acquiredFirst);
            first!.Dispose();
            Assert.Single(Directory.GetFiles(Path.Combine(cacheRoot, "watch"), "*.lock"));

            var acquiredNext = provider.TryAcquire([invocation], out var next);
            using var nextLease = next;
            Assert.True(acquiredNext);
            Assert.NotNull(nextLease);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryAcquire_WhenProjectsUseTheSameRelativeOutput_ShouldAllowBothSessions()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "scarlet-tailwind-lock-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var provider = new WatchSessionLockProvider(cacheRoot);
            var firstProject = Path.Combine(Path.GetTempPath(), "project-a");
            var secondProject = Path.Combine(Path.GetTempPath(), "project-b");
            var firstInvocation = CreateInvocation(firstProject, Path.Combine("wwwroot", "css", "app.css"));
            var secondInvocation = CreateInvocation(secondProject, Path.Combine("wwwroot", "css", "app.css"));

            var acquiredFirst = provider.TryAcquire([firstInvocation], out var first);
            using var firstLease = first;
            var acquiredSecond = provider.TryAcquire([secondInvocation], out var second);
            using var secondLease = second;

            Assert.True(acquiredFirst);
            Assert.NotNull(firstLease);
            Assert.True(acquiredSecond);
            Assert.NotNull(secondLease);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryAcquire_AfterLockOwnerIsForciblyTerminated_ShouldAllowAnotherSession()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "scarlet-tailwind-lock-tests", Guid.NewGuid().ToString("N"));
        Process? process = null;

        try
        {
            var provider = new WatchSessionLockProvider(cacheRoot);
            var invocation = CreateInvocation("app.css");
            var output = WatchSessionLockProvider.NormalizePath(
                invocation.GeneratedPaths[0],
                invocation.WorkingDirectory,
                OperatingSystem.IsWindows());
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(output)));
            var lockPath = Path.Combine(cacheRoot, "watch", hash + ".lock");
            var readyPath = Path.Combine(cacheRoot, "lock-ready");
            var probePath = Path.Combine(AppContext.BaseDirectory, "ProcessProbe", "tailwindcss.dll");
            Assert.True(File.Exists(probePath), $"Process probe was not copied to '{probePath}'.");

            var startInfo = new ProcessStartInfo(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(probePath);
            startInfo.ArgumentList.Add("--hold-lock");
            startInfo.ArgumentList.Add(lockPath);
            startInfo.ArgumentList.Add(readyPath);
            process = Process.Start(startInfo);
            Assert.NotNull(process);
            Assert.True(SpinWait.SpinUntil(() => File.Exists(readyPath), TimeSpan.FromSeconds(10)),
                "The process probe did not acquire the lock in time.");

            var acquiredWhileOwnerIsRunning = provider.TryAcquire([invocation], out var blockedLease);
            Assert.False(acquiredWhileOwnerIsRunning);
            Assert.Null(blockedLease);

            process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(10_000), "The process probe did not exit after it was killed.");

            var acquiredAfterOwnerExited = provider.TryAcquire([invocation], out var next);
            using var nextLease = next;
            Assert.True(acquiredAfterOwnerExited);
            Assert.NotNull(nextLease);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            process?.Dispose();

            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryAcquire_WhenOnlyAnAdditionalGeneratedFileOverlaps_ShouldRejectTheSession()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "scarlet-tailwind-lock-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var provider = new WatchSessionLockProvider(cacheRoot);
            var sharedMap = Path.Combine(Path.GetTempPath(), "app", "wwwroot", "css", "shared.css.map");
            var first = CreateInvocation("first.css");
            var second = CreateInvocation("second.css");
            first = first with { GeneratedPaths = [first.OutputPath, sharedMap] };
            second = second with { GeneratedPaths = [second.OutputPath, sharedMap] };

            var acquiredFirst = provider.TryAcquire([first], out var firstLock);
            using var firstLease = firstLock;
            var acquiredOverlapping = provider.TryAcquire([second], out var overlappingLock);

            Assert.True(acquiredFirst);
            Assert.NotNull(firstLease);
            Assert.False(acquiredOverlapping);
            Assert.Null(overlappingLock);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryAcquire_WhenPathNormalizationFails_ShouldWrapTheFailureAndLeaveNoLease()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "scarlet-tailwind-lock-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var provider = new WatchSessionLockProvider(cacheRoot);
            var invocation = CreateInvocation("app.css") with
            {
                WorkingDirectory = "relative",
                GeneratedPaths = ["app.css"]
            };
            using var staleLease = new MemoryStream();
            IDisposable? lease = staleLease;

            var exception = Assert.Throws<TailwindWatchException>(
                () => provider.TryAcquire([invocation], out lease));

            Assert.Contains("could not create watch-session locks", exception.Message);
            Assert.Null(lease);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizePath_ShouldApplyTheRequestedPlatformCasing(bool isWindows)
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "MixedCaseProject");
        var relativePath = Path.Combine("Styles", "App.css");
        var fullPath = Path.GetFullPath(relativePath, workingDirectory);

        var normalized = WatchSessionLockProvider.NormalizePath(relativePath, workingDirectory, isWindows);

        Assert.Equal(isWindows ? fullPath.ToUpperInvariant() : fullPath, normalized);
    }

    private static TailwindWatchInvocation CreateInvocation(string outputName)
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), "app");
        return CreateInvocation(projectDirectory, Path.Combine("wwwroot", "css", outputName));
    }

    private static TailwindWatchInvocation CreateInvocation(string projectDirectory, string outputPath)
    {
        outputPath = Path.GetFullPath(outputPath, projectDirectory);

        return new TailwindWatchInvocation(
            new TailwindLaunchRequest("tailwindcss", ["--watch=always"]),
            projectDirectory,
            Path.Combine(projectDirectory, "Styles", "app.css"),
            outputPath,
            [outputPath]);
    }
}
