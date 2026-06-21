namespace IronDev.Core.Governance.CommitExecution;

public interface IControlledCommitGateway
{
    Task<ControlledCommitReceipt?> CommitAsync(
        ControlledCommitGatewayRequest request,
        CancellationToken cancellationToken);
}

public sealed record ControlledCommitGatewayRequest
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string WorktreeRoot { get; init; }
    public required string ExpectedHeadCommitId { get; init; }
    public required IReadOnlyCollection<string> FilePathsToStage { get; init; }
    public required string CommitSubject { get; init; }
    public required string CommitBody { get; init; }
    public required bool DisableHooks { get; init; }
    public required string PackageId { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }
}
