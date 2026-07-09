using System.Text.Json;
using Microsoft.Extensions.Options;
using OpcBridge.App;
using OpcBridge.Core;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class ConnectedTagsTests
{
    private static DaLinkStore CreateLinkStore()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "links.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return new DaLinkStore(Options.Create(new BridgeOptions()));
    }

    [Fact]
    public void MappingRequestDto_DoesNotExposeProviderEditFields()
    {
        Assert.Null(typeof(MappingTagDto).GetProperty("ProviderSourceId"));
        Assert.Null(typeof(MappingTagDto).GetProperty("ProviderDaItemId"));
    }

    [Fact]
    public void LegacyProviderFields_RoundTripForMigrationCompatibility()
    {
        var tag = new TagMapping
        {
            SourceId = "consumerA",
            DaItemId = "itemC",
            ProviderSourceId = "providerA",
            ProviderDaItemId = "itemP"
        };

        string json = JsonSerializer.Serialize(tag);
        Assert.Contains("\"ProviderSourceId\"", json);
        Assert.Contains("\"ProviderDaItemId\"", json);

        TagMapping? roundTripped = JsonSerializer.Deserialize<TagMapping>(json);
        Assert.NotNull(roundTripped);
        Assert.Equal("providerA", roundTripped!.ProviderSourceId);
        Assert.Equal("itemP", roundTripped.ProviderDaItemId);
    }

    [Fact]
    public void LegacyProviderFields_MigrateIntoDaLinkRules()
    {
        DaLinkStore store = CreateLinkStore();

        int migrated = store.MigrateFromMappings(
            new[]
            {
                new TagMapping
                {
                    SourceId = "consumerA",
                    DaItemId = "itemC",
                    ProviderSourceId = "providerA",
                    ProviderDaItemId = "itemP",
                    Enabled = true
                }
            });

        (IReadOnlyList<DaLinkRule> rules, _) = store.GetSnapshot();

        Assert.Equal(1, migrated);
        DaLinkRule rule = Assert.Single(rules);
        Assert.Equal("providerA", rule.ProviderSourceId);
        Assert.Equal("itemP", rule.ProviderItemId);
        Assert.Equal("consumerA", rule.ConsumerSourceId);
        Assert.Equal("itemC", rule.ConsumerItemId);
    }

    [Fact]
    public void RuntimeIndex_UsesDaLinkRules_NotMappingProviderFields()
    {
        var rules = new[]
        {
            new DaLinkRule(Guid.NewGuid(), "providerA", "itemP", "consumerA", "itemC", true, 5, 5)
        };

        BridgeWorker.SourceMappingCache cache = BridgeWorker.SourceMappingCache.Build(
            mappings: new TagMapping[]
            {
                new()
                {
                    SourceId = "consumerA",
                    DaItemId = "itemC",
                    ProviderSourceId = "legacyProvider",
                    ProviderDaItemId = "legacyItem",
                    AccessRights = TagAccessRights.Write
                }
            },
            rules: rules);

        IReadOnlyList<TagMapping> consumers = cache.GetConsumersByProvider("providerA", "itemP");
        TagMapping consumer = Assert.Single(consumers);
        Assert.Equal("consumerA", consumer.SourceId);
        Assert.Equal("itemC", consumer.DaItemId);

        IReadOnlyList<TagMapping> legacyConsumers = cache.GetConsumersByProvider("legacyProvider", "legacyItem");
        Assert.Empty(legacyConsumers);
    }
}
