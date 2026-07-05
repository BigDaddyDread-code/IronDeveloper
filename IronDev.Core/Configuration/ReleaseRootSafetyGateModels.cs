namespace IronDev.Core.Configuration;

public enum ReleaseRootSafetyStatus
{
    Passed = 0,
    Blocked = 1,
    NotConfigured = 2,
    NotEvaluated = 3
}

public sealed record ReleaseRootSafetyRoot(
    LocalRootKind Kind,
    string ConfigKey,
    string? ConfiguredPath,
    bool Required = true,
    bool MustExist = false);

public sealed record ReleaseRootSafetyRequest(
    string RepositoryRoot,
    string EnvironmentName,
    IReadOnlyList<ReleaseRootSafetyRoot> Roots,
    bool Evaluate = true);

public sealed record ReleaseRootSafetyRootResult(
    LocalRootKind Kind,
    string ConfigKey,
    ReleaseRootSafetyStatus Status,
    string RedactedDisplayPath,
    string ReasonCode,
    string Message,
    string NextSafeAction);

public sealed record ReleaseRootSafetyReport(
    ReleaseRootSafetyStatus Status,
    IReadOnlyList<ReleaseRootSafetyRootResult> Results,
    string BoundaryStatement)
{
    public IReadOnlyList<ReleaseRootSafetyRootResult> BlockingResults =>
        Results.Where(result => result.Status == ReleaseRootSafetyStatus.Blocked).ToArray();
}
