using System.Net;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ReleaseReadinessCliRegression")]
[TestCategory("PR222")]
public sealed class ReleaseReadinessCliRegressionTests
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyEnvironment = new Dictionary<string, string?>();
    private static readonly Guid ProjectId = Guid.Parse("4f209f4d-99d1-499a-9908-3fa30c8c21d6");

    [TestMethod]
    public async Task ReleaseReadinessCliRegression_CliCallsApiOnly()
    {
        var requestFile = await WriteRequestFileAsync();
        var handler = new RecordingHandler(SuccessEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliReleaseGate.HandleAsync(
            ["release", "gate", "governed", "--request-file", requestFile, "--api-base-url", "https://api.example.test", "--json"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/projects/{ProjectId}/release-readiness/gate/governed", handler.Request?.RequestUri?.PathAndQuery);
        Assert.IsNotNull(handler.Body);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual(ProjectId, body.RootElement.GetProperty("projectId").GetGuid());
    }

    [TestMethod]
    public async Task ReleaseReadinessCliRegression_CliRejectsReleaseAuthorityOptions()
    {
        foreach (var option in new[] { "--approve-release", "--release-approved", "--deploy", "--merge", "--execute-release", "--tag", "--git-push", "--source-apply", "--rollback", "--continue-workflow" })
        {
            var requestFile = await WriteRequestFileAsync();
            var handler = new RecordingHandler(SuccessEnvelope());
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await IronDevCliReleaseGate.HandleAsync(
                ["release", "gate", "governed", "--request-file", requestFile, option, "--api-base-url", "https://api.example.test"],
                output,
                error,
                EmptyEnvironment,
                handler,
                CancellationToken.None);

            Assert.AreEqual(3, exitCode, option);
            Assert.IsNull(handler.Request, option);
            StringAssert.Contains(error.ToString(), "Unsupported governed release gate option");
        }
    }

    [TestMethod]
    public void ReleaseReadinessCliRegression_CliDoesNotUseEvaluatorStoreSqlGitOrRuntime()
    {
        var root = RepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReleaseGate.cs"));

        StringAssert.Contains(cli, "CreateGovernedReleaseGateAsync");
        foreach (var forbidden in new[]
        {
            "ReleaseReadinessGateEvaluator",
            "IReleaseReadinessDecisionRecordStore",
            "SqlConnection",
            "IDbConnection",
            "Dapper",
            "SaveAsync",
            "ReleaseExecutionService",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "GovernedWorkflowContinuationService",
            "Process.Start",
            "ProcessStartInfo",
            "git commit",
            "git push",
            "git merge",
            "gh pr",
            "PromoteMemory",
            "ActivateRetrieval",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker"
        })
        {
            Assert.IsFalse(cli.Contains(forbidden, StringComparison.Ordinal), $"Forbidden CLI token: {forbidden}");
        }
    }

    [TestMethod]
    public async Task ReleaseReadinessCliRegression_CliDoesNotPrintReleaseApprovedAsGranted()
    {
        var requestFile = await WriteRequestFileAsync();
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

        var text = output.ToString();
        Assert.AreEqual(0, exitCode, error.ToString());
        StringAssert.Contains(text, "Release gate result is evidence only. It does not release the product.");
        StringAssert.Contains(text, "Release approval: False");
        StringAssert.Contains(text, "Deployment approval: False");
        StringAssert.Contains(text, "Merge approval: False");
        Assert.IsFalse(text.Contains("Release approval: True", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("safe to deploy", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("safe to merge", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> WriteRequestFileAsync()
    {
        var file = Path.Combine(Path.GetTempPath(), $"irondeveloper-pr222-release-readiness-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, $$"""
        {
          "projectId": "{{ProjectId}}",
          "requestedBy": "human-reviewer-pr222"
        }
        """);
        return file;
    }

    private static string SuccessEnvelope() =>
        $$"""
        {
          "status": "DecisionRecordStored",
          "data": {
            "projectId": "{{ProjectId}}",
            "succeeded": true,
            "status": "DecisionRecordStored",
            "releaseReadinessGateRan": true,
            "decisionRecordStored": true,
            "decisionRecord": {
              "releaseReadinessDecisionRecordId": "f773ec1d-492f-45e5-bdbd-8067bc121aaa",
              "releaseReadinessDecisionRecordHash": "4c7a85a7619e97d9f20ac62ca64798e351d119ad35ff0b4e040b40d4f3377d43",
              "decisionStatus": "ReadyEvidenceSatisfied",
              "releaseReadinessEvidenceSatisfied": true,
              "releaseApproved": false,
              "deploymentApproved": false,
              "mergeApproved": false,
              "humanReviewRequiredForReleaseApproval": true,
              "humanReviewRequiredForDeployment": true,
              "humanReviewRequiredForMerge": true
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
            "humanReviewRequiredForMerge": true
          },
          "warnings": ["A stored ReleaseReadinessDecisionRecord is evidence, not release approval."],
          "errors": []
        }
        """;

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
