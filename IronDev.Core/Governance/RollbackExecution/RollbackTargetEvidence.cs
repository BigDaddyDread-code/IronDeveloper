namespace IronDev.Core.Governance.RollbackExecution;

public sealed record RollbackTargetEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }

    public required string RollbackTargetId { get; init; }

    public required bool IsBoundToSourceApplyReceipt { get; init; }
    public required bool IsCompleteRollback { get; init; }
    public required bool RequiresPartialRollback { get; init; }
    public required bool HasPartialRollbackRisk { get; init; }

    public required IReadOnlyCollection<RollbackFileExpectation> ExpectedFiles { get; init; }
}
