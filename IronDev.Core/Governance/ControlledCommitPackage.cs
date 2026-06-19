using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public enum CommitReadinessDecision
{
    ReadyForHumanCommitReview = 0,
    NeedsMoreEvidence,
    Blocked
}

public sealed record CommitPackageBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanApproveCommit { get; init; }
    public bool CanStageFiles { get; init; }
    public bool CanCreateCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanCreatePullRequest { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanSatisfyPolicy { get; init; }
}

public static class CommitPackageBoundaryText
{
    public const string Boundary = """
        Block AL prepares a controlled commit package.
        It does not stage files.
        It does not create commits.
        It does not push.
        It does not create pull requests.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not continue workflow.
        """;
}

public sealed record CommitPackageRequestInput
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string BaseCommit { get; init; }
    public required string CurrentHeadCommit { get; init; }
    public required string SourceApplyReceiptId { get; init; }
    public required string PatchHash { get; init; }
    public required string PostApplyDiffHash { get; init; }
    public required string RequestedBy { get; init; }
    public required string Reason { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public DateTimeOffset? RequestedAtUtc { get; init; }
}

public sealed record CommitPackageRequest
{
    public required string CommitPackageRequestId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string BaseCommit { get; init; }
    public required string CurrentHeadCommit { get; init; }
    public required string SourceApplyReceiptId { get; init; }
    public required string PatchHash { get; init; }
    public required string PostApplyDiffHash { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required string Reason { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public CommitPackageBoundary Boundary { get; init; } = new();
}

public static class CommitPackageRequestWriter
{
    public static CommitPackageRequest Create(CommitPackageRequestInput input) => new()
    {
        CommitPackageRequestId = $"commit_pkg_req_{CommitPackageHashing.ShortHash($"{input.RunId}|{input.ProjectId}|{input.CurrentHeadCommit}")}",
        RunId = CommitPackageText.Safe(input.RunId),
        ProjectId = CommitPackageText.Safe(input.ProjectId),
        SourceRepoIdentity = CommitPackageText.Safe(input.SourceRepoIdentity),
        SourceRepoPath = CommitPackageText.Safe(input.SourceRepoPath),
        BaseCommit = CommitPackageText.Safe(input.BaseCommit),
        CurrentHeadCommit = CommitPackageText.Safe(input.CurrentHeadCommit),
        SourceApplyReceiptId = CommitPackageText.Safe(input.SourceApplyReceiptId),
        PatchHash = CommitPackageText.Safe(input.PatchHash),
        PostApplyDiffHash = CommitPackageText.Safe(input.PostApplyDiffHash),
        RequestedBy = CommitPackageText.Safe(input.RequestedBy),
        RequestedAtUtc = input.RequestedAtUtc ?? DateTimeOffset.UtcNow,
        Reason = CommitPackageText.Safe(input.Reason),
        EvidenceRefs = CommitPackageText.SafeList(input.EvidenceRefs),
        Boundary = new()
    };
}

public sealed record CommitFileHash
{
    public required string Path { get; init; }
    public required string ContentHash { get; init; }
}

public sealed record CommitExcludedFile
{
    public required string Path { get; init; }
    public required string Reason { get; init; }
}

public sealed record CommitFileManifest
{
    public required string CommitFileManifestId { get; init; }
    public required string RunId { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string BaseCommit { get; init; }
    public required string CurrentHeadCommit { get; init; }
    public string[] ChangedFiles { get; init; } = [];
    public string[] IncludedFiles { get; init; } = [];
    public CommitExcludedFile[] ExcludedFiles { get; init; } = [];
    public string[] UnexpectedFiles { get; init; } = [];
    public CommitFileHash[] FileHashes { get; init; } = [];
    public required string DiffHash { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public CommitPackageBoundary Boundary { get; init; } = new();
}

public sealed record CommitFileManifestInput
{
    public required string RunId { get; init; }
    public required string SourceRepoIdentity { get; init; }
    public required string SourceRepoPath { get; init; }
    public required string BaseCommit { get; init; }
    public required string CurrentHeadCommit { get; init; }
    public string[] KnownChangedFiles { get; init; } = [];
    public string[] ActualChangedFiles { get; init; } = [];
    public string[] ExplicitExcludedFiles { get; init; } = [];
    public CommitFileHash[] FileHashes { get; init; } = [];
    public required string DiffHash { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record CommitStagingPlan
{
    public required string CommitStagingPlanId { get; init; }
    public required string RunId { get; init; }
    public required string CommitFileManifestId { get; init; }
    public string[] ProposedIncludedFiles { get; init; } = [];
    public CommitExcludedFile[] ProposedExcludedFiles { get; init; } = [];
    public string[] StagingCommandsForHuman { get; init; } = [];
    public string[] Warnings { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public CommitPackageBoundary Boundary { get; init; } = new();
}

public static class CommitFileManifestBuilder
{
    public static CommitFileManifest Build(CommitFileManifestInput input)
    {
        var known = CommitPackageText.SafeList(input.KnownChangedFiles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = CommitPackageText.SafeList(input.ActualChangedFiles);
        var explicitExcluded = CommitPackageText.SafeList(input.ExplicitExcludedFiles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexpected = actual.Where(file => known.Count > 0 && !known.Contains(file)).ToArray();
        var included = actual.Where(file => !explicitExcluded.Contains(file) && !unexpected.Contains(file, StringComparer.OrdinalIgnoreCase)).ToArray();
        var excluded = explicitExcluded
            .Select(file => new CommitExcludedFile { Path = file, Reason = "Explicitly excluded from the future commit package." })
            .Concat(unexpected.Select(file => new CommitExcludedFile { Path = file, Reason = "Unexpected changed file requires manual review before a future commit." }))
            .ToArray();

        return new CommitFileManifest
        {
            CommitFileManifestId = $"commit_manifest_{CommitPackageHashing.ShortHash($"{input.RunId}|{input.DiffHash}")}",
            RunId = CommitPackageText.Safe(input.RunId),
            SourceRepoIdentity = CommitPackageText.Safe(input.SourceRepoIdentity),
            SourceRepoPath = CommitPackageText.Safe(input.SourceRepoPath),
            BaseCommit = CommitPackageText.Safe(input.BaseCommit),
            CurrentHeadCommit = CommitPackageText.Safe(input.CurrentHeadCommit),
            ChangedFiles = actual,
            IncludedFiles = included,
            ExcludedFiles = excluded,
            UnexpectedFiles = unexpected,
            FileHashes = input.FileHashes,
            DiffHash = CommitPackageText.Safe(input.DiffHash),
            CreatedAtUtc = input.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = new()
        };
    }

    public static CommitStagingPlan BuildStagingPlan(CommitFileManifest manifest, DateTimeOffset? now = null) => new()
    {
        CommitStagingPlanId = $"commit_stage_plan_{CommitPackageHashing.ShortHash(manifest.CommitFileManifestId)}",
        RunId = manifest.RunId,
        CommitFileManifestId = manifest.CommitFileManifestId,
        ProposedIncludedFiles = manifest.IncludedFiles,
        ProposedExcludedFiles = manifest.ExcludedFiles,
        StagingCommandsForHuman = manifest.IncludedFiles
            .Select(file => $"git add -- {file}")
            .Concat(["git diff --cached --check"])
            .ToArray(),
        Warnings = manifest.UnexpectedFiles.Length == 0
            ? ["Manual staging remains required. This plan does not stage files."]
            : ["Unexpected files require human review before any future staging.", "Manual staging remains required. This plan does not stage files."],
        CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
        Boundary = new()
    };
}

public sealed record CommitEvidenceBundleInput
{
    public required string RunId { get; init; }
    public required string CommitPackageRequestId { get; init; }
    public required string CommitFileManifestId { get; init; }
    public string[] AvailableArtifactNames { get; init; } = [];
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record CommitEvidenceBundle
{
    public required string CommitEvidenceBundleId { get; init; }
    public required string RunId { get; init; }
    public required string CommitPackageRequestId { get; init; }
    public required string CommitFileManifestId { get; init; }
    public string[] PatchEvidenceRefs { get; init; } = [];
    public string[] ValidationEvidenceRefs { get; init; } = [];
    public string[] GovernanceEvidenceRefs { get; init; } = [];
    public string[] SourceApplyEvidenceRefs { get; init; } = [];
    public string[] RollbackEvidenceRefs { get; init; } = [];
    public string[] ArtifactAuditRefs { get; init; } = [];
    public string[] UnsafeMaterialRefs { get; init; } = [];
    public string[] PlanningEvidenceRefs { get; init; } = [];
    public string[] KnownRiskRefs { get; init; } = [];
    public string[] MissingEvidence { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public CommitPackageBoundary Boundary { get; init; } = new();
}

public static class CommitEvidenceBundleBuilder
{
    public static CommitEvidenceBundle Build(CommitEvidenceBundleInput input)
    {
        var artifacts = CommitPackageText.SafeList(input.AvailableArtifactNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var patch = Any(artifacts, "patch.diff", "source-apply-receipt.json") ? Existing(artifacts, "patch.diff", "source-apply-receipt.json") : Missing(missing, "PatchDiffOrSourceApplyReceipt");
        var changed = Any(artifacts, "changed-files.txt") ? Existing(artifacts, "changed-files.txt") : Missing(missing, "ChangedFiles");
        var tests = Any(artifacts, "test-results.txt", "test-gap.md") ? Existing(artifacts, "test-results.txt", "test-gap.md") : Missing(missing, "TestsOrExplicitGap");
        var build = Any(artifacts, "build-results.txt", "build-gap.md") ? Existing(artifacts, "build-results.txt", "build-gap.md") : Missing(missing, "BuildOrExplicitGap");
        var audit = Any(artifacts, "artifact-consistency-report.json", "artifact-consistency-gap.md") ? Existing(artifacts, "artifact-consistency-report.json", "artifact-consistency-gap.md") : Missing(missing, "ArtifactConsistencyReportOrExplicitGap");
        var unsafeRefs = Any(artifacts, "unsafe-material-report.json", "unsafe-material-gap.md") ? Existing(artifacts, "unsafe-material-report.json", "unsafe-material-gap.md") : Missing(missing, "UnsafeMaterialReportOrExplicitGap");
        var rollback = Any(artifacts, "source-rollback-receipt.json", "rollback-gap.md", "source-post-apply-state.json") ? Existing(artifacts, "source-rollback-receipt.json", "rollback-gap.md", "source-post-apply-state.json") : Missing(missing, "RollbackEvidenceOrExplicitGap");
        var governance = Any(artifacts, "governance-events.jsonl") ? Existing(artifacts, "governance-events.jsonl") : Missing(missing, "GovernanceEvents");

        return new CommitEvidenceBundle
        {
            CommitEvidenceBundleId = $"commit_evidence_{CommitPackageHashing.ShortHash($"{input.RunId}|{input.CommitPackageRequestId}|{input.CommitFileManifestId}")}",
            RunId = CommitPackageText.Safe(input.RunId),
            CommitPackageRequestId = CommitPackageText.Safe(input.CommitPackageRequestId),
            CommitFileManifestId = CommitPackageText.Safe(input.CommitFileManifestId),
            PatchEvidenceRefs = patch.Concat(changed).ToArray(),
            ValidationEvidenceRefs = tests.Concat(build).ToArray(),
            GovernanceEvidenceRefs = governance,
            SourceApplyEvidenceRefs = Existing(artifacts, "source-apply-request.json", "source-apply-binding-report.json", "source-apply-receipt.json", "source-post-apply-state.json"),
            RollbackEvidenceRefs = rollback,
            ArtifactAuditRefs = audit,
            UnsafeMaterialRefs = unsafeRefs,
            PlanningEvidenceRefs = Existing(artifacts, "planner-context.json", "memory-informed-plan.json", "killjoy-plan-review.json"),
            KnownRiskRefs = Existing(artifacts, "known-risks.md", "commit-risk-notes.md"),
            MissingEvidence = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CreatedAtUtc = input.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = new()
        };
    }

    private static bool Any(HashSet<string> artifacts, params string[] names) => names.Any(artifacts.Contains);
    private static string[] Existing(HashSet<string> artifacts, params string[] names) => names.Where(artifacts.Contains).ToArray();
    private static string[] Missing(List<string> missing, string code)
    {
        missing.Add(code);
        return [];
    }
}

public sealed record CommitMessageProposal
{
    public required string CommitMessageProposalId { get; init; }
    public required string RunId { get; init; }
    public required string CommitEvidenceBundleId { get; init; }
    public required string ProposedTitle { get; init; }
    public required string ProposedBody { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public string[] RiskNotes { get; init; } = [];
    public bool HumanEditRequired { get; init; } = true;
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public CommitPackageBoundary Boundary { get; init; } = new();
}

public static class CommitMessageProposalBuilder
{
    private static readonly string[] ForbiddenPhrases =
    [
        "approved",
        "ready to merge",
        "ready to release",
        "deploy now",
        "ship it",
        "autonomous",
        "self-approved",
        "policy satisfied"
    ];

    public static CommitMessageProposal Build(
        CommitEvidenceBundle bundle,
        CommitFileManifest manifest,
        string reason,
        DateTimeOffset? now = null)
    {
        var title = SanitizeTitle(reason);
        var risks = bundle.MissingEvidence.Length == 0
            ? ["Manual review is still required before any future commit."]
            : bundle.MissingEvidence.Select(item => $"Missing evidence: {item}.").Concat(["Manual review is still required before any future commit."]).ToArray();
        var body = $"""
            Source run: {bundle.RunId}

            Summary:
            {title}

            Changed files:
            {string.Join(Environment.NewLine, manifest.IncludedFiles.Select(file => $"- {file}"))}

            Validation evidence:
            {string.Join(Environment.NewLine, bundle.ValidationEvidenceRefs.DefaultIfEmpty("validation evidence gap").Select(item => $"- {item}"))}

            Risk notes:
            {string.Join(Environment.NewLine, risks.Select(item => $"- {item}"))}

            Rollback evidence:
            {string.Join(Environment.NewLine, bundle.RollbackEvidenceRefs.DefaultIfEmpty("rollback evidence gap").Select(item => $"- {item}"))}

            Manual review remains required before any commit exists.
            """;

        return new CommitMessageProposal
        {
            CommitMessageProposalId = $"commit_msg_{CommitPackageHashing.ShortHash($"{bundle.RunId}|{bundle.CommitEvidenceBundleId}")}",
            RunId = bundle.RunId,
            CommitEvidenceBundleId = bundle.CommitEvidenceBundleId,
            ProposedTitle = title,
            ProposedBody = SanitizeForbiddenLanguage(body),
            EvidenceRefs = bundle.ValidationEvidenceRefs.Concat(bundle.PatchEvidenceRefs).Concat(bundle.GovernanceEvidenceRefs).ToArray(),
            RiskNotes = risks,
            HumanEditRequired = true,
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = new()
        };
    }

    public static bool ContainsForbiddenPhrase(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ForbiddenPhrases.Any(phrase => value.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static string SanitizeTitle(string reason)
    {
        var title = CommitPackageText.Safe(reason);
        if (title.Length == 0)
            title = "Prepare controlled commit package";
        if (title.Length > 72)
            title = title[..72].TrimEnd();
        return SanitizeForbiddenLanguage(title);
    }

    private static string SanitizeForbiddenLanguage(string value)
    {
        var result = value;
        foreach (var phrase in ForbiddenPhrases)
            result = Regex.Replace(result, Regex.Escape(phrase), "requires review", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return result;
    }
}

public sealed record CommitReadinessReview
{
    public required string CommitReadinessReviewId { get; init; }
    public required string RunId { get; init; }
    public required CommitReadinessDecision Decision { get; init; }
    public string[] Findings { get; init; } = [];
    public bool CanStageFiles { get; init; }
    public bool CanCreateCommit { get; init; }
    public bool CanPush { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public CommitPackageBoundary Boundary { get; init; } = new();
}

public sealed record CommitPackageRiskReport
{
    public required string CommitPackageRiskReportId { get; init; }
    public required string RunId { get; init; }
    public string[] Risks { get; init; } = [];
    public CommitPackageBoundary Boundary { get; init; } = new();
}

public sealed record CommitPackageBoundaryReport
{
    public required string CommitPackageBoundaryReportId { get; init; }
    public required string RunId { get; init; }
    public required string BoundaryText { get; init; }
    public CommitPackageBoundary Boundary { get; init; } = new();
}

public static class CommitReadinessReviewer
{
    public static CommitReadinessReview Review(
        CommitPackageRequest? request,
        CommitFileManifest? manifest,
        CommitStagingPlan? stagingPlan,
        CommitEvidenceBundle? bundle,
        CommitMessageProposal? message,
        string[]? unsafeMaterialFindings,
        DateTimeOffset? now = null)
    {
        var findings = new List<string>();
        var unsafeFindings = unsafeMaterialFindings ?? [];
        if (request is null) findings.Add("MissingCommitPackageRequest");
        if (manifest is null) findings.Add("MissingCommitFileManifest");
        if (stagingPlan is null) findings.Add("MissingCommitStagingPlan");
        if (bundle is null) findings.Add("MissingCommitEvidenceBundle");
        if (message is null) findings.Add("MissingCommitMessageProposal");
        if (manifest?.UnexpectedFiles.Length > 0) findings.Add("UnexpectedFilesRequireReview");
        if (bundle?.MissingEvidence.Length > 0) findings.AddRange(bundle.MissingEvidence.Select(item => $"MissingEvidence:{item}"));
        if (unsafeFindings.Length > 0) findings.Add("UnsafeMaterialFindingsBlockCommitPackage");
        if (message is not null && (CommitMessageProposalBuilder.ContainsForbiddenPhrase(message.ProposedTitle) || CommitMessageProposalBuilder.ContainsForbiddenPhrase(message.ProposedBody)))
            findings.Add("ForbiddenAuthorityLanguage");

        var decision = findings.Any(item => string.Equals(item, "UnsafeMaterialFindingsBlockCommitPackage", StringComparison.OrdinalIgnoreCase) || string.Equals(item, "ForbiddenAuthorityLanguage", StringComparison.OrdinalIgnoreCase))
            ? CommitReadinessDecision.Blocked
            : findings.Count > 0
                ? CommitReadinessDecision.NeedsMoreEvidence
                : CommitReadinessDecision.ReadyForHumanCommitReview;

        return new CommitReadinessReview
        {
            CommitReadinessReviewId = $"commit_review_{CommitPackageHashing.ShortHash($"{request?.RunId ?? manifest?.RunId ?? "missing"}|{decision}")}",
            RunId = CommitPackageText.Safe(request?.RunId ?? manifest?.RunId ?? bundle?.RunId ?? message?.RunId ?? string.Empty),
            Decision = decision,
            Findings = findings.Count == 0 ? ["Commit package is ready for human commit review. It is not approved and no commit exists."] : findings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = new()
        };
    }

    public static CommitPackageRiskReport BuildRiskReport(CommitReadinessReview review) => new()
    {
        CommitPackageRiskReportId = $"commit_risk_{CommitPackageHashing.ShortHash(review.CommitReadinessReviewId)}",
        RunId = review.RunId,
        Risks = review.Decision == CommitReadinessDecision.ReadyForHumanCommitReview
            ? ["Human review remains required before staging or committing files."]
            : review.Findings,
        Boundary = new()
    };

    public static CommitPackageBoundaryReport BuildBoundaryReport(string runId) => new()
    {
        CommitPackageBoundaryReportId = $"commit_boundary_{CommitPackageHashing.ShortHash(runId)}",
        RunId = CommitPackageText.Safe(runId),
        BoundaryText = CommitPackageBoundaryText.Boundary,
        Boundary = new()
    };
}

public sealed record CommitPackageBypassReport
{
    public required string CommitPackageBypassReportId { get; init; }
    public required string RunId { get; init; }
    public string[] EvidenceSubjects { get; init; } = [];
    public bool FilesStaged { get; init; }
    public bool CommitCreated { get; init; }
    public bool PushPerformed { get; init; }
    public bool PullRequestCreated { get; init; }
    public bool MergePerformed { get; init; }
    public bool ReleasePerformed { get; init; }
    public bool DeployPerformed { get; init; }
    public bool WorkflowContinued { get; init; }
    public CommitPackageBoundary Boundary { get; init; } = new();
}

public static class CommitPackageBypassEvaluator
{
    public static CommitPackageBypassReport Evaluate(string runId, IEnumerable<string> evidenceSubjects) => new()
    {
        CommitPackageBypassReportId = $"commit_bypass_{CommitPackageHashing.ShortHash(runId)}",
        RunId = CommitPackageText.Safe(runId),
        EvidenceSubjects = CommitPackageText.SafeList(evidenceSubjects),
        Boundary = new()
    };

    public static bool CanCreateCommit(object? evidence) => false;
}

internal static class CommitPackageText
{
    public static string Safe(string? value) => (value ?? string.Empty).Trim();
    public static string[] SafeList(IEnumerable<string>? values) => values?
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item.Trim().Replace('\\', '/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];
}

internal static class CommitPackageHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
