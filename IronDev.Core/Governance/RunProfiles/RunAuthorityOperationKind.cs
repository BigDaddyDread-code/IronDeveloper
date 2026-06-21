namespace IronDev.Core.Governance;

public enum RunAuthorityOperationKind
{
    Unknown = 0,
    RepoInspect = 1,
    TaskInterpretation = 2,
    DisposableWorkspaceCreate = 3,
    DisposableWorkspaceModify = 4,
    DisposableWorkspaceValidate = 5,
    PatchProposal = 6,
    PatchPackageWrite = 7,
    ValidationResultPackageWrite = 8,
    GovernedStatusInspect = 9,

    SourceApply = 100,
    Rollback = 101,
    Commit = 102,
    Push = 103,
    DraftPullRequest = 104,
    ReadyForReview = 105,
    Merge = 106,
    Release = 107,
    Deployment = 108,
    MemoryPromotion = 109,
    WorkflowContinuation = 110,
    ApprovalRequestCreate = 111,
    PolicySatisfaction = 112,
    ProviderMutation = 113,
    PackagePublication = 114,
    DurableSourceMutation = 115,
    DurableEventWrite = 116
}
