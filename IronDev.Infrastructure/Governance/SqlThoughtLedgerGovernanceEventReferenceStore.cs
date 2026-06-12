using System.Data;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlThoughtLedgerGovernanceEventReferenceStore : IThoughtLedgerGovernanceEventReferenceStore
{
    private const string RecordProcedure = "governance.usp_ThoughtLedgerGovernanceEventReference_Record";
    private const string GetProcedure = "governance.usp_ThoughtLedgerGovernanceEventReference_GetById";
    private const string ListForThoughtLedgerEntryProcedure = "governance.usp_ThoughtLedgerGovernanceEventReference_ListForThoughtLedgerEntry";
    private const string ListForGovernanceEventProcedure = "governance.usp_ThoughtLedgerGovernanceEventReference_ListForGovernanceEvent";
    private const string ListForCorrelationProcedure = "governance.usp_ThoughtLedgerGovernanceEventReference_ListForCorrelation";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ThoughtLedgerGovernanceEventReferenceValidator _validator;

    public SqlThoughtLedgerGovernanceEventReferenceStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new ThoughtLedgerGovernanceEventReferenceValidator())
    {
    }

    internal SqlThoughtLedgerGovernanceEventReferenceStore(IDbConnectionFactory connectionFactory, ThoughtLedgerGovernanceEventReferenceValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ThoughtLedgerGovernanceEventReference> RecordAsync(
        ThoughtLedgerGovernanceEventReferenceRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalid(_validator.ValidateRecord(request), nameof(request));

        var referenceId = request.ThoughtLedgerGovernanceEventReferenceId.GetValueOrDefault(Guid.NewGuid());
        var referenceType = _validator.NormalizeReferenceType(request.ReferenceType);

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleAsync<ThoughtLedgerGovernanceEventReferenceRow>(
            new CommandDefinition(
                RecordProcedure,
                new
                {
                    ThoughtLedgerGovernanceEventReferenceId = referenceId,
                    request.ProjectId,
                    ThoughtLedgerEntryId = request.ThoughtLedgerEntryId.Trim(),
                    request.GovernanceEventId,
                    ReferenceType = referenceType,
                    ReasonCode = request.ReasonCode.Trim(),
                    request.Reason,
                    request.CorrelationId,
                    request.CausationId,
                    CreatedByActorType = request.CreatedByActorType.Trim(),
                    CreatedByActorId = request.CreatedByActorId.Trim(),
                    request.MetadataVersion,
                    MetadataJson = request.MetadataJson.Trim(),
                    request.CreatedUtc
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return row.ToReference();
    }

    public async Task<ThoughtLedgerGovernanceEventReferenceReadModel?> GetAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        if (referenceId == Guid.Empty)
            return null;

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ThoughtLedgerGovernanceEventReferenceRow>(
            new CommandDefinition(
                GetProcedure,
                new { ThoughtLedgerGovernanceEventReferenceId = referenceId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return row?.ToReadModel();
    }

    public async Task<IReadOnlyList<ThoughtLedgerGovernanceEventReferenceSummary>> ListForThoughtLedgerEntryAsync(
        ThoughtLedgerGovernanceReferencesForThoughtLedgerEntryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(_validator.ValidateEntryQuery(query), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ThoughtLedgerGovernanceEventReferenceSummaryRow>(
            new CommandDefinition(
                ListForThoughtLedgerEntryProcedure,
                new { query.ProjectId, ThoughtLedgerEntryId = query.ThoughtLedgerEntryId.Trim(), query.Take },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<ThoughtLedgerGovernanceEventReferenceSummary>> ListForGovernanceEventAsync(
        ThoughtLedgerGovernanceReferencesForGovernanceEventQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(_validator.ValidateGovernanceEventQuery(query), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ThoughtLedgerGovernanceEventReferenceSummaryRow>(
            new CommandDefinition(
                ListForGovernanceEventProcedure,
                new { query.ProjectId, query.GovernanceEventId, query.Take },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<ThoughtLedgerGovernanceEventReferenceSummary>> ListForCorrelationAsync(
        ThoughtLedgerGovernanceReferencesForCorrelationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(_validator.ValidateCorrelationQuery(query), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ThoughtLedgerGovernanceEventReferenceSummaryRow>(
            new CommandDefinition(
                ListForCorrelationProcedure,
                new { query.ProjectId, query.CorrelationId, query.Take },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private static void ThrowIfInvalid(ThoughtLedgerGovernanceEventReferenceValidationResult result, string paramName)
    {
        if (!result.IsValid)
            throw new ArgumentException(FormatIssues(result.Issues), paramName);
    }

    private static string FormatIssues(IReadOnlyList<ThoughtLedgerGovernanceEventReferenceValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private sealed class ThoughtLedgerGovernanceEventReferenceRow
    {
        public Guid ThoughtLedgerGovernanceEventReferenceId { get; init; }
        public Guid ProjectId { get; init; }
        public string ThoughtLedgerEntryId { get; init; } = string.Empty;
        public Guid GovernanceEventId { get; init; }
        public string ReferenceType { get; init; } = string.Empty;
        public string ReasonCode { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string CreatedByActorType { get; init; } = string.Empty;
        public string CreatedByActorId { get; init; } = string.Empty;
        public int MetadataVersion { get; init; }
        public string MetadataJson { get; init; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; init; }

        public ThoughtLedgerGovernanceEventReference ToReference() =>
            new()
            {
                ThoughtLedgerGovernanceEventReferenceId = ThoughtLedgerGovernanceEventReferenceId,
                ProjectId = ProjectId,
                ThoughtLedgerEntryId = ThoughtLedgerEntryId,
                GovernanceEventId = GovernanceEventId,
                ReferenceType = ReferenceType,
                ReasonCode = ReasonCode,
                Reason = Reason,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                CreatedByActorType = CreatedByActorType,
                CreatedByActorId = CreatedByActorId,
                MetadataVersion = MetadataVersion,
                MetadataJson = MetadataJson,
                CreatedUtc = CreatedUtc
            };

        public ThoughtLedgerGovernanceEventReferenceReadModel ToReadModel() =>
            new()
            {
                ThoughtLedgerGovernanceEventReferenceId = ThoughtLedgerGovernanceEventReferenceId,
                ProjectId = ProjectId,
                ThoughtLedgerEntryId = ThoughtLedgerEntryId,
                GovernanceEventId = GovernanceEventId,
                ReferenceType = ReferenceType,
                ReasonCode = ReasonCode,
                Reason = Reason,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                CreatedByActorType = CreatedByActorType,
                CreatedByActorId = CreatedByActorId,
                MetadataVersion = MetadataVersion,
                MetadataJson = MetadataJson,
                CreatedUtc = CreatedUtc
            };
    }

    private sealed class ThoughtLedgerGovernanceEventReferenceSummaryRow
    {
        public Guid ThoughtLedgerGovernanceEventReferenceId { get; init; }
        public Guid ProjectId { get; init; }
        public string ThoughtLedgerEntryId { get; init; } = string.Empty;
        public Guid GovernanceEventId { get; init; }
        public string ReferenceType { get; init; } = string.Empty;
        public string ReasonCode { get; init; } = string.Empty;
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string CreatedByActorType { get; init; } = string.Empty;
        public string CreatedByActorId { get; init; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; init; }

        public ThoughtLedgerGovernanceEventReferenceSummary ToSummary() =>
            new()
            {
                ThoughtLedgerGovernanceEventReferenceId = ThoughtLedgerGovernanceEventReferenceId,
                ProjectId = ProjectId,
                ThoughtLedgerEntryId = ThoughtLedgerEntryId,
                GovernanceEventId = GovernanceEventId,
                ReferenceType = ReferenceType,
                ReasonCode = ReasonCode,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                CreatedByActorType = CreatedByActorType,
                CreatedByActorId = CreatedByActorId,
                CreatedUtc = CreatedUtc
            };
    }
}