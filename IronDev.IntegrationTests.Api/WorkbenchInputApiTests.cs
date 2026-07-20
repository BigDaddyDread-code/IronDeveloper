using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class WorkbenchInputApiTests : ApiTestBase
{
    [TestMethod]
    public async Task Commands_AreDeterministicIdempotentPrivateAndNeverStartTheBusinessAnalyst()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Command router contract");
        var chatSessionId = await CreateChatSessionAsync(project.ProjectId, "Command router chat");

        var helpOperationId = Guid.NewGuid();
        var helpPayload = new
        {
            project.WorkbenchSessionId,
            project.LeaseEpoch,
            clientOperationId = helpOperationId,
            chatSessionId,
            composerText = "  /HELP\r\n"
        };
        var helpResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/inputs",
            helpPayload);
        Assert.AreEqual(HttpStatusCode.OK, helpResponse.StatusCode);
        var help = await helpResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Help", help.GetProperty("kind").GetString());
        Assert.AreEqual("/help", help.GetProperty("normalizedCommand").GetString());
        Assert.AreEqual("Workbench commands", help.GetProperty("title").GetString());
        Assert.AreEqual(JsonValueKind.Null, help.GetProperty("instruction").ValueKind);
        Assert.IsFalse(help.GetProperty("isReplay").GetBoolean());

        var helpReplayResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/inputs",
            helpPayload);
        Assert.AreEqual(HttpStatusCode.OK, helpReplayResponse.StatusCode);
        var helpReplay = await helpReplayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(helpReplay.GetProperty("isReplay").GetBoolean());

        var ticketResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/inputs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                composerText = "/TiCkEt   focus on the login flow"
            });
        Assert.AreEqual(HttpStatusCode.OK, ticketResponse.StatusCode);
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Ticket", ticket.GetProperty("kind").GetString());
        Assert.AreEqual("/ticket", ticket.GetProperty("normalizedCommand").GetString());
        Assert.AreEqual("focus on the login flow", ticket.GetProperty("instruction").GetString());

        const string privateInstruction = "do-not-store-this-rejected-payload";
        var unknownOperationId = Guid.NewGuid();
        var unknownPayload = new
        {
            project.WorkbenchSessionId,
            project.LeaseEpoch,
            clientOperationId = unknownOperationId,
            chatSessionId,
            composerText = $" /tickte {privateInstruction}"
        };
        var unknownResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/inputs",
            unknownPayload);
        await AssertUnknownCommandAsync(unknownResponse, "/tickte");

        var unknownReplayResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/inputs",
            unknownPayload);
        await AssertUnknownCommandAsync(unknownReplayResponse, "/tickte");

        var mismatchResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/inputs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = unknownOperationId,
                chatSessionId,
                composerText = "/different changed payload"
            });
        Assert.AreEqual(HttpStatusCode.Conflict, mismatchResponse.StatusCode);
        var mismatch = await mismatchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("operation_id_payload_mismatch", mismatch.GetProperty("error").GetString());

        var legacyBypass = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/agent-runs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                chatSessionId,
                message = "/help"
            });
        Assert.AreEqual(HttpStatusCode.BadRequest, legacyBypass.StatusCode);
        var legacyError = await legacyBypass.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("workbench_input_route_required", legacyError.GetProperty("error").GetString());

        await using var connection = new SqlConnection(ConnectionString);
        var state = await connection.QuerySingleAsync<CommandAuditState>(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.WorkbenchCommandRejections
                 WHERE ClientOperationId=@UnknownOperationId) AS Rejections,
                (SELECT MAX(RawCommandToken) FROM dbo.WorkbenchCommandRejections
                 WHERE ClientOperationId=@UnknownOperationId) AS RawCommandToken,
                (SELECT MAX(PayloadHash) FROM dbo.WorkbenchCommandRejections
                 WHERE ClientOperationId=@UnknownOperationId) AS PayloadHash,
                (SELECT MAX(ReasonCode) FROM dbo.WorkbenchCommandRejections
                 WHERE ClientOperationId=@UnknownOperationId) AS ReasonCode,
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns WHERE ProjectId=@ProjectId) AS AgentRuns,
                (SELECT COUNT(1) FROM dbo.ChatMessages WHERE ProjectId=@ProjectId) AS ChatMessages,
                (SELECT COUNT(1) FROM dbo.WorkbenchBusinessAnalystPreparations preparation
                 INNER JOIN dbo.WorkbenchAgentRuns run ON run.AgentRunId=preparation.AgentRunId
                 WHERE run.ProjectId=@ProjectId) AS Preparations,
                (SELECT COUNT(1) FROM dbo.ClientOperations
                 WHERE ClientOperationId=@UnknownOperationId
                   AND CanonicalResultJson LIKE N'%' + @PrivateInstruction + N'%')
                +
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                 WHERE ClientOperationId=@UnknownOperationId
                   AND PayloadJson LIKE N'%' + @PrivateInstruction + N'%')
                +
                (SELECT COUNT(1) FROM dbo.WorkbenchCommandRejections
                 WHERE ClientOperationId=@UnknownOperationId
                   AND RawCommandToken LIKE N'%' + @PrivateInstruction + N'%') AS PrivatePayloadCopies;
            """,
            new
            {
                UnknownOperationId = unknownOperationId,
                project.ProjectId,
                PrivateInstruction = privateInstruction
            });
        Assert.AreEqual(1, state.Rejections);
        Assert.AreEqual("/tickte", state.RawCommandToken);
        Assert.AreEqual(64, state.PayloadHash?.Length);
        Assert.AreEqual("UnknownCommand", state.ReasonCode);
        Assert.AreEqual(0, state.AgentRuns);
        Assert.AreEqual(0, state.ChatMessages);
        Assert.AreEqual(0, state.Preparations);
        Assert.AreEqual(0, state.PrivatePayloadCopies);
    }

    [TestMethod]
    public async Task OrdinaryProse_RemainsConversationAndCreatesExactlyOneAgentRun()
    {
        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Conversation router contract");
        var chatSessionId = await CreateChatSessionAsync(project.ProjectId, "Conversation router chat");
        var operationId = Guid.NewGuid();
        const string prose = "We may use /ticket later. For now, discuss creating tickets with me.";

        var response = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/inputs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = operationId,
                chatSessionId,
                composerText = prose
            });
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("AgentRun", body.GetProperty("kind").GetString());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("normalizedCommand").ValueKind);
        Assert.AreEqual("Pending", body.GetProperty("agentRun").GetProperty("status").GetString());

        await using var connection = new SqlConnection(ConnectionString);
        var state = await connection.QuerySingleAsync<ConversationState>(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.WorkbenchAgentRuns
                 WHERE ProjectId=@ProjectId AND ClientOperationId=@OperationId) AS AgentRuns,
                (SELECT COUNT(1) FROM dbo.ChatMessages
                 WHERE ProjectId=@ProjectId AND Role=N'user' AND Message=@Prose) AS UserMessages,
                (SELECT COUNT(1) FROM dbo.WorkbenchCommandRejections
                 WHERE ProjectId=@ProjectId) AS Rejections;
            """,
            new { project.ProjectId, OperationId = operationId, Prose = prose });
        Assert.AreEqual(1, state.AgentRuns);
        Assert.AreEqual(1, state.UserMessages);
        Assert.AreEqual(0, state.Rejections);
    }

    [TestMethod]
    public async Task UnknownCommand_RequiresAuthenticationAndACurrentLeaseBeforeAuditWrite()
    {
        var unauthenticated = await Client.PostAsJsonAsync(
            "/api/workbench/projects/123/inputs",
            new
            {
                workbenchSessionId = 1,
                leaseEpoch = 1,
                clientOperationId = Guid.NewGuid(),
                composerText = "/unknown"
            });
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var token = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(token);
        var project = await StartProjectAsync(client, "Fenced command router");
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

        var rejected = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/inputs",
            new
            {
                project.WorkbenchSessionId,
                project.LeaseEpoch,
                clientOperationId = operationId,
                composerText = "/unknown"
            });
        Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.WorkbenchCommandRejections WHERE ClientOperationId=@OperationId;",
                new { OperationId = operationId }));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.ClientOperations WHERE ClientOperationId=@OperationId;",
                new { OperationId = operationId }));
        }
    }

    private static async Task AssertUnknownCommandAsync(HttpResponseMessage response, string token)
    {
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("workbench_command_unknown", body.GetProperty("error").GetString());
        Assert.AreEqual(token, body.GetProperty("rawCommandToken").GetString());
        Assert.IsFalse(body.TryGetProperty("composerText", out _));
        Assert.IsFalse(body.TryGetProperty("instruction", out _));
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

    private sealed class CommandAuditState
    {
        public int Rejections { get; init; }
        public string? RawCommandToken { get; init; }
        public string? PayloadHash { get; init; }
        public string? ReasonCode { get; init; }
        public int AgentRuns { get; init; }
        public int ChatMessages { get; init; }
        public int Preparations { get; init; }
        public int PrivatePayloadCopies { get; init; }
    }

    private sealed class ConversationState
    {
        public int AgentRuns { get; init; }
        public int UserMessages { get; init; }
        public int Rejections { get; init; }
    }
}
