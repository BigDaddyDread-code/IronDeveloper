namespace IronDev.Core.Workbench;

public static class TicketProposalReviewOperationKinds
{
    public const string Edit = "EditTicketProposal";
    public const string Reorder = "ReorderTicketProposals";
    public const string Remove = "RemoveTicketProposal";
    public const string ResolveIssue = "ResolveTicketProposalIssue";
}

public sealed record TicketProposalReadModel(
    Guid TicketProposalId,
    string Title,
    string Problem,
    string ProposedChange,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<Guid> DependencyProposalIds,
    int SuggestedOrder,
    IReadOnlyList<long> SourceMessageIds);

public sealed record TicketProposalIssueReadModel(
    Guid IssueId,
    string Kind,
    string Text,
    string Status,
    string? Resolution,
    IReadOnlyList<long> SourceMessageIds);

public sealed record TicketProposalSetReadModel(
    Guid TicketProposalSetId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    long Revision,
    long BasedOnUnderstandingRevision,
    string Status,
    string? SplitReason,
    IReadOnlyList<TicketProposalReadModel> Proposals,
    IReadOnlyList<TicketProposalIssueReadModel> OpenQuestions,
    IReadOnlyList<TicketProposalIssueReadModel> PotentialConflicts,
    IReadOnlyList<long> SourceMessageIds,
    Guid CreatedByAgentRunId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static TicketProposalSetReadModel FromDocument(TicketProposalSetDocument document) => new(
        document.TicketProposalSetId,
        document.ProjectId,
        document.WorkbenchSessionId,
        document.LeaseEpoch,
        document.Revision,
        document.BasedOnUnderstandingRevision,
        document.Status,
        document.SplitReason,
        document.Proposals.Select(proposal => new TicketProposalReadModel(
            proposal.TicketProposalId,
            proposal.Title,
            proposal.Problem,
            proposal.ProposedChange,
            proposal.AcceptanceCriteria,
            proposal.DependencyProposalIds,
            proposal.SuggestedOrder,
            proposal.SourceMessageIds)).ToArray(),
        document.OpenQuestions.Select(FromIssue).ToArray(),
        document.PotentialConflicts.Select(FromIssue).ToArray(),
        document.SourceMessageIds,
        document.CreatedByAgentRunId,
        document.CreatedAtUtc,
        document.UpdatedAtUtc);

    private static TicketProposalIssueReadModel FromIssue(TicketProposalIssueDocument issue) => new(
        issue.IssueId,
        issue.Kind,
        issue.Text,
        issue.Status,
        issue.Resolution,
        issue.SourceMessageIds);
}

public sealed record TicketProposalSetHistoryEntry(
    long Revision,
    string ChangeKind,
    int ActorUserId,
    Guid? AgentRunId,
    DateTime CreatedAtUtc,
    TicketProposalSetReadModel ProposalSet);

public sealed record TicketProposalSetMutationResult(
    TicketProposalSetReadModel ProposalSet,
    Guid ClientOperationId,
    bool IsReplay);

public sealed record EditTicketProposalCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    Guid TicketProposalSetId,
    Guid TicketProposalId,
    long ExpectedProposalSetRevision,
    string Title,
    string Problem,
    string ProposedChange,
    IReadOnlyList<string> AcceptanceCriteria);

public sealed record ReorderTicketProposalsCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    Guid TicketProposalSetId,
    long ExpectedProposalSetRevision,
    IReadOnlyList<Guid> OrderedProposalIds);

public sealed record RemoveTicketProposalCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    Guid TicketProposalSetId,
    Guid TicketProposalId,
    long ExpectedProposalSetRevision);

public sealed record ResolveTicketProposalIssueCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    Guid TicketProposalSetId,
    Guid IssueId,
    long ExpectedProposalSetRevision,
    string Resolution);

public sealed record RegenerateTicketProposalSetCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    long ChatSessionId,
    Guid TicketProposalSetId,
    long ExpectedProposalSetRevision,
    string Instruction);

public interface IWorkbenchTicketProposalService
{
    Task<TicketProposalSetReadModel?> GetCurrentAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken = default);

    Task<TicketProposalSetReadModel> GetAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        Guid ticketProposalSetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketProposalSetHistoryEntry>> GetHistoryAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        Guid ticketProposalSetId,
        CancellationToken cancellationToken = default);

    Task<TicketProposalSetMutationResult> EditAsync(
        EditTicketProposalCommand command,
        CancellationToken cancellationToken = default);

    Task<TicketProposalSetMutationResult> ReorderAsync(
        ReorderTicketProposalsCommand command,
        CancellationToken cancellationToken = default);

    Task<TicketProposalSetMutationResult> RemoveAsync(
        RemoveTicketProposalCommand command,
        CancellationToken cancellationToken = default);

    Task<TicketProposalSetMutationResult> ResolveIssueAsync(
        ResolveTicketProposalIssueCommand command,
        CancellationToken cancellationToken = default);

    Task<SubmitWorkbenchAgentRunResult> RegenerateAsync(
        RegenerateTicketProposalSetCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class TicketProposalValidationException(string message) : Exception(message);

public sealed class TicketProposalIssueNotOpenException : Exception
{
    public const string ErrorCode = "ticket_proposal_issue_not_open";

    public TicketProposalIssueNotOpenException()
        : base("The selected ticket proposal question or conflict is no longer open.")
    {
    }
}

public sealed class TicketProposalDependencyException(string message) : Exception(message)
{
    public const string ErrorCode = "ticket_proposal_dependency_invalid";
}

public sealed class TicketProposalFinalRemovalException : Exception
{
    public const string ErrorCode = "ticket_proposal_final_removal";

    public TicketProposalFinalRemovalException()
        : base("A ready proposal set must retain at least one ticket proposal.")
    {
    }
}
