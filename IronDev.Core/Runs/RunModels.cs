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

    Task<RunRecord?> TransitionAsync(
        RunStateTransition transition,
        CancellationToken cancellationToken = default);
}
