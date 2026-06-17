using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("GovernedReleaseGateCli")]
[TestCategory("PR221")]
public sealed class GovernedReleaseGateCliTests
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyEnvironment = new Dictionary<string, string?>();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 17, 23, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CliGovernedReleaseGate_CallsGovernedReleaseGateApi()
    {
        var requestFile = await WriteRequestFileAsync("cli-post");
        var handler = new RecordingHandler(SuccessEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliReleaseGate.HandleAsync(
            ["release", "gate", "governed", "--request-file", requestFile, "--api-base-url", "https://api.example.test", "--token", "super-secret-token", "--json"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/projects/{ProjectId}/release-readiness/gate/governed", handler.Request?.RequestUri?.PathAndQuery);
        Assert.AreEqual("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.AreEqual("super-secret-token", handler.Request?.Headers.Authorization?.Parameter);
        Assert.IsNotNull(handler.Body);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual(ProjectId, body.RootElement.GetProperty("projectId").GetGuid());
        Assert.AreEqual(DeterministicRequestId("cli-post"), body.RootElement.GetProperty("governedReleaseGateRequestId").GetGuid());
        AssertNoTokenLeak(output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task CliGovernedReleaseGate_AliasCallsGovernedReleaseGateApi()
    {
        var requestFile = await WriteRequestFileAsync("cli-alias");
        var handler = new RecordingHandler(SuccessEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliReleaseGate.HandleAsync(
            ["release", "readiness", "gate", "governed", "--request-file", requestFile, "--api-base-url", "https://api.example.test", "--output", "text"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/projects/{ProjectId}/release-readiness/gate/governed", handler.Request?.RequestUri?.PathAndQuery);
        StringAssert.Contains(output.ToString(), "Governed Release Gate");
        StringAssert.Contains(output.ToString(), "Release gate result is evidence only. It does not release the product.");
        AssertNoReleaseAuthority(output.ToString());
    }

    [TestMethod]
    public async Task CliGovernedReleaseGate_RejectsReleaseAuthorityOptionsBeforeApi()
    {
        var requestFile = await WriteRequestFileAsync("cli-reject-flag");
        var handler = new RecordingHandler(SuccessEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliReleaseGate.HandleAsync(
            ["release", "gate", "governed", "--request-file", requestFile, "--approve-release", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        Assert.IsNull(handler.Request);
        StringAssert.Contains(error.ToString(), "Unsupported governed release gate option");
    }

    [TestMethod]
    public async Task CliGovernedReleaseGate_RejectsUnsafeRequestFileBeforeApi()
    {
        var requestFile = Path.Combine(Path.GetTempPath(), $"irondeveloper-pr221-unsafe-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(requestFile, "{\"projectId\":\"" + ProjectId + "\",\"requestedBy\":\"raw prompt release approved\"}");
        var handler = new RecordingHandler(SuccessEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliReleaseGate.HandleAsync(
            ["release", "gate", "governed", "--request-file", requestFile, "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        Assert.IsNull(handler.Request);
        Assert.IsFalse(output.ToString().Contains("raw prompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(error.ToString().Contains("release approved", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CliGovernedReleaseGate_StaticBoundaryDoesNotUseEvaluatorStoreSqlOrRuntime()
    {
        var root = RepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReleaseGate.cs"));
        var dispatcher = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"));

        StringAssert.Contains(cli, "CreateGovernedReleaseGateAsync");
        StringAssert.Contains(dispatcher, "IronDevCliReleaseGate");

        foreach (var marker in new[]
        {
            "ReleaseReadinessGateEvaluator",
            "IReleaseReadinessDecisionRecordStore",
            "SqlConnection",
            "SaveAsync",
            "ExecuteRelease",
            "DeployRelease",
            "MergeRelease",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "GovernedWorkflowContinuationService",
            "Process.Start",
            "ProcessStartInfo",
            "PromoteMemory",
            "ActivateRetrieval",
            "DispatchAgent",
            "RunTool",
            "CallModel"
        })
        {
            Assert.IsFalse(cli.Contains(marker, StringComparison.Ordinal), $"Forbidden CLI marker: {marker}");
        }
    }

    private static async Task<string> WriteRequestFileAsync(string suffix)
    {
        var file = Path.Combine(Path.GetTempPath(), $"irondeveloper-pr221-{suffix}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(ValidRequest(suffix), JsonOptions));
        return file;
    }

    private static GovernedReleaseGateRequest ValidRequest(string suffix) => new()
    {
        GovernedReleaseGateRequestId = DeterministicRequestId(suffix),
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
            ReleaseReadinessReportId = DeterministicGuid($"release-readiness-report-cli-{suffix}"),
            ProjectId = ProjectId,
            ReleaseReadinessReportRequestId = DeterministicGuid($"release-readiness-report-request-cli-{suffix}"),
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
            ReportedAtUtc = RequestedAt.AddMinutes(-5),
            ReleaseReadinessReportHash = "sha256:pending"
        };

        return report with { ReleaseReadinessReportHash = ReleaseReadinessReportHashing.ComputeReportHash(report) };
    }

    private static string SuccessEnvelope() =>
        $$"""
        {
          "status": "DecisionRecordStored",
          "data": {
            "governedReleaseGateRequestId": "{{DeterministicRequestId("api-response")}}",
            "projectId": "{{ProjectId}}",
            "succeeded": true,
            "status": "DecisionRecordStored",
            "releaseReadinessGateRan": true,
            "decisionRecordStored": true,
            "decisionRecord": {
              "releaseReadinessDecisionRecordId": "{{DeterministicRequestId("api-response")}}",
              "projectId": "{{ProjectId}}",
              "releaseReadinessReportId": "{{DeterministicGuid("report-api-response")}}",
              "releaseReadinessReportHash": "{{RawHash("report-api-response")}}",
              "workflowRunId": "workflow-run-api-response",
              "workflowStepId": "workflow-step-api-response",
              "subjectKind": "ReleasePackage",
              "subjectId": "release-package-api-response",
              "subjectHash": "{{RawHash("subject-api-response")}}",
              "decisionStatus": "ReadyEvidenceSatisfied",
              "releaseReadinessEvidenceSatisfied": true,
              "releaseApproved": false,
              "deploymentApproved": false,
              "mergeApproved": false,
              "sourceApplyExecutedByDecision": false,
              "rollbackExecutedByDecision": false,
              "workflowMutatedByDecision": false,
              "gitOperationExecutedByDecision": false,
              "releaseExecutedByDecision": false,
              "humanReviewRequiredForReleaseApproval": true,
              "humanReviewRequiredForDeployment": true,
              "humanReviewRequiredForMerge": true,
              "decidedAtUtc": "2026-06-17T23:00:00Z",
              "releaseReadinessDecisionRecordHash": "{{RawHash("decision-api-response")}}"
            },
            "releaseReadinessEvidenceSatisfied": true,
            "releaseApproved": false,
            "deploymentApproved": false,
            "mergeApproved": false,
            "releaseExecuted": false,
            "sourceApplyExecuted": false,
            "rollbackExecuted": false,
            "workflowContinued": false,
            "workflowMutated": false,
            "gitOperationExecuted": false,
            "humanReviewRequiredForReleaseApproval": true,
            "humanReviewRequiredForDeployment": true,
            "humanReviewRequiredForMerge": true,
            "completedAtUtc": "2026-06-17T23:01:00Z"
          },
          "warnings": ["A stored ReleaseReadinessDecisionRecord is evidence, not release approval."],
          "errors": []
        }
        """;

    private static void AssertNoTokenLeak(params string[] values)
    {
        foreach (var value in values)
            Assert.IsFalse(value.Contains("super-secret-token", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertNoReleaseAuthority(string text)
    {
        foreach (var token in new[]
        {
            "Release approval: True",
            "Deployment approval: True",
            "Merge approval: True",
            "releaseExecuted: True",
            "sourceApplyExecuted: True",
            "rollbackExecuted: True",
            "workflowContinued: True",
            "gitOperationExecuted: True",
            "safe to deploy",
            "safe to merge"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"CLI output contained authority token: {token}");
        }
    }

    private static readonly Guid ProjectId = Guid.Parse("b49388de-df64-4277-b05f-c77e165d0d45");

    private static Guid DeterministicRequestId(string suffix) => DeterministicGuid($"governed-release-gate-cli-{suffix}");

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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _statusCode;

        public RecordingHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _body = body;
            _statusCode = statusCode;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
