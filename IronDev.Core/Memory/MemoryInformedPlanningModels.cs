using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.Core.Memory;

public enum MemoryPlanningStaleness
{
    Current = 0,
    Stale,
    Unknown
}

public enum MemoryCitationUsedFor
{
    PlanContext = 0,
    RiskWarning,
    TestSuggestion,
    PatchStrategy,
    ConstraintReminder,
    HistoricalCaveat
}

public enum MemoryInformedPlanStatus
{
    Proposed = 0,
    NeedsHumanReview,
    Blocked
}

public enum MemoryInformedPlanStepKind
{
    Inspect = 0,
    Plan,
    PatchProposal,
    TestSuggestion,
    RiskReview,
    MemoryReview,
    HumanReview
}

public enum SuggestedTestProfileKind
{
    Core = 0,
    Focused,
    Api,
    Tauri,
    Full,
    Custom,
    NoneYet
}

public enum KilljoyPlanSeverity
{
    Info = 0,
    Warning,
    Blocker
}

public sealed record MemoryPlanningBoundary
{
    public bool ReadsOnly { get; init; } = true;
    public bool ContextOnly { get; init; }
    public bool ApprovesNothing { get; init; } = true;
    public bool ExecutesNothing { get; init; } = true;
    public bool CanApprove { get; init; }
    public bool CanExecute { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanApplySource { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanApproveRelease { get; init; }
    public bool CanMerge { get; init; }
    public bool CanDeploy { get; init; }

    public static MemoryPlanningBoundary ReadOnlyEvidence { get; } = new();
    public static MemoryPlanningBoundary ContextEvidence { get; } = new() { ContextOnly = true };
}

public sealed record AcceptedMemoryRetrievalRequest
{
    public required string RetrievalRequestId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string TaskSummary { get; init; }
    public required string RepoIdentity { get; init; }
    public MemoryScope[] AllowedMemoryScopes { get; init; } = [MemoryScope.Project, MemoryScope.PortableEngineering, MemoryScope.Run];
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public int MaxResults { get; init; } = 10;
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ReadOnlyEvidence;
}

public sealed record AcceptedMemoryRetrievalResult
{
    public required string RetrievalResultId { get; init; }
    public required string RetrievalRequestId { get; init; }
    public required string RunId { get; init; }
    public required DateTimeOffset RetrievedAtUtc { get; init; }
    public MemoryContextItem[] Items { get; init; } = [];
    public string[] Warnings { get; init; } = [];
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ReadOnlyEvidence;
}

public sealed record MemoryContextItem
{
    public required string MemoryId { get; init; }
    public required string MemoryVersionId { get; init; }
    public required MemoryScope MemoryScope { get; init; }
    public required MemoryKind MemoryKind { get; init; }
    public string? ProjectId { get; init; }
    public required string ContentSummary { get; init; }
    public required string SafeContent { get; init; }
    public string[] SourceEvidenceRefs { get; init; } = [];
    public required DateTimeOffset AcceptedAtUtc { get; init; }
    public required DateTimeOffset LastVerifiedAtUtc { get; init; }
    public required MemoryPlanningStaleness Staleness { get; init; }
    public required string Confidence { get; init; }
    public string[] Caveats { get; init; } = [];
}

public static class AcceptedMemoryRetriever
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(120);

    public static AcceptedMemoryRetrievalResult Retrieve(AcceptedMemoryStore store, AcceptedMemoryRetrievalRequest request, DateTimeOffset? now = null)
    {
        var retrievedAt = now ?? DateTimeOffset.UtcNow;
        var allowed = request.AllowedMemoryScopes.Length == 0
            ? new HashSet<MemoryScope>([MemoryScope.Project, MemoryScope.PortableEngineering, MemoryScope.Run])
            : new HashSet<MemoryScope>(request.AllowedMemoryScopes);
        var warnings = new List<string>();
        var items = new List<MemoryContextItem>();
        var maxResults = Math.Clamp(request.MaxResults <= 0 ? 10 : request.MaxResults, 1, 50);

        foreach (var record in store.List().OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (items.Count >= maxResults)
                break;
            if (!allowed.Contains(record.Scope))
                continue;
            if (record.Scope == MemoryScope.Project && !string.Equals(record.ProjectId, request.ProjectId, StringComparison.OrdinalIgnoreCase))
                continue;

            var version = store.Versions(record.MemoryId)
                .OrderByDescending(item => item.Version)
                .FirstOrDefault(item => item.Version == record.CurrentVersion) ??
                store.Versions(record.MemoryId).OrderByDescending(item => item.Version).FirstOrDefault();

            if (version is null)
            {
                warnings.Add($"MissingAcceptedMemoryVersion:{record.MemoryId}");
                continue;
            }

            var safeContent = MemoryContentSafety.SanitiseMemoryContent(string.IsNullOrWhiteSpace(version.SanitisedContent) ? version.Content : version.SanitisedContent);
            if (MemoryContentSafety.ContainsHiddenReasoning(safeContent) ||
                MemoryContentSafety.ContainsSecretShape(safeContent) ||
                MemoryContentSafety.ContainsAuthorityClaim(safeContent))
            {
                warnings.Add($"UnsafeAcceptedMemoryExcluded:{record.MemoryId}");
                continue;
            }

            if (record.Scope == MemoryScope.PortableEngineering && MemoryContentSafety.ContainsProjectSpecificDetail(safeContent))
            {
                warnings.Add($"PortableMemoryProjectSpecificDetailExcluded:{record.MemoryId}");
                continue;
            }

            var staleness = retrievedAt - version.CreatedAtUtc > StaleAfter
                ? MemoryPlanningStaleness.Stale
                : MemoryPlanningStaleness.Current;
            var caveats = new List<string>
            {
                record.Scope == MemoryScope.PortableEngineering
                    ? "Portable engineering memory is a caveated heuristic, not project fact."
                    : record.Scope == MemoryScope.Project
                        ? "Project memory is scoped to this project only."
                        : "Run memory is evidence from a previous run."
            };
            if (staleness == MemoryPlanningStaleness.Stale)
            {
                caveats.Add("Memory is stale and needs human review before relying on it.");
                warnings.Add($"StaleMemory:{record.MemoryId}");
            }

            items.Add(new MemoryContextItem
            {
                MemoryId = record.MemoryId,
                MemoryVersionId = version.MemoryVersionId,
                MemoryScope = record.Scope,
                MemoryKind = Enum.IsDefined(record.MemoryKind) ? record.MemoryKind : version.MemoryKind,
                ProjectId = record.ProjectId,
                ContentSummary = MemoryContentSafety.SanitiseSummary(safeContent),
                SafeContent = safeContent,
                SourceEvidenceRefs = version.EvidenceRefs.Select(item => item.RefId).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
                AcceptedAtUtc = version.CreatedAtUtc,
                LastVerifiedAtUtc = version.CreatedAtUtc,
                Staleness = staleness,
                Confidence = "AcceptedMemoryEvidence",
                Caveats = caveats.ToArray()
            });
        }

        return new AcceptedMemoryRetrievalResult
        {
            RetrievalResultId = $"mem_retrieval_{Guid.NewGuid():N}",
            RetrievalRequestId = request.RetrievalRequestId,
            RunId = request.RunId,
            RetrievedAtUtc = retrievedAt,
            Items = items.ToArray(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Boundary = MemoryPlanningBoundary.ReadOnlyEvidence
        };
    }
}

public sealed record MemoryCitation
{
    public required string CitationId { get; init; }
    public required string MemoryId { get; init; }
    public required string MemoryVersionId { get; init; }
    public required MemoryScope MemoryScope { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public required MemoryCitationUsedFor UsedFor { get; init; }
    public required string Caveat { get; init; }
}

public sealed record MemoryCitationBundle
{
    public required string MemoryCitationBundleId { get; init; }
    public required string RunId { get; init; }
    public required string RetrievalResultId { get; init; }
    public MemoryCitation[] Citations { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ReadOnlyEvidence;
}

public static class MemoryCitationWriter
{
    public static MemoryCitationBundle CreateBundle(AcceptedMemoryRetrievalResult retrieval, DateTimeOffset? now = null)
    {
        var citations = retrieval.Items.Select(item => new MemoryCitation
        {
            CitationId = $"mem_cite_{Guid.NewGuid():N}",
            MemoryId = item.MemoryId,
            MemoryVersionId = item.MemoryVersionId,
            MemoryScope = item.MemoryScope,
            EvidenceRefs = item.SourceEvidenceRefs,
            UsedFor = UsedFor(item),
            Caveat = item.Caveats.FirstOrDefault() ?? "Memory is planning evidence only."
        }).ToArray();

        return new MemoryCitationBundle
        {
            MemoryCitationBundleId = $"mem_cite_bundle_{Guid.NewGuid():N}",
            RunId = retrieval.RunId,
            RetrievalResultId = retrieval.RetrievalResultId,
            Citations = citations,
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ReadOnlyEvidence
        };
    }

    public static bool IsAuthorityUse(MemoryCitationUsedFor usedFor) =>
        !Enum.IsDefined(usedFor);

    private static MemoryCitationUsedFor UsedFor(MemoryContextItem item) => item.MemoryKind switch
    {
        MemoryKind.FailureMode or MemoryKind.RiskPattern => MemoryCitationUsedFor.RiskWarning,
        MemoryKind.TestCommandLesson or MemoryKind.ToolGateLesson => MemoryCitationUsedFor.TestSuggestion,
        MemoryKind.ReviewHeuristic or MemoryKind.ProjectConvention => MemoryCitationUsedFor.ConstraintReminder,
        MemoryKind.AiAssistanceLesson => MemoryCitationUsedFor.PatchStrategy,
        _ => MemoryCitationUsedFor.PlanContext
    };
}

public sealed record PlannerContextBuildRequest
{
    public required string RunId { get; init; }
    public required string TaskSummary { get; init; }
    public required string RepoIdentity { get; init; }
    public required string BaseCommit { get; init; }
    public string[] RelevantFiles { get; init; } = [];
}

public sealed record PlannerContextBundle
{
    public required string PlannerContextBundleId { get; init; }
    public required string RunId { get; init; }
    public required string TaskSummary { get; init; }
    public required string RepoIdentity { get; init; }
    public required string BaseCommit { get; init; }
    public string[] RelevantFiles { get; init; } = [];
    public string[] AcceptedMemoryRefs { get; init; } = [];
    public required string MemoryCitationBundleId { get; init; }
    public string[] KnownRisks { get; init; } = [];
    public string[] KnownConstraints { get; init; } = [];
    public string[] SuggestedTestHints { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ContextEvidence;
}

public static class PlannerContextBuilder
{
    public static PlannerContextBundle Build(
        PlannerContextBuildRequest request,
        AcceptedMemoryRetrievalResult retrieval,
        MemoryCitationBundle citations,
        DateTimeOffset? now = null)
    {
        var citedMemoryIds = citations.Citations.Select(item => item.MemoryId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var citedItems = retrieval.Items.Where(item => citedMemoryIds.Contains(item.MemoryId)).ToArray();
        return new PlannerContextBundle
        {
            PlannerContextBundleId = $"planner_ctx_{Guid.NewGuid():N}",
            RunId = request.RunId,
            TaskSummary = MemoryContentSafety.SanitiseSummary(request.TaskSummary),
            RepoIdentity = request.RepoIdentity.Trim(),
            BaseCommit = request.BaseCommit.Trim(),
            RelevantFiles = request.RelevantFiles.Select(MemoryContentSafety.SanitiseSummary).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            AcceptedMemoryRefs = citations.Citations.Select(item => item.CitationId).ToArray(),
            MemoryCitationBundleId = citations.MemoryCitationBundleId,
            KnownRisks = citedItems.Where(item =>
                    item.Staleness == MemoryPlanningStaleness.Stale ||
                    item.MemoryKind is MemoryKind.FailureMode or MemoryKind.RiskPattern ||
                    item.Caveats.Any(c => c.Contains("risk", StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.ContentSummary).ToArray(),
            KnownConstraints = citedItems.SelectMany(item => item.Caveats)
                .Concat(citedItems
                    .Where(item => item.MemoryKind is MemoryKind.ProjectConvention or MemoryKind.ReviewHeuristic)
                    .Select(item => item.ContentSummary))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SuggestedTestHints = citations.Citations.Where(item => item.UsedFor == MemoryCitationUsedFor.TestSuggestion)
                .Select(item => $"Review test profile using memory citation {item.CitationId}.").ToArray(),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ContextEvidence
        };
    }
}

public sealed record MemoryInformedPlanProposal
{
    public required string PlanProposalId { get; init; }
    public required string RunId { get; init; }
    public required string PlannerContextBundleId { get; init; }
    public required MemoryInformedPlanStatus PlanStatus { get; init; }
    public MemoryInformedPlanStep[] PlanSteps { get; init; } = [];
    public string[] Assumptions { get; init; } = [];
    public string[] Risks { get; init; } = [];
    public required SuggestedTestProfileKind SuggestedTestProfile { get; init; }
    public string[] MemoryCitations { get; init; } = [];
    public bool RequiredHumanReview { get; init; } = true;
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ContextEvidence;
}

public sealed record MemoryInformedPlanStep
{
    public required string StepId { get; init; }
    public required MemoryInformedPlanStepKind StepKind { get; init; }
    public required string Description { get; init; }
    public required string ExpectedEvidence { get; init; }
    public string? SuggestedCommand { get; init; }
    public bool RequiresGovernedAction { get; init; }
    public GovernedActionKind? RequiredActionKind { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ContextEvidence;
}

public static class MemoryInformedPlanProposalBuilder
{
    public static MemoryInformedPlanProposal Build(PlannerContextBundle context, MemoryCitationBundle citations, DateTimeOffset? now = null)
    {
        var steps = new List<MemoryInformedPlanStep>
        {
            Step(MemoryInformedPlanStepKind.Inspect, "Inspect the task and cited memory context.", "Planner context and memory citations reviewed."),
            Step(MemoryInformedPlanStepKind.Plan, "Draft a bounded implementation approach.", "Plan proposal remains review-only."),
            Step(MemoryInformedPlanStepKind.PatchProposal, "Prepare a future patch proposal package if human review accepts the plan.", "Future governed action envelope may be required.", requiresGovernedAction: true, actionKind: GovernedActionKind.PatchProposalRunStarted),
            Step(MemoryInformedPlanStepKind.TestSuggestion, "Use the suggested test profile as review evidence only.", "Suggested tests remain recommendations."),
            Step(MemoryInformedPlanStepKind.RiskReview, "Review known risks and stale-memory caveats.", "Risk report reviewed."),
            Step(MemoryInformedPlanStepKind.HumanReview, "Human review remains required before any authority-bearing action.", "Human reviewer decision outside this plan.")
        };

        return new MemoryInformedPlanProposal
        {
            PlanProposalId = $"mem_plan_{Guid.NewGuid():N}",
            RunId = context.RunId,
            PlannerContextBundleId = context.PlannerContextBundleId,
            PlanStatus = context.AcceptedMemoryRefs.Length == 0 ? MemoryInformedPlanStatus.NeedsHumanReview : MemoryInformedPlanStatus.Proposed,
            PlanSteps = steps.ToArray(),
            Assumptions = ["Accepted memory is planning evidence only.", "Human review remains required before mutation."],
            Risks = context.KnownRisks.Length == 0 ? ["No memory-derived risk removed the need for human review."] : context.KnownRisks,
            SuggestedTestProfile = context.SuggestedTestHints.Length > 0 ? SuggestedTestProfileKind.Focused : SuggestedTestProfileKind.NoneYet,
            MemoryCitations = citations.Citations.Select(item => item.CitationId).ToArray(),
            RequiredHumanReview = true,
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ContextEvidence
        };
    }

    private static MemoryInformedPlanStep Step(
        MemoryInformedPlanStepKind kind,
        string description,
        string evidence,
        bool requiresGovernedAction = false,
        GovernedActionKind? actionKind = null) => new()
        {
            StepId = $"plan_step_{Guid.NewGuid():N}",
            StepKind = kind,
            Description = description,
            ExpectedEvidence = evidence,
            SuggestedCommand = null,
            RequiresGovernedAction = requiresGovernedAction,
            RequiredActionKind = actionKind,
            Boundary = MemoryPlanningBoundary.ContextEvidence
        };
}

public sealed record PlanRiskReport
{
    public required string PlanRiskReportId { get; init; }
    public required string RunId { get; init; }
    public required string PlanProposalId { get; init; }
    public string[] RiskItems { get; init; } = [];
    public string[] MemoryRiskRefs { get; init; } = [];
    public string[] TestCoverageConcerns { get; init; } = [];
    public string[] AuthorityBoundaryConcerns { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ContextEvidence;
}

public sealed record SuggestedTestProfile
{
    public required string SuggestedTestProfileId { get; init; }
    public required string RunId { get; init; }
    public required string PlanProposalId { get; init; }
    public required SuggestedTestProfileKind SuggestedProfile { get; init; }
    public required string Rationale { get; init; }
    public string[] MemoryCitations { get; init; } = [];
    public required string Confidence { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ContextEvidence;
}

public sealed record KilljoyPlanReview
{
    public required string KilljoyPlanReviewId { get; init; }
    public required string RunId { get; init; }
    public required string PlanProposalId { get; init; }
    public string[] Findings { get; init; } = [];
    public required KilljoyPlanSeverity Severity { get; init; }
    public string[] RecommendedChanges { get; init; } = [];
    public bool AuthorityClaimsFound { get; init; }
    public string[] MissingEvidence { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ContextEvidence;
}

public sealed record PlanningBoundaryReport
{
    public required string PlanningBoundaryReportId { get; init; }
    public required string RunId { get; init; }
    public required string PlanProposalId { get; init; }
    public required string MemoryCitationBundleId { get; init; }
    public string[] BoundaryChecks { get; init; } = [];
    public string[] ForbiddenClaimsFound { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ContextEvidence;
}

public static class MemoryPlanReviewBuilder
{
    public static PlanRiskReport BuildRiskReport(MemoryInformedPlanProposal plan, PlannerContextBundle context, DateTimeOffset? now = null) => new()
    {
        PlanRiskReportId = $"plan_risk_{Guid.NewGuid():N}",
        RunId = plan.RunId,
        PlanProposalId = plan.PlanProposalId,
        RiskItems = plan.Risks,
        MemoryRiskRefs = context.AcceptedMemoryRefs,
        TestCoverageConcerns = plan.SuggestedTestProfile == SuggestedTestProfileKind.NoneYet ? ["No concrete memory-informed test profile was suggested."] : [],
        AuthorityBoundaryConcerns = ["Plan proposal is not approval, execution, source apply, memory promotion, policy satisfaction, workflow continuation, release, merge, or deployment."],
        CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
        Boundary = MemoryPlanningBoundary.ContextEvidence
    };

    public static SuggestedTestProfile BuildSuggestedTestProfile(MemoryInformedPlanProposal plan, DateTimeOffset? now = null) => new()
    {
        SuggestedTestProfileId = $"test_profile_{Guid.NewGuid():N}",
        RunId = plan.RunId,
        PlanProposalId = plan.PlanProposalId,
        SuggestedProfile = plan.SuggestedTestProfile,
        Rationale = plan.SuggestedTestProfile == SuggestedTestProfileKind.NoneYet
            ? "No accepted memory citation justified a stronger test profile."
            : "Accepted memory suggested a focused validation profile.",
        MemoryCitations = plan.MemoryCitations,
        Confidence = "AdvisoryOnly",
        Boundary = MemoryPlanningBoundary.ContextEvidence
    };

    public static KilljoyPlanReview Review(MemoryInformedPlanProposal plan, MemoryCitationBundle citations, DateTimeOffset? now = null)
    {
        var findings = new List<string>();
        var missing = new List<string>();
        var authorityClaims = PlanContainsAuthorityClaim(plan);
        if (plan.MemoryCitations.Length == 0)
            missing.Add("MissingMemoryCitations");
        if (authorityClaims)
            findings.Add("Plan contains authority-shaped language.");
        if (plan.PlanSteps.Any(step => IsForbiddenStepKindName(step.StepKind.ToString())))
            findings.Add("Plan contains forbidden execution step shape.");
        if (citations.Citations.Any(item => item.MemoryScope == MemoryScope.PortableEngineering))
            findings.Add("Portable memory citations remain caveated and cannot become project fact.");

        return new KilljoyPlanReview
        {
            KilljoyPlanReviewId = $"killjoy_plan_{Guid.NewGuid():N}",
            RunId = plan.RunId,
            PlanProposalId = plan.PlanProposalId,
            Findings = findings.Count == 0 ? ["No authority claim found. Human review remains required."] : findings.ToArray(),
            Severity = authorityClaims || missing.Count > 0 ? KilljoyPlanSeverity.Warning : KilljoyPlanSeverity.Info,
            RecommendedChanges = missing.Count > 0 ? ["Add cited accepted memory before using memory in planning."] : ["Keep the plan review-only."],
            AuthorityClaimsFound = authorityClaims,
            MissingEvidence = missing.ToArray(),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ContextEvidence
        };
    }

    public static PlanningBoundaryReport BuildBoundaryReport(MemoryInformedPlanProposal plan, MemoryCitationBundle citations, DateTimeOffset? now = null)
    {
        var forbidden = new List<string>();
        if (PlanContainsAuthorityClaim(plan))
            forbidden.Add("AuthorityClaim");
        if (plan.PlanSteps.Any(step => IsForbiddenStepKindName(step.StepKind.ToString())))
            forbidden.Add("ForbiddenStepKind");
        if (citations.Citations.Any(citation => MemoryCitationWriter.IsAuthorityUse(citation.UsedFor)))
            forbidden.Add("AuthorityCitationUse");

        return new PlanningBoundaryReport
        {
            PlanningBoundaryReportId = $"plan_boundary_{Guid.NewGuid():N}",
            RunId = plan.RunId,
            PlanProposalId = plan.PlanProposalId,
            MemoryCitationBundleId = citations.MemoryCitationBundleId,
            BoundaryChecks =
            [
                "Accepted memory is read-only.",
                "Memory citations are mandatory.",
                "Planner context is not authority.",
                "Plan proposal is not approval.",
                "Killjoy plan review is not approval.",
                "Suggested test profile is not test sufficiency.",
                "No execution, source apply, workflow continuation, memory promotion, release, merge, or deployment occurred."
            ],
            ForbiddenClaimsFound = forbidden.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ContextEvidence
        };
    }

    private static bool PlanContainsAuthorityClaim(MemoryInformedPlanProposal plan)
    {
        var text = string.Join("\n", plan.Assumptions.Concat(plan.Risks).Concat(plan.PlanSteps.SelectMany(step => new[] { step.Description, step.ExpectedEvidence, step.SuggestedCommand ?? string.Empty })));
        return MemoryPlanningTextSafety.ContainsAuthorityClaim(text);
    }

    private static bool IsForbiddenStepKindName(string value) =>
        Regex.IsMatch(value, "(ExecuteTool|ApplySource|RollbackSource|Commit|Push|CreatePullRequest|Merge|Release|Deploy|PromoteMemory|ContinueWorkflow)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

public static class MemoryPlanningTextSafety
{
    private static readonly string[] AuthorityMarkers =
    [
        "approved",
        "approval granted",
        "execution permission",
        "policy satisfied",
        "release ready",
        "merge ready",
        "deploy ready",
        "source apply permission",
        "rollback permission",
        "workflow continued",
        "continue workflow",
        "memory promoted",
        "promote memory",
        "commit this",
        "push this",
        "create pull request"
    ];

    public static bool ContainsAuthorityClaim(string? value) =>
        MemoryContentSafety.ContainsAuthorityClaim(value) ||
        (!string.IsNullOrWhiteSpace(value) && AuthorityMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));
}
