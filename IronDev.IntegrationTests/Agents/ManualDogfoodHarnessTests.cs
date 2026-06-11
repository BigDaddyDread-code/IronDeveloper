using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ManualDogfoodHarnessTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 11, 23, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ManualDogfoodHarnessContracts_ExposeProofOnlyShape()
    {
        Assert.IsNotNull(typeof(IManualDogfoodHarnessService));
        Assert.IsNotNull(typeof(ManualDogfoodHarnessService));
        Assert.IsNotNull(typeof(ManualDogfoodHarnessRequest));
        Assert.IsNotNull(typeof(ManualDogfoodHarnessResult));
        Assert.IsNotNull(typeof(ManualDogfoodHarnessScenarioResult));
        Assert.IsNotNull(typeof(ManualDogfoodHarnessSummary));
        Assert.IsNotNull(typeof(ManualDogfoodHarnessStatus));
        Assert.IsNotNull(typeof(ManualDogfoodHarnessScenario));

        var forbiddenStates = new[] { "Approved", "Executing", "Applied", "Committed", "Submitted", "Promoted" };
        var names = Enum.GetNames<ManualDogfoodHarnessStatus>();
        foreach (var forbidden in forbiddenStates)
            Assert.IsFalse(names.Contains(forbidden, StringComparer.Ordinal), $"Harness status exposed forbidden execution state: {forbidden}");
    }

    [TestMethod]
    public async Task ManualDogfoodHarness_AllScenariosSucceeded_ReturnsSafeAdvisoryProof()
    {
        var service = new ManualDogfoodHarnessService();

        var result = await service.RunAsync(ValidRequest());

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(ManualDogfoodHarnessStatus.Succeeded, result.Status);
        Assert.AreEqual(3, result.Scenarios.Count);
        AssertScenarioSucceeded(result, ManualDogfoodHarnessScenario.TicketReviewFixProposal);
        AssertScenarioSucceeded(result, ManualDogfoodHarnessScenario.TestFailureRepairProposal);
        AssertScenarioSucceeded(result, ManualDogfoodHarnessScenario.RealRunMemoryImprovement);
        Assert.IsNotNull(result.Summary);
        Assert.IsNotNull(result.AuditEnvelope);
        AssertSafeSummary(result.Summary);
        AssertNoAuditIssues(result.AuditEnvelope);
        AssertNoThoughtLedgerIssues(result.AuditEnvelope.ThoughtLedger);
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.CreateReport && use.Outcome == AgentCapabilityUseOutcome.Allowed));
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.RunTool && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.MutateSource && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.PromoteCollectiveMemory && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.All(decision => !decision.GrantsAuthority && !decision.GrantsHumanApproval && !decision.GrantsPolicyApproval && !decision.GrantsMemoryPromotion));
        Assert.IsTrue(result.AuditEnvelope.ThoughtLedger.All(entry => !entry.ContainsRawPrivateReasoning && !entry.GrantsAuthority && !entry.GrantsApproval && !entry.GrantsMemoryPromotion));
    }

    [TestMethod]
    public async Task ManualDogfoodHarness_SelectedScenarioRunsOnlyThatScenario()
    {
        var service = new ManualDogfoodHarnessService();

        var result = await service.RunAsync(ValidRequest() with
        {
            IncludeTicketReviewLoop = false,
            IncludeTestFailureRepairLoop = true,
            IncludeRealRunMemoryImprovement = false
        });

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(ManualDogfoodHarnessStatus.Succeeded, result.Status);
        Assert.AreEqual(1, result.Scenarios.Count);
        AssertScenarioSucceeded(result, ManualDogfoodHarnessScenario.TestFailureRepairProposal);
        Assert.IsFalse(result.Scenarios.Any(scenario => scenario.Scenario == ManualDogfoodHarnessScenario.TicketReviewFixProposal));
        Assert.IsFalse(result.Scenarios.Any(scenario => scenario.Scenario == ManualDogfoodHarnessScenario.RealRunMemoryImprovement));
        Assert.IsNotNull(result.Summary);
        AssertSafeSummary(result.Summary);
    }

    [TestMethod]
    public async Task ManualDogfoodHarness_NoScenariosSelected_ReturnsInvalidRequest()
    {
        var service = new ManualDogfoodHarnessService();

        var result = await service.RunAsync(ValidRequest() with
        {
            IncludeTicketReviewLoop = false,
            IncludeTestFailureRepairLoop = false,
            IncludeRealRunMemoryImprovement = false
        });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualDogfoodHarnessStatus.InvalidRequest, result.Status);
        Assert.AreEqual(0, result.Scenarios.Count);
        Assert.IsNull(result.AuditEnvelope);
        Assert.IsTrue(result.Issues.Any(issue => issue.Severity == AgentDefinitionValidator.SeverityError));
    }

    [TestMethod]
    public async Task ManualDogfoodHarness_OneFailedScenario_ReturnsPartialWithoutAuthority()
    {
        var service = new ManualDogfoodHarnessService(
            new FailingTicketLoopService(),
            new ManualTestFailureRepairProposalLoopService(),
            new ManualRealRunMemoryImprovementService(),
            toolAuditStoreAvailable: false);

        var result = await service.RunAsync(ValidRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualDogfoodHarnessStatus.Partial, result.Status);
        AssertScenarioFailed(result, ManualDogfoodHarnessScenario.TicketReviewFixProposal);
        AssertScenarioSucceeded(result, ManualDogfoodHarnessScenario.TestFailureRepairProposal);
        AssertScenarioSucceeded(result, ManualDogfoodHarnessScenario.RealRunMemoryImprovement);
        Assert.IsNotNull(result.Summary);
        Assert.IsNotNull(result.AuditEnvelope);
        AssertSafeSummary(result.Summary);
        AssertNoAuditIssues(result.AuditEnvelope);
    }

    [TestMethod]
    public async Task ManualDogfoodHarness_AllScenariosFailed_ReturnsFailedWithoutAuditEnvelope()
    {
        var service = new ManualDogfoodHarnessService(
            new FailingTicketLoopService(),
            new FailingTestFailureLoopService(),
            new FailingRealRunMemoryService(),
            toolAuditStoreAvailable: false);

        var result = await service.RunAsync(ValidRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualDogfoodHarnessStatus.Failed, result.Status);
        Assert.AreEqual(3, result.Scenarios.Count);
        Assert.IsNotNull(result.AuditEnvelope);
        AssertNoAuditIssues(result.AuditEnvelope);
        Assert.IsNotNull(result.Summary);
        AssertSafeSummary(result.Summary);
        Assert.IsTrue(result.Issues.Count > 0);
    }

    [TestMethod]
    public async Task ManualDogfoodHarness_PersistToolAuditRequiresExplicitStore()
    {
        var service = new ManualDogfoodHarnessService();

        var result = await service.RunAsync(ValidRequest() with { PersistToolExecutionAudit = true });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualDogfoodHarnessStatus.InvalidRequest, result.Status);
        Assert.IsNull(result.AuditEnvelope);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualDogfoodHarnessValidator.DogfoodHarnessAuditStoreRequired));
    }

    [TestMethod]
    public async Task ManualDogfoodHarness_WithExplicitAuditStore_AppendsLoopToolAuditOnly()
    {
        var store = new FakeToolExecutionAuditStore();
        var service = new ManualDogfoodHarnessService(store);

        var result = await service.RunAsync(ValidRequest() with { PersistToolExecutionAudit = true });

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(ManualDogfoodHarnessStatus.Succeeded, result.Status);
        Assert.IsTrue(store.AppendCallCount >= 2);
        Assert.IsNotNull(result.Summary);
        AssertSafeSummary(result.Summary);
        Assert.IsTrue(store.Requests.All(request => request.Record.MutatesSource == false && request.Record.AppliesPatch == false));
    }

    [TestMethod]
    public void ManualDogfoodHarnessValidator_RejectsUnsafeScenarioFlags()
    {
        var scenario = SafeScenario() with { MutatesSource = true };

        var issues = new ManualDogfoodHarnessValidator().ValidateScenarioResult(scenario);

        Assert.IsTrue(issues.Any(issue => issue.Code == ManualDogfoodHarnessValidator.DogfoodHarnessSourceMutationForbidden));
    }

    [TestMethod]
    public void ManualDogfoodHarnessValidator_RejectsUnsafeSummaryFlags()
    {
        var summary = SafeSummary() with { CreatesAuthority = true };

        var issues = new ManualDogfoodHarnessValidator().ValidateSummary(summary);

        Assert.IsTrue(issues.Any(issue => issue.Code == ManualDogfoodHarnessValidator.DogfoodHarnessScenarioUnsafe));
    }

    [TestMethod]
    public void ManualDogfoodHarnessService_DoesNotExposeRuntimeOrMutationBoundary()
    {
        var repoRoot = FindRepoRoot();
        var servicePath = Path.Combine(repoRoot, "IronDev.Core", "Agents", "Concrete", "ManualDogfoodHarnessService.cs");
        var text = File.ReadAllText(servicePath);
        var normalised = text
            .Replace("PromoteCollectiveMemory", string.Empty, StringComparison.Ordinal)
            .Replace("PromotesMemory", string.Empty, StringComparison.Ordinal)
            .Replace("WritesWeaviate", string.Empty, StringComparison.Ordinal)
            .Replace("CreatesPullRequest", string.Empty, StringComparison.Ordinal)
            .Replace("IToolExecutionAuditStore", string.Empty, StringComparison.Ordinal)
            .Replace("ToolExecutionAudit", string.Empty, StringComparison.Ordinal)
            .Replace("SubmitsGitHubReview", string.Empty, StringComparison.Ordinal)
            .Replace("GitHubReview", string.Empty, StringComparison.Ordinal);

        var forbiddenTokens = new[]
        {
            "ProcessStartInfo",
            "Process.Start",
            "File.Copy",
            "File.Delete",
            "Directory.CreateDirectory",
            "SqlConnection",
            "HttpClient",
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "GitHubReview",
            "PullRequest",
            "WeaviateClient",
            "CollectiveMemoryStore"
        };

        foreach (var token in forbiddenTokens)
            Assert.IsFalse(normalised.Contains(token, StringComparison.Ordinal), $"Manual dogfood harness introduced forbidden runtime token: {token}");
    }

    [TestMethod]
    public void ManualDogfoodHarness_IsNotRegisteredInApiCliOrRuntime()
    {
        var repoRoot = FindRepoRoot();
        var filesToScan = Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("ManualDogfoodHarnessService.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("ManualDogfoodHarnessTests.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
                path.Contains($"{Path.DirectorySeparatorChar}IronDev.Api{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                path.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                path.Contains($"{Path.DirectorySeparatorChar}IronDev.Infrastructure{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                path.Contains($"{Path.DirectorySeparatorChar}IronDev.Client{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var path in filesToScan)
        {
            var text = File.ReadAllText(path);
            Assert.IsFalse(text.Contains(nameof(ManualDogfoodHarnessService), StringComparison.Ordinal), $"Manual dogfood harness service was wired into runtime file: {path}");
            Assert.IsFalse(text.Contains(nameof(IManualDogfoodHarnessService), StringComparison.Ordinal), $"Manual dogfood harness interface was wired into runtime file: {path}");
        }
    }

    private static ManualDogfoodHarnessRequest ValidRequest() =>
        new()
        {
            HarnessRunId = "manual-dogfood-harness-001",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RequestedByUserId = "human-reviewer",
            RequestedAtUtc = RequestedAt
        };

    private static ManualDogfoodHarnessScenarioResult SafeScenario() =>
        new()
        {
            Scenario = ManualDogfoodHarnessScenario.TicketReviewFixProposal,
            Succeeded = true,
            ScenarioRunId = "scenario-1",
            EvidenceRefs = ["evidence:scenario"],
            IsAdvisoryOnly = true,
            RequiresHumanReview = true
        };

    private static ManualDogfoodHarnessSummary SafeSummary() =>
        new()
        {
            Title = "Manual dogfood harness proof.",
            Summary = "Manual dogfood harness completed.",
            ScenarioSummaries = ["Scenario completed."],
            RequiredHumanDecisions = ["Decide whether to continue manually."],
            RequiredValidation = ["Run focused regression tests separately."],
            EvidenceRefs = ["evidence:harness"],
            IsAdvisoryOnly = true
        };

    private static void AssertScenarioSucceeded(ManualDogfoodHarnessResult result, ManualDogfoodHarnessScenario scenario)
    {
        var scenarioResult = result.Scenarios.Single(item => item.Scenario == scenario);
        Assert.IsTrue(scenarioResult.Succeeded);
        Assert.IsTrue(scenarioResult.IsAdvisoryOnly);
        Assert.IsFalse(scenarioResult.MutatesSource);
        Assert.IsFalse(scenarioResult.AppliesPatch);
        Assert.IsFalse(scenarioResult.RunsTestsAutomatically);
        Assert.IsFalse(scenarioResult.PromotesMemory);
        Assert.IsFalse(scenarioResult.WritesWeaviate);
        Assert.IsFalse(scenarioResult.CreatesPullRequest);
    }

    private static void AssertScenarioFailed(ManualDogfoodHarnessResult result, ManualDogfoodHarnessScenario scenario)
    {
        var scenarioResult = result.Scenarios.Single(item => item.Scenario == scenario);
        Assert.IsFalse(scenarioResult.Succeeded);
        Assert.IsTrue(scenarioResult.IsAdvisoryOnly);
        Assert.IsFalse(scenarioResult.MutatesSource);
        Assert.IsFalse(scenarioResult.AppliesPatch);
        Assert.IsFalse(scenarioResult.RunsTestsAutomatically);
        Assert.IsFalse(scenarioResult.PromotesMemory);
        Assert.IsFalse(scenarioResult.WritesWeaviate);
        Assert.IsFalse(scenarioResult.CreatesPullRequest);
    }

    private static void AssertSafeSummary(ManualDogfoodHarnessSummary summary)
    {
        Assert.IsTrue(summary.IsAdvisoryOnly);
        Assert.IsFalse(summary.GrantsApproval);
        Assert.IsFalse(summary.CreatesAuthority);
        Assert.IsFalse(summary.MutatesSource);
        Assert.IsFalse(summary.AppliesPatch);
        Assert.IsFalse(summary.RunsTestsAutomatically);
        Assert.IsFalse(summary.PromotesMemory);
        Assert.IsFalse(summary.WritesWeaviate);
        Assert.IsFalse(summary.CreatesPullRequest);
        Assert.IsTrue(summary.ScenarioSummaries.Count > 0);
        Assert.IsTrue(summary.RequiredHumanDecisions.Count > 0);
        Assert.IsTrue(summary.RequiredValidation.Count > 0);
    }

    private static void AssertNoAuditIssues(AgentRunAuditEnvelope envelope)
    {
        var issues = new AgentRunAuditEnvelopeValidator().Validate(envelope);
        Assert.AreEqual(0, issues.Count, string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static void AssertNoThoughtLedgerIssues(IReadOnlyList<AuditThoughtLedgerEntry> entries)
    {
        var issues = new ThoughtLedgerSafetyValidator().Validate(entries);
        Assert.AreEqual(0, issues.Count, string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static string FormatIssues(IReadOnlyList<ManualDogfoodHarnessIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory.FullName;
    }

    private sealed class FailingTicketLoopService : IManualTicketReviewFixProposalLoopService
    {
        public Task<ManualTicketReviewFixProposalLoopResult> RunAsync(
            ManualTicketReviewFixProposalLoopRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ManualTicketReviewFixProposalLoopResult
            {
                Succeeded = false,
                Status = ManualTicketReviewFixProposalLoopStatus.Failed,
                LoopRunId = request.LoopRunId,
                Issues =
                [
                    new ManualTicketReviewFixProposalLoopIssue
                    {
                        Code = "FAKE_TICKET_FAILURE",
                        Severity = AgentDefinitionValidator.SeverityError,
                        Message = "Fake ticket loop failed."
                    }
                ]
            });
    }

    private sealed class FailingTestFailureLoopService : IManualTestFailureRepairProposalLoopService
    {
        public Task<ManualTestFailureRepairProposalLoopResult> RunAsync(
            ManualTestFailureRepairProposalLoopRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ManualTestFailureRepairProposalLoopResult
            {
                Succeeded = false,
                Status = ManualTestFailureRepairProposalLoopStatus.Failed,
                LoopRunId = request.LoopRunId,
                Issues =
                [
                    new ManualTestFailureRepairProposalLoopIssue
                    {
                        Code = "FAKE_TEST_FAILURE",
                        Severity = AgentDefinitionValidator.SeverityError,
                        Message = "Fake test failure loop failed."
                    }
                ]
            });
    }

    private sealed class FailingRealRunMemoryService : IManualRealRunMemoryImprovementService
    {
        public Task<ManualRealRunMemoryImprovementResult> RunAsync(
            ManualRealRunMemoryImprovementRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ManualRealRunMemoryImprovementResult
            {
                Succeeded = false,
                Status = ManualRealRunMemoryImprovementStatus.Failed,
                MemoryImprovementRunId = request.MemoryImprovementRunId,
                Issues =
                [
                    new ManualRealRunMemoryImprovementIssue
                    {
                        Code = "FAKE_MEMORY_FAILURE",
                        Severity = AgentDefinitionValidator.SeverityError,
                        Message = "Fake memory improvement failed."
                    }
                ]
            });
    }

    private sealed class FakeToolExecutionAuditStore(ToolExecutionAuditAppendStatus status = ToolExecutionAuditAppendStatus.Appended) : IToolExecutionAuditStore
    {
        public int AppendCallCount { get; private set; }
        public IReadOnlyList<ToolExecutionAuditAppendRequest> Requests => _requests;
        private readonly List<ToolExecutionAuditAppendRequest> _requests = [];

        public Task<ToolExecutionAuditAppendResult> AppendAsync(ToolExecutionAuditAppendRequest request, CancellationToken cancellationToken = default)
        {
            AppendCallCount++;
            _requests.Add(request);
            return Task.FromResult(new ToolExecutionAuditAppendResult
            {
                Status = status,
                ToolExecutionAuditId = request.Record.ToolExecutionAuditId,
                PayloadSha256 = request.Record.PayloadSha256,
                AuditEnvelopeSha256 = request.Record.AuditEnvelopeSha256,
                Issues = status == ToolExecutionAuditAppendStatus.Rejected
                    ? [new ToolExecutionAuditIssue { Code = "FAKE_REJECTED", Severity = AgentDefinitionValidator.SeverityError, Message = "Rejected by fake store." }]
                    : []
            });
        }

        public Task<ToolExecutionAuditReadResult> GetAsync(ToolExecutionAuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ToolExecutionAuditRecord>> ListByRunAsync(ToolExecutionAuditRunQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
