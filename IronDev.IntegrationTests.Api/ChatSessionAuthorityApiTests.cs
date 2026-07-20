using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class ChatSessionAuthorityApiTests : ApiTestBase
{
    [TestMethod]
    public async Task V2Create_PostCommitRetryReplaysBeforeFence_AndCutoverRejectsUpdateDelete()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var authorityFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WorkbenchV2:Enabled", "true");
            builder.UseSetting("WorkbenchV2:ConversationAuthorityEnabled", "true");
        });
        using var client = authorityFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var startResponse = await client.PostAsJsonAsync("/api/projects/start", new
        {
            clientOperationId = Guid.NewGuid(),
            name = "Chat session durable replay"
        });
        Assert.AreEqual(HttpStatusCode.Created, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = start.GetProperty("projectId").GetInt32();
        var workbenchSessionId = start.GetProperty("workbenchSessionId").GetInt64();
        var leaseEpoch = start.GetProperty("leaseEpoch").GetInt64();
        var clientOperationId = Guid.NewGuid();
        var createPayload = new
        {
            id = (long?)null,
            projectId,
            title = "Durable shaping conversation",
            summary = "Created once even when the response is ambiguous.",
            workbenchSessionId,
            leaseEpoch,
            clientOperationId
        };

        var createdResponse = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/chat/sessions",
            createPayload);
        Assert.AreEqual(HttpStatusCode.OK, createdResponse.StatusCode);
        var sessionId = await createdResponse.Content.ReadFromJsonAsync<long>();
        Assert.IsTrue(sessionId > 0);

        // Model the client losing the committed response and retrying after its
        // original write lease has become invalid. Durable replay must win before
        // the fence and must not create a second row.
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                UPDATE dbo.WorkbenchWriteLeases
                SET RevokedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=1 AND ProjectId=@ProjectId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                """,
                new { ProjectId = projectId, WorkbenchSessionId = workbenchSessionId, LeaseEpoch = leaseEpoch });
        }

        var replayResponse = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/chat/sessions",
            createPayload);
        Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.AreEqual(sessionId, await replayResponse.Content.ReadFromJsonAsync<long>());

        await using (var connection = new SqlConnection(ConnectionString))
        {
            var counts = await connection.QuerySingleAsync<(int Sessions, int Operations)>(
                """
                SELECT
                    (SELECT COUNT(1) FROM dbo.ProjectChatSessions
                     WHERE TenantId=1 AND ProjectId=@ProjectId) AS Sessions,
                    (SELECT COUNT(1) FROM dbo.ClientOperations
                     WHERE TenantId=1 AND OperationKind=N'CreateProjectChatSession'
                       AND ClientOperationId=@ClientOperationId AND Status=N'Completed') AS Operations;
                """,
                new { ProjectId = projectId, ClientOperationId = clientOperationId });
            Assert.AreEqual(1, counts.Sessions);
            Assert.AreEqual(1, counts.Operations);
        }

        var mismatchResponse = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/chat/sessions",
            new
            {
                createPayload.id,
                createPayload.projectId,
                title = "Changed retry payload",
                createPayload.summary,
                createPayload.workbenchSessionId,
                createPayload.leaseEpoch,
                createPayload.clientOperationId
            });
        Assert.AreEqual(HttpStatusCode.Conflict, mismatchResponse.StatusCode);
        var mismatch = await mismatchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("operation_id_payload_mismatch", mismatch.GetProperty("error").GetString());

        var updateResponse = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/chat/sessions",
            new { id = sessionId, projectId, title = "Legacy rename", summary = (string?)null });
        await AssertConversationAuthorityConflictAsync(updateResponse);

        var deleteResponse = await client.DeleteAsync($"/api/projects/{projectId}/chat/sessions/{sessionId}");
        await AssertConversationAuthorityConflictAsync(deleteResponse);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            Assert.AreEqual(
                "Durable shaping conversation",
                await connection.QuerySingleAsync<string>(
                    "SELECT Title FROM dbo.ProjectChatSessions WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SessionId;",
                    new { ProjectId = projectId, SessionId = sessionId }));
        }
    }

    [TestMethod]
    public async Task V2Create_StaleFenceWritesNeitherSessionNorClientOperation()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var authorityFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WorkbenchV2:Enabled", "true");
            builder.UseSetting("WorkbenchV2:ConversationAuthorityEnabled", "true");
        });
        using var client = authorityFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var startResponse = await client.PostAsJsonAsync("/api/projects/start", new
        {
            clientOperationId = Guid.NewGuid(),
            name = "Chat session stale fence"
        });
        Assert.AreEqual(HttpStatusCode.Created, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = start.GetProperty("projectId").GetInt32();
        var workbenchSessionId = start.GetProperty("workbenchSessionId").GetInt64();
        var staleLeaseEpoch = start.GetProperty("leaseEpoch").GetInt64() + 1;
        var clientOperationId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/chat/sessions",
            new
            {
                id = (long?)null,
                projectId,
                title = "Must not be created",
                summary = (string?)null,
                workbenchSessionId,
                leaseEpoch = staleLeaseEpoch,
                clientOperationId
            });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var rejection = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("workbench_lease_fence_rejected", rejection.GetProperty("error").GetString());

        await using var connection = new SqlConnection(ConnectionString);
        var counts = await connection.QuerySingleAsync<(int Sessions, int Operations)>(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.ProjectChatSessions
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS Sessions,
                (SELECT COUNT(1) FROM dbo.ClientOperations
                 WHERE TenantId=1 AND OperationKind=N'CreateProjectChatSession'
                   AND ClientOperationId=@ClientOperationId) AS Operations;
            """,
            new { ProjectId = projectId, ClientOperationId = clientOperationId });
        Assert.AreEqual(0, counts.Sessions);
        Assert.AreEqual(0, counts.Operations);
    }

    [TestMethod]
    public async Task SessionRoutes_ConcealCrossProjectReadUpdateDelete_AndRequireActiveMembership()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var sourceProjectId = await StartProjectAsync(client, "Chat source project");
        var routeProjectId = await StartProjectAsync(client, "Chat route project");

        var createResponse = await client.PostAsJsonAsync(
            $"/api/projects/{sourceProjectId}/chat/sessions",
            new
            {
                id = (long?)null,
                projectId = sourceProjectId,
                title = "Source-only conversation",
                summary = (string?)null
            });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        var sourceSessionId = await createResponse.Content.ReadFromJsonAsync<long>();

        var messageResponse = await client.PostAsJsonAsync(
            $"/api/projects/{sourceProjectId}/chat/sessions/{sourceSessionId}/messages",
            new
            {
                projectId = sourceProjectId,
                chatSessionId = sourceSessionId,
                role = "assistant",
                message = "Source-project message",
                tags = (string?)null,
                contextSummary = (string?)null,
                linkedFilePaths = (string?)null,
                linkedSymbols = (string?)null,
                replyToMessageId = (long?)null,
                documentVersionIds = Array.Empty<long>()
            });
        Assert.AreEqual(HttpStatusCode.OK, messageResponse.StatusCode);
        var sourceMessageId = await messageResponse.Content.ReadFromJsonAsync<long>();

        var sourceMessageRead = await client.GetAsync(
            $"/api/projects/{sourceProjectId}/chat/messages/{sourceMessageId}");
        Assert.AreEqual(HttpStatusCode.OK, sourceMessageRead.StatusCode);
        var sourceMessage = await sourceMessageRead.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(sourceMessageId, sourceMessage.GetProperty("id").GetInt64());
        Assert.AreEqual("Source-project message", sourceMessage.GetProperty("message").GetString());

        var routeList = await client.GetAsync($"/api/projects/{routeProjectId}/chat/sessions");
        Assert.AreEqual(HttpStatusCode.OK, routeList.StatusCode);
        var routeSessions = await routeList.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(0, routeSessions.GetArrayLength());

        var wrongProjectRead = await client.GetAsync(
            $"/api/projects/{routeProjectId}/chat/sessions/{sourceSessionId}");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectRead.StatusCode);

        var wrongProjectMessage = await client.GetAsync(
            $"/api/projects/{routeProjectId}/chat/messages/{sourceMessageId}");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectMessage.StatusCode);

        var wrongProjectUpdate = await client.PostAsJsonAsync(
            $"/api/projects/{routeProjectId}/chat/sessions",
            new
            {
                id = sourceSessionId,
                projectId = routeProjectId,
                title = "Cross-project rename",
                summary = (string?)null
            });
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectUpdate.StatusCode);

        var wrongProjectDelete = await client.DeleteAsync(
            $"/api/projects/{routeProjectId}/chat/sessions/{sourceSessionId}");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectDelete.StatusCode);

        var wrongProjectFeedback = await client.PostAsJsonAsync(
            $"/api/projects/{routeProjectId}/chat/feedback",
            new
            {
                projectId = routeProjectId,
                chatSessionId = sourceSessionId,
                chatMessageId = sourceMessageId,
                rating = "Useful"
            });
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectFeedback.StatusCode);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            Assert.AreEqual(
                "Source-only conversation",
                await connection.QuerySingleAsync<string>(
                    "SELECT Title FROM dbo.ProjectChatSessions WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@SessionId;",
                    new { ProjectId = sourceProjectId, SessionId = sourceSessionId }));

            await connection.ExecuteAsync(
                """
                UPDATE dbo.ProjectMembers
                SET Status=N'Removed', RemovedUtc=SYSUTCDATETIME(), RemovedByUserId=UserId
                WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=1;
                """,
                new { ProjectId = sourceProjectId });
        }

        var removedMemberList = await client.GetAsync($"/api/projects/{sourceProjectId}/chat/sessions");
        Assert.AreEqual(HttpStatusCode.NotFound, removedMemberList.StatusCode);

        var removedMemberSources = await client.GetAsync($"/api/projects/{sourceProjectId}/chat/document-sources");
        Assert.AreEqual(HttpStatusCode.NotFound, removedMemberSources.StatusCode);

        var removedMemberMessages = await client.GetAsync(
            $"/api/projects/{sourceProjectId}/chat/sessions/{sourceSessionId}/messages");
        Assert.AreEqual(HttpStatusCode.NotFound, removedMemberMessages.StatusCode);

        var removedMemberMessage = await client.GetAsync(
            $"/api/projects/{sourceProjectId}/chat/messages/{sourceMessageId}");
        Assert.AreEqual(HttpStatusCode.NotFound, removedMemberMessage.StatusCode);

        var removedMemberAudit = await client.GetAsync(
            $"/api/projects/{sourceProjectId}/chat/sessions/{sourceSessionId}/messages/{sourceMessageId}/audit");
        Assert.AreEqual(HttpStatusCode.NotFound, removedMemberAudit.StatusCode);

        var removedMemberFeedback = await client.PostAsJsonAsync(
            $"/api/projects/{sourceProjectId}/chat/feedback",
            new
            {
                projectId = sourceProjectId,
                chatSessionId = sourceSessionId,
                chatMessageId = sourceMessageId,
                rating = "Useful"
            });
        Assert.AreEqual(HttpStatusCode.NotFound, removedMemberFeedback.StatusCode);

        var removedMemberMessageWrite = await client.PostAsJsonAsync(
            $"/api/projects/{sourceProjectId}/chat/sessions/{sourceSessionId}/messages",
            new
            {
                projectId = sourceProjectId,
                chatSessionId = sourceSessionId,
                role = "user",
                message = "Must not be written",
                tags = (string?)null,
                contextSummary = (string?)null,
                linkedFilePaths = (string?)null,
                linkedSymbols = (string?)null,
                replyToMessageId = (long?)null,
                documentVersionIds = Array.Empty<long>()
            });
        Assert.AreEqual(HttpStatusCode.NotFound, removedMemberMessageWrite.StatusCode);

        var removedMemberCompletion = await client.PostAsJsonAsync(
            $"/api/projects/{sourceProjectId}/chat/complete",
            new
            {
                projectId = sourceProjectId,
                sessionId = sourceSessionId,
                prompt = "Must not run",
                activeModel = (string?)null,
                mode = "projectQuestion"
            });
        Assert.AreEqual(HttpStatusCode.NotFound, removedMemberCompletion.StatusCode);
    }

    private static async Task<int> StartProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/projects/start", new
        {
            clientOperationId = Guid.NewGuid(),
            name
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("projectId").GetInt32();
    }

    private static async Task AssertConversationAuthorityConflictAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(
            "workbench_conversation_authority_required",
            body.GetProperty("error").GetString());
    }
}
