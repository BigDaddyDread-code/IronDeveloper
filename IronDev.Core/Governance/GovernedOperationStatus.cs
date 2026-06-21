namespace IronDev.Core.Governance;

public sealed record GovernedOperationStatus
{
    public required string OperationId { get; init; }
    public required string OperationKind { get; init; }
    public required string Subject { get; init; }

    public required GovernedOperationState State { get; init; }

    public required IReadOnlyList<string> BlockedReasons { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> NextSafeActions { get; init; }
    public required IReadOnlyList<string> ForbiddenActions { get; init; }

    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required IReadOnlyList<string> ReceiptRefs { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record GovernedOperationStatusBoundary
{
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

    public static GovernedOperationStatusBoundary Status { get; } = new();
}

public sealed record GovernedOperationStatusValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> RedFlags { get; init; }
    public required IReadOnlyList<string> AmberFlags { get; init; }
    public GovernedOperationStatusBoundary Boundary { get; init; } = GovernedOperationStatusBoundary.Status;
}
