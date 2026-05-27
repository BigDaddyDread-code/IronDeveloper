namespace IronDev.Core.RunReports;

public interface IRunEventStore
{
    Task PublishAsync(RunEventDto runEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunEventDto>> GetEventsAsync(string runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRecentRunIdsAsync(int limit = 50, CancellationToken cancellationToken = default);

    IAsyncEnumerable<RunEventDto> StreamEventsAsync(string runId, CancellationToken cancellationToken = default);
}

public sealed class NullRunEventStore : IRunEventStore
{
    public static NullRunEventStore Instance { get; } = new();

    private NullRunEventStore()
    {
    }

    public Task PublishAsync(RunEventDto runEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<RunEventDto>> GetEventsAsync(string runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RunEventDto>>([]);

    public Task<IReadOnlyList<string>> GetRecentRunIdsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public async IAsyncEnumerable<RunEventDto> StreamEventsAsync(
        string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
