namespace IronDev.Core.Agents;

public sealed record WorkspaceApplyReportRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
}

public sealed record WorkspaceApplyChangedFileSummary
{
    public required string Operation { get; init; }
    public required string RelativePath { get; init; }
    public bool Applied { get; init; }
    public bool Verified { get; init; }
}

public sealed record WorkspaceApplyReportSummary
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public string? SourceRepo { get; init; }
    public required string Outcome { get; init; }
    public string? Recommendation { get; init; }
    public string? FailedStage { get; init; }
    public string? FailureSeverity { get; init; }
    public string? RecommendedNextAction { get; init; }
    public bool SourceRepoMutated { get; init; }
    public bool ApplyVerified { get; init; }
    public bool SourceMatchesWorkspace { get; init; }
    public bool PostApplyValidationSucceeded { get; init; }
    public int AddCount { get; init; }
    public int ModifyCount { get; init; }
    public int DeleteCount { get; init; }
    public IReadOnlyList<WorkspaceApplyChangedFileSummary> Files { get; init; } = [];
    public string? SourceReportPath { get; init; }
    public string? FailurePackagePath { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> RiskNotes { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
