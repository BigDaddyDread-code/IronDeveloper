namespace IronDev.Core.Governance.InterruptedRunRecovery;

public enum InterruptedRunStage
{
    Unknown = 0,
    WorkspaceCreatedNoPatch = 1,
    PatchCreatedNoValidation = 2,
    ValidationFailed = 3,
    SourceApplyStartedNotCompleted = 4,
    CommitPackageCreatedNoCommit = 5,
    CommitCreatedNoPush = 6,
    PushCompletedNoPullRequest = 7
}

public enum InterruptedRunRecoveryState
{
    Blocked = 1,
    NeedsHumanReview = 2,
    NeedsFreshAuthority = 3,
    NeedsRollbackDecision = 4,
    NeedsValidationEvidence = 5,
    NeedsPullRequestCreationDecision = 6
}

public enum InterruptedRunValidationOutcome
{
    Unknown = 0,
    Passed = 1,
    Failed = 2,
    Inconclusive = 3
}

public enum InterruptedRunWorktreeState
{
    Unknown = 0,
    Clean = 1,
    Dirty = 2,
    Mismatched = 3,
    ApplyFailed = 4
}

public sealed record InterruptedRunEvidenceSnapshot
{
    public required string RunId { get; init; }

    public IReadOnlyCollection<string>? WorkspaceEvidenceRefs { get; init; } = [];
    public IReadOnlyCollection<string>? PatchProposalEvidenceRefs { get; init; } = [];
    public IReadOnlyCollection<string>? PatchPackageEvidenceRefs { get; init; } = [];

    public IReadOnlyCollection<string>? ValidationResultPackageEvidenceRefs { get; init; } = [];
    public InterruptedRunValidationOutcome ValidationOutcome { get; init; } = InterruptedRunValidationOutcome.Unknown;

    public IReadOnlyCollection<string>? SourceApplyStartedEvidenceRefs { get; init; } = [];
    public IReadOnlyCollection<string>? CompletedSourceApplyReceiptRefs { get; init; } = [];
    public InterruptedRunWorktreeState WorktreeState { get; init; } = InterruptedRunWorktreeState.Unknown;

    public IReadOnlyCollection<string>? CommitPackageEvidenceRefs { get; init; } = [];
    public IReadOnlyCollection<string>? CommitReceiptRefs { get; init; } = [];
    public IReadOnlyCollection<string>? CommitHashEvidenceRefs { get; init; } = [];

    public IReadOnlyCollection<string>? PushReceiptRefs { get; init; } = [];
    public IReadOnlyCollection<string>? RemoteBranchEvidenceRefs { get; init; } = [];
    public IReadOnlyCollection<string>? DraftPullRequestReceiptRefs { get; init; } = [];

    public IReadOnlyCollection<string>? HostileTextEvidenceRefs { get; init; } = [];
    public IReadOnlyCollection<string>? UiStateEvidenceRefs { get; init; } = [];
    public IReadOnlyCollection<string>? MemoryEvidenceRefs { get; init; } = [];
    public IReadOnlyCollection<string>? HistoricalApprovalEvidenceRefs { get; init; } = [];
}

public sealed record InterruptedRunRecoveryReport
{
    public required string RunId { get; init; }
    public required InterruptedRunStage DetectedStage { get; init; }
    public required InterruptedRunRecoveryState RecoveryState { get; init; }
    public required IReadOnlyList<string> CompletedEvidenceRefs { get; init; }
    public required IReadOnlyList<string> MissingEvidenceRefs { get; init; }
    public required IReadOnlyList<string> BlockingReasons { get; init; }
    public required IReadOnlyList<string> NextSafeActions { get; init; }
    public required RunRecoveryBoundary Boundary { get; init; }
}

public sealed record RunRecoveryBoundary
{
    public required bool CanExplainState { get; init; }
    public required bool CanInspectEvidence { get; init; }

    public required bool CanResumeRun { get; init; }
    public required bool CanRetryStep { get; init; }
    public required bool CanApplySource { get; init; }
    public required bool CanRollbackSource { get; init; }
    public required bool CanCreateCommit { get; init; }
    public required bool CanPush { get; init; }
    public required bool CanCreatePullRequest { get; init; }
    public required bool CanContinueWorkflow { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanAcceptApproval { get; init; }

    public static RunRecoveryBoundary Diagnosis { get; } = new()
    {
        CanExplainState = true,
        CanInspectEvidence = true,
        CanResumeRun = false,
        CanRetryStep = false,
        CanApplySource = false,
        CanRollbackSource = false,
        CanCreateCommit = false,
        CanPush = false,
        CanCreatePullRequest = false,
        CanContinueWorkflow = false,
        CanPromoteMemory = false,
        CanSatisfyPolicy = false,
        CanAcceptApproval = false
    };
}

public sealed record InterruptedRunRecoveryReportValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
}
