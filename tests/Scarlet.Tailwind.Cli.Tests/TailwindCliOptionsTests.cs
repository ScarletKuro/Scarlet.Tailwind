using Scarlet.Tailwind.Cli.Tests.Mock;

namespace Scarlet.Tailwind.Cli.Tests;

public class TailwindCliOptionsTests
{
    [Fact]
    public void FromEnvironment_WithNothingSet_ShouldUseThePinnedVersion()
    {
        // Act
        var options = TailwindCliOptions.FromEnvironment(new FakeEnvironmentProvider(), "1.4.2");

        // Assert
        Assert.Equal("1.4.2", options.RequestedVersion);
        Assert.Null(options.RequestedVersionOverride);
        Assert.False(options.UseLatest);
        Assert.Equal("1.4.2", options.DownloadVersion);
        Assert.Null(options.ExplicitTailwindPath);
        Assert.False(options.IgnoreEmbedded);
        Assert.False(options.PurePassthrough);
        Assert.Null(options.CacheRootOverride);
        Assert.Equal(300, options.DownloadTimeoutSeconds);
        Assert.Null(options.DownloadTimeoutOverride);
    }

    [Fact]
    public void FromEnvironment_WithVersionSet_ShouldExposeTheRawOverride()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [TailwindCliOptions.VersionVariable] = "1.2.3"
        });

        // Act
        var options = TailwindCliOptions.FromEnvironment(environment, "1.4.2");

        // Assert
        Assert.Equal("1.2.3", options.RequestedVersion);
        Assert.Equal("1.2.3", options.RequestedVersionOverride);
    }

    [Fact]
    public void FromEnvironment_WithCacheSet_ShouldExposeTheRawOverride()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [TailwindCliOptions.CacheVariable] = "/cache"
        });

        // Act
        var options = TailwindCliOptions.FromEnvironment(environment, "1.4.2");

        // Assert
        Assert.Equal("/cache", options.CacheRoot);
        Assert.Equal("/cache", options.CacheRootOverride);
    }

    [Fact]
    public void FromEnvironment_WithTimeoutSet_ShouldExposeTheRawOverride()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [TailwindCliOptions.DownloadTimeoutVariable] = "42"
        });

        // Act
        var options = TailwindCliOptions.FromEnvironment(environment, "1.4.2");

        // Assert
        Assert.Equal(42, options.DownloadTimeoutSeconds);
        Assert.Equal("42", options.DownloadTimeoutOverride);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("LATEST")]
    public void FromEnvironment_WithLatest_ShouldAskTheDownloaderForTheLatestRelease(string requested)
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [TailwindCliOptions.VersionVariable] = requested
        });

        // Act
        var options = TailwindCliOptions.FromEnvironment(environment, "1.4.2");

        // Assert
        Assert.True(options.UseLatest);
        Assert.Null(options.DownloadVersion);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    public void FromEnvironment_ShouldReadBooleanVariablesForgivingly(string value, bool expected)
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [TailwindCliOptions.NoEmbeddedVariable] = value
        });

        // Act
        var options = TailwindCliOptions.FromEnvironment(environment, "1.4.2");

        // Assert
        Assert.Equal(expected, options.IgnoreEmbedded);
    }

    [Theory]
    [InlineData("600", 600)]
    [InlineData("not-a-number", 300)]
    [InlineData("0", 300)]
    [InlineData("-5", 300)]
    public void FromEnvironment_ShouldFallBackToTheDefaultTimeoutOnNonsense(string value, int expected)
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [TailwindCliOptions.DownloadTimeoutVariable] = value
        });

        // Act
        var options = TailwindCliOptions.FromEnvironment(environment, "1.4.2");

        // Assert
        Assert.Equal(expected, options.DownloadTimeoutSeconds);
    }

    [Fact]
    public void ResolveCacheRoot_OnWindows_ShouldUseLocalApplicationData()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(
            isWindows: true,
            home: @"C:\Users\tester",
            folders: new Dictionary<Environment.SpecialFolder, string>
            {
                [Environment.SpecialFolder.LocalApplicationData] = @"C:\Users\tester\AppData\Local"
            });

        // Act
        var root = TailwindCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine(@"C:\Users\tester\AppData\Local", "ScarletKuro", "Scarlet.Tailwind"), root);
    }

    [Fact]
    public void ResolveCacheRoot_OnMacOs_ShouldUseLibraryCaches()
    {
        // Arrange - .NET maps LocalApplicationData to ~/.local/share on macOS, which is not where a macOS
        // user expects a cache, so this path is written out explicitly
        var environment = new FakeEnvironmentProvider(home: "/Users/tester", isMacOs: true);

        // Act
        var root = TailwindCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine("/Users/tester", "Library", "Caches", "ScarletKuro", "Scarlet.Tailwind"), root);
    }

    [Fact]
    public void ResolveCacheRoot_OnLinuxWithXdg_ShouldHonourXdgCacheHome()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(
            new Dictionary<string, string> { ["XDG_CACHE_HOME"] = "/xdg" },
            home: "/home/tester");

        // Act
        var root = TailwindCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine("/xdg", "ScarletKuro", "Scarlet.Tailwind"), root);
    }

    [Fact]
    public void ResolveCacheRoot_OnLinuxWithoutXdg_ShouldFallBackToDotCache()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(home: "/home/tester");

        // Act
        var root = TailwindCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine("/home/tester", ".cache", "ScarletKuro", "Scarlet.Tailwind"), root);
    }

    [Fact]
    public void ResolveCacheRoot_WithNoHome_ShouldFallBackToTemp()
    {
        // Arrange - containers frequently run without HOME; failing on a path we could not build would be
        // worse than using temp
        var environment = new FakeEnvironmentProvider(home: null, tempDirectory: "/tmp");

        // Act
        var root = TailwindCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine("/tmp", "ScarletKuro", "Scarlet.Tailwind"), root);
    }

    [Fact]
    public void ResolveCacheRoot_OnMacOsWithNoHome_ShouldFallBackToTemp()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(home: null, isMacOs: true, tempDirectory: "/tmp");

        // Act
        var root = TailwindCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine("/tmp", "ScarletKuro", "Scarlet.Tailwind"), root);
    }

    [Fact]
    public void ResolveCacheRoot_OnWindowsWithoutLocalAppData_ShouldFallBackToHome()
    {
        // Arrange - GetFolderPath can come back empty on a stripped-down or containerised Windows
        var environment = new FakeEnvironmentProvider(home: @"C:\Users\tester", isWindows: true);

        // Act
        var root = TailwindCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine(@"C:\Users\tester", "ScarletKuro", "Scarlet.Tailwind"), root);
    }

    [Fact]
    public void ResolveCacheRoot_OnWindowsWithNeither_ShouldFallBackToTemp()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(home: null, isWindows: true, tempDirectory: @"C:\Temp");

        // Act
        var root = TailwindCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine(@"C:\Temp", "ScarletKuro", "Scarlet.Tailwind"), root);
    }

    [Fact]
    public void ResolveCacheRoot_WithOverride_ShouldWinEverywhere()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(
            new Dictionary<string, string> { [TailwindCliOptions.CacheVariable] = "/explicit" },
            home: "/home/tester",
            isWindows: true);

        // Act
        var root = TailwindCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal("/explicit", root);
    }
}
