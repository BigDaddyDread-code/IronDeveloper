using Dapper;
using IronDev.Core.Runs;
using IronDev.Data;

namespace IronDev.Infrastructure.Services.Runs;

public sealed class SqlRunStore : IRunStore
{
    private readonly IDbConnectionFactory _connectionFactory;
    private int _schemaEnsured;

    public SqlRunStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<RunRecord> CreateAsync(
        CreateRunRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        var runId = string.IsNullOrWhiteSpace(request.RunId) ? Guid.NewGuid().ToString("D") : request.RunId;
        var existing = await GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var now = DateTimeOffset.UtcNow;
        var run = new RunRecord
        {
            RunId = runId,
            ProjectId = request.ProjectId,
            TicketId = request.TicketId,
            State = RunLifecycleState.Created,
            IsDisposable = request.IsDisposable,
            Summary = request.Summary,
            WorkspacePath = request.WorkspacePath,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            INSERT INTO dbo.Runs
            (
                RunId,
                ProjectId,
                TicketId,
                State,
                IsDisposable,
                Summary,
                FailureReason,
                WorkspacePath,
                CreatedUtc,
                UpdatedUtc
            )
            VALUES
            (
                @RunId,
                @ProjectId,
                @TicketId,
                @State,
                @IsDisposable,
                @Summary,
                @FailureReason,
                @WorkspacePath,
                @CreatedUtc,
                @UpdatedUtc
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, ToRow(run), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return run;
    }

    public async Task<RunRecord?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            SELECT TOP (1)
                RunId,
                ProjectId,
                TicketId,
                State,
                IsDisposable,
                Summary,
                FailureReason,
                WorkspacePath,
                CreatedUtc,
                UpdatedUtc,
                StartedUtc,
                CompletedUtc
            FROM dbo.Runs
            WHERE RunId = @RunId;
            """;

        var row = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(
            sql,
            new { RunId = runId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<RunRecord>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            SELECT TOP (@Limit)
                RunId,
                ProjectId,
                TicketId,
                State,
                IsDisposable,
                Summary,
                FailureReason,
                WorkspacePath,
                CreatedUtc,
                UpdatedUtc,
                StartedUtc,
                CompletedUtc
            FROM dbo.Runs
            ORDER BY UpdatedUtc DESC, Id DESC;
            """;

        var rows = await connection.QueryAsync<RunRow>(new CommandDefinition(
            sql,
            new { Limit = limit <= 0 ? 50 : limit },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(ToRecord).ToArray();
    }

    public async Task<RunRecord?> TransitionAsync(
        RunStateTransition transition,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        var existing = await GetAsync(transition.RunId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return null;

        RunLifecycle.ThrowIfTransitionBlocked(existing.State, transition.State, transition.RunId);
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
            CompletedUtc = transition.State is RunLifecycleState.Completed || RunLifecycle.IsTerminal(transition.State)
                ? existing.CompletedUtc ?? now
                : existing.CompletedUtc
        };

        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            UPDATE dbo.Runs
            SET
                State = @State,
                Summary = @Summary,
                FailureReason = @FailureReason,
                WorkspacePath = @WorkspacePath,
                UpdatedUtc = @UpdatedUtc,
                StartedUtc = @StartedUtc,
                CompletedUtc = @CompletedUtc
            WHERE RunId = @RunId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, ToRow(run), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return run;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _schemaEnsured) == 1)
            return;

        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            IF OBJECT_ID('dbo.Runs', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Runs
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    RunId NVARCHAR(100) NOT NULL,
                    ProjectId INT NULL,
                    TicketId BIGINT NULL,
                    State NVARCHAR(50) NOT NULL,
                    IsDisposable BIT NOT NULL CONSTRAINT DF_Runs_IsDisposable DEFAULT 0,
                    Summary NVARCHAR(MAX) NOT NULL CONSTRAINT DF_Runs_Summary DEFAULT '',
                    FailureReason NVARCHAR(MAX) NULL,
                    WorkspacePath NVARCHAR(1000) NULL,
                    CreatedUtc DATETIME2 NOT NULL,
                    UpdatedUtc DATETIME2 NOT NULL,
                    StartedUtc DATETIME2 NULL,
                    CompletedUtc DATETIME2 NULL
                );

                CREATE UNIQUE INDEX UX_Runs_RunId ON dbo.Runs(RunId);
                CREATE INDEX IX_Runs_ProjectTicketUpdated ON dbo.Runs(ProjectId, TicketId, UpdatedUtc DESC);
            END
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        Volatile.Write(ref _schemaEnsured, 1);
    }

    private static object ToRow(RunRecord run) => new
    {
        run.RunId,
        run.ProjectId,
        run.TicketId,
        State = run.State.ToString(),
        run.IsDisposable,
        run.Summary,
        run.FailureReason,
        run.WorkspacePath,
        CreatedUtc = run.CreatedUtc.UtcDateTime,
        UpdatedUtc = run.UpdatedUtc.UtcDateTime,
        StartedUtc = run.StartedUtc?.UtcDateTime,
        CompletedUtc = run.CompletedUtc?.UtcDateTime
    };

    private static RunRecord ToRecord(RunRow row) => new()
    {
        RunId = row.RunId,
        ProjectId = row.ProjectId,
        TicketId = row.TicketId,
        State = Enum.TryParse<RunLifecycleState>(row.State, ignoreCase: true, out var state)
            ? state
            : RunLifecycleState.Failed,
        IsDisposable = row.IsDisposable,
        Summary = row.Summary,
        FailureReason = row.FailureReason,
        WorkspacePath = row.WorkspacePath,
        CreatedUtc = ToUtc(row.CreatedUtc),
        UpdatedUtc = ToUtc(row.UpdatedUtc),
        StartedUtc = row.StartedUtc is null ? null : ToUtc(row.StartedUtc.Value),
        CompletedUtc = row.CompletedUtc is null ? null : ToUtc(row.CompletedUtc.Value)
    };

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class RunRow
    {
        public string RunId { get; set; } = string.Empty;
        public int? ProjectId { get; set; }
        public long? TicketId { get; set; }
        public string State { get; set; } = string.Empty;
        public bool IsDisposable { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string? FailureReason { get; set; }
        public string? WorkspacePath { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
    }
}
