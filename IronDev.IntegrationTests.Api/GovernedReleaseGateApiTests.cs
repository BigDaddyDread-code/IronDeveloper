using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[TestCategory("GovernedReleaseGateApi")]
[TestCategory("PR221")]
public sealed class GovernedReleaseGateApiTests : ApiTestBase
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 17, 22, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task GovernedReleaseGateApi_PostStoresReadyDecisionRecord()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest("api-ready");
        var before = await CountAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{request.ProjectId}/release-readiness/gate/governed", request);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        var after = await CountAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before + 1, after);
        AssertEnvelopeBoundary(json.RootElement);
        AssertNoReleaseAuthority(text);

        var data = json.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("succeeded").GetBoolean());
        Assert.IsTrue(data.GetProperty("releaseReadinessGateRan").GetBoolean());
        Assert.IsTrue(data.GetProperty("decisionRecordStored").GetBoolean());
        Assert.IsTrue(data.GetProperty("releaseReadinessEvidenceSatisfied").GetBoolean());
        Assert.IsFalse(data.GetProperty("releaseApproved").GetBoolean());
        Assert.IsFalse(data.GetProperty("deploymentApproved").GetBoolean());
        Assert.IsFalse(data.GetProperty("mergeApproved").GetBoolean());
        Assert.IsTrue(data.GetProperty("humanReviewRequiredForReleaseApproval").GetBoolean());

        var record = data.GetProperty("decisionRecord");
        Assert.AreEqual(request.GovernedReleaseGateRequestId, record.GetProperty("releaseReadinessDecisionRecordId").GetGuid());
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, record.GetProperty("decisionStatus").GetString());
        Assert.IsFalse(record.GetProperty("releaseApproved").GetBoolean());
        Assert.IsFalse(record.GetProperty("deploymentApproved").GetBoolean());
        Assert.IsFalse(record.GetProperty("mergeApproved").GetBoolean());
        Assert.IsTrue(record.GetProperty("humanReviewRequiredForReleaseApproval").GetBoolean());
    }

    [TestMethod]
    public async Task GovernedReleaseGateApi_PostStoresBlockedDecisionRecord()
    {
        using var client = await AuthedClientAsync();
        var report = Rehash(CompleteReport("api-blocked") with
        {
            Status = ReleaseReadinessReportStatuses.BlockedByMissingEvidence,
            Findings = [Finding("EvidenceMissing", ReleaseReadinessFindingSeverities.Blocking)]
        });
        var request = ValidRequest("api-blocked") with { ReleaseReadinessReport = report };

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{request.ProjectId}/release-readiness/gate/governed", request);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        var data = json.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("succeeded").GetBoolean());
        Assert.IsTrue(data.GetProperty("decisionRecordStored").GetBoolean());
        Assert.IsFalse(data.GetProperty("releaseReadinessEvidenceSatisfied").GetBoolean());
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence, data.GetProperty("decisionRecord").GetProperty("decisionStatus").GetString());
        AssertEnvelopeBoundary(json.RootElement);
        AssertNoReleaseAuthority(text);
    }

    [TestMethod]
    public async Task GovernedReleaseGateApi_InvalidRequestDoesNotStoreDecisionRecord()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest("api-invalid") with { EvidenceReferences = [] };
        var before = await CountAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{request.ProjectId}/release-readiness/gate/governed", request);
        var text = await response.Content.ReadAsStringAsync();
        var after = await CountAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        Assert.AreEqual(before, after);
        AssertNoReleaseAuthority(text);
    }

    [TestMethod]
    public async Task GovernedReleaseGateApi_UnsafeRequestDoesNotEchoOrStoreDecisionRecord()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest("api-unsafe") with { RequestedBy = "raw prompt release approved" };
        var before = await CountAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{request.ProjectId}/release-readiness/gate/governed", request);
        var text = await response.Content.ReadAsStringAsync();
        var after = await CountAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        Assert.AreEqual(before, after);
        Assert.IsFalse(text.Contains("raw prompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("release approved", StringComparison.OrdinalIgnoreCase));
        AssertNoReleaseAuthority(text);
    }

    [TestMethod]
    public void GovernedReleaseGateApi_ControllerUsesOnlyGovernedServiceAndPostRoute()
    {
        var constructor = typeof(GovernedReleaseGateController).GetConstructors().Single();
        var dependencyTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        CollectionAssert.AreEqual(new[] { typeof(IGovernedReleaseGateService) }, dependencyTypes);

        var action = typeof(GovernedReleaseGateController).GetMethod(nameof(GovernedReleaseGateController.EvaluateGoverned))!;
        Assert.IsNotNull(action.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).SingleOrDefault());
        Assert.AreEqual(0, typeof(GovernedReleaseGateController).GetMethods().Count(method => method.GetCustomAttributes(typeof(HttpPutAttribute), false).Any()));
        Assert.AreEqual(0, typeof(GovernedReleaseGateController).GetMethods().Count(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), false).Any()));
        Assert.AreEqual(0, typeof(GovernedReleaseGateController).GetMethods().Count(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), false).Any()));
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task<int> CountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.ReleaseReadinessDecisionRecord");
    }

    private static GovernedReleaseGateRequest ValidRequest(string suffix) => new()
    {
        GovernedReleaseGateRequestId = DeterministicGuid($"governed-release-gate-api-{suffix}"),
        ProjectId = ProjectId,
        ReleaseReadinessReport = CompleteReport(suffix),
        RequestedBy = $"human-reviewer-{suffix}",
        RequestedAtUtc = RequestedAt,
        EvidenceReferences = [$"release-readiness-report:{suffix}", $"human-review:{suffix}"],
        BoundaryMaxims = ["Governed release gate is not release approval.", "Human review remains required."]
    };

    private static ReleaseReadinessReport CompleteReport(string suffix)
    {
        var report = new ReleaseReadinessReport
        {
            ReleaseReadinessReportId = DeterministicGuid($"release-readiness-report-api-{suffix}"),
            ProjectId = ProjectId,
            ReleaseReadinessReportRequestId = DeterministicGuid($"release-readiness-report-request-api-{suffix}"),
            Status = ReleaseReadinessReportStatuses.Complete,
            WorkflowRunId = $"workflow-run-{suffix}",
            WorkflowStepId = $"workflow-step-{suffix}",
            SubjectKind = "ReleasePackage",
            SubjectId = $"release-package-{suffix}",
            SubjectHash = H($"release-package-{suffix}"),
            AcceptedApprovalId = DeterministicGuid($"accepted-approval-{suffix}"),
            AcceptedApprovalHash = H($"accepted-approval-{suffix}"),
            PolicySatisfactionId = DeterministicGuid($"policy-satisfaction-{suffix}"),
            PolicySatisfactionHash = H($"policy-satisfaction-{suffix}"),
            SourceApplyRequestId = DeterministicGuid($"source-apply-request-{suffix}"),
            SourceApplyRequestHash = H($"source-apply-request-{suffix}"),
            SourceApplyReceiptId = DeterministicGuid($"source-apply-receipt-{suffix}"),
            SourceApplyReceiptHash = H($"source-apply-receipt-{suffix}"),
            WorkflowContinuationGateEvaluationId = DeterministicGuid($"workflow-continuation-gate-{suffix}"),
            WorkflowContinuationGateEvaluationHash = H($"workflow-continuation-gate-{suffix}"),
            WorkflowTransitionRecordId = DeterministicGuid($"workflow-transition-record-{suffix}"),
            WorkflowTransitionRecordHash = H($"workflow-transition-record-{suffix}"),
            ApprovalEvidencePresent = true,
            PolicyEvidencePresent = true,
            SourceApplySucceeded = true,
            SourceApplyPartial = false,
            RollbackWasExecuted = false,
            RollbackSucceeded = false,
            RollbackPartial = false,
            RollbackAuditConsistent = false,
            WorkflowContinuationSucceeded = true,
            WorkflowTransitionRecordValid = true,
            ReleaseReadinessDecided = false,
            ReleaseReady = false,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            SourceApplyExecutedByReport = false,
            RollbackExecutedByReport = false,
            WorkflowMutatedByReport = false,
            GitOperationExecutedByReport = false,
            HumanReviewRequiredForReadiness = true,
            HumanReviewRequiredForReleaseApproval = true,
            Findings = [Finding("ReportEvidenceComplete", ReleaseReadinessFindingSeverities.Info)],
            EvidenceReferences = [$"release-readiness-report:{suffix}", $"workflow-transition-record:{suffix}"],
            BoundaryMaxims = ["Release readiness report is evidence summary only.", "Release readiness report is not release approval."],
            ReportedAtUtc = RequestedAt.AddMinutes(-5),
            ReleaseReadinessReportHash = "sha256:pending"
        };

        return Rehash(report);
    }

    private static ReleaseReadinessReportFinding Finding(string code, string severity) => new()
    {
        Code = code,
        Severity = severity,
        Field = "ReleaseReadinessReport",
        Message = $"{code} recorded as evidence only."
    };

    private static ReleaseReadinessReport Rehash(ReleaseReadinessReport report) =>
        report with { ReleaseReadinessReportHash = ReleaseReadinessReportHashing.ComputeReportHash(report) };

    private static void AssertEnvelopeBoundary(JsonElement root)
    {
        var boundary = root.GetProperty("boundary");
        Assert.IsTrue(boundary.GetProperty("releaseReadinessGateRan").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("decisionRecordStored").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("releaseStateMutated").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("workflowStateMutated").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceStateMutated").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("gitStateMutated").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("releaseApproved").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("deploymentApproved").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("mergeApproved").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("releaseExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplyExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("rollbackExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("workflowContinued").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("gitOperationExecuted").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequired").GetBoolean());
    }

    private static void AssertNoReleaseAuthority(string text)
    {
        foreach (var token in new[]
        {
            "releaseApproved\":true",
            "deploymentApproved\":true",
            "mergeApproved\":true",
            "releaseExecuted\":true",
            "sourceApplyExecuted\":true",
            "rollbackExecuted\":true",
            "workflowContinued\":true",
            "workflowMutated\":true",
            "gitOperationExecuted\":true",
            "canDeploy",
            "canMerge"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained authority token: {token}");
        }
    }

    private static readonly Guid ProjectId = Guid.Parse("9ec41fc5-b205-4479-a066-84aa43c3c906");

    private static Guid DeterministicGuid(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static string H(string value) => $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";
}
