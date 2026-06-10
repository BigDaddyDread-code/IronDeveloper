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

    public static IReadOnlyList<AgentSpecialisationDefinition> CriticProfiles { get; } =
    [
        CodeReviewCritic,
        ArchitectureCritic,
        SecurityCritic,
        TestFailureCritic,
        BuildFailureCritic
    ];

    public static IReadOnlyList<AgentSpecialisationDefinition> All { get; } =
    [
        .. CriticProfiles
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

    private static AgentSpecialisationValidationRequirement[] CommonCriticValidationRequirements() =>
    [
        Validation("AgentDefinitionValidator", "Validates base agent identity and authority boundary."),
        Validation("AgentRunAuditEnvelopeValidator", "Validates durable audit envelope safety."),
        Validation("ThoughtLedgerSafetyValidator", "Validates thought-ledger evidence safety."),
        Validation("CriticReviewResultValidator", "Validates typed critic output."),
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
}
