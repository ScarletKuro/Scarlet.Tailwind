namespace Scarlet.Tailwind.Cli.Tests;

public class ConsoleTailwindLoggerTests
{
    [Fact]
    public void LogMessage_ShouldPrefixAndWriteToTheGivenWriter()
    {
        // Arrange - stderr, never stdout: `dotnet tailwind ... | jq` must not receive the tool's own chatter
        var stderr = new StringWriter();
        var logger = new ConsoleTailwindLogger(stderr);

        // Act
        logger.LogMessage("Another process is downloading the Tailwind runtime. Waiting...");

        // Assert
        Assert.Equal(
            "Scarlet.Tailwind: Another process is downloading the Tailwind runtime. Waiting..." + Environment.NewLine,
            stderr.ToString());
    }
}
