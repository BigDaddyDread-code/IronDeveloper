namespace IronDev.Core.Validation;

public enum ValidationFailureKind
{
    Passed = 0,
    ProcessExitNonZero,
    Timeout,
    Cancelled,
    HarnessException,
    RestoreFailed,
    BuildFailed,
    TestFailed,
    DiffCheckFailed,
    EnvironmentAccessDenied,
    DirtyGeneratedArtifacts,
    InvalidLanePlan,
    CachePolicyViolation,
    UnknownFailure
}

public enum ValidationRunVerdict
{
    Passed = 0,
    Failed,
    Incomplete,
    Blocked
}

public enum ValidationCommandKind
{
    Generic = 0,
    Restore,
    Build,
    Test,
    DiffCheck
}

public enum ValidationLaneRequirement
{
    Required = 0,
    Recommended,
    Deferred
}

public enum ValidationChangedFileKind
{
    Source = 0,
    GeneratedRestoreArtifact,
    TemporaryNuGetConfig
}

public sealed record ValidationLanePlanRequest
{
    public string BaseRef { get; init; } = "unknown";
    public string HeadRef { get; init; } = "unknown";
    public string Phase { get; init; } = "unknown";
    public string CurrentBlock { get; init; } = "unknown";
    public string[] ChangedFiles { get; init; } = [];
}

public sealed record ValidationRuntimeBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanApprove { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanTag { get; init; }
    public bool CanPublish { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanMarkReadyForReview { get; init; }

    public static ValidationRuntimeBoundary Evidence { get; } = new();
}

public sealed record ValidationArtifactPaths
{
    public required string RootDirectory { get; init; }
    public required string ReceiptPath { get; init; }
    public required string SummaryPath { get; init; }
    public required string StdoutPath { get; init; }
    public required string StderrPath { get; init; }
}

public sealed record ValidationCommandSpec
{
    public required string Command { get; init; }
    public string[] Arguments { get; init; } = [];
    public required string WorkingDirectory { get; init; }
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public required TimeSpan Timeout { get; init; }
    public required string StdoutPath { get; init; }
    public required string StderrPath { get; init; }
    public string LaneName { get; init; } = "ad-hoc";
    public ValidationCommandKind CommandKind { get; init; } = ValidationCommandKind.Generic;
}

public sealed record ValidationProcessResult
{
    public required string LaneName { get; init; }
    public required string Command { get; init; }
    public string[] Arguments { get; init; } = [];
    public required string WorkingDirectory { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset FinishedUtc { get; init; }
    public required long DurationMs { get; init; }
    public int? ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool Cancelled { get; init; }
    public bool ProcessTreeKillAttempted { get; init; }
    public bool ProcessTreeKillSucceeded { get; init; }
    public required string StdoutPath { get; init; }
    public required string StderrPath { get; init; }
    public string StdoutTail { get; init; } = string.Empty;
    public string StderrTail { get; init; } = string.Empty;
    public required ValidationFailureKind FailureClassification { get; init; }
}

public sealed record ValidationLane
{
    public required string Name { get; init; }
    public required string Reason { get; init; }
    public ValidationLaneRequirement Requirement { get; init; } = ValidationLaneRequirement.Required;
    public required TimeSpan Timeout { get; init; }
    public ValidationCommandKind CommandKind { get; init; } = ValidationCommandKind.Test;
    public string[] Commands { get; init; } = [];
    public bool SafeToParallelize { get; init; }
    public string ParallelismGroup { get; init; } = "serial";
    public string CacheCategory { get; init; } = "test";
}

public sealed record ValidationLanePlan
{
    public required string ValidationPlanId { get; init; }
    public string BaseRef { get; init; } = "unknown";
    public string HeadRef { get; init; } = "unknown";
    public string Phase { get; init; } = "unknown";
    public string CurrentBlock { get; init; } = "unknown";
    public string[] ChangedFiles { get; init; } = [];
    public ValidationLane[] Lanes { get; init; } = [];
    public string[] EscalationReasons { get; init; } = [];
    public ValidationRuntimeBoundary Boundary { get; init; } = ValidationRuntimeBoundary.Evidence;
}

public sealed record ChangedFileLaneRule
{
    public required string Pattern { get; init; }
    public required string[] LaneNames { get; init; }
    public required string Reason { get; init; }
}

public sealed record ValidationCachePolicy
{
    public bool CacheUsedForRestore { get; init; }
    public bool CacheUsedForBuild { get; init; }
    public bool CachedTestResultAccepted { get; init; }
    public string[] ProhibitedCachedEvidenceCategories { get; init; } =
    [
        "authority",
        "source-apply",
        "rollback",
        "workflow",
        "memory-promotion",
        "cli-mutation",
        "db",
        "dogfood",
        "release",
        "merge"
    ];
}

public sealed record SlowFlakyTestInventoryItem
{
    public required string TestNameOrFilter { get; init; }
    public required string Category { get; init; }
    public required string Reason { get; init; }
    public required TimeSpan ExpectedDuration { get; init; }
    public required string OwnerArea { get; init; }
    public bool SafeToParallelize { get; init; }
    public bool RequiresNetwork { get; init; }
    public bool RequiresDatabase { get; init; }
    public bool MutatesWorkspace { get; init; }
    public bool MutatesRepository { get; init; }
    public bool UsesCli { get; init; }
    public bool UsesDogfoodPath { get; init; }
    public string QuarantineStatus { get; init; } = "None";
}

public sealed record SlowFlakyTestInventory
{
    public SlowFlakyTestInventoryItem[] Items { get; init; } = [];
    public ValidationRuntimeBoundary Boundary { get; init; } = ValidationRuntimeBoundary.Evidence;
}

public sealed record ValidationRunReceipt
{
    public required string ValidationRunId { get; init; }
    public required string ValidationPlanId { get; init; }
    public required string Branch { get; init; }
    public required string CommitSha { get; init; }
    public required string ChangedFilesHash { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset FinishedUtc { get; init; }
    public required ValidationRunVerdict Verdict { get; init; }
    public ValidationLane[] RequiredLanes { get; init; } = [];
    public ValidationLane[] RecommendedLanes { get; init; } = [];
    public ValidationLane[] DeferredLanes { get; init; } = [];
    public ValidationProcessResult[] Results { get; init; } = [];
    public ValidationFailureKind[] FailureClassifications { get; init; } = [];
    public string[] SkippedLanes { get; init; } = [];
    public string[] SkippedLaneReasons { get; init; } = [];
    public ValidationChangedFileClassification[] DirtyChangedFiles { get; init; } = [];
    public bool WorktreeCleanBefore { get; init; }
    public bool WorktreeCleanAfter { get; init; }
    public ValidationCachePolicy CachePolicy { get; init; } = new();
    public ValidationRuntimeBoundary Boundary { get; init; } = ValidationRuntimeBoundary.Evidence;
}

public sealed record ValidationChangedFileClassification
{
    public required string Path { get; init; }
    public required ValidationChangedFileKind Kind { get; init; }
    public required string Reason { get; init; }
}

public sealed record ValidationReceiptWriteResult
{
    public required ValidationRunReceipt Receipt { get; init; }
    public required string ReceiptPath { get; init; }
    public required string SummaryPath { get; init; }
}
