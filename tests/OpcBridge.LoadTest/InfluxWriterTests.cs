using System.Text.Json;
using Microsoft.Extensions.Options;
using OpcBridge.App;
using OpcBridge.Core;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class InfluxWriterTests
{
    [Fact]
    public void InfluxOptions_Defaults_AreSafe()
    {
        InfluxOptions options = new();
        Assert.False(options.Enabled);
        Assert.Equal("http://localhost:8086", options.Url);
        Assert.Equal(string.Empty, options.Org);
        Assert.Equal(string.Empty, options.Bucket);
        Assert.Null(options.Token);
        Assert.Equal("opc_tags", options.Measurement);
        Assert.Equal(5000, options.TimeoutMs);
        Assert.True(options.VerifySsl);
    }

    [Fact]
    public void TagMapping_InfluxEnabled_JsonRoundTrip()
    {
        TagMapping tag = new()
        {
            SourceId = "default",
            DaItemId = "Random.Int1",
            InfluxEnabled = true
        };

        string json = JsonSerializer.Serialize(tag);
        TagMapping? roundTrip = JsonSerializer.Deserialize<TagMapping>(json);
        Assert.NotNull(roundTrip);
        Assert.True(roundTrip!.InfluxEnabled);
    }

    [Fact]
    public void MappingStore_Preserves_InfluxEnabled()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "mappings.json");
        if (File.Exists(path)) File.Delete(path);

        MappingStore store = new(Options.Create(new BridgeOptions()));
        store.Add(
        [
            new TagMapping
            {
                SourceId = "default",
                DaItemId = "tag.a",
                DisplayName = "A",
                InfluxEnabled = true
            }
        ]);

        (IReadOnlyList<TagMapping> snapshot, _) = store.GetSnapshot();
        TagMapping mapping = Assert.Single(snapshot.Where(m => m.DaItemId == "tag.a"));
        Assert.True(mapping.InfluxEnabled);
    }
}
