using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlPolicySatisfactionStore : IPolicySatisfactionStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlPolicySatisfactionStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(PolicySatisfactionRecord record, CancellationToken cancellationToken = default)
    {
        var validation = PolicySatisfactionValidation.Validate(record);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(record));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_PolicySatisfaction_Save",
            new
            {
                record.PolicySatisfactionId,
                record.ProjectId,
                PolicyCode = Normalize(record.PolicyCode),
                PolicyVersion = Normalize(record.PolicyVersion),
                SubjectKind = Normalize(record.SubjectKind),
                SubjectId = Normalize(record.SubjectId),
                SubjectHash = Normalize(record.SubjectHash),
                CapabilityCode = Normalize(record.CapabilityCode),
                record.AcceptedApprovalId,
                ApprovalRequirementHash = Normalize(record.ApprovalRequirementHash),
                record.ApprovalEvaluatedAtUtc,
                record.SatisfiedAtUtc,
                record.ExpiresAtUtc,
                CorrelationId = Normalize(record.CorrelationId),
                CausationId = Normalize(record.CausationId),
                EvidenceReferencesJson = JsonSerializer.Serialize(record.EvidenceReferences),
                BoundaryMaximsJson = JsonSerializer.Serialize(record.BoundaryMaxims)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<PolicySatisfactionRecord?> GetAsync(Guid projectId, Guid policySatisfactionId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<PolicySatisfactionRow>(new CommandDefinition(
            "governance.usp_PolicySatisfaction_Get",
            new { ProjectId = projectId, PolicySatisfactionId = policySatisfactionId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<PolicySatisfactionRecord>> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectKind))
        {
            throw new ArgumentException("Subject kind is required.", nameof(subjectKind));
        }

        if (string.IsNullOrWhiteSpace(subjectId))
        {
            throw new ArgumentException("Subject ID is required.", nameof(subjectId));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PolicySatisfactionRow>(new CommandDefinition(
            "governance.usp_PolicySatisfaction_ListBySubject",
            new { ProjectId = projectId, SubjectKind = Normalize(subjectKind), SubjectId = Normalize(subjectId) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToArray();
    }

    public async Task<IReadOnlyList<PolicySatisfactionRecord>> ListByAcceptedApprovalAsync(
        Guid projectId,
        Guid acceptedApprovalId,
        CancellationToken cancellationToken = default)
    {
        if (acceptedApprovalId == Guid.Empty)
        {
            throw new ArgumentException("Accepted approval ID is required.", nameof(acceptedApprovalId));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PolicySatisfactionRow>(new CommandDefinition(
            "governance.usp_PolicySatisfaction_ListByAcceptedApproval",
            new { ProjectId = projectId, AcceptedApprovalId = acceptedApprovalId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToArray();
    }

    public async Task<IReadOnlyList<PolicySatisfactionRecord>> ListByProjectAndCorrelationAsync(
        Guid projectId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PolicySatisfactionRow>(new CommandDefinition(
            "governance.usp_PolicySatisfaction_ListByProjectAndCorrelation",
            new { ProjectId = projectId, CorrelationId = Normalize(correlationId) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToArray();
    }

    private static string Normalize(string value) => value.Trim();

    private sealed class PolicySatisfactionRow
    {
        public Guid PolicySatisfactionId { get; init; }
        public Guid ProjectId { get; init; }
        public string PolicyCode { get; init; } = string.Empty;
        public string PolicyVersion { get; init; } = string.Empty;
        public string SubjectKind { get; init; } = string.Empty;
        public string SubjectId { get; init; } = string.Empty;
        public string SubjectHash { get; init; } = string.Empty;
        public string CapabilityCode { get; init; } = string.Empty;
        public Guid AcceptedApprovalId { get; init; }
        public string ApprovalRequirementHash { get; init; } = string.Empty;
        public DateTimeOffset ApprovalEvaluatedAtUtc { get; init; }
        public DateTimeOffset SatisfiedAtUtc { get; init; }
        public DateTimeOffset? ExpiresAtUtc { get; init; }
        public string CorrelationId { get; init; } = string.Empty;
        public string CausationId { get; init; } = string.Empty;
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";

        public PolicySatisfactionRecord ToRecord() =>
            new()
            {
                PolicySatisfactionId = PolicySatisfactionId,
                ProjectId = ProjectId,
                PolicyCode = PolicyCode,
                PolicyVersion = PolicyVersion,
                SubjectKind = SubjectKind,
                SubjectId = SubjectId,
                SubjectHash = SubjectHash,
                CapabilityCode = CapabilityCode,
                AcceptedApprovalId = AcceptedApprovalId,
                ApprovalRequirementHash = ApprovalRequirementHash,
                ApprovalEvaluatedAtUtc = ApprovalEvaluatedAtUtc,
                SatisfiedAtUtc = SatisfiedAtUtc,
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
