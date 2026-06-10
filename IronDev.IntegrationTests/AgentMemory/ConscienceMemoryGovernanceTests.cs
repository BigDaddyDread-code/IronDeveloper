using System.Reflection;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ConscienceMemoryGovernanceTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    private AgentMemorySiloService _siloService = null!;
    private SqlConscienceMemoryGovernanceService _governance = null!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentMemorySchemaAsync();
        await ApplyAgentMemoryMigrationsAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var store = new SqlAgentLocalMemoryStore(connectionFactory, new AgentMemoryContractValidator());
        var influenceStore = new SqlAgentMemoryInfluenceStore(connectionFactory);
        var handoffStore = new SqlAgentMemoryHandoffStore(connectionFactory);

        _siloService = new AgentMemorySiloService(store, influenceStore, handoffStore);
        _governance = new SqlConscienceMemoryGovernanceService(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        try
        {
            await DropAgentMemorySchemaAsync();
        }
        catch
        {
            // Test cleanup should not hide the original assertion failure.
        }

        await base.TestCleanup();
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_MissingRequiredRequestFields_Block()
    {
        var missingTenant = await _governance.CheckAsync(BuildRequest("memory-1") with
        {
            Scope = BuildScope(tenantId: "")
        });
        AssertBlockedWith(missingTenant, MemoryGovernanceIssueCode.MissingScope);

        var missingDecision = await _governance.CheckAsync(BuildRequest("memory-1") with
        {
            DecisionId = ""
        });
        AssertBlockedWith(missingDecision, MemoryGovernanceIssueCode.MissingDecisionId);

        var missingArtifacts = await _governance.CheckAsync(BuildRequest("memory-1") with
        {
            ReferencedArtifacts = []
        });
        AssertBlockedWith(missingArtifacts, MemoryGovernanceIssueCode.MissingReferencedArtifacts);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_ActiveOwnedMemory_AllowsContextUse()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-active"));

        var result = await _governance.CheckAsync(BuildRequest(
            "memory-active",
            MemoryGovernanceActionType.ContextUse,
            influenceRequired: false));

        Assert.AreEqual(MemoryGovernanceDecision.Allow, result.Decision);
        Assert.AreEqual(0, result.Issues.Count);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_AgentCannotUseAnotherAgentsLocalMemoryById()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-owned-by-builder"));

        var result = await _governance.CheckAsync(BuildRequest(
            "memory-owned-by-builder",
            MemoryGovernanceActionType.ContextUse,
            scope: BuildScope(agentId: "tester-agent"),
            influenceRequired: false));

        AssertBlockedWith(result, MemoryGovernanceIssueCode.MemoryNotFoundInScope);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_WrongRunMemory_IsNotFoundAndBlocks()
    {
        var builder = OpenSilo("builder-agent", runId: "run-other");
        await builder.CreateAsync(BuildMemoryDraft("memory-other-run"));

        var result = await _governance.CheckAsync(BuildRequest(
            "memory-other-run",
            MemoryGovernanceActionType.ContextUse,
            influenceRequired: false));

        AssertBlockedWith(result, MemoryGovernanceIssueCode.MemoryNotFoundInScope);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_TerminalAndTimeExpiredMemory_Block()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-expired"));
        await builder.AddEventAsync(BuildEventDraft("memory-expired", AgentLocalMemoryEventType.Expired, 1));

        await builder.CreateAsync(BuildMemoryDraft("memory-invalidated"));
        await builder.AddEventAsync(BuildEventDraft("memory-invalidated", AgentLocalMemoryEventType.Invalidated, 1));

        await builder.CreateAsync(BuildMemoryDraft("memory-superseded"));
        await builder.AddEventAsync(BuildEventDraft("memory-superseded", AgentLocalMemoryEventType.Superseded, 1));

        await builder.CreateAsync(BuildMemoryDraft("memory-time-expired") with
        {
            ExpiresAt = Now.AddMinutes(1)
        });

        AssertBlockedWith(
            await _governance.CheckAsync(BuildRequest("memory-expired", influenceRequired: false)),
            MemoryGovernanceIssueCode.MemoryExpired);
        AssertBlockedWith(
            await _governance.CheckAsync(BuildRequest("memory-invalidated", influenceRequired: false)),
            MemoryGovernanceIssueCode.MemoryInvalidated);
        AssertBlockedWith(
            await _governance.CheckAsync(BuildRequest("memory-superseded", influenceRequired: false)),
            MemoryGovernanceIssueCode.MemorySuperseded);
        AssertBlockedWith(
            await _governance.CheckAsync(BuildRequest("memory-time-expired", influenceRequired: false) with
            {
                RequestedAt = Now.AddMinutes(2)
            }),
            MemoryGovernanceIssueCode.MemoryExpired);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_CandidatePatternActionRules_AreDeterministic()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildCandidatePatternDraft("memory-candidate"));

        var sourceMutation = await _governance.CheckAsync(BuildRequest(
            "memory-candidate",
            MemoryGovernanceActionType.SourceMutation,
            influenceRequired: false));
        AssertBlockedWith(sourceMutation, MemoryGovernanceIssueCode.CandidatePatternCannotJustifyExternalEffect);

        var toolCall = await _governance.CheckAsync(BuildRequest(
            "memory-candidate",
            MemoryGovernanceActionType.ToolCallJustification,
            influenceRequired: false));
        AssertWarnedWith(toolCall, MemoryGovernanceIssueCode.CandidatePatternCannotJustifyExternalEffect);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_ProposedForReviewRules_AreDeterministic()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-proposed"));
        await builder.AddEventAsync(BuildEventDraft("memory-proposed", AgentLocalMemoryEventType.ProposedForReview, 1));

        var sourceMutation = await _governance.CheckAsync(BuildRequest(
            "memory-proposed",
            MemoryGovernanceActionType.SourceMutation,
            influenceRequired: false));
        AssertBlockedWith(sourceMutation, MemoryGovernanceIssueCode.ProposedMemoryRequiresVerification);

        var proposal = await _governance.CheckAsync(BuildRequest(
            "memory-proposed",
            MemoryGovernanceActionType.ProposalCreation,
            influenceRequired: false));
        Assert.AreEqual(MemoryGovernanceDecision.Allow, proposal.Decision);

        var toolCall = await _governance.CheckAsync(BuildRequest(
            "memory-proposed",
            MemoryGovernanceActionType.ToolCallJustification,
            influenceRequired: false));
        AssertWarnedWith(toolCall, MemoryGovernanceIssueCode.ProposedMemoryRequiresVerification);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_ToolCallWithoutInfluence_Blocks()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-tool"));

        var result = await _governance.CheckAsync(BuildRequest(
            "memory-tool",
            MemoryGovernanceActionType.ToolCallJustification));

        AssertBlockedWith(result, MemoryGovernanceIssueCode.MissingInfluenceRecord);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_ToolCallWithMatchingInfluence_Allows()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-tool"));
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-tool", "memory-tool", "decision-1"));

        var result = await _governance.CheckAsync(BuildRequest(
            "memory-tool",
            MemoryGovernanceActionType.ToolCallJustification));

        Assert.AreEqual(MemoryGovernanceDecision.Allow, result.Decision);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_InfluenceOnlyForExpiredMemory_Blocks()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-influence-expired"));
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-expired", "memory-influence-expired", "decision-1"));
        await builder.AddEventAsync(BuildEventDraft("memory-influence-expired", AgentLocalMemoryEventType.Expired, 1));

        var result = await _governance.CheckAsync(BuildInfluenceOnlyRequest(
            "influence-expired",
            MemoryGovernanceActionType.ToolCallJustification));

        AssertBlockedWith(result, MemoryGovernanceIssueCode.MemoryExpired);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_InfluenceOnlyForInvalidatedMemory_Blocks()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-influence-invalidated"));
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-invalidated", "memory-influence-invalidated", "decision-1"));
        await builder.AddEventAsync(BuildEventDraft("memory-influence-invalidated", AgentLocalMemoryEventType.Invalidated, 1));

        var result = await _governance.CheckAsync(BuildInfluenceOnlyRequest(
            "influence-invalidated",
            MemoryGovernanceActionType.ToolCallJustification));

        AssertBlockedWith(result, MemoryGovernanceIssueCode.MemoryInvalidated);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_InfluenceOnlyForTimeExpiredMemory_Blocks()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-influence-time-expired") with
        {
            ExpiresAt = Now.AddYears(1)
        });
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-time-expired", "memory-influence-time-expired", "decision-1"));

        var result = await _governance.CheckAsync(BuildInfluenceOnlyRequest(
            "influence-time-expired",
            MemoryGovernanceActionType.ToolCallJustification) with
        {
            RequestedAt = Now.AddYears(2)
        });

        AssertBlockedWith(result, MemoryGovernanceIssueCode.MemoryExpired);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_InfluenceOnlyForCandidatePatternSourceMutation_Blocks()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildCandidatePatternDraft("memory-influence-candidate"));
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-candidate", "memory-influence-candidate", "decision-1"));

        var result = await _governance.CheckAsync(BuildInfluenceOnlyRequest(
            "influence-candidate",
            MemoryGovernanceActionType.SourceMutation));

        AssertBlockedWith(result, MemoryGovernanceIssueCode.CandidatePatternCannotJustifyExternalEffect);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_InfluenceMismatches_DoNotSatisfyRequirement()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-one"));
        await builder.CreateAsync(BuildMemoryDraft("memory-two"));
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-wrong-decision", "memory-one", "decision-other"));
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-wrong-memory", "memory-two", "decision-1"));

        var wrongDecision = await _governance.CheckAsync(BuildRequest(
            "memory-one",
            MemoryGovernanceActionType.ToolCallJustification,
            influenceId: "influence-wrong-decision"));
        AssertBlockedWith(wrongDecision, MemoryGovernanceIssueCode.InfluenceDecisionMismatch);

        var wrongMemory = await _governance.CheckAsync(BuildRequest(
            "memory-one",
            MemoryGovernanceActionType.ToolCallJustification,
            influenceId: "influence-wrong-memory"));
        AssertBlockedWith(wrongMemory, MemoryGovernanceIssueCode.InfluenceMemoryMismatch);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_ProposalCreationWithoutInfluence_Warns()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-proposal"));

        var result = await _governance.CheckAsync(BuildRequest(
            "memory-proposal",
            MemoryGovernanceActionType.ProposalCreation));

        AssertWarnedWith(result, MemoryGovernanceIssueCode.MissingInfluenceRecord);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_SourceAndExternalEffects_NeverReturnAllowFromMemory()
    {
        var builder = OpenSilo("builder-agent");
        await builder.CreateAsync(BuildMemoryDraft("memory-high-impact"));
        await builder.RecordInfluenceAsync(BuildInfluenceDraft("influence-high-impact", "memory-high-impact", "decision-1"));

        var sourceMutation = await _governance.CheckAsync(BuildRequest(
            "memory-high-impact",
            MemoryGovernanceActionType.SourceMutation));
        AssertWarnedWith(sourceMutation, MemoryGovernanceIssueCode.SourceMutationRequiresApprovalBeyondMemory);

        var externalEffect = await _governance.CheckAsync(BuildRequest(
            "memory-high-impact",
            MemoryGovernanceActionType.ExternalEffect));
        AssertWarnedWith(externalEffect, MemoryGovernanceIssueCode.ExternalEffectRequiresApprovalBeyondMemory);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_TargetAgentCanUseAddressedHandoffForContext()
    {
        await CreateHandoffAsync("handoff-context", HandoffMemoryAllowedUse.ContextOnly, "tester-agent");

        var result = await _governance.CheckAsync(BuildHandoffRequest(
            "handoff-context",
            MemoryGovernanceActionType.ContextUse,
            BuildScope(agentId: "tester-agent")));

        Assert.AreEqual(MemoryGovernanceDecision.Allow, result.Decision);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_NonTargetAgentCannotUseHandoff()
    {
        await CreateHandoffAsync("handoff-target", HandoffMemoryAllowedUse.ContextOnly, "tester-agent");

        var result = await _governance.CheckAsync(BuildHandoffRequest(
            "handoff-target",
            MemoryGovernanceActionType.ContextUse,
            BuildScope(agentId: "critic-agent")));

        AssertBlockedWith(result, MemoryGovernanceIssueCode.HandoffNotAddressedToAgent);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_ExpiredHandoff_Blocks()
    {
        await CreateHandoffAsync(
            "handoff-expired",
            HandoffMemoryAllowedUse.ContextOnly,
            "tester-agent",
            expiresAt: Now.AddMinutes(11));

        var result = await _governance.CheckAsync(BuildHandoffRequest(
            "handoff-expired",
            MemoryGovernanceActionType.ContextUse,
            BuildScope(agentId: "tester-agent")) with
        {
            RequestedAt = Now.AddMinutes(12)
        });

        AssertBlockedWith(result, MemoryGovernanceIssueCode.HandoffExpired);
    }

    [TestMethod]
    public async Task ConscienceMemoryGovernance_HandoffAllowedUseRules_AreEnforced()
    {
        await CreateHandoffAsync("handoff-context-only", HandoffMemoryAllowedUse.ContextOnly, "tester-agent");
        await CreateHandoffAsync("handoff-avoid", HandoffMemoryAllowedUse.AvoidRepeat, "tester-agent");
        await CreateHandoffAsync("handoff-needs", HandoffMemoryAllowedUse.NeedsVerification, "tester-agent");
        await CreateHandoffAsync("handoff-proposal", HandoffMemoryAllowedUse.ProposalSupport, "tester-agent");

        AssertBlockedWith(
            await _governance.CheckAsync(BuildHandoffRequest("handoff-context-only", MemoryGovernanceActionType.ToolCallJustification, BuildScope(agentId: "tester-agent"))),
            MemoryGovernanceIssueCode.HandoffAllowedUseViolation);

        var avoidRepeat = await _governance.CheckAsync(BuildHandoffRequest(
            "handoff-avoid",
            MemoryGovernanceActionType.AvoidRepeat,
            BuildScope(agentId: "tester-agent")));
        Assert.AreEqual(MemoryGovernanceDecision.Allow, avoidRepeat.Decision);

        AssertWarnedWith(
            await _governance.CheckAsync(BuildHandoffRequest("handoff-needs", MemoryGovernanceActionType.ContextUse, BuildScope(agentId: "tester-agent"))),
            MemoryGovernanceIssueCode.HandoffAllowedUseViolation);

        var proposal = await _governance.CheckAsync(BuildHandoffRequest(
            "handoff-proposal",
            MemoryGovernanceActionType.ProposalCreation,
            BuildScope(agentId: "tester-agent")));
        Assert.AreEqual(MemoryGovernanceDecision.Allow, proposal.Decision);
    }

    [TestMethod]
    public void ConscienceMemoryGovernance_ApiShapeDoesNotExposeRawReasoningOrSiloSearch()
    {
        Assert.IsNotNull(typeof(IConscienceMemoryGovernanceService).GetMethod(nameof(IConscienceMemoryGovernanceService.CheckAsync)));

        var siloMethods = typeof(IAgentMemorySilo).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        Assert.IsFalse(siloMethods.Any(method =>
            method.Name.Contains("Conscience", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Governance", StringComparison.OrdinalIgnoreCase) ||
            method.ReturnType == typeof(MemoryGovernanceCheckResult)));

        var modelTypes = new[]
        {
            typeof(MemoryGovernanceReferencedArtifact),
            typeof(MemoryGovernanceCheckRequest),
            typeof(MemoryGovernanceIssue),
            typeof(MemoryGovernanceCheckResult)
        };

        string[] banned =
        [
            "RawPrompt",
            "RawCompletion",
            "ChainOfThought",
            "Scratchpad",
            "PrivateReasoning",
            "Prompt",
            "Completion"
        ];

        var names = modelTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        foreach (var bannedName in banned)
        {
            Assert.IsFalse(
                names.Any(name => name.Contains(bannedName, StringComparison.OrdinalIgnoreCase)),
                $"Memory governance model must not expose raw reasoning field '{bannedName}'.");
        }
    }

    private async Task CreateHandoffAsync(
        string handoffId,
        HandoffMemoryAllowedUse allowedUse,
        string targetAgentId,
        DateTimeOffset? expiresAt = null)
    {
        var builder = OpenSilo("builder-agent");
        var memoryId = $"memory-{handoffId}";

        await builder.CreateAsync(BuildMemoryDraft(memoryId));
        await builder.CreateHandoffAsync(BuildHandoffDraft(handoffId, targetAgentId, [memoryId]) with
        {
            AllowedUse = allowedUse,
            ExpiresAt = expiresAt
        });
    }

    private IAgentMemorySilo OpenSilo(
        string agentId,
        string tenantId = "tenant-1",
        string projectId = "project-1",
        string campaignId = "campaign-1",
        string runId = "run-1") =>
        _siloService.Open(BuildContext(tenantId, projectId, campaignId, runId, agentId));

    private static AgentMemoryScope BuildScope(
        string tenantId = "tenant-1",
        string projectId = "project-1",
        string campaignId = "campaign-1",
        string runId = "run-1",
        string agentId = "builder-agent") =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            CampaignId = campaignId,
            RunId = runId,
            AgentId = agentId
        };

    private static AgentMemorySiloContext BuildContext(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        string agentId) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            CampaignId = campaignId,
            RunId = runId,
            AgentId = agentId,
            WorkflowId = "workflow-1",
            TicketId = "ticket-1",
            CorrelationId = "correlation-1"
        };

    private static MemoryGovernanceCheckRequest BuildRequest(
        string memoryItemId,
        MemoryGovernanceActionType actionType = MemoryGovernanceActionType.ContextUse,
        AgentMemoryScope? scope = null,
        string? influenceId = null,
        bool influenceRequired = true) =>
        new()
        {
            Scope = scope ?? BuildScope(),
            ActionType = actionType,
            DecisionId = "decision-1",
            ReferencedArtifacts =
            [
                new MemoryGovernanceReferencedArtifact
                {
                    MemoryItemId = memoryItemId,
                    InfluenceId = influenceId,
                    DecisionId = "decision-1",
                    ThoughtLedgerEntryId = "thought-request-1"
                }
            ],
            RequestedAt = Now,
            ToolName = actionType == MemoryGovernanceActionType.ToolCallJustification ? "test.run" : null,
            AffectedArtifactType = actionType == MemoryGovernanceActionType.SourceMutation ? "source" : null,
            AffectedArtifactId = actionType == MemoryGovernanceActionType.SourceMutation ? "file.cs" : null,
            CorrelationId = "correlation-1",
            InfluenceRecordRequired = influenceRequired
        };

    private static MemoryGovernanceCheckRequest BuildInfluenceOnlyRequest(
        string influenceId,
        MemoryGovernanceActionType actionType,
        AgentMemoryScope? scope = null) =>
        new()
        {
            Scope = scope ?? BuildScope(),
            ActionType = actionType,
            DecisionId = "decision-1",
            ReferencedArtifacts =
            [
                new MemoryGovernanceReferencedArtifact
                {
                    InfluenceId = influenceId,
                    DecisionId = "decision-1",
                    ThoughtLedgerEntryId = "thought-request-1"
                }
            ],
            RequestedAt = Now,
            ToolName = actionType == MemoryGovernanceActionType.ToolCallJustification ? "test.run" : null,
            AffectedArtifactType = actionType == MemoryGovernanceActionType.SourceMutation ? "source" : null,
            AffectedArtifactId = actionType == MemoryGovernanceActionType.SourceMutation ? "file.cs" : null,
            CorrelationId = "correlation-1"
        };

    private static MemoryGovernanceCheckRequest BuildHandoffRequest(
        string handoffId,
        MemoryGovernanceActionType actionType,
        AgentMemoryScope scope) =>
        new()
        {
            Scope = scope,
            ActionType = actionType,
            DecisionId = "decision-1",
            ReferencedArtifacts =
            [
                new MemoryGovernanceReferencedArtifact
                {
                    HandoffMemorySliceId = handoffId,
                    DecisionId = "decision-1",
                    ThoughtLedgerEntryId = "thought-handoff-use"
                }
            ],
            RequestedAt = Now,
            TargetAgentId = scope.AgentId,
            CorrelationId = "correlation-1",
            InfluenceRecordRequired = false
        };

    private static AgentLocalMemoryDraft BuildMemoryDraft(string memoryItemId) =>
        new()
        {
            MemoryItemId = memoryItemId,
            MemoryType = AgentMemoryType.Episodic,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Title = "Observed build failure",
            Summary = "TesterAgent observed a reproducible build failure.",
            EvidenceRefs = [BuildEvidence()],
            Confidence = 0.8m,
            CreatedAt = Now
        };

    private static AgentLocalMemoryDraft BuildCandidatePatternDraft(string memoryItemId) =>
        BuildMemoryDraft(memoryItemId) with
        {
            MemoryType = AgentMemoryType.CandidatePattern,
            AuthorityLevel = MemoryAuthorityLevel.CandidatePattern,
            Title = "Candidate retry pattern",
            Summary = "Repeated validation failures may share a cause.",
            KnownLimitations = "Observed during one run only and must not be promoted without review."
        };

    private static AgentLocalMemoryEventDraft BuildEventDraft(
        string memoryItemId,
        AgentLocalMemoryEventType eventType,
        int minutesAfterCreated) =>
        new()
        {
            MemoryEventId = $"event-{memoryItemId}-{eventType}-{minutesAfterCreated}",
            MemoryItemId = memoryItemId,
            EventType = eventType,
            EventReason = $"Lifecycle event {eventType}.",
            CreatedAt = Now.AddMinutes(minutesAfterCreated),
            CreatedByUserId = "human-reviewer",
            DecisionId = "decision-event",
            ThoughtLedgerEntryId = "thought-1",
            EventJson = "{}"
        };

    private static MemoryInfluenceDraft BuildInfluenceDraft(
        string influenceId,
        string memoryItemId,
        string decisionId) =>
        new()
        {
            InfluenceId = influenceId,
            MemoryItemId = memoryItemId,
            DecisionId = decisionId,
            InfluenceType = MemoryInfluenceType.ToolCallJustified,
            InfluenceSummary = "Memory was used to justify a governed tool call.",
            EvidenceRefs = [BuildEvidence($"evidence-{influenceId}")],
            Confidence = 0.8m,
            CreatedAt = Now.AddMinutes(5),
            ThoughtLedgerEntryId = $"thought-{influenceId}"
        };

    private static HandoffMemorySliceDraft BuildHandoffDraft(
        string handoffId,
        string targetAgentId,
        IReadOnlyList<string> memoryItemIds) =>
        new()
        {
            HandoffMemorySliceId = handoffId,
            TargetAgentId = targetAgentId,
            MemoryItemIds = memoryItemIds,
            Summary = "Builder hands bounded memory context to Tester.",
            AllowedUse = HandoffMemoryAllowedUse.ContextOnly,
            EvidenceRefs = [BuildEvidence($"evidence-{handoffId}")],
            Confidence = 0.8m,
            CreatedAt = Now.AddMinutes(10),
            ThoughtLedgerEntryId = $"thought-{handoffId}",
            CorrelationId = "correlation-1",
            HandoffJson = "{}"
        };

    private static EvidenceRef BuildEvidence(string evidenceId = "evidence-1") =>
        new()
        {
            EvidenceId = evidenceId,
            EvidenceType = EvidenceType.TestResult,
            SourceId = $"source-{evidenceId}",
            SourceUri = $"workspace://run-1/{evidenceId}.json",
            Summary = "Focused test result captured during the run.",
            CapturedAt = Now
        };

    private static void AssertBlockedWith(MemoryGovernanceCheckResult result, MemoryGovernanceIssueCode code)
    {
        Assert.AreEqual(MemoryGovernanceDecision.Block, result.Decision);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected issue {code}.");
    }

    private static void AssertWarnedWith(MemoryGovernanceCheckResult result, MemoryGovernanceIssueCode code)
    {
        Assert.AreEqual(MemoryGovernanceDecision.Warn, result.Decision);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected issue {code}.");
    }

    private async Task ApplyAgentMemoryMigrationsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_local_memory.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_influence.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_handoff.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_stored_procedures.sql")));
    }

    private async Task DropAgentMemorySchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID('agent.usp_MemoryExecutionAudit_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryExecutionAudit_Create;
            IF OBJECT_ID('agent.usp_MemoryIndexEvent_Add', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryIndexEvent_Add;
            IF OBJECT_ID('agent.usp_MemoryIndexQueue_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryIndexQueue_Create;
            IF OBJECT_ID('agent.usp_MemoryImprovementProposal_AddEvent', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryImprovementProposal_AddEvent;
            IF OBJECT_ID('agent.usp_MemoryImprovementProposal_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryImprovementProposal_Create;
            IF OBJECT_ID('agent.usp_AgentMemoryHandoff_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentMemoryHandoff_Create;
            IF OBJECT_ID('agent.usp_AgentMemoryInfluence_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentMemoryInfluence_Create;
            IF OBJECT_ID('agent.usp_AgentLocalMemory_AddEvent', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentLocalMemory_AddEvent;
            IF OBJECT_ID('agent.usp_AgentLocalMemory_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentLocalMemory_Create;
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryExecutionAudit', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryExecutionAudit;            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryHandoffSlice', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryHandoffSlice;
            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_ValidateScope', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_ValidateScope;
            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryInfluenceRecord', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryInfluenceRecord;
            IF OBJECT_ID('agent.vwAgentLocalMemoryCurrentState', 'V') IS NOT NULL
                DROP VIEW agent.vwAgentLocalMemoryCurrentState;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryItem_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryItem_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentLocalMemoryEvidenceRef', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryEvidenceRef;
            IF OBJECT_ID('agent.AgentLocalMemoryEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryEvent;
            IF OBJECT_ID('agent.AgentLocalMemoryItem', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryItem;
            IF SCHEMA_ID('agent') IS NOT NULL
                DROP SCHEMA agent;
            """);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for conscience memory governance tests.");
    }
}
