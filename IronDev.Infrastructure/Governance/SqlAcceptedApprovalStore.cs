using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlAcceptedApprovalStore : IAcceptedApprovalStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlAcceptedApprovalStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(AcceptedApprovalRecord record, CancellationToken cancellationToken = default)
    {
        var validation = AcceptedApprovalValidation.Validate(record);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(record));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_AcceptedApproval_Save",
            new
            {
                record.AcceptedApprovalId,
                record.ProjectId,
                ApprovalTargetKind = Normalize(record.ApprovalTargetKind),
                ApprovalTargetId = Normalize(record.ApprovalTargetId),
                ApprovalTargetHash = Normalize(record.ApprovalTargetHash),
                CapabilityCode = Normalize(record.CapabilityCode),
                ApprovalPurpose = Normalize(record.ApprovalPurpose),
                ApprovedByActorId = Normalize(record.ApprovedByActorId),
                ApprovedByActorDisplayName = NormalizeOptional(record.ApprovedByActorDisplayName),
                record.AcceptedAtUtc,
                record.ExpiresAtUtc,
                CorrelationId = Normalize(record.CorrelationId),
                CausationId = Normalize(record.CausationId),
                EvidenceReferencesJson = JsonSerializer.Serialize(record.EvidenceReferences),
                BoundaryMaximsJson = JsonSerializer.Serialize(record.BoundaryMaxims)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<AcceptedApprovalRecord?> GetAsync(Guid projectId, Guid acceptedApprovalId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<AcceptedApprovalRow>(new CommandDefinition(
            "governance.usp_AcceptedApproval_Get",
            new { ProjectId = projectId, AcceptedApprovalId = acceptedApprovalId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<AcceptedApprovalRecord>> ListByTargetAsync(
        Guid projectId,
        string approvalTargetKind,
        string approvalTargetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(approvalTargetKind))
        {
            throw new ArgumentException("Approval target kind is required.", nameof(approvalTargetKind));
        }

        if (string.IsNullOrWhiteSpace(approvalTargetId))
        {
            throw new ArgumentException("Approval target ID is required.", nameof(approvalTargetId));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AcceptedApprovalRow>(new CommandDefinition(
            "governance.usp_AcceptedApproval_ListByTarget",
            new
            {
                ProjectId = projectId,
                ApprovalTargetKind = Normalize(approvalTargetKind),
                ApprovalTargetId = Normalize(approvalTargetId)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToArray();
    }

    public async Task<IReadOnlyList<AcceptedApprovalRecord>> ListByCorrelationAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AcceptedApprovalRow>(new CommandDefinition(
            "governance.usp_AcceptedApproval_ListByCorrelation",
            new { CorrelationId = Normalize(correlationId) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToArray();
    }

    public async Task<IReadOnlyList<AcceptedApprovalRecord>> ListByProjectAndCorrelationAsync(
        Guid projectId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AcceptedApprovalRow>(new CommandDefinition(
            "governance.usp_AcceptedApproval_ListByProjectAndCorrelation",
            new { ProjectId = projectId, CorrelationId = Normalize(correlationId) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToArray();
    }

    private static string Normalize(string value) => value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class AcceptedApprovalRow
    {
        public Guid AcceptedApprovalId { get; init; }
        public Guid ProjectId { get; init; }
        public string ApprovalTargetKind { get; init; } = string.Empty;
        public string ApprovalTargetId { get; init; } = string.Empty;
        public string ApprovalTargetHash { get; init; } = string.Empty;
        public string CapabilityCode { get; init; } = string.Empty;
        public string ApprovalPurpose { get; init; } = string.Empty;
        public string ApprovedByActorId { get; init; } = string.Empty;
        public string? ApprovedByActorDisplayName { get; init; }
        public DateTimeOffset AcceptedAtUtc { get; init; }
        public DateTimeOffset? ExpiresAtUtc { get; init; }
        public string CorrelationId { get; init; } = string.Empty;
        public string CausationId { get; init; } = string.Empty;
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";

        public AcceptedApprovalRecord ToRecord() =>
            new()
            {
                AcceptedApprovalId = AcceptedApprovalId,
                ProjectId = ProjectId,
                ApprovalTargetKind = ApprovalTargetKind,
                ApprovalTargetId = ApprovalTargetId,
                ApprovalTargetHash = ApprovalTargetHash,
                CapabilityCode = CapabilityCode,
                ApprovalPurpose = ApprovalPurpose,
                ApprovedByActorId = ApprovedByActorId,
                ApprovedByActorDisplayName = ApprovedByActorDisplayName,
                AcceptedAtUtc = AcceptedAtUtc,
                ExpiresAtUtc = ExpiresAtUtc,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                EvidenceReferences = DeserializeList(EvidenceReferencesJson),
                BoundaryMaxims = DeserializeList(BoundaryMaximsJson)
            };

        private static IReadOnlyList<string> DeserializeList(string json) =>
            JsonSerializer.Deserialize<string[]>(json) ?? [];
    }
}
