using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ManualTesterAgentToolExecutionTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 11, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GateEvaluatedAt = new(2026, 6, 11, 12, 55, 0, TimeSpan.Zero);

    [TestMethod]
    public void ManualTesterAgentToolExecutionContracts_ExposeExpectedTypes()
    {
        Assert.IsNotNull(typeof(IManualTesterAgentToolExecutionService));
        Assert.IsNotNull(typeof(ManualTesterAgentToolExecutionService));
        Assert.IsNotNull(typeof(ManualTesterAgentToolExecutionRequest));
        Assert.IsNotNull(typeof(ManualTesterAgentToolExecutionResult));
        Assert.IsNotNull(typeof(ManualTesterAgentToolExecutionStatus));
        Assert.IsNotNull(typeof(ManualTesterAgentToolExecutionOutput));
        Assert.IsNotNull(typeof(ManualTesterAgentToolExecutionIssue));
        Assert.IsNotNull(typeof(ManualTesterAgentToolExecutionValidator));
        Assert.IsNotNull(typeof(ITestRunPlanExecutor));
        Assert.IsNotNull(typeof(ScriptedTestRunPlanExecutor));
        Assert.IsNotNull(typeof(TestRunPlanExecutionRequest));
        Assert.IsNotNull(typeof(TestRunPlanExecutionResult));
    }

    [TestMethod]
    public void ManualTesterAgentToolExecution_ValidGatedTestRunExecutesScriptedExecutor()
    {
        var executor = SuccessExecutor();
        var service = new ManualTesterAgentToolExecutionService(executor);
        var request = ValidExecutionRequest();

        var result = service.Execute(request);

        Assert.AreEqual(1, executor.CallCount);
        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(ManualTesterAgentToolExecutionStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.Output);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.AreEqual(request.ToolRequest.ToolRequestId, result.ToolRequestId);
        Assert.AreEqual(request.GateDecision.GateDecisionId, result.GateDecisionId);
        Assert.AreEqual("passed", result.Output.Outcome);
        Assert.IsTrue(result.Output.EvidenceRefs.Count > 0);
        AssertOutputHasNoDangerousFlags(result.Output);
        AssertNoAuditIssues(result.AuditEnvelope);
        AssertNoThoughtLedgerIssues(result.AuditEnvelope.ThoughtLedger);
        AssertCapability(result.AuditEnvelope, AgentCapability.RunTool, AgentCapabilityUseOutcome.Allowed);
        AssertCapability(result.AuditEnvelope, AgentCapability.CreateReport, AgentCapabilityUseOutcome.Allowed);
        AssertCapability(result.AuditEnvelope, AgentCapability.MutateSource, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(result.AuditEnvelope, AgentCapability.CallExternalSystem, AgentCapabilityUseOutcome.Blocked);
        AssertCapability(result.AuditEnvelope, AgentCapability.PromoteCollectiveMemory, AgentCapabilityUseOutcome.Blocked);
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.All(decision => !decision.GrantsAuthority && !decision.GrantsHumanApproval && !decision.GrantsPolicyApproval && !decision.GrantsMemoryPromotion));
        Assert.IsTrue(result.AuditEnvelope.ThoughtLedger.All(entry => !entry.ContainsRawPrivateReasoning && !entry.GrantsAuthority && !entry.GrantsApproval && !entry.GrantsMemoryPromotion));
    }

    [TestMethod]
    public void ManualTesterAgentToolExecution_FailedTestRunReturnsOutputAndSafeAuditEnvelope()
    {
        var executor = new ScriptedTestRunPlanExecutor(request => new TestRunPlanExecutionResult
        {
            Succeeded = false,
            ExecutionId = request.ExecutionId,
            ExitCode = 1,
            Summary = "Scripted test-plan executor reported failing tests.",
            Outcome = "failed",
            TestsPassed = 7,
            TestsFailed = 2,
            TestsSkipped = 1,
            Duration = TimeSpan.FromSeconds(3),
            EvidenceRefs = ["test-result-failed-1"]
        });
        var service = new ManualTesterAgentToolExecutionService(executor);

        var result = service.Execute(ValidExecutionRequest());

        Assert.AreEqual(1, executor.CallCount);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTesterAgentToolExecutionStatus.Failed, result.Status);
        Assert.IsNotNull(result.Output);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.AreEqual(2, result.Output.TestsFailed);
        Assert.AreEqual(IronDev.Core.Agents.Audit.AgentRunStatus.Failed, result.AuditEnvelope.Run.Status);
        AssertOutputHasNoDangerousFlags(result.Output);
        AssertNoAuditIssues(result.AuditEnvelope);
        AssertNoThoughtLedgerIssues(result.AuditEnvelope.ThoughtLedger);
    }

    [TestMethod]
    public void ManualTesterAgentToolExecution_GateRejectionsPreventExecutorCall()
    {
        var baseRequest = ValidExecutionRequest();
        var unsafeGateCases = new[]
        {
            baseRequest.GateDecision with { Decision = AgentToolExecutionGateDecisionType.Blocked, GrantsExecution = false, RequiresExecutor = false },
            baseRequest.GateDecision with { Decision = AgentToolExecutionGateDecisionType.RequiresApproval, GrantsExecution = false, RequiresExecutor = false },
            baseRequest.GateDecision with { GrantsExecution = false },
            baseRequest.GateDecision with { RequiresExecutor = false },
            baseRequest.GateDecision with { ExecutesTool = true },
            baseRequest.GateDecision with { MutatesSource = true },
            baseRequest.GateDecision with { CallsExternalSystem = true },
            baseRequest.GateDecision with { SubmitsGitHubReview = true },
            baseRequest.GateDecision with { PersistsResult = true },
            baseRequest.GateDecision with { PromotesMemory = true },
            baseRequest.GateDecision with { CreatesCollectiveMemory = true },
            baseRequest.GateDecision with { WritesWeaviate = true },
            baseRequest.GateDecision with { ToolRequestId = "different-tool-request" }
        };

        foreach (var gate in unsafeGateCases)
        {
            var executor = SuccessExecutor();
            var service = new ManualTesterAgentToolExecutionService(executor);
            var result = service.Execute(baseRequest with { GateDecision = gate });

            Assert.AreEqual(0, executor.CallCount);
            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Status is ManualTesterAgentToolExecutionStatus.Blocked or ManualTesterAgentToolExecutionStatus.InvalidRequest);
            Assert.IsNull(result.Output);
            Assert.IsNull(result.AuditEnvelope);
            Assert.IsTrue(result.Issues.Count > 0);
        }
    }

    [TestMethod]
    public void ManualTesterAgentToolExecution_RequestRejectionsPreventExecutorCall()
    {
        var baseRequest = ValidExecutionRequest();
        var cases = new[]
        {
            baseRequest with { ManualExecutionId = string.Empty },
            baseRequest with { RequestedByUserId = string.Empty },
            baseRequest with { TestPlanRef = string.Empty },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { ToolKind = AgentToolKind.BuildRun } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { RequestType = AgentToolRequestType.BuildExecutionRequest } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { Actor = ValidActor(AgentDefinitionCatalog.IndependentCriticAgent) } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { ClaimsApproval = true } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { ClaimsExecutionPermission = true } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { ContainsExecutionResult = true } },
            baseRequest with { ToolRequest = baseRequest.ToolRequest with { IsExecutableWithoutGate = true } },
            baseRequest with { TestPlanPath = "..\\outside.json" },
            baseRequest with { TestPlanPath = "C:\\temp\\test-plan.json" },
            baseRequest with { WorkingDirectory = "..\\outside" },
            baseRequest with { Parameters = new Dictionary<string, string> { ["filter"] = "A && B" } },
            baseRequest with { Parameters = new Dictionary<string, string> { ["password"] = "not-allowed" } }
        };

        foreach (var request in cases)
        {
            var executor = SuccessExecutor();
            var result = new ManualTesterAgentToolExecutionService(executor).Execute(request);

            Assert.AreEqual(0, executor.CallCount);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ManualTesterAgentToolExecutionStatus.InvalidRequest, result.Status);
            Assert.IsNull(result.Output);
            Assert.IsNull(result.AuditEnvelope);
            Assert.IsTrue(result.Issues.Count > 0);
        }
    }

    [TestMethod]
    public void ManualTesterAgentToolExecution_UnsafeExecutorOutputIsRejectedWithoutAuditEnvelope()
    {
        var cases = new[]
        {
            new TestRunPlanExecutionResult
            {
                Succeeded = true,
                ExecutionId = "manual-test-execution-1",
                ExitCode = 0,
                Summary = "Contains private reasoning.",
                Outcome = "passed",
                EvidenceRefs = ["test-evidence-1"]
            },
            new TestRunPlanExecutionResult
            {
                Succeeded = true,
                ExecutionId = "manual-test-execution-1",
                ExitCode = 0,
                Summary = "No evidence.",
                Outcome = "passed",
                EvidenceRefs = []
            }
        };

        foreach (var testCase in cases)
        {
            var executor = new ScriptedTestRunPlanExecutor(_ => testCase);
            var result = new ManualTesterAgentToolExecutionService(executor).Execute(ValidExecutionRequest());

            Assert.AreEqual(1, executor.CallCount);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ManualTesterAgentToolExecutionStatus.InvalidRequest, result.Status);
            Assert.IsNull(result.AuditEnvelope);
            AssertHasIssue(result.Issues, ManualTesterAgentToolExecutionValidator.OutputUnsafe);
        }
    }

    [TestMethod]
    public void ManualTesterAgentToolExecution_OutputValidatorRejectsDangerousFlags()
    {
        var validator = new ManualTesterAgentToolExecutionValidator();
        var cases = new[]
        {
            SafeOutput() with { ContainsRawPrivateReasoning = true },
            SafeOutput() with { MutatesSource = true },
            SafeOutput() with { CallsExternalSystem = true },
            SafeOutput() with { SubmitsGitHubReview = true },
            SafeOutput() with { PromotesMemory = true },
            SafeOutput() with { CreatesCollectiveMemory = true },
            SafeOutput() with { WritesWeaviate = true },
            SafeOutput() with { EvidenceRefs = [] }
        };

        foreach (var output in cases)
            AssertHasIssue(validator.ValidateOutput(output), ManualTesterAgentToolExecutionValidator.OutputUnsafe);
    }

    [TestMethod]
    public void AgentRunAuditEnvelopeValidator_AllowsRunToolOnlyForBuiltInTestingAgent()
    {
        var valid = new ManualTesterAgentToolExecutionService(SuccessExecutor()).Execute(ValidExecutionRequest()).AuditEnvelope!;
        AssertNoAuditIssues(valid);

        var invalid = valid with
        {
            Run = valid.Run with
            {
                AgentId = AgentDefinitionCatalog.ImplementationAgent.AgentId,
                AgentName = AgentDefinitionCatalog.ImplementationAgent.Name
            },
            AgentDefinitionSnapshot = AgentDefinitionCatalog.ImplementationAgent
        };

        var issues = new AgentRunAuditEnvelopeValidator().Validate(invalid);
        Assert.IsTrue(issues.Any(issue => issue.Code == AgentRunAuditEnvelopeValidator.DangerousCapabilityAllowed));
    }

    [TestMethod]
    public void ManualTesterAgentToolExecution_NoRuntimeApiPersistenceOrModelWiring()
    {
        var files = new[]
        {
            ReadRepositoryFile("IronDev.Api", "Program.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualMemoryImprovementAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ModelBackedManualIndependentCriticAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ModelBackedManualMemoryImprovementAgentService.cs"),
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "StoredManualAgentExecutionService.cs")
        };
        var forbidden = new[]
        {
            "ManualTesterAgentToolExecutionService",
            "IManualTesterAgentToolExecutionService",
            "AgentToolRouter",
            "IAgentToolRouter",
            "ToolExecutionAuditStore",
            "SqlToolExecutionAuditStore"
        };

        foreach (var file in files)
        {
            foreach (var token in forbidden)
                Assert.IsFalse(file.Contains(token, StringComparison.Ordinal), $"Unexpected runtime/API/persistence wiring token found: {token}");
        }
    }

    [TestMethod]
    public void ManualTesterAgentToolExecution_StaticBoundary_HasNoRuntimePersistenceExternalOrShellTokens()
    {
        var changedProductionFiles = new[]
        {
            ReadRepositoryFile("IronDev.Core", "Agents", "Concrete", "ManualTesterAgentToolExecutionService.cs")
        };
        var forbidden = new[]
        {
            "ProcessStartInfo",
            "System.Diagnostics.Process",
            "PowerShell",
            "cmd.exe",
            "bash",
            "File.WriteAllText",
            "File.Delete",
            "File.Copy",
            "Directory.Delete",
            "SqlConnection",
            "DbConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "HttpClient",
            "OpenAiLlmService",
            "ChatCompletion",
            "ResponsesApi",
            "WeaviateClient",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentToolRouter",
            "SubmitReview",
            "CreatePullRequest",
            "IAgentRunAuditEnvelopeStore",
            "SqlMemoryImprovementProposalStore",
            "SqlCollectiveMemoryPromotionService"
        };

        foreach (var source in changedProductionFiles)
        {
            foreach (var token in forbidden)
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden active token found in manual tester execution production file: {token}");
        }
    }

    private static ManualTesterAgentToolExecutionRequest ValidExecutionRequest()
    {
        var toolRequest = ValidToolRequest();
        return new ManualTesterAgentToolExecutionRequest
        {
            ManualExecutionId = "manual-test-execution-1",
            ToolRequest = toolRequest,
            GateDecision = AllowedGate(toolRequest),
            RequestedByUserId = "user-1",
            TestPlanRef = "test-plan-1",
            TestPlanPath = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
            WorkingDirectory = "workspace/run-1",
            Parameters = new Dictionary<string, string> { ["filter"] = "IronDevCliTests" },
            RequestedAtUtc = RequestedAt
        };
    }

    private static AgentToolRequest ValidToolRequest() =>
        new()
        {
            ToolRequestId = "tool-request-test-run-1",
            Status = AgentToolRequestStatus.PendingGate,
            RequestType = AgentToolRequestType.TestExecutionRequest,
            ToolKind = AgentToolKind.TestRun,
            RiskLevel = AgentToolRiskLevel.Medium,
            Scope = new AgentToolRequestScope
            {
                TenantId = "tenant-1",
                ProjectId = "project-1",
                CampaignId = "campaign-1",
                RunId = "run-1",
                AgentRunId = "agent-run-1",
                CorrelationId = "correlation-1"
            },
            Actor = ValidActor(AgentDefinitionCatalog.TestingAgent),
            Purpose = "Run the controlled scripted test plan.",
            Inputs =
            [
                new AgentToolRequestInput
                {
                    InputId = "input-test-plan-1",
                    RefType = "TestPlanRef",
                    RefId = "test-plan-1",
                    Source = "test",
                    Summary = "Sanitised test plan reference.",
                    EvidenceRefs = ["evidence-test-plan-1"],
                    IsSanitised = true
                }
            ],
            Evidence =
            [
                new AgentToolRequestEvidence
                {
                    EvidenceId = "evidence-test-plan-1",
                    RefType = "RunReport",
                    RefId = "run-report-1",
                    Summary = "Evidence supports requesting this test run.",
                    SupportsNeedForTool = true
                }
            ],
            ApprovalRequirement = new AgentToolRequestApprovalRequirement
            {
                RequiresGovernanceGate = true,
                Reason = "Test execution requires gate metadata."
            },
            RequestedAtUtc = RequestedAt
        };

    private static AgentToolRequestActor ValidActor(AgentDefinition agent) =>
        new()
        {
            AgentId = agent.AgentId,
            AgentName = agent.Name,
            AgentKind = agent.Kind,
            ExecutionMode = agent.ExecutionMode,
            DeclaredCapabilities = agent.Capabilities?.ToArray() ?? [],
            ForbiddenCapabilities = agent.ForbiddenCapabilities?.ToArray() ?? []
        };

    private static AgentToolExecutionGateDecision AllowedGate(AgentToolRequest request)
    {
        var result = new AgentToolExecutionGate().Evaluate(new AgentToolExecutionGateRequest
        {
            ToolRequest = request,
            PolicyContext = new AgentToolExecutionGatePolicyContext
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                AllowsToolExecution = true,
                AllowsTestExecution = true,
                PolicyRefs = ["policy:test-execution"]
            },
            ApprovalContext = new AgentToolExecutionGateApprovalContext
            {
                HasGovernanceGateApproval = true,
                GovernanceGateDecisionId = "governance-gate-1",
                ApprovalRefs = ["governance-gate-1"]
            },
            EvaluatedAtUtc = GateEvaluatedAt
        });

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Decision);
        Assert.AreEqual(AgentToolExecutionGateDecisionType.Allowed, result.Decision.Decision);
        return result.Decision;
    }

    private static ScriptedTestRunPlanExecutor SuccessExecutor() =>
        new(request => new TestRunPlanExecutionResult
        {
            Succeeded = true,
            ExecutionId = request.ExecutionId,
            ExitCode = 0,
            Summary = "Scripted test-plan executor completed successfully.",
            Outcome = "passed",
            TestsPassed = 9,
            TestsFailed = 0,
            TestsSkipped = 1,
            Duration = TimeSpan.FromSeconds(2),
            EvidenceRefs = ["test-result-1", request.TestPlanRef]
        });

    private static ManualTesterAgentToolExecutionOutput SafeOutput() =>
        new()
        {
            OutputId = "output-1",
            Summary = "Safe test output.",
            ExitCode = 0,
            Outcome = "passed",
            TestsPassed = 1,
            Duration = TimeSpan.FromMilliseconds(1),
            EvidenceRefs = ["evidence-1"]
        };

    private static void AssertOutputHasNoDangerousFlags(ManualTesterAgentToolExecutionOutput output)
    {
        Assert.IsFalse(output.ContainsRawPrivateReasoning);
        Assert.IsFalse(output.MutatesSource);
        Assert.IsFalse(output.CallsExternalSystem);
        Assert.IsFalse(output.SubmitsGitHubReview);
        Assert.IsFalse(output.PromotesMemory);
        Assert.IsFalse(output.CreatesCollectiveMemory);
        Assert.IsFalse(output.WritesWeaviate);
    }

    private static void AssertCapability(
        AgentRunAuditEnvelope envelope,
        AgentCapability capability,
        AgentCapabilityUseOutcome outcome) =>
        Assert.IsTrue(
            envelope.CapabilityUses.Any(use => use.Capability == capability && use.Outcome == outcome),
            $"Expected {capability} to be {outcome}.");

    private static void AssertNoAuditIssues(AgentRunAuditEnvelope envelope)
    {
        var issues = new AgentRunAuditEnvelopeValidator().Validate(envelope);
        Assert.IsFalse(issues.Any(), FormatDefinitionIssues(issues));
    }

    private static void AssertNoThoughtLedgerIssues(IReadOnlyList<AuditThoughtLedgerEntry> entries)
    {
        var issues = new ThoughtLedgerSafetyValidator().Validate(entries);
        Assert.IsFalse(issues.Any(), FormatDefinitionIssues(issues));
    }

    private static void AssertHasIssue(IReadOnlyList<ManualTesterAgentToolExecutionIssue> issues, string code) =>
        Assert.IsTrue(issues.Any(issue => issue.Code == code), $"Expected issue {code}.{Environment.NewLine}{FormatIssues(issues)}");

    private static string FormatIssues(IReadOnlyList<ManualTesterAgentToolExecutionIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string FormatDefinitionIssues(IReadOnlyList<AgentDefinitionValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

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
