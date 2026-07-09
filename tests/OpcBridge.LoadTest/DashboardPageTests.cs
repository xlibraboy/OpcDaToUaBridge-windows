using OpcBridge.App;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class DashboardPageTests
{
    [Fact]
    public void ScriptAndMarkup_RemoveFaceplateProviderEditing()
    {
        Assert.DoesNotContain("id=\"fpProvider\"", DashboardPage.Html);
        Assert.DoesNotContain("Set up links from a tag's faceplate", DashboardPage.Html);
        Assert.Contains("DA Links", DashboardPage.Html);
        Assert.Contains("/api/da-links", DashboardPage.Script);
    }
}
