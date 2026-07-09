using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using OpcBridge.App;
using OpcBridge.Core;
using Xunit;
namespace OpcBridge.LoadTest;
[Collection(nameof(DaLinkApiAppCollection))]
public sealed class DaLinkApiTests
{
    [Fact]
    public void TryMigrateLegacyDaLinks_LogsWarningAndLeavesStoreUsableOnConflict()
    {
        DeleteIfExists(Path.Combine(AppContext.BaseDirectory, "links.json"));

        DaLinkStore store = new(ToOptions(new BridgeOptions()));
        DashboardLogStore logStore = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(_ => { });

        TagMapping[] legacyMappings =
        {
            new()
            {
                SourceId = "consumerA",
                DaItemId = "itemA",
                ProviderSourceId = "providerA",
                ProviderDaItemId = "itemP1"
            },
            new()
            {
                SourceId = "consumerA",
                DaItemId = "itemA",
                ProviderSourceId = "providerB",
                ProviderDaItemId = "itemP2"
            }
        };

        bool migrated = DaLinkApiHelpers.TryMigrateLegacyDaLinks(
            store,
            legacyMappings,
            logStore,
            loggerFactory.CreateLogger("DaLinkApiTests"),
            out string? warning);

        Assert.False(migrated);
        Assert.Equal("Skipping legacy DA link migration from mappings.json because Consumer already has a provider.", warning);

        IReadOnlyList<DashboardLogEntry> entries = logStore.GetEntries(10, LogLevel.Warning);
        DashboardLogEntry entry = Assert.Single(entries);
        Assert.Contains("Consumer already has a provider.", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Consumer already has a provider.", entry.ExceptionText, StringComparison.Ordinal);

        (IReadOnlyList<DaLinkRule> rules, long version) = store.GetSnapshot();
        Assert.Empty(rules);
        Assert.Equal(0, version);
    }

    [Fact]
    public async Task PutMissingRule_ReturnsNotFoundBeforeMetadataValidation()
    {
        await using TestAppHandle app = await TestAppHandle.StartAsync(static appDirectory =>
        {
            File.WriteAllText(Path.Combine(appDirectory, "mappings.json"), "[]");
            DeleteIfExists(Path.Combine(appDirectory, "links.json"));
        });

        Guid id = Guid.NewGuid();
        using HttpResponseMessage response = await app.Client.PutAsync(
            $"/api/da-links/{id}",
            CreateJsonContent(new UpdateDaLinkRequest(new DaLinkDto(
                Id: id,
                ProviderSourceId: "providerA",
                ProviderItemId: "itemP",
                ConsumerSourceId: "consumerA",
                ConsumerItemId: "itemC",
                Enabled: true,
                ProviderCanonicalType: 5,
                ConsumerCanonicalType: 5))));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Rule not found.", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PostForgedMetadata_ReturnsProviderNotFound()
    {
        await using TestAppHandle app = await TestAppHandle.StartAsync(static appDirectory =>
        {
            File.WriteAllText(Path.Combine(appDirectory, "mappings.json"), "[]");
            DeleteIfExists(Path.Combine(appDirectory, "links.json"));
        });

        using HttpResponseMessage response = await app.Client.PostAsync(
            "/api/da-links",
            CreateJsonContent(new CreateDaLinkRequest(new DaLinkDto(
                Id: Guid.NewGuid(),
                ProviderSourceId: "providerA",
                ProviderItemId: "itemP",
                ConsumerSourceId: "consumerA",
                ConsumerItemId: "itemC",
                Enabled: true,
                ProviderCanonicalType: 5,
                ConsumerCanonicalType: 5,
                ProviderAccessRights: 1,
                ConsumerAccessRights: 3))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Provider tag not found.", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RemoveSource_RemovesDaLinkRulesReferencingThatSource()
    {
        Guid providerRuleId = Guid.NewGuid();
        Guid consumerRuleId = Guid.NewGuid();
        Guid retainedRuleId = Guid.NewGuid();

        await using TestAppHandle app = await TestAppHandle.StartAsync(appDirectory =>
        {
            File.WriteAllText(Path.Combine(appDirectory, "mappings.json"), JsonSerializer.Serialize(new[]
            {
                new TagMapping
                {
                    SourceId = "providerA",
                    DaItemId = "itemP",
                    AccessRights = TagAccessRights.Read,
                    Enabled = true
                },
                new TagMapping
                {
                    SourceId = "consumerA",
                    DaItemId = "itemC",
                    AccessRights = TagAccessRights.Write,
                    Enabled = true
                },
                new TagMapping
                {
                    SourceId = "otherA",
                    DaItemId = "itemO",
                    AccessRights = TagAccessRights.ReadWrite,
                    Enabled = true
                }
            }));

            File.WriteAllText(
                Path.Combine(appDirectory, "sources.json"),
                JsonSerializer.Serialize(new DaRuntimeSettingsSnapshot(
                    1000,
                    true,
                    new[]
                    {
                        new DaSourceRuntimeSettings("providerA", "Provider", string.Empty, "localhost", null, null, null, 1000),
                        new DaSourceRuntimeSettings("consumerA", "Consumer", string.Empty, "localhost", null, null, null, 1000),
                        new DaSourceRuntimeSettings("otherA", "Other", string.Empty, "localhost", null, null, null, 1000)
                    },
                    0)));

            File.WriteAllText(Path.Combine(appDirectory, "links.json"), JsonSerializer.Serialize(new[]
            {
                new DaLinkRule(providerRuleId, "providerA", "itemP", "consumerA", "itemC", true, 5, 5),
                new DaLinkRule(consumerRuleId, "otherA", "itemO", "providerA", "itemP", true, 5, 5),
                new DaLinkRule(retainedRuleId, "otherA", "itemO", "consumerA", "itemC", true, 5, 5)
            }));
        });

        using HttpResponseMessage response = await app.Client.PostAsync(
            "/api/da/sources/remove",
            CreateJsonContent(new DaSourceRemoveRequest("providerA")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument linksBody = await app.GetJsonAsync("/api/da-links");
        JsonElement.ArrayEnumerator links = linksBody.RootElement.GetProperty("links").EnumerateArray();
        List<Guid> remainingIds = new();
        foreach (JsonElement link in links)
        {
            remainingIds.Add(link.GetProperty("id").GetGuid());
        }

        Assert.DoesNotContain(providerRuleId, remainingIds);
        Assert.DoesNotContain(consumerRuleId, remainingIds);
        Assert.Contains(retainedRuleId, remainingIds);
    }

    [Fact]
    public void ValidateLink_RejectsTypeMismatch()
    {
        DaLinkDto request = new(
            Id: Guid.NewGuid(),
            ProviderSourceId: "providerA",
            ProviderItemId: "itemP",
            ConsumerSourceId: "consumerA",
            ConsumerItemId: "itemC",
            Enabled: true,
            ProviderCanonicalType: 5,
            ConsumerCanonicalType: 3);

        string? error = DaLinkValidators.Validate(request, consumerHasProvider: false, providerReadable: true, consumerWritable: true);
        Assert.Equal("Provider and consumer must use the same native OPC DA type.", error);
    }


    private static Microsoft.Extensions.Options.IOptions<BridgeOptions> ToOptions(BridgeOptions options)
    {
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    private static StringContent CreateJsonContent<T>(T value)
    {
        return new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class TestAppHandle : IAsyncDisposable
    {
        private readonly Process process_;
        private readonly string app_directory_;
        private readonly StringBuilder output_ = new();

        private TestAppHandle(Process process, string appDirectory)
        {
            process_ = process;
            app_directory_ = appDirectory;
            Client = new HttpClient
            {
                BaseAddress = new Uri("http://127.0.0.1:8080")
            };
        }

        public HttpClient Client { get; }

        public static async Task<TestAppHandle> StartAsync(Action<string> configureAppDirectory)
        {
            string sourceDirectory = Path.GetDirectoryName(typeof(DaLinkStore).Assembly.Location)
                ?? throw new InvalidOperationException("Could not locate OpcBridge.App output.");
            string appDirectory = Path.Combine(Path.GetTempPath(), "OpcBridge.LoadTest", nameof(DaLinkApiTests), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(appDirectory);

            foreach (string file in Directory.GetFiles(sourceDirectory))
            {
                File.Copy(file, Path.Combine(appDirectory, Path.GetFileName(file)), overwrite: true);
            }

            configureAppDirectory(appDirectory);

            ProcessStartInfo startInfo = new()
            {
                FileName = "dotnet",
                WorkingDirectory = appDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(Path.Combine(appDirectory, "OpcBridge.App.dll"));

            Process process = new() { StartInfo = startInfo };
            TestAppHandle handle = new(process, appDirectory);
            process.OutputDataReceived += (_, args) => handle.AppendOutput(args.Data);
            process.ErrorDataReceived += (_, args) => handle.AppendOutput(args.Data);

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start OpcBridge.App test host.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await handle.WaitForHealthyAsync();
            return handle;
        }

        public async Task<JsonDocument> GetJsonAsync(string path)
        {
            using HttpResponseMessage response = await Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            if (!process_.HasExited)
            {
                process_.Kill(entireProcessTree: true);
                await process_.WaitForExitAsync();
            }

            process_.Dispose();

            try
            {
                Directory.Delete(app_directory_, recursive: true);
            }
            catch
            {
            }
        }

        private void AppendOutput(string? line)
        {
            if (!string.IsNullOrEmpty(line))
            {
                output_.AppendLine(line);
            }
        }

        private async Task WaitForHealthyAsync()
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                if (process_.HasExited)
                {
                    throw new Xunit.Sdk.XunitException($"OpcBridge.App exited during startup with code {process_.ExitCode}.{Environment.NewLine}{output_}");
                }

                try
                {
                    using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(250));
                    using HttpResponseMessage response = await Client.GetAsync("/health", timeout.Token);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                catch
                {
                }

                await Task.Delay(250);
            }

            throw new Xunit.Sdk.XunitException($"Timed out waiting for OpcBridge.App to become healthy.{Environment.NewLine}{output_}");
        }
    }
}

[CollectionDefinition(nameof(DaLinkApiAppCollection), DisableParallelization = true)]
public sealed class DaLinkApiAppCollection;
