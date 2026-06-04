using IronDev.Core.Chat;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ProjectChatResponseServiceTests
{
    [TestMethod]
    public void BuildExplorationClarificationResponse_UsesNaturalProductLanguage()
    {
        var response = InvokeExplorationClarificationResponse(
            "I want build monopoly game",
            new ChatClarificationState(
                true,
                ChatClarificationKind.ProductScope,
                ["What do you want to build first: board movement, player turns, buying properties, rent, cards, trading, or a simple UI prototype?"],
                "Product scope is missing."));

        StringAssert.Contains(response, "Nice.");
        StringAssert.Contains(response, "small playable slice");
        StringAssert.Contains(response, "board movement");
        Assert.IsFalse(response.Contains("Exploration", StringComparison.Ordinal), response);
        Assert.IsFalse(response.Contains("commitment lane", StringComparison.Ordinal), response);
        Assert.IsFalse(response.Contains("governance ceremony", StringComparison.Ordinal), response);
        Assert.IsFalse(response.Contains("save, ticket, or build", StringComparison.Ordinal), response);
        Assert.IsFalse(response.Contains("I can't safely answer", StringComparison.Ordinal), response);
    }

    private static string InvokeExplorationClarificationResponse(
        string prompt,
        ChatClarificationState clarification)
    {
        return ProjectChatResponseComposer.BuildExplorationClarificationResponse(
            prompt,
            "IronDeveloper",
            clarification);
    }

    [TestMethod]
    public void ProjectChatResponseService_RemainsGovernanceSpine_NotComposerOrContextPipeline()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseService.cs"));

        Assert.IsFalse(source.Contains("GetRecentTicketsAsync", StringComparison.Ordinal), "Context loading belongs in ProjectChatContextPipeline.");
        Assert.IsFalse(source.Contains("GetResponseAsync", StringComparison.Ordinal), "LLM response composition belongs in ProjectChatResponseComposer.");
        Assert.IsFalse(source.Contains("BuildReasoningTrace", StringComparison.Ordinal), "Trace formatting belongs in ProjectChatResponseMetadataBuilder.");
        StringAssert.Contains(source, "ChatGovernanceGate.FromDecision(modeDecision)");
        StringAssert.Contains(source, "_modeClassifier.ClassifyAsync");
        StringAssert.Contains(source, "_clarificationClassifier.ClassifyAsync");
    }

    [TestMethod]
    public void ProjectChatContextPipeline_NamesBroadAgentContextAndSummaryContextSeparately()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatContextPipeline.cs"));

        StringAssert.Contains(source, "contextAgentTickets");
        StringAssert.Contains(source, "contextAgentDecisions");
        StringAssert.Contains(source, "summaryTickets");
        StringAssert.Contains(source, "summaryDecisions");
        StringAssert.Contains(source, "RecentTickets = contextAgentTickets");
        StringAssert.Contains(source, "RecentDecisions = contextAgentDecisions");
    }

    [TestMethod]
    public void ProjectChatResponseComposer_ForbidsInternalGovernanceLeakage()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseComposer.cs"));

        StringAssert.Contains(source, "Do not mention governance modes");
        StringAssert.Contains(source, "classifier names");
        StringAssert.Contains(source, "route hints");
        StringAssert.Contains(source, "Translate the selected mode into natural user-facing language.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Directory.Exists(Path.Combine(directory, ".git")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        Assert.Fail("Could not locate repository root.");
        return string.Empty;
    }
}
