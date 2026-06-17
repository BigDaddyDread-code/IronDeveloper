using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlReleaseReadinessDecisionRecordStore : IReleaseReadinessDecisionRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlReleaseReadinessDecisionRecordStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(ReleaseReadinessDecisionRecord record, CancellationToken cancellationToken = default)
    {
        var validation = ReleaseReadinessDecisionRecordValidation.Validate(record);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(record));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_ReleaseReadinessDecisionRecord_Save",
            new
            {
                record.ReleaseReadinessDecisionRecordId,
                record.ProjectId,
                record.ReleaseReadinessReportId,
                ReleaseReadinessReportHash = Normalize(record.ReleaseReadinessReportHash),
                WorkflowRunId = Normalize(record.WorkflowRunId),
                WorkflowStepId = Normalize(record.WorkflowStepId),
                SubjectKind = Normalize(record.SubjectKind),
                SubjectId = Normalize(record.SubjectId),
                SubjectHash = Normalize(record.SubjectHash),
                DecisionStatus = Normalize(record.DecisionStatus),
                record.ReleaseReadinessEvidenceSatisfied,
                record.ReleaseApproved,
                record.DeploymentApproved,
                record.MergeApproved,
                record.SourceApplyExecutedByDecision,
                record.RollbackExecutedByDecision,
                record.WorkflowMutatedByDecision,
                record.GitOperationExecutedByDecision,
                record.ReleaseExecutedByDecision,
                record.HumanReviewRequiredForReleaseApproval,
                record.HumanReviewRequiredForDeployment,
                record.HumanReviewRequiredForMerge,
                ReasonsJson = JsonSerializer.Serialize(record.Reasons.Select(NormalizeReason).ToArray(), JsonOptions),
                EvidenceReferencesJson = JsonSerializer.Serialize(record.EvidenceReferences.Select(Normalize).ToArray(), JsonOptions),
                BoundaryMaximsJson = JsonSerializer.Serialize(record.BoundaryMaxims.Select(Normalize).ToArray(), JsonOptions),
                record.DecidedAtUtc,
                ReleaseReadinessDecisionRecordHash = Normalize(record.ReleaseReadinessDecisionRecordHash),
                Boundary = Normalize(record.Boundary)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<ReleaseReadinessDecisionRecord?> GetAsync(Guid projectId, Guid releaseReadinessDecisionRecordId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ReleaseReadinessDecisionRecordRow>(new CommandDefinition(
            "governance.usp_ReleaseReadinessDecisionRecord_Get",
            new { ProjectId = projectId, ReleaseReadinessDecisionRecordId = releaseReadinessDecisionRecordId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task<ReleaseReadinessDecisionRecord?> GetByRecordHashAsync(Guid projectId, string releaseReadinessDecisionRecordHash, CancellationToken cancellationToken = default)
    {
        RequireText(releaseReadinessDecisionRecordHash, nameof(releaseReadinessDecisionRecordHash));
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ReleaseReadinessDecisionRecordRow>(new CommandDefinition(
            "governance.usp_ReleaseReadinessDecisionRecord_GetByHash",
            new { ProjectId = projectId, ReleaseReadinessDecisionRecordHash = Normalize(releaseReadinessDecisionRecordHash) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListByReleaseReadinessReportAsync(Guid projectId, Guid releaseReadinessReportId, CancellationToken cancellationToken = default) =>
        await QueryListAsync(
            "governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport",
            new { ProjectId = projectId, ReleaseReadinessReportId = releaseReadinessReportId },
            cancellationToken);

    public async Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListByWorkflowRunAsync(Guid projectId, string workflowRunId, CancellationToken cancellationToken = default)
    {
        RequireText(workflowRunId, nameof(workflowRunId));
        return await QueryListAsync(
            "governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun",
            new { ProjectId = projectId, WorkflowRunId = Normalize(workflowRunId) },
            cancellationToken);
    }

    public async Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default)
    {
        RequireText(subjectKind, nameof(subjectKind));
        RequireText(subjectId, nameof(subjectId));
        return await QueryListAsync(
            "governance.usp_ReleaseReadinessDecisionRecord_ListBySubject",
            new { ProjectId = projectId, SubjectKind = Normalize(subjectKind), SubjectId = Normalize(subjectId) },
            cancellationToken);
    }

    private async Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> QueryListAsync(string procedure, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ReleaseReadinessDecisionRecordRow>(new CommandDefinition(
            procedure,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToArray();
    }

    private static ReleaseReadinessDecisionReason NormalizeReason(ReleaseReadinessDecisionReason reason) =>
        new()
        {
            Code = Normalize(reason.Code),
            Severity = Normalize(reason.Severity),
            Field = Normalize(reason.Field),
            Message = Normalize(reason.Message),
        };

    private static string Normalize(string value) => value.Trim();

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }

    private sealed class ReleaseReadinessDecisionRecordRow
    {
        public Guid ReleaseReadinessDecisionRecordId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid ReleaseReadinessReportId { get; init; }
        public string ReleaseReadinessReportHash { get; init; } = string.Empty;
        public string WorkflowRunId { get; init; } = string.Empty;
        public string WorkflowStepId { get; init; } = string.Empty;
        public string SubjectKind { get; init; } = string.Empty;
        public string SubjectId { get; init; } = string.Empty;
        public string SubjectHash { get; init; } = string.Empty;
        public string DecisionStatus { get; init; } = string.Empty;
        public bool ReleaseReadinessEvidenceSatisfied { get; init; }
        public bool ReleaseApproved { get; init; }
        public bool DeploymentApproved { get; init; }
        public bool MergeApproved { get; init; }
        public bool SourceApplyExecutedByDecision { get; init; }
        public bool RollbackExecutedByDecision { get; init; }
        public bool WorkflowMutatedByDecision { get; init; }
        public bool GitOperationExecutedByDecision { get; init; }
        public bool ReleaseExecutedByDecision { get; init; }
        public bool HumanReviewRequiredForReleaseApproval { get; init; }
        public bool HumanReviewRequiredForDeployment { get; init; }
        public bool HumanReviewRequiredForMerge { get; init; }
        public string ReasonsJson { get; init; } = "[]";
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";
        public DateTimeOffset DecidedAtUtc { get; init; }
        public string ReleaseReadinessDecisionRecordHash { get; init; } = string.Empty;
        public string Boundary { get; init; } = string.Empty;

        public ReleaseReadinessDecisionRecord ToRecord() => new()
        {
            ReleaseReadinessDecisionRecordId = ReleaseReadinessDecisionRecordId,
            ProjectId = ProjectId,
            ReleaseReadinessReportId = ReleaseReadinessReportId,
            ReleaseReadinessReportHash = ReleaseReadinessReportHash,
            WorkflowRunId = WorkflowRunId,
            WorkflowStepId = WorkflowStepId,
            SubjectKind = SubjectKind,
            SubjectId = SubjectId,
            SubjectHash = SubjectHash,
            DecisionStatus = DecisionStatus,
            ReleaseReadinessEvidenceSatisfied = ReleaseReadinessEvidenceSatisfied,
            ReleaseApproved = ReleaseApproved,
            DeploymentApproved = DeploymentApproved,
            MergeApproved = MergeApproved,
            SourceApplyExecutedByDecision = SourceApplyExecutedByDecision,
            RollbackExecutedByDecision = RollbackExecutedByDecision,
            WorkflowMutatedByDecision = WorkflowMutatedByDecision,
            GitOperationExecutedByDecision = GitOperationExecutedByDecision,
            ReleaseExecutedByDecision = ReleaseExecutedByDecision,
            HumanReviewRequiredForReleaseApproval = HumanReviewRequiredForReleaseApproval,
            HumanReviewRequiredForDeployment = HumanReviewRequiredForDeployment,
            HumanReviewRequiredForMerge = HumanReviewRequiredForMerge,
            Reasons = DeserializeReasons(ReasonsJson),
            EvidenceReferences = DeserializeList(EvidenceReferencesJson),
            BoundaryMaxims = DeserializeList(BoundaryMaximsJson),
            DecidedAtUtc = DecidedAtUtc,
            ReleaseReadinessDecisionRecordHash = ReleaseReadinessDecisionRecordHash,
            Boundary = Boundary,
        };
    }

    private static IReadOnlyList<ReleaseReadinessDecisionReason> DeserializeReasons(string json) =>
        JsonSerializer.Deserialize<ReleaseReadinessDecisionReason[]>(json, JsonOptions) ?? [];

    private static IReadOnlyList<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
}
