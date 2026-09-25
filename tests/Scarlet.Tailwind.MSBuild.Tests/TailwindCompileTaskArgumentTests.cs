namespace Scarlet.Tailwind.MSBuild.Tests;

public class TailwindCompileTaskArgumentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SplitArguments_WithNoArguments_ShouldReturnEmpty(string value)
    {
        Assert.Empty(TailwindCompileTask.SplitArguments(value));
    }

    [Fact]
    public void SplitArguments_ShouldSplitWhitespaceAndPreserveQuotedValues()
    {
        var result = TailwindCompileTask.SplitArguments("--alpha one --path \"directory with spaces\" \"\"");

        Assert.Equal(["--alpha", "one", "--path", "directory with spaces", ""], result);
    }

    [Fact]
    public void SplitArguments_ShouldTreatWhitespaceInsideQuotesAsLiteral()
    {
        var result = TailwindCompileTask.SplitArguments("--value=\"one two\"");

        Assert.Equal(["--value=one two"], result);
    }
}
