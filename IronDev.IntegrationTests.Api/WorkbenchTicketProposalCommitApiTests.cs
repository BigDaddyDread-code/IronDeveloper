using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class WorkbenchTicketProposalCommitApiTests : ApiTestBase
{
    [TestMethod]
    public async Task Commit_AtomicallyCreatesPermanentDeliveryProjectionAndReplaysExactIds()
    {
        var workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "Test",
            $"ticket-commit-{Guid.NewGuid():N}");
        Assert.IsFalse(Directory.Exists(workspaceRoot));

        using var commitFactory = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("LocalTest:WorkspaceRoot", workspaceRoot));
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = commitFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var project = await StartProjectAsync(client, "Atomic proposal commitment");
        var seeded = await SeedProposalSetAsync(client, project, ProposalSeedKind.Ready);
        var operationId = Guid.NewGuid();
        var request = CommitPayload(project, operationId, expectedRevision: 1);

        var response = await client.PostAsJsonAsync(CommitUrl(project.ProjectId, seeded.SetId), request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var committed = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.IsFalse(committed.GetProperty("isReplay").GetBoolean());
        Assert.AreEqual(operationId, committed.GetProperty("clientOperationId").GetGuid());
        Assert.AreEqual("Delivery", committed.GetProperty("projectLifecyclePhase").GetString());
        Assert.AreEqual("NotConfigured", committed.GetProperty("executionReadiness").GetString());

        var committedSet = committed.GetProperty("proposalSet");
        Assert.AreEqual(seeded.SetId, committedSet.GetProperty("ticketProposalSetId").GetGuid());
        Assert.AreEqual(2L, committedSet.GetProperty("revision").GetInt64());
        Assert.AreEqual(TicketProposalSetStatuses.Committed, committedSet.GetProperty("status").GetString());

        var commitment = committed.GetProperty("commitment");
        var commitmentId = commitment.GetProperty("commitmentId").GetGuid();
        Assert.AreNotEqual(Guid.Empty, commitmentId);
        Assert.AreEqual(seeded.SetId, commitment.GetProperty("ticketProposalSetId").GetGuid());
        Assert.AreEqual(1L, commitment.GetProperty("reviewedRevision").GetInt64());
        Assert.AreEqual(2L, commitment.GetProperty("committedRevision").GetInt64());
        Assert.AreEqual(64, commitment.GetProperty("reviewedSnapshotHash").GetString()!.Length);

        var responseMappings = commitment.GetProperty("tickets")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("ticketProposalId").GetGuid(),
                item => new ResponseTicketMapping(
                    item.GetProperty("projectTicketId").GetInt64(),
                    item.GetProperty("title").GetString()!,
                    item.GetProperty("suggestedOrder").GetInt32(),
                    item.GetProperty("blockedByTicketIds").EnumerateArray()
                        .Select(value => value.GetInt64()).ToArray()));
        Assert.AreEqual(3, responseMappings.Count);
        Assert.AreEqual("Sign in", responseMappings[seeded.FirstProposalId].Title);
        Assert.AreEqual("Profile", responseMappings[seeded.SecondProposalId].Title);
        Assert.AreEqual("Sign out", responseMappings[seeded.ThirdProposalId].Title);
        CollectionAssert.AreEqual(
            new[] { responseMappings[seeded.FirstProposalId].ProjectTicketId },
            responseMappings[seeded.SecondProposalId].BlockedByTicketIds.ToArray());

        await using (var connection = new SqlConnection(ConnectionString))
        {
            var projection = (await connection.QueryAsync<TicketProjectionRow>(
                """
                SELECT
                    mapping.TicketProposalId,
                    mapping.ProjectTicketId,
                    mapping.SuggestedOrder,
                    ticket.Title,
                    ticket.BlockedByTicketIds,
                    ticket.SourceChatMessageId,
                    workItem.Id AS WorkItemId,
                    workItem.OriginKind,
                    workItem.LegacyTicketId,
                    workItem.CurrentContractId,
                    contract.Id AS ContractId,
                    contract.ContractVersion,
                    contract.SourceTicketId,
                    contract.SourceWorkshopSessionId,
                    contract.SourceWorkshopMessageIds
                FROM dbo.TicketProposalCommitmentTickets mapping
                INNER JOIN dbo.ProjectTickets ticket
                    ON ticket.TenantId=mapping.TenantId
                   AND ticket.ProjectId=mapping.ProjectId
                   AND ticket.Id=mapping.ProjectTicketId
                INNER JOIN dbo.WorkItems workItem
                    ON workItem.TenantId=mapping.TenantId
                   AND workItem.ProjectId=mapping.ProjectId
                   AND workItem.Id=mapping.ProjectTicketId
                INNER JOIN dbo.WorkItemContracts contract
                    ON contract.Id=workItem.CurrentContractId
                WHERE mapping.TenantId=1 AND mapping.ProjectId=@ProjectId
                  AND mapping.TicketProposalCommitmentId=@CommitmentId
                ORDER BY mapping.SuggestedOrder;
                """,
                new { project.ProjectId, CommitmentId = commitmentId })).ToArray();
            Assert.AreEqual(3, projection.Length);

            foreach (var row in projection)
            {
                var responseMapping = responseMappings[row.TicketProposalId];
                Assert.AreEqual(responseMapping.ProjectTicketId, row.ProjectTicketId);
                Assert.AreEqual(responseMapping.SuggestedOrder, row.SuggestedOrder);
                Assert.AreEqual(row.ProjectTicketId, row.WorkItemId);
                Assert.AreEqual(row.ProjectTicketId, row.LegacyTicketId);
                Assert.AreEqual(row.ProjectTicketId, row.SourceTicketId);
                Assert.AreEqual(row.ContractId, row.CurrentContractId);
                Assert.AreEqual(1, row.ContractVersion);
                Assert.AreEqual("Workshop", row.OriginKind);
                Assert.AreEqual(seeded.SourceMessageId, row.SourceChatMessageId);
                Assert.AreEqual(seeded.ChatSessionId, row.SourceWorkshopSessionId);
                StringAssert.Contains(
                    row.SourceWorkshopMessageIds ?? string.Empty,
                    seeded.SourceMessageId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            var dependent = projection.Single(row => row.TicketProposalId == seeded.SecondProposalId);
            CollectionAssert.AreEqual(
                new[] { responseMappings[seeded.FirstProposalId].ProjectTicketId },
                ParseBlockedByTicketIds(dependent.BlockedByTicketIds));

            var dependency = await connection.QuerySingleAsync<DependencyProjectionRow>(
                """
                SELECT
                    DependentTicketProposalId,
                    DependentProjectTicketId,
                    DependsOnTicketProposalId,
                    DependsOnProjectTicketId
                FROM dbo.TicketProposalCommitmentDependencies
                WHERE TenantId=1 AND ProjectId=@ProjectId
                  AND TicketProposalCommitmentId=@CommitmentId;
                """,
                new { project.ProjectId, CommitmentId = commitmentId });
            Assert.AreEqual(seeded.SecondProposalId, dependency.DependentTicketProposalId);
            Assert.AreEqual(responseMappings[seeded.SecondProposalId].ProjectTicketId,
                dependency.DependentProjectTicketId);
            Assert.AreEqual(seeded.FirstProposalId, dependency.DependsOnTicketProposalId);
            Assert.AreEqual(responseMappings[seeded.FirstProposalId].ProjectTicketId,
                dependency.DependsOnProjectTicketId);

            var state = await ReadCommitStateAsync(connection, project.ProjectId, seeded.SetId, operationId);
            Assert.AreEqual(3, state.PermanentTickets);
            Assert.AreEqual(3, state.WorkItems);
            Assert.AreEqual(3, state.WorkItemContracts);
            Assert.AreEqual(3, state.TicketMappings);
            Assert.AreEqual(1, state.DependencyMappings);
            Assert.AreEqual(3, state.SourceReferences);
            Assert.AreEqual(1, state.Commitments);
            Assert.AreEqual(2, state.ProposalRevisions);
            Assert.AreEqual(2, state.LifecycleRevisions);
            Assert.AreEqual(1, state.ReadinessRevisions);
            Assert.AreEqual(1, state.CompletedClientOperations);
            Assert.AreEqual(1, state.CommitOutboxEvents);
            Assert.AreEqual(TicketProposalSetStatuses.Committed, state.ProposalStatus);
            Assert.AreEqual(2L, state.ProposalCurrentRevision);
            Assert.AreEqual("Delivery", state.LifecyclePhase);
            Assert.AreEqual("NotConfigured", state.ExecutionReadiness);
            Assert.IsNull(state.LocalPath);
        }

        Assert.IsFalse(Directory.Exists(workspaceRoot),
            "Committing reviewed tickets must not create a repository or workspace directory.");

        var replayResponse = await client.PostAsJsonAsync(
            CommitUrl(project.ProjectId, seeded.SetId),
            request);
        Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(replay.GetProperty("isReplay").GetBoolean());
        Assert.AreEqual(commitmentId,
            replay.GetProperty("commitment").GetProperty("commitmentId").GetGuid());
        var replayMappings = replay.GetProperty("commitment").GetProperty("tickets")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("ticketProposalId").GetGuid(),
                item => item.GetProperty("projectTicketId").GetInt64());
        CollectionAssert.AreEquivalent(
            responseMappings.Select(pair => pair.Value.ProjectTicketId).ToArray(),
            replayMappings.Values.ToArray());

        var mismatchResponse = await client.PostAsJsonAsync(
            CommitUrl(project.ProjectId, seeded.SetId),
            CommitPayload(project, operationId, expectedRevision: 2));
        Assert.AreEqual(HttpStatusCode.Conflict, mismatchResponse.StatusCode);
        await AssertErrorAsync(mismatchResponse, ProjectStartOperationMismatchException.ErrorCode);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            var replayState = await ReadCommitStateAsync(
                connection,
                project.ProjectId,
                seeded.SetId,
                operationId);
            Assert.AreEqual(3, replayState.PermanentTickets);
            Assert.AreEqual(3, replayState.WorkItems);
            Assert.AreEqual(3, replayState.WorkItemContracts);
            Assert.AreEqual(1, replayState.Commitments);
            Assert.AreEqual(2, replayState.ProposalRevisions);
            Assert.AreEqual(1, replayState.CompletedClientOperations);
            Assert.AreEqual(1, replayState.CommitOutboxEvents);
        }
    }

    [TestMethod]
    public async Task Commit_RejectsStaleBlockingNeedsInputAndAlreadyCommittedStatesWithoutPartialWrites()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);

        var staleProject = await StartProjectAsync(client, "Stale proposal commitment");
        var staleSet = await SeedProposalSetAsync(client, staleProject, ProposalSeedKind.Ready);
        var stale = await client.PostAsJsonAsync(
            CommitUrl(staleProject.ProjectId, staleSet.SetId),
            CommitPayload(staleProject, Guid.NewGuid(), expectedRevision: 99));
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        await AssertErrorAsync(stale, TicketProposalRevisionConflictException.ErrorCode);
        await AssertNoCommitWritesAsync(staleProject.ProjectId, staleSet.SetId);

        var blockingProject = await StartProjectAsync(client, "Open proposal issue commitment");
        var blockingSet = await SeedProposalSetAsync(client, blockingProject, ProposalSeedKind.ReadyWithOpenIssue);
        var blocking = await client.PostAsJsonAsync(
            CommitUrl(blockingProject.ProjectId, blockingSet.SetId),
            CommitPayload(blockingProject, Guid.NewGuid(), expectedRevision: 1));
        Assert.AreEqual(HttpStatusCode.Conflict, blocking.StatusCode);
        await AssertErrorAsync(blocking, TicketProposalBlockingIssuesException.ErrorCode);
        await AssertNoCommitWritesAsync(blockingProject.ProjectId, blockingSet.SetId);

        var needsInputProject = await StartProjectAsync(client, "Needs input proposal commitment");
        var needsInputSet = await SeedProposalSetAsync(client, needsInputProject, ProposalSeedKind.NeedsInput);
        var needsInput = await client.PostAsJsonAsync(
            CommitUrl(needsInputProject.ProjectId, needsInputSet.SetId),
            CommitPayload(needsInputProject, Guid.NewGuid(), expectedRevision: 1));
        Assert.AreEqual(HttpStatusCode.Conflict, needsInput.StatusCode);
        await AssertErrorAsync(needsInput, TicketProposalSetNotReadyException.ErrorCode);
        await AssertNoCommitWritesAsync(needsInputProject.ProjectId, needsInputSet.SetId);

        var committedProject = await StartProjectAsync(client, "Single commitment boundary");
        var committedSet = await SeedProposalSetAsync(client, committedProject, ProposalSeedKind.Ready);
        var firstCommit = await client.PostAsJsonAsync(
            CommitUrl(committedProject.ProjectId, committedSet.SetId),
            CommitPayload(committedProject, Guid.NewGuid(), expectedRevision: 1));
        Assert.AreEqual(HttpStatusCode.OK, firstCommit.StatusCode);

        var secondOperation = await client.PostAsJsonAsync(
            CommitUrl(committedProject.ProjectId, committedSet.SetId),
            CommitPayload(committedProject, Guid.NewGuid(), expectedRevision: 2));
        Assert.AreEqual(HttpStatusCode.Conflict, secondOperation.StatusCode);
        await AssertErrorAsync(secondOperation, TicketProposalAlreadyCommittedException.ErrorCode);

        await using var connection = new SqlConnection(ConnectionString);
        var committedState = await ReadCommitStateAsync(
            connection,
            committedProject.ProjectId,
            committedSet.SetId,
            operationId: null);
        Assert.AreEqual(3, committedState.PermanentTickets);
        Assert.AreEqual(1, committedState.Commitments);
        Assert.AreEqual(2, committedState.ProposalRevisions);
        Assert.AreEqual(1, committedState.CommitOutboxEvents);
    }

    [TestMethod]
    public async Task Commit_RequiresRouteScopedAccessAndTheCurrentWriteLeaseBeforeAnyWrite()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Commit lease fence");
        var set = await SeedProposalSetAsync(client, project, ProposalSeedKind.Ready);
        var foreignProject = await StartProjectAsync(client, "Foreign commit route");

        var concealed = await client.PostAsJsonAsync(
            CommitUrl(foreignProject.ProjectId, set.SetId),
            CommitPayload(foreignProject, Guid.NewGuid(), expectedRevision: 1));
        Assert.AreEqual(HttpStatusCode.NotFound, concealed.StatusCode);
        await AssertErrorAsync(concealed, "ticket_proposal_set_not_found");
        await AssertNoCommitWritesAsync(project.ProjectId, set.SetId);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                UPDATE dbo.WorkbenchWriteLeases
                SET RevokedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=1 AND ProjectId=@ProjectId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                """,
                project);
        }

        var fencedOperationId = Guid.NewGuid();
        var fenced = await client.PostAsJsonAsync(
            CommitUrl(project.ProjectId, set.SetId),
            CommitPayload(project, fencedOperationId, expectedRevision: 1));
        Assert.AreEqual(HttpStatusCode.Conflict, fenced.StatusCode);
        await AssertErrorAsync(fenced, WorkbenchLeaseFenceException.ErrorCode);
        await AssertNoCommitWritesAsync(project.ProjectId, set.SetId, fencedOperationId);
    }

    [TestMethod]
    public async Task Commit_RollsBackEveryDurableWriteWhenAnyTransactionalStageFails()
    {
        var injector = new TestTicketProposalCommitFailureInjector();
        using var failureFactory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITicketProposalCommitFailureInjector>();
                services.AddSingleton<ITicketProposalCommitFailureInjector>(injector);
            }));
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = failureFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        foreach (var failurePoint in Enum.GetValues<TicketProposalCommitFailurePoint>())
        {
            var project = await StartProjectAsync(client, $"Rollback at {failurePoint}");
            var set = await SeedProposalSetAsync(client, project, ProposalSeedKind.Ready);
            var operationId = Guid.NewGuid();
            injector.FailurePoint = failurePoint;
            try
            {
                await Assert.ThrowsExceptionAsync<InjectedTicketProposalCommitFailureException>(() =>
                    client.PostAsJsonAsync(
                        CommitUrl(project.ProjectId, set.SetId),
                        CommitPayload(project, operationId, expectedRevision: 1)));
            }
            finally
            {
                injector.FailurePoint = null;
            }
            await AssertNoCommitWritesAsync(project.ProjectId, set.SetId, operationId);
        }
    }

    [TestMethod]
    public async Task Commit_PreservesTheCurrentExecutionReadinessProjection()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Readiness-preserving ticket commitment");
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                INSERT dbo.ProjectReadinessAssessments
                    (TenantId, ProjectId, Revision, ExecutionReadiness, ReasonCode,
                     Summary, AssessedByActorUserId)
                VALUES
                    (1, @ProjectId, 2, N'Ready', N'TestReadyProjection',
                     N'Established readiness must survive ticket commitment.', 1);
                """,
                new { project.ProjectId });
        }
        var set = await SeedProposalSetAsync(client, project, ProposalSeedKind.Ready);

        var response = await client.PostAsJsonAsync(
            CommitUrl(project.ProjectId, set.SetId),
            CommitPayload(project, Guid.NewGuid(), expectedRevision: 1));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Delivery", body.GetProperty("projectLifecyclePhase").GetString());
        Assert.AreEqual("Ready", body.GetProperty("executionReadiness").GetString());
        await using var verification = new SqlConnection(ConnectionString);
        var readiness = await verification.QuerySingleAsync<ReadinessProjectionState>(
            """
            SELECT COUNT(1) AS Revisions,
                   MAX(Revision) AS CurrentRevision,
                   (SELECT TOP (1) ExecutionReadiness
                    FROM dbo.ProjectReadinessAssessments
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                    ORDER BY Revision DESC) AS CurrentState
            FROM dbo.ProjectReadinessAssessments
            WHERE TenantId=1 AND ProjectId=@ProjectId;
            """,
            new { project.ProjectId });
        Assert.AreEqual(2, readiness.Revisions);
        Assert.AreEqual(2L, readiness.CurrentRevision);
        Assert.AreEqual("Ready", readiness.CurrentState);
    }

    [TestMethod]
    public async Task HistoricalLongTitle_RemainsReviewableAndMustBeShortenedBeforeCommit()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Historical proposal-title upgrade");
        var historicalTitle = new string(
            'h',
            TicketProposalConstraints.MaximumTitleCharacters + 1);
        var set = await SeedProposalSetAsync(
            client,
            project,
            ProposalSeedKind.Ready,
            historicalTitle);

        var read = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{set.SetId}" +
            $"?workbenchSessionId={project.WorkbenchSessionId}&leaseEpoch={project.LeaseEpoch}");
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
        var readBody = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(
            historicalTitle,
            readBody.GetProperty("proposals")[0].GetProperty("title").GetString());

        var rejectedOperationId = Guid.NewGuid();
        var rejected = await client.PostAsJsonAsync(
            CommitUrl(project.ProjectId, set.SetId),
            CommitPayload(project, rejectedOperationId, expectedRevision: 1));
        Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);
        await AssertErrorAsync(rejected, TicketProposalCommitBoundaryException.ErrorCode);
        await AssertNoCommitWritesAsync(
            project.ProjectId,
            set.SetId,
            rejectedOperationId);

        var shortenedTitle = new string(
            's',
            TicketProposalConstraints.MaximumTitleCharacters);
        var edit = await client.PatchAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{set.SetId}" +
            $"/proposals/{set.FirstProposalId}",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedProposalSetRevision = 1,
                title = shortenedTitle,
                problem = "People cannot enter their workspace.",
                proposedChange = "Add a secure sign-in journey.",
                acceptanceCriteria = new[] { "Valid credentials open the workspace." }
            });
        Assert.AreEqual(HttpStatusCode.OK, edit.StatusCode);

        var committed = await client.PostAsJsonAsync(
            CommitUrl(project.ProjectId, set.SetId),
            CommitPayload(project, Guid.NewGuid(), expectedRevision: 2));
        Assert.AreEqual(HttpStatusCode.OK, committed.StatusCode);
        var committedBody = await committed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(
            shortenedTitle,
            committedBody.GetProperty("commitment").GetProperty("tickets")[0]
                .GetProperty("title").GetString());
    }

    [TestMethod]
    public async Task CommittedProposalSet_RejectsEveryReviewMutationAndRegenerationBeforeAnySideEffect()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Committed proposal authority boundary");
        var set = await SeedProposalSetAsync(client, project, ProposalSeedKind.ReadyWithResolvedIssue);

        var commit = await client.PostAsJsonAsync(
            CommitUrl(project.ProjectId, set.SetId),
            CommitPayload(project, Guid.NewGuid(), expectedRevision: 1));
        Assert.AreEqual(HttpStatusCode.OK, commit.StatusCode);

        var editOperationId = Guid.NewGuid();
        var edit = await client.PatchAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{set.SetId}" +
            $"/proposals/{set.FirstProposalId}",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = editOperationId,
                expectedProposalSetRevision = 2,
                title = "Attempted post-commit edit",
                problem = "Committed content must be immutable.",
                proposedChange = "Reject this edit before persistence.",
                acceptanceCriteria = new[] { "No committed proposal revision changes." }
            });
        Assert.AreEqual(HttpStatusCode.Conflict, edit.StatusCode);
        await AssertErrorAsync(edit, TicketProposalAlreadyCommittedException.ErrorCode);

        var reorderOperationId = Guid.NewGuid();
        var reorder = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{set.SetId}/reorder",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = reorderOperationId,
                expectedProposalSetRevision = 2,
                orderedProposalIds = new[]
                {
                    set.ThirdProposalId,
                    set.FirstProposalId,
                    set.SecondProposalId
                }
            });
        Assert.AreEqual(HttpStatusCode.Conflict, reorder.StatusCode);
        await AssertErrorAsync(reorder, TicketProposalAlreadyCommittedException.ErrorCode);

        var removeOperationId = Guid.NewGuid();
        var remove = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{set.SetId}" +
            $"/proposals/{set.ThirdProposalId}/remove",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = removeOperationId,
                expectedProposalSetRevision = 2
            });
        Assert.AreEqual(HttpStatusCode.Conflict, remove.StatusCode);
        await AssertErrorAsync(remove, TicketProposalAlreadyCommittedException.ErrorCode);

        var resolveOperationId = Guid.NewGuid();
        var resolve = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{set.SetId}" +
            $"/issues/{set.IssueId}/resolve",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = resolveOperationId,
                expectedProposalSetRevision = 2,
                resolution = "Attempt to replace the reviewed resolution."
            });
        Assert.AreEqual(HttpStatusCode.Conflict, resolve.StatusCode);
        await AssertErrorAsync(resolve, TicketProposalAlreadyCommittedException.ErrorCode);

        var regenerationOperationId = Guid.NewGuid();
        var regeneration = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{set.SetId}/regenerations",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = regenerationOperationId,
                chatSessionId = set.ChatSessionId,
                expectedProposalSetRevision = 2,
                instruction = "Attempt to regenerate committed proposals."
            });
        Assert.AreEqual(HttpStatusCode.Conflict, regeneration.StatusCode);
        await AssertErrorAsync(regeneration, TicketProposalAlreadyCommittedException.ErrorCode);

        await using var connection = new SqlConnection(ConnectionString);
        var authority = await connection.QuerySingleAsync<CommittedAuthorityState>(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.TicketProposalSetRevisions
                 WHERE TenantId=1 AND ProjectId=@ProjectId
                   AND TicketProposalSetId=@SetId) AS ProposalRevisions,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS AgentRuns,
                (SELECT COUNT(1) FROM dbo.ChatMessages
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS ChatMessages,
                (SELECT COUNT(1) FROM dbo.ClientOperations
                 WHERE TenantId=1 AND ClientOperationId IN @RejectedOperationIds) AS RejectedClientOperations,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                 WHERE TenantId=1 AND ProjectId=@ProjectId
                   AND EventKind=N'TicketProposalSetCommitted') AS CommitOutboxEvents,
                (SELECT COUNT(1) FROM dbo.ProjectTickets
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND IsDeleted=0) AS PermanentTickets,
                (SELECT Status FROM dbo.TicketProposalSets
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SetId) AS ProposalStatus,
                (SELECT CurrentRevision FROM dbo.TicketProposalSets
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SetId) AS ProposalCurrentRevision;
            """,
            new
            {
                project.ProjectId,
                set.SetId,
                RejectedOperationIds = new[]
                {
                    editOperationId,
                    reorderOperationId,
                    removeOperationId,
                    resolveOperationId,
                    regenerationOperationId
                }
            });
        Assert.AreEqual(2, authority.ProposalRevisions);
        Assert.AreEqual(1, authority.AgentRuns,
            "Rejected regeneration must not create a BA run.");
        Assert.AreEqual(1, authority.ChatMessages,
            "Rejected commands must not be persisted as BA conversation turns.");
        Assert.AreEqual(0, authority.RejectedClientOperations);
        Assert.AreEqual(1, authority.CommitOutboxEvents);
        Assert.AreEqual(3, authority.PermanentTickets);
        Assert.AreEqual(TicketProposalSetStatuses.Committed, authority.ProposalStatus);
        Assert.AreEqual(2L, authority.ProposalCurrentRevision);
    }

    [TestMethod]
    public async Task RegenerationClaimedBeforeCommit_CannotMaterializeOverTheCommittedRevision()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "In-flight regeneration commit race");
        var set = await SeedProposalSetAsync(client, project, ProposalSeedKind.Ready);

        var submit = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{set.SetId}/regenerations",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId = set.ChatSessionId,
                expectedProposalSetRevision = 1,
                instruction = "Prepare a competing regeneration before commitment."
            });
        Assert.AreEqual(HttpStatusCode.Accepted, submit.StatusCode);
        var submitted = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var agentRunId = submitted.GetProperty("agentRunId").GetGuid();

        using var scope = Factory.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IWorkbenchAgentRunService>();
        var assembler = scope.ServiceProvider.GetRequiredService<IWorkbenchAgentContextAssembler>();
        var claim = await runs.ClaimAsync(
            agentRunId,
            "committed-set-race-worker",
            TimeSpan.FromMinutes(5));
        Assert.IsNotNull(claim);
        var context = await assembler.AssembleAsync(claim);

        var commit = await client.PostAsJsonAsync(
            CommitUrl(project.ProjectId, set.SetId),
            CommitPayload(project, Guid.NewGuid(), expectedRevision: 1));
        Assert.AreEqual(HttpStatusCode.OK, commit.StatusCode);

        var output = new WorkbenchBusinessAnalystOutput(
            WorkbenchBusinessAnalystContract.OutputSchemaVersion3,
            context.ContextHash,
            context.UnderstandingRevision,
            WorkbenchAgentRunStates.Completed,
            "This late regeneration must never become visible.",
            UnderstandingPatch: null,
            RenameProposal: null,
            TicketProposalSet: new TicketProposalSetOutput(
                "A competing regenerated outcome.",
                [
                    new TicketProposalOutput(
                        "late-proposal",
                        "Late regenerated proposal",
                        "A completed commitment must be terminal.",
                        "Attempt to overwrite the committed proposal set.",
                        ["The late output remains invisible."],
                        [],
                        1,
                        [context.SourceUserMessageId])
                ],
                [],
                [],
                [context.SourceUserMessageId]));

        await Assert.ThrowsExceptionAsync<TicketProposalAlreadyCommittedException>(() =>
            runs.MaterializeAsync(claim, context, output));

        await using var connection = new SqlConnection(ConnectionString);
        var race = await connection.QuerySingleAsync<CommittedMaterializationRaceState>(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.TicketProposalSetRevisions
                 WHERE TenantId=1 AND ProjectId=@ProjectId
                   AND TicketProposalSetId=@SetId) AS ProposalRevisions,
                (SELECT COUNT(1) FROM dbo.TicketProposalCommitments
                 WHERE TenantId=1 AND ProjectId=@ProjectId
                   AND TicketProposalSetId=@SetId) AS Commitments,
                (SELECT COUNT(1) FROM dbo.ProjectTickets
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND IsDeleted=0) AS PermanentTickets,
                (SELECT COUNT(1) FROM dbo.ChatMessages
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND Role=N'assistant') AS AssistantMessages,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                 WHERE TenantId=1 AND ProjectId=@ProjectId
                   AND EventKind=N'AgentRunMaterialized') AS MaterializationEvents,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE AgentRunId=@AgentRunId
                   AND AssistantMessageId IS NOT NULL) AS RunsWithVisibleOutput,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE AgentRunId=@AgentRunId
                   AND MaterializedTicketProposalRevision IS NOT NULL) AS RunsWithMaterializedRevision,
                (SELECT Status FROM dbo.TicketProposalSets
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SetId) AS ProposalStatus,
                (SELECT CurrentRevision FROM dbo.TicketProposalSets
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SetId) AS ProposalCurrentRevision;
            """,
            new { project.ProjectId, set.SetId, AgentRunId = agentRunId });
        Assert.AreEqual(2, race.ProposalRevisions);
        Assert.AreEqual(1, race.Commitments);
        Assert.AreEqual(3, race.PermanentTickets);
        Assert.AreEqual(0, race.AssistantMessages);
        Assert.AreEqual(0, race.MaterializationEvents);
        Assert.AreEqual(0, race.RunsWithVisibleOutput);
        Assert.AreEqual(0, race.RunsWithMaterializedRevision);
        Assert.AreEqual(TicketProposalSetStatuses.Committed, race.ProposalStatus);
        Assert.AreEqual(2L, race.ProposalCurrentRevision);
    }

    private static string CommitUrl(int projectId, Guid setId) =>
        $"/api/workbench/projects/{projectId}/ticket-proposal-sets/{setId}/commits";

    private static long[] ParseBlockedByTicketIds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(item => long.Parse(
                    item.Trim(),
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

    private static object CommitPayload(
        StartedProject project,
        Guid operationId,
        long expectedRevision) => new
        {
            project.WorkbenchSessionId,
            project.LeaseEpoch,
            clientOperationId = operationId,
            expectedProposalSetRevision = expectedRevision
        };

    private static async Task AssertErrorAsync(HttpResponseMessage response, string expectedError)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(expectedError, body.GetProperty("error").GetString());
    }

    private static async Task AssertNoCommitWritesAsync(
        int projectId,
        Guid setId,
        Guid? operationId = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var state = await ReadCommitStateAsync(connection, projectId, setId, operationId);
        Assert.AreEqual(0, state.PermanentTickets);
        Assert.AreEqual(0, state.WorkItems);
        Assert.AreEqual(0, state.WorkItemContracts);
        Assert.AreEqual(0, state.TicketMappings);
        Assert.AreEqual(0, state.DependencyMappings);
        Assert.AreEqual(0, state.SourceReferences);
        Assert.AreEqual(0, state.Commitments);
        Assert.AreEqual(1, state.ProposalRevisions);
        Assert.AreEqual(1, state.LifecycleRevisions);
        Assert.AreEqual(1, state.ReadinessRevisions);
        Assert.AreEqual(0, state.CompletedClientOperations);
        Assert.AreEqual(0, state.CommitOutboxEvents);
        Assert.AreEqual(1L, state.ProposalCurrentRevision);
        Assert.AreNotEqual(TicketProposalSetStatuses.Committed, state.ProposalStatus);
        Assert.AreEqual("Shaping", state.LifecyclePhase);
        Assert.AreEqual("NotConfigured", state.ExecutionReadiness);
        Assert.IsNull(state.LocalPath);
    }

    private static Task<CommitDatabaseState> ReadCommitStateAsync(
        SqlConnection connection,
        int projectId,
        Guid setId,
        Guid? operationId) =>
        connection.QuerySingleAsync<CommitDatabaseState>(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.ProjectTickets
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND IsDeleted=0) AS PermanentTickets,
                (SELECT COUNT(1) FROM dbo.WorkItems
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS WorkItems,
                (SELECT COUNT(1) FROM dbo.WorkItemContracts
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS WorkItemContracts,
                (SELECT COUNT(1) FROM dbo.TicketProposalCommitmentTickets
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS TicketMappings,
                (SELECT COUNT(1) FROM dbo.TicketProposalCommitmentDependencies
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS DependencyMappings,
                (SELECT COUNT(1) FROM dbo.ArtifactSourceReferences
                 WHERE TenantId=1 AND ProjectId=@ProjectId
                   AND ArtifactType=N'Ticket' AND SourceType=N'ChatMessage'
                   AND ReferenceType=N'CreatedFrom') AS SourceReferences,
                (SELECT COUNT(1) FROM dbo.TicketProposalCommitments
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS Commitments,
                (SELECT COUNT(1) FROM dbo.TicketProposalSetRevisions
                 WHERE TenantId=1 AND ProjectId=@ProjectId
                   AND TicketProposalSetId=@SetId) AS ProposalRevisions,
                (SELECT COUNT(1) FROM dbo.ProjectLifecyclePhases
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS LifecycleRevisions,
                (SELECT COUNT(1) FROM dbo.ProjectReadinessAssessments
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS ReadinessRevisions,
                (SELECT COUNT(1) FROM dbo.ClientOperations
                 WHERE TenantId=1
                   AND (@OperationId IS NULL OR ClientOperationId=@OperationId)
                   AND OperationKind=N'CommitTicketProposalSet'
                   AND Status=N'Completed') AS CompletedClientOperations,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                 WHERE TenantId=1 AND ProjectId=@ProjectId
                   AND EventKind=N'TicketProposalSetCommitted') AS CommitOutboxEvents,
                (SELECT Status FROM dbo.TicketProposalSets
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SetId) AS ProposalStatus,
                (SELECT CurrentRevision FROM dbo.TicketProposalSets
                 WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SetId) AS ProposalCurrentRevision,
                (SELECT TOP (1) Phase FROM dbo.ProjectLifecyclePhases
                 WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY Revision DESC) AS LifecyclePhase,
                (SELECT TOP (1) ExecutionReadiness FROM dbo.ProjectReadinessAssessments
                 WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY Revision DESC) AS ExecutionReadiness,
                (SELECT LocalPath FROM dbo.Projects
                 WHERE TenantId=1 AND Id=@ProjectId) AS LocalPath;
            """,
            new { ProjectId = projectId, SetId = setId, OperationId = operationId });

    private static async Task<StartedProject> StartProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/projects/start", new
        {
            clientOperationId = Guid.NewGuid(),
            name
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new StartedProject(
            body.GetProperty("projectId").GetInt32(),
            body.GetProperty("workbenchSessionId").GetInt64(),
            body.GetProperty("leaseEpoch").GetInt64());
    }

    private static async Task<SeededProposalSet> SeedProposalSetAsync(
        HttpClient client,
        StartedProject project,
        ProposalSeedKind kind,
        string? firstProposalTitle = null)
    {
        long chatSessionId;
        await using (var connection = new SqlConnection(ConnectionString))
        {
            chatSessionId = await connection.QuerySingleAsync<long>(
                """
                INSERT dbo.ProjectChatSessions(TenantId, ProjectId, Title)
                OUTPUT inserted.Id
                VALUES (1, @ProjectId, N'Proposal commitment seed');
                """,
                new { project.ProjectId });
        }

        var runResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/inputs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                composerText = "Capture this source turn for permanent ticket commitment tests."
            });
        Assert.AreEqual(HttpStatusCode.Accepted, runResponse.StatusCode);
        var runEnvelope = await runResponse.Content.ReadFromJsonAsync<JsonElement>();
        var run = runEnvelope.GetProperty("agentRun");
        var agentRunId = run.GetProperty("agentRunId").GetGuid();
        var sourceMessageId = run.GetProperty("userMessageId").GetInt64();

        var setId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        var issueId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var proposals = kind == ProposalSeedKind.NeedsInput
            ? Array.Empty<TicketProposalDocument>()
            :
            [
                new TicketProposalDocument(
                    firstId,
                    firstProposalTitle ?? "Sign in",
                    "People cannot enter their workspace.",
                    "Add a secure sign-in journey.",
                    ["Valid credentials open the workspace."],
                    [],
                    1,
                    [sourceMessageId]),
                new TicketProposalDocument(
                    secondId,
                    "Profile",
                    "People cannot see their identity.",
                    "Show the signed-in profile.",
                    ["The current user name is visible."],
                    [firstId],
                    2,
                    [sourceMessageId]),
                new TicketProposalDocument(
                    thirdId,
                    "Sign out",
                    "People cannot end a session.",
                    "Add sign out.",
                    ["Sign out returns to the entry screen."],
                    [],
                    3,
                    [sourceMessageId])
            ];
        var questions = kind switch
        {
            ProposalSeedKind.ReadyWithOpenIssue or ProposalSeedKind.NeedsInput =>
            [
                new TicketProposalIssueDocument(
                    issueId,
                    TicketProposalIssueKinds.Question,
                    "Which sign-in identifier should v0.1 use?",
                    TicketProposalIssueStatuses.Open,
                    null,
                    [sourceMessageId])
            ],
            ProposalSeedKind.ReadyWithResolvedIssue =>
            [
                new TicketProposalIssueDocument(
                    issueId,
                    TicketProposalIssueKinds.Question,
                    "Which sign-in identifier should v0.1 use?",
                    TicketProposalIssueStatuses.Resolved,
                    "Use email and password for v0.1.",
                    [sourceMessageId])
            ],
            _ => Array.Empty<TicketProposalIssueDocument>()
        };
        var status = kind == ProposalSeedKind.NeedsInput
            ? TicketProposalSetStatuses.NeedsInput
            : TicketProposalSetStatuses.Ready;
        var document = new TicketProposalSetDocument(
            setId,
            project.ProjectId,
            project.WorkbenchSessionId,
            project.LeaseEpoch,
            Revision: 1,
            BasedOnUnderstandingRevision: 1,
            status,
            kind == ProposalSeedKind.NeedsInput
                ? "More project context is required before tickets can be proposed."
                : "Three independently reviewable user outcomes.",
            proposals,
            questions,
            [],
            [sourceMessageId],
            agentRunId,
            now,
            now);
        var snapshotJson = TicketProposalSetDocumentCodec.Serialize(document);
        var snapshotHash = TicketProposalSetDocumentCodec.ComputeHash(snapshotJson);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                INSERT dbo.TicketProposalSets
                    (Id, TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch, CurrentRevision,
                     BasedOnUnderstandingRevision, Status, SplitReason, SourceMessageIdsJson,
                     CreatedByAgentRunId, CreatedByActorUserId, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@SetId, 1, @ProjectId, @WorkbenchSessionId, @LeaseEpoch, 1,
                     1, @Status, @SplitReason, @SourceMessageIdsJson,
                     @AgentRunId, 1, @CreatedAtUtc, @UpdatedAtUtc);

                INSERT dbo.TicketProposalSetRevisions
                    (TenantId, ProjectId, TicketProposalSetId, Revision, SnapshotJson,
                     SnapshotHash, ActorUserId, AgentRunId, ChangeKind)
                VALUES
                    (1, @ProjectId, @SetId, 1, @SnapshotJson,
                     @SnapshotHash, 1, @AgentRunId, N'Generated');

                UPDATE dbo.WorkbenchAgentRuns
                SET Status=N'Cancelled', ActiveRunSlot=NULL,
                    CancellationRequestedAtUtc=SYSUTCDATETIME(), CompletedAtUtc=SYSUTCDATETIME()
                WHERE AgentRunId=@AgentRunId AND Status IN (N'Pending', N'Running');
                """,
                new
                {
                    SetId = setId,
                    project.ProjectId,
                    project.WorkbenchSessionId,
                    project.LeaseEpoch,
                    Status = document.Status,
                    document.SplitReason,
                    SourceMessageIdsJson = JsonSerializer.Serialize(document.SourceMessageIds),
                    AgentRunId = agentRunId,
                    document.CreatedAtUtc,
                    document.UpdatedAtUtc,
                    SnapshotJson = snapshotJson,
                    SnapshotHash = snapshotHash
                });
        }

        return new SeededProposalSet(
            setId,
            firstId,
            secondId,
            thirdId,
            issueId,
            chatSessionId,
            sourceMessageId);
    }

    private enum ProposalSeedKind
    {
        Ready,
        ReadyWithOpenIssue,
        ReadyWithResolvedIssue,
        NeedsInput
    }

    private sealed class TestTicketProposalCommitFailureInjector : ITicketProposalCommitFailureInjector
    {
        public TicketProposalCommitFailurePoint? FailurePoint { get; set; }

        public void ThrowIfRequested(TicketProposalCommitFailurePoint point)
        {
            if (FailurePoint == point)
                throw new InjectedTicketProposalCommitFailureException(point);
        }
    }

    private sealed class InjectedTicketProposalCommitFailureException(
        TicketProposalCommitFailurePoint point)
        : Exception($"Injected ticket-proposal commit failure at {point}.");

    private sealed record StartedProject(int ProjectId, long WorkbenchSessionId, long LeaseEpoch);

    private sealed record SeededProposalSet(
        Guid SetId,
        Guid FirstProposalId,
        Guid SecondProposalId,
        Guid ThirdProposalId,
        Guid IssueId,
        long ChatSessionId,
        long SourceMessageId);

    private sealed record ResponseTicketMapping(
        long ProjectTicketId,
        string Title,
        int SuggestedOrder,
        IReadOnlyList<long> BlockedByTicketIds);

    private sealed class TicketProjectionRow
    {
        public Guid TicketProposalId { get; init; }
        public long ProjectTicketId { get; init; }
        public int SuggestedOrder { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? BlockedByTicketIds { get; init; }
        public long? SourceChatMessageId { get; init; }
        public long WorkItemId { get; init; }
        public string OriginKind { get; init; } = string.Empty;
        public long? LegacyTicketId { get; init; }
        public long? CurrentContractId { get; init; }
        public long ContractId { get; init; }
        public int ContractVersion { get; init; }
        public long? SourceTicketId { get; init; }
        public long? SourceWorkshopSessionId { get; init; }
        public string? SourceWorkshopMessageIds { get; init; }
    }

    private sealed class DependencyProjectionRow
    {
        public Guid DependentTicketProposalId { get; init; }
        public long DependentProjectTicketId { get; init; }
        public Guid DependsOnTicketProposalId { get; init; }
        public long DependsOnProjectTicketId { get; init; }
    }

    private sealed class CommitDatabaseState
    {
        public int PermanentTickets { get; init; }
        public int WorkItems { get; init; }
        public int WorkItemContracts { get; init; }
        public int TicketMappings { get; init; }
        public int DependencyMappings { get; init; }
        public int SourceReferences { get; init; }
        public int Commitments { get; init; }
        public int ProposalRevisions { get; init; }
        public int LifecycleRevisions { get; init; }
        public int ReadinessRevisions { get; init; }
        public int CompletedClientOperations { get; init; }
        public int CommitOutboxEvents { get; init; }
        public string ProposalStatus { get; init; } = string.Empty;
        public long ProposalCurrentRevision { get; init; }
        public string LifecyclePhase { get; init; } = string.Empty;
        public string ExecutionReadiness { get; init; } = string.Empty;
        public string? LocalPath { get; init; }
    }

    private sealed class CommittedAuthorityState
    {
        public int ProposalRevisions { get; init; }
        public int AgentRuns { get; init; }
        public int ChatMessages { get; init; }
        public int RejectedClientOperations { get; init; }
        public int CommitOutboxEvents { get; init; }
        public int PermanentTickets { get; init; }
        public string ProposalStatus { get; init; } = string.Empty;
        public long ProposalCurrentRevision { get; init; }
    }

    private sealed class CommittedMaterializationRaceState
    {
        public int ProposalRevisions { get; init; }
        public int Commitments { get; init; }
        public int PermanentTickets { get; init; }
        public int AssistantMessages { get; init; }
        public int MaterializationEvents { get; init; }
        public int RunsWithVisibleOutput { get; init; }
        public int RunsWithMaterializedRevision { get; init; }
        public string ProposalStatus { get; init; } = string.Empty;
        public long ProposalCurrentRevision { get; init; }
    }

    private sealed class ReadinessProjectionState
    {
        public int Revisions { get; init; }
        public long CurrentRevision { get; init; }
        public string CurrentState { get; init; } = string.Empty;
    }
}
