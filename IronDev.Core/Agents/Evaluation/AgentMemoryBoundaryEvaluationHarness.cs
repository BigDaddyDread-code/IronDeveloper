using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Execution;
using IronDev.Core.Agents.Concrete;
using BoxedMemoryDetectionResult = IronDev.Core.Agents.Concrete.MemoryImprovementDetectionResult;
using BoxedMemoryPatternFinding = IronDev.Core.Agents.Concrete.MemoryImprovementPatternFinding;
using BoxedMemoryPatternType = IronDev.Core.Agents.Concrete.MemoryImprovementPatternType;
using BoxedMemoryProposalDraft = IronDev.Core.Agents.Concrete.MemoryImprovementProposalDraft;

namespace IronDev.Core.Agents.Evaluation;

public sealed class AgentMemoryBoundaryEvaluationHarness : IAgentMemoryBoundaryEvaluationHarness
{
    private static readonly IReadOnlyList<AgentMemoryBoundaryScenario> DefinedScenarios =
    [
        Scenario(
            "agent-memory-boundary-001",
            AgentMemoryBoundaryScenarioType.ProposalAgentCannotPromoteMemory,
            "Proposal agent shape cannot promote collective memory.",
            "MemoryImprovementAgent remains ProposalAgent + ProposalOnly and PromoteCollectiveMemory stays forbidden.",
            "AgentDefinitionCatalog.MemoryImprovementAgent"),
        Scenario(
            "agent-memory-boundary-002",
            AgentMemoryBoundaryScenarioType.ReviewAgentCannotBlockExecutionDirectly,
            "Review agent shape can recommend but cannot enforce blocks.",
            "IndependentCriticAgent remains ReviewAgent + OutOfBandReviewOnly and cannot gain BlockExecution."),
        Scenario(
            "agent-memory-boundary-003",
            AgentMemoryBoundaryScenarioType.RetrievalAgentCannotApproveAction,
            "Retrieval agent and retrieval result cannot approve action.",
            "Retrieval remains read-only and retrieval candidates are not authoritative for action."),
        Scenario(
            "agent-memory-boundary-004",
            AgentMemoryBoundaryScenarioType.StabilityScoreCannotAuthorizeAction,
            "Stability scoring cannot become approval or promotion authority.",
            "StronglyStable remains a score band only and exposes no approval, promotion, or retrieval authority fields."),
        Scenario(
            "agent-memory-boundary-005",
            AgentMemoryBoundaryScenarioType.RetrievedAcceptedMemoryCannotApproveToolExecution,
            "Accepted retrieved memory still cannot authorize tool execution.",
            "Accepted retrieval candidates require conscience and policy approval before action."),
        Scenario(
            "agent-memory-boundary-006",
            AgentMemoryBoundaryScenarioType.MemoryImprovementDraftCannotCreateCollectiveMemory,
            "Memory-improvement draft remains proposal-only.",
            "Drafts cannot create CollectiveMemory, cannot promote memory, and require human review."),
        Scenario(
            "agent-memory-boundary-007",
            AgentMemoryBoundaryScenarioType.HumanProxyRequiresExplicitHumanEvent,
            "Human authority requires explicit HumanProxyAgent shape.",
            "HumanAuthorityProxy mode requires HumanProxyAgent kind and explicit human representation capabilities."),
        Scenario(
            "agent-memory-boundary-008",
            AgentMemoryBoundaryScenarioType.PersonaCannotImplyApproval,
            "Persona text cannot imply approval or authority.",
            "AgentDefinitionValidator rejects persona authority claims."),
        Scenario(
            "agent-memory-boundary-009",
            AgentMemoryBoundaryScenarioType.GovernanceAgentCannotExecuteGovernedAction,
            "Governance agent can warn/block but cannot perform governed actions.",
            "GovernanceAgent cannot run tools, mutate source, call external systems, or promote memory."),
        Scenario(
            "agent-memory-boundary-010",
            AgentMemoryBoundaryScenarioType.ImplementationAgentCannotPromoteMemory,
            "Implementation agent cannot promote memory.",
            "Source mutation capability does not imply collective-memory promotion authority."),
        Scenario(
            "agent-memory-boundary-011",
            AgentMemoryBoundaryScenarioType.CriticFindingCannotEnforceBlock,
            "Critic finding cannot enforce a governance block.",
            "RecommendBlock is advisory output and authority claims are rejected."),
        Scenario(
            "agent-memory-boundary-012",
            AgentMemoryBoundaryScenarioType.CollectiveRetrievalCannotSatisfyPolicyApproval,
            "Collective retrieval result cannot satisfy approval evidence.",
            "Retrieval output contains no approval evidence and warns that policy approval remains separate."),
        Scenario(
            "agent-memory-boundary-013",
            AgentMemoryBoundaryScenarioType.LocalMemoryInfluenceCannotReplaceApproval,
            "Local memory influence cannot replace policy approval.",
            "Memory influence can reach warning/outer-approval state but does not carry approval evidence."),
        Scenario(
            "agent-memory-boundary-014",
            AgentMemoryBoundaryScenarioType.HandoffCannotGrantMemoryOwnership,
            "Handoff does not grant source local-memory ownership.",
            "Agent memory silo exposes addressed incoming/outgoing handoff queries, not cross-agent ownership transfer."),
        Scenario(
            "agent-memory-boundary-015",
            AgentMemoryBoundaryScenarioType.ProposalAcceptedStatusCannotPromoteCollectiveMemory,
            "Proposal accepted-for-future status cannot promote collective memory.",
            "Memory-improvement proposal lifecycle records review intent without creating CollectiveMemory."),
        Scenario(
            "agent-memory-boundary-016",
            AgentMemoryBoundaryScenarioType.WeaviateIndexingCannotCreateAuthority,
            "Indexing cannot create authority.",
            "Indexing projection/result models do not create accepted memory, promotion, or action authority.")
    ];

    private static readonly AgentDefinitionValidator AgentValidator = new();
    private static readonly CriticReviewResultValidator CriticValidator = new();
    private static readonly MemoryImprovementDetectionResultValidator MemoryImprovementValidator = new();

    public AgentMemoryBoundaryEvaluationResult Evaluate(DateTimeOffset evaluatedAt)
    {
        var violations = new List<AgentMemoryBoundaryViolation>();

        foreach (var scenario in DefinedScenarios)
            EvaluateScenario(scenario, violations);

        return new AgentMemoryBoundaryEvaluationResult
        {
            EvaluationRunId = $"agent-memory-boundary-{evaluatedAt.ToUnixTimeSeconds()}",
            Scenarios = DefinedScenarios,
            Violations = violations,
            EvaluatedAt = evaluatedAt
        };
    }

    private static AgentMemoryBoundaryScenario Scenario(
        string scenarioId,
        AgentMemoryBoundaryScenarioType scenarioType,
        string description,
        string expectedBoundary,
        params string[] evidenceRefs) =>
        new()
        {
            ScenarioId = scenarioId,
            ScenarioType = scenarioType,
            Description = description,
            ExpectedBoundary = expectedBoundary,
            EvidenceRefs = evidenceRefs
        };

    private static void EvaluateScenario(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        switch (scenario.ScenarioType)
        {
            case AgentMemoryBoundaryScenarioType.ProposalAgentCannotPromoteMemory:
                EvaluateProposalAgentCannotPromoteMemory(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.ReviewAgentCannotBlockExecutionDirectly:
                EvaluateReviewAgentCannotBlockExecutionDirectly(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.RetrievalAgentCannotApproveAction:
                EvaluateRetrievalAgentCannotApproveAction(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.StabilityScoreCannotAuthorizeAction:
                EvaluateStabilityScoreCannotAuthorizeAction(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.RetrievedAcceptedMemoryCannotApproveToolExecution:
                EvaluateRetrievedAcceptedMemoryCannotApproveToolExecution(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.MemoryImprovementDraftCannotCreateCollectiveMemory:
                EvaluateMemoryImprovementDraftCannotCreateCollectiveMemory(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.HumanProxyRequiresExplicitHumanEvent:
                EvaluateHumanProxyRequiresExplicitHumanEvent(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.PersonaCannotImplyApproval:
                EvaluatePersonaCannotImplyApproval(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.GovernanceAgentCannotExecuteGovernedAction:
                EvaluateGovernanceAgentCannotExecuteGovernedAction(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.ImplementationAgentCannotPromoteMemory:
                EvaluateImplementationAgentCannotPromoteMemory(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.CriticFindingCannotEnforceBlock:
                EvaluateCriticFindingCannotEnforceBlock(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.CollectiveRetrievalCannotSatisfyPolicyApproval:
                EvaluateCollectiveRetrievalCannotSatisfyPolicyApproval(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.LocalMemoryInfluenceCannotReplaceApproval:
                EvaluateLocalMemoryInfluenceCannotReplaceApproval(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.HandoffCannotGrantMemoryOwnership:
                EvaluateHandoffCannotGrantMemoryOwnership(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.ProposalAcceptedStatusCannotPromoteCollectiveMemory:
                EvaluateProposalAcceptedStatusCannotPromoteCollectiveMemory(scenario, violations);
                break;
            case AgentMemoryBoundaryScenarioType.WeaviateIndexingCannotCreateAuthority:
                EvaluateWeaviateIndexingCannotCreateAuthority(scenario, violations);
                break;
            default:
                AddViolation(violations, scenario, "AGENT_MEMORY_BOUNDARY_UNKNOWN_SCENARIO", "Scenario type is not recognized.");
                break;
        }
    }

    private static void EvaluateProposalAgentCannotPromoteMemory(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var definition = AgentDefinitionCatalog.MemoryImprovementAgent;
        Require(scenario, violations, definition.Kind == AgentKind.ProposalAgent, "MEMORY_IMPROVEMENT_KIND_UNSAFE", "MemoryImprovementAgent must remain ProposalAgent.");
        Require(scenario, violations, definition.ExecutionMode == AgentExecutionMode.ProposalOnly, "MEMORY_IMPROVEMENT_MODE_UNSAFE", "MemoryImprovementAgent must remain ProposalOnly.");
        RequireHasCapability(scenario, violations, definition, AgentCapability.CreateMemoryProposal);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.PromoteCollectiveMemory);
        RequireForbiddenCapability(scenario, violations, definition, AgentCapability.PromoteCollectiveMemory);

        var unsafeDefinition = CopyDefinition(
            definition,
            capabilities: definition.Capabilities!.Append(AgentCapability.PromoteCollectiveMemory).ToHashSet());
        RequireValidatorIssue(
            scenario,
            violations,
            unsafeDefinition,
            AgentDefinitionValidator.ExecutionModeCapabilityConflict,
            "MEMORY_IMPROVEMENT_PROMOTION_NOT_REJECTED",
            "AgentDefinitionValidator must reject PromoteCollectiveMemory on MemoryImprovementAgent.");
    }

    private static void EvaluateReviewAgentCannotBlockExecutionDirectly(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var definition = AgentDefinitionCatalog.IndependentCriticAgent;
        Require(scenario, violations, definition.Kind == AgentKind.ReviewAgent, "CRITIC_KIND_UNSAFE", "IndependentCriticAgent must remain ReviewAgent.");
        Require(scenario, violations, definition.ExecutionMode == AgentExecutionMode.OutOfBandReviewOnly, "CRITIC_MODE_UNSAFE", "IndependentCriticAgent must remain OutOfBandReviewOnly.");
        RequireHasCapability(scenario, violations, definition, AgentCapability.CreateCriticFinding);
        RequireHasCapability(scenario, violations, definition, AgentCapability.WarnExecution);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.BlockExecution);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.RunTool);
        Require(scenario, violations, definition.Kind != AgentKind.GovernanceAgent, "CRITIC_GOVERNANCE_KIND_UNSAFE", "IndependentCriticAgent must not be GovernanceAgent.");

        var unsafeDefinition = CopyDefinition(
            definition,
            capabilities: definition.Capabilities!.Append(AgentCapability.BlockExecution).ToHashSet());
        RequireValidatorIssue(
            scenario,
            violations,
            unsafeDefinition,
            AgentDefinitionValidator.KindCapabilityConflict,
            "CRITIC_BLOCK_EXECUTION_NOT_REJECTED",
            "AgentDefinitionValidator must reject BlockExecution on ReviewAgent.");
    }

    private static void EvaluateRetrievalAgentCannotApproveAction(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var definition = AgentDefinitionCatalog.RetrievalAgent;
        RequireHasCapability(scenario, violations, definition, AgentCapability.RetrieveCollectiveMemory);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.RepresentHumanApproval);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.BlockExecution);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.PromoteCollectiveMemory);

        var candidateType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory.Collective",
            "CollectiveMemory" + "Retrieval",
            "Candidate");
        RequireBooleanProperty(candidateType, scenario, violations, "IsAuthoritativeForAction");
        RequireBooleanProperty(candidateType, scenario, violations, "RequiresPolicyApprovalForAction");
    }

    private static void EvaluateStabilityScoreCannotAuthorizeAction(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var scoreType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory.Collective",
            "CollectiveMemory" + "Stability",
            "Score");
        var breakdownType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory.Collective",
            "CollectiveMemory" + "Stability",
            "Breakdown");
        var bandType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory.Collective",
            "CollectiveMemory" + "Stability",
            "Band");

        Require(
            scenario,
            violations,
            Enum.GetNames(bandType).Any(name => string.Equals(name, "StronglyStable", StringComparison.Ordinal)),
            "STABILITY_STRONGLY_STABLE_MISSING",
            "StronglyStable band must exist as a score band.");
        RequireNoProperties(
            scenario,
            violations,
            [scoreType, breakdownType],
            ["Accepted" + "Decision", "Promotion" + "Decision", "Retrieval" + "Boost", "IsAuthoritativeForAction", "Policy" + "Approved"]);
    }

    private static void EvaluateRetrievedAcceptedMemoryCannotApproveToolExecution(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var candidateType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory.Collective",
            "CollectiveMemory" + "Retrieval",
            "Candidate");
        RequireBooleanProperty(candidateType, scenario, violations, "IsAuthoritativeForAction");
        RequireBooleanProperty(candidateType, scenario, violations, "RequiresConscienceBeforeUse");
        RequireBooleanProperty(candidateType, scenario, violations, "RequiresPolicyApprovalForAction");
    }

    private static void EvaluateMemoryImprovementDraftCannotCreateCollectiveMemory(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var safeDraft = BuildBoxedMemoryProposalDraft();
        Require(scenario, violations, safeDraft.IsProposalOnly, "MEMORY_DRAFT_NOT_PROPOSAL_ONLY", "Memory improvement draft must be proposal-only.");
        Require(scenario, violations, !safeDraft.CreatesCollectiveMemory, "MEMORY_DRAFT_CREATES_COLLECTIVE_MEMORY", "Memory improvement draft must not create CollectiveMemory.");
        Require(scenario, violations, !safeDraft.PromotesMemory, "MEMORY_DRAFT_PROMOTES_MEMORY", "Memory improvement draft must not promote memory.");
        Require(scenario, violations, safeDraft.RequiresHumanReview, "MEMORY_DRAFT_HUMAN_REVIEW_MISSING", "Memory improvement draft must require human review.");

        var unsafeResult = BuildBoxedDetectionResult(
            BuildBoxedMemoryProposalDraft(
                isProposalOnly: false,
                createsCollectiveMemory: true,
                promotesMemory: true,
                requiresHumanReview: false));
        RequireAnyValidatorIssue(
            scenario,
            violations,
            MemoryImprovementValidator.Validate(unsafeResult),
            [
                MemoryImprovementDetectionResultValidator.ProposalOnlyRequired,
                MemoryImprovementDetectionResultValidator.CreatesCollectiveMemoryBlocked,
                MemoryImprovementDetectionResultValidator.PromotesMemoryBlocked,
                MemoryImprovementDetectionResultValidator.HumanReviewRequired
            ],
            "MEMORY_DRAFT_UNSAFE_FLAGS_NOT_REJECTED",
            "Memory improvement validator must reject unsafe draft authority flags.");
    }

    private static void EvaluateHumanProxyRequiresExplicitHumanEvent(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var definition = AgentDefinitionCatalog.HumanProxyAgent;
        Require(scenario, violations, definition.Kind == AgentKind.HumanProxyAgent, "HUMAN_PROXY_KIND_UNSAFE", "HumanProxyAgent must remain HumanProxyAgent.");
        Require(scenario, violations, definition.ExecutionMode == AgentExecutionMode.HumanAuthorityProxy, "HUMAN_PROXY_MODE_UNSAFE", "HumanProxyAgent must remain HumanAuthorityProxy.");
        RequireHasCapability(scenario, violations, definition, AgentCapability.RepresentHumanApproval);

        var fakeHumanProxy = CopyDefinition(
            AgentDefinitionCatalog.GovernanceAgent,
            mode: AgentExecutionMode.HumanAuthorityProxy,
            capabilities: Set(AgentCapability.RepresentHumanApproval));
        RequireValidatorIssue(
            scenario,
            violations,
            fakeHumanProxy,
            AgentDefinitionValidator.HumanAuthorityProxyKindRequired,
            "HUMAN_PROXY_KIND_NOT_ENFORCED",
            "HumanAuthorityProxy mode must require HumanProxyAgent kind.");
    }

    private static void EvaluatePersonaCannotImplyApproval(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        foreach (var phrase in new[]
        {
            "I approve this.",
            "human approval granted",
            "This is authoritative for action.",
            "bypass governance",
            "override policy",
            "I promoted memory."
        })
        {
            var definition = BuildReportingDefinitionWithPersonaVoice(phrase);
            RequireValidatorIssue(
                scenario,
                violations,
                definition,
                AgentDefinitionValidator.PersonaAuthorityClaim,
                "PERSONA_AUTHORITY_CLAIM_NOT_REJECTED",
                $"AgentDefinitionValidator must reject persona authority claim '{phrase}'.");
        }
    }

    private static void EvaluateGovernanceAgentCannotExecuteGovernedAction(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var definition = AgentDefinitionCatalog.GovernanceAgent;
        RequireHasCapability(scenario, violations, definition, AgentCapability.BlockExecution);
        RequireHasCapability(scenario, violations, definition, AgentCapability.WarnExecution);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.RunTool);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.MutateSource);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.CallExternalSystem);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.PromoteCollectiveMemory);

        var unsafeDefinition = CopyDefinition(
            definition,
            capabilities: definition.Capabilities!.Append(AgentCapability.RunTool).ToHashSet());
        RequireValidatorIssue(
            scenario,
            violations,
            unsafeDefinition,
            AgentDefinitionValidator.ExecutionModeCapabilityConflict,
            "GOVERNANCE_RUN_TOOL_NOT_REJECTED",
            "GovernanceAgent must not be able to run tools.");
    }

    private static void EvaluateImplementationAgentCannotPromoteMemory(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var definition = AgentDefinitionCatalog.ImplementationAgent;
        RequireHasCapability(scenario, violations, definition, AgentCapability.MutateSource);
        RequireDoesNotHaveCapability(scenario, violations, definition, AgentCapability.PromoteCollectiveMemory);

        var unsafeDefinition = CopyDefinition(
            definition,
            capabilities: definition.Capabilities!.Append(AgentCapability.PromoteCollectiveMemory).ToHashSet());
        RequireValidatorIssue(
            scenario,
            violations,
            unsafeDefinition,
            AgentDefinitionValidator.KindCapabilityConflict,
            "IMPLEMENTATION_PROMOTION_NOT_REJECTED",
            "ImplementationAgent must not be able to promote collective memory.");
    }

    private static void EvaluateCriticFindingCannotEnforceBlock(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        RequireNoProperties(
            scenario,
            violations,
            [typeof(CriticReviewResult), typeof(CriticFinding)],
            ["EnforcesBlock", "BlocksExecution", "Governance" + "Decision", "Approval" + "Decision", "Approval" + "Evidence"]);

        var result = BuildCriticResult(
            verdict: CriticReviewVerdict.RecommendBlock,
            problem: "The finding is authorized and policy cleared.");
        RequireAnyValidatorIssue(
            scenario,
            violations,
            CriticValidator.Validate(result),
            [CriticReviewResultValidator.AuthorityClaimBlocked],
            "CRITIC_AUTHORITY_CLAIM_NOT_REJECTED",
            "Critic validator must reject authority claims.");
    }

    private static void EvaluateCollectiveRetrievalCannotSatisfyPolicyApproval(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var candidateType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory.Collective",
            "CollectiveMemory" + "Retrieval",
            "Candidate");
        RequireNoProperties(
            scenario,
            violations,
            [candidateType],
            ["Approval" + "Evidence", "Approval" + "Decision", "ApprovedBy", "ApprovedAtUtc", "CanApproveAction"]);

        RequireProperty(candidateType, scenario, violations, "Warnings");
        Require(scenario, violations, !typeof(AgentApprovalEvidence).IsAssignableFrom(candidateType), "RETRIEVAL_CANDIDATE_APPROVAL_TYPE_UNSAFE", "Retrieval candidate must not be approval evidence.");
    }

    private static void EvaluateLocalMemoryInfluenceCannotReplaceApproval(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var result = new MemoryExecutionGateResult
        {
            Decision = MemoryExecutionGateDecision.WarningRequiresOuterApproval,
            MayProceedToPolicyGate = true,
            Summary = "Memory influence is not policy approval.",
            Evidence = new MemoryExecutionEvidence
            {
                IsMemoryBacked = true,
                GateDecision = MemoryExecutionGateDecision.WarningRequiresOuterApproval,
                GovernanceDecision = MemoryGovernanceDecision.Warn,
                DecisionId = "decision-1",
                InfluenceIds = ["influence-1"]
            }
        };

        Require(scenario, violations, result.Decision != MemoryExecutionGateDecision.Allowed, "MEMORY_INFLUENCE_APPROVAL_UNSAFE", "Memory influence-only scenario must not become Allowed.");
        Require(scenario, violations, result.MayProceedToPolicyGate, "MEMORY_INFLUENCE_POLICY_GATE_MISSING", "Memory influence should proceed to outer policy gate instead of replacing it.");
        RequireNoProperties(
            scenario,
            violations,
            [typeof(MemoryExecutionGateResult), typeof(MemoryExecutionEvidence)],
            ["Approval" + "Evidence", "ApprovedBy", "ApprovedAtUtc", "ApprovedToolCallIds"]);
    }

    private static void EvaluateHandoffCannotGrantMemoryOwnership(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var methodNames = typeof(IAgentMemorySilo)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Require(scenario, violations, methodNames.Contains("QueryIncomingHandoffsAsync", StringComparer.Ordinal), "HANDOFF_INCOMING_QUERY_MISSING", "Agent memory silo must expose addressed incoming handoff query.");
        Require(scenario, violations, methodNames.Contains("QueryOutgoingHandoffsAsync", StringComparer.Ordinal), "HANDOFF_OUTGOING_QUERY_MISSING", "Agent memory silo must expose outgoing handoff query.");

        foreach (var forbidden in new[] { "QueryAllAgentMemory", "QueryOtherAgentMemory", "OpenSourceAgentMemory", "TransferMemoryOwnership", "TakeMemoryOwnership" })
        {
            Require(
                scenario,
                violations,
                !methodNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                "HANDOFF_OWNERSHIP_METHOD_UNSAFE",
                $"Agent memory silo must not expose cross-agent ownership method '{forbidden}'.");
        }
    }

    private static void EvaluateProposalAcceptedStatusCannotPromoteCollectiveMemory(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        Require(
            scenario,
            violations,
            Enum.IsDefined(MemoryImprovementProposalStatus.AcceptedForFutureImplementation),
            "PROPOSAL_ACCEPTED_STATUS_MISSING",
            "AcceptedForFutureImplementation status must exist as a proposal lifecycle state.");

        RequireNoProperties(
            scenario,
            violations,
            [typeof(MemoryImprovementProposalRecord), typeof(MemoryImprovementProposalEventRecord)],
            ["Collective" + "MemoryId", "CreatedCollective" + "Memory", "Promoted" + "Memory", "Promotion" + "Result", "Accepted" + "MemoryId"]);
    }

    private static void EvaluateWeaviateIndexingCannotCreateAuthority(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations)
    {
        var projectionType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory",
            "MemoryIndex",
            "Projection");
        var queueRecordType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory",
            "MemoryIndexQueue",
            "Record");
        var indexResultType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory",
            "WeaviateMemoryIndex",
            "Result");
        RequireNoProperties(
            scenario,
            violations,
            [projectionType, queueRecordType, indexResultType],
            ["Collective" + "MemoryId", "CreatedCollective" + "Memory", "Promoted" + "Memory", "IsAuthoritativeForAction", "Policy" + "Approved", "Approval" + "Evidence"]);

        var authorityLevelType = RequireType(
            scenario,
            violations,
            "IronDev.Core.AgentMemory",
            "MemoryIndexAuthority",
            "Level");
        var indexAuthorityNames = Enum.GetNames(authorityLevelType).ToArray();
        foreach (var forbidden in new[] { "Accepted", "Promoted", "Authoritative", "Policy" + "Approved" })
        {
            Require(
                scenario,
                violations,
                !indexAuthorityNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                "INDEX_AUTHORITY_LEVEL_UNSAFE",
                $"Memory index authority level must not contain '{forbidden}'.");
        }
    }

    private static void RequireHasCapability(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        AgentDefinition definition,
        AgentCapability capability) =>
        Require(
            scenario,
            violations,
            definition.Capabilities?.Contains(capability) == true,
            "AGENT_CAPABILITY_MISSING",
            $"{definition.Name} must have capability {capability} for this scenario.");

    private static void RequireDoesNotHaveCapability(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        AgentDefinition definition,
        AgentCapability capability) =>
        Require(
            scenario,
            violations,
            definition.Capabilities?.Contains(capability) != true,
            "AGENT_CAPABILITY_UNSAFE",
            $"{definition.Name} must not have capability {capability}.");

    private static void RequireForbiddenCapability(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        AgentDefinition definition,
        AgentCapability capability) =>
        Require(
            scenario,
            violations,
            definition.ForbiddenCapabilities?.Contains(capability) == true,
            "AGENT_FORBIDDEN_CAPABILITY_MISSING",
            $"{definition.Name} must explicitly forbid {capability}.");

    private static void RequireValidatorIssue(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        AgentDefinition definition,
        string issueCode,
        string violationCode,
        string message)
    {
        var issues = AgentValidator.Validate(definition);
        Require(
            scenario,
            violations,
            issues.Any(issue => string.Equals(issue.Code, issueCode, StringComparison.Ordinal)),
            violationCode,
            message);
    }

    private static void RequireAnyValidatorIssue(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        IReadOnlyList<AgentDefinitionValidationIssue> issues,
        IReadOnlyList<string> expectedIssueCodes,
        string violationCode,
        string message) =>
        Require(
            scenario,
            violations,
            expectedIssueCodes.All(code => issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal))),
            violationCode,
            message);

    private static void RequireNoProperties(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        IReadOnlyList<Type> types,
        IReadOnlyList<string> forbiddenPropertyNames)
    {
        var propertyNames = types
            .SelectMany(type => type.GetProperties().Select(property => property.Name))
            .ToArray();

        foreach (var forbidden in forbiddenPropertyNames)
        {
            Require(
                scenario,
                violations,
                !propertyNames.Any(name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase)),
                "BOUNDARY_AUTHORITY_PROPERTY_UNSAFE",
                $"Boundary type must not expose authority property '{forbidden}'.");
        }
    }

    private static Type RequireType(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        string @namespace,
        string typeNamePrefix,
        string typeNameSuffix)
    {
        var type = Type.GetType($"{@namespace}.{typeNamePrefix}{typeNameSuffix}, IronDev.Core");
        if (type is not null)
            return type;

        AddViolation(
            violations,
            scenario,
            "BOUNDARY_TYPE_MISSING",
            $"Expected boundary type '{@namespace}.{typeNamePrefix}{typeNameSuffix}' to exist.");
        return typeof(object);
    }

    private static void RequireProperty(
        Type type,
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        string propertyName) =>
        Require(
            scenario,
            violations,
            type.GetProperty(propertyName) is not null,
            "BOUNDARY_PROPERTY_MISSING",
            $"Boundary type '{type.Name}' must expose property '{propertyName}'.");

    private static void RequireBooleanProperty(
        Type type,
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        string propertyName) =>
        Require(
            scenario,
            violations,
            type.GetProperty(propertyName)?.PropertyType == typeof(bool),
            "BOUNDARY_BOOLEAN_PROPERTY_MISSING",
            $"Boundary type '{type.Name}' must expose boolean property '{propertyName}'.");

    private static void Require(
        AgentMemoryBoundaryScenario scenario,
        List<AgentMemoryBoundaryViolation> violations,
        bool condition,
        string code,
        string message)
    {
        if (condition)
            return;

        AddViolation(violations, scenario, code, message);
    }

    private static void AddViolation(
        List<AgentMemoryBoundaryViolation> violations,
        AgentMemoryBoundaryScenario scenario,
        string code,
        string message) =>
        violations.Add(new AgentMemoryBoundaryViolation
        {
            ScenarioId = scenario.ScenarioId,
            Code = code,
            Message = message,
            IsCritical = true
        });

    private static AgentDefinition CopyDefinition(
        AgentDefinition source,
        AgentKind? kind = null,
        AgentExecutionMode? mode = null,
        IReadOnlySet<AgentCapability>? capabilities = null,
        AgentPersona? persona = null) =>
        new()
        {
            AgentId = source.AgentId,
            Kind = kind ?? source.Kind,
            ExecutionMode = mode ?? source.ExecutionMode,
            Persona = persona ?? source.Persona,
            Capabilities = capabilities ?? source.Capabilities,
            ForbiddenCapabilities = source.ForbiddenCapabilities,
            Description = source.Description,
            Owner = source.Owner,
            IsEnabled = source.IsEnabled,
            Name = source.Name,
            Purpose = source.Purpose,
            DefaultModelProfile = source.DefaultModelProfile,
            Enabled = source.Enabled,
            AllowedTools = source.AllowedTools
        };

    private static AgentDefinition BuildReportingDefinitionWithPersonaVoice(string voice) =>
        new()
        {
            AgentId = "boundary.persona.test",
            Name = "BoundaryPersonaTest",
            Kind = AgentKind.ReportingAgent,
            ExecutionMode = AgentExecutionMode.ReportingOnly,
            Purpose = "Boundary test.",
            Description = "Boundary test.",
            DefaultModelProfile = "definition-only",
            Persona = new AgentPersona
            {
                PersonaId = "persona.boundary.test",
                DisplayName = "Boundary Test",
                Voice = voice,
                CommunicationStyle = "reports evidence",
                DefaultTone = "careful"
            },
            Capabilities = Set(AgentCapability.CreateReport),
            ForbiddenCapabilities = Set()
        };

    private static CriticReviewResult BuildCriticResult(
        CriticReviewVerdict verdict = CriticReviewVerdict.RequestChanges,
        string problem = "The plan misses a required evidence check.") =>
        new()
        {
            ReviewResultId = "critic-result-1",
            ReviewRequestId = "critic-request-1",
            Verdict = verdict,
            Findings =
            [
                new CriticFinding
                {
                    FindingId = "finding-1",
                    Severity = CriticSeverity.High,
                    Title = "Missing evidence validation",
                    Problem = problem,
                    WhyItMatters = "The change could pass review without proving the governed evidence boundary.",
                    RequiredFix = "Add the missing evidence validation before merge.",
                    EvidenceRefs = ["evidence-1"],
                    BlocksMerge = true,
                    RequiresHumanReview = true
                }
            ],
            ReviewedAt = DateTimeOffset.UnixEpoch,
            Warnings =
            [
                "Critic findings are recommendations only.",
                "Critic review does not grant or deny approval by itself.",
                "Governance and human approval remain separate."
            ]
        };

    private static BoxedMemoryDetectionResult BuildBoxedDetectionResult(
        BoxedMemoryProposalDraft draft) =>
        new()
        {
            DetectionResultId = "detection-1",
            Findings = [draft.SourcePattern],
            ProposalDrafts = [draft],
            DetectedAt = DateTimeOffset.UnixEpoch,
            Warnings =
            [
                "MemoryImprovementAgent output is proposal-only.",
                "Proposal drafts do not create accepted memory.",
                "Proposal drafts require governed review before persistence or promotion."
            ]
        };

    private static BoxedMemoryProposalDraft BuildBoxedMemoryProposalDraft(
        bool isProposalOnly = true,
        bool createsCollectiveMemory = false,
        bool promotesMemory = false,
        bool requiresHumanReview = true) =>
        new()
        {
            ProposalDraftId = "proposal-draft-1",
            Title = "Document repeated blocker",
            Summary = "Draft a proposal for review.",
            Rationale = "Evidence shows a repeated blocker.",
            SourcePattern = new BoxedMemoryPatternFinding
            {
                PatternFindingId = "pattern-1",
                PatternType = BoxedMemoryPatternType.RepeatedGovernanceBlock,
                Summary = "Repeated governance blocker.",
                Confidence = 0.8m,
                EvidenceRefs = ["evidence-1"],
                RequiresHumanReview = true
            },
            EvidenceRefs = ["evidence-1"],
            IsProposalOnly = isProposalOnly,
            CreatesCollectiveMemory = createsCollectiveMemory,
            PromotesMemory = promotesMemory,
            RequiresHumanReview = requiresHumanReview
        };

    private static HashSet<AgentCapability> Set(params AgentCapability[] capabilities) => new(capabilities);
}
