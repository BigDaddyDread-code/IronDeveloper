namespace IronDev.Core.Governance;

public interface IEvidenceMetadataReadRepository
{
    EvidenceMetadataReadResult GetByEvidenceRef(
        string evidenceRef,
        FrontendReadinessReadScope scope);
}

public sealed record EvidenceMetadataReadRecord
{
    public required string EvidenceRef { get; init; }
    public required string EvidenceKind { get; init; }
    public required string Summary { get; init; }
    public bool IsTenantScoped { get; init; } = true;
    public int? TenantId { get; init; }
    public bool ContainsRawPayload { get; init; }
    public bool ContainsPrivateMaterial { get; init; }
    public bool ContainsPatchPayload { get; init; }
    public bool ContainsHiddenMaterial { get; init; }
    public required IReadOnlyCollection<string> Warnings { get; init; }
    public required IReadOnlyCollection<string> AuthorityWarnings { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record EvidenceMetadataReadResult
{
    public required bool Found { get; init; }
    public FrontendEvidenceMetadataReadModel? Metadata { get; init; }
    public required IReadOnlyCollection<string> Issues { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }

    public static EvidenceMetadataReadResult NotFound(params string[] issues) =>
        new()
        {
            Found = false,
            Metadata = null,
            Issues = issues,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
}
