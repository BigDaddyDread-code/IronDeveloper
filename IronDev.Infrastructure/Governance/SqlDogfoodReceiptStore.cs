using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlDogfoodReceiptStore : IDogfoodReceiptStore
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DogfoodReceiptValidator _validator;

    public SqlDogfoodReceiptStore(IDbConnectionFactory connectionFactory, DogfoodReceiptValidator? validator = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? new DogfoodReceiptValidator();
    }

    public async Task<DogfoodReceiptReadModel> RecordAsync(DogfoodReceiptRecordRequest request, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateRecord(request);
        if (!validation.IsValid)
            throw new ArgumentException(FormatIssues(validation.Issues), nameof(request));

        var dogfoodReceiptId = request.DogfoodReceiptId ?? Guid.NewGuid();
        var governanceEventId = request.GovernanceEventId ?? Guid.NewGuid();
        var createdUtc = request.CreatedUtc ?? DateTimeOffset.UtcNow;
        var receiptType = DogfoodReceiptValidator.NormalizeText(request.ReceiptType);
        var subjectType = DogfoodReceiptValidator.NormalizeText(request.SubjectType);
        var subjectId = DogfoodReceiptValidator.NormalizeText(request.SubjectId);
        var outcome = DogfoodReceiptValidator.NormalizeOutcome(request.Outcome);
        var summaryCode = DogfoodReceiptValidator.NormalizeText(request.SummaryCode);
        var actorType = DogfoodReceiptValidator.NormalizeText(request.RecordedByActorType);
        var actorId = DogfoodReceiptValidator.NormalizeText(request.RecordedByActorId);
        var correlationId = request.CorrelationId ?? dogfoodReceiptId;

        var eventPayloadJson = JsonSerializer.Serialize(new
        {
            schema = "dogfood.receipt.recorded.v1",
            dogfoodReceiptId,
            receiptType,
            receiptSubjectType = subjectType,
            receiptSubjectId = subjectId,
            outcome,
            summaryCode,
            approvesRelease = false,
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
        var row = await connection.QuerySingleAsync<DogfoodReceiptRow>(new CommandDefinition(
            "governance.usp_DogfoodReceipt_Record",
            new
            {
                DogfoodReceiptId = dogfoodReceiptId,
                request.ProjectId,
                GovernanceEventId = governanceEventId,
                ReceiptType = receiptType,
                SubjectType = subjectType,
                SubjectId = subjectId,
                Outcome = outcome,
                SummaryCode = summaryCode,
                Summary = request.Summary?.Trim(),
                RecordedByActorType = actorType,
                RecordedByActorId = actorId,
                request.RelatedToolRequestId,
                request.RelatedToolGateDecisionId,
                request.RelatedApprovalDecisionId,
                request.RelatedPolicyDecisionEventId,
                CorrelationId = correlationId,
                request.CausationId,
                request.EvidenceVersion,
                EvidenceJson = request.EvidenceJson.Trim(),
                GovernanceEventPayloadJson = eventPayloadJson,
                CreatedUtc = createdUtc
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row.ToReadModel();
    }

    public async Task<DogfoodReceiptReadModel?> GetAsync(Guid dogfoodReceiptId, CancellationToken cancellationToken = default)
    {
        if (dogfoodReceiptId == Guid.Empty)
            return null;

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<DogfoodReceiptRow>(new CommandDefinition(
            "governance.usp_DogfoodReceipt_GetById",
            new { DogfoodReceiptId = dogfoodReceiptId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReadModel();
    }

    public async Task<IReadOnlyList<DogfoodReceiptSummary>> ListForSubjectAsync(DogfoodReceiptsForSubjectQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateSubjectQuery(query);
        if (!validation.IsValid)
            throw new ArgumentException(FormatIssues(validation.Issues), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<DogfoodReceiptSummaryRow>(new CommandDefinition(
            "governance.usp_DogfoodReceipt_ListForSubject",
            new
            {
                query.ProjectId,
                ReceiptType = DogfoodReceiptValidator.NormalizeText(query.ReceiptType),
                SubjectType = DogfoodReceiptValidator.NormalizeText(query.SubjectType),
                SubjectId = DogfoodReceiptValidator.NormalizeText(query.SubjectId),
                Take = DogfoodReceiptValidator.NormalizeTake(query.Take)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<DogfoodReceiptSummary>> ListForProjectAsync(DogfoodReceiptsForProjectQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateProjectQuery(query);
        if (!validation.IsValid)
            throw new ArgumentException(FormatIssues(validation.Issues), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<DogfoodReceiptSummaryRow>(new CommandDefinition(
            "governance.usp_DogfoodReceipt_ListForProject",
            new { query.ProjectId, Take = DogfoodReceiptValidator.NormalizeTake(query.Take) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<IReadOnlyList<DogfoodReceiptSummary>> ListForCorrelationAsync(DogfoodReceiptsForCorrelationQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateCorrelationQuery(query);
        if (!validation.IsValid)
            throw new ArgumentException(FormatIssues(validation.Issues), nameof(query));

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<DogfoodReceiptSummaryRow>(new CommandDefinition(
            "governance.usp_DogfoodReceipt_ListForCorrelation",
            new { query.ProjectId, query.CorrelationId, Take = DogfoodReceiptValidator.NormalizeTake(query.Take) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private static string FormatIssues(IReadOnlyList<DogfoodReceiptValidationIssue> issues) =>
        string.Join("; ", issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private sealed class DogfoodReceiptRow
    {
        public Guid DogfoodReceiptId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid GovernanceEventId { get; set; }
        public string ReceiptType { get; set; } = string.Empty;
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string SummaryCode { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string RecordedByActorType { get; set; } = string.Empty;
        public string RecordedByActorId { get; set; } = string.Empty;
        public Guid? RelatedToolRequestId { get; set; }
        public Guid? RelatedToolGateDecisionId { get; set; }
        public Guid? RelatedApprovalDecisionId { get; set; }
        public Guid? RelatedPolicyDecisionEventId { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public int EvidenceVersion { get; set; }
        public string EvidenceJson { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }

        public DogfoodReceiptReadModel ToReadModel() => new()
        {
            DogfoodReceiptId = DogfoodReceiptId,
            ProjectId = ProjectId,
            GovernanceEventId = GovernanceEventId,
            ReceiptType = ReceiptType,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            Outcome = Outcome,
            SummaryCode = SummaryCode,
            Summary = Summary,
            RecordedByActorType = RecordedByActorType,
            RecordedByActorId = RecordedByActorId,
            RelatedToolRequestId = RelatedToolRequestId,
            RelatedToolGateDecisionId = RelatedToolGateDecisionId,
            RelatedApprovalDecisionId = RelatedApprovalDecisionId,
            RelatedPolicyDecisionEventId = RelatedPolicyDecisionEventId,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            EvidenceVersion = EvidenceVersion,
            EvidenceJson = EvidenceJson,
            CreatedUtc = CreatedUtc
        };
    }

    private sealed class DogfoodReceiptSummaryRow
    {
        public Guid DogfoodReceiptId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid GovernanceEventId { get; set; }
        public string ReceiptType { get; set; } = string.Empty;
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string SummaryCode { get; set; } = string.Empty;
        public string RecordedByActorType { get; set; } = string.Empty;
        public string RecordedByActorId { get; set; } = string.Empty;
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public DogfoodReceiptSummary ToSummary() => new()
        {
            DogfoodReceiptId = DogfoodReceiptId,
            ProjectId = ProjectId,
            GovernanceEventId = GovernanceEventId,
            ReceiptType = ReceiptType,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            Outcome = Outcome,
            SummaryCode = SummaryCode,
            RecordedByActorType = RecordedByActorType,
            RecordedByActorId = RecordedByActorId,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedUtc = CreatedUtc
        };
    }
}
