namespace IronDev.Core.Governance.Commit;

public sealed record SourceApplyReceiptEvidence
{
    public required string ReceiptRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required IReadOnlyCollection<string> AppliedFilePaths { get; init; }

    public required DateTimeOffset AppliedAtUtc { get; init; }

    public required string AppliedByAuthorityPath { get; init; }
}
