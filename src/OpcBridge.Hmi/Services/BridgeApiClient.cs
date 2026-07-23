using System.Net.Http.Json;
using System.Text.Json;
using OpcBridge.Client;

namespace OpcBridge.Hmi.Services;

public sealed class BridgeApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private HttpClient client_ = new();

    public void SetBaseAddress(string baseUrl)
    {
        client_.Dispose();
        client_ = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    }

    public async Task<HmiTagsResponse> GetTagsAsync(CancellationToken ct)
    {
        HmiTagsResponse? response = await client_.GetFromJsonAsync<HmiTagsResponse>("api/hmi/tags", JsonOptions, ct)
            .ConfigureAwait(false);
        return response ?? new HmiTagsResponse();
    }

    public async Task<HmiWriteResponse> WriteAsync(HmiWriteRequest request, CancellationToken ct)
    {
        using HttpResponseMessage http = await client_.PostAsJsonAsync("api/hmi/write", request, JsonOptions, ct)
            .ConfigureAwait(false);
        HmiWriteResponse? body = await http.Content.ReadFromJsonAsync<HmiWriteResponse>(JsonOptions, ct)
            .ConfigureAwait(false);
        return body ?? new HmiWriteResponse { Ok = false, Error = $"HTTP {(int)http.StatusCode}" };
    }

    public void Dispose() => client_.Dispose();
}
