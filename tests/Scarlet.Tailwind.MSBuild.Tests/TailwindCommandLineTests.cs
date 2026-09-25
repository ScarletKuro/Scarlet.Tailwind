namespace Scarlet.Tailwind.MSBuild.Tests;

public class TailwindCommandLineTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("say \"hello\"", "\"say \\\"hello\\\"\"")]
    [InlineData("C:\\path with spaces\\", "\"C:\\path with spaces\\\\\"")]
    public void QuoteArgument_ShouldRoundTripCommandLineSpecialCharacters(string value, string expected)
    {
        Assert.Equal(expected, TailwindCommandLine.QuoteArgument(value));
    }

    [Fact]
    public void BuildProcessArguments_ShouldPreserveTokenBoundaries()
    {
        var result = TailwindCommandLine.BuildProcessArguments(
            ["--input=C:\\project files\\app.css", "--minify", ""]);

        Assert.Equal("\"--input=C:\\project files\\app.css\" --minify \"\"", result);
    }

    [Fact]
    public void FormatProcessCommand_ShouldQuoteTheExecutableAndAppendRenderedArguments()
    {
        var result = TailwindCommandLine.FormatProcessCommand(
            "C:\\Program Files\\tailwindcss.exe",
            "--minify");

        Assert.Equal("\"C:\\Program Files\\tailwindcss.exe\" --minify", result);
    }

    [Fact]
    public void FormatProcessCommand_WithNoArguments_ShouldReturnOnlyTheExecutable()
    {
        Assert.Equal("tailwindcss", TailwindCommandLine.FormatProcessCommand("tailwindcss", string.Empty));
    }
}
