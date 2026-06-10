using System;
using System.Collections.Generic;
using System.Linq;

namespace IronDev.Core.Agents;

public enum AgentSpecialisationKind
{
    Unknown = 0,
    CriticalReview = 1,
    MemoryImprovementDetection = 2,
    EvidenceSynthesis = 3,
    ReportProjection = 4,
    PolicyInterpretation = 5,
    PlanAnalysis = 6
}

public sealed record AgentSpecialisationDefinition
{
    public string SpecialisationId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public AgentSpecialisationKind Kind { get; init; }

    public string AppliesToAgentId { get; init; } = string.Empty;

    public AgentKind RequiredAgentKind { get; init; }

    public AgentExecutionMode RequiredExecutionMode { get; init; }

    public IReadOnlyList<AgentCapability> RequiredCapabilities { get; init; } =
        Array.Empty<AgentCapability>();

    public IReadOnlyList<AgentCapability> ForbiddenCapabilities { get; init; } =
        Array.Empty<AgentCapability>();

    public IReadOnlyList<string> Purposes { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<AgentSpecialisationInputRequirement> InputRequirements { get; init; } =
        Array.Empty<AgentSpecialisationInputRequirement>();

    public IReadOnlyList<AgentSpecialisationOutputRequirement> OutputRequirements { get; init; } =
        Array.Empty<AgentSpecialisationOutputRequirement>();

    public IReadOnlyList<AgentSpecialisationEvidenceRequirement> EvidenceRequirements { get; init; } =
        Array.Empty<AgentSpecialisationEvidenceRequirement>();

    public IReadOnlyList<AgentSpecialisationValidationRequirement> ValidationRequirements { get; init; } =
        Array.Empty<AgentSpecialisationValidationRequirement>();

    public IReadOnlyList<AgentSpecialisationForbiddenBehaviour> ForbiddenBehaviours { get; init; } =
        Array.Empty<AgentSpecialisationForbiddenBehaviour>();

    public AgentSpecialisationAuthorityBoundary AuthorityBoundary { get; init; } =
        AgentSpecialisationAuthorityBoundary.None;
}

public sealed record AgentSpecialisationInputRequirement
{
    public string InputType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool Required { get; init; } = true;

    public IReadOnlyList<string> AllowedAuthorityReferenceTypes { get; init; } =
        Array.Empty<string>();
}

public sealed record AgentSpecialisationOutputRequirement
{
    public string OutputType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool RequiresHumanReview { get; init; } = true;

    public bool MustBeReviewOnly { get; init; }

    public bool MustBeProposalOnly { get; init; }

    public bool MayCreateAuthority { get; init; }

    public bool MayCreateRuntimeAction { get; init; }

    public bool MayPromoteMemory { get; init; }
}

public sealed record AgentSpecialisationEvidenceRequirement
{
    public string EvidenceType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool Required { get; init; } = true;

    public IReadOnlyList<string> AllowedAuthorityEvidenceTypes { get; init; } =
        Array.Empty<string>();
}

public sealed record AgentSpecialisationValidationRequirement
{
    public string ValidatorName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool Required { get; init; } = true;
}

public sealed record AgentSpecialisationForbiddenBehaviour
{
    public string Behaviour { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public bool Required { get; init; } = true;
}

public sealed record AgentSpecialisationAuthorityBoundary
{
    public static AgentSpecialisationAuthorityBoundary None { get; } = new();

    public bool CanGrantApproval { get; init; }

    public bool CanRepresentHumanDecision { get; init; }

    public bool CanOverridePolicy { get; init; }

    public bool CanExecuteTools { get; init; }

    public bool CanMutateSource { get; init; }

    public bool CanCallExternalSystems { get; init; }

    public bool CanPromoteMemory { get; init; }

    public bool CanCreateAuthority { get; init; }

    public bool CanCreateRuntimeAction { get; init; }

    public bool CanWriteMemory { get; init; }
}

public sealed record AgentSpecialisationCompatibilityResult(
    bool IsCompatible,
    IReadOnlyList<AgentDefinitionValidationIssue> Issues);

public sealed class AgentSpecialisationValidator
{
    public const string SpecialisationIdRequired = "AgentSpecialisation.SpecialisationId.Required";
    public const string SpecialisationIdUnsafe = "AgentSpecialisation.SpecialisationId.Unsafe";
    public const string NameRequired = "AgentSpecialisation.Name.Required";
    public const string KindInvalid = "AgentSpecialisation.Kind.Invalid";
    public const string DescriptionRequired = "AgentSpecialisation.Description.Required";
    public const string AppliesToAgentIdRequired = "AgentSpecialisation.AppliesToAgentId.Required";
    public const string RequiredAgentKindInvalid = "AgentSpecialisation.RequiredAgentKind.Invalid";
    public const string RequiredExecutionModeInvalid = "AgentSpecialisation.RequiredExecutionMode.Invalid";
    public const string AuthorityBoundaryRequired = "AgentSpecialisation.AuthorityBoundary.Required";
    public const string AuthorityBoundaryCannotGrantPower = "AgentSpecialisation.AuthorityBoundary.CannotGrantPower";
    public const string DangerousCapabilityRequired = "AgentSpecialisation.Capability.DangerousRequired";
    public const string DangerousCapabilityMustBeForbidden = "AgentSpecialisation.Capability.DangerousMustBeForbidden";
    public const string ForbiddenCapabilityOverride = "AgentSpecialisation.Capability.ForbiddenCannotBeRequired";
    public const string OutputRequirementRequired = "AgentSpecialisation.Output.Required";
    public const string OutputRequirementInvalid = "AgentSpecialisation.Output.Invalid";
    public const string OutputAuthorityBlocked = "AgentSpecialisation.Output.AuthorityBlocked";
    public const string OutputRuntimeActionBlocked = "AgentSpecialisation.Output.RuntimeActionBlocked";
    public const string OutputPromotionBlocked = "AgentSpecialisation.Output.PromotionBlocked";
    public const string OutputHumanReviewRequired = "AgentSpecialisation.Output.HumanReviewRequired";
    public const string InputRequirementInvalid = "AgentSpecialisation.Input.Invalid";
    public const string InputAuthorityConsumptionDeclared = "AgentSpecialisation.Input.AuthorityConsumptionDeclared";
    public const string EvidenceRequirementInvalid = "AgentSpecialisation.Evidence.Invalid";
    public const string EvidenceAuthorityConsumptionDeclared = "AgentSpecialisation.Evidence.AuthorityConsumptionDeclared";
    public const string ForbiddenBehaviourRequired = "AgentSpecialisation.ForbiddenBehaviour.Required";
    public const string ForbiddenBehaviourEmpty = "AgentSpecialisation.ForbiddenBehaviour.Empty";
    public const string ForbiddenBehaviourOptional = "AgentSpecialisation.ForbiddenBehaviour.Optional";
    public const string ValidationRequirementRequired = "AgentSpecialisation.Validation.Required";
    public const string ValidationRequirementUnsafe = "AgentSpecialisation.Validation.Unsafe";
    public const string RawPrivateReasoningBlocked = "AgentSpecialisation.Text.RawPrivateReasoningBlocked";
    public const string AuthorityClaimBlocked = "AgentSpecialisation.Text.AuthorityClaimBlocked";
    public const string CompatibilityAgentIdMismatch = "AgentSpecialisation.Compatibility.AgentIdMismatch";
    public const string CompatibilityKindMismatch = "AgentSpecialisation.Compatibility.KindMismatch";
    public const string CompatibilityExecutionModeMismatch = "AgentSpecialisation.Compatibility.ExecutionModeMismatch";
    public const string CompatibilityRequiredCapabilityMissing = "AgentSpecialisation.Compatibility.RequiredCapabilityMissing";
    public const string CompatibilityRequiredCapabilityForbidden = "AgentSpecialisation.Compatibility.RequiredCapabilityForbidden";
    public const string CompatibilityForbiddenCapabilityConflict = "AgentSpecialisation.Compatibility.ForbiddenCapabilityConflict";

    private static readonly AgentCapability[] DangerousRequiredCapabilities =
    {
        AgentCapability.RunTool,
        AgentCapability.MutateSource,
        AgentCapability.CallExternalSystem,
        AgentCapability.PromoteCollectiveMemory,
        AgentCapability.RepresentHumanApproval,
        AgentCapability.RepresentHumanPromotionDecision,
        AgentCapability.BlockExecution
    };

    private static readonly AgentCapability[] RequiredForbiddenCapabilities =
    {
        AgentCapability.RunTool,
        AgentCapability.MutateSource,
        AgentCapability.CallExternalSystem,
        AgentCapability.PromoteCollectiveMemory,
        AgentCapability.RepresentHumanApproval,
        AgentCapability.RepresentHumanPromotionDecision
    };

    private static readonly AgentCapability[] ProposalAndReviewForbiddenCapabilities =
    {
        AgentCapability.BlockExecution
    };

    private static readonly string[] RequiredForbiddenBehaviours =
    {
        "RunTool",
        "MutateSource",
        "CallExternalSystem",
        "PromoteCollectiveMemory",
        "RepresentHumanApproval",
        "RepresentHumanPromotionDecision",
        "OverridePolicy",
        "BypassGovernance",
        "CreateAuthority",
        "CreateRuntimeAction",
        "StoreRawPrompt",
        "StoreRawCompletion",
        "StoreChainOfThought",
        "StoreScratchpad",
        "StorePrivateReasoning"
    };

    private static readonly string[] RequiredValidationRequirements =
    {
        "AgentDefinitionValidator",
        "AgentRunAuditEnvelopeValidator",
        "ThoughtLedgerSafetyValidator"
    };

    private static readonly string[] UnsafeIdMarkers =
    {
        "approve",
        "approval",
        "promote",
        "promotion",
        "execute",
        "runtime",
        "mutate",
        "admin",
        "authority",
        "god",
        "root",
        "override",
        "bypass"
    };

    private static readonly string[] RawPrivateReasoningMarkers =
    {
        "raw prompt",
        "raw completion",
        "chain-of-thought",
        "scratchpad",
        "private reasoning",
        "hidden reasoning",
        "hidden deliberation",
        "system prompt",
        "developer prompt"
    };

    private static readonly string[] AuthorityClaimMarkers =
    {
        "approval granted",
        "human approved",
        "approved for execution",
        "policy cleared",
        "authoritative for action",
        "grant authority",
        "override policy",
        "bypass governance",
        "promote memory",
        "accepted memory",
        "system rule"
    };

    private static readonly string[] AuthorityReferenceTypes =
    {
        "HumanApprovalEvidence",
        "GovernanceDecision",
        "PolicyDecision"
    };

    private static readonly string[] UnsafeValidationMarkers =
    {
        "Execution",
        "ToolRouter",
        "PromptRunner",
        "AuthorityGrant",
        "ApprovalGrant",
        "Promotion"
    };

    public IReadOnlyList<AgentDefinitionValidationIssue> Validate(AgentSpecialisationDefinition definition)
    {
        var issues = new List<AgentDefinitionValidationIssue>();

        if (string.IsNullOrWhiteSpace(definition.SpecialisationId))
        {
            AddError(issues, SpecialisationIdRequired, "Agent specialisation requires a stable identifier.");
        }
        else if (ContainsAny(definition.SpecialisationId, UnsafeIdMarkers))
        {
            AddError(issues, SpecialisationIdUnsafe, "Agent specialisation identifiers must not imply approval, execution, promotion, or authority.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            AddError(issues, NameRequired, "Agent specialisation requires a name.");
        }

        if (string.IsNullOrWhiteSpace(definition.Description))
        {
            AddError(issues, DescriptionRequired, "Agent specialisation requires a description.");
        }

        if (!Enum.IsDefined(typeof(AgentSpecialisationKind), definition.Kind) ||
            definition.Kind == AgentSpecialisationKind.Unknown)
        {
            AddError(issues, KindInvalid, "Agent specialisation kind must be a known non-authority kind.");
        }

        if (string.IsNullOrWhiteSpace(definition.AppliesToAgentId))
        {
            AddError(issues, AppliesToAgentIdRequired, "Agent specialisation must bind to one agent definition.");
        }

        if (!Enum.IsDefined(typeof(AgentKind), definition.RequiredAgentKind))
        {
            AddError(issues, RequiredAgentKindInvalid, "Agent specialisation must declare a valid required agent kind.");
        }

        if (!Enum.IsDefined(typeof(AgentExecutionMode), definition.RequiredExecutionMode))
        {
            AddError(issues, RequiredExecutionModeInvalid, "Agent specialisation must declare a valid required execution mode.");
        }

        if (definition.AuthorityBoundary is null)
        {
            AddError(issues, AuthorityBoundaryRequired, "Agent specialisation must declare an authority boundary.");
        }
        else
        {
            ValidateAuthorityBoundary(definition.AuthorityBoundary, issues);
        }

        ValidateRequiredCapabilities(definition, issues);
        ValidateForbiddenCapabilities(definition, issues);
        ValidateInputs(definition.InputRequirements, issues);
        ValidateOutputs(definition.OutputRequirements, issues);
        ValidateEvidence(definition.EvidenceRequirements, issues);
        ValidateValidationRequirements(definition.ValidationRequirements, issues);
        ValidateForbiddenBehaviours(definition.ForbiddenBehaviours, issues);
        ValidateTextSafety(definition, issues);

        return issues;
    }

    public AgentSpecialisationCompatibilityResult ValidateCompatibility(
        AgentDefinition agentDefinition,
        AgentSpecialisationDefinition specialisation)
    {
        var issues = new List<AgentDefinitionValidationIssue>(Validate(specialisation));
        var agentCapabilities = (agentDefinition.Capabilities ?? new HashSet<AgentCapability>()).ToHashSet();
        var agentForbiddenCapabilities = (agentDefinition.ForbiddenCapabilities ?? new HashSet<AgentCapability>()).ToHashSet();

        if (!string.Equals(agentDefinition.AgentId, specialisation.AppliesToAgentId, StringComparison.Ordinal))
        {
            AddError(issues, CompatibilityAgentIdMismatch, "Agent specialisation applies to a different agent definition.");
        }

        if (agentDefinition.Kind != specialisation.RequiredAgentKind)
        {
            AddError(issues, CompatibilityKindMismatch, "Agent specialisation required kind does not match the agent definition.");
        }

        if (agentDefinition.ExecutionMode != specialisation.RequiredExecutionMode)
        {
            AddError(issues, CompatibilityExecutionModeMismatch, "Agent specialisation required execution mode does not match the agent definition.");
        }

        foreach (var capability in specialisation.RequiredCapabilities.Distinct())
        {
            if (!agentCapabilities.Contains(capability))
            {
                AddError(issues, CompatibilityRequiredCapabilityMissing, $"Agent definition does not provide required capability {capability}.");
            }

            if (agentForbiddenCapabilities.Contains(capability))
            {
                AddError(issues, CompatibilityRequiredCapabilityForbidden, $"Agent definition forbids required capability {capability}.");
            }
        }

        foreach (var capability in specialisation.ForbiddenCapabilities.Distinct())
        {
            if (agentCapabilities.Contains(capability) &&
                !agentForbiddenCapabilities.Contains(capability))
            {
                AddError(issues, CompatibilityForbiddenCapabilityConflict, $"Agent definition does not preserve forbidden capability {capability}.");
            }
        }

        var isCompatible = issues.All(issue =>
            !string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.Ordinal));

        return new AgentSpecialisationCompatibilityResult(isCompatible, issues);
    }

    private static void ValidateAuthorityBoundary(
        AgentSpecialisationAuthorityBoundary boundary,
        ICollection<AgentDefinitionValidationIssue> issues)
    {
        if (boundary.CanGrantApproval ||
            boundary.CanRepresentHumanDecision ||
            boundary.CanOverridePolicy ||
            boundary.CanExecuteTools ||
            boundary.CanMutateSource ||
            boundary.CanCallExternalSystems ||
            boundary.CanPromoteMemory ||
            boundary.CanCreateAuthority ||
            boundary.CanCreateRuntimeAction ||
            boundary.CanWriteMemory)
        {
            AddError(issues, AuthorityBoundaryCannotGrantPower, "Agent specialisation authority boundary must not grant approval, execution, mutation, promotion, or memory authority.");
        }
    }

    private static void ValidateRequiredCapabilities(
        AgentSpecialisationDefinition definition,
        ICollection<AgentDefinitionValidationIssue> issues)
    {
        foreach (var capability in definition.RequiredCapabilities.Distinct())
        {
            if (DangerousRequiredCapabilities.Contains(capability))
            {
                AddError(issues, DangerousCapabilityRequired, $"Agent specialisation cannot require dangerous capability {capability}.");
            }

            if (definition.ForbiddenCapabilities.Contains(capability))
            {
                AddError(issues, ForbiddenCapabilityOverride, $"Agent specialisation cannot require and forbid capability {capability}.");
            }
        }
    }

    private static void ValidateForbiddenCapabilities(
        AgentSpecialisationDefinition definition,
        ICollection<AgentDefinitionValidationIssue> issues)
    {
        foreach (var capability in RequiredForbiddenCapabilities)
        {
            if (!definition.ForbiddenCapabilities.Contains(capability))
            {
                AddError(issues, DangerousCapabilityMustBeForbidden, $"Agent specialisation must explicitly forbid capability {capability}.");
            }
        }

        if (definition.RequiredAgentKind is AgentKind.ReviewAgent or AgentKind.ProposalAgent)
        {
            foreach (var capability in ProposalAndReviewForbiddenCapabilities)
            {
                if (!definition.ForbiddenCapabilities.Contains(capability))
                {
                    AddError(issues, DangerousCapabilityMustBeForbidden, $"Review and proposal specialisations must explicitly forbid capability {capability}.");
                }
            }
        }
    }

    private static void ValidateInputs(
        IEnumerable<AgentSpecialisationInputRequirement> requirements,
        ICollection<AgentDefinitionValidationIssue> issues)
    {
        foreach (var requirement in requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.InputType))
            {
                AddError(issues, InputRequirementInvalid, "Agent specialisation input requirements must declare an input type.");
            }

            foreach (var referenceType in requirement.AllowedAuthorityReferenceTypes)
            {
                if (AuthorityReferenceTypes.Contains(referenceType, StringComparer.OrdinalIgnoreCase))
                {
                    AddWarning(issues, InputAuthorityConsumptionDeclared, $"Input requirement consumes authority evidence type {referenceType}; this must remain evidence only.");
                }
            }
        }
    }

    private static void ValidateOutputs(
        IReadOnlyCollection<AgentSpecialisationOutputRequirement> requirements,
        ICollection<AgentDefinitionValidationIssue> issues)
    {
        if (requirements.Count == 0)
        {
            AddError(issues, OutputRequirementRequired, "Agent specialisation must declare at least one typed output requirement.");
        }

        foreach (var requirement in requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.OutputType))
            {
                AddError(issues, OutputRequirementInvalid, "Agent specialisation output requirements must declare an output type.");
            }

            if (!requirement.RequiresHumanReview)
            {
                AddError(issues, OutputHumanReviewRequired, $"Output requirement {requirement.OutputType} must require human review.");
            }

            if (requirement.MayCreateAuthority)
            {
                AddError(issues, OutputAuthorityBlocked, $"Output requirement {requirement.OutputType} must not create authority.");
            }

            if (requirement.MayCreateRuntimeAction)
            {
                AddError(issues, OutputRuntimeActionBlocked, $"Output requirement {requirement.OutputType} must not create runnable actions.");
            }

            if (requirement.MayPromoteMemory)
            {
                AddError(issues, OutputPromotionBlocked, $"Output requirement {requirement.OutputType} must not promote memory.");
            }

            if (string.Equals(requirement.OutputType, "CriticReviewResult", StringComparison.Ordinal) &&
                !requirement.MustBeReviewOnly)
            {
                AddError(issues, OutputRequirementInvalid, "CriticReviewResult outputs must be review-only.");
            }

            if ((string.Equals(requirement.OutputType, "MemoryImprovementDetectionResult", StringComparison.Ordinal) ||
                 string.Equals(requirement.OutputType, "MemoryImprovementProposalDraft", StringComparison.Ordinal)) &&
                !requirement.MustBeProposalOnly)
            {
                AddError(issues, OutputRequirementInvalid, "Memory improvement outputs must be proposal-only.");
            }
        }
    }

    private static void ValidateEvidence(
        IEnumerable<AgentSpecialisationEvidenceRequirement> requirements,
        ICollection<AgentDefinitionValidationIssue> issues)
    {
        foreach (var requirement in requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.EvidenceType))
            {
                AddError(issues, EvidenceRequirementInvalid, "Agent specialisation evidence requirements must declare an evidence type.");
            }

            foreach (var evidenceType in requirement.AllowedAuthorityEvidenceTypes)
            {
                if (AuthorityReferenceTypes.Contains(evidenceType, StringComparer.OrdinalIgnoreCase))
                {
                    AddWarning(issues, EvidenceAuthorityConsumptionDeclared, $"Evidence requirement consumes authority evidence type {evidenceType}; this must remain evidence only.");
                }
            }
        }
    }

    private static void ValidateValidationRequirements(
        IReadOnlyCollection<AgentSpecialisationValidationRequirement> requirements,
        ICollection<AgentDefinitionValidationIssue> issues)
    {
        if (requirements.Count == 0)
        {
            AddError(issues, ValidationRequirementRequired, "Agent specialisation must declare validation requirements.");
        }

        foreach (var requiredValidator in RequiredValidationRequirements)
        {
            if (!requirements.Any(requirement =>
                    string.Equals(requirement.ValidatorName, requiredValidator, StringComparison.Ordinal)))
            {
                AddError(issues, ValidationRequirementRequired, $"Agent specialisation must require {requiredValidator}.");
            }
        }

        foreach (var requirement in requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.ValidatorName))
            {
                AddError(issues, ValidationRequirementRequired, "Agent specialisation validation requirements must declare a validator name.");
            }
            else if (UnsafeValidationMarkers.Any(marker =>
                         requirement.ValidatorName.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                AddError(issues, ValidationRequirementUnsafe, $"Validation requirement {requirement.ValidatorName} implies execution or authority creation.");
            }
        }
    }

    private static void ValidateForbiddenBehaviours(
        IReadOnlyCollection<AgentSpecialisationForbiddenBehaviour> behaviours,
        ICollection<AgentDefinitionValidationIssue> issues)
    {
        foreach (var requiredBehaviour in RequiredForbiddenBehaviours)
        {
            if (!behaviours.Any(behaviour =>
                    string.Equals(behaviour.Behaviour, requiredBehaviour, StringComparison.Ordinal)))
            {
                AddError(issues, ForbiddenBehaviourRequired, $"Agent specialisation must forbid behaviour {requiredBehaviour}.");
            }
        }

        foreach (var behaviour in behaviours)
        {
            if (string.IsNullOrWhiteSpace(behaviour.Behaviour) ||
                string.IsNullOrWhiteSpace(behaviour.Reason))
            {
                AddError(issues, ForbiddenBehaviourEmpty, "Agent specialisation forbidden behaviours must include behaviour and reason.");
            }

            if (!behaviour.Required)
            {
                AddError(issues, ForbiddenBehaviourOptional, $"Forbidden behaviour {behaviour.Behaviour} cannot be optional.");
            }
        }
    }

    private static void ValidateTextSafety(
        AgentSpecialisationDefinition definition,
        ICollection<AgentDefinitionValidationIssue> issues)
    {
        foreach (var text in EnumerateTexts(definition))
        {
            if (ContainsAny(text, RawPrivateReasoningMarkers))
            {
                AddError(issues, RawPrivateReasoningBlocked, "Agent specialisation text must not request raw prompts, completions, scratchpads, or private reasoning.");
            }

            if (ContainsAny(text, AuthorityClaimMarkers))
            {
                AddError(issues, AuthorityClaimBlocked, "Agent specialisation text must not claim approval, policy, promotion, or system-rule authority.");
            }
        }
    }

    private static IEnumerable<string> EnumerateTexts(AgentSpecialisationDefinition definition)
    {
        yield return definition.Name;
        yield return definition.Description;

        foreach (var purpose in definition.Purposes)
        {
            yield return purpose;
        }

        foreach (var behaviour in definition.ForbiddenBehaviours)
        {
            yield return behaviour.Behaviour;
            yield return behaviour.Reason;
        }
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        !string.IsNullOrWhiteSpace(value) &&
        markers.Any(marker => ContainsMarker(value, marker));

    private static bool ContainsMarker(string value, string marker)
    {
        if (marker.Any(char.IsWhiteSpace) || marker.Contains('-', StringComparison.Ordinal))
        {
            return value.Contains(marker, StringComparison.OrdinalIgnoreCase);
        }

        var startIndex = 0;
        while (startIndex < value.Length)
        {
            var index = value.IndexOf(marker, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeIsBoundary = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
            var afterIndex = index + marker.Length;
            var afterIsBoundary = afterIndex >= value.Length || !char.IsLetterOrDigit(value[afterIndex]);

            if (beforeIsBoundary && afterIsBoundary)
            {
                return true;
            }

            startIndex = index + marker.Length;
        }

        return false;
    }

    private static void AddError(
        ICollection<AgentDefinitionValidationIssue> issues,
        string code,
        string message) =>
        issues.Add(new AgentDefinitionValidationIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message
        });

    private static void AddWarning(
        ICollection<AgentDefinitionValidationIssue> issues,
        string code,
        string message) =>
        issues.Add(new AgentDefinitionValidationIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityWarning,
            Message = message
        });
}
