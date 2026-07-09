using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpcBridge.App;
using OpcBridge.Core;
using OpcBridge.Da;
using OpcBridge.Ua;
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
    private static BridgeWorker CreateWorker(MappingStore mappingStore, DaLinkStore linkStore)
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(_ => { });
        BridgeState state = new(Options.Create(new BridgeOptions()));
        DaRuntimeSettings settings = new(Options.Create(new DaClientOptions
        {
            Sources =
            [
                new DaSourceOptions { SourceId = "providerA", DisplayName = "Provider A", ProgId = string.Empty, Host = "localhost" },
                new DaSourceOptions { SourceId = "consumerA", DisplayName = "Consumer A", ProgId = string.Empty, Host = "localhost" }
            ]
        }));
        UaServerHost uaServer = new(
            Options.Create(new UaServerOptions()),
            loggerFactory.CreateLogger<UaServerHost>(),
            loggerFactory);

        return new BridgeWorker(
            uaServer,
            state,
            mappingStore,
            linkStore,
            settings,
            new DaClientFactory(),
            Options.Create(new BridgeOptions()),
            loggerFactory.CreateLogger<BridgeWorker>());
    }

    private static void SetPrivateField(object instance, string name, object value)
    {
        FieldInfo? field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void InvokePrivateVoid(object instance, string name, params object?[] args)
    {
        MethodInfo? method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(instance, args);
    }

    [Fact]
    public async Task SubscriptionCallbackValues_ForwardToDaLinkConsumers()
    {
        MappingStore mappingStore = new(Options.Create(new BridgeOptions()));
        mappingStore.SetAll(
        [
            new TagMapping
            {
                SourceId = "providerA",
                DaItemId = "itemP",
                Enabled = true,
                AccessRights = TagAccessRights.Read
            },
            new TagMapping
            {
                SourceId = "consumerA",
                DaItemId = "itemC",
                Enabled = true,
                AccessRights = TagAccessRights.Write
            }
        ]);

        DaLinkStore linkStore = CreateLinkStore();
        Assert.True(linkStore.TryAdd(
            new DaLinkRule(Guid.NewGuid(), "providerA", "itemP", "consumerA", "itemC", true, 5, 5),
            out _,
            out _));

        BridgeWorker worker = CreateWorker(mappingStore, linkStore);
        WriteQueue queue = new();

        SetPrivateField(worker, "write_queue_", queue);
        SetPrivateField(worker, "source_mapping_cache_", BridgeWorker.SourceMappingCache.Build(mappingStore.GetSnapshot().Mappings, linkStore.GetSnapshot().Rules));

        BridgeValue value = new("providerA", "itemP", 42.0, DateTime.UtcNow, 192, true);
        InvokePrivateVoid(worker, "OnSubscriptionValues", new List<BridgeValue> { value });

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        await using IAsyncEnumerator<WriteRequest> reader = queue.ReaderAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());

        WriteRequest request = reader.Current;
        Assert.Equal("consumerA", request.SourceId);
        Assert.Equal("itemC", request.DaItemId);
        Assert.Equal(42.0, Assert.IsType<double>(request.Value));
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
