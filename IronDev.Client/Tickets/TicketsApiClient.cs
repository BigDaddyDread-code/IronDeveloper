using IronDev.Client.Http;
using IronDev.Core.Builder;
using IronDev.Core.Models;
using IronDev.Core.Workflow;
using IronDev.Data.Models;

namespace IronDev.Client.Tickets;

public sealed class TicketsApiClient : IronDevApiClientBase, ITicketsApiClient
{
    public TicketsApiClient(HttpClient http)
        : base(http)
    {
    }

    public Task<ProjectTicket> CreateTicketAsync(
        int projectId,
        CreateProjectTicketRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<ProjectTicket>($"projects/{projectId}/tickets", request, cancellationToken);

    public Task<ProjectTicket> ImportExternalTicketAsync(
        int projectId,
        ImportExternalTicketRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<ProjectTicket>($"projects/{projectId}/tickets/import-external", request, cancellationToken);

    public Task<ProjectTicket> GenerateTicketFromDiscussionAsync(
        int projectId,
        GenerateTicketFromDiscussionRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<ProjectTicket>($"projects/{projectId}/tickets/generate-from-discussion", request, cancellationToken);

    public async Task<long> SaveTicketAsync(ProjectTicket ticket, CancellationToken cancellationToken = default)
    {
        var saved = await PostAsync<ProjectTicket>($"projects/{ticket.ProjectId}/tickets/legacy", ticket, cancellationToken);
        return saved.Id;
    }

    public Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ProjectTicket>>($"projects/{projectId}/tickets?take={take}", cancellationToken);

    public Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken cancellationToken = default) =>
        GetAsync<ProjectTicket?>($"tickets/{ticketId}", cancellationToken);

    public async Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        await DeleteAsync($"tickets/{ticketId}", cancellationToken);
        return true;
    }

    public Task<DraftTicket> GenerateDraftAsync(
        int projectId,
        string projectName,
        string proposedTitle,
        string messageText,
        string? linkedFilePaths,
        string? linkedSymbols,
        long? sessionId = null,
        CancellationToken ct = default) =>
        PostAsync<DraftTicket>($"projects/{projectId}/tickets/draft", new
        {
            projectName,
            proposedTitle,
            messageText,
            linkedFilePaths,
            linkedSymbols,
            sessionId
        }, ct);

    public Task<DraftTicket> GeneratePlanAsync(int projectId, DraftTicket current, CancellationToken ct = default) =>
        PostAsync<DraftTicket>($"projects/{projectId}/tickets/draft/plan", current, ct);

    public Task<DraftTicket> RegenerateTestsAsync(int projectId, DraftTicket current, CancellationToken ct = default) =>
        PostAsync<DraftTicket>($"projects/{projectId}/tickets/draft/tests", current, ct);

    public Task<CodebaseTicketGenerationResult> GenerateTicketsAsync(int projectId, CancellationToken cancellationToken = default) =>
        PostAsync<CodebaseTicketGenerationResult>($"projects/{projectId}/tickets/generate-from-codebase", new { }, cancellationToken);

    public Task<TicketBuildPreview> CreateBuildPreviewAsync(int projectId, long ticketId, CancellationToken cancellationToken = default) =>
        PostAsync<TicketBuildPreview>($"projects/{projectId}/tickets/{ticketId}/build-preview", new { }, cancellationToken);

    public Task<TicketBuildRunDto> StartTicketBuildRunAsync(
        int projectId,
        long ticketId,
        StartTicketBuildRunRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<TicketBuildRunDto>($"projects/{projectId}/tickets/{ticketId}/build-runs", request, cancellationToken);

    public Task<BuildReadinessResult> EvaluateReadinessAsync(int projectId, long ticketId, CancellationToken cancellationToken = default) =>
        GetAsync<BuildReadinessResult>($"projects/{projectId}/tickets/{ticketId}/build-readiness", cancellationToken);

    public Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken cancellationToken = default) =>
        PostAsync<BuilderProposal>($"tickets/{ticketId}/proposal", new { }, cancellationToken);

    public Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default) =>
        PostAsync<BuilderProposal>($"projects/{projectId}/proposal", new { request }, ct);

    public Task<BuilderProposal> GenerateRepairProposalAsync(long ticketId, SkeletonRepairContext repair, CancellationToken ct = default) =>
        // REPAIR-1 runs inside the server-side skeleton orchestrator; there is no
        // client-initiated repair route — a client cannot direct repair attempts.
        throw new NotSupportedException("Repair proposals are orchestrated server-side inside bounded skeleton runs.");

    public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default) =>
        PostAsync<object>($"projects/{proposal.ProjectId}/proposal/apply", proposal, ct);

    public Task<TicketBuildResult> ApplyAndBuildAsync(TicketBuildApproval approval, CancellationToken cancellationToken = default) =>
        PostAsync<TicketBuildResult>($"tickets/{approval.TicketId}/apply-and-build", approval, cancellationToken);

    public Task<BuildReadinessResult> ValidateProposalArchitectureAsync(BuilderProposal proposal, CancellationToken cancellationToken = default) =>
        PostAsync<BuildReadinessResult>($"projects/{proposal.ProjectId}/proposal/validate-architecture", proposal, cancellationToken);
}
