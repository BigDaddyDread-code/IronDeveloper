namespace IronDev.Core.Workbench;

public static class TicketProposalReviewOperationKinds
{
    public const string Edit = "EditTicketProposal";
    public const string Reorder = "ReorderTicketProposals";
    public const string Remove = "RemoveTicketProposal";
    public const string ResolveIssue = "ResolveTicketProposalIssue";
}

public static class TicketProposalCommitOperationKinds
{
    public const string Commit = "CommitTicketProposalSet";
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

/// <summary>
/// Public mutation body for committing one reviewed proposal-set revision.
/// Project, tenant, actor, and proposal-set identities remain route/server owned.
/// </summary>
public sealed record CommitTicketProposalSetRequest(
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    long ExpectedProposalSetRevision);

public sealed record CommitTicketProposalSetCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    Guid TicketProposalSetId,
    long ExpectedProposalSetRevision);

/// <summary>
/// Stable mapping returned for each permanent ticket created from the reviewed set.
/// Dependencies contain permanent ticket identifiers, never proposal identifiers.
/// </summary>
public sealed record CommittedProjectTicketReadModel(
    Guid TicketProposalId,
    long ProjectTicketId,
    string Title,
    int SuggestedOrder,
    IReadOnlyList<long> BlockedByTicketIds);

public sealed record TicketProposalCommitReadModel(
    Guid CommitmentId,
    Guid TicketProposalSetId,
    long ReviewedRevision,
    long CommittedRevision,
    string ReviewedSnapshotHash,
    int ActorUserId,
    DateTime CommittedAtUtc,
    IReadOnlyList<CommittedProjectTicketReadModel> Tickets);

public sealed record TicketProposalCommitResult(
    TicketProposalSetReadModel ProposalSet,
    TicketProposalCommitReadModel Commitment,
    string ProjectLifecyclePhase,
    string ExecutionReadiness,
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

public interface IWorkbenchTicketProposalCommitService
{
    Task<TicketProposalCommitResult> CommitAsync(
        CommitTicketProposalSetCommand command,
        CancellationToken cancellationToken = default);
}

public enum TicketProposalCommitFailurePoint
{
    ClientOperationCreated,
    TicketsCreated,
    ProposalCommitted,
    CommitmentRecorded,
    LifecycleAdvanced,
    OutboxRecorded
}

public interface ITicketProposalCommitFailureInjector
{
    void ThrowIfRequested(TicketProposalCommitFailurePoint point);
}

public sealed class NoOpTicketProposalCommitFailureInjector : ITicketProposalCommitFailureInjector
{
    public void ThrowIfRequested(TicketProposalCommitFailurePoint point)
    {
    }
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

public sealed class TicketProposalBlockingIssuesException : Exception
{
    public const string ErrorCode = "ticket_proposal_blocking_issues";

    public TicketProposalBlockingIssuesException()
        : base("Resolve every open ticket proposal question and conflict before creating tickets.")
    {
    }
}

public sealed class TicketProposalAlreadyCommittedException : Exception
{
    public const string ErrorCode = "ticket_proposal_already_committed";

    public TicketProposalAlreadyCommittedException()
        : base("This ticket proposal set has already been committed.")
    {
    }
}

public sealed class TicketProposalSetNotReadyException : Exception
{
    public const string ErrorCode = "ticket_proposal_set_not_ready";

    public TicketProposalSetNotReadyException()
        : base("Only a ready ticket proposal set can create permanent tickets.")
    {
    }
}

public sealed class TicketProposalCommitBoundaryException : Exception
{
    public const string ErrorCode = "ticket_proposal_commit_boundary_invalid";

    public TicketProposalCommitBoundaryException()
        : base("Shorten every proposal title to 200 characters or fewer before creating permanent tickets.")
    {
    }
}

public sealed class TicketProposalProjectNotShapingException : Exception
{
    public const string ErrorCode = "ticket_proposal_project_not_shaping";

    public TicketProposalProjectNotShapingException()
        : base("Permanent tickets can be created from proposals only while the project is Shaping.")
    {
    }
}
