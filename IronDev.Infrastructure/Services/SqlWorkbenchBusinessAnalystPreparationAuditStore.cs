using System.Data;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class SqlWorkbenchBusinessAnalystPreparationAuditStore
    : IWorkbenchBusinessAnalystPreparationAuditStore
{
    private readonly IDbConnectionFactory _connections;
    private readonly IWorkbenchBusinessAnalystExecutableContractRegistry _contracts;

    public SqlWorkbenchBusinessAnalystPreparationAuditStore(
        IDbConnectionFactory connections,
        IWorkbenchBusinessAnalystExecutableContractRegistry contracts)
    {
        _connections = connections;
        _contracts = contracts;
    }

    public async Task<WorkbenchBusinessAnalystPreparationWriteResult> RecordAsync(
        WorkbenchBusinessAnalystPreparationProvenance provenance,
        CancellationToken cancellationToken = default)
    {
        var normalized = WorkbenchBusinessAnalystPreparationAuditCanonicalizer.NormalizeAndValidate(provenance);
        var preparationHash = WorkbenchBusinessAnalystPreparationAuditCanonicalizer.ComputePreparationHash(normalized);
        var toolCallHashes = normalized.ToolCalls.ToDictionary(
            call => call.ToolName,
            WorkbenchBusinessAnalystPreparationAuditCanonicalizer.ComputeToolCallHash,
            StringComparer.OrdinalIgnoreCase);

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // Preserve the run-before-attempt lock order used by the terminal paths so
            // preparation cannot deadlock a concurrent cancel or materialization.
            var run = await connection.QuerySingleOrDefaultAsync<RunBindingRow>(new CommandDefinition(
                """
                SELECT Status AS RunStatus,
                       ClaimToken AS CurrentClaimToken,
                       AttemptCount AS CurrentAttemptNumber,
                       AgentVersion,
                       PromptVersion,
                       ToolPolicyVersion,
                       ContextSchemaVersion,
                       ContextCanonicalizationVersion,
                       OutputSchemaVersion
                FROM dbo.WorkbenchAgentRuns WITH (UPDLOCK, HOLDLOCK)
                WHERE AgentRunId=@AgentRunId;
                """,
                new { normalized.AgentRunId },
                transaction,
                cancellationToken: cancellationToken));
            if (run is null)
                throw Conflict("The preparation does not match a durable Workbench agent run.");
            ValidatePinnedContract(normalized, run);

            var attempt = await connection.QuerySingleOrDefaultAsync<AttemptBindingRow>(new CommandDefinition(
                """
                SELECT Id AS AgentRunAttemptId,
                       CompletedAtUtc AS AttemptCompletedAtUtc
                FROM dbo.WorkbenchAgentRunAttempts WITH (UPDLOCK, HOLDLOCK)
                WHERE AgentRunId=@AgentRunId
                  AND ClaimToken=@ClaimToken
                  AND AttemptNumber=@AttemptNumber;
                """,
                new { normalized.AgentRunId, normalized.ClaimToken, normalized.AttemptNumber },
                transaction,
                cancellationToken: cancellationToken));
            if (attempt is null)
                throw Conflict("The preparation does not match a durable Workbench agent-run attempt.");

            var existing = await connection.QuerySingleOrDefaultAsync<ExistingPreparationRow>(new CommandDefinition(
                """
                SELECT Id, PreparationHash
                FROM dbo.WorkbenchBusinessAnalystPreparations WITH (UPDLOCK, HOLDLOCK)
                WHERE AgentRunAttemptId=@AgentRunAttemptId;
                """,
                new { attempt.AgentRunAttemptId },
                transaction,
                cancellationToken: cancellationToken));
            if (existing is not null)
            {
                await EnsureIdenticalReplayAsync(
                    connection,
                    transaction,
                    existing,
                    preparationHash,
                    toolCallHashes,
                    cancellationToken);
                transaction.Commit();
                return Result(
                    WorkbenchBusinessAnalystPreparationWriteStatus.AlreadyExists,
                    preparationHash,
                    toolCallHashes);
            }

            if (!string.Equals(run.RunStatus, WorkbenchAgentRunStates.Running, StringComparison.Ordinal) ||
                run.CurrentClaimToken != normalized.ClaimToken ||
                run.CurrentAttemptNumber != normalized.AttemptNumber ||
                attempt.AttemptCompletedAtUtc is not null)
            {
                throw Conflict("The preparation attempt is no longer the current unfinished Workbench claim.");
            }

            var preparationId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT dbo.WorkbenchBusinessAnalystPreparations
                (
                    AgentRunAttemptId, AgentRunId, ClaimToken, AttemptNumber,
                    EffectiveAnalystProfileHash, AnalystProfilePublishedVersion,
                    ActualProvider, ActualModel, ProviderTimeoutSeconds,
                    PromptHash, ToolManifestHash, PreparationHash, PreparedAtUtc
                )
                OUTPUT inserted.Id
                VALUES
                (
                    @AgentRunAttemptId, @AgentRunId, @ClaimToken, @AttemptNumber,
                    @EffectiveAnalystProfileHash, @AnalystProfilePublishedVersion,
                    @ActualProvider, @ActualModel, @ProviderTimeoutSeconds,
                    @PromptHash, @ToolManifestHash, @PreparationHash, @PreparedAtUtc
                );
                """,
                new
                {
                    attempt.AgentRunAttemptId,
                    normalized.AgentRunId,
                    normalized.ClaimToken,
                    normalized.AttemptNumber,
                    normalized.EffectiveAnalystProfileHash,
                    normalized.AnalystProfilePublishedVersion,
                    normalized.ActualProvider,
                    normalized.ActualModel,
                    normalized.ProviderTimeoutSeconds,
                    normalized.PromptHash,
                    normalized.ToolManifestHash,
                    PreparationHash = preparationHash,
                    PreparedAtUtc = normalized.PreparedAtUtc.UtcDateTime
                },
                transaction,
                cancellationToken: cancellationToken));

            foreach (var call in normalized.ToolCalls)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT dbo.WorkbenchBusinessAnalystToolCallAudits
                    (
                        PreparationId, AgentRunId, ClaimToken, AttemptNumber,
                        ToolName, DefinitionVersion, PolicyVersion, Status,
                        InputHash, OutputHash, SafeSummary,
                        StartedAtUtc, CompletedAtUtc, ToolCallHash
                    )
                    VALUES
                    (
                        @PreparationId, @AgentRunId, @ClaimToken, @AttemptNumber,
                        @ToolName, @DefinitionVersion, @PolicyVersion, @Status,
                        @InputHash, @OutputHash, @SafeSummary,
                        @StartedAtUtc, @CompletedAtUtc, @ToolCallHash
                    );
                    """,
                    new
                    {
                        PreparationId = preparationId,
                        normalized.AgentRunId,
                        normalized.ClaimToken,
                        normalized.AttemptNumber,
                        call.ToolName,
                        call.DefinitionVersion,
                        call.PolicyVersion,
                        Status = call.Status.ToString(),
                        call.InputHash,
                        call.OutputHash,
                        call.SafeSummary,
                        StartedAtUtc = call.StartedAtUtc.UtcDateTime,
                        CompletedAtUtc = call.CompletedAtUtc.UtcDateTime,
                        ToolCallHash = toolCallHashes[call.ToolName]
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            transaction.Commit();
            return Result(
                WorkbenchBusinessAnalystPreparationWriteStatus.Recorded,
                preparationHash,
                toolCallHashes);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task EnsureIdenticalReplayAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ExistingPreparationRow existing,
        string preparationHash,
        IReadOnlyDictionary<string, string> toolCallHashes,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(existing.PreparationHash, preparationHash, StringComparison.Ordinal))
            throw Conflict("Preparation provenance already exists with different sanitized content.");

        var storedCalls = (await connection.QueryAsync<ExistingToolCallRow>(new CommandDefinition(
            """
            SELECT ToolName, ToolCallHash
            FROM dbo.WorkbenchBusinessAnalystToolCallAudits WITH (HOLDLOCK)
            WHERE PreparationId=@PreparationId;
            """,
            new { PreparationId = existing.Id },
            transaction,
            cancellationToken: cancellationToken))).AsList();
        if (storedCalls.Count != toolCallHashes.Count ||
            storedCalls.Any(stored =>
                !toolCallHashes.TryGetValue(stored.ToolName, out var expected) ||
                !string.Equals(stored.ToolCallHash, expected, StringComparison.Ordinal)))
        {
            throw Conflict("Stored preparation tool-call provenance does not match the identical replay.");
        }
    }

    private static WorkbenchBusinessAnalystPreparationWriteResult Result(
        WorkbenchBusinessAnalystPreparationWriteStatus status,
        string preparationHash,
        IReadOnlyDictionary<string, string> toolCallHashes) =>
        new()
        {
            Status = status,
            PreparationHash = preparationHash,
            ToolCallHashes = new Dictionary<string, string>(toolCallHashes, StringComparer.OrdinalIgnoreCase)
        };

    private void ValidatePinnedContract(
        WorkbenchBusinessAnalystPreparationProvenance provenance,
        RunBindingRow run)
    {
        var key = new WorkbenchBusinessAnalystContractKey(
            run.AgentVersion,
            run.PromptVersion,
            run.ToolPolicyVersion,
            run.ContextSchemaVersion,
            run.ContextCanonicalizationVersion,
            run.OutputSchemaVersion);
        var matches = _contracts.List()
            .Where(contract => contract.Key == key)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
            throw Conflict("The durable Workbench run does not pin exactly one executable Business Analyst contract.");

        var contract = matches[0];
        var expectedManifestHash =
            WorkbenchBusinessAnalystPreparationAuditCanonicalizer.ComputeToolManifestHash(contract);
        if (!string.Equals(provenance.ToolManifestHash, expectedManifestHash, StringComparison.Ordinal))
            throw Conflict("The preparation tool manifest does not match the durable Workbench run contract.");
        if (provenance.ToolCalls.Count != contract.SnapshotTools.Count)
            throw Conflict("The preparation must audit exactly the snapshot tools pinned by the durable Workbench run.");

        var actualByName = provenance.ToolCalls.ToDictionary(
            call => call.ToolName,
            StringComparer.Ordinal);
        foreach (var expected in contract.SnapshotTools)
        {
            if (!actualByName.TryGetValue(expected.Name, out var actual) ||
                !string.Equals(actual.DefinitionVersion, expected.Version, StringComparison.Ordinal) ||
                !string.Equals(actual.PolicyVersion, contract.Key.ToolPolicyVersion, StringComparison.Ordinal) ||
                actual.Status != WorkbenchBusinessAnalystToolCallAuditStatus.Completed)
            {
                throw Conflict(
                    "The preparation tool audit does not match the name, definition, policy, and successful status pinned by the durable Workbench run contract.");
            }
        }
    }

    private static WorkbenchBusinessAnalystPreparationAuditConflictException Conflict(string message) => new(message);

    private sealed class AttemptBindingRow
    {
        public long AgentRunAttemptId { get; init; }
        public DateTime? AttemptCompletedAtUtc { get; init; }
    }

    private sealed class RunBindingRow
    {
        public string RunStatus { get; init; } = string.Empty;
        public Guid? CurrentClaimToken { get; init; }
        public int CurrentAttemptNumber { get; init; }
        public string AgentVersion { get; init; } = string.Empty;
        public string PromptVersion { get; init; } = string.Empty;
        public string ToolPolicyVersion { get; init; } = string.Empty;
        public int ContextSchemaVersion { get; init; }
        public int ContextCanonicalizationVersion { get; init; }
        public int OutputSchemaVersion { get; init; }
    }

    private sealed class ExistingPreparationRow
    {
        public long Id { get; init; }
        public string PreparationHash { get; init; } = string.Empty;
    }

    private sealed class ExistingToolCallRow
    {
        public string ToolName { get; init; } = string.Empty;
        public string ToolCallHash { get; init; } = string.Empty;
    }
}
