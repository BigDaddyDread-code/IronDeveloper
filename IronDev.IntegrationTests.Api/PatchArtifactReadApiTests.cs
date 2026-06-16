using System.Net;
using System.Reflection;
using System.Text.Json;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class PatchArtifactReadApiTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 16, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = new(2026, 6, 17, 17, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PatchArtifactReadApi_CanGetById()
    {
        using var client = await AuthedClientAsync();
        var artifact = ValidArtifact("get-by-id");
        await SeedAsync(artifact);
        var before = await CountAsync();

        var response = await client.GetAsync($"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/{artifact.PatchArtifactId}");
        var json = await ReadJsonAsync(response);
        var after = await CountAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not mutate patch artifact rows.");
        AssertReadOnlyEnvelope(json.RootElement);
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        AssertNoSourceApplyAuthority(text);

        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(artifact.PatchArtifactId, data.GetProperty("patchArtifactId").GetGuid());
        Assert.AreEqual(artifact.ProjectId, data.GetProperty("projectId").GetGuid());
        Assert.AreEqual(artifact.PatchArtifactKind, data.GetProperty("patchArtifactKind").GetString());
        Assert.AreEqual(artifact.ControlledDryRunRequestId, data.GetProperty("controlledDryRunRequestId").GetGuid());
        Assert.AreEqual(artifact.DryRunExecutionAuditId, data.GetProperty("dryRunExecutionAuditId").GetGuid());
        Assert.AreEqual(artifact.DryRunAuditHash, data.GetProperty("dryRunAuditHash").GetString());
        Assert.AreEqual(artifact.DryRunReceiptHash, data.GetProperty("dryRunReceiptHash").GetString());
        Assert.AreEqual(artifact.PolicySatisfactionId, data.GetProperty("policySatisfactionId").GetGuid());
        Assert.AreEqual(artifact.PolicySatisfactionHash, data.GetProperty("policySatisfactionHash").GetString());
        Assert.AreEqual(artifact.SubjectKind, data.GetProperty("subjectKind").GetString());
        Assert.AreEqual(artifact.SubjectId, data.GetProperty("subjectId").GetString());
        Assert.AreEqual(artifact.SubjectHash, data.GetProperty("subjectHash").GetString());
        Assert.AreEqual(artifact.SourceSnapshotReference, data.GetProperty("sourceSnapshotReference").GetString());
        Assert.AreEqual(artifact.SourceBaselineHash, data.GetProperty("sourceBaselineHash").GetString());
        Assert.AreEqual(artifact.WorkspaceBoundaryHash, data.GetProperty("workspaceBoundaryHash").GetString());
        Assert.AreEqual(artifact.ValidationPlanId, data.GetProperty("validationPlanId").GetString());
        Assert.AreEqual(artifact.ValidationPlanHash, data.GetProperty("validationPlanHash").GetString());
        Assert.AreEqual(artifact.PatchHash, data.GetProperty("patchHash").GetString());
        Assert.AreEqual(artifact.ChangeSetHash, data.GetProperty("changeSetHash").GetString());
        Assert.AreEqual(artifact.FileChanges[0].Path, data.GetProperty("fileChanges")[0].GetProperty("path").GetString());
        Assert.AreEqual(artifact.FileChanges[0].NormalizedDiff, data.GetProperty("fileChanges")[0].GetProperty("normalizedDiff").GetString());
        Assert.AreEqual(artifact.EvidenceReferences[0], data.GetProperty("evidenceReferences")[0].GetString());
        Assert.AreEqual(artifact.BoundaryMaxims[0], data.GetProperty("boundaryMaxims")[0].GetString());
        StringAssert.Contains(text, "Patch artifact read API is read-only.");
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_GetByIdIsProjectScoped()
    {
        using var client = await AuthedClientAsync();
        var artifact = ValidArtifact("project-scope");
        await SeedAsync(artifact);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/patch-artifacts/{artifact.PatchArtifactId}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_CanListByDryRunReceiptHash()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-dry-run-receipt-pr190";
        var first = ValidArtifact("receipt-a") with { ProjectId = projectId, DryRunReceiptHash = hash };
        var second = ValidArtifact("receipt-b") with { ProjectId = projectId, DryRunReceiptHash = hash };
        var otherHash = ValidArtifact("receipt-c") with { ProjectId = projectId, DryRunReceiptHash = "sha256:other-receipt-pr190" };
        var otherProject = ValidArtifact("receipt-d") with { ProjectId = Guid.NewGuid(), DryRunReceiptHash = hash };
        await SeedAsync(first, second, otherHash, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/patch-artifacts/by-dry-run-receipt-hash/{hash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.PatchArtifactId, second.PatchArtifactId);
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_CanListByDryRunAuditHash()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-dry-run-audit-pr190";
        var first = ValidArtifact("audit-a") with { ProjectId = projectId, DryRunAuditHash = hash };
        var second = ValidArtifact("audit-b") with { ProjectId = projectId, DryRunAuditHash = hash };
        var otherHash = ValidArtifact("audit-c") with { ProjectId = projectId, DryRunAuditHash = "sha256:other-audit-pr190" };
        var otherProject = ValidArtifact("audit-d") with { ProjectId = Guid.NewGuid(), DryRunAuditHash = hash };
        await SeedAsync(first, second, otherHash, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/patch-artifacts/by-dry-run-audit-hash/{hash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.PatchArtifactId, second.PatchArtifactId);
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_CanListByControlledDryRunRequest()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var first = ValidArtifact("request-a") with { ProjectId = projectId, ControlledDryRunRequestId = requestId };
        var second = ValidArtifact("request-b") with { ProjectId = projectId, ControlledDryRunRequestId = requestId };
        var otherRequest = ValidArtifact("request-c") with { ProjectId = projectId, ControlledDryRunRequestId = Guid.NewGuid() };
        var otherProject = ValidArtifact("request-d") with { ProjectId = Guid.NewGuid(), ControlledDryRunRequestId = requestId };
        await SeedAsync(first, second, otherRequest, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/patch-artifacts/by-controlled-dry-run-request/{requestId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.PatchArtifactId, second.PatchArtifactId);
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_CanListBySubject()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var subjectKind = "PatchProposal";
        var subjectId = "patch-proposal-pr190";
        var first = ValidArtifact("subject-a") with { ProjectId = projectId, SubjectKind = subjectKind, SubjectId = subjectId };
        var second = ValidArtifact("subject-b") with { ProjectId = projectId, SubjectKind = subjectKind, SubjectId = subjectId };
        var otherSubject = ValidArtifact("subject-c") with { ProjectId = projectId, SubjectKind = subjectKind, SubjectId = "patch-proposal-other" };
        var otherProject = ValidArtifact("subject-d") with { ProjectId = Guid.NewGuid(), SubjectKind = subjectKind, SubjectId = subjectId };
        await SeedAsync(first, second, otherSubject, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/patch-artifacts/by-subject/{subjectKind}/{subjectId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.PatchArtifactId, second.PatchArtifactId);
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_CanListByPatchHash()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-patch-pr190";
        var matching = ValidArtifact("patch-a") with { ProjectId = projectId, PatchHash = hash };
        var otherHash = ValidArtifact("patch-b") with { ProjectId = projectId, PatchHash = "sha256:other-patch-pr190" };
        var otherProject = ValidArtifact("patch-c") with { ProjectId = Guid.NewGuid(), PatchHash = hash };
        await SeedAsync(matching, otherHash, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/patch-artifacts/by-patch-hash/{hash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.PatchArtifactId);
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_CanListBySourceBaselineHash()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-source-baseline-pr190";
        var first = ValidArtifact("baseline-a") with { ProjectId = projectId, SourceBaselineHash = hash };
        var second = ValidArtifact("baseline-b") with { ProjectId = projectId, SourceBaselineHash = hash };
        var otherHash = ValidArtifact("baseline-c") with { ProjectId = projectId, SourceBaselineHash = "sha256:other-baseline-pr190" };
        var otherProject = ValidArtifact("baseline-d") with { ProjectId = Guid.NewGuid(), SourceBaselineHash = hash };
        await SeedAsync(first, second, otherHash, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/patch-artifacts/by-source-baseline-hash/{hash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.PatchArtifactId, second.PatchArtifactId);
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_RejectsUnsafeRouteValues()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        foreach (var routeValue in new[] { "raw%20prompt", "chain-of-thought", "secret", "source%20applied", "release%20ready" })
        {
            var response = await client.GetAsync($"/api/v1/projects/{projectId}/patch-artifacts/by-patch-hash/{routeValue}");
            var json = await ReadJsonAsync(response);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"{routeValue}: {json.RootElement}");
            Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
            AssertReadOnlyEnvelope(json.RootElement);
        }
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_RejectsBlankRouteValues()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/patch-artifacts/by-patch-hash/%20");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        AssertNoSourceApplyAuthority(text);
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_DoesNotExposeSourceApplyAuthority()
    {
        using var client = await AuthedClientAsync();
        var artifact = ValidArtifact("authority-json");
        await SeedAsync(artifact);

        var response = await client.GetAsync($"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/{artifact.PatchArtifactId}");
        var text = (await ReadJsonAsync(response)).RootElement.ToString();

        AssertNoSourceApplyAuthority(text);
    }

    [TestMethod]
    public async Task PatchArtifactReadApi_AllResponsesAreReadOnly()
    {
        using var client = await AuthedClientAsync();
        var artifact = ValidArtifact("all-read-only");
        await SeedAsync(artifact);

        var urls = new[]
        {
            $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/{artifact.PatchArtifactId}",
            $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-dry-run-receipt-hash/{artifact.DryRunReceiptHash}",
            $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-dry-run-audit-hash/{artifact.DryRunAuditHash}",
            $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-controlled-dry-run-request/{artifact.ControlledDryRunRequestId}",
            $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-subject/{artifact.SubjectKind}/{artifact.SubjectId}",
            $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-patch-hash/{artifact.PatchHash}",
            $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-source-baseline-hash/{artifact.SourceBaselineHash}"
        };

        foreach (var url in urls)
        {
            var response = await client.GetAsync(url);
            var json = await ReadJsonAsync(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
            AssertReadOnlyEnvelope(json.RootElement);
        }
    }

    [TestMethod]
    public void PatchArtifactReadApi_HasOnlyGetRoutes()
    {
        var methods = ReadRouteMethods().ToArray();

        Assert.AreEqual(7, methods.Length);
        Assert.IsTrue(methods.All(method => method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void PatchArtifactReadApi_DoesNotCreatePatchArtifact() =>
        AssertNoProductionTokens("CreatePatchArtifactAsync", "PatchArtifactCreator", "BuildPatchArtifactFromDryRun");

    [TestMethod]
    public void PatchArtifactReadApi_DoesNotApplySourceOrContinueWorkflow() =>
        AssertNoProductionTokens("ApplySourceAsync", "SourceApplyService", "ControlledSourceApply", "ContinueWorkflowAsync", "ApproveReleaseAsync", "ReleaseReady = true", "CanApplySource = true");

    [TestMethod]
    public void PatchArtifactReadApi_DoesNotAddCliUi()
    {
        foreach (var file in Pr190ChangedFiles())
        {
            foreach (var token in new[] { "Cli", "Tauri", "UI" })
            {
                Assert.IsFalse(file.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR190 must not add {token}: {file}");
            }
        }
    }

    [TestMethod]
    public void PatchArtifactReadApi_DoesNotCallModelsAgentsMemoryRetrieval() =>
        AssertNoProductionTokens("LLM", "model call", "AgentDispatch", "ToolExecution", "PromoteMemory", "ActivateRetrieval", "Vector", "Embedding", "Weaviate");

    [TestMethod]
    public void PatchArtifactReadApi_UsesQueryServiceNotSqlStoreDirectlyInController()
    {
        var controller = File.ReadAllText(ControllerPath());

        StringAssert.Contains(controller, "IPatchArtifactQueryService");
        Assert.IsFalse(controller.Contains("SqlPatchArtifactStore", StringComparison.Ordinal), "Controller must not depend on SQL store directly.");
        Assert.IsFalse(controller.Contains("IDbConnection", StringComparison.Ordinal), "Controller must not depend on DB connection directly.");
        Assert.IsFalse(controller.Contains("IPatchArtifactStore", StringComparison.Ordinal), "Controller must not depend on patch artifact store directly.");
    }

    [TestMethod]
    public void PatchArtifactReadApi_QueryServiceUsesPatchArtifactStoreOnlyForReads()
    {
        var queryService = File.ReadAllText(QueryServicePath());

        StringAssert.Contains(queryService, "IPatchArtifactStore");
        StringAssert.Contains(queryService, "_store.GetAsync");
        StringAssert.Contains(queryService, "_store.ListByDryRunReceiptHashAsync");
        StringAssert.Contains(queryService, "_store.ListByDryRunAuditHashAsync");
        StringAssert.Contains(queryService, "_store.ListByControlledDryRunRequestAsync");
        StringAssert.Contains(queryService, "_store.ListBySubjectAsync");
        StringAssert.Contains(queryService, "_store.ListByPatchHashAsync");
        StringAssert.Contains(queryService, "_store.ListBySourceBaselineHashAsync");
        Assert.IsFalse(queryService.Contains(".SaveAsync", StringComparison.Ordinal), "Query service must not save patch artifacts.");
        Assert.IsFalse(queryService.Contains("CreatePatchArtifactAsync", StringComparison.Ordinal), "Query service must not create patch artifacts.");
        Assert.IsFalse(queryService.Contains("ApplySourceAsync", StringComparison.Ordinal), "Query service must not apply source.");
    }

    [TestMethod]
    public void PatchArtifactReadApi_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR190_PATCH_ARTIFACT_READ_API.md"));

        foreach (var statement in new[]
        {
            "PR190 adds the Patch Artifact Read API.",
            "This PR exposes project-scoped read-only endpoints for persisted PatchArtifact records.",
            "This PR does not create patch artifacts.",
            "This PR does not validate patch artifacts as source-apply authority.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "Patch artifact read API is read-only.",
            "Patch artifact read API is not patch artifact creation.",
            "Patch artifact read API is not source apply.",
            "Patch artifact read API is not rollback.",
            "Patch artifact read API is not workflow continuation.",
            "Patch artifact read API is not release readiness.",
            "Patch artifact read API does not authorize source mutation by itself.",
            "Reading a patch artifact does not authorize source mutation.",
            "Patch artifact must still be reviewed before source apply.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block S target is Patch Artifact Creator.",
            "PR191 - Patch Artifact Creator",
            "PR190 opens the package window. It does not ship or apply it."
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

    private static async Task SeedAsync(params PatchArtifact[] artifacts)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPatchArtifactStore>();
        foreach (var artifact in artifacts)
        {
            await store.SaveAsync(artifact);
        }
    }

    private static async Task<int> CountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.PatchArtifact");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static PatchArtifact ValidArtifact(string suffix) => new()
    {
        PatchArtifactId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        PatchArtifactKind = "UnifiedDiffPackage",
        ControlledDryRunRequestId = Guid.NewGuid(),
        DryRunExecutionAuditId = Guid.NewGuid(),
        DryRunAuditHash = $"sha256:dry-run-audit-{suffix}",
        DryRunReceiptHash = $"sha256:dry-run-receipt-{suffix}",
        PolicySatisfactionId = Guid.NewGuid(),
        PolicySatisfactionHash = $"sha256:policy-satisfaction-{suffix}",
        SubjectKind = "PatchProposal",
        SubjectId = $"patch-proposal-{suffix}",
        SubjectHash = $"sha256:subject-{suffix}",
        SourceSnapshotReference = $"source-snapshot:{suffix}",
        SourceBaselineHash = $"sha256:source-baseline-{suffix}",
        WorkspaceBoundaryHash = $"sha256:workspace-boundary-{suffix}",
        ValidationPlanId = $"validation-plan-{suffix}",
        ValidationPlanHash = $"sha256:validation-plan-{suffix}",
        PatchHash = $"sha256:patch-{suffix}",
        ChangeSetHash = $"sha256:change-set-{suffix}",
        FileChanges = [ModifyChange($"src/{suffix}.cs")],
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        EvidenceReferences = [$"controlled-dry-run-receipt:{suffix}"],
        BoundaryMaxims = ["Patch artifact read API is read-only."],
        Boundary = PatchArtifactBoundaryText.Boundary
    };

    private static PatchArtifactFileChange ModifyChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Modify",
        BeforeContentHash = $"sha256:before-{path}",
        AfterContentHash = $"sha256:after-{path}",
        DiffHash = $"sha256:diff-{path}",
        NormalizedDiff = $"modify {path}",
        IsBinary = false
    };

    private static void AssertItems(JsonElement root, params Guid[] expectedIds)
    {
        AssertReadOnlyEnvelope(root);
        var ids = root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("patchArtifactId").GetGuid()).ToArray();
        CollectionAssert.AreEquivalent(expectedIds, ids);
    }

    private static void AssertReadOnlyEnvelope(JsonElement root)
    {
        Assert.IsFalse(root.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(root.GetProperty("humanApprovalRequired").GetBoolean());
        StringAssert.Contains(root.GetProperty("warnings")[0].GetString(), "Patch artifact read API is read-only.");
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("patchArtifactReadIsCreation").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("patchArtifactReadAppliesSource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("patchArtifactReadExecutesRollback").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("patchArtifactReadContinuesWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("patchArtifactReadApprovesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("patchArtifactReadAuthorizesSourceMutation").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
    }

    private static void AssertNoSourceApplyAuthority(string text)
    {
        foreach (var token in new[]
        {
            "canApplySource",
            "sourceApplyApproved",
            "appliedAtUtc",
            "applyResult",
            "canContinueWorkflow",
            "canApproveRelease",
            "releaseReady",
            "mutationOccurred\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained source apply authority token: {token}");
        }
    }

    private static IEnumerable<MethodInfo> ReadRouteMethods()
    {
        var routeNames = new[]
        {
            "Get",
            "ListByDryRunReceiptHash",
            "ListByDryRunAuditHash",
            "ListByControlledDryRunRequest",
            "ListBySubject",
            "ListByPatchHash",
            "ListBySourceBaselineHash"
        };

        return typeof(PatchArtifactsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(PatchArtifactsV1Controller))
            .Where(method => routeNames.Contains(method.Name, StringComparer.Ordinal));
    }

    private static void AssertNoProductionTokens(params string[] tokens)
    {
        foreach (var file in Pr190ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden token found in {file}: {token}");
            }
        }
    }

    private static string[] Pr190ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            ControllerPath(),
            QueryServicePath(),
            Path.Combine(root, "IronDev.Core", "Governance", "PatchArtifactReadModels.cs")
        ];
    }

    private static string[] Pr190ChangedFiles()
    {
        return
        [
            "IronDev.Core/Governance/PatchArtifactReadModels.cs",
            "IronDev.Infrastructure/Governance/PatchArtifactQueryService.cs",
            "IronDev.Api/Controllers/PatchArtifactsV1Controller.cs",
            "IronDev.Api/Program.cs",
            "IronDev.IntegrationTests.Api/ApiTestBase.cs",
            "Docs/receipts/PR190_PATCH_ARTIFACT_READ_API.md",
            "IronDev.IntegrationTests.Api/PatchArtifactReadApiTests.cs"
        ];
    }

    private static string ControllerPath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "PatchArtifactsV1Controller.cs");

    private static string QueryServicePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "PatchArtifactQueryService.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
