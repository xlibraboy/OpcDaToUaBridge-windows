using OpcBridge.App;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class DashboardPageTests
{
    [Fact]
    public void Html_UsesDedicatedDaLinksBrowseWorkflow()
    {
        Assert.DoesNotContain("id=\"fpProvider\"", DashboardPage.Html);
        Assert.DoesNotContain("Set up links from a tag's faceplate", DashboardPage.Html);
        Assert.DoesNotContain("id=\"linkConsumerSelect\"", DashboardPage.Html);
        Assert.DoesNotContain("id=\"linkProviderSelect\"", DashboardPage.Html);
        Assert.Contains("DA Links", DashboardPage.Html);
        Assert.Contains("id=\"linkSourceStatus\"", DashboardPage.Html);
        Assert.Contains("id=\"linkBrowseTree\"", DashboardPage.Html);
    }

    [Fact]
    public void Script_BrowsesDaTagsForLinksInsteadOfReusingMappings()
    {
        Assert.Contains("/api/da-links", DashboardPage.Script);
        Assert.Contains("function browseLinkTags(", DashboardPage.Script);
        Assert.Contains("state.linkDraft", DashboardPage.Script);
        Assert.Contains("data-action=\"pick-link-consumer\"", DashboardPage.Script);
        Assert.Contains("data-action=\"pick-link-provider\"", DashboardPage.Script);
        Assert.DoesNotContain("el('linkConsumerSelect').innerHTML = opts;", DashboardPage.Script);
        Assert.DoesNotContain("el('linkProviderSelect').innerHTML = opts;", DashboardPage.Script);
        Assert.DoesNotContain("const opts = '<option value=\"\">— select —</option>' + mappings.map", DashboardPage.Script);
    }
}
