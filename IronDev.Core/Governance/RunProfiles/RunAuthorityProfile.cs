namespace IronDev.Core.Governance;

public sealed record RunAuthorityProfile
{
    public required string ProfileId { get; init; }
    public required RunAuthorityProfileKind Kind { get; init; }
    public required IReadOnlyCollection<RunAuthorityOperationKind> AllowedOperations { get; init; }
    public required IReadOnlyCollection<RunAuthorityOperationKind> ForbiddenOperations { get; init; }

    public required bool CanReadRepo { get; init; }
    public required bool CanMutateDisposableWorkspace { get; init; }
    public required bool CanWriteProposalEvidence { get; init; }
    public required bool CanInspectGovernedStatus { get; init; }

    public required bool CanMutateDurableSource { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool CanExecuteRollback { get; init; }
    public required bool CanCommit { get; init; }
    public required bool CanPush { get; init; }
    public required bool CanCreatePullRequest { get; init; }
    public required bool CanMarkReadyForReview { get; init; }
    public required bool CanMerge { get; init; }
    public required bool CanRelease { get; init; }
    public required bool CanDeploy { get; init; }
    public required bool CanCreateApprovalRequest { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanContinueWorkflow { get; init; }
    public required bool CanExecuteProviderMutation { get; init; }
    public required bool CanPublishPackage { get; init; }
}

public sealed record RunAuthorityProfileValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyCollection<string> Issues { get; init; }
}
