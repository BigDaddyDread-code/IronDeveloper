namespace IronDev.Core.Governance.RollbackStatus;

public sealed record RollbackAvailabilityEvidence
{
    public required string EvidenceRef { get; init; }

    public required bool IsRollbackAvailable { get; init; }
    public required string AvailabilityReason { get; init; }
}
