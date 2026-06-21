namespace IronDev.Core.Governance.Commit;

public sealed record CommitMessageEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Subject { get; init; }
    public string? Body { get; init; }

    public required string MessageSource { get; init; }
}
