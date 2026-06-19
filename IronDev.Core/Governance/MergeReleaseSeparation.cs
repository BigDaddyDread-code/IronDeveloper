using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum MergeReadinessOutcome
{
    ReadyForMergeDecision = 0,
    NeedsMoreMergeEvidence,
    BlockedByFeedback,
    BlockedByCi,
    BlockedByUnsafeMaterial,
    BlockedByArtifactMismatch,
    HeadChanged
}

public enum ReleaseReadinessEvidenceOutcome
{
    ReadyForReleaseDecision = 0,
    NeedsMoreReleaseEvidence,
    BlockedByProductRisk,
    BlockedByUnsafeMaterial,
    BlockedByArtifactMismatch,
    BlockedByMissingRecoveryEvidence,
    NotApplicableBeforeMerge
}

public enum MergeSeparationReadinessOutcome
{
    MergeDecisionCandidate = 0,
    NeedsMergeEvidence,
    MergeBlocked,
    HeadChanged
}

public enum ReleaseSeparationReadinessOutcome
{
    ReleaseDecisionCandidate = 0,
    NeedsReleaseEvidence,
    ReleaseBlocked,
    NotApplicableBeforeMerge
}

public sealed record MergeReleaseSeparationBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanTag { get; init; }
    public bool CanPublish { get; init; }
    public bool CanUpdatePullRequest { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanRerunCi { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanContinueWorkflow { get; init; }

    public static MergeReleaseSeparationBoundary Evidence { get; } = new();
}

public static class MergeReleaseSeparationBoundaryText
{
    public const string Boundary = """
        Block AO separates merge readiness from release readiness.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not tag.
        It does not publish.
        It does not continue workflow.
        CI pass is not merge permission.
        Review approval is not merge permission.
        A draft pull request is not merge readiness.
        Merge readiness is not release readiness.
        A merged pull request is not release candidate evidence.
        Release readiness is not release execution.
        """;
}

public sealed record MergeReleaseSeparationRequestInput
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string PullRequestCreationReceiptId { get; init; }
    public required string FeedbackReadinessReportId { get; init; }
    public required string RequestedBy { get; init; }
    public required string Reason { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public DateTimeOffset? RequestedAtUtc { get; init; }
}

public sealed record MergeReleaseSeparationRequest
{
    public required string MergeReleaseSeparationRequestId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string PullRequestCreationReceiptId { get; init; }
    public required string FeedbackReadinessReportId { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required string Reason { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public MergeReleaseSeparationBoundary Boundary { get; init; } = MergeReleaseSeparationBoundary.Evidence;
}

public static class MergeReleaseSeparationRequestWriter
{
    public static MergeReleaseSeparationRequest Create(MergeReleaseSeparationRequestInput input) => new()
    {
        MergeReleaseSeparationRequestId = $"merge_release_req_{MergeReleaseHashing.ShortHash($"{input.RunId}|{input.RepositoryFullName}|{input.PullRequestNumber}|{input.ExpectedHeadSha}")}",
        RunId = MergeReleaseText.Safe(input.RunId),
        ProjectId = MergeReleaseText.Safe(input.ProjectId),
        RepositoryFullName = MergeReleaseText.Safe(input.RepositoryFullName),
        PullRequestNumber = input.PullRequestNumber,
        PullRequestUrl = MergeReleaseText.Safe(input.PullRequestUrl),
        BaseBranch = MergeReleaseText.Safe(input.BaseBranch),
        HeadBranch = MergeReleaseText.Safe(input.HeadBranch),
        ExpectedHeadSha = MergeReleaseText.Safe(input.ExpectedHeadSha),
        PullRequestCreationReceiptId = MergeReleaseText.Safe(input.PullRequestCreationReceiptId),
        FeedbackReadinessReportId = MergeReleaseText.Safe(input.FeedbackReadinessReportId),
        RequestedBy = MergeReleaseText.Safe(input.RequestedBy),
        RequestedAtUtc = input.RequestedAtUtc ?? DateTimeOffset.UtcNow,
        Reason = MergeReleaseText.Safe(input.Reason),
        EvidenceRefs = MergeReleaseText.SafeList(input.EvidenceRefs),
        Boundary = MergeReleaseSeparationBoundary.Evidence
    };
}

public sealed record MergeReadinessEvidenceInput
{
    public required MergeReleaseSeparationRequest Request { get; init; }
    public bool PullRequestReceiptExists { get; init; }
    public bool PullRequestStatusExists { get; init; }
    public string? ObservedHeadSha { get; init; }
    public bool PullRequestDraft { get; init; }
    public bool CommitReadinessReviewExists { get; init; }
    public CommitReadinessDecision? CommitReadinessDecision { get; init; }
    public bool CiObservationExists { get; init; }
    public FeedbackCiState? CiState { get; init; }
    public bool ReviewFeedbackSnapshotExists { get; init; }
    public int RequestedChangeCount { get; init; }
    public bool FeedbackReadinessReportExists { get; init; }
    public FeedbackReadinessOutcome? FeedbackReadinessOutcome { get; init; }
    public bool ArtifactConsistencyReportExists { get; init; }
    public int ArtifactConsistencyBlockers { get; init; }
    public bool UnsafeMaterialReportExists { get; init; }
    public int UnsafeMaterialFindings { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record MergeReadinessEvidencePackage
{
    public required string MergeReadinessEvidencePackageId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string? ObservedHeadSha { get; init; }
    public required MergeReadinessOutcome Outcome { get; init; }
    public string[] MergeEvidenceRefs { get; init; } = [];
    public string[] MergeBlockers { get; init; } = [];
    public string[] MergeEvidenceGaps { get; init; } = [];
    public string[] KnownRisks { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MergeReleaseSeparationBoundary Boundary { get; init; } = MergeReleaseSeparationBoundary.Evidence;
}

public static class MergeReadinessEvidencePackager
{
    public static MergeReadinessEvidencePackage Build(MergeReadinessEvidenceInput input)
    {
        var blockers = new List<string>();
        var gaps = new List<string>();
        var risks = new List<string>
        {
            "ReadyForMergeDecision is evidence for a future decision process, not permission to merge."
        };

        if (!input.PullRequestReceiptExists) gaps.Add("MissingPullRequestCreationReceipt");
        if (!input.PullRequestStatusExists) gaps.Add("MissingPullRequestStatusReport");
        if (!input.CommitReadinessReviewExists) gaps.Add("MissingCommitReadinessReview");
        if (!input.CiObservationExists) gaps.Add("MissingCiObservationSnapshot");
        if (!input.ReviewFeedbackSnapshotExists) gaps.Add("MissingReviewFeedbackSnapshot");
        if (!input.FeedbackReadinessReportExists) gaps.Add("MissingFeedbackReadinessReport");
        if (!input.ArtifactConsistencyReportExists) gaps.Add("MissingArtifactConsistencyReport");
        if (!input.UnsafeMaterialReportExists) gaps.Add("MissingUnsafeMaterialReport");
        if (input.PullRequestStatusExists && input.PullRequestDraft) gaps.Add("DraftPullRequestRequiresReadyForReviewEvidence");
        if (input.CommitReadinessReviewExists && input.CommitReadinessDecision != CommitReadinessDecision.ReadyForHumanCommitReview)
            gaps.Add($"CommitReadinessNotReady:{input.CommitReadinessDecision}");
        if (input.CiObservationExists && input.CiState is FeedbackCiState.Pending or FeedbackCiState.Missing or FeedbackCiState.Unknown)
            gaps.Add($"CiEvidenceNotComplete:{input.CiState}");

        if (!string.IsNullOrWhiteSpace(input.ObservedHeadSha) &&
            !string.Equals(input.ObservedHeadSha, input.Request.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            blockers.Add("PullRequestHeadChanged");
        if (input.UnsafeMaterialFindings > 0) blockers.Add("UnsafeMaterialFindingsBlockMergeDecision");
        if (input.ArtifactConsistencyBlockers > 0) blockers.Add("ArtifactConsistencyBlockersBlockMergeDecision");
        if (input.CiState is FeedbackCiState.Failed or FeedbackCiState.Cancelled or FeedbackCiState.Stale)
            blockers.Add($"CiStateBlocksMergeDecision:{input.CiState}");
        if (input.RequestedChangeCount > 0)
            blockers.Add("ReviewRequestedChangesBlockMergeDecision");
        if (input.FeedbackReadinessOutcome is FeedbackReadinessOutcome.NeedsGovernedPatchRun or FeedbackReadinessOutcome.NeedsHumanTriage)
            blockers.Add($"FeedbackReadinessBlocksMergeDecision:{input.FeedbackReadinessOutcome}");

        var outcome = blockers.Any(item => item.Contains("HeadChanged", StringComparison.OrdinalIgnoreCase))
            ? MergeReadinessOutcome.HeadChanged
            : blockers.Any(item => item.Contains("UnsafeMaterial", StringComparison.OrdinalIgnoreCase))
                ? MergeReadinessOutcome.BlockedByUnsafeMaterial
                : blockers.Any(item => item.Contains("ArtifactConsistency", StringComparison.OrdinalIgnoreCase))
                    ? MergeReadinessOutcome.BlockedByArtifactMismatch
                    : blockers.Any(item => item.StartsWith("CiState", StringComparison.OrdinalIgnoreCase))
                        ? MergeReadinessOutcome.BlockedByCi
                        : blockers.Count > 0
                            ? MergeReadinessOutcome.BlockedByFeedback
                            : gaps.Count > 0
                                ? MergeReadinessOutcome.NeedsMoreMergeEvidence
                                : MergeReadinessOutcome.ReadyForMergeDecision;

        return new MergeReadinessEvidencePackage
        {
            MergeReadinessEvidencePackageId = $"merge_ready_{MergeReleaseHashing.ShortHash($"{input.Request.MergeReleaseSeparationRequestId}|{outcome}|{string.Join("|", blockers)}|{string.Join("|", gaps)}")}",
            RunId = input.Request.RunId,
            RepositoryFullName = input.Request.RepositoryFullName,
            PullRequestNumber = input.Request.PullRequestNumber,
            ExpectedHeadSha = input.Request.ExpectedHeadSha,
            ObservedHeadSha = MergeReleaseText.SafeOrNull(input.ObservedHeadSha),
            Outcome = outcome,
            MergeEvidenceRefs = MergeReleaseText.SafeList(input.EvidenceRefs),
            MergeBlockers = MergeReleaseText.SafeList(blockers),
            MergeEvidenceGaps = MergeReleaseText.SafeList(gaps),
            KnownRisks = MergeReleaseText.SafeList(risks),
            CreatedAtUtc = input.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = MergeReleaseSeparationBoundary.Evidence
        };
    }
}

public sealed record ReleaseReadinessEvidenceInput
{
    public required MergeReleaseSeparationRequest Request { get; init; }
    public bool PullRequestStatusExists { get; init; }
    public bool PullRequestMerged { get; init; }
    public string? ReleaseCandidateRef { get; init; }
    public bool ProductHardeningEvidenceExists { get; init; }
    public bool ProductHardeningPassed { get; init; }
    public bool ReleaseReadinessReportExists { get; init; }
    public string? ReleaseReadinessReportOutcome { get; init; }
    public bool ReleaseReadinessDecisionRecordExists { get; init; }
    public bool ArtifactConsistencyReportExists { get; init; }
    public int ArtifactConsistencyBlockers { get; init; }
    public bool UnsafeMaterialReportExists { get; init; }
    public int UnsafeMaterialFindings { get; init; }
    public bool KnownRisksDocumented { get; init; }
    public bool RecoveryEvidenceExists { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record ReleaseReadinessEvidencePackage
{
    public required string ReleaseReadinessEvidencePackageId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string? ReleaseCandidateRef { get; init; }
    public required ReleaseReadinessEvidenceOutcome Outcome { get; init; }
    public string[] ReleaseEvidenceRefs { get; init; } = [];
    public string[] ReleaseBlockers { get; init; } = [];
    public string[] ReleaseEvidenceGaps { get; init; } = [];
    public string[] KnownRisks { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MergeReleaseSeparationBoundary Boundary { get; init; } = MergeReleaseSeparationBoundary.Evidence;
}

public static class ReleaseReadinessEvidencePackager
{
    public static ReleaseReadinessEvidencePackage Build(ReleaseReadinessEvidenceInput input)
    {
        var blockers = new List<string>();
        var gaps = new List<string>();
        var risks = new List<string>
        {
            "ReadyForReleaseDecision is evidence for a future release decision process, not release execution."
        };

        if (!input.PullRequestStatusExists) gaps.Add("MissingPullRequestStatusReport");
        if (!input.ProductHardeningEvidenceExists) gaps.Add("MissingProductHardeningEvidence");
        if (!input.ReleaseReadinessReportExists) gaps.Add("MissingReleaseReadinessReport");
        if (!input.ReleaseReadinessDecisionRecordExists) gaps.Add("MissingReleaseReadinessDecisionRecord");
        if (!input.ArtifactConsistencyReportExists) gaps.Add("MissingArtifactConsistencyReport");
        if (!input.UnsafeMaterialReportExists) gaps.Add("MissingUnsafeMaterialReport");
        if (!input.KnownRisksDocumented) gaps.Add("MissingKnownRisks");
        if (!input.RecoveryEvidenceExists) gaps.Add("MissingRecoveryEvidence");
        if (input.PullRequestMerged && string.IsNullOrWhiteSpace(input.ReleaseCandidateRef)) gaps.Add("MissingReleaseCandidateRef");
        if (input.PullRequestMerged && IsPullRequestUrl(input.ReleaseCandidateRef)) gaps.Add("InvalidReleaseCandidateRef:PullRequestUrl");
        if (input.ProductHardeningEvidenceExists && !input.ProductHardeningPassed) blockers.Add("ProductHardeningEvidenceBlocksReleaseDecision");
        if (input.ReleaseReadinessReportExists && IsNotReady(input.ReleaseReadinessReportOutcome)) blockers.Add($"ReleaseReadinessReportBlocksReleaseDecision:{input.ReleaseReadinessReportOutcome}");
        if (input.UnsafeMaterialFindings > 0) blockers.Add("UnsafeMaterialFindingsBlockReleaseDecision");
        if (input.ArtifactConsistencyBlockers > 0) blockers.Add("ArtifactConsistencyBlockersBlockReleaseDecision");

        var outcome = input.PullRequestStatusExists && !input.PullRequestMerged
            ? ReleaseReadinessEvidenceOutcome.NotApplicableBeforeMerge
            : blockers.Any(item => item.Contains("UnsafeMaterial", StringComparison.OrdinalIgnoreCase))
                ? ReleaseReadinessEvidenceOutcome.BlockedByUnsafeMaterial
                : blockers.Any(item => item.Contains("ArtifactConsistency", StringComparison.OrdinalIgnoreCase))
                    ? ReleaseReadinessEvidenceOutcome.BlockedByArtifactMismatch
                    : blockers.Count > 0
                        ? ReleaseReadinessEvidenceOutcome.BlockedByProductRisk
                        : gaps.Any(item => item.Contains("Recovery", StringComparison.OrdinalIgnoreCase))
                            ? ReleaseReadinessEvidenceOutcome.BlockedByMissingRecoveryEvidence
                            : gaps.Count > 0 || IsNeedsMoreEvidence(input.ReleaseReadinessReportOutcome)
                                ? ReleaseReadinessEvidenceOutcome.NeedsMoreReleaseEvidence
                                : ReleaseReadinessEvidenceOutcome.ReadyForReleaseDecision;

        return new ReleaseReadinessEvidencePackage
        {
            ReleaseReadinessEvidencePackageId = $"release_ready_{MergeReleaseHashing.ShortHash($"{input.Request.MergeReleaseSeparationRequestId}|{outcome}|{string.Join("|", blockers)}|{string.Join("|", gaps)}")}",
            RunId = input.Request.RunId,
            RepositoryFullName = input.Request.RepositoryFullName,
            PullRequestNumber = input.Request.PullRequestNumber,
            ExpectedHeadSha = input.Request.ExpectedHeadSha,
            ReleaseCandidateRef = MergeReleaseText.SafeOrNull(input.ReleaseCandidateRef),
            Outcome = outcome,
            ReleaseEvidenceRefs = MergeReleaseText.SafeList(input.EvidenceRefs),
            ReleaseBlockers = MergeReleaseText.SafeList(blockers),
            ReleaseEvidenceGaps = MergeReleaseText.SafeList(gaps),
            KnownRisks = MergeReleaseText.SafeList(risks),
            CreatedAtUtc = input.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = MergeReleaseSeparationBoundary.Evidence
        };
    }

    private static bool IsNotReady(string? outcome) =>
        !string.IsNullOrWhiteSpace(outcome) &&
        (outcome.Contains("NotReady", StringComparison.OrdinalIgnoreCase) ||
         outcome.Contains("Blocked", StringComparison.OrdinalIgnoreCase) ||
         outcome.Contains("Failed", StringComparison.OrdinalIgnoreCase));

    private static bool IsNeedsMoreEvidence(string? outcome) =>
        !string.IsNullOrWhiteSpace(outcome) && outcome.Contains("NeedsMoreEvidence", StringComparison.OrdinalIgnoreCase);

    private static bool IsPullRequestUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains("/pull/", StringComparison.OrdinalIgnoreCase);
}

public sealed record MergeReleaseBoundaryMap
{
    public required string MergeReleaseBoundaryMapId { get; init; }
    public required string RunId { get; init; }
    public string[] MergeEvidenceRefs { get; init; } = [];
    public string[] ReleaseEvidenceRefs { get; init; } = [];
    public string[] SharedEvidenceRefs { get; init; } = [];
    public string[] ForbiddenCrossUseFindings { get; init; } = [];
    public string[] Warnings { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MergeReleaseSeparationBoundary Boundary { get; init; } = MergeReleaseSeparationBoundary.Evidence;
}

public static class MergeReleaseBoundaryMapper
{
    public static MergeReleaseBoundaryMap Build(
        string runId,
        IEnumerable<string> availableArtifacts,
        IEnumerable<string>? claimedReleaseEvidence = null,
        IEnumerable<string>? claimedMergeEvidence = null,
        DateTimeOffset? now = null)
    {
        var artifacts = MergeReleaseText.SafeList(availableArtifacts);
        var merge = artifacts.Where(IsMergeEvidence).Except(artifacts.Where(IsSharedEvidence), StringComparer.OrdinalIgnoreCase).ToArray();
        var release = artifacts.Where(IsReleaseEvidence).Except(artifacts.Where(IsSharedEvidence), StringComparer.OrdinalIgnoreCase).ToArray();
        var shared = artifacts.Where(IsSharedEvidence).ToArray();
        var findings = new List<string>();
        foreach (var evidence in MergeReleaseText.SafeList(claimedReleaseEvidence))
        {
            if (IsForbiddenReleaseClaim(evidence))
                findings.Add($"ForbiddenReleaseEvidence:{evidence}:CI/review/merge evidence cannot satisfy release readiness.");
        }

        foreach (var evidence in MergeReleaseText.SafeList(claimedMergeEvidence))
        {
            if (IsForbiddenMergeClaim(evidence))
                findings.Add($"ForbiddenMergeEvidence:{evidence}:release evidence cannot satisfy merge readiness.");
        }

        return new MergeReleaseBoundaryMap
        {
            MergeReleaseBoundaryMapId = $"merge_release_map_{MergeReleaseHashing.ShortHash($"{runId}|{string.Join("|", artifacts)}|{string.Join("|", findings)}")}",
            RunId = MergeReleaseText.Safe(runId),
            MergeEvidenceRefs = MergeReleaseText.SafeList(merge),
            ReleaseEvidenceRefs = MergeReleaseText.SafeList(release),
            SharedEvidenceRefs = MergeReleaseText.SafeList(shared),
            ForbiddenCrossUseFindings = findings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings =
            [
                "CI pass cannot be release evidence by itself.",
                "Review approval cannot be release evidence.",
                "Merge readiness cannot be release readiness.",
                "Release readiness cannot be merge readiness.",
                "No known blocking feedback cannot be release readiness."
            ],
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = MergeReleaseSeparationBoundary.Evidence
        };
    }

    private static bool IsMergeEvidence(string value) =>
        Contains(value, "commit-") ||
        Contains(value, "pull-request-") ||
        Contains(value, "ci-observation") ||
        Contains(value, "review-feedback") ||
        Contains(value, "feedback-");

    private static bool IsReleaseEvidence(string value) =>
        Contains(value, "product-hardening") ||
        Contains(value, "dogfood-") ||
        Contains(value, "release-readiness") ||
        Contains(value, "release-candidate") ||
        Contains(value, "merge-commit") ||
        Contains(value, "build-artifact") ||
        Contains(value, "package-hash") ||
        Contains(value, "known-risks") ||
        Contains(value, "resume-report") ||
        Contains(value, "recovery") ||
        Contains(value, "rollback");

    private static bool IsSharedEvidence(string value) =>
        Contains(value, "artifact-consistency") ||
        Contains(value, "unsafe-material");

    private static bool IsForbiddenReleaseClaim(string value) =>
        Contains(value, "ci") ||
        Contains(value, "review approval") ||
        Contains(value, "review-feedback") ||
        Contains(value, "feedback-readiness") ||
        Contains(value, "merge-readiness") ||
        Contains(value, "pull-request") ||
        Contains(value, "no known blocking");

    private static bool IsForbiddenMergeClaim(string value) =>
        Contains(value, "release-readiness") ||
        Contains(value, "release readiness") ||
        Contains(value, "release evidence");

    private static bool Contains(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);
}

public sealed record MergeSeparationReadinessRecord
{
    public required string RecordId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string RecordKind { get; init; } = "Merge";
    public required MergeSeparationReadinessOutcome Outcome { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public string[] Blockers { get; init; } = [];
    public string[] Gaps { get; init; } = [];
    public required string BoundaryMapId { get; init; }
    public required string ReviewedBy { get; init; }
    public required DateTimeOffset ReviewedAtUtc { get; init; }
    public MergeReleaseSeparationBoundary Boundary { get; init; } = MergeReleaseSeparationBoundary.Evidence;
}

public sealed record ReleaseSeparationReadinessRecord
{
    public required string RecordId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string RecordKind { get; init; } = "Release";
    public required ReleaseSeparationReadinessOutcome Outcome { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public string[] Blockers { get; init; } = [];
    public string[] Gaps { get; init; } = [];
    public required string BoundaryMapId { get; init; }
    public required string ReviewedBy { get; init; }
    public required DateTimeOffset ReviewedAtUtc { get; init; }
    public MergeReleaseSeparationBoundary Boundary { get; init; } = MergeReleaseSeparationBoundary.Evidence;
}

public sealed record MergeReleaseSeparationReport
{
    public required string MergeReleaseSeparationReportId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required MergeSeparationReadinessOutcome MergeOutcome { get; init; }
    public required ReleaseSeparationReadinessOutcome ReleaseOutcome { get; init; }
    public required string BoundaryMapId { get; init; }
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MergeReleaseSeparationBoundary Boundary { get; init; } = MergeReleaseSeparationBoundary.Evidence;
}

public sealed record MergeReleaseSeparationRecords
{
    public required MergeSeparationReadinessRecord MergeRecord { get; init; }
    public required ReleaseSeparationReadinessRecord ReleaseRecord { get; init; }
    public required MergeReleaseSeparationReport CombinedReport { get; init; }
}

public static class MergeReleaseSeparationRecordBuilder
{
    public static MergeReleaseSeparationRecords Build(
        MergeReleaseSeparationRequest request,
        MergeReadinessEvidencePackage merge,
        ReleaseReadinessEvidencePackage release,
        MergeReleaseBoundaryMap map,
        string reviewedBy,
        DateTimeOffset? now = null)
    {
        var reviewedAt = now ?? DateTimeOffset.UtcNow;
        var mergeOutcome = ToMergeRecordOutcome(merge.Outcome);
        var releaseOutcome = ToReleaseRecordOutcome(release.Outcome);
        var mergeRecord = new MergeSeparationReadinessRecord
        {
            RecordId = $"merge_sep_{MergeReleaseHashing.ShortHash($"{merge.MergeReadinessEvidencePackageId}|{mergeOutcome}|{reviewedBy}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            PullRequestNumber = request.PullRequestNumber,
            ExpectedHeadSha = request.ExpectedHeadSha,
            Outcome = mergeOutcome,
            EvidenceRefs = MergeReleaseText.SafeList([merge.MergeReadinessEvidencePackageId, .. merge.MergeEvidenceRefs]),
            Blockers = merge.MergeBlockers,
            Gaps = merge.MergeEvidenceGaps,
            BoundaryMapId = map.MergeReleaseBoundaryMapId,
            ReviewedBy = MergeReleaseText.Safe(reviewedBy),
            ReviewedAtUtc = reviewedAt,
            Boundary = MergeReleaseSeparationBoundary.Evidence
        };
        var releaseRecord = new ReleaseSeparationReadinessRecord
        {
            RecordId = $"release_sep_{MergeReleaseHashing.ShortHash($"{release.ReleaseReadinessEvidencePackageId}|{releaseOutcome}|{reviewedBy}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            PullRequestNumber = request.PullRequestNumber,
            ExpectedHeadSha = request.ExpectedHeadSha,
            Outcome = releaseOutcome,
            EvidenceRefs = MergeReleaseText.SafeList([release.ReleaseReadinessEvidencePackageId, .. release.ReleaseEvidenceRefs]),
            Blockers = release.ReleaseBlockers,
            Gaps = release.ReleaseEvidenceGaps,
            BoundaryMapId = map.MergeReleaseBoundaryMapId,
            ReviewedBy = MergeReleaseText.Safe(reviewedBy),
            ReviewedAtUtc = reviewedAt,
            Boundary = MergeReleaseSeparationBoundary.Evidence
        };
        var report = new MergeReleaseSeparationReport
        {
            MergeReleaseSeparationReportId = $"merge_release_report_{MergeReleaseHashing.ShortHash($"{request.MergeReleaseSeparationRequestId}|{mergeRecord.RecordId}|{releaseRecord.RecordId}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            PullRequestNumber = request.PullRequestNumber,
            ExpectedHeadSha = request.ExpectedHeadSha,
            MergeOutcome = mergeOutcome,
            ReleaseOutcome = releaseOutcome,
            BoundaryMapId = map.MergeReleaseBoundaryMapId,
            BoundaryStatements =
            [
                "Merge-readiness and release-readiness are separate.",
                "This report does not merge.",
                "This report does not release.",
                "This report does not deploy.",
                "This report does not continue workflow."
            ],
            CreatedAtUtc = reviewedAt,
            Boundary = MergeReleaseSeparationBoundary.Evidence
        };
        return new MergeReleaseSeparationRecords
        {
            MergeRecord = mergeRecord,
            ReleaseRecord = releaseRecord,
            CombinedReport = report
        };
    }

    private static MergeSeparationReadinessOutcome ToMergeRecordOutcome(MergeReadinessOutcome outcome) => outcome switch
    {
        MergeReadinessOutcome.ReadyForMergeDecision => MergeSeparationReadinessOutcome.MergeDecisionCandidate,
        MergeReadinessOutcome.HeadChanged => MergeSeparationReadinessOutcome.HeadChanged,
        MergeReadinessOutcome.NeedsMoreMergeEvidence => MergeSeparationReadinessOutcome.NeedsMergeEvidence,
        _ => MergeSeparationReadinessOutcome.MergeBlocked
    };

    private static ReleaseSeparationReadinessOutcome ToReleaseRecordOutcome(ReleaseReadinessEvidenceOutcome outcome) => outcome switch
    {
        ReleaseReadinessEvidenceOutcome.ReadyForReleaseDecision => ReleaseSeparationReadinessOutcome.ReleaseDecisionCandidate,
        ReleaseReadinessEvidenceOutcome.NotApplicableBeforeMerge => ReleaseSeparationReadinessOutcome.NotApplicableBeforeMerge,
        ReleaseReadinessEvidenceOutcome.NeedsMoreReleaseEvidence => ReleaseSeparationReadinessOutcome.NeedsReleaseEvidence,
        _ => ReleaseSeparationReadinessOutcome.ReleaseBlocked
    };
}

public sealed record MergeReleaseBypassReport
{
    public required string MergeReleaseBypassReportId { get; init; }
    public required string RunId { get; init; }
    public string[] EvidenceSubjects { get; init; } = [];
    public bool Merged { get; init; }
    public bool Released { get; init; }
    public bool Deployed { get; init; }
    public bool Tagged { get; init; }
    public bool Published { get; init; }
    public bool WorkflowContinued { get; init; }
    public bool PullRequestUpdated { get; init; }
    public bool CommitCreated { get; init; }
    public bool PushPerformed { get; init; }
    public bool PolicySatisfied { get; init; }
    public MergeReleaseSeparationBoundary Boundary { get; init; } = MergeReleaseSeparationBoundary.Evidence;
}

public static class MergeReleaseBypassEvaluator
{
    public static MergeReleaseBypassReport Evaluate(string runId, IEnumerable<string> evidenceSubjects) => new()
    {
        MergeReleaseBypassReportId = $"merge_release_bypass_{MergeReleaseHashing.ShortHash(runId)}",
        RunId = MergeReleaseText.Safe(runId),
        EvidenceSubjects = MergeReleaseText.SafeList(evidenceSubjects),
        Boundary = MergeReleaseSeparationBoundary.Evidence
    };

    public static bool CanMerge(object? evidence) => false;
    public static bool CanRelease(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanTag(object? evidence) => false;
    public static bool CanPublish(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
}

public static class MergeReleaseText
{
    public static string Safe(string? value) => (value ?? string.Empty).Trim();

    public static string? SafeOrNull(string? value)
    {
        var safe = Safe(value);
        return safe.Length == 0 ? null : safe;
    }

    public static string[] SafeList(IEnumerable<string>? values) => values?
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item.Trim().Replace('\\', '/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];
}

internal static class MergeReleaseHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
