using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchAgentContextAssembler : IWorkbenchAgentContextAssembler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
                       AgentVersion, PromptVersion, ToolPolicyVersion, OutputSchemaVersion,
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
                var stored = JsonSerializer.Deserialize<WorkbenchBusinessAnalystContext>(run.ContextSnapshotJson, JsonOptions)
                    ?? throw new InvalidOperationException("The stored Workbench agent context could not be read.");
                EnsureContextIntegrity(stored, run.ContextHash, run.BasedOnUnderstandingRevision);
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

            var messages = (await connection.QueryAsync<WorkbenchAgentContextMessage>(new CommandDefinition(
                """
                SELECT bounded.Id AS MessageId, bounded.Role, bounded.Message, bounded.CreatedDate AS CreatedAtUtc
                FROM
                (
                    SELECT TOP (50) message.Id,
                           CASE WHEN message.Role=N'assistant' AND trustedRun.AgentRunId IS NOT NULL
                                THEN N'assistant' ELSE N'user' END AS Role,
                           message.Message, message.CreatedDate
                    FROM dbo.ChatMessages message
                    LEFT JOIN dbo.WorkbenchAgentRuns trustedRun
                        ON trustedRun.TenantId=message.TenantId
                       AND trustedRun.ProjectId=message.ProjectId
                       AND trustedRun.ChatSessionId=message.ChatSessionId
                       AND trustedRun.AssistantMessageId=message.Id
                       AND trustedRun.Status IN (N'Completed', N'NeedsInput')
                    WHERE message.TenantId=@TenantId AND message.ProjectId=@ProjectId
                      AND message.ChatSessionId=@ChatSessionId AND message.Id <= @SourceUserMessageId
                      AND message.Role IN (N'user', N'assistant')
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
                run.OutputSchemaVersion,
                ContextHash: string.Empty);
            var contextHash = ComputeContextHash(contextWithoutHash);
            var context = contextWithoutHash with { ContextHash = contextHash };
            var snapshotJson = JsonSerializer.Serialize(context, JsonOptions);

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
        WorkbenchAgentRunService.ComputeHash(JsonSerializer.Serialize(
            context with { ContextHash = string.Empty }, JsonOptions));

    private static void EnsureContextIntegrity(
        WorkbenchBusinessAnalystContext context,
        string? storedHash,
        long? storedRevision)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || storedRevision is null ||
            !string.Equals(context.ContextHash, storedHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ComputeContextHash(context), storedHash, StringComparison.OrdinalIgnoreCase) ||
            context.UnderstandingRevision != storedRevision)
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
            run.ToolPolicyVersion != claim.ToolPolicyVersion || run.OutputSchemaVersion != claim.OutputSchemaVersion)
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
        public int OutputSchemaVersion { get; init; }
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
            ORDER BY candidate.Recovery, candidate.ReadyAtUtc, candidate.OutboxEventId;
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
    private readonly IWorkbenchAgentRunService _runs;
    private readonly IWorkbenchAgentContextAssembler _contexts;
    private readonly IWorkbenchBusinessAnalystAgent _agent;
    private readonly IWorkbenchAgentRunOutbox _outbox;

    public WorkbenchAgentRunProcessor(
        IWorkbenchAgentRunService runs,
        IWorkbenchAgentContextAssembler contexts,
        IWorkbenchBusinessAnalystAgent agent,
        IWorkbenchAgentRunOutbox outbox)
    {
        _runs = runs;
        _contexts = contexts;
        _agent = agent;
        _outbox = outbox;
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
        try
        {
            var context = await _contexts.AssembleAsync(claim, cancellationToken);
            rawOutput = await _agent.ExecuteAsync(context, cancellationToken);
            var output = WorkbenchBusinessAnalystOutputValidator.DeserializeAndValidate(rawOutput, context);
            await _runs.MaterializeAsync(claim, context, output, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave the durable Running claim to expire so another worker can recover it.
            throw;
        }
        catch (Exception exception)
        {
            var code = exception is WorkbenchAgentOutputValidationException
                ? "agent_output_schema_invalid"
                : "agent_execution_failed";
            var diagnosticHash = exception is WorkbenchAgentOutputValidationException && rawOutput is not null
                ? WorkbenchAgentRunService.ComputeHash(rawOutput)
                : WorkbenchAgentRunService.ComputeHash(
                    $"{exception.GetType().FullName}\n{exception.Message}");
            await _runs.MarkFailedAsync(claim, code, diagnosticHash, cancellationToken);
        }
    }
}

public sealed class UnavailableWorkbenchBusinessAnalystAgent : IWorkbenchBusinessAnalystAgent
{
    public Task<string> ExecuteAsync(
        WorkbenchBusinessAnalystContext context,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "The Business Analyst host is not configured. PR-02B supplies the process-ready agent implementation.");
}
