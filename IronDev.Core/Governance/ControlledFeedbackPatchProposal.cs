using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum FeedbackPatchProposalVerdict
{
    ProposalCreated = 0,
    Rejected,
    Incomplete
}

public enum FeedbackPatchChangeKind
{
    Modify = 0,
    Add,
    Delete
}

public enum FeedbackPatchApplicability
{
    ManualReviewOnly = 0,
    ApplyCandidate
}

public sealed record FeedbackPatchProposalBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanApplySource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanUpdatePullRequest { get; init; }
    public bool CanApprove { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanContinueWorkflow { get; init; }

    public static FeedbackPatchProposalBoundary Evidence { get; } = new();
}

public static class FeedbackPatchProposalBoundaryText
{
    public const string Boundary = """
        Block AQ turns an AP remediation package into a bounded patch proposal.
        It does not apply source changes.
        It does not mutate workspaces.
        It does not commit.
        It does not push.
        It does not update PR branches.
        It does not approve.
        It does not mark PRs ready.
        It does not request reviewers.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not continue workflow.
        Patch proposal is not source apply.
        """;
}

public sealed record FeedbackPatchHunkProposal
{
    public required string HunkId { get; init; }
    public required string FilePath { get; init; }
    public string[] RemediationCandidateIds { get; init; } = [];
    public required string Rationale { get; init; }
    public required string TargetLineHint { get; init; }
    public required string OriginalContext { get; init; }
    public required string ProposedReplacement { get; init; }
    public required string ProposalText { get; init; }
    public FeedbackPatchApplicability PatchApplicability { get; init; } = FeedbackPatchApplicability.ManualReviewOnly;
    public FeedbackPatchProposalBoundary Boundary { get; init; } = FeedbackPatchProposalBoundary.Evidence;
}

public sealed record FeedbackPatchFileProposal
{
    public required string FilePath { get; init; }
    public required FeedbackPatchChangeKind ChangeKind { get; init; }
    public string[] RemediationCandidateIds { get; init; } = [];
    public required string Rationale { get; init; }
    public FeedbackPatchHunkProposal[] PatchHunks { get; init; } = [];
    public string[] ExpectedTests { get; init; } = [];
    public bool AuthorityRisk { get; init; }
    public FeedbackPatchProposalBoundary Boundary { get; init; } = FeedbackPatchProposalBoundary.Evidence;
}

public sealed record FeedbackPatchProposalInput
{
    public FeedbackRemediationPackage? Package { get; init; }
    public int? ExpectedPrNumber { get; init; }
    public string? ExpectedHeadSha { get; init; }
    public string? BaseSha { get; init; }
    public string[] SelectedRemediationIds { get; init; } = [];
    public bool AllowStalePackage { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record FeedbackPatchProposal
{
    public required string PatchProposalId { get; init; }
    public required string SourcePackageId { get; init; }
    public int TargetPrNumber { get; init; }
    public required string TargetHeadSha { get; init; }
    public required string BaseSha { get; init; }
    public FeedbackPatchFileProposal[] ProposedFiles { get; init; } = [];
    public FeedbackPatchHunkProposal[] ProposedHunks { get; init; } = [];
    public string[] RemediationCandidateIds { get; init; } = [];
    public string[] ExpectedChangedFiles { get; init; } = [];
    public string[] ExpectedValidationLanes { get; init; } = [];
    public required FeedbackRiskLevel RiskLevel { get; init; }
    public string[] IncompleteReasons { get; init; } = [];
    public string[] CannotApplyReasons { get; init; } = [];
    public required FeedbackPatchProposalVerdict Verdict { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public FeedbackPatchProposalBoundary Boundary { get; init; } = FeedbackPatchProposalBoundary.Evidence;
}

public sealed record FeedbackPatchProposalReceipt
{
    public required string FeedbackPatchProposalReceiptId { get; init; }
    public required string PatchProposalId { get; init; }
    public required string SourcePackageId { get; init; }
    public required FeedbackPatchProposalVerdict Verdict { get; init; }
    public int ProposedFileCount { get; init; }
    public int ProposedHunkCount { get; init; }
    public string[] CannotApplyReasons { get; init; } = [];
    public string[] IncompleteReasons { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public FeedbackPatchProposalBoundary Boundary { get; init; } = FeedbackPatchProposalBoundary.Evidence;
}

public sealed record FeedbackPatchProposalArtifacts
{
    public required FeedbackPatchProposal Proposal { get; init; }
    public required FeedbackPatchProposalReceipt Receipt { get; init; }
}

public static class FeedbackPatchProposalBuilder
{
    public static FeedbackPatchProposalArtifacts Build(FeedbackPatchProposalInput input)
    {
        var package = input.Package;
        var cannot = new List<string>();
        var incomplete = new List<string>();
        if (package is null)
            cannot.Add("MissingFeedbackRemediationPackage");

        if (package is not null && input.ExpectedPrNumber is not null && input.ExpectedPrNumber.Value != package.PullRequestNumber)
            cannot.Add("FeedbackPackagePrMismatch");
        if (package is not null && !string.IsNullOrWhiteSpace(input.ExpectedHeadSha) &&
            !string.Equals(input.ExpectedHeadSha, package.CurrentHeadSha, StringComparison.OrdinalIgnoreCase) &&
            !input.AllowStalePackage)
            cannot.Add("FeedbackPackageHeadMismatch");

        var selected = FeedbackText.SafeList(input.SelectedRemediationIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = package?.RemediationCandidates ?? [];
        if (selected.Count > 0)
            candidates = candidates.Where(item => selected.Contains(item.RemediationId)).ToArray();

        var eligible = candidates
            .Where(item => item.Disposition == FeedbackDisposition.Remediate && !item.RequiresHumanDecision)
            .ToArray();
        if (package is not null && candidates.Length == 0)
            cannot.Add("NoSelectedRemediationCandidates");
        if (package is not null && eligible.Length == 0)
            cannot.Add("NoActionableRemediationCandidates");

        foreach (var skipped in candidates.Except(eligible))
        {
            if (skipped.RequiresHumanDecision)
                incomplete.Add($"CandidateRequiresHumanDecision:{skipped.RemediationId}");
            if (skipped.Disposition != FeedbackDisposition.Remediate)
                incomplete.Add($"CandidateNotRemediate:{skipped.RemediationId}:{skipped.Disposition}");
        }

        var files = new List<FeedbackPatchFileProposal>();
        foreach (var candidate in eligible)
            files.AddRange(BuildFiles(candidate, cannot, incomplete));

        var hunks = files.SelectMany(item => item.PatchHunks).ToArray();
        if (package is not null && eligible.Length > 0 && files.Count == 0)
            cannot.Add("NoSafePatchFiles");

        var verdict = cannot.Count > 0
            ? FeedbackPatchProposalVerdict.Rejected
            : incomplete.Count > 0
                ? FeedbackPatchProposalVerdict.Incomplete
                : FeedbackPatchProposalVerdict.ProposalCreated;
        var now = input.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var proposal = new FeedbackPatchProposal
        {
            PatchProposalId = $"feedback_patch_{AqFeedbackHashing.ShortHash($"{package?.FeedbackRemediationPackageId ?? "missing"}|{verdict}|{string.Join("|", files.Select(item => item.FilePath))}")}",
            SourcePackageId = package?.FeedbackRemediationPackageId ?? "missing-feedback-remediation-package",
            TargetPrNumber = package?.PullRequestNumber ?? input.ExpectedPrNumber ?? 0,
            TargetHeadSha = package?.CurrentHeadSha ?? FeedbackText.Safe(input.ExpectedHeadSha ?? "unknown"),
            BaseSha = FeedbackText.Safe(input.BaseSha ?? package?.CurrentHeadSha ?? "unknown"),
            ProposedFiles = files.ToArray(),
            ProposedHunks = hunks,
            RemediationCandidateIds = files.SelectMany(item => item.RemediationCandidateIds).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            ExpectedChangedFiles = FeedbackText.SafeList(files.Select(item => item.FilePath)),
            ExpectedValidationLanes = FeedbackText.SafeList(eligible.SelectMany(item => item.SuggestedValidationLanes)),
            RiskLevel = HighestRisk(eligible),
            IncompleteReasons = FeedbackText.SafeList(incomplete),
            CannotApplyReasons = FeedbackText.SafeList(cannot),
            Verdict = verdict,
            CreatedAtUtc = now,
            Boundary = FeedbackPatchProposalBoundary.Evidence
        };
        var receipt = new FeedbackPatchProposalReceipt
        {
            FeedbackPatchProposalReceiptId = $"feedback_patch_receipt_{AqFeedbackHashing.ShortHash($"{proposal.PatchProposalId}|{proposal.Verdict}")}",
            PatchProposalId = proposal.PatchProposalId,
            SourcePackageId = proposal.SourcePackageId,
            Verdict = proposal.Verdict,
            ProposedFileCount = proposal.ProposedFiles.Length,
            ProposedHunkCount = proposal.ProposedHunks.Length,
            CannotApplyReasons = proposal.CannotApplyReasons,
            IncompleteReasons = proposal.IncompleteReasons,
            BoundaryStatements =
            [
                "AQ proposal is evidence only.",
                "AQ proposal requires an AP remediation package.",
                "Every proposed hunk is attributed to a remediation candidate.",
                "AQ proposal does not apply source changes.",
                "AQ proposal does not commit, push, update PR branches, mark ready, request reviewers, merge, release, deploy, or continue workflow."
            ],
            CreatedAtUtc = now,
            Boundary = FeedbackPatchProposalBoundary.Evidence
        };

        return new FeedbackPatchProposalArtifacts
        {
            Proposal = proposal,
            Receipt = receipt
        };
    }

    public static string RenderManualReviewProposal(FeedbackPatchProposal proposal)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AQ Feedback Patch Proposal Notes");
        builder.AppendLine();
        builder.AppendLine("Evidence only. This is a manual-review proposal note, not a diff and not source apply.");
        builder.AppendLine($"Proposal: `{proposal.PatchProposalId}`");
        builder.AppendLine($"Source package: `{proposal.SourcePackageId}`");
        builder.AppendLine();
        foreach (var file in proposal.ProposedFiles)
        {
            builder.AppendLine($"## {file.FilePath}");
            builder.AppendLine();
            builder.AppendLine($"Change kind: `{file.ChangeKind}`");
            builder.AppendLine($"Remediation candidates: `{string.Join("`, `", file.RemediationCandidateIds)}`");
            builder.AppendLine($"Rationale: {file.Rationale}");
            builder.AppendLine();
            foreach (var hunk in file.PatchHunks)
            {
                builder.AppendLine($"### {hunk.HunkId}");
                builder.AppendLine();
                builder.AppendLine($"Patch applicability: `{hunk.PatchApplicability}`");
                builder.AppendLine($"Target line hint: {hunk.TargetLineHint}");
                builder.AppendLine($"Original context: {hunk.OriginalContext}");
                builder.AppendLine($"Proposed replacement: {hunk.ProposedReplacement}");
                builder.AppendLine();
                builder.AppendLine(hunk.ProposalText.TrimEnd());
                builder.AppendLine();
            }

            builder.AppendLine();
        }

        if (proposal.ProposedFiles.Length == 0)
        {
            builder.AppendLine("No safe patch proposals were produced.");
            foreach (var reason in proposal.CannotApplyReasons)
                builder.AppendLine($"- {reason}");
        }

        return builder.ToString();
    }

    private static IEnumerable<FeedbackPatchFileProposal> BuildFiles(FeedbackRemediationCandidate candidate, List<string> cannot, List<string> incomplete)
    {
        var files = FeedbackText.SafeList(candidate.LikelyFiles);
        if (files.Length == 0)
        {
            incomplete.Add($"CandidateMissingLikelyFiles:{candidate.RemediationId}");
            yield break;
        }

        foreach (var file in files)
        {
            if (IsUnsafePath(file))
            {
                cannot.Add($"UnsafePatchFile:{candidate.RemediationId}:{file}");
                continue;
            }

            if (IsGeneratedPath(file))
            {
                cannot.Add($"GeneratedFileCannotBeProposed:{candidate.RemediationId}:{file}");
                continue;
            }

            if (IsGovernancePath(file) && !candidate.AuthorityRisk)
            {
                cannot.Add($"GovernanceFileRequiresAuthorityRisk:{candidate.RemediationId}:{file}");
                continue;
            }

            var hunk = new FeedbackPatchHunkProposal
            {
                HunkId = $"feedback_hunk_{AqFeedbackHashing.ShortHash($"{candidate.RemediationId}|{file}")}",
                FilePath = file,
                RemediationCandidateIds = [candidate.RemediationId],
                Rationale = candidate.Rationale,
                TargetLineHint = "File-level candidate only; AP feedback did not provide a trusted line anchor.",
                OriginalContext = "AQ did not read source context. A human reviewer must inspect the target file before editing.",
                ProposedReplacement = $"Manual remediation for {candidate.RemediationId}: {FeedbackText.Summary(candidate.Rationale, 220)}",
                ProposalText = RenderProposalText(file, candidate),
                PatchApplicability = FeedbackPatchApplicability.ManualReviewOnly,
                Boundary = FeedbackPatchProposalBoundary.Evidence
            };
            yield return new FeedbackPatchFileProposal
            {
                FilePath = file,
                ChangeKind = FeedbackPatchChangeKind.Modify,
                RemediationCandidateIds = [candidate.RemediationId],
                Rationale = candidate.Rationale,
                PatchHunks = [hunk],
                ExpectedTests = candidate.SuggestedValidationLanes,
                AuthorityRisk = candidate.AuthorityRisk,
                Boundary = FeedbackPatchProposalBoundary.Evidence
            };
        }
    }

    private static string RenderProposalText(string file, FeedbackRemediationCandidate candidate) => $"""
        Manual review proposal for `{file}`
        RemediationCandidateId: `{candidate.RemediationId}`
        Applicability: `ManualReviewOnly`
        Rationale: {FeedbackText.Summary(candidate.Rationale, 180)}
        Expected validation: {string.Join(", ", candidate.SuggestedValidationLanes.DefaultIfEmpty("FocusedCurrentBlock"))}
        """;

    private static FeedbackRiskLevel HighestRisk(FeedbackRemediationCandidate[] candidates)
    {
        if (candidates.Any(item => item.RiskLevel == FeedbackRiskLevel.High || item.AuthorityRisk))
            return FeedbackRiskLevel.High;
        if (candidates.Any(item => item.RiskLevel == FeedbackRiskLevel.Medium))
            return FeedbackRiskLevel.Medium;
        return FeedbackRiskLevel.Low;
    }

    private static bool IsUnsafePath(string path)
    {
        var normalized = NormalizePath(path);
        return string.IsNullOrWhiteSpace(normalized) ||
               normalized.Equals("..", StringComparison.Ordinal) ||
               normalized.StartsWith("../", StringComparison.Ordinal) ||
               normalized.Contains("/../", StringComparison.Ordinal) ||
               normalized.EndsWith("/..", StringComparison.Ordinal) ||
               normalized.StartsWith("/", StringComparison.Ordinal) ||
               normalized.StartsWith("//", StringComparison.Ordinal) ||
               IsWindowsDrivePath(normalized) ||
               normalized.StartsWith("~", StringComparison.Ordinal) ||
               normalized.Contains('$', StringComparison.Ordinal) ||
               normalized.Contains('%', StringComparison.Ordinal) ||
               string.Equals(normalized, ".git", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsDrivePath(string normalizedPath) =>
        normalizedPath.Length >= 2 &&
        char.IsAsciiLetter(normalizedPath[0]) &&
        normalizedPath[1] == ':';

    private static bool IsGeneratedPath(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("/nuget.config", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "nuget.config", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/packages/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/.nuget/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGovernancePath(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Contains("/governance/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("governedaction", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("authority", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => path.Trim().Replace('\\', '/');
}

public static class FeedbackPatchProposalBypassEvaluator
{
    public static bool CanApplySource(object? evidence) => false;
    public static bool CanCommit(object? evidence) => false;
    public static bool CanPush(object? evidence) => false;
    public static bool CanUpdatePullRequest(object? evidence) => false;
    public static bool CanApprove(object? evidence) => false;
    public static bool CanMarkReadyForReview(object? evidence) => false;
    public static bool CanRequestReviewers(object? evidence) => false;
    public static bool CanMerge(object? evidence) => false;
    public static bool CanRelease(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
}

internal static class AqFeedbackHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
