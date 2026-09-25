using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.IO.Abstractions;
using System.Security.Cryptography;
using RichardSzalay.MockHttp;
using Scarlet.Tailwind.MSBuild.Tests.Mock;

namespace Scarlet.Tailwind.MSBuild.Tests;

public class TailwindDownloaderTests
{
    private const string ChecksumsUrlLatest = "https://github.com/tailwindlabs/tailwindcss/releases/latest/download/sha256sums.txt";

    [Fact]
    public async Task DownloadRuntimeAsync_WithNullRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, TailwindRuntimeResolver.GetCurrentPlatform(), NoOpTailwindLogger.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRuntimeAsync(null!));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithEmptyRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, TailwindRuntimeResolver.GetCurrentPlatform(), NoOpTailwindLogger.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRuntimeAsync(string.Empty));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithWhitespaceRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, TailwindRuntimeResolver.GetCurrentPlatform(), NoOpTailwindLogger.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRuntimeAsync("   "));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithValidDirectory_ShouldReturnExecutablePath()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Tailwind executable
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
        Assert.Equal(
            new[] { mockFileSystem.Path.GetFullPath(result) },
            mockFileSystem.Directory.GetFiles(Path.Combine(tempDir, runtimeId, "native")));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithSpecificVersion_ShouldDownloadThatVersion()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Tailwind executable
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When($"https://github.com/tailwindlabs/tailwindcss/releases/download/v{version}/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir, version);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
    }

    [Fact]
    public async Task DownloadRuntimeAsync_CalledTwice_ShouldReuseExistingRuntime()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Track request count
        var requestCount = 0;

        // Create a mock zip file with the Tailwind executable
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When($"https://github.com/tailwindlabs/tailwindcss/releases/download/v{version}/tailwindcss-linux-x64")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(binaryContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act - first download
        var result1 = await downloader.DownloadRuntimeAsync(tempDir, version);

        // Verify file was created
        Assert.True(mockFileSystem.File.Exists(result1));

        // Act - second download (should reuse without downloading)
        var result2 = await downloader.DownloadRuntimeAsync(tempDir, version);

        // Assert
        Assert.Equal(result1, result2);

        // Verify HTTP was called only once (file was reused on second call)
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_ShouldKeepFinalExecutableHiddenUntilPublished()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var nativeDirectory = Path.Combine(tempDir, runtimeId, "native");
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var normalizedExpectedPath = mockFileSystem.Path.GetFullPath(expectedPath);
        var mockHttp = new MockHttpMessageHandler();
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        // chmod is the observation point now that there is no extraction step: it runs on the staged file,
        // inside PublishStagedExecutable, immediately before the move that publishes it. That is the only
        // moment where "downloaded but not yet visible" is observable from outside.
        var chmodProvider = new ObservingChmodProvider(observedPath =>
        {
            Assert.True(mockFileSystem.File.Exists(observedPath), "Staged executable should exist by the time it is chmod'd");
            Assert.False(mockFileSystem.File.Exists(expectedPath), "Final executable should not be visible before publication");
        });
        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, chmodProvider, platform, NoOpTailwindLogger.Instance);

        var result = await downloader.DownloadRuntimeAsync(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(expectedPath));
        Assert.True(chmodProvider.Observed, "Expected the staging observation to run before publication");
        Assert.NotNull(chmodProvider.LastPath);
        Assert.NotEqual(expectedPath, chmodProvider.LastPath);
        Assert.Equal(
            new[] { normalizedExpectedPath },
            mockFileSystem.Directory.GetFiles(nativeDirectory).Select(mockFileSystem.Path.GetFullPath));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithInvalidVersion_ShouldThrowException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var invalidVersion = "invalid_version";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Mock a 404 response for invalid version
        mockHttp.When($"https://github.com/tailwindlabs/tailwindcss/releases/download/v{invalidVersion}/tailwindcss-linux-x64")
                .Respond(System.Net.HttpStatusCode.NotFound);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            downloader.DownloadRuntimeAsync(tempDir, invalidVersion));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenPublicationFails_ShouldCleanUpStagedExecutable()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var nativeDirectory = Path.Combine(tempDir, runtimeId, "native");
        var expectedPath = Path.Combine(nativeDirectory, executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        var chmodProvider = new ThrowingChmodProvider();
        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, chmodProvider, platform, NoOpTailwindLogger.Instance);

        await Assert.ThrowsAsync<IOException>(() => downloader.DownloadRuntimeAsync(tempDir));

        Assert.False(mockFileSystem.File.Exists(expectedPath));
        Assert.Empty(mockFileSystem.Directory.GetFiles(nativeDirectory));
    }

    [Fact]
    public void PublishStagedExecutable_WhenStagedExecutableIsMissing_ShouldThrowFileNotFoundException()
    {
        var platform = Platform.LinuxX64;
        var stagedPath = "/test-runtime/linux-x64/native/.tailwind.staged.tmp";
        var finalPath = "/test-runtime/linux-x64/native/tailwind";
        var mockFileSystem = new MockFileSystem();
        var downloader = new TailwindDownloader(new HttpClient(), new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var ex = Assert.Throws<FileNotFoundException>(() =>
            downloader.PublishStagedExecutable(stagedPath, finalPath));

        Assert.Contains("not found after download before publication", ex.Message);
        Assert.Contains(finalPath, ex.Message);
        Assert.Contains(stagedPath, ex.Message);
    }

    [Fact]
    public void PublishStagedExecutable_WhenAnotherCallerPublishesBetweenDeleteAndMove_ShouldSwallowMoveFailureAndReturnFinalPath()
    {
        // Covers the concurrent-publish fallback in PublishStagedExecutable. The mutex in DownloadRuntime is
        // specifically designed to make this unreachable through the public API on a single machine (see its
        // own remarks) - PublishStagedExecutable's own stale-file check would just delete a merely
        // pre-existing final path before Move ever ran. To reach the fallback at all, the destination has to
        // reappear *between* that delete and the Move call, which RaceInjectingFile simulates.
        var platform = Platform.LinuxX64;
        var stagedPath = "/test-runtime/linux-x64/native/.tailwind.staged.tmp";
        var finalPath = "/test-runtime/linux-x64/native/tailwind";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(stagedPath, new MockFileData("staged tailwind executable"));
        // A stale file here is what makes PublishStagedExecutable call Delete(finalPath) at all - that
        // Delete is the hook RaceInjectingFile uses to simulate the other caller's publish landing
        // in the gap right after it.
        mockFileSystem.AddFile(finalPath, new MockFileData("stale tailwind executable"));

        var raceSimulatingFileSystem = new MockFileSystemWithFile(
            mockFileSystem,
            new RaceInjectingFile(mockFileSystem, finalPath, "published by another caller"));
        var downloader = new TailwindDownloader(new HttpClient(), new FakeLatestVersionResolver(null), raceSimulatingFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result = downloader.PublishStagedExecutable(stagedPath, finalPath);

        Assert.Equal(finalPath, result);
        Assert.Equal("published by another caller", mockFileSystem.File.ReadAllText(finalPath));
        Assert.False(mockFileSystem.File.Exists(stagedPath), "The staged file must still be cleaned up.");
    }

    [Fact]
    public void PublishStagedExecutable_WhenMoveSucceedsButDestinationStillMissing_ShouldThrowFileNotFoundException()
    {
        // Covers the final safety check in PublishStagedExecutable. File.Move is documented to either move
        // the file or throw - it never reports success while leaving the destination absent - but the mutex
        // in DownloadRuntime exists precisely because that guarantee doesn't hold across independent
        // processes/machines sharing a runtime directory. This simulates a broken Move to prove the check
        // actually catches such a violation rather than silently returning a path that doesn't exist.
        var platform = Platform.LinuxX64;
        var stagedPath = "/test-runtime/linux-x64/native/.tailwind.staged.tmp";
        var finalPath = "/test-runtime/linux-x64/native/tailwind";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(stagedPath, new MockFileData("staged tailwind executable"));

        var noOpMoveFileSystem = new NoOpMoveFileSystem(mockFileSystem);
        var downloader = new TailwindDownloader(new HttpClient(), new FakeLatestVersionResolver(null), noOpMoveFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var ex = Assert.Throws<FileNotFoundException>(() =>
            downloader.PublishStagedExecutable(stagedPath, finalPath));

        Assert.Contains("not found after publication", ex.Message);
        Assert.Contains(finalPath, ex.Message);
    }

    [Fact]
    public void DownloadRuntime_WhenMutexAlreadyExists_ShouldLogWaitingAndResumedMessages()
    {
        // Covers the "another process is already downloading" logging branch. `createdNew` is false
        // whenever the named mutex object already exists, regardless of whether it is currently held -
        // which is exactly the situation described in DownloadRuntime's own doc comment: multiple MSBuild
        // projects targeting the same runtime directory in a monorepo build.
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mutexName = TailwindDownloader.CreateMutexName(expectedPath);
        using var preExistingMutex = new Mutex(false, mutexName, out _);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var logger = new RecordingTailwindLogger();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, logger);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.Contains(logger.Messages, m => m.Contains("Another process is downloading"));
        Assert.Contains(logger.Messages, m => m.Contains("Finished waiting"));
    }

    [Fact]
    public void DownloadRuntime_WhenThePreviousOwnerAbandonedTheMutex_ShouldCarryOn()
    {
        // A build killed mid-download leaves the mutex owned by a thread that no longer exists, and the next
        // build's WaitOne throws AbandonedMutexException instead of returning. Treating that as failure would
        // mean one interrupted build poisons every later one until the machine is restarted.
        //
        // This is deterministic rather than timing-dependent: abandonment is defined by the owning thread
        // terminating, and Join proves it has. The mutex is created here and held open for the whole test so
        // the named object survives the thread that abandons it.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, TailwindRuntimeResolver.GetRuntimeIdentifier(platform), "native", executableName);

        using var mutex = new Mutex(false, TailwindDownloader.CreateMutexName(expectedPath));

        var abandoningThread = new Thread(() => mutex.WaitOne()) { IsBackground = true };
        abandoningThread.Start();
        abandoningThread.Join();

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        var downloader = new TailwindDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            NoOpChmodProvider.Instance,
            platform,
            NoOpTailwindLogger.Instance);

        // Act - without the AbandonedMutexException catch this throws instead of downloading.
        var result = downloader.DownloadRuntime(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_WhenMutexIsHeldPastTheTimeout_ShouldThrow()
    {
        // The test above creates the named mutex without ever holding it, so WaitOne returns immediately and
        // the timeout branch never runs. Observing it needs the mutex held from a genuinely different thread:
        // a named Mutex is reentrant for the thread that already owns it, so holding it here would succeed
        // instead of blocking.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, TailwindRuntimeResolver.GetRuntimeIdentifier(platform), "native", executableName);

        using var mutexHeldSignal = new ManualResetEventSlim(false);
        using var releaseMutexSignal = new ManualResetEventSlim(false);

        var holderThread = new Thread(() =>
        {
            using var mutex = new Mutex(false, TailwindDownloader.CreateMutexName(expectedPath), out _);
            mutex.WaitOne();
            mutexHeldSignal.Set();
            releaseMutexSignal.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true
        };
        holderThread.Start();

        try
        {
            mutexHeldSignal.Wait();

            var mockFileSystem = new MockFileSystem();
            var mockHttp = new MockHttpMessageHandler();
            var logger = new RecordingTailwindLogger();
            var downloader = new TailwindDownloader(
                mockHttp.ToHttpClient(),
                new FakeLatestVersionResolver(null),
                mockFileSystem,
                NoOpChmodProvider.Instance,
                platform,
                logger);

            // Act & Assert - the holder never releases within the timeout.
            Assert.Throws<TimeoutException>(() => downloader.DownloadRuntime(tempDir, "1.3.6", mutexTimeoutSeconds: 0));
            Assert.Contains(logger.Messages, m => m.Contains("Another process is downloading", StringComparison.Ordinal));
        }
        finally
        {
            releaseMutexSignal.Set();
            holderThread.Join();
        }
    }

    [Fact]
    public async Task DownloadRuntime_WhenAnotherProcessPublishesWhileWaiting_ShouldSkipTheDownload()
    {
        // The re-check after acquiring the mutex is the entire reason the mutex exists, and nothing covered
        // it. Without it every process that queued behind the winner would download and republish on top of
        // the executable the others are already running.
        var platform = Platform.LinuxX64;
        const string tempDir = "/test-runtime";
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, TailwindRuntimeResolver.GetRuntimeIdentifier(platform), "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        // No handler is registered, so the assertions below are backed up by the request failing outright if
        // the downloader ever decides to go to the network.
        using var mockHttp = new MockHttpMessageHandler();
        var logger = new RecordingTailwindLogger();
        var chmod = new RecordingChmodProvider();
        var downloader = new TailwindDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            chmod,
            platform,
            logger);

        using var mutexHeldSignal = new ManualResetEventSlim(false);
        using var publishSignal = new ManualResetEventSlim(false);

        var holderThread = new Thread(() =>
        {
            using var mutex = new Mutex(false, TailwindDownloader.CreateMutexName(expectedPath), out _);
            mutex.WaitOne();
            mutexHeldSignal.Set();
            publishSignal.Wait();

            // Publish the way a real download does, marker last, while still holding the mutex.
            mockFileSystem.AddFile(expectedPath, new MockFileData("tailwindcss"));
            mockFileSystem.AddFile(markerPath, new MockFileData("1.3.6"));

            mutex.ReleaseMutex();
        })
        {
            IsBackground = true
        };
        holderThread.Start();

        try
        {
            mutexHeldSignal.Wait();

            var download = Task.Run(() => downloader.DownloadRuntime(tempDir, "1.3.6", mutexTimeoutSeconds: 60));

            // This message is logged after the pre-mutex cache check and before WaitOne, so seeing it means
            // the downloader looked at an empty cache and is now queued behind the holder. Publishing before
            // that point would exercise the pre-mutex check instead of the branch under test.
            Assert.True(
                SpinWait.SpinUntil(
                    () => logger.Messages.Any(m => m.Contains("Another process is downloading", StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(30)),
                "The downloader never reported that it was waiting for another process.");

            publishSignal.Set();

            // Act
            var result = await download;

            // Assert
            Assert.Equal(expectedPath, result);
            Assert.Contains(
                logger.Messages,
                m => m.Contains("was downloaded by another process while waiting", StringComparison.Ordinal));
            // The skip path still has to chmod: the executable it is handing back was written by a process
            // whose permission bits this one cannot assume anything about.
            Assert.Equal(expectedPath, chmod.LastPath);
        }
        finally
        {
            publishSignal.Set();
            holderThread.Join();
        }
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenPinnedVersionBumped_ShouldRedownloadAndUpdateMarker()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale tailwind executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.3.12"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/download/v1.4.2/tailwindcss-linux-x64")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(binaryContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion("1.4.2"), "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act - request a newer pinned version against a cache directory holding an older one
        var result = await downloader.DownloadRuntimeAsync(tempDir, "1.4.2");

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal("1.4.2", (await mockFileSystem.File.ReadAllTextAsync(markerPath)).Trim());
        Assert.Equal("mock tailwind executable", await mockFileSystem.File.ReadAllTextAsync(expectedPath));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_LatestResolvesToUnchangedVersion_ShouldSkipReDownload()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("already-cached tailwind executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.4.2"));

        // No .When(...) registered: if the code tried to download, the mock would throw.
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var resolver = new FakeLatestVersionResolver("1.4.2");
        var downloader = new TailwindDownloader(httpClient, resolver, mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert - no download was attempted, so the cached executable is untouched
        Assert.Equal(expectedPath, result);
        Assert.Equal(1, resolver.CallCount);
        Assert.Equal("already-cached tailwind executable", await mockFileSystem.File.ReadAllTextAsync(expectedPath));
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "tailwindcss.exe")]
    [InlineData(Platform.WindowsArm64, "tailwindcss.exe")]
    [InlineData(Platform.LinuxX64, "tailwindcss")]
    [InlineData(Platform.LinuxArm64, "tailwindcss")]
    [InlineData(Platform.MacOsX64, "tailwindcss")]
    [InlineData(Platform.MacOsArm64, "tailwindcss")]
    public async Task DownloadRuntimeAsync_ForAllPlatforms_ShouldDownloadCorrectExecutable(Platform platform, string expectedExecutable)
    {
        // Arrange
        var tempDir = "/test-runtime";
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var downloadName = TailwindRuntimeResolver.GetDownloadName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Tailwind executable
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When($"https://github.com/tailwindlabs/tailwindcss/releases/latest/download/{downloadName}")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, $"{downloadName}", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result));
        Assert.EndsWith(expectedExecutable, result);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenChecksumDoesNotMatch_ShouldThrowInvalidDataException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        // Deliberately wrong checksum: a 64-hex-char value that does not match the actual archive.
        mockHttp.When(ChecksumsUrlLatest)
                .Respond("text/plain", $"{new string('0', 64)}  tailwindcss-linux-x64\n");

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("Checksum mismatch", ex.Message);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenChecksumsFileHasNoMatchingEntry_ShouldThrowInvalidDataException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        mockHttp.When(ChecksumsUrlLatest)
                .Respond("text/plain", $"{new string('a', 64)}  tailwind-windows-x64.zip\n");

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("No checksum entry", ex.Message);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithRealWorldChecksumsFileFormat_ShouldVerifySuccessfully()
    {
        // Arrange - tailwindlabs/tailwindcss's sha256sums.txt for v4.3.3, verbatim, with only the target
        // entry's hash swapped for the mock binary's actual one. It is the whole file rather than an
        // excerpt, which makes it worth two things a synthetic single-line fixture cannot be:
        //
        //  * "tailwindcss-linux-x64" is a strict prefix of "tailwindcss-linux-x64-musl", so a substring
        //    match, or a regex where an unescaped "." or a missing anchor slips in, picks the wrong line
        //    and reports a checksum mismatch on a perfectly good download.
        //  * every filename carries the "./" prefix, exactly as upstream writes it. A parser that compares
        //    the raw last field matches nothing here and fails with "No checksum entry", which is the
        //    failure this whole fixture exists to catch.
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var binaryContent = CreateMockTailwindBinary();
        var actualHash = Convert.ToHexString(SHA256.HashData(binaryContent.ToArray())).ToLowerInvariant();

        var realWorldChecksumsLines = new[]
        {
            "55fd0b241214eff3de1e8ee4f22796662f2d2e7a49bcfca7477cfd0bac398195  ./tailwindcss-linux-arm64",
            "71ea4be79c9de9827545682df3e040053fb535d37c71ed2cfdedf9385a0868e0  ./tailwindcss-linux-arm64-musl",
            $"{actualHash}  ./tailwindcss-linux-x64",
            "a04d34ceacc8f52cbe8920ad846cdeb61d3d0021dba32db0d1f77c9d9fad7a6c  ./tailwindcss-linux-x64-musl",
            "cdf646702987a743464dff4d9c60fd4480d1c1e73dd819a9a67f1078815dce9d  ./tailwindcss-macos-arm64",
            "7922e0953f2110c05976e3bf58f14e643d90427575e766b7d433f5f80cbee7e1  ./tailwindcss-macos-x64",
            "e0e260ce048014e9268f6237ff18f8ccf02cef521cbd0ae04e82c2cdf7aa3955  ./tailwindcss-windows-x64.exe",
        };
        var realWorldChecksums = string.Join("\n", realWorldChecksumsLines) + "\n";

        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        mockHttp.When(ChecksumsUrlLatest)
                .Respond("text/plain", realWorldChecksums);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.True(mockFileSystem.File.Exists(result));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenChecksumsDownloadFails_ShouldThrowInvalidDataException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        mockHttp.When(ChecksumsUrlLatest)
                .Respond(System.Net.HttpStatusCode.NotFound);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("Failed to download checksums", ex.Message);
    }

    #region DownloadRuntime (synchronous, mutex-protected)

    [Fact]
    public void DownloadRuntime_WithNullRuntimeDirectory_ShouldThrowArgumentException()
    {
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, TailwindRuntimeResolver.GetCurrentPlatform(), NoOpTailwindLogger.Instance);

        Assert.Throws<ArgumentException>(() =>
            downloader.DownloadRuntime(null!));
    }

    [Fact]
    public void DownloadRuntime_WithEmptyRuntimeDirectory_ShouldThrowArgumentException()
    {
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, TailwindRuntimeResolver.GetCurrentPlatform(), NoOpTailwindLogger.Instance);

        Assert.Throws<ArgumentException>(() =>
            downloader.DownloadRuntime(string.Empty));
    }

    [Fact]
    public void DownloadRuntime_WithWhitespaceRuntimeDirectory_ShouldThrowArgumentException()
    {
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, TailwindRuntimeResolver.GetCurrentPlatform(), NoOpTailwindLogger.Instance);

        Assert.Throws<ArgumentException>(() =>
            downloader.DownloadRuntime("   "));
    }

    [Fact]
    public void DownloadRuntime_WithValidDirectory_ShouldReturnExecutablePath()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
        Assert.Equal(
            new[] { mockFileSystem.Path.GetFullPath(result) },
            mockFileSystem.Directory.GetFiles(Path.Combine(tempDir, runtimeId, "native")));
    }

    [Fact]
    public void DownloadRuntime_WithSpecificVersion_ShouldDownloadThatVersion()
    {
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When($"https://github.com/tailwindlabs/tailwindcss/releases/download/v{version}/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
    }

    [Fact]
    public void DownloadRuntime_CalledTwice_ShouldReuseExistingRuntime()
    {
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var requestCount = 0;

        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When($"https://github.com/tailwindlabs/tailwindcss/releases/download/v{version}/tailwindcss-linux-x64")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(binaryContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result1 = downloader.DownloadRuntime(tempDir, version);
        Assert.True(mockFileSystem.File.Exists(result1));

        var result2 = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(result1, result2);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public void DownloadRuntime_WhenPinnedVersionIsAlreadyCached_ShouldSkipDownload()
    {
        var tempDir = "/test-runtime";
        var version = "1.4.2";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("already-cached tailwind executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData(version));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        mockHttp.When($"https://github.com/tailwindlabs/tailwindcss/releases/download/v{version}/tailwindcss-linux-x64")
                .Respond(() =>
                {
                    requestCount++;
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
                });

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(expectedPath, result);
        Assert.Equal(0, requestCount);
        Assert.Equal("already-cached tailwind executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_WhenPinnedVersionBumped_ShouldRedownloadAndUpdateMarker()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale tailwind executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.3.12"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/download/v1.4.2/tailwindcss-linux-x64")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(binaryContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion("1.4.2"), "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        // Request a newer pinned version against a cache directory holding an older one.
        var result = downloader.DownloadRuntime(tempDir, "1.4.2");

        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(markerPath).Trim());
        Assert.Equal("mock tailwind executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_WhenMarkerMissingButExecutableExists_ShouldRedownloadAndCreateMarker()
    {
        // Simulate a runtime cached by a pre-fix version of TailwindDownloader (executable present, no marker).
        var tempDir = "/test-runtime";
        var version = "1.4.2";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale tailwind executable"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When($"https://github.com/tailwindlabs/tailwindcss/releases/download/v{version}/tailwindcss-linux-x64")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(binaryContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal(version, mockFileSystem.File.ReadAllText(markerPath).Trim());
        Assert.Equal("mock tailwind executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_LatestResolvesToUnchangedVersion_ShouldSkipReDownload()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("already-cached tailwind executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.4.2"));

        // No .When(...) registered: if the code tried to download, the mock would throw.
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var resolver = new FakeLatestVersionResolver("1.4.2");
        var downloader = new TailwindDownloader(httpClient, resolver, mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        // No download was attempted, so the cached executable is untouched.
        Assert.Equal(expectedPath, result);
        Assert.Equal(1, resolver.CallCount);
        Assert.Equal("already-cached tailwind executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_LatestResolvesToNewVersion_ShouldRedownloadAndUpdateMarker()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale tailwind executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.4.1"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        var binaryContent = CreateMockTailwindBinary();
        // Resolved to 1.4.2, so the code downloads the tag-scoped URL directly, not /latest/download/.
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/download/v1.4.2/tailwindcss-linux-x64")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(binaryContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion("1.4.2"), "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var resolver = new FakeLatestVersionResolver("1.4.2");
        var downloader = new TailwindDownloader(httpClient, resolver, mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(markerPath).Trim());
        Assert.Equal("mock tailwind executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_LatestCannotResolveVersion_ShouldRedownloadAndClearStaleMarker()
    {
        // Simulate GitHub's redirect shape changing so the version can't be resolved.
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale tailwind executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.4.1"));

        var mockHttp = new MockHttpMessageHandler();
        var binaryContent = CreateMockTailwindBinary();
        // Resolution failed, so the code falls back to the plain "latest" URL.
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var resolver = new FakeLatestVersionResolver(resolvedVersion: null);
        var downloader = new TailwindDownloader(httpClient, resolver, mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.False(mockFileSystem.File.Exists(markerPath), "Expected the stale marker to be cleared when the resolved version could not be determined");
        Assert.Equal("mock tailwind executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "tailwindcss.exe")]
    [InlineData(Platform.WindowsArm64, "tailwindcss.exe")]
    [InlineData(Platform.LinuxX64, "tailwindcss")]
    [InlineData(Platform.LinuxArm64, "tailwindcss")]
    [InlineData(Platform.MacOsX64, "tailwindcss")]
    [InlineData(Platform.MacOsArm64, "tailwindcss")]
    public void DownloadRuntime_ForAllPlatforms_ShouldDownloadCorrectExecutable(Platform platform, string expectedExecutable)
    {
        var tempDir = "/test-runtime";
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var downloadName = TailwindRuntimeResolver.GetDownloadName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When($"https://github.com/tailwindlabs/tailwindcss/releases/latest/download/{downloadName}")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, $"{downloadName}", binaryContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result));
        Assert.EndsWith(expectedExecutable, result);
    }

    [Fact]
    public void DownloadRuntime_ReleasesMutexOnAcquiringThread_EvenWhenAwaitsResumeOnAnotherThread()
    {
        // Regression test: an earlier refactor made the mutex-guarded critical section a genuinely `async`
        // method, so a continuation resuming on a different pooled thread after an `await` made
        // Mutex.ReleaseMutex() throw ApplicationException - it is thread-affine, only the thread that
        // called WaitOne may release it. DownloadRuntime must stay fully synchronous end to end (blocking
        // via GetAwaiter().GetResult(), never `await`, inside the mutex try/finally) so the acquiring
        // thread never changes, no matter how its internal awaited HTTP calls get scheduled.
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        // Forces every awaited HTTP call to genuinely suspend rather than complete synchronously - only
        // then does `await` register a continuation with the ambient SynchronizationContext at all.
        var httpClient = new HttpClient(new AlwaysYieldsHandler(mockHttp));
        var downloader = new TailwindDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, NoOpChmodProvider.Instance, platform, NoOpTailwindLogger.Instance);

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AlwaysResumesOnNewThreadSynchronizationContext());
        try
        {
            // If any await inside the download/verify/extract chain resumed inside the mutex's
            // try/finally instead of blocking it from the outside, this throws ApplicationException from
            // ReleaseMutex instead of returning normally.
            var result = downloader.DownloadRuntime(tempDir);

            Assert.True(mockFileSystem.File.Exists(result));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    #endregion

    private static string ChecksumsUrlForVersion(string version) =>
        $"https://github.com/tailwindlabs/tailwindcss/releases/download/v{version}/sha256sums.txt";

    /// <summary>
    /// Registers a mock response for the sha256sums.txt endpoint that matches the real content of
    /// <paramref name="binaryContent"/>, mirroring the "hash  filename" format Tailwind publishes per release.
    /// </summary>
    [Fact]
    public void DownloadRuntime_WhenScratchFilesCannotBeDeleted_ShouldStillSucceed()
    {
        // TryDeleteFile's callers all run in a finally, so a throw there would replace whatever the download
        // actually reported - and on this path nothing failed: Tailwind is published and usable, only the scratch
        // staged file could not be removed. A scanner holding one open is all it takes.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, TailwindRuntimeResolver.GetRuntimeIdentifier(platform), "native", executableName);

        var real = new MockFileSystem();
        var file = new RecordingFile(real, failDeletes: true);
        var fileSystem = new MockFileSystemWithFile(real, file);

        var mockHttp = new MockHttpMessageHandler();
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        var downloader = new TailwindDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            NoOpTailwindLogger.Instance);

        // Act
        var result = downloader.DownloadRuntime(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(real.File.Exists(expectedPath), "Tailwind should still be published when cleanup fails.");

        // Swallowing the error is the point; skipping the cleanup is not.
        Assert.Contains(file.Deleted, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void DownloadRuntime_ShouldDeleteScratchFilesWithoutProbingForThemFirst()
    {
        // File.Delete does not throw when the file is missing, so an Exists guard would only defend against
        // the harmless outcome while doing nothing about the locked file that actually fails. This pins that
        // the guard stays gone: re-adding it is a silent no-op that makes the cleanup look safer than it is.
        //
        // The version marker is the subject rather than the staged file. On this path - "latest" that the
        // resolver could not pin to a version - the marker is deleted so it cannot go on claiming a version
        // nobody can confirm, and nothing anywhere asks whether it exists first. The staged file would be a
        // worse subject: PublishStagedExecutable does check it for existence, deliberately, as the
        // precondition for publishing, so an assertion about it would fail for a reason unrelated to
        // TryDeleteFile.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";
        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(platform);
        var markerPath = Path.Combine(tempDir, runtimeId, "native", executableName + ".version");

        var real = new MockFileSystem();
        var file = new RecordingFile(real);
        var fileSystem = new MockFileSystemWithFile(real, file);

        var mockHttp = new MockHttpMessageHandler();
        var binaryContent = CreateMockTailwindBinary();
        mockHttp.When("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
                .Respond("application/octet-stream", binaryContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "tailwindcss-linux-x64", binaryContent);

        var downloader = new TailwindDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            NoOpTailwindLogger.Instance);

        // Act
        downloader.DownloadRuntime(tempDir);

        // Assert
        Assert.Contains(file.Deleted, path => PathsMatch(path, markerPath));
        Assert.DoesNotContain(file.ExistenceChecks, path => PathsMatch(path, markerPath));
    }

    /// <summary>
    /// Compares two paths for the same file, tolerating the separator and rooting differences a
    /// <see cref="MockFileSystem"/> introduces on Windows.
    /// </summary>
    private static bool PathsMatch(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).Replace('\\', '/'),
            Path.GetFullPath(right).Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Publishes a checksum line for <paramref name="binaryContent"/> in upstream's exact format.
    /// </summary>
    /// <remarks>
    /// The <c>./</c> prefix is deliberate and load-bearing: it is what Tailwind actually writes, and
    /// <c>TailwindDownloader</c> has to strip it before comparing. Emitting a bare filename here would make
    /// every one of these tests pass against a parser that never learned about the prefix.
    /// </remarks>
    private static void MockChecksums(MockHttpMessageHandler mockHttp, string checksumsUrl, string assetFileName, MemoryStream binaryContent)
    {
        using var sha256 = SHA256.Create();
        var hash = Convert.ToHexString(sha256.ComputeHash(binaryContent.ToArray())).ToLowerInvariant();
        mockHttp.When(checksumsUrl).Respond("text/plain", $"{hash}  ./{assetFileName}\n");
    }

    /// <summary>
    /// The stand-in for a downloaded Tailwind: the response body is the executable itself, with no archive
    /// wrapped around it.
    /// </summary>
    private static MemoryStream CreateMockTailwindBinary()
    {
        return new MemoryStream("mock tailwind executable"u8.ToArray());
    }

    /// <summary>
    /// A chmod provider that runs an assertion at the moment the staged executable is made executable -
    /// after the download and checksum, before the move that publishes it.
    /// </summary>
    private sealed class ObservingChmodProvider : IChmodProvider
    {
        private readonly Action<string> _onChmod;

        public ObservingChmodProvider(Action<string> onChmod) => _onChmod = onChmod;

        public string? LastPath { get; private set; }

        public bool Observed { get; private set; }

        public void EnsureExecutablePermissions(string path)
        {
            LastPath = path;
            Observed = true;
            _onChmod(path);
        }
    }

    /// <summary>
    /// Forces every request through this handler to suspend rather than complete synchronously, so an
    /// `await` on it actually registers a continuation with the ambient <see cref="SynchronizationContext"/>
    /// instead of continuing inline (which is what an already-completed <see cref="Task"/> does).
    /// </summary>
    private sealed class AlwaysYieldsHandler : DelegatingHandler
    {
        public AlwaysYieldsHandler(HttpMessageHandler inner) : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// A <see cref="SynchronizationContext"/> that marshals every continuation onto a brand-new dedicated
    /// thread. A real thread pool only sometimes reuses a different thread after an await, which would make
    /// a test relying on it flaky; spawning a fresh OS thread every time makes the worst case deterministic.
    /// </summary>
    private sealed class AlwaysResumesOnNewThreadSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            new Thread(() => d(state)) { IsBackground = true }.Start();
        }
    }

    /// <summary>
    /// Wraps a <see cref="MockFileSystem"/>, swapping in a custom <see cref="IFile"/> and forwarding
    /// everything else untouched. Subclassing <see cref="MockFile"/> keeps the real behaviour for every
    /// member the test does not override, so a new call in the downloader cannot silently get a default.
    /// </summary>
    private sealed class MockFileSystemWithFile : IFileSystem
    {
        private readonly MockFileSystem _inner;

        public MockFileSystemWithFile(MockFileSystem inner, IFile file)
        {
            _inner = inner;
            File = file;
        }

        public IFile File { get; }

        public IDirectory Directory => _inner.Directory;
        public IDirectoryInfoFactory DirectoryInfo => _inner.DirectoryInfo;
        public IDriveInfoFactory DriveInfo => _inner.DriveInfo;
        public IFileInfoFactory FileInfo => _inner.FileInfo;
        public IFileStreamFactory FileStream => _inner.FileStream;
        public IFileSystemWatcherFactory FileSystemWatcher => _inner.FileSystemWatcher;
        public IFileVersionInfoFactory FileVersionInfo => _inner.FileVersionInfo;
        public IPath Path => _inner.Path;
    }

    /// <summary>
    /// Simulates another caller publishing <paramref name="_raceTargetPath"/> in the gap between
    /// <see cref="TailwindDownloader"/>'s own stale-file <c>Delete</c> and its subsequent <c>Move</c> - the one
    /// interleaving that makes <c>Move</c> throw <see cref="IOException"/> with the destination existing
    /// again, which no amount of pre-seeding state before the call can reach on its own.
    /// </summary>
    /// <summary>
    /// Records the paths passed to <c>Delete</c> and <c>Exists</c>, and optionally fails every delete to
    /// stand in for a scanner holding a file open. Everything else behaves like the underlying mock.
    /// </summary>
    private sealed class RecordingFile : MockFile
    {
        private readonly bool _failDeletes;

        public RecordingFile(MockFileSystem inner, bool failDeletes = false) : base(inner)
        {
            _failDeletes = failDeletes;
        }

        public List<string> Deleted { get; } = new();

        public List<string> ExistenceChecks { get; } = new();

        public override void Delete(string path)
        {
            Deleted.Add(path);

            if (_failDeletes)
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            base.Delete(path);
        }

        public override bool Exists(string? path)
        {
            if (path is not null)
            {
                ExistenceChecks.Add(path);
            }

            return base.Exists(path);
        }
    }

    private sealed class RaceInjectingFile : MockFile
    {
        private readonly MockFileSystem _inner;
        private readonly string _raceTargetPath;
        private readonly string _raceContent;

        public RaceInjectingFile(MockFileSystem inner, string raceTargetPath, string raceContent) : base(inner)
        {
            _inner = inner;
            _raceTargetPath = raceTargetPath;
            _raceContent = raceContent;
        }

        public override void Delete(string path)
        {
            base.Delete(path);

            if (path == _raceTargetPath)
            {
                _inner.AddFile(_raceTargetPath, new MockFileData(_raceContent));
            }
        }
    }

    /// <summary>
    /// Wraps a <see cref="MockFileSystem"/>, swapping in <see cref="NoOpMoveFile"/> for
    /// <see cref="IFileSystem.File"/> and forwarding everything else untouched.
    /// </summary>
    private sealed class NoOpMoveFileSystem : IFileSystem
    {
        private readonly MockFileSystem _inner;

        public NoOpMoveFileSystem(MockFileSystem inner)
        {
            _inner = inner;
            File = new NoOpMoveFile(inner);
        }

        public IFile File { get; }

        public IDirectory Directory => _inner.Directory;
        public IDirectoryInfoFactory DirectoryInfo => _inner.DirectoryInfo;
        public IDriveInfoFactory DriveInfo => _inner.DriveInfo;
        public IFileInfoFactory FileInfo => _inner.FileInfo;
        public IFileStreamFactory FileStream => _inner.FileStream;
        public IFileSystemWatcherFactory FileSystemWatcher => _inner.FileSystemWatcher;
        public IFileVersionInfoFactory FileVersionInfo => _inner.FileVersionInfo;
        public IPath Path => _inner.Path;
    }

    /// <summary>
    /// Simulates a filesystem whose <c>Move</c> reports success without actually publishing the destination
    /// file - a contract violation a real <see cref="File.Move(string, string)"/> never commits, but one
    /// PublishStagedExecutable's own final existence check must still catch defensively.
    /// </summary>
    private sealed class NoOpMoveFile : MockFile
    {
        public NoOpMoveFile(MockFileSystem inner) : base(inner)
        {
        }

        public override void Move(string sourceFileName, string destFileName)
        {
            // Deliberately does not call base.Move and does not create destFileName.
        }
    }

    private sealed class RecordingTailwindLogger : ITailwindLogger
    {
        private readonly List<string> _messages = new();

        // Snapshotted under the lock: the contended-mutex tests read this from the test thread while the
        // downloader is still logging from another one.
        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToList();
                }
            }
        }

        public void LogMessage(string message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }
        }
    }

    private sealed class RecordingChmodProvider : IChmodProvider
    {
        public string? LastPath { get; private set; }

        public void EnsureExecutablePermissions(string filePath)
        {
            LastPath = filePath;
        }
    }

    private sealed class ThrowingChmodProvider : IChmodProvider
    {
        public void EnsureExecutablePermissions(string filePath)
        {
            throw new IOException($"chmod failed for {filePath}");
        }
    }

    /// <summary>
    /// Stands in for <see cref="GitHubLatestVersionResolver"/> so tests can dictate what "latest" resolves
    /// to without touching the network. <see cref="GitHubLatestVersionResolver"/>'s own HTTP handling
    /// (redirect probe, Location header parsing) is covered separately in
    /// <c>GitHubLatestVersionResolverTests</c>.
    /// </summary>
    private sealed class FakeLatestVersionResolver : ILatestVersionResolver
    {
        private readonly string? _resolvedVersion;

        public FakeLatestVersionResolver(string? resolvedVersion)
        {
            _resolvedVersion = resolvedVersion;
        }

        public int CallCount { get; private set; }

        public Task<string?> TryResolveVersionAsync(string latestDownloadUrl, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_resolvedVersion);
        }
    }
}
