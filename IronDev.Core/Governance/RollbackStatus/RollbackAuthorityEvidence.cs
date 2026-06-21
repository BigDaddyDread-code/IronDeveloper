namespace IronDev.Core.Governance.RollbackStatus;

public sealed record RollbackAuthorityEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }

    public required OperationEligibilityDecision? Decision { get; init; }
}
