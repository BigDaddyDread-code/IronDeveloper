using IronDev.Core.Governance;

namespace IronDev.Core.SourceApply;

public sealed record SourceSnapshot
{
    public required string SourceSnapshotId { get; init; }
    public required string RunId { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string HeadCommit { get; init; }
    public required string Branch { get; init; }
    public required string StatusPorcelain { get; init; }
    public required string DiffSha256 { get; init; }
    public string[] ChangedFiles { get; init; } = [];
    public required DateTimeOffset CapturedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceApplyExecutionRequest
{
    public required string SourceApplyExecutionRequestId { get; init; }
    public required string RunId { get; init; }
    public required string SourceApplyRequestId { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string BaseCommit { get; init; }
    public required string PatchPath { get; init; }
    public required string PatchSha256 { get; init; }
    public string[] ChangedFiles { get; init; } = [];
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required string? ConscienceDecisionId { get; init; }
    public required string ThoughtLedgerRef { get; init; }
    public SourceApplyEvidenceRef[] EvidenceRefs { get; init; } = [];
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public enum SourceApplyExecutionGateDecisionOutcome
{
    AllowApplyToWorkingTree = 0,
    Block
}

public sealed record SourceApplyExecutionGateDecision
{
    public required string SourceApplyExecutionGateDecisionId { get; init; }
    public required string RunId { get; init; }
    public required string SourceApplyExecutionRequestId { get; init; }
    public required string SourceApplyRequestId { get; init; }
    public required SourceApplyExecutionGateDecisionOutcome Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceApplyCommandResult
{
    public required string SourceApplyCommandResultId { get; init; }
    public required string RunId { get; init; }
    public required string SourceApplyExecutionRequestId { get; init; }
    public required string Command { get; init; }
    public required int ExitCode { get; init; }
    public required string StdoutPath { get; init; }
    public required string StderrPath { get; init; }
    public required string CombinedOutputPath { get; init; }
    public required bool SourceAppliedToWorkingTree { get; init; }
    public required bool GitCommitCreated { get; init; }
    public required bool GitPushPerformed { get; init; }
    public required bool PullRequestCreated { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public enum SourceApplyReceiptDecision
{
    AppliedToWorkingTree = 0,
    Blocked,
    Failed
}

public sealed record SourceApplyReceipt
{
    public required string SourceApplyReceiptId { get; init; }
    public required string RunId { get; init; }
    public required string SourceApplyExecutionRequestId { get; init; }
    public required string SourceApplyRequestId { get; init; }
    public required string SourceApplyExecutionGateDecisionId { get; init; }
    public string? SourceApplyCommandResultId { get; init; }
    public string? PreSourceSnapshotId { get; init; }
    public string? PostSourceSnapshotId { get; init; }
    public required SourceApplyReceiptDecision Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public required string BaseCommit { get; init; }
    public required string PatchSha256 { get; init; }
    public string? PostApplyDiffSha256 { get; init; }
    public required bool SourceRepoMutated { get; init; }
    public required bool SourceAppliedToWorkingTree { get; init; }
    public required bool GitCommitCreated { get; init; }
    public required bool GitPushPerformed { get; init; }
    public required bool PullRequestCreated { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceRollbackRequest
{
    public required string SourceRollbackRequestId { get; init; }
    public required string RunId { get; init; }
    public required string SourceApplyReceiptId { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string BaseCommit { get; init; }
    public required string PatchPath { get; init; }
    public required string ExpectedPostApplyDiffSha256 { get; init; }
    public required string CurrentDiffSha256 { get; init; }
    public required string? ConscienceDecisionId { get; init; }
    public required string ThoughtLedgerRef { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public enum SourceRollbackGateDecisionOutcome
{
    AllowRollback = 0,
    Block
}

public sealed record SourceRollbackGateDecision
{
    public required string SourceRollbackGateDecisionId { get; init; }
    public required string RunId { get; init; }
    public required string SourceRollbackRequestId { get; init; }
    public required string SourceApplyReceiptId { get; init; }
    public required SourceRollbackGateDecisionOutcome Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceRollbackCommandResult
{
    public required string SourceRollbackCommandResultId { get; init; }
    public required string RunId { get; init; }
    public required string SourceRollbackRequestId { get; init; }
    public required string Command { get; init; }
    public required int ExitCode { get; init; }
    public required string StdoutPath { get; init; }
    public required string StderrPath { get; init; }
    public required string CombinedOutputPath { get; init; }
    public required bool RolledBackWorkingTree { get; init; }
    public required bool GitCommitCreated { get; init; }
    public required bool GitPushPerformed { get; init; }
    public required bool PullRequestCreated { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public enum SourceRollbackReceiptDecision
{
    RolledBackWorkingTree = 0,
    Blocked,
    Failed
}

public sealed record SourceRollbackReceipt
{
    public required string SourceRollbackReceiptId { get; init; }
    public required string RunId { get; init; }
    public required string SourceRollbackRequestId { get; init; }
    public required string SourceApplyReceiptId { get; init; }
    public required string SourceRollbackGateDecisionId { get; init; }
    public string? SourceRollbackCommandResultId { get; init; }
    public required SourceRollbackReceiptDecision Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public required string BaseCommit { get; init; }
    public required string PreRollbackDiffSha256 { get; init; }
    public string? PostRollbackDiffSha256 { get; init; }
    public required bool SourceRepoMutated { get; init; }
    public required bool RolledBackWorkingTree { get; init; }
    public required bool GitCommitCreated { get; init; }
    public required bool GitPushPerformed { get; init; }
    public required bool PullRequestCreated { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public static class SourceApplyDecisionTemplates
{
    public static ConscienceDecision SourceApply(string runId, string sourceApplyRequestId)
    {
        var decision = Template(GovernedActionKind.SourceApply, "SourceApplyExecutionRequest", sourceApplyRequestId, runId);
        return decision with { DecisionHash = ConscienceDecisionHash.Compute(decision) };
    }

    public static ConscienceDecision SourceRollback(string runId, string sourceApplyReceiptId)
    {
        var decision = Template(GovernedActionKind.SourceRollback, "SourceApplyReceipt", sourceApplyReceiptId, runId);
        return decision with { DecisionHash = ConscienceDecisionHash.Compute(decision) };
    }

    private static ConscienceDecision Template(GovernedActionKind actionKind, string subjectKind, string subjectId, string runId) =>
        new()
        {
            DecisionId = $"conscience_{Guid.NewGuid():N}",
            ActionId = $"gov_action_{Guid.NewGuid():N}",
            ActionKind = actionKind,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            RequestedBy = "human-reviewer",
            EvidenceRefs =
            [
                new ConscienceDecisionEvidenceRef
                {
                    RefId = runId,
                    EvidenceKind = "PatchRun",
                    SafeSummary = "Human must review run evidence before changing this decision to Allow."
                }
            ],
            PolicyRefs = ["BlockAG.ControlledSourceApply"],
            RiskLevel = ConscienceDecisionRiskLevel.Critical,
            Decision = ConscienceDecisionOutcome.RequiresHumanReview,
            RequiredHumanReview = true,
            ThoughtLedgerRef = null,
            DecisionHash = string.Empty,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
}
