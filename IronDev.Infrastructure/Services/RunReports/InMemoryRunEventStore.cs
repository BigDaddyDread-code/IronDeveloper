using System.Collections.Concurrent;
using System.Threading.Channels;
using IronDev.Core.RunReports;

namespace IronDev.Infrastructure.Services.RunReports;

public sealed class InMemoryRunEventStore : IRunEventStore
{
    private readonly ConcurrentDictionary<string, RunEventBuffer> _runs = new(StringComparer.OrdinalIgnoreCase);

    public Task PublishAsync(RunEventDto runEvent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runEvent.RunId))
            return Task.CompletedTask;

        var normalized = runEvent with
        {
            EventId = runEvent.EventId == Guid.Empty ? Guid.NewGuid() : runEvent.EventId,
            TimestampUtc = runEvent.TimestampUtc == default ? DateTimeOffset.UtcNow : runEvent.TimestampUtc
        };

        var buffer = _runs.GetOrAdd(normalized.RunId, _ => new RunEventBuffer());
        buffer.Publish(normalized);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RunEventDto>> GetEventsAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (!_runs.TryGetValue(runId, out var buffer))
            return Task.FromResult<IReadOnlyList<RunEventDto>>([]);

        return Task.FromResult<IReadOnlyList<RunEventDto>>(buffer.Snapshot());
    }

    public Task<IReadOnlyList<string>> GetRecentRunIdsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var take = limit <= 0 ? 50 : limit;
        var runIds = _runs
            .Select(pair => new
            {
                RunId = pair.Key,
                LastTimestamp = pair.Value.LastTimestamp()
            })
            .OrderByDescending(row => row.LastTimestamp)
            .Take(take)
            .Select(row => row.RunId)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(runIds);
    }

    public async IAsyncEnumerable<RunEventDto> StreamEventsAsync(
        string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = _runs.GetOrAdd(runId, _ => new RunEventBuffer());
        var subscription = buffer.Subscribe();

        try
        {
            foreach (var runEvent in subscription.Existing)
                yield return runEvent;

            if (subscription.Completed)
                yield break;

            await foreach (var runEvent in subscription.Channel.Reader.ReadAllAsync(cancellationToken))
                yield return runEvent;
        }
        finally
        {
            buffer.Unsubscribe(subscription.Channel);
        }
    }

    private sealed class RunEventBuffer
    {
        private readonly object _gate = new();
        private readonly List<RunEventDto> _events = [];
        private readonly List<Channel<RunEventDto>> _subscribers = [];
        private bool _completed;

        public void Publish(RunEventDto runEvent)
        {
            List<Channel<RunEventDto>> subscribers;
            lock (_gate)
            {
                _events.Add(runEvent);
                if (IsTerminal(runEvent.EventType))
                    _completed = true;

                subscribers = _subscribers.ToList();
            }

            foreach (var subscriber in subscribers)
            {
                subscriber.Writer.TryWrite(runEvent);
                if (IsTerminal(runEvent.EventType))
                    subscriber.Writer.TryComplete();
            }
        }

        public IReadOnlyList<RunEventDto> Snapshot()
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }

        public DateTimeOffset LastTimestamp()
        {
            lock (_gate)
            {
                return _events.Count == 0 ? DateTimeOffset.MinValue : _events[^1].TimestampUtc;
            }
        }

        public RunEventSubscription Subscribe()
        {
            lock (_gate)
            {
                var channel = Channel.CreateUnbounded<RunEventDto>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

                var existing = _events.ToArray();
                if (_completed)
                    channel.Writer.TryComplete();
                else
                    _subscribers.Add(channel);

                return new RunEventSubscription(channel, existing, _completed);
            }
        }

        public void Unsubscribe(Channel<RunEventDto> channel)
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }

        private static bool IsTerminal(string eventType) =>
            string.Equals(eventType, "RunCompleted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RunEventSubscription(
        Channel<RunEventDto> Channel,
        IReadOnlyList<RunEventDto> Existing,
        bool Completed);
}
