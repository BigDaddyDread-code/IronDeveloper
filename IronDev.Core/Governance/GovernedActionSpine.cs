using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Core.Governance;

public enum GovernedActionKind
{
    Unknown = 0,
    PatchProposalRunStarted,
    DisposableWorkspaceCreated,
    PatchArtifactExported,
    ChangedFilesDetected,
    WorkspaceTestsExecuted,
    WorkspaceToolRequestCreated,
    WorkspaceToolGateEvaluated,
    WorkspaceCommandExecuted,
    WorkspaceToolResultRecorded,
    PatchContextBundleCreated,
    ModelPatchSuggestionRequested,
    ModelPatchSuggestionReceived,
    WorkspacePatchEditApplied,
    ModelTestFailureAnalysisRequested,
    ModelTestFailureAnalysisReceived,
    ModelPatchReviewRequested,
    ModelPatchReviewReceived,
    PatchRefinementIterationCompleted,
    ReviewPackageCreated,
    PatchRunStatusRead,
    PatchRunListed,
    PatchWorkspaceCleaned,
    ToolExecution,
    MemoryPromotion,
    AcceptedMemoryMutation,
    SourceApply,
    RollbackExecution,
    WorkflowContinuation,
    ReleaseReadinessDecision,
    ReleaseApproval,
    DeploymentApproval,
    MergeApproval,
    ProductionDeployment,
    DirectGitCommitToSource,
    DirectGitPush,
    DirectAcceptedMemoryWrite,
    UIApprovalCreation,
    AgentSelfApproval,
    WorkflowContinuationFromTextEvidence
}

public enum GovernedActionClassification
{
    NonAuthority = 0,
    AuthorityBearing,
    ForbiddenOrUnsupported
}

public enum AuthorityActionImplementationStatus
{
    ImplementedNonAuthorityEvent = 0,
    RegisteredOnly,
    Forbidden,
    FutureBlock
}

public sealed record GovernedActionBoundary
{
    public bool SourceRepoMutated { get; init; }
    public bool SourceApplied { get; init; }
    public bool GitCommitCreated { get; init; }
    public bool GitPushPerformed { get; init; }
    public bool PullRequestCreated { get; init; }
    public bool ApprovalGranted { get; init; }
    public bool PolicySatisfied { get; init; }
    public bool ReleaseApproved { get; init; }
    public bool WorkflowContinued { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool AgentDispatched { get; init; }
    public bool ModelCalled { get; init; }

    public static GovernedActionBoundary None { get; } = new();
}

public sealed record GovernedAction
{
    public required string ActionId { get; init; }
    public required GovernedActionKind ActionKind { get; init; }
    public required GovernedActionClassification Classification { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string RequestedBy { get; init; }
    public required string SourceComponent { get; init; }
    public required string RunId { get; init; }
    public string? WorkflowRunId { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required bool RequiresConscience { get; init; }
    public required bool RequiresThoughtLedger { get; init; }
    public required bool AllowedInCurrentBlock { get; init; }
    public GovernedActionBoundary Boundary { get; init; } = GovernedActionBoundary.None;

    public static GovernedAction Create(
        GovernedActionKind actionKind,
        string subjectKind,
        string subjectId,
        string requestedBy,
        string sourceComponent,
        string runId,
        IEnumerable<string>? evidenceRefs = null,
        string? workflowRunId = null,
        DateTimeOffset? createdAtUtc = null)
    {
        var classification = GovernedActionClassifier.Classify(actionKind);
        return new GovernedAction
        {
            ActionId = $"gov_act_{Guid.NewGuid():N}",
            ActionKind = actionKind,
            Classification = classification,
            SubjectKind = subjectKind.Trim(),
            SubjectId = subjectId.Trim(),
            RequestedBy = requestedBy.Trim(),
            SourceComponent = sourceComponent.Trim(),
            RunId = runId.Trim(),
            WorkflowRunId = string.IsNullOrWhiteSpace(workflowRunId) ? null : workflowRunId.Trim(),
            EvidenceRefs = (evidenceRefs ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow,
            RequiresConscience = GovernedActionClassifier.RequiresConscience(actionKind),
            RequiresThoughtLedger = GovernedActionClassifier.RequiresThoughtLedger(actionKind),
            AllowedInCurrentBlock = GovernedActionClassifier.IsAllowedInCurrentBlock(actionKind),
            Boundary = GovernedActionBoundary.None
        };
    }
}

public static class GovernedActionClassifier
{
    private static readonly HashSet<GovernedActionKind> NonAuthorityActions =
    [
        GovernedActionKind.PatchProposalRunStarted,
        GovernedActionKind.DisposableWorkspaceCreated,
        GovernedActionKind.PatchArtifactExported,
        GovernedActionKind.ChangedFilesDetected,
        GovernedActionKind.WorkspaceTestsExecuted,
        GovernedActionKind.WorkspaceToolRequestCreated,
        GovernedActionKind.WorkspaceToolGateEvaluated,
        GovernedActionKind.WorkspaceCommandExecuted,
        GovernedActionKind.WorkspaceToolResultRecorded,
        GovernedActionKind.PatchContextBundleCreated,
        GovernedActionKind.ModelPatchSuggestionRequested,
        GovernedActionKind.ModelPatchSuggestionReceived,
        GovernedActionKind.WorkspacePatchEditApplied,
        GovernedActionKind.ModelTestFailureAnalysisRequested,
        GovernedActionKind.ModelTestFailureAnalysisReceived,
        GovernedActionKind.ModelPatchReviewRequested,
        GovernedActionKind.ModelPatchReviewReceived,
        GovernedActionKind.PatchRefinementIterationCompleted,
        GovernedActionKind.ReviewPackageCreated,
        GovernedActionKind.PatchRunStatusRead,
        GovernedActionKind.PatchRunListed,
        GovernedActionKind.PatchWorkspaceCleaned
    ];

    private static readonly HashSet<GovernedActionKind> AuthorityBearingActions =
    [
        GovernedActionKind.ToolExecution,
        GovernedActionKind.MemoryPromotion,
        GovernedActionKind.AcceptedMemoryMutation,
        GovernedActionKind.SourceApply,
        GovernedActionKind.RollbackExecution,
        GovernedActionKind.WorkflowContinuation,
        GovernedActionKind.ReleaseReadinessDecision,
        GovernedActionKind.ReleaseApproval,
        GovernedActionKind.DeploymentApproval,
        GovernedActionKind.MergeApproval
    ];

    public static GovernedActionClassification Classify(GovernedActionKind actionKind)
    {
        if (NonAuthorityActions.Contains(actionKind))
            return GovernedActionClassification.NonAuthority;

        if (AuthorityBearingActions.Contains(actionKind))
            return GovernedActionClassification.AuthorityBearing;

        return GovernedActionClassification.ForbiddenOrUnsupported;
    }

    public static GovernedActionClassification Classify(string? actionKind)
    {
        if (!Enum.TryParse<GovernedActionKind>(actionKind, ignoreCase: true, out var parsed))
            return GovernedActionClassification.ForbiddenOrUnsupported;

        return Classify(parsed);
    }

    public static bool IsAllowedInCurrentBlock(GovernedActionKind actionKind) =>
        Classify(actionKind) == GovernedActionClassification.NonAuthority;

    public static bool RequiresConscience(GovernedActionKind actionKind) =>
        Classify(actionKind) == GovernedActionClassification.AuthorityBearing;

    public static bool RequiresThoughtLedger(GovernedActionKind actionKind) =>
        Classify(actionKind) == GovernedActionClassification.AuthorityBearing;
}

public sealed record AuthorityActionInventoryEntry
{
    public required GovernedActionKind ActionKind { get; init; }
    public required GovernedActionClassification Classification { get; init; }
    public required string Description { get; init; }
    public required bool AllowedInCurrentBlock { get; init; }
    public required bool RequiresConscience { get; init; }
    public required bool RequiresThoughtLedger { get; init; }
    public string[] RequiredEvidenceKinds { get; init; } = [];
    public string[] RequiredPolicyKinds { get; init; } = [];
    public string[] RequiredStores { get; init; } = [];
    public string[] ForbiddenDirectPaths { get; init; } = [];
    public required AuthorityActionImplementationStatus CurrentImplementationStatus { get; init; }
}

public static class AuthorityActionInventory
{
    public static IReadOnlyList<AuthorityActionInventoryEntry> All { get; } =
    [
        Patch(GovernedActionKind.PatchProposalRunStarted, "Patch proposal run was started."),
        Patch(GovernedActionKind.DisposableWorkspaceCreated, "Disposable patch workspace was created."),
        Patch(GovernedActionKind.PatchArtifactExported, "Patch artifact was exported from a disposable workspace."),
        Patch(GovernedActionKind.ChangedFilesDetected, "Changed files were detected for review."),
        Patch(GovernedActionKind.WorkspaceTestsExecuted, "Workspace tests were executed or explicitly skipped for review."),
        Patch(GovernedActionKind.WorkspaceToolRequestCreated, "Workspace-scoped tool request was created for a patch run."),
        Patch(GovernedActionKind.WorkspaceToolGateEvaluated, "Workspace-scoped tool gate was evaluated for a patch run."),
        Patch(GovernedActionKind.WorkspaceCommandExecuted, "Workspace-confined command was executed for patch-run evidence."),
        Patch(GovernedActionKind.WorkspaceToolResultRecorded, "Workspace tool result evidence was recorded for a patch run."),
        Patch(GovernedActionKind.PatchContextBundleCreated, "Bounded patch task context bundle was created for model assistance."),
        Patch(GovernedActionKind.ModelPatchSuggestionRequested, "Model patch suggestion was requested as proposal evidence."),
        Patch(GovernedActionKind.ModelPatchSuggestionReceived, "Model patch suggestion was received as proposal evidence."),
        Patch(GovernedActionKind.WorkspacePatchEditApplied, "Model-suggested edit plan was applied or blocked inside the disposable workspace."),
        Patch(GovernedActionKind.ModelTestFailureAnalysisRequested, "Model test-failure analysis was requested as proposal evidence."),
        Patch(GovernedActionKind.ModelTestFailureAnalysisReceived, "Model test-failure analysis was received as proposal evidence."),
        Patch(GovernedActionKind.ModelPatchReviewRequested, "Model patch review was requested as review evidence."),
        Patch(GovernedActionKind.ModelPatchReviewReceived, "Model patch review was received as review evidence."),
        Patch(GovernedActionKind.PatchRefinementIterationCompleted, "Bounded patch refinement iteration was completed inside the disposable workspace."),
        Patch(GovernedActionKind.ReviewPackageCreated, "Review package artifacts were created."),
        Patch(GovernedActionKind.PatchRunStatusRead, "Patch run status was inspected."),
        Patch(GovernedActionKind.PatchRunListed, "Patch runs were listed."),
        Patch(GovernedActionKind.PatchWorkspaceCleaned, "Disposable patch workspace was cleaned."),
        Authority(GovernedActionKind.ToolExecution, "Run a tool with effects outside passive inspection."),
        Authority(GovernedActionKind.MemoryPromotion, "Promote memory from proposal/evidence into accepted memory."),
        Authority(GovernedActionKind.AcceptedMemoryMutation, "Mutate accepted memory directly."),
        Authority(GovernedActionKind.SourceApply, "Apply source changes to a source repository."),
        Authority(GovernedActionKind.RollbackExecution, "Execute rollback source mutation."),
        Authority(GovernedActionKind.WorkflowContinuation, "Continue workflow state after governed evidence."),
        Authority(GovernedActionKind.ReleaseReadinessDecision, "Record release readiness decision."),
        Authority(GovernedActionKind.ReleaseApproval, "Approve release."),
        Authority(GovernedActionKind.DeploymentApproval, "Approve deployment."),
        Authority(GovernedActionKind.MergeApproval, "Approve merge."),
        Forbidden(GovernedActionKind.ProductionDeployment, "Direct production deployment is unsupported in this spine."),
        Forbidden(GovernedActionKind.DirectGitCommitToSource, "Direct source commit bypasses governed source apply."),
        Forbidden(GovernedActionKind.DirectGitPush, "Direct git push bypasses governed release/source controls."),
        Forbidden(GovernedActionKind.DirectAcceptedMemoryWrite, "Direct accepted-memory write bypasses promotion governance."),
        Forbidden(GovernedActionKind.UIApprovalCreation, "UI cannot create approval authority."),
        Forbidden(GovernedActionKind.AgentSelfApproval, "Agent cannot approve itself."),
        Forbidden(GovernedActionKind.WorkflowContinuationFromTextEvidence, "Workflow continuation cannot be created from receipt text alone.")
    ];

    public static AuthorityActionInventoryEntry Get(GovernedActionKind actionKind) =>
        All.FirstOrDefault(entry => entry.ActionKind == actionKind) ??
        Forbidden(actionKind, "Unknown action kind is forbidden until explicitly registered.");

    public static AuthorityActionInventoryEntry Get(string? actionKind)
    {
        if (!Enum.TryParse<GovernedActionKind>(actionKind, ignoreCase: true, out var parsed))
            return Forbidden(GovernedActionKind.Unknown, "Unknown action kind is forbidden until explicitly registered.");

        return Get(parsed);
    }

    private static AuthorityActionInventoryEntry Patch(GovernedActionKind actionKind, string description) =>
        new()
        {
            ActionKind = actionKind,
            Classification = GovernedActionClassification.NonAuthority,
            Description = description,
            AllowedInCurrentBlock = true,
            RequiresConscience = false,
            RequiresThoughtLedger = false,
            RequiredEvidenceKinds = ["PatchProposalRun", "RunScopedGovernanceEvent"],
            RequiredPolicyKinds = [],
            RequiredStores = ["PatchRunArtifacts"],
            ForbiddenDirectPaths = ["SourceApply", "GitCommit", "GitPush", "PullRequestCreation"],
            CurrentImplementationStatus = AuthorityActionImplementationStatus.ImplementedNonAuthorityEvent
        };

    private static AuthorityActionInventoryEntry Authority(GovernedActionKind actionKind, string description) =>
        new()
        {
            ActionKind = actionKind,
            Classification = GovernedActionClassification.AuthorityBearing,
            Description = description,
            AllowedInCurrentBlock = false,
            RequiresConscience = true,
            RequiresThoughtLedger = true,
            RequiredEvidenceKinds = ["ConscienceDecision", "ThoughtLedger", "AcceptedApproval", "PolicySatisfaction", "ActionSpecificReceipt"],
            RequiredPolicyKinds = ["ProjectAutonomyPolicy", "ApprovalRule"],
            RequiredStores = ["FutureGovernedActionStore"],
            ForbiddenDirectPaths = ["DirectExecution", "ReceiptTextOnly", "UIStateOnly", "AgentSelfApproval"],
            CurrentImplementationStatus = AuthorityActionImplementationStatus.RegisteredOnly
        };

    private static AuthorityActionInventoryEntry Forbidden(GovernedActionKind actionKind, string description) =>
        new()
        {
            ActionKind = actionKind,
            Classification = GovernedActionClassification.ForbiddenOrUnsupported,
            Description = description,
            AllowedInCurrentBlock = false,
            RequiresConscience = true,
            RequiresThoughtLedger = true,
            RequiredEvidenceKinds = ["ExplicitFutureDesign"],
            RequiredPolicyKinds = ["ExplicitFuturePolicy"],
            RequiredStores = [],
            ForbiddenDirectPaths = ["DirectExecution", "TextEvidenceOnly", "UIStateOnly", "AgentSelfApproval"],
            CurrentImplementationStatus = AuthorityActionImplementationStatus.Forbidden
        };
}

public sealed record RunScopedGovernanceEvent
{
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public required string ActionId { get; init; }
    public required string ActionKind { get; init; }
    public required string Classification { get; init; }
    public required string RunId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public GovernedActionBoundary Boundary { get; init; } = GovernedActionBoundary.None;
    public required string Message { get; init; }

    public static RunScopedGovernanceEvent FromAction(GovernedAction action, string eventType, string message) =>
        new()
        {
            EventId = $"gov_evt_{Guid.NewGuid():N}",
            EventType = eventType,
            ActionId = action.ActionId,
            ActionKind = action.ActionKind.ToString(),
            Classification = action.Classification.ToString(),
            RunId = action.RunId,
            SubjectKind = action.SubjectKind,
            SubjectId = action.SubjectId,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            EvidenceRefs = action.EvidenceRefs,
            Boundary = action.Boundary,
            Message = message
        };
}

public enum ConscienceDecisionOutcome
{
    Allow = 0,
    Block,
    RequiresHumanReview,
    NotImplemented
}

public enum ConscienceDecisionRiskLevel
{
    Low = 0,
    Medium,
    High,
    Critical
}

public sealed record ConscienceDecisionEvidenceRef
{
    public required string RefId { get; init; }
    public required string EvidenceKind { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record ConscienceDecision
{
    public required string DecisionId { get; init; }
    public required string ActionId { get; init; }
    public required GovernedActionKind ActionKind { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string RequestedBy { get; init; }
    public ConscienceDecisionEvidenceRef[] EvidenceRefs { get; init; } = [];
    public string[] PolicyRefs { get; init; } = [];
    public required ConscienceDecisionRiskLevel RiskLevel { get; init; }
    public required ConscienceDecisionOutcome Decision { get; init; }
    public string[] BlockReasons { get; init; } = [];
    public required bool RequiredHumanReview { get; init; }
    public string? ThoughtLedgerRef { get; init; }
    public required string DecisionHash { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ConscienceDecisionExecutionEvaluation
{
    public required bool IsExecutable { get; init; }
    public required string Status { get; init; }
    public string[] Reasons { get; init; } = [];
}

public static class ConscienceDecisionEvaluator
{
    public static ConscienceDecisionExecutionEvaluation Evaluate(GovernedActionKind actionKind, ConscienceDecision? decision, DateTimeOffset? now = null)
    {
        var classification = GovernedActionClassifier.Classify(actionKind);
        if (classification == GovernedActionClassification.NonAuthority)
            return Pass("NonAuthorityActionDoesNotRequireConscienceDecision");

        if (classification == GovernedActionClassification.ForbiddenOrUnsupported)
            return Block("ForbiddenOrUnsupportedAction", "Forbidden or unsupported actions are not executable.");

        var inventoryEntry = AuthorityActionInventory.Get(actionKind);
        if (classification == GovernedActionClassification.AuthorityBearing && !inventoryEntry.AllowedInCurrentBlock)
            return Block("ActionNotAllowedInCurrentBlock", "Authority-bearing action is registered but not executable in the current block.");

        if (decision is null)
            return Block("MissingConscienceDecision", "Authority-bearing actions require a Conscience decision.");

        if (decision.ActionKind != actionKind)
            return Block("ConscienceDecisionActionMismatch", "Conscience decision action kind does not match the governed action.");

        if (decision.ExpiresAtUtc is not null && decision.ExpiresAtUtc <= (now ?? DateTimeOffset.UtcNow))
            return Block("ConscienceDecisionExpired", "Conscience decision is expired.");

        return decision.Decision == ConscienceDecisionOutcome.Allow
            ? Pass("ConscienceDecisionAllowsFutureExecution")
            : Block(decision.Decision.ToString(), "Conscience decision does not allow execution.");
    }

    private static ConscienceDecisionExecutionEvaluation Pass(string status) =>
        new() { IsExecutable = true, Status = status };

    private static ConscienceDecisionExecutionEvaluation Block(string status, string reason) =>
        new() { IsExecutable = false, Status = status, Reasons = [reason] };
}

public static class ConscienceDecisionHash
{
    public static string Compute(ConscienceDecision decision)
    {
        var payload = JsonSerializer.Serialize(new
        {
            decision.DecisionId,
            decision.ActionId,
            decision.ActionKind,
            decision.SubjectKind,
            decision.SubjectId,
            decision.RequestedBy,
            decision.EvidenceRefs,
            decision.PolicyRefs,
            decision.RiskLevel,
            decision.Decision,
            decision.BlockReasons,
            decision.RequiredHumanReview,
            decision.ThoughtLedgerRef,
            decision.ExpiresAtUtc,
            decision.CreatedAtUtc
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}

public enum ThoughtLedgerRequirementFailureMode
{
    None = 0,
    FailClosed
}

public sealed record ThoughtLedgerRequirement
{
    public required GovernedActionKind ActionKind { get; init; }
    public required bool RequiresThoughtLedger { get; init; }
    public required GovernedActionClassification RequiredForClassification { get; init; }
    public required bool RequiredBeforeExecution { get; init; }
    public required ThoughtLedgerRequirementFailureMode FailureMode { get; init; }
}

public sealed record ThoughtLedgerRequirementEvaluation
{
    public required bool IsSatisfied { get; init; }
    public required string Status { get; init; }
    public string[] Issues { get; init; } = [];
}

public static class ThoughtLedgerRequirementCatalog
{
    public static ThoughtLedgerRequirement Get(GovernedActionKind actionKind)
    {
        var classification = GovernedActionClassifier.Classify(actionKind);
        var requires = classification == GovernedActionClassification.AuthorityBearing;
        return new ThoughtLedgerRequirement
        {
            ActionKind = actionKind,
            RequiresThoughtLedger = requires,
            RequiredForClassification = classification,
            RequiredBeforeExecution = requires,
            FailureMode = requires ? ThoughtLedgerRequirementFailureMode.FailClosed : ThoughtLedgerRequirementFailureMode.None
        };
    }

    public static ThoughtLedgerRequirementEvaluation Evaluate(GovernedActionKind actionKind, string? thoughtLedgerRef)
    {
        var requirement = Get(actionKind);
        if (!requirement.RequiresThoughtLedger)
            return new ThoughtLedgerRequirementEvaluation { IsSatisfied = true, Status = "ThoughtLedgerNotRequired" };

        if (string.IsNullOrWhiteSpace(thoughtLedgerRef))
        {
            return new ThoughtLedgerRequirementEvaluation
            {
                IsSatisfied = false,
                Status = "MissingThoughtLedgerFailClosed",
                Issues = ["Authority-bearing actions require ThoughtLedger evidence before execution."]
            };
        }

        return new ThoughtLedgerRequirementEvaluation { IsSatisfied = true, Status = "ThoughtLedgerEvidencePresentForFutureExecution" };
    }
}

public sealed record AuthorityBypassTestLane
{
    public required string LaneId { get; init; }
    public required GovernedActionKind ActionKind { get; init; }
    public required string BypassShape { get; init; }
    public required GovernedActionClassification ExpectedClassification { get; init; }
    public required bool ExecutableInCurrentBlock { get; init; }
    public string[] RequiredFutureEvidence { get; init; } = [];
}

public static class AuthorityBypassTestLaneCatalog
{
    public static IReadOnlyList<AuthorityBypassTestLane> All { get; } =
    [
        Lane("memory-promotion-without-conscience", GovernedActionKind.MemoryPromotion, "Memory promotion without Conscience", ["ConscienceDecision", "ThoughtLedger"]),
        Lane("memory-promotion-without-thoughtledger", GovernedActionKind.MemoryPromotion, "Memory promotion without ThoughtLedger", ["ThoughtLedger"]),
        Lane("tool-execution-without-gate", GovernedActionKind.ToolExecution, "Tool execution without gate", ["ConscienceDecision", "ThoughtLedger", "ToolGateDecision"]),
        Lane("source-apply-without-accepted-approval", GovernedActionKind.SourceApply, "Source apply without accepted approval", ["AcceptedApproval"]),
        Lane("source-apply-without-policy-satisfaction", GovernedActionKind.SourceApply, "Source apply without policy satisfaction", ["PolicySatisfaction"]),
        Lane("source-apply-without-patch-artifact", GovernedActionKind.SourceApply, "Source apply without patch artifact", ["PatchArtifact"]),
        Lane("source-apply-without-dry-run", GovernedActionKind.SourceApply, "Source apply without dry-run", ["DryRunReceipt"]),
        Lane("source-apply-without-rollback-plan", GovernedActionKind.SourceApply, "Source apply without rollback plan", ["RollbackPlan"]),
        Lane("workflow-continuation-from-receipt-text", GovernedActionKind.WorkflowContinuationFromTextEvidence, "Workflow continuation from receipt text", ["ConscienceDecision", "ThoughtLedger"]),
        Lane("release-readiness-decision-from-report-text", GovernedActionKind.ReleaseReadinessDecision, "Release readiness decision from report text", ["ConscienceDecision", "ThoughtLedger"]),
        Lane("ui-approval-creation", GovernedActionKind.UIApprovalCreation, "UI approval creation", ["ExplicitHumanApprovalPath"]),
        Lane("agent-self-approval", GovernedActionKind.AgentSelfApproval, "Agent self-approval", ["HumanActor"]),
        Lane("direct-git-push-from-irondev-action-path", GovernedActionKind.DirectGitPush, "Direct git push from IronDev action path", ["GovernedSourcePath"])
    ];

    private static AuthorityBypassTestLane Lane(string laneId, GovernedActionKind actionKind, string bypassShape, string[] requiredFutureEvidence)
    {
        var classification = GovernedActionClassifier.Classify(actionKind);
        return new AuthorityBypassTestLane
        {
            LaneId = laneId,
            ActionKind = actionKind,
            BypassShape = bypassShape,
            ExpectedClassification = classification,
            ExecutableInCurrentBlock = false,
            RequiredFutureEvidence = requiredFutureEvidence
        };
    }
}
