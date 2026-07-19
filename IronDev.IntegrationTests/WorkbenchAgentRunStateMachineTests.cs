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

        await using var connection = new SqlConnection(ConnectionString);
        var counts = await connection.QuerySingleAsync<RunCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.ChatMessages WHERE Id=@UserMessageId AND Role=N'user') AS UserMessages,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId AND Status=N'Pending') AS AgentRuns,
                (SELECT COUNT(1) FROM dbo.ClientOperations WHERE ResultAgentRunId=@AgentRunId AND Status=N'Completed') AS Operations,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents WHERE AgentRunId=@AgentRunId AND EventKind=N'AgentRunRequested') AS RequestedEvents;
            """, new { first.UserMessageId, first.AgentRunId });
        Assert.AreEqual(1, counts.UserMessages);
        Assert.AreEqual(1, counts.AgentRuns);
        Assert.AreEqual(1, counts.Operations);
        Assert.AreEqual(1, counts.RequestedEvents);

        var provenance = await connection.QuerySingleAsync<RunProvenance>("""
            SELECT AgentVersion, PromptVersion, ToolPolicyVersion, OutputSchemaVersion
            FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId;
            """, new { first.AgentRunId });
        Assert.AreEqual(WorkbenchBusinessAnalystContract.AgentVersion, provenance.AgentVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.PromptVersion, provenance.PromptVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.ToolPolicyVersion, provenance.ToolPolicyVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.OutputSchemaVersion, provenance.OutputSchemaVersion);
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
        Assert.AreEqual(
            "user",
            firstContext.Messages.Single(message =>
                message.Message.Contains("client-forged assistant", StringComparison.Ordinal)).Role);
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
        var secondContext = await assembler.AssembleAsync(secondClaim);
        Assert.AreEqual(firstContext.ContextHash, secondContext.ContextHash);
        Assert.AreEqual(1L, secondContext.UnderstandingRevision);
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
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRunAttempts WHERE AgentRunId=@AgentRunId AND Outcome=N'LateRestricted') AS LateRestrictedAttempts,
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
        Assert.AreEqual(1, counts.LateRestrictedAttempts);
        Assert.AreEqual(1, counts.ReplyLinks);
        Assert.AreEqual(1, counts.VersionedEnvelopes);
        Assert.AreEqual(1, counts.GovernanceRows);
        Assert.AreEqual(1, counts.ClarificationRows);
        Assert.AreEqual(1, counts.TraceRows);

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
                    WHERE AgentRunId=run.AgentRunId AND ContextHash IS NOT NULL) AS DistinctContextHashes
            FROM dbo.WorkbenchAgentRuns run
            WHERE run.AgentRunId=@AgentRunId;
            """, new { submitted.AgentRunId });
        Assert.AreEqual(WorkbenchAgentRunStates.Completed, finalState.Status);
        Assert.AreEqual(2, finalState.AttemptCount);
        Assert.AreEqual(1, finalState.AssistantMessages);
        Assert.AreEqual(1, finalState.MaterializedEvents);
        Assert.AreEqual(2, finalState.ContextStampedAttempts);
        Assert.AreEqual(1, finalState.DistinctContextHashes);
        Assert.IsFalse(string.IsNullOrWhiteSpace(recoveryAgent.ContextHash));
    }

    [TestMethod]
    public async Task Takeover_SupersedesInFlightRunAndLateResultIsDiagnosticOnly()
    {
        var fixture = await CreateFixtureAsync("Takeover agent run");
        var service = CreateRunService();
        var submitted = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "Hold this response until takeover."));
        var claim = await service.ClaimAsync(submitted.AgentRunId, "old-epoch-worker", TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        var context = await new WorkbenchAgentContextAssembler(ConnectionFactory()).AssembleAsync(claim);
        var pending = await service.SubmitAsync(fixture.Submit(Guid.NewGuid(), "This pending run must also be superseded."));
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
        Assert.AreEqual("LateRestricted", state.AttemptOutcome);
        Assert.IsNotNull(state.DiagnosticHash);
        Assert.IsNotNull(state.AttemptResponseHash);

        var supersession = await verify.QuerySingleAsync<TakeoverCounts>("""
            SELECT
                (SELECT Status FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@PendingAgentRunId) AS PendingStatus,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE AgentRunId IN (@RunningAgentRunId, @PendingAgentRunId) AND Status=N'Superseded') AS SupersededRuns,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                 WHERE AgentRunId IN (@RunningAgentRunId, @PendingAgentRunId)
                   AND EventKind=N'AgentRunSuperseded') AS SupersededEvents;
            """, new
        {
            RunningAgentRunId = submitted.AgentRunId,
            PendingAgentRunId = pending.AgentRunId
        });
        Assert.AreEqual(WorkbenchAgentRunStates.Superseded, supersession.PendingStatus);
        Assert.AreEqual(2, supersession.SupersededRuns);
        Assert.AreEqual(2, supersession.SupersededEvents);
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
                   (SELECT COUNT(1) FROM dbo.ChatMessages WHERE TenantId=1 AND ProjectId=@ProjectId AND Role=N'assistant') AS AssistantMessages
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
        Assert.AreEqual("LateRestricted", state.AttemptOutcome);
        Assert.AreEqual(1, state.CancelledEvents);
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

        public Task<string> ExecuteAsync(
            WorkbenchBusinessAnalystContext context,
            CancellationToken cancellationToken = default)
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
            return Task.FromResult(LastRawOutput);
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
        public Task<string> ExecuteAsync(
            WorkbenchBusinessAnalystContext context,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The agent must not be invoked after controlled worker cancellation.");
    }

    private sealed class ValidSchemaAgent(string assistantMessage) : IWorkbenchBusinessAnalystAgent
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public string? ContextHash { get; private set; }

        public Task<string> ExecuteAsync(
            WorkbenchBusinessAnalystContext context,
            CancellationToken cancellationToken = default)
        {
            ContextHash = context.ContextHash;
            return Task.FromResult(JsonSerializer.Serialize(
                ValidOutput(context, assistantMessage),
                JsonOptions));
        }
    }

    private sealed class RunCounts
    {
        public int UserMessages { get; init; }
        public int AgentRuns { get; init; }
        public int Operations { get; init; }
        public int RequestedEvents { get; init; }
        public int Attributions { get; init; }
    }

    private sealed class RunProvenance
    {
        public string AgentVersion { get; init; } = string.Empty;
        public string PromptVersion { get; init; } = string.Empty;
        public string ToolPolicyVersion { get; init; } = string.Empty;
        public int OutputSchemaVersion { get; init; }
    }

    private sealed class MaterializationCounts
    {
        public int AssistantMessages { get; init; }
        public int MaterializedEvents { get; init; }
        public int Attempts { get; init; }
        public int ContextStampedAttempts { get; init; }
        public int LateRestrictedAttempts { get; init; }
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
        public string PendingStatus { get; init; } = string.Empty;
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
    }
}
