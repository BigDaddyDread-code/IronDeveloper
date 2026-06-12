using System.Data;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlGovernanceEventStore : IGovernanceEventStore
{
    private const string AppendProcedure = "governance.AppendGovernanceEvent";
    private const string GetProcedure = "governance.GetGovernanceEvent";
    private const string ListForProjectProcedure = "governance.ListGovernanceEventsForProject";
    private const string ListForCorrelationProcedure = "governance.ListGovernanceEventsForCorrelation";
    private const string ListForSubjectProcedure = "governance.ListGovernanceEventsForSubject";
    private const string ListCausedByProcedure = "governance.ListGovernanceEventsCausedBy";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly GovernanceEventValidator _validator;

    public SqlGovernanceEventStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new GovernanceEventValidator())
    {
    }

    internal SqlGovernanceEventStore(IDbConnectionFactory connectionFactory, GovernanceEventValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<GovernanceEvent> AppendAsync(
        GovernanceEventAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ThrowIfInvalid(_validator.ValidateAppend(request), nameof(request));

        var normalized = _validator.Normalize(request);
        var eventId = Guid.NewGuid();

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleAsync<GovernanceEventRow>(
            new CommandDefinition(
                AppendProcedure,
                new
                {
                    EventId = eventId,
                    normalized.ProjectId,
                    normalized.EventType,
                    normalized.ActorType,
                    normalized.ActorId,
                    normalized.CorrelationId,
                    normalized.CausationId,
                    normalized.SubjectType,
                    normalized.SubjectId,
                    normalized.PayloadVersion,
                    normalized.PayloadJson
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return row.ToEvent();
    }

    public async Task<GovernanceEventReadModel?> GetAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
            return null;

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<GovernanceEventRow>(
            new CommandDefinition(
                GetProcedure,
                new { EventId = eventId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return row?.ToReadModel();
    }

    public async Task<IReadOnlyList<GovernanceEventSummary>> ListForProjectAsync(
        GovernanceEventsForProjectQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(_validator.ValidateProjectQuery(query), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<GovernanceEventSummaryRow>(
            new CommandDefinition(
                ListForProjectProcedure,
                new { query.ProjectId, query.Take, query.BeforeCreatedUtc },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<GovernanceEventSummary>> ListForCorrelationAsync(
        GovernanceEventsForCorrelationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(_validator.ValidateCorrelationQuery(query), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<GovernanceEventSummaryRow>(
            new CommandDefinition(
                ListForCorrelationProcedure,
                new { query.CorrelationId, query.Take },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<GovernanceEventSummary>> ListForSubjectAsync(
        GovernanceEventsForSubjectQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(_validator.ValidateSubjectQuery(query), nameof(query));
        var normalized = _validator.Normalize(query);

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<GovernanceEventSummaryRow>(
            new CommandDefinition(
                ListForSubjectProcedure,
                new { normalized.ProjectId, normalized.SubjectType, normalized.SubjectId, normalized.Take },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<GovernanceEventSummary>> ListCausedByAsync(
        GovernanceEventsCausedByQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(_validator.ValidateCausedByQuery(query), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<GovernanceEventSummaryRow>(
            new CommandDefinition(
                ListCausedByProcedure,
                new { query.CausationId, query.Take },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private static void ThrowIfInvalid(IReadOnlyList<GovernanceEventValidationIssue> issues, string paramName)
    {
        if (issues.Count > 0)
            throw new ArgumentException(FormatIssues(issues), paramName);
    }

    private static string FormatIssues(IReadOnlyList<GovernanceEventValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private sealed class GovernanceEventRow
    {
        public Guid EventId { get; init; }
        public Guid ProjectId { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string ActorType { get; init; } = string.Empty;
        public string ActorId { get; init; } = string.Empty;
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string? SubjectType { get; init; }
        public string? SubjectId { get; init; }
        public int PayloadVersion { get; init; }
        public string PayloadJson { get; init; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; init; }

        public GovernanceEvent ToEvent() =>
            new()
            {
                EventId = EventId,
                ProjectId = ProjectId,
                EventType = EventType,
                ActorType = ActorType,
                ActorId = ActorId,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                SubjectType = SubjectType,
                SubjectId = SubjectId,
                PayloadVersion = PayloadVersion,
                PayloadJson = PayloadJson,
                CreatedUtc = CreatedUtc
            };

        public GovernanceEventReadModel ToReadModel() =>
            new()
            {
                EventId = EventId,
                ProjectId = ProjectId,
                EventType = EventType,
                ActorType = ActorType,
                ActorId = ActorId,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                SubjectType = SubjectType,
                SubjectId = SubjectId,
                PayloadVersion = PayloadVersion,
                PayloadJson = PayloadJson,
                CreatedUtc = CreatedUtc
            };
    }

    private sealed class GovernanceEventSummaryRow
    {
        public Guid EventId { get; init; }
        public Guid ProjectId { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string ActorType { get; init; } = string.Empty;
        public string ActorId { get; init; } = string.Empty;
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string? SubjectType { get; init; }
        public string? SubjectId { get; init; }
        public int PayloadVersion { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }

        public GovernanceEventSummary ToSummary() =>
            new()
            {
                EventId = EventId,
                ProjectId = ProjectId,
                EventType = EventType,
                ActorType = ActorType,
                ActorId = ActorId,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                SubjectType = SubjectType,
                SubjectId = SubjectId,
                PayloadVersion = PayloadVersion,
                CreatedUtc = CreatedUtc
            };
    }
}