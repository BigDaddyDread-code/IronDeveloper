using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlApprovalDecisionStore : IApprovalDecisionStore
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ApprovalDecisionValidator _validator;

    public SqlApprovalDecisionStore(IDbConnectionFactory connectionFactory, ApprovalDecisionValidator? validator = null)
    {
        _connectionFactory = connectionFactory;
        _validator = validator ?? new ApprovalDecisionValidator();
    }

    public async Task<ApprovalDecisionReadModel> RecordAsync(ApprovalDecisionRecordRequest request, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateRecord(request);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(request));
        }

        var approvalDecisionId = request.ApprovalDecisionId ?? Guid.NewGuid();
        var governanceEventId = request.GovernanceEventId ?? Guid.NewGuid();
        var decision = ApprovalDecisionValidator.NormalizeDecision(request.Decision);
        var actorType = ApprovalDecisionValidator.NormalizeActorType(request.DecidedByActorType);
        var approvalScope = ApprovalDecisionValidator.NormalizeText(request.ApprovalScope);
        var subjectType = ApprovalDecisionValidator.NormalizeText(request.SubjectType);
        var subjectId = ApprovalDecisionValidator.NormalizeText(request.SubjectId);
        var reasonCode = ApprovalDecisionValidator.NormalizeText(request.ReasonCode);
        var actorId = ApprovalDecisionValidator.NormalizeText(request.DecidedByActorId);
        var evidenceJson = request.EvidenceJson.Trim();
        var createdUtc = request.CreatedUtc ?? DateTimeOffset.UtcNow;
        var correlationId = request.CorrelationId ?? approvalDecisionId;

        var eventPayloadJson = JsonSerializer.Serialize(new
        {
            schema = "approval.decision.recorded.v1",
            approvalDecisionId,
            approvalScope,
            approvedSubjectType = subjectType,
            approvedSubjectId = subjectId,
            decision,
            reasonCode,
            evidenceVersion = request.EvidenceVersion,
            grantsExecution = false,
            mutatesSource = false,
            promotesMemory = false,
            createsExternalEffect = false,
            startsWorkflow = false,
            recordedAtUtc = createdUtc
        });

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleAsync<ApprovalDecisionRow>(new CommandDefinition(
            "governance.usp_ApprovalDecision_Record",
            new
            {
                ApprovalDecisionId = approvalDecisionId,
                ProjectId = request.ProjectId,
                GovernanceEventId = governanceEventId,
                ApprovalScope = approvalScope,
                SubjectType = subjectType,
                SubjectId = subjectId,
                Decision = decision,
                ReasonCode = reasonCode,
                Reason = request.Reason?.Trim(),
                DecidedByActorType = actorType,
                DecidedByActorId = actorId,
                SupersedesApprovalDecisionId = request.SupersedesApprovalDecisionId,
                CorrelationId = correlationId,
                CausationId = request.CausationId,
                EvidenceVersion = request.EvidenceVersion,
                EvidenceJson = evidenceJson,
                GovernanceEventPayloadJson = eventPayloadJson,
                CreatedUtc = createdUtc
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row.ToReadModel();
    }

    public async Task<ApprovalDecisionReadModel?> GetAsync(Guid approvalDecisionId, CancellationToken cancellationToken = default)
    {
        if (approvalDecisionId == Guid.Empty)
            throw new ArgumentException("ApprovalDecisionId is required.", nameof(approvalDecisionId));

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ApprovalDecisionRow>(new CommandDefinition(
            "governance.usp_ApprovalDecision_GetById",
            new { ApprovalDecisionId = approvalDecisionId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReadModel();
    }

    public async Task<IReadOnlyList<ApprovalDecisionSummary>> ListForSubjectAsync(ApprovalDecisionsForSubjectQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateSubjectQuery(query);
        if (!validation.IsValid)
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ApprovalDecisionSummaryRow>(new CommandDefinition(
            "governance.usp_ApprovalDecision_ListForSubject",
            new
            {
                ProjectId = query.ProjectId,
                ApprovalScope = ApprovalDecisionValidator.NormalizeText(query.ApprovalScope),
                SubjectType = ApprovalDecisionValidator.NormalizeText(query.SubjectType),
                SubjectId = ApprovalDecisionValidator.NormalizeText(query.SubjectId),
                Take = ApprovalDecisionValidator.NormalizeTake(query.Take)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<ApprovalDecisionSummary>> ListForProjectAsync(ApprovalDecisionsForProjectQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateProjectQuery(query);
        if (!validation.IsValid)
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ApprovalDecisionSummaryRow>(new CommandDefinition(
            "governance.usp_ApprovalDecision_ListForProject",
            new
            {
                ProjectId = query.ProjectId,
                Take = ApprovalDecisionValidator.NormalizeTake(query.Take)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<ApprovalDecisionSummary>> ListForCorrelationAsync(ApprovalDecisionsForCorrelationQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateCorrelationQuery(query);
        if (!validation.IsValid)
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ApprovalDecisionSummaryRow>(new CommandDefinition(
            "governance.usp_ApprovalDecision_ListForCorrelation",
            new
            {
                ProjectId = query.ProjectId,
                CorrelationId = query.CorrelationId,
                Take = ApprovalDecisionValidator.NormalizeTake(query.Take)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private sealed class ApprovalDecisionRow
    {
        public Guid ApprovalDecisionId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid GovernanceEventId { get; set; }
        public string ApprovalScope { get; set; } = string.Empty;
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string DecidedByActorType { get; set; } = string.Empty;
        public string DecidedByActorId { get; set; } = string.Empty;
        public Guid? SupersedesApprovalDecisionId { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public int EvidenceVersion { get; set; }
        public string EvidenceJson { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }

        public ApprovalDecisionReadModel ToReadModel() => new()
        {
            ApprovalDecisionId = ApprovalDecisionId,
            ProjectId = ProjectId,
            GovernanceEventId = GovernanceEventId,
            ApprovalScope = ApprovalScope,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            Decision = Decision,
            ReasonCode = ReasonCode,
            Reason = Reason,
            DecidedByActorType = DecidedByActorType,
            DecidedByActorId = DecidedByActorId,
            SupersedesApprovalDecisionId = SupersedesApprovalDecisionId,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            EvidenceVersion = EvidenceVersion,
            EvidenceJson = EvidenceJson,
            CreatedUtc = CreatedUtc
        };
    }

    private sealed class ApprovalDecisionSummaryRow
    {
        public Guid ApprovalDecisionId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid GovernanceEventId { get; set; }
        public string ApprovalScope { get; set; } = string.Empty;
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string DecidedByActorType { get; set; } = string.Empty;
        public string DecidedByActorId { get; set; } = string.Empty;
        public Guid? SupersedesApprovalDecisionId { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public ApprovalDecisionSummary ToSummary() => new()
        {
            ApprovalDecisionId = ApprovalDecisionId,
            ProjectId = ProjectId,
            GovernanceEventId = GovernanceEventId,
            ApprovalScope = ApprovalScope,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            Decision = Decision,
            ReasonCode = ReasonCode,
            DecidedByActorType = DecidedByActorType,
            DecidedByActorId = DecidedByActorId,
            SupersedesApprovalDecisionId = SupersedesApprovalDecisionId,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedUtc = CreatedUtc
        };
    }
}
