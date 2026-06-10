using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentDefinitionBoundaryTests
{
    private static readonly AgentDefinitionValidator Validator = new();

    [TestMethod]
    public void AgentDefinitionContracts_Exist()
    {
        Assert.AreEqual(nameof(AgentDefinition), typeof(AgentDefinition).Name);
        Assert.AreEqual(nameof(AgentKind), typeof(AgentKind).Name);
        Assert.AreEqual(nameof(AgentExecutionMode), typeof(AgentExecutionMode).Name);
        Assert.AreEqual(nameof(AgentCapability), typeof(AgentCapability).Name);
        Assert.AreEqual(nameof(AgentPersona), typeof(AgentPersona).Name);
        Assert.AreEqual(nameof(AgentDefinitionValidationIssue), typeof(AgentDefinitionValidationIssue).Name);
        Assert.AreEqual(nameof(IAgentDefinitionValidator), typeof(IAgentDefinitionValidator).Name);
        Assert.AreEqual(nameof(AgentDefinitionValidator), typeof(AgentDefinitionValidator).Name);
        Assert.AreEqual(nameof(AgentDefinitionCatalog), typeof(AgentDefinitionCatalog).Name);
    }

    [TestMethod]
    public void AgentDefinitionCatalog_ContainsValidDefinitions()
    {
        Assert.IsTrue(AgentDefinitionCatalog.All.Count >= 6);

        foreach (var definition in AgentDefinitionCatalog.All)
            AssertNoIssues(Validator.Validate(definition), definition.AgentId);
    }

    [TestMethod]
    public void AgentDefinition_MissingRequiredFields_Fail()
    {
        AssertHasIssue(BuildDefinition(agentId: string.Empty), AgentDefinitionValidator.AgentIdRequired);
        AssertHasIssue(BuildDefinition(kind: (AgentKind)999), AgentDefinitionValidator.AgentKindInvalid);
        AssertHasIssue(BuildDefinition(mode: (AgentExecutionMode)999), AgentDefinitionValidator.ExecutionModeInvalid);
        AssertHasIssue(BuildDefinition(useDefaultPersona: false), AgentDefinitionValidator.PersonaRequired);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(personaId: string.Empty)), AgentDefinitionValidator.PersonaIdRequired);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(displayName: string.Empty)), AgentDefinitionValidator.PersonaDisplayNameRequired);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(voice: string.Empty)), AgentDefinitionValidator.PersonaVoiceRequired);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(style: string.Empty)), AgentDefinitionValidator.PersonaCommunicationStyleRequired);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(tone: string.Empty)), AgentDefinitionValidator.PersonaDefaultToneRequired);
        AssertHasIssue(BuildDefinition(useDefaultCapabilities: false), AgentDefinitionValidator.CapabilitiesRequired);
        AssertHasIssue(BuildDefinition(useDefaultForbiddenCapabilities: false), AgentDefinitionValidator.ForbiddenCapabilitiesRequired);
        AssertHasIssue(
            BuildDefinition(
                capabilities: Set(AgentCapability.CreateReport),
                forbiddenCapabilities: Set(AgentCapability.CreateReport)),
            AgentDefinitionValidator.CapabilityAllowedAndForbidden);
    }

    [TestMethod]
    public void Persona_CannotGrantCapabilityApprovalPromotionHumanAuthorityOrBypassGovernance()
    {
        AssertHasIssue(BuildDefinition(persona: BuildPersona(voice: "I can run tools whenever needed.")), AgentDefinitionValidator.PersonaAuthorityClaim);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(voice: "I approve this.")), AgentDefinitionValidator.PersonaAuthorityClaim);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(voice: "I promoted this memory.")), AgentDefinitionValidator.PersonaAuthorityClaim);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(style: "human approval granted")), AgentDefinitionValidator.PersonaAuthorityClaim);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(style: "can bypass governance")), AgentDefinitionValidator.PersonaAuthorityClaim);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(tone: "I can override policy")), AgentDefinitionValidator.PersonaAuthorityClaim);
        AssertHasIssue(BuildDefinition(persona: BuildPersona(mustSay: ["This is authoritative for action."])), AgentDefinitionValidator.PersonaAuthorityClaim);
        AssertHasIssue(BuildDefinition(description: "Trusted automatically by the system."), AgentDefinitionValidator.PersonaAuthorityClaim);
    }

    [TestMethod]
    public void Persona_CannotOverrideForbiddenCapability()
    {
        var definition = BuildDefinition(
            persona: BuildPersona(voice: "I can mutate source because I am careful."),
            capabilities: Set(AgentCapability.CreateReport),
            forbiddenCapabilities: Set(AgentCapability.MutateSource));

        var issues = Validator.Validate(definition);

        AssertHasIssue(issues, AgentDefinitionValidator.PersonaAuthorityClaim);
        Assert.IsTrue(definition.ForbiddenCapabilities!.Contains(AgentCapability.MutateSource));
    }

    [TestMethod]
    public void ForbiddenCapabilities_AlwaysWinAgainstAllowedKindAndMode()
    {
        var definition = BuildDefinition(
            kind: AgentKind.ImplementationAgent,
            mode: AgentExecutionMode.SourceMutation,
            capabilities: Set(AgentCapability.RunTool, AgentCapability.MutateSource),
            forbiddenCapabilities: Set(AgentCapability.MutateSource));

        AssertHasIssue(definition, AgentDefinitionValidator.CapabilityAllowedAndForbidden);
    }

    [TestMethod]
    public void ProposalOnly_CannotExecuteMutateExternalPromoteApproveOrBlock()
    {
        foreach (var capability in new[]
        {
            AgentCapability.RunTool,
            AgentCapability.MutateSource,
            AgentCapability.CallExternalSystem,
            AgentCapability.PromoteCollectiveMemory,
            AgentCapability.RepresentHumanApproval,
            AgentCapability.BlockExecution
        })
        {
            AssertHasIssue(
                BuildDefinition(
                    kind: AgentKind.ProposalAgent,
                    mode: AgentExecutionMode.ProposalOnly,
                    capabilities: Set(capability)),
                AgentDefinitionValidator.ExecutionModeCapabilityConflict);
        }
    }

    [TestMethod]
    public void OutOfBandReviewOnly_CannotExecuteMutatePromoteOrApprove()
    {
        foreach (var capability in new[]
        {
            AgentCapability.RunTool,
            AgentCapability.MutateSource,
            AgentCapability.PromoteCollectiveMemory,
            AgentCapability.RepresentHumanApproval
        })
        {
            AssertHasIssue(
                BuildDefinition(
                    kind: AgentKind.ReviewAgent,
                    mode: AgentExecutionMode.OutOfBandReviewOnly,
                    capabilities: Set(capability)),
                AgentDefinitionValidator.ExecutionModeCapabilityConflict);
        }
    }

    [TestMethod]
    public void GovernanceCheckOnly_CanBlockButCannotPerformGovernedAction()
    {
        AssertNoIssues(Validator.Validate(BuildDefinition(
            kind: AgentKind.GovernanceAgent,
            mode: AgentExecutionMode.GovernanceCheckOnly,
            capabilities: Set(AgentCapability.BlockExecution, AgentCapability.WarnExecution))));

        foreach (var capability in new[]
        {
            AgentCapability.RunTool,
            AgentCapability.MutateSource,
            AgentCapability.CallExternalSystem,
            AgentCapability.PromoteCollectiveMemory
        })
        {
            AssertHasIssue(
                BuildDefinition(
                    kind: AgentKind.GovernanceAgent,
                    mode: AgentExecutionMode.GovernanceCheckOnly,
                    capabilities: Set(capability)),
                AgentDefinitionValidator.ExecutionModeCapabilityConflict);
        }
    }

    [TestMethod]
    public void RetrievalOnly_CannotBlockPromoteApproveOrDeclareActionAuthority()
    {
        foreach (var capability in new[]
        {
            AgentCapability.BlockExecution,
            AgentCapability.PromoteCollectiveMemory,
            AgentCapability.RepresentHumanApproval
        })
        {
            AssertHasIssue(
                BuildDefinition(
                    kind: AgentKind.RetrievalAgent,
                    mode: AgentExecutionMode.RetrievalOnly,
                    capabilities: Set(capability)),
                AgentDefinitionValidator.ExecutionModeCapabilityConflict);
        }
    }

    [TestMethod]
    public void HumanAuthorityProxy_RequiresHumanProxyKindAndCannotExecute()
    {
        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.GovernanceAgent,
                mode: AgentExecutionMode.HumanAuthorityProxy,
                capabilities: Set(AgentCapability.RepresentHumanApproval)),
            AgentDefinitionValidator.HumanAuthorityProxyKindRequired);

        foreach (var capability in new[]
        {
            AgentCapability.RunTool,
            AgentCapability.MutateSource,
            AgentCapability.PromoteCollectiveMemory
        })
        {
            AssertHasIssue(
                BuildDefinition(
                    kind: AgentKind.HumanProxyAgent,
                    mode: AgentExecutionMode.HumanAuthorityProxy,
                    capabilities: Set(AgentCapability.RepresentHumanApproval, capability)),
                AgentDefinitionValidator.ExecutionModeCapabilityConflict);
        }
    }

    [TestMethod]
    public void KindCompatibility_BlocksUnsafeAgentShapes()
    {
        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.ImplementationAgent,
                mode: AgentExecutionMode.SourceMutation,
                capabilities: Set(AgentCapability.PromoteCollectiveMemory)),
            AgentDefinitionValidator.KindCapabilityConflict);

        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.TestingAgent,
                mode: AgentExecutionMode.ToolExecution,
                capabilities: Set(AgentCapability.MutateSource)),
            AgentDefinitionValidator.KindCapabilityConflict);

        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.ReviewAgent,
                mode: AgentExecutionMode.OutOfBandReviewOnly,
                capabilities: Set(AgentCapability.RunTool)),
            AgentDefinitionValidator.KindCapabilityConflict);

        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.ProposalAgent,
                mode: AgentExecutionMode.ProposalOnly,
                capabilities: Set(AgentCapability.BlockExecution)),
            AgentDefinitionValidator.KindCapabilityConflict);

        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.ReportingAgent,
                mode: AgentExecutionMode.ReportingOnly,
                capabilities: Set(AgentCapability.BlockExecution)),
            AgentDefinitionValidator.KindCapabilityConflict);

        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.HumanProxyAgent,
                mode: AgentExecutionMode.HumanAuthorityProxy,
                capabilities: Set(AgentCapability.CreateReport)),
            AgentDefinitionValidator.HumanProxyCapabilityRequired);
    }

    [TestMethod]
    public void KindCompatibility_BlocksIncompatibleMode()
    {
        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.RetrievalAgent,
                mode: AgentExecutionMode.ToolExecution,
                capabilities: Set(AgentCapability.RetrieveCollectiveMemory)),
            AgentDefinitionValidator.KindExecutionModeConflict);
    }

    [TestMethod]
    public void DangerousCapabilities_RequireDangerousExecutionModes()
    {
        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.ImplementationAgent,
                mode: AgentExecutionMode.ToolExecution,
                capabilities: Set(AgentCapability.RunTool, AgentCapability.MutateSource)),
            AgentDefinitionValidator.SourceMutationModeRequired);

        AssertHasIssue(
            BuildDefinition(
                kind: AgentKind.ImplementationAgent,
                mode: AgentExecutionMode.ToolExecution,
                capabilities: Set(AgentCapability.RunTool, AgentCapability.CallExternalSystem)),
            AgentDefinitionValidator.ExternalEffectModeRequired);
    }

    [TestMethod]
    public void AgentDefinitionBoundary_DoesNotAddRuntimeEngineSqlWeaviateOrConcreteFutureAgents()
    {
        var repositoryRoot = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "AgentModels.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "IAgentDefinitionValidator.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "AgentDefinitionValidator.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "AgentDefinitionCatalog.cs")
        };

        var forbiddenTokens = new[]
        {
            "IAgentRuntime",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentToolRouter",
            "ExecuteAgentAsync",
            "RunAgentAsync",
            "MemoryImprovementAgent",
            "IndependentCriticAgent",
            "WeaviateAgent",
            "AgentPromptRunner",
            "ICollectiveMemoryRetrievalService",
            "ICollectiveMemoryPromotionService",
            "SqlConnection",
            "CREATE PROCEDURE",
            "CREATE TABLE"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Agent definition boundary file contains forbidden runtime token '{token}': {file}");
            }
        }
    }

    private static AgentDefinition BuildDefinition(
        string agentId = "agent.definition.test",
        AgentKind kind = AgentKind.ReportingAgent,
        AgentExecutionMode mode = AgentExecutionMode.ReportingOnly,
        AgentPersona? persona = null,
        IReadOnlySet<AgentCapability>? capabilities = default,
        IReadOnlySet<AgentCapability>? forbiddenCapabilities = default,
        string? description = "Definition-only test agent.",
        bool useDefaultPersona = true,
        bool useDefaultCapabilities = true,
        bool useDefaultForbiddenCapabilities = true) =>
        new()
        {
            AgentId = agentId,
            Name = agentId,
            Kind = kind,
            ExecutionMode = mode,
            Purpose = "Definition boundary test.",
            Description = description,
            DefaultModelProfile = "definition-only",
            Persona = useDefaultPersona ? persona ?? BuildPersona() : persona,
            Capabilities = useDefaultCapabilities ? capabilities ?? Set(AgentCapability.CreateReport) : capabilities,
            ForbiddenCapabilities = useDefaultForbiddenCapabilities ? forbiddenCapabilities ?? Set() : forbiddenCapabilities
        };

    private static AgentPersona BuildPersona(
        string personaId = "persona.test",
        string displayName = "Definition Test Persona",
        string voice = "plain reporter",
        string style = "reports evidence and limitations",
        string tone = "careful",
        IReadOnlyList<string>? mustSay = null,
        IReadOnlyList<string>? mustNeverClaim = null) =>
        new()
        {
            PersonaId = personaId,
            DisplayName = displayName,
            Voice = voice,
            CommunicationStyle = style,
            DefaultTone = tone,
            MustSayWhenRelevant = mustSay ?? [],
            MustNeverClaim = mustNeverClaim ?? []
        };

    private static HashSet<AgentCapability> Set(params AgentCapability[] capabilities) => new(capabilities);

    private static void AssertHasIssue(AgentDefinition definition, string code) =>
        AssertHasIssue(Validator.Validate(definition), code);

    private static void AssertHasIssue(IReadOnlyList<AgentDefinitionValidationIssue> issues, string code)
    {
        Assert.IsTrue(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected validation issue '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoIssues(IReadOnlyList<AgentDefinitionValidationIssue> issues, string context = "")
    {
        Assert.AreEqual(
            0,
            issues.Count,
            $"{context} expected no validation issues but got: {string.Join(", ", issues.Select(issue => $"{issue.Code}:{issue.Message}"))}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Infrastructure")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
