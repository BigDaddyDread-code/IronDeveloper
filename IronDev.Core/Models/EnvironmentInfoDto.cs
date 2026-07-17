namespace IronDev.Core.Models;

public sealed record EnvironmentInfoDto
{
    public string Environment { get; init; } = string.Empty;
    public string Database { get; init; } = string.Empty;
    public string WeaviatePrefix { get; init; } = string.Empty;
    public bool IsTestEnvironment { get; init; }
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string LogsRoot { get; init; } = string.Empty;
    public bool DangerRealRepoWritesEnabled { get; init; }
    public WorkbenchReleaseInfoDto Workbench { get; init; } = new();
}
