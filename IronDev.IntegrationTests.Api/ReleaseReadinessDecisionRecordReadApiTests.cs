using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[TestCategory("ReleaseReadinessDecisionRecordReadApi")]
public sealed class ReleaseReadinessDecisionRecordReadApiTests : ApiTestBase
{
    private static readonly DateTimeOffset DecidedAtUtc = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_GetByIdReturnsStoredEvidence()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord("get-by-id");
        await SeedAsync(record);
        var before = await CountAsync();

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/release-readiness-decision-records/{record.ReleaseReadinessDecisionRecordId}");
        var json = await ReadJsonAsync(response);
        var after = await CountAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not mutate release readiness decision records.");
        AssertReadOnlyEnvelope(json.RootElement);
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        AssertNoReleaseGateAuthority(text);

        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(record.ReleaseReadinessDecisionRecordId, data.GetProperty("releaseReadinessDecisionRecordId").GetGuid());
        Assert.AreEqual(record.ProjectId, data.GetProperty("projectId").GetGuid());
        Assert.AreEqual(record.ReleaseReadinessReportId, data.GetProperty("releaseReadinessReportId").GetGuid());
        Assert.AreEqual(record.WorkflowRunId, data.GetProperty("workflowRunId").GetString());
        Assert.AreEqual(record.WorkflowStepId, data.GetProperty("workflowStepId").GetString());
        Assert.AreEqual(record.SubjectKind, data.GetProperty("subjectKind").GetString());
        Assert.AreEqual(record.SubjectId, data.GetProperty("subjectId").GetString());
        Assert.AreEqual(record.DecisionStatus, data.GetProperty("decisionStatus").GetString());
        Assert.AreEqual(record.ReleaseReadinessDecisionRecordHash, data.GetProperty("releaseReadinessDecisionRecordHash").GetString());
        Assert.IsTrue(data.GetProperty("releaseReadinessEvidenceSatisfied").GetBoolean());
        Assert.IsFalse(data.GetProperty("releaseApproved").GetBoolean());
        Assert.IsFalse(data.GetProperty("deploymentApproved").GetBoolean());
        Assert.IsFalse(data.GetProperty("mergeApproved").GetBoolean());
        Assert.IsFalse(data.GetProperty("mutationOccurredInThisApi").GetBoolean());
        Assert.IsFalse(data.GetProperty("releaseReadinessGateRanByThisApi").GetBoolean());
        Assert.IsFalse(data.GetProperty("releaseApprovedByThisApi").GetBoolean());
        Assert.IsFalse(data.GetProperty("deploymentApprovedByThisApi").GetBoolean());
        Assert.IsFalse(data.GetProperty("mergeApprovedByThisApi").GetBoolean());
        Assert.IsTrue(data.GetProperty("humanReviewRequired").GetBoolean());
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_GetByRawHashReturnsStoredEvidence()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord("get-by-hash");
        await SeedAsync(record);

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/release-readiness-decision-records/by-hash/{record.ReleaseReadinessDecisionRecordHash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(record.ReleaseReadinessDecisionRecordId, json.RootElement.GetProperty("data").GetProperty("releaseReadinessDecisionRecordId").GetGuid());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_RejectsPrefixedHash()
    {
        using var client = await AuthedClientAsync();
        var hash = H("prefixed-hash");

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/release-readiness-decision-records/by-hash/sha256:{hash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        AssertReadOnlyEnvelope(json.RootElement);
        StringAssert.Contains(json.RootElement.ToString(), "raw 64-character hexadecimal SHA-256 hash without a prefix");
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_ListByReportReturnsProjectScopedRecords()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var first = Rehash(ValidRecord("report-a") with { ProjectId = projectId, ReleaseReadinessReportId = reportId });
        var second = Rehash(ValidRecord("report-b") with { ProjectId = projectId, ReleaseReadinessReportId = reportId });
        var otherReport = Rehash(ValidRecord("report-c") with { ProjectId = projectId, ReleaseReadinessReportId = Guid.NewGuid() });
        var otherProject = Rehash(ValidRecord("report-d") with { ProjectId = Guid.NewGuid(), ReleaseReadinessReportId = reportId });
        await SeedAsync(first, second, otherReport, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/release-readiness-decision-records/by-report/{reportId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.ReleaseReadinessDecisionRecordId, second.ReleaseReadinessDecisionRecordId);
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_ListByWorkflowRunReturnsProjectScopedRecords()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var workflowRunId = "workflow-run-pr220";
        var first = Rehash(ValidRecord("run-a") with { ProjectId = projectId, WorkflowRunId = workflowRunId });
        var second = Rehash(ValidRecord("run-b") with { ProjectId = projectId, WorkflowRunId = workflowRunId });
        var otherRun = Rehash(ValidRecord("run-c") with { ProjectId = projectId, WorkflowRunId = "other-run-pr220" });
        var otherProject = Rehash(ValidRecord("run-d") with { ProjectId = Guid.NewGuid(), WorkflowRunId = workflowRunId });
        await SeedAsync(first, second, otherRun, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/release-readiness-decision-records/by-workflow-run/{workflowRunId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.ReleaseReadinessDecisionRecordId, second.ReleaseReadinessDecisionRecordId);
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_ListBySubjectReturnsProjectScopedRecords()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var subjectKind = "ReleasePackage";
        var subjectId = "release-package-pr220";
        var matching = Rehash(ValidRecord("subject-a") with { ProjectId = projectId, SubjectKind = subjectKind, SubjectId = subjectId });
        var otherSubject = Rehash(ValidRecord("subject-b") with { ProjectId = projectId, SubjectKind = subjectKind, SubjectId = "other-subject-pr220" });
        var otherProject = Rehash(ValidRecord("subject-c") with { ProjectId = Guid.NewGuid(), SubjectKind = subjectKind, SubjectId = subjectId });
        await SeedAsync(matching, otherSubject, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/release-readiness-decision-records/by-subject/{subjectKind}/{subjectId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.ReleaseReadinessDecisionRecordId);
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_MissingRecordReturnsNotFound()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/release-readiness-decision-records/{Guid.NewGuid()}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_EmptyListReturnsEmptyArray()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/release-readiness-decision-records/by-workflow-run/no-matches-pr220");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertReadOnlyEnvelope(json.RootElement);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(0, data.GetProperty("count").GetInt32());
        Assert.AreEqual(0, data.GetProperty("records").GetArrayLength());
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_TakeMustBeWithinBounds()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();

        foreach (var take in new[] { 0, -1, 501 })
        {
            var response = await client.GetAsync($"/api/v1/projects/{projectId}/release-readiness-decision-records/by-workflow-run/safe-run?take={take}");
            var json = await ReadJsonAsync(response);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
            AssertReadOnlyEnvelope(json.RootElement);
        }
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordReadApi_RejectsUnsafeLookupWithoutEchoingValue()
    {
        using var client = await AuthedClientAsync();
        var unsafeRun = Uri.EscapeDataString("raw prompt release approved");

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/release-readiness-decision-records/by-workflow-run/{unsafeRun}");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        AssertReadOnlyEnvelope(json.RootElement);
        Assert.IsFalse(text.Contains("raw prompt release approved", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("raw prompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("release approved", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordReadApi_ResponseDoesNotExposeRawPrivateMaterial()
    {
        var unsafeRecord = ValidRecord("unsafe-read") with
        {
            WorkflowRunId = "rawPromptLeak-pr220",
            WorkflowStepId = "private reasoning leak",
            Reasons =
            [
                new ReleaseReadinessDecisionReason
                {
                    Code = "UnsafeReason",
                    Severity = ReleaseReadinessDecisionReasonSeverities.Warning,
                    Field = "ReleaseApproval",
                    Message = "raw completion evidence leaked."
                }
            ],
            EvidenceReferences = ["raw tool output evidence"],
            BoundaryMaxims = ["chain-of-thought maxim"],
            Boundary = "hidden reasoning boundary"
        };
        var query = new ReleaseReadinessDecisionRecordQueryService(new FakeReleaseReadinessDecisionRecordStore(unsafeRecord));

        var read = query.GetAsync(unsafeRecord.ProjectId, unsafeRecord.ReleaseReadinessDecisionRecordId).GetAwaiter().GetResult();
        var json = JsonSerializer.Serialize(read);

        Assert.IsNotNull(read);
        Assert.IsFalse(json.Contains("rawPromptLeak-pr220", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("private reasoning leak", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("raw completion evidence", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("raw tool output evidence", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("chain-of-thought maxim", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("hidden reasoning boundary", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(json, "[redacted: sensitive release readiness decision record text]");
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordReadApi_DoesNotExposePostPutPatchDelete()
    {
        var methods = ReadRouteMethods().ToArray();

        Assert.AreEqual(5, methods.Length);
        Assert.IsTrue(methods.All(method => method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordReadApi_ControllerDoesNotDependOnStoreSave()
    {
        var controller = File.ReadAllText(ControllerPath());

        StringAssert.Contains(controller, "IReleaseReadinessDecisionRecordQueryService");
        Assert.IsFalse(controller.Contains("IReleaseReadinessDecisionRecordStore", StringComparison.Ordinal), "Controller must not depend on write-capable store directly.");
        Assert.IsFalse(controller.Contains("SqlReleaseReadinessDecisionRecordStore", StringComparison.Ordinal), "Controller must not depend on SQL store directly.");
        Assert.IsFalse(controller.Contains("SaveAsync", StringComparison.Ordinal), "Controller must not save release readiness decision records.");
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordReadApi_QueryServiceUsesReadOnlyStoreMethods()
    {
        var queryService = File.ReadAllText(QueryServicePath());

        StringAssert.Contains(queryService, "IReleaseReadinessDecisionRecordStore");
        StringAssert.Contains(queryService, "_store.GetAsync");
        StringAssert.Contains(queryService, "_store.GetByRecordHashAsync");
        StringAssert.Contains(queryService, "_store.ListByReleaseReadinessReportAsync");
        StringAssert.Contains(queryService, "_store.ListByWorkflowRunAsync");
        StringAssert.Contains(queryService, "_store.ListBySubjectAsync");
        Assert.IsFalse(queryService.Contains(".SaveAsync", StringComparison.Ordinal), "Query service must not save release readiness decision records.");
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordReadApi_ReadModelsDoNotClaimGateApprovalOrExecution()
    {
        var model = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Core", "Governance", "ReleaseReadinessDecisionRecordReadModels.cs"));

        StringAssert.Contains(model, "ReleaseReadinessGateRanByThisApi");
        StringAssert.Contains(model, "ReleaseApprovedByThisApi");
        StringAssert.Contains(model, "DeploymentApprovedByThisApi");
        StringAssert.Contains(model, "MergeApprovedByThisApi");
        StringAssert.Contains(model, "ReleaseExecutedByThisApi");
        StringAssert.Contains(model, "SourceApplyExecutedByThisApi");
        StringAssert.Contains(model, "RollbackExecutedByThisApi");
        StringAssert.Contains(model, "WorkflowContinuedByThisApi");
        StringAssert.Contains(model, "GitOperationExecutedByThisApi");
        StringAssert.Contains(model, "HumanReviewRequired");
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordReadApi_DoesNotAddMutationRuntimeOrAuthorityTokens()
    {
        foreach (var token in new[]
        {
            "HttpPost",
            "HttpPut",
            "HttpPatch",
            "HttpDelete",
            "File.WriteAllText",
            "File.WriteAllBytes",
            "File.Delete",
            "File.Move",
            "Directory.CreateDirectory",
            "Process.Start",
            "ProcessStartInfo",
            "IHostedService",
            "BackgroundService",
            "Scheduler",
            "ReleaseExecutionService",
            "ReleaseReadinessGateEvaluator",
            "ReleaseApprovalExecutor",
            "DeployRelease",
            "MergeRelease",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "WorkflowContinuationExecutor",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding"
        })
        {
            AssertNoProductionTokens(token);
        }
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordReadApi_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR220_RELEASE_GATE_READ_API.md"));

        foreach (var statement in new[]
        {
            "PR220 adds release readiness decision record read API only.",
            "PR220 exposes stored ReleaseReadinessDecisionRecord evidence for review.",
            "PR220 does not run a release-readiness gate.",
            "PR220 does not create release readiness decision records.",
            "PR220 does not approve release.",
            "PR220 does not approve deployment.",
            "PR220 does not approve merge.",
            "PR220 does not execute release.",
            "PR220 does not execute source apply.",
            "PR220 does not execute rollback.",
            "PR220 does not continue workflow.",
            "PR220 does not run git.",
            "PR220 does not add SQL.",
            "PR220 does not add CLI.",
            "PR220 does not add UI.",
            "PR220 does not add runtime execution.",
            "Release gate read API is not release readiness gate execution.",
            "Release gate read API is not release approval.",
            "Release gate read API is not deployment approval.",
            "Release gate read API is not merge approval.",
            "Read ReleaseReadinessDecisionRecord is evidence only.",
            "Read ReleaseReadinessDecisionRecord is not ReleaseApproved.",
            "Read ReleaseReadinessDecisionRecord is not DeploymentApproved.",
            "Read ReleaseReadinessDecisionRecord is not MergeApproved.",
            "Human review remains required.",
            "PR220 opens the release-gate receipt window. It does not open the release gate."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task SeedAsync(params ReleaseReadinessDecisionRecord[] records)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IReleaseReadinessDecisionRecordStore>();
        foreach (var record in records)
        {
            await store.SaveAsync(record);
        }
    }

    private static async Task<int> CountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.ReleaseReadinessDecisionRecord");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static ReleaseReadinessDecisionRecord ValidRecord(string suffix = "main") =>
        Rehash(new ReleaseReadinessDecisionRecord
        {
            ReleaseReadinessDecisionRecordId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ReleaseReadinessReportId = Guid.NewGuid(),
            ReleaseReadinessReportHash = H($"release-readiness-report-{suffix}"),
            WorkflowRunId = $"workflow-run-{suffix}",
            WorkflowStepId = $"workflow-step-{suffix}",
            SubjectKind = "ReleasePackage",
            SubjectId = $"release-package-{suffix}",
            SubjectHash = H($"release-package-{suffix}"),
            DecisionStatus = ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied,
            ReleaseReadinessEvidenceSatisfied = true,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            SourceApplyExecutedByDecision = false,
            RollbackExecutedByDecision = false,
            WorkflowMutatedByDecision = false,
            GitOperationExecutedByDecision = false,
            ReleaseExecutedByDecision = false,
            HumanReviewRequiredForReleaseApproval = true,
            HumanReviewRequiredForDeployment = true,
            HumanReviewRequiredForMerge = true,
            Reasons =
            [
                new ReleaseReadinessDecisionReason
                {
                    Code = "ReportComplete",
                    Severity = ReleaseReadinessDecisionReasonSeverities.Info,
                    Field = "ReleaseReadinessReport",
                    Message = "Release readiness report evidence was complete."
                },
                new ReleaseReadinessDecisionReason
                {
                    Code = "HumanReviewRequiredForReleaseApproval",
                    Severity = ReleaseReadinessDecisionReasonSeverities.Warning,
                    Field = "ReleaseApproval",
                    Message = "Human review remains required for release approval."
                }
            ],
            EvidenceReferences = [$"release-readiness-report:{suffix}", $"workflow-transition-record:{suffix}"],
            BoundaryMaxims = ["Release readiness read evidence is not release approval.", "Human review remains required."],
            DecidedAtUtc = DecidedAtUtc.AddMinutes(Math.Abs(suffix.GetHashCode(StringComparison.Ordinal)) % 1000),
            ReleaseReadinessDecisionRecordHash = H($"placeholder-{suffix}"),
            Boundary = ReleaseReadinessDecisionRecordBoundaryText.Boundary
        });

    private static ReleaseReadinessDecisionRecord Rehash(ReleaseReadinessDecisionRecord record) =>
        record with { ReleaseReadinessDecisionRecordHash = ReleaseReadinessDecisionRecordHashing.ComputeHash(record) };

    private static string H(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void AssertItems(JsonElement root, params Guid[] expectedIds)
    {
        AssertReadOnlyEnvelope(root);
        var data = root.GetProperty("data");
        var ids = data.GetProperty("records").EnumerateArray().Select(item => item.GetProperty("releaseReadinessDecisionRecordId").GetGuid()).ToArray();
        CollectionAssert.AreEquivalent(expectedIds, ids);
        Assert.AreEqual(expectedIds.Length, data.GetProperty("count").GetInt32());
    }

    private static void AssertReadOnlyEnvelope(JsonElement root)
    {
        Assert.IsFalse(root.GetProperty("mutationOccurredInThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("releaseReadinessGateRanByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("releaseApprovedByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("deploymentApprovedByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("mergeApprovedByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("releaseExecutedByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("sourceApplyExecutedByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("rollbackExecutedByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("workflowContinuedByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("gitOperationExecutedByThisApi").GetBoolean());
        Assert.IsTrue(root.GetProperty("humanReviewRequired").GetBoolean());
        StringAssert.Contains(root.GetProperty("warnings")[0].GetString(), "Release readiness decision record read API is read-only.");
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("readCreatesReleaseReadinessDecisionRecord").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readRunsReleaseReadinessGate").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readApprovesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readApprovesDeployment").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readApprovesMerge").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readExecutesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readExecutesSourceApply").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readExecutesRollback").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readContinuesWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readRunsGit").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequired").GetBoolean());
    }

    private static void AssertNoReleaseGateAuthority(string text)
    {
        foreach (var token in new[]
        {
            "releaseReadinessGateRanByThisApi\":true",
            "releaseApprovedByThisApi\":true",
            "deploymentApprovedByThisApi\":true",
            "mergeApprovedByThisApi\":true",
            "releaseExecutedByThisApi\":true",
            "sourceApplyExecutedByThisApi\":true",
            "rollbackExecutedByThisApi\":true",
            "workflowContinuedByThisApi\":true",
            "gitOperationExecutedByThisApi\":true",
            "canApproveRelease",
            "canDeployRelease",
            "canMergeRelease",
            "releaseExecuted\":true",
            "releaseGateOpened\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained authority token: {token}");
        }
    }

    private static IEnumerable<MethodInfo> ReadRouteMethods()
    {
        var routeNames = new[]
        {
            "Get",
            "GetByRecordHash",
            "ListByReleaseReadinessReport",
            "ListByWorkflowRun",
            "ListBySubject"
        };

        return typeof(ReleaseReadinessDecisionRecordsController).GetMethods()
            .Where(method => method.DeclaringType == typeof(ReleaseReadinessDecisionRecordsController))
            .Where(method => routeNames.Contains(method.Name, StringComparer.Ordinal));
    }

    private static void AssertNoProductionTokens(params string[] tokens)
    {
        foreach (var file in Pr220ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden token found in {file}: {token}");
            }
        }
    }

    private static string[] Pr220ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            ControllerPath(),
            QueryServicePath(),
            Path.Combine(root, "IronDev.Core", "Governance", "ReleaseReadinessDecisionRecordReadModels.cs")
        ];
    }

    private static string ControllerPath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "ReleaseReadinessDecisionRecordsController.cs");

    private static string QueryServicePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "ReleaseReadinessDecisionRecordQueryService.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class FakeReleaseReadinessDecisionRecordStore : IReleaseReadinessDecisionRecordStore
    {
        private readonly ReleaseReadinessDecisionRecord _record;

        public FakeReleaseReadinessDecisionRecordStore(ReleaseReadinessDecisionRecord record) => _record = record;

        public Task SaveAsync(ReleaseReadinessDecisionRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fake read store must not save.");

        public Task<ReleaseReadinessDecisionRecord?> GetAsync(Guid projectId, Guid releaseReadinessDecisionRecordId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReleaseReadinessDecisionRecord?>(_record);

        public Task<ReleaseReadinessDecisionRecord?> GetByRecordHashAsync(Guid projectId, string releaseReadinessDecisionRecordHash, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReleaseReadinessDecisionRecord?>(_record);

        public Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListByReleaseReadinessReportAsync(Guid projectId, Guid releaseReadinessReportId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReleaseReadinessDecisionRecord>>([_record]);

        public Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListByWorkflowRunAsync(Guid projectId, string workflowRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReleaseReadinessDecisionRecord>>([_record]);

        public Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReleaseReadinessDecisionRecord>>([_record]);
    }
}
