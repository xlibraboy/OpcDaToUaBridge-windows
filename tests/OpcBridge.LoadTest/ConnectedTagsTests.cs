using System.Text.Json;
using Microsoft.Extensions.Options;
using OpcBridge.App;
using OpcBridge.Core;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class ConnectedTagsTests
{
    // MappingStore persists to a fixed file under AppContext.BaseDirectory. Clear it before each
    // test so a prior run cannot leak mappings into the snapshot under test.
    private static MappingStore CreateStore()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "mappings.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return new MappingStore(Options.Create(new BridgeOptions()));
    }

    [Fact]
    public void SelfLinkRejected()
    {
        MappingStore store = CreateStore();

        var tag = new TagMapping
        {
            SourceId = "srcA",
            DaItemId = "itemA",
            ProviderSourceId = "srcA",
            ProviderDaItemId = "itemA",
            AccessRights = TagAccessRights.ReadWrite
        };

        store.Add(new[] { tag });
        (IReadOnlyList<TagMapping> mappings, _) = store.GetSnapshot();

        TagMapping stored = Assert.Single(mappings);
        Assert.Null(stored.ProviderSourceId);
        Assert.Null(stored.ProviderDaItemId);
    }

    [Fact]
    public void ValidLinkPreserved()
    {
        MappingStore store = CreateStore();

        var tag = new TagMapping
        {
            SourceId = "consumer",
            DaItemId = "cItem",
            ProviderSourceId = "  srcA  ",
            ProviderDaItemId = "  itemA  ",
            AccessRights = TagAccessRights.Write
        };

        store.Add(new[] { tag });
        (IReadOnlyList<TagMapping> mappings, _) = store.GetSnapshot();

        TagMapping stored = Assert.Single(mappings);
        Assert.Equal("srcA", stored.ProviderSourceId);
        Assert.Equal("itemA", stored.ProviderDaItemId);
    }

    [Fact]
    public void BlankProviderNormalizedToNull()
    {
        MappingStore store = CreateStore();

        var tag = new TagMapping
        {
            SourceId = "srcB",
            DaItemId = "itemB",
            ProviderSourceId = "   ",
            ProviderDaItemId = "\t",
            AccessRights = TagAccessRights.Read
        };

        store.Add(new[] { tag });
        (IReadOnlyList<TagMapping> mappings, _) = store.GetSnapshot();

        TagMapping stored = Assert.Single(mappings);
        Assert.Null(stored.ProviderSourceId);
        Assert.Null(stored.ProviderDaItemId);
    }

    [Fact]
    public void JsonRoundTripIncludesProviderFields()
    {
        var tag = new TagMapping
        {
            SourceId = "consumer",
            DaItemId = "cItem",
            ProviderSourceId = "srcA",
            ProviderDaItemId = "itemA"
        };

        string json = JsonSerializer.Serialize(tag);
        Assert.Contains("\"ProviderSourceId\"", json);
        Assert.Contains("\"ProviderDaItemId\"", json);

        TagMapping? roundTripped = JsonSerializer.Deserialize<TagMapping>(json);
        Assert.NotNull(roundTripped);
        Assert.Equal("srcA", roundTripped!.ProviderSourceId);
        Assert.Equal("itemA", roundTripped.ProviderDaItemId);
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
