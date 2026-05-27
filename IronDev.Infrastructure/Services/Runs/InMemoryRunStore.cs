using System.Collections.Concurrent;
using IronDev.Core.Runs;

namespace IronDev.Infrastructure.Services.Runs;

public sealed class InMemoryRunStore : IRunStore
{
    private readonly ConcurrentDictionary<string, RunRecord> _runs = new(StringComparer.OrdinalIgnoreCase);

    public Task<RunRecord> CreateAsync(
        CreateRunRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var run = new RunRecord
        {
            RunId = string.IsNullOrWhiteSpace(request.RunId) ? Guid.NewGuid().ToString("D") : request.RunId,
            ProjectId = request.ProjectId,
            TicketId = request.TicketId,
            State = RunLifecycleState.Created,
            IsDisposable = request.IsDisposable,
            Summary = request.Summary,
            WorkspacePath = request.WorkspacePath,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        _runs[run.RunId] = run;
        return Task.FromResult(run);
    }

    public Task<RunRecord?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_runs.TryGetValue(runId, out var run) ? run : null);
    }

    public Task<IReadOnlyList<RunRecord>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var take = limit <= 0 ? 50 : limit;
        var runs = _runs.Values
            .OrderByDescending(run => run.UpdatedUtc)
            .Take(take)
            .ToArray();

        return Task.FromResult<IReadOnlyList<RunRecord>>(runs);
    }

    public Task<RunRecord?> TransitionAsync(
        RunStateTransition transition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_runs.TryGetValue(transition.RunId, out var existing))
            return Task.FromResult<RunRecord?>(null);

        var now = transition.TimestampUtc ?? DateTimeOffset.UtcNow;
        var run = existing with
        {
            State = transition.State,
            Summary = string.IsNullOrWhiteSpace(transition.Summary) ? existing.Summary : transition.Summary,
            FailureReason = transition.FailureReason ?? existing.FailureReason,
            WorkspacePath = transition.WorkspacePath ?? existing.WorkspacePath,
            UpdatedUtc = now,
            StartedUtc = transition.State == RunLifecycleState.Running && existing.StartedUtc is null
                ? now
                : existing.StartedUtc,
            CompletedUtc = IsTerminal(transition.State) ? now : existing.CompletedUtc
        };

        _runs[transition.RunId] = run;
        return Task.FromResult<RunRecord?>(run);
    }

    private static bool IsTerminal(RunLifecycleState state) =>
        state is RunLifecycleState.Failed
            or RunLifecycleState.Cancelled
            or RunLifecycleState.Completed
            or RunLifecycleState.Promoted
            or RunLifecycleState.Applied;
}
