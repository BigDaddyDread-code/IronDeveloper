namespace IronDev.Core.RunReports;

public sealed record RunReportSummary
{
    public string RunId { get; init; } = string.Empty;
    public string? TraceId { get; init; }
    public string Project { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public int RealRepoMutationCount { get; init; }
    public int DisposableFilesChanged { get; init; }
}

public sealed record RunReportDetail
{
    public string RunId { get; init; } = string.Empty;
    public string? TraceId { get; init; }
    public string Project { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public int RealRepoMutationCount { get; init; }
    public int DisposableFilesChanged { get; init; }
    public IReadOnlyList<RunStageStatus> Stages { get; init; } = [];
    public IReadOnlyList<RunAttemptSummary> Attempts { get; init; } = [];
    public IReadOnlyList<RunRepairSummary> Repairs { get; init; } = [];
    public IReadOnlyList<RunEvidenceItem> Evidence { get; init; } = [];
    public string Boundary { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? ReportPath { get; init; }
}

public sealed record RunStageStatus
{
    public string StageName { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}

public sealed record RunAttemptSummary
{
    public int AttemptNumber { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string FailureClassification { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}

public sealed record RunRepairSummary
{
    public int RepairAttemptNumber { get; init; }
    public string TriggerFailureClassification { get; init; } = string.Empty;
    public string PlannedFix { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int RetryBudgetRemaining { get; init; }
}

public sealed record RunEvidenceItem
{
    public string Type { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}
