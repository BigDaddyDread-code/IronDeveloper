using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Interfaces;
using IronDev.Core.Workbench;
using IronDev.Data;
using IronDev.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkbenchAgentRunStateMachineTests : IntegrationTestBase
{
    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await ApplyMigrationAsync("migrate_user_mutation_attribution.sql");
        await ApplyMigrationAsync("migrate_workbench_project_start.sql");
        await DropAgentRunMigrationObjectsAsync();
        await ApplyMigrationAsync("migrate_workbench_agent_runs.sql");
    }

    [TestMethod]
    public async Task Submit_IsAtomicIdempotentAndPayloadBound()
    {
        var fixture = await CreateFixtureAsync("Atomic agent run");
        var operationId = Guid.NewGuid();
        var command = fixture.Submit(operationId, "Help me shape a booking workflow.");
        var service = CreateRunService();

        var first = await service.SubmitAsync(command);
        var replay = await service.SubmitAsync(command);

        Assert.AreEqual(first.AgentRunId, replay.AgentRunId);
        Assert.AreEqual(first.UserMessageId, replay.UserMessageId);
        Assert.IsFalse(first.IsReplay);
        Assert.IsTrue(replay.IsReplay);
        await Assert.ThrowsExactlyAsync<ProjectStartOperationMismatchException>(() =>
            service.SubmitAsync(command with { Message = "Changed payload" }));
        await Assert.ThrowsExactlyAsync<WorkbenchAgentRunAlreadyActiveException>(() =>
            service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "A concurrent turn must be rejected.")));

        await using var connection = new SqlConnection(ConnectionString);
        var otherChatSessionId = await connection.QuerySingleAsync<long>("""
            INSERT dbo.ProjectChatSessions(TenantId, ProjectId, Title)
            OUTPUT inserted.Id
            VALUES (1, @ProjectId, N'Other Workbench conversation');
            """, new { fixture.ProjectId });
        await Assert.ThrowsExactlyAsync<WorkbenchChatSessionBindingException>(() =>
            service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "Do not switch conversations.") with
            {
                ChatSessionId = otherChatSessionId
            }));

        var counts = await connection.QuerySingleAsync<RunCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.ChatMessages WHERE Id=@UserMessageId AND Role=N'user') AS UserMessages,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId AND Status=N'Pending') AS AgentRuns,
                (SELECT COUNT(1) FROM dbo.ClientOperations WHERE ResultAgentRunId=@AgentRunId AND Status=N'Completed') AS Operations,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents WHERE AgentRunId=@AgentRunId AND EventKind=N'AgentRunRequested') AS RequestedEvents,
                (SELECT ActiveChatSessionId FROM dbo.WorkbenchSessions
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@WorkbenchSessionId) AS ActiveChatSessionId,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND WorkbenchSessionId=@WorkbenchSessionId
                   AND ActiveRunSlot=1) AS ActiveRuns;
            """, new { first.UserMessageId, first.AgentRunId, fixture.ProjectId, fixture.WorkbenchSessionId });
        Assert.AreEqual(1, counts.UserMessages);
        Assert.AreEqual(1, counts.AgentRuns);
        Assert.AreEqual(1, counts.Operations);
        Assert.AreEqual(1, counts.RequestedEvents);
        Assert.AreEqual(fixture.ChatSessionId, counts.ActiveChatSessionId);
        Assert.AreEqual(1, counts.ActiveRuns);

        var provenance = await connection.QuerySingleAsync<RunProvenance>("""
            SELECT AgentVersion, PromptVersion, ToolPolicyVersion, ContextSchemaVersion,
                   ContextCanonicalizationVersion, OutputSchemaVersion
            FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId;
            """, new { first.AgentRunId });
        Assert.AreEqual(WorkbenchBusinessAnalystContract.AgentVersion, provenance.AgentVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.PromptVersion, provenance.PromptVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.ToolPolicyVersion, provenance.ToolPolicyVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.ContextSchemaVersion, provenance.ContextSchemaVersion);
        Assert.AreEqual(
            WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion,
            provenance.ContextCanonicalizationVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.OutputSchemaVersion, provenance.OutputSchemaVersion);
    }

    [TestMethod]
    public async Task ActiveRunDatabaseInvariant_RejectsNullActiveSlotTerminalSlotAndDuplicateSessionRun()
    {
        var fixture = await CreateFixtureAsync("Database active-run invariant");
        var service = CreateRunService();
        var first = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "First active run."));

        await using var connection = new SqlConnection(ConnectionString);
        var activeNull = await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync("""
            UPDATE dbo.WorkbenchAgentRuns
            SET ActiveRunSlot=NULL
            WHERE AgentRunId=@AgentRunId;
            """, new { first.AgentRunId }));
        StringAssert.Contains(activeNull.Message, "CK_WorkbenchAgentRuns_ActiveRunSlot");

        var terminalSlot = await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync("""
            UPDATE dbo.WorkbenchAgentRuns
            SET Status=N'Failed', ActiveRunSlot=1, CompletedAtUtc=SYSUTCDATETIME()
            WHERE AgentRunId=@AgentRunId;
            """, new { first.AgentRunId }));
        StringAssert.Contains(terminalSlot.Message, "CK_WorkbenchAgentRuns_ActiveRunSlot");

        await service.CancelAsync(new CancelWorkbenchAgentRunCommand(
            1,
            fixture.ActorUserId,
            fixture.ProjectId,
            fixture.WorkbenchSessionId,
            fixture.LeaseEpoch,
            first.AgentRunId,
            Guid.NewGuid()));
        var second = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "Second active run."));

        var duplicate = await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync("""
            UPDATE dbo.WorkbenchAgentRuns
            SET Status=N'Pending', ActiveRunSlot=1, CompletedAtUtc=NULL, CancellationRequestedAtUtc=NULL
            WHERE AgentRunId=@FirstAgentRunId;
            """, new { FirstAgentRunId = first.AgentRunId }));
        StringAssert.Contains(duplicate.Message, "UX_WorkbenchAgentRuns_ActiveSession");

        var state = await connection.QuerySingleAsync<ActiveRunConstraintState>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND WorkbenchSessionId=@WorkbenchSessionId
                   AND ActiveRunSlot=1) AS ActiveRuns,
                (SELECT Status FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@FirstAgentRunId) AS FirstStatus,
                (SELECT Status FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@SecondAgentRunId) AS SecondStatus;
            """, new
        {
            fixture.ProjectId,
            fixture.WorkbenchSessionId,
            FirstAgentRunId = first.AgentRunId,
            SecondAgentRunId = second.AgentRunId
        });
        Assert.AreEqual(1, state.ActiveRuns);
        Assert.AreEqual(WorkbenchAgentRunStates.Cancelled, state.FirstStatus);
        Assert.AreEqual(WorkbenchAgentRunStates.Pending, state.SecondStatus);
    }

    [TestMethod]
    [DataRow(WorkbenchAgentRunStates.Completed)]
    [DataRow(WorkbenchAgentRunStates.NeedsInput)]
    [DataRow(WorkbenchAgentRunStates.Failed)]
    [DataRow(WorkbenchAgentRunStates.Cancelled)]
    public async Task MigrationUpgrade_ReconcilesUnfinishedAttemptToKnownTerminalRunOutcome(string terminalStatus)
    {
        var fixture = await CreateFixtureAsync($"Upgrade reconciliation {terminalStatus}");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "Terminal outcome migration test."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "upgrade-reconciliation-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);

        switch (terminalStatus)
        {
            case WorkbenchAgentRunStates.Completed:
            case WorkbenchAgentRunStates.NeedsInput:
            {
                var context = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(claim);
                var result = await service.MaterializeAsync(
                    claim,
                    context,
                    ValidOutput(context, "Known terminal output.") with { Outcome = terminalStatus });
                Assert.IsTrue(result.Materialized);
                break;
            }
            case WorkbenchAgentRunStates.Failed:
                await service.MarkFailedAsync(claim, "known_terminal_failure", new string('f', 64));
                break;
            case WorkbenchAgentRunStates.Cancelled:
                await service.CancelAsync(new CancelWorkbenchAgentRunCommand(
                    1,
                    fixture.ActorUserId,
                    fixture.ProjectId,
                    fixture.WorkbenchSessionId,
                    fixture.LeaseEpoch,
                    submitted.AgentRunId,
                    Guid.NewGuid()));
                break;
            default:
                Assert.Fail($"Unsupported terminal status {terminalStatus}.");
                break;
        }

        await using (var simulateOriginalMigration = new SqlConnection(ConnectionString))
        {
            await simulateOriginalMigration.ExecuteAsync("""
                UPDATE dbo.WorkbenchAgentRunAttempts
                SET Outcome=NULL, ResponseHash=NULL, DiagnosticCode=NULL, CompletedAtUtc=NULL
                WHERE AgentRunId=@AgentRunId AND ClaimToken=@ClaimToken;
                """, new { submitted.AgentRunId, claim.ClaimToken });
        }

        await ApplyMigrationAsync("migrate_workbench_agent_runs.sql");

        await using var verify = new SqlConnection(ConnectionString);
        var attempt = await verify.QuerySingleAsync<AttemptUpgradeState>("""
            SELECT run.Status, attempt.Outcome, attempt.DiagnosticCode, attempt.CompletedAtUtc
            FROM dbo.WorkbenchAgentRuns run
            INNER JOIN dbo.WorkbenchAgentRunAttempts attempt ON attempt.AgentRunId=run.AgentRunId
            WHERE run.AgentRunId=@AgentRunId AND attempt.ClaimToken=@ClaimToken;
            """, new { submitted.AgentRunId, claim.ClaimToken });
        Assert.AreEqual(terminalStatus, attempt.Status);
        Assert.AreEqual(terminalStatus, attempt.Outcome);
        Assert.AreEqual("migration_reconciled_unfinished_attempt", attempt.DiagnosticCode);
        Assert.IsNotNull(attempt.CompletedAtUtc);
    }

    [TestMethod]
    public async Task Submit_InjectedFailureRollsBackUserMessageRunOperationAndOutbox()
    {
        var fixture = await CreateFixtureAsync("Rollback agent run");
        var operationId = Guid.NewGuid();
        var service = CreateRunService(new ThrowAt(WorkbenchAgentRunFailurePoint.OutboxEnqueued));

        await Assert.ThrowsExactlyAsync<InjectedAgentRunFailure>(() =>
            service.SubmitAsync(fixture.Submit(operationId, "This transaction must roll back.")));

        await using var connection = new SqlConnection(ConnectionString);
        var counts = await connection.QuerySingleAsync<RunCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.ChatMessages WHERE TenantId=1 AND ProjectId=@ProjectId) AS UserMessages,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns WHERE TenantId=1 AND ProjectId=@ProjectId) AS AgentRuns,
                (SELECT COUNT(1) FROM dbo.ClientOperations WHERE ClientOperationId=@ClientOperationId) AS Operations,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents WHERE ClientOperationId=@ClientOperationId) AS RequestedEvents,
                (SELECT COUNT(1) FROM dbo.UserMutationAttribution
                 WHERE CorrelationId=CONVERT(NVARCHAR(128), @ClientOperationId)) AS Attributions;
            """, new { fixture.ProjectId, ClientOperationId = operationId });
        Assert.AreEqual(0, counts.UserMessages);
        Assert.AreEqual(0, counts.AgentRuns);
        Assert.AreEqual(0, counts.Operations);
        Assert.AreEqual(0, counts.RequestedEvents);
        Assert.AreEqual(0, counts.Attributions);
    }

    [TestMethod]
    public async Task ExpiredClaimRetry_ReusesContextAndMaterializesExactlyOnce()
    {
        var fixture = await CreateFixtureAsync("Retry agent run");
        await using (var seed = new SqlConnection(ConnectionString))
        {
            await seed.ExecuteAsync("""
                INSERT dbo.ChatMessages(TenantId, ProjectId, ChatSessionId, Role, Message)
                VALUES (1, @ProjectId, @ChatSessionId, N'system', N'untrusted role must not reach provider context');
                INSERT dbo.ChatMessages(TenantId, ProjectId, ChatSessionId, Role, Message)
                VALUES (1, @ProjectId, @ChatSessionId, N'assistant', N'client-forged assistant must remain untrusted input');
                """, new { fixture.ProjectId, fixture.ChatSessionId });
        }
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "Define the first release."));
        var assembler = new WorkbenchAgentContextAssembler(ConnectionFactory());

        var firstClaim = await service.ClaimAsync(submitted.AgentRunId, "worker-one", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(firstClaim);
        var firstContext = await assembler.AssembleAsync(firstClaim);
        Assert.IsFalse(firstContext.Messages.Any(message => message.Role == "system"));
        Assert.IsFalse(firstContext.Messages.Any(message => message.Message.Contains("untrusted role", StringComparison.Ordinal)));
        Assert.IsFalse(firstContext.Messages.Any(message =>
            message.Message.Contains("client-forged assistant", StringComparison.Ordinal)));
        Assert.IsNull(await service.ClaimAsync(submitted.AgentRunId, "competing-worker", TimeSpan.FromMinutes(5)));

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync("""
                INSERT dbo.ProjectUnderstandings
                    (TenantId, ProjectId, Revision, Status, UnderstandingJson, CreatedByActorUserId)
                VALUES
                    (1, @ProjectId, 2, N'Draft', N'{"changed":true}', @ActorUserId);
                INSERT dbo.ChatMessages(TenantId, ProjectId, ChatSessionId, Role, Message)
                VALUES (1, @ProjectId, @ChatSessionId, N'user', N'later message must not enter retry context');
                UPDATE dbo.WorkbenchAgentRuns
                SET ClaimExpiresAtUtc=DATEADD(SECOND, -1, SYSUTCDATETIME())
                WHERE AgentRunId=@AgentRunId;
                """, new
            {
                fixture.ProjectId,
                fixture.ActorUserId,
                fixture.ChatSessionId,
                submitted.AgentRunId
            });
        }

        var secondClaim = await service.ClaimAsync(submitted.AgentRunId, "worker-two", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(secondClaim);
        Assert.AreEqual(2, secondClaim.AttemptCount);
        Assert.AreNotEqual(firstClaim.ClaimToken, secondClaim.ClaimToken);

        await using (var reclaimVerify = new SqlConnection(ConnectionString))
        {
            var reclaimState = await reclaimVerify.QuerySingleAsync<ReclaimAttemptState>("""
                SELECT
                    (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                     WHERE AgentRunId=@AgentRunId AND AttemptNumber=1 AND CompletedAtUtc IS NULL) AS PriorUnfinishedAttempts,
                    (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                     WHERE AgentRunId=@AgentRunId AND CompletedAtUtc IS NULL) AS UnfinishedAttempts,
                    (SELECT Outcome FROM dbo.WorkbenchAgentRunAttempts
                     WHERE AgentRunId=@AgentRunId AND AttemptNumber=1) AS PriorOutcome,
                    (SELECT DiagnosticCode FROM dbo.WorkbenchAgentRunAttempts
                     WHERE AgentRunId=@AgentRunId AND AttemptNumber=1) AS PriorDiagnosticCode;
                """, new { submitted.AgentRunId });
            Assert.AreEqual(0, reclaimState.PriorUnfinishedAttempts);
            Assert.AreEqual(1, reclaimState.UnfinishedAttempts);
            Assert.AreEqual("ClaimExpired", reclaimState.PriorOutcome);
            Assert.AreEqual("claim_expired_before_reclaim", reclaimState.PriorDiagnosticCode);

            await Assert.ThrowsExactlyAsync<SqlException>(() => reclaimVerify.ExecuteAsync("""
                INSERT dbo.WorkbenchAgentRunAttempts
                    (AgentRunId, AttemptNumber, ClaimToken, WorkerId)
                VALUES
                    (@AgentRunId, 3, NEWID(), N'invalid-second-active-attempt');
                """, new { submitted.AgentRunId }));
        }

        var secondContext = await assembler.AssembleAsync(secondClaim);
        Assert.AreEqual(firstContext.ContextHash, secondContext.ContextHash);
        Assert.AreEqual(1L, secondContext.UnderstandingRevision);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.ContextSchemaVersion, secondContext.ContextSchemaVersion);
        Assert.AreEqual(
            WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion,
            secondContext.ContextCanonicalizationVersion);
        Assert.AreEqual(firstContext.Messages.Count, secondContext.Messages.Count);

        var output = ValidOutput(secondContext, "A single durable assistant answer.");
        var firstMaterialization = await service.MaterializeAsync(secondClaim, secondContext, output);
        var replayMaterialization = await service.MaterializeAsync(secondClaim, secondContext, output);
        Assert.IsTrue(firstMaterialization.Materialized);
        Assert.IsFalse(firstMaterialization.IsReplay);
        Assert.IsTrue(replayMaterialization.Materialized);
        Assert.IsTrue(replayMaterialization.IsReplay);
        Assert.AreEqual(firstMaterialization.AssistantMessageId, replayMaterialization.AssistantMessageId);

        var mismatchedReplay = await service.MaterializeAsync(
            secondClaim,
            secondContext,
            ValidOutput(secondContext, "A different response cannot masquerade as replay."));
        Assert.IsFalse(mismatchedReplay.Materialized);
        Assert.AreEqual("materialization_replay_output_mismatch", mismatchedReplay.RejectionReason);

        var staleAttempt = await service.MaterializeAsync(firstClaim, firstContext, output);
        Assert.IsFalse(staleAttempt.Materialized);
        Assert.AreEqual("late_result_after_reclaim", staleAttempt.RejectionReason);

        await using var verify = new SqlConnection(ConnectionString);
        var counts = await verify.QuerySingleAsync<MaterializationCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.ChatMessages WHERE Id=@AssistantMessageId AND Role=N'assistant') AS AssistantMessages,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents WHERE AgentRunId=@AgentRunId AND EventKind=N'AgentRunMaterialized') AS MaterializedEvents,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts WHERE AgentRunId=@AgentRunId) AS Attempts,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts WHERE AgentRunId=@AgentRunId AND ContextHash IS NOT NULL) AS ContextStampedAttempts,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts WHERE AgentRunId=@AgentRunId AND Outcome=N'ClaimExpired') AS ClaimExpiredAttempts,
                (SELECT COUNT(1) FROM dbo.ChatMessages WHERE Id=@AssistantMessageId AND ReplyToMessageId=@SourceUserMessageId) AS ReplyLinks,
                (SELECT COUNT(1) FROM dbo.ChatMessages
                 WHERE Id=@AssistantMessageId
                   AND JSON_VALUE(Tags, '$.v')=N'1'
                   AND JSON_VALUE(Tags, '$.mode')=N'Exploration'
                   AND JSON_VALUE(Tags, '$.clarification.kind')=N'None'
                   AND JSON_VALUE(Tags, '$.gate.mode')=N'Exploration') AS VersionedEnvelopes,
                (SELECT COUNT(1) FROM dbo.ChatTurnGovernance WHERE ChatMessageId=@AssistantMessageId AND TenantId=1) AS GovernanceRows,
                (SELECT COUNT(1) FROM dbo.ChatTurnClarifications WHERE ChatMessageId=@AssistantMessageId AND TenantId=1) AS ClarificationRows,
                (SELECT COUNT(1) FROM dbo.ChatTurnTraces WHERE ChatMessageId=@AssistantMessageId AND TenantId=1) AS TraceRows;
            """, new
        {
            fixture.ProjectId,
            submitted.AgentRunId,
            AssistantMessageId = firstMaterialization.AssistantMessageId,
            SourceUserMessageId = submitted.UserMessageId
        });
        Assert.AreEqual(1, counts.AssistantMessages);
        Assert.AreEqual(1, counts.MaterializedEvents);
        Assert.AreEqual(2, counts.Attempts);
        Assert.AreEqual(2, counts.ContextStampedAttempts);
        Assert.AreEqual(1, counts.ClaimExpiredAttempts);
        Assert.AreEqual(1, counts.ReplyLinks);
        Assert.AreEqual(1, counts.VersionedEnvelopes);
        Assert.AreEqual(1, counts.GovernanceRows);
        Assert.AreEqual(1, counts.ClarificationRows);
        Assert.AreEqual(1, counts.TraceRows);

        var otherChatSessionId = await verify.QuerySingleAsync<long>("""
            INSERT dbo.ProjectChatSessions(TenantId, ProjectId, Title)
            OUTPUT inserted.Id
            VALUES (1, @ProjectId, N'Conversation switch after completion');
            """, new { fixture.ProjectId });
        await Assert.ThrowsExactlyAsync<WorkbenchChatSessionBindingException>(() =>
            service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "A terminal run must not release the chat binding.") with
            {
                ChatSessionId = otherChatSessionId
            }));

        var followup = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Use the trusted prior assistant response."));
        var followupClaim = await service.ClaimAsync(followup.AgentRunId, "followup-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(followupClaim);
        var followupContext = await assembler.AssembleAsync(followupClaim);
        Assert.AreEqual(
            "assistant",
            followupContext.Messages.Single(message =>
                message.Message == "A single durable assistant answer.").Role);
    }

    [TestMethod]
    public async Task Outbox_PublishedClaimCrashRecoversExpiredRunExactlyOnce()
    {
        var fixture = await CreateFixtureAsync("Outbox recovery agent run");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Recover this run after the first worker stops."));
        var outbox = new WorkbenchAgentRunOutbox(ConnectionFactory());
        var requestItem = (await outbox.ReadPendingAsync(10))
            .Single(item => item.AgentRunId == submitted.AgentRunId);
        using var cancellation = new CancellationTokenSource();
        var firstProcessor = new WorkbenchAgentRunProcessor(
            service,
            new CancelAfterContextAssembler(
                new WorkbenchAgentContextAssembler(ConnectionFactory()),
                cancellation),
            new NeverInvokedAgent(),
            outbox);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            firstProcessor.ProcessAsync(
                requestItem,
                "crashing-worker",
                cancellation.Token));

        await using (var expire = new SqlConnection(ConnectionString))
        {
            var firstState = await expire.QuerySingleAsync<RecoveryState>("""
                SELECT run.Status, run.AttemptCount,
                       (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                        WHERE Id=@OutboxEventId AND PublishedAtUtc IS NOT NULL) AS PublishedRequests
                FROM dbo.WorkbenchAgentRuns run
                WHERE run.AgentRunId=@AgentRunId;
                """, new { requestItem.OutboxEventId, submitted.AgentRunId });
            Assert.AreEqual(WorkbenchAgentRunStates.Running, firstState.Status);
            Assert.AreEqual(1, firstState.AttemptCount);
            Assert.AreEqual(1, firstState.PublishedRequests);

            await expire.ExecuteAsync("""
                UPDATE dbo.WorkbenchAgentRuns
                SET ClaimExpiresAtUtc=DATEADD(SECOND, -1, SYSUTCDATETIME())
                WHERE AgentRunId=@AgentRunId;
                """, new { submitted.AgentRunId });
        }

        var recoveryItem = (await outbox.ReadPendingAsync(10))
            .Single(item => item.AgentRunId == submitted.AgentRunId);
        Assert.AreEqual(0L, recoveryItem.OutboxEventId);
        var recoveryAgent = new ValidSchemaAgent("Recovered exactly once.");
        var recoveryProcessor = new WorkbenchAgentRunProcessor(
            service,
            new WorkbenchAgentContextAssembler(ConnectionFactory()),
            recoveryAgent,
            outbox);

        await recoveryProcessor.ProcessAsync(recoveryItem, "recovery-worker");

        await using var verify = new SqlConnection(ConnectionString);
        var finalState = await verify.QuerySingleAsync<RecoveryState>("""
            SELECT run.Status, run.AttemptCount,
                   (SELECT COUNT(1) FROM dbo.ChatMessages
                    WHERE TenantId=run.TenantId AND ProjectId=run.ProjectId AND Role=N'assistant') AS AssistantMessages,
                   (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                    WHERE AgentRunId=run.AgentRunId AND EventKind=N'AgentRunMaterialized') AS MaterializedEvents,
                   (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=run.AgentRunId AND ContextHash IS NOT NULL) AS ContextStampedAttempts,
                   (SELECT COUNT(DISTINCT ContextHash) FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=run.AgentRunId AND ContextHash IS NOT NULL) AS DistinctContextHashes,
                   (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=run.AgentRunId AND CompletedAtUtc IS NULL) AS UnfinishedAttempts,
                   (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=run.AgentRunId AND Outcome=N'ClaimExpired') AS ClaimExpiredAttempts
            FROM dbo.WorkbenchAgentRuns run
            WHERE run.AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId });
        Assert.AreEqual(WorkbenchAgentRunStates.Completed, finalState.Status);
        Assert.AreEqual(2, finalState.AttemptCount);
        Assert.AreEqual(1, finalState.AssistantMessages);
        Assert.AreEqual(1, finalState.MaterializedEvents);
        Assert.AreEqual(2, finalState.ContextStampedAttempts);
        Assert.AreEqual(1, finalState.DistinctContextHashes);
        Assert.AreEqual(0, finalState.UnfinishedAttempts);
        Assert.AreEqual(1, finalState.ClaimExpiredAttempts);
        Assert.IsFalse(string.IsNullOrWhiteSpace(recoveryAgent.ContextHash));
    }

    [TestMethod]
    public async Task Outbox_OldestExpiredRecoveryCannotBeStarvedByFreshRequests()
    {
        var recoveryFixture = await CreateFixtureAsync("Oldest recovery candidate");
        var service = CreateRunService();
        var recoveryRun = await service.SubmitAsync(
            recoveryFixture.Submit(Guid.NewGuid(), "Recover me before newer requests."));
        var outbox = new WorkbenchAgentRunOutbox(ConnectionFactory());
        var request = (await outbox.ReadPendingAsync(10))
            .Single(item => item.AgentRunId == recoveryRun.AgentRunId);
        var claim = await service.ClaimAsync(recoveryRun.AgentRunId, "fair-recovery-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        await outbox.MarkPublishedAsync(request.OutboxEventId);

        await using (var expire = new SqlConnection(ConnectionString))
        {
            await expire.ExecuteAsync("""
                UPDATE dbo.WorkbenchAgentRuns
                SET ClaimExpiresAtUtc=DATEADD(MINUTE, -10, SYSUTCDATETIME())
                WHERE AgentRunId=@AgentRunId;
                """, new { recoveryRun.AgentRunId });
        }

        for (var index = 0; index < 12; index++)
        {
            var freshFixture = await CreateProjectFixtureAsync(
                recoveryFixture.ActorUserId,
                $"Fresh request {index}");
            _ = await service.SubmitAsync(
                freshFixture.Submit(Guid.NewGuid(), $"Newer request {index}."));
        }

        var firstBatch = await outbox.ReadPendingAsync(10);
        Assert.AreEqual(recoveryRun.AgentRunId, firstBatch[0].AgentRunId);
        Assert.AreEqual(0L, firstBatch[0].OutboxEventId);
    }

    [TestMethod]
    [DataRow("lease_revoked")]
    [DataRow("project_member_inactive")]
    [DataRow("tenant_member_removed")]
    [DataRow("user_inactive")]
    public async Task InvocationAuthorization_LostAfterPreparationNeverInvokesProvider(string authorityChange)
    {
        var fixture = await CreateFixtureAsync($"Invocation authority {authorityChange}");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Do not spend provider work after authority is lost."));
        var outbox = new WorkbenchAgentRunOutbox(ConnectionFactory());
        var agent = new PreparationMutationAgent(async () =>
        {
            await using var connection = new SqlConnection(ConnectionString);
            var sql = authorityChange switch
            {
                "lease_revoked" => """
                    UPDATE dbo.WorkbenchWriteLeases
                    SET RevokedAtUtc=SYSUTCDATETIME()
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                    """,
                "project_member_inactive" => """
                    UPDATE dbo.ProjectMembers
                    SET Status=N'Removed'
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=@ActorUserId;
                    """,
                "tenant_member_removed" => """
                    DELETE dbo.TenantUsers
                    WHERE TenantId=1 AND UserId=@ActorUserId;
                    """,
                "user_inactive" => """
                    UPDATE dbo.Users SET IsActive=0 WHERE Id=@ActorUserId;
                    """,
                _ => throw new AssertFailedException($"Unknown authority change '{authorityChange}'.")
            };
            await connection.ExecuteAsync(sql, fixture);
        });
        var processor = new WorkbenchAgentRunProcessor(
            service,
            new WorkbenchAgentContextAssembler(ConnectionFactory()),
            agent,
            outbox);
        var item = (await outbox.ReadPendingAsync(10))
            .Single(value => value.AgentRunId == submitted.AgentRunId);

        await processor.ProcessAsync(item, $"authority-{authorityChange}-worker");

        Assert.AreEqual(0, agent.InvocationCount);
        await using var verify = new SqlConnection(ConnectionString);
        var state = await verify.QuerySingleAsync<InvocationSafetyState>("""
            SELECT run.Status, run.DiagnosticCode,
                   (SELECT COUNT(1) FROM dbo.ChatMessages
                    WHERE TenantId=run.TenantId AND ProjectId=run.ProjectId AND Role=N'assistant') AS AssistantMessages,
                   (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=run.AgentRunId AND CompletedAtUtc IS NULL) AS UnfinishedAttempts,
                   (SELECT TOP (1) Outcome FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=run.AgentRunId ORDER BY AttemptNumber DESC) AS AttemptOutcome
            FROM dbo.WorkbenchAgentRuns run
            WHERE run.AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId });
        Assert.AreEqual(WorkbenchAgentRunStates.Stale, state.Status);
        Assert.AreEqual("invocation_authority_not_current", state.DiagnosticCode);
        Assert.AreEqual(0, state.AssistantMessages);
        Assert.AreEqual(0, state.UnfinishedAttempts);
        Assert.AreEqual(WorkbenchAgentRunStates.Stale, state.AttemptOutcome);
    }

    [TestMethod]
    public async Task ProviderTimeout_NonCooperativeProviderFailsBoundedlyWithoutAssistantMaterialization()
    {
        var fixture = await CreateFixtureAsync("Provider timeout");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Time out this provider call safely."));
        var outbox = new WorkbenchAgentRunOutbox(ConnectionFactory());
        var agent = new NonCooperativeTimeoutAgent(TimeSpan.FromMilliseconds(25));
        var processor = new WorkbenchAgentRunProcessor(
            service,
            new WorkbenchAgentContextAssembler(ConnectionFactory()),
            agent,
            outbox);
        var item = (await outbox.ReadPendingAsync(10))
            .Single(value => value.AgentRunId == submitted.AgentRunId);

        var processing = processor.ProcessAsync(item, "provider-timeout-worker");
        await agent.InvocationStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await processing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, agent.InvocationCount);
        Assert.IsTrue(agent.ProviderCancellationRequested);
        agent.FaultAfterTimeout();
        await using var verify = new SqlConnection(ConnectionString);
        var state = await verify.QuerySingleAsync<InvocationSafetyState>("""
            SELECT run.Status, run.DiagnosticCode,
                   (SELECT COUNT(1) FROM dbo.ChatMessages
                    WHERE TenantId=run.TenantId AND ProjectId=run.ProjectId AND Role=N'assistant') AS AssistantMessages,
                   (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=run.AgentRunId AND CompletedAtUtc IS NULL) AS UnfinishedAttempts,
                   (SELECT TOP (1) Outcome FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=run.AgentRunId ORDER BY AttemptNumber DESC) AS AttemptOutcome
            FROM dbo.WorkbenchAgentRuns run
            WHERE run.AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId });
        Assert.AreEqual(WorkbenchAgentRunStates.Failed, state.Status);
        Assert.AreEqual("agent_provider_timeout", state.DiagnosticCode);
        Assert.AreEqual(0, state.AssistantMessages);
        Assert.AreEqual(0, state.UnfinishedAttempts);
        Assert.AreEqual(WorkbenchAgentRunStates.Failed, state.AttemptOutcome);
    }

    [TestMethod]
    public async Task Takeover_SupersedesRunningRunAndLateResultIsDiagnosticOnly()
    {
        var fixture = await CreateFixtureAsync("Takeover agent run");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "Hold this response until takeover."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "old-epoch-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        var context = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(claim);
        var secondActor = await SeedAdditionalActorAsync("takeover");
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync("""
                INSERT dbo.ProjectMembers(TenantId, ProjectId, UserId, ProjectRole, Status, AddedByUserId)
                VALUES (1, @ProjectId, @SecondActor, N'Contributor', N'Active', @OwnerActor);
                """, new { fixture.ProjectId, SecondActor = secondActor, OwnerActor = fixture.ActorUserId });
        }

        var takeover = await new WorkbenchProjectEntryService(ConnectionFactory()).OpenAsync(
            new OpenWorkbenchProjectCommand(1, secondActor, fixture.ProjectId, Guid.NewGuid(), TakeOver: true));
        Assert.IsTrue(takeover.WasTakenOver);
        Assert.AreEqual(2L, takeover.LeaseEpoch);

        await AssertSingleAttemptClosedWithoutResponseAsync(
            submitted.AgentRunId,
            "Superseded",
            "workbench_lease_taken_over");

        await service.MarkFailedAsync(
            claim,
            "provider_failed_after_takeover",
            new string('f', 64));
        var late = await service.MaterializeAsync(claim, context, ValidOutput(context, "This must remain hidden."));
        Assert.IsFalse(late.Materialized);
        Assert.AreEqual(WorkbenchAgentRunStates.Superseded, late.Status);

        await using var verify = new SqlConnection(ConnectionString);
        var state = await verify.QuerySingleAsync<LateResultState>("""
            SELECT run.Status, run.CancellationRequestedAtUtc, run.SupersededByWorkbenchSessionId,
                   run.SupersededByLeaseEpoch, run.ValidatedOutputJson, run.OutputHash,
                   run.AssistantMessageId, run.DiagnosticHash,
                   attempt.Outcome AS AttemptOutcome, attempt.ResponseHash AS AttemptResponseHash,
                   (SELECT COUNT(1) FROM dbo.ChatMessages WHERE TenantId=1 AND ProjectId=run.ProjectId AND Role=N'assistant') AS AssistantMessages
            FROM dbo.WorkbenchAgentRuns run
            INNER JOIN dbo.WorkbenchAgentRunAttempts attempt
                ON attempt.AgentRunId=run.AgentRunId AND attempt.ClaimToken=@ClaimToken
            WHERE run.AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId, claim.ClaimToken });
        Assert.AreEqual(WorkbenchAgentRunStates.Superseded, state.Status);
        Assert.IsNotNull(state.CancellationRequestedAtUtc);
        Assert.AreEqual(takeover.WorkbenchSessionId, state.SupersededByWorkbenchSessionId);
        Assert.AreEqual(takeover.LeaseEpoch, state.SupersededByLeaseEpoch);
        Assert.IsNull(state.ValidatedOutputJson);
        Assert.IsNull(state.OutputHash);
        Assert.IsNull(state.AssistantMessageId);
        Assert.AreEqual(0, state.AssistantMessages);
        Assert.AreEqual("Superseded", state.AttemptOutcome);
        Assert.IsNotNull(state.DiagnosticHash);
        Assert.IsNotNull(state.AttemptResponseHash);

        var supersession = await verify.QuerySingleAsync<TakeoverCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE AgentRunId=@RunningAgentRunId AND Status=N'Superseded' AND ActiveRunSlot IS NULL) AS SupersededRuns,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                 WHERE AgentRunId=@RunningAgentRunId
                   AND EventKind=N'AgentRunSuperseded') AS SupersededEvents;
            """, new
        {
            RunningAgentRunId = submitted.AgentRunId
        });
        Assert.AreEqual(1, supersession.SupersededRuns);
        Assert.AreEqual(1, supersession.SupersededEvents);
    }

    [TestMethod]
    public async Task Takeover_SupersedesPendingRunAndReleasesActiveSlot()
    {
        var fixture = await CreateFixtureAsync("Takeover pending agent run");
        var service = CreateRunService();
        var pending = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Supersede this pending turn during takeover."));
        var secondActor = await SeedAdditionalActorAsync("pending-takeover");
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync("""
                INSERT dbo.ProjectMembers(TenantId, ProjectId, UserId, ProjectRole, Status, AddedByUserId)
                VALUES (1, @ProjectId, @SecondActor, N'Contributor', N'Active', @OwnerActor);
                """, new { fixture.ProjectId, SecondActor = secondActor, OwnerActor = fixture.ActorUserId });
        }

        var takeover = await new WorkbenchProjectEntryService(ConnectionFactory()).OpenAsync(
            new OpenWorkbenchProjectCommand(1, secondActor, fixture.ProjectId, Guid.NewGuid(), TakeOver: true));

        Assert.IsTrue(takeover.WasTakenOver);
        await using var verify = new SqlConnection(ConnectionString);
        var supersession = await verify.QuerySingleAsync<TakeoverCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE AgentRunId=@AgentRunId AND Status=N'Superseded' AND ActiveRunSlot IS NULL) AS SupersededRuns,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                 WHERE AgentRunId=@AgentRunId AND EventKind=N'AgentRunSuperseded') AS SupersededEvents;
            """, new { AgentRunId = pending.AgentRunId });
        Assert.AreEqual(1, supersession.SupersededRuns);
        Assert.AreEqual(1, supersession.SupersededEvents);
    }

    [TestMethod]
    public async Task ExpiredWorkbenchLease_ClosesRunningAttemptWithoutWaitingForProviderResult()
    {
        var fixture = await CreateFixtureAsync("Expired lease attempt");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "The provider will not return."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "expired-lease-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);

        await using (var expire = new SqlConnection(ConnectionString))
        {
            await expire.ExecuteAsync("""
                UPDATE dbo.WorkbenchWriteLeases
                SET ExpiresAtUtc=DATEADD(SECOND, -1, SYSUTCDATETIME())
                WHERE TenantId=1 AND ProjectId=@ProjectId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                """, fixture);
        }

        var reopened = await new WorkbenchProjectEntryService(ConnectionFactory()).OpenAsync(
            new OpenWorkbenchProjectCommand(1, fixture.ActorUserId, fixture.ProjectId, Guid.NewGuid(), TakeOver: false));
        Assert.AreEqual(fixture.LeaseEpoch + 1, reopened.LeaseEpoch);

        await AssertSingleAttemptClosedWithoutResponseAsync(
            submitted.AgentRunId,
            "Stale",
            "lease_expired");

        var snapshot = await service.GetAsync(1, fixture.ActorUserId, fixture.ProjectId, submitted.AgentRunId);
        Assert.AreEqual(WorkbenchAgentRunStates.Stale, snapshot.Status);
    }

    [TestMethod]
    public async Task InvalidSchema_FailsWithoutAssistantMaterialization()
    {
        var fixture = await CreateFixtureAsync("Invalid schema agent run");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "Return a controlled invalid result."));
        var outbox = new WorkbenchAgentRunOutbox(ConnectionFactory());
        var invalidAgent = new InvalidSchemaAgent();
        var processor = new WorkbenchAgentRunProcessor(
            service,
            new WorkbenchAgentContextAssembler(ConnectionFactory()),
            invalidAgent,
            outbox);
        var item = (await outbox.ReadPendingAsync(10)).Single(value => value.AgentRunId == submitted.AgentRunId);

        await processor.ProcessAsync(item, "schema-test-worker");

        await using var connection = new SqlConnection(ConnectionString);
        var state = await connection.QuerySingleAsync<InvalidOutputState>("""
            SELECT Status, DiagnosticCode, DiagnosticHash, AssistantMessageId, ValidatedOutputJson,
                   (SELECT COUNT(1) FROM dbo.ChatMessages WHERE TenantId=1 AND ProjectId=@ProjectId AND Role=N'assistant') AS AssistantMessages,
                   (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=@AgentRunId AND CompletedAtUtc IS NULL) AS UnfinishedAttempts,
                   (SELECT TOP (1) Outcome FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=@AgentRunId ORDER BY AttemptNumber DESC) AS AttemptOutcome
            FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId;
            """, new { fixture.ProjectId, submitted.AgentRunId });
        Assert.AreEqual(WorkbenchAgentRunStates.Failed, state.Status);
        Assert.AreEqual("agent_output_schema_invalid", state.DiagnosticCode);
        var expectedRawHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(invalidAgent.LastRawOutput!))).ToLowerInvariant();
        Assert.AreEqual(expectedRawHash, state.DiagnosticHash);
        Assert.IsNull(state.AssistantMessageId);
        Assert.IsNull(state.ValidatedOutputJson);
        Assert.AreEqual(0, state.AssistantMessages);
        Assert.AreEqual(0, state.UnfinishedAttempts);
        Assert.AreEqual("Failed", state.AttemptOutcome);
    }

    [TestMethod]
    public async Task Materialize_RejectsCallerContextMutationThatRetainsOriginalHash()
    {
        var fixture = await CreateFixtureAsync("Caller context authority");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Keep the persisted context authoritative."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "mutated-context-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        var storedContext = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(claim);
        var mutatedContext = storedContext with
        {
            ProjectName = "Caller-controlled replacement",
            ContextHash = storedContext.ContextHash
        };

        var result = await service.MaterializeAsync(
            claim,
            mutatedContext,
            ValidOutput(mutatedContext, "This response must not become visible."));

        Assert.IsFalse(result.Materialized);
        Assert.AreEqual(WorkbenchAgentRunStates.Failed, result.Status);
        Assert.AreEqual("context_snapshot_mismatch", result.RejectionReason);
        await AssertRejectedContextDidNotMaterializeAsync(fixture.ProjectId, submitted.AgentRunId);
    }

    [TestMethod]
    public async Task Materialize_RejectsStoredSnapshotContentTampering()
    {
        var fixture = await CreateFixtureAsync("Stored context authority");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Detect persisted snapshot tampering."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "tampered-snapshot-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        var context = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(claim);

        await using (var tamper = new SqlConnection(ConnectionString))
        {
            await tamper.ExecuteAsync("""
                UPDATE dbo.WorkbenchAgentRuns
                SET ContextSnapshotJson=JSON_MODIFY(ContextSnapshotJson, '$.projectName', N'Tampered persisted name')
                WHERE AgentRunId=@AgentRunId;
                """, new { submitted.AgentRunId });
        }

        var result = await service.MaterializeAsync(
            claim,
            context,
            ValidOutput(context, "This response must not become visible."));

        Assert.IsFalse(result.Materialized);
        Assert.AreEqual(WorkbenchAgentRunStates.Failed, result.Status);
        Assert.AreEqual("context_snapshot_mismatch", result.RejectionReason);
        await AssertRejectedContextDidNotMaterializeAsync(fixture.ProjectId, submitted.AgentRunId);
    }

    [TestMethod]
    public async Task Materialize_StrictlyRejectsUnknownStoredSnapshotMembers()
    {
        var fixture = await CreateFixtureAsync("Strict stored context schema");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Reject unknown stored context fields."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "unknown-context-field-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        var context = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(claim);

        await using (var tamper = new SqlConnection(ConnectionString))
        {
            await tamper.ExecuteAsync("""
                UPDATE dbo.WorkbenchAgentRuns
                SET ContextSnapshotJson=JSON_MODIFY(ContextSnapshotJson, '$.unrecognizedAuthority', N'forged')
                WHERE AgentRunId=@AgentRunId;
                """, new { submitted.AgentRunId });
        }

        var result = await service.MaterializeAsync(
            claim,
            context,
            ValidOutput(context, "This response must not become visible."));

        Assert.IsFalse(result.Materialized);
        Assert.AreEqual("context_snapshot_mismatch", result.RejectionReason);
        await AssertRejectedContextDidNotMaterializeAsync(fixture.ProjectId, submitted.AgentRunId);
    }

    [TestMethod]
    public async Task Materialize_AcceptsPinnedVersion1SnapshotWithoutRewritingIt()
    {
        var fixture = await CreateFixtureAsync("Version 1 context upgrade");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Continue a genuinely pre-hardening context snapshot."));
        var currentClaim = await service.ClaimAsync(submitted.AgentRunId, "version-one-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(currentClaim);
        var currentContext = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(currentClaim);
        var (version1SnapshotJson, version1Hash) = CreateVersion1Snapshot(currentContext);
        var version1Claim = currentClaim with
        {
            ContextSchemaVersion = WorkbenchBusinessAnalystContract.ContextSchemaVersion1,
            ContextCanonicalizationVersion = WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion1
        };

        await using (var upgrade = new SqlConnection(ConnectionString))
        {
            await upgrade.ExecuteAsync("""
                UPDATE dbo.WorkbenchAgentRuns
                SET ContextSchemaVersion=@ContextSchemaVersion,
                    ContextCanonicalizationVersion=@ContextCanonicalizationVersion,
                    ContextSnapshotJson=@ContextSnapshotJson,
                    ContextHash=@ContextHash
                WHERE AgentRunId=@AgentRunId;

                UPDATE dbo.WorkbenchAgentRunAttempts
                SET ContextHash=@ContextHash
                WHERE AgentRunId=@AgentRunId AND ClaimToken=@ClaimToken;
                """, new
            {
                submitted.AgentRunId,
                currentClaim.ClaimToken,
                ContextSchemaVersion = WorkbenchBusinessAnalystContract.ContextSchemaVersion1,
                ContextCanonicalizationVersion = WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion1,
                ContextSnapshotJson = version1SnapshotJson,
                ContextHash = version1Hash
            });
        }

        var version1Context = await new WorkbenchAgentContextAssembler(ConnectionFactory())
            .AssembleAsync(version1Claim);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.ContextSchemaVersion1, version1Context.ContextSchemaVersion);
        Assert.AreEqual(
            WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion1,
            version1Context.ContextCanonicalizationVersion);
        Assert.AreEqual(version1Hash, version1Context.ContextHash);

        var result = await service.MaterializeAsync(
            version1Claim,
            version1Context,
            ValidOutput(version1Context, "The pinned legacy snapshot remains usable."));
        Assert.IsTrue(result.Materialized);

        await using var verify = new SqlConnection(ConnectionString);
        var persistedSnapshot = await verify.QuerySingleAsync<string>("""
            SELECT ContextSnapshotJson FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId });
        Assert.AreEqual(version1SnapshotJson, persistedSnapshot);
        Assert.IsFalse(persistedSnapshot.Contains("contextSchemaVersion", StringComparison.Ordinal));
        Assert.IsFalse(persistedSnapshot.Contains("contextCanonicalizationVersion", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ContextCodec_FailsClosedForPartialAndUnknownVersionPairs()
    {
        var fixture = await CreateFixtureAsync("Unsupported context formats");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Reject ambiguous context format dispatch."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "unsupported-context-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        _ = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(claim);

        await using var connection = new SqlConnection(ConnectionString);
        await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync("""
            UPDATE dbo.WorkbenchAgentRuns
            SET ContextSchemaVersion=1, ContextCanonicalizationVersion=2
            WHERE AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId }));

        await connection.ExecuteAsync("""
            UPDATE dbo.WorkbenchAgentRuns
            SET ContextSchemaVersion=3, ContextCanonicalizationVersion=3
            WHERE AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId });
        var unknownClaim = claim with { ContextSchemaVersion = 3, ContextCanonicalizationVersion = 3 };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(unknownClaim));
    }

    [TestMethod]
    public async Task Materialize_InvalidSourceReferenceFailsRunAndRejectsAttempt()
    {
        var fixture = await CreateFixtureAsync("Invalid source reference");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(
            fixture.Submit(Guid.NewGuid(), "Reject a source message whose trusted role changed."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "source-reference-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        var context = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(claim);

        await using (var tamper = new SqlConnection(ConnectionString))
        {
            await tamper.ExecuteAsync("""
                UPDATE dbo.ChatMessages SET Role=N'assistant' WHERE Id=@SourceUserMessageId;
                """, new { claim.SourceUserMessageId });
        }

        var result = await service.MaterializeAsync(
            claim,
            context,
            ValidOutput(context, "This response must not become visible."));

        Assert.IsFalse(result.Materialized);
        Assert.AreEqual(WorkbenchAgentRunStates.Failed, result.Status);
        Assert.AreEqual("source_reference_invalid", result.RejectionReason);

        await using var verify = new SqlConnection(ConnectionString);
        var state = await verify.QuerySingleAsync<InvalidOutputState>("""
            SELECT Status, DiagnosticCode, AssistantMessageId, ValidatedOutputJson,
                   (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=@AgentRunId AND CompletedAtUtc IS NULL) AS UnfinishedAttempts,
                   (SELECT TOP (1) Outcome FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=@AgentRunId ORDER BY AttemptNumber DESC) AS AttemptOutcome
            FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId });
        Assert.AreEqual(WorkbenchAgentRunStates.Failed, state.Status);
        Assert.AreEqual("source_reference_invalid", state.DiagnosticCode);
        Assert.IsNull(state.AssistantMessageId);
        Assert.IsNull(state.ValidatedOutputJson);
        Assert.AreEqual(0, state.UnfinishedAttempts);
        Assert.AreEqual("Rejected", state.AttemptOutcome);
    }

    [TestMethod]
    public async Task Cancel_IsIdempotentAndLateResultCannotMaterialize()
    {
        var fixture = await CreateFixtureAsync("Cancelled agent run");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "Cancel this in-flight request."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "cancel-test-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        var context = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(claim);
        var cancellationOperationId = Guid.NewGuid();
        var cancelCommand = new CancelWorkbenchAgentRunCommand(
            1,
            fixture.ActorUserId,
            fixture.ProjectId,
            fixture.WorkbenchSessionId,
            fixture.LeaseEpoch,
            submitted.AgentRunId,
            cancellationOperationId);

        var cancelled = await service.CancelAsync(cancelCommand);
        var replay = await service.CancelAsync(cancelCommand);
        Assert.AreEqual(WorkbenchAgentRunStates.Cancelled, cancelled.Status);
        Assert.IsTrue(cancelled.CancellationRequested);
        Assert.IsTrue(replay.IsReplay);

        await AssertSingleAttemptClosedWithoutResponseAsync(
            submitted.AgentRunId,
            "Cancelled",
            "run_cancelled_before_result");

        var late = await service.MaterializeAsync(claim, context, ValidOutput(context, "Cancelled output must stay hidden."));
        Assert.IsFalse(late.Materialized);
        Assert.AreEqual(WorkbenchAgentRunStates.Cancelled, late.Status);

        await using var connection = new SqlConnection(ConnectionString);
        var state = await connection.QuerySingleAsync<LateResultState>("""
            SELECT run.Status, run.CancellationRequestedAtUtc, run.SupersededByWorkbenchSessionId,
                   run.SupersededByLeaseEpoch, run.ValidatedOutputJson, run.OutputHash,
                   run.AssistantMessageId, run.DiagnosticHash,
                   attempt.Outcome AS AttemptOutcome, attempt.ResponseHash AS AttemptResponseHash,
                   (SELECT COUNT(1) FROM dbo.ChatMessages WHERE TenantId=1 AND ProjectId=run.ProjectId AND Role=N'assistant') AS AssistantMessages,
                   (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                    WHERE AgentRunId=run.AgentRunId AND EventKind=N'AgentRunCancelled') AS CancelledEvents
            FROM dbo.WorkbenchAgentRuns run
            INNER JOIN dbo.WorkbenchAgentRunAttempts attempt
                ON attempt.AgentRunId=run.AgentRunId AND attempt.ClaimToken=@ClaimToken
            WHERE run.AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId, claim.ClaimToken });
        Assert.AreEqual(WorkbenchAgentRunStates.Cancelled, state.Status);
        Assert.IsNotNull(state.CancellationRequestedAtUtc);
        Assert.IsNull(state.ValidatedOutputJson);
        Assert.IsNull(state.OutputHash);
        Assert.IsNull(state.AssistantMessageId);
        Assert.AreEqual(0, state.AssistantMessages);
        Assert.AreEqual("Cancelled", state.AttemptOutcome);
        Assert.AreEqual(1, state.CancelledEvents);
    }

    private async Task AssertSingleAttemptClosedWithoutResponseAsync(
        Guid agentRunId,
        string expectedOutcome,
        string expectedDiagnosticCode)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var state = await connection.QuerySingleAsync<AttemptClosureState>("""
            SELECT COUNT(1) AS Attempts,
                   SUM(CASE WHEN CompletedAtUtc IS NULL THEN 1 ELSE 0 END) AS UnfinishedAttempts,
                   MAX(Outcome) AS Outcome,
                   MAX(DiagnosticCode) AS DiagnosticCode,
                   MAX(ResponseHash) AS ResponseHash
            FROM dbo.WorkbenchAgentRunAttempts
            WHERE AgentRunId=@AgentRunId;
            """, new { AgentRunId = agentRunId });
        Assert.AreEqual(1, state.Attempts);
        Assert.AreEqual(0, state.UnfinishedAttempts);
        Assert.AreEqual(expectedOutcome, state.Outcome);
        Assert.AreEqual(expectedDiagnosticCode, state.DiagnosticCode);
        Assert.IsNull(state.ResponseHash);
    }

    private WorkbenchAgentRunService CreateRunService(IWorkbenchAgentRunFailureInjector? injector = null) =>
        new(
            ConnectionFactory(),
            ServiceProvider.GetRequiredService<IChatTurnPersistenceService>(),
            injector);

    private IDbConnectionFactory ConnectionFactory() =>
        ServiceProvider.GetRequiredService<IDbConnectionFactory>();

    private async Task<Fixture> CreateFixtureAsync(string name)
    {
        var actorUserId = await SeedActorAsync();
        return await CreateProjectFixtureAsync(actorUserId, name);
    }

    private async Task<Fixture> CreateProjectFixtureAsync(int actorUserId, string name)
    {
        var start = await new ProjectStartService(ConnectionFactory(), new NoOpProjectStartFailureInjector()).StartAsync(
            new StartProjectCommand(1, actorUserId, Guid.NewGuid(), name));
        await using var connection = new SqlConnection(ConnectionString);
        var chatSessionId = await connection.QuerySingleAsync<long>("""
            INSERT dbo.ProjectChatSessions(TenantId, ProjectId, Title)
            OUTPUT inserted.Id
            VALUES (1, @ProjectId, N'Workbench shaping');
            """, new { start.ProjectId });
        return new Fixture(actorUserId, start.ProjectId, start.WorkbenchSessionId, start.LeaseEpoch, chatSessionId);
    }

    private async Task<int> SeedActorAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            SET IDENTITY_INSERT dbo.Tenants ON;
            INSERT dbo.Tenants(Id, Name, Slug) VALUES (1, N'Agent Run Test', N'agent-run-test');
            SET IDENTITY_INSERT dbo.Tenants OFF;
            """);
        var actorUserId = await connection.ExecuteScalarAsync<int>("""
            INSERT dbo.Users(Email, DisplayName, IsActive)
            OUTPUT inserted.Id
            VALUES (N'agent-run-test@irondev.local', N'Agent Run Tester', 1);
            """);
        await connection.ExecuteAsync(
            "INSERT dbo.TenantUsers(TenantId, UserId, Role) VALUES (1, @ActorUserId, N'Owner');",
            new { ActorUserId = actorUserId });
        return actorUserId;
    }

    private async Task AssertRejectedContextDidNotMaterializeAsync(int projectId, Guid agentRunId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var state = await connection.QuerySingleAsync<InvalidOutputState>("""
            SELECT Status, DiagnosticCode, DiagnosticHash, AssistantMessageId, ValidatedOutputJson,
                   (SELECT COUNT(1) FROM dbo.ChatMessages
                    WHERE TenantId=1 AND ProjectId=@ProjectId AND Role=N'assistant') AS AssistantMessages,
                   (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=@AgentRunId AND CompletedAtUtc IS NULL) AS UnfinishedAttempts,
                   (SELECT TOP (1) Outcome FROM dbo.WorkbenchAgentRunAttempts
                    WHERE AgentRunId=@AgentRunId ORDER BY AttemptNumber DESC) AS AttemptOutcome
            FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId;
            """, new { ProjectId = projectId, AgentRunId = agentRunId });
        Assert.AreEqual(WorkbenchAgentRunStates.Failed, state.Status);
        Assert.AreEqual("context_snapshot_mismatch", state.DiagnosticCode);
        Assert.IsNull(state.AssistantMessageId);
        Assert.IsNull(state.ValidatedOutputJson);
        Assert.AreEqual(0, state.AssistantMessages);
        Assert.AreEqual(0, state.UnfinishedAttempts);
        Assert.AreEqual("Rejected", state.AttemptOutcome);
    }

    private async Task<int> SeedAdditionalActorAsync(string suffix)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var actorUserId = await connection.ExecuteScalarAsync<int>("""
            INSERT dbo.Users(Email, DisplayName, IsActive)
            OUTPUT inserted.Id
            VALUES (@Email, @DisplayName, 1);
            """, new { Email = $"agent-run-{suffix}@irondev.local", DisplayName = $"Agent Run {suffix}" });
        await connection.ExecuteAsync(
            "INSERT dbo.TenantUsers(TenantId, UserId, Role) VALUES (1, @ActorUserId, N'Member');",
            new { ActorUserId = actorUserId });
        return actorUserId;
    }

    private async Task ApplyMigrationAsync(string fileName)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", fileName));
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(sql.Replace("\r\n", "\n", StringComparison.Ordinal), @"(?im)^\s*GO\s*$"))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                await connection.ExecuteAsync(batch);
        }
    }

    private async Task DropAgentRunMigrationObjectsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync("""
            IF EXISTS
            (
                SELECT 1 FROM sys.foreign_keys
                WHERE parent_object_id=OBJECT_ID(N'dbo.WorkbenchOutboxEvents')
                  AND name=N'FK_WorkbenchOutboxEvents_AgentRun'
            )
                ALTER TABLE dbo.WorkbenchOutboxEvents DROP CONSTRAINT FK_WorkbenchOutboxEvents_AgentRun;
            DROP TABLE IF EXISTS dbo.WorkbenchBusinessAnalystToolCallAudits;
            DROP TABLE IF EXISTS dbo.WorkbenchBusinessAnalystPreparations;
            DROP TABLE IF EXISTS dbo.WorkbenchAgentRunAttempts;
            DROP TABLE IF EXISTS dbo.WorkbenchAgentRuns;
            """);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static WorkbenchBusinessAnalystOutput ValidOutput(
        WorkbenchBusinessAnalystContext context,
        string message) => new(
        WorkbenchBusinessAnalystContract.OutputSchemaVersion,
        context.ContextHash,
        context.UnderstandingRevision,
        WorkbenchAgentRunStates.Completed,
        message);

    private static (string SnapshotJson, string ContextHash) CreateVersion1Snapshot(
        WorkbenchBusinessAnalystContext context)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var withoutHash = new Version1ContextFixture(
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
            ContextHash: string.Empty);
        var canonicalJson = JsonSerializer.Serialize(withoutHash, options);
        var contextHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
        return (JsonSerializer.Serialize(withoutHash with { ContextHash = contextHash }, options), contextHash);
    }

    private sealed record Fixture(
        int ActorUserId,
        int ProjectId,
        long WorkbenchSessionId,
        long LeaseEpoch,
        long ChatSessionId)
    {
        public SubmitWorkbenchAgentRunCommand Submit(Guid operationId, string message) => new(
            1,
            ActorUserId,
            ProjectId,
            WorkbenchSessionId,
            LeaseEpoch,
            operationId,
            ChatSessionId,
            message);
    }

    private sealed record Version1ContextFixture(
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

    private sealed class ThrowAt(WorkbenchAgentRunFailurePoint point) : IWorkbenchAgentRunFailureInjector
    {
        public void ThrowIfRequested(WorkbenchAgentRunFailurePoint candidate)
        {
            if (candidate == point)
                throw new InjectedAgentRunFailure();
        }
    }

    private sealed class InjectedAgentRunFailure : Exception;

    private sealed class InvalidSchemaAgent : IWorkbenchBusinessAnalystAgent
    {
        public string? LastRawOutput { get; private set; }

        public Task<IWorkbenchBusinessAnalystPreparedInvocation> PrepareAsync(
            WorkbenchAgentRunClaim claim,
            WorkbenchBusinessAnalystContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IWorkbenchBusinessAnalystPreparedInvocation>(new PreparedInvocation(_ =>
            {
                LastRawOutput = $$"""
                    {
                      "outputSchemaVersion": 999,
                      "contextHash": "{{context.ContextHash}}",
                      "basedOnUnderstandingRevision": {{context.UnderstandingRevision}},
                      "outcome": "Completed",
                      "assistantMessage": "This invalid response must not materialize."
                    }
                    """;
                return Task.FromResult(LastRawOutput!);
            }));
        }
    }

    private sealed class CancelAfterContextAssembler(
        IWorkbenchAgentContextAssembler inner,
        CancellationTokenSource cancellation) : IWorkbenchAgentContextAssembler
    {
        public async Task<WorkbenchBusinessAnalystContext> AssembleAsync(
            WorkbenchAgentRunClaim claim,
            CancellationToken cancellationToken = default)
        {
            _ = await inner.AssembleAsync(claim, cancellationToken);
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }
    }

    private sealed class NeverInvokedAgent : IWorkbenchBusinessAnalystAgent
    {
        public Task<IWorkbenchBusinessAnalystPreparedInvocation> PrepareAsync(
            WorkbenchAgentRunClaim claim,
            WorkbenchBusinessAnalystContext context,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The agent must not be invoked after controlled worker cancellation.");
    }

    private sealed class ValidSchemaAgent(string assistantMessage) : IWorkbenchBusinessAnalystAgent
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public string? ContextHash { get; private set; }

        public Task<IWorkbenchBusinessAnalystPreparedInvocation> PrepareAsync(
            WorkbenchAgentRunClaim claim,
            WorkbenchBusinessAnalystContext context,
            CancellationToken cancellationToken = default)
        {
            ContextHash = context.ContextHash;
            return Task.FromResult<IWorkbenchBusinessAnalystPreparedInvocation>(new PreparedInvocation(_ =>
                Task.FromResult(JsonSerializer.Serialize(
                    ValidOutput(context, assistantMessage),
                    JsonOptions))));
        }
    }

    private sealed class PreparedInvocation(
        Func<CancellationToken, Task<string>> invoke,
        TimeSpan? providerTimeout = null) : IWorkbenchBusinessAnalystPreparedInvocation
    {
        public TimeSpan ProviderTimeout { get; } = providerTimeout ?? TimeSpan.FromMinutes(1);

        public Task<string> InvokeProviderAsync(CancellationToken cancellationToken = default) =>
            invoke(cancellationToken);
    }

    private sealed class PreparationMutationAgent(Func<Task> mutateAuthority) : IWorkbenchBusinessAnalystAgent
    {
        private int _invocationCount;

        public int InvocationCount => _invocationCount;

        public async Task<IWorkbenchBusinessAnalystPreparedInvocation> PrepareAsync(
            WorkbenchAgentRunClaim claim,
            WorkbenchBusinessAnalystContext context,
            CancellationToken cancellationToken = default)
        {
            await mutateAuthority();
            return new PreparedInvocation(_ =>
            {
                Interlocked.Increment(ref _invocationCount);
                return Task.FromResult("provider must not be invoked");
            });
        }
    }

    private sealed class NonCooperativeTimeoutAgent(TimeSpan providerTimeout) : IWorkbenchBusinessAnalystAgent
    {
        private readonly TaskCompletionSource _invocationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _providerCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _providerCancellationRequested;
        private int _invocationCount;

        public int InvocationCount => _invocationCount;
        public Task InvocationStarted => _invocationStarted.Task;
        public bool ProviderCancellationRequested => Volatile.Read(ref _providerCancellationRequested) != 0;

        public Task<IWorkbenchBusinessAnalystPreparedInvocation> PrepareAsync(
            WorkbenchAgentRunClaim claim,
            WorkbenchBusinessAnalystContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IWorkbenchBusinessAnalystPreparedInvocation>(new PreparedInvocation(token =>
            {
                Interlocked.Increment(ref _invocationCount);
                token.Register(() => Volatile.Write(ref _providerCancellationRequested, 1));
                _invocationStarted.TrySetResult();
                return _providerCompletion.Task;
            }, providerTimeout));

        public void FaultAfterTimeout() =>
            _providerCompletion.TrySetException(new InvalidOperationException("Late provider failure."));
    }

    private sealed class InvocationSafetyState
    {
        public string Status { get; init; } = string.Empty;
        public string? DiagnosticCode { get; init; }
        public int AssistantMessages { get; init; }
        public int UnfinishedAttempts { get; init; }
        public string? AttemptOutcome { get; init; }
    }

    private sealed class ActiveRunConstraintState
    {
        public int ActiveRuns { get; init; }
        public string FirstStatus { get; init; } = string.Empty;
        public string SecondStatus { get; init; } = string.Empty;
    }

    private sealed class AttemptUpgradeState
    {
        public string Status { get; init; } = string.Empty;
        public string? Outcome { get; init; }
        public string? DiagnosticCode { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
    }

    private sealed class RunCounts
    {
        public int UserMessages { get; init; }
        public int AgentRuns { get; init; }
        public int Operations { get; init; }
        public int RequestedEvents { get; init; }
        public int Attributions { get; init; }
        public long? ActiveChatSessionId { get; init; }
        public int ActiveRuns { get; init; }
    }

    private sealed class RunProvenance
    {
        public string AgentVersion { get; init; } = string.Empty;
        public string PromptVersion { get; init; } = string.Empty;
        public string ToolPolicyVersion { get; init; } = string.Empty;
        public int ContextSchemaVersion { get; init; }
        public int ContextCanonicalizationVersion { get; init; }
        public int OutputSchemaVersion { get; init; }
    }

    private sealed class MaterializationCounts
    {
        public int AssistantMessages { get; init; }
        public int MaterializedEvents { get; init; }
        public int Attempts { get; init; }
        public int ContextStampedAttempts { get; init; }
        public int ClaimExpiredAttempts { get; init; }
        public int ReplyLinks { get; init; }
        public int VersionedEnvelopes { get; init; }
        public int GovernanceRows { get; init; }
        public int ClarificationRows { get; init; }
        public int TraceRows { get; init; }
    }

    private sealed class RecoveryState
    {
        public string Status { get; init; } = string.Empty;
        public int AttemptCount { get; init; }
        public int PublishedRequests { get; init; }
        public int AssistantMessages { get; init; }
        public int MaterializedEvents { get; init; }
        public int ContextStampedAttempts { get; init; }
        public int DistinctContextHashes { get; init; }
        public int UnfinishedAttempts { get; init; }
        public int ClaimExpiredAttempts { get; init; }
    }

    private sealed class ReclaimAttemptState
    {
        public int PriorUnfinishedAttempts { get; init; }
        public int UnfinishedAttempts { get; init; }
        public string? PriorOutcome { get; init; }
        public string? PriorDiagnosticCode { get; init; }
    }

    private sealed class AttemptClosureState
    {
        public int Attempts { get; init; }
        public int UnfinishedAttempts { get; init; }
        public string? Outcome { get; init; }
        public string? DiagnosticCode { get; init; }
        public string? ResponseHash { get; init; }
    }

    private sealed class LateResultState
    {
        public string Status { get; init; } = string.Empty;
        public DateTime? CancellationRequestedAtUtc { get; init; }
        public long? SupersededByWorkbenchSessionId { get; init; }
        public long? SupersededByLeaseEpoch { get; init; }
        public string? ValidatedOutputJson { get; init; }
        public string? OutputHash { get; init; }
        public long? AssistantMessageId { get; init; }
        public string? DiagnosticHash { get; init; }
        public string? AttemptOutcome { get; init; }
        public string? AttemptResponseHash { get; init; }
        public int AssistantMessages { get; init; }
        public int CancelledEvents { get; init; }
    }

    private sealed class TakeoverCounts
    {
        public int SupersededRuns { get; init; }
        public int SupersededEvents { get; init; }
    }

    private sealed class InvalidOutputState
    {
        public string Status { get; init; } = string.Empty;
        public string? DiagnosticCode { get; init; }
        public string? DiagnosticHash { get; init; }
        public long? AssistantMessageId { get; init; }
        public string? ValidatedOutputJson { get; init; }
        public int AssistantMessages { get; init; }
        public int UnfinishedAttempts { get; init; }
        public string? AttemptOutcome { get; init; }
    }
}
