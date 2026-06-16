using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlControlledDryRunReceiptStore : IControlledDryRunReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlControlledDryRunReceiptStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(ControlledDryRunExecutionAudit audit, CancellationToken cancellationToken = default)
    {
        var validation = ControlledDryRunExecutionAuditValidation.Validate(audit);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(audit));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_ControlledDryRunReceipt_Save",
            new
            {
                audit.DryRunExecutionAuditId,
                audit.ProjectId,
                audit.ControlledDryRunRequestId,
                audit.PolicySatisfactionId,
                PolicySatisfactionHash = Normalize(audit.PolicySatisfactionHash),
                SubjectKind = Normalize(audit.SubjectKind),
                SubjectId = Normalize(audit.SubjectId),
                SubjectHash = Normalize(audit.SubjectHash),
                WorkspaceId = Normalize(audit.WorkspaceId),
                WorkspaceKind = Normalize(audit.WorkspaceKind),
                WorkspaceBoundaryHash = Normalize(audit.WorkspaceBoundaryHash),
                SourceSnapshotReference = Normalize(audit.SourceSnapshotReference),
                ValidationPlanId = Normalize(audit.ValidationPlanId),
                ValidationPlanHash = Normalize(audit.ValidationPlanHash),
                audit.StartedAtUtc,
                audit.CompletedAtUtc,
                audit.DryRunCompleted,
                audit.DryRunSucceeded,
                ExecutionReportHash = Normalize(audit.ExecutionReportHash),
                AuditHash = Normalize(audit.AuditHash),
                CommandAuditsJson = JsonSerializer.Serialize(audit.CommandAudits.Select(command => new CommandAuditDto(
                    Normalize(command.CommandId),
                    Normalize(command.WorkingDirectory),
                    Normalize(command.Executable),
                    Normalize(command.CommandHash),
                    command.ExitCode,
                    command.TimedOut,
                    Normalize(command.StandardOutputSummaryHash),
                    Normalize(command.StandardErrorSummaryHash),
                    Normalize(command.StandardOutputSummary),
                    Normalize(command.StandardErrorSummary))), JsonOptions),
                EvidenceReferencesJson = JsonSerializer.Serialize(audit.EvidenceReferences.Select(Normalize).ToArray(), JsonOptions),
                BoundaryMaximsJson = JsonSerializer.Serialize(audit.BoundaryMaxims.Select(Normalize).ToArray(), JsonOptions),
                BoundaryText = Normalize(audit.Boundary)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<ControlledDryRunExecutionAudit?> GetAsync(Guid projectId, Guid dryRunExecutionAuditId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ControlledDryRunReceiptRow>(new CommandDefinition(
            "governance.usp_ControlledDryRunReceipt_Get",
            new { ProjectId = projectId, DryRunExecutionAuditId = dryRunExecutionAuditId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToAudit();
    }

    public async Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByRequestAsync(Guid projectId, Guid controlledDryRunRequestId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ControlledDryRunReceiptRow>(new CommandDefinition(
            "governance.usp_ControlledDryRunReceipt_ListByRequest",
            new { ProjectId = projectId, ControlledDryRunRequestId = controlledDryRunRequestId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToAudit()).ToArray();
    }

    public async Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByPolicySatisfactionAsync(Guid projectId, Guid policySatisfactionId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ControlledDryRunReceiptRow>(new CommandDefinition(
            "governance.usp_ControlledDryRunReceipt_ListByPolicySatisfaction",
            new { ProjectId = projectId, PolicySatisfactionId = policySatisfactionId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToAudit()).ToArray();
    }

    public async Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default)
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
        var rows = await connection.QueryAsync<ControlledDryRunReceiptRow>(new CommandDefinition(
            "governance.usp_ControlledDryRunReceipt_ListBySubject",
            new { ProjectId = projectId, SubjectKind = Normalize(subjectKind), SubjectId = Normalize(subjectId) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToAudit()).ToArray();
    }

    public async Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByAuditHashAsync(Guid projectId, string auditHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auditHash))
        {
            throw new ArgumentException("Audit hash is required.", nameof(auditHash));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ControlledDryRunReceiptRow>(new CommandDefinition(
            "governance.usp_ControlledDryRunReceipt_ListByAuditHash",
            new { ProjectId = projectId, AuditHash = Normalize(auditHash) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToAudit()).ToArray();
    }

    private static string Normalize(string value) => value.Trim();

    private sealed class ControlledDryRunReceiptRow
    {
        public Guid DryRunExecutionAuditId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid ControlledDryRunRequestId { get; init; }
        public Guid PolicySatisfactionId { get; init; }
        public string PolicySatisfactionHash { get; init; } = string.Empty;
        public string SubjectKind { get; init; } = string.Empty;
        public string SubjectId { get; init; } = string.Empty;
        public string SubjectHash { get; init; } = string.Empty;
        public string WorkspaceId { get; init; } = string.Empty;
        public string WorkspaceKind { get; init; } = string.Empty;
        public string WorkspaceBoundaryHash { get; init; } = string.Empty;
        public string SourceSnapshotReference { get; init; } = string.Empty;
        public string ValidationPlanId { get; init; } = string.Empty;
        public string ValidationPlanHash { get; init; } = string.Empty;
        public DateTimeOffset StartedAtUtc { get; init; }
        public DateTimeOffset CompletedAtUtc { get; init; }
        public bool DryRunCompleted { get; init; }
        public bool DryRunSucceeded { get; init; }
        public string ExecutionReportHash { get; init; } = string.Empty;
        public string AuditHash { get; init; } = string.Empty;
        public string CommandAuditsJson { get; init; } = "[]";
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";
        public string BoundaryText { get; init; } = string.Empty;

        public ControlledDryRunExecutionAudit ToAudit() => new()
        {
            DryRunExecutionAuditId = DryRunExecutionAuditId,
            ProjectId = ProjectId,
            ControlledDryRunRequestId = ControlledDryRunRequestId,
            PolicySatisfactionId = PolicySatisfactionId,
            PolicySatisfactionHash = PolicySatisfactionHash,
            SubjectKind = SubjectKind,
            SubjectId = SubjectId,
            SubjectHash = SubjectHash,
            WorkspaceId = WorkspaceId,
            WorkspaceKind = WorkspaceKind,
            WorkspaceBoundaryHash = WorkspaceBoundaryHash,
            SourceSnapshotReference = SourceSnapshotReference,
            ValidationPlanId = ValidationPlanId,
            ValidationPlanHash = ValidationPlanHash,
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            DryRunCompleted = DryRunCompleted,
            DryRunSucceeded = DryRunSucceeded,
            ExecutionReportHash = ExecutionReportHash,
            AuditHash = AuditHash,
            CommandAudits = DeserializeCommands(CommandAuditsJson),
            EvidenceReferences = DeserializeList(EvidenceReferencesJson),
            BoundaryMaxims = DeserializeList(BoundaryMaximsJson),
            Boundary = BoundaryText
        };
    }

    private static IReadOnlyList<ControlledDryRunCommandAudit> DeserializeCommands(string json) =>
        JsonSerializer.Deserialize<CommandAuditDto[]>(json, JsonOptions)?.Select(command => new ControlledDryRunCommandAudit
        {
            CommandId = command.CommandId,
            WorkingDirectory = command.WorkingDirectory,
            Executable = command.Executable,
            CommandHash = command.CommandHash,
            ExitCode = command.ExitCode,
            TimedOut = command.TimedOut,
            StandardOutputSummaryHash = command.StandardOutputSummaryHash,
            StandardErrorSummaryHash = command.StandardErrorSummaryHash,
            StandardOutputSummary = command.StandardOutputSummary,
            StandardErrorSummary = command.StandardErrorSummary
        }).ToArray() ?? [];

    private static IReadOnlyList<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];

    private sealed record CommandAuditDto(
        string CommandId,
        string WorkingDirectory,
        string Executable,
        string CommandHash,
        int ExitCode,
        bool TimedOut,
        string StandardOutputSummaryHash,
        string StandardErrorSummaryHash,
        string StandardOutputSummary,
        string StandardErrorSummary);
}