namespace IronDev.Core.Governance;

public enum RunProfileKind
{
    ProposalOnly = 1
}

public static class ProposalOnlyOperationKinds
{
    public const string RepoInspect = "RepoInspect";
    public const string TaskInterpretation = "TaskInterpretation";
    public const string DisposableWorkspaceCreate = "DisposableWorkspaceCreate";
    public const string DisposableWorkspaceModify = "DisposableWorkspaceModify";
    public const string DisposableWorkspaceValidate = "DisposableWorkspaceValidate";
    public const string PatchProposal = "PatchProposal";
    public const string PatchPackageWrite = "PatchPackageWrite";
    public const string GovernedStatusInspect = "GovernedStatusInspect";

    public const string SourceApply = "SourceApply";
    public const string Rollback = "Rollback";
    public const string Commit = "Commit";
    public const string Push = "Push";
    public const string DraftPullRequest = "DraftPullRequest";
    public const string ReadyForReview = "ReadyForReview";
    public const string Merge = "Merge";
    public const string Release = "Release";
    public const string Deployment = "Deployment";
    public const string MemoryPromotion = "MemoryPromotion";
    public const string WorkflowContinuation = "WorkflowContinuation";
    public const string ApprovalRequestCreate = "ApprovalRequestCreate";
    public const string PolicySatisfaction = "PolicySatisfaction";
    public const string ProviderMutation = "ProviderMutation";
    public const string PackagePublication = "PackagePublication";
}

public sealed record ProposalOnlyRunProfileEvaluationRequest
{
    public required string OperationId { get; init; }
    public required string OperationKind { get; init; }
    public required string Subject { get; init; }

    public required string RepoId { get; init; }
    public required string Branch { get; init; }

    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> ArtifactRefs { get; init; } = [];
    public IReadOnlyList<string> RequestedPaths { get; init; } = [];

    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record ProposalOnlyRunProfileEvaluationResult
{
    public required bool IsAllowed { get; init; }
    public required GovernedOperationStatus Status { get; init; }
    public required GovernedOperationStatusValidationResult StatusValidation { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> RedFlags { get; init; }
}

public sealed record ProposalOnlyRunProfileBoundary
{
    public bool ProfileOnly { get; init; } = true;
    public bool EvidenceOnly { get; init; } = true;
    public bool CanCreateProposalEvidence { get; init; } = true;
    public bool CanWritePatchPackageArtifacts { get; init; } = true;
    public bool CanMutateDisposableWorkspace { get; init; } = true;
    public bool CanApprove { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanExecuteSourceApply { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanCreatePullRequest { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanRollback { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanCreateAuthorityRecords { get; init; }
    public bool CanMutateProvider { get; init; }
    public bool CanPublishPackages { get; init; }

    public static ProposalOnlyRunProfileBoundary ProposalOnly { get; } = new();
}
