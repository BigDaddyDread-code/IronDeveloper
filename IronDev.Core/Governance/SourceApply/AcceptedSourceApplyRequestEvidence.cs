namespace IronDev.Core.Governance;

public sealed record AcceptedSourceApplyRequestEvidence
{
    public required string RequestId { get; init; }
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required IReadOnlyCollection<string> AllowedFileGlobs { get; init; }
    public required IReadOnlyCollection<string> ForbiddenFileGlobs { get; init; }

    public required DateTimeOffset AcceptedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public required string AcceptedByPrincipalId { get; init; }
    public required string AcceptedByPrincipalKind { get; init; }
}
