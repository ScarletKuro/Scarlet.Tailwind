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
    public void TryAcquire_AfterTheOwnerDisposes_ShouldAllowAnotherSession()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "scarlet-tailwind-lock-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var provider = new WatchSessionLockProvider(cacheRoot);
            var invocation = CreateInvocation("app.css");

            var acquiredFirst = provider.TryAcquire([invocation], out var first);
            Assert.True(acquiredFirst);
            first!.Dispose();

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
        var outputPath = Path.Combine(projectDirectory, "wwwroot", "css", outputName);

        return new TailwindWatchInvocation(
            new TailwindLaunchRequest("tailwindcss", ["--watch=always"]),
            projectDirectory,
            Path.Combine(projectDirectory, "Styles", "app.css"),
            outputPath,
            [outputPath]);
    }
}
