using IronDev.Core.Agents;

namespace IronDev.Core.Agents.Audit;

public sealed class AgentRunAuditEnvelopeValidator
{
    public const string RunRequired = "AGENT_RUN_AUDIT_RUN_REQUIRED";
    public const string AgentDefinitionRequired = "AGENT_RUN_AUDIT_AGENT_DEFINITION_REQUIRED";
    public const string RunIdRequired = "AGENT_RUN_AUDIT_RUN_ID_REQUIRED";
    public const string ScopeRequired = "AGENT_RUN_AUDIT_SCOPE_REQUIRED";
    public const string RequesterRequired = "AGENT_RUN_AUDIT_REQUESTER_REQUIRED";
    public const string TriggerTypeInvalid = "AGENT_RUN_AUDIT_TRIGGER_TYPE_INVALID";
    public const string RunStatusInvalid = "AGENT_RUN_AUDIT_STATUS_INVALID";
    public const string CompletedBeforeCreated = "AGENT_RUN_AUDIT_COMPLETED_BEFORE_CREATED";
    public const string AgentDefinitionMismatch = "AGENT_RUN_AUDIT_AGENT_DEFINITION_MISMATCH";
    public const string ChildRunIdMismatch = "AGENT_RUN_AUDIT_CHILD_RUN_ID_MISMATCH";
    public const string RawPrivateReasoningBlocked = "AGENT_RUN_AUDIT_RAW_PRIVATE_REASONING_BLOCKED";
    public const string AuthorityClaimBlocked = "AGENT_RUN_AUDIT_AUTHORITY_CLAIM_BLOCKED";
    public const string InputAuthorityBlocked = "AGENT_RUN_AUDIT_INPUT_AUTHORITY_BLOCKED";
    public const string OutputAuthorityBlocked = "AGENT_RUN_AUDIT_OUTPUT_AUTHORITY_BLOCKED";
    public const string OutputRuntimeActionBlocked = "AGENT_RUN_AUDIT_OUTPUT_RUNTIME_ACTION_BLOCKED";
    public const string CriticOutputMustBeReviewOnly = "AGENT_RUN_AUDIT_CRITIC_OUTPUT_MUST_BE_REVIEW_ONLY";
    public const string MemoryProposalMustBeProposalOnly = "AGENT_RUN_AUDIT_MEMORY_PROPOSAL_MUST_BE_PROPOSAL_ONLY";
    public const string CapabilityUseRequiredField = "AGENT_RUN_AUDIT_CAPABILITY_USE_REQUIRED_FIELD";
    public const string CapabilityUseInvalid = "AGENT_RUN_AUDIT_CAPABILITY_USE_INVALID";
    public const string ForbiddenCapabilityNotBlocked = "AGENT_RUN_AUDIT_FORBIDDEN_CAPABILITY_NOT_BLOCKED";
    public const string UndeclaredCapabilityAllowed = "AGENT_RUN_AUDIT_UNDECLARED_CAPABILITY_ALLOWED";
    public const string DangerousCapabilityAllowed = "AGENT_RUN_AUDIT_DANGEROUS_CAPABILITY_ALLOWED";
    public const string CapabilitySnapshotMismatch = "AGENT_RUN_AUDIT_CAPABILITY_SNAPSHOT_MISMATCH";
    public const string BoundaryDecisionRequiredField = "AGENT_RUN_AUDIT_BOUNDARY_DECISION_REQUIRED_FIELD";
    public const string BoundaryDecisionInvalid = "AGENT_RUN_AUDIT_BOUNDARY_DECISION_INVALID";
    public const string BoundaryAuthorityBlocked = "AGENT_RUN_AUDIT_BOUNDARY_AUTHORITY_BLOCKED";
    public const string BoundaryApprovalBlocked = "AGENT_RUN_AUDIT_BOUNDARY_APPROVAL_BLOCKED";
    public const string BoundaryPolicyApprovalBlocked = "AGENT_RUN_AUDIT_BOUNDARY_POLICY_APPROVAL_BLOCKED";
    public const string BoundaryMemoryPromotionBlocked = "AGENT_RUN_AUDIT_BOUNDARY_MEMORY_PROMOTION_BLOCKED";
    public const string StepRequiredField = "AGENT_RUN_AUDIT_STEP_REQUIRED_FIELD";
    public const string StepInvalid = "AGENT_RUN_AUDIT_STEP_INVALID";
    public const string DuplicateStepSequence = "AGENT_RUN_AUDIT_DUPLICATE_STEP_SEQUENCE";

    private static readonly IReadOnlySet<AgentCapability> DangerousExecutionCapabilities = new HashSet<AgentCapability>
    {
        AgentCapability.RunTool,
        AgentCapability.MutateSource,
        AgentCapability.CallExternalSystem,
        AgentCapability.PromoteCollectiveMemory
    };

    private static readonly IReadOnlySet<string> AuthoritativeInputTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "HumanApprovalEvidence",
        "GovernanceDecision",
        "PolicyDecision"
    };

    public IReadOnlyList<AgentDefinitionValidationIssue> Validate(AgentRunAuditEnvelope envelope)
    {
        var issues = new List<AgentDefinitionValidationIssue>();

        if (envelope.Run is null)
        {
            AddError(issues, RunRequired, "Run is required.");
            return issues;
        }

        if (envelope.AgentDefinitionSnapshot is null)
        {
            AddError(issues, AgentDefinitionRequired, "AgentDefinitionSnapshot is required.");
            return issues;
        }

        ValidateRun(envelope.Run, issues);
        ValidateAgentDefinitionSnapshot(envelope.Run, envelope.AgentDefinitionSnapshot, issues);
        ValidateInputs(envelope.Run.AgentRunId, envelope.Inputs, issues);
        ValidateOutputs(envelope.Run.AgentRunId, envelope.Outputs, issues);
        ValidateSteps(envelope.Run.AgentRunId, envelope.Steps, issues);
        ValidateCapabilityUses(envelope.Run.AgentRunId, envelope.AgentDefinitionSnapshot, envelope.CapabilityUses, issues);
        ValidateBoundaryDecisions(envelope.Run.AgentRunId, envelope.BoundaryDecisions, issues);
        ValidateThoughtLedger(envelope.Run.AgentRunId, envelope.ThoughtLedger, issues);

        return issues;
    }

    private static void ValidateRun(AgentRunRecord run, List<AgentDefinitionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(run.AgentRunId))
            AddError(issues, RunIdRequired, "AgentRunId is required.");

        if (string.IsNullOrWhiteSpace(run.TenantId) ||
            string.IsNullOrWhiteSpace(run.ProjectId) ||
            string.IsNullOrWhiteSpace(run.CampaignId) ||
            string.IsNullOrWhiteSpace(run.RunId) ||
            string.IsNullOrWhiteSpace(run.AgentId) ||
            string.IsNullOrWhiteSpace(run.AgentName))
        {
            AddError(issues, ScopeRequired, "TenantId, ProjectId, CampaignId, RunId, AgentId, and AgentName are required.");
        }

        if (string.IsNullOrWhiteSpace(run.RequestedByUserId) &&
            string.IsNullOrWhiteSpace(run.RequestedByAgentId))
        {
            AddError(issues, RequesterRequired, "A user or agent requester is required.");
        }

        if (!Enum.IsDefined(run.TriggerType))
            AddError(issues, TriggerTypeInvalid, "AgentRunTriggerType is invalid.");

        if (!Enum.IsDefined(run.Status))
            AddError(issues, RunStatusInvalid, "AgentRunStatus is invalid.");

        if (run.CompletedAtUtc.HasValue && run.CompletedAtUtc.Value < run.CreatedAtUtc)
            AddError(issues, CompletedBeforeCreated, "CompletedAtUtc cannot be earlier than CreatedAtUtc.");
    }

    private static void ValidateAgentDefinitionSnapshot(
        AgentRunRecord run,
        AgentDefinition definition,
        List<AgentDefinitionValidationIssue> issues)
    {
        if (!string.Equals(run.AgentId, definition.AgentId, StringComparison.Ordinal) ||
            !string.Equals(run.AgentName, definition.Name, StringComparison.Ordinal))
        {
            AddError(issues, AgentDefinitionMismatch, "Run agent identity must match AgentDefinitionSnapshot.");
        }
    }

    private static void ValidateInputs(
        string agentRunId,
        IReadOnlyList<AgentRunInputRef> inputs,
        List<AgentDefinitionValidationIssue> issues)
    {
        foreach (var input in inputs)
        {
            ValidateChildRunId(agentRunId, input.AgentRunId, "input", issues);

            if (string.IsNullOrWhiteSpace(input.InputRefId) ||
                string.IsNullOrWhiteSpace(input.RefType) ||
                string.IsNullOrWhiteSpace(input.RefId))
            {
                AddError(issues, StepRequiredField, "InputRefId, RefType, and RefId are required.");
            }

            if (input.ContainsRawPrivateReasoning ||
                AgentAuditTextSafety.ContainsRawPrivateReasoning([input.Summary, input.Source]))
            {
                AddError(issues, RawPrivateReasoningBlocked, "Input references cannot contain raw private reasoning.");
            }

            if (input.IsAuthoritativeForAction && !AuthoritativeInputTypes.Contains(input.RefType))
            {
                AddError(issues, InputAuthorityBlocked, "Input references can be authoritative only when they are explicit human/governance approval evidence.");
            }
        }
    }

    private static void ValidateOutputs(
        string agentRunId,
        IReadOnlyList<AgentRunOutputRef> outputs,
        List<AgentDefinitionValidationIssue> issues)
    {
        foreach (var output in outputs)
        {
            ValidateChildRunId(agentRunId, output.AgentRunId, "output", issues);

            if (string.IsNullOrWhiteSpace(output.OutputRefId) ||
                string.IsNullOrWhiteSpace(output.RefType) ||
                string.IsNullOrWhiteSpace(output.RefId))
            {
                AddError(issues, StepRequiredField, "OutputRefId, RefType, and RefId are required.");
            }

            if (output.ContainsRawPrivateReasoning ||
                AgentAuditTextSafety.ContainsRawPrivateReasoning([output.Summary, .. output.EvidenceRefs]))
            {
                AddError(issues, RawPrivateReasoningBlocked, "Output references cannot contain raw private reasoning.");
            }

            if (AgentAuditTextSafety.ContainsAuthorityClaim([output.Summary]) ||
                AgentAuditTextSafety.ContainsApprovalClaim([output.Summary]) ||
                AgentAuditTextSafety.ContainsMemoryPromotionClaim([output.Summary]))
            {
                AddError(issues, AuthorityClaimBlocked, "Output references cannot claim approval, promotion, or action authority.");
            }

            if (output.CreatesAuthority)
                AddError(issues, OutputAuthorityBlocked, "Output references cannot create authority.");

            if (output.CreatesRuntimeAction)
                AddError(issues, OutputRuntimeActionBlocked, "Output references cannot create runtime actions.");

            if (string.Equals(output.RefType, "CriticReviewResult", StringComparison.OrdinalIgnoreCase) &&
                !output.IsReviewOnly)
            {
                AddError(issues, CriticOutputMustBeReviewOnly, "CriticReviewResult outputs must be review-only.");
            }

            if (string.Equals(output.RefType, "MemoryImprovementProposalDraft", StringComparison.OrdinalIgnoreCase) &&
                !output.IsProposalOnly)
            {
                AddError(issues, MemoryProposalMustBeProposalOnly, "MemoryImprovementProposalDraft outputs must be proposal-only.");
            }
        }
    }

    private static void ValidateSteps(
        string agentRunId,
        IReadOnlyList<AgentRunStep> steps,
        List<AgentDefinitionValidationIssue> issues)
    {
        var sequences = new HashSet<int>();

        foreach (var step in steps)
        {
            ValidateChildRunId(agentRunId, step.AgentRunId, "step", issues);

            if (string.IsNullOrWhiteSpace(step.StepId) || string.IsNullOrWhiteSpace(step.Summary))
                AddError(issues, StepRequiredField, "StepId and Summary are required.");

            if (step.Sequence <= 0 || !Enum.IsDefined(step.StepType))
                AddError(issues, StepInvalid, "Step sequence and type must be valid.");

            if (step.Sequence > 0 && !sequences.Add(step.Sequence))
                AddError(issues, DuplicateStepSequence, $"Step sequence '{step.Sequence}' is duplicated.");

            if (step.ContainsRawPrivateReasoning ||
                AgentAuditTextSafety.ContainsRawPrivateReasoning([step.Summary, .. step.EvidenceRefs]))
            {
                AddError(issues, RawPrivateReasoningBlocked, "Run steps cannot contain raw private reasoning.");
            }

            if (AgentAuditTextSafety.ContainsAuthorityClaim([step.Summary]) ||
                AgentAuditTextSafety.ContainsApprovalClaim([step.Summary]) ||
                AgentAuditTextSafety.ContainsMemoryPromotionClaim([step.Summary]))
            {
                AddError(issues, AuthorityClaimBlocked, "Run step summaries cannot claim approval, promotion, or authority.");
            }
        }
    }

    private static void ValidateCapabilityUses(
        string agentRunId,
        AgentDefinition definition,
        IReadOnlyList<AgentCapabilityUseRecord> uses,
        List<AgentDefinitionValidationIssue> issues)
    {
        var declared = definition.Capabilities ?? new HashSet<AgentCapability>();
        var forbidden = definition.ForbiddenCapabilities ?? new HashSet<AgentCapability>();

        foreach (var use in uses)
        {
            ValidateChildRunId(agentRunId, use.AgentRunId, "capability use", issues);

            if (string.IsNullOrWhiteSpace(use.CapabilityUseId))
                AddError(issues, CapabilityUseRequiredField, "CapabilityUseId is required.");

            if (!Enum.IsDefined(use.Capability) || !Enum.IsDefined(use.Outcome))
                AddError(issues, CapabilityUseInvalid, "Capability use has invalid enum values.");

            var isDeclared = declared.Contains(use.Capability);
            var isForbidden = forbidden.Contains(use.Capability);

            if (use.WasDeclaredOnAgent != isDeclared || use.WasForbiddenOnAgent != isForbidden)
            {
                AddError(issues, CapabilitySnapshotMismatch, $"Capability snapshot flags do not match AgentDefinitionSnapshot for '{use.Capability}'.");
            }

            if (isForbidden && use.Outcome != AgentCapabilityUseOutcome.Blocked)
            {
                AddError(issues, ForbiddenCapabilityNotBlocked, $"Forbidden capability '{use.Capability}' must be blocked.");
            }

            if (!isDeclared && use.Outcome == AgentCapabilityUseOutcome.Allowed)
            {
                AddError(issues, UndeclaredCapabilityAllowed, $"Undeclared capability '{use.Capability}' cannot be allowed.");
            }

            if (DangerousExecutionCapabilities.Contains(use.Capability) &&
                use.Outcome == AgentCapabilityUseOutcome.Allowed)
            {
                AddError(issues, DangerousCapabilityAllowed, $"Dangerous capability '{use.Capability}' cannot be allowed by an audit envelope.");
            }
        }
    }

    private static void ValidateBoundaryDecisions(
        string agentRunId,
        IReadOnlyList<AgentBoundaryDecision> decisions,
        List<AgentDefinitionValidationIssue> issues)
    {
        foreach (var decision in decisions)
        {
            ValidateChildRunId(agentRunId, decision.AgentRunId, "boundary decision", issues);

            if (string.IsNullOrWhiteSpace(decision.BoundaryDecisionId) ||
                string.IsNullOrWhiteSpace(decision.Decision) ||
                string.IsNullOrWhiteSpace(decision.Reason))
            {
                AddError(issues, BoundaryDecisionRequiredField, "BoundaryDecisionId, Decision, and Reason are required.");
            }

            if (!Enum.IsDefined(decision.BoundaryType))
                AddError(issues, BoundaryDecisionInvalid, "Boundary decision type is invalid.");

            if (AgentAuditTextSafety.ContainsRawPrivateReasoning([decision.Decision, decision.Reason, .. decision.EvidenceRefs]))
                AddError(issues, RawPrivateReasoningBlocked, "Boundary decisions cannot contain raw private reasoning.");

            if (decision.GrantsMemoryPromotion)
                AddError(issues, BoundaryMemoryPromotionBlocked, "Boundary decisions cannot grant memory promotion.");

            if (decision.GrantsHumanApproval && decision.BoundaryType != AgentBoundaryDecisionType.HumanApproval)
                AddError(issues, BoundaryApprovalBlocked, "Only explicit human approval boundary decisions can carry human approval.");

            if (decision.GrantsPolicyApproval && decision.BoundaryType != AgentBoundaryDecisionType.Policy)
                AddError(issues, BoundaryPolicyApprovalBlocked, "Only explicit policy boundary decisions can carry policy approval.");

            if (decision.GrantsAuthority &&
                decision.BoundaryType is not AgentBoundaryDecisionType.HumanApproval and
                    not AgentBoundaryDecisionType.Policy and
                    not AgentBoundaryDecisionType.GovernanceDecision)
            {
                AddError(issues, BoundaryAuthorityBlocked, "Boundary decisions cannot grant authority unless they are explicit approval, policy, or governance records.");
            }
        }
    }

    private static void ValidateThoughtLedger(
        string agentRunId,
        IReadOnlyList<ThoughtLedgerEntry> entries,
        List<AgentDefinitionValidationIssue> issues)
    {
        var validator = new ThoughtLedgerSafetyValidator();

        foreach (var entry in entries)
        {
            ValidateChildRunId(agentRunId, entry.AgentRunId, "thought ledger entry", issues);
            issues.AddRange(validator.Validate(entry));
        }
    }

    private static void ValidateChildRunId(
        string expectedAgentRunId,
        string actualAgentRunId,
        string childName,
        List<AgentDefinitionValidationIssue> issues)
    {
        if (!string.Equals(expectedAgentRunId, actualAgentRunId, StringComparison.Ordinal))
        {
            AddError(issues, ChildRunIdMismatch, $"{childName} AgentRunId must match the parent run.");
        }
    }

    private static void AddError(List<AgentDefinitionValidationIssue> issues, string code, string message) =>
        issues.Add(new AgentDefinitionValidationIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message
        });
}
