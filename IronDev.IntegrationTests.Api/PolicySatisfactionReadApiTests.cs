using System.Net;
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
public sealed class PolicySatisfactionReadApiTests : ApiTestBase
{
    private static readonly DateTimeOffset ApprovalEvaluatedAtUtc = new(2026, 6, 16, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SatisfiedAtUtc = new(2026, 6, 16, 14, 1, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PolicySatisfactionReadApi_CanGetPolicySatisfactionById()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord();
        await SeedAsync(record);
        var before = await CountAsync();

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/policy-satisfactions/{record.PolicySatisfactionId}");
        var json = await ReadJsonAsync(response);
        var after = await CountAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not create or mutate policy satisfaction records.");
        Assert.AreEqual("found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));

        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(record.PolicySatisfactionId, data.GetProperty("policySatisfactionId").GetGuid());
        Assert.AreEqual(record.ProjectId, data.GetProperty("projectId").GetGuid());
        Assert.AreEqual(record.PolicyCode, data.GetProperty("policyCode").GetString());
        Assert.AreEqual(record.PolicyVersion, data.GetProperty("policyVersion").GetString());
        Assert.AreEqual(record.SubjectHash, data.GetProperty("subjectHash").GetString());
        Assert.AreEqual(record.AcceptedApprovalId, data.GetProperty("acceptedApprovalId").GetGuid());
        Assert.AreEqual(record.EvidenceReferences[0], data.GetProperty("evidenceReferences")[0].GetString());
        Assert.AreEqual(record.BoundaryMaxims[0], data.GetProperty("boundaryMaxims")[0].GetString());
        Assert.AreEqual(PolicySatisfactionReadBoundaryText.AuthorityBoundary, data.GetProperty("authorityBoundary").GetString());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task PolicySatisfactionReadApi_Returns404ForUnknownRecord()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/policy-satisfactions/{Guid.NewGuid()}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("not_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task PolicySatisfactionReadApi_DoesNotLeakAcrossProjects()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord();
        await SeedAsync(record);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/policy-satisfactions/{record.PolicySatisfactionId}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task PolicySatisfactionReadApi_CanListBySubjectWithinProject()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var subjectKind = "patch-artifact";
        var subjectId = "patch-artifact-pr177";
        var matchingFirst = ValidRecord(projectId) with { PolicySatisfactionId = Guid.NewGuid(), SubjectKind = subjectKind, SubjectId = subjectId, SatisfiedAtUtc = SatisfiedAtUtc.AddMinutes(1) };
        var matchingSecond = ValidRecord(projectId) with { PolicySatisfactionId = Guid.NewGuid(), SubjectKind = subjectKind, SubjectId = subjectId, SatisfiedAtUtc = SatisfiedAtUtc.AddMinutes(2) };
        var otherSubject = ValidRecord(projectId) with { PolicySatisfactionId = Guid.NewGuid(), SubjectKind = subjectKind, SubjectId = "patch-artifact-other" };
        var otherProject = ValidRecord(Guid.NewGuid()) with { PolicySatisfactionId = Guid.NewGuid(), SubjectKind = subjectKind, SubjectId = subjectId };
        await SeedAsync(matchingFirst, matchingSecond, otherSubject, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/policy-satisfactions/by-subject/{subjectKind}/{subjectId}");
        var json = await ReadJsonAsync(response);
        var ids = json.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("policySatisfactionId").GetGuid()).ToArray();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        CollectionAssert.AreEquivalent(new[] { matchingFirst.PolicySatisfactionId, matchingSecond.PolicySatisfactionId }, ids);
    }

    [TestMethod]
    public async Task PolicySatisfactionReadApi_CanListByAcceptedApprovalWithinProject()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var acceptedApprovalId = Guid.NewGuid();
        var matchingFirst = ValidRecord(projectId) with { PolicySatisfactionId = Guid.NewGuid(), AcceptedApprovalId = acceptedApprovalId, SatisfiedAtUtc = SatisfiedAtUtc.AddMinutes(1) };
        var matchingSecond = ValidRecord(projectId) with { PolicySatisfactionId = Guid.NewGuid(), AcceptedApprovalId = acceptedApprovalId, SatisfiedAtUtc = SatisfiedAtUtc.AddMinutes(2) };
        var otherApproval = ValidRecord(projectId) with { PolicySatisfactionId = Guid.NewGuid(), AcceptedApprovalId = Guid.NewGuid() };
        var otherProject = ValidRecord(Guid.NewGuid()) with { PolicySatisfactionId = Guid.NewGuid(), AcceptedApprovalId = acceptedApprovalId };
        await SeedAsync(matchingFirst, matchingSecond, otherApproval, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/policy-satisfactions/by-accepted-approval/{acceptedApprovalId}");
        var json = await ReadJsonAsync(response);
        var ids = json.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("policySatisfactionId").GetGuid()).ToArray();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        CollectionAssert.AreEquivalent(new[] { matchingFirst.PolicySatisfactionId, matchingSecond.PolicySatisfactionId }, ids);
    }

    [TestMethod]
    public async Task PolicySatisfactionReadApi_CanListByCorrelationWithinProject()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var correlationId = "correlation-pr177";
        var matchingFirst = ValidRecord(projectId) with { PolicySatisfactionId = Guid.NewGuid(), CorrelationId = correlationId, SatisfiedAtUtc = SatisfiedAtUtc.AddMinutes(1) };
        var matchingSecond = ValidRecord(projectId) with { PolicySatisfactionId = Guid.NewGuid(), CorrelationId = correlationId, SatisfiedAtUtc = SatisfiedAtUtc.AddMinutes(2) };
        var otherCorrelation = ValidRecord(projectId) with { PolicySatisfactionId = Guid.NewGuid(), CorrelationId = "correlation-other" };
        var otherProject = ValidRecord(Guid.NewGuid()) with { PolicySatisfactionId = Guid.NewGuid(), CorrelationId = correlationId };
        await SeedAsync(matchingFirst, matchingSecond, otherCorrelation, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/policy-satisfactions/by-correlation/{correlationId}");
        var json = await ReadJsonAsync(response);
        var ids = json.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("policySatisfactionId").GetGuid()).ToArray();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        CollectionAssert.AreEquivalent(new[] { matchingFirst.PolicySatisfactionId, matchingSecond.PolicySatisfactionId }, ids);
    }

    [TestMethod]
    public async Task PolicySatisfactionReadApi_RejectsUnsafeLookupValues()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/policy-satisfactions/by-subject/patch-artifact/raw%20prompt");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public void PolicySatisfactionReadApi_UsesGetOnlyRoutes()
    {
        var methods = ReadRouteMethods().ToArray();

        Assert.AreEqual(4, methods.Count(method => method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void PolicySatisfactionReadApi_DoesNotCreatePolicySatisfaction()
    {
        var controller = File.ReadAllText(ControllerPath());

        foreach (var token in new[] { "CreatePolicySatisfaction", "SavePolicySatisfaction", "HttpPost", "[HttpPost]", "IPolicySatisfactionStore", ".SaveAsync" })
        {
            Assert.IsFalse(controller.Contains(token, StringComparison.Ordinal), $"Controller must not contain {token}.");
        }

        StringAssert.Contains(controller, "IPolicySatisfactionQueryService");
    }

    [TestMethod]
    public void PolicySatisfactionReadApi_DoesNotAuthorizeExecution() =>
        AssertNoProductionTokens(
            "RunDryRunAsync",
            "CreatePatchArtifactAsync",
            "ApplySourceAsync",
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync",
            "ReleaseReady = true",
            "CanApplySource = true");

    [TestMethod]
    public async Task PolicySatisfactionReadApi_ReturnsBoundaryLanguage()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord();
        await SeedAsync(record);

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/policy-satisfactions/{record.PolicySatisfactionId}");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        StringAssert.Contains(text, "Reading persisted policy satisfaction does not authorize execution by itself.");
        StringAssert.Contains(text, "Persisted policy satisfaction is not source apply.");
        StringAssert.Contains(text, "Persisted policy satisfaction is not workflow continuation.");
        StringAssert.Contains(text, "Persisted policy satisfaction is not release readiness.");
    }

    [TestMethod]
    public async Task PolicySatisfactionReadApi_DoesNotReturnRawPrivateMaterial()
    {
        var record = ValidRecord() with
        {
            SubjectId = "rawPrompt-private-target",
            EvidenceReferences = ["approval-satisfaction:chain-of-thought"],
            BoundaryMaxims = ["private reasoning boundary"]
        };
        var query = new PolicySatisfactionQueryService(new FakePolicySatisfactionStore(record));

        var read = await query.GetAsync(record.ProjectId, record.PolicySatisfactionId);
        var text = JsonSerializer.Serialize(read, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.IsNotNull(read);
        AssertNoPrivateReasoningLeak(text);
        StringAssert.Contains(text, "[redacted: sensitive policy satisfaction text]");
    }

    [TestMethod]
    public void PolicySatisfactionReadApi_ProjectScopedCorrelationIsRequired()
    {
        var controller = File.ReadAllText(ControllerPath());
        var store = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "SqlPolicySatisfactionStore.cs"));
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "Database", "migrate_policy_satisfaction.sql"));

        StringAssert.Contains(controller, "api/v1/projects/{projectId:guid}/policy-satisfactions");
        Assert.IsFalse(controller.Contains("api/v1/policy-satisfactions", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(controller, "ListByProjectAndCorrelationAsync");
        StringAssert.Contains(store, "governance.usp_PolicySatisfaction_ListByProjectAndCorrelation");
        StringAssert.Contains(sql, "governance.usp_PolicySatisfaction_ListByProjectAndCorrelation");
        StringAssert.Contains(sql, "WHERE ProjectId = @ProjectId");
    }

    [TestMethod]
    public void PolicySatisfactionReadApi_ReceiptStatesReadOnlyBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR177_POLICY_SATISFACTION_READ_API.md"));

        foreach (var statement in new[]
        {
            "PR177 adds the Policy Satisfaction Read API.",
            "This PR exposes policy satisfaction records through read-only project-scoped GET endpoints.",
            "This PR does not create policy satisfaction records.",
            "This PR does not add a policy satisfaction create API.",
            "This PR does not evaluate policy satisfaction.",
            "This PR does not run dry-runs.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add UI.",
            "This PR does not add CLI.",
            "Policy satisfaction read API is not policy satisfaction creation.",
            "Reading persisted policy satisfaction is not dry-run execution.",
            "Reading persisted policy satisfaction is not patch artifact creation.",
            "Reading persisted policy satisfaction is not source apply.",
            "Reading persisted policy satisfaction is not rollback.",
            "Reading persisted policy satisfaction is not workflow continuation.",
            "Reading persisted policy satisfaction is not release readiness.",
            "Reading persisted policy satisfaction does not authorize execution by itself.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block Q target is Governed Policy Satisfaction Create API.",
            "PR178 - Governed Policy Satisfaction Create API"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public async Task PolicySatisfactionReadApi_ReadsDoNotMutateRows()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord();
        await SeedAsync(record);
        var before = await CountAsync();

        _ = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/policy-satisfactions/{record.PolicySatisfactionId}");
        _ = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/policy-satisfactions/by-subject/{record.SubjectKind}/{record.SubjectId}");
        _ = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/policy-satisfactions/by-accepted-approval/{record.AcceptedApprovalId}");
        _ = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/policy-satisfactions/by-correlation/{record.CorrelationId}");
        var after = await CountAsync();

        Assert.AreEqual(before, after);
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task SeedAsync(params PolicySatisfactionRecord[] records)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPolicySatisfactionStore>();
        foreach (var record in records)
        {
            await store.SaveAsync(record);
        }
    }

    private static async Task<int> CountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.PolicySatisfaction");
    }

    private static PolicySatisfactionRecord ValidRecord(Guid? projectId = null) =>
        new()
        {
            PolicySatisfactionId = Guid.NewGuid(),
            ProjectId = projectId ?? Guid.NewGuid(),
            PolicyCode = "source-apply-policy",
            PolicyVersion = "2026-06-16.v1",
            SubjectKind = "patch-artifact",
            SubjectId = "patch-artifact-pr177",
            SubjectHash = "sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            CapabilityCode = "SOURCE_APPLY",
            AcceptedApprovalId = Guid.NewGuid(),
            ApprovalRequirementHash = "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
            ApprovalEvaluatedAtUtc = ApprovalEvaluatedAtUtc,
            SatisfiedAtUtc = SatisfiedAtUtc,
            ExpiresAtUtc = SatisfiedAtUtc.AddDays(7),
            CorrelationId = "correlation-pr177",
            CausationId = "approval-satisfaction-pr177",
            EvidenceReferences = ["accepted-approval:pr177", "approval-satisfaction:evaluation-pr177"],
            BoundaryMaxims =
            [
                "Policy satisfaction read API is not policy satisfaction creation.",
                "Reading persisted policy satisfaction does not authorize execution by itself."
            ]
        };

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("policySatisfactionReadIsCreation").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readingPersistedPolicySatisfactionRunsDryRun").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readingPersistedPolicySatisfactionCreatesPatchArtifact").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readingPersistedPolicySatisfactionAppliesSource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readingPersistedPolicySatisfactionExecutesRollback").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readingPersistedPolicySatisfactionContinuesWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readingPersistedPolicySatisfactionApprovesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readingPersistedPolicySatisfactionAuthorizesExecution").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());
    }

    private static void AssertNoMisleadingAuthorityLanguage(string text)
    {
        foreach (var token in new[]
        {
            "policySatisfactionReadIsCreation\":true",
            "readingPersistedPolicySatisfactionRunsDryRun\":true",
            "readingPersistedPolicySatisfactionCreatesPatchArtifact\":true",
            "readingPersistedPolicySatisfactionAppliesSource\":true",
            "readingPersistedPolicySatisfactionExecutesRollback\":true",
            "readingPersistedPolicySatisfactionContinuesWorkflow\":true",
            "readingPersistedPolicySatisfactionApprovesRelease\":true",
            "readingPersistedPolicySatisfactionAuthorizesExecution\":true",
            "mutationOccurred\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained misleading authority language: {token}");
        }
    }

    private static void AssertNoPrivateReasoningLeak(string text)
    {
        foreach (var token in new[]
        {
            "RawPrompt",
            "rawPrompt",
            "raw prompt",
            "RawCompletion",
            "rawCompletion",
            "raw completion",
            "RawToolOutput",
            "rawToolOutput",
            "raw tool output",
            "ChainOfThought",
            "chainOfThought",
            "chain-of-thought",
            "PrivateReasoning",
            "private reasoning",
            "HiddenReasoning",
            "hidden reasoning",
            "Scratchpad",
            "scratchpad",
            "PayloadJson"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained private material marker: {token}");
        }
    }

    private static void AssertNoProductionTokens(params string[] tokens)
    {
        foreach (var file in Pr177ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden implementation token found in {file}: {token}");
            }
        }
    }

    private static IReadOnlyList<string> Pr177ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            ControllerPath(),
            Path.Combine(root, "IronDev.Core", "Governance", "PolicySatisfactionReadModels.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "PolicySatisfactionQueryService.cs")
        ];
    }

    private static IEnumerable<System.Reflection.MethodInfo> ReadRouteMethods()
    {
        var readRouteNames = new[] { "Get", "ListBySubject", "ListByAcceptedApproval", "ListByCorrelation" };
        return typeof(PolicySatisfactionsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(PolicySatisfactionsV1Controller))
            .Where(method => readRouteNames.Contains(method.Name, StringComparer.Ordinal));
    }

    private static string ControllerPath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "PolicySatisfactionsV1Controller.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate repository root.");
        }

        return directory.FullName;
    }

    private sealed class FakePolicySatisfactionStore : IPolicySatisfactionStore
    {
        private readonly PolicySatisfactionRecord _record;

        public FakePolicySatisfactionStore(PolicySatisfactionRecord record) => _record = record;

        public Task SaveAsync(PolicySatisfactionRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Fake read store does not save.");

        public Task<PolicySatisfactionRecord?> GetAsync(Guid projectId, Guid policySatisfactionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PolicySatisfactionRecord?>(_record.ProjectId == projectId && _record.PolicySatisfactionId == policySatisfactionId ? _record : null);

        public Task<IReadOnlyList<PolicySatisfactionRecord>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PolicySatisfactionRecord>>([]);

        public Task<IReadOnlyList<PolicySatisfactionRecord>> ListByAcceptedApprovalAsync(Guid projectId, Guid acceptedApprovalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PolicySatisfactionRecord>>([]);

        public Task<IReadOnlyList<PolicySatisfactionRecord>> ListByProjectAndCorrelationAsync(Guid projectId, string correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PolicySatisfactionRecord>>([]);
    }
}
