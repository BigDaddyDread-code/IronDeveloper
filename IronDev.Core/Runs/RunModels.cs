namespace IronDev.Core.Runs;

public enum RunLifecycleState
{
    Created,
    Running,
    PausedForApproval,
    Failed,
    Cancelled,
    Completed,
    Promoted,
    Applied
}

public static class RunLifecycle
{
    public static bool IsTerminal(RunLifecycleState state) =>
        state is RunLifecycleState.Failed
            or RunLifecycleState.Cancelled
            or RunLifecycleState.Applied;

    public static bool IsTransitionAllowed(RunLifecycleState from, RunLifecycleState to) =>
        from == to ||
        from switch
        {
            RunLifecycleState.Created => to is RunLifecycleState.Running
                or RunLifecycleState.Failed
                or RunLifecycleState.Cancelled,
            RunLifecycleState.Running => to is RunLifecycleState.PausedForApproval
                or RunLifecycleState.Completed
                or RunLifecycleState.Failed
                or RunLifecycleState.Cancelled,
            RunLifecycleState.PausedForApproval => to is RunLifecycleState.Running
                or RunLifecycleState.Completed
                or RunLifecycleState.Failed
                or RunLifecycleState.Cancelled,
            RunLifecycleState.Completed => to is RunLifecycleState.Promoted,
            RunLifecycleState.Promoted => to is RunLifecycleState.Applied,
            RunLifecycleState.Failed
                or RunLifecycleState.Cancelled
                or RunLifecycleState.Applied => false,
            _ => false
        };

    public static void ThrowIfTransitionBlocked(RunLifecycleState from, RunLifecycleState to, string runId)
    {
        if (!IsTransitionAllowed(from, to))
            throw new InvalidOperationException($"Run '{runId}' cannot transition from {from} to {to}.");
    }
}

public sealed record RunRecord
{
    public required string RunId { get; init; }
    public int? ProjectId { get; init; }
    public long? TicketId { get; init; }
    public required RunLifecycleState State { get; init; }
    public bool IsDisposable { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public string? WorkspacePath { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
}

public sealed record CreateRunRequest
{
    public string? RunId { get; init; }
    public int? ProjectId { get; init; }
    public long? TicketId { get; init; }
    public bool IsDisposable { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? WorkspacePath { get; init; }
}

public sealed record RunStateTransition
{
    public required string RunId { get; init; }
    public required RunLifecycleState State { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public string? WorkspacePath { get; init; }
    public DateTimeOffset? TimestampUtc { get; init; }
}

public interface IRunStore
{
    Task<RunRecord> CreateAsync(
        CreateRunRequest request,
        CancellationToken cancellationToken = default);

    Task<RunRecord?> GetAsync(
        string runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunRecord>> GetRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<RunRecord>> GetRecentForProjectAsync(
        int projectId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = limit <= 0 ? 200 : limit;
        var runs = await GetRecentAsync(boundedLimit, cancellationToken).ConfigureAwait(false);
        return runs
            .Where(run => run.ProjectId == projectId)
            .OrderByDescending(run => run.UpdatedUtc)
            .Take(boundedLimit)
            .ToArray();
    }

    Task<RunRecord?> TransitionAsync(
        RunStateTransition transition,
        CancellationToken cancellationToken = default);
}
