namespace IronDev.Core.Agents;

public sealed class AgentDefinitionValidator : IAgentDefinitionValidator
{
    public const string SeverityError = "Error";
    public const string SeverityWarning = "Warning";

    public const string AgentIdRequired = "AGENT_DEFINITION_AGENT_ID_REQUIRED";
    public const string AgentKindInvalid = "AGENT_DEFINITION_KIND_INVALID";
    public const string ExecutionModeInvalid = "AGENT_DEFINITION_EXECUTION_MODE_INVALID";
    public const string PersonaRequired = "AGENT_DEFINITION_PERSONA_REQUIRED";
    public const string PersonaIdRequired = "AGENT_DEFINITION_PERSONA_ID_REQUIRED";
    public const string PersonaDisplayNameRequired = "AGENT_DEFINITION_PERSONA_DISPLAY_NAME_REQUIRED";
    public const string PersonaVoiceRequired = "AGENT_DEFINITION_PERSONA_VOICE_REQUIRED";
    public const string PersonaCommunicationStyleRequired = "AGENT_DEFINITION_PERSONA_COMMUNICATION_STYLE_REQUIRED";
    public const string PersonaDefaultToneRequired = "AGENT_DEFINITION_PERSONA_DEFAULT_TONE_REQUIRED";
    public const string CapabilitiesRequired = "AGENT_DEFINITION_CAPABILITIES_REQUIRED";
    public const string ForbiddenCapabilitiesRequired = "AGENT_DEFINITION_FORBIDDEN_CAPABILITIES_REQUIRED";
    public const string CapabilityAllowedAndForbidden = "AGENT_DEFINITION_CAPABILITY_ALLOWED_AND_FORBIDDEN";
    public const string PersonaAuthorityClaim = "AGENT_DEFINITION_PERSONA_AUTHORITY_CLAIM";
    public const string ExecutionModeCapabilityConflict = "AGENT_DEFINITION_EXECUTION_MODE_CAPABILITY_CONFLICT";
    public const string KindExecutionModeConflict = "AGENT_DEFINITION_KIND_EXECUTION_MODE_CONFLICT";
    public const string KindCapabilityConflict = "AGENT_DEFINITION_KIND_CAPABILITY_CONFLICT";
    public const string HumanProxyCapabilityRequired = "AGENT_DEFINITION_HUMAN_PROXY_CAPABILITY_REQUIRED";
    public const string HumanAuthorityProxyKindRequired = "AGENT_DEFINITION_HUMAN_AUTHORITY_PROXY_KIND_REQUIRED";
    public const string SourceMutationModeRequired = "AGENT_DEFINITION_SOURCE_MUTATION_MODE_REQUIRED";
    public const string ExternalEffectModeRequired = "AGENT_DEFINITION_EXTERNAL_EFFECT_MODE_REQUIRED";

    private static readonly IReadOnlyList<string> ForbiddenPersonaPhrases =
    [
        "I approve",
        "I authorize",
        "I promoted",
        "I accepted",
        "human approved",
        "human approval granted",
        "bypass governance",
        "override policy",
        "execute without approval",
        "trusted automatically",
        "authoritative for action",
        "I can run tools",
        "I can mutate source",
        "I can promote memory",
        "I grant capability"
    ];

    private static readonly IReadOnlySet<AgentCapability> HumanRepresentationCapabilities = new HashSet<AgentCapability>
    {
        AgentCapability.RepresentHumanApproval,
        AgentCapability.RepresentHumanRejection,
        AgentCapability.RepresentHumanPromotionDecision
    };

    public IReadOnlyList<AgentDefinitionValidationIssue> Validate(AgentDefinition definition)
    {
        var issues = new List<AgentDefinitionValidationIssue>();

        if (string.IsNullOrWhiteSpace(definition.AgentId))
            AddError(issues, AgentIdRequired, "AgentId is required.");

        if (!Enum.IsDefined(definition.Kind))
            AddError(issues, AgentKindInvalid, "AgentKind is invalid.");

        if (!Enum.IsDefined(definition.ExecutionMode))
            AddError(issues, ExecutionModeInvalid, "AgentExecutionMode is invalid.");

        ValidatePersona(definition, issues);

        if (definition.Capabilities is null)
        {
            AddError(issues, CapabilitiesRequired, "Capabilities are required.");
        }

        if (definition.ForbiddenCapabilities is null)
        {
            AddError(issues, ForbiddenCapabilitiesRequired, "ForbiddenCapabilities are required.");
        }

        if (definition.Capabilities is null || definition.ForbiddenCapabilities is null)
            return issues;

        foreach (var capability in definition.Capabilities)
        {
            if (!Enum.IsDefined(capability))
                AddError(issues, ExecutionModeCapabilityConflict, $"Capability '{capability}' is invalid.");
        }

        foreach (var capability in definition.ForbiddenCapabilities)
        {
            if (!Enum.IsDefined(capability))
                AddError(issues, ExecutionModeCapabilityConflict, $"Forbidden capability '{capability}' is invalid.");
        }

        foreach (var capability in definition.Capabilities.Intersect(definition.ForbiddenCapabilities))
        {
            AddError(
                issues,
                CapabilityAllowedAndForbidden,
                $"Capability '{capability}' cannot be both allowed and forbidden. Forbidden capabilities always win.");
        }

        ValidateExecutionMode(definition, issues);
        ValidateKind(definition, issues);
        ValidateDangerousCapabilityModes(definition, issues);

        return issues;
    }

    private static void ValidatePersona(AgentDefinition definition, List<AgentDefinitionValidationIssue> issues)
    {
        if (definition.Persona is null)
        {
            AddError(issues, PersonaRequired, "Persona is required.");
            ValidateTextForAuthority("Description", definition.Description, issues);
            return;
        }

        if (string.IsNullOrWhiteSpace(definition.Persona.PersonaId))
            AddError(issues, PersonaIdRequired, "PersonaId is required.");

        if (string.IsNullOrWhiteSpace(definition.Persona.DisplayName))
            AddError(issues, PersonaDisplayNameRequired, "Persona DisplayName is required.");

        if (string.IsNullOrWhiteSpace(definition.Persona.Voice))
            AddError(issues, PersonaVoiceRequired, "Persona Voice is required.");

        if (string.IsNullOrWhiteSpace(definition.Persona.CommunicationStyle))
            AddError(issues, PersonaCommunicationStyleRequired, "Persona CommunicationStyle is required.");

        if (string.IsNullOrWhiteSpace(definition.Persona.DefaultTone))
            AddError(issues, PersonaDefaultToneRequired, "Persona DefaultTone is required.");

        ValidateTextForAuthority("Persona.DisplayName", definition.Persona.DisplayName, issues);
        ValidateTextForAuthority("Persona.Voice", definition.Persona.Voice, issues);
        ValidateTextForAuthority("Persona.CommunicationStyle", definition.Persona.CommunicationStyle, issues);
        ValidateTextForAuthority("Persona.DefaultTone", definition.Persona.DefaultTone, issues);
        ValidateTextForAuthority("Description", definition.Description, issues);

        foreach (var item in definition.Persona.MustSayWhenRelevant)
            ValidateTextForAuthority("Persona.MustSayWhenRelevant", item, issues);

        foreach (var item in definition.Persona.MustNeverClaim)
            ValidateTextForAuthority("Persona.MustNeverClaim", item, issues);
    }

    private static void ValidateTextForAuthority(string fieldName, string? value, List<AgentDefinitionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var phrase in ForbiddenPersonaPhrases)
        {
            if (!value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                continue;

            AddError(
                issues,
                PersonaAuthorityClaim,
                $"{fieldName} contains forbidden authority/capability phrase '{phrase}'. Persona is communication-only.");
        }
    }

    private static void ValidateExecutionMode(AgentDefinition definition, List<AgentDefinitionValidationIssue> issues)
    {
        var forbidden = definition.ExecutionMode switch
        {
            AgentExecutionMode.PassiveAnalysisOnly =>
                Set(
                    AgentCapability.CreateMemoryProposal,
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision,
                    AgentCapability.BlockExecution),

            AgentExecutionMode.ProposalOnly =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision,
                    AgentCapability.BlockExecution),

            AgentExecutionMode.OutOfBandReviewOnly =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision,
                    AgentCapability.BlockExecution),

            AgentExecutionMode.GovernanceCheckOnly =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision),

            AgentExecutionMode.RetrievalOnly =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision,
                    AgentCapability.BlockExecution),

            AgentExecutionMode.ReportingOnly =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision,
                    AgentCapability.BlockExecution),

            AgentExecutionMode.SourceMutation =>
                Set(
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision),

            AgentExecutionMode.ExternalEffect =>
                Set(
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision),

            AgentExecutionMode.HumanAuthorityProxy =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory),

            _ => new HashSet<AgentCapability>()
        };

        foreach (var capability in definition.Capabilities!.Where(forbidden.Contains))
        {
            AddError(
                issues,
                ExecutionModeCapabilityConflict,
                $"Execution mode '{definition.ExecutionMode}' cannot grant capability '{capability}'.");
        }

        if (definition.ExecutionMode == AgentExecutionMode.HumanAuthorityProxy &&
            definition.Kind != AgentKind.HumanProxyAgent)
        {
            AddError(
                issues,
                HumanAuthorityProxyKindRequired,
                "HumanAuthorityProxy mode requires AgentKind.HumanProxyAgent.");
        }
    }

    private static void ValidateKind(AgentDefinition definition, List<AgentDefinitionValidationIssue> issues)
    {
        var allowedModes = definition.Kind switch
        {
            AgentKind.ImplementationAgent => Set(
                AgentExecutionMode.ToolExecution,
                AgentExecutionMode.SourceMutation,
                AgentExecutionMode.PassiveAnalysisOnly),

            AgentKind.TestingAgent => Set(
                AgentExecutionMode.ToolExecution,
                AgentExecutionMode.ReportingOnly,
                AgentExecutionMode.PassiveAnalysisOnly),

            AgentKind.ReviewAgent => Set(
                AgentExecutionMode.OutOfBandReviewOnly,
                AgentExecutionMode.PassiveAnalysisOnly,
                AgentExecutionMode.ReportingOnly),

            AgentKind.GovernanceAgent => Set(
                AgentExecutionMode.GovernanceCheckOnly,
                AgentExecutionMode.PassiveAnalysisOnly),

            AgentKind.ProposalAgent => Set(
                AgentExecutionMode.ProposalOnly,
                AgentExecutionMode.PassiveAnalysisOnly),

            AgentKind.RetrievalAgent => Set(
                AgentExecutionMode.RetrievalOnly,
                AgentExecutionMode.PassiveAnalysisOnly),

            AgentKind.ReportingAgent => Set(
                AgentExecutionMode.ReportingOnly,
                AgentExecutionMode.PassiveAnalysisOnly),

            AgentKind.HumanProxyAgent => Set(AgentExecutionMode.HumanAuthorityProxy),

            AgentKind.OrchestratorAgent => Set(
                AgentExecutionMode.ProposalOnly,
                AgentExecutionMode.PassiveAnalysisOnly),

            _ => new HashSet<AgentExecutionMode>()
        };

        if (allowedModes.Count > 0 && !allowedModes.Contains(definition.ExecutionMode))
        {
            AddError(
                issues,
                KindExecutionModeConflict,
                $"Agent kind '{definition.Kind}' cannot use execution mode '{definition.ExecutionMode}'.");
        }

        var forbidden = definition.Kind switch
        {
            AgentKind.ImplementationAgent =>
                Set(
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision),

            AgentKind.TestingAgent =>
                Set(
                    AgentCapability.MutateSource,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval),

            AgentKind.ReviewAgent =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision,
                    AgentCapability.BlockExecution),

            AgentKind.GovernanceAgent =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval),

            AgentKind.ProposalAgent =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanPromotionDecision,
                    AgentCapability.BlockExecution),

            AgentKind.RetrievalAgent =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.BlockExecution),

            AgentKind.ReportingAgent =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.BlockExecution),

            AgentKind.HumanProxyAgent =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory),

            AgentKind.OrchestratorAgent =>
                Set(
                    AgentCapability.RunTool,
                    AgentCapability.MutateSource,
                    AgentCapability.CallExternalSystem,
                    AgentCapability.PromoteCollectiveMemory,
                    AgentCapability.RepresentHumanApproval,
                    AgentCapability.RepresentHumanRejection,
                    AgentCapability.RepresentHumanPromotionDecision,
                    AgentCapability.BlockExecution,
                    AgentCapability.CreateCriticFinding,
                    AgentCapability.CreateTestReport),

            _ => new HashSet<AgentCapability>()
        };

        foreach (var capability in definition.Capabilities!.Where(forbidden.Contains))
        {
            AddError(
                issues,
                KindCapabilityConflict,
                $"Agent kind '{definition.Kind}' cannot grant capability '{capability}'.");
        }

        if (definition.Kind == AgentKind.HumanProxyAgent &&
            !definition.Capabilities!.Any(HumanRepresentationCapabilities.Contains))
        {
            AddError(
                issues,
                HumanProxyCapabilityRequired,
                "HumanProxyAgent requires at least one explicit human representation capability.");
        }
    }

    private static void ValidateDangerousCapabilityModes(AgentDefinition definition, List<AgentDefinitionValidationIssue> issues)
    {
        if (definition.Capabilities!.Contains(AgentCapability.MutateSource) &&
            definition.ExecutionMode != AgentExecutionMode.SourceMutation)
        {
            AddError(
                issues,
                SourceMutationModeRequired,
                "MutateSource requires AgentExecutionMode.SourceMutation.");
        }

        if (definition.Capabilities!.Contains(AgentCapability.CallExternalSystem) &&
            definition.ExecutionMode != AgentExecutionMode.ExternalEffect)
        {
            AddError(
                issues,
                ExternalEffectModeRequired,
                "CallExternalSystem requires AgentExecutionMode.ExternalEffect.");
        }
    }

    private static HashSet<T> Set<T>(params T[] values) => new(values);

    private static void AddError(List<AgentDefinitionValidationIssue> issues, string code, string message) =>
        issues.Add(new AgentDefinitionValidationIssue
        {
            Code = code,
            Severity = SeverityError,
            Message = message
        });
}
