using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[TestCategory("ReleaseReadinessApiRegression")]
[TestCategory("PR222")]
public sealed class ReleaseReadinessApiRegressionTests : ApiTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 23, 45, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ReleaseReadinessApiRegression_OnlyGovernedGatePostCanCreateDecisionRecord()
    {
        using var client = await AuthedClientAsync();
        var request = ValidGovernedRequest("only-post-creates");
        var before = await CountAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{request.ProjectId}/release-readiness/gate/governed", request);
        var after = await CountAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(before + 1, after);
        var postRoutes = typeof(GovernedReleaseGateController).GetMethods()
            .Where(method => method.DeclaringType == typeof(GovernedReleaseGateController))
            .Where(method => method.GetCustomAttributes(typeof(HttpPostAttribute), false).Any())
            .ToArray();
        Assert.AreEqual(1, postRoutes.Length);
        Assert.AreEqual(nameof(GovernedReleaseGateController.EvaluateGoverned), postRoutes[0].Name);
    }

    [TestMethod]
    public void ReleaseReadinessApiRegression_ReadApiRemainsGetOnly()
    {
        var readMethods = typeof(ReleaseReadinessDecisionRecordsController).GetMethods()
            .Where(method => method.DeclaringType == typeof(ReleaseReadinessDecisionRecordsController))
            .ToArray();

        Assert.IsTrue(readMethods.Any(method => method.GetCustomAttributes(typeof(HttpGetAttribute), false).Any()));
        Assert.AreEqual(0, readMethods.Count(method => method.GetCustomAttributes(typeof(HttpPostAttribute), false).Any()));
        Assert.AreEqual(0, readMethods.Count(method => method.GetCustomAttributes(typeof(HttpPutAttribute), false).Any()));
        Assert.AreEqual(0, readMethods.Count(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), false).Any()));
        Assert.AreEqual(0, readMethods.Count(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), false).Any()));
    }

    [TestMethod]
    public void ReleaseReadinessApiRegression_NoReleaseApprovalDeploymentMergeOrExecutionRoutes()
    {
        var routeText = string.Join(
            "\n",
            new[] { typeof(GovernedReleaseGateController), typeof(ReleaseReadinessDecisionRecordsController) }
                .SelectMany(type => type.GetMethods())
                .SelectMany(method => method.GetCustomAttributes()
                    .Where(attribute => attribute is HttpMethodAttribute)
                    .Cast<HttpMethodAttribute>()
                    .Select(attribute => $"{method.Name}:{attribute.Template}")));

        foreach (var forbidden in new[] { "approve", "deploy", "merge", "execute", "tag", "source-apply", "rollback", "continue", "git" })
            Assert.IsFalse(routeText.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Forbidden action route token: {forbidden}");
    }

    [TestMethod]
    public void ReleaseReadinessApiRegression_ControllersDoNotBypassGovernedServices()
    {
        CollectionAssert.AreEqual(
            new[] { typeof(IGovernedReleaseGateService) },
            typeof(GovernedReleaseGateController).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        CollectionAssert.AreEqual(
            new[] { typeof(IReleaseReadinessDecisionRecordQueryService) },
            typeof(ReleaseReadinessDecisionRecordsController).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        var root = RepositoryRoot();
        foreach (var file in new[]
        {
            Path.Combine(root, "IronDev.Api", "Controllers", "GovernedReleaseGateController.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "ReleaseReadinessDecisionRecordsController.cs")
        })
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[]
            {
                "IReleaseReadinessDecisionRecordStore",
                "ReleaseReadinessGateEvaluator",
                "SqlConnection",
                "IDbConnection",
                "Dapper",
                "IReleaseApproval",
                "ReleaseApprovalService",
                "DeploymentApprovalService",
                "MergeApprovalService",
                "ReleaseExecutionService",
                "ControlledSourceApplyExecutor",
                "ControlledRollbackExecutor",
                "GovernedWorkflowContinuationService",
                "Process"
            })
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal), $"Forbidden controller dependency token in {file}: {forbidden}");
            }
        }
    }

    [TestMethod]
    public async Task ReleaseReadinessApiRegression_ApiDoesNotEchoUnsafeReleaseGateInput()
    {
        using var client = await AuthedClientAsync();
        var request = ValidGovernedRequest("unsafe") with { RequestedBy = "raw prompt release approved" };
        var before = await CountAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/projects/{request.ProjectId}/release-readiness/gate/governed", request);
        var text = await response.Content.ReadAsStringAsync();
        var after = await CountAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        Assert.AreEqual(before, after);
        Assert.IsFalse(text.Contains("raw prompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("release approved", StringComparison.OrdinalIgnoreCase));
        AssertNoAuthorityFlags(text);
    }

    [TestMethod]
    public async Task ReleaseReadinessApiRegression_ReadApiRejectsPrefixedDecisionRecordHash()
    {
        using var client = await AuthedClientAsync();
        var response = await client.GetAsync($"/api/v1/projects/{ProjectId}/release-readiness-decision-records/by-hash/sha256:{RawHash("prefixed")}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "raw 64-character hexadecimal SHA-256 hash without a prefix");
        AssertNoAuthorityFlags(text);
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

    private static GovernedReleaseGateRequest ValidGovernedRequest(string suffix) => new()
    {
        GovernedReleaseGateRequestId = DeterministicGuid($"governed-release-gate-api-regression-{suffix}"),
        ProjectId = ProjectId,
        ReleaseReadinessReport = CompleteReport(suffix),
        RequestedBy = $"human-reviewer-{suffix}",
        RequestedAtUtc = Now,
        EvidenceReferences = [$"release-readiness-report:{suffix}", $"human-review:{suffix}"],
        BoundaryMaxims = ["Governed release gate is not release approval.", "Human review remains required."]
    };

    private static ReleaseReadinessReport CompleteReport(string suffix)
    {
        var report = new ReleaseReadinessReport
        {
            ReleaseReadinessReportId = DeterministicGuid($"release-readiness-report-api-regression-{suffix}"),
            ProjectId = ProjectId,
            ReleaseReadinessReportRequestId = DeterministicGuid($"release-readiness-report-request-api-regression-{suffix}"),
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
            Findings = [new ReleaseReadinessReportFinding { Code = "ReportEvidenceComplete", Severity = ReleaseReadinessFindingSeverities.Info, Field = "ReleaseReadinessReport", Message = "Report evidence complete." }],
            EvidenceReferences = [$"release-readiness-report:{suffix}", $"workflow-transition-record:{suffix}"],
            BoundaryMaxims = ["Release readiness report is evidence summary only.", "Release readiness report is not release approval."],
            ReportedAtUtc = Now.AddMinutes(-5),
            ReleaseReadinessReportHash = "sha256:pending"
        };

        return report with { ReleaseReadinessReportHash = ReleaseReadinessReportHashing.ComputeReportHash(report) };
    }

    private static void AssertNoAuthorityFlags(string text)
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
            "gitOperationExecuted\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained authority flag: {token}");
        }
    }

    private static readonly Guid ProjectId = Guid.Parse("a00a298b-aa8a-4a63-93da-6d35d34ed961");

    private static Guid DeterministicGuid(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static string H(string value) => $"sha256:{RawHash(value)}";

    private static string RawHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
