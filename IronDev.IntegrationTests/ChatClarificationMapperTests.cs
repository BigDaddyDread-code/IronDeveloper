using IronDev.Core.Chat;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ChatClarificationMapperTests
{
    [TestMethod]
    public void Map_NoClarificationQuestionsReturnsNone()
    {
        var mapper = new ChatClarificationMapper();

        var clarification = mapper.Map(
            new ChatContextState(true, [], "Context needs more detail."),
            "I want build a game");

        Assert.IsFalse(clarification.Required);
        Assert.AreEqual(ChatClarificationKind.None, clarification.Kind);
    }

    [TestMethod]
    public void Map_BroadProductIntentWithQuestionsReturnsProductScopeWithoutSampleNouns()
    {
        var mapper = new ChatClarificationMapper();

        var clarification = mapper.Map(
            new ChatContextState(true, ["What first slice should we shape?"], "Context needs product scope."),
            "I want an invoicing dashboard");

        Assert.IsTrue(clarification.Required);
        Assert.AreEqual(ChatClarificationKind.ProductScope, clarification.Kind);
        Assert.AreEqual("What first slice should we shape?", clarification.Questions.Single());
    }

    [TestMethod]
    public void Map_ClarificationQuestionsWithoutProductIntentReturnsGeneralScope()
    {
        var mapper = new ChatClarificationMapper();

        var clarification = mapper.Map(
            new ChatContextState(true, ["Which repository should I inspect?"], "Context needs a repository."),
            "Which one should we look at?");

        Assert.IsTrue(clarification.Required);
        Assert.AreEqual(ChatClarificationKind.GeneralScope, clarification.Kind);
    }
}
