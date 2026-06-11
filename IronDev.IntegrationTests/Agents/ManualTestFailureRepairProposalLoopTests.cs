using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ManualTestFailureRepairProposalLoopTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 11, 22, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ManualTestFailureRepairProposalLoopContracts_ExposeNonExecutableLoopShape()
    {
        Assert.IsNotNull(typeof(IManualTestFailureRepairProposalLoopService));
        Assert.IsNotNull(typeof(ManualTestFailureRepairProposalLoopService));
        Assert.IsNotNull(typeof(ManualTestFailureRepairProposalLoopRequest));
        Assert.IsNotNull(typeof(ManualTestFailureRepairProposalLoopResult));
        Assert.IsNotNull(typeof(ManualTestFailureRepairProposalLoopStatus));
        Assert.IsNotNull(typeof(ManualTestFailureInput));
        Assert.IsNotNull(typeof(ManualTestFailureEvidenceBundle));
        Assert.IsNotNull(typeof(ManualTestFailureCriticStage));
        Assert.IsNotNull(typeof(ManualTestFailureRepairProposalStage));
        Assert.IsNotNull(typeof(ManualTestFailureAuditStage));
        Assert.IsNotNull(typeof(ManualTestFailureLoopSummary));

        var forbiddenStates = new[] { "Approved", "Executing", "Applied", "Committed", "Submitted", "Promoted" };
        var names = Enum.GetNames<ManualTestFailureRepairProposalLoopStatus>();
        foreach (var forbidden in forbiddenStates)
            Assert.IsFalse(names.Contains(forbidden, StringComparer.Ordinal), $"Loop status exposed forbidden execution state: {forbidden}");
    }

    [TestMethod]
    public async Task ManualTestFailureRepairProposalLoop_HappyPathCreatesReviewAndProposalOnlySummary()
    {
        var service = new ManualTestFailureRepairProposalLoopService();

        var result = await service.RunAsync(ValidRequest());

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(ManualTestFailureRepairProposalLoopStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.CriticStage);
        Assert.IsNotNull(result.ProposalStage);
        Assert.IsNotNull(result.Summary);
        Assert.IsNotNull(result.AuditStage);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.AreEqual(AgentSpecialisationCatalog.TestFailureCritic.SpecialisationId, result.CriticStage.CriticProfileId);
        Assert.AreEqual(CriticReviewVerdict.RequestChanges, result.CriticStage.Verdict);
        Assert.IsTrue(result.CriticStage.IsReviewOnly);
        Assert.IsFalse(result.CriticStage.BlocksExecution);
        Assert.IsFalse(result.CriticStage.GrantsApproval);
        Assert.IsTrue(result.ProposalStage.IsProposalOnly);
        Assert.IsTrue(result.ProposalStage.RequiresHumanReview);
        AssertDangerousProposalFlagsFalse(result.ProposalStage);
        Assert.IsTrue(result.Summary.IsAdvisoryOnly);
        Assert.IsFalse(result.Summary.GrantsApproval);
        Assert.IsFalse(result.Summary.GrantsExecutionPermission);
        Assert.IsFalse(result.Summary.MutatesSource);
        Assert.IsFalse(result.Summary.AppliesPatch);
        Assert.IsFalse(result.Summary.RunsTests);
        Assert.IsFalse(result.Summary.CreatesPullRequest);
        Assert.IsFalse(result.Summary.PromotesMemory);
        AssertNoAuditIssues(result.AuditEnvelope);
        AssertNoThoughtLedgerIssues(result.AuditEnvelope.ThoughtLedger);
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.CreateReport && use.Outcome == AgentCapabilityUseOutcome.Allowed));
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.RunTool && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.MutateSource && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.All(decision => !decision.GrantsAuthority && !decision.GrantsHumanApproval && !decision.GrantsPolicyApproval && !decision.GrantsMemoryPromotion));
        Assert.IsTrue(result.AuditEnvelope.ThoughtLedger.All(entry => !entry.ContainsRawPrivateReasoning && !entry.GrantsAuthority && !entry.GrantsApproval && !entry.GrantsMemoryPromotion));
    }

    [TestMethod]
    public async Task ManualTestFailureRepairProposalLoop_PersistFalseDoesNotAppendToolAudit()
    {
        var store = new FakeToolExecutionAuditStore();
        var service = new ManualTestFailureRepairProposalLoopService(
            new ManualIndependentCriticAgentService(),
            new ManualImplementationAgentPatchProposalService(),
            new AgentToolExecutionGate(),
            store);

        var result = await service.RunAsync(ValidRequest() with { PersistToolExecutionAudit = false });

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(0, store.AppendCallCount);
        Assert.IsFalse(result.AuditStage!.PersistedToolExecutionAudit);
    }

    [TestMethod]
    public async Task ManualTestFailureRepairProposalLoop_PersistTrueAppendsSafeToolAudit()
    {
        var store = new FakeToolExecutionAuditStore();
        var service = new ManualTestFailureRepairProposalLoopService(
            new ManualIndependentCriticAgentService(),
            new ManualImplementationAgentPatchProposalService(),
            new AgentToolExecutionGate(),
            store);

        var result = await service.RunAsync(ValidRequest() with { PersistToolExecutionAudit = true });

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(1, store.AppendCallCount);
        Assert.IsNotNull(store.LastRequest);
        Assert.AreEqual(AgentToolKind.PatchProposal, store.LastRequest.Record.ToolKind);
        Assert.AreEqual(AgentToolRequestType.PatchProposalRequest, store.LastRequest.Record.RequestType);
        Assert.AreEqual(AgentKind.ImplementationAgent, store.LastRequest.Record.AgentKind);
        Assert.IsFalse(store.LastRequest.Record.MutatesSource);
        Assert.IsFalse(store.LastRequest.Record.AppliesPatch);
        Assert.IsFalse(store.LastRequest.Record.ExecutesTool);
        Assert.IsFalse(store.LastRequest.Record.CallsExternalSystem);
        Assert.IsTrue(result.AuditStage!.PersistedToolExecutionAudit);
        Assert.AreEqual(ToolExecutionAuditAppendStatus.Appended, result.ProposalStage!.ToolExecutionAuditStatus);
    }

    [TestMethod]
    public async Task ManualTestFailureRepairProposalLoop_ToolAuditRejectedNeedsHumanReview()
    {
        var store = new FakeToolExecutionAuditStore(ToolExecutionAuditAppendStatus.Rejected);
        var service = new ManualTestFailureRepairProposalLoopService(
            new ManualIndependentCriticAgentService(),
            new ManualImplementationAgentPatchProposalService(),
            new AgentToolExecutionGate(),
            store);

        var result = await service.RunAsync(ValidRequest() with { PersistToolExecutionAudit = true });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTestFailureRepairProposalLoopStatus.NeedsHumanReview, result.Status);
        Assert.AreEqual(1, store.AppendCallCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTestFailureRepairProposalLoopValidator.TestFailureLoopToolAuditRejected));
        Assert.AreEqual(ToolExecutionAuditAppendStatus.Rejected, result.ProposalStage!.ToolExecutionAuditStatus);
    }

    [TestMethod]
    public async Task ManualTestFailureRepairProposalLoop_InvalidRequestStopsBeforeCriticAndProposal()
    {
        var critic = new CountingCriticService();
        var proposal = new CountingProposalService(new ManualImplementationAgentPatchProposalService());
        var service = new ManualTestFailureRepairProposalLoopService(critic, proposal, new AgentToolExecutionGate());

        var request = ValidRequest() with
        {
            Failure = ValidRequest().Failure with { ContainsRawPrivateReasoning = true }
        };

        var result = await service.RunAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTestFailureRepairProposalLoopStatus.InvalidRequest, result.Status);
        Assert.AreEqual(0, critic.CallCount);
        Assert.AreEqual(0, proposal.CallCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTestFailureRepairProposalLoopValidator.TestFailureLoopUnsafeInput));
    }

    [TestMethod]
    public async Task ManualTestFailureRepairProposalLoop_CriticNoObjectionStopsBeforeProposal()
    {
        var critic = new NoObjectionCriticService();
        var proposal = new CountingProposalService(new ManualImplementationAgentPatchProposalService());
        var service = new ManualTestFailureRepairProposalLoopService(critic, proposal, new AgentToolExecutionGate());

        var result = await service.RunAsync(ValidRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTestFailureRepairProposalLoopStatus.CriticRejected, result.Status);
        Assert.AreEqual(1, critic.CallCount);
        Assert.AreEqual(0, proposal.CallCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTestFailureRepairProposalLoopValidator.TestFailureLoopCriticRejected));
    }

    [TestMethod]
    public async Task ManualTestFailureRepairProposalLoop_GateBlockedStopsBeforeProposal()
    {
        var proposal = new CountingProposalService(new ManualImplementationAgentPatchProposalService());
        var service = new ManualTestFailureRepairProposalLoopService(
            new ManualIndependentCriticAgentService(),
            proposal,
            new FixedGate(AgentToolExecutionGateDecisionType.Blocked));

        var result = await service.RunAsync(ValidRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTestFailureRepairProposalLoopStatus.Blocked, result.Status);
        Assert.AreEqual(0, proposal.CallCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTestFailureRepairProposalLoopValidator.TestFailureLoopGateBlocked));
    }

    [TestMethod]
    public async Task ManualTestFailureRepairProposalLoop_UnsafeProposalOutputIsRejected()
    {
        var service = new ManualTestFailureRepairProposalLoopService(
            new ManualIndependentCriticAgentService(),
            new UnsafeProposalService(),
            new AgentToolExecutionGate());

        var result = await service.RunAsync(ValidRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTestFailureRepairProposalLoopStatus.ProposalRejected, result.Status);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTestFailureRepairProposalLoopValidator.TestFailureLoopProposalRejected));
    }

    [TestMethod]
    public void ManualTestFailureRepairProposalLoop_ProductionFileDoesNotAddRuntimeOrMutationBoundary()
    {
        var root = FindRepoRoot();
        var servicePath = Path.Combine(root, "IronDev.Core", "Agents", "Concrete", "ManualTestFailureRepairProposalLoopService.cs");
        var serviceText = File.ReadAllText(servicePath);

        var forbiddenTokens = new[]
        {
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete",
            "Directory.CreateDirectory",
            "SqlConnection",
            "HttpClient",
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication"
        };

        foreach (var token in forbiddenTokens)
            Assert.IsFalse(serviceText.Contains(token, StringComparison.Ordinal), $"Loop service introduced forbidden runtime/mutation token: {token}");

        var programPath = Path.Combine(root, "IronDev.Api", "Program.cs");
        if (File.Exists(programPath))
        {
            var programText = File.ReadAllText(programPath);
            Assert.IsFalse(programText.Contains(nameof(ManualTestFailureRepairProposalLoopService), StringComparison.Ordinal));
        }
    }

    private static ManualTestFailureRepairProposalLoopRequest ValidRequest() =>
        new()
        {
            LoopRunId = "test-failure-loop-001",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RequestedByUserId = "human-reviewer",
            RequestedAtUtc = RequestedAt,
            Failure = new ManualTestFailureInput
            {
                FailureRef = "failure-123",
                TestRunRef = "test-run-123",
                TestName = "IronDev.Tests.CliEnvelopeTests.SupervisorOutputUsesStableEnvelope",
                FailureSummary = "Supervisor CLI output did not match the stable envelope contract.",
                FailureMessage = "Assert.AreEqual failed. Expected:<succeeded>. Actual:<failed>.",
                StackTraceSummary = "CliEnvelopeTests.cs:42",
                FailedAssertions = ["Expected status succeeded but got failed."],
                RelatedFiles = ["tests/CliEnvelopeTests.cs", "tools/IronDev.Cli/IronDevCli.cs"],
                EvidenceRefs = ["test-run:test-run-123", "failure:failure-123"]
            },
            EvidenceBundle = new ManualTestFailureEvidenceBundle
            {
                Items =
                [
                    new ManualTestFailureEvidenceItem
                    {
                        EvidenceId = "evidence-test-failure-report",
                        RefType = "TestFailureEvidence",
                        RefId = "failure-123-report",
                        Source = "manual-review",
                        Summary = "Test failure evidence captured by a human reviewer.",
                        EvidenceRefs = ["test-run:test-run-123", "failure:failure-123"],
                        SupportsFailureReview = true,
                        SupportsRepairProposal = true
                    }
                ]
            }
        };

    private static void AssertDangerousProposalFlagsFalse(ManualTestFailureRepairProposalStage stage)
    {
        Assert.IsFalse(stage.MutatesSource);
        Assert.IsFalse(stage.AppliesPatch);
        Assert.IsTrue(stage.RequiresValidation);
        Assert.IsTrue(stage.RequiresSeparateTestRerun);
        Assert.IsFalse(stage.RunsTests);
        Assert.IsFalse(stage.CreatesPullRequest);
        Assert.IsFalse(stage.PromotesMemory);
        Assert.IsFalse(stage.CreatesAuthority);
        Assert.IsFalse(stage.CreatesRuntimeAction);
        Assert.IsNotNull(stage.Output);
        Assert.IsFalse(stage.Output.MutatesSource);
        Assert.IsFalse(stage.Output.AppliesPatch);
        Assert.IsFalse(stage.Output.WritesFiles);
        Assert.IsFalse(stage.Output.DeletesFiles);
        Assert.IsFalse(stage.Output.RunsGit);
        Assert.IsFalse(stage.Output.CallsExternalSystem);
        Assert.IsFalse(stage.Output.SubmitsGitHubReview);
        Assert.IsFalse(stage.Output.PromotesMemory);
        Assert.IsFalse(stage.Output.CreatesCollectiveMemory);
        Assert.IsFalse(stage.Output.WritesWeaviate);
        Assert.IsTrue(stage.Output.Proposal.IsProposalOnly);
        Assert.IsTrue(stage.Output.Proposal.RequiresHumanReview);
        Assert.IsTrue(stage.Output.Proposal.RequiresValidation);
        Assert.IsFalse(stage.Output.Proposal.CreatesAuthority);
        Assert.IsFalse(stage.Output.Proposal.CreatesRuntimeAction);
        Assert.IsFalse(stage.Output.Proposal.MutatesSource);
        Assert.IsFalse(stage.Output.Proposal.AppliesPatch);
    }

    private static void AssertNoAuditIssues(AgentRunAuditEnvelope envelope)
    {
        var issues = new AgentRunAuditEnvelopeValidator().Validate(envelope);
        Assert.AreEqual(0, issues.Count, string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static void AssertNoThoughtLedgerIssues(IReadOnlyList<IronDev.Core.Agents.Audit.ThoughtLedgerEntry> entries)
    {
        var issues = new ThoughtLedgerSafetyValidator().Validate(entries);
        Assert.AreEqual(0, issues.Count, string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static string FormatIssues(IReadOnlyList<ManualTestFailureRepairProposalLoopIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory.FullName;
    }

    private sealed class FakeToolExecutionAuditStore(ToolExecutionAuditAppendStatus status = ToolExecutionAuditAppendStatus.Appended) : IToolExecutionAuditStore
    {
        public int AppendCallCount { get; private set; }
        public ToolExecutionAuditAppendRequest? LastRequest { get; private set; }

        public Task<ToolExecutionAuditAppendResult> AppendAsync(ToolExecutionAuditAppendRequest request, CancellationToken cancellationToken = default)
        {
            AppendCallCount++;
            LastRequest = request;
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

    private sealed class CountingCriticService : IManualIndependentCriticAgentService
    {
        private readonly IManualIndependentCriticAgentService _inner = new ManualIndependentCriticAgentService();

        public int CallCount { get; private set; }

        public ManualCriticReviewResult Review(ManualCriticReviewRequest request, DateTimeOffset reviewedAtUtc)
        {
            CallCount++;
            return _inner.Review(request, reviewedAtUtc);
        }
    }

    private sealed class NoObjectionCriticService : IManualIndependentCriticAgentService
    {
        private readonly IManualIndependentCriticAgentService _inner = new ManualIndependentCriticAgentService();

        public int CallCount { get; private set; }

        public ManualCriticReviewResult Review(ManualCriticReviewRequest request, DateTimeOffset reviewedAtUtc)
        {
            CallCount++;
            var result = _inner.Review(request with { RequestedVerdict = CriticReviewVerdict.NoObjection, FindingDrafts = [] }, reviewedAtUtc);
            return result;
        }
    }

    private sealed class CountingProposalService(IManualImplementationAgentPatchProposalService inner) : IManualImplementationAgentPatchProposalService
    {
        public int CallCount { get; private set; }

        public ManualImplementationPatchProposalResult Propose(ManualImplementationPatchProposalRequest request)
        {
            CallCount++;
            return inner.Propose(request);
        }
    }

    private sealed class UnsafeProposalService : IManualImplementationAgentPatchProposalService
    {
        private readonly IManualImplementationAgentPatchProposalService _inner = new ManualImplementationAgentPatchProposalService();

        public ManualImplementationPatchProposalResult Propose(ManualImplementationPatchProposalRequest request)
        {
            var result = _inner.Propose(request);
            if (result.Output is null)
                return result;

            return result with
            {
                Output = result.Output with { MutatesSource = true }
            };
        }
    }

    private sealed class FixedGate(AgentToolExecutionGateDecisionType decisionType) : IAgentToolExecutionGate
    {
        public AgentToolExecutionGateResult Evaluate(AgentToolExecutionGateRequest request) =>
            new()
            {
                Succeeded = true,
                Decision = new AgentToolExecutionGateDecision
                {
                    GateDecisionId = $"fixed-gate-{request.ToolRequest.ToolRequestId}",
                    ToolRequestId = request.ToolRequest.ToolRequestId,
                    Decision = decisionType,
                    ToolKind = request.ToolRequest.ToolKind,
                    RequestType = request.ToolRequest.RequestType,
                    RiskLevel = request.ToolRequest.RiskLevel,
                    EvaluatedAtUtc = RequestedAt,
                    GrantsExecution = decisionType == AgentToolExecutionGateDecisionType.Allowed,
                    ExecutesTool = false,
                    MutatesSource = false,
                    CallsExternalSystem = false,
                    SubmitsGitHubReview = false,
                    PersistsResult = false,
                    PromotesMemory = false,
                    CreatesCollectiveMemory = false,
                    WritesWeaviate = false,
                    RequiresExecutor = decisionType == AgentToolExecutionGateDecisionType.Allowed,
                    Reasons =
                    [
                        new AgentToolExecutionGateReason
                        {
                            Code = "FIXED_GATE",
                            Severity = decisionType == AgentToolExecutionGateDecisionType.Allowed ? "info" : "error",
                            Message = $"Fixed gate returned {decisionType}."
                        }
                    ]
                }
            };
    }
}
