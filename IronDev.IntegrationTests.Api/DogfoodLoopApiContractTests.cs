using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Api.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class DogfoodLoopApiContractTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 12, 7, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task DogfoodLoopApi_Create_IsReceiptOnly()
    {
        using var client = await AuthedClientAsync();
        var before = StoreCount();

        var response = await client.PostAsJsonAsync("/api/v1/dogfood-loops", ValidRequest(1001, "receipt-only").ToBody());
        var json = await ReadJsonAsync(response);
        var after = StoreCount();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before + 1, after, "POST may create only a non-durable dogfood receipt.");
        Assert.AreEqual("receipt_created", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("humanApprovalRequired").GetBoolean());
        AssertDogfoodBoundary(json.RootElement.GetProperty("boundary"));

        var data = json.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("receiptOnly").GetBoolean());
        Assert.IsTrue(data.GetProperty("durable").GetBoolean());
        Assert.IsTrue(data.GetProperty("containsNonDurableReferences").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("warnings").EnumerateArray().Any(warning =>
            warning.GetString()?.Contains("Dogfood receipt is not release approval", StringComparison.OrdinalIgnoreCase) == true));
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task DogfoodLoopApi_Get_IsReadOnlyInspection()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/dogfood-loops", ValidRequest(1002, "get-readonly").ToBody());
        var createJson = await ReadJsonAsync(create);
        var loopId = createJson.RootElement.GetProperty("dogfoodLoopId").GetString();
        var before = StoreCount();

        var response = await client.GetAsync($"/api/v1/dogfood-loops/{loopId}?projectId=1002");
        var json = await ReadJsonAsync(response);
        var after = StoreCount();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not create dogfood receipts.");
        Assert.AreEqual("receipt_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertDogfoodBoundary(json.RootElement.GetProperty("boundary"));
        Assert.AreEqual(loopId, json.RootElement.GetProperty("data").GetProperty("dogfoodLoopId").GetString());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task DogfoodLoopApi_UnknownReceipt_ReturnsNotFound()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/dogfood-loops/missing-loop?projectId=1003");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("not_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task DogfoodLoopApi_RejectsCrossProjectAccess()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/dogfood-loops", ValidRequest(1004, "cross-project").ToBody());
        var createJson = await ReadJsonAsync(create);
        var loopId = createJson.RootElement.GetProperty("dogfoodLoopId").GetString();

        var response = await client.GetAsync($"/api/v1/dogfood-loops/{loopId}?projectId=1005");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DogfoodLoopApi_RejectsExecutionFields()
    {
        using var client = await AuthedClientAsync();
        var before = StoreCount();
        var request = ValidRequest(1006, "execution-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["executed"] = true,
                ["toolRan"] = true,
                ["workflowCompleted"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/dogfood-loops", request.ToBody());
        var json = await ReadJsonAsync(response);
        var after = StoreCount();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(before, after, "Execution-shaped fields must not create receipts.");
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
            error.GetProperty("category").GetString() == "unsupported_field"));
    }

    [TestMethod]
    public async Task DogfoodLoopApi_RejectsReleaseApprovalFields()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest(1007, "approval-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["releaseApproved"] = true,
                ["approvalSource"] = "dogfood",
                ["readyToShip"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/dogfood-loops", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task DogfoodLoopApi_RejectsSourceApplyAndMemoryPromotionFields()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest(1008, "apply-promotion-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["sourceApplied"] = true,
                ["memoryPromoted"] = true,
                ["collectiveMemoryWritten"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/dogfood-loops", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task DogfoodLoopApi_RejectsMissingProjectSummaryOrGoal()
    {
        using var client = await AuthedClientAsync();

        var missingProject = await client.PostAsJsonAsync("/api/v1/dogfood-loops", (ValidRequest(0, "missing-project")).ToBody());
        var missingSummary = await client.PostAsJsonAsync("/api/v1/dogfood-loops", (ValidRequest(1009, "missing-summary") with { Summary = "" }).ToBody());
        var missingGoal = await client.PostAsJsonAsync("/api/v1/dogfood-loops", (ValidRequest(1009, "missing-goal") with { Goal = "" }).ToBody());

        Assert.AreEqual(HttpStatusCode.BadRequest, missingProject.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingSummary.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingGoal.StatusCode);
    }

    [TestMethod]
    public async Task DogfoodLoopApi_RejectsOversizedTextAndTooManyReferences()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest(1010, "oversized") with
        {
            Summary = new string('x', 801),
            ToolRequestIds = Enumerable.Range(0, 51).Select(index => $"tool-request-{index}").ToArray()
        };

        var response = await client.PostAsJsonAsync("/api/v1/dogfood-loops", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.IsTrue(json.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
            error.GetProperty("code").GetString() == "content_too_large"));
    }

    [TestMethod]
    public async Task DogfoodLoopApi_DoesNotExposeHiddenReasoning()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest(1011, "hidden-reasoning") with
        {
            Observations = ["chain-of-thought PRIVATE_MARKER should never enter dogfood receipts."]
        };

        var response = await client.PostAsJsonAsync("/api/v1/dogfood-loops", request.ToBody());
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        AssertNoPrivateReasoningLeak(text);
    }

    [TestMethod]
    public void DogfoodLoopApi_DurableStoreRejectsHiddenReasoningReceipts()
    {
        var before = StoreCount();

        Assert.ThrowsException<ArgumentException>(() => StoreSave(BuildPrivateReceipt("dogfood-loop-private-1", 1012)));

        Assert.AreEqual(before, StoreCount(), "Private reasoning shaped receipts must not enter the durable dogfood receipt ledger.");
    }

    [TestMethod]
    public async Task DogfoodLoopApi_IdentifiesDurableReceiptWithNonAuthoritativeReferences()
    {
        using var client = await AuthedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/dogfood-loops", ValidRequest(1013, "non-durable-refs").ToBody());
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("containsNonDurableReferences").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("warnings").EnumerateArray().Any(warning =>
            warning.GetString()?.Contains("Dogfood loop receipts are durable SQL-backed evidence", StringComparison.OrdinalIgnoreCase) == true));
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task DogfoodLoopApi_UnauthenticatedRequestsAreRejected()
    {
        var get = await Client.GetAsync("/api/v1/dogfood-loops/missing?projectId=1014");
        var post = await Client.PostAsJsonAsync("/api/v1/dogfood-loops", ValidRequest(1014, "unauthenticated").ToBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [TestMethod]
    public void DogfoodLoopApi_ControllerDoesNotReferenceForbiddenServices()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "DogfoodLoopsV1Controller.cs"));

        var forbiddenTokens = new[]
        {
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "ManualDogfoodHarnessService",
            "IManualDogfoodHarnessService",
            "ManualTesterAgentToolExecutionService",
            "IManualTesterAgentToolExecutionService",
            "ToolExecutionAuditStore",
            "IToolExecutionAuditStore",
            "AgentToolExecutor",
            "IAgentToolExecutor",
            "AgentToolRouter",
            "IAgentToolRouter",
            "IWorkspaceApply",
            "ApplyCopy",
            "IControlledWorktreeApplyService",
            "IControlledWriteApprovalService",
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "ICollectiveMemoryPromotion",
            "PromoteCollectiveMemory",
            "WeaviateSemanticMemoryService",
            "IWeaviate",
            "MemoryIndexQueue",
            "BackgroundService",
            "IHostedService"
        };

        foreach (var token in forbiddenTokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in dogfood loop API controller: {token}");

        StringAssert.Contains(text, "IDogfoodLoopApiStore");
        Assert.IsFalse(text.Contains("InMemoryDogfoodLoopApiStore", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void DogfoodLoopApi_DocumentationPreservesBoundaries()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "API_DOGFOOD_LOOPS_V1.md"));

        StringAssert.Contains(text, "Dogfood Loop API v1");
        StringAssert.Contains(text, "POST `/api/v1/dogfood-loops`");
        StringAssert.Contains(text, "GET `/api/v1/dogfood-loops/{dogfoodLoopId}?projectId={projectId}`");
        StringAssert.Contains(text, "Dogfood receipt is not release approval.");
        StringAssert.Contains(text, "Dogfood loop is not autonomous workflow.");
        StringAssert.Contains(text, "Tool request is request form, not execution permission.");
        StringAssert.Contains(text, "Gate is not executor.");
        StringAssert.Contains(text, "Gate pass is not human approval.");
        StringAssert.Contains(text, "Audit evidence is not approval.");
        StringAssert.Contains(text, "Endpoint access is not execution permission.");
        StringAssert.Contains(text, "API response status is not governance.");
        StringAssert.Contains(text, "Human review remains required for source apply.");
        StringAssert.Contains(text, "Human review remains required for memory promotion.");
        StringAssert.Contains(text, "This API stores durable SQL-backed dogfood receipt evidence");
        StringAssert.Contains(text, "does not make the receipt release approval");
        StringAssert.Contains(text, "\"durable\": true");
        StringAssert.Contains(text, "\"containsNonDurableReferences\": true");
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static int StoreCount()
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDogfoodLoopApiStore>().Count();
    }

    private static void StoreSave(DogfoodLoopApiStoredReceipt receipt)
    {
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDogfoodLoopApiStore>().Save(receipt);
    }

    private static DogfoodLoopApiRequestBody ValidRequest(int projectId, string correlationId) =>
        new(
            projectId,
            "Manual dogfood loop receipt",
            "Collect dogfood loop evidence for human review.",
            [$"agent-run-{projectId}"],
            [$"critic-run-{projectId}"],
            [$"memory-run-{projectId}"],
            [$"tool-request-{projectId}"],
            [$"tool-gate-{projectId}"],
            [new DogfoodEvidenceBody("source_report", $"source-report-{projectId}", "Caller-supplied source report reference.", "caller")],
            ["Receipt is advisory evidence only."],
            [],
            correlationId);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static DogfoodLoopApiStoredReceipt BuildPrivateReceipt(string loopId, int projectId)
    {
        var privateText = "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning";
        return new DogfoodLoopApiStoredReceipt
        {
            TenantId = AssignedTenantId.ToString(),
            ProjectId = projectId.ToString(),
            DogfoodLoopId = loopId,
            RunId = $"run-{loopId}",
            ReceiptId = $"receipt-{loopId}",
            EvidenceId = $"evidence-{loopId}",
            Summary = privateText,
            Goal = privateText,
            Observations = [privateText],
            BlockedReasons = [privateText],
            ReferencedAgentRuns = [PrivateReference(privateText)],
            ReferencedCriticReviews = [PrivateReference(privateText)],
            ReferencedMemoryImprovements = [PrivateReference(privateText)],
            ReferencedToolRequests = [PrivateReference(privateText)],
            ReferencedGateDecisions = [PrivateReference(privateText)],
            EvidenceRefs = [PrivateReference(privateText)],
            Durable = true,
            ContainsNonDurableReferences = true,
            CreatedAtUtc = CreatedAt,
            CreatedByUserId = "user-1",
            CorrelationId = privateText,
            ContainsRawPrivateReasoning = true,
            Warnings = ["Stored receipt contains private reasoning and must be redacted."]
        };
    }

    private static DogfoodLoopReferenceDto PrivateReference(string privateText) =>
        new()
        {
            RefType = privateText,
            RefId = privateText,
            Summary = privateText,
            Durable = true,
            BackendRecorded = false,
            Source = privateText
        };

    private static void AssertDogfoodBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("dogfoodReceiptIsReleaseApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("dogfoodLoopIsAutonomousWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("toolExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("requestApproved").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("gateExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("gateIsExecutor").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryPromoted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("collectiveMemoryWritten").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("vectorAuthorityWritten").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("auditIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("modelOutputIsAuthority").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("endpointAccessIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("apiResponseStatusIsGovernance").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("durable").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("containsNonDurableReferences").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());
    }

    private static void AssertNoMisleadingAuthorityLanguage(string text)
    {
        var forbidden = new[]
        {
            "releaseApproved\":true",
            "approved\":true",
            "governed\":true",
            "applied\":true",
            "promoted\":true",
            "executionPermitted\":true",
            "toolRan\":true",
            "gateExecuted\":true",
            "sourceApplied\":true",
            "memoryPromoted\":true",
            "toolExecuted\":true",
            "requestApproved\":true",
            "dogfoodReceiptIsReleaseApproval\":true",
            "dogfoodLoopIsAutonomousWorkflow\":true",
            "auditIsApproval\":true",
            "gateIsExecutor\":true",
            "apiResponseStatusIsGovernance\":true",
            "endpointAccessIsExecutionPermission\":true",
            "modelOutputIsAuthority\":true"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained misleading authority language: {token}");
    }

    private static void AssertNoPrivateReasoningLeak(string text)
    {
        var forbidden = new[]
        {
            "PRIVATE_MARKER",
            "chain-of-thought",
            "hidden reasoning",
            "raw prompt",
            "scratchpad",
            "private reasoning"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained private reasoning marker: {token}");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }

    private sealed record DogfoodEvidenceBody(string RefType, string RefId, string Summary, string Source);

    private sealed record DogfoodLoopApiRequestBody(
        int ProjectId,
        string Summary,
        string Goal,
        IReadOnlyList<string> AgentRunIds,
        IReadOnlyList<string> CriticReviewRunIds,
        IReadOnlyList<string> MemoryImprovementRunIds,
        IReadOnlyList<string> ToolRequestIds,
        IReadOnlyList<string> ToolGateDecisionIds,
        IReadOnlyList<DogfoodEvidenceBody> EvidenceRefs,
        IReadOnlyList<string> Observations,
        IReadOnlyList<string> BlockedReasons,
        string CorrelationId)
    {
        [JsonIgnore]
        public Dictionary<string, object?> Extra { get; init; } = [];

        public Dictionary<string, object?> ToBody()
        {
            var body = new Dictionary<string, object?>
            {
                ["projectId"] = ProjectId,
                ["summary"] = Summary,
                ["goal"] = Goal,
                ["agentRunIds"] = AgentRunIds,
                ["criticReviewRunIds"] = CriticReviewRunIds,
                ["memoryImprovementRunIds"] = MemoryImprovementRunIds,
                ["toolRequestIds"] = ToolRequestIds,
                ["toolGateDecisionIds"] = ToolGateDecisionIds,
                ["evidenceRefs"] = EvidenceRefs,
                ["observations"] = Observations,
                ["blockedReasons"] = BlockedReasons,
                ["correlationId"] = CorrelationId
            };

            foreach (var pair in Extra)
                body[pair.Key] = pair.Value;

            return body;
        }
    }
}
