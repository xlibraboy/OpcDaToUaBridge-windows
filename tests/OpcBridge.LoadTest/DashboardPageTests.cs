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

    [Fact]
    public void LinkDraft_CanBeClearedWithoutDeletingSavedRule()
    {
        Assert.Contains("id=\"btnClearLinkSelection\"", DashboardPage.Html);
        Assert.Contains(">Clear Selection<", DashboardPage.Html);
        Assert.Contains(">Delete Saved Link<", DashboardPage.Html);
        Assert.Contains("function clearLinkDraftSelection()", DashboardPage.Script);
        Assert.Contains("state.linkDraft.consumer = null", DashboardPage.Script);
        Assert.Contains("state.linkDraft.provider = null", DashboardPage.Script);
    }

    [Fact]
    public void Html_ContainsAppsPill()
    {
        Assert.Contains("id=\"pApps\"", DashboardPage.Html);
        Assert.Contains("Apps", DashboardPage.Html);
    }

    [Fact]
    public void Script_UpdatesAppsPillFromDetectedCount()
    {
        Assert.Contains("pApps", DashboardPage.Script);
        Assert.Contains("detectedCount", DashboardPage.Script);
    }
}
