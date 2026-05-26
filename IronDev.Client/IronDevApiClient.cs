using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IronDev.Core.Auth;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Data.Models;

namespace IronDev.Client;

public sealed class IronDevApiClient : IIronDevApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public IronDevApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("health", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        => PostAsync<LoginRequest, LoginResponse>("api/auth/login", request, cancellationToken);

    public Task<UserProfileDto> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        => GetAsync<UserProfileDto>("api/auth/me", cancellationToken);

    public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<TenantDto>>("api/tenants", cancellationToken);

    public Task<LoginResponse> SelectTenantAsync(SelectTenantRequest request, CancellationToken cancellationToken = default)
        => PostAsync<SelectTenantRequest, LoginResponse>("api/tenants/select", request, cancellationToken);

    public Task LogoutAsync(CancellationToken cancellationToken = default)
        => PostAsync<object, object>("api/auth/logout", new { }, cancellationToken);

    public Task<ProjectTicket> CreateTicketAsync(
        int projectId,
        CreateProjectTicketRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<CreateProjectTicketRequest, ProjectTicket>($"api/projects/{projectId}/tickets", request, cancellationToken);

    public Task<IReadOnlyList<ProjectTicket>> GetTicketsAsync(
        int projectId,
        int take = 50,
        CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<ProjectTicket>>($"api/projects/{projectId}/tickets?take={take}", cancellationToken);

    public Task<ProjectTicket?> GetProjectTicketAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken = default)
        => GetAsync<ProjectTicket?>($"api/projects/{projectId}/tickets/{ticketId}", cancellationToken);

    public Task<ProjectTicket> ImportExternalTicketAsync(
        int projectId,
        ImportExternalTicketRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<ImportExternalTicketRequest, ProjectTicket>($"api/projects/{projectId}/tickets/import-external", request, cancellationToken);

    public Task<RunStatusDto> GetRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
        => GetAsync<RunStatusDto>($"api/runs/{Uri.EscapeDataString(runId)}", cancellationToken);

    public Task<RunReportDto> GetRunReportAsync(
        string runId,
        CancellationToken cancellationToken = default)
        => GetAsync<RunReportDto>($"api/runs/{Uri.EscapeDataString(runId)}/report", cancellationToken);

    public async IAsyncEnumerable<RunEventDto> StreamRunEventsAsync(
        string runId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync($"api/runs/{Uri.EscapeDataString(runId)}/events", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        string? eventType = null;
        var data = new List<string>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfUnsuccessful(response, body);

        if (typeof(T) == typeof(object))
            return (T)new object();

        if (string.IsNullOrWhiteSpace(body))
            throw new IronDevApiException(response.StatusCode, "IronDev API returned an empty response body.");

        var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
        return result ?? throw new IronDevApiException(response.StatusCode, "IronDev API returned a null or invalid response body.", body);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfUnsuccessful(response, body);
    }

    private static void ThrowIfUnsuccessful(HttpResponseMessage response, string? body)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = string.IsNullOrWhiteSpace(body)
            ? $"IronDev API returned {(int)response.StatusCode} {response.ReasonPhrase}."
            : $"IronDev API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}";

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
