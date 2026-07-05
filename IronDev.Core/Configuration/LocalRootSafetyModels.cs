namespace IronDev.Core.Configuration;

public enum LocalRootKind
{
    LogsRoot = 0,
    EvidenceRoot = 1,
    WorkspaceRoot = 2,
    DisposableWorkspaceRoot = 3,
    SandboxRepositoryPath = 4,
    CanaryMeasurementRoot = 5,
    BatchMapEvidenceRoot = 6
}

public sealed record LocalRootSafetyRequest(
    LocalRootKind Kind,
    string? ConfigKey,
    string? ConfiguredPath,
    string RepositoryRoot,
    string EnvironmentName,
    bool MustExist);

public sealed record LocalRootSafetyResult(
    LocalRootKind Kind,
    string ConfigKey,
    bool IsSafe,
    string? NormalizedPath,
    string? ReasonCode,
    string Message,
    string NextSafeAction);

public sealed record LocalRootSafetyValidationResult(
    bool IsSafe,
    IReadOnlyList<LocalRootSafetyResult> Results)
{
    public IReadOnlyList<LocalRootSafetyResult> UnsafeResults =>
        Results.Where(result => !result.IsSafe).ToArray();
}
