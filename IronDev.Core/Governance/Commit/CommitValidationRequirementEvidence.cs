namespace IronDev.Core.Governance.Commit;

public sealed record CommitValidationRequirementEvidence
{
    public required bool IsSatisfied { get; init; }
    public required bool IsExplicitlyBlocked { get; init; }

    public required IReadOnlyCollection<string> ValidationEvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> BlockedReasons { get; init; }
}
