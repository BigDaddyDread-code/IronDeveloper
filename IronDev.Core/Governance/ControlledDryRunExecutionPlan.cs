namespace IronDev.Core.Governance;

public sealed record ControlledDryRunExecutionPlan
{
    public required string ValidationPlanId { get; init; }
    public required string ValidationPlanHash { get; init; }
    public required IReadOnlyList<ControlledDryRunCommand> Commands { get; init; }
    public required IReadOnlyList<string> ExpectedOutputArtifacts { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
}

public sealed record ControlledDryRunCommand
{
    public required string CommandId { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string Executable { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public int TimeoutSeconds { get; init; } = 300;
    public bool AllowNetwork { get; init; }
    public bool AllowSourceWorkspaceWrite { get; init; }
}
