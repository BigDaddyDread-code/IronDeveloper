namespace IronDev.Core.Agents;

public static class AgentSpecialisationCatalog
{
    public static AgentSpecialisationDefinition CodeReviewCritic { get; } = BuildCriticProfile(
        specialisationId: "builtin.critic.code-review",
        name: "CodeReviewCritic",
        description: "Reviews code changes, diffs, implementation plans, and code-quality evidence.",
        purposes:
        [
            "Find incorrect implementation details.",
            "Find missing tests and weak assertions.",
            "Find unsafe source-mutation assumptions.",
            "Find weak abstractions, duplicated logic, broken boundaries, hidden side effects, unvalidated error paths, observability gaps, and maintainability risks."
        ],
        inputTypes:
        [
            "PullRequest",
            "PullRequestDiff",
            "Patch",
            "CodeChangeProposal",
            "ImplementationPlan",
            "Ticket",
            "TestReport",
            "BuildReport",
            "RunReport",
            "AgentRunAuditEnvelope",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("Patch", "Patch evidence for code-change review."),
            Evidence("PullRequestDiff", "Diff evidence for code-change review."),
            Evidence("Ticket", "Ticket context for intended behaviour."),
            Evidence("ImplementationPlan", "Implementation plan context for intended behaviour."),
            Evidence("TestReport", "Test evidence when available."),
            Evidence("BuildReport", "Build evidence when available."),
            Evidence("AgentRunAuditEnvelope", "Agent-run audit evidence when linked to an agent run.")
        ]);

    public static AgentSpecialisationDefinition ArchitectureCritic { get; } = BuildCriticProfile(
        specialisationId: "builtin.critic.architecture-review",
        name: "ArchitectureCritic",
        description: "Reviews system design, backend boundaries, workflows, memory governance, agent governance, and architectural decisions.",
        purposes:
        [
            "Find unclear ownership and state-ownership confusion.",
            "Find hidden authority paths and weak boundaries.",
            "Find fake abstractions, over-engineering, under-engineering, audit gaps, memory-governance bypass risk, and future migration pain."
        ],
        inputTypes:
        [
            "ArchitecturePlan",
            "DesignDocument",
            "DecisionRecord",
            "Ticket",
            "RunReport",
            "AgentRunAuditEnvelope",
            "MemoryInfluenceRecord",
            "MemoryHandoff",
            "CollectiveMemoryCandidate",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("DesignDocument", "Design evidence for architecture review."),
            Evidence("ArchitecturePlan", "Architecture plan evidence for architecture review."),
            Evidence("DecisionRecord", "Decision record evidence when available."),
            Evidence("RunReport", "Run report evidence when reviewing implemented behaviour."),
            Evidence("AgentRunAuditEnvelope", "Agent-run audit evidence when reviewing implemented behaviour.")
        ]);

    public static AgentSpecialisationDefinition SecurityCritic { get; } = BuildCriticProfile(
        specialisationId: "builtin.critic.security-review",
        name: "SecurityCritic",
        description: "Reviews security-sensitive implementation and design evidence.",
        purposes:
        [
            "Find authorization bypass, tenant or project leakage, unsafe write paths, secret exposure, privilege escalation, unsafe tool execution, audit bypass, SQL write-surface risk, and dangerous defaults.",
            "Treat policy and governance artifacts as consumed evidence only."
        ],
        inputTypes:
        [
            "Patch",
            "PullRequestDiff",
            "SecurityReviewPacket",
            "ThreatModel",
            "AuthDesign",
            "PermissionModel",
            "AuditEnvelope",
            "AgentRunAuditEnvelope",
            "PolicyDecision",
            "GovernanceDecision",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("Patch", "Patch evidence for security review."),
            Evidence("DesignDocument", "Design evidence for security review."),
            Evidence("ThreatModel", "Threat model evidence when available."),
            Evidence("PermissionModel", "Permission model evidence when available."),
            Evidence(
                "PolicyDecision",
                "Policy decision evidence may be consumed but does not grant specialisation authority.",
                allowedAuthorityEvidenceTypes: ["PolicyDecision"]),
            Evidence(
                "GovernanceDecision",
                "Governance decision evidence may be consumed but does not grant specialisation authority.",
                allowedAuthorityEvidenceTypes: ["GovernanceDecision"]),
            Evidence(
                "HumanApprovalEvidence",
                "Human approval evidence may be consumed but does not grant specialisation authority.",
                allowedAuthorityEvidenceTypes: ["HumanApprovalEvidence"])
        ],
        inputRequirements:
        [
            Input("PolicyDecision", "Policy decision evidence may be consumed but does not grant specialisation authority.", allowedAuthorityReferenceTypes: ["PolicyDecision"]),
            Input("GovernanceDecision", "Governance decision evidence may be consumed but does not grant specialisation authority.", allowedAuthorityReferenceTypes: ["GovernanceDecision"]),
            Input("HumanApprovalEvidence", "Human approval evidence may be consumed but does not grant specialisation authority.", allowedAuthorityReferenceTypes: ["HumanApprovalEvidence"])
        ]);

    public static AgentSpecialisationDefinition TestFailureCritic { get; } = BuildCriticProfile(
        specialisationId: "builtin.critic.test-failure-review",
        name: "TestFailureCritic",
        description: "Reviews failed test evidence and likely failure modes.",
        purposes:
        [
            "Find real failure causes, bad test setup, flaky tests, unreliable assertions, missing regression coverage, failure masking, overfitted fixes, unproven fix claims, and test evidence gaps.",
            "Describe what evidence suggests without certifying a fix."
        ],
        inputTypes:
        [
            "TestReport",
            "TestFailureLog",
            "BuildReport",
            "RunReport",
            "Patch",
            "Ticket",
            "AgentRunAuditEnvelope",
            "ThoughtLedgerSummary",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("TestReport", "Test report evidence for failure review."),
            Evidence("TestFailureLog", "Test failure log evidence for failure review."),
            Evidence("Patch", "Patch evidence when failure relates to a proposed change."),
            Evidence("Ticket", "Ticket evidence when failure relates to intended behaviour."),
            Evidence("RunReport", "Run report evidence when failure comes from agent execution."),
            Evidence("AgentRunAuditEnvelope", "Agent-run audit evidence when failure comes from agent execution.")
        ]);

    public static AgentSpecialisationDefinition BuildFailureCritic { get; } = BuildCriticProfile(
        specialisationId: "builtin.critic.build-failure-review",
        name: "BuildFailureCritic",
        description: "Reviews build, compile, and package failure evidence.",
        purposes:
        [
            "Find compile errors, missing package references, wrong project references, broken public API usage, configuration mismatch, local and CI mismatch, unvalidated build claims, and environment-versus-code uncertainty."
        ],
        inputTypes:
        [
            "BuildReport",
            "CompilerError",
            "PackageRestoreLog",
            "TestReport",
            "Patch",
            "Ticket",
            "RunReport",
            "AgentRunAuditEnvelope",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("BuildReport", "Build report evidence for failure review."),
            Evidence("CompilerError", "Compiler error evidence for failure review."),
            Evidence("Patch", "Patch evidence when reviewing an implementation change."),
            Evidence("Ticket", "Ticket evidence when reviewing intended behaviour."),
            Evidence("PackageRestoreLog", "Package restore evidence when package resolution is involved.")
        ]);

    public static AgentSpecialisationDefinition RepeatedFailureModeDetector { get; } = BuildMemoryImprovementProfile(
        specialisationId: "builtin.memory.repeated-failure-mode-detector",
        name: "RepeatedFailureModeDetector",
        description: "Detects repeated failure patterns that may justify a memory-improvement proposal draft.",
        purposes:
        [
            "Find repeated test failures across runs.",
            "Find repeated build failures across runs.",
            "Find repeated implementation mistakes that appear in audit or run evidence.",
            "Find repeated missing-validation patterns without treating the pattern as authority."
        ],
        inputTypes:
        [
            "AgentRunAuditEnvelope",
            "RunReport",
            "TestReport",
            "BuildReport",
            "CriticReviewResult",
            "MemoryInfluenceRecord",
            "Ticket",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("AgentRunAuditEnvelope", "Agent-run audit evidence for repeated failure detection."),
            Evidence("RunReport", "Run report evidence for repeated failure detection."),
            Evidence("TestReport", "Test evidence when repeated failures involve tests."),
            Evidence("BuildReport", "Build evidence when repeated failures involve build or compile behaviour."),
            Evidence("CriticReviewResult", "Critic review evidence when repeated failure analysis references prior review findings.")
        ]);

    public static AgentSpecialisationDefinition RepeatedGovernanceBlockDetector { get; } = BuildMemoryImprovementProfile(
        specialisationId: "builtin.memory.repeated-governance-block-detector",
        name: "RepeatedGovernanceBlockDetector",
        description: "Detects repeated governance block patterns that may justify a memory-improvement proposal draft.",
        purposes:
        [
            "Find repeated blocked execution attempts.",
            "Find repeated missing evidence patterns in governed requests.",
            "Find repeated boundary mistakes without weakening the boundary.",
            "Find repeated policy-context misunderstandings while preserving governance as separate authority."
        ],
        inputTypes:
        [
            "AgentRunAuditEnvelope",
            "AgentBoundaryDecision",
            "MemoryExecutionAudit",
            "MemoryExecutionGateResult",
            "ConscienceMemoryGovernanceResult",
            "RunReport",
            "CriticReviewResult",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("AgentRunAuditEnvelope", "Agent-run audit evidence for governance-block pattern detection."),
            Evidence("AgentBoundaryDecision", "Boundary decision evidence for repeated governance-block detection."),
            Evidence("MemoryExecutionGateResult", "Memory execution gate evidence for repeated governance-block detection."),
            Evidence("ConscienceMemoryGovernanceResult", "Conscience memory governance evidence for repeated governance-block detection."),
            Evidence("RunReport", "Run report evidence when blocked behaviour appears in run output.")
        ]);

    public static AgentSpecialisationDefinition RepeatedManualCorrectionDetector { get; } = BuildMemoryImprovementProfile(
        specialisationId: "builtin.memory.repeated-manual-correction-detector",
        name: "RepeatedManualCorrectionDetector",
        description: "Detects repeated human correction patterns that may justify a memory-improvement proposal draft.",
        purposes:
        [
            "Find repeated human corrections across related runs.",
            "Find repeated clarification patterns that show weak local assumptions.",
            "Find repeated review corrections that should become proposal evidence.",
            "Describe correction patterns without claiming to represent a human decision."
        ],
        inputTypes:
        [
            "HumanCorrection",
            "HumanInstruction",
            "AgentRunAuditEnvelope",
            "CriticReviewResult",
            "MemoryImprovementDetectionResult",
            "MemoryImprovementProposalDraft",
            "RunReport",
            "Ticket"
        ],
        evidenceRequirements:
        [
            Evidence("HumanCorrection", "Human correction evidence for repeated manual correction detection."),
            Evidence("HumanInstruction", "Human instruction evidence for repeated manual correction detection."),
            Evidence("CriticReviewResult", "Critic review evidence when corrections are review-related."),
            Evidence("AgentRunAuditEnvelope", "Agent-run audit evidence when corrections are tied to runs."),
            Evidence("RunReport", "Run report evidence when corrections are tied to run outcomes.")
        ]);

    public static AgentSpecialisationDefinition StaleMemoryDetector { get; } = BuildMemoryImprovementProfile(
        specialisationId: "builtin.memory.stale-memory-detector",
        name: "StaleMemoryDetector",
        description: "Detects memory evidence that may be stale and should be reviewed through a memory-improvement proposal draft.",
        purposes:
        [
            "Find memory evidence contradicted by newer run evidence.",
            "Find memory evidence that repeatedly fails to explain current behaviour.",
            "Find memory evidence that needs verification before further use.",
            "Describe stale-memory risk without changing memory authority."
        ],
        inputTypes:
        [
            "CollectiveMemoryCandidate",
            "MemoryItemReference",
            "MemoryInfluenceRecord",
            "RetrievalCandidate",
            "AgentRunAuditEnvelope",
            "RunReport",
            "DecisionRecord",
            "HumanCorrection",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("CollectiveMemoryCandidate", "Memory candidate evidence for stale-memory detection."),
            Evidence("MemoryItemReference", "Memory item reference evidence for stale-memory detection."),
            Evidence("MemoryInfluenceRecord", "Influence record evidence showing where memory affected a decision."),
            Evidence("RunReport", "Newer run report evidence for stale-memory comparison."),
            Evidence("HumanCorrection", "Human correction evidence when stale-memory risk was surfaced by correction.")
        ]);

    public static AgentSpecialisationDefinition ContradictionDetector { get; } = BuildMemoryImprovementProfile(
        specialisationId: "builtin.memory.contradiction-detector",
        name: "ContradictionDetector",
        description: "Detects contradictory memory evidence that may justify a memory-improvement proposal draft.",
        purposes:
        [
            "Find contradictory memory candidates.",
            "Find contradictory proposal drafts.",
            "Find evidence conflicts between run outcomes, review findings, and memory influence.",
            "Describe the contradiction without resolving it as authority."
        ],
        inputTypes:
        [
            "MemoryItemReference",
            "CollectiveMemoryCandidate",
            "MemoryImprovementProposalDraft",
            "MemoryInfluenceRecord",
            "RetrievalCandidate",
            "AgentRunAuditEnvelope",
            "DecisionRecord",
            "CriticReviewResult",
            "HumanCorrection",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("MemoryItemReference", "Memory item reference evidence for contradiction detection."),
            Evidence("CollectiveMemoryCandidate", "Memory candidate evidence for contradiction detection."),
            Evidence("ConflictingEvidenceReference", "Conflicting evidence reference for contradiction detection."),
            Evidence("CriticReviewResult", "Critic review evidence when contradiction appears in review output."),
            Evidence("DecisionRecord", "Decision record evidence when contradiction appears in decision history.")
        ]);

    public static AgentSpecialisationDefinition RetrievalMissDetector { get; } = BuildMemoryImprovementProfile(
        specialisationId: "builtin.memory.retrieval-miss-detector",
        name: "RetrievalMissDetector",
        description: "Detects memory retrieval misses that may justify a memory-improvement proposal draft.",
        purposes:
        [
            "Find queries that should have surfaced relevant memory but did not.",
            "Find repeated retrieval gaps in related runs.",
            "Find weak retrieval evidence without creating retrieval authority.",
            "Describe missing-evidence patterns for later human review."
        ],
        inputTypes:
        [
            "RetrievalQuery",
            "RetrievalCandidate",
            "RetrievalResult",
            "AgentRunAuditEnvelope",
            "MemoryInfluenceRecord",
            "CriticReviewResult",
            "HumanCorrection",
            "RunReport",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("RetrievalQuery", "Retrieval query evidence for retrieval-miss detection."),
            Evidence("RetrievalResult", "Retrieval result evidence for retrieval-miss detection."),
            Evidence("RetrievalCandidate", "Retrieved candidate evidence for retrieval-miss comparison."),
            Evidence("AgentRunAuditEnvelope", "Agent-run audit evidence when retrieval misses are tied to runs."),
            Evidence("HumanCorrection", "Human correction evidence when retrieval miss was noticed by correction.")
        ]);

    public static AgentSpecialisationDefinition DuplicateProposalDetector { get; } = BuildMemoryImprovementProfile(
        specialisationId: "builtin.memory.duplicate-proposal-detector",
        name: "DuplicateProposalDetector",
        description: "Detects duplicate or overlapping memory-improvement proposal drafts.",
        purposes:
        [
            "Find repeated proposal drafts for the same pattern.",
            "Find overlapping proposal drafts that should be reviewed together.",
            "Find proposal noise before human review.",
            "Describe duplicate proposal risk without persisting or resolving proposals."
        ],
        inputTypes:
        [
            "MemoryImprovementProposalDraft",
            "MemoryImprovementDetectionResult",
            "MemoryImprovementProposal",
            "AgentRunAuditEnvelope",
            "RunReport",
            "HumanCorrection",
            "HumanInstruction"
        ],
        evidenceRequirements:
        [
            Evidence("MemoryImprovementProposalDraft", "Proposal draft evidence for duplicate proposal detection."),
            Evidence("MemoryImprovementProposal", "Proposal evidence for duplicate proposal detection."),
            Evidence("MemoryImprovementDetectionResult", "Detection result evidence for duplicate proposal detection."),
            Evidence("AgentRunAuditEnvelope", "Agent-run audit evidence when duplicate proposals are tied to runs."),
            Evidence("HumanCorrection", "Human correction evidence when duplicate proposal risk was noticed by correction.")
        ]);

    public static IReadOnlyList<AgentSpecialisationDefinition> CriticProfiles { get; } =
    [
        CodeReviewCritic,
        ArchitectureCritic,
        SecurityCritic,
        TestFailureCritic,
        BuildFailureCritic
    ];

    public static IReadOnlyList<AgentSpecialisationDefinition> MemoryImprovementProfiles { get; } =
    [
        RepeatedFailureModeDetector,
        RepeatedGovernanceBlockDetector,
        RepeatedManualCorrectionDetector,
        StaleMemoryDetector,
        ContradictionDetector,
        RetrievalMissDetector,
        DuplicateProposalDetector
    ];

    public static IReadOnlyList<AgentSpecialisationDefinition> All { get; } =
    [
        .. CriticProfiles,
        .. MemoryImprovementProfiles
    ];

    public static AgentSpecialisationDefinition? GetById(string specialisationId) =>
        All.FirstOrDefault(profile =>
            string.Equals(profile.SpecialisationId, specialisationId, StringComparison.Ordinal));

    public static IReadOnlyList<AgentSpecialisationDefinition> GetForAgent(string agentId) =>
        All.Where(profile =>
                string.Equals(profile.AppliesToAgentId, agentId, StringComparison.Ordinal))
            .ToArray();

    public static IReadOnlyList<AgentSpecialisationDefinition> GetByKind(AgentSpecialisationKind kind) =>
        All.Where(profile => profile.Kind == kind).ToArray();

    private static AgentSpecialisationDefinition BuildCriticProfile(
        string specialisationId,
        string name,
        string description,
        IReadOnlyList<string> purposes,
        IReadOnlyList<string> inputTypes,
        IReadOnlyList<AgentSpecialisationEvidenceRequirement> evidenceRequirements,
        IReadOnlyList<AgentSpecialisationInputRequirement>? inputRequirements = null) =>
        new()
        {
            SpecialisationId = specialisationId,
            Name = name,
            Description = description,
            Kind = AgentSpecialisationKind.CriticalReview,
            AppliesToAgentId = AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
            RequiredAgentKind = AgentKind.ReviewAgent,
            RequiredExecutionMode = AgentExecutionMode.OutOfBandReviewOnly,
            RequiredCapabilities =
            [
                AgentCapability.CreateCriticFinding,
                AgentCapability.CreateReport,
                AgentCapability.WarnExecution
            ],
            ForbiddenCapabilities = CommonCriticForbiddenCapabilities(),
            Purposes = purposes,
            InputRequirements = inputTypes.Select(type => Input(type, $"{type} evidence for critic review."))
                .Concat(inputRequirements ?? Array.Empty<AgentSpecialisationInputRequirement>())
                .ToArray(),
            EvidenceRequirements = evidenceRequirements,
            OutputRequirements =
            [
                ReviewOnlyCriticOutput()
            ],
            ValidationRequirements = CommonCriticValidationRequirements(),
            ForbiddenBehaviours = CommonCriticForbiddenBehaviours(),
            AuthorityBoundary = AgentSpecialisationAuthorityBoundary.None
        };

    private static AgentSpecialisationDefinition BuildMemoryImprovementProfile(
        string specialisationId,
        string name,
        string description,
        IReadOnlyList<string> purposes,
        IReadOnlyList<string> inputTypes,
        IReadOnlyList<AgentSpecialisationEvidenceRequirement> evidenceRequirements) =>
        new()
        {
            SpecialisationId = specialisationId,
            Name = name,
            Description = description,
            Kind = AgentSpecialisationKind.MemoryImprovementDetection,
            AppliesToAgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
            RequiredAgentKind = AgentKind.ProposalAgent,
            RequiredExecutionMode = AgentExecutionMode.ProposalOnly,
            RequiredCapabilities =
            [
                AgentCapability.CreateMemoryProposal,
                AgentCapability.CreateReport
            ],
            ForbiddenCapabilities = CommonMemoryImprovementForbiddenCapabilities(),
            Purposes = purposes,
            InputRequirements = inputTypes.Select(type => Input(type, $"{type} evidence for memory-improvement detection."))
                .ToArray(),
            EvidenceRequirements = evidenceRequirements,
            OutputRequirements =
            [
                ProposalOnlyMemoryImprovementDetectionOutput(),
                ProposalOnlyMemoryImprovementDraftOutput()
            ],
            ValidationRequirements = CommonMemoryImprovementValidationRequirements(),
            ForbiddenBehaviours = CommonMemoryImprovementForbiddenBehaviours(),
            AuthorityBoundary = AgentSpecialisationAuthorityBoundary.None
        };

    private static AgentCapability[] CommonCriticForbiddenCapabilities() =>
    [
        AgentCapability.RunTool,
        AgentCapability.MutateSource,
        AgentCapability.CallExternalSystem,
        AgentCapability.PromoteCollectiveMemory,
        AgentCapability.RepresentHumanApproval,
        AgentCapability.RepresentHumanPromotionDecision,
        AgentCapability.BlockExecution
    ];

    private static AgentCapability[] CommonMemoryImprovementForbiddenCapabilities() =>
    [
        AgentCapability.RunTool,
        AgentCapability.MutateSource,
        AgentCapability.CallExternalSystem,
        AgentCapability.PromoteCollectiveMemory,
        AgentCapability.RepresentHumanApproval,
        AgentCapability.RepresentHumanPromotionDecision,
        AgentCapability.BlockExecution
    ];

    private static AgentSpecialisationForbiddenBehaviour[] CommonCriticForbiddenBehaviours() =>
    [
        Forbidden("RunTool"),
        Forbidden("MutateSource"),
        Forbidden("CallExternalSystem"),
        Forbidden("PromoteCollectiveMemory"),
        Forbidden("RepresentHumanApproval"),
        Forbidden("RepresentHumanPromotionDecision"),
        Forbidden("OverridePolicy"),
        Forbidden("BypassGovernance"),
        Forbidden("CreateAuthority"),
        Forbidden("CreateRuntimeAction"),
        Forbidden("StoreRawPrompt"),
        Forbidden("StoreRawCompletion"),
        Forbidden("StoreChainOfThought"),
        Forbidden("StoreScratchpad"),
        Forbidden("StorePrivateReasoning")
    ];

    private static AgentSpecialisationForbiddenBehaviour[] CommonMemoryImprovementForbiddenBehaviours() =>
    [
        ForbiddenMemory("RunTool"),
        ForbiddenMemory("MutateSource"),
        ForbiddenMemory("CallExternalSystem"),
        ForbiddenMemory("PromoteCollectiveMemory"),
        ForbiddenMemory("RepresentHumanApproval"),
        ForbiddenMemory("RepresentHumanPromotionDecision"),
        ForbiddenMemory("OverridePolicy"),
        ForbiddenMemory("BypassGovernance"),
        ForbiddenMemory("CreateAuthority"),
        ForbiddenMemory("CreateRuntimeAction"),
        ForbiddenMemory("StoreRawPrompt"),
        ForbiddenMemory("StoreRawCompletion"),
        ForbiddenMemory("StoreChainOfThought"),
        ForbiddenMemory("StoreScratchpad"),
        ForbiddenMemory("StorePrivateReasoning"),
        ForbiddenMemory("PersistMemoryProposal"),
        ForbiddenMemory("CreateCollectiveMemory"),
        ForbiddenMemory("AcceptCollectiveMemory"),
        ForbiddenMemory("WriteWeaviateIndex")
    ];

    private static AgentSpecialisationValidationRequirement[] CommonCriticValidationRequirements() =>
    [
        Validation("AgentDefinitionValidator", "Validates base agent identity and authority boundary."),
        Validation("AgentRunAuditEnvelopeValidator", "Validates durable audit envelope safety."),
        Validation("ThoughtLedgerSafetyValidator", "Validates thought-ledger evidence safety."),
        Validation("CriticReviewResultValidator", "Validates typed critic output."),
        Validation("AgentSpecialisationValidator", "Validates specialisation contract safety.")
    ];

    private static AgentSpecialisationValidationRequirement[] CommonMemoryImprovementValidationRequirements() =>
    [
        Validation("AgentDefinitionValidator", "Validates base agent identity and authority boundary."),
        Validation("AgentRunAuditEnvelopeValidator", "Validates durable audit envelope safety."),
        Validation("ThoughtLedgerSafetyValidator", "Validates thought-ledger evidence safety."),
        Validation("MemoryImprovementDetectionResultValidator", "Validates typed memory-improvement detection output."),
        Validation("AgentSpecialisationValidator", "Validates specialisation contract safety.")
    ];

    private static AgentSpecialisationOutputRequirement ReviewOnlyCriticOutput() =>
        new()
        {
            OutputType = "CriticReviewResult",
            Description = "Review-only critic result for human review.",
            RequiresHumanReview = true,
            MustBeReviewOnly = true,
            MayCreateAuthority = false,
            MayCreateRuntimeAction = false,
            MayPromoteMemory = false
        };

    private static AgentSpecialisationOutputRequirement ProposalOnlyMemoryImprovementDetectionOutput() =>
        new()
        {
            OutputType = "MemoryImprovementDetectionResult",
            Description = "Proposal-only memory-improvement detection result for human review.",
            RequiresHumanReview = true,
            MustBeProposalOnly = true,
            MayCreateAuthority = false,
            MayCreateRuntimeAction = false,
            MayPromoteMemory = false
        };

    private static AgentSpecialisationOutputRequirement ProposalOnlyMemoryImprovementDraftOutput() =>
        new()
        {
            OutputType = "MemoryImprovementProposalDraft",
            Description = "Proposal-only memory-improvement draft for human review.",
            RequiresHumanReview = true,
            MustBeProposalOnly = true,
            MayCreateAuthority = false,
            MayCreateRuntimeAction = false,
            MayPromoteMemory = false
        };

    private static AgentSpecialisationInputRequirement Input(
        string inputType,
        string description,
        IReadOnlyList<string>? allowedAuthorityReferenceTypes = null) =>
        new()
        {
            InputType = inputType,
            Description = description,
            Required = true,
            AllowedAuthorityReferenceTypes = allowedAuthorityReferenceTypes ?? Array.Empty<string>()
        };

    private static AgentSpecialisationEvidenceRequirement Evidence(
        string evidenceType,
        string description,
        IReadOnlyList<string>? allowedAuthorityEvidenceTypes = null) =>
        new()
        {
            EvidenceType = evidenceType,
            Description = description,
            Required = true,
            AllowedAuthorityEvidenceTypes = allowedAuthorityEvidenceTypes ?? Array.Empty<string>()
        };

    private static AgentSpecialisationValidationRequirement Validation(
        string validatorName,
        string description) =>
        new()
        {
            ValidatorName = validatorName,
            Description = description,
            Required = true
        };

    private static AgentSpecialisationForbiddenBehaviour Forbidden(string behaviour) =>
        new()
        {
            Behaviour = behaviour,
            Reason = $"{behaviour} is outside this critic profile boundary.",
            Required = true
        };

    private static AgentSpecialisationForbiddenBehaviour ForbiddenMemory(string behaviour) =>
        new()
        {
            Behaviour = behaviour,
            Reason = $"{behaviour} is outside this memory-improvement profile boundary.",
            Required = true
        };
}
