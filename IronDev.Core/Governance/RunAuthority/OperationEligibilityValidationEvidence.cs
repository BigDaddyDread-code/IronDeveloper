namespace IronDev.Core.Governance;

public sealed record OperationEligibilityValidationEvidence
{
    public required string ValidationKind { get; init; }
    public required OperationEligibilityValidationOutcome Outcome { get; init; }
    public required string EvidenceRef { get; init; }
    public string? PatchHash { get; init; }
}
