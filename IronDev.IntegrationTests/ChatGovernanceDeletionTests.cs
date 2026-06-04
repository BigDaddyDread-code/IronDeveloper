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
            Path.Combine(root, "IronDev.Core", "Models", "ContextAgentModels.cs"),
            "get => ContextModeHint");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.Infrastructure", "Services", "LlmChatModeClassifier.cs"),
            "Context retrieval requires clarification before governance actions can be exposed.");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatContextPanel.tsx"),
            "mode === 'Formalization'");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatMessage.tsx"),
            "mode === 'Formalization'");

        AssertFileDoesNotContain(
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "chatToBuild", "ChatSuggestedActions.tsx"),
            "mode === 'Formalization'");
    }

    private static void AssertFileDoesNotContain(string path, string forbidden)
    {
        Assert.IsTrue(File.Exists(path), $"Expected source file missing: {path}");
        var text = File.ReadAllText(path);
        Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal), $"Forbidden symbol remains in {path}: {forbidden}");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
