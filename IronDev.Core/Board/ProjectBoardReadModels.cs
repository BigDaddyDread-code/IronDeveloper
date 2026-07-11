using IronDev.Core.Provisioning;
using IronDev.Core.Runs;
using IronDev.Data.Models;
using IronDev.Core.Models;

namespace IronDev.Core.Board;

public static class ProjectBoardStages
{
    public const string Shape = "Shape";
    public const string Ticket = "Ticket";
    public const string Build = "Build";
    public const string Review = "Review";
    public const string Done = "Done";
}

public static class ProjectBoardWaitingOnKinds
{
    public const string Human = "Human";
    public const string IronDev = "IronDev";
    public const string Dependency = "Dependency";
}

public sealed record ProjectBoardReadModel
{
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public DateTimeOffset GeneratedUtc { get; init; }
    public required ProjectProvisioningReadiness Readiness { get; init; }
    public IReadOnlyList<ProjectBoardItemReadModel> Items { get; init; } = [];
}

public sealed record ProjectBoardItemReadModel
{
    public long WorkItemId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Stage { get; init; } = ProjectBoardStages.Shape;
    public string State { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public bool NeedsAttention { get; init; }
    public string? AttentionReason { get; init; }
    public string NextSafeAction { get; init; } = string.Empty;
    public ProjectBoardWaitingOnReadModel? WaitingOn { get; init; }
    public ProjectBoardAssigneeReadModel? Assignee { get; init; }
    public DateTimeOffset LastMeaningfulEventUtc { get; init; }
    public ProjectBoardRunReadModel? LatestRun { get; init; }
}

public sealed record ProjectBoardWaitingOnReadModel
{
    public string Kind { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed record ProjectBoardAssigneeReadModel
{
    public int UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}

public sealed record ProjectBoardRunReadModel
{
    public string RunId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public bool RequiresHumanAction { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}

public interface IProjectBoardReadService
{
    Task<ProjectBoardReadModel?> GetAsync(
        int projectId,
        int take = 200,
        CancellationToken cancellationToken = default);
}

public static class ProjectBoardProjector
{
    public static ProjectBoardReadModel Build(
        Project project,
        ProjectProvisioningReadiness readiness,
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<RunRecord> runs,
        DateTimeOffset generatedUtc,
        IReadOnlyDictionary<long, ProjectWorkItemCollaborationSnapshot>? collaboration = null)
    {
        var latestRuns = runs
            .Where(run => run.ProjectId == project.Id && run.TicketId.HasValue)
            .GroupBy(run => run.TicketId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(run => run.UpdatedUtc).ThenByDescending(run => run.CreatedUtc).First());

        var items = tickets
            .Where(ticket => ticket.ProjectId == project.Id && !ticket.IsDeleted)
            .Select(ticket => BuildItem(ticket, latestRuns.GetValueOrDefault(ticket.Id), collaboration?.GetValueOrDefault(ticket.Id)))
            .OrderBy(item => StageOrder(item.Stage))
            .ThenByDescending(item => item.LastMeaningfulEventUtc)
            .ToArray();

        return new ProjectBoardReadModel
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            GeneratedUtc = generatedUtc,
            Readiness = readiness,
            Items = items
        };
    }

    private static ProjectBoardItemReadModel BuildItem(ProjectTicket ticket, RunRecord? latestRun, ProjectWorkItemCollaborationSnapshot? collaboration)
    {
        var stage = StageFor(ticket, latestRun);
        var attention = AttentionFor(ticket, latestRun);
        var lastMeaningfulEventUtc = latestRun?.UpdatedUtc ?? ToUtc(ticket.CreatedDate);

        return new ProjectBoardItemReadModel
        {
            WorkItemId = ticket.Id,
            Title = string.IsNullOrWhiteSpace(ticket.Title) ? $"Work item {ticket.Id}" : ticket.Title,
            Stage = stage,
            State = latestRun?.State.ToString() ?? NormalizeTicketState(ticket.Status),
            Priority = ticket.Priority,
            NeedsAttention = attention.NeedsAttention,
            AttentionReason = attention.Reason,
            NextSafeAction = attention.NextSafeAction,
            WaitingOn = collaboration?.WaitingOn is { } waiting
                ? new ProjectBoardWaitingOnReadModel { Kind = waiting.Kind, Label = waiting.DisplayName }
                : attention.WaitingOn,
            Assignee = collaboration?.Assignee is { UserId: int assigneeId } assignee
                ? new ProjectBoardAssigneeReadModel { UserId = assigneeId, DisplayName = assignee.DisplayName }
                : null,
            LastMeaningfulEventUtc = lastMeaningfulEventUtc,
            LatestRun = latestRun is null
                ? null
                : new ProjectBoardRunReadModel
                {
                    RunId = latestRun.RunId,
                    Status = latestRun.State.ToString(),
                    Summary = latestRun.Summary,
                    FailureReason = latestRun.FailureReason,
                    RequiresHumanAction = latestRun.State is RunLifecycleState.PausedForApproval
                        or RunLifecycleState.Completed
                        or RunLifecycleState.Promoted
                        or RunLifecycleState.Failed
                        or RunLifecycleState.Cancelled,
                    UpdatedUtc = latestRun.UpdatedUtc
                }
        };
    }

    private static string StageFor(ProjectTicket ticket, RunRecord? run)
    {
        if (run is not null)
        {
            return run.State switch
            {
                RunLifecycleState.Applied => ProjectBoardStages.Done,
                RunLifecycleState.PausedForApproval or RunLifecycleState.Completed or RunLifecycleState.Promoted => ProjectBoardStages.Review,
                _ => ProjectBoardStages.Build
            };
        }

        var status = ticket.Status?.Trim() ?? string.Empty;
        if (ContainsAny(status, "applied", "done", "closed")) return ProjectBoardStages.Done;
        if (ContainsAny(status, "approval", "review")) return ProjectBoardStages.Review;
        if (ContainsAny(status, "build", "progress", "failed", "blocked")) return ProjectBoardStages.Build;
        if (ContainsAny(status, "draft", "shape")) return ProjectBoardStages.Shape;
        return ProjectBoardStages.Ticket;
    }

    private static AttentionProjection AttentionFor(ProjectTicket ticket, RunRecord? run)
    {
        if (run is not null)
        {
            return run.State switch
            {
                RunLifecycleState.PausedForApproval => Attention(
                    TextOr(run.Summary, "The latest run is paused for human review."),
                    "Review the waiting run and disposition its findings.",
                    ProjectBoardWaitingOnKinds.Human,
                    "Human review"),
                RunLifecycleState.Completed => Attention(
                    TextOr(run.Summary, "The latest run completed and is waiting for a continuation decision."),
                    "Review the completed run and decide whether to continue.",
                    ProjectBoardWaitingOnKinds.Human,
                    "Continuation decision"),
                RunLifecycleState.Promoted => Attention(
                    TextOr(run.Summary, "The approved run is waiting for controlled apply."),
                    "Review apply preflight and perform the separate controlled apply action.",
                    ProjectBoardWaitingOnKinds.Human,
                    "Controlled apply"),
                RunLifecycleState.Failed => Attention(
                    TextOr(run.FailureReason, TextOr(run.Summary, "The latest run failed.")),
                    "Inspect the failure evidence, then repair or retry through the Work Item.",
                    ProjectBoardWaitingOnKinds.Human,
                    "Project team"),
                RunLifecycleState.Cancelled => Attention(
                    TextOr(run.Summary, "The latest run was cancelled."),
                    "Open the Work Item and decide whether a new run is safe.",
                    ProjectBoardWaitingOnKinds.Human,
                    "Project team"),
                RunLifecycleState.Created or RunLifecycleState.Running => new AttentionProjection(
                    false,
                    null,
                    "Wait for the governed run to report its next state.",
                    WaitingOn(ProjectBoardWaitingOnKinds.IronDev, "IronDev run")),
                RunLifecycleState.Applied => new AttentionProjection(
                    false,
                    null,
                    "Inspect the applied outcome and its receipts.",
                    null),
                _ => new AttentionProjection(false, null, "Open the Work Item for current backend state.", null)
            };
        }

        if (!string.IsNullOrWhiteSpace(ticket.BlockedByTicketIds))
        {
            return Attention(
                $"Blocked by work item(s) {ticket.BlockedByTicketIds}.",
                "Open the Work Item and resolve or re-evaluate its dependencies.",
                ProjectBoardWaitingOnKinds.Dependency,
                "Dependent work");
        }

        if (ContainsAny(ticket.Status, "blocked", "failed"))
        {
            return Attention(
                $"Ticket state is {NormalizeTicketState(ticket.Status)}.",
                "Open the Work Item and resolve the named blocker before starting a run.",
                ProjectBoardWaitingOnKinds.Human,
                "Project team");
        }

        if (string.IsNullOrWhiteSpace(ticket.AcceptanceCriteria))
        {
            return new AttentionProjection(
                false,
                "Acceptance criteria have not been confirmed.",
                "Shape the requirement and confirm acceptance criteria.",
                WaitingOn(ProjectBoardWaitingOnKinds.Human, "Ticket shaping"));
        }

        return new AttentionProjection(false, null, "Open the Work Item and check build readiness.", null);
    }

    private static AttentionProjection Attention(string reason, string nextSafeAction, string kind, string label) =>
        new(true, reason, nextSafeAction, WaitingOn(kind, label));

    private static ProjectBoardWaitingOnReadModel WaitingOn(string kind, string label) => new()
    {
        Kind = kind,
        Label = label
    };

    private static bool ContainsAny(string? value, params string[] needles) =>
        !string.IsNullOrWhiteSpace(value) &&
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeTicketState(string? state) =>
        string.IsNullOrWhiteSpace(state) ? "Unknown" : state.Trim();

    private static string TextOr(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static DateTimeOffset ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value),
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
    };

    private static int StageOrder(string stage) => stage switch
    {
        ProjectBoardStages.Shape => 0,
        ProjectBoardStages.Ticket => 1,
        ProjectBoardStages.Build => 2,
        ProjectBoardStages.Review => 3,
        ProjectBoardStages.Done => 4,
        _ => 5
    };

    private sealed record AttentionProjection(
        bool NeedsAttention,
        string? Reason,
        string NextSafeAction,
        ProjectBoardWaitingOnReadModel? WaitingOn);
}
