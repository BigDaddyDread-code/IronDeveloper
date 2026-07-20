using System.Net;
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
public sealed class WorkbenchAgentRunApiTests : ApiTestBase
{
    [TestMethod]
    public async Task SubmitStatusAndCancel_AreAuthenticatedScopedAndIdempotent()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var startOperationId = Guid.NewGuid();
        var startResponse = await client.PostAsJsonAsync("/api/projects/start", new
        {
            clientOperationId = startOperationId,
            name = "Agent run API contract"
        });
        Assert.AreEqual(HttpStatusCode.Created, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = start.GetProperty("projectId").GetInt32();
        var workbenchSessionId = start.GetProperty("workbenchSessionId").GetInt64();
        var leaseEpoch = start.GetProperty("leaseEpoch").GetInt64();

        var projectReadinessResponse = await client.GetAsync(
            $"/api/workbench/projects/{projectId}/agent-runs/current" +
            $"?workbenchSessionId={workbenchSessionId}&leaseEpoch={leaseEpoch}");
        Assert.AreEqual(HttpStatusCode.OK, projectReadinessResponse.StatusCode);
        var projectReadiness = await projectReadinessResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(projectReadiness.GetProperty("submissionAvailable").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, projectReadiness.GetProperty("boundChatSessionId").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, projectReadiness.GetProperty("activeRun").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, projectReadiness.GetProperty("latestRun").ValueKind);

        long chatSessionId;
        await using (var connection = new SqlConnection(ConnectionString))
        {
            chatSessionId = await connection.QuerySingleAsync<long>("""
                INSERT dbo.ProjectChatSessions(TenantId, ProjectId, Title)
                OUTPUT inserted.Id
                VALUES (1, @ProjectId, N'Agent run API');
                """, new { ProjectId = projectId });
        }

        DateTime leaseExpiryBeforeRecovery;
        await using (var connection = new SqlConnection(ConnectionString))
        {
            leaseExpiryBeforeRecovery = await connection.QuerySingleAsync<DateTime>(
                """
                SELECT ExpiresAtUtc FROM dbo.WorkbenchWriteLeases
                WHERE TenantId=1 AND ProjectId=@ProjectId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                """,
                new { ProjectId = projectId, WorkbenchSessionId = workbenchSessionId, LeaseEpoch = leaseEpoch });
        }

        var initialRecoveryResponse = await client.GetAsync(
            $"/api/workbench/projects/{projectId}/agent-runs/current" +
            $"?workbenchSessionId={workbenchSessionId}&leaseEpoch={leaseEpoch}&chatSessionId={chatSessionId}");
        Assert.AreEqual(HttpStatusCode.OK, initialRecoveryResponse.StatusCode);
        var initialRecovery = await initialRecoveryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(initialRecovery.GetProperty("submissionAvailable").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, initialRecovery.GetProperty("boundChatSessionId").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, initialRecovery.GetProperty("activeRun").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, initialRecovery.GetProperty("latestRun").ValueKind);
        await using (var connection = new SqlConnection(ConnectionString))
        {
            var leaseExpiryAfterRecovery = await connection.QuerySingleAsync<DateTime>(
                """
                SELECT ExpiresAtUtc FROM dbo.WorkbenchWriteLeases
                WHERE TenantId=1 AND ProjectId=@ProjectId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                """,
                new { ProjectId = projectId, WorkbenchSessionId = workbenchSessionId, LeaseEpoch = leaseEpoch });
            Assert.AreEqual(leaseExpiryBeforeRecovery, leaseExpiryAfterRecovery);
        }

        var submitOperationId = Guid.NewGuid();
        var payload = new
        {
            workbenchSessionId,
            leaseEpoch,
            clientOperationId = submitOperationId,
            chatSessionId,
            message = "Shape this project through the durable operation path."
        };
        var submittedResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{projectId}/agent-runs",
            payload);
        Assert.AreEqual(HttpStatusCode.Accepted, submittedResponse.StatusCode);
        var submitted = await submittedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var agentRunId = submitted.GetProperty("agentRunId").GetGuid();
        Assert.AreEqual("Pending", submitted.GetProperty("status").GetString());
        Assert.IsFalse(submitted.TryGetProperty("contextSnapshotJson", out _));
        Assert.IsFalse(submitted.TryGetProperty("validatedOutputJson", out _));
        Assert.IsFalse(submitted.TryGetProperty("diagnosticHash", out _));

        var replayResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{projectId}/agent-runs",
            payload);
        Assert.AreEqual(HttpStatusCode.Accepted, replayResponse.StatusCode);
        var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(agentRunId, replay.GetProperty("agentRunId").GetGuid());
        Assert.IsTrue(replay.GetProperty("isReplay").GetBoolean());

        var concurrentResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{projectId}/agent-runs",
            new
            {
                workbenchSessionId,
                leaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                message = "A second active turn must wait."
            });
        Assert.AreEqual(HttpStatusCode.Conflict, concurrentResponse.StatusCode);
        var concurrent = await concurrentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("workbench_agent_run_active", concurrent.GetProperty("error").GetString());
        Assert.AreEqual(agentRunId, concurrent.GetProperty("agentRunId").GetGuid());

        var recoveryResponse = await client.GetAsync(
            $"/api/workbench/projects/{projectId}/agent-runs/current" +
            $"?workbenchSessionId={workbenchSessionId}&leaseEpoch={leaseEpoch}&chatSessionId={chatSessionId}");
        Assert.AreEqual(HttpStatusCode.OK, recoveryResponse.StatusCode);
        var recovery = await recoveryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(chatSessionId, recovery.GetProperty("boundChatSessionId").GetInt64());
        Assert.AreEqual(agentRunId, recovery.GetProperty("activeRun").GetProperty("agentRunId").GetGuid());

        var otherChatSessionId = await CreateChatSessionAsync(projectId, "Different Workbench conversation");
        var switchedChatResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{projectId}/agent-runs",
            new
            {
                workbenchSessionId,
                leaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId = otherChatSessionId,
                message = "Do not switch this Workbench session to another chat."
            });
        Assert.AreEqual(HttpStatusCode.Conflict, switchedChatResponse.StatusCode);
        var switchedChat = await switchedChatResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("workbench_chat_session_mismatch", switchedChat.GetProperty("error").GetString());

        var mismatchResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{projectId}/agent-runs",
            new
            {
                workbenchSessionId,
                leaseEpoch,
                clientOperationId = submitOperationId,
                chatSessionId,
                message = "Changed payload"
            });
        Assert.AreEqual(HttpStatusCode.Conflict, mismatchResponse.StatusCode);
        var mismatch = await mismatchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("operation_id_payload_mismatch", mismatch.GetProperty("error").GetString());

        var statusResponse = await client.GetAsync(
            $"/api/workbench/projects/{projectId}/agent-runs/{agentRunId}");
        Assert.AreEqual(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Pending", status.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, status.GetProperty("failureCategory").ValueKind);
        Assert.IsFalse(status.GetProperty("retryable").GetBoolean());
        Assert.IsFalse(status.TryGetProperty("contextSnapshotJson", out _));
        Assert.IsFalse(status.TryGetProperty("diagnosticCode", out _));
        Assert.IsFalse(status.TryGetProperty("diagnosticHash", out _));

        var cancelOperationId = Guid.NewGuid();
        var cancelPayload = new { workbenchSessionId, leaseEpoch, clientOperationId = cancelOperationId };
        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{projectId}/agent-runs/{agentRunId}/cancel",
            cancelPayload);
        Assert.AreEqual(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.IsTrue(cancelled.GetProperty("cancellationRequested").GetBoolean());

        var cancelReplayResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{projectId}/agent-runs/{agentRunId}/cancel",
            cancelPayload);
        Assert.AreEqual(HttpStatusCode.OK, cancelReplayResponse.StatusCode);
        var cancelReplay = await cancelReplayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(cancelReplay.GetProperty("isReplay").GetBoolean());

        var terminalRecoveryResponse = await client.GetAsync(
            $"/api/workbench/projects/{projectId}/agent-runs/current" +
            $"?workbenchSessionId={workbenchSessionId}&leaseEpoch={leaseEpoch}&chatSessionId={chatSessionId}");
        Assert.AreEqual(HttpStatusCode.OK, terminalRecoveryResponse.StatusCode);
        var terminalRecovery = await terminalRecoveryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(chatSessionId, terminalRecovery.GetProperty("boundChatSessionId").GetInt64());
        Assert.AreEqual(JsonValueKind.Null, terminalRecovery.GetProperty("activeRun").ValueKind);
        Assert.AreEqual(agentRunId, terminalRecovery.GetProperty("latestRun").GetProperty("agentRunId").GetGuid());
        Assert.AreEqual("Cancelled", terminalRecovery.GetProperty("latestRun").GetProperty("status").GetString());

        var nextTurnResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{projectId}/agent-runs",
            new
            {
                workbenchSessionId,
                leaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                message = "The terminal run released the conversation slot."
            });
        Assert.AreEqual(HttpStatusCode.Accepted, nextTurnResponse.StatusCode);
    }

    [TestMethod]
    public async Task Submit_RequiresAuthentication()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/workbench/projects/123/agent-runs",
            new
            {
                workbenchSessionId = 1,
                leaseEpoch = 1,
                clientOperationId = Guid.NewGuid(),
                chatSessionId = 1,
                message = "unauthenticated"
            });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Submit_EnforcesTheProviderConversationMessageBoundaryBeforeWrites()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Agent run message boundary");
        var chatSessionId = await CreateChatSessionAsync(project.ProjectId, "Message boundary chat");

        var rejected = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                message = new string('x', WorkbenchBusinessAnalystProviderContract.MaximumConversationMessageCharacters + 1)
            });
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns WHERE ProjectId=@ProjectId;",
                new { project.ProjectId }));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.ChatMessages WHERE ProjectId=@ProjectId;",
                new { project.ProjectId }));
        }

        var accepted = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                message = new string('x', WorkbenchBusinessAnalystProviderContract.MaximumConversationMessageCharacters)
            });
        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    [TestMethod]
    public async Task WrongProjectRunAccess_IsConcealedAndForeignChatSessionIsRejected()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var sourceProject = await StartProjectAsync(client, "Agent run source project");
        var otherProject = await StartProjectAsync(client, "Agent run other project");
        var sourceChatSessionId = await CreateChatSessionAsync(sourceProject.ProjectId, "Source project chat");
        var agentRunId = await SubmitRunAsync(
            client,
            sourceProject,
            sourceChatSessionId,
            "Keep this run scoped to its source project.");

        var wrongProjectStatus = await client.GetAsync(
            $"/api/workbench/projects/{otherProject.ProjectId}/agent-runs/{agentRunId}");
        await AssertConcealedNotFoundAsync(wrongProjectStatus);

        var wrongProjectCancel = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{otherProject.ProjectId}/agent-runs/{agentRunId}/cancel",
            new
            {
                otherProject.WorkbenchSessionId,
                otherProject.LeaseEpoch,
                clientOperationId = Guid.NewGuid()
            });
        await AssertConcealedNotFoundAsync(wrongProjectCancel);

        var foreignChatSubmit = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{otherProject.ProjectId}/agent-runs",
            new
            {
                otherProject.WorkbenchSessionId,
                otherProject.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId = sourceChatSessionId,
                message = "This chat session belongs to another project."
            });
        Assert.AreEqual(HttpStatusCode.BadRequest, foreignChatSubmit.StatusCode);
        var foreignChatError = await foreignChatSubmit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("workbench_agent_run_invalid", foreignChatError.GetProperty("error").GetString());

        await using var connection = new SqlConnection(ConnectionString);
        var otherProjectRunCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns WHERE TenantId=1 AND ProjectId=@ProjectId;",
            new { otherProject.ProjectId });
        Assert.AreEqual(0, otherProjectRunCount);
    }

    [TestMethod]
    public async Task RevokedProjectMembership_ConcealsRunAndRejectsCancellation()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Revoked agent run project");
        var chatSessionId = await CreateChatSessionAsync(project.ProjectId, "Revoked project chat");
        var agentRunId = await SubmitRunAsync(
            client,
            project,
            chatSessionId,
            "This run must be inaccessible after membership revocation.");

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                UPDATE dbo.ProjectMembers
                SET Status=N'Removed', RemovedByUserId=UserId, RemovedUtc=SYSUTCDATETIME()
                WHERE TenantId=1 AND ProjectId=@ProjectId AND Status=N'Active';
                """,
                new { project.ProjectId });
        }

        var revokedStatus = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs/{agentRunId}");
        await AssertRevokedProjectConcealedAsync(revokedStatus);

        var revokedCurrent = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs/current" +
            $"?workbenchSessionId={project.WorkbenchSessionId}&leaseEpoch={project.LeaseEpoch}&chatSessionId={chatSessionId}");
        await AssertRevokedProjectConcealedAsync(revokedCurrent);

        var cancelOperationId = Guid.NewGuid();
        var revokedCancel = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs/{agentRunId}/cancel",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = cancelOperationId
            });
        await AssertRevokedProjectConcealedAsync(revokedCancel);

        await using var verify = new SqlConnection(ConnectionString);
        var state = await verify.QuerySingleAsync<RevokedRunState>(
            """
            SELECT run.Status,
                   (SELECT COUNT(1) FROM dbo.ClientOperations
                    WHERE ClientOperationId=@CancelOperationId) AS CancelOperations
            FROM dbo.WorkbenchAgentRuns run
            WHERE run.AgentRunId=@AgentRunId;
            """,
            new { AgentRunId = agentRunId, CancelOperationId = cancelOperationId });
        Assert.AreEqual("Pending", state.Status);
        Assert.AreEqual(0, state.CancelOperations);
    }

    [TestMethod]
    public async Task FailedSnapshot_MapsOnlyBoundedCategory_AndRetryIsFailClosed()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Safe failed run snapshot");
        var chatSessionId = await CreateChatSessionAsync(project.ProjectId, "Safe failed run chat");
        var agentRunId = await SubmitRunAsync(
            client,
            project,
            chatSessionId,
            "This source message must never be duplicated by an automatic retry.");

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                UPDATE dbo.WorkbenchAgentRuns
                SET Status=N'Failed', ActiveRunSlot=NULL,
                    DiagnosticCode=N'agent_context_too_large',
                    DiagnosticHash=REPLICATE('a', 64),
                    DiagnosticAtUtc=SYSUTCDATETIME(), CompletedAtUtc=SYSUTCDATETIME()
                WHERE AgentRunId=@AgentRunId AND Status=N'Pending';
                """,
                new { AgentRunId = agentRunId });
        }

        var statusResponse = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs/{agentRunId}");
        Assert.AreEqual(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("context_too_large", status.GetProperty("failureCategory").GetString());
        Assert.IsFalse(status.GetProperty("retryable").GetBoolean());
        Assert.IsFalse(status.TryGetProperty("diagnosticCode", out _));
        Assert.IsFalse(status.TryGetProperty("diagnosticHash", out _));

        var recoveryResponse = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs/current" +
            $"?workbenchSessionId={project.WorkbenchSessionId}&leaseEpoch={project.LeaseEpoch}&chatSessionId={chatSessionId}");
        Assert.AreEqual(HttpStatusCode.OK, recoveryResponse.StatusCode);
        var recovery = await recoveryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(JsonValueKind.Null, recovery.GetProperty("activeRun").ValueKind);
        Assert.AreEqual(agentRunId, recovery.GetProperty("latestRun").GetProperty("agentRunId").GetGuid());
        Assert.AreEqual("Failed", recovery.GetProperty("latestRun").GetProperty("status").GetString());
        Assert.AreEqual("context_too_large", recovery.GetProperty("latestRun").GetProperty("failureCategory").GetString());

        await using var verify = new SqlConnection(ConnectionString);
        var sourceMessages = await verify.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM dbo.ChatMessages message
            INNER JOIN dbo.WorkbenchAgentRuns run ON run.SourceUserMessageId=message.Id
            WHERE run.AgentRunId=@AgentRunId AND message.Role=N'user';
            """,
            new { AgentRunId = agentRunId });
        Assert.AreEqual(1, sourceMessages);
    }

    [TestMethod]
    public async Task UnavailableSubmission_FailsBeforeAnyMessageRunOrOutboxWrite()
    {
        using var unavailableFactory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWorkbenchAgentRunSubmissionAvailability>();
                services.AddSingleton<IWorkbenchAgentRunSubmissionAvailability,
                    UnavailableWorkbenchAgentRunSubmissionAvailability>();
            }));
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = unavailableFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var project = await StartProjectAsync(client, "Unavailable agent run");

        var projectOnlyRecoveryResponse = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs/current" +
            $"?workbenchSessionId={project.WorkbenchSessionId}&leaseEpoch={project.LeaseEpoch}");
        Assert.AreEqual(HttpStatusCode.OK, projectOnlyRecoveryResponse.StatusCode);
        var projectOnlyRecovery = await projectOnlyRecoveryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(projectOnlyRecovery.GetProperty("submissionAvailable").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, projectOnlyRecovery.GetProperty("boundChatSessionId").ValueKind);

        var chatSessionId = await CreateChatSessionAsync(project.ProjectId, "Unavailable run chat");

        await using var connection = new SqlConnection(ConnectionString);
        var messagesBefore = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.ChatMessages WHERE ProjectId=@ProjectId;",
            new { project.ProjectId });
        var runsBefore = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns WHERE ProjectId=@ProjectId;",
            new { project.ProjectId });
        var outboxBefore = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents WHERE ProjectId=@ProjectId;",
            new { project.ProjectId });

        var recoveryResponse = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs/current" +
            $"?workbenchSessionId={project.WorkbenchSessionId}&leaseEpoch={project.LeaseEpoch}&chatSessionId={chatSessionId}");
        Assert.AreEqual(HttpStatusCode.OK, recoveryResponse.StatusCode);
        var recovery = await recoveryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(recovery.GetProperty("submissionAvailable").GetBoolean());
        Assert.AreEqual("service_unavailable", recovery.GetProperty("unavailableCategory").GetString());
        Assert.AreEqual(JsonValueKind.Null, recovery.GetProperty("activeRun").ValueKind);

        var staleFenceResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs",
            new
            {
                project.WorkbenchSessionId,
                leaseEpoch = project.LeaseEpoch + 1,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                message = "Availability must not bypass the exact lease fence."
            });
        Assert.AreEqual(HttpStatusCode.Conflict, staleFenceResponse.StatusCode);
        var staleFence = await staleFenceResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("workbench_lease_fence_rejected", staleFence.GetProperty("error").GetString());

        var response = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                message = "Do not persist this unavailable turn."
            });
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("workbench_agent_run_unavailable", error.GetProperty("error").GetString());
        Assert.AreEqual("service_unavailable", error.GetProperty("failureCategory").GetString());
        Assert.IsFalse(error.GetProperty("retryable").GetBoolean());

        Assert.AreEqual(messagesBefore, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.ChatMessages WHERE ProjectId=@ProjectId;",
            new { project.ProjectId }));
        Assert.AreEqual(runsBefore, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns WHERE ProjectId=@ProjectId;",
            new { project.ProjectId }));
        Assert.AreEqual(outboxBefore, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents WHERE ProjectId=@ProjectId;",
            new { project.ProjectId }));

        using var availableClient = GetAuthedClient(token);
        var replayOperationId = Guid.NewGuid();
        var replayPayload = new
        {
            project.WorkbenchSessionId,
            project.LeaseEpoch,
            clientOperationId = replayOperationId,
            chatSessionId,
            message = "An authoritative receipt must remain recoverable after availability changes."
        };
        var accepted = await availableClient.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs",
            replayPayload);
        Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode);
        var acceptedBody = await accepted.Content.ReadFromJsonAsync<JsonElement>();

        var replay = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs",
            replayPayload);
        Assert.AreEqual(HttpStatusCode.Accepted, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(
            acceptedBody.GetProperty("agentRunId").GetGuid(),
            replayBody.GetProperty("agentRunId").GetGuid());
        Assert.IsTrue(replayBody.GetProperty("isReplay").GetBoolean());
        Assert.AreEqual(messagesBefore + 1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.ChatMessages WHERE ProjectId=@ProjectId;",
            new { project.ProjectId }));
        Assert.AreEqual(runsBefore + 1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns WHERE ProjectId=@ProjectId;",
            new { project.ProjectId }));
    }

    [TestMethod]
    public async Task ConversationAuthorityCutover_BlocksLegacyMessageAndCompletionMutationsOnly()
    {
        using var cutoverFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WorkbenchV2:Enabled", "true");
            builder.UseSetting("WorkbenchV2:ConversationAuthorityEnabled", "true");
        });
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = cutoverFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var environmentResponse = await client.GetAsync("/api/environment");
        Assert.AreEqual(HttpStatusCode.OK, environmentResponse.StatusCode);
        var environment = await environmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(
            environment.GetProperty("workbench").GetProperty("conversationAuthorityEnabled").GetBoolean());

        var project = await StartProjectAsync(client, "Conversation cutover gate");
        var sessionResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/chat/sessions",
            new
            {
                id = (long?)null,
                projectId = project.ProjectId,
                title = "Cutover chat",
                summary = (string?)null,
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid()
            });
        Assert.AreEqual(HttpStatusCode.OK, sessionResponse.StatusCode);
        var chatSessionId = await sessionResponse.Content.ReadFromJsonAsync<long>();

        var messageResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/chat/sessions/{chatSessionId}/messages",
            new
            {
                projectId = project.ProjectId,
                chatSessionId,
                role = "user",
                message = "Legacy mutation must be rejected.",
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid()
            });
        await AssertConversationAuthorityRequiredAsync(messageResponse);

        var completionResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/chat/complete",
            new
            {
                projectId = project.ProjectId,
                sessionId = chatSessionId,
                prompt = "Legacy completion must be rejected.",
                mode = "projectQuestion",
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid()
            });
        await AssertConversationAuthorityRequiredAsync(completionResponse);

        var messagesRead = await client.GetAsync(
            $"/api/projects/{project.ProjectId}/chat/sessions/{chatSessionId}/messages");
        Assert.AreEqual(HttpStatusCode.OK, messagesRead.StatusCode);
        var sessionsRead = await client.GetAsync($"/api/projects/{project.ProjectId}/chat/sessions");
        Assert.AreEqual(HttpStatusCode.OK, sessionsRead.StatusCode);
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

    private static async Task<Guid> SubmitRunAsync(
        HttpClient client,
        StartedProject project,
        long chatSessionId,
        string message)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                message
            });
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("agentRunId").GetGuid();
    }

    private static async Task AssertConcealedNotFoundAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("agent_run_not_found", body.GetProperty("error").GetString());
    }

    private static async Task AssertRevokedProjectConcealedAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
        Assert.IsFalse(body.TryGetProperty("agentRunId", out _));
    }

    private static async Task AssertConversationAuthorityRequiredAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("workbench_conversation_authority_required", body.GetProperty("error").GetString());
    }

    private static async Task<long> CreateChatSessionAsync(int projectId, string title)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.QuerySingleAsync<long>(
            """
            INSERT dbo.ProjectChatSessions(TenantId, ProjectId, Title)
            OUTPUT inserted.Id
            VALUES (1, @ProjectId, @Title);
            """,
            new { ProjectId = projectId, Title = title });
    }

    private sealed record StartedProject(int ProjectId, long WorkbenchSessionId, long LeaseEpoch);

    private sealed class RevokedRunState
    {
        public string Status { get; init; } = string.Empty;
        public int CancelOperations { get; init; }
    }

    private sealed class UnavailableWorkbenchAgentRunSubmissionAvailability
        : IWorkbenchAgentRunSubmissionAvailability
    {
        public Task<WorkbenchAgentRunSubmissionAvailability> CheckAsync(
            int tenantId,
            int projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkbenchAgentRunSubmissionAvailability(
                false,
                WorkbenchAgentRunFailureCategories.ServiceUnavailable));
    }
}
