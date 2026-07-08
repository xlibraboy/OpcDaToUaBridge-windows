using Microsoft.Extensions.Options;
using OpcBridge.App;
using OpcBridge.Core;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class DaLinkStoreTests
{
    // DaLinkStore persists to a fixed file under AppContext.BaseDirectory. Clear it before each
    // test so a prior run cannot leak rules into the snapshot under test.
    private static DaLinkStore CreateStore()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "links.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return new DaLinkStore(Options.Create(new BridgeOptions()));
    }

    [Fact]
    public void MigrateFromMappings_CreatesOneRulePerLegacyProviderLink()
    {
        var mappings = new[]
        {
            new TagMapping
            {
                SourceId = "consumerA",
                DaItemId = "itemA",
                ProviderSourceId = "providerA",
                ProviderDaItemId = "itemP"
            }
        };

        DaLinkStore store = CreateStore();

        int migrated = store.MigrateFromMappings(mappings);
        (IReadOnlyList<DaLinkRule> rules, _) = store.GetSnapshot();

        Assert.Equal(1, migrated);
        DaLinkRule rule = Assert.Single(rules);
        Assert.Equal("providerA", rule.ProviderSourceId);
        Assert.Equal("itemP", rule.ProviderItemId);
        Assert.Equal("consumerA", rule.ConsumerSourceId);
        Assert.Equal("itemA", rule.ConsumerItemId);
    }

    [Fact]
    public void TryAdd_RejectsSecondProviderForSameConsumer()
    {
        DaLinkStore store = CreateStore();

        Assert.True(store.TryAdd(
            new DaLinkRule(Guid.NewGuid(), "providerA", "itemP1", "consumerA", "itemA", true, 5, 5),
            out _,
            out _));

        bool ok = store.TryAdd(
            new DaLinkRule(Guid.NewGuid(), "providerB", "itemP2", "consumerA", "itemA", true, 5, 5),
            out _,
            out string? error);

        Assert.False(ok);
        Assert.Equal("Consumer already has a provider.", error);
    }
}
