using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class WorkbenchTicketProposalReviewApiTests : ApiTestBase
{
    [TestMethod]
    public async Task Regenerate_StartsOneTrustedPurposeRunAgainstTheExactSetRevision()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Proposal regeneration contract");
        var seeded = await SeedProposalSetAsync(client, project);
        var operationId = Guid.NewGuid();
        var request = new
        {
            project.WorkbenchSessionId,
            project.LeaseEpoch,
            clientOperationId = operationId,
            chatSessionId = seeded.ChatSessionId,
            expectedProposalSetRevision = 1,
            instruction = "Focus the set on the smallest independently testable outcomes."
        };

        var response = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}/regenerations",
            request);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<JsonElement>();
        var runId = run.GetProperty("agentRunId").GetGuid();
        Assert.AreEqual(
            WorkbenchAgentInvocationKinds.TicketProposalRegeneration,
            run.GetProperty("invocationKind").GetString());
        Assert.AreEqual(seeded.SetId, run.GetProperty("ticketProposalSetId").GetGuid());
        Assert.AreEqual(1L, run.GetProperty("ticketProposalRevision").GetInt64());
        Assert.IsFalse(run.GetProperty("isReplay").GetBoolean());

        var replayResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}/regenerations",
            request);
        Assert.AreEqual(HttpStatusCode.Accepted, replayResponse.StatusCode);
        var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(runId, replay.GetProperty("agentRunId").GetGuid());
        Assert.IsTrue(replay.GetProperty("isReplay").GetBoolean());

        var stale = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}/regenerations",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId = seeded.ChatSessionId,
                expectedProposalSetRevision = 99,
                instruction = "This revision is stale."
            });
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        await AssertErrorAsync(stale, TicketProposalRevisionConflictException.ErrorCode);

        var foreignProject = await StartProjectAsync(client, "Foreign regeneration route");
        var concealed = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{foreignProject.ProjectId}/ticket-proposal-sets/{seeded.SetId}/regenerations",
            new
            {
                foreignProject.WorkbenchSessionId,
                foreignProject.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId = seeded.ChatSessionId,
                expectedProposalSetRevision = 1,
                instruction = "Do not reveal a foreign proposal set."
            });
        Assert.AreEqual(HttpStatusCode.NotFound, concealed.StatusCode);
        await AssertErrorAsync(concealed, "ticket_proposal_set_not_found");

        await using var connection = new SqlConnection(ConnectionString);
        var state = await connection.QuerySingleAsync<RegenerationAuditState>(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE ClientOperationId=@ClientOperationId) AS Runs,
                (SELECT MAX(InvocationKind) FROM dbo.WorkbenchAgentRuns
                 WHERE ClientOperationId=@ClientOperationId) AS InvocationKind,
                (SELECT TOP (1) TicketProposalSetId FROM dbo.WorkbenchAgentRuns
                 WHERE ClientOperationId=@ClientOperationId) AS TicketProposalSetId,
                (SELECT MAX(TicketProposalRevision) FROM dbo.WorkbenchAgentRuns
                 WHERE ClientOperationId=@ClientOperationId) AS TicketProposalRevision,
                (SELECT MAX(message.Message)
                 FROM dbo.WorkbenchAgentRuns agentRun
                 INNER JOIN dbo.ChatMessages message
                    ON message.Id=agentRun.SourceUserMessageId
                 WHERE agentRun.ClientOperationId=@ClientOperationId) AS SourceMessage,
                (SELECT COUNT(1) FROM dbo.ProjectTickets WHERE ProjectId=@ProjectId) AS PermanentTickets;
            """,
            new { ClientOperationId = operationId, project.ProjectId });
        Assert.AreEqual(1, state.Runs);
        Assert.AreEqual(WorkbenchAgentInvocationKinds.TicketProposalRegeneration, state.InvocationKind);
        Assert.AreEqual(seeded.SetId, state.TicketProposalSetId);
        Assert.AreEqual(1L, state.TicketProposalRevision);
        Assert.AreEqual(
            "/ticket Focus the set on the smallest independently testable outcomes.",
            state.SourceMessage);
        Assert.AreEqual(0, state.PermanentTickets);
    }

    [TestMethod]
    public async Task Review_IsFencedRevisionedIdempotentAndNeverCreatesPermanentTickets()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Proposal review contract");
        var seeded = await SeedProposalSetAsync(client, project);

        var currentResponse = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/current" +
            $"?workbenchSessionId={project.WorkbenchSessionId}&leaseEpoch={project.LeaseEpoch}");
        Assert.AreEqual(HttpStatusCode.OK, currentResponse.StatusCode);
        var current = await currentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(seeded.SetId, current.GetProperty("ticketProposalSetId").GetGuid());
        Assert.AreEqual(1L, current.GetProperty("revision").GetInt64());
        Assert.AreEqual(3, current.GetProperty("proposals").GetArrayLength());

        var editOperationId = Guid.NewGuid();
        var editPayload = new
        {
            project.WorkbenchSessionId,
            project.LeaseEpoch,
            clientOperationId = editOperationId,
            expectedProposalSetRevision = 1,
            title = "  User-edited sign-in  ",
            problem = "  People cannot enter their workspace.  ",
            proposedChange = "  Add a secure sign-in journey.  ",
            acceptanceCriteria = new[] { " Valid credentials open the workspace. " }
        };
        var editResponse = await client.PatchAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"/proposals/{seeded.FirstProposalId}",
            editPayload);
        Assert.AreEqual(HttpStatusCode.OK, editResponse.StatusCode);
        var edited = await editResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(2L, edited.GetProperty("proposalSet").GetProperty("revision").GetInt64());
        Assert.AreEqual(
            "User-edited sign-in",
            edited.GetProperty("proposalSet").GetProperty("proposals")[0]
                .GetProperty("title").GetString());
        Assert.IsFalse(edited.GetProperty("isReplay").GetBoolean());

        var replayResponse = await client.PatchAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"/proposals/{seeded.FirstProposalId}",
            editPayload);
        Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(replay.GetProperty("isReplay").GetBoolean());
        Assert.AreEqual(2L, replay.GetProperty("proposalSet").GetProperty("revision").GetInt64());

        var mismatchResponse = await client.PatchAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"/proposals/{seeded.FirstProposalId}",
            editPayload with { title = "Changed operation payload" });
        Assert.AreEqual(HttpStatusCode.Conflict, mismatchResponse.StatusCode);
        await AssertErrorAsync(mismatchResponse, ProjectStartOperationMismatchException.ErrorCode);

        var dependencyReorder = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}/reorder",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedProposalSetRevision = 2,
                orderedProposalIds = new[]
                {
                    seeded.SecondProposalId,
                    seeded.FirstProposalId,
                    seeded.ThirdProposalId
                }
            });
        Assert.AreEqual(HttpStatusCode.Conflict, dependencyReorder.StatusCode);
        await AssertErrorAsync(dependencyReorder, TicketProposalDependencyException.ErrorCode);

        var reorderResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}/reorder",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedProposalSetRevision = 2,
                orderedProposalIds = new[]
                {
                    seeded.ThirdProposalId,
                    seeded.FirstProposalId,
                    seeded.SecondProposalId
                }
            });
        Assert.AreEqual(HttpStatusCode.OK, reorderResponse.StatusCode);
        var reordered = await reorderResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(
            seeded.ThirdProposalId,
            reordered.GetProperty("proposalSet").GetProperty("proposals")[0]
                .GetProperty("ticketProposalId").GetGuid());
        Assert.AreEqual(3L, reordered.GetProperty("proposalSet").GetProperty("revision").GetInt64());

        var incompleteReorder = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}/reorder",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedProposalSetRevision = 3,
                orderedProposalIds = new[] { seeded.FirstProposalId }
            });
        Assert.AreEqual(HttpStatusCode.BadRequest, incompleteReorder.StatusCode);
        await AssertErrorAsync(incompleteReorder, "ticket_proposal_invalid");

        var dependencyRemoval = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"/proposals/{seeded.FirstProposalId}/remove",
            MutationPayload(project, expectedRevision: 3));
        Assert.AreEqual(HttpStatusCode.Conflict, dependencyRemoval.StatusCode);
        await AssertErrorAsync(dependencyRemoval, TicketProposalDependencyException.ErrorCode);

        var removeResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"/proposals/{seeded.ThirdProposalId}/remove",
            MutationPayload(project, expectedRevision: 3));
        Assert.AreEqual(HttpStatusCode.OK, removeResponse.StatusCode);
        var removed = await removeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(4L, removed.GetProperty("proposalSet").GetProperty("revision").GetInt64());
        Assert.AreEqual(2, removed.GetProperty("proposalSet").GetProperty("proposals").GetArrayLength());

        var resolveResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"/issues/{seeded.QuestionId}/resolve",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedProposalSetRevision = 4,
                resolution = "Use email and password for this first slice."
            });
        Assert.AreEqual(HttpStatusCode.OK, resolveResponse.StatusCode);
        var resolved = await resolveResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(5L, resolved.GetProperty("proposalSet").GetProperty("revision").GetInt64());
        Assert.AreEqual(
            TicketProposalIssueStatuses.Resolved,
            resolved.GetProperty("proposalSet").GetProperty("openQuestions")[0]
                .GetProperty("status").GetString());

        var repeatedResolution = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"/issues/{seeded.QuestionId}/resolve",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedProposalSetRevision = 5,
                resolution = "Try to resolve the same issue again."
            });
        Assert.AreEqual(HttpStatusCode.Conflict, repeatedResolution.StatusCode);
        await AssertErrorAsync(repeatedResolution, TicketProposalIssueNotOpenException.ErrorCode);

        var historyResponse = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}/history" +
            $"?workbenchSessionId={project.WorkbenchSessionId}&leaseEpoch={project.LeaseEpoch}");
        Assert.AreEqual(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(5, history.GetArrayLength());
        Assert.AreEqual(5L, history[0].GetProperty("revision").GetInt64());
        Assert.AreEqual(TicketProposalRevisionChangeKinds.IssueResolved,
            history[0].GetProperty("changeKind").GetString());
        Assert.IsTrue(history[0].GetProperty("actorUserId").GetInt32() > 0);
        Assert.AreEqual(5L, history[0].GetProperty("proposalSet").GetProperty("revision").GetInt64());

        var staleEdit = await client.PatchAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"/proposals/{seeded.FirstProposalId}",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedProposalSetRevision = 4,
                title = "Stale",
                problem = "Stale",
                proposedChange = "Stale",
                acceptanceCriteria = new[] { "Stale" }
            });
        Assert.AreEqual(HttpStatusCode.Conflict, staleEdit.StatusCode);
        await AssertErrorAsync(staleEdit, TicketProposalRevisionConflictException.ErrorCode);

        var secondProject = await StartProjectAsync(client, "Foreign proposal route");
        var concealed = await client.GetAsync(
            $"/api/workbench/projects/{secondProject.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"?workbenchSessionId={secondProject.WorkbenchSessionId}&leaseEpoch={secondProject.LeaseEpoch}");
        Assert.AreEqual(HttpStatusCode.NotFound, concealed.StatusCode);
        await AssertErrorAsync(concealed, "ticket_proposal_set_not_found");

        await using var connection = new SqlConnection(ConnectionString);
        var audit = await connection.QuerySingleAsync<ReviewAuditState>(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.TicketProposalSetRevisions
                 WHERE TicketProposalSetId=@TicketProposalSetId) AS Revisions,
                (SELECT COUNT(1) FROM dbo.ProjectTickets WHERE ProjectId=@ProjectId) AS PermanentTickets,
                (SELECT TOP (1) Phase FROM dbo.ProjectLifecyclePhases
                 WHERE ProjectId=@ProjectId ORDER BY Revision DESC) AS LifecyclePhase,
                (SELECT TOP (1) ExecutionReadiness FROM dbo.ProjectReadinessAssessments
                 WHERE ProjectId=@ProjectId ORDER BY Revision DESC) AS ExecutionReadiness,
                (SELECT COUNT(1) FROM dbo.Projects
                 WHERE Id=@ProjectId AND LocalPath IS NOT NULL) AS RepositoryBindings,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                 WHERE ProjectId=@ProjectId AND EventKind=N'TicketProposalSetReviewed') AS ReviewEvents;
            """,
            new { TicketProposalSetId = seeded.SetId, project.ProjectId });
        Assert.AreEqual(5, audit.Revisions);
        Assert.AreEqual(0, audit.PermanentTickets);
        Assert.AreEqual("Shaping", audit.LifecyclePhase);
        Assert.AreEqual("NotConfigured", audit.ExecutionReadiness);
        Assert.AreEqual(0, audit.RepositoryBindings);
        Assert.AreEqual(4, audit.ReviewEvents);
    }

    [TestMethod]
    public async Task ReviewMutation_RequiresTheCurrentHolderLeaseBeforeAnyWrite()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Proposal review fence");
        var seeded = await SeedProposalSetAsync(client, project);
        var operationId = Guid.NewGuid();

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

        var response = await client.PatchAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/ticket-proposal-sets/{seeded.SetId}" +
            $"/proposals/{seeded.FirstProposalId}",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = operationId,
                expectedProposalSetRevision = 1,
                title = "Blocked",
                problem = "Blocked",
                proposedChange = "Blocked",
                acceptanceCriteria = new[] { "Blocked" }
            });
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorAsync(response, WorkbenchLeaseFenceException.ErrorCode);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.TicketProposalSetRevisions WHERE TicketProposalSetId=@SetId;",
                new { seeded.SetId }));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.ClientOperations WHERE ClientOperationId=@OperationId;",
                new { OperationId = operationId }));
        }
    }

    [TestMethod]
    public async Task ProposalSchema_RejectsCrossProjectRevisionParentsAndUnknownUnderstandingRevisions()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Proposal schema parent scope");
        var otherProject = await StartProjectAsync(client, "Other proposal schema scope");
        var seeded = await SeedProposalSetAsync(client, project);

        await using var connection = new SqlConnection(ConnectionString);
        var storedJson = await connection.QuerySingleAsync<string>(
            """
            SELECT SnapshotJson
            FROM dbo.TicketProposalSetRevisions
            WHERE TenantId=1 AND ProjectId=@ProjectId
              AND TicketProposalSetId=@SetId AND Revision=1;
            """,
            new { project.ProjectId, seeded.SetId });
        var stored = TicketProposalSetDocumentCodec.Deserialize(storedJson);
        var crossProjectRevision = stored with
        {
            ProjectId = otherProject.ProjectId,
            WorkbenchSessionId = otherProject.WorkbenchSessionId,
            LeaseEpoch = otherProject.LeaseEpoch,
            Revision = 2,
            UpdatedAtUtc = stored.UpdatedAtUtc.AddTicks(1)
        };
        var crossProjectJson = TicketProposalSetDocumentCodec.Serialize(crossProjectRevision);

        var crossProjectError = await Assert.ThrowsExceptionAsync<SqlException>(() =>
            connection.ExecuteAsync(
                """
                INSERT dbo.TicketProposalSetRevisions
                    (TenantId, ProjectId, TicketProposalSetId, Revision, SnapshotJson,
                     SnapshotHash, ActorUserId, AgentRunId, ChangeKind)
                VALUES
                    (1, @OtherProjectId, @SetId, 2, @SnapshotJson,
                     @SnapshotHash, 1, NULL, N'Edited');
                """,
                new
                {
                    OtherProjectId = otherProject.ProjectId,
                    seeded.SetId,
                    SnapshotJson = crossProjectJson,
                    SnapshotHash = TicketProposalSetDocumentCodec.ComputeHash(crossProjectJson)
                }));
        Assert.AreEqual(547, crossProjectError.Number);

        var understandingError = await Assert.ThrowsExceptionAsync<SqlException>(() =>
            connection.ExecuteAsync(
                """
                UPDATE dbo.TicketProposalSets
                SET BasedOnUnderstandingRevision=999999
                WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SetId;
                """,
                new { project.ProjectId, seeded.SetId }));
        Assert.AreEqual(547, understandingError.Number);
        Assert.AreEqual(1L, await connection.QuerySingleAsync<long>(
            """
            SELECT BasedOnUnderstandingRevision
            FROM dbo.TicketProposalSets
            WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SetId;
            """,
            new { project.ProjectId, seeded.SetId }));
    }

    [TestMethod]
    public async Task ProposalReads_FailClosedWhenARehashedSnapshotHasTheWrongAuthorizedIdentity()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Proposal snapshot identity");
        var seeded = await SeedProposalSetAsync(client, project);

        await using var connection = new SqlConnection(ConnectionString);
        var originalJson = await connection.QuerySingleAsync<string>(
            """
            SELECT SnapshotJson
            FROM dbo.TicketProposalSetRevisions
            WHERE TenantId=1 AND ProjectId=@ProjectId
              AND TicketProposalSetId=@SetId AND Revision=1;
            """,
            new { project.ProjectId, seeded.SetId });
        var original = TicketProposalSetDocumentCodec.Deserialize(originalJson);

        async Task StoreSnapshotAsync(TicketProposalSetDocument document)
        {
            var json = TicketProposalSetDocumentCodec.Serialize(document);
            await connection.ExecuteAsync(
                "DISABLE TRIGGER dbo.trg_TicketProposalSetRevisions_AppendOnly ON dbo.TicketProposalSetRevisions;");
            try
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE dbo.TicketProposalSetRevisions
                    SET SnapshotJson=@SnapshotJson, SnapshotHash=@SnapshotHash
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND TicketProposalSetId=@SetId AND Revision=1;
                    """,
                    new
                    {
                        project.ProjectId,
                        seeded.SetId,
                        SnapshotJson = json,
                        SnapshotHash = TicketProposalSetDocumentCodec.ComputeHash(json)
                    });
            }
            finally
            {
                await connection.ExecuteAsync(
                    "ENABLE TRIGGER dbo.trg_TicketProposalSetRevisions_AppendOnly ON dbo.TicketProposalSetRevisions;");
            }
        }

        using var scope = Factory.Services.CreateScope();
        var proposals = scope.ServiceProvider.GetRequiredService<IWorkbenchTicketProposalService>();

        await StoreSnapshotAsync(original with { TicketProposalSetId = Guid.NewGuid() });
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            proposals.GetCurrentAsync(
                1, 1, project.ProjectId, project.WorkbenchSessionId, project.LeaseEpoch));

        await StoreSnapshotAsync(original with { ProjectId = checked(project.ProjectId + 1_000_000) });
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            proposals.GetAsync(
                1, 1, project.ProjectId, project.WorkbenchSessionId, project.LeaseEpoch,
                seeded.SetId));

        await StoreSnapshotAsync(original with
        {
            WorkbenchSessionId = checked(project.WorkbenchSessionId + 1),
            LeaseEpoch = checked(project.LeaseEpoch + 1)
        });
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            proposals.GetHistoryAsync(
                1, 1, project.ProjectId, project.WorkbenchSessionId, project.LeaseEpoch,
                seeded.SetId));

        await StoreSnapshotAsync(original);
    }

    private static object MutationPayload(StartedProject project, long expectedRevision) => new
    {
        project.WorkbenchSessionId,
        project.LeaseEpoch,
        clientOperationId = Guid.NewGuid(),
        expectedProposalSetRevision = expectedRevision
    };

    private static async Task AssertErrorAsync(HttpResponseMessage response, string expectedError)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(expectedError, body.GetProperty("error").GetString());
    }

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
        StartedProject project)
    {
        long chatSessionId;
        await using (var connection = new SqlConnection(ConnectionString))
        {
            chatSessionId = await connection.QuerySingleAsync<long>(
                """
                INSERT dbo.ProjectChatSessions(TenantId, ProjectId, Title)
                OUTPUT inserted.Id
                VALUES (1, @ProjectId, N'Proposal review seed');
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
                composerText = "Capture this source turn for proposal review tests."
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
        var questionId = Guid.NewGuid();
        var conflictId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var document = new TicketProposalSetDocument(
            setId,
            project.ProjectId,
            project.WorkbenchSessionId,
            project.LeaseEpoch,
            Revision: 1,
            BasedOnUnderstandingRevision: 1,
            TicketProposalSetStatuses.Ready,
            "Three independent user-visible outcomes.",
            [
                new TicketProposalDocument(
                    firstId,
                    "Sign in",
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
            ],
            [new TicketProposalIssueDocument(
                questionId,
                TicketProposalIssueKinds.Question,
                "Which sign-in identifier should v0.1 use?",
                TicketProposalIssueStatuses.Open,
                null,
                [sourceMessageId])],
            [new TicketProposalIssueDocument(
                conflictId,
                TicketProposalIssueKinds.Conflict,
                "Two identity providers were mentioned.",
                TicketProposalIssueStatuses.Open,
                null,
                [sourceMessageId])],
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
                     1, N'Ready', @SplitReason, @SourceMessageIdsJson,
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
                    document.SplitReason,
                    SourceMessageIdsJson = JsonSerializer.Serialize(document.SourceMessageIds),
                    AgentRunId = agentRunId,
                    document.CreatedAtUtc,
                    document.UpdatedAtUtc,
                    SnapshotJson = snapshotJson,
                    SnapshotHash = snapshotHash
                });
        }

        return new SeededProposalSet(setId, firstId, secondId, thirdId, questionId, chatSessionId);
    }

    private sealed record StartedProject(int ProjectId, long WorkbenchSessionId, long LeaseEpoch);
    private sealed record SeededProposalSet(
        Guid SetId,
        Guid FirstProposalId,
        Guid SecondProposalId,
        Guid ThirdProposalId,
        Guid QuestionId,
        long ChatSessionId);

    private sealed class RegenerationAuditState
    {
        public int Runs { get; init; }
        public string InvocationKind { get; init; } = string.Empty;
        public Guid? TicketProposalSetId { get; init; }
        public long? TicketProposalRevision { get; init; }
        public string SourceMessage { get; init; } = string.Empty;
        public int PermanentTickets { get; init; }
    }

    private sealed class ReviewAuditState
    {
        public int Revisions { get; init; }
        public int PermanentTickets { get; init; }
        public string LifecyclePhase { get; init; } = string.Empty;
        public string ExecutionReadiness { get; init; } = string.Empty;
        public int RepositoryBindings { get; init; }
        public int ReviewEvents { get; init; }
    }
}
