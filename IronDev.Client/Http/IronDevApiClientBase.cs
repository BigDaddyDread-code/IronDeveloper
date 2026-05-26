using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IronDev.Core.RunReports;

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

    protected async IAsyncEnumerable<RunEventDto> StreamSseRunEventsAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? eventType = null;
        var data = new List<string>();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;

            if (line.Length == 0)
            {
                var parsed = TryParseRunEvent(eventType, data);
                if (parsed is not null)
                    yield return parsed;

                eventType = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventType = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                data.Add(line["data:".Length..].TrimStart());
            }
        }

        var trailing = TryParseRunEvent(eventType, data);
        if (trailing is not null)
            yield return trailing;
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

    private static RunEventDto? TryParseRunEvent(string? eventType, IReadOnlyCollection<string> data)
    {
        if (data.Count == 0)
            return null;

        var json = string.Join('\n', data);
        var parsed = JsonSerializer.Deserialize<RunEventDto>(json, JsonOptions);
        if (parsed is null || !string.IsNullOrWhiteSpace(parsed.EventType) || string.IsNullOrWhiteSpace(eventType))
            return parsed;

        return parsed with { EventType = eventType };
    }
}
