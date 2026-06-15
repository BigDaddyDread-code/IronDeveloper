namespace IronDev.Core.Governance;

public sealed record ApprovalRequirement
{
    public required Guid ProjectId { get; init; }
    public required string ApprovalTargetKind { get; init; }
    public required string ApprovalTargetId { get; init; }
    public required string ApprovalTargetHash { get; init; }
    public required string CapabilityCode { get; init; }
    public required string ApprovalPurpose { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public IReadOnlyList<string> RequiredEvidenceReferences { get; init; } = [];
    public IReadOnlyList<string> RequiredBoundaryMaxims { get; init; } = [];
}
