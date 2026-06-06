using System.Reflection;
using System.Runtime.CompilerServices;
using IronDev.Core.Chat;
using IronDev.Core.Models;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentMemoryContractTests
{
    private static readonly string[] ForbiddenGovernanceFields =
    [
        "SuggestedMode",
        "SuggestedAction",
        "ForceConfirmation",
        "RequiresGovernanceCommitment",
        "RecommendedGateState",
        "AutoCreateTicket",
        "AutoSaveDiscussion",
        "ShouldShow",
        "CanCreateTicket",
        "CanSaveDiscussion",
        "CanViewSources",
        "CanCopyMarkdown",
        "GovernanceActions",
        "ChatGovernanceMode",
        "ChatGovernanceGate"
    ];

    [TestMethod]
    public void AgentProposal_RequiresApprovalDefaultsTrue()
    {
        var proposal = new AgentProposal
        {
            Intent = "MemoryBackedSuggestion",
            Message = "Memory can be cited, but the user must approve any action.",
            EvidenceSourceIds = ["decision-123"]
        };

        Assert.IsTrue(proposal.RequiresApproval);
    }

    [TestMethod]
    public void AgentProposal_EvidenceSourceIdsAreCompileTimeRequired()
    {
        var property = typeof(AgentProposal).GetProperty(nameof(AgentProposal.EvidenceSourceIds));

        Assert.IsNotNull(property);
        Assert.IsTrue(property.GetCustomAttribute<RequiredMemberAttribute>() is not null,
            "AgentProposal.EvidenceSourceIds must be required so new proposals cannot omit evidence provenance.");
    }

    [TestMethod]
    public void AgentProposal_DoesNotExposeGovernanceSuggestionFields()
    {
        var propertyNames = typeof(AgentProposal)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbidden in ForbiddenGovernanceFields)
        {
            Assert.IsFalse(propertyNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"AgentProposal must not expose governance field '{forbidden}'.");
        }
    }

    [TestMethod]
    public void AgentResult_DoesNotExposeGovernanceModeOrGate()
    {
        var propertyNames = typeof(ContextAgentResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Any(name => name.Contains("ChatGovernanceMode", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(propertyNames.Any(name => name.Contains("ChatGovernanceGate", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(propertyNames.Any(name => name.Contains("RecommendedGateState", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void MemoryBackedAgentProposal_DoesNotRevealGovernanceActions()
    {
        var proposal = new AgentProposal
        {
            Intent = "MemoryBackedSuggestion",
            Message = "A remembered decision may be relevant evidence.",
            RecommendedNextActions = ["Ask for approval before drafting any artefact."],
            EvidenceSourceIds = ["decision-accepted-001"]
        };

        var gate = ChatGovernanceGate.FromDecision(new ChatModeDecision(
            ChatGovernanceMode.Exploration,
            0.88,
            "The user is exploring memory-backed context, not committing work."));

        Assert.IsTrue(proposal.RequiresApproval);
        Assert.AreEqual(1, proposal.EvidenceSourceIds.Count);
        Assert.IsFalse(gate.ShowGovernanceActions);
        Assert.IsFalse(gate.CanCreateTicket);
        Assert.AreEqual(0, gate.GovernanceActions.Count);
    }

    [TestMethod]
    public void AgentSource_CannotSetModeGateOrAutoCreateArtifacts()
    {
        var root = FindRepoRoot();
        var sourceFiles = new[]
        {
            Path.Combine(root, "IronDev.Core", "Models", "ContextAgentModels.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ContextAgentService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ContextAgentRouteJudgeService.cs")
        };

        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            var fileName = Path.GetFileName(sourceFile);

            Assert.IsFalse(source.Contains("new ChatModeDecision(", StringComparison.Ordinal),
                $"Agents must not construct ChatModeDecision directly: {fileName}");
            Assert.IsFalse(source.Contains("new ChatGovernanceGate(", StringComparison.Ordinal),
                $"Agents must not construct ChatGovernanceGate directly: {fileName}");
            Assert.IsFalse(source.Contains("ChatGovernanceGate.FromDecision", StringComparison.Ordinal),
                $"Agents must not derive gate state directly: {fileName}");
            Assert.IsFalse(source.Contains("SuggestedMode", StringComparison.Ordinal),
                $"Agents must not suggest governance mode: {fileName}");
            Assert.IsFalse(source.Contains("RecommendedGateState", StringComparison.Ordinal),
                $"Agents must not recommend gate state: {fileName}");
            Assert.IsFalse(source.Contains("AutoCreateTicket", StringComparison.Ordinal),
                $"Agents must not auto-create artefacts from memory: {fileName}");
            Assert.IsFalse(source.Contains("RequiresApproval = false", StringComparison.Ordinal),
                $"Agents must not lower proposal approval: {fileName}");
        }
    }

    private static string FindRepoRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("IRONDEV_REPO_ROOT"),
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var directory = new DirectoryInfo(candidate!);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate IronDev repository root.");
    }
}
