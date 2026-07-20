using System.Data;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class SqlWorkbenchBusinessAnalystInvocationAuditStore
    : IWorkbenchBusinessAnalystInvocationAuditStore
{
    private readonly IDbConnectionFactory _connections;

    public SqlWorkbenchBusinessAnalystInvocationAuditStore(
        IDbConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<WorkbenchBusinessAnalystInvocationAuditWriteResult> RecordAsync(
        WorkbenchBusinessAnalystInvocationAudit audit,
        CancellationToken cancellationToken = default)
    {
        var normalized = WorkbenchBusinessAnalystInvocationAuditCanonicalizer
            .NormalizeAndValidate(audit);
        var invocationHash = WorkbenchBusinessAnalystInvocationAuditCanonicalizer
            .ComputeHash(normalized);

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // Preserve the run -> attempt -> preparation lock order used by the run
            // terminal paths and preparation store. A concurrent cancellation may make
            // the attempt terminal, but it cannot erase the fact that invocation occurred.
            var runExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.WorkbenchAgentRuns WITH (UPDLOCK, HOLDLOCK)
                WHERE AgentRunId=@AgentRunId;
                """,
                new { normalized.AgentRunId },
                transaction,
                cancellationToken: cancellationToken));
            if (runExists != 1)
                throw Conflict("The invocation does not match a durable Workbench agent run.");

            var attemptExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.WorkbenchAgentRunAttempts WITH (UPDLOCK, HOLDLOCK)
                WHERE AgentRunId=@AgentRunId
                  AND ClaimToken=@ClaimToken
                  AND AttemptNumber=@AttemptNumber;
                """,
                new
                {
                    normalized.AgentRunId,
                    normalized.ClaimToken,
                    normalized.AttemptNumber
                },
                transaction,
                cancellationToken: cancellationToken));
            if (attemptExists != 1)
                throw Conflict("The invocation does not match a durable Workbench agent-run attempt.");

            var preparation = await connection.QuerySingleOrDefaultAsync<PreparationRow>(new CommandDefinition(
                """
                SELECT Id AS PreparationId
                FROM dbo.WorkbenchBusinessAnalystPreparations WITH (UPDLOCK, HOLDLOCK)
                WHERE AgentRunId=@AgentRunId
                  AND ClaimToken=@ClaimToken
                  AND AttemptNumber=@AttemptNumber;
                """,
                new
                {
                    normalized.AgentRunId,
                    normalized.ClaimToken,
                    normalized.AttemptNumber
                },
                transaction,
                cancellationToken: cancellationToken));
            if (preparation is null)
                throw Conflict("The invocation does not match durable Business Analyst preparation provenance.");

            var existingHash = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT InvocationHash
                FROM dbo.WorkbenchBusinessAnalystInvocationAudits WITH (UPDLOCK, HOLDLOCK)
                WHERE PreparationId=@PreparationId;
                """,
                new { preparation.PreparationId },
                transaction,
                cancellationToken: cancellationToken));
            if (existingHash is not null)
            {
                if (!string.Equals(existingHash, invocationHash, StringComparison.Ordinal))
                    throw Conflict("Invocation provenance already exists with different sanitized content.");
                transaction.Commit();
                return Result(
                    WorkbenchBusinessAnalystInvocationAuditWriteStatus.AlreadyExists,
                    invocationHash);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchBusinessAnalystInvocationAudits
                (
                    PreparationId, AgentRunId, ClaimToken, AttemptNumber,
                    SafeRequestId, ProviderRequestId,
                    UsageReported, InputTokens, OutputTokens,
                    DurationMilliseconds, Outcome, FailureCategory,
                    InvocationHash, CompletedAtUtc
                )
                VALUES
                (
                    @PreparationId, @AgentRunId, @ClaimToken, @AttemptNumber,
                    @SafeRequestId, @ProviderRequestId,
                    @UsageReported, @InputTokens, @OutputTokens,
                    @DurationMilliseconds, @Outcome, @FailureCategory,
                    @InvocationHash, @CompletedAtUtc
                );
                """,
                new
                {
                    preparation.PreparationId,
                    normalized.AgentRunId,
                    normalized.ClaimToken,
                    normalized.AttemptNumber,
                    normalized.SafeRequestId,
                    normalized.ProviderRequestId,
                    normalized.UsageReported,
                    InputTokens = normalized.UsageReported
                        ? normalized.Usage.InputTokens
                        : (int?)null,
                    OutputTokens = normalized.UsageReported
                        ? normalized.Usage.OutputTokens
                        : (int?)null,
                    normalized.DurationMilliseconds,
                    Outcome = normalized.Outcome.ToString(),
                    normalized.FailureCategory,
                    InvocationHash = invocationHash,
                    CompletedAtUtc = normalized.CompletedAtUtc.UtcDateTime
                },
                transaction,
                cancellationToken: cancellationToken));

            transaction.Commit();
            return Result(
                WorkbenchBusinessAnalystInvocationAuditWriteStatus.Recorded,
                invocationHash);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static WorkbenchBusinessAnalystInvocationAuditWriteResult Result(
        WorkbenchBusinessAnalystInvocationAuditWriteStatus status,
        string invocationHash) =>
        new() { Status = status, InvocationHash = invocationHash };

    private static WorkbenchBusinessAnalystInvocationAuditConflictException Conflict(
        string message) => new(message);

    private sealed class PreparationRow
    {
        public long PreparationId { get; init; }
    }
}
