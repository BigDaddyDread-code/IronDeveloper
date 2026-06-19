using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Validation;

namespace IronDev.Core.Governance;

public enum ReadyForReviewEligibilityVerdict
{
    EligibleForReadyExecutor = 0,
    Incomplete,
    Blocked,
    Rejected
}

public enum ReadyForReviewBlockReason
{
    MissingPullRequestIdentity = 0,
    PullRequestNotOpen,
    PullRequestAlreadyReady,
    PullRequestNotDraft,
    HeadShaMismatch,
    BaseBranchMismatch,
    BaseShaMismatch,
    MissingBranchUpdateEvidence,
    BranchUpdateNotExecuted,
    BranchUpdateReceiptPrMismatch,
    BranchUpdateReceiptHeadMismatch,
    BranchUpdateReceiptNotPushed,
    MissingValidationEvidence,
    ValidationReceiptNotPassed,
    ValidationReceiptShaMismatch,
    MissingRequiredValidationFamily,
    SkippedRequiredValidationLane,
    MissingPhaseAuthorityReceipt,
    PhaseAuthorityReceiptInvalid,
    ReadyForReviewMutationNotAllowed,
    ReviewerRequestNotAllowed,
    MergeReleaseDeployNotAllowed
}

public enum ReadyForReviewValidationFamily
{
    FocusedCurrentBlock = 0,
    ImpactedArea,
    FastAuthorityInvariant,
    Build,
    DiffCheck,
    PhaseAuthority
}

public sealed record ReadyForReviewBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanResolveReviewThreads { get; init; }
    public bool CanApprove { get; init; }
    public bool CanMerge { get; init; }
    public bool CanAutoMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanTag { get; init; }
    public bool CanPublish { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }

    public static ReadyForReviewBoundary Evidence { get; } = new();
}

public static class ReadyForReviewBoundaryText
{
    public const string Boundary = """
        Block AT separates PR branch update evidence from ready-for-review authority.
        It packages ready-for-review eligibility evidence.
        It does not mark PRs ready.
        It does not request reviewers.
        It does not resolve review threads.
        It does not approve.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not tag.
        It does not publish.
        It does not promote memory.
        It does not continue workflow.
        PR branch update is not ready-for-review.
        Ready-for-review is not reviewer request.
        Reviewer request is not merge readiness.
        Validation evidence is not approval.
        """;
}

public sealed record ReadyForReviewTarget
{
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required string PullRequestState { get; init; }
    public required bool PullRequestDraft { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ObservedHeadSha { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseSha { get; init; }
}

public sealed record ReadyForReviewEvidence
{
    public string? BranchUpdateReceiptId { get; init; }
    public string? BranchUpdatePackageId { get; init; }
    public PrBranchUpdateExecutionVerdict? BranchUpdateVerdict { get; init; }
    public string? BranchUpdateCommitSha { get; init; }
    public string? BranchUpdatePostHeadSha { get; init; }
    public bool BranchUpdatePushed { get; init; }
    public bool ExplicitNoBranchUpdateRequired { get; init; }
    public string? NoBranchUpdateEvidenceId { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public ReadyForReviewBoundary Boundary { get; init; } = ReadyForReviewBoundary.Evidence;
}

public sealed record ReadyForReviewValidationEvidence
{
    public required string ValidationRunId { get; init; }
    public required string ValidationPlanId { get; init; }
    public required string CommitSha { get; init; }
    public required ValidationRunVerdict Verdict { get; init; }
    public string[] RequiredLaneNames { get; init; } = [];
    public string[] ResultLaneNames { get; init; } = [];
    public string[] SkippedLanes { get; init; } = [];
    public ReadyForReviewValidationFamily[] SatisfiedFamilies { get; init; } = [];
    public ReadyForReviewBoundary Boundary { get; init; } = ReadyForReviewBoundary.Evidence;
}

public sealed record ExplicitNoBranchUpdateRequiredEvidence
{
    public required string EvidenceId { get; init; }
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseSha { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ReadyForReviewBoundary Boundary { get; init; } = ReadyForReviewBoundary.Evidence;
}

public sealed record ReadyForReviewSeparationInput
{
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public string? PullRequestUrl { get; init; }
    public required string PullRequestState { get; init; }
    public required bool PullRequestDraft { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string? ObservedHeadSha { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseSha { get; init; }
    public string? ExpectedBaseBranch { get; init; }
    public string? ExpectedBaseSha { get; init; }
    public PrBranchUpdateExecutionReceipt? BranchUpdateReceipt { get; init; }
    public ExplicitNoBranchUpdateRequiredEvidence? NoBranchUpdateRequiredEvidence { get; init; }
    public ValidationRunReceipt[] ValidationReceipts { get; init; } = [];
    public string? PhaseAuthorityReceiptId { get; init; }
    public string? PhaseAuthorityReceiptText { get; init; }
    public string? PackageCreatedBy { get; init; }
    public DateTimeOffset? PackageCreatedAtUtc { get; init; }
}

public sealed record ReadyForReviewEligibilityPackage
{
    public required string ReadyForReviewPackageId { get; init; }
    public required ReadyForReviewTarget Target { get; init; }
    public required ReadyForReviewEvidence BranchUpdateEvidence { get; init; }
    public ReadyForReviewValidationEvidence[] ValidationEvidence { get; init; } = [];
    public ReadyForReviewValidationFamily[] RequiredValidationFamilies { get; init; } = [];
    public ReadyForReviewValidationFamily[] MissingValidationFamilies { get; init; } = [];
    public required string PhaseAuthorityReceiptId { get; init; }
    public required bool PhaseAuthorityReceiptValid { get; init; }
    public required ReadyForReviewEligibilityVerdict Verdict { get; init; }
    public required bool CanMarkReadyForReview { get; init; }
    public ReadyForReviewBlockReason[] BlockReasons { get; init; } = [];
    public string[] PackageIssues { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string CreatedBy { get; init; }
    public ReadyForReviewBoundary Boundary { get; init; } = ReadyForReviewBoundary.Evidence;
}

public sealed record ReadyForReviewSeparationReceipt
{
    public required string ReadyForReviewReceiptId { get; init; }
    public required string ReadyForReviewPackageId { get; init; }
    public required ReadyForReviewEligibilityVerdict Verdict { get; init; }
    public required bool CanMarkReadyForReview { get; init; }
    public ReadyForReviewBlockReason[] BlockReasons { get; init; } = [];
    public string[] MissingValidationFamilies { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ReadyForReviewBoundary Boundary { get; init; } = ReadyForReviewBoundary.Evidence;
}

public sealed record ReadyForReviewSeparationArtifacts
{
    public required ReadyForReviewEligibilityPackage Package { get; init; }
    public required ReadyForReviewSeparationReceipt Receipt { get; init; }
}

public static class ReadyForReviewSeparationBuilder
{
    public static readonly ReadyForReviewValidationFamily[] MinimumValidationFamilies =
    [
        ReadyForReviewValidationFamily.FocusedCurrentBlock,
        ReadyForReviewValidationFamily.ImpactedArea,
        ReadyForReviewValidationFamily.FastAuthorityInvariant,
        ReadyForReviewValidationFamily.Build,
        ReadyForReviewValidationFamily.DiffCheck,
        ReadyForReviewValidationFamily.PhaseAuthority
    ];

    public static ReadyForReviewSeparationArtifacts Build(ReadyForReviewSeparationInput input)
    {
        var now = input.PackageCreatedAtUtc ?? DateTimeOffset.UtcNow;
        var rejected = new List<ReadyForReviewBlockReason>();
        var blocked = new List<ReadyForReviewBlockReason>();
        var incomplete = new List<ReadyForReviewBlockReason>();
        var issues = new List<string>();
        var target = BuildTarget(input);

        ValidateTarget(input, target, rejected, blocked, issues);
        var branchEvidence = ValidateBranchUpdateEvidence(input, target, incomplete, blocked, issues);
        var validationEvidence = input.ValidationReceipts.Select(BuildValidationEvidence).ToArray();
        ValidateValidation(input.ValidationReceipts, validationEvidence, target.ExpectedHeadSha, incomplete, blocked, issues);
        var missingFamilies = MissingValidationFamilies(validationEvidence);
        if (missingFamilies.Length > 0)
        {
            incomplete.Add(ReadyForReviewBlockReason.MissingRequiredValidationFamily);
            issues.AddRange(missingFamilies.Select(family => $"MissingRequiredValidationFamily:{family}"));
        }

        var phaseValid = ValidatePhaseReceipt(input.PhaseAuthorityReceiptText, incomplete, blocked, issues);
        ValidateBoundary(blocked, issues);
        var blockReasons = rejected.Concat(blocked).Concat(incomplete).Distinct().ToArray();
        var verdict = DetermineVerdict(rejected, blocked, incomplete);
        var canMarkReady = verdict == ReadyForReviewEligibilityVerdict.EligibleForReadyExecutor;
        var packageId = $"ready_review_pkg_{AtReadyForReviewHashing.ShortHash($"{target.Repository}|{target.PullRequestNumber}|{target.ExpectedHeadSha}|{verdict}|{string.Join(",", blockReasons)}")}";
        var package = new ReadyForReviewEligibilityPackage
        {
            ReadyForReviewPackageId = packageId,
            Target = target,
            BranchUpdateEvidence = branchEvidence,
            ValidationEvidence = validationEvidence,
            RequiredValidationFamilies = MinimumValidationFamilies,
            MissingValidationFamilies = missingFamilies,
            PhaseAuthorityReceiptId = FeedbackText.Safe(input.PhaseAuthorityReceiptId ?? "missing-phase-authority-receipt"),
            PhaseAuthorityReceiptValid = phaseValid,
            Verdict = verdict,
            CanMarkReadyForReview = canMarkReady,
            BlockReasons = blockReasons,
            PackageIssues = FeedbackText.SafeList(issues),
            CreatedAtUtc = now,
            CreatedBy = FeedbackText.Safe(input.PackageCreatedBy ?? "unknown"),
            Boundary = ReadyForReviewBoundary.Evidence
        };
        var receipt = new ReadyForReviewSeparationReceipt
        {
            ReadyForReviewReceiptId = $"ready_review_receipt_{AtReadyForReviewHashing.ShortHash($"{package.ReadyForReviewPackageId}|{package.Verdict}|{package.CanMarkReadyForReview}")}",
            ReadyForReviewPackageId = package.ReadyForReviewPackageId,
            Verdict = package.Verdict,
            CanMarkReadyForReview = package.CanMarkReadyForReview,
            BlockReasons = package.BlockReasons,
            MissingValidationFamilies = package.MissingValidationFamilies.Select(item => item.ToString()).ToArray(),
            BoundaryStatements =
            [
                "AT package is eligibility evidence only.",
                "PR branch update is not ready-for-review.",
                "Ready-for-review package is not ready-for-review execution.",
                "Ready-for-review is not reviewer request.",
                "Reviewer request is not approval.",
                "Validation evidence is not approval.",
                "AT does not mark ready, request reviewers, approve, merge, release, deploy, tag, publish, promote memory, or continue workflow."
            ],
            CreatedAtUtc = now,
            Boundary = ReadyForReviewBoundary.Evidence
        };

        return new ReadyForReviewSeparationArtifacts
        {
            Package = package,
            Receipt = receipt
        };
    }

    private static ReadyForReviewTarget BuildTarget(ReadyForReviewSeparationInput input) => new()
    {
        Repository = FeedbackText.Safe(input.Repository),
        PullRequestNumber = input.PullRequestNumber,
        PullRequestUrl = FeedbackText.Safe(input.PullRequestUrl ?? $"https://github.com/{input.Repository}/pull/{input.PullRequestNumber}"),
        PullRequestState = FeedbackText.Safe(input.PullRequestState),
        PullRequestDraft = input.PullRequestDraft,
        HeadBranch = FeedbackText.Safe(input.HeadBranch),
        ExpectedHeadSha = FeedbackText.Safe(input.ExpectedHeadSha),
        ObservedHeadSha = FeedbackText.Safe(input.ObservedHeadSha ?? input.ExpectedHeadSha),
        BaseBranch = FeedbackText.Safe(input.BaseBranch),
        BaseSha = FeedbackText.Safe(input.BaseSha)
    };

    private static void ValidateTarget(
        ReadyForReviewSeparationInput input,
        ReadyForReviewTarget target,
        List<ReadyForReviewBlockReason> rejected,
        List<ReadyForReviewBlockReason> blocked,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(target.Repository) ||
            target.PullRequestNumber <= 0 ||
            string.IsNullOrWhiteSpace(target.HeadBranch) ||
            string.IsNullOrWhiteSpace(target.ExpectedHeadSha) ||
            string.IsNullOrWhiteSpace(target.BaseBranch) ||
            string.IsNullOrWhiteSpace(target.BaseSha))
        {
            rejected.Add(ReadyForReviewBlockReason.MissingPullRequestIdentity);
            issues.Add("MissingPullRequestIdentity");
        }

        if (!Same(target.PullRequestState, "open"))
        {
            blocked.Add(ReadyForReviewBlockReason.PullRequestNotOpen);
            issues.Add("PullRequestNotOpen");
        }

        if (!target.PullRequestDraft)
        {
            blocked.Add(ReadyForReviewBlockReason.PullRequestNotDraft);
            blocked.Add(ReadyForReviewBlockReason.PullRequestAlreadyReady);
            issues.Add("PullRequestNotDraft");
            issues.Add("PullRequestAlreadyReady");
        }

        if (!Same(target.ObservedHeadSha, target.ExpectedHeadSha))
        {
            blocked.Add(ReadyForReviewBlockReason.HeadShaMismatch);
            issues.Add("HeadShaMismatch");
        }

        if (!string.IsNullOrWhiteSpace(input.ExpectedBaseBranch) && !Same(input.ExpectedBaseBranch, target.BaseBranch))
        {
            blocked.Add(ReadyForReviewBlockReason.BaseBranchMismatch);
            issues.Add("BaseBranchMismatch");
        }

        if (!string.IsNullOrWhiteSpace(input.ExpectedBaseSha) && !Same(input.ExpectedBaseSha, target.BaseSha))
        {
            blocked.Add(ReadyForReviewBlockReason.BaseShaMismatch);
            issues.Add("BaseShaMismatch");
        }
    }

    private static ReadyForReviewEvidence ValidateBranchUpdateEvidence(
        ReadyForReviewSeparationInput input,
        ReadyForReviewTarget target,
        List<ReadyForReviewBlockReason> incomplete,
        List<ReadyForReviewBlockReason> blocked,
        List<string> issues)
    {
        if (input.BranchUpdateReceipt is null && input.NoBranchUpdateRequiredEvidence is null)
        {
            incomplete.Add(ReadyForReviewBlockReason.MissingBranchUpdateEvidence);
            issues.Add("MissingBranchUpdateEvidence");
            return new ReadyForReviewEvidence();
        }

        if (input.BranchUpdateReceipt is not null)
        {
            var receipt = input.BranchUpdateReceipt;
            if (receipt.ExecutionVerdict != PrBranchUpdateExecutionVerdict.Executed)
            {
                blocked.Add(ReadyForReviewBlockReason.BranchUpdateNotExecuted);
                issues.Add($"BranchUpdateNotExecuted:{receipt.ExecutionVerdict}");
            }

            if (!receipt.Pushed)
            {
                blocked.Add(ReadyForReviewBlockReason.BranchUpdateReceiptNotPushed);
                issues.Add("BranchUpdateReceiptNotPushed");
            }

            if (!Same(receipt.Repository, target.Repository) ||
                receipt.PrNumber != target.PullRequestNumber ||
                !Same(receipt.Branch, target.HeadBranch) ||
                !Same(receipt.PushBranch, target.HeadBranch))
            {
                blocked.Add(ReadyForReviewBlockReason.BranchUpdateReceiptPrMismatch);
                issues.Add("BranchUpdateReceiptPrMismatch");
            }

            if (!Same(receipt.PostExecutionHeadSha, target.ExpectedHeadSha) || !Same(receipt.CommitSha, target.ExpectedHeadSha))
            {
                blocked.Add(ReadyForReviewBlockReason.BranchUpdateReceiptHeadMismatch);
                issues.Add("BranchUpdateReceiptHeadMismatch");
            }

            return new ReadyForReviewEvidence
            {
                BranchUpdateReceiptId = receipt.ExecutionId,
                BranchUpdatePackageId = receipt.PackageId,
                BranchUpdateVerdict = receipt.ExecutionVerdict,
                BranchUpdateCommitSha = receipt.CommitSha,
                BranchUpdatePostHeadSha = receipt.PostExecutionHeadSha,
                BranchUpdatePushed = receipt.Pushed,
                EvidenceRefs = FeedbackText.SafeList([receipt.ExecutionId, receipt.PackageId]),
                Boundary = ReadyForReviewBoundary.Evidence
            };
        }

        var evidence = input.NoBranchUpdateRequiredEvidence!;
        if (!Same(evidence.Repository, target.Repository) ||
            evidence.PullRequestNumber != target.PullRequestNumber ||
            !Same(evidence.HeadBranch, target.HeadBranch))
        {
            blocked.Add(ReadyForReviewBlockReason.BranchUpdateReceiptPrMismatch);
            issues.Add("NoBranchUpdateEvidencePrMismatch");
        }

        if (!Same(evidence.ExpectedHeadSha, target.ExpectedHeadSha))
        {
            blocked.Add(ReadyForReviewBlockReason.BranchUpdateReceiptHeadMismatch);
            issues.Add("NoBranchUpdateEvidenceHeadMismatch");
        }

        if (!Same(evidence.BaseBranch, target.BaseBranch))
        {
            blocked.Add(ReadyForReviewBlockReason.BaseBranchMismatch);
            issues.Add("NoBranchUpdateEvidenceBaseBranchMismatch");
        }

        if (!Same(evidence.BaseSha, target.BaseSha))
        {
            blocked.Add(ReadyForReviewBlockReason.BaseShaMismatch);
            issues.Add("NoBranchUpdateEvidenceBaseShaMismatch");
        }

        return new ReadyForReviewEvidence
        {
            ExplicitNoBranchUpdateRequired = true,
            NoBranchUpdateEvidenceId = evidence.EvidenceId,
            EvidenceRefs = FeedbackText.SafeList([evidence.EvidenceId]),
            Boundary = ReadyForReviewBoundary.Evidence
        };
    }

    private static void ValidateValidation(
        ValidationRunReceipt[] receipts,
        ReadyForReviewValidationEvidence[] evidence,
        string expectedHeadSha,
        List<ReadyForReviewBlockReason> incomplete,
        List<ReadyForReviewBlockReason> blocked,
        List<string> issues)
    {
        if (receipts.Length == 0)
        {
            incomplete.Add(ReadyForReviewBlockReason.MissingValidationEvidence);
            issues.Add("MissingValidationEvidence");
            return;
        }

        foreach (var receipt in receipts)
        {
            if (receipt.Verdict != ValidationRunVerdict.Passed)
            {
                blocked.Add(ReadyForReviewBlockReason.ValidationReceiptNotPassed);
                issues.Add($"ValidationReceiptNotPassed:{receipt.ValidationRunId}:{receipt.Verdict}");
            }

            if (!Same(receipt.CommitSha, expectedHeadSha))
            {
                blocked.Add(ReadyForReviewBlockReason.ValidationReceiptShaMismatch);
                issues.Add($"ValidationReceiptShaMismatch:{receipt.ValidationRunId}");
            }

            var resultNames = receipt.Results.Select(result => result.LaneName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var requiredLane in receipt.RequiredLanes)
            {
                if (!resultNames.Contains(requiredLane.Name))
                {
                    incomplete.Add(ReadyForReviewBlockReason.SkippedRequiredValidationLane);
                    issues.Add($"SkippedRequiredValidationLane:{receipt.ValidationRunId}:{requiredLane.Name}");
                }
            }

            if (receipt.SkippedLanes.Length > 0)
            {
                incomplete.Add(ReadyForReviewBlockReason.SkippedRequiredValidationLane);
                issues.AddRange(receipt.SkippedLanes.Select(lane => $"SkippedRequiredValidationLane:{receipt.ValidationRunId}:{lane}"));
            }
        }
    }

    private static bool ValidatePhaseReceipt(
        string? receiptText,
        List<ReadyForReviewBlockReason> incomplete,
        List<ReadyForReviewBlockReason> blocked,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(receiptText))
        {
            incomplete.Add(ReadyForReviewBlockReason.MissingPhaseAuthorityReceipt);
            issues.Add("MissingPhaseAuthorityReceipt");
            return false;
        }

        var hasPhaseBoundary = receiptText.Contains("Phase 1 closes the feedback loop", StringComparison.OrdinalIgnoreCase) ||
                               receiptText.Contains("PR branch update is not ready-for-review.", StringComparison.OrdinalIgnoreCase);
        var hasValidationBoundary = receiptText.Contains("Validation evidence is not approval.", StringComparison.OrdinalIgnoreCase);
        if (!hasPhaseBoundary || !hasValidationBoundary)
        {
            blocked.Add(ReadyForReviewBlockReason.PhaseAuthorityReceiptInvalid);
            issues.Add("PhaseAuthorityReceiptInvalid");
            return false;
        }

        return true;
    }

    private static void ValidateBoundary(List<ReadyForReviewBlockReason> blocked, List<string> issues)
    {
        var boundary = ReadyForReviewBoundary.Evidence;
        if (boundary.CanMarkReadyForReview)
        {
            blocked.Add(ReadyForReviewBlockReason.ReadyForReviewMutationNotAllowed);
            issues.Add("ReadyForReviewMutationNotAllowed");
        }

        if (boundary.CanRequestReviewers || boundary.CanResolveReviewThreads)
        {
            blocked.Add(ReadyForReviewBlockReason.ReviewerRequestNotAllowed);
            issues.Add("ReviewerRequestNotAllowed");
        }

        if (boundary.CanMerge || boundary.CanAutoMerge || boundary.CanRelease || boundary.CanDeploy || boundary.CanTag || boundary.CanPublish || boundary.CanContinueWorkflow)
        {
            blocked.Add(ReadyForReviewBlockReason.MergeReleaseDeployNotAllowed);
            issues.Add("MergeReleaseDeployNotAllowed");
        }
    }

    private static ReadyForReviewValidationEvidence BuildValidationEvidence(ValidationRunReceipt receipt)
    {
        var laneNames = receipt.Results.Select(result => result.LaneName)
            .Concat(receipt.RequiredLanes.Select(lane => lane.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ReadyForReviewValidationEvidence
        {
            ValidationRunId = receipt.ValidationRunId,
            ValidationPlanId = receipt.ValidationPlanId,
            CommitSha = receipt.CommitSha,
            Verdict = receipt.Verdict,
            RequiredLaneNames = receipt.RequiredLanes.Select(lane => lane.Name).ToArray(),
            ResultLaneNames = receipt.Results.Select(result => result.LaneName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SkippedLanes = receipt.SkippedLanes,
            SatisfiedFamilies = receipt.Verdict == ValidationRunVerdict.Passed ? SatisfiedFamilies(laneNames, receipt.Results) : [],
            Boundary = ReadyForReviewBoundary.Evidence
        };
    }

    private static ReadyForReviewValidationFamily[] MissingValidationFamilies(ReadyForReviewValidationEvidence[] evidence)
    {
        var satisfied = evidence.SelectMany(item => item.SatisfiedFamilies).Distinct().ToHashSet();
        return MinimumValidationFamilies.Where(family => !satisfied.Contains(family)).ToArray();
    }

    private static ReadyForReviewValidationFamily[] SatisfiedFamilies(string[] laneNames, ValidationProcessResult[] results)
    {
        var families = new List<ReadyForReviewValidationFamily>();
        if (laneNames.Any(name => name.Contains("phase-authority", StringComparison.OrdinalIgnoreCase) || name.Contains("phase1-authority", StringComparison.OrdinalIgnoreCase)))
            families.Add(ReadyForReviewValidationFamily.PhaseAuthority);
        if (laneNames.Any(name => name.StartsWith("focused-", StringComparison.OrdinalIgnoreCase) || Same(name, "phase-gate")))
            families.Add(ReadyForReviewValidationFamily.FocusedCurrentBlock);
        if (laneNames.Any(name => name.Contains("impacted", StringComparison.OrdinalIgnoreCase) || name.Contains("governance", StringComparison.OrdinalIgnoreCase) || Same(name, "cli-command-surface")))
            families.Add(ReadyForReviewValidationFamily.ImpactedArea);
        if (laneNames.Any(name => name.Contains("authority", StringComparison.OrdinalIgnoreCase)))
            families.Add(ReadyForReviewValidationFamily.FastAuthorityInvariant);
        if (laneNames.Any(name => Same(name, "build")) || results.Any(result => result.Command.Contains("build", StringComparison.OrdinalIgnoreCase)))
            families.Add(ReadyForReviewValidationFamily.Build);
        if (laneNames.Any(name => Same(name, "diff-check")) || results.Any(result => result.Command.Contains("diff", StringComparison.OrdinalIgnoreCase)))
            families.Add(ReadyForReviewValidationFamily.DiffCheck);
        return families.Distinct().ToArray();
    }

    private static ReadyForReviewEligibilityVerdict DetermineVerdict(
        List<ReadyForReviewBlockReason> rejected,
        List<ReadyForReviewBlockReason> blocked,
        List<ReadyForReviewBlockReason> incomplete)
    {
        if (rejected.Count > 0)
            return ReadyForReviewEligibilityVerdict.Rejected;
        if (blocked.Count > 0)
            return ReadyForReviewEligibilityVerdict.Blocked;
        if (incomplete.Count > 0)
            return ReadyForReviewEligibilityVerdict.Incomplete;
        return ReadyForReviewEligibilityVerdict.EligibleForReadyExecutor;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class ReadyForReviewBypassEvaluator
{
    public static bool CanMarkReadyForReview(object? evidence) => false;
    public static bool CanRequestReviewers(object? evidence) => false;
    public static bool CanResolveReviewThreads(object? evidence) => false;
    public static bool CanApprove(object? evidence) => false;
    public static bool CanMerge(object? evidence) => false;
    public static bool CanRelease(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanTag(object? evidence) => false;
    public static bool CanPublish(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
}

internal static class AtReadyForReviewHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
