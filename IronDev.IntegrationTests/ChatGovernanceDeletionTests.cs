using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ChatGovernanceDeletionTests
{
    [TestMethod]
    public void OldModeAuthoritySymbols_AreDeletedFromTrackedSource()
    {
        var root = FindRepoRoot();

        Assert.IsFalse(
            File.Exists(Path.Combine(root, "IronDev.Infrastructure", "Services", "ChatModeClassifierService.cs")),
            "ChatModeClassifierService must stay deleted. LlmChatModeClassifier is the single mode owner.");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Api", "Controllers", "ChatController.cs"),
            "TryResolveExplicitConversationMode");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Api", "Controllers", "ChatController.cs"),
            "save this");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Api", "Controllers", "ChatController.cs"),
            "save discussion");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Core", "Models", "ContextAgentModels.cs"),
            "get => ContextModeHint");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "LlmChatModeClassifier.cs"),
            "Context retrieval requires clarification before governance actions can be exposed.");

        Assert.IsFalse(
            File.Exists(Path.Combine(root, "IronDev.Core", "Interfaces", "IChatClarificationMapper.cs")),
            "IChatClarificationMapper must stay deleted. LlmChatClarificationClassifier owns clarification.");

        Assert.IsFalse(
            File.Exists(Path.Combine(root, "IronDev.Infrastructure", "Services", "ChatClarificationMapper.cs")),
            "ChatClarificationMapper must stay deleted. Slice 2 uses active clarification classification.");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ChatTurnPersistenceService.cs"),
            "EnsureTablesAsync");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ChatTurnPersistenceService.cs"),
            "CREATE TABLE dbo.ChatTurn");

        AssertFileContains(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ChatHistoryService.cs"),
            "BeginTransaction");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseService.cs"),
            "governance ceremony");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseService.cs"),
            "commitment lane");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ContextAgentRouteJudgeService.cs"),
            "ContextModeHint = \"Formalization\"");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ContextAgentRouteJudgeService.cs"),
            "intent == \"CreateTicket\"");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ContextAgentRouteJudgeService.cs"),
            "HasExplicitFormalizationIntent");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ContextAgentRouteJudgeService.cs"),
            "save this discussion");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ConversationContextResolver.cs"),
            "BookSeller");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ConversationContextResolver.cs"),
            "bookservice");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ConversationContextResolver.cs"),
            "book.cs");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatContextPanel.tsx"),
            "mode === 'Formalization'");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatMessage.tsx"),
            "mode === 'Formalization'");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatMessage.tsx"),
            "message.content.includes");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatMessage.tsx"),
            "<details open");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatSuggestedActions.tsx"),
            "mode === 'Formalization'");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "useProjectChat.ts"),
            "content.includes(");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "useProjectChat.ts"),
            "message.includes(");
    }

    private static void AssertFileDoesNotContain(string path, string forbidden)
    {
        Assert.IsTrue(File.Exists(path), $"Expected source file missing: {path}");
        var text = File.ReadAllText(path);
        Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal), $"Forbidden symbol remains in {path}: {forbidden}");
    }

    private static void AssertFileContains(string path, string required)
    {
        Assert.IsTrue(File.Exists(path), $"Expected source file missing: {path}");
        var text = File.ReadAllText(path);
        Assert.IsTrue(text.Contains(required, StringComparison.Ordinal), $"Required symbol missing in {path}: {required}");
    }

    private static string FindRepoRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("IRONDEV_REPO_ROOT"),
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var current = new DirectoryInfo(candidate!);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory or current working directory.");
    }
}
