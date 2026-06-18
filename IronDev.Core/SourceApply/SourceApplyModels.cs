using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.SourceApply;

public sealed record SourceApplyBoundary
{
    public bool SourceRepoMutated { get; init; }
    public bool SourceApplied { get; init; }
    public bool GitCommitCreated { get; init; }
    public bool GitPushPerformed { get; init; }
    public bool PullRequestCreated { get; init; }
    public bool ApprovalGranted { get; init; }
    public bool PolicySatisfied { get; init; }
    public bool ReleaseApproved { get; init; }
    public bool DeploymentApproved { get; init; }
    public bool MergeApproved { get; init; }
    public bool WorkflowContinued { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool AgentDispatched { get; init; }
    public bool ModelCalled { get; init; }
    public bool RollbackExecuted { get; init; }
    public bool PatchAppliedInRehearsalWorkspace { get; init; }

    public static SourceApplyBoundary None { get; } = new();

    public static SourceApplyBoundary RehearsalApplied { get; } = new()
    {
        PatchAppliedInRehearsalWorkspace = true
    };
}

public sealed record SourceApplyEvidenceRef
{
    public required string RefId { get; init; }
    public required string EvidenceKind { get; init; }
    public required string Path { get; init; }
    public required string SafeSummary { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record SourceApplyRequest
{
    public required string SourceApplyRequestId { get; init; }
    public required string RunId { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseCommit { get; init; }
    public required string PatchPath { get; init; }
    public required string PatchSha256 { get; init; }
    public string[] ChangedFiles { get; init; } = [];
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public SourceApplyEvidenceRef[] EvidenceRefs { get; init; } = [];
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceApplyApprovalEvidence
{
    public required string ApprovalEvidenceId { get; init; }
    public required string RunId { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string BaseCommit { get; init; }
    public required string PatchSha256 { get; init; }
    public string[] ApprovedChangedFiles { get; init; } = [];
    public required string ApprovedBy { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required string ApprovalText { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public enum PatchArtifactVerificationDecision
{
    Verified = 0,
    Blocked
}

public sealed record PatchArtifactVerificationResult
{
    public required string PatchArtifactVerificationId { get; init; }
    public required string RunId { get; init; }
    public required string PatchPath { get; init; }
    public required bool PatchExists { get; init; }
    public required string PatchSha256 { get; init; }
    public required string ExpectedPatchSha256 { get; init; }
    public required bool PatchHashMatchesRun { get; init; }
    public required bool RunMetadataExists { get; init; }
    public required bool BaseCommitMatchesRun { get; init; }
    public required bool ChangedFilesMatchRun { get; init; }
    public required bool ManualApplyInstructionsExist { get; init; }
    public required PatchArtifactVerificationDecision Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public required DateTimeOffset VerifiedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public enum SourceApplyGateDecisionOutcome
{
    AllowDryRun = 0,
    Block
}

public sealed record SourceApplyGateDecision
{
    public required string SourceApplyGateDecisionId { get; init; }
    public required string RunId { get; init; }
    public required string SourceApplyRequestId { get; init; }
    public required string PatchArtifactVerificationId { get; init; }
    public string? ApprovalEvidenceId { get; init; }
    public required SourceApplyGateDecisionOutcome Decision { get; init; }
    public string[] Reasons { get; init; } = [];
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceApplyDryRunPlan
{
    public required string SourceApplyDryRunPlanId { get; init; }
    public required string RunId { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string ApplyRehearsalWorkspacePath { get; init; }
    public required string PatchPath { get; init; }
    public required string PatchSha256 { get; init; }
    public required string BaseCommit { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceApplyDryRunResult
{
    public required string SourceApplyDryRunResultId { get; init; }
    public required string RunId { get; init; }
    public required string SourceApplyDryRunPlanId { get; init; }
    public required string RehearsalWorkspacePath { get; init; }
    public required string RehearsalBaseCommit { get; init; }
    public required string RehearsalHeadCommit { get; init; }
    public required string Command { get; init; }
    public required int ExitCode { get; init; }
    public required string StdoutPath { get; init; }
    public required string StderrPath { get; init; }
    public required string CombinedOutputPath { get; init; }
    public required bool PatchAppliedInRehearsalWorkspace { get; init; }
    public required bool SourceRepoMutated { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record RollbackPlanDraft
{
    public required string RollbackPlanDraftId { get; init; }
    public required string RunId { get; init; }
    public required string PatchSha256 { get; init; }
    public string[] ChangedFiles { get; init; } = [];
    public required string ReversePatchPath { get; init; }
    public required string RevertInstructionsPath { get; init; }
    public string[] RiskNotes { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public enum SourceApplyReadiness
{
    ReadyForFutureControlledApply = 0,
    Blocked
}

public sealed record SourceApplyReadinessReport
{
    public required string SourceApplyReadinessReportId { get; init; }
    public required string RunId { get; init; }
    public required string SourceApplyRequestId { get; init; }
    public required string PatchArtifactVerificationId { get; init; }
    public required string SourceApplyGateDecisionId { get; init; }
    public string? SourceApplyDryRunResultId { get; init; }
    public string? RollbackPlanDraftId { get; init; }
    public required SourceApplyReadiness Readiness { get; init; }
    public string[] Reasons { get; init; } = [];
    public SourceApplyEvidenceRef[] EvidenceRefs { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceApplyReadinessSummary
{
    public required string RunId { get; init; }
    public required SourceApplyReadiness Readiness { get; init; }
    public required bool PatchVerified { get; init; }
    public required bool ApprovalEvidenceValid { get; init; }
    public required bool GateAllowedDryRun { get; init; }
    public required bool DryRunSucceededInRehearsalWorkspace { get; init; }
    public required bool RollbackPlanDrafted { get; init; }
    public string[] Reasons { get; init; } = [];
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceApplyRunMetadata
{
    public required string RunId { get; init; }
    public required string RunPath { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseCommit { get; init; }
    public string? PatchSha256 { get; init; }
    public string[] ChangedFiles { get; init; } = [];
}

public static class SourceApplyHash
{
    public static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string TextSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
