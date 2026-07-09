using OpcBridge.App;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class HelpContentTests
{
    [Fact]
    public void HelpText_DescribesDaLinksAsIndependentSubsystem()
    {
        Assert.Contains("DA Links", HelpContent.Markdown);
        Assert.Contains("separate subsystem", HelpContent.Markdown);
        Assert.DoesNotContain("faceplate → Setup → Provider", HelpContent.Markdown);
    }
}
