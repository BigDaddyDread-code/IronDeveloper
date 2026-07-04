using System.Text.RegularExpressions;

namespace IronDev.Core.Memory;

public sealed record PortableEngineeringMemoryCandidate
{
    public required string CandidateId { get; init; }
    public required string SourceRef { get; init; }

    public required string Summary { get; init; }
    public required string Detail { get; init; }

    public required string SourceRepository { get; init; }
    public string? SourceProjectId { get; init; }

    public required bool ClaimedSanitized { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record PortableEngineeringMemoryGuardrailResult
{
    public required string CandidateId { get; init; }
    public required PortableEngineeringMemoryVerdict Verdict { get; init; }

    public required IReadOnlyCollection<string> AllowedLessons { get; init; }
    public required IReadOnlyCollection<string> RejectedReasons { get; init; }
    public required IReadOnlyCollection<string> RedFlags { get; init; }
    public required IReadOnlyCollection<string> Warnings { get; init; }

    public required PortableEngineeringMemoryBoundary Boundary { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
}

public enum PortableEngineeringMemoryVerdict
{
    AllowedSanitizedLesson,
    BlockedConfidentialProjectTruth,
    BlockedAuthorityTransfer,
    BlockedUnsanitizedContent,
    BlockedUnsafeSource,
    BlockedUnknown
}

public sealed record PortableEngineeringMemoryBoundary
{
    public bool ReadOnly { get; init; } = true;
    public bool GuardrailOnly { get; init; } = true;

    public bool CanApprove { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanAuthorizeSourceApply { get; init; }
    public bool CanAuthorizeRollback { get; init; }
    public bool CanAuthorizeCommit { get; init; }
    public bool CanAuthorizePush { get; init; }
    public bool CanAuthorizePullRequest { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanWriteDurableMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanTransferCrossRepoAuthority { get; init; }

    public static PortableEngineeringMemoryBoundary Guardrail { get; } = new();
}

public static class PortableEngineeringMemoryGuardrail
{
    private const string AdvisoryWarning = "Portable memory is advisory only.";
    private const string NonAuthorityWarning = "Portable memory does not approve, satisfy policy, authorize mutation, promote memory, or continue workflow.";
    private const string BoundaryWarning = "Portable memory can carry lessons, not authority or confidential project truth.";

    private static readonly Regex RepositoryReference = new(@"\b[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TicketReference = new(@"\b[A-Z][A-Z0-9]+-\d+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex IncidentReference = new(@"\bINC-\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PrivatePath = new(string.Concat(@"([a-zA-Z]:\\|\\\\|/", "Users/", "|/", "home/", "|/var/log/|/repo/|/src/)"), RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SchemaReference = new(@"\b(schema|table|database|CREATE\s+TABLE|SELECT\s+\*|[A-Za-z0-9_]+\.[A-Za-z0-9_]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CodeShape = new(@"\b(public\s+class|private\s+class|namespace\s+\w+|using\s+System|function\s+\w+|=>|var\s+\w+\s*=|const\s+\w+\s*=|className=)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] AllowedLessonMarkers =
    [
        "prefer",
        "keep",
        "separate",
        "blocked states",
        "missing evidence",
        "next safe action",
        "review heuristic",
        "architecture lesson",
        "failure mode",
        "validation evidence is useful",
        "not approval",
        "bypass"
    ];

    private static readonly string[] ClientFactMarkers =
    [
        "client ",
        "customer ",
        "employer ",
        "acme",
        "contoso",
        "customersecret",
        "business rule",
        "product name"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "previous project approved this",
        "cross-repo approval should transfer",
        "approved this pattern",
        "approval granted",
        "accepted approval",
        "policy satisfied",
        "governance passed",
        "old validation passed so skip validation",
        "source apply approved",
        "accepted source apply",
        "safe to apply",
        "safe to merge",
        "rollback is safe",
        "commit and push",
        "open pr",
        "create pr",
        "portable memory says apply source",
        "portable memory says continue workflow"
    ];

    private static readonly string[] ReleaseDeploymentStateMarkers =
    [
        "release candidate",
        "release readiness approved",
        "released to production",
        "deployment succeeded",
        "deployed",
        "environment approved",
        "go-live approved",
        "ready to deploy",
        "safe to deploy",
        "portable memory says release"
    ];

    private static readonly string[] SelfPromotionMarkers =
    [
        "portable memory says promote itself",
        "promote itself",
        "self-promote",
        "self promotion"
    ];

    public static PortableEngineeringMemoryGuardrailResult Evaluate(PortableEngineeringMemoryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var rejected = new List<string>();
        var redFlags = new List<string>();
        var text = CandidateText(candidate);

        AddSourceIssues(candidate, rejected, redFlags);
        AddSanitizationIssues(candidate, text, rejected, redFlags);
        AddProjectTruthIssues(text, rejected, redFlags);
        AddLeakageIssues(text, rejected, redFlags);
        AddAuthorityIssues(text, rejected, redFlags);

        var cleanRejected = Clean(rejected);
        var cleanRedFlags = Clean(redFlags);
        var allowed = cleanRejected.Count == 0
            ? Clean([MemoryContentSafety.SanitiseSummary(candidate.Summary), MemoryContentSafety.SanitiseSummary(candidate.Detail)])
            : [];

        return new PortableEngineeringMemoryGuardrailResult
        {
            CandidateId = string.IsNullOrWhiteSpace(candidate.CandidateId) ? "missing-candidate-id" : candidate.CandidateId.Trim(),
            Verdict = ResolveVerdict(cleanRejected),
            AllowedLessons = allowed,
            RejectedReasons = cleanRejected,
            RedFlags = cleanRedFlags,
            Warnings = [AdvisoryWarning, NonAuthorityWarning, BoundaryWarning],
            Boundary = PortableEngineeringMemoryBoundary.Guardrail,
            EvidenceRefs = BuildEvidenceRefs(candidate)
        };
    }

    private static void AddSourceIssues(
        PortableEngineeringMemoryCandidate candidate,
        ICollection<string> rejected,
        ICollection<string> redFlags)
    {
        if (string.IsNullOrWhiteSpace(candidate.CandidateId))
            Add(rejected, redFlags, "PortableMemoryCandidateIdRequired", "PortableMemoryUnsafeSource");
        if (string.IsNullOrWhiteSpace(candidate.SourceRef))
            Add(rejected, redFlags, "PortableMemorySourceRefRequired", "PortableMemoryUnsafeSource");
        if (string.IsNullOrWhiteSpace(candidate.SourceRepository))
            Add(rejected, redFlags, "PortableMemorySourceRepositoryRequired", "PortableMemoryUnsafeSource");
        if (candidate.ObservedAtUtc == default)
            Add(rejected, redFlags, "PortableMemoryObservedAtUtcRequired", "PortableMemoryUnsafeSource");
        if (candidate.EvidenceRefs is null || candidate.EvidenceRefs.Count == 0)
            Add(rejected, redFlags, "PortableMemoryEvidenceRequired", "PortableMemoryUnsafeSource");
        if (string.IsNullOrWhiteSpace(candidate.Summary) || string.IsNullOrWhiteSpace(candidate.Detail))
            Add(rejected, redFlags, "PortableMemoryUnsanitizedOrUnknownScope", "PortableMemoryUnknownScope");
    }

    private static void AddSanitizationIssues(
        PortableEngineeringMemoryCandidate candidate,
        string text,
        ICollection<string> rejected,
        ICollection<string> redFlags)
    {
        if (!candidate.ClaimedSanitized)
            Add(rejected, redFlags, "PortableMemoryUnsanitizedOrUnknownScope", "PortableMemoryUnsanitized");

        if (!ContainsAllowedLessonShape(text))
            Add(rejected, redFlags, "PortableMemoryUnsanitizedOrUnknownScope", "PortableMemoryUnknownScope");

        if (!string.IsNullOrWhiteSpace(candidate.SourceProjectId) &&
            (ContainsProjectTruth(text) || ContainsLeakage(text)))
        {
            Add(rejected, redFlags, "PortableMemoryUnsanitizedOrUnknownScope", "PortableMemoryProjectSpecificSource");
        }
    }

    private static void AddProjectTruthIssues(
        string text,
        ICollection<string> rejected,
        ICollection<string> redFlags)
    {
        if (ContainsProjectTruth(text))
            Add(rejected, redFlags, "PortableMemoryContainsConfidentialProjectTruth", "PortableMemoryConfidentialProjectTruth");
        if (RepositoryReference.IsMatch(text) || text.Contains("repo ", StringComparison.OrdinalIgnoreCase))
            Add(rejected, redFlags, "PortableMemoryContainsConfidentialProjectTruth", "PortableMemoryRepositoryFact");
        if (text.Contains("branch ", StringComparison.OrdinalIgnoreCase))
            Add(rejected, redFlags, "PortableMemoryContainsConfidentialProjectTruth", "PortableMemoryBranchFact");
    }

    private static void AddLeakageIssues(
        string text,
        ICollection<string> rejected,
        ICollection<string> redFlags)
    {
        if (CodeShape.IsMatch(text) || MemoryContentSafety.ContainsRawSourceCode(text) || text.Contains("code snippet", StringComparison.OrdinalIgnoreCase))
            Add(rejected, redFlags, "PortableMemoryContainsCode", "PortableMemoryCodeLeak");
        if (SchemaReference.IsMatch(text))
            Add(rejected, redFlags, "PortableMemoryContainsSchema", "PortableMemorySchemaLeak");
        if (TicketReference.IsMatch(text) || text.Contains("ticket ", StringComparison.OrdinalIgnoreCase))
            Add(rejected, redFlags, "PortableMemoryContainsTicket", "PortableMemoryTicketLeak");
        if (IncidentReference.IsMatch(text) || text.Contains("incident ", StringComparison.OrdinalIgnoreCase))
            Add(rejected, redFlags, "PortableMemoryContainsIncident", "PortableMemoryIncidentLeak");
        if (PrivatePath.IsMatch(text) || MemoryContentSafety.ContainsPathLikeText(text))
            Add(rejected, redFlags, "PortableMemoryContainsPrivatePath", "PortableMemoryPrivatePathLeak");
    }

    private static void AddAuthorityIssues(
        string text,
        ICollection<string> rejected,
        ICollection<string> redFlags)
    {
        if (ContainsAny(text, AuthorityMarkers) ||
            ContainsAny(text, ReleaseDeploymentStateMarkers) ||
            ContainsAny(text, SelfPromotionMarkers) ||
            MemoryContentSafety.ContainsAuthorityClaim(text))
        {
            Add(rejected, redFlags, "PortableMemoryAuthorityTransferRejected", "PortableMemoryAuthorityTransfer");
        }

        if (ContainsAny(text, ReleaseDeploymentStateMarkers))
            Add(rejected, redFlags, "PortableMemoryContainsReleaseOrDeploymentState", "PortableMemoryReleaseDeploymentState");
        if (ContainsAny(text, SelfPromotionMarkers))
            Add(rejected, redFlags, "PortableMemorySelfPromotionRejected", "PortableMemorySelfPromotion");
    }

    private static PortableEngineeringMemoryVerdict ResolveVerdict(IReadOnlyCollection<string> rejected)
    {
        if (rejected.Count == 0)
            return PortableEngineeringMemoryVerdict.AllowedSanitizedLesson;
        if (rejected.Contains("PortableMemoryAuthorityTransferRejected", StringComparer.OrdinalIgnoreCase) ||
            rejected.Contains("PortableMemoryContainsReleaseOrDeploymentState", StringComparer.OrdinalIgnoreCase) ||
            rejected.Contains("PortableMemorySelfPromotionRejected", StringComparer.OrdinalIgnoreCase))
        {
            return PortableEngineeringMemoryVerdict.BlockedAuthorityTransfer;
        }
        if (rejected.Contains("PortableMemoryContainsConfidentialProjectTruth", StringComparer.OrdinalIgnoreCase) ||
            rejected.Any(value => value.StartsWith("PortableMemoryContains", StringComparison.OrdinalIgnoreCase)))
        {
            return PortableEngineeringMemoryVerdict.BlockedConfidentialProjectTruth;
        }
        if (rejected.Contains("PortableMemoryUnsanitizedOrUnknownScope", StringComparer.OrdinalIgnoreCase))
            return PortableEngineeringMemoryVerdict.BlockedUnsanitizedContent;
        if (rejected.Any(value => value.EndsWith("Required", StringComparison.OrdinalIgnoreCase)))
            return PortableEngineeringMemoryVerdict.BlockedUnsafeSource;

        return PortableEngineeringMemoryVerdict.BlockedUnknown;
    }

    private static IReadOnlyList<string> BuildEvidenceRefs(PortableEngineeringMemoryCandidate candidate) =>
        Clean(
        [
            Ref("portable-memory-candidate", candidate.CandidateId),
            Ref("portable-memory-source", candidate.SourceRef),
            Ref("source-repository", candidate.SourceRepository),
            Ref("source-project", candidate.SourceProjectId),
            .. ValuesOrEmpty(candidate.EvidenceRefs)
        ]);

    private static bool ContainsAllowedLessonShape(string text) =>
        ContainsAny(text, AllowedLessonMarkers) &&
        !string.IsNullOrWhiteSpace(text) &&
        text.Length >= 12;

    private static bool ContainsProjectTruth(string text) =>
        ContainsAny(text, ClientFactMarkers) ||
        text.Contains("project x", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("secretrepo", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("client-go-live", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("environment ", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsLeakage(string text) =>
        CodeShape.IsMatch(text) ||
        SchemaReference.IsMatch(text) ||
        TicketReference.IsMatch(text) ||
        IncidentReference.IsMatch(text) ||
        PrivatePath.IsMatch(text) ||
        MemoryContentSafety.ContainsRawSourceCode(text) ||
        MemoryContentSafety.ContainsPathLikeText(text);

    private static void Add(ICollection<string> rejected, ICollection<string> redFlags, string reason, string redFlag)
    {
        rejected.Add(reason);
        redFlags.Add(redFlag);
    }

    private static string CandidateText(PortableEngineeringMemoryCandidate candidate) =>
        $"{candidate.Summary}\n{candidate.Detail}";

    private static string Ref(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static bool ContainsAny(string? value, IReadOnlyCollection<string> markers) =>
        !string.IsNullOrWhiteSpace(value) &&
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> ValuesOrEmpty(IEnumerable<string>? values) =>
        values ?? [];
}
