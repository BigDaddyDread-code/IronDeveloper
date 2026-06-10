using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Execution;
using IronDev.Core.Agents.Skills;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlMemoryExecutionAuditStore : IMemoryExecutionAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] BannedPrivateReasoningTokens =
    [
        "RawPrompt",
        "Prompt",
        "RawCompletion",
        "Completion",
        "ChainOfThought",
        "Scratchpad",
        "PrivateReasoning"
    ];

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlMemoryExecutionAuditStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<MemoryExecutionAuditRecord> AppendAsync(
        MemoryExecutionAuditDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ThrowIfInvalidDraft(draft);

        var record = ToRecord(draft);
        ThrowIfContainsPrivateReasoning(record);

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "agent.usp_MemoryExecutionAudit_Create",
            new
            {
                record.AuditId,
                record.Scope.TenantId,
                record.Scope.ProjectId,
                record.Scope.CampaignId,
                record.Scope.RunId,
                record.Scope.AgentId,
                record.ExecutionId,
                record.ContextId,
                record.RequestId,
                record.ReviewId,
                record.SkillId,
                record.DecisionId,
                ActionType = (int)record.ActionType,
                Outcome = (int)record.Outcome,
                record.ExecutionStatus,
                GateDecision = (int)record.GateDecision,
                GovernanceDecision = record.GovernanceDecision is null ? (int?)null : (int)record.GovernanceDecision.Value,
                record.GovernanceCheckId,
                record.Executed,
                record.SourceMutated,
                record.WorkspaceMutated,
                record.ExternalSystemCalled,
                record.TicketCreated,
                record.MemoryWritten,
                record.ApprovalGranted,
                record.ShellCommandRun,
                record.ToolName,
                record.AffectedArtifactType,
                record.AffectedArtifactId,
                record.ThoughtLedgerEntryId,
                record.CorrelationId,
                record.Summary,
                MemoryItemIdsJson = JsonSerializer.Serialize(record.MemoryItemIds, JsonOptions),
                InfluenceIdsJson = JsonSerializer.Serialize(record.InfluenceIds, JsonOptions),
                HandoffMemorySliceIdsJson = JsonSerializer.Serialize(record.HandoffMemorySliceIds, JsonOptions),
                EvidencePathsJson = JsonSerializer.Serialize(record.EvidencePaths, JsonOptions),
                BlockersJson = JsonSerializer.Serialize(record.Blockers, JsonOptions),
                WarningsJson = JsonSerializer.Serialize(record.Warnings, JsonOptions),
                IssueCodesJson = JsonSerializer.Serialize(record.IssueCodes.Select(code => (int)code).ToArray(), JsonOptions),
                CreatedAtUtc = record.CreatedAt.UtcDateTime
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return record;
    }

    public async Task<IReadOnlyList<MemoryExecutionAuditRecord>> QueryAsync(
        MemoryExecutionAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalidQuery(query);

        var take = Math.Clamp(query.Take <= 0 ? 100 : query.Take, 1, 500);
        using var connection = _connectionFactory.CreateConnection();

        var rows = (await connection.QueryAsync<AuditRow>(new CommandDefinition(
            """
            SELECT TOP (@Take)
                AuditId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                ExecutionId,
                ContextId,
                RequestId,
                ReviewId,
                SkillId,
                DecisionId,
                ActionType,
                Outcome,
                ExecutionStatus,
                GateDecision,
                GovernanceDecision,
                GovernanceCheckId,
                Executed,
                SourceMutated,
                WorkspaceMutated,
                ExternalSystemCalled,
                TicketCreated,
                MemoryWritten,
                ApprovalGranted,
                ShellCommandRun,
                ToolName,
                AffectedArtifactType,
                AffectedArtifactId,
                ThoughtLedgerEntryId,
                CorrelationId,
                Summary,
                MemoryItemIdsJson,
                InfluenceIdsJson,
                HandoffMemorySliceIdsJson,
                EvidencePathsJson,
                BlockersJson,
                WarningsJson,
                IssueCodesJson,
                CreatedAtUtc
            FROM agent.AgentMemoryExecutionAudit
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
              AND (@AgentId IS NULL OR AgentId = @AgentId)
              AND (@ExecutionId IS NULL OR ExecutionId = @ExecutionId)
              AND (@DecisionId IS NULL OR DecisionId = @DecisionId)
              AND (@GovernanceCheckId IS NULL OR GovernanceCheckId = @GovernanceCheckId)
              AND (@MemoryItemId IS NULL OR EXISTS (SELECT 1 FROM OPENJSON(MemoryItemIdsJson) WHERE [value] = @MemoryItemId))
              AND (@InfluenceId IS NULL OR EXISTS (SELECT 1 FROM OPENJSON(InfluenceIdsJson) WHERE [value] = @InfluenceId))
              AND (@HandoffMemorySliceId IS NULL OR EXISTS (SELECT 1 FROM OPENJSON(HandoffMemorySliceIdsJson) WHERE [value] = @HandoffMemorySliceId))
            ORDER BY CreatedAtUtc DESC, AuditId DESC;
            """,
            new
            {
                query.TenantId,
                query.ProjectId,
                query.CampaignId,
                query.RunId,
                query.AgentId,
                query.ExecutionId,
                query.DecisionId,
                query.GovernanceCheckId,
                query.MemoryItemId,
                query.InfluenceId,
                query.HandoffMemorySliceId,
                Take = take
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        return rows.Select(ToRecord).ToArray();
    }

    private static MemoryExecutionAuditRecord ToRecord(MemoryExecutionAuditDraft draft)
    {
        var request = draft.Request;
        var context = request.MemoryExecutionContext!;
        var skillContext = request.SkillRequestContext;
        var result = draft.Result;
        var gate = draft.GateResult;
        var evidence = result.MemoryEvidence!;
        var scope = context.Scope;
        var memoryItemIds = MergeIds(evidence.MemoryItemIds, gate.Evidence.MemoryItemIds, context.ReferencedArtifacts.Select(item => item.MemoryItemId));
        var influenceIds = MergeIds(evidence.InfluenceIds, gate.Evidence.InfluenceIds, context.ReferencedArtifacts.Select(item => item.InfluenceId));
        var handoffIds = MergeIds(evidence.HandoffMemorySliceIds, gate.Evidence.HandoffMemorySliceIds, context.ReferencedArtifacts.Select(item => item.HandoffMemorySliceId));
        var thoughtLedgerEntryId = FirstNonEmpty(
            gate.GovernanceResult?.ThoughtLedgerEntryId,
            context.ReferencedArtifacts.Select(item => item.ThoughtLedgerEntryId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));

        return new MemoryExecutionAuditRecord
        {
            AuditId = $"memory-execution-audit-{Guid.NewGuid():N}",
            Scope = scope,
            ExecutionId = result.ExecutionId,
            ContextId = result.ContextId,
            RequestId = result.RequestId,
            ReviewId = result.ReviewId,
            SkillId = result.SkillId,
            DecisionId = context.DecisionId,
            ActionType = context.ActionType,
            Outcome = draft.Outcome,
            ExecutionStatus = result.Status,
            GateDecision = gate.Decision,
            GovernanceDecision = gate.GovernanceResult?.Decision ?? evidence.GovernanceDecision,
            GovernanceCheckId = gate.GovernanceResult?.GovernanceCheckId ?? evidence.GovernanceCheckId,
            Executed = result.Executed,
            SourceMutated = result.SourceMutated,
            WorkspaceMutated = result.WorkspaceMutated,
            ExternalSystemCalled = result.ExternalSystemCalled,
            TicketCreated = result.TicketCreated,
            MemoryWritten = result.MemoryWritten,
            ApprovalGranted = result.ApprovalGranted,
            ShellCommandRun = result.ShellCommandRun,
            ToolName = context.ToolName,
            AffectedArtifactType = context.AffectedArtifactType,
            AffectedArtifactId = context.AffectedArtifactId,
            ThoughtLedgerEntryId = thoughtLedgerEntryId,
            CorrelationId = FirstNonEmpty(context.CorrelationId, gate.GovernanceResult?.CorrelationId),
            Summary = FirstNonEmpty(result.Summary, skillContext.Purpose) ?? "Memory-backed skill execution audited.",
            MemoryItemIds = memoryItemIds,
            InfluenceIds = influenceIds,
            HandoffMemorySliceIds = handoffIds,
            EvidencePaths = MergeIds(result.EvidencePaths, skillContext.EvidencePaths),
            Blockers = result.Blockers.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = result.Warnings.Distinct(StringComparer.Ordinal).ToArray(),
            IssueCodes = evidence.IssueCodes.Concat(gate.Issues.Select(issue => issue.Code)).Distinct().ToArray(),
            CreatedAt = draft.CreatedAt
        };
    }

    private static MemoryExecutionAuditRecord ToRecord(AuditRow row) =>
        new()
        {
            AuditId = row.AuditId,
            Scope = new AgentMemoryScope
            {
                TenantId = row.TenantId,
                ProjectId = row.ProjectId,
                CampaignId = row.CampaignId,
                RunId = row.RunId,
                AgentId = row.AgentId
            },
            ExecutionId = row.ExecutionId,
            ContextId = row.ContextId,
            RequestId = row.RequestId,
            ReviewId = row.ReviewId,
            SkillId = row.SkillId,
            DecisionId = row.DecisionId,
            ActionType = (MemoryGovernanceActionType)row.ActionType,
            Outcome = (MemoryExecutionAuditOutcome)row.Outcome,
            ExecutionStatus = row.ExecutionStatus,
            GateDecision = (MemoryExecutionGateDecision)row.GateDecision,
            GovernanceDecision = row.GovernanceDecision is null ? null : (MemoryGovernanceDecision)row.GovernanceDecision.Value,
            GovernanceCheckId = row.GovernanceCheckId,
            Executed = row.Executed,
            SourceMutated = row.SourceMutated,
            WorkspaceMutated = row.WorkspaceMutated,
            ExternalSystemCalled = row.ExternalSystemCalled,
            TicketCreated = row.TicketCreated,
            MemoryWritten = row.MemoryWritten,
            ApprovalGranted = row.ApprovalGranted,
            ShellCommandRun = row.ShellCommandRun,
            ToolName = row.ToolName,
            AffectedArtifactType = row.AffectedArtifactType,
            AffectedArtifactId = row.AffectedArtifactId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId,
            Summary = row.Summary,
            MemoryItemIds = DeserializeStringArray(row.MemoryItemIdsJson),
            InfluenceIds = DeserializeStringArray(row.InfluenceIdsJson),
            HandoffMemorySliceIds = DeserializeStringArray(row.HandoffMemorySliceIdsJson),
            EvidencePaths = DeserializeStringArray(row.EvidencePathsJson),
            Blockers = DeserializeStringArray(row.BlockersJson),
            Warnings = DeserializeStringArray(row.WarningsJson),
            IssueCodes = DeserializeIntArray(row.IssueCodesJson).Select(value => (MemoryGovernanceIssueCode)value).ToArray(),
            CreatedAt = ToUtc(row.CreatedAtUtc)
        };

    private static void ThrowIfInvalidDraft(MemoryExecutionAuditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft.Request);
        ArgumentNullException.ThrowIfNull(draft.Result);
        ArgumentNullException.ThrowIfNull(draft.GateResult);

        var request = draft.Request;
        var context = request.MemoryExecutionContext;
        if (context is null)
            throw new InvalidOperationException("Memory execution audit requires a memory execution context.");

        ThrowIfInvalidScope(context.Scope);

        if (string.IsNullOrWhiteSpace(context.DecisionId))
            throw new InvalidOperationException("Memory execution audit requires a decision ID.");

        if (context.ReferencedArtifacts is null || context.ReferencedArtifacts.Count == 0)
            throw new InvalidOperationException("Memory execution audit requires memory, influence, or handoff references.");

        var resultEvidence = draft.Result.MemoryEvidence;
        if (resultEvidence is null || !resultEvidence.IsMemoryBacked)
            throw new InvalidOperationException("Memory execution audit requires memory-backed execution evidence.");

        if (!string.Equals(resultEvidence.DecisionId, context.DecisionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Memory execution audit evidence decision ID must match the memory execution context.");

        if (!string.Equals(draft.GateResult.Evidence.DecisionId, context.DecisionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Memory execution gate evidence decision ID must match the memory execution context.");

        if (resultEvidence.GateDecision != draft.GateResult.Evidence.GateDecision ||
            resultEvidence.GateDecision != draft.GateResult.Decision)
        {
            throw new InvalidOperationException("Memory execution audit gate decision must match result and gate evidence.");
        }

        if (resultEvidence.GovernanceDecision != draft.GateResult.Evidence.GovernanceDecision)
            throw new InvalidOperationException("Memory execution audit governance decision must match gate evidence.");

        if (!string.Equals(resultEvidence.GovernanceCheckId, draft.GateResult.Evidence.GovernanceCheckId, StringComparison.Ordinal))
            throw new InvalidOperationException("Memory execution audit governance check ID must match gate evidence.");

        if (!SameIds(resultEvidence.MemoryItemIds, draft.GateResult.Evidence.MemoryItemIds) ||
            !SameIds(resultEvidence.InfluenceIds, draft.GateResult.Evidence.InfluenceIds) ||
            !SameIds(resultEvidence.HandoffMemorySliceIds, draft.GateResult.Evidence.HandoffMemorySliceIds))
        {
            throw new InvalidOperationException("Memory execution audit evidence references must match gate evidence.");
        }

        if (!HasAnyReference(resultEvidence.MemoryItemIds, resultEvidence.InfluenceIds, resultEvidence.HandoffMemorySliceIds))
            throw new InvalidOperationException("Memory execution audit requires at least one memory, influence, or handoff reference.");

        if (!Enum.IsDefined(draft.Outcome))
            throw new InvalidOperationException($"Unsupported memory execution audit outcome '{draft.Outcome}'.");
    }

    private static void ThrowIfInvalidQuery(MemoryExecutionAuditQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.TenantId) ||
            string.IsNullOrWhiteSpace(query.ProjectId) ||
            string.IsNullOrWhiteSpace(query.CampaignId) ||
            string.IsNullOrWhiteSpace(query.RunId))
        {
            throw new InvalidOperationException("Memory execution audit queries require tenant, project, campaign, and run identity.");
        }
    }

    private static void ThrowIfInvalidScope(AgentMemoryScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.TenantId) ||
            string.IsNullOrWhiteSpace(scope.ProjectId) ||
            string.IsNullOrWhiteSpace(scope.CampaignId) ||
            string.IsNullOrWhiteSpace(scope.RunId) ||
            string.IsNullOrWhiteSpace(scope.AgentId))
        {
            throw new InvalidOperationException("Memory execution audit requires complete tenant, project, campaign, run, and agent scope.");
        }
    }

    private static void ThrowIfContainsPrivateReasoning(MemoryExecutionAuditRecord record)
    {
        var values = new List<string?>
        {
            record.ToolName,
            record.AffectedArtifactType,
            record.AffectedArtifactId,
            record.Summary
        };
        values.AddRange(record.EvidencePaths);
        values.AddRange(record.Blockers);
        values.AddRange(record.Warnings);

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var token in BannedPrivateReasoningTokens)
            {
                if (value!.Contains(token, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Memory execution audit must not contain raw private reasoning field '{token}'.");
            }
        }
    }

    private static bool HasAnyReference(params IReadOnlyList<string>[] references) =>
        references.Any(group => group.Any(value => !string.IsNullOrWhiteSpace(value)));

    private static bool SameIds(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal)
            .SetEquals(right.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static IReadOnlyList<string> MergeIds(params IEnumerable<string?>[] values) =>
        values
            .SelectMany(value => value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> DeserializeStringArray(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? Array.Empty<string>();

    private static IReadOnlyList<int> DeserializeIntArray(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Array.Empty<int>()
            : JsonSerializer.Deserialize<IReadOnlyList<int>>(json, JsonOptions) ?? Array.Empty<int>();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static async Task OpenAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State == ConnectionState.Open)
            return;

        if (connection is DbConnection dbConnection)
            await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        else
            connection.Open();
    }

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class AuditRow
    {
        public string AuditId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string ExecutionId { get; set; } = string.Empty;
        public string ContextId { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string ReviewId { get; set; } = string.Empty;
        public string SkillId { get; set; } = string.Empty;
        public string DecisionId { get; set; } = string.Empty;
        public int ActionType { get; set; }
        public int Outcome { get; set; }
        public string ExecutionStatus { get; set; } = string.Empty;
        public int GateDecision { get; set; }
        public int? GovernanceDecision { get; set; }
        public string? GovernanceCheckId { get; set; }
        public bool Executed { get; set; }
        public bool SourceMutated { get; set; }
        public bool WorkspaceMutated { get; set; }
        public bool ExternalSystemCalled { get; set; }
        public bool TicketCreated { get; set; }
        public bool MemoryWritten { get; set; }
        public bool ApprovalGranted { get; set; }
        public bool ShellCommandRun { get; set; }
        public string? ToolName { get; set; }
        public string? AffectedArtifactType { get; set; }
        public string? AffectedArtifactId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string MemoryItemIdsJson { get; set; } = "[]";
        public string InfluenceIdsJson { get; set; } = "[]";
        public string HandoffMemorySliceIdsJson { get; set; } = "[]";
        public string EvidencePathsJson { get; set; } = "[]";
        public string BlockersJson { get; set; } = "[]";
        public string WarningsJson { get; set; } = "[]";
        public string IssueCodesJson { get; set; } = "[]";
        public DateTime CreatedAtUtc { get; set; }
    }
}
