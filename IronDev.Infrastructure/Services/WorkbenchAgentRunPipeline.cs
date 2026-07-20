using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

internal static class WorkbenchBusinessAnalystContextCodec
{
    private static readonly JsonSerializerOptions StrictJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly JsonSerializerOptions Version1CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    internal static WorkbenchBusinessAnalystContext Deserialize(
        string snapshotJson,
        int contextSchemaVersion,
        int contextCanonicalizationVersion)
    {
        var context = (contextSchemaVersion, contextCanonicalizationVersion) switch
        {
            (WorkbenchBusinessAnalystContract.ContextSchemaVersion1,
             WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion1) =>
                DeserializeVersion1(snapshotJson),
            (WorkbenchBusinessAnalystContract.ContextSchemaVersion2,
             WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion2) =>
                DeserializeVersion2(snapshotJson),
            (WorkbenchBusinessAnalystContract.ContextSchemaVersion3,
             WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion3) =>
                DeserializeVersion3(snapshotJson),
            _ => throw Unsupported(contextSchemaVersion, contextCanonicalizationVersion)
        };
        EnsureComplete(context);
        return context;
    }

    internal static string Serialize(WorkbenchBusinessAnalystContext context) =>
        (context.ContextSchemaVersion, context.ContextCanonicalizationVersion) switch
        {
            (WorkbenchBusinessAnalystContract.ContextSchemaVersion1,
             WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion1) =>
                JsonSerializer.Serialize(ToVersion1(context), Version1CanonicalJsonOptions),
            (WorkbenchBusinessAnalystContract.ContextSchemaVersion2,
             WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion2) =>
                JsonSerializer.Serialize(ToVersion2(context), StrictJsonOptions),
            (WorkbenchBusinessAnalystContract.ContextSchemaVersion3,
             WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion3) =>
                JsonSerializer.Serialize(context, StrictJsonOptions),
            _ => throw Unsupported(context.ContextSchemaVersion, context.ContextCanonicalizationVersion)
        };

    internal static string CanonicalizeWithoutHash(WorkbenchBusinessAnalystContext context) =>
        (context.ContextSchemaVersion, context.ContextCanonicalizationVersion) switch
        {
            (WorkbenchBusinessAnalystContract.ContextSchemaVersion1,
             WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion1) =>
                JsonSerializer.Serialize(
                    ToVersion1(context) with { ContextHash = string.Empty },
                    Version1CanonicalJsonOptions),
            (WorkbenchBusinessAnalystContract.ContextSchemaVersion2,
             WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion2) =>
                JsonSerializer.Serialize(ToVersion2(context) with { ContextHash = string.Empty }, StrictJsonOptions),
            (WorkbenchBusinessAnalystContract.ContextSchemaVersion3,
             WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion3) =>
                JsonSerializer.Serialize(context with { ContextHash = string.Empty }, StrictJsonOptions),
            _ => throw Unsupported(context.ContextSchemaVersion, context.ContextCanonicalizationVersion)
        };

    internal static string ComputeHash(WorkbenchBusinessAnalystContext context) =>
        WorkbenchAgentRunService.ComputeHash(CanonicalizeWithoutHash(context));

    private static WorkbenchBusinessAnalystContext DeserializeVersion1(string snapshotJson)
    {
        var legacy = JsonSerializer.Deserialize<Version1Context>(snapshotJson, StrictJsonOptions)
            ?? throw new InvalidOperationException("The stored Workbench agent context could not be read.");
        return new WorkbenchBusinessAnalystContext(
            legacy.AgentRunId,
            legacy.TenantId,
            legacy.ProjectId,
            legacy.ProjectName,
            legacy.WorkbenchSessionId,
            legacy.LeaseEpoch,
            legacy.ChatSessionId,
            legacy.SourceUserMessageId,
            legacy.UnderstandingRevision,
            legacy.UnderstandingJson,
            legacy.Messages,
            legacy.AgentVersion,
            legacy.PromptVersion,
            legacy.ToolPolicyVersion,
            WorkbenchBusinessAnalystContract.ContextSchemaVersion1,
            WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion1,
            legacy.OutputSchemaVersion,
            legacy.ContextHash);
    }

    private static WorkbenchBusinessAnalystContext DeserializeVersion2(string snapshotJson)
    {
        var stored = JsonSerializer.Deserialize<Version2Context>(snapshotJson, StrictJsonOptions)
            ?? throw new InvalidOperationException("The stored Workbench agent context could not be read.");
        var context = new WorkbenchBusinessAnalystContext(
            stored.AgentRunId, stored.TenantId, stored.ProjectId, stored.ProjectName,
            stored.WorkbenchSessionId, stored.LeaseEpoch, stored.ChatSessionId,
            stored.SourceUserMessageId, stored.UnderstandingRevision, stored.UnderstandingJson,
            stored.Messages, stored.AgentVersion, stored.PromptVersion, stored.ToolPolicyVersion,
            stored.ContextSchemaVersion, stored.ContextCanonicalizationVersion,
            stored.OutputSchemaVersion, stored.ContextHash);
        if (context.ContextSchemaVersion != WorkbenchBusinessAnalystContract.ContextSchemaVersion2 ||
            context.ContextCanonicalizationVersion != WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion2)
            throw new InvalidOperationException("The stored Workbench agent context version does not match its run.");
        return context;
    }

    private static WorkbenchBusinessAnalystContext DeserializeVersion3(string snapshotJson)
    {
        var context = JsonSerializer.Deserialize<WorkbenchBusinessAnalystContext>(snapshotJson, StrictJsonOptions)
            ?? throw new InvalidOperationException("The stored Workbench agent context could not be read.");
        if (context.ContextSchemaVersion != WorkbenchBusinessAnalystContract.ContextSchemaVersion3 ||
            context.ContextCanonicalizationVersion != WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion3 ||
            !WorkbenchAgentInvocationKinds.IsTicketProposal(context.InvocationKind))
            throw new InvalidOperationException("The stored Workbench proposal context version does not match its run.");
        return context;
    }

    private static Version1Context ToVersion1(WorkbenchBusinessAnalystContext context) => new(
        context.AgentRunId,
        context.TenantId,
        context.ProjectId,
        context.ProjectName,
        context.WorkbenchSessionId,
        context.LeaseEpoch,
        context.ChatSessionId,
        context.SourceUserMessageId,
        context.UnderstandingRevision,
        context.UnderstandingJson,
        context.Messages,
        context.AgentVersion,
        context.PromptVersion,
        context.ToolPolicyVersion,
        context.OutputSchemaVersion,
        context.ContextHash);

    private static Version2Context ToVersion2(WorkbenchBusinessAnalystContext context) => new(
        context.AgentRunId, context.TenantId, context.ProjectId, context.ProjectName,
        context.WorkbenchSessionId, context.LeaseEpoch, context.ChatSessionId,
        context.SourceUserMessageId, context.UnderstandingRevision, context.UnderstandingJson,
        context.Messages, context.AgentVersion, context.PromptVersion, context.ToolPolicyVersion,
        context.ContextSchemaVersion, context.ContextCanonicalizationVersion,
        context.OutputSchemaVersion, context.ContextHash);

    private static void EnsureComplete(WorkbenchBusinessAnalystContext context)
    {
        if (context.AgentRunId == Guid.Empty || string.IsNullOrWhiteSpace(context.ProjectName) ||
            string.IsNullOrWhiteSpace(context.UnderstandingJson) || context.Messages is null ||
            string.IsNullOrWhiteSpace(context.AgentVersion) || string.IsNullOrWhiteSpace(context.PromptVersion) ||
            string.IsNullOrWhiteSpace(context.ToolPolicyVersion) || string.IsNullOrWhiteSpace(context.ContextHash))
            throw new InvalidOperationException("The stored Workbench agent context is incomplete.");
        if (context.InvocationKind == WorkbenchAgentInvocationKinds.TicketProposalRegeneration &&
            string.IsNullOrWhiteSpace(context.TicketProposalSnapshotJson))
            throw new InvalidOperationException("The stored Workbench regeneration context has no proposal snapshot.");
        if (context.InvocationKind != WorkbenchAgentInvocationKinds.TicketProposalRegeneration &&
            context.TicketProposalSnapshotJson is not null)
            throw new InvalidOperationException("Only regeneration context may contain a proposal snapshot.");
    }

    private static InvalidOperationException Unsupported(
        int contextSchemaVersion,
        int contextCanonicalizationVersion) =>
        new($"Unsupported Workbench agent context format {contextSchemaVersion}/{contextCanonicalizationVersion}.");

    // Version 1 is the exact pre-hardening JSON shape and property order. It deliberately omits
    // explicit context version fields so its historical hash remains reproducible without rewriting
    // the immutable stored snapshot. Version 2 is the first shape that embeds those fields.
    private sealed record Version1Context(
        Guid AgentRunId,
        int TenantId,
        int ProjectId,
        string ProjectName,
        long WorkbenchSessionId,
        long LeaseEpoch,
        long ChatSessionId,
        long SourceUserMessageId,
        long UnderstandingRevision,
        string UnderstandingJson,
        IReadOnlyList<WorkbenchAgentContextMessage> Messages,
        string AgentVersion,
        string PromptVersion,
        string ToolPolicyVersion,
        int OutputSchemaVersion,
        string ContextHash);

    private sealed record Version2Context(
        Guid AgentRunId,
        int TenantId,
        int ProjectId,
        string ProjectName,
        long WorkbenchSessionId,
        long LeaseEpoch,
        long ChatSessionId,
        long SourceUserMessageId,
        long UnderstandingRevision,
        string UnderstandingJson,
        IReadOnlyList<WorkbenchAgentContextMessage> Messages,
        string AgentVersion,
        string PromptVersion,
        string ToolPolicyVersion,
        int ContextSchemaVersion,
        int ContextCanonicalizationVersion,
        int OutputSchemaVersion,
        string ContextHash);
}

public sealed class WorkbenchAgentContextAssembler : IWorkbenchAgentContextAssembler
{
    private readonly IDbConnectionFactory _connections;

    public WorkbenchAgentContextAssembler(IDbConnectionFactory connections) => _connections = connections;

    public async Task<WorkbenchBusinessAnalystContext> AssembleAsync(
        WorkbenchAgentRunClaim claim,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var run = await connection.QuerySingleOrDefaultAsync<ContextRunRow>(new CommandDefinition(
                """
                SELECT AgentRunId, TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch, ActorUserId,
                       ChatSessionId, SourceUserMessageId, Status, ClaimToken,
                       AgentVersion, PromptVersion, ToolPolicyVersion, ContextSchemaVersion,
                       ContextCanonicalizationVersion, OutputSchemaVersion,
                       InvocationKind, TicketInstruction, TicketProposalSetId, TicketProposalRevision,
                       ContextSnapshotJson, ContextHash, BasedOnUnderstandingRevision
                FROM dbo.WorkbenchAgentRuns WITH (UPDLOCK, HOLDLOCK)
                WHERE AgentRunId=@AgentRunId;
                """,
                new { claim.AgentRunId },
                transaction,
                cancellationToken: cancellationToken)) ?? throw new WorkbenchAgentRunNotFoundException();

            EnsureCurrentClaim(run, claim);
            if (!string.IsNullOrWhiteSpace(run.ContextSnapshotJson))
            {
                var stored = WorkbenchBusinessAnalystContextCodec.Deserialize(
                    run.ContextSnapshotJson,
                    run.ContextSchemaVersion,
                    run.ContextCanonicalizationVersion);
                EnsureContextIntegrity(stored, run);
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.WorkbenchAgentRunAttempts
                    SET ContextHash=COALESCE(ContextHash, @ContextHash)
                    WHERE AgentRunId=@AgentRunId AND ClaimToken=@ClaimToken;
                    """,
                    new { run.AgentRunId, claim.ClaimToken, stored.ContextHash },
                    transaction,
                    cancellationToken: cancellationToken));
                transaction.Commit();
                return stored;
            }

            var project = await connection.QuerySingleOrDefaultAsync<ProjectContextRow>(new CommandDefinition(
                """
                SELECT project.Name AS ProjectName,
                       understanding.Revision AS UnderstandingRevision,
                       understanding.UnderstandingJson
                FROM dbo.Projects project
                CROSS APPLY
                (
                    SELECT TOP (1) value.Revision, value.UnderstandingJson
                    FROM dbo.ProjectUnderstandings value
                    WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                    ORDER BY value.Revision DESC
                ) understanding
                WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
                """,
                run,
                transaction,
                cancellationToken: cancellationToken))
                ?? throw new InvalidOperationException("The project understanding required for agent context is missing.");

            string? ticketProposalSnapshotJson = null;
            if (run.InvocationKind == WorkbenchAgentInvocationKinds.TicketProposalRegeneration)
            {
                var proposalRevision = await connection.QuerySingleOrDefaultAsync<ProposalSnapshotRow>(new CommandDefinition(
                    """
                    SELECT revision.SnapshotJson, revision.SnapshotHash
                    FROM dbo.TicketProposalSets value
                    INNER JOIN dbo.TicketProposalSetRevisions revision
                       ON revision.TenantId=value.TenantId
                      AND revision.TicketProposalSetId=value.Id
                      AND revision.Revision=@TicketProposalRevision
                    WHERE value.TenantId=@TenantId AND value.ProjectId=@ProjectId
                      AND value.Id=@TicketProposalSetId
                      AND value.WorkbenchSessionId=@WorkbenchSessionId
                      AND value.LeaseEpoch=@LeaseEpoch;
                    """,
                    run,
                    transaction,
                    cancellationToken: cancellationToken));
                if (proposalRevision is null || string.IsNullOrWhiteSpace(proposalRevision.SnapshotJson))
                    throw new TicketProposalRevisionConflictException(run.TicketProposalRevision ?? 0);
                var computedSnapshotHash = TicketProposalSetDocumentCodec.ComputeHash(proposalRevision.SnapshotJson);
                if (!string.Equals(computedSnapshotHash, proposalRevision.SnapshotHash, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The Workbench regeneration proposal snapshot failed its integrity check.");
                ticketProposalSnapshotJson = proposalRevision.SnapshotJson;
                var proposalSnapshot = TicketProposalSetDocumentCodec.Deserialize(ticketProposalSnapshotJson);
                if (proposalSnapshot.TicketProposalSetId != run.TicketProposalSetId ||
                    proposalSnapshot.ProjectId != run.ProjectId ||
                    proposalSnapshot.WorkbenchSessionId != run.WorkbenchSessionId ||
                    proposalSnapshot.LeaseEpoch != run.LeaseEpoch ||
                    proposalSnapshot.Revision != run.TicketProposalRevision)
                    throw new InvalidOperationException(
                        "The Workbench regeneration proposal snapshot does not match its immutable run purpose.");
            }

            var messages = (await connection.QueryAsync<WorkbenchAgentContextMessage>(new CommandDefinition(
                """
                SELECT bounded.Id AS MessageId, bounded.Role, bounded.Message, bounded.CreatedDate AS CreatedAtUtc
                FROM
                (
                    SELECT TOP (50) message.Id, message.Role, message.Message, message.CreatedDate
                    FROM dbo.ChatMessages message
                    LEFT JOIN dbo.WorkbenchAgentRuns trustedRun
                        ON trustedRun.TenantId=message.TenantId
                       AND trustedRun.ProjectId=message.ProjectId
                       AND trustedRun.ChatSessionId=message.ChatSessionId
                       AND trustedRun.AssistantMessageId=message.Id
                       AND trustedRun.Status IN (N'Completed', N'NeedsInput')
                    WHERE message.TenantId=@TenantId AND message.ProjectId=@ProjectId
                      AND message.ChatSessionId=@ChatSessionId AND message.Id <= @SourceUserMessageId
                      AND
                      (
                          message.Role=N'user'
                          OR (message.Role=N'assistant' AND trustedRun.AgentRunId IS NOT NULL)
                      )
                    ORDER BY message.Id DESC
                ) bounded
                ORDER BY bounded.Id;
                """,
                run,
                transaction,
                cancellationToken: cancellationToken))).AsList();

            if (messages.Count == 0 || messages[^1].MessageId != run.SourceUserMessageId ||
                !string.Equals(messages[^1].Role, "user", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The source user message is not the final bounded context message.");

            var contextWithoutHash = new WorkbenchBusinessAnalystContext(
                run.AgentRunId,
                run.TenantId,
                run.ProjectId,
                project.ProjectName,
                run.WorkbenchSessionId,
                run.LeaseEpoch,
                run.ChatSessionId,
                run.SourceUserMessageId,
                project.UnderstandingRevision,
                project.UnderstandingJson,
                messages,
                run.AgentVersion,
                run.PromptVersion,
                run.ToolPolicyVersion,
                run.ContextSchemaVersion,
                run.ContextCanonicalizationVersion,
                run.OutputSchemaVersion,
                ContextHash: string.Empty,
                run.InvocationKind,
                run.TicketInstruction,
                run.TicketProposalSetId,
                run.TicketProposalRevision,
                ticketProposalSnapshotJson);
            var contextHash = ComputeContextHash(contextWithoutHash);
            var context = contextWithoutHash with { ContextHash = contextHash };
            var snapshotJson = WorkbenchBusinessAnalystContextCodec.Serialize(context);

            var bound = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.WorkbenchAgentRuns
                SET ContextSnapshotJson=@ContextSnapshotJson, ContextHash=@ContextHash,
                    BasedOnUnderstandingRevision=@BasedOnUnderstandingRevision
                WHERE AgentRunId=@AgentRunId AND Status=N'Running' AND ClaimToken=@ClaimToken
                  AND ContextSnapshotJson IS NULL;

                UPDATE dbo.WorkbenchAgentRunAttempts
                SET ContextHash=@ContextHash
                WHERE ClaimToken=@ClaimToken;
                """,
                new
                {
                    run.AgentRunId,
                    claim.ClaimToken,
                    ContextSnapshotJson = snapshotJson,
                    ContextHash = contextHash,
                    BasedOnUnderstandingRevision = project.UnderstandingRevision
                },
                transaction,
                cancellationToken: cancellationToken));
            if (bound == 0)
                throw new WorkbenchLeaseFenceException();

            transaction.Commit();
            return context;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    internal static string ComputeContextHash(WorkbenchBusinessAnalystContext context) =>
        WorkbenchBusinessAnalystContextCodec.ComputeHash(context);

    private static void EnsureContextIntegrity(
        WorkbenchBusinessAnalystContext context,
        ContextRunRow run)
    {
        if (string.IsNullOrWhiteSpace(run.ContextHash) || run.BasedOnUnderstandingRevision is null ||
            !string.Equals(context.ContextHash, run.ContextHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeContextHash(context), run.ContextHash, StringComparison.OrdinalIgnoreCase) ||
            context.AgentRunId != run.AgentRunId || context.TenantId != run.TenantId ||
            context.ProjectId != run.ProjectId || context.WorkbenchSessionId != run.WorkbenchSessionId ||
            context.LeaseEpoch != run.LeaseEpoch || context.ChatSessionId != run.ChatSessionId ||
            context.SourceUserMessageId != run.SourceUserMessageId ||
            context.UnderstandingRevision != run.BasedOnUnderstandingRevision ||
            context.AgentVersion != run.AgentVersion || context.PromptVersion != run.PromptVersion ||
            context.ToolPolicyVersion != run.ToolPolicyVersion ||
            context.ContextSchemaVersion != run.ContextSchemaVersion ||
            context.ContextCanonicalizationVersion != run.ContextCanonicalizationVersion ||
            context.OutputSchemaVersion != run.OutputSchemaVersion ||
            context.InvocationKind != run.InvocationKind ||
            context.TicketInstruction != run.TicketInstruction ||
            context.TicketProposalSetId != run.TicketProposalSetId ||
            context.TicketProposalRevision != run.TicketProposalRevision)
            throw new InvalidOperationException("The stored Workbench agent context failed its integrity check.");
    }

    private static void EnsureCurrentClaim(ContextRunRow run, WorkbenchAgentRunClaim claim)
    {
        if (run.Status != WorkbenchAgentRunStates.Running || run.ClaimToken != claim.ClaimToken ||
            run.AgentRunId != claim.AgentRunId || run.TenantId != claim.TenantId ||
            run.ProjectId != claim.ProjectId || run.WorkbenchSessionId != claim.WorkbenchSessionId ||
            run.LeaseEpoch != claim.LeaseEpoch || run.ActorUserId != claim.ActorUserId ||
            run.ChatSessionId != claim.ChatSessionId || run.SourceUserMessageId != claim.SourceUserMessageId ||
            run.AgentVersion != claim.AgentVersion || run.PromptVersion != claim.PromptVersion ||
            run.ToolPolicyVersion != claim.ToolPolicyVersion ||
            run.ContextSchemaVersion != claim.ContextSchemaVersion ||
            run.ContextCanonicalizationVersion != claim.ContextCanonicalizationVersion ||
            run.OutputSchemaVersion != claim.OutputSchemaVersion ||
            run.InvocationKind != claim.InvocationKind ||
            run.TicketInstruction != claim.TicketInstruction ||
            run.TicketProposalSetId != claim.TicketProposalSetId ||
            run.TicketProposalRevision != claim.TicketProposalRevision)
            throw new WorkbenchLeaseFenceException();
    }

    private sealed class ContextRunRow
    {
        public Guid AgentRunId { get; init; }
        public int TenantId { get; init; }
        public int ProjectId { get; init; }
        public long WorkbenchSessionId { get; init; }
        public long LeaseEpoch { get; init; }
        public int ActorUserId { get; init; }
        public long ChatSessionId { get; init; }
        public long SourceUserMessageId { get; init; }
        public string Status { get; init; } = string.Empty;
        public Guid? ClaimToken { get; init; }
        public string AgentVersion { get; init; } = string.Empty;
        public string PromptVersion { get; init; } = string.Empty;
        public string ToolPolicyVersion { get; init; } = string.Empty;
        public int ContextSchemaVersion { get; init; }
        public int ContextCanonicalizationVersion { get; init; }
        public int OutputSchemaVersion { get; init; }
        public string InvocationKind { get; init; } = WorkbenchAgentInvocationKinds.Conversation;
        public string? TicketInstruction { get; init; }
        public Guid? TicketProposalSetId { get; init; }
        public long? TicketProposalRevision { get; init; }
        public string? ContextSnapshotJson { get; init; }
        public string? ContextHash { get; init; }
        public long? BasedOnUnderstandingRevision { get; init; }
    }


    private sealed class ProjectContextRow
    {
        public string ProjectName { get; init; } = string.Empty;
        public long UnderstandingRevision { get; init; }
        public string UnderstandingJson { get; init; } = "{}";
    }

    private sealed class ProposalSnapshotRow
    {
        public string SnapshotJson { get; init; } = string.Empty;
        public string SnapshotHash { get; init; } = string.Empty;
    }
}

public sealed class WorkbenchAgentRunOutbox : IWorkbenchAgentRunOutbox
{
    private readonly IDbConnectionFactory _connections;

    public WorkbenchAgentRunOutbox(IDbConnectionFactory connections) => _connections = connections;

    public async Task<IReadOnlyList<WorkbenchAgentRunOutboxItem>> ReadPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));

        using var connection = _connections.CreateConnection();
        var rows = await connection.QueryAsync<WorkbenchAgentRunOutboxItem>(new CommandDefinition(
            """
            SELECT TOP (@MaximumCount)
                   candidate.OutboxEventId,
                   candidate.AgentRunId
            FROM
            (
                SELECT event.Id AS OutboxEventId, event.AgentRunId, event.OccurredAtUtc AS ReadyAtUtc, 0 AS Recovery
                FROM dbo.WorkbenchOutboxEvents event
                WHERE event.EventKind=N'AgentRunRequested' AND event.PublishedAtUtc IS NULL
                  AND event.AgentRunId IS NOT NULL

                UNION ALL

                SELECT CONVERT(BIGINT, 0) AS OutboxEventId, run.AgentRunId,
                       COALESCE(run.ClaimExpiresAtUtc, run.CreatedAtUtc) AS ReadyAtUtc, 1 AS Recovery
                FROM dbo.WorkbenchAgentRuns run
                WHERE run.Status=N'Running' AND run.ClaimExpiresAtUtc <= SYSUTCDATETIME()
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM dbo.WorkbenchOutboxEvents request
                      WHERE request.AgentRunId=run.AgentRunId
                        AND request.EventKind=N'AgentRunRequested' AND request.PublishedAtUtc IS NULL
                  )
            ) candidate
            ORDER BY candidate.ReadyAtUtc, candidate.Recovery,
                     candidate.OutboxEventId, candidate.AgentRunId;
            """,
            new { MaximumCount = maximumCount },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task MarkPublishedAsync(long outboxEventId, CancellationToken cancellationToken = default)
    {
        if (outboxEventId <= 0)
            return;
        using var connection = _connections.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.WorkbenchOutboxEvents
            SET PublishedAtUtc=COALESCE(PublishedAtUtc, SYSUTCDATETIME())
            WHERE Id=@OutboxEventId AND EventKind=N'AgentRunRequested';
            """,
            new { OutboxEventId = outboxEventId },
            cancellationToken: cancellationToken));
    }
}

public sealed class WorkbenchAgentRunProcessor : IWorkbenchAgentRunProcessor
{
    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumProviderTimeout = TimeSpan.FromMinutes(4);
    private readonly IWorkbenchAgentRunService _runs;
    private readonly IWorkbenchAgentContextAssembler _contexts;
    private readonly IWorkbenchBusinessAnalystAgent _agent;
    private readonly IWorkbenchAgentRunOutbox _outbox;
    private readonly IWorkbenchBusinessAnalystInvocationAuditStore _invocationAudit;

    public WorkbenchAgentRunProcessor(
        IWorkbenchAgentRunService runs,
        IWorkbenchAgentContextAssembler contexts,
        IWorkbenchBusinessAnalystAgent agent,
        IWorkbenchAgentRunOutbox outbox,
        IWorkbenchBusinessAnalystInvocationAuditStore invocationAudit)
    {
        _runs = runs;
        _contexts = contexts;
        _agent = agent;
        _outbox = outbox;
        _invocationAudit = invocationAudit;
    }

    public async Task ProcessAsync(
        WorkbenchAgentRunOutboxItem item,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var claim = await _runs.ClaimAsync(item.AgentRunId, workerId, ClaimDuration, cancellationToken);
        if (claim is null)
        {
            await _outbox.MarkPublishedAsync(item.OutboxEventId, cancellationToken);
            return;
        }

        // Once claimed, an expired Running claim is the durable recovery signal.
        await _outbox.MarkPublishedAsync(item.OutboxEventId, cancellationToken);

        string? rawOutput = null;
        var providerInvocationTerminal = false;
        try
        {
            var context = await _contexts.AssembleAsync(claim, cancellationToken);
            var invocation = await _agent.PrepareAsync(claim, context, cancellationToken);
            if (invocation.ProviderTimeout <= TimeSpan.Zero ||
                invocation.ProviderTimeout > MaximumProviderTimeout ||
                invocation.ProviderTimeout >= ClaimDuration)
                throw new WorkbenchAgentProviderTimeoutConfigurationException(
                    $"The Business Analyst provider timeout must be greater than zero and no more than {MaximumProviderTimeout.TotalSeconds:0} seconds.");

            // This is deliberately the final await before InvokeProviderAsync. Provider/profile
            // resolution, prompt construction, and tool preparation must all finish before the
            // exact authority/fence check so revocation cannot be hidden behind preparation work.
            if (!await _runs.AuthorizeInvocationAsync(claim, ClaimDuration, cancellationToken))
                return;

            using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            providerCancellation.CancelAfter(invocation.ProviderTimeout);
            Task<WorkbenchBusinessAnalystProviderResponse>? providerTask = null;
            WorkbenchBusinessAnalystProviderResponse providerResponse;
            var providerStarted = Stopwatch.GetTimestamp();
            try
            {
                providerTask = invocation.InvokeProviderAsync(providerCancellation.Token);
                providerResponse = await providerTask.WaitAsync(
                    invocation.ProviderTimeout,
                    cancellationToken);
            }
            catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
            {
                providerCancellation.Cancel();
                ObserveLateProviderFault(providerTask);
                providerInvocationTerminal = true;
                await RecordFailedInvocationAsync(
                    claim,
                    invocation.SafeRequestId,
                    "agent_provider_timeout",
                    ElapsedMilliseconds(providerStarted),
                    CancellationToken.None);
                throw new WorkbenchAgentProviderTimeoutException();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                ObserveLateProviderFault(providerTask);
                // Some provider clients enforce their own timeout and may throw before the linked
                // timeout token observes cancellation. Any provider-side cancellation that is not
                // the worker shutdown token is the same bounded provider-timeout outcome.
                providerInvocationTerminal = true;
                await RecordFailedInvocationAsync(
                    claim,
                    invocation.SafeRequestId,
                    "agent_provider_timeout",
                    ElapsedMilliseconds(providerStarted),
                    CancellationToken.None);
                throw new WorkbenchAgentProviderTimeoutException();
            }
            catch (OperationCanceledException)
            {
                providerCancellation.Cancel();
                if (providerTask?.IsCompletedSuccessfully == true)
                {
                    // Completion and worker cancellation can race. Once the provider has
                    // produced a response, enter the non-cancellable audit/materialization
                    // commit phase so recovery cannot invoke the same attempt again.
                    providerResponse = await providerTask.ConfigureAwait(false);
                    providerInvocationTerminal = true;
                }
                else
                {
                    ObserveLateProviderFault(providerTask);
                    // Record the abandoned invocation independently of worker shutdown
                    // before leaving the claim recoverable. A later attempt then has an
                    // explicit, distinct predecessor rather than a hidden provider call.
                    providerInvocationTerminal = true;
                    await RecordFailedInvocationAsync(
                        claim,
                        invocation.SafeRequestId,
                        "agent_worker_cancelled",
                        ElapsedMilliseconds(providerStarted),
                        CancellationToken.None);
                    providerInvocationTerminal = false;
                    throw;
                }
            }
            catch (Exception)
            {
                providerInvocationTerminal = true;
                await RecordFailedInvocationAsync(
                    claim,
                    invocation.SafeRequestId,
                    "agent_provider_failed",
                    ElapsedMilliseconds(providerStarted),
                    CancellationToken.None);
                throw;
            }

            providerInvocationTerminal = true;
            await _invocationAudit.RecordAsync(
                new WorkbenchBusinessAnalystInvocationAudit
                {
                    AgentRunId = claim.AgentRunId,
                    ClaimToken = claim.ClaimToken,
                    AttemptNumber = claim.AttemptCount,
                    SafeRequestId = providerResponse.SafeRequestId,
                    ProviderRequestId = providerResponse.ProviderRequestId,
                    UsageReported = providerResponse.UsageReported,
                    Usage = providerResponse.Usage,
                    DurationMilliseconds = providerResponse.DurationMilliseconds,
                    Outcome = WorkbenchBusinessAnalystInvocationOutcome.Succeeded,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                },
                CancellationToken.None);
            rawOutput = providerResponse.Output;
            var output = WorkbenchBusinessAnalystOutputValidator.DeserializeAndValidate(rawOutput, context);
            await _runs.MaterializeAsync(claim, context, output, CancellationToken.None);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested &&
            !providerInvocationTerminal)
        {
            // Leave the durable Running claim to expire so another worker can recover it.
            throw;
        }
        catch (Exception exception)
        {
            var code = exception switch
            {
                WorkbenchAgentOutputValidationException => "agent_output_schema_invalid",
                WorkbenchBusinessAnalystContractNotSupportedException => "agent_contract_version_unsupported",
                WorkbenchBusinessAnalystContextTooLargeException =>
                    WorkbenchBusinessAnalystContextTooLargeException.ErrorCode,
                WorkbenchBusinessAnalystRoleAwareProviderRequiredException =>
                    WorkbenchBusinessAnalystRoleAwareProviderRequiredException.ErrorCode,
                WorkbenchBusinessAnalystProviderEnvelopeException =>
                    "agent_provider_envelope_invalid",
                WorkbenchAgentProviderTimeoutException => "agent_provider_timeout",
                WorkbenchAgentProviderTimeoutConfigurationException => "agent_provider_timeout_invalid",
                _ => "agent_execution_failed"
            };
            var diagnosticHash = exception is WorkbenchAgentOutputValidationException && rawOutput is not null
                ? WorkbenchAgentRunService.ComputeHash(rawOutput)
                : WorkbenchAgentRunService.ComputeHash(
                    $"{exception.GetType().FullName}\n{exception.Message}");
            await _runs.MarkFailedAsync(
                claim,
                code,
                diagnosticHash,
                providerInvocationTerminal ? CancellationToken.None : cancellationToken);
        }
    }

    private async Task RecordFailedInvocationAsync(
        WorkbenchAgentRunClaim claim,
        string safeRequestId,
        string failureCategory,
        long durationMilliseconds,
        CancellationToken cancellationToken)
    {
        _ = await _invocationAudit.RecordAsync(
            new WorkbenchBusinessAnalystInvocationAudit
            {
                AgentRunId = claim.AgentRunId,
                ClaimToken = claim.ClaimToken,
                AttemptNumber = claim.AttemptCount,
                SafeRequestId = safeRequestId,
                UsageReported = false,
                DurationMilliseconds = durationMilliseconds,
                Outcome = WorkbenchBusinessAnalystInvocationOutcome.Failed,
                FailureCategory = failureCategory,
                CompletedAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);
    }

    private static long ElapsedMilliseconds(long startedTimestamp) =>
        Math.Max(
            0,
            Stopwatch.GetElapsedTime(startedTimestamp).Ticks /
            TimeSpan.TicksPerMillisecond);

    private static void ObserveLateProviderFault(Task? providerTask)
    {
        if (providerTask is null || providerTask.IsCompletedSuccessfully || providerTask.IsCanceled)
            return;

        if (providerTask.IsFaulted)
        {
            _ = providerTask.Exception;
            return;
        }

        _ = providerTask.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

}
