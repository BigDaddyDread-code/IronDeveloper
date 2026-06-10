using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Execution;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
public sealed class MemoryExecutionGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task MemoryExecutionGate_NullContext_ReturnsNotMemoryBackedWithoutCallingConscience()
    {
        var conscience = new FakeConscienceMemoryGovernanceService();
        var gate = new MemoryExecutionGate(conscience);

        var result = await gate.EvaluateAsync(null);

        Assert.AreEqual(MemoryExecutionGateDecision.NotMemoryBacked, result.Decision);
        Assert.IsTrue(result.MayProceedToPolicyGate);
        Assert.IsFalse(result.Evidence.IsMemoryBacked);
        Assert.AreEqual(0, conscience.CallCount);
    }

    [TestMethod]
    public async Task MemoryExecutionGate_EmptyReferences_BlocksWithoutCallingConscience()
    {
        var conscience = new FakeConscienceMemoryGovernanceService();
        var gate = new MemoryExecutionGate(conscience);

        var result = await gate.EvaluateAsync(BuildContext() with { ReferencedArtifacts = [] });

        Assert.AreEqual(MemoryExecutionGateDecision.Blocked, result.Decision);
        Assert.IsFalse(result.MayProceedToPolicyGate);
        CollectionAssert.Contains(result.Evidence.IssueCodes.ToArray(), MemoryGovernanceIssueCode.MissingReferencedArtifacts);
        Assert.AreEqual(0, conscience.CallCount);
    }

    [TestMethod]
    public async Task MemoryExecutionGate_ValidMemoryContext_CallsConscienceAndAllowsPolicyGate()
    {
        var conscience = new FakeConscienceMemoryGovernanceService();
        var gate = new MemoryExecutionGate(conscience);

        var result = await gate.EvaluateAsync(BuildContext());

        Assert.AreEqual(MemoryExecutionGateDecision.Allowed, result.Decision);
        Assert.IsTrue(result.MayProceedToPolicyGate);
        Assert.AreEqual(1, conscience.CallCount);
        Assert.AreEqual("memory-1", conscience.LastRequest!.ReferencedArtifacts.Single().MemoryItemId);
        Assert.AreEqual("decision-1", conscience.LastRequest.DecisionId);
        Assert.AreEqual("memory-governance-allow", result.Evidence.GovernanceCheckId);
    }

    [TestMethod]
    public async Task MemoryExecutionGate_GovernanceBlock_BlocksExecution()
    {
        var conscience = new FakeConscienceMemoryGovernanceService
        {
            Result = BuildGovernanceResult(MemoryGovernanceDecision.Block, [BuildIssue(MemoryGovernanceIssueCode.MemoryExpired)])
        };
        var gate = new MemoryExecutionGate(conscience);

        var result = await gate.EvaluateAsync(BuildContext());

        Assert.AreEqual(MemoryExecutionGateDecision.Blocked, result.Decision);
        Assert.IsFalse(result.MayProceedToPolicyGate);
        CollectionAssert.Contains(result.Evidence.IssueCodes.ToArray(), MemoryGovernanceIssueCode.MemoryExpired);
    }

    [TestMethod]
    public async Task MemoryExecutionGate_GovernanceWarn_RequiresOuterApprovalButProceedsToPolicy()
    {
        var conscience = new FakeConscienceMemoryGovernanceService
        {
            Result = BuildGovernanceResult(MemoryGovernanceDecision.Warn, [BuildIssue(MemoryGovernanceIssueCode.LowConfidenceMemoryUse)])
        };
        var gate = new MemoryExecutionGate(conscience);

        var result = await gate.EvaluateAsync(BuildContext());

        Assert.AreEqual(MemoryExecutionGateDecision.WarningRequiresOuterApproval, result.Decision);
        Assert.IsTrue(result.MayProceedToPolicyGate);
        CollectionAssert.Contains(result.Evidence.IssueCodes.ToArray(), MemoryGovernanceIssueCode.LowConfidenceMemoryUse);
    }

    [TestMethod]
    public async Task MemoryExecutionGate_GovernanceAllow_ProceedsToPolicy()
    {
        var gate = new MemoryExecutionGate(new FakeConscienceMemoryGovernanceService());

        var result = await gate.EvaluateAsync(BuildContext());

        Assert.AreEqual(MemoryExecutionGateDecision.Allowed, result.Decision);
        Assert.IsTrue(result.MayProceedToPolicyGate);
        Assert.AreEqual(MemoryGovernanceDecision.Allow, result.Evidence.GovernanceDecision);
    }

    [TestMethod]
    public async Task MemoryExecutionGate_SuppliedMismatchedResult_Blocks()
    {
        var conscience = new FakeConscienceMemoryGovernanceService();
        var gate = new MemoryExecutionGate(conscience);
        var supplied = BuildGovernanceResult(MemoryGovernanceDecision.Allow) with { DecisionId = "other-decision" };

        var result = await gate.EvaluateAsync(BuildContext() with { SuppliedGovernanceResult = supplied });

        Assert.AreEqual(MemoryExecutionGateDecision.Blocked, result.Decision);
        Assert.IsFalse(result.MayProceedToPolicyGate);
        CollectionAssert.Contains(result.Evidence.IssueCodes.ToArray(), MemoryGovernanceIssueCode.GovernanceResultMismatch);
        Assert.AreEqual(0, conscience.CallCount);
    }

    [TestMethod]
    public async Task MemoryExecutionGate_SourceMutationSuppliedAllow_DowngradesToWarning()
    {
        var gate = new MemoryExecutionGate(new FakeConscienceMemoryGovernanceService());
        var context = BuildContext(MemoryGovernanceActionType.SourceMutation) with
        {
            SuppliedGovernanceResult = BuildGovernanceResult(MemoryGovernanceDecision.Allow, actionType: MemoryGovernanceActionType.SourceMutation)
        };

        var result = await gate.EvaluateAsync(context);

        Assert.AreEqual(MemoryExecutionGateDecision.WarningRequiresOuterApproval, result.Decision);
        Assert.IsTrue(result.MayProceedToPolicyGate);
        CollectionAssert.Contains(result.Evidence.IssueCodes.ToArray(), MemoryGovernanceIssueCode.SourceMutationRequiresApprovalBeyondMemory);
    }

    [TestMethod]
    public async Task MemoryExecutionGate_ExternalEffectSuppliedAllow_DowngradesToWarning()
    {
        var gate = new MemoryExecutionGate(new FakeConscienceMemoryGovernanceService());
        var context = BuildContext(MemoryGovernanceActionType.ExternalEffect) with
        {
            SuppliedGovernanceResult = BuildGovernanceResult(MemoryGovernanceDecision.Allow, actionType: MemoryGovernanceActionType.ExternalEffect)
        };

        var result = await gate.EvaluateAsync(context);

        Assert.AreEqual(MemoryExecutionGateDecision.WarningRequiresOuterApproval, result.Decision);
        Assert.IsTrue(result.MayProceedToPolicyGate);
        CollectionAssert.Contains(result.Evidence.IssueCodes.ToArray(), MemoryGovernanceIssueCode.ExternalEffectRequiresApprovalBeyondMemory);
    }

    private static MemoryBackedExecutionContext BuildContext(
        MemoryGovernanceActionType actionType = MemoryGovernanceActionType.ContextUse) =>
        new()
        {
            Scope = BuildScope(),
            ActionType = actionType,
            DecisionId = "decision-1",
            ReferencedArtifacts =
            [
                new MemoryBackedExecutionReference
                {
                    MemoryItemId = "memory-1",
                    InfluenceId = "influence-1",
                    DecisionId = "decision-1",
                    ThoughtLedgerEntryId = "thought-1"
                }
            ],
            RequestedAt = Now,
            ToolName = actionType == MemoryGovernanceActionType.ToolCallJustification ? "workspace.validate" : null,
            AffectedArtifactType = actionType == MemoryGovernanceActionType.SourceMutation ? "source" : actionType == MemoryGovernanceActionType.ExternalEffect ? "external" : null,
            AffectedArtifactId = actionType == MemoryGovernanceActionType.SourceMutation ? "file.cs" : actionType == MemoryGovernanceActionType.ExternalEffect ? "github" : null,
            CorrelationId = "correlation-1",
            InfluenceRecordRequired = false
        };

    private static AgentMemoryScope BuildScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "critic-agent"
        };

    private static MemoryGovernanceCheckResult BuildGovernanceResult(
        MemoryGovernanceDecision decision,
        IReadOnlyList<MemoryGovernanceIssue>? issues = null,
        MemoryGovernanceActionType actionType = MemoryGovernanceActionType.ContextUse) =>
        new()
        {
            GovernanceCheckId = $"memory-governance-{decision.ToString().ToLowerInvariant()}",
            Scope = BuildScope(),
            DecisionId = "decision-1",
            ActionType = actionType,
            Decision = decision,
            Issues = issues ?? [],
            CheckedAt = Now,
            CorrelationId = "correlation-1",
            ThoughtLedgerEntryId = "thought-1"
        };

    private static MemoryGovernanceIssue BuildIssue(MemoryGovernanceIssueCode code) =>
        new()
        {
            Code = code,
            Severity = MemoryGovernanceIssueSeverity.Critical,
            Summary = $"Synthetic issue {code}.",
            MemoryItemId = "memory-1",
            InfluenceId = "influence-1"
        };

    private sealed class FakeConscienceMemoryGovernanceService : IConscienceMemoryGovernanceService
    {
        public int CallCount { get; private set; }

        public MemoryGovernanceCheckRequest? LastRequest { get; private set; }

        public MemoryGovernanceCheckResult? Result { get; init; }

        public Task<MemoryGovernanceCheckResult> CheckAsync(
            MemoryGovernanceCheckRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(Result ?? BuildGovernanceResult(MemoryGovernanceDecision.Allow));
        }
    }
}
