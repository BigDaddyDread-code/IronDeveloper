using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Dapper;
using IronDev.Core.RunReports;
using IronDev.Data;

namespace IronDev.Infrastructure.Services.RunReports;

public sealed class SqlRunEventStore : IRunEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ConcurrentDictionary<string, RunEventBuffer> _liveRuns = new(StringComparer.OrdinalIgnoreCase);
    private int _schemaEnsured;

    public SqlRunEventStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task PublishAsync(RunEventDto runEvent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runEvent.RunId))
            return;

        var normalized = Normalize(runEvent);
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            INSERT INTO dbo.RunEvents
            (
                EventId,
                RunId,
                TimestampUtc,
                EventType,
                Message,
                PayloadJson
            )
            VALUES
            (
                @EventId,
                @RunId,
                @TimestampUtc,
                @EventType,
                @Message,
                @PayloadJson
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                normalized.EventId,
                normalized.RunId,
                TimestampUtc = normalized.TimestampUtc.UtcDateTime,
                normalized.EventType,
                normalized.Message,
                PayloadJson = JsonSerializer.Serialize(normalized.Payload, JsonOptions)
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        _liveRuns.GetOrAdd(normalized.RunId, _ => new RunEventBuffer()).Publish(normalized);
    }

    public async Task<IReadOnlyList<RunEventDto>> GetEventsAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return [];

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT EventId, RunId, TimestampUtc, EventType, Message, PayloadJson
            FROM dbo.RunEvents
            WHERE RunId = @RunId
            ORDER BY TimestampUtc, Id;
            """;

        var rows = await connection.QueryAsync<RunEventRow>(new CommandDefinition(
            sql,
            new { RunId = runId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(ToDto).ToArray();
    }

    public async IAsyncEnumerable<RunEventDto> StreamEventsAsync(
        string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = _liveRuns.GetOrAdd(runId, _ => new RunEventBuffer());
        var subscription = buffer.Subscribe();
        var seen = new HashSet<Guid>();

        try
        {
            var persistedEvents = await GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
            var completedFromHistory = false;
            foreach (var runEvent in persistedEvents)
            {
                seen.Add(runEvent.EventId);
                completedFromHistory |= IsTerminal(runEvent.EventType);
                yield return runEvent;
            }

            if (completedFromHistory || subscription.Completed)
                yield break;

            await foreach (var runEvent in subscription.Channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (runEvent.EventId != Guid.Empty && !seen.Add(runEvent.EventId))
                    continue;

                yield return runEvent;
            }
        }
        finally
        {
            buffer.Unsubscribe(subscription.Channel);
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _schemaEnsured) == 1)
            return;

        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            IF OBJECT_ID('dbo.RunEvents', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.RunEvents
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    EventId UNIQUEIDENTIFIER NOT NULL,
                    RunId NVARCHAR(100) NOT NULL,
                    TimestampUtc DATETIME2 NOT NULL,
                    EventType NVARCHAR(100) NOT NULL,
                    Message NVARCHAR(MAX) NOT NULL,
                    PayloadJson NVARCHAR(MAX) NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_RunEvents_CreatedUtc DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX UX_RunEvents_EventId ON dbo.RunEvents(EventId);
                CREATE INDEX IX_RunEvents_RunId_Timestamp ON dbo.RunEvents(RunId, TimestampUtc, Id);
            END
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        Volatile.Write(ref _schemaEnsured, 1);
    }

    private static RunEventDto Normalize(RunEventDto runEvent) => runEvent with
    {
        EventId = runEvent.EventId == Guid.Empty ? Guid.NewGuid() : runEvent.EventId,
        TimestampUtc = runEvent.TimestampUtc == default ? DateTimeOffset.UtcNow : runEvent.TimestampUtc,
        Payload = runEvent.Payload ?? new Dictionary<string, string>()
    };

    private static RunEventDto ToDto(RunEventRow row) => new()
    {
        EventId = row.EventId,
        RunId = row.RunId,
        TimestampUtc = new DateTimeOffset(DateTime.SpecifyKind(row.TimestampUtc, DateTimeKind.Utc)),
        EventType = row.EventType,
        Message = row.Message,
        Payload = string.IsNullOrWhiteSpace(row.PayloadJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(row.PayloadJson, JsonOptions) ?? new Dictionary<string, string>()
    };

    private static bool IsTerminal(string eventType) =>
        string.Equals(eventType, "RunCompleted", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase);

    private sealed class RunEventRow
    {
        public Guid EventId { get; set; }
        public string RunId { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? PayloadJson { get; set; }
    }

    private sealed class RunEventBuffer
    {
        private readonly object _gate = new();
        private readonly List<Channel<RunEventDto>> _subscribers = [];
        private bool _completed;

        public void Publish(RunEventDto runEvent)
        {
            List<Channel<RunEventDto>> subscribers;
            lock (_gate)
            {
                if (SqlRunEventStore.IsTerminal(runEvent.EventType))
                    _completed = true;

                subscribers = _subscribers.ToList();
            }

            foreach (var subscriber in subscribers)
            {
                subscriber.Writer.TryWrite(runEvent);
                if (SqlRunEventStore.IsTerminal(runEvent.EventType))
                    subscriber.Writer.TryComplete();
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

                if (_completed)
                    channel.Writer.TryComplete();
                else
                    _subscribers.Add(channel);

                return new RunEventSubscription(channel, _completed);
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
    }

    private sealed record RunEventSubscription(Channel<RunEventDto> Channel, bool Completed);
}
