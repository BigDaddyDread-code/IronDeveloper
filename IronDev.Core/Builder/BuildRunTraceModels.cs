namespace IronDev.Core.Builder;

public sealed class BuildRunTrace
{
    public string TraceId { get; init; } = Guid.NewGuid().ToString("N");
    public string RunId { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public List<string> SourceSpecIds { get; init; } = [];
    public List<string> SourceTicketIds { get; init; } = [];
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; set; }
    public string Status { get; set; } = "NotStarted";
    public string GovernedTier { get; init; } = "Tier4DisposableWorkspaceApply";
    public bool RealRepoMutationAllowed { get; init; }
    public bool DisposableWorkspaceMutationAllowed { get; init; }
    public int RealRepoMutationCount { get; set; }
    public int DisposableFilesChanged { get; set; }
    public string Recommendation { get; set; } = "NeedsHumanReview";
    public string Boundary { get; init; } = "Trace only. Does not grant write authority.";
    public List<AgentStageTrace> Stages { get; init; } = [];
    public ContextTrace? Context { get; set; }
    public ConscienceDecisionTrace? Conscience { get; set; }
    public ThoughtLedgerTrace? ThoughtLedger { get; set; }
    public BuilderPlanTrace? BuilderPlan { get; set; }
    public WorkspaceMutationTrace? WorkspaceMutation { get; set; }
    public List<BuildAttemptTrace> BuildAttempts { get; init; } = [];
    public List<TestAttemptTrace> TestAttempts { get; init; } = [];
    public List<RepairAttemptTrace> RepairAttempts { get; init; } = [];
    public List<EvidenceArtifact> EvidenceArtifacts { get; init; } = [];
}

public sealed class AgentStageTrace
{
    public string StageId { get; init; } = Guid.NewGuid().ToString("N");
    public string TraceId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> InputRefs { get; init; } = [];
    public List<string> OutputRefs { get; init; } = [];
    public List<string> EvidenceRefs { get; init; } = [];
    public string Decision { get; set; } = "None";
    public List<string> BoundaryNotes { get; init; } = [];
}

public sealed class ContextTrace
{
    public string TraceId { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public string SemanticTraceId { get; init; } = string.Empty;
    public string PrimarySourceId { get; init; } = string.Empty;
    public List<string> IncludedSources { get; init; } = [];
    public List<string> RejectedSources { get; init; } = [];
    public List<string> RiskNotes { get; init; } = [];
    public string AgentFacingSummary { get; init; } = string.Empty;
}

public sealed class ConscienceDecisionTrace
{
    public string TraceId { get; init; } = string.Empty;
    public string Decision { get; init; } = "NeedsMoreEvidence";
    public decimal Confidence { get; init; }
    public List<string> Reasons { get; init; } = [];
    public List<string> AllowingFactors { get; init; } = [];
    public List<string> BlockingFactors { get; init; } = [];
    public List<string> MissingEvidence { get; init; } = [];
    public List<string> ViolatedBoundaries { get; init; } = [];
    public List<string> RequiredNextSteps { get; init; } = [];
    public string ObservedProject { get; init; } = string.Empty;
    public string AffectedProject { get; init; } = string.Empty;
    public List<string> AuthoritySources { get; init; } = [];
}

public sealed class ThoughtLedgerTrace
{
    public string TraceId { get; init; } = string.Empty;
    public string CurrentBelief { get; init; } = string.Empty;
    public List<string> EvidenceSummary { get; init; } = [];
    public List<string> Uncertainties { get; init; } = [];
    public List<string> Assumptions { get; init; } = [];
    public List<string> TemptingActions { get; init; } = [];
    public List<string> BlockedActions { get; init; } = [];
    public List<string> SaferAlternatives { get; init; } = [];
    public string RecommendedNextMove { get; init; } = string.Empty;
    public string Boundary { get; init; } = "Visible reasoning summary only. No hidden chain-of-thought.";
}

public sealed class BuilderPlanTrace
{
    public string TraceId { get; init; } = string.Empty;
    public string BuildBriefId { get; init; } = string.Empty;
    public string ProposalId { get; init; } = string.Empty;
    public string SourceSpecId { get; init; } = string.Empty;
    public string Target { get; init; } = "DisposableWorkspaceOnly";
    public List<string> PlannedProjects { get; init; } = [];
    public List<string> PlannedFiles { get; init; } = [];
    public List<string> ForbiddenPaths { get; init; } = [];
    public List<string> Assumptions { get; init; } = [];
    public List<string> Risks { get; init; } = [];
    public List<string> TestPlan { get; init; } = [];
}

public sealed class WorkspaceMutationTrace
{
    public string TraceId { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public bool IsDisposableWorkspace { get; init; }
    public bool IsOutsideRealRepo { get; init; }
    public string RealRepoBeforeHash { get; init; } = string.Empty;
    public string RealRepoAfterHash { get; init; } = string.Empty;
    public int RealRepoMutationCount { get; init; }
    public List<ChangedFileTrace> ChangedFiles { get; init; } = [];
}

public sealed class ChangedFileTrace
{
    public string Path { get; init; } = string.Empty;
    public string ChangeType { get; init; } = "Create";
    public string ShaBefore { get; init; } = string.Empty;
    public string ShaAfter { get; init; } = string.Empty;
}

public sealed class BuildAttemptTrace
{
    public int AttemptNumber { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string Status { get; init; } = "Failed";
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; init; }
    public string StdoutRef { get; init; } = string.Empty;
    public string StderrRef { get; init; } = string.Empty;
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public string FailureClassification { get; init; } = "Unknown";
}

public sealed class TestAttemptTrace
{
    public int AttemptNumber { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string Status { get; init; } = "Failed";
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public string FailureClassification { get; init; } = "Unknown";
    public List<string> FailedTests { get; init; } = [];
}

public sealed class RepairAttemptTrace
{
    public int RepairAttemptNumber { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public int TriggerAttemptNumber { get; init; }
    public string TriggerFailureClassification { get; init; } = string.Empty;
    public string PlannedFix { get; init; } = string.Empty;
    public List<string> FilesAllowed { get; init; } = [];
    public List<string> FilesChanged { get; init; } = [];
    public string Status { get; init; } = "Applied";
    public string Reason { get; init; } = string.Empty;
    public int RetryBudgetRemaining { get; init; }
}

public sealed class EvidenceArtifact
{
    public string EvidenceId { get; init; } = Guid.NewGuid().ToString("N");
    public string TraceId { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class FinalBuildRunReport
{
    public string TraceId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = "NeedsMoreEvidence";
    public string Summary { get; init; } = string.Empty;
    public List<string> Timeline { get; init; } = [];
    public List<AgentStageTrace> StageStatuses { get; init; } = [];
    public List<BuildAttemptTrace> BuildAttempts { get; init; } = [];
    public List<TestAttemptTrace> TestAttempts { get; init; } = [];
    public List<RepairAttemptTrace> RepairAttempts { get; init; } = [];
    public int RealRepoMutationCount { get; init; }
    public int DisposableFilesChanged { get; init; }
    public string Recommendation { get; init; } = "NeedsHumanReview";
    public List<string> NextSafeActions { get; init; } = [];
    public List<EvidenceArtifact> EvidenceRefs { get; init; } = [];
    public string Boundary { get; init; } = "Report only. Does not approve real repo promotion.";
}
