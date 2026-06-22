namespace IronDev.Core.Governance.PushExecution;

public sealed record PushPostStateObservation
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }

    public required string RemoteName { get; init; }
    public required string RemoteUrl { get; init; }
    public required string RemoteBranch { get; init; }

    public required string RemoteHeadCommitId { get; init; }

    public required IReadOnlyCollection<string> RemainingUnpushedCommitIds { get; init; }

    public required bool IsObservedAfterPush { get; init; }
}
