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

public sealed record RunStatusDto
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

public sealed record RunReportDto
{
    public RunStatusDto Status { get; init; } = new();
    public RunReportDetail? Report { get; init; }
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
    public RunPromotionReview? PromotionReview { get; init; }
    public RunAdversarialReview? AdversarialReview { get; init; }
    public RunMemoryImprovementReview? MemoryImprovement { get; init; }
    public RunReviewPolicySnapshot Policy { get; init; } = RunReviewPolicySnapshot.Default;
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

public sealed record RunPromotionReview
{
    public string PackageId { get; init; } = string.Empty;
    public string ProposedChangeId { get; init; } = string.Empty;
    public string ApprovalState { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string RuntimeProfileId { get; init; } = string.Empty;
    public string TargetLanguage { get; init; } = string.Empty;
    public string TargetStack { get; init; } = string.Empty;
    public int PromotableFileCount { get; init; }
    public int BlockedFileCount { get; init; }
    public IReadOnlyList<RunPromotionFile> PromotableFiles { get; init; } = [];
    public IReadOnlyList<RunPromotionFile> BlockedFiles { get; init; } = [];
    public IReadOnlyList<RunPromotionRisk> Risks { get; init; } = [];
    public IReadOnlyList<string> RequiredChecks { get; init; } = [];
    public IReadOnlyList<string> ExplicitApprovalsNeeded { get; init; } = [];
    public IReadOnlyList<string> BlockedActions { get; init; } = [];
}

public sealed record RunPromotionFile
{
    public string RelativePath { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool HashMatchesPackage { get; init; }
}

public sealed record RunPromotionRisk
{
    public string Severity { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Mitigation { get; init; } = string.Empty;
}

public sealed record RunReviewPolicySnapshot
{
    public static RunReviewPolicySnapshot Default { get; } = new()
    {
        PolicyId = "irondev-default-promotion-review",
        ConfigurableSettings = [
            "runtime profile selection",
            "build command per runtime profile",
            "test command per runtime profile",
            "promotable source extensions",
            "blocked generated path segments",
            "human review checklist",
            "risk visibility thresholds"
        ],
        HardInvariants = [
            "real repo writes require explicit reviewed approval",
            "agents cannot approve their own changes",
            "ConscienceAgent and ThoughtLedger cannot be bypassed for governed apply",
            "mutation requires trace and evidence",
            "project scope must be explicit",
            "blocked files must not be promoted silently"
        ]
    };

    public string PolicyId { get; init; } = string.Empty;
    public IReadOnlyList<string> ConfigurableSettings { get; init; } = [];
    public IReadOnlyList<string> HardInvariants { get; init; } = [];
}

public sealed record RunAdversarialReview
{
    public int FindingCount { get; init; }
    public int HighCriticalCount { get; init; }
    public bool RebuttalRequired { get; init; }
    public bool KilljoyEscalation { get; init; }
    public bool KilljoyAddressedHighCritical { get; init; }
    public IReadOnlyList<RunDoubtFinding> Findings { get; init; } = [];
}

public sealed record RunDoubtFinding
{
    public string Severity { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string EvidenceCitation { get; init; } = string.Empty;
    public string SuggestedFix { get; init; } = string.Empty;
}

public sealed record RunMemoryImprovementReview
{
    public int ProposalCount { get; init; }
    public string MemoryHealthScore { get; init; } = string.Empty;
    public bool ReadyForAcceptedMemoryKey { get; init; }
    public string CurrentAuthorityLevel { get; init; } = string.Empty;
    public int EvidenceBundleCount { get; init; }
    public string KeyGateDecision { get; init; } = string.Empty;
    public string KeyGateRequestedLevel { get; init; } = string.Empty;
    public IReadOnlyList<RunMemoryProposal> Proposals { get; init; } = [];
}

public sealed record RunMemoryProposal
{
    public string ActionType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string RecommendedDisposition { get; init; } = string.Empty;
    public string MemoryAuthorityImpact { get; init; } = string.Empty;
}
