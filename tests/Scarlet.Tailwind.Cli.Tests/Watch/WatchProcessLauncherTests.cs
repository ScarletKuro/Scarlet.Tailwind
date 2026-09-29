namespace Scarlet.Tailwind.Cli.Tests;

public class WatchProcessLauncherTests
{
    [Fact]
    public void CreateStartInfo_ShouldPreserveTheBuildWorkingDirectoryAndArgumentVector()
    {
        var invocation = new TailwindWatchInvocation(
            new TailwindLaunchRequest("tailwindcss", ["--input=Styles/app.css", string.Empty, "--watch=always"]),
            "/repo/project",
            "/repo/project/Styles/app.css",
            "/repo/project/wwwroot/css/app.css",
            ["/repo/project/wwwroot/css/app.css"]);

        var startInfo = WatchProcessLauncher.CreateStartInfo(invocation);

        Assert.Equal("tailwindcss", startInfo.FileName);
        Assert.Equal("/repo/project", startInfo.WorkingDirectory);
        Assert.Equal(invocation.Request.Arguments, startInfo.ArgumentList);
    }
}
