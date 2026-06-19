using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Validation;

namespace IronDev.Core.Governance;

public enum FeedbackSourceKind
{
    GitHubReviewComment = 0,
    GitHubReviewThread,
    GitHubIssueComment,
    GitHubCheckRun,
    GitHubWorkflowRun,
    LocalValidationReceipt,
    ManualOperatorNote
}

public enum FeedbackClassification
{
    ActionableCodeChange = 0,
    ActionableTestChange,
    ActionableDocsChange,
    ActionableGovernanceChange,
    EnvironmentFailure,
    ValidationHarnessFailure,
    StaleFeedback,
    ResolvedFeedback,
    DuplicateFeedback,
    NonActionableComment,
    NeedsHumanDecision,
    OutOfScopeForPhase,
    SecurityOrAuthorityRisk,
    Unknown
}

public enum FeedbackDisposition
{
    Remediate = 0,
    DoNotRemediate,
    NeedsClarification,
    Blocked,
    Duplicate,
    Stale,
    OutOfScope
}

public enum FeedbackRiskLevel
{
    Low = 0,
    Medium,
    High
}

public enum FeedbackRemediationPackageVerdict
{
    PackageCreated = 0,
    NoActionableFeedback,
    NeedsHumanDecision,
    Blocked
}

public sealed record FeedbackAuthorityBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanProposePatch { get; init; }
    public bool CanApplySource { get; init; }
    public bool CanUpdatePullRequest { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanApprove { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanReplyToReviewComments { get; init; }
    public bool CanResolveReviewThreads { get; init; }
    public bool CanRerunCi { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanTag { get; init; }
    public bool CanPublish { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanContinueWorkflow { get; init; }

    public static FeedbackAuthorityBoundary Evidence { get; } = new();
}

public static class FeedbackRemediationBoundaryText
{
    public const string Boundary = """
        Block AP packages review and CI feedback into bounded remediation evidence.
        It does not propose patches.
        It does not apply source changes.
        It does not update PR branches.
        It does not approve.
        It does not mark PRs ready.
        It does not request reviewers.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not continue workflow.
        Feedback evidence is not accepted remediation.
        """;
}

public sealed record FeedbackSource
{
    public required FeedbackSourceKind SourceKind { get; init; }
    public string? SourceUrl { get; init; }
    public required string SourceId { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

public sealed record FeedbackItem
{
    public required string FeedbackItemId { get; init; }
    public required FeedbackSourceKind SourceKind { get; init; }
    public string? SourceUrl { get; init; }
    public required string SourceId { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public string? CommitSha { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public string? ThreadId { get; init; }
    public required string RawExcerpt { get; init; }
    public bool IsResolved { get; init; }
    public FeedbackClassification Classification { get; init; } = FeedbackClassification.Unknown;
    public FeedbackStalenessAssessment Staleness { get; init; } = FeedbackStalenessAssessment.Unknown;
    public FeedbackAuthorityBoundary Boundary { get; init; } = FeedbackAuthorityBoundary.Evidence;
}

public sealed record FeedbackItemInput
{
    public required FeedbackSourceKind SourceKind { get; init; }
    public string? SourceUrl { get; init; }
    public required string SourceId { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public string? CommitSha { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public string? ThreadId { get; init; }
    public required string RawExcerpt { get; init; }
    public bool IsResolved { get; init; }
}

public sealed record FeedbackStalenessAssessment
{
    public string? FeedbackCommitSha { get; init; }
    public required string CurrentHeadSha { get; init; }
    public bool? StillApplies { get; init; }
    public required string StalenessReason { get; init; }

    public static FeedbackStalenessAssessment Unknown { get; } = new()
    {
        CurrentHeadSha = "unknown",
        StillApplies = null,
        StalenessReason = "NotAssessed"
    };
}

public sealed record FeedbackRemediationCandidate
{
    public required string RemediationId { get; init; }
    public string[] FeedbackItemIds { get; init; } = [];
    public required FeedbackDisposition Disposition { get; init; }
    public required string Rationale { get; init; }
    public string[] AffectedAreas { get; init; } = [];
    public string[] LikelyFiles { get; init; } = [];
    public required FeedbackRiskLevel RiskLevel { get; init; }
    public bool AuthorityRisk { get; init; }
    public string[] SuggestedValidationLanes { get; init; } = [];
    public bool RequiresHumanDecision { get; init; }
    public string? BlockedReason { get; init; }
    public FeedbackAuthorityBoundary Boundary { get; init; } = FeedbackAuthorityBoundary.Evidence;
}

public sealed record FeedbackRemediationPackageInput
{
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string CurrentHeadSha { get; init; }
    public string? PullRequestUrl { get; init; }
    public FeedbackItemInput[] Items { get; init; } = [];
    public string[] EvidenceRefs { get; init; } = [];
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record FeedbackRemediationPackage
{
    public required string FeedbackRemediationPackageId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string CurrentHeadSha { get; init; }
    public string? PullRequestUrl { get; init; }
    public FeedbackItem[] FeedbackItems { get; init; } = [];
    public FeedbackRemediationCandidate[] RemediationCandidates { get; init; } = [];
    public string[] EvidenceRefs { get; init; } = [];
    public string[] UnresolvedQuestions { get; init; } = [];
    public string[] KnownRisks { get; init; } = [];
    public string[] ValidationExpectations { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public FeedbackAuthorityBoundary Boundary { get; init; } = FeedbackAuthorityBoundary.Evidence;
}

public sealed record FeedbackRemediationPackageReceipt
{
    public required string FeedbackRemediationPackageReceiptId { get; init; }
    public required string FeedbackRemediationPackageId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string CurrentHeadSha { get; init; }
    public required FeedbackRemediationPackageVerdict Verdict { get; init; }
    public int FeedbackItemCount { get; init; }
    public int RemediationCandidateCount { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public FeedbackAuthorityBoundary Boundary { get; init; } = FeedbackAuthorityBoundary.Evidence;
}

public sealed record FeedbackRemediationPackageArtifacts
{
    public required FeedbackRemediationPackage Package { get; init; }
    public required FeedbackRemediationPackageReceipt Receipt { get; init; }
}

public static class FeedbackRemediationPackager
{
    public static FeedbackRemediationPackageArtifacts Build(FeedbackRemediationPackageInput input)
    {
        var currentHead = FeedbackText.Safe(input.CurrentHeadSha);
        var itemInputs = input.Items ?? [];
        var duplicateGroups = itemInputs
            .Select((item, index) => new { Item = item, Index = index, Fingerprint = Fingerprint(item) })
            .GroupBy(item => item.Fingerprint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Index).ToHashSet(), StringComparer.OrdinalIgnoreCase);
        var firstIndexes = duplicateGroups.ToDictionary(item => item.Key, item => item.Value.Min(), StringComparer.OrdinalIgnoreCase);

        var items = itemInputs.Select((item, index) => BuildItem(item, currentHead, index, firstIndexes[Fingerprint(item)] != index)).ToArray();
        var candidates = BuildCandidates(items);
        var validations = FeedbackText.SafeList(candidates.SelectMany(item => item.SuggestedValidationLanes));
        var questions = FeedbackText.SafeList(candidates.Where(item => item.RequiresHumanDecision).Select(item => item.Rationale));
        var risks = new List<string>
        {
            "Feedback evidence is not accepted remediation.",
            "This package cannot propose patches, apply source changes, update pull requests, or continue workflow."
        };
        risks.AddRange(candidates.Where(item => item.AuthorityRisk).Select(item => $"Authority risk: {item.Rationale}"));

        var package = new FeedbackRemediationPackage
        {
            FeedbackRemediationPackageId = $"feedback_pkg_{ApFeedbackHashing.ShortHash($"{input.RunId}|{input.RepositoryFullName}|{input.PullRequestNumber}|{currentHead}|{items.Length}|{candidates.Length}")}",
            RunId = FeedbackText.Safe(input.RunId),
            RepositoryFullName = FeedbackText.Safe(input.RepositoryFullName),
            PullRequestNumber = input.PullRequestNumber,
            CurrentHeadSha = currentHead,
            PullRequestUrl = FeedbackText.SafeOrNull(input.PullRequestUrl),
            FeedbackItems = items,
            RemediationCandidates = candidates,
            EvidenceRefs = FeedbackText.SafeList(input.EvidenceRefs),
            UnresolvedQuestions = questions,
            KnownRisks = FeedbackText.SafeList(risks),
            ValidationExpectations = validations,
            CreatedAtUtc = input.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            Boundary = FeedbackAuthorityBoundary.Evidence
        };

        var verdict = DetermineVerdict(candidates);
        var receipt = new FeedbackRemediationPackageReceipt
        {
            FeedbackRemediationPackageReceiptId = $"feedback_pkg_receipt_{ApFeedbackHashing.ShortHash($"{package.FeedbackRemediationPackageId}|{verdict}")}",
            FeedbackRemediationPackageId = package.FeedbackRemediationPackageId,
            RunId = package.RunId,
            RepositoryFullName = package.RepositoryFullName,
            PullRequestNumber = package.PullRequestNumber,
            CurrentHeadSha = package.CurrentHeadSha,
            Verdict = verdict,
            FeedbackItemCount = items.Length,
            RemediationCandidateCount = candidates.Length,
            EvidenceRefs = package.EvidenceRefs,
            BoundaryStatements =
            [
                "AP package is evidence only.",
                "AP package is not accepted remediation.",
                "AP package does not propose patches.",
                "AP package does not apply source changes.",
                "AP package does not update PR branches.",
                "AP package does not approve, mark ready, request reviewers, merge, release, deploy, or continue workflow."
            ],
            CreatedAtUtc = package.CreatedAtUtc,
            Boundary = FeedbackAuthorityBoundary.Evidence
        };

        return new FeedbackRemediationPackageArtifacts
        {
            Package = package,
            Receipt = receipt
        };
    }

    private static FeedbackItem BuildItem(FeedbackItemInput input, string currentHeadSha, int index, bool duplicate)
    {
        var staleness = AssessStaleness(input.CommitSha, currentHeadSha);
        var classification = duplicate ? FeedbackClassification.DuplicateFeedback : Classify(input, staleness);
        return new FeedbackItem
        {
            FeedbackItemId = $"feedback_item_{ApFeedbackHashing.ShortHash($"{input.SourceKind}|{input.SourceId}|{input.FilePath}|{input.Line}|{FeedbackText.Summary(input.RawExcerpt)}|{index}")}",
            SourceKind = input.SourceKind,
            SourceUrl = FeedbackText.SafeOrNull(input.SourceUrl),
            SourceId = FeedbackText.Safe(input.SourceId),
            Author = FeedbackText.SafeOrNull(input.Author),
            CreatedAtUtc = input.CreatedAtUtc,
            UpdatedAtUtc = input.UpdatedAtUtc,
            CommitSha = FeedbackText.SafeOrNull(input.CommitSha),
            FilePath = FeedbackText.SafeOrNull(input.FilePath),
            Line = input.Line,
            ThreadId = FeedbackText.SafeOrNull(input.ThreadId),
            RawExcerpt = FeedbackText.Summary(input.RawExcerpt, maxLength: 500),
            IsResolved = input.IsResolved,
            Classification = classification,
            Staleness = staleness,
            Boundary = FeedbackAuthorityBoundary.Evidence
        };
    }

    private static FeedbackRemediationCandidate[] BuildCandidates(FeedbackItem[] items)
    {
        var candidates = new List<FeedbackRemediationCandidate>();
        foreach (var group in items.GroupBy(CandidateFingerprint, StringComparer.OrdinalIgnoreCase))
        {
            var grouped = group.ToArray();
            var primary = grouped.FirstOrDefault(item => item.Classification != FeedbackClassification.DuplicateFeedback) ?? grouped[0];
            var classification = primary.Classification;
            var authorityRisk = grouped.Any(item => item.Classification == FeedbackClassification.SecurityOrAuthorityRisk || LooksAuthorityRisk(item.RawExcerpt));
            var disposition = ToDisposition(classification, authorityRisk);
            var requiresHumanDecision = classification is FeedbackClassification.NeedsHumanDecision or FeedbackClassification.Unknown or FeedbackClassification.SecurityOrAuthorityRisk ||
                                        disposition is FeedbackDisposition.NeedsClarification or FeedbackDisposition.Blocked ||
                                        grouped.Any(item => item.Staleness.StillApplies is null);

            candidates.Add(new FeedbackRemediationCandidate
            {
                RemediationId = $"feedback_remed_{ApFeedbackHashing.ShortHash($"{group.Key}|{string.Join("|", grouped.Select(item => item.FeedbackItemId))}")}",
                FeedbackItemIds = grouped.Select(item => item.FeedbackItemId).ToArray(),
                Disposition = disposition,
                Rationale = BuildRationale(classification, grouped.Length),
                AffectedAreas = BuildAffectedAreas(grouped, classification, authorityRisk),
                LikelyFiles = FeedbackText.SafeList(grouped.Select(item => item.FilePath ?? string.Empty)),
                RiskLevel = authorityRisk || classification == FeedbackClassification.ActionableGovernanceChange ? FeedbackRiskLevel.High :
                    classification is FeedbackClassification.EnvironmentFailure or FeedbackClassification.ValidationHarnessFailure ? FeedbackRiskLevel.Medium :
                    FeedbackRiskLevel.Low,
                AuthorityRisk = authorityRisk,
                SuggestedValidationLanes = SuggestLanes(classification, authorityRisk),
                RequiresHumanDecision = requiresHumanDecision,
                BlockedReason = BlockedReason(classification, disposition, grouped),
                Boundary = FeedbackAuthorityBoundary.Evidence
            });
        }

        return candidates
            .OrderBy(item => item.Disposition)
            .ThenBy(item => item.RemediationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FeedbackStalenessAssessment AssessStaleness(string? feedbackCommitSha, string currentHeadSha)
    {
        var feedback = FeedbackText.SafeOrNull(feedbackCommitSha);
        var current = FeedbackText.Safe(currentHeadSha);
        if (feedback is null)
        {
            return new FeedbackStalenessAssessment
            {
                FeedbackCommitSha = null,
                CurrentHeadSha = current,
                StillApplies = null,
                StalenessReason = "FeedbackCommitShaMissing"
            };
        }

        var same = string.Equals(feedback, current, StringComparison.OrdinalIgnoreCase);
        return new FeedbackStalenessAssessment
        {
            FeedbackCommitSha = feedback,
            CurrentHeadSha = current,
            StillApplies = same,
            StalenessReason = same ? "FeedbackMatchesCurrentHead" : "FeedbackCommitShaDiffersFromCurrentHead"
        };
    }

    private static FeedbackClassification Classify(FeedbackItemInput input, FeedbackStalenessAssessment staleness)
    {
        if (input.IsResolved)
            return FeedbackClassification.ResolvedFeedback;
        if (staleness.StillApplies == false)
            return FeedbackClassification.StaleFeedback;
        if (staleness.StillApplies is null && input.SourceKind != FeedbackSourceKind.ManualOperatorNote)
            return FeedbackClassification.NeedsHumanDecision;
        if (input.SourceKind == FeedbackSourceKind.LocalValidationReceipt && TryClassifyValidationReceipt(input.RawExcerpt, out var validationClassification))
            return validationClassification;

        var text = $"{input.SourceKind} {input.FilePath} {input.RawExcerpt}";
        if (LooksAuthorityRisk(text))
            return FeedbackClassification.SecurityOrAuthorityRisk;
        if (LooksEnvironmentFailure(text))
            return FeedbackClassification.EnvironmentFailure;
        if (LooksHarnessFailure(text))
            return FeedbackClassification.ValidationHarnessFailure;
        if (LooksNonActionable(text))
            return FeedbackClassification.NonActionableComment;
        if (LooksQuestion(text))
            return FeedbackClassification.NeedsHumanDecision;
        if (LooksGovernanceChange(text))
            return FeedbackClassification.ActionableGovernanceChange;
        if (LooksDocsChange(text))
            return FeedbackClassification.ActionableDocsChange;
        if (LooksTestChange(text))
            return FeedbackClassification.ActionableTestChange;
        if (LooksCodeChange(text))
            return FeedbackClassification.ActionableCodeChange;
        return FeedbackClassification.Unknown;
    }

    private static FeedbackDisposition ToDisposition(FeedbackClassification classification, bool authorityRisk) => classification switch
    {
        FeedbackClassification.ActionableCodeChange or
            FeedbackClassification.ActionableTestChange or
            FeedbackClassification.ActionableDocsChange or
            FeedbackClassification.ActionableGovernanceChange when !authorityRisk => FeedbackDisposition.Remediate,
        FeedbackClassification.SecurityOrAuthorityRisk => FeedbackDisposition.Blocked,
        FeedbackClassification.EnvironmentFailure or FeedbackClassification.ValidationHarnessFailure => FeedbackDisposition.Blocked,
        FeedbackClassification.StaleFeedback => FeedbackDisposition.Stale,
        FeedbackClassification.ResolvedFeedback or FeedbackClassification.NonActionableComment => FeedbackDisposition.DoNotRemediate,
        FeedbackClassification.DuplicateFeedback => FeedbackDisposition.Duplicate,
        FeedbackClassification.OutOfScopeForPhase => FeedbackDisposition.OutOfScope,
        _ => FeedbackDisposition.NeedsClarification
    };

    private static string BuildRationale(FeedbackClassification classification, int groupCount)
    {
        var suffix = groupCount > 1 ? $" Grouped {groupCount} related feedback items." : string.Empty;
        return classification switch
        {
            FeedbackClassification.ActionableCodeChange => "Feedback appears to require a product code change." + suffix,
            FeedbackClassification.ActionableTestChange => "Feedback appears to require a test or regression coverage change." + suffix,
            FeedbackClassification.ActionableDocsChange => "Feedback appears to require a documentation or receipt change." + suffix,
            FeedbackClassification.ActionableGovernanceChange => "Feedback appears to require a governance or authority-boundary change." + suffix,
            FeedbackClassification.EnvironmentFailure => "Failure appears environmental and is not automatically a product defect." + suffix,
            FeedbackClassification.ValidationHarnessFailure => "Failure appears to involve the validation harness rather than product behavior." + suffix,
            FeedbackClassification.StaleFeedback => "Feedback is tied to an older commit and must not drive automatic remediation." + suffix,
            FeedbackClassification.ResolvedFeedback => "Feedback is already resolved and is not treated as actionable without new evidence." + suffix,
            FeedbackClassification.DuplicateFeedback => "Feedback duplicates another item and is grouped for review." + suffix,
            FeedbackClassification.NonActionableComment => "Feedback does not request a concrete remediation." + suffix,
            FeedbackClassification.NeedsHumanDecision => "Feedback requires a human decision before remediation." + suffix,
            FeedbackClassification.SecurityOrAuthorityRisk => "Feedback contains authority-sensitive language and requires human triage." + suffix,
            _ => "Feedback could not be safely classified and requires clarification." + suffix
        };
    }

    private static string[] BuildAffectedAreas(FeedbackItem[] items, FeedbackClassification classification, bool authorityRisk)
    {
        var areas = new List<string>();
        if (authorityRisk || classification == FeedbackClassification.ActionableGovernanceChange)
            areas.Add("governance");
        if (items.Any(item => !string.IsNullOrWhiteSpace(item.FilePath)))
            areas.AddRange(items.Select(item => item.FilePath!).Select(AffectedAreaFromPath));
        if (areas.Count == 0)
            areas.Add(classification switch
            {
                FeedbackClassification.EnvironmentFailure => "environment",
                FeedbackClassification.ValidationHarnessFailure => "validation",
                FeedbackClassification.ActionableDocsChange => "docs",
                FeedbackClassification.ActionableTestChange => "tests",
                _ => "unknown"
            });

        return FeedbackText.SafeList(areas);
    }

    private static string AffectedAreaFromPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("Docs/", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return "docs";
        if (normalized.Contains("Tests", StringComparison.OrdinalIgnoreCase))
            return "tests";
        if (normalized.Contains("Governance", StringComparison.OrdinalIgnoreCase))
            return "governance";
        if (normalized.Contains("Validation", StringComparison.OrdinalIgnoreCase))
            return "validation";
        return "source";
    }

    private static string[] SuggestLanes(FeedbackClassification classification, bool authorityRisk)
    {
        var lanes = new List<string> { "FocusedCurrentBlock" };
        if (authorityRisk || classification == FeedbackClassification.ActionableGovernanceChange)
            lanes.Add("FastAuthorityInvariant");
        if (classification is FeedbackClassification.ActionableCodeChange or FeedbackClassification.ActionableTestChange or FeedbackClassification.ValidationHarnessFailure)
            lanes.Add("ImpactedArea");
        if (classification is FeedbackClassification.ActionableCodeChange or FeedbackClassification.ActionableGovernanceChange)
            lanes.Add("Build");
        if (classification is FeedbackClassification.ActionableDocsChange)
            lanes.Add("DiffCheck");
        return FeedbackText.SafeList(lanes);
    }

    private static string? BlockedReason(FeedbackClassification classification, FeedbackDisposition disposition, FeedbackItem[] items)
    {
        if (classification == FeedbackClassification.EnvironmentFailure)
            return "Environment failure requires refreshed evidence or a future rerun request boundary, not product patching.";
        if (classification == FeedbackClassification.ValidationHarnessFailure)
            return "Validation harness failure requires harness triage before product remediation.";
        if (classification == FeedbackClassification.SecurityOrAuthorityRisk)
            return "Authority-sensitive feedback requires explicit human decision.";
        if (items.Any(item => item.Staleness.StillApplies is null))
            return "Feedback staleness could not be determined from the supplied evidence.";
        return disposition == FeedbackDisposition.Blocked ? "Feedback package blocked remediation." : null;
    }

    private static FeedbackRemediationPackageVerdict DetermineVerdict(FeedbackRemediationCandidate[] candidates)
    {
        if (candidates.Any(item => item.Disposition == FeedbackDisposition.Blocked))
            return FeedbackRemediationPackageVerdict.Blocked;
        if (candidates.Any(item => item.RequiresHumanDecision))
            return FeedbackRemediationPackageVerdict.NeedsHumanDecision;
        if (!candidates.Any(item => item.Disposition == FeedbackDisposition.Remediate))
            return FeedbackRemediationPackageVerdict.NoActionableFeedback;
        return FeedbackRemediationPackageVerdict.PackageCreated;
    }

    private static string CandidateFingerprint(FeedbackItem item)
    {
        return Fingerprint(item.SourceKind, item.FilePath, item.Line, item.RawExcerpt);
    }

    private static string Fingerprint(FeedbackItemInput input) =>
        Fingerprint(input.SourceKind, input.FilePath, input.Line, input.RawExcerpt);

    private static string Fingerprint(FeedbackSourceKind sourceKind, string? filePath, int? line, string rawExcerpt)
    {
        var normalized = FeedbackText.Summary(rawExcerpt, maxLength: 160).ToUpperInvariant();
        return $"{sourceKind}|{FeedbackText.Safe(filePath)}|{line?.ToString() ?? "none"}|{normalized}";
    }

    private static bool LooksCodeChange(string text) =>
        ContainsAny(text, ".cs", ".ts", ".js", ".tsx", ".razor", "compile", "build failure", "buildfailed", "null reference", "bug", "fix this", "please change", "must change");

    private static bool LooksTestChange(string text) =>
        ContainsAny(text, "test", "testfailed", "regression", "coverage", "assert", "mstest", "xunit");

    private static bool LooksDocsChange(string text) =>
        ContainsAny(text, "docs/", ".md", "readme", "documentation", "wording", "diffcheckfailed");

    private static bool LooksGovernanceChange(string text) =>
        ContainsAny(text, "governance", "authority", "gate", "receipt", "bypass", "evidence binding", "boundary");

    private static bool LooksEnvironmentFailure(string text) =>
        ContainsAny(text, "access denied", "environmentaccessdenied", "permission denied", "nuget.config", "restorefailed", "network", "rate limit", "infrastructure", "credential", "authentication");

    private static bool LooksHarnessFailure(string text) =>
        ContainsAny(text, "timeout", "timed out", "harnessexception", "dirtygeneratedartifacts", "cachepolicyviolation", "process tree", "harness", "testhost", "could not start process", "invalidlaneplan");

    private static bool LooksAuthorityRisk(string text) =>
        ContainsAny(text, "approve", "approved", "ready for review", "request reviewers", "merge", "release", "deploy", "tag", "publish", "continue workflow", "policy satisfied");

    private static bool LooksNonActionable(string text) =>
        ContainsAny(text, "looks good", "thanks", "thank you", "nice", "lgtm", "no action");

    private static bool LooksQuestion(string text) =>
        text.Contains('?', StringComparison.Ordinal) ||
        ContainsAny(text, "can you explain", "needs decision", "should we", "which option");

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static bool TryClassifyValidationReceipt(string text, out FeedbackClassification classification)
    {
        if (ContainsAny(text, nameof(ValidationFailureKind.EnvironmentAccessDenied), nameof(ValidationFailureKind.RestoreFailed)))
        {
            classification = FeedbackClassification.EnvironmentFailure;
            return true;
        }

        if (ContainsAny(text, nameof(ValidationFailureKind.Timeout), nameof(ValidationFailureKind.Cancelled), nameof(ValidationFailureKind.HarnessException), nameof(ValidationFailureKind.InvalidLanePlan), nameof(ValidationFailureKind.CachePolicyViolation), nameof(ValidationFailureKind.DirtyGeneratedArtifacts)))
        {
            classification = FeedbackClassification.ValidationHarnessFailure;
            return true;
        }

        if (ContainsAny(text, nameof(ValidationFailureKind.BuildFailed), nameof(ValidationFailureKind.ProcessExitNonZero), nameof(ValidationFailureKind.UnknownFailure)))
        {
            classification = FeedbackClassification.ActionableCodeChange;
            return true;
        }

        if (ContainsAny(text, nameof(ValidationFailureKind.TestFailed)))
        {
            classification = FeedbackClassification.ActionableTestChange;
            return true;
        }

        if (ContainsAny(text, nameof(ValidationFailureKind.DiffCheckFailed)))
        {
            classification = FeedbackClassification.ActionableDocsChange;
            return true;
        }

        classification = FeedbackClassification.Unknown;
        return false;
    }
}

public static class FeedbackRemediationBypassEvaluator
{
    public static bool CanProposePatch(object? evidence) => false;
    public static bool CanApplySource(object? evidence) => false;
    public static bool CanUpdatePullRequest(object? evidence) => false;
    public static bool CanApprove(object? evidence) => false;
    public static bool CanMarkReadyForReview(object? evidence) => false;
    public static bool CanRequestReviewers(object? evidence) => false;
    public static bool CanMerge(object? evidence) => false;
    public static bool CanRelease(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
}

internal static class ApFeedbackHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
