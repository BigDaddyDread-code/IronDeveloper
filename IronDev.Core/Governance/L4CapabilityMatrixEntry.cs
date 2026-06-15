namespace IronDev.Core.Governance;

public sealed record L4CapabilityMatrixEntry(
    string CapabilityCode,
    string CapabilityName,
    string Stage,
    int Order,
    bool Implemented,
    bool AuthorityRequired,
    bool EvidenceRequired,
    IReadOnlyList<string> RequiredAuthorityRecords,
    IReadOnlyList<string> RequiredEvidenceRecords,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> ForbiddenEffects,
    IReadOnlyList<string> BoundaryMaxims);
