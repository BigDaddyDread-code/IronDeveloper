using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlToolRequestStore : IToolRequestStore
{
    private const string CreateProcedure = "governance.usp_ToolRequest_Create";
    private const string GetProcedure = "governance.usp_ToolRequest_GetById";
    private const string ListForProjectProcedure = "governance.usp_ToolRequest_ListForProject";
    private const string ListForCorrelationProcedure = "governance.usp_ToolRequest_ListForCorrelation";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ToolRequestValidator _validator;

    public SqlToolRequestStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new ToolRequestValidator())
    {
    }

    internal SqlToolRequestStore(IDbConnectionFactory connectionFactory, ToolRequestValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ToolRequestReadModel> CreateAsync(
        ToolRequestCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalid(_validator.ValidateCreate(request), nameof(request));

        var normalized = _validator.Normalize(request);
        var toolRequestId = normalized.ToolRequestId.GetValueOrDefault(Guid.NewGuid());
        var governanceEventId = Guid.NewGuid();
        var eventPayloadJson = JsonSerializer.Serialize(new
        {
            schema = "tool.request.created.v1",
            toolRequestId,
            toolName = normalized.ToolName,
            operationName = normalized.OperationName,
            requestPayloadVersion = normalized.RequestPayloadVersion
        });

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleAsync<ToolRequestRow>(
            new CommandDefinition(
                CreateProcedure,
                new
                {
                    ToolRequestId = toolRequestId,
                    normalized.ProjectId,
                    GovernanceEventId = governanceEventId,
                    normalized.ToolName,
                    normalized.OperationName,
                    normalized.RequestedByActorType,
                    normalized.RequestedByActorId,
                    normalized.CorrelationId,
                    normalized.CausationId,
                    normalized.Purpose,
                    normalized.RequestPayloadVersion,
                    normalized.RequestPayloadJson,
                    GovernanceEventPayloadJson = eventPayloadJson
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return row.ToReadModel();
    }

    public async Task<ToolRequestReadModel?> GetAsync(Guid toolRequestId, CancellationToken cancellationToken = default)
    {
        if (toolRequestId == Guid.Empty)
            return null;

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ToolRequestRow>(
            new CommandDefinition(
                GetProcedure,
                new { ToolRequestId = toolRequestId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return row?.ToReadModel();
    }

    public async Task<IReadOnlyList<ToolRequestSummary>> ListForProjectAsync(
        ToolRequestsForProjectQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(_validator.ValidateProjectQuery(query), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ToolRequestSummaryRow>(
            new CommandDefinition(
                ListForProjectProcedure,
                new { query.ProjectId, query.Take },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<ToolRequestSummary>> ListForCorrelationAsync(
        ToolRequestsForCorrelationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(_validator.ValidateCorrelationQuery(query), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ToolRequestSummaryRow>(
            new CommandDefinition(
                ListForCorrelationProcedure,
                new { query.CorrelationId, query.Take },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private static void ThrowIfInvalid(IReadOnlyList<ToolRequestValidationIssue> issues, string paramName)
    {
        if (issues.Count > 0)
            throw new ArgumentException(FormatIssues(issues), paramName);
    }

    private static string FormatIssues(IReadOnlyList<ToolRequestValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private sealed class ToolRequestRow
    {
        public Guid ToolRequestId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid GovernanceEventId { get; init; }
        public string ToolName { get; init; } = string.Empty;
        public string OperationName { get; init; } = string.Empty;
        public string RequestedByActorType { get; init; } = string.Empty;
        public string RequestedByActorId { get; init; } = string.Empty;
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string? Purpose { get; init; }
        public int RequestPayloadVersion { get; init; }
        public string RequestPayloadJson { get; init; } = string.Empty;
        public string Status { get; init; } = nameof(ToolRequestStatus.Recorded);
        public DateTimeOffset CreatedUtc { get; init; }
        public DateTimeOffset? CancelledUtc { get; init; }
        public string? CancelledReason { get; init; }

        public ToolRequestReadModel ToReadModel() =>
            new()
            {
                ToolRequestId = ToolRequestId,
                ProjectId = ProjectId,
                GovernanceEventId = GovernanceEventId,
                ToolName = ToolName,
                OperationName = OperationName,
                RequestedByActorType = RequestedByActorType,
                RequestedByActorId = RequestedByActorId,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                Purpose = Purpose,
                RequestPayloadVersion = RequestPayloadVersion,
                RequestPayloadJson = RequestPayloadJson,
                Status = Enum.Parse<ToolRequestStatus>(Status),
                CreatedUtc = CreatedUtc,
                CancelledUtc = CancelledUtc,
                CancelledReason = CancelledReason
            };
    }

    private sealed class ToolRequestSummaryRow
    {
        public Guid ToolRequestId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid GovernanceEventId { get; init; }
        public string ToolName { get; init; } = string.Empty;
        public string OperationName { get; init; } = string.Empty;
        public string RequestedByActorType { get; init; } = string.Empty;
        public string RequestedByActorId { get; init; } = string.Empty;
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string Status { get; init; } = nameof(ToolRequestStatus.Recorded);
        public DateTimeOffset CreatedUtc { get; init; }

        public ToolRequestSummary ToSummary() =>
            new()
            {
                ToolRequestId = ToolRequestId,
                ProjectId = ProjectId,
                GovernanceEventId = GovernanceEventId,
                ToolName = ToolName,
                OperationName = OperationName,
                RequestedByActorType = RequestedByActorType,
                RequestedByActorId = RequestedByActorId,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                Status = Enum.Parse<ToolRequestStatus>(Status),
                CreatedUtc = CreatedUtc
            };
    }
}
