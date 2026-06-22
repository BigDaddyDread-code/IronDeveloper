namespace IronDev.Core.Governance.RollbackStatus;

public sealed record RollbackStatusMappingResult
{
    public required GovernedOperationStatus Status { get; init; }
    public required GovernedOperationStatusValidationResult StatusValidation { get; init; }

    public required bool IsRollbackExecutionAllowed { get; init; }
    public required bool IsRollbackExecuted { get; init; }

    public required IReadOnlyCollection<string> Issues { get; init; }
}
