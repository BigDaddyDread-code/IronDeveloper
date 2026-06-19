using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Validation;

namespace IronDev.Core.Governance;

public enum PrUpdatePackageVerdict
{
    PackageReadyForExecutor = 0,
    PackageIncomplete,
    PackageBlocked,
    PackageRejected
}

public enum PrUpdateExecutionEligibility
{
    NotEligible = 0,
    Eligible
}

public enum PrUpdateValidationFamily
{
    FocusedCurrentBlock = 0,
    ImpactedArea,
    FastAuthorityInvariant,
    Build,
    DiffCheck
}

public sealed record PrUpdateBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanApplyPatch { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanStage { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanUpdatePullRequest { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanApprove { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanContinueWorkflow { get; init; }

    public static PrUpdateBoundary Evidence { get; } = new();
}

public static class PrUpdateBoundaryText
{
    public const string Boundary = """
        Block AR packages a proposed PR branch update with evidence, expected branch state, validation requirements, and rollback posture.
        It does not apply patches.
        It does not mutate source.
        It does not stage files.
        It does not commit.
        It does not push.
        It does not update PR branches.
        It does not mark PRs ready.
        It does not request reviewers.
        It does not approve.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not continue workflow.
        PR update package is not PR branch mutation.
        """;
}

public sealed record PrUpdateTarget
{
    public required string Repository { get; init; }
    public required int PrNumber { get; init; }
    public required string PrUrl { get; init; }
    public required string PrState { get; init; }
    public required bool PrDraftState { get; init; }
    public required string TargetBranch { get; init; }
    public required string ExpectedCurrentHeadSha { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseSha { get; init; }
    public required DateTimeOffset PackageCreatedAtUtc { get; init; }
    public required string PackageCreatedBy { get; init; }
}

public sealed record PrUpdateExpectedState
{
    public string[] ExpectedChangedFiles { get; init; } = [];
    public required string ExpectedCommitMessage { get; init; }
    public required string ExpectedCommitBody { get; init; }
    public string? ExpectedDiffHash { get; init; }
    public required bool SourceApplyPending { get; init; }
    public string? ExpectedPostUpdateHeadSha { get; init; }
}

public sealed record PrUpdateSourceApplyEvidence
{
    public required string SourceApplyReceiptId { get; init; }
    public required string SourceApplyReceiptHash { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required string SourceApplyDryRunReceiptHash { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ObservedBranch { get; init; }
    public required bool ApplySucceeded { get; init; }
    public required bool MutationOccurred { get; init; }
    public required bool PartialApplyOccurred { get; init; }
    public string[] AppliedFiles { get; init; } = [];
    public required DateTimeOffset AppliedAtUtc { get; init; }
}

public sealed record PrUpdateValidationEvidence
{
    public required string ValidationRunId { get; init; }
    public required string ValidationPlanId { get; init; }
    public required string CommitSha { get; init; }
    public required ValidationRunVerdict Verdict { get; init; }
    public string[] RequiredLaneNames { get; init; } = [];
    public string[] ResultLaneNames { get; init; } = [];
    public string[] SkippedLanes { get; init; } = [];
    public PrUpdateValidationFamily[] SatisfiedFamilies { get; init; } = [];
    public PrUpdateBoundary Boundary { get; init; } = PrUpdateBoundary.Evidence;
}

public sealed record PrUpdateRollbackPlan
{
    public required bool RollbackAvailable { get; init; }
    public required string RollbackStrategy { get; init; }
    public required string PreUpdateHeadSha { get; init; }
    public string? ExpectedPostUpdateHeadSha { get; init; }
    public required string RollbackCommandPreview { get; init; }
    public string[] RollbackRisks { get; init; } = [];
    public PrUpdateBoundary Boundary { get; init; } = PrUpdateBoundary.Evidence;
}

public sealed record PrUpdateBranchUpdateConstraints
{
    public required string TargetBranch { get; init; }
    public required string ExpectedCurrentHeadSha { get; init; }
    public bool CommitAllowed { get; init; }
    public bool PushAllowed { get; init; }
    public string TargetRemote { get; init; } = "origin";
    public required bool ForcePushAllowed { get; init; }
    public required bool ReadyForReviewAllowed { get; init; }
    public required bool ReviewerRequestAllowed { get; init; }
    public required bool MergeAllowed { get; init; }
    public required bool ReleaseAllowed { get; init; }
    public required bool WorkflowContinuationAllowed { get; init; }
}

public sealed record ControlledPrUpdatePackageInput
{
    public FeedbackPatchProposal? Proposal { get; init; }
    public PrUpdateTarget? Target { get; init; }
    public ValidationRunReceipt[] ValidationReceipts { get; init; } = [];
    public PrUpdateSourceApplyEvidence? SourceApplyEvidence { get; init; }
    public bool SourceApplyPending { get; init; }
    public string? ExpectedPostUpdateHeadSha { get; init; }
    public string? ExpectedCommitMessage { get; init; }
    public string? ExpectedCommitBody { get; init; }
    public string? ExpectedDiffHash { get; init; }
    public bool CommitAllowed { get; init; }
    public bool PushAllowed { get; init; }
    public string? TargetRemote { get; init; }
    public string? RollbackStrategy { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record ControlledPrUpdatePackage
{
    public required string PrUpdatePackageId { get; init; }
    public required string PatchProposalId { get; init; }
    public required string SourcePackageId { get; init; }
    public required PrUpdateTarget Target { get; init; }
    public required PrUpdateExpectedState ExpectedState { get; init; }
    public PrUpdateSourceApplyEvidence? SourceApplyEvidence { get; init; }
    public PrUpdateValidationEvidence[] ValidationEvidence { get; init; } = [];
    public required PrUpdateRollbackPlan RollbackPlan { get; init; }
    public required PrUpdateBranchUpdateConstraints BranchUpdateConstraints { get; init; }
    public PrUpdateValidationFamily[] RequiredValidationFamilies { get; init; } = [];
    public PrUpdateValidationFamily[] MissingValidationFamilies { get; init; } = [];
    public string[] PackageIssues { get; init; } = [];
    public required bool CanExecuteBranchUpdate { get; init; }
    public required PrUpdateExecutionEligibility ExecutionEligibility { get; init; }
    public required PrUpdatePackageVerdict Verdict { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PrUpdateBoundary Boundary { get; init; } = PrUpdateBoundary.Evidence;
}

public sealed record ControlledPrUpdatePackageReceipt
{
    public required string PrUpdatePackageReceiptId { get; init; }
    public required string PrUpdatePackageId { get; init; }
    public required string PatchProposalId { get; init; }
    public required PrUpdatePackageVerdict Verdict { get; init; }
    public required PrUpdateExecutionEligibility ExecutionEligibility { get; init; }
    public required bool CanExecuteBranchUpdate { get; init; }
    public int ValidationReceiptCount { get; init; }
    public string[] MissingValidationFamilies { get; init; } = [];
    public string[] PackageIssues { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PrUpdateBoundary Boundary { get; init; } = PrUpdateBoundary.Evidence;
}

public sealed record ControlledPrUpdatePackageArtifacts
{
    public required ControlledPrUpdatePackage Package { get; init; }
    public required ControlledPrUpdatePackageReceipt Receipt { get; init; }
}

public static class ControlledPrUpdatePackageBuilder
{
    public static readonly PrUpdateValidationFamily[] MinimumValidationFamilies =
    [
        PrUpdateValidationFamily.FocusedCurrentBlock,
        PrUpdateValidationFamily.ImpactedArea,
        PrUpdateValidationFamily.FastAuthorityInvariant,
        PrUpdateValidationFamily.Build,
        PrUpdateValidationFamily.DiffCheck
    ];

    public static ControlledPrUpdatePackageArtifacts Build(ControlledPrUpdatePackageInput input)
    {
        var proposal = input.Proposal;
        var target = input.Target;
        var issues = new List<string>();
        var incomplete = new List<string>();
        var blocked = new List<string>();

        if (proposal is null)
            issues.Add("MissingFeedbackPatchProposal");
        if (target is null)
            issues.Add("MissingPrUpdateTarget");

        var safeTarget = target ?? MissingTarget(input.CreatedAtUtc);
        ValidateTarget(safeTarget, issues, blocked);
        ValidateProposalBinding(proposal, safeTarget, issues, blocked);

        var sourceApplyPending = input.SourceApplyEvidence is null || input.SourceApplyPending;
        var expectedPostUpdateHead = FeedbackText.SafeOrNull(input.ExpectedPostUpdateHeadSha);
        ValidateSourceApply(input.SourceApplyEvidence, sourceApplyPending, safeTarget, proposal, expectedPostUpdateHead, incomplete, blocked);

        var validationEvidence = input.ValidationReceipts.Select(BuildValidationEvidence).ToArray();
        ValidateValidation(input.ValidationReceipts, validationEvidence, sourceApplyPending ? safeTarget.ExpectedCurrentHeadSha : expectedPostUpdateHead, issues, incomplete, blocked);
        var missingFamilies = MissingValidationFamilies(validationEvidence);
        if (missingFamilies.Length > 0)
            incomplete.AddRange(missingFamilies.Select(family => $"MissingRequiredValidationFamily:{family}"));

        if (sourceApplyPending)
            incomplete.Add("SourceApplyPending");
        if (!sourceApplyPending && string.IsNullOrWhiteSpace(expectedPostUpdateHead))
            incomplete.Add("ExpectedPostUpdateHeadShaRequired");

        var verdict = DetermineVerdict(issues, incomplete, blocked);
        var canExecute = verdict == PrUpdatePackageVerdict.PackageReadyForExecutor &&
                         input.SourceApplyEvidence is not null &&
                         missingFamilies.Length == 0 &&
                         blocked.Count == 0 &&
                         issues.Count == 0;
        var expectedChangedFiles = FeedbackText.SafeList(proposal?.ExpectedChangedFiles ?? []);
        var now = input.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var packageId = $"pr_update_pkg_{ArPrUpdateHashing.ShortHash($"{proposal?.PatchProposalId ?? "missing"}|{safeTarget.PrNumber}|{safeTarget.TargetBranch}|{safeTarget.ExpectedCurrentHeadSha}|{verdict}")}";
        var packageIssues = FeedbackText.SafeList(issues.Concat(blocked).Concat(incomplete));
        var package = new ControlledPrUpdatePackage
        {
            PrUpdatePackageId = packageId,
            PatchProposalId = proposal?.PatchProposalId ?? "missing-feedback-patch-proposal",
            SourcePackageId = proposal?.SourcePackageId ?? "missing-source-package",
            Target = safeTarget,
            ExpectedState = new PrUpdateExpectedState
            {
                ExpectedChangedFiles = expectedChangedFiles,
                ExpectedCommitMessage = FeedbackText.Safe(input.ExpectedCommitMessage ?? $"Apply feedback patch proposal {proposal?.PatchProposalId ?? "missing"}"),
                ExpectedCommitBody = FeedbackText.Safe(input.ExpectedCommitBody ?? "Future AS executor must create the actual commit only after package eligibility is re-verified."),
                ExpectedDiffHash = FeedbackText.SafeOrNull(input.ExpectedDiffHash),
                SourceApplyPending = sourceApplyPending,
                ExpectedPostUpdateHeadSha = expectedPostUpdateHead
            },
            SourceApplyEvidence = input.SourceApplyEvidence,
            ValidationEvidence = validationEvidence,
            RollbackPlan = BuildRollbackPlan(safeTarget, expectedPostUpdateHead, input.RollbackStrategy),
            BranchUpdateConstraints = new PrUpdateBranchUpdateConstraints
            {
                TargetBranch = safeTarget.TargetBranch,
                ExpectedCurrentHeadSha = safeTarget.ExpectedCurrentHeadSha,
                CommitAllowed = input.CommitAllowed,
                PushAllowed = input.PushAllowed,
                TargetRemote = FeedbackText.Safe(input.TargetRemote ?? "origin"),
                ForcePushAllowed = false,
                ReadyForReviewAllowed = false,
                ReviewerRequestAllowed = false,
                MergeAllowed = false,
                ReleaseAllowed = false,
                WorkflowContinuationAllowed = false
            },
            RequiredValidationFamilies = MinimumValidationFamilies,
            MissingValidationFamilies = missingFamilies,
            PackageIssues = packageIssues,
            CanExecuteBranchUpdate = canExecute,
            ExecutionEligibility = canExecute ? PrUpdateExecutionEligibility.Eligible : PrUpdateExecutionEligibility.NotEligible,
            Verdict = verdict,
            CreatedAtUtc = now,
            Boundary = PrUpdateBoundary.Evidence
        };
        var receipt = new ControlledPrUpdatePackageReceipt
        {
            PrUpdatePackageReceiptId = $"pr_update_pkg_receipt_{ArPrUpdateHashing.ShortHash($"{package.PrUpdatePackageId}|{package.Verdict}|{package.ExecutionEligibility}")}",
            PrUpdatePackageId = package.PrUpdatePackageId,
            PatchProposalId = package.PatchProposalId,
            Verdict = package.Verdict,
            ExecutionEligibility = package.ExecutionEligibility,
            CanExecuteBranchUpdate = package.CanExecuteBranchUpdate,
            ValidationReceiptCount = package.ValidationEvidence.Length,
            MissingValidationFamilies = package.MissingValidationFamilies.Select(item => item.ToString()).ToArray(),
            PackageIssues = package.PackageIssues,
            BoundaryStatements =
            [
                "AR package is evidence only.",
                "AR package requires an AQ feedback patch proposal.",
                "AR package records PR identity, expected branch/head state, validation evidence, and rollback posture.",
                "AR package does not apply patches, commit, push, update PR branches, mark ready, request reviewers, merge, release, deploy, or continue workflow.",
                "PR update package is not PR branch mutation."
            ],
            CreatedAtUtc = now,
            Boundary = PrUpdateBoundary.Evidence
        };

        return new ControlledPrUpdatePackageArtifacts
        {
            Package = package,
            Receipt = receipt
        };
    }

    public static PrUpdateSourceApplyEvidence FromSourceApplyReceipt(SourceApplyReceipt receipt) => new()
    {
        SourceApplyReceiptId = receipt.SourceApplyReceiptId.ToString("D"),
        SourceApplyReceiptHash = receipt.SourceApplyReceiptHash,
        SourceApplyRequestHash = receipt.SourceApplyRequestHash,
        SourceApplyDryRunReceiptHash = receipt.SourceApplyDryRunReceiptHash,
        RollbackSupportReceiptHash = receipt.RollbackSupportReceiptHash,
        ExpectedBranch = receipt.ExpectedBranch,
        ObservedBranch = receipt.ObservedBranch,
        ApplySucceeded = receipt.ApplySucceeded,
        MutationOccurred = receipt.MutationOccurred,
        PartialApplyOccurred = receipt.PartialApplyOccurred,
        AppliedFiles = FeedbackText.SafeList(receipt.FileResults.Select(item => item.Path)),
        AppliedAtUtc = receipt.AppliedAtUtc
    };

    private static PrUpdateTarget MissingTarget(DateTimeOffset? createdAt) => new()
    {
        Repository = "missing",
        PrNumber = 0,
        PrUrl = "missing",
        PrState = "missing",
        PrDraftState = true,
        TargetBranch = "missing",
        ExpectedCurrentHeadSha = "missing",
        BaseBranch = "missing",
        BaseSha = "missing",
        PackageCreatedAtUtc = createdAt ?? DateTimeOffset.UtcNow,
        PackageCreatedBy = "missing"
    };

    private static void ValidateTarget(PrUpdateTarget target, List<string> issues, List<string> blocked)
    {
        RequireText(target.Repository, "MissingRepository", issues);
        RequireText(target.PrUrl, "MissingPrUrl", issues);
        RequireText(target.TargetBranch, "MissingTargetBranch", issues);
        RequireText(target.ExpectedCurrentHeadSha, "MissingExpectedCurrentHeadSha", issues);
        RequireText(target.BaseBranch, "MissingBaseBranch", issues);
        RequireText(target.BaseSha, "MissingBaseSha", issues);
        RequireText(target.PackageCreatedBy, "MissingPackageCreatedBy", issues);
        if (target.PrNumber <= 0)
            issues.Add("MissingPrNumber");
        if (!string.Equals(target.PrState, "open", StringComparison.OrdinalIgnoreCase))
            blocked.Add("PullRequestNotOpen");
    }

    private static void ValidateProposalBinding(FeedbackPatchProposal? proposal, PrUpdateTarget target, List<string> issues, List<string> blocked)
    {
        if (proposal is null)
            return;
        if (proposal.Verdict == FeedbackPatchProposalVerdict.Rejected)
            blocked.Add("PatchProposalRejected");
        if (proposal.TargetPrNumber != target.PrNumber)
            issues.Add("PatchProposalPrMismatch");
        if (!Same(proposal.TargetHeadSha, target.ExpectedCurrentHeadSha))
            blocked.Add("PatchProposalHeadMismatch");
        if (!string.IsNullOrWhiteSpace(proposal.BaseSha) && !Same(proposal.BaseSha, target.BaseSha))
            issues.Add("PatchProposalBaseShaMismatch");
    }

    private static void ValidateSourceApply(
        PrUpdateSourceApplyEvidence? evidence,
        bool sourceApplyPending,
        PrUpdateTarget target,
        FeedbackPatchProposal? proposal,
        string? expectedPostUpdateHead,
        List<string> incomplete,
        List<string> blocked)
    {
        if (sourceApplyPending)
            return;
        if (evidence is null)
        {
            incomplete.Add("SourceApplyEvidenceRequired");
            return;
        }

        if (!evidence.ApplySucceeded)
            blocked.Add("SourceApplyEvidenceNotSucceeded");
        if (!evidence.MutationOccurred)
            blocked.Add("SourceApplyEvidenceNoMutation");
        if (evidence.PartialApplyOccurred)
            blocked.Add("SourceApplyEvidencePartialApply");
        if (!Same(evidence.ExpectedBranch, target.TargetBranch) || !Same(evidence.ObservedBranch, target.TargetBranch))
            blocked.Add("SourceApplyBranchMismatch");
        if (string.IsNullOrWhiteSpace(expectedPostUpdateHead))
            incomplete.Add("ExpectedPostUpdateHeadShaRequired");

        var expectedFiles = FeedbackText.SafeList(proposal?.ExpectedChangedFiles ?? []);
        var expected = expectedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appliedFiles = FeedbackText.SafeList(evidence.AppliedFiles);
        var applied = appliedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in expectedFiles)
        {
            if (!applied.Contains(file))
                blocked.Add($"SourceApplyMissingExpectedFile:{file}");
        }

        foreach (var file in appliedFiles)
        {
            if (!expected.Contains(file))
                blocked.Add($"SourceApplyUnexpectedFile:{file}");
        }
    }

    private static void ValidateValidation(
        ValidationRunReceipt[] receipts,
        PrUpdateValidationEvidence[] evidence,
        string? expectedValidationSha,
        List<string> issues,
        List<string> incomplete,
        List<string> blocked)
    {
        if (receipts.Length == 0)
        {
            issues.Add("MissingValidationReceipt");
            return;
        }

        if (string.IsNullOrWhiteSpace(expectedValidationSha))
        {
            incomplete.Add("ExpectedValidationShaMissing");
            return;
        }

        foreach (var receipt in receipts)
        {
            if (receipt.Verdict != ValidationRunVerdict.Passed)
                blocked.Add($"ValidationReceiptNotPassed:{receipt.ValidationRunId}:{receipt.Verdict}");
            if (!Same(receipt.CommitSha, expectedValidationSha))
                blocked.Add($"ValidationReceiptShaMismatch:{receipt.ValidationRunId}");
            var executedLaneNames = receipt.Results.Select(result => result.LaneName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var requiredLane in receipt.RequiredLanes)
            {
                if (!executedLaneNames.Contains(requiredLane.Name))
                    incomplete.Add($"ValidationRequiredLaneMissingResult:{receipt.ValidationRunId}:{requiredLane.Name}");
            }

            if (receipt.SkippedLanes.Any(skipped => receipt.RequiredLanes.Any(lane => Same(lane.Name, skipped))))
                incomplete.Add($"ValidationSkippedRequiredLane:{receipt.ValidationRunId}");
        }

        foreach (var item in evidence.Where(item => item.SkippedLanes.Length > 0))
            incomplete.AddRange(item.SkippedLanes.Select(lane => $"ValidationSkippedLane:{item.ValidationRunId}:{lane}"));
    }

    private static PrUpdateValidationEvidence BuildValidationEvidence(ValidationRunReceipt receipt)
    {
        var laneNames = receipt.Results.Select(result => result.LaneName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new PrUpdateValidationEvidence
        {
            ValidationRunId = receipt.ValidationRunId,
            ValidationPlanId = receipt.ValidationPlanId,
            CommitSha = receipt.CommitSha,
            Verdict = receipt.Verdict,
            RequiredLaneNames = receipt.RequiredLanes.Select(lane => lane.Name).ToArray(),
            ResultLaneNames = receipt.Results.Select(result => result.LaneName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SkippedLanes = receipt.SkippedLanes,
            SatisfiedFamilies = SatisfiedFamilies(laneNames, receipt.Results),
            Boundary = PrUpdateBoundary.Evidence
        };
    }

    private static PrUpdateValidationFamily[] MissingValidationFamilies(PrUpdateValidationEvidence[] evidence)
    {
        var satisfied = evidence.SelectMany(item => item.SatisfiedFamilies).Distinct().ToHashSet();
        return MinimumValidationFamilies.Where(family => !satisfied.Contains(family)).ToArray();
    }

    private static PrUpdateValidationFamily[] SatisfiedFamilies(string[] laneNames, ValidationProcessResult[] results)
    {
        var families = new List<PrUpdateValidationFamily>();
        if (laneNames.Any(name => name.StartsWith("focused-", StringComparison.OrdinalIgnoreCase) || Same(name, "phase-gate")))
            families.Add(PrUpdateValidationFamily.FocusedCurrentBlock);
        if (laneNames.Any(name => name.Contains("impacted", StringComparison.OrdinalIgnoreCase) || name.Contains("governance", StringComparison.OrdinalIgnoreCase) || Same(name, "cli-command-surface")))
            families.Add(PrUpdateValidationFamily.ImpactedArea);
        if (laneNames.Any(name => name.Contains("authority", StringComparison.OrdinalIgnoreCase)))
            families.Add(PrUpdateValidationFamily.FastAuthorityInvariant);
        if (laneNames.Any(name => Same(name, "build")) || results.Any(result => result.Command.Contains("build", StringComparison.OrdinalIgnoreCase)))
            families.Add(PrUpdateValidationFamily.Build);
        if (laneNames.Any(name => Same(name, "diff-check")) || results.Any(result => result.Command.Contains("diff", StringComparison.OrdinalIgnoreCase)))
            families.Add(PrUpdateValidationFamily.DiffCheck);
        return families.Distinct().ToArray();
    }

    private static PrUpdateRollbackPlan BuildRollbackPlan(PrUpdateTarget target, string? expectedPostUpdateHead, string? strategy) => new()
    {
        RollbackAvailable = !string.IsNullOrWhiteSpace(target.ExpectedCurrentHeadSha),
        RollbackStrategy = FeedbackText.Safe(strategy ?? "Future AS execution must preserve pre-update head and verify post-update head before any rollback action."),
        PreUpdateHeadSha = target.ExpectedCurrentHeadSha,
        ExpectedPostUpdateHeadSha = expectedPostUpdateHead,
        RollbackCommandPreview = "Future AS-only rollback preview: restore target branch to PreUpdateHeadSha after verifying ExpectedPostUpdateHeadSha. AR does not execute this.",
        RollbackRisks =
        [
            "Rollback plan is not rollback execution.",
            "Rollback cannot be executed by AR.",
            "Future executor must re-check branch and head before mutation."
        ],
        Boundary = PrUpdateBoundary.Evidence
    };

    private static PrUpdatePackageVerdict DetermineVerdict(List<string> issues, List<string> incomplete, List<string> blocked)
    {
        if (issues.Count > 0)
            return PrUpdatePackageVerdict.PackageRejected;
        if (blocked.Count > 0)
            return PrUpdatePackageVerdict.PackageBlocked;
        if (incomplete.Count > 0)
            return PrUpdatePackageVerdict.PackageIncomplete;
        return PrUpdatePackageVerdict.PackageReadyForExecutor;
    }

    private static void RequireText(string? value, string issue, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(issue);
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class ControlledPrUpdatePackageBypassEvaluator
{
    public static bool CanApplyPatch(object? evidence) => false;
    public static bool CanMutateSource(object? evidence) => false;
    public static bool CanCommit(object? evidence) => false;
    public static bool CanPush(object? evidence) => false;
    public static bool CanUpdatePullRequest(object? evidence) => false;
    public static bool CanMarkReadyForReview(object? evidence) => false;
    public static bool CanRequestReviewers(object? evidence) => false;
    public static bool CanApprove(object? evidence) => false;
    public static bool CanMerge(object? evidence) => false;
    public static bool CanRelease(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
}

internal static class ArPrUpdateHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
