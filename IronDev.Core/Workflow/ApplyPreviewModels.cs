namespace IronDev.Core.Workflow;

public enum ApplyPreviewStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    MissingPreviewEvidence = 2,
    PreviewAvailable = 3
}

public enum ApplyPreviewIssueKind
{
    Unknown = 0,
    MissingWorkflowRunId = 1,
    MissingWorkflowStepId = 2,
    UnsafeRequestText = 3,
    MissingDryRunEvidence = 4
}

public sealed record ApplyPreviewRequest
{
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string ControlledApplyPlanReferenceId { get; init; } = string.Empty;
    public int TakeDryRuns { get; init; } = 10;
    public bool IncludeDryRunSummaries { get; init; } = true;
}

public sealed record ApplyPreviewResponse
{
    public required ApplyPreviewStatus Status { get; init; }
    public required string PreviewReferenceId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public string ControlledApplyPlanReferenceId { get; init; } = string.Empty;
    public string SourceApplyApprovalRequirementReferenceId { get; init; } = string.Empty;
    public string PatchProposalEvidencePackageReferenceId { get; init; } = string.Empty;
    public IReadOnlyList<ApplyDryRunSummary> DryRunSummaries { get; init; } = [];
    public IReadOnlyList<ApplyPreviewGate> Gates { get; init; } = [];
    public IReadOnlyList<ApplyPreviewRisk> Risks { get; init; } = [];
    public IReadOnlyList<ApplyPreviewMissingEvidence> MissingEvidence { get; init; } = [];
    public IReadOnlyList<ApplyPreviewIssue> Issues { get; init; } = [];
    public IReadOnlyList<string> SafeSummaryLines { get; init; } = [];
    public bool IsPreviewOnly { get; init; } = true;
    public bool CanExecuteDryRun { get; init; }
    public bool IsDryRunExecution { get; init; }
    public bool CanApplySource { get; init; }
    public bool AppliesPatch { get; init; }
    public bool ReadsSourceFiles { get; init; }
    public bool MutatesFiles { get; init; }
    public bool RunsCommand { get; init; }
    public bool InvokesTool { get; init; }
    public bool RunsValidation { get; init; }
    public bool RunsRollback { get; init; }
    public bool SatisfiesApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransitionsWorkflow { get; init; }
    public bool PromotesMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool DispatchesAgent { get; init; }
    public bool CallsModel { get; init; }
}

public sealed record ApplyPreviewGate
{
    public required string GateId { get; init; }
    public required string GateKind { get; init; }
    public required string SafeSummary { get; init; }
    public bool IsSatisfied { get; init; }
    public bool IsApproval { get; init; }
    public bool IsExecutionPermission { get; init; }
}

public sealed record ApplyPreviewRisk
{
    public required string RiskId { get; init; }
    public required string RiskKind { get; init; }
    public required string Severity { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record ApplyPreviewMissingEvidence
{
    public required string EvidenceId { get; init; }
    public required string EvidenceKind { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record ApplyPreviewIssue
{
    public required ApplyPreviewIssueKind Kind { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}
