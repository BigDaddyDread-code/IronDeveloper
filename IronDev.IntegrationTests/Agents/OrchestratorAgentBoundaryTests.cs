using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
[TestCategory("Orchestrator")]
[TestCategory("Contract")]
[TestCategory("Governance")]
[TestCategory("Boundary")]
public sealed class OrchestratorAgentBoundaryTests
{
    private static readonly AgentDefinitionValidator Validator = new();

    [TestMethod]
    public void OrchestratorAgentKind_HasStableValueWithoutRenumberingExistingKinds()
    {
        Assert.AreEqual(1, (int)AgentKind.ImplementationAgent);
        Assert.AreEqual(2, (int)AgentKind.TestingAgent);
        Assert.AreEqual(3, (int)AgentKind.ReviewAgent);
        Assert.AreEqual(4, (int)AgentKind.GovernanceAgent);
        Assert.AreEqual(5, (int)AgentKind.ProposalAgent);
        Assert.AreEqual(6, (int)AgentKind.RetrievalAgent);
        Assert.AreEqual(7, (int)AgentKind.ReportingAgent);
        Assert.AreEqual(8, (int)AgentKind.HumanProxyAgent);
        Assert.AreEqual(9, (int)AgentKind.OrchestratorAgent);
    }

    [TestMethod]
    public void AgentDefinitionCatalog_ContainsProposalOnlyOrchestratorBa()
    {
        var definition = AgentDefinitionCatalog.OrchestratorAgent;

        Assert.AreEqual("builtin.orchestrator-ba", definition.AgentId);
        Assert.AreEqual("OrchestratorAgent", definition.Name);
        Assert.AreEqual(AgentKind.OrchestratorAgent, definition.Kind);
        Assert.AreEqual(AgentExecutionMode.ProposalOnly, definition.ExecutionMode);
        Assert.AreEqual("Orchestrator / BA", definition.Persona!.DisplayName);
        Assert.IsTrue(definition.Purpose.Contains("structured work contract", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(definition.Purpose.Contains("without approving", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(definition.Purpose.Contains("judging the result", StringComparison.OrdinalIgnoreCase));

        CollectionAssert.Contains(definition.Capabilities!.ToArray(), AgentCapability.CreateReport);
        CollectionAssert.Contains(definition.Capabilities!.ToArray(), AgentCapability.CreateHandoff);
        Assert.AreEqual(2, definition.Capabilities!.Count);

        AssertNoIssues(Validator.Validate(definition));
        CollectionAssert.Contains(AgentDefinitionCatalog.All.ToArray(), definition);
    }

    [TestMethod]
    public void OrchestratorAgent_ForbidsMutationApprovalPolicyCriticTestAndBlockingCapabilities()
    {
        var forbidden = AgentDefinitionCatalog.OrchestratorAgent.ForbiddenCapabilities!;

        foreach (var capability in new[]
        {
            AgentCapability.RunTool,
            AgentCapability.MutateSource,
            AgentCapability.CallExternalSystem,
            AgentCapability.PromoteCollectiveMemory,
            AgentCapability.RepresentHumanApproval,
            AgentCapability.RepresentHumanRejection,
            AgentCapability.RepresentHumanPromotionDecision,
            AgentCapability.BlockExecution,
            AgentCapability.CreateCriticFinding,
            AgentCapability.CreateTestReport
        })
        {
            CollectionAssert.Contains(forbidden.ToArray(), capability);
        }
    }

    [TestMethod]
    public void OrchestratorAgent_CanRecommendButCannotOwnBackendRefusal()
    {
        Assert.IsFalse(AgentDefinitionCatalog.OrchestratorAgent.Capabilities!.Contains(AgentCapability.BlockExecution));
        Assert.IsTrue(AgentDefinitionCatalog.OrchestratorAgent.Persona!.MustSayWhenRelevant.Any(
            value => value.Contains("does not judge the result", StringComparison.OrdinalIgnoreCase)));

        var source = AgentDefinitionCatalog.OrchestratorAgent;
        var invalid = new AgentDefinition
        {
            AgentId = source.AgentId,
            Name = source.Name,
            Kind = source.Kind,
            ExecutionMode = source.ExecutionMode,
            Purpose = source.Purpose,
            Description = source.Description,
            DefaultModelProfile = source.DefaultModelProfile,
            Persona = source.Persona,
            Capabilities = source.Capabilities!
                .Concat(new[] { AgentCapability.BlockExecution })
                .ToHashSet(),
            ForbiddenCapabilities = source.ForbiddenCapabilities,
            IsEnabled = source.IsEnabled,
            Enabled = source.Enabled
        };

        AssertHasIssue(Validator.Validate(invalid), AgentDefinitionValidator.ExecutionModeCapabilityConflict);
        AssertHasIssue(Validator.Validate(invalid), AgentDefinitionValidator.KindCapabilityConflict);
    }

    [TestMethod]
    public void OrchestratorAgent_DoesNotAddContractAuthoringExecutionMode()
    {
        var executionModeNames = Enum.GetNames<AgentExecutionMode>();

        CollectionAssert.DoesNotContain(executionModeNames, "ContractAuthoringOnly");
        Assert.AreEqual(AgentExecutionMode.ProposalOnly, AgentDefinitionCatalog.OrchestratorAgent.ExecutionMode);
    }

    [TestMethod]
    public void OrchestratorAgent_SourceFilesDoNotAddRuntimeExecutionSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "AgentModels.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "AgentDefinitionCatalog.cs"),
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "AgentDefinitionValidator.cs")
        };

        var forbiddenTokens = new[]
        {
            "ContractAuthoringOnly",
            "IOrchestratorRunner",
            "RunOrchestratorAsync",
            "ContinueWorkflowAsync",
            "ApplySourceAsync",
            "RecordApprovalAsync",
            "SatisfyPolicyAsync",
            "CreatePullRequestAsync",
            "PromoteMemoryAsync"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden runtime token '{token}' found in {file}.");
            }
        }
    }

    private static void AssertHasIssue(IReadOnlyList<AgentDefinitionValidationIssue> issues, string code)
    {
        Assert.IsTrue(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected validation issue '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoIssues(IReadOnlyList<AgentDefinitionValidationIssue> issues)
    {
        Assert.AreEqual(
            0,
            issues.Count,
            $"Expected no validation issues but got: {string.Join(", ", issues.Select(issue => $"{issue.Code}:{issue.Message}"))}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.IntegrationTests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
