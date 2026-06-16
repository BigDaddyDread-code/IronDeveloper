using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class PolicySatisfactionCreateApiTests : ApiTestBase
{
    private static readonly Guid AcceptedApprovalId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 6, 16, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_CanCreatePolicySatisfactionRecord()
    {
        using var client = await AuthedClientAsync();
        var before = await CountPolicySatisfactionsAsync();
        var request = ValidRequest(ProjectId);

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{ProjectId}/policy-satisfactions", request);
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();
        var after = await CountPolicySatisfactionsAsync();

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, text);
        Assert.AreEqual(before + 1, after);
        Assert.AreEqual("created", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("humanApprovalRequired").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));

        var data = json.RootElement.GetProperty("data");
        Assert.AreNotEqual(Guid.Empty, data.GetProperty("policySatisfactionId").GetGuid());
        Assert.AreEqual(ProjectId, data.GetProperty("projectId").GetGuid());
        Assert.AreEqual(request.PolicyRequirement!.PolicyCode, data.GetProperty("policyCode").GetString());
        Assert.AreEqual(request.PolicyRequirement.PolicyVersion, data.GetProperty("policyVersion").GetString());
        Assert.AreEqual(request.PolicyRequirement.SubjectKind, data.GetProperty("subjectKind").GetString());
        Assert.AreEqual(request.PolicyRequirement.SubjectId, data.GetProperty("subjectId").GetString());
        Assert.AreEqual(request.PolicyRequirement.SubjectHash, data.GetProperty("subjectHash").GetString());
        Assert.AreEqual(request.PolicyRequirement.CapabilityCode, data.GetProperty("capabilityCode").GetString());
        Assert.AreEqual(AcceptedApprovalId, data.GetProperty("acceptedApprovalId").GetGuid());
        Assert.AreEqual(request.PolicyRequirement.ApprovalRequirementHash, data.GetProperty("approvalRequirementHash").GetString());
        Assert.AreEqual(request.EvidenceReferences![0], data.GetProperty("evidenceReferences")[0].GetString());
        Assert.AreEqual(request.BoundaryMaxims![0], data.GetProperty("boundaryMaxims")[0].GetString());
        AssertNoAuthoritySpendLanguage(text);
    }

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_RejectsClientOwnedServerFields()
    {
        await AssertBadRequestNoRowAsync(new
        {
            policySatisfactionId = Guid.NewGuid(),
            projectId = Guid.NewGuid(),
            satisfiedAtUtc = DateTimeOffset.UtcNow,
            createdAtUtc = DateTimeOffset.UtcNow,
            canApplySource = true,
            canContinueWorkflow = true,
            releaseReady = true,
            policyRequirement = ValidRequirement(ProjectId),
            approvalSatisfactionEvaluation = ValidApprovalEvaluation(),
            policyRequirementHash = PolicyRequirementHash.Compute(ValidRequirement(ProjectId)),
            correlationId = "correlation-pr178",
            causationId = "approval-satisfaction-pr178",
            evidenceReferences = EvidenceReferences(),
            boundaryMaxims = BoundaryMaxims()
        });
    }

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_UsesRouteProjectId() =>
        await AssertBadRequestNoRowAsync(ValidRequest(ProjectId) with { PolicyRequirement = ValidRequirement(Guid.NewGuid()) });

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_RejectsUnsatisfiedPolicyEvaluation() =>
        await AssertConflictNoRowAsync(ValidRequest(ProjectId) with { ApprovalSatisfactionEvaluation = ValidApprovalEvaluation() with { IsSatisfied = false } });

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_RejectsApprovalEvaluationWithIssues() =>
        await AssertConflictNoRowAsync(ValidRequest(ProjectId) with
        {
            ApprovalSatisfactionEvaluation = ValidApprovalEvaluation() with
            {
                Issues = [new ApprovalSatisfactionIssue("APPROVAL_TARGET_HASH_MISMATCH", "approvalTargetHash", "Mismatch.")]
            }
        });

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_RejectsMissingAcceptedApprovalId() =>
        await AssertConflictNoRowAsync(ValidRequest(ProjectId) with { ApprovalSatisfactionEvaluation = ValidApprovalEvaluation() with { AcceptedApprovalId = null } });

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_RejectsPolicyRequirementHashMismatch() =>
        await AssertBadRequestNoRowAsync(ValidRequest(ProjectId) with { PolicyRequirementHash = "sha256:9999999999999999999999999999999999999999999999999999999999999999" });

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_RejectsExpiredPolicyRequirement() =>
        await AssertConflictNoRowAsync(ValidRequest(ProjectId) with { PolicyRequirement = ValidRequirement(ProjectId) with { ExpiresAtUtc = EvaluatedAtUtc } });

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_RejectsMissingEvidenceReferences() =>
        await AssertBadRequestNoRowAsync(ValidRequest(ProjectId) with { EvidenceReferences = [] });

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_RejectsMissingBoundaryMaxims() =>
        await AssertBadRequestNoRowAsync(ValidRequest(ProjectId) with { BoundaryMaxims = [] });

    [TestMethod]
    [DataRow("chain-of-thought")]
    [DataRow("private reasoning")]
    [DataRow("raw prompt")]
    [DataRow("scratchpad")]
    public async Task PolicySatisfactionCreateApi_RejectsPrivateReasoningMaterial(string marker) =>
        await AssertBadRequestNoRowAsync(ValidRequest(ProjectId) with { EvidenceReferences = [$"approval-satisfaction:{marker}"] });

    [TestMethod]
    [DataRow("applies source")]
    [DataRow("continues workflow")]
    [DataRow("approves release")]
    [DataRow("release ready")]
    public async Task PolicySatisfactionCreateApi_RejectsExecutionAuthorityClaims(string marker) =>
        await AssertBadRequestNoRowAsync(ValidRequest(ProjectId) with { BoundaryMaxims = [$"This says {marker}."] });

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_DoesNotApplySourceOrRunAnything()
    {
        using var client = await AuthedClientAsync();
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{ProjectId}/policy-satisfactions", ValidRequest(ProjectId));
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, text);
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("policySatisfactionCreateRunsDryRun").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("policySatisfactionCreateCreatesPatchArtifact").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("policySatisfactionCreateAppliesSource").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("policySatisfactionCreateContinuesWorkflow").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("policySatisfactionCreateApprovesRelease").GetBoolean());
        AssertNoForbiddenImplementationTokens();
    }

    [TestMethod]
    public void PolicySatisfactionCreateApi_ReadRoutesRemainGetOnly()
    {
        foreach (var method in typeof(PolicySatisfactionsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(PolicySatisfactionsV1Controller))
            .Where(method => new[] { "Get", "ListBySubject", "ListByAcceptedApproval", "ListByCorrelation" }.Contains(method.Name)))
        {
            Assert.IsTrue(method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any(), $"Read route is not GET: {method.Name}");
            Assert.IsFalse(method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any(), $"Read route unexpectedly used POST: {method.Name}");
        }
    }

    [TestMethod]
    public void PolicySatisfactionCreateApi_AddsExactlyOnePostRoute()
    {
        var methods = typeof(PolicySatisfactionsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(PolicySatisfactionsV1Controller))
            .ToArray();

        var create = methods.Single(method => method.Name == "Create");
        Assert.IsTrue(create.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any());
        Assert.AreEqual(1, methods.Count(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_ResponseBoundarySaysCreatedRecordIsNotExecutionAuthority()
    {
        using var client = await AuthedClientAsync();
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{ProjectId}/policy-satisfactions", ValidRequest(ProjectId));
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, text);
        StringAssert.Contains(text, "Created policy satisfaction does not authorize execution by itself.");
        StringAssert.Contains(text, "Policy satisfaction record creation is not source apply.");
        StringAssert.Contains(text, "Policy satisfaction record creation is not workflow continuation.");
        StringAssert.Contains(text, "Policy satisfaction record creation is not release readiness.");
    }

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_CrossProjectAcceptedApprovalIsRejected() =>
        await AssertBadRequestNoRowAsync(ValidRequest(ProjectId) with { PolicyRequirement = ValidRequirement(Guid.NewGuid()) });

    [TestMethod]
    public void PolicySatisfactionCreateApi_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR178_GOVERNED_POLICY_SATISFACTION_CREATE_API.md"));

        foreach (var statement in new[]
        {
            "PR178 adds the Governed Policy Satisfaction Create API.",
            "This PR can create durable policy satisfaction records after deterministic policy requirement satisfaction evaluation succeeds.",
            "This PR does not authorize execution.",
            "This PR does not run dry-runs.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add UI.",
            "This PR does not add CLI.",
            "Policy satisfaction record creation is not dry-run execution.",
            "Policy satisfaction record creation is not patch artifact creation.",
            "Policy satisfaction record creation is not source apply.",
            "Policy satisfaction record creation is not rollback.",
            "Policy satisfaction record creation is not workflow continuation.",
            "Policy satisfaction record creation is not release readiness.",
            "Created policy satisfaction does not authorize execution by itself.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block R target is Controlled Dry-Run Requirement Contract.",
            "PR179 - Controlled Dry-Run Requirement Contract"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void PolicySatisfactionCreateApi_DoesNotAddUiCliRuntimeScheduler()
    {
        foreach (var file in Pr178ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in new[]
            {
                "IHostedService",
                "BackgroundService",
                "Scheduler",
                "AgentDispatch",
                "ModelBacked",
                "PromoteMemory",
                "ActivateRetrieval",
                "RunDryRunAsync",
                "CreatePatchArtifactAsync",
                "ApplySourceAsync",
                "ContinueWorkflowAsync",
                "ApproveReleaseAsync"
            })
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden token in {file}: {token}");
            }
        }
    }

    [TestMethod]
    public async Task PolicySatisfactionCreateApi_CreatedRecordCanBeReadBackThroughReadApi()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync($"/api/v1/projects/{ProjectId}/policy-satisfactions", ValidRequest(ProjectId));
        var createdJson = await ReadJsonAsync(create);
        var id = createdJson.RootElement.GetProperty("policySatisfactionId").GetGuid();

        var read = await client.GetAsync($"/api/v1/projects/{ProjectId}/policy-satisfactions/{id}");
        var readJson = await ReadJsonAsync(read);
        var text = readJson.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode, text);
        Assert.AreEqual("found", readJson.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(readJson.RootElement.GetProperty("mutationOccurred").GetBoolean());
        StringAssert.Contains(text, "Reading persisted policy satisfaction does not authorize execution by itself.");
    }

    private static async Task AssertBadRequestNoRowAsync(object request)
    {
        using var client = await AuthedClientAsync();
        var before = await CountPolicySatisfactionsAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{ProjectId}/policy-satisfactions", request);
        var text = await response.Content.ReadAsStringAsync();
        var after = await CountPolicySatisfactionsAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        Assert.AreEqual(before, after, text);
    }

    private static async Task AssertConflictNoRowAsync(object request)
    {
        using var client = await AuthedClientAsync();
        var before = await CountPolicySatisfactionsAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{ProjectId}/policy-satisfactions", request);
        var text = await response.Content.ReadAsStringAsync();
        var after = await CountPolicySatisfactionsAsync();

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, text);
        Assert.AreEqual(before, after, text);
    }

    private static PolicySatisfactionCreateRequest ValidRequest(Guid projectId)
    {
        var requirement = ValidRequirement(projectId);
        return new PolicySatisfactionCreateRequest(
            requirement,
            ValidApprovalEvaluation(),
            PolicyRequirementHash.Compute(requirement),
            DateTimeOffset.UtcNow.AddDays(7),
            "correlation-pr178",
            "approval-satisfaction-pr178",
            EvidenceReferences(),
            BoundaryMaxims(),
            "client-request-pr178");
    }

    private static PolicyRequirement ValidRequirement(Guid projectId) =>
        new()
        {
            ProjectId = projectId,
            PolicyCode = "source-apply-policy",
            PolicyVersion = "2026-06-16.v1",
            SubjectKind = "patch-artifact",
            SubjectId = "patch-artifact-pr178",
            SubjectHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CapabilityCode = "SOURCE_APPLY",
            ApprovalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            ApprovalTargetId = "patch-artifact-pr178",
            ApprovalTargetHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ApprovalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            ApprovalRequirementHash = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            EvaluatedAtUtc = EvaluatedAtUtc,
            ExpiresAtUtc = EvaluatedAtUtc.AddDays(1),
            RequiredEvidenceReferences = EvidenceReferences(),
            RequiredBoundaryMaxims = BoundaryMaxims()
        };

    private static ApprovalSatisfactionEvaluation ValidApprovalEvaluation() =>
        new()
        {
            IsSatisfied = true,
            AcceptedApprovalId = AcceptedApprovalId,
            EvidenceReferences = EvidenceReferences(),
            BoundaryMaxims = BoundaryMaxims(),
            Issues = []
        };

    private static IReadOnlyList<string> EvidenceReferences() =>
    [
        "accepted-approval:" + AcceptedApprovalId,
        "approval-satisfaction:evaluation-pr178"
    ];

    private static IReadOnlyList<string> BoundaryMaxims() =>
    [
        "Accepted approval record is not policy satisfaction.",
        "Satisfied approval requirement is not policy satisfaction.",
        "Satisfied policy requirement does not authorize execution."
    ];

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task<int> CountPolicySatisfactionsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.PolicySatisfaction");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("policySatisfactionCreateRunsDryRun").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("policySatisfactionCreateCreatesPatchArtifact").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("policySatisfactionCreateAppliesSource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("policySatisfactionCreateExecutesRollback").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("policySatisfactionCreateContinuesWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("policySatisfactionCreateApprovesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("policySatisfactionCreateAuthorizesExecution").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());
    }

    private static void AssertNoAuthoritySpendLanguage(string text)
    {
        foreach (var token in new[]
        {
            "policySatisfactionCreateRunsDryRun\":true",
            "policySatisfactionCreateCreatesPatchArtifact\":true",
            "policySatisfactionCreateAppliesSource\":true",
            "policySatisfactionCreateContinuesWorkflow\":true",
            "policySatisfactionCreateApprovesRelease\":true",
            "policySatisfactionCreateAuthorizesExecution\":true",
            "canApplySource\":true",
            "releaseReady\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained misleading authority language: {token}");
        }
    }

    private static void AssertNoForbiddenImplementationTokens()
    {
        foreach (var file in Pr178ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in new[]
            {
                "RunDryRunAsync",
                "CreatePatchArtifactAsync",
                "ApplySourceAsync",
                "ContinueWorkflowAsync",
                "ApproveReleaseAsync",
                "ReleaseReady = true",
                "CanApplySource = true"
            })
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden implementation token found in {file}: {token}");
            }
        }
    }

    private static IReadOnlyList<string> Pr178ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Api", "Controllers", "PolicySatisfactionsV1Controller.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "PolicySatisfactionCreateModels.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "PolicySatisfactionCreateService.cs")
        ];
    }

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
}
