namespace IronDev.Core.Governance.RollbackStatus;

public sealed record RollbackApplyReceiptEvidence
{
    public required string ReceiptRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required bool IsSourceApplyReceipt { get; init; }
    public required bool IsApplyReceiptAcceptedForRollback { get; init; }
}
