using IronDev.Data.Models;

namespace IronDev.Client.Memory;

public interface IMemoryApiClient
{
    Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default);
    Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectContextDocument>> GetContextDocumentsAsync(int projectId, string? documentType = null, string? status = null, string? searchText = null, int take = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectContextDocument>> GetRelevantContextDocumentsAsync(int projectId, string query, int take = 20, CancellationToken cancellationToken = default);
    Task<ProjectContextDocument?> GetContextDocumentByIdAsync(long documentId, CancellationToken cancellationToken = default);
    Task<long> SaveContextDocumentAsync(ProjectContextDocument document, CancellationToken cancellationToken = default);
    Task<bool> ArchiveContextDocumentAsync(long documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectImplementationPlan>> GetRecentPlansAsync(int projectId, int take = 10, CancellationToken cancellationToken = default);
    Task<ProjectImplementationPlan?> GetPlanByIdAsync(long planId, CancellationToken cancellationToken = default);
    Task<ProjectImplementationPlan?> GetPlanByTicketIdAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<long> SavePlanAsync(ProjectImplementationPlan plan, CancellationToken cancellationToken = default);
    Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectRule>> GetProjectRulesAsync(int projectId, CancellationToken cancellationToken = default);
    Task<long> SaveProjectRuleAsync(ProjectRule rule, CancellationToken cancellationToken = default);
}
