using System.Reflection;
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
        var method = typeof(ProjectChatResponseService).GetMethod(
            "BuildExplorationClarificationResponse",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method, "Expected exploration clarification response helper to exist.");
        return (string)method.Invoke(null, [prompt, "IronDeveloper", clarification])!;
    }
}
