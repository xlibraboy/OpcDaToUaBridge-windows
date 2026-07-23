using System.Net;
using System.Text.Json;
using Xunit;

namespace OpcBridge.LoadTest;

[Collection(nameof(DaLinkApiAppCollection))]
public sealed class HmiApiTests
{
    private static void WriteAppsettings(string dir, object? mappings = null)
    {
        var appsettings = new
        {
            Da = new { ProgId = "Matrikon.OPC.Simulation.1", Host = "localhost", UpdateRateMs = 1000, UseSubscriptions = true },
            Ua = new { ApplicationName = "OpcDaToUaBridge", EndpointUrl = "opc.tcp://0.0.0.0:4840/OpcBridge", AutoAcceptUntrustedCertificates = true, RequireAuthentication = false, Username = "", Password = "", AllowedIpAddresses = Array.Empty<string>() },
            Bridge = new
            {
                RateLimits = new { },
                ExpectedTagCount = 100,
                Mappings = mappings ?? new object[]
                {
                    new
                    {
                        SourceId = "default",
                        DaItemId = "Random.Int1",
                        DisplayName = "Int1",
                        DataType = "Int32",
                        UaNodeId = "",
                        Enabled = true,
                        Mode = "Source",
                        Writeable = true,
                        AccessRights = "Read-Write"
                    },
                    new
                    {
                        SourceId = "default",
                        DaItemId = "Random.Real4",
                        DisplayName = "Real4",
                        DataType = "Float",
                        UaNodeId = "",
                        Enabled = true,
                        Mode = "Source",
                        Writeable = false,
                        AccessRights = "Read"
                    },
                    new
                    {
                        SourceId = "default",
                        DaItemId = "Disabled.Tag",
                        DisplayName = "Disabled",
                        DataType = "Int32",
                        UaNodeId = "",
                        Enabled = false,
                        Mode = "Source",
                        Writeable = true,
                        AccessRights = "Read-Write"
                    }
                }
            },
            Mqtt = new { Enabled = false, BrokerUrl = "tcp://localhost:1883", ClientId = "OpcDaToUaBridge", UserName = (string?)null, Password = (string?)null, Tls = false, IgnoreCertErrors = false, TopicPrefix = "bridge/tags", PayloadFields = "Value, Timestamp" }
        };
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), JsonSerializer.Serialize(appsettings, new JsonSerializerOptions { WriteIndented = true }));
        // Ensure seed mappings are used: no pre-existing mappings.json
        string mapPath = Path.Combine(dir, "mappings.json");
        if (File.Exists(mapPath)) File.Delete(mapPath);
    }

    [Fact]
    public async Task HmiTags_ReturnsEnabledMappingsOnly()
    {
        await using var handle = await TestAppHandle.StartAsync(dir => WriteAppsettings(dir));

        using var doc = await handle.GetJsonAsync("/api/hmi/tags");
        Assert.True(doc.RootElement.TryGetProperty("version", out var version));
        Assert.True(version.GetInt64() >= 0);
        Assert.True(doc.RootElement.TryGetProperty("tags", out var tags));
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);

        var list = tags.EnumerateArray().ToList();
        Assert.Equal(2, list.Count); // disabled excluded

        var int1 = list.Single(t => t.GetProperty("daItemId").GetString() == "Random.Int1");
        Assert.Equal("default", int1.GetProperty("sourceId").GetString());
        Assert.Equal("Int1", int1.GetProperty("displayName").GetString());
        Assert.True(int1.GetProperty("writeable").GetBoolean());

        var real4 = list.Single(t => t.GetProperty("daItemId").GetString() == "Random.Real4");
        Assert.False(real4.GetProperty("writeable").GetBoolean());
    }
}
