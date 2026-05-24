using System.Net.Http.Json;
using System.Text.Json;

namespace IronDev.Client.Http;

public abstract class IronDevApiClientBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    protected readonly HttpClient Http;

    protected IronDevApiClientBase(HttpClient http)
    {
        Http = http;
    }

    protected async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(path, ct);
        return await ReadAsync<T>(response, ct);
    }

    protected async Task<T> PostAsync<T>(string path, object? body, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync(path, body, JsonOptions, ct);
        return await ReadAsync<T>(response, ct);
    }

    protected async Task<T> PutAsync<T>(string path, object? body, CancellationToken ct = default)
    {
        using var response = await Http.PutAsJsonAsync(path, body, JsonOptions, ct);
        return await ReadAsync<T>(response, ct);
    }

    protected async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var response = await Http.DeleteAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
    }

    protected async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return value ?? throw new IronDevApiException(response.StatusCode, "API returned an empty response.");
    }

    protected static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = response.Content is null ? null : await response.Content.ReadAsStringAsync(ct);
        var message = string.IsNullOrWhiteSpace(body)
            ? $"IronDev API request failed with {(int)response.StatusCode} {response.ReasonPhrase}."
            : body;

        throw new IronDevApiException(response.StatusCode, message, body);
    }
}
