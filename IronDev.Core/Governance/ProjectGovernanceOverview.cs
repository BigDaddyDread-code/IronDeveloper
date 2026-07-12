using IronDev.Core.Board;
using IronDev.Core.WorkItems;

namespace IronDev.Core.Governance;

public static class ProjectGovernanceOverallStatuses
{
    public const string ControlsActive = "ControlsActive";
    public const string AttentionRequired = "AttentionRequired";
    public const string Degraded = "Degraded";
}

public static class ProjectGovernanceAttentionKinds
{
    public const string InterruptedApply = "InterruptedApply";
    public const string MissingExecutionEvidence = "MissingExecutionEvidence";
    public const string FindingsAwaitingDisposition = "FindingsAwaitingDisposition";
    public const string ApprovalRequired = "ApprovalRequired";
    public const string ContinuationRequired = "ContinuationRequired";
    public const string ControlledApplyReview = "ControlledApplyReview";
    public const string Blocked = "Blocked";
}

public sealed record ProjectGovernanceOverview
{
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string OverallStatus { get; init; } = ProjectGovernanceOverallStatuses.ControlsActive;
    public string StatusSummary { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public string Version { get; init; } = "1";
    public required ProjectGovernancePrimaryAction PrimaryAction { get; init; }
    public IReadOnlyList<ProjectGovernanceAttentionItem> AttentionItems { get; init; } = [];
    public IReadOnlyList<ProjectGovernanceControl> Controls { get; init; } = [];
    public IReadOnlyList<ProjectGovernanceException> Exceptions { get; init; } = [];
    public IReadOnlyList<ProjectGovernanceDecision> RecentDecisions { get; init; } = [];
    public required ProjectGovernanceNavigation Navigation { get; init; }
    public IReadOnlyList<ProjectGovernanceSectionIssue> SectionIssues { get; init; } = [];
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "Governance reports effective controls, pending decisions, exceptions, and evidence. " +
        "It grants no approval, continues no workflow, and applies no source.";
}

public sealed record ProjectGovernancePrimaryAction
{
    public string Kind { get; init; } = "None";
    public string Label { get; init; } = "No action required";
    public string Summary { get; init; } = "No governance action is waiting.";
    public long? WorkItemId { get; init; }
    public string? TargetRoute { get; init; }
}

public sealed record ProjectGovernanceAttentionItem
{
    public long WorkItemId { get; init; }
    public string WorkItemReference { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Kind { get; init; } = ProjectGovernanceAttentionKinds.Blocked;
    public string Severity { get; init; } = "ActionRequired";
    public string Summary { get; init; } = string.Empty;
    public string WaitingOn { get; init; } = string.Empty;
    public DateTimeOffset RecordedUtc { get; init; }
    public string NextSafeAction { get; init; } = string.Empty;
    public string TargetRoute { get; init; } = string.Empty;
}

public sealed record ProjectGovernanceControl
{
    public string Id { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string EffectiveValue { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string Source { get; init; } = "IronDev invariant";
    public bool Configurable { get; init; }
    public string DetailRoute { get; init; } = string.Empty;
    public string? RemedyRoute { get; init; }
}

public sealed record ProjectGovernanceException
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Severity { get; init; } = "Warning";
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset RecordedUtc { get; init; }
    public long? WorkItemId { get; init; }
    public string TargetRoute { get; init; } = string.Empty;
}

public sealed record ProjectGovernanceDecision
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? ActorDisplayName { get; init; }
    public long WorkItemId { get; init; }
    public DateTimeOffset RecordedUtc { get; init; }
    public string TargetRoute { get; init; } = string.Empty;
}

public sealed record ProjectGovernanceNavigation
{
    public string Overview { get; init; } = string.Empty;
    public string Controls { get; init; } = string.Empty;
    public string Exceptions { get; init; } = string.Empty;
    public string Decisions { get; init; } = string.Empty;
    public string Technical { get; init; } = string.Empty;
    public string Audit { get; init; } = string.Empty;
    public string Settings { get; init; } = string.Empty;
}

public sealed record ProjectGovernanceSectionIssue
{
    public string Section { get; init; } = string.Empty;
    public string Status { get; init; } = "Unavailable";
    public string Summary { get; init; } = string.Empty;
    public bool Retryable { get; init; } = true;
}

public interface IProjectGovernanceOverviewService
{
    Task<ProjectGovernanceOverview?> GetAsync(
        int projectId,
        int currentUserId,
        CancellationToken cancellationToken = default);
}

public static class ProjectGovernanceOverviewProjector
{
    private static readonly HashSet<string> DecisionEventKinds = new(StringComparer.Ordinal)
    {
        "SkeletonFindingDispositionRecorded",
        "SkeletonContinuationUnblocked",
        "ContinuationRefused",
        "SkeletonApplyRecoveryDecision",
        "SkeletonApplyAttemptAbandoned",
        "SkeletonApplyStarted",
        "SkeletonApplied",
        "SkeletonApplyRefused"
    };

    public static ProjectGovernanceOverview Build(
        ProjectBoardReadModel board,
        IReadOnlyList<ProjectWorkItemReadModel> workItems,
        bool soloApprovalExceptionAllowed,
        IReadOnlyList<ProjectGovernanceSectionIssue>? sectionIssues = null,
        DateTimeOffset? generatedUtc = null)
    {
        var now = generatedUtc ?? DateTimeOffset.UtcNow;
        var projectWorkItems = workItems.Where(item => item.ProjectId == board.ProjectId).ToArray();
        var workItemsById = projectWorkItems.ToDictionary(item => item.WorkItemId);
        var attention = board.Items
            .Select(item => AttentionFor(board.ProjectId, item, workItemsById.GetValueOrDefault(item.WorkItemId)))
            .Where(item => item is not null)
            .Cast<ProjectGovernanceAttentionItem>()
            .OrderBy(item => Priority(item.Kind))
            .ThenByDescending(item => item.RecordedUtc)
            .ToArray();
        var exceptions = ExceptionsFor(board, projectWorkItems, now);
        var issues = sectionIssues ?? [];
        var degraded = !board.Readiness.IsReady || exceptions.Any(item => item.Severity == "Critical") || issues.Count > 0;
        var overallStatus = degraded
            ? ProjectGovernanceOverallStatuses.Degraded
            : attention.Length > 0
                ? ProjectGovernanceOverallStatuses.AttentionRequired
                : ProjectGovernanceOverallStatuses.ControlsActive;

        return new ProjectGovernanceOverview
        {
            ProjectId = board.ProjectId,
            ProjectName = board.ProjectName,
            OverallStatus = overallStatus,
            StatusSummary = StatusSummary(overallStatus, attention.Length, exceptions.Length, issues.Count),
            GeneratedUtc = now,
            PrimaryAction = PrimaryAction(attention),
            AttentionItems = attention,
            Controls = ControlsFor(board.ProjectId, soloApprovalExceptionAllowed),
            Exceptions = exceptions,
            RecentDecisions = DecisionsFor(projectWorkItems),
            Navigation = NavigationFor(board.ProjectId),
            SectionIssues = issues
        };
    }

    private static ProjectGovernanceAttentionItem? AttentionFor(
        int projectId,
        ProjectBoardItemReadModel boardItem,
        ProjectWorkItemReadModel? workItem)
    {
        var route = WorkItemRoute(boardItem.WorkItemId, projectId);
        if (workItem is not null)
            route = WorkItemRoute(workItem.WorkItemId, workItem.ProjectId);

        if (workItem?.ApplyRecovery is { Required: true } recovery)
        {
            return Item(
                boardItem,
                ProjectGovernanceAttentionKinds.InterruptedApply,
                "Critical",
                recovery.Reason,
                recovery.NextSafeAction,
                workItem.Collaboration.WaitingOn?.DisplayName ?? "Project owner",
                route);
        }

        if (workItem?.LatestRun is { UnresolvedFindingCount: > 0 } run)
        {
            return Item(
                boardItem,
                ProjectGovernanceAttentionKinds.FindingsAwaitingDisposition,
                "ActionRequired",
                $"{run.UnresolvedFindingCount} critic finding(s) require disposition.",
                "Review the findings and record a disposition for each one.",
                workItem.Collaboration.WaitingOn?.DisplayName ?? "Eligible reviewer",
                route);
        }

        if (workItem?.LatestRun is { Status: "PausedForApproval" })
        {
            return Item(
                boardItem,
                ProjectGovernanceAttentionKinds.ApprovalRequired,
                "ActionRequired",
                workItem.StatusSummary,
                workItem.Gate.NextSafeAction,
                workItem.Collaboration.WaitingOn?.DisplayName ?? "Eligible approver",
                route);
        }

        if (workItem?.PrimaryAction.Kind == ProjectWorkItemActionKinds.Apply)
        {
            return Item(
                boardItem,
                ProjectGovernanceAttentionKinds.ControlledApplyReview,
                "ActionRequired",
                workItem.PrimaryAction.Reason,
                "Review apply preflight and use the Work Item's separate controlled apply action.",
                workItem.Collaboration.WaitingOn?.DisplayName ?? "Eligible operator",
                route);
        }

        if (workItem?.LatestRun is { Status: "Completed" })
        {
            return Item(
                boardItem,
                ProjectGovernanceAttentionKinds.ContinuationRequired,
                "ActionRequired",
                workItem.StatusSummary,
                workItem.Gate.NextSafeAction,
                workItem.Collaboration.WaitingOn?.DisplayName ?? "Eligible approver",
                route);
        }

        if (workItem?.ExecutionProof is { Status: ProjectWorkItemExecutionProofStatuses.ProofMissing } proof)
        {
            return Item(
                boardItem,
                ProjectGovernanceAttentionKinds.MissingExecutionEvidence,
                "ActionRequired",
                proof.Reason,
                proof.NextSafeAction,
                workItem.Collaboration.WaitingOn?.DisplayName ?? "Project team",
                route);
        }

        if (!boardItem.NeedsAttention)
            return null;

        return Item(
            boardItem,
            ProjectGovernanceAttentionKinds.Blocked,
            "ActionRequired",
            boardItem.AttentionReason ?? "The Work Item requires human attention.",
            boardItem.NextSafeAction,
            boardItem.WaitingOn?.Label ?? "Project team",
            route);
    }

    private static ProjectGovernanceAttentionItem Item(
        ProjectBoardItemReadModel item,
        string kind,
        string severity,
        string summary,
        string nextSafeAction,
        string waitingOn,
        string route) => new()
    {
        WorkItemId = item.WorkItemId,
        WorkItemReference = $"WI-{item.WorkItemId}",
        Title = item.Title,
        Kind = kind,
        Severity = severity,
        Summary = summary,
        WaitingOn = waitingOn,
        RecordedUtc = item.LastMeaningfulEventUtc,
        NextSafeAction = nextSafeAction,
        TargetRoute = route
    };

    private static ProjectGovernancePrimaryAction PrimaryAction(IReadOnlyList<ProjectGovernanceAttentionItem> attention)
    {
        var item = attention.FirstOrDefault();
        if (item is null)
            return new ProjectGovernancePrimaryAction();

        return new ProjectGovernancePrimaryAction
        {
            Kind = item.Kind,
            Label = item.Kind switch
            {
                ProjectGovernanceAttentionKinds.InterruptedApply => "Review interrupted apply",
                ProjectGovernanceAttentionKinds.FindingsAwaitingDisposition => "Disposition findings",
                ProjectGovernanceAttentionKinds.ApprovalRequired => "Review approval package",
                ProjectGovernanceAttentionKinds.ContinuationRequired => "Review continuation decision",
                ProjectGovernanceAttentionKinds.ControlledApplyReview => "Review controlled apply",
                ProjectGovernanceAttentionKinds.MissingExecutionEvidence => "Resolve missing execution evidence",
                _ => "Review next item"
            },
            Summary = item.Summary,
            WorkItemId = item.WorkItemId,
            TargetRoute = item.TargetRoute
        };
    }

    private static ProjectGovernanceControl[] ControlsFor(int projectId, bool soloApprovalExceptionAllowed)
    {
        var controlsPath = $"/projects/{projectId}/library/governance/controls";
        return
        [
            Control("human-approval", "Human authority", "Human approval", "Required", "Governed continuation requires accepted human approval evidence.", controlsPath),
            Control("self-approval", "Human authority", "Self-approval", soloApprovalExceptionAllowed ? "Solo exception enabled" : "Prohibited", soloApprovalExceptionAllowed ? "The configured solo-user exception remains recorded and backend-governed." : "A different eligible human must provide approval evidence.", controlsPath, soloApprovalExceptionAllowed, "Tenant policy"),
            Control("controlled-apply", "Source mutation", "Source mutation", "Controlled apply only", "Approval does not grant source mutation; apply remains a separate governed operation.", controlsPath),
            Control("exact-package", "Source mutation", "Exact-package binding", "Required", "Continuation and apply re-evaluate the accepted package and current evidence.", controlsPath),
            Control("execution-proof", "Evidence and memory", "Execution proof", "Durable events required", "Packages, files, receipts, and timestamps do not prove execution by themselves.", controlsPath),
            Control("memory-promotion", "Evidence and memory", "Memory promotion", "Proposal separated", "Memory proposals do not become promoted memory without the governed promotion boundary.", controlsPath)
        ];
    }

    private static ProjectGovernanceControl Control(
        string id,
        string group,
        string label,
        string value,
        string explanation,
        string detailRoute,
        bool configurable = false,
        string source = "IronDev invariant") => new()
    {
        Id = id,
        Group = group,
        Label = label,
        EffectiveValue = value,
        Explanation = explanation,
        Source = source,
        Configurable = configurable,
        DetailRoute = detailRoute
    };

    private static ProjectGovernanceException[] ExceptionsFor(
        ProjectBoardReadModel board,
        IReadOnlyList<ProjectWorkItemReadModel> workItems,
        DateTimeOffset now)
    {
        var exceptions = new List<ProjectGovernanceException>();
        if (!board.Readiness.IsReady)
        {
            exceptions.Add(new ProjectGovernanceException
            {
                Id = $"project-readiness-{board.ProjectId}",
                Category = "ProjectReadinessDegraded",
                Severity = "Warning",
                Title = "Project readiness is degraded",
                Summary = board.Readiness.NextAction.NextSafeAction,
                RecordedUtc = now,
                TargetRoute = $"/projects/{board.ProjectId}/library/provisioning"
            });
        }

        foreach (var item in workItems.Where(item => item.ApplyRecovery.Required))
        {
            exceptions.Add(new ProjectGovernanceException
            {
                Id = $"apply-recovery-{item.WorkItemId}",
                Category = item.ApplyRecovery.PartialMutationPossible ? "PartialMutationRisk" : "InterruptedApply",
                Severity = "Critical",
                Title = item.ApplyRecovery.PartialMutationPossible ? "Partial source mutation requires review" : "Apply recovery requires review",
                Summary = item.ApplyRecovery.Reason,
                RecordedUtc = item.LastMeaningfulEventUtc,
                WorkItemId = item.WorkItemId,
                TargetRoute = WorkItemRoute(item.WorkItemId, item.ProjectId)
            });
        }

        foreach (var item in workItems.Where(item => item.ExecutionProof.Status == ProjectWorkItemExecutionProofStatuses.ProofMissing))
        {
            exceptions.Add(new ProjectGovernanceException
            {
                Id = $"execution-proof-{item.WorkItemId}",
                Category = "MissingExecutionEvidence",
                Severity = "ActionRequired",
                Title = "Execution evidence is incomplete",
                Summary = item.ExecutionProof.Reason,
                RecordedUtc = item.LastMeaningfulEventUtc,
                WorkItemId = item.WorkItemId,
                TargetRoute = WorkItemRoute(item.WorkItemId, item.ProjectId)
            });
        }

        return exceptions.OrderBy(item => item.Severity == "Critical" ? 0 : 1).ThenByDescending(item => item.RecordedUtc).ToArray();
    }

    private static ProjectGovernanceDecision[] DecisionsFor(IReadOnlyList<ProjectWorkItemReadModel> workItems) =>
        workItems
            .SelectMany(item => item.Collaboration.RecentActivity
                .Where(activity => DecisionEventKinds.Contains(activity.Kind))
                .Select(activity => new ProjectGovernanceDecision
                {
                    Id = $"{item.WorkItemId}:{activity.Kind}:{activity.TimestampUtc.UtcTicks}",
                    Kind = activity.Kind,
                    Summary = activity.Summary,
                    ActorDisplayName = activity.Actor?.DisplayName,
                    WorkItemId = item.WorkItemId,
                    RecordedUtc = activity.TimestampUtc,
                    TargetRoute = WorkItemRoute(item.WorkItemId, item.ProjectId)
                }))
            .OrderByDescending(item => item.RecordedUtc)
            .Take(10)
            .ToArray();

    private static ProjectGovernanceNavigation NavigationFor(int projectId)
    {
        var root = $"/projects/{projectId}/library/governance";
        return new ProjectGovernanceNavigation
        {
            Overview = root,
            Controls = $"{root}/controls",
            Exceptions = $"{root}/exceptions",
            Decisions = $"{root}/decisions",
            Technical = $"{root}/technical",
            Audit = $"/projects/{projectId}/library/audit",
            Settings = $"/projects/{projectId}/library/settings"
        };
    }

    private static string StatusSummary(string status, int attentionCount, int exceptionCount, int issueCount) => status switch
    {
        ProjectGovernanceOverallStatuses.Degraded when issueCount > 0 => $"Governance is degraded because {issueCount} required section(s) could not be evaluated.",
        ProjectGovernanceOverallStatuses.Degraded => $"Governance is degraded with {exceptionCount} active exception or recovery state(s).",
        ProjectGovernanceOverallStatuses.AttentionRequired => $"{attentionCount} item(s) require a human decision or recovery response.",
        _ => "Required controls are active and no governance action is waiting."
    };

    private static int Priority(string kind) => kind switch
    {
        ProjectGovernanceAttentionKinds.InterruptedApply => 0,
        ProjectGovernanceAttentionKinds.FindingsAwaitingDisposition => 2,
        ProjectGovernanceAttentionKinds.ApprovalRequired => 3,
        ProjectGovernanceAttentionKinds.ContinuationRequired => 4,
        ProjectGovernanceAttentionKinds.ControlledApplyReview => 5,
        ProjectGovernanceAttentionKinds.MissingExecutionEvidence => 6,
        _ => 7
    };

    private static string WorkItemRoute(long workItemId, int projectId) => $"/projects/{projectId}/work-items/{workItemId}";
}
