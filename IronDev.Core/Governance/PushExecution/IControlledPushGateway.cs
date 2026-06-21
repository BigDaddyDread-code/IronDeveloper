namespace IronDev.Core.Governance.PushExecution;

public interface IControlledPushGateway
{
    Task<ControlledPushReceipt?> PushAsync(
        ControlledPushGatewayRequest request,
        CancellationToken cancellationToken);
}

public sealed record ControlledPushGatewayRequest
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }

    public required string RemoteName { get; init; }
    public required string RemoteUrl { get; init; }
    public required string RemoteBranch { get; init; }

    public required string ExpectedLocalCommitId { get; init; }
    public required string ExpectedRemoteHeadCommitId { get; init; }

    public required bool ForcePushDisabled { get; init; }
    public required bool TagsDisabled { get; init; }

    public required string RunId { get; init; }
    public required string PatchHash { get; init; }
}
