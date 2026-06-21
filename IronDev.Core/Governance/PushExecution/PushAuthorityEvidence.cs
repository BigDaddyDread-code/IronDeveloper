namespace IronDev.Core.Governance.PushExecution;

public sealed record PushAuthorityEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string RemoteName { get; init; }
    public required string RemoteUrl { get; init; }
    public required string RemoteBranch { get; init; }

    public required string CommitId { get; init; }
    public required string ExpectedRemoteHeadCommitId { get; init; }

    public required OperationEligibilityDecision? Decision { get; init; }
}
