namespace IronDev.Core.Governance.RollbackStatus;

public sealed record RollbackPlanEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }

    public required bool HasRollbackPlan { get; init; }
    public required bool IsPlanBoundToApplyReceipt { get; init; }
    public required bool RequiresPartialRollback { get; init; }
    public required bool HasPartialRollbackRisk { get; init; }

    public required IReadOnlyCollection<string> PlannedRollbackFilePaths { get; init; }
}
