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

public enum KilljoyPlanDecision
{
    NoBlockingIssuesFound = 0,
    NeedsMoreEvidence,
    Blocked
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
    public required string SourceRunId { get; init; }
    public required string AcceptedBy { get; init; }
    public required string ContentSummary { get; init; }
    public required string SafeSummary { get; init; }
    public required string SafeContent { get; init; }
    public required string CitationRef { get; init; }
    public required string ContentHash { get; init; }
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

            var acceptedAtUtc = version.CreatedAtUtc == default ? record.UpdatedAtUtc : version.CreatedAtUtc;
            var computedContentHash = MemoryContentSafety.ContentHash(safeContent);
            var staleness = retrievedAt - acceptedAtUtc > StaleAfter
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
                SourceRunId = string.IsNullOrWhiteSpace(version.SourceRunId) ? version.ProposalId : version.SourceRunId,
                AcceptedBy = string.IsNullOrWhiteSpace(version.AcceptedBy) ? "unknown-reviewer" : version.AcceptedBy,
                ContentSummary = MemoryContentSafety.SanitiseSummary(safeContent),
                SafeSummary = MemoryContentSafety.SanitiseSummary(safeContent),
                SafeContent = safeContent,
                CitationRef = $"accepted-memory:{record.MemoryId}:{version.MemoryVersionId}",
                ContentHash = computedContentHash,
                SourceEvidenceRefs = version.EvidenceRefs.Select(item => item.RefId).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
                AcceptedAtUtc = acceptedAtUtc,
                LastVerifiedAtUtc = acceptedAtUtc,
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
    public required MemoryKind MemoryKind { get; init; }
    public required MemoryScope MemoryScope { get; init; }
    public string? ProjectId { get; init; }
    public required string SourceRunId { get; init; }
    public required DateTimeOffset AcceptedAtUtc { get; init; }
    public required string AcceptedBy { get; init; }
    public required string SafeSummary { get; init; }
    public required string ContentHash { get; init; }
    public required string RelevanceReason { get; init; }
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
            MemoryKind = item.MemoryKind,
            MemoryScope = item.MemoryScope,
            ProjectId = item.ProjectId,
            SourceRunId = item.SourceRunId,
            AcceptedAtUtc = item.AcceptedAtUtc,
            AcceptedBy = item.AcceptedBy,
            SafeSummary = item.SafeSummary,
            ContentHash = item.ContentHash,
            RelevanceReason = RelevanceReason(item),
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

    private static string RelevanceReason(MemoryContextItem item) => item.MemoryKind switch
    {
        MemoryKind.FailureMode or MemoryKind.RiskPattern => "Accepted memory describes a known planning risk.",
        MemoryKind.TestCommandLesson or MemoryKind.ToolGateLesson => "Accepted memory suggests a validation profile.",
        MemoryKind.ReviewHeuristic or MemoryKind.ProjectConvention => "Accepted memory reminds the planner of a project constraint.",
        MemoryKind.AiAssistanceLesson => "Accepted memory informs patch strategy.",
        _ => "Accepted memory provides planning context."
    };
}

public sealed record MemoryCitationValidationResult
{
    public required bool IsValid { get; init; }
    public string[] Issues { get; init; } = [];
}

public static class MemoryCitationValidator
{
    public static MemoryCitationValidationResult Validate(
        MemoryCitationBundle bundle,
        AcceptedMemoryRetrievalResult retrieval,
        string projectId)
    {
        var issues = new List<string>();
        var items = retrieval.Items.ToDictionary(item => item.MemoryId, StringComparer.OrdinalIgnoreCase);

        foreach (var citation in bundle.Citations)
        {
            if (!items.TryGetValue(citation.MemoryId, out var item))
            {
                issues.Add($"UnknownMemoryId:{citation.MemoryId}");
                continue;
            }

            AddCitationIssues(issues, citation, item.MemoryVersionId, item.MemoryKind, item.MemoryScope, item.ProjectId, item.SafeContent, item.SafeSummary, projectId);
        }

        return new MemoryCitationValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public static MemoryCitationValidationResult Validate(
        MemoryCitationBundle bundle,
        AcceptedMemoryStore store,
        string projectId)
    {
        var issues = new List<string>();
        var records = store.List().ToDictionary(item => item.MemoryId, StringComparer.OrdinalIgnoreCase);

        foreach (var citation in bundle.Citations)
        {
            if (!records.TryGetValue(citation.MemoryId, out var record))
            {
                issues.Add($"UnknownMemoryId:{citation.MemoryId}");
                continue;
            }

            var version = store.Versions(record.MemoryId)
                .FirstOrDefault(item => string.Equals(item.MemoryVersionId, citation.MemoryVersionId, StringComparison.OrdinalIgnoreCase));
            if (version is null)
            {
                issues.Add($"MissingAcceptedMemoryVersion:{citation.MemoryId}");
                continue;
            }

            var safeContent = MemoryContentSafety.SanitiseMemoryContent(string.IsNullOrWhiteSpace(version.SanitisedContent) ? version.Content : version.SanitisedContent);
            var memoryKind = Enum.IsDefined(record.MemoryKind) ? record.MemoryKind : version.MemoryKind;
            AddCitationIssues(issues, citation, version.MemoryVersionId, memoryKind, record.Scope, record.ProjectId, safeContent, MemoryContentSafety.SanitiseSummary(safeContent), projectId);

            if (!string.IsNullOrWhiteSpace(version.ContentHash) &&
                !string.Equals(version.ContentHash, MemoryContentSafety.ContentHash(safeContent), StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"StoredContentHashMismatch:{citation.MemoryId}");
            }

            if (MemoryContentSafety.ContainsHiddenReasoning(safeContent) ||
                MemoryContentSafety.ContainsSecretShape(safeContent) ||
                MemoryContentSafety.ContainsAuthorityClaim(safeContent))
            {
                issues.Add($"UnsafeAcceptedMemory:{citation.MemoryId}");
            }

            if (record.Scope == MemoryScope.PortableEngineering && MemoryContentSafety.ContainsProjectSpecificDetail(safeContent))
                issues.Add($"PortableMemoryProjectSpecificDetail:{citation.MemoryId}");
        }

        return new MemoryCitationValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static void AddCitationIssues(
        List<string> issues,
        MemoryCitation citation,
        string memoryVersionId,
        MemoryKind memoryKind,
        MemoryScope memoryScope,
        string? projectId,
        string safeContent,
        string safeSummary,
        string requestedProjectId)
    {
        var computedHash = MemoryContentSafety.ContentHash(safeContent);
        if (!string.Equals(citation.MemoryVersionId, memoryVersionId, StringComparison.OrdinalIgnoreCase))
            issues.Add($"MemoryVersionMismatch:{citation.MemoryId}");
        if (!string.Equals(citation.ContentHash, computedHash, StringComparison.OrdinalIgnoreCase))
            issues.Add($"ContentHashMismatch:{citation.MemoryId}");
        if (citation.MemoryKind != memoryKind)
            issues.Add($"MemoryKindMismatch:{citation.MemoryId}");
        if (citation.MemoryScope != memoryScope)
            issues.Add($"MemoryScopeMismatch:{citation.MemoryId}");
        if (memoryScope == MemoryScope.Project &&
            (!string.Equals(citation.ProjectId, projectId, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(projectId, requestedProjectId, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add($"ProjectScopeMismatch:{citation.MemoryId}");
        }
        if (memoryScope == MemoryScope.PortableEngineering &&
            (MemoryContentSafety.ContainsProjectSpecificDetail(safeSummary) || MemoryContentSafety.ContainsProjectSpecificDetail(citation.SafeSummary)))
        {
            issues.Add($"PortableCitationNotSanitized:{citation.MemoryId}");
        }
        if (!string.Equals(citation.SafeSummary, safeSummary, StringComparison.Ordinal))
            issues.Add($"SafeSummaryMismatch:{citation.MemoryId}");
        if (MemoryCitationWriter.IsAuthorityUse(citation.UsedFor))
            issues.Add($"AuthorityCitationUse:{citation.MemoryId}");
    }
}

public sealed record PlannerContextBuildRequest
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string TaskSummary { get; init; }
    public required string CurrentGoal { get; init; }
    public required string RepoIdentity { get; init; }
    public required string BaseCommit { get; init; }
    public string[] Constraints { get; init; } = [];
    public string[] RelevantFiles { get; init; } = [];
}

public sealed record PlannerContextBundle
{
    public required string PlannerContextBundleId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string TaskSummary { get; init; }
    public required string CurrentGoal { get; init; }
    public required string RepoIdentity { get; init; }
    public required string BaseCommit { get; init; }
    public string[] RelevantFiles { get; init; } = [];
    public string[] AcceptedMemoryRefs { get; init; } = [];
    public required string MemoryCitationBundleId { get; init; }
    public string[] Constraints { get; init; } = [];
    public string[] KnownRisks { get; init; } = [];
    public string[] KnownConstraints { get; init; } = [];
    public string[] SuggestedTestHints { get; init; } = [];
    public string[] IgnoredMemory { get; init; } = [];
    public required string ContextBoundary { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ContextEvidence;
}

public static class PlannerContextBuilder
{
    public static PlannerContextBundle Build(
        PlannerContextBuildRequest request,
        AcceptedMemoryStore store,
        MemoryCitationBundle citations,
        DateTimeOffset? now = null)
    {
        var citationValidation = MemoryCitationValidator.Validate(citations, store, request.ProjectId);
        if (!citationValidation.IsValid)
            throw new InvalidOperationException("memory citations are invalid: " + string.Join(",", citationValidation.Issues));

        var retrieval = AcceptedMemoryRetriever.Retrieve(store, new AcceptedMemoryRetrievalRequest
        {
            RetrievalRequestId = $"mem_retrieval_req_{Guid.NewGuid():N}",
            RunId = request.RunId,
            ProjectId = request.ProjectId,
            TaskSummary = request.TaskSummary,
            RepoIdentity = request.RepoIdentity,
            AllowedMemoryScopes = [MemoryScope.Project, MemoryScope.PortableEngineering, MemoryScope.Run],
            RequestedBy = "PlannerContextBuilder",
            RequestedAtUtc = now ?? DateTimeOffset.UtcNow,
            MaxResults = 50,
            Boundary = MemoryPlanningBoundary.ReadOnlyEvidence
        }, now);

        return Build(request, retrieval, citations, now);
    }

    public static PlannerContextBundle Build(
        PlannerContextBuildRequest request,
        AcceptedMemoryRetrievalResult retrieval,
        MemoryCitationBundle citations,
        DateTimeOffset? now = null)
    {
        var citationValidation = MemoryCitationValidator.Validate(citations, retrieval, request.ProjectId);
        if (!citationValidation.IsValid)
            throw new InvalidOperationException("memory citations are invalid: " + string.Join(",", citationValidation.Issues));

        var citedMemoryIds = citations.Citations.Select(item => item.MemoryId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var citedItems = retrieval.Items.Where(item => citedMemoryIds.Contains(item.MemoryId)).ToArray();
        return new PlannerContextBundle
        {
            PlannerContextBundleId = $"planner_ctx_{Guid.NewGuid():N}",
            RunId = request.RunId,
            ProjectId = request.ProjectId.Trim(),
            TaskSummary = MemoryContentSafety.SanitiseSummary(request.TaskSummary),
            CurrentGoal = MemoryContentSafety.SanitiseSummary(request.CurrentGoal),
            RepoIdentity = request.RepoIdentity.Trim(),
            BaseCommit = request.BaseCommit.Trim(),
            RelevantFiles = request.RelevantFiles.Select(MemoryContentSafety.SanitiseSummary).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            AcceptedMemoryRefs = citations.Citations.Select(item => item.CitationId).ToArray(),
            MemoryCitationBundleId = citations.MemoryCitationBundleId,
            Constraints = request.Constraints.Select(MemoryContentSafety.SanitiseSummary).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            KnownRisks = citedItems.Where(item =>
                    item.Staleness == MemoryPlanningStaleness.Stale ||
                    item.MemoryKind is MemoryKind.FailureMode or MemoryKind.RiskPattern ||
                    item.Caveats.Any(c => c.Contains("risk", StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.ContentSummary).ToArray(),
            KnownConstraints = citedItems.SelectMany(item => item.Caveats)
                .Concat(request.Constraints.Select(MemoryContentSafety.SanitiseSummary))
                .Concat(citedItems
                    .Where(item => item.MemoryKind is MemoryKind.ProjectConvention or MemoryKind.ReviewHeuristic)
                    .Select(item => item.ContentSummary))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SuggestedTestHints = citations.Citations.Where(item => item.UsedFor == MemoryCitationUsedFor.TestSuggestion)
                .Select(item => $"Review test profile using memory citation {item.CitationId}.").ToArray(),
            IgnoredMemory = retrieval.Items
                .Where(item => !citedMemoryIds.Contains(item.MemoryId))
                .Select(item => item.CitationRef)
                .Concat(retrieval.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ContextBoundary = "Planner context is advisory. It cannot approve, execute, promote memory, satisfy policy, continue workflow, or mutate source/workspace.",
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = MemoryPlanningBoundary.ContextEvidence
        };
    }
}

public sealed record MemoryInformedPlanProposal
{
    public required string PlanProposalId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string Goal { get; init; }
    public required string PlannerContextBundleId { get; init; }
    public required MemoryInformedPlanStatus PlanStatus { get; init; }
    public MemoryInformedPlanStep[] PlanSteps { get; init; } = [];
    public string[] Assumptions { get; init; } = [];
    public string[] Risks { get; init; } = [];
    public string[] EvidenceRequirements { get; init; } = [];
    public required SuggestedTestProfileKind SuggestedTestProfile { get; init; }
    public string[] MemoryCitations { get; init; } = [];
    public bool RequiredHumanReview { get; init; } = true;
    public required string NonAuthorityBoundary { get; init; }
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
            ProjectId = context.ProjectId,
            Goal = context.CurrentGoal,
            PlannerContextBundleId = context.PlannerContextBundleId,
            PlanStatus = context.AcceptedMemoryRefs.Length == 0 ? MemoryInformedPlanStatus.NeedsHumanReview : MemoryInformedPlanStatus.Proposed,
            PlanSteps = steps.ToArray(),
            Assumptions = ["Accepted memory is planning evidence only.", "Human review remains required before mutation."],
            Risks = context.KnownRisks.Length == 0 ? ["No memory-derived risk removed the need for human review."] : context.KnownRisks,
            EvidenceRequirements =
            [
                "Human review is required before any authority-bearing action.",
                "Governed action evidence is required before source mutation, rollback, workflow continuation, release, deploy, merge, tool execution, ticket creation, scheduler creation, or memory promotion."
            ],
            SuggestedTestProfile = context.SuggestedTestHints.Length > 0 ? SuggestedTestProfileKind.Focused : SuggestedTestProfileKind.NoneYet,
            MemoryCitations = citations.Citations.Select(item => item.CitationId).ToArray(),
            RequiredHumanReview = true,
            NonAuthorityBoundary = "This plan proposal is not approved, not executable, does not satisfy policy, does not authorize tools/source mutation, and does not continue workflow.",
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
    public required KilljoyPlanDecision Decision { get; init; }
    public string[] Findings { get; init; } = [];
    public PlanReviewFinding[] StructuredFindings { get; init; } = [];
    public required KilljoyPlanSeverity Severity { get; init; }
    public string[] RecommendedChanges { get; init; } = [];
    public bool AuthorityClaimsFound { get; init; }
    public string[] MissingEvidence { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MemoryPlanningBoundary Boundary { get; init; } = MemoryPlanningBoundary.ContextEvidence;
}

public sealed record PlanReviewFinding
{
    public required string FindingId { get; init; }
    public required KilljoyPlanSeverity Severity { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    public required string EvidenceRef { get; init; }
    public required string SuggestedFix { get; init; }
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
        var findings = new List<PlanReviewFinding>();
        var missing = new List<string>();
        var authorityClaims = PlanContainsAuthorityClaim(plan);
        if (plan.MemoryCitations.Length == 0)
        {
            missing.Add("MissingMemoryCitations");
            findings.Add(Finding(KilljoyPlanSeverity.Warning, "MissingEvidence", "Plan uses no memory citations.", plan.PlanProposalId, "Add accepted-memory citations or mark the plan as not memory-informed."));
        }
        if (authorityClaims)
            findings.Add(Finding(KilljoyPlanSeverity.Blocker, "AuthorityLeak", "Plan contains authority-shaped language.", plan.PlanProposalId, "Remove approval/execution/readiness language and route authority through governed actions."));
        if (plan.PlanSteps.Any(step => IsForbiddenStepKindName(step.StepKind.ToString())))
            findings.Add(Finding(KilljoyPlanSeverity.Blocker, "ForbiddenStep", "Plan contains forbidden execution step shape.", plan.PlanProposalId, "Rename or remove execution-shaped plan steps."));
        if (citations.Citations.Any(item => item.MemoryScope == MemoryScope.PortableEngineering))
            findings.Add(Finding(KilljoyPlanSeverity.Info, "PortableMemoryCaveat", "Portable memory citations remain caveated and cannot become project fact.", citations.MemoryCitationBundleId, "Keep portable lessons sanitized and advisory."));

        var decision = authorityClaims || findings.Any(item => item.Severity == KilljoyPlanSeverity.Blocker)
            ? KilljoyPlanDecision.Blocked
            : missing.Count > 0
                ? KilljoyPlanDecision.NeedsMoreEvidence
                : KilljoyPlanDecision.NoBlockingIssuesFound;

        return new KilljoyPlanReview
        {
            KilljoyPlanReviewId = $"killjoy_plan_{Guid.NewGuid():N}",
            RunId = plan.RunId,
            PlanProposalId = plan.PlanProposalId,
            Decision = decision,
            Findings = findings.Count == 0 ? ["No authority claim found. Human review remains required."] : findings.Select(item => item.Message).ToArray(),
            StructuredFindings = findings.ToArray(),
            Severity = decision == KilljoyPlanDecision.Blocked ? KilljoyPlanSeverity.Blocker : decision == KilljoyPlanDecision.NeedsMoreEvidence ? KilljoyPlanSeverity.Warning : KilljoyPlanSeverity.Info,
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

    private static PlanReviewFinding Finding(KilljoyPlanSeverity severity, string category, string message, string evidenceRef, string suggestedFix) => new()
    {
        FindingId = $"plan_finding_{Guid.NewGuid():N}",
        Severity = severity,
        Category = category,
        Message = message,
        EvidenceRef = evidenceRef,
        SuggestedFix = suggestedFix
    };
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
