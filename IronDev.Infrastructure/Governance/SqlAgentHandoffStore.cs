using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Agents;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlAgentHandoffStore : IAgentHandoffStore
{
    private const int DefaultTake = 100;
    private const int MaxTake = 500;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly AgentHandoffValidator _handoffValidator;
    private readonly IAgentHandoffAuthorityTransferValidator _authorityTransferValidator;

    public SqlAgentHandoffStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new AgentHandoffValidator(), new AgentHandoffAuthorityTransferValidator())
    {
    }

    internal SqlAgentHandoffStore(
        IDbConnectionFactory connectionFactory,
        AgentHandoffValidator handoffValidator,
        IAgentHandoffAuthorityTransferValidator authorityTransferValidator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _handoffValidator = handoffValidator ?? throw new ArgumentNullException(nameof(handoffValidator));
        _authorityTransferValidator = authorityTransferValidator ?? throw new ArgumentNullException(nameof(authorityTransferValidator));
    }

    public async Task<AgentHandoff> CreateAsync(AgentHandoffCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var createValidation = _handoffValidator.Validate(request);
        ThrowIfInvalid(createValidation.Issues, nameof(request));

        var agentHandoffId = Guid.NewGuid();
        var governanceEventId = Guid.NewGuid();
        var createdUtc = DateTimeOffset.UtcNow;
        var materialized = Materialize(request, agentHandoffId, createdUtc);

        var handoffValidation = _handoffValidator.Validate(materialized);
        ThrowIfInvalid(handoffValidation.Issues, nameof(request));

        var authorityValidation = _authorityTransferValidator.Validate(materialized);
        if (!authorityValidation.IsSafe)
            throw new ArgumentException(FormatAuthorityViolations(authorityValidation.Violations), nameof(request));

        var eventPayloadJson = JsonSerializer.Serialize(new
        {
            schema = "a2a.handoff.recorded.v1",
            agentHandoffId,
            handoffType = materialized.HandoffType.ToString(),
            status = materialized.Status.ToString(),
            sourceAgentId = materialized.SourceAgent.AgentId,
            targetAgentId = materialized.TargetAgent.AgentId,
            subjectType = materialized.Subject.SubjectType.ToString(),
            subjectId = materialized.Subject.SubjectId,
            evidenceReferenceCount = materialized.EvidenceReferences.Count,
            constraintCount = materialized.Constraints.Count,
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
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            "a2a.usp_AgentHandoff_Create",
            new
            {
                AgentHandoffId = agentHandoffId,
                materialized.ProjectId,
                GovernanceEventId = governanceEventId,
                HandoffType = materialized.HandoffType.ToString(),
                Status = materialized.Status.ToString(),
                SourceAgentId = materialized.SourceAgent.AgentId,
                SourceAgentRole = materialized.SourceAgent.AgentRole.ToString(),
                SourceAgentDisplayName = materialized.SourceAgent.DisplayName,
                TargetAgentId = materialized.TargetAgent.AgentId,
                TargetAgentRole = materialized.TargetAgent.AgentRole.ToString(),
                TargetAgentDisplayName = materialized.TargetAgent.DisplayName,
                SubjectType = materialized.Subject.SubjectType.ToString(),
                SubjectId = materialized.Subject.SubjectId,
                SubjectActionName = materialized.Subject.ActionName,
                SubjectSummary = materialized.Subject.Summary,
                materialized.CorrelationId,
                materialized.CausationId,
                materialized.SupersedesHandoffId,
                materialized.CreatedByActorType,
                materialized.CreatedByActorId,
                materialized.MetadataVersion,
                materialized.MetadataJson,
                EvidenceReferencesJson = JsonSerializer.Serialize(materialized.EvidenceReferences.Select(evidence => new
                {
                    evidenceType = evidence.EvidenceType.ToString(),
                    evidence.EvidenceId,
                    evidence.EvidenceLabel,
                    evidence.EvidenceSummary,
                    evidence.GovernanceEventId,
                    allowedUses = evidence.AllowedUses.Select(allowedUse => allowedUse.ToString()).ToArray()
                })),
                ConstraintsJson = JsonSerializer.Serialize(materialized.Constraints.Select(constraint => new
                {
                    constraintType = constraint.ConstraintType.ToString(),
                    constraint.ConstraintCode,
                    constraint.Description
                })),
                GovernanceEventPayloadJson = eventPayloadJson,
                CreatedUtc = createdUtc
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return ReadHandoff(grid) ?? throw new InvalidOperationException("Agent handoff record procedure returned no row.");
    }

    public async Task<AgentHandoff?> GetAsync(Guid projectId, Guid agentHandoffId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || agentHandoffId == Guid.Empty)
            return null;

        using var connection = _connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            "a2a.usp_AgentHandoff_Get",
            new { ProjectId = projectId, AgentHandoffId = agentHandoffId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return ReadHandoff(grid);
    }

    public Task<IReadOnlyList<AgentHandoffSummary>> ListByProjectAsync(Guid projectId, int take, CancellationToken cancellationToken = default) =>
        ListAsync("a2a.usp_AgentHandoff_ListByProject", new { ProjectId = RequireProject(projectId), Take = NormalizeTake(take) }, cancellationToken);

    public Task<IReadOnlyList<AgentHandoffSummary>> ListByCorrelationAsync(Guid projectId, Guid correlationId, int take, CancellationToken cancellationToken = default)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId is required.", nameof(correlationId));

        return ListAsync("a2a.usp_AgentHandoff_ListByCorrelation", new { ProjectId = RequireProject(projectId), CorrelationId = correlationId, Take = NormalizeTake(take) }, cancellationToken);
    }

    public Task<IReadOnlyList<AgentHandoffSummary>> ListBySubjectAsync(Guid projectId, string subjectType, string subjectId, int take, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectType))
            throw new ArgumentException("SubjectType is required.", nameof(subjectType));

        if (string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("SubjectId is required.", nameof(subjectId));

        return ListAsync(
            "a2a.usp_AgentHandoff_ListBySubject",
            new { ProjectId = RequireProject(projectId), SubjectType = subjectType.Trim(), SubjectId = subjectId.Trim(), Take = NormalizeTake(take) },
            cancellationToken);
    }

    private async Task<IReadOnlyList<AgentHandoffSummary>> ListAsync(string procedureName, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            procedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private static AgentHandoff Materialize(AgentHandoffCreateRequest request, Guid agentHandoffId, DateTimeOffset createdUtc) =>
        new()
        {
            AgentHandoffId = agentHandoffId,
            ProjectId = request.ProjectId,
            HandoffType = request.HandoffType,
            Status = request.Status,
            SourceAgent = request.SourceAgent,
            TargetAgent = request.TargetAgent,
            Subject = request.Subject,
            EvidenceReferences = request.EvidenceReferences,
            Constraints = request.Constraints,
            CorrelationId = request.CorrelationId,
            CausationId = request.CausationId,
            SupersedesHandoffId = request.SupersedesHandoffId,
            CreatedByActorType = request.CreatedByActorType,
            CreatedByActorId = request.CreatedByActorId,
            MetadataVersion = request.MetadataVersion,
            MetadataJson = request.MetadataJson,
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = createdUtc
        };

    private static AgentHandoff? ReadHandoff(SqlMapper.GridReader grid)
    {
        var row = grid.ReadSingleOrDefault<HandoffRow>();
        var evidenceRows = grid.Read<EvidenceRow>().ToArray();
        var allowedUseRows = grid.Read<AllowedUseRow>().ToArray();
        var constraintRows = grid.Read<ConstraintRow>().ToArray();

        return row?.ToHandoff(evidenceRows, allowedUseRows, constraintRows);
    }

    private static Guid RequireProject(Guid projectId)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId is required.", nameof(projectId));

        return projectId;
    }

    private static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

    private static void ThrowIfInvalid(IReadOnlyList<AgentHandoffValidationIssue> issues, string paramName)
    {
        if (issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(FormatIssues(issues), paramName);
    }

    private static string FormatIssues(IReadOnlyList<AgentHandoffValidationIssue> issues) =>
        string.Join("; ", issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string FormatAuthorityViolations(IReadOnlyList<AgentHandoffAuthorityTransferViolation> violations) =>
        string.Join("; ", violations.Select(violation => $"{violation.Code}: {violation.Message}"));

    private sealed class HandoffRow
    {
        public Guid AgentHandoffId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid GovernanceEventId { get; set; }
        public string HandoffType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SourceAgentId { get; set; } = string.Empty;
        public string SourceAgentRole { get; set; } = string.Empty;
        public string? SourceAgentDisplayName { get; set; }
        public string TargetAgentId { get; set; } = string.Empty;
        public string TargetAgentRole { get; set; } = string.Empty;
        public string? TargetAgentDisplayName { get; set; }
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public string? SubjectActionName { get; set; }
        public string? SubjectSummary { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public Guid? SupersedesHandoffId { get; set; }
        public string CreatedByActorType { get; set; } = string.Empty;
        public string CreatedByActorId { get; set; } = string.Empty;
        public int MetadataVersion { get; set; }
        public string MetadataJson { get; set; } = string.Empty;
        public bool GrantsApproval { get; set; }
        public bool GrantsExecution { get; set; }
        public bool MutatesSource { get; set; }
        public bool PromotesMemory { get; set; }
        public bool StartsWorkflow { get; set; }
        public bool SatisfiesPolicy { get; set; }
        public bool TransfersAuthority { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public AgentHandoff ToHandoff(IReadOnlyList<EvidenceRow> evidenceRows, IReadOnlyList<AllowedUseRow> allowedUseRows, IReadOnlyList<ConstraintRow> constraintRows)
        {
            var allowedUsesByEvidence = allowedUseRows
                .GroupBy(row => row.AgentHandoffEvidenceReferenceId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<AgentHandoffEvidenceAllowedUse>)group.Select(row => Enum.Parse<AgentHandoffEvidenceAllowedUse>(row.AllowedUse)).ToArray());

            return new AgentHandoff
            {
                AgentHandoffId = AgentHandoffId,
                ProjectId = ProjectId,
                HandoffType = Enum.Parse<AgentHandoffType>(HandoffType),
                Status = Enum.Parse<AgentHandoffStatus>(Status),
                SourceAgent = new AgentHandoffParticipant
                {
                    AgentId = SourceAgentId,
                    AgentRole = Enum.Parse<AgentHandoffParticipantRole>(SourceAgentRole),
                    DisplayName = SourceAgentDisplayName
                },
                TargetAgent = new AgentHandoffParticipant
                {
                    AgentId = TargetAgentId,
                    AgentRole = Enum.Parse<AgentHandoffParticipantRole>(TargetAgentRole),
                    DisplayName = TargetAgentDisplayName
                },
                Subject = new AgentHandoffSubject
                {
                    SubjectType = Enum.Parse<AgentHandoffSubjectType>(SubjectType),
                    SubjectId = SubjectId,
                    ActionName = SubjectActionName,
                    Summary = SubjectSummary
                },
                EvidenceReferences = evidenceRows.Select(evidence => new AgentHandoffEvidenceReference
                {
                    EvidenceType = Enum.Parse<AgentHandoffEvidenceType>(evidence.EvidenceType),
                    EvidenceId = evidence.EvidenceId,
                    EvidenceLabel = evidence.EvidenceLabel,
                    EvidenceSummary = evidence.EvidenceSummary,
                    GovernanceEventId = evidence.GovernanceEventId,
                    AllowedUses = allowedUsesByEvidence.TryGetValue(evidence.AgentHandoffEvidenceReferenceId, out var allowedUses) ? allowedUses : []
                }).ToArray(),
                Constraints = constraintRows.Select(constraint => new AgentHandoffConstraint
                {
                    ConstraintType = Enum.Parse<AgentHandoffConstraintType>(constraint.ConstraintType),
                    ConstraintCode = constraint.ConstraintCode,
                    Description = constraint.Description
                }).ToArray(),
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                SupersedesHandoffId = SupersedesHandoffId,
                CreatedByActorType = CreatedByActorType,
                CreatedByActorId = CreatedByActorId,
                MetadataVersion = MetadataVersion,
                MetadataJson = MetadataJson,
                GrantsApproval = GrantsApproval,
                GrantsExecution = GrantsExecution,
                MutatesSource = MutatesSource,
                PromotesMemory = PromotesMemory,
                StartsWorkflow = StartsWorkflow,
                SatisfiesPolicy = SatisfiesPolicy,
                TransfersAuthority = TransfersAuthority,
                CreatedUtc = CreatedUtc
            };
        }
    }

    private sealed class EvidenceRow
    {
        public Guid AgentHandoffEvidenceReferenceId { get; set; }
        public string EvidenceType { get; set; } = string.Empty;
        public string EvidenceId { get; set; } = string.Empty;
        public string? EvidenceLabel { get; set; }
        public string? EvidenceSummary { get; set; }
        public Guid? GovernanceEventId { get; set; }
    }

    private sealed class AllowedUseRow
    {
        public Guid AgentHandoffEvidenceReferenceId { get; set; }
        public string AllowedUse { get; set; } = string.Empty;
    }

    private sealed class ConstraintRow
    {
        public string ConstraintType { get; set; } = string.Empty;
        public string ConstraintCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private sealed class SummaryRow
    {
        public Guid AgentHandoffId { get; set; }
        public Guid ProjectId { get; set; }
        public string HandoffType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SourceAgentId { get; set; } = string.Empty;
        public string TargetAgentId { get; set; } = string.Empty;
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public int EvidenceReferenceCount { get; set; }
        public int ConstraintCount { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public AgentHandoffSummary ToSummary() => new()
        {
            AgentHandoffId = AgentHandoffId,
            ProjectId = ProjectId,
            HandoffType = Enum.Parse<AgentHandoffType>(HandoffType),
            Status = Enum.Parse<AgentHandoffStatus>(Status),
            SourceAgentId = SourceAgentId,
            TargetAgentId = TargetAgentId,
            SubjectType = Enum.Parse<AgentHandoffSubjectType>(SubjectType),
            SubjectId = SubjectId,
            EvidenceReferenceCount = EvidenceReferenceCount,
            ConstraintCount = ConstraintCount,
            CreatedUtc = CreatedUtc
        };
    }
}
