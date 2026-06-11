using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ManualTicketReviewFixProposalLoopTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 11, 22, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ManualTicketReviewFixProposalLoopContracts_ExposeNonExecutableLoopShape()
    {
        Assert.IsNotNull(typeof(IManualTicketReviewFixProposalLoopService));
        Assert.IsNotNull(typeof(ManualTicketReviewFixProposalLoopService));
        Assert.IsNotNull(typeof(ManualTicketReviewFixProposalLoopRequest));
        Assert.IsNotNull(typeof(ManualTicketReviewFixProposalLoopResult));
        Assert.IsNotNull(typeof(ManualTicketReviewFixProposalLoopStatus));
        Assert.IsNotNull(typeof(ManualTicketReviewTicketInput));
        Assert.IsNotNull(typeof(ManualTicketReviewEvidenceBundle));
        Assert.IsNotNull(typeof(ManualTicketReviewCriticStage));
        Assert.IsNotNull(typeof(ManualTicketReviewProposalStage));
        Assert.IsNotNull(typeof(ManualTicketReviewAuditStage));
        Assert.IsNotNull(typeof(ManualTicketReviewLoopSummary));

        var forbiddenStates = new[] { "Approved", "Executing", "Applied", "Committed", "Submitted", "Promoted" };
        var names = Enum.GetNames<ManualTicketReviewFixProposalLoopStatus>();
        foreach (var forbidden in forbiddenStates)
            Assert.IsFalse(names.Contains(forbidden, StringComparer.Ordinal), $"Loop status exposed forbidden execution state: {forbidden}");
    }

    [TestMethod]
    public async Task ManualTicketReviewFixProposalLoop_HappyPathCreatesReviewAndProposalOnlySummary()
    {
        var service = new ManualTicketReviewFixProposalLoopService();

        var result = await service.RunAsync(ValidRequest());

        Assert.IsTrue(result.Succeeded, FormatIssues(result.Issues));
        Assert.AreEqual(ManualTicketReviewFixProposalLoopStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.CriticStage);
        Assert.IsNotNull(result.ProposalStage);
        Assert.IsNotNull(result.Summary);
        Assert.IsNotNull(result.AuditStage);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.AreEqual(AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId, result.CriticStage.CriticProfileId);
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
        AssertNoAuditIssues(result.AuditEnvelope);
        AssertNoThoughtLedgerIssues(result.AuditEnvelope.ThoughtLedger);
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.CreateReport && use.Outcome == AgentCapabilityUseOutcome.Allowed));
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.RunTool && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(result.AuditEnvelope.CapabilityUses.Any(use => use.Capability == AgentCapability.MutateSource && use.Outcome == AgentCapabilityUseOutcome.Blocked));
        Assert.IsTrue(result.AuditEnvelope.BoundaryDecisions.All(decision => !decision.GrantsAuthority && !decision.GrantsHumanApproval && !decision.GrantsPolicyApproval && !decision.GrantsMemoryPromotion));
        Assert.IsTrue(result.AuditEnvelope.ThoughtLedger.All(entry => !entry.ContainsRawPrivateReasoning && !entry.GrantsAuthority && !entry.GrantsApproval && !entry.GrantsMemoryPromotion));
    }

    [TestMethod]
    public async Task ManualTicketReviewFixProposalLoop_PersistFalseDoesNotAppendToolAudit()
    {
        var store = new FakeToolExecutionAuditStore();
        var service = new ManualTicketReviewFixProposalLoopService(
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
    public async Task ManualTicketReviewFixProposalLoop_PersistTrueAppendsSafeToolAudit()
    {
        var store = new FakeToolExecutionAuditStore();
        var service = new ManualTicketReviewFixProposalLoopService(
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
    public async Task ManualTicketReviewFixProposalLoop_ToolAuditRejectedNeedsHumanReview()
    {
        var store = new FakeToolExecutionAuditStore(ToolExecutionAuditAppendStatus.Rejected);
        var service = new ManualTicketReviewFixProposalLoopService(
            new ManualIndependentCriticAgentService(),
            new ManualImplementationAgentPatchProposalService(),
            new AgentToolExecutionGate(),
            store);

        var result = await service.RunAsync(ValidRequest() with { PersistToolExecutionAudit = true });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTicketReviewFixProposalLoopStatus.NeedsHumanReview, result.Status);
        Assert.AreEqual(1, store.AppendCallCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTicketReviewFixProposalLoopValidator.TicketLoopToolAuditRejected));
        Assert.AreEqual(ToolExecutionAuditAppendStatus.Rejected, result.ProposalStage!.ToolExecutionAuditStatus);
    }

    [TestMethod]
    public async Task ManualTicketReviewFixProposalLoop_InvalidRequestStopsBeforeCriticAndProposal()
    {
        var critic = new CountingCriticService();
        var proposal = new CountingProposalService(new ManualImplementationAgentPatchProposalService());
        var service = new ManualTicketReviewFixProposalLoopService(critic, proposal, new AgentToolExecutionGate());

        var request = ValidRequest() with
        {
            Ticket = ValidRequest().Ticket with { ContainsRawPrivateReasoning = true }
        };

        var result = await service.RunAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTicketReviewFixProposalLoopStatus.InvalidRequest, result.Status);
        Assert.AreEqual(0, critic.CallCount);
        Assert.AreEqual(0, proposal.CallCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTicketReviewFixProposalLoopValidator.TicketLoopUnsafeInput));
    }

    [TestMethod]
    public async Task ManualTicketReviewFixProposalLoop_CriticNoObjectionStopsBeforeProposal()
    {
        var critic = new NoObjectionCriticService();
        var proposal = new CountingProposalService(new ManualImplementationAgentPatchProposalService());
        var service = new ManualTicketReviewFixProposalLoopService(critic, proposal, new AgentToolExecutionGate());

        var result = await service.RunAsync(ValidRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTicketReviewFixProposalLoopStatus.CriticRejected, result.Status);
        Assert.AreEqual(1, critic.CallCount);
        Assert.AreEqual(0, proposal.CallCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTicketReviewFixProposalLoopValidator.TicketLoopCriticRejected));
    }

    [TestMethod]
    public async Task ManualTicketReviewFixProposalLoop_GateBlockedStopsBeforeProposal()
    {
        var proposal = new CountingProposalService(new ManualImplementationAgentPatchProposalService());
        var service = new ManualTicketReviewFixProposalLoopService(
            new ManualIndependentCriticAgentService(),
            proposal,
            new FixedGate(AgentToolExecutionGateDecisionType.Blocked));

        var result = await service.RunAsync(ValidRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTicketReviewFixProposalLoopStatus.Blocked, result.Status);
        Assert.AreEqual(0, proposal.CallCount);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTicketReviewFixProposalLoopValidator.TicketLoopGateBlocked));
    }

    [TestMethod]
    public async Task ManualTicketReviewFixProposalLoop_UnsafeProposalOutputIsRejected()
    {
        var service = new ManualTicketReviewFixProposalLoopService(
            new ManualIndependentCriticAgentService(),
            new UnsafeProposalService(),
            new AgentToolExecutionGate());

        var result = await service.RunAsync(ValidRequest());

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ManualTicketReviewFixProposalLoopStatus.ProposalRejected, result.Status);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == ManualTicketReviewFixProposalLoopValidator.TicketLoopProposalRejected));
    }

    [TestMethod]
    public void ManualTicketReviewFixProposalLoop_ProductionFileDoesNotAddRuntimeOrMutationBoundary()
    {
        var root = FindRepoRoot();
        var servicePath = Path.Combine(root, "IronDev.Core", "Agents", "Concrete", "ManualTicketReviewFixProposalLoopService.cs");
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
            Assert.IsFalse(programText.Contains(nameof(ManualTicketReviewFixProposalLoopService), StringComparison.Ordinal));
        }
    }

    private static ManualTicketReviewFixProposalLoopRequest ValidRequest() =>
        new()
        {
            LoopRunId = "ticket-loop-001",
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RequestedByUserId = "human-reviewer",
            RequestedAtUtc = RequestedAt,
            Ticket = new ManualTicketReviewTicketInput
            {
                TicketRef = "TICKET-123",
                Title = "Add guarded ticket report",
                Description = "The system needs a proposal-only fix package for the ticket report path.",
                AcceptanceCriteria = ["Report path has deterministic validation and no source mutation in the proposal loop."],
                EvidenceRefs = ["ticket-evidence:TICKET-123"]
            },
            EvidenceBundle = new ManualTicketReviewEvidenceBundle
            {
                Items =
                [
                    new ManualTicketReviewEvidenceItem
                    {
                        EvidenceId = "evidence-ticket-report",
                        RefType = "TicketEvidence",
                        RefId = "TICKET-123-report",
                        Source = "manual-review",
                        Summary = "Ticket report evidence captured by a human reviewer.",
                        EvidenceRefs = ["ticket-evidence:TICKET-123"],
                        SupportsReview = true
                    }
                ]
            }
        };

    private static void AssertDangerousProposalFlagsFalse(ManualTicketReviewProposalStage stage)
    {
        Assert.IsFalse(stage.MutatesSource);
        Assert.IsFalse(stage.AppliesPatch);
        Assert.IsFalse(stage.WritesFiles);
        Assert.IsFalse(stage.DeletesFiles);
        Assert.IsFalse(stage.RunsGit);
        Assert.IsFalse(stage.CallsExternalSystem);
        Assert.IsFalse(stage.SubmitsGitHubReview);
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

    private static string FormatIssues(IReadOnlyList<ManualTicketReviewFixProposalLoopIssue> issues) =>
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

