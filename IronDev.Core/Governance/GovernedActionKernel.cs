using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Memory;
using IronDev.Core.SourceApply;
using IronDev.Core.Tools;

namespace IronDev.Core.Governance;

public enum GovernedActionStatus
{
    Requested = 0,
    EvidenceAttached,
    GateEvidenceRecorded,
    NeedsMoreEvidence,
    Blocked,
    Allowed,
    Executed,
    Failed,
    ReceiptCreated
}

public enum GovernedActionRiskLevel
{
    Low = 0,
    Medium,
    High,
    Critical
}

public sealed record GovernedActionSubject
{
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
}

public sealed record GovernedActionEvidenceRef
{
    public required string EvidenceRefId { get; init; }
    public required string EvidenceKind { get; init; }
    public required string PathOrUri { get; init; }
    public required string Hash { get; init; }
    public required string SafeSummary { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record GovernedActionEnvelope
{
    public required string ActionId { get; init; }
    public required GovernedActionKind ActionKind { get; init; }
    public required GovernedActionSubject Subject { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required string RequestedInputsRef { get; init; }
    public GovernedActionEvidenceRef[] EvidenceRefs { get; init; } = [];
    public required GovernedActionRiskLevel RiskLevel { get; init; }
    public string[] RequiredPolicyRefs { get; init; } = [];
    public required bool RequiredHumanReview { get; init; }
    public required GovernedActionStatus Status { get; init; }
    public GovernedActionBoundary Boundary { get; init; } = GovernedActionBoundary.None;

    public bool GrantsApproval => false;
    public bool AllowsExecution => false;
    public bool SatisfiesPolicy => false;

    public static GovernedActionEnvelope FromInventory(
        GovernedActionKind actionKind,
        string subjectKind,
        string subjectId,
        string requestedBy,
        string requestedInputsRef,
        IEnumerable<GovernedActionEvidenceRef>? evidenceRefs = null,
        DateTimeOffset? requestedAtUtc = null)
    {
        var entry = AuthorityActionInventory.Get(actionKind);
        return new GovernedActionEnvelope
        {
            ActionId = $"gov_env_{Guid.NewGuid():N}",
            ActionKind = actionKind,
            Subject = new GovernedActionSubject { SubjectKind = subjectKind.Trim(), SubjectId = subjectId.Trim() },
            RequestedBy = requestedBy.Trim(),
            RequestedAtUtc = requestedAtUtc ?? DateTimeOffset.UtcNow,
            RequestedInputsRef = requestedInputsRef.Trim(),
            EvidenceRefs = (evidenceRefs ?? []).ToArray(),
            RiskLevel = entry.Classification == GovernedActionClassification.AuthorityBearing ? GovernedActionRiskLevel.High : GovernedActionRiskLevel.Low,
            RequiredPolicyRefs = entry.RequiredPolicyKinds,
            RequiredHumanReview = entry.RequiresConscience,
            Status = GovernedActionStatus.Requested,
            Boundary = GovernedActionBoundary.None
        };
    }
}

public enum GateEvidenceDecision
{
    Satisfied = 0,
    NotSatisfied,
    NeedsMoreEvidence
}

public enum GateEvidenceKind
{
    MemoryKeyGate = 0,
    WorkspaceToolGate,
    SourceApplyEligibilityGate,
    SourceApplyExecutionGate,
    SourceRollbackGate,
    WorkflowGate,
    ReleaseGate,
    MergeGate,
    DeploymentGate
}

public sealed record GateEvidence
{
    public required string GateEvidenceId { get; init; }
    public required string ActionId { get; init; }
    public required GateEvidenceKind GateKind { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required GateEvidenceDecision Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public string[] EvidenceRefs { get; init; } = [];
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public GovernedActionBoundary Boundary { get; init; } = GovernedActionBoundary.None;

    public bool GrantsAuthority => false;
    public bool AllowsExecution => false;
    public bool ApprovesAction => false;
}

public static class GateEvidenceWriter
{
    public static GateEvidence FromMemoryKeyGate(string actionId, MemoryKeyGateResult result) =>
        Create(actionId, GateEvidenceKind.MemoryKeyGate, "MemoryProposal", result.MemoryProposalId, result.Decision == MemoryKeyGateDecision.Allow, result.Reasons, [result.MemoryKeyGateResultId], result.EvaluatedAtUtc);

    public static GateEvidence FromWorkspaceToolGate(string actionId, WorkspaceToolGateDecision result) =>
        Create(actionId, GateEvidenceKind.WorkspaceToolGate, "ToolRequest", result.ToolRequestId, result.Decision == WorkspaceToolGateDecisionOutcome.Allow, result.Reasons, [result.ToolGateDecisionId], result.EvaluatedAtUtc);

    public static GateEvidence FromSourceApplyGate(string actionId, SourceApplyGateDecision result) =>
        Create(actionId, GateEvidenceKind.SourceApplyEligibilityGate, "SourceApplyRequest", result.SourceApplyRequestId, result.Decision == SourceApplyGateDecisionOutcome.AllowDryRun, result.Reasons, [result.SourceApplyGateDecisionId], result.EvaluatedAtUtc);

    public static GateEvidence FromSourceApplyExecutionGate(string actionId, SourceApplyExecutionGateDecision result) =>
        Create(actionId, GateEvidenceKind.SourceApplyExecutionGate, "SourceApplyExecutionRequest", result.SourceApplyExecutionRequestId, result.Decision == SourceApplyExecutionGateDecisionOutcome.AllowApplyToWorkingTree, result.Reasons, [result.SourceApplyExecutionGateDecisionId], result.EvaluatedAtUtc);

    public static GateEvidence FromSourceRollbackGate(string actionId, SourceRollbackGateDecision result) =>
        Create(actionId, GateEvidenceKind.SourceRollbackGate, "SourceRollbackRequest", result.SourceRollbackRequestId, result.Decision == SourceRollbackGateDecisionOutcome.AllowRollback, result.Reasons, [result.SourceRollbackGateDecisionId], result.EvaluatedAtUtc);

    private static GateEvidence Create(
        string actionId,
        GateEvidenceKind gateKind,
        string subjectKind,
        string subjectId,
        bool satisfied,
        string[] reasons,
        string[] evidenceRefs,
        DateTimeOffset evaluatedAtUtc) => new()
        {
            GateEvidenceId = $"gate_ev_{Guid.NewGuid():N}",
            ActionId = actionId.Trim(),
            GateKind = gateKind,
            SubjectKind = subjectKind,
            SubjectId = subjectId.Trim(),
            Decision = satisfied ? GateEvidenceDecision.Satisfied : GateEvidenceDecision.NotSatisfied,
            Reasons = reasons,
            EvidenceRefs = evidenceRefs,
            EvaluatedAtUtc = evaluatedAtUtc,
            Boundary = GovernedActionBoundary.None
        };
}

public enum ConscienceDecisionValue
{
    Allow = 0,
    Block,
    NeedsMoreEvidence
}

public sealed record ConscienceDecisionRequest
{
    public GovernedActionEnvelope? Action { get; init; }
    public GateEvidence[] GateEvidenceRefs { get; init; } = [];
    public string[] PolicyRefs { get; init; } = [];
    public required string RequestedBy { get; init; }
    public required string ReasoningSummary { get; init; }
    public required ConscienceDecisionValue RequestedDecision { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record ConscienceDecisionRecord
{
    public required string DecisionId { get; init; }
    public required string ActionId { get; init; }
    public required GovernedActionKind ActionKind { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string RequestedBy { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public string[] GateEvidenceRefs { get; init; } = [];
    public string[] PolicyRefs { get; init; } = [];
    public required GovernedActionRiskLevel RiskLevel { get; init; }
    public required ConscienceDecisionValue Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public required bool RequiredHumanReview { get; init; }
    public string? ThoughtLedgerEntryId { get; init; }
    public required string DecisionHash { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public GovernedActionBoundary Boundary { get; init; } = GovernedActionBoundary.None;

    public bool AllowsExecution => Decision == ConscienceDecisionValue.Allow && Reasons.Length == 0 && !string.IsNullOrWhiteSpace(ThoughtLedgerEntryId);
}

public sealed record ConscienceDecisionValidationResult
{
    public required bool IsValidForFutureExecution { get; init; }
    public string[] Issues { get; init; } = [];
}

public static class ConscienceDecisionRecordHash
{
    public static string Compute(ConscienceDecisionRecord record)
    {
        var payload = JsonSerializer.Serialize(new
        {
            record.DecisionId,
            record.ActionId,
            record.ActionKind,
            record.SubjectKind,
            record.SubjectId,
            record.RequestedBy,
            record.EvidenceRefs,
            record.GateEvidenceRefs,
            record.PolicyRefs,
            record.RiskLevel,
            record.Decision,
            record.Reasons,
            record.RequiredHumanReview,
            record.ThoughtLedgerEntryId,
            record.ExpiresAtUtc,
            record.CreatedAtUtc
        });
        return Sha256(payload);
    }

    internal static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}

public static class ConscienceDecisionValidator
{
    public static ConscienceDecisionValidationResult Validate(ConscienceDecisionRecord? record, DateTimeOffset? now = null)
    {
        var issues = new List<string>();
        if (record is null)
            return new ConscienceDecisionValidationResult { IsValidForFutureExecution = false, Issues = ["MissingDecision"] };

        if (string.IsNullOrWhiteSpace(record.ActionId))
            issues.Add("MissingActionId");
        if (record.ActionKind == GovernedActionKind.Unknown)
            issues.Add("MissingActionKind");
        if (string.IsNullOrWhiteSpace(record.SubjectKind) || string.IsNullOrWhiteSpace(record.SubjectId))
            issues.Add("MissingSubject");
        if (record.Decision == ConscienceDecisionValue.Allow && record.EvidenceRefs.Length == 0)
            issues.Add("MissingEvidenceForAllow");
        if (record.Decision == ConscienceDecisionValue.Allow && GovernedActionKernelRequirements.RequiresGateEvidence(record.ActionKind) && record.GateEvidenceRefs.Length == 0)
            issues.Add("MissingGateEvidenceForAllow");
        if (string.IsNullOrWhiteSpace(record.ThoughtLedgerEntryId))
            issues.Add("MissingThoughtLedger");
        if (record.ExpiresAtUtc is not null && record.ExpiresAtUtc <= (now ?? DateTimeOffset.UtcNow))
            issues.Add("DecisionExpired");
        if (string.IsNullOrWhiteSpace(record.DecisionHash))
            issues.Add("MissingDecisionHash");
        else if (!string.Equals(record.DecisionHash, ConscienceDecisionRecordHash.Compute(record), StringComparison.OrdinalIgnoreCase))
            issues.Add("DecisionHashMismatch");
        if (record.Decision is ConscienceDecisionValue.Block or ConscienceDecisionValue.NeedsMoreEvidence)
            issues.Add($"DecisionIs{record.Decision}");

        return new ConscienceDecisionValidationResult
        {
            IsValidForFutureExecution = issues.Count == 0,
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}

public sealed record ThoughtLedgerEntry
{
    public required string ThoughtLedgerEntryId { get; init; }
    public required string ActionId { get; init; }
    public required string DecisionId { get; init; }
    public required GovernedActionKind ActionKind { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string ReasoningSummary { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public required GovernedActionRiskLevel RiskLevel { get; init; }
    public required ConscienceDecisionValue Decision { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string EntryHash { get; init; }
}

public sealed record ThoughtLedgerWriteRequest
{
    public required string ActionId { get; init; }
    public required string DecisionId { get; init; }
    public required GovernedActionKind ActionKind { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string ReasoningSummary { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public required GovernedActionRiskLevel RiskLevel { get; init; }
    public required ConscienceDecisionValue Decision { get; init; }
}

public sealed record ThoughtLedgerWriteResult
{
    public required bool Succeeded { get; init; }
    public ThoughtLedgerEntry? Entry { get; init; }
    public string[] Issues { get; init; } = [];

    public static ThoughtLedgerWriteResult Failed(params string[] issues) => new() { Succeeded = false, Issues = issues };
}

public interface IThoughtLedgerWriter
{
    ThoughtLedgerWriteResult Write(ThoughtLedgerWriteRequest request, DateTimeOffset? now = null);
}

public sealed class InMemoryThoughtLedgerWriter : IThoughtLedgerWriter
{
    private readonly List<ThoughtLedgerEntry> _entries = [];

    public IReadOnlyList<ThoughtLedgerEntry> Entries => _entries;
    public bool ForceFailure { get; init; }

    public ThoughtLedgerWriteResult Write(ThoughtLedgerWriteRequest request, DateTimeOffset? now = null)
    {
        if (ForceFailure)
            return ThoughtLedgerWriteResult.Failed("LedgerWriteFailed");
        if (ContainsUnsafeReasoning(request.ReasoningSummary))
            return ThoughtLedgerWriteResult.Failed("UnsafeThoughtLedgerSummary");

        var createdAt = now ?? DateTimeOffset.UtcNow;
        var draft = new ThoughtLedgerEntry
        {
            ThoughtLedgerEntryId = $"thought_{Guid.NewGuid():N}",
            ActionId = request.ActionId.Trim(),
            DecisionId = request.DecisionId.Trim(),
            ActionKind = request.ActionKind,
            SubjectKind = request.SubjectKind.Trim(),
            SubjectId = request.SubjectId.Trim(),
            ReasoningSummary = SanitiseReasoningSummary(request.ReasoningSummary),
            EvidenceRefs = request.EvidenceRefs,
            RiskLevel = request.RiskLevel,
            Decision = request.Decision,
            CreatedAtUtc = createdAt,
            EntryHash = string.Empty
        };
        var entry = draft with { EntryHash = ConscienceDecisionRecordHash.Sha256(JsonSerializer.Serialize(draft with { EntryHash = string.Empty })) };
        _entries.Add(entry);
        return new ThoughtLedgerWriteResult { Succeeded = true, Entry = entry };
    }

    public static string SanitiseReasoningSummary(string value)
    {
        var text = value ?? string.Empty;
        foreach (var marker in UnsafeReasoningMarkers)
            text = text.Replace(marker, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        return text.Trim();
    }

    public static bool ContainsUnsafeReasoning(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeReasoningMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] UnsafeReasoningMarkers =
    [
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "private reasoning",
        "hidden reasoning",
        "raw prompt",
        "raw completion",
        "raw tool output",
        "entire patch"
    ];
}

public sealed class ConscienceDecisionService
{
    private readonly IThoughtLedgerWriter _thoughtLedgerWriter;

    public ConscienceDecisionService(IThoughtLedgerWriter thoughtLedgerWriter)
    {
        _thoughtLedgerWriter = thoughtLedgerWriter;
    }

    public ConscienceDecisionRecord Decide(ConscienceDecisionRequest request, DateTimeOffset? now = null)
    {
        var createdAt = now ?? DateTimeOffset.UtcNow;
        var reasons = new List<string>();
        var action = request.Action;
        if (action is null)
        {
            var decisionId = $"conscience_{Guid.NewGuid():N}";
            var ledger = WriteThoughtLedger(decisionId, null, request, ConscienceDecisionValue.Block, createdAt);
            var missingActionReasons = ledger.Succeeded && ledger.Entry is not null
                ? new[] { "MissingAction" }
                : [.. ledger.Issues, "LedgerWriteFailed", "MissingAction"];
            return BuildMissingActionRecord(decisionId, request, missingActionReasons, ledger.Entry?.ThoughtLedgerEntryId, createdAt);
        }

        if (string.IsNullOrWhiteSpace(action.ActionId))
            reasons.Add("MissingActionId");
        if (action.ActionKind == GovernedActionKind.Unknown)
            reasons.Add("MissingActionKind");
        if (string.IsNullOrWhiteSpace(action.Subject.SubjectKind) || string.IsNullOrWhiteSpace(action.Subject.SubjectId))
            reasons.Add("MissingSubject");
        if (action.EvidenceRefs.Length == 0 && action.RequiredHumanReview)
            reasons.Add("MissingEvidence");
        if (GovernedActionKernelRequirements.RequiresGateEvidence(action.ActionKind))
        {
            if (request.GateEvidenceRefs.Length == 0)
            {
                reasons.Add("MissingGateEvidence");
            }
            else if (!request.GateEvidenceRefs.Any(gate => GovernedActionKernelRequirements.GateEvidenceMatchesAction(action.ActionKind, gate.GateKind)))
            {
                reasons.Add("GateEvidenceKindMismatch");
            }
        }

        if (request.GateEvidenceRefs.Any(gate => !string.Equals(gate.ActionId, action.ActionId, StringComparison.OrdinalIgnoreCase)))
            reasons.Add("GateEvidenceActionMismatch");
        if (request.GateEvidenceRefs.Any(gate => gate.Decision != GateEvidenceDecision.Satisfied))
            reasons.Add("GateEvidenceNotSatisfied");
        if (request.ExpiresAtUtc is not null && request.ExpiresAtUtc <= createdAt)
            reasons.Add("DecisionExpired");
        if (InMemoryThoughtLedgerWriter.ContainsUnsafeReasoning(request.ReasoningSummary))
            reasons.Add("UnsafeThoughtLedgerSummary");

        var intendedDecision = reasons.Count == 0 ? request.RequestedDecision : ConscienceDecisionValue.Block;

        if (intendedDecision == ConscienceDecisionValue.NeedsMoreEvidence)
            reasons.Add("NeedsMoreEvidenceCannotAuthorizeExecution");

        var finalReasons = intendedDecision == ConscienceDecisionValue.Allow
            ? Array.Empty<string>()
            : reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var finalDecision = intendedDecision;
        var finalDecisionId = $"conscience_{Guid.NewGuid():N}";
        var finalLedger = WriteThoughtLedger(finalDecisionId, action, request, finalDecision, createdAt);
        if (!finalLedger.Succeeded || finalLedger.Entry is null)
            return BuildRecord(finalDecisionId, action, request, ConscienceDecisionValue.Block, [.. finalReasons, .. finalLedger.Issues, "LedgerWriteFailed"], null, createdAt);

        return BuildRecord(finalDecisionId, action, request, finalDecision, finalReasons, finalLedger.Entry.ThoughtLedgerEntryId, createdAt);
    }

    private ThoughtLedgerWriteResult WriteThoughtLedger(
        string decisionId,
        GovernedActionEnvelope? action,
        ConscienceDecisionRequest request,
        ConscienceDecisionValue decision,
        DateTimeOffset createdAt) => _thoughtLedgerWriter.Write(new ThoughtLedgerWriteRequest
        {
            ActionId = action?.ActionId ?? string.Empty,
            DecisionId = decisionId,
            ActionKind = action?.ActionKind ?? GovernedActionKind.Unknown,
            SubjectKind = action?.Subject.SubjectKind ?? string.Empty,
            SubjectId = action?.Subject.SubjectId ?? string.Empty,
            ReasoningSummary = request.ReasoningSummary,
            EvidenceRefs = action?.EvidenceRefs.Select(item => item.EvidenceRefId).ToArray() ?? [],
            RiskLevel = action?.RiskLevel ?? GovernedActionRiskLevel.Critical,
            Decision = decision
        }, createdAt);

    private static ConscienceDecisionRecord BuildMissingActionRecord(
        string decisionId,
        ConscienceDecisionRequest request,
        string[] reasons,
        string? thoughtLedgerEntryId,
        DateTimeOffset createdAt)
    {
        var draft = new ConscienceDecisionRecord
        {
            DecisionId = decisionId,
            ActionId = string.Empty,
            ActionKind = GovernedActionKind.Unknown,
            SubjectKind = string.Empty,
            SubjectId = string.Empty,
            RequestedBy = request.RequestedBy,
            EvidenceRefs = [],
            GateEvidenceRefs = [],
            PolicyRefs = request.PolicyRefs,
            RiskLevel = GovernedActionRiskLevel.Critical,
            Decision = ConscienceDecisionValue.Block,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RequiredHumanReview = true,
            ThoughtLedgerEntryId = thoughtLedgerEntryId,
            DecisionHash = string.Empty,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CreatedAtUtc = createdAt,
            Boundary = GovernedActionBoundary.None
        };
        return draft with { DecisionHash = ConscienceDecisionRecordHash.Compute(draft) };
    }

    private static ConscienceDecisionRecord BuildRecord(
        string decisionId,
        GovernedActionEnvelope action,
        ConscienceDecisionRequest request,
        ConscienceDecisionValue decision,
        string[] reasons,
        string? thoughtLedgerEntryId,
        DateTimeOffset createdAt)
    {
        var draft = new ConscienceDecisionRecord
        {
            DecisionId = decisionId,
            ActionId = action.ActionId,
            ActionKind = action.ActionKind,
            SubjectKind = action.Subject.SubjectKind,
            SubjectId = action.Subject.SubjectId,
            RequestedBy = request.RequestedBy.Trim(),
            EvidenceRefs = action.EvidenceRefs.Select(item => item.EvidenceRefId).ToArray(),
            GateEvidenceRefs = request.GateEvidenceRefs.Select(item => item.GateEvidenceId).ToArray(),
            PolicyRefs = request.PolicyRefs,
            RiskLevel = action.RiskLevel,
            Decision = decision,
            Reasons = reasons,
            RequiredHumanReview = action.RequiredHumanReview,
            ThoughtLedgerEntryId = thoughtLedgerEntryId,
            DecisionHash = string.Empty,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CreatedAtUtc = createdAt,
            Boundary = GovernedActionBoundary.None
        };
        return draft with { DecisionHash = ConscienceDecisionRecordHash.Compute(draft) };
    }
}

public static class GovernedActionKernelRequirements
{
    public static bool RequiresGateEvidence(GovernedActionKind actionKind) =>
        RequiredGateEvidenceKinds(actionKind).Length > 0;

    public static bool GateEvidenceMatchesAction(GovernedActionKind actionKind, GateEvidenceKind gateKind) =>
        RequiredGateEvidenceKinds(actionKind).Contains(gateKind);

    public static GateEvidenceKind[] RequiredGateEvidenceKinds(GovernedActionKind actionKind) => actionKind switch
    {
        GovernedActionKind.MemoryPromotion or
        GovernedActionKind.MemoryPromotionRequested or
        GovernedActionKind.MemoryPromotionAccepted or
        GovernedActionKind.AcceptedMemoryVersionAppended => [GateEvidenceKind.MemoryKeyGate],

        GovernedActionKind.ToolExecution => [GateEvidenceKind.WorkspaceToolGate],

        GovernedActionKind.SourceApply or
        GovernedActionKind.SourceApplyExecutionRequested or
        GovernedActionKind.SourceApplyCommandExecuted => [GateEvidenceKind.SourceApplyExecutionGate],

        GovernedActionKind.SourceRollback or
        GovernedActionKind.SourceRollbackRequested or
        GovernedActionKind.SourceRollbackCommandExecuted or
        GovernedActionKind.RollbackExecution => [GateEvidenceKind.SourceRollbackGate],

        GovernedActionKind.WorkflowContinuation => [GateEvidenceKind.WorkflowGate],

        GovernedActionKind.ReleaseReadinessDecision or
        GovernedActionKind.ReleaseApproval => [GateEvidenceKind.ReleaseGate],

        GovernedActionKind.DeploymentApproval => [GateEvidenceKind.DeploymentGate],

        GovernedActionKind.MergeApproval => [GateEvidenceKind.MergeGate],

        _ => []
    };
}

public enum GovernanceKernelEventKind
{
    ActionRequested = 0,
    EvidenceAttached,
    GateEvidenceRecorded,
    ConscienceDecisionRequested,
    ThoughtLedgerWriteRequested,
    ThoughtLedgerRecorded,
    ConscienceDecisionRecorded,
    ActionBlocked,
    ActionAllowed,
    ActionExecuted,
    ActionFailed,
    ReceiptCreated,
    RollbackRequired,
    RollbackExecuted,
    MemoryPromotionRecorded,
    AuthorityBypassBlocked,
    AcceptedMemoryRetrievalRequested,
    AcceptedMemoryRetrieved,
    MemoryCitationBundleCreated,
    PlannerContextBundleCreated,
    MemoryInformedPlanProposed,
    PlanRiskReportCreated,
    SuggestedTestProfileCreated,
    KilljoyPlanReviewCreated,
    PlanningBoundaryReportCreated,
    CommitPackageRequestCreated,
    CommitFileManifestCreated,
    CommitEvidenceBundleCreated,
    CommitMessageProposalCreated,
    CommitReadinessReviewCreated,
    CommitPackageBoundaryReportCreated,
    CommitPackageBypassReportCreated,
    PullRequestCreationRequestCreated,
    PullRequestBranchValidationCreated,
    PullRequestEvidenceValidationCreated,
    PullRequestTextProposalCreated,
    PullRequestCreationGateCreated,
    DraftPullRequestCreated,
    PullRequestCreationBypassReportCreated,
    FeedbackLoopRequestCreated,
    CiObservationSnapshotCreated,
    ReviewFeedbackSnapshotCreated,
    FeedbackClassificationReportCreated,
    FeedbackRemediationPlanCreated,
    FeedbackReadinessReportCreated,
    FeedbackLoopBypassReportCreated,
    MergeReleaseSeparationRequestCreated,
    MergeReadinessEvidencePackageCreated,
    ReleaseReadinessEvidencePackageCreated,
    MergeReleaseBoundaryMapCreated,
    MergeReleaseSeparationRecordsCreated,
    MergeReleaseBypassReportCreated,
    FeedbackRemediationPackageCreated
}

public sealed record GovernedActionKernelEvent
{
    public required string EventId { get; init; }
    public required string RunId { get; init; }
    public required string ActionId { get; init; }
    public required GovernanceKernelEventKind EventKind { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string Summary { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public GovernedActionBoundary Boundary { get; init; } = GovernedActionBoundary.None;
    public required string Hash { get; init; }
    public string? PreviousEventHash { get; init; }
}

public sealed record GovernanceKernelVerificationResult
{
    public required bool Passed { get; init; }
    public string[] Issues { get; init; } = [];
    public int EventCount { get; init; }
    public int ActionCount { get; init; }
    public int GateEvidenceCount { get; init; }
    public int ConscienceDecisionCount { get; init; }
    public int ThoughtLedgerCount { get; init; }
}

public sealed class FileBackedGovernanceEventStore
{
    public const string ArtifactName = "governance-events.jsonl";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _runPath;

    public FileBackedGovernanceEventStore(string runPath)
    {
        _runPath = Path.GetFullPath(runPath);
    }

    public GovernedActionKernelEvent Append(
        string runId,
        string actionId,
        GovernanceKernelEventKind eventKind,
        string subjectKind,
        string subjectId,
        string summary,
        IEnumerable<string>? evidenceRefs = null,
        DateTimeOffset? createdAtUtc = null)
    {
        Directory.CreateDirectory(_runPath);
        var existing = ReadAll();
        var previousHash = existing.LastOrDefault()?.Hash;
        var draft = new GovernedActionKernelEvent
        {
            EventId = $"gov_evt_{Guid.NewGuid():N}",
            RunId = runId.Trim(),
            ActionId = actionId.Trim(),
            EventKind = eventKind,
            SubjectKind = subjectKind.Trim(),
            SubjectId = subjectId.Trim(),
            Summary = summary.Trim(),
            EvidenceRefs = (evidenceRefs ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToArray(),
            CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = GovernedActionBoundary.None,
            Hash = string.Empty,
            PreviousEventHash = previousHash
        };
        var evt = draft with { Hash = HashEvent(draft) };
        File.AppendAllText(Path.Combine(_runPath, ArtifactName), JsonSerializer.Serialize(evt, JsonOptions) + Environment.NewLine);
        return evt;
    }

    public GovernedActionKernelEvent[] ReadAll()
    {
        var path = Path.Combine(_runPath, ArtifactName);
        if (!File.Exists(path))
            return [];

        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<GovernedActionKernelEvent>(line, JsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    public GovernanceKernelVerificationResult VerifyIntegrity()
    {
        var events = ReadAll();
        var issues = new List<string>();
        string? previous = null;
        foreach (var evt in events)
        {
            if (!string.Equals(evt.PreviousEventHash, previous, StringComparison.OrdinalIgnoreCase))
                issues.Add($"PreviousHashMismatch:{evt.EventId}");
            if (!string.Equals(evt.Hash, HashEvent(evt), StringComparison.OrdinalIgnoreCase))
                issues.Add($"EventHashMismatch:{evt.EventId}");
            previous = evt.Hash;
        }

        return new GovernanceKernelVerificationResult
        {
            Passed = issues.Count == 0,
            Issues = issues.ToArray(),
            EventCount = events.Length,
            ActionCount = CountLines("governed-actions.jsonl"),
            GateEvidenceCount = CountLines("gate-evidence.jsonl"),
            ConscienceDecisionCount = CountLines("conscience-decisions.jsonl"),
            ThoughtLedgerCount = CountLines("thought-ledger.jsonl")
        };
    }

    public static string HashEvent(GovernedActionKernelEvent evt)
    {
        var payload = JsonSerializer.Serialize(new
        {
            evt.EventId,
            evt.RunId,
            evt.ActionId,
            evt.EventKind,
            evt.SubjectKind,
            evt.SubjectId,
            evt.Summary,
            evt.EvidenceRefs,
            evt.CreatedAtUtc,
            evt.Boundary,
            evt.PreviousEventHash
        }, JsonOptions);
        return ConscienceDecisionRecordHash.Sha256(payload);
    }

    private int CountLines(string artifact)
    {
        var path = Path.Combine(_runPath, artifact);
        return File.Exists(path) ? File.ReadAllLines(path).Count(line => !string.IsNullOrWhiteSpace(line)) : 0;
    }
}

public static class GovernanceKernelArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static void AppendJsonLine<T>(string runPath, string artifactName, T value)
    {
        Directory.CreateDirectory(runPath);
        File.AppendAllText(Path.Combine(runPath, artifactName), JsonSerializer.Serialize(value, JsonLineOptions) + Environment.NewLine);
    }

    public static GovernanceKernelVerificationResult VerifyAndWrite(string runPath)
    {
        var result = new FileBackedGovernanceEventStore(runPath).VerifyIntegrity();
        Directory.CreateDirectory(runPath);
        File.WriteAllText(Path.Combine(runPath, "governance-kernel-verification.json"), JsonSerializer.Serialize(result, JsonOptions));
        File.WriteAllText(Path.Combine(runPath, "governance-kernel-verification.md"), RenderMarkdown(result));
        return result;
    }

    private static string RenderMarkdown(GovernanceKernelVerificationResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Governance Kernel Verification");
        builder.AppendLine();
        builder.AppendLine($"Passed: `{result.Passed.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"Events: `{result.EventCount}`");
        builder.AppendLine($"Actions: `{result.ActionCount}`");
        builder.AppendLine($"Gate evidence: `{result.GateEvidenceCount}`");
        builder.AppendLine($"Conscience decisions: `{result.ConscienceDecisionCount}`");
        builder.AppendLine($"ThoughtLedger entries: `{result.ThoughtLedgerCount}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: verification is inspection only. It does not allow, approve, execute, continue workflow, promote memory, release, merge, deploy, commit, push, or create a pull request.");
        if (result.Issues.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Issues");
            foreach (var issue in result.Issues)
                builder.AppendLine($"- `{issue}`");
        }
        return builder.ToString();
    }
}
