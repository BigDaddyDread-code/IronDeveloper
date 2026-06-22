namespace IronDev.Core.Governance;

public interface IReceiptMetadataReadRepository
{
    ReceiptMetadataReadResult GetByReceiptRef(
        string receiptRef,
        FrontendReadinessReadScope scope);
}

public sealed record ReceiptMetadataReadRecord
{
    public required string ReceiptRef { get; init; }
    public required string ReceiptKind { get; init; }
    public required string Summary { get; init; }
    public string OperationId { get; init; } = string.Empty;
    public string OperationKind { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public bool IsTenantScoped { get; init; } = true;
    public int? TenantId { get; init; }
    public bool ContainsRawPayload { get; init; }
    public bool ContainsPrivateMaterial { get; init; }
    public bool ContainsPatchPayload { get; init; }
    public bool ContainsHiddenMaterial { get; init; }
    public bool ClaimsAuthority { get; init; }
    public bool ClaimsContinuation { get; init; }
    public bool ClaimsApproval { get; init; }
    public bool ClaimsPolicySatisfaction { get; init; }
    public required IReadOnlyCollection<string> Warnings { get; init; }
    public required IReadOnlyCollection<string> AuthorityWarnings { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record ReceiptMetadataReadResult
{
    public required bool Found { get; init; }
    public FrontendReceiptMetadataReadModel? Metadata { get; init; }
    public required IReadOnlyCollection<string> Issues { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }

    public static ReceiptMetadataReadResult NotFound(params string[] issues) =>
        new()
        {
            Found = false,
            Metadata = null,
            Issues = issues,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
}
