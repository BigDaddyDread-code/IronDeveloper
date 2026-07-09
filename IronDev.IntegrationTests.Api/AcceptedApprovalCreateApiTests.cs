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
public sealed class AcceptedApprovalCreateApiTests : ApiTestBase
{
    [TestMethod]
    public async Task AcceptedApprovalCreateApi_CanCreateAcceptedApproval()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var before = await CountAcceptedApprovalsAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/accepted-approvals", ValidRequest());
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();
        var after = await CountAcceptedApprovalsAsync();

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, text);
        Assert.AreEqual(before + 1, after);
        Assert.AreEqual("created", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));

        var data = json.RootElement.GetProperty("data");
        Assert.AreNotEqual(Guid.Empty, data.GetProperty("acceptedApprovalId").GetGuid());
        Assert.AreEqual(projectId, data.GetProperty("projectId").GetGuid());
        Assert.AreEqual("patch-artifact-pr171", data.GetProperty("approvalTargetId").GetString());
        Assert.AreEqual("sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd", data.GetProperty("approvalTargetHash").GetString());
        Assert.AreEqual("1", data.GetProperty("approvedByActorId").GetString());
        Assert.AreEqual("Admin User", data.GetProperty("approvedByActorDisplayName").GetString());
        Assert.IsTrue(data.GetProperty("acceptedAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.AreEqual(AcceptedApprovalCreateBoundaryText.AuthorityBoundary, data.GetProperty("authorityBoundary").GetString());
        AssertNoAuthoritySpendLanguage(text);
    }

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_DoesNotAcceptClientSuppliedActor()
    {
        await AssertBadRequestNoRowAsync(new
        {
            approvedByActorId = "evil-user",
            approvedByActorDisplayName = "Fake Approver",
            approvalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            approvalTargetId = "patch-artifact-pr171",
            approvalTargetHash = "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            capabilityCode = "L4_ACCEPTED_APPROVAL_RECORD",
            approvalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(3),
            correlationId = "correlation-pr171",
            causationId = "approval-package-pr171",
            evidenceReferences = new[] { "approval-package:approval-package-pr171" },
            boundaryMaxims = new[] { "Accepted approval creation is not policy satisfaction." }
        });
    }

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_DoesNotAcceptClientSuppliedAcceptedAt() =>
        await AssertBadRequestNoRowAsync(ValidRequestWith(acceptedAtUtc: DateTimeOffset.UtcNow));

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_DoesNotAcceptClientSuppliedAcceptedApprovalId() =>
        await AssertBadRequestNoRowAsync(ValidRequestWith(acceptedApprovalId: Guid.NewGuid()));

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_CrossProjectRouteOwnsProjectId() =>
        await AssertBadRequestNoRowAsync(ValidRequestWith(projectId: Guid.NewGuid()));

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_RejectsMissingTargetHash() =>
        await AssertBadRequestNoRowAsync(ValidRequest() with { ApprovalTargetHash = " " });

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_RejectsMissingEvidenceReferences() =>
        await AssertBadRequestNoRowAsync(ValidRequest() with { EvidenceReferences = [] });

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_RejectsMissingBoundaryMaxims() =>
        await AssertBadRequestNoRowAsync(ValidRequest() with { BoundaryMaxims = [] });

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_AcceptsTheApprovalCeremonysEncodedHumanReason()
    {
        // DOGFOOD-2 finding F-L: the cockpit's approval ceremony rides the typed
        // reason as a labeled evidence entry, ENCODED to the reference alphabet.
        // This pins the UI-shaped entry against the REAL validator — the mocked
        // Playwright spec cannot prove this contract.
        using var client = await AuthedClientAsync();
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}/accepted-approvals", ValidRequest() with
        {
            EvidenceReferences =
            [
                "approval-package:approval-package-pr171",
                "human-reason:Package-reviewed-end-to-end-criteria-covered-no-findings."
            ]
        });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_RefusesFreeTextEvidenceNamingTheAlphabet()
    {
        // A refusal the operator cannot act on is only half a refusal: the
        // UNSUPPORTED_CHARACTERS message names the allowed alphabet.
        using var client = await AuthedClientAsync();
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}/accepted-approvals", ValidRequest() with
        {
            EvidenceReferences = ["human-reason: free text with spaces"]
        });
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "UNSUPPORTED_CHARACTERS");
        StringAssert.Contains(text, "letters, digits");
    }

    [TestMethod]
    [DataRow("chain-of-thought")]
    [DataRow("raw prompt")]
    [DataRow("scratchpad")]
    [DataRow("private reasoning")]
    public async Task AcceptedApprovalCreateApi_RejectsPrivateReasoningMarkers(string marker) =>
        await AssertBadRequestNoRowAsync(ValidRequest() with { EvidenceReferences = [$"approval-package:{marker}"] });

    [TestMethod]
    [DataRow("policy satisfied")]
    [DataRow("source applied")]
    [DataRow("release approved")]
    [DataRow("workflow continued")]
    [DataRow("dry-run executed")]
    [DataRow("patch artifact created")]
    public async Task AcceptedApprovalCreateApi_RejectsAuthorityEscalationClaims(string marker) =>
        await AssertBadRequestNoRowAsync(ValidRequest() with { BoundaryMaxims = [$"This says {marker}."] });

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_RejectsInvalidExpiry() =>
        await AssertBadRequestNoRowAsync(ValidRequest() with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1) });

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_DoesNotSatisfyPolicy()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/accepted-approvals", ValidRequest());
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, text);
        Assert.AreEqual(0, await CountIfTableExistsAsync("governance.PolicyDecisionEvent"));
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("acceptedApprovalCreateSatisfiesPolicy").GetBoolean());
        StringAssert.Contains(text, "Accepted approval creation is not policy satisfaction.");
    }

    [TestMethod]
    public async Task AcceptedApprovalCreateApi_DoesNotApplySource()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/accepted-approvals", ValidRequest());
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, text);
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("acceptedApprovalCreateRunsDryRun").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("acceptedApprovalCreateCreatesPatchArtifact").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("acceptedApprovalCreateAppliesSource").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("acceptedApprovalCreateContinuesWorkflow").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("acceptedApprovalCreateApprovesRelease").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("acceptedApprovalCreateAuthorizesExecution").GetBoolean());
        AssertNoForbiddenImplementationTokens();
    }

    [TestMethod]
    public void AcceptedApprovalCreateApi_UsesPostOnlyForCreate()
    {
        var methods = typeof(AcceptedApprovalsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(AcceptedApprovalsV1Controller))
            .ToArray();

        var create = methods.Single(method => method.Name == "Create");
        Assert.IsTrue(create.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any());
        Assert.IsFalse(create.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any());
        Assert.IsFalse(create.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any());
        Assert.IsFalse(create.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any());
        Assert.AreEqual(1, methods.Count(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void AcceptedApprovalCreateApi_ReadRoutesRemainGetOnly()
    {
        foreach (var method in typeof(AcceptedApprovalsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(AcceptedApprovalsV1Controller))
            .Where(method => new[] { "Get", "ListByTarget", "ListByCorrelation" }.Contains(method.Name)))
        {
            Assert.IsTrue(method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any(), $"Read route is not GET: {method.Name}");
            Assert.IsFalse(method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any(), $"Read route unexpectedly used POST: {method.Name}");
        }
    }

    [TestMethod]
    public void AcceptedApprovalCreateApi_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR171_GOVERNED_ACCEPTED_APPROVAL_CREATE_API.md"));

        foreach (var statement in new[]
        {
            "PR171 adds the Governed Accepted Approval Create API.",
            "This PR can create accepted approval records through an authenticated, project-scoped, target-bound backend API.",
            "This PR does not satisfy policy.",
            "This PR does not run dry-runs.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add UI.",
            "This PR does not add CLI.",
            "Accepted approval creation is not policy satisfaction.",
            "Accepted approval creation is not dry-run execution.",
            "Accepted approval creation is not patch artifact creation.",
            "Accepted approval creation is not source apply.",
            "Accepted approval creation is not workflow continuation.",
            "Accepted approval creation is not release readiness.",
            "Creating an accepted approval record does not authorize execution.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block P target is accepted approval creation regression coverage or approval satisfaction evaluator."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static async Task AssertBadRequestNoRowAsync(object request)
    {
        using var client = await AuthedClientAsync();
        var before = await CountAcceptedApprovalsAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{Guid.NewGuid()}/accepted-approvals", request);
        var text = await response.Content.ReadAsStringAsync();
        var after = await CountAcceptedApprovalsAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        Assert.AreEqual(before, after, text);
    }

    private static CreateAcceptedApprovalRequest ValidRequest() =>
        new(
            AcceptedApprovalTargetKinds.PatchArtifact,
            "patch-artifact-pr171",
            "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            "L4_ACCEPTED_APPROVAL_RECORD",
            AcceptedApprovalPurposes.PolicySatisfactionInput,
            DateTimeOffset.UtcNow.AddDays(7),
            "correlation-pr171",
            "approval-package-pr171",
            ["approval-package:approval-package-pr171"],
            [
                "Accepted approval creation is not policy satisfaction.",
                "Creating an accepted approval record does not authorize execution."
            ],
            "client-request-pr171");

    private static object ValidRequestWith(
        Guid? acceptedApprovalId = null,
        Guid? projectId = null,
        DateTimeOffset? acceptedAtUtc = null) =>
        new
        {
            acceptedApprovalId,
            projectId,
            acceptedAtUtc,
            approvalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            approvalTargetId = "patch-artifact-pr171",
            approvalTargetHash = "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            capabilityCode = "L4_ACCEPTED_APPROVAL_RECORD",
            approvalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
            correlationId = "correlation-pr171",
            causationId = "approval-package-pr171",
            evidenceReferences = new[] { "approval-package:approval-package-pr171" },
            boundaryMaxims = new[] { "Accepted approval creation is not policy satisfaction." },
            clientRequestId = "client-request-pr171"
        };

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task<int> CountAcceptedApprovalsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.AcceptedApproval");
    }

    private static async Task<int> CountIfTableExistsAsync(string tableName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>($"""
            IF OBJECT_ID(N'{tableName}', N'U') IS NULL
                SELECT 0;
            ELSE
                SELECT COUNT(*) FROM {tableName};
            """);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalCreateSatisfiesPolicy").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalCreateRunsDryRun").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalCreateCreatesPatchArtifact").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalCreateAppliesSource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalCreateContinuesWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalCreateApprovesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalCreateAuthorizesExecution").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());
    }

    private static void AssertNoAuthoritySpendLanguage(string text)
    {
        foreach (var token in new[]
        {
            "acceptedApprovalCreateSatisfiesPolicy\":true",
            "acceptedApprovalCreateRunsDryRun\":true",
            "acceptedApprovalCreateCreatesPatchArtifact\":true",
            "acceptedApprovalCreateAppliesSource\":true",
            "acceptedApprovalCreateContinuesWorkflow\":true",
            "acceptedApprovalCreateApprovesRelease\":true",
            "acceptedApprovalCreateAuthorizesExecution\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained misleading authority language: {token}");
        }
    }

    private static void AssertNoForbiddenImplementationTokens()
    {
        foreach (var file in Pr171ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in new[]
            {
                "SatisfyPolicy",
                "PolicySatisfied = true",
                "CanApplySource = true",
                "ApplySourceAsync",
                "RunDryRunAsync",
                "CreatePatchArtifactAsync",
                "ContinueWorkflowAsync",
                "ApproveReleaseAsync",
                "ReleaseReady = true",
                "SourceApplyExecutor",
                "WorkflowContinuationExecutor",
                "ReleaseReadinessDecisionStore"
            })
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden implementation token found in {file}: {token}");
            }
        }
    }

    private static IReadOnlyList<string> Pr171ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Api", "Controllers", "AcceptedApprovalsV1Controller.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "AcceptedApprovalCreateModels.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "AcceptedApprovalCreateService.cs")
        ];
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
}
