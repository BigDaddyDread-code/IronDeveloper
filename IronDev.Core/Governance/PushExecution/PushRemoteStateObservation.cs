namespace IronDev.Core.Governance.PushExecution;

public sealed record PushRemoteStateObservation
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }

    public required string RemoteName { get; init; }
    public required string RemoteUrl { get; init; }
    public required string RemoteBranch { get; init; }

    public required string LocalHeadCommitId { get; init; }
    public required string RemoteHeadCommitId { get; init; }

    public required IReadOnlyCollection<string> LocalUnpushedCommitIds { get; init; }
    public required IReadOnlyCollection<string> LocalUncommittedFilePaths { get; init; }

    public required bool IsRemoteReachable { get; init; }
}
