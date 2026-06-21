namespace IronDev.Core.Governance;

public sealed record GovernedOperationStatusInspectRequest
{
    public required GovernedOperationStatus Status { get; init; }
    public bool IncludeRefs { get; init; } = true;
    public bool IncludeValidation { get; init; } = true;
}

public sealed record GovernedOperationStatusInspectBoundary
{
    public bool ReadOnly { get; init; } = true;
    public bool DisplayOnly { get; init; } = true;
    public bool StatusOnly { get; init; } = true;
    public bool ReferenceOnly { get; init; } = true;
    public bool CanApprove { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanExecute { get; init; }
    public bool CanRetry { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanRollback { get; init; }
    public bool CanMerge { get; init; }
    public bool CanSourceApply { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanPublishPackages { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanDispatchPipeline { get; init; }
    public bool CanMutate { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateEnvironment { get; init; }
    public bool CanCreateAuthorityRecords { get; init; }

    public static GovernedOperationStatusInspectBoundary ReadModel { get; } = new();
}

public sealed record GovernedOperationStatusInspectResult
{
    public required GovernedOperationStatus Status { get; init; }
    public required GovernedOperationStatusValidationResult Validation { get; init; }
    public GovernedOperationStatusInspectBoundary Boundary { get; init; } = GovernedOperationStatusInspectBoundary.ReadModel;

    public required string Summary { get; init; }

    public required IReadOnlyList<string> StateLines { get; init; }
    public required IReadOnlyList<string> BlockedReasonLines { get; init; }
    public required IReadOnlyList<string> MissingEvidenceLines { get; init; }
    public required IReadOnlyList<string> NextSafeActionLines { get; init; }
    public required IReadOnlyList<string> ForbiddenActionLines { get; init; }
    public required IReadOnlyList<string> EvidenceRefLines { get; init; }
    public required IReadOnlyList<string> ReceiptRefLines { get; init; }
    public required IReadOnlyList<string> ValidationIssueLines { get; init; }
    public required IReadOnlyList<string> RedFlagLines { get; init; }
    public required IReadOnlyList<string> AmberFlagLines { get; init; }
    public required IReadOnlyList<string> BoundaryLines { get; init; }
    public required IReadOnlyList<string> ResultLines { get; init; }

    public required bool IsValid { get; init; }
    public required bool HasAuthorityRedFlags { get; init; }
}
