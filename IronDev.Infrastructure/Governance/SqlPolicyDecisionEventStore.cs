using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlPolicyDecisionEventStore : IPolicyDecisionEventStore
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly PolicyDecisionValidator _validator;

    public SqlPolicyDecisionEventStore(IDbConnectionFactory connectionFactory, PolicyDecisionValidator? validator = null)
    {
        _connectionFactory = connectionFactory;
        _validator = validator ?? new PolicyDecisionValidator();
    }

    public async Task<PolicyDecisionReadModel> RecordAsync(PolicyDecisionRecordRequest request, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateRecord(request);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(request));
        }

        var policyDecisionEventId = request.PolicyDecisionEventId ?? Guid.NewGuid();
        var governanceEventId = request.GovernanceEventId ?? Guid.NewGuid();
        var policyScope = PolicyDecisionValidator.NormalizeText(request.PolicyScope);
        var policyName = PolicyDecisionValidator.NormalizeText(request.PolicyName);
        var subjectType = PolicyDecisionValidator.NormalizeText(request.SubjectType);
        var subjectId = PolicyDecisionValidator.NormalizeText(request.SubjectId);
        var decision = PolicyDecisionValidator.NormalizeDecision(request.Decision);
        var requirementCode = PolicyDecisionValidator.NormalizeText(request.RequirementCode);
        var reasonCode = PolicyDecisionValidator.NormalizeText(request.ReasonCode);
        var actorType = PolicyDecisionValidator.NormalizeText(request.DecidedByActorType);
        var actorId = PolicyDecisionValidator.NormalizeText(request.DecidedByActorId);
        var createdUtc = request.CreatedUtc ?? DateTimeOffset.UtcNow;
        var correlationId = request.CorrelationId ?? policyDecisionEventId;

        var eventPayloadJson = JsonSerializer.Serialize(new
        {
            schema = "policy.decision.recorded.v1",
            policyDecisionEventId,
            policyScope,
            policyName,
            policyVersion = request.PolicyVersion,
            policySubjectType = subjectType,
            policySubjectId = subjectId,
            decision,
            requirementCode,
            reasonCode,
            grantsApproval = false,
            grantsExecution = false,
            mutatesSource = false,
            promotesMemory = false,
            startsWorkflow = false,
            satisfiesPolicy = false,
            transfersAuthority = false,
            recordedAtUtc = createdUtc
        });

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleAsync<PolicyDecisionRow>(new CommandDefinition(
            "governance.usp_PolicyDecisionEvent_Record",
            new
            {
                PolicyDecisionEventId = policyDecisionEventId,
                ProjectId = request.ProjectId,
                GovernanceEventId = governanceEventId,
                PolicyScope = policyScope,
                PolicyName = policyName,
                PolicyVersion = request.PolicyVersion,
                SubjectType = subjectType,
                SubjectId = subjectId,
                Decision = decision,
                RequirementCode = requirementCode,
                ReasonCode = reasonCode,
                Reason = request.Reason?.Trim(),
                DecidedByActorType = actorType,
                DecidedByActorId = actorId,
                RelatedToolRequestId = request.RelatedToolRequestId,
                RelatedToolGateDecisionId = request.RelatedToolGateDecisionId,
                RelatedApprovalDecisionId = request.RelatedApprovalDecisionId,
                CorrelationId = correlationId,
                CausationId = request.CausationId,
                EvidenceVersion = request.EvidenceVersion,
                EvidenceJson = request.EvidenceJson.Trim(),
                GovernanceEventPayloadJson = eventPayloadJson,
                CreatedUtc = createdUtc
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row.ToReadModel();
    }

    public async Task<PolicyDecisionReadModel?> GetAsync(Guid policyDecisionEventId, CancellationToken cancellationToken = default)
    {
        if (policyDecisionEventId == Guid.Empty)
            throw new ArgumentException("PolicyDecisionEventId is required.", nameof(policyDecisionEventId));

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<PolicyDecisionRow>(new CommandDefinition(
            "governance.usp_PolicyDecisionEvent_GetById",
            new { PolicyDecisionEventId = policyDecisionEventId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReadModel();
    }

    public async Task<IReadOnlyList<PolicyDecisionSummary>> ListForSubjectAsync(PolicyDecisionsForSubjectQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateSubjectQuery(query);
        if (!validation.IsValid)
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PolicyDecisionSummaryRow>(new CommandDefinition(
            "governance.usp_PolicyDecisionEvent_ListForSubject",
            new
            {
                ProjectId = query.ProjectId,
                PolicyScope = PolicyDecisionValidator.NormalizeText(query.PolicyScope),
                SubjectType = PolicyDecisionValidator.NormalizeText(query.SubjectType),
                SubjectId = PolicyDecisionValidator.NormalizeText(query.SubjectId),
                Take = PolicyDecisionValidator.NormalizeTake(query.Take)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<PolicyDecisionSummary>> ListForProjectAsync(PolicyDecisionsForProjectQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateProjectQuery(query);
        if (!validation.IsValid)
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PolicyDecisionSummaryRow>(new CommandDefinition(
            "governance.usp_PolicyDecisionEvent_ListForProject",
            new { ProjectId = query.ProjectId, Take = PolicyDecisionValidator.NormalizeTake(query.Take) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<PolicyDecisionSummary>> ListForCorrelationAsync(PolicyDecisionsForCorrelationQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateCorrelationQuery(query);
        if (!validation.IsValid)
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PolicyDecisionSummaryRow>(new CommandDefinition(
            "governance.usp_PolicyDecisionEvent_ListForCorrelation",
            new { ProjectId = query.ProjectId, CorrelationId = query.CorrelationId, Take = PolicyDecisionValidator.NormalizeTake(query.Take) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private sealed class PolicyDecisionRow
    {
        public Guid PolicyDecisionEventId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid GovernanceEventId { get; set; }
        public string PolicyScope { get; set; } = string.Empty;
        public string PolicyName { get; set; } = string.Empty;
        public int PolicyVersion { get; set; }
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string RequirementCode { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string DecidedByActorType { get; set; } = string.Empty;
        public string DecidedByActorId { get; set; } = string.Empty;
        public Guid? RelatedToolRequestId { get; set; }
        public Guid? RelatedToolGateDecisionId { get; set; }
        public Guid? RelatedApprovalDecisionId { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public int EvidenceVersion { get; set; }
        public string EvidenceJson { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }

        public PolicyDecisionReadModel ToReadModel() => new()
        {
            PolicyDecisionEventId = PolicyDecisionEventId,
            ProjectId = ProjectId,
            GovernanceEventId = GovernanceEventId,
            PolicyScope = PolicyScope,
            PolicyName = PolicyName,
            PolicyVersion = PolicyVersion,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            Decision = Decision,
            RequirementCode = RequirementCode,
            ReasonCode = ReasonCode,
            Reason = Reason,
            DecidedByActorType = DecidedByActorType,
            DecidedByActorId = DecidedByActorId,
            RelatedToolRequestId = RelatedToolRequestId,
            RelatedToolGateDecisionId = RelatedToolGateDecisionId,
            RelatedApprovalDecisionId = RelatedApprovalDecisionId,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            EvidenceVersion = EvidenceVersion,
            EvidenceJson = EvidenceJson,
            CreatedUtc = CreatedUtc
        };
    }

    private sealed class PolicyDecisionSummaryRow
    {
        public Guid PolicyDecisionEventId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid GovernanceEventId { get; set; }
        public string PolicyScope { get; set; } = string.Empty;
        public string PolicyName { get; set; } = string.Empty;
        public int PolicyVersion { get; set; }
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string RequirementCode { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string DecidedByActorType { get; set; } = string.Empty;
        public string DecidedByActorId { get; set; } = string.Empty;
        public Guid? RelatedToolRequestId { get; set; }
        public Guid? RelatedToolGateDecisionId { get; set; }
        public Guid? RelatedApprovalDecisionId { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public PolicyDecisionSummary ToSummary() => new()
        {
            PolicyDecisionEventId = PolicyDecisionEventId,
            ProjectId = ProjectId,
            GovernanceEventId = GovernanceEventId,
            PolicyScope = PolicyScope,
            PolicyName = PolicyName,
            PolicyVersion = PolicyVersion,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            Decision = Decision,
            RequirementCode = RequirementCode,
            ReasonCode = ReasonCode,
            DecidedByActorType = DecidedByActorType,
            DecidedByActorId = DecidedByActorId,
            RelatedToolRequestId = RelatedToolRequestId,
            RelatedToolGateDecisionId = RelatedToolGateDecisionId,
            RelatedApprovalDecisionId = RelatedApprovalDecisionId,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedUtc = CreatedUtc
        };
    }
}
