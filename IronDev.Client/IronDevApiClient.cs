using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IronDev.Core.Auth;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Workflow;
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

    public Task<IronDevApiResponse<JsonElement?>> PingAsync(CancellationToken cancellationToken = default)
        => GetJsonEnvelopeAsync("health", cancellationToken);

    public Task<IronDevApiResponse<JsonElement?>> ListAgentRunsAsync(
        AgentRunListQuery query,
        CancellationToken cancellationToken = default)
        => GetJsonEnvelopeAsync(BuildAgentRunListPath(query), cancellationToken);

    public Task<IronDevApiResponse<JsonElement?>> GetAgentRunAsync(
        int projectId,
        string agentRunId,
        CancellationToken cancellationToken = default)
        => GetJsonEnvelopeAsync($"api/v1/agent-runs/{Uri.EscapeDataString(agentRunId)}?projectId={projectId}", cancellationToken);

    public Task<IronDevApiResponse<JsonElement?>> GetAgentRunAuditAsync(
        int projectId,
        string agentRunId,
        CancellationToken cancellationToken = default)
        => GetJsonEnvelopeAsync($"api/v1/agent-runs/{Uri.EscapeDataString(agentRunId)}/audit?projectId={projectId}", cancellationToken);

    public Task<IronDevApiResponse<JsonElement?>> CreateManualCriticReviewAsync(
        ManualCriticReviewCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var summary = string.IsNullOrWhiteSpace(request.Focus)
            ? $"Manual critic review for agent run {request.TargetAgentRunId}."
            : request.Focus.Trim();
        var content = string.IsNullOrWhiteSpace(request.Reason)
            ? "Manual critic review requested from the IronDev CLI. This creates advisory critic evidence only."
            : request.Reason.Trim();
        var context = string.IsNullOrWhiteSpace(request.ReviewKind)
            ? "Manual critic CLI request. Critic review is not governance, approval, source apply, memory promotion, or tool execution."
            : $"Review kind: {request.ReviewKind.Trim()}. Critic review is not governance, approval, source apply, memory promotion, or tool execution.";

        var body = new
        {
            projectId = request.ProjectId,
            subjectType = "AgentRun",
            subjectId = request.TargetAgentRunId,
            summary,
            content,
            evidenceRefs = request.EvidenceRefs.Count == 0
                ? [$"agent-run:{request.TargetAgentRunId}"]
                : request.EvidenceRefs,
            context,
            severityHint = "Medium",
            correlationId = request.CorrelationId
        };

        return PostJsonEnvelopeAsync("api/v1/manual-critic/reviews", body, cancellationToken);
    }

    public Task<IronDevApiResponse<JsonElement?>> GetManualCriticReviewAsync(
        int projectId,
        string agentRunId,
        CancellationToken cancellationToken = default)
        => GetJsonEnvelopeAsync($"api/v1/manual-critic/reviews/{Uri.EscapeDataString(agentRunId)}?projectId={projectId}", cancellationToken);

    public Task<IronDevApiResponse<JsonElement?>> CreateManualMemoryImprovementAsync(
        ManualMemoryImprovementCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var summary = string.IsNullOrWhiteSpace(request.Focus)
            ? $"Manual memory-improvement proposal for agent run {request.TargetAgentRunId}."
            : request.Focus.Trim();
        var content = string.IsNullOrWhiteSpace(request.Reason)
            ? "Manual memory-improvement proposal requested from the IronDev CLI. This creates proposal-only evidence for human review."
            : request.Reason.Trim();

        var body = new
        {
            projectId = request.ProjectId,
            sourceType = "AgentRunAuditEnvelope",
            sourceId = request.TargetAgentRunId,
            summary,
            content,
            evidenceRefs = request.EvidenceRefs.Count == 0
                ? [$"agent-run-audit:{request.TargetAgentRunId}"]
                : request.EvidenceRefs,
            context = "Manual memory-improvement CLI request. Proposal-only evidence for human review.",
            candidateType = "RepeatedManualCorrection",
            correlationId = request.CorrelationId
        };

        return PostJsonEnvelopeAsync("api/v1/manual-memory-improvements", body, cancellationToken);
    }

    public Task<IronDevApiResponse<JsonElement?>> GetManualMemoryImprovementAsync(
        int projectId,
        string agentRunId,
        CancellationToken cancellationToken = default)
        => GetJsonEnvelopeAsync($"api/v1/manual-memory-improvements/{Uri.EscapeDataString(agentRunId)}?projectId={projectId}", cancellationToken);

    public Task<IronDevApiResponse<JsonElement?>> CreateToolRequestAsync(
        ToolRequestCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var summary = string.IsNullOrWhiteSpace(request.Summary)
            ? $"Tool request form for {request.ToolKind}."
            : request.Summary.Trim();

        var payload = new
        {
            runId = request.RunId,
            inputRefs = request.InputRefs,
            policyRefs = request.PolicyRefs,
            riskLevel = request.RiskLevel,
            dryRunRequired = request.DryRunRequired
        };

        var body = new
        {
            projectId = request.ProjectId,
            requestedTool = request.ToolKind,
            requestKind = request.RequestKind,
            summary,
            payload,
            evidenceRefs = request.EvidenceRefs,
            correlationId = request.CorrelationId,
            reason = request.Reason,
            requestedByAgentRunId = request.RunId
        };

        return PostJsonEnvelopeAsync("api/v1/tool-requests", body, cancellationToken);
    }

    public Task<IronDevApiResponse<JsonElement?>> GetToolRequestAsync(
        int projectId,
        string toolRequestId,
        CancellationToken cancellationToken = default)
        => GetJsonEnvelopeAsync($"api/v1/tool-requests/{Uri.EscapeDataString(toolRequestId)}?projectId={projectId}", cancellationToken);

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

    public Task<TicketBuildRunDto> StartTicketBuildRunAsync(
        int projectId,
        long ticketId,
        StartTicketBuildRunRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<StartTicketBuildRunRequest, TicketBuildRunDto>($"api/projects/{projectId}/tickets/{ticketId}/build-runs", request, cancellationToken);

    public Task<SaveDiscussionResponse> SaveDiscussionAsync(
        int projectId,
        SaveDiscussionRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<SaveDiscussionRequest, SaveDiscussionResponse>($"api/projects/{projectId}/discussions", request, cancellationToken);

    public Task<IReadOnlyList<BuildScenario>> GetBuildScenariosAsync(
        int projectId,
        CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<BuildScenario>>($"api/projects/{projectId}/code-scenarios", cancellationToken);

    public Task<CreateTicketFromDocumentResponse> CreateTicketFromDocumentAsync(
        int projectId,
        long documentVersionId,
        CreateTicketFromDocumentRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<CreateTicketFromDocumentRequest, CreateTicketFromDocumentResponse>($"api/projects/{projectId}/documents/{documentVersionId}/tickets", request, cancellationToken);

    public Task<RunTicketReviewResponse> ReviewTicketAsync(
        int projectId,
        long ticketId,
        RunTicketReviewRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<RunTicketReviewRequest, RunTicketReviewResponse>($"api/projects/{projectId}/tickets/{ticketId}/review", request, cancellationToken);

    public Task<StartDisposableCodeRunResponse> StartDisposableCodeRunAsync(
        int projectId,
        long ticketId,
        StartDisposableCodeRunRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<StartDisposableCodeRunRequest, StartDisposableCodeRunResponse>($"api/projects/{projectId}/tickets/{ticketId}/disposable-code-runs", request, cancellationToken);

    public Task<RunReviewPackage> GetRunReviewPackageAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default)
        => GetAsync<RunReviewPackage>($"api/projects/{projectId}/tickets/{ticketId}/build-runs/{Uri.EscapeDataString(runId)}/review-package", cancellationToken);

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

    private async Task<IronDevApiResponse<JsonElement?>> GetJsonEnvelopeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var warnings = ExtractStringArray(body, "warnings");
        var errors = response.IsSuccessStatusCode
            ? Array.Empty<IronDevApiError>()
            : ExtractErrors(body, response);
        var status = ExtractStatus(body, response.IsSuccessStatusCode ? "succeeded" : "failed");
        var data = TryParseJson(body);

        return new IronDevApiResponse<JsonElement?>(
            response.IsSuccessStatusCode,
            (int)response.StatusCode,
            status,
            data,
            warnings,
            errors,
            body);
    }

    private async Task<IronDevApiResponse<JsonElement?>> PostJsonEnvelopeAsync<TRequest>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken).ConfigureAwait(false);
        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var warnings = ExtractStringArray(body, "warnings");
        var errors = response.IsSuccessStatusCode
            ? Array.Empty<IronDevApiError>()
            : ExtractErrors(body, response);
        var status = ExtractStatus(body, response.IsSuccessStatusCode ? "succeeded" : "failed");
        var data = TryParseJson(body);

        return new IronDevApiResponse<JsonElement?>(
            response.IsSuccessStatusCode,
            (int)response.StatusCode,
            status,
            data,
            warnings,
            errors,
            body);
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

    private static JsonElement? TryParseJson(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ExtractStringArray(string? body, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>();
            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value);
                }
            }

            return values;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<IronDevApiError> ExtractErrors(string? body, HttpResponseMessage response)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("errors", out var errors) &&
                    errors.ValueKind == JsonValueKind.Array)
                {
                    var values = new List<IronDevApiError>();
                    foreach (var item in errors.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            var code = item.TryGetProperty("code", out var codeProperty) && codeProperty.ValueKind == JsonValueKind.String
                                ? codeProperty.GetString()
                                : null;
                            var message = item.TryGetProperty("message", out var messageProperty) && messageProperty.ValueKind == JsonValueKind.String
                                ? messageProperty.GetString()
                                : null;

                            values.Add(new IronDevApiError(
                                string.IsNullOrWhiteSpace(code) ? "IRONDEV_API_NON_SUCCESS" : code,
                                string.IsNullOrWhiteSpace(message) ? $"IronDev API returned {(int)response.StatusCode} {response.ReasonPhrase}." : message));
                        }
                    }

                    if (values.Count > 0)
                        return values;
                }
            }
            catch (JsonException)
            {
            }
        }

        return
        [
            new IronDevApiError(
                "IRONDEV_API_NON_SUCCESS",
                $"IronDev API returned {(int)response.StatusCode} {response.ReasonPhrase}.")
        ];
    }

    private static string ExtractStatus(string? body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
            return fallback;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String)
            {
                var value = status.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch (JsonException)
        {
        }

        return fallback;
    }

    private static string BuildAgentRunListPath(AgentRunListQuery query)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("projectId", query.ProjectId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        Add(parameters, "agentId", query.AgentId);
        Add(parameters, "agentKind", query.AgentKind);
        Add(parameters, "status", query.Status);
        Add(parameters, "triggerType", query.TriggerType);
        Add(parameters, "createdAfterUtc", query.CreatedAfterUtc);
        Add(parameters, "createdBeforeUtc", query.CreatedBeforeUtc);
        Add(parameters, "runId", query.RunId);
        Add(parameters, "correlationId", query.CorrelationId);
        Add(parameters, "take", query.Take?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(parameters, "skip", query.Skip?.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return "api/v1/agent-runs?" + string.Join("&", parameters.Select(parameter =>
            $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
    }

    private static void Add(List<KeyValuePair<string, string>> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parameters.Add(new KeyValuePair<string, string>(name, value));
    }
}
