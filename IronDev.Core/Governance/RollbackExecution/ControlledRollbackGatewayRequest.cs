namespace IronDev.Core.Governance.RollbackExecution;

public sealed record ControlledRollbackGatewayRequest
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }
    public required string RollbackTargetId { get; init; }

    public required IReadOnlyCollection<RollbackFileExpectation> ExpectedFiles { get; init; }

    public required bool CompleteRollbackOnly { get; init; }
    public required bool PartialRollbackDisabled { get; init; }
    public required bool CommitDisabled { get; init; }
    public required bool PushDisabled { get; init; }
    public required bool PullRequestDisabled { get; init; }
    public required bool MergeDisabled { get; init; }
    public required bool ReleaseDisabled { get; init; }
    public required bool DeploymentDisabled { get; init; }
    public required bool MemoryWriteDisabled { get; init; }
    public required bool WorkflowContinuationDisabled { get; init; }
}
