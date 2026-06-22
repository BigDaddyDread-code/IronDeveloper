using IronDev.Core.Governance;

namespace IronDev.Core.Memory;

public sealed record PatchRunMemoryContextRequest
{
    public required string RequestId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchIntent { get; init; }
    public required IReadOnlyCollection<string> CandidateFilePaths { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record PatchRunMemoryContextSource
{
    public required string SourceRef { get; init; }
    public required MemoryScope MemoryScope { get; init; }
    public required MemoryKind MemoryKind { get; init; }
    public required string SourceRepository { get; init; }
    public string? SourceProjectId { get; init; }
    public required string Summary { get; init; }
    public required string Detail { get; init; }
    public required bool IsSanitized { get; init; }
    public required bool IsAcceptedMemory { get; init; }
}

public sealed record PatchRunMemoryContextResult
{
    public required string RequestId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required IReadOnlyCollection<MemoryContextHint> Hints { get; init; }
    public required MemoryContextBoundary Boundary { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> RejectedRefs { get; init; }
    public required IReadOnlyCollection<string> Warnings { get; init; }
}

public sealed record MemoryContextHint
{
    public required string HintId { get; init; }
    public required MemoryContextHintKind Kind { get; init; }
    public required string Summary { get; init; }
    public required string Detail { get; init; }
    public required string SourceRef { get; init; }
    public required bool IsSanitized { get; init; }
    public required bool IsProjectLocal { get; init; }
}

public enum MemoryContextHintKind
{
    PriorFailureHint,
    ProjectConvention,
    PreviousPattern,
    SanitizedEngineeringHeuristic
}

public sealed record MemoryContextBoundary
{
    public bool ReadOnly { get; init; } = true;
    public bool ContextOnly { get; init; } = true;
    public bool CanApprove { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanAuthorizeSourceApply { get; init; }
    public bool CanAuthorizeRollback { get; init; }
    public bool CanAuthorizeCommit { get; init; }
    public bool CanAuthorizePush { get; init; }
    public bool CanAuthorizePullRequest { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanTransferCrossRepoAuthority { get; init; }

    public static MemoryContextBoundary AdvisoryOnly { get; } = new();
}

public static class PatchRunMemoryContextBuilder
{
    private const string AdvisoryWarning =
        "Memory hints do not approve, satisfy policy, authorize source apply, promote memory, or continue workflow.";

    private static readonly string[] AuthorityMarkers =
    [
        "approved",
        "approval",
        "policy was satisfied",
        "policy satisfied",
        "apply source",
        "source apply",
        "rollback is safe",
        "commit and push",
        "open PR",
        "create PR",
        "continue workflow",
        "promote this memory",
        "self-promote",
        "release",
        "deploy",
        "skip validation",
        "old validation passed",
        "authority should transfer",
        "approval should transfer",
        "authorized"
    ];

    public static PatchRunMemoryContextResult Build(
        PatchRunMemoryContextRequest request,
        IEnumerable<PatchRunMemoryContextSource> sources)
    {
        ArgumentNullException.ThrowIfNull(request);

        var warnings = new List<string>
        {
            "Memory context: advisory only",
            AdvisoryWarning
        };
        var rejected = new List<string>();
        var hints = new List<MemoryContextHint>();

        ValidateRequest(request, warnings, rejected);

        foreach (var source in sources ?? [])
        {
            var sourceRef = string.IsNullOrWhiteSpace(source.SourceRef) ? "memory-source:missing-ref" : source.SourceRef.Trim();
            var text = $"{source.Summary}\n{source.Detail}";
            var isProjectLocal = Same(source.SourceRepository, request.Repository);
            var rejectReason = RejectReason(request, source, text, isProjectLocal);
            if (rejectReason is not null)
            {
                rejected.Add($"{sourceRef}:{rejectReason}");
                continue;
            }

            hints.Add(new MemoryContextHint
            {
                HintId = $"memory-hint:{sourceRef}",
                Kind = KindFor(source),
                Summary = MemoryContentSafety.SanitiseSummary(source.Summary),
                Detail = MemoryContentSafety.SanitiseMemoryContent(source.Detail),
                SourceRef = sourceRef,
                IsSanitized = true,
                IsProjectLocal = isProjectLocal
            });
        }

        return new PatchRunMemoryContextResult
        {
            RequestId = request.RequestId.Trim(),
            Repository = request.Repository.Trim(),
            Branch = request.Branch.Trim(),
            RunId = request.RunId.Trim(),
            Hints = hints
                .GroupBy(item => item.SourceRef, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray(),
            Boundary = MemoryContextBoundary.AdvisoryOnly,
            EvidenceRefs = Clean(request.EvidenceRefs)
                .Concat(hints.Select(item => $"memory-context:{item.SourceRef}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RejectedRefs = Clean(rejected),
            Warnings = Clean(warnings)
        };
    }

    public static string RenderReviewSummarySection(PatchRunMemoryContextResult context) =>
        string.Join(Environment.NewLine,
        [
            "## Memory context: advisory only",
            string.Empty,
            AdvisoryWarning,
            string.Empty,
            "Memory hints:",
            .. (context.Hints.Count == 0
                ? ["- none"]
                : context.Hints.Select(hint => $"- [{hint.Kind}] {hint.Summary} (source: {hint.SourceRef})")),
            string.Empty,
            "Rejected memory refs:",
            .. (context.RejectedRefs.Count == 0
                ? ["- none"]
                : context.RejectedRefs.Select(value => $"- {value}")),
            string.Empty,
            "Missing authority:",
            "- Memory context is not approval.",
            "- Memory context is not policy satisfaction.",
            "- Memory context is not source apply, rollback, commit, push, PR, memory promotion, or workflow continuation authority.",
            string.Empty,
            "Forbidden actions:",
            "- do not approve from memory context",
            "- do not satisfy policy from memory context",
            "- do not apply source from memory context",
            "- do not promote memory from memory context",
            "- do not continue workflow from memory context"
        ]);

    public static string RenderKnownRisksSection(PatchRunMemoryContextResult context) =>
        string.Join(Environment.NewLine,
        [
            "## Memory-derived risks: advisory only",
            string.Empty,
            AdvisoryWarning,
            string.Empty,
            .. (context.Hints.Count == 0
                ? ["- no memory-derived risks were included"]
                : context.Hints
                    .Where(hint => hint.Kind is MemoryContextHintKind.PriorFailureHint or MemoryContextHintKind.SanitizedEngineeringHeuristic)
                    .Select(hint => $"- {hint.Summary}")),
            "- Memory-derived risks do not remove missing authority."
        ]);

    public static GovernedOperationStatus PreserveStatus(GovernedOperationStatus status, PatchRunMemoryContextResult _) => status;

    private static void ValidateRequest(
        PatchRunMemoryContextRequest request,
        ICollection<string> warnings,
        ICollection<string> rejected)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
            rejected.Add("request:MissingRequestId");
        if (string.IsNullOrWhiteSpace(request.Repository))
            rejected.Add("request:MissingRepository");
        if (string.IsNullOrWhiteSpace(request.Branch))
            rejected.Add("request:MissingBranch");
        if (string.IsNullOrWhiteSpace(request.RunId))
            rejected.Add("request:MissingRunId");
        if (string.IsNullOrWhiteSpace(request.PatchIntent))
            rejected.Add("request:MissingPatchIntent");
        if (request.ObservedAtUtc == default)
            rejected.Add("request:MissingObservedAtUtc");

        foreach (var path in request.CandidateFilePaths ?? [])
        {
            if (IsUnsafePath(path))
                rejected.Add($"candidate-path:{path}:UnsafeCandidateFilePath");
        }

        if ((request.CandidateFilePaths?.Count ?? 0) == 0)
            warnings.Add("No candidate files were supplied; memory context remains advisory.");
    }

    private static string? RejectReason(
        PatchRunMemoryContextRequest request,
        PatchRunMemoryContextSource source,
        string text,
        bool isProjectLocal)
    {
        if (string.IsNullOrWhiteSpace(source.SourceRef))
            return "MissingSourceRef";
        if (!source.IsAcceptedMemory)
            return "UnacceptedMemoryRejected";
        if (!source.IsSanitized)
            return "UnsanitizedMemoryRejected";
        if (!Enum.IsDefined(source.MemoryScope) || !Enum.IsDefined(source.MemoryKind))
            return "UnknownMemoryShape";
        if (ContainsAuthorityMarker(text) || MemoryPlanningTextSafety.ContainsAuthorityClaim(text))
            return "MemoryAuthorityTextRejected";
        if (source.MemoryScope == MemoryScope.Project && !isProjectLocal)
            return "CrossRepoProjectMemoryRejected";
        if (source.MemoryScope == MemoryScope.Run && !isProjectLocal)
            return "CrossRepoRunMemoryRejected";
        if (source.MemoryScope == MemoryScope.PortableEngineering &&
            (MemoryContentSafety.ContainsProjectSpecificDetail(text) ||
             ContainsOtherProjectText(text, request.Repository)))
        {
            return "UnsanitizedCrossProjectFactsRejected";
        }

        return null;
    }

    private static MemoryContextHintKind KindFor(PatchRunMemoryContextSource source) => source.MemoryKind switch
    {
        MemoryKind.FailureMode or MemoryKind.RiskPattern => MemoryContextHintKind.PriorFailureHint,
        MemoryKind.ProjectConvention => MemoryContextHintKind.ProjectConvention,
        MemoryKind.ToolGateLesson or MemoryKind.TestCommandLesson or MemoryKind.AiAssistanceLesson => MemoryContextHintKind.PreviousPattern,
        _ => MemoryContextHintKind.SanitizedEngineeringHeuristic
    };

    private static bool ContainsAuthorityMarker(string text) =>
        AuthorityMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsOtherProjectText(string text, string currentRepository) =>
        text.Contains("previous project", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("another project", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("client", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("customer", StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(currentRepository) &&
         text.Contains("repository:", StringComparison.OrdinalIgnoreCase) &&
         !text.Contains(currentRepository, StringComparison.OrdinalIgnoreCase));

    private static bool IsUnsafePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var normalized = path.Replace('\\', '/').Trim();
        return normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("~", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) ||
            normalized.Contains("../", StringComparison.Ordinal) ||
            normalized.Contains("/..", StringComparison.Ordinal) ||
            (normalized.Length > 2 && char.IsLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/');
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
