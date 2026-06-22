namespace IronDev.Core.Governance.RollbackExecution;

public sealed record RollbackFileExpectation
{
    public required string Path { get; init; }

    public required string ExpectedPreRollbackHash { get; init; }
    public required string ExpectedPostRollbackHash { get; init; }

    public required bool ShouldExistBeforeRollback { get; init; }
    public required bool ShouldExistAfterRollback { get; init; }
}
