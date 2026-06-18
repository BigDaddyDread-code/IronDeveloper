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
    public string? SourceApplyRequestId { get; init; }
    public required string RunId { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string BaseCommit { get; init; }
    public required string PatchSha256 { get; init; }
    public string[] ApprovedChangedFiles { get; init; } = [];
    public required string ApprovedBy { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public string? ConscienceDecisionId { get; init; }
    public string? ThoughtLedgerEntryId { get; init; }
    public required string ApprovalText { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public sealed record SourceApplyBindingReport
{
    public required string SourceApplyBindingReportId { get; init; }
    public required string SourceApplyRequestId { get; init; }
    public required string SourceApplyApprovalId { get; init; }
    public required string RunId { get; init; }
    public required bool SourceApplyRequestIdMatched { get; init; }
    public required bool RunIdMatched { get; init; }
    public required bool PatchHashMatched { get; init; }
    public required bool ChangedFilesHashMatched { get; init; }
    public required bool SourceRepoIdentityMatched { get; init; }
    public required bool BaseCommitMatched { get; init; }
    public required bool ConscienceDecisionPresent { get; init; }
    public required bool ThoughtLedgerEntryPresent { get; init; }
    public required bool ApprovedByPresent { get; init; }
    public required bool ApprovalStatementBounded { get; init; }
    public required bool BindingPassed { get; init; }
    public string[] BlockingReasons { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public SourceApplyBoundary Boundary { get; init; } = SourceApplyBoundary.None;
}

public static class SourceApplyApprovalBinding
{
    private const string RequiredApprovalStatement = "I approve this source-apply request for controlled working-tree application only.";
    private const string ForbiddenAuthorityStatement = "commit, push, pull request creation, merge, release, deployment, or workflow continuation";

    public static SourceApplyBindingReport Validate(SourceApplyRequest request, SourceApplyApprovalEvidence? approval)
    {
        var reasons = new List<string>();
        var requestIdMatched = approval is not null && (string.IsNullOrWhiteSpace(approval.SourceApplyRequestId) || Same(approval.SourceApplyRequestId, request.SourceApplyRequestId));
        var runMatched = approval is not null && Same(approval.RunId, request.RunId);
        var patchMatched = approval is not null && Same(approval.PatchSha256, request.PatchSha256);
        var changedFilesMatched = approval is not null && SameSet(approval.ApprovedChangedFiles, request.ChangedFiles);
        var repoMatched = approval is not null && Same(approval.SourceRepoIdentity, request.SourceRepoIdentity);
        var baseMatched = approval is not null && Same(approval.BaseCommit, request.BaseCommit);
        var consciencePresent = approval is not null && !string.IsNullOrWhiteSpace(approval.ConscienceDecisionId);
        var thoughtLedgerPresent = approval is not null && !string.IsNullOrWhiteSpace(approval.ThoughtLedgerEntryId);
        var approvedByPresent = approval is not null && !string.IsNullOrWhiteSpace(approval.ApprovedBy) && approval.HumanReviewRequired;
        var boundedStatement = approval is not null &&
            approval.ApprovalText.Contains(RequiredApprovalStatement, StringComparison.OrdinalIgnoreCase) &&
            approval.ApprovalText.Contains(ForbiddenAuthorityStatement, StringComparison.OrdinalIgnoreCase) &&
            !ContainsOverbroadApproval(approval.ApprovalText);

        if (approval is null)
            reasons.Add("MissingApproval");
        if (!requestIdMatched)
            reasons.Add("SourceApplyRequestIdMismatch");
        if (!runMatched)
            reasons.Add("RunIdMismatch");
        if (!patchMatched)
            reasons.Add("PatchHashMismatch");
        if (!changedFilesMatched)
            reasons.Add("ChangedFilesHashMismatch");
        if (!repoMatched)
            reasons.Add("SourceRepoIdentityMismatch");
        if (!baseMatched)
            reasons.Add("BaseCommitMismatch");
        if (!consciencePresent)
            reasons.Add("MissingConscienceDecision");
        if (!thoughtLedgerPresent)
            reasons.Add("MissingThoughtLedgerEntry");
        if (!approvedByPresent)
            reasons.Add("MissingApprovedBy");
        if (!boundedStatement)
            reasons.Add("OverbroadApproval");

        return new SourceApplyBindingReport
        {
            SourceApplyBindingReportId = $"source_apply_binding_{Guid.NewGuid():N}",
            SourceApplyRequestId = request.SourceApplyRequestId,
            SourceApplyApprovalId = approval?.ApprovalEvidenceId ?? string.Empty,
            RunId = request.RunId,
            SourceApplyRequestIdMatched = requestIdMatched,
            RunIdMatched = runMatched,
            PatchHashMatched = patchMatched,
            ChangedFilesHashMatched = changedFilesMatched,
            SourceRepoIdentityMatched = repoMatched,
            BaseCommitMatched = baseMatched,
            ConscienceDecisionPresent = consciencePresent,
            ThoughtLedgerEntryPresent = thoughtLedgerPresent,
            ApprovedByPresent = approvedByPresent,
            ApprovalStatementBounded = boundedStatement,
            BindingPassed = reasons.Count == 0,
            BlockingReasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = SourceApplyBoundary.None
        };
    }

    private static bool ContainsOverbroadApproval(string text)
    {
        foreach (var marker in new[]
                 {
                     "approve commit",
                     "approve push",
                     "approve pull request",
                     "approve pr",
                     "approve merge",
                     "approve release",
                     "approve deployment",
                     "continue workflow",
                     "ship it",
                     "ready to merge",
                     "ready to release"
                 })
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameSet(string[] first, string[] second)
    {
        var normalizedFirst = first.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        var normalizedSecond = second.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        return normalizedFirst.Length == normalizedSecond.Length && normalizedFirst.Zip(normalizedSecond).All(pair => Same(pair.First, pair.Second));
    }
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
