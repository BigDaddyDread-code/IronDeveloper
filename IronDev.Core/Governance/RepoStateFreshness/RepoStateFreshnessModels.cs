using IronDev.Core.Governance;

namespace IronDev.Core.Governance.RepoStateFreshness;

public sealed record RepoStateFreshnessRequest
{
    public required string CheckId { get; init; }
    public required string Repository { get; init; }
    public required string RunId { get; init; }
    public required RunAuthorityOperationKind OperationKind { get; init; }

    public required RepoStateExpectation? Expected { get; init; }
    public required RepoStateObservation? Observed { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record RepoStateExpectation
{
    public required string BaseBranch { get; init; }
    public required string BaseSha { get; init; }

    public required string HeadBranch { get; init; }
    public required string HeadSha { get; init; }

    public required string PatchHash { get; init; }

    public required string? CommitHeadSha { get; init; }
    public required string? RemoteBranch { get; init; }
    public required string? RemoteSha { get; init; }

    public required DateTimeOffset ValidationObservedAtUtc { get; init; }
    public required string ValidationBaseSha { get; init; }
    public required string ValidationHeadSha { get; init; }
    public required string ValidationPatchHash { get; init; }
    public required DateTimeOffset ValidationExpiresAtUtc { get; init; }
}

public sealed record RepoStateObservation
{
    public required string BaseBranch { get; init; }
    public required string BaseSha { get; init; }

    public required string HeadBranch { get; init; }
    public required string HeadSha { get; init; }

    public required RepoWorktreeState WorktreeState { get; init; }
    public required PatchApplicability PatchApplicability { get; init; }

    public required string? CommitHeadSha { get; init; }
    public required string? RemoteBranch { get; init; }
    public required string? RemoteSha { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public enum RepoWorktreeState
{
    Unknown = 0,
    Clean = 1,
    Dirty = 2
}

public enum PatchApplicability
{
    Unknown = 0,
    Applies = 1,
    DoesNotApply = 2
}

public enum RepoStateFreshnessVerdict
{
    Fresh = 1,
    Blocked = 2,
    Stale = 3,
    Contradictory = 4
}

public enum RepoStateFreshnessIssueKind
{
    None = 0,
    MissingRequest = 1,
    MissingExpectedState = 2,
    MissingObservedState = 3,
    DirtyWorktree = 4,
    UnknownWorktree = 5,
    BaseBranchMoved = 6,
    HeadBranchMoved = 7,
    PatchNoLongerApplies = 8,
    PatchApplicabilityUnknown = 9,
    CommitHeadChanged = 10,
    RemoteChanged = 11,
    StaleValidation = 12,
    ValidationMismatch = 13,
    ContradictoryEvidence = 14
}

public sealed record RepoStateFreshnessResult
{
    public required bool IsFreshForMutation { get; init; }
    public required RepoStateFreshnessVerdict Verdict { get; init; }

    public required IReadOnlyList<RepoStateFreshnessIssueKind> IssueKinds { get; init; }
    public required IReadOnlyList<string> BlockingReasons { get; init; }
    public required IReadOnlyList<string> MissingEvidenceRefs { get; init; }
    public required IReadOnlyList<string> NextSafeActions { get; init; }

    public required RepoStateFreshnessBoundary Boundary { get; init; }
}

public sealed record RepoStateFreshnessBoundary
{
    public required bool CanExplainFreshness { get; init; }
    public required bool CanInspectEvidence { get; init; }

    public required bool CanRefreshEvidence { get; init; }
    public required bool CanRevalidate { get; init; }
    public required bool CanRegeneratePatch { get; init; }
    public required bool CanApplySource { get; init; }
    public required bool CanRollbackSource { get; init; }
    public required bool CanCommit { get; init; }
    public required bool CanPush { get; init; }
    public required bool CanCreatePullRequest { get; init; }
    public required bool CanContinueWorkflow { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanAcceptApproval { get; init; }

    public static RepoStateFreshnessBoundary GuardOnly { get; } = new()
    {
        CanExplainFreshness = true,
        CanInspectEvidence = true,

        CanRefreshEvidence = false,
        CanRevalidate = false,
        CanRegeneratePatch = false,
        CanApplySource = false,
        CanRollbackSource = false,
        CanCommit = false,
        CanPush = false,
        CanCreatePullRequest = false,
        CanContinueWorkflow = false,
        CanPromoteMemory = false,
        CanSatisfyPolicy = false,
        CanAcceptApproval = false
    };
}
