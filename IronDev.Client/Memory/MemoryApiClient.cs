using System.Net;
using IronDev.Client.Http;
using IronDev.Data.Models;

namespace IronDev.Client.Memory;

public sealed class MemoryApiClient : IronDevApiClientBase, IMemoryApiClient
{
    public MemoryApiClient(HttpClient http)
        : base(http)
    {
    }

    public Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default) =>
        GetAsync<ProjectSummary?>($"projects/{projectId}/memory/summary", cancellationToken);

    public Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ProjectDecision>>($"projects/{projectId}/memory/decisions?take={take}", cancellationToken);

    public Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default) =>
        PostAsync<long>($"projects/{summary.ProjectId}/memory/summary", summary, cancellationToken);

    public Task<IReadOnlyList<ProjectContextDocument>> GetContextDocumentsAsync(int projectId, string? documentType = null, string? status = null, string? searchText = null, int take = 50, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ProjectContextDocument>>($"projects/{projectId}/memory/documents?documentType={WebUtility.UrlEncode(documentType)}&status={WebUtility.UrlEncode(status)}&searchText={WebUtility.UrlEncode(searchText)}&take={take}", cancellationToken);

    public Task<IReadOnlyList<ProjectContextDocument>> GetRelevantContextDocumentsAsync(int projectId, string query, int take = 20, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ProjectContextDocument>>($"projects/{projectId}/memory/search?q={WebUtility.UrlEncode(query)}&take={take}", cancellationToken);

    public Task<ProjectContextDocument?> GetContextDocumentByIdAsync(long documentId, CancellationToken cancellationToken = default) =>
        GetAsync<ProjectContextDocument?>($"memory/documents/{documentId}", cancellationToken);

    public Task<long> SaveContextDocumentAsync(ProjectContextDocument document, CancellationToken cancellationToken = default) =>
        PostAsync<long>($"projects/{document.ProjectId}/memory/documents", document, cancellationToken);

    public async Task<bool> ArchiveContextDocumentAsync(long documentId, CancellationToken cancellationToken = default)
    {
        await DeleteAsync($"memory/documents/{documentId}", cancellationToken);
        return true;
    }

    public Task<IReadOnlyList<ProjectImplementationPlan>> GetRecentPlansAsync(int projectId, int take = 10, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ProjectImplementationPlan>>($"projects/{projectId}/memory/plans?take={take}", cancellationToken);

    public Task<ProjectImplementationPlan?> GetPlanByIdAsync(long planId, CancellationToken cancellationToken = default) =>
        GetAsync<ProjectImplementationPlan?>($"memory/plans/{planId}", cancellationToken);

    public Task<ProjectImplementationPlan?> GetPlanByTicketIdAsync(long ticketId, CancellationToken cancellationToken = default) =>
        GetAsync<ProjectImplementationPlan?>($"tickets/{ticketId}/implementation-plan", cancellationToken);

    public Task<long> SavePlanAsync(ProjectImplementationPlan plan, CancellationToken cancellationToken = default) =>
        PostAsync<long>($"projects/{plan.ProjectId}/memory/plans", plan, cancellationToken);

    public Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken cancellationToken = default) =>
        PostAsync<long>($"projects/{decision.ProjectId}/memory/decisions", decision, cancellationToken);

    public Task<IReadOnlyList<ProjectRule>> GetProjectRulesAsync(int projectId, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ProjectRule>>($"projects/{projectId}/memory/rules", cancellationToken);

    public Task<long> SaveProjectRuleAsync(ProjectRule rule, CancellationToken cancellationToken = default) =>
        PostAsync<long>($"projects/{rule.ProjectId}/memory/rules", rule, cancellationToken);
}
