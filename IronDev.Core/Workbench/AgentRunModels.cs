using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Workbench;

public static class WorkbenchAgentRunStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string NeedsInput = "NeedsInput";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Superseded = "Superseded";
    public const string Stale = "Stale";

    public static bool IsTerminal(string status) => status is
        NeedsInput or Completed or Failed or Cancelled or Superseded or Stale;
}

public static class WorkbenchAgentRunOperationKinds
{
    public const string DispatchInput = "DispatchWorkbenchInput";
    public const string Submit = "SubmitWorkbenchAgentRun";
    public const string Cancel = "CancelWorkbenchAgentRun";
}

public static class WorkbenchAgentRunFailureCategories
{
    public const string ServiceUnavailable = "service_unavailable";
    public const string ProviderTimeout = "provider_timeout";
    public const string InvalidResponse = "invalid_response";
    public const string ContextTooLarge = "context_too_large";
    public const string Configuration = "configuration_error";
    public const string AuthorityChanged = "authority_changed";
    public const string ExecutionFailed = "execution_failed";
}

public static class WorkbenchBusinessAnalystContract
{
    public const string AgentVersion = "business-analyst-v0.1";
    public const string PromptVersion1 = "workbench-shaping-v1";
    public const string PromptVersion2 = "workbench-shaping-v2";
    public const string PromptVersion3 = "workbench-ticket-proposals-v3";
    public const string PromptVersion = PromptVersion2;
    public const string ToolPolicyVersion = "workbench-ba-readonly-v1";
    public const int ContextSchemaVersion1 = 1;
    public const int ContextCanonicalizationVersion1 = 1;
    public const int ContextSchemaVersion2 = 2;
    public const int ContextCanonicalizationVersion2 = 2;
    public const int ContextSchemaVersion3 = 3;
    public const int ContextCanonicalizationVersion3 = 3;
    public const int OutputSchemaVersion1 = 1;
    public const int OutputSchemaVersion2 = 2;
    public const int OutputSchemaVersion3 = 3;
    public const int ContextSchemaVersion = ContextSchemaVersion2;
    public const int ContextCanonicalizationVersion = ContextCanonicalizationVersion2;
    public const int OutputSchemaVersion = OutputSchemaVersion2;
}

public enum WorkbenchAgentRunFailurePoint
{
    UserMessagePersisted,
    AgentRunCreated,
    OutboxEnqueued
}

public interface IWorkbenchAgentRunFailureInjector
{
    void ThrowIfRequested(WorkbenchAgentRunFailurePoint point);
}

public sealed class NoOpWorkbenchAgentRunFailureInjector : IWorkbenchAgentRunFailureInjector
{
    public void ThrowIfRequested(WorkbenchAgentRunFailurePoint point)
    {
    }
}

public sealed record SubmitWorkbenchAgentRunCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    long ChatSessionId,
    string Message,
    string InvocationKind = WorkbenchAgentInvocationKinds.Conversation,
    string? TicketInstruction = null,
    Guid? TicketProposalSetId = null,
    long? ExpectedTicketProposalRevision = null);

public sealed record SubmitWorkbenchAgentRunResult(
    Guid AgentRunId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    long ChatSessionId,
    long UserMessageId,
    string Status,
    Guid ClientOperationId,
    DateTime CreatedAtUtc,
    bool IsReplay,
    string InvocationKind = WorkbenchAgentInvocationKinds.Conversation,
    Guid? TicketProposalSetId = null,
    long? TicketProposalRevision = null);

public sealed record CancelWorkbenchAgentRunCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid AgentRunId,
    Guid ClientOperationId);

public sealed record CancelWorkbenchAgentRunResult(
    Guid AgentRunId,
    string Status,
    bool CancellationRequested,
    Guid ClientOperationId,
    bool IsReplay);

public sealed record WorkbenchAgentRunSnapshot(
    Guid AgentRunId,
    int TenantId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    int ActorUserId,
    long ChatSessionId,
    long SourceUserMessageId,
    string Status,
    int AttemptCount,
    long? AssistantMessageId,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancellationRequestedAtUtc,
    string? FailureCategory,
    bool Retryable,
    string InvocationKind = WorkbenchAgentInvocationKinds.Conversation,
    Guid? TicketProposalSetId = null,
    long? TicketProposalRevision = null);

public sealed record WorkbenchAgentRunRecoveryContext(
    bool SubmissionAvailable,
    string? UnavailableCategory,
    long? BoundChatSessionId,
    WorkbenchAgentRunSnapshot? ActiveRun,
    WorkbenchAgentRunSnapshot? LatestRun);

public sealed record WorkbenchAgentRunCurrentState(
    long? BoundChatSessionId,
    WorkbenchAgentRunSnapshot? ActiveRun,
    WorkbenchAgentRunSnapshot? LatestRun);

public sealed record WorkbenchAgentRunSubmissionAvailability(
    bool IsAvailable,
    string? FailureCategory)
{
    public static WorkbenchAgentRunSubmissionAvailability Available { get; } = new(true, null);
}

public interface IWorkbenchAgentRunSubmissionAvailability
{
    Task<WorkbenchAgentRunSubmissionAvailability> CheckAsync(
        int tenantId,
        int projectId,
        CancellationToken cancellationToken = default);
}

public sealed class AlwaysAvailableWorkbenchAgentRunSubmissionAvailability
    : IWorkbenchAgentRunSubmissionAvailability
{
    public Task<WorkbenchAgentRunSubmissionAvailability> CheckAsync(
        int tenantId,
        int projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(WorkbenchAgentRunSubmissionAvailability.Available);
}

public sealed class UnavailableWorkbenchAgentRunSubmissionAvailability
    : IWorkbenchAgentRunSubmissionAvailability
{
    public Task<WorkbenchAgentRunSubmissionAvailability> CheckAsync(
        int tenantId,
        int projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WorkbenchAgentRunSubmissionAvailability(
            false,
            WorkbenchAgentRunFailureCategories.ServiceUnavailable));
}

public sealed record WorkbenchAgentRunClaim(
    Guid AgentRunId,
    Guid ClaimToken,
    int TenantId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    int ActorUserId,
    long ChatSessionId,
    long SourceUserMessageId,
    int AttemptCount,
    string AgentVersion,
    string PromptVersion,
    string ToolPolicyVersion,
    int ContextSchemaVersion,
    int ContextCanonicalizationVersion,
    int OutputSchemaVersion,
    string InvocationKind = WorkbenchAgentInvocationKinds.Conversation,
    string? TicketInstruction = null,
    Guid? TicketProposalSetId = null,
    long? TicketProposalRevision = null);

public sealed record WorkbenchAgentContextMessage(
    long MessageId,
    string Role,
    string Message,
    DateTime CreatedAtUtc);

public sealed record WorkbenchBusinessAnalystContext(
    Guid AgentRunId,
    int TenantId,
    int ProjectId,
    string ProjectName,
    long WorkbenchSessionId,
    long LeaseEpoch,
    long ChatSessionId,
    long SourceUserMessageId,
    long UnderstandingRevision,
    string UnderstandingJson,
    IReadOnlyList<WorkbenchAgentContextMessage> Messages,
    string AgentVersion,
    string PromptVersion,
    string ToolPolicyVersion,
    int ContextSchemaVersion,
    int ContextCanonicalizationVersion,
    int OutputSchemaVersion,
    string ContextHash,
    string InvocationKind = WorkbenchAgentInvocationKinds.Conversation,
    string? TicketInstruction = null,
    Guid? TicketProposalSetId = null,
    long? TicketProposalRevision = null,
    string? TicketProposalSnapshotJson = null);

public sealed record WorkbenchBusinessAnalystOutput(
    int OutputSchemaVersion,
    string ContextHash,
    long BasedOnUnderstandingRevision,
    string Outcome,
    string AssistantMessage,
    ProjectUnderstandingPatch? UnderstandingPatch = null,
    WorkbenchProjectRenameProposalOutput? RenameProposal = null,
    TicketProposalSetOutput? TicketProposalSet = null);

public sealed record WorkbenchAgentRunMaterializationResult(
    Guid AgentRunId,
    string Status,
    bool Materialized,
    long? AssistantMessageId,
    bool IsReplay,
    string? RejectionReason = null,
    Guid? TicketProposalSetId = null,
    long? TicketProposalRevision = null);

public sealed record WorkbenchAgentRunOutboxItem(
    long OutboxEventId,
    Guid AgentRunId);

public interface IWorkbenchAgentRunService
{
    Task<WorkbenchAgentRunSubmissionAvailability> GetSubmissionAvailabilityAsync(
        int tenantId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<SubmitWorkbenchAgentRunResult> SubmitAsync(
        SubmitWorkbenchAgentRunCommand command,
        CancellationToken cancellationToken = default);

    Task<CancelWorkbenchAgentRunResult> CancelAsync(
        CancelWorkbenchAgentRunCommand command,
        CancellationToken cancellationToken = default);

    Task<WorkbenchAgentRunSnapshot> GetAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        Guid agentRunId,
        CancellationToken cancellationToken = default);

    Task<WorkbenchAgentRunCurrentState> GetCurrentActiveAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        long? chatSessionId,
        CancellationToken cancellationToken = default);

    Task<WorkbenchAgentRunClaim?> ClaimAsync(
        Guid agentRunId,
        string workerId,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default);

    Task<bool> AuthorizeInvocationAsync(
        WorkbenchAgentRunClaim claim,
        TimeSpan renewedClaimDuration,
        CancellationToken cancellationToken = default);

    Task<WorkbenchAgentRunMaterializationResult> MaterializeAsync(
        WorkbenchAgentRunClaim claim,
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystOutput output,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        WorkbenchAgentRunClaim claim,
        string diagnosticCode,
        string diagnosticHash,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchAgentContextAssembler
{
    Task<WorkbenchBusinessAnalystContext> AssembleAsync(
        WorkbenchAgentRunClaim claim,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchBusinessAnalystAgent
{
    Task<IWorkbenchBusinessAnalystPreparedInvocation> PrepareAsync(
        WorkbenchAgentRunClaim claim,
        WorkbenchBusinessAnalystContext context,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchBusinessAnalystPreparedInvocation
{
    TimeSpan ProviderTimeout { get; }
    string SafeRequestId { get; }

    Task<WorkbenchBusinessAnalystProviderResponse> InvokeProviderAsync(
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchAgentRunOutbox
{
    Task<IReadOnlyList<WorkbenchAgentRunOutboxItem>> ReadPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    Task MarkPublishedAsync(
        long outboxEventId,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchAgentRunProcessor
{
    Task ProcessAsync(
        WorkbenchAgentRunOutboxItem item,
        string workerId,
        CancellationToken cancellationToken = default);
}

public static class WorkbenchBusinessAnalystOutputValidator
{
    private static readonly JsonSerializerOptions StrictJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly string[] Version1Properties =
    [
        "outputSchemaVersion", "contextHash", "basedOnUnderstandingRevision", "outcome", "assistantMessage"
    ];

    private static readonly string[] Version2Properties =
    [
        "outputSchemaVersion", "contextHash", "basedOnUnderstandingRevision", "outcome", "assistantMessage",
        "understandingPatch", "renameProposal"
    ];

    private static readonly string[] Version3Properties =
    [
        "outputSchemaVersion", "contextHash", "basedOnUnderstandingRevision", "outcome", "assistantMessage",
        "understandingPatch", "renameProposal", "ticketProposalSet"
    ];

    public static WorkbenchBusinessAnalystOutput DeserializeAndValidate(
        string json,
        WorkbenchBusinessAnalystContext context)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new WorkbenchAgentOutputValidationException("The Business Analyst output is empty.");

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("outputSchemaVersion", out var versionElement) ||
                !versionElement.TryGetInt32(out var version))
                throw new WorkbenchAgentOutputValidationException("outputSchemaVersion is required.");

            var expectedProperties = version switch
            {
                WorkbenchBusinessAnalystContract.OutputSchemaVersion1 => Version1Properties,
                WorkbenchBusinessAnalystContract.OutputSchemaVersion2 => Version2Properties,
                WorkbenchBusinessAnalystContract.OutputSchemaVersion3 => Version3Properties,
                _ => throw new WorkbenchAgentOutputValidationException(
                    $"Unsupported Business Analyst output schema version {version}.")
            };
            var actualProperties = document.RootElement.EnumerateObject()
                .Select(property => property.Name).ToArray();
            if (actualProperties.Length != expectedProperties.Length ||
                actualProperties.Except(expectedProperties, StringComparer.Ordinal).Any() ||
                expectedProperties.Except(actualProperties, StringComparer.Ordinal).Any())
                throw new WorkbenchAgentOutputValidationException(
                    $"The Business Analyst output does not contain the exact schema version {version} properties.");

            var output = JsonSerializer.Deserialize<WorkbenchBusinessAnalystOutput>(json, StrictJsonOptions)
                ?? throw new WorkbenchAgentOutputValidationException("The Business Analyst output is empty.");
            Validate(output, context);
            return output;
        }
        catch (JsonException exception)
        {
            throw new WorkbenchAgentOutputValidationException(
                $"The Business Analyst output does not match schema version {context.OutputSchemaVersion}.",
                exception);
        }
    }

    public static string Serialize(WorkbenchBusinessAnalystOutput output) =>
        output.OutputSchemaVersion switch
        {
            WorkbenchBusinessAnalystContract.OutputSchemaVersion1 => JsonSerializer.Serialize(new
            {
                output.OutputSchemaVersion,
                output.ContextHash,
                output.BasedOnUnderstandingRevision,
                output.Outcome,
                output.AssistantMessage
            }, StrictJsonOptions),
            WorkbenchBusinessAnalystContract.OutputSchemaVersion2 => JsonSerializer.Serialize(new
            {
                output.OutputSchemaVersion,
                output.ContextHash,
                output.BasedOnUnderstandingRevision,
                output.Outcome,
                output.AssistantMessage,
                output.UnderstandingPatch,
                output.RenameProposal
            }, StrictJsonOptions),
            WorkbenchBusinessAnalystContract.OutputSchemaVersion3 => JsonSerializer.Serialize(output, StrictJsonOptions),
            _ => throw new WorkbenchAgentOutputValidationException(
                $"Unsupported Business Analyst output schema version {output.OutputSchemaVersion}.")
        };

    public static void Validate(
        WorkbenchBusinessAnalystOutput output,
        WorkbenchBusinessAnalystContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);

        if (context.OutputSchemaVersion is not (
            WorkbenchBusinessAnalystContract.OutputSchemaVersion1 or
            WorkbenchBusinessAnalystContract.OutputSchemaVersion2 or
            WorkbenchBusinessAnalystContract.OutputSchemaVersion3))
            throw new WorkbenchAgentOutputValidationException(
                $"Unsupported output schema version {context.OutputSchemaVersion} for this agent run.");

        if (output.OutputSchemaVersion != context.OutputSchemaVersion)
            throw new WorkbenchAgentOutputValidationException(
                $"outputSchemaVersion must be {context.OutputSchemaVersion} for this agent run.");

        if (string.IsNullOrWhiteSpace(output.ContextHash) ||
            output.ContextHash.Length != 64 ||
            !string.Equals(output.ContextHash, context.ContextHash, StringComparison.OrdinalIgnoreCase))
            throw new WorkbenchAgentOutputValidationException("contextHash does not match the server-owned context snapshot.");

        if (output.BasedOnUnderstandingRevision != context.UnderstandingRevision)
            throw new WorkbenchAgentOutputValidationException(
                "basedOnUnderstandingRevision does not match the server-owned context snapshot.");

        if (output.Outcome is not (WorkbenchAgentRunStates.Completed or WorkbenchAgentRunStates.NeedsInput))
            throw new WorkbenchAgentOutputValidationException(
                "outcome must be Completed or NeedsInput.");

        if (string.IsNullOrWhiteSpace(output.AssistantMessage))
            throw new WorkbenchAgentOutputValidationException("assistantMessage is required.");

        if (output.AssistantMessage.Length >
            WorkbenchBusinessAnalystProviderContract.MaximumAssistantMessageCharacters)
            throw new WorkbenchAgentOutputValidationException(
                $"assistantMessage exceeds the " +
                $"{WorkbenchBusinessAnalystProviderContract.MaximumAssistantMessageCharacters} character limit.");

        if (Encoding.UTF8.GetByteCount(output.AssistantMessage) >
            WorkbenchBusinessAnalystProviderContract.MaximumOutputUtf8Bytes)
            throw new WorkbenchAgentOutputValidationException(
                "assistantMessage exceeds the reserved UTF-8 output budget.");

        if (output.OutputSchemaVersion == WorkbenchBusinessAnalystContract.OutputSchemaVersion1)
        {
            if (output.UnderstandingPatch is not null || output.RenameProposal is not null ||
                output.TicketProposalSet is not null)
                throw new WorkbenchAgentOutputValidationException(
                    "Schema version 1 cannot contain project-understanding mutations.");
            return;
        }

        if (output.OutputSchemaVersion == WorkbenchBusinessAnalystContract.OutputSchemaVersion3)
        {
            if (!WorkbenchAgentInvocationKinds.IsTicketProposal(context.InvocationKind) ||
                output.UnderstandingPatch is not null || output.RenameProposal is not null)
                throw new WorkbenchAgentOutputValidationException(
                    "Schema version 3 is reserved for proposal-purpose runs and cannot mutate project understanding.");
            ValidateTicketProposalSet(output, context);
            return;
        }

        if (output.TicketProposalSet is not null)
            throw new WorkbenchAgentOutputValidationException(
                "Schema version 2 cannot contain ticket proposals.");
        ValidateUnderstandingPatch(output.UnderstandingPatch, context);
        if (output.RenameProposal is not null)
        {
            var name = output.RenameProposal.ProposedName?.Trim() ?? string.Empty;
            if (name.Length == 0 || name.Length > 200)
                throw new WorkbenchAgentOutputValidationException(
                    "renameProposal.proposedName must contain 1 to 200 characters.");
            ValidateEvidence(
                "renameProposal",
                output.RenameProposal.SourceMessageIds,
                output.RenameProposal.EvidenceSummary,
                context);
        }
    }

    private static void ValidateTicketProposalSet(
        WorkbenchBusinessAnalystOutput output,
        WorkbenchBusinessAnalystContext context)
    {
        var set = output.TicketProposalSet
            ?? throw new WorkbenchAgentOutputValidationException("ticketProposalSet is required for schema version 3.");
        if (set.Proposals is null || set.OpenQuestions is null || set.PotentialConflicts is null ||
            set.SourceMessageIds is null)
            throw new WorkbenchAgentOutputValidationException("ticketProposalSet collections are required.");
        if (set.SplitReason?.Length > 2_000)
            throw new WorkbenchAgentOutputValidationException("ticketProposalSet.splitReason is too long.");

        var trustedIds = context.Messages
            .Where(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Select(message => message.MessageId)
            .ToHashSet();
        ProjectUnderstandingDocument understanding;
        try
        {
            understanding = ProjectUnderstandingDocumentCodec.Deserialize(context.UnderstandingJson);
        }
        catch (ProjectUnderstandingValidationException exception)
        {
            throw new WorkbenchAgentOutputValidationException(
                "The frozen project-understanding provenance is invalid.",
                exception);
        }
        trustedIds.UnionWith(understanding.Facts.SelectMany(fact => fact.SourceMessageIds));
        trustedIds.UnionWith(understanding.Conflicts.SelectMany(conflict => conflict.SourceMessageIds));
        if (context.TicketProposalSnapshotJson is not null)
        {
            TicketProposalSetDocument reviewedSnapshot;
            try
            {
                reviewedSnapshot = TicketProposalSetDocumentCodec.Deserialize(
                    context.TicketProposalSnapshotJson);
            }
            catch (InvalidOperationException exception)
            {
                throw new WorkbenchAgentOutputValidationException(
                    "The frozen reviewed proposal provenance is invalid.",
                    exception);
            }
            trustedIds.UnionWith(reviewedSnapshot.SourceMessageIds);
            trustedIds.UnionWith(reviewedSnapshot.Proposals.SelectMany(proposal => proposal.SourceMessageIds));
            trustedIds.UnionWith(reviewedSnapshot.OpenQuestions.SelectMany(issue => issue.SourceMessageIds));
            trustedIds.UnionWith(reviewedSnapshot.PotentialConflicts.SelectMany(issue => issue.SourceMessageIds));
        }
        ValidateSourceIds("ticketProposalSet", set.SourceMessageIds, trustedIds, required: true);

        if (output.Outcome == WorkbenchAgentRunStates.NeedsInput)
        {
            if (set.Proposals.Count != 0 || set.OpenQuestions.Count + set.PotentialConflicts.Count == 0)
                throw new WorkbenchAgentOutputValidationException(
                    "NeedsInput must contain zero proposals and at least one open question or conflict.");
        }
        else if (set.Proposals.Count is < 1 or > 5)
        {
            throw new WorkbenchAgentOutputValidationException(
                "Completed ticket proposal generation must contain one to five proposals.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        foreach (var proposal in set.Proposals)
        {
            if (string.IsNullOrWhiteSpace(proposal.ProposalKey) || proposal.ProposalKey.Length > 80 ||
                proposal.ProposalKey.Any(char.IsWhiteSpace) || !keys.Add(proposal.ProposalKey))
                throw new WorkbenchAgentOutputValidationException("Ticket proposal keys must be unique bounded tokens.");
            if (string.IsNullOrWhiteSpace(proposal.Title) || proposal.Title.Length > 300 ||
                string.IsNullOrWhiteSpace(proposal.Problem) || proposal.Problem.Length > 4_000 ||
                string.IsNullOrWhiteSpace(proposal.ProposedChange) || proposal.ProposedChange.Length > 8_000 ||
                proposal.AcceptanceCriteria is null || proposal.AcceptanceCriteria.Count is < 1 or > 20 ||
                proposal.AcceptanceCriteria.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 2_000) ||
                proposal.Dependencies is null || proposal.Dependencies.Count > 4 ||
                proposal.SuggestedOrder <= 0 || !orders.Add(proposal.SuggestedOrder))
                throw new WorkbenchAgentOutputValidationException("A ticket proposal is incomplete or exceeds its bounds.");
            ValidateSourceIds($"ticketProposalSet.proposals[{proposal.ProposalKey}]", proposal.SourceMessageIds, trustedIds, required: true);
        }
        if (set.Proposals.Count > 0 && !orders.SetEquals(Enumerable.Range(1, set.Proposals.Count)))
            throw new WorkbenchAgentOutputValidationException("Ticket proposal suggested order must be contiguous from one.");
        foreach (var proposal in set.Proposals)
        {
            if (proposal.Dependencies.Distinct(StringComparer.Ordinal).Count() != proposal.Dependencies.Count ||
                proposal.Dependencies.Any(key => key == proposal.ProposalKey || !keys.Contains(key)))
                throw new WorkbenchAgentOutputValidationException("A ticket proposal dependency is unknown, duplicated, or self-referential.");
            if (proposal.Dependencies.Any(key =>
                    set.Proposals.Single(value => value.ProposalKey == key).SuggestedOrder >= proposal.SuggestedOrder))
                throw new WorkbenchAgentOutputValidationException("Ticket proposal dependencies must precede their dependants.");
        }

        ValidateIssues(set.OpenQuestions, TicketProposalIssueKinds.Question, trustedIds);
        ValidateIssues(set.PotentialConflicts, TicketProposalIssueKinds.Conflict, trustedIds);
    }

    private static void ValidateIssues(
        IReadOnlyList<TicketProposalIssueOutput> issues,
        string expectedKind,
        IReadOnlySet<long> trustedIds)
    {
        if (issues.Count > 20)
            throw new WorkbenchAgentOutputValidationException("A ticket proposal set contains too many issues.");
        foreach (var issue in issues)
        {
            if (issue.Kind != expectedKind || string.IsNullOrWhiteSpace(issue.Text) || issue.Text.Length > 2_000)
                throw new WorkbenchAgentOutputValidationException("A ticket proposal issue is invalid.");
            ValidateSourceIds("ticketProposalSet.issue", issue.SourceMessageIds, trustedIds, required: true);
        }
    }

    private static void ValidateSourceIds(
        string field,
        IReadOnlyList<long>? ids,
        IReadOnlySet<long> trustedIds,
        bool required)
    {
        if (ids is null || (required && ids.Count == 0) || ids.Count > 50 ||
            ids.Any(id => id <= 0 || !trustedIds.Contains(id)) || ids.Distinct().Count() != ids.Count)
            throw new WorkbenchAgentOutputValidationException($"{field} has invalid source-message provenance.");
    }

    private static void ValidateUnderstandingPatch(
        ProjectUnderstandingPatch? patch,
        WorkbenchBusinessAnalystContext context)
    {
        if (patch is null)
            return;
        if (patch.FactChanges is null)
            throw new WorkbenchAgentOutputValidationException("understandingPatch.factChanges is required.");
        if (patch.FactChanges.Count > ProjectUnderstandingContract.FactKeys.Count)
            throw new WorkbenchAgentOutputValidationException("understandingPatch contains too many fact changes.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in patch.FactChanges)
        {
            if (!ProjectUnderstandingContract.IsKnownFactKey(change.Key) || !keys.Add(change.Key))
                throw new WorkbenchAgentOutputValidationException(
                    "understandingPatch contains an unknown or duplicate fact key.");
            if (string.IsNullOrWhiteSpace(change.Value) ||
                change.Value.Length > ProjectUnderstandingContract.MaximumFactValueCharacters)
                throw new WorkbenchAgentOutputValidationException(
                    $"Fact change '{change.Key}' has an invalid value.");
            if (change.State is not (ProjectUnderstandingFactStates.Inferred or ProjectUnderstandingFactStates.Confirmed))
                throw new WorkbenchAgentOutputValidationException(
                    $"Fact change '{change.Key}' must be Inferred or Confirmed.");
            ValidateEvidence(change.Key, change.SourceMessageIds, change.EvidenceSummary, context);
        }

        if (patch.OpenQuestions is { Count: > 10 } ||
            patch.OpenQuestions?.Any(question => string.IsNullOrWhiteSpace(question) || question.Length > 1_000) == true)
            throw new WorkbenchAgentOutputValidationException("understandingPatch contains invalid open questions.");
    }

    private static void ValidateEvidence(
        string field,
        IReadOnlyList<long>? sourceMessageIds,
        string? evidenceSummary,
        WorkbenchBusinessAnalystContext context)
    {
        if (sourceMessageIds is null || sourceMessageIds.Count == 0 ||
            sourceMessageIds.Count > ProjectUnderstandingContract.MaximumSourceMessagesPerFact ||
            sourceMessageIds.Any(value => value <= 0) ||
            sourceMessageIds.Distinct().Count() != sourceMessageIds.Count ||
            string.IsNullOrWhiteSpace(evidenceSummary) ||
            evidenceSummary.Length > ProjectUnderstandingContract.MaximumEvidenceSummaryCharacters)
            throw new WorkbenchAgentOutputValidationException($"{field} has invalid evidence provenance.");

        var trustedUserMessageIds = context.Messages
            .Where(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Select(message => message.MessageId)
            .ToHashSet();
        if (sourceMessageIds.Any(messageId => !trustedUserMessageIds.Contains(messageId)))
            throw new WorkbenchAgentOutputValidationException(
                $"{field} cites a source outside the frozen trusted user-message context.");
    }
}

public sealed class WorkbenchAgentRunValidationException(string message) : Exception(message);

public sealed class WorkbenchChatSessionBindingException : Exception
{
    public const string ErrorCode = "workbench_chat_session_mismatch";

    public WorkbenchChatSessionBindingException()
        : base("This Workbench session is already bound to a different chat session.")
    {
    }
}

public sealed class WorkbenchAgentRunAlreadyActiveException : Exception
{
    public const string ErrorCode = "workbench_agent_run_active";

    public WorkbenchAgentRunAlreadyActiveException(Guid agentRunId)
        : base("This Workbench session already has a pending or running Business Analyst turn.")
    {
        AgentRunId = agentRunId;
    }

    public Guid AgentRunId { get; }
}

public sealed class WorkbenchAgentRunUnavailableException : Exception
{
    public const string ErrorCode = "workbench_agent_run_unavailable";

    public WorkbenchAgentRunUnavailableException(string failureCategory)
        : base("The Workbench Business Analyst is not available in this environment.")
    {
        FailureCategory = failureCategory;
    }

    public string FailureCategory { get; }
}

public sealed class WorkbenchAgentRunNotFoundException : Exception;

public sealed class WorkbenchAgentOutputValidationException : Exception
{
    public WorkbenchAgentOutputValidationException(string message)
        : base(message)
    {
    }

    public WorkbenchAgentOutputValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WorkbenchAgentProviderTimeoutException : Exception
{
    public WorkbenchAgentProviderTimeoutException()
        : base("The Business Analyst provider did not complete within the bounded invocation timeout.")
    {
    }
}

public sealed class WorkbenchAgentProviderTimeoutConfigurationException : Exception
{
    public WorkbenchAgentProviderTimeoutConfigurationException(string message)
        : base(message)
    {
    }
}
