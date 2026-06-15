namespace IronDev.Core.Governance;

public sealed record PolicySatisfactionSubject
{
    public required Guid ProjectId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string CapabilityCode { get; init; }
}
