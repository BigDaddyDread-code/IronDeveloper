using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
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

        long chatSessionId;
        await using (var connection = new SqlConnection(ConnectionString))
        {
            chatSessionId = await connection.QuerySingleAsync<long>("""
                INSERT dbo.ProjectChatSessions(TenantId, ProjectId, Title)
                OUTPUT inserted.Id
                VALUES (1, @ProjectId, N'Agent run API');
                """, new { ProjectId = projectId });
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
        Assert.IsFalse(status.TryGetProperty("contextSnapshotJson", out _));
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
}
