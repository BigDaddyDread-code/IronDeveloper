using IronDev.Core.ChatProbe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.ChatProbe;

[TestClass]
public sealed class AdaptiveProbeLogicTests
{
    private readonly AdaptiveProbeLogic _adaptive = new();
    private readonly ProbeRunOptions _options     = new() { MaxAdaptiveProbes = 3 };

    private string? GetProbe(string userMessage, string response, IReadOnlyList<ProbeTurnResult>? prior = null) =>
        _adaptive.GetAdaptiveProbe(userMessage, response, prior ?? [], 0, _options);

    // ── Multiplayer over-build trigger ────────────────────────────────────────

    [TestMethod]
    public void WhenResponseSuggestsMultiplayer_AndUserSaidOnlineNotMultiplayer_InjectsCorrection()
    {
        var probe = GetProbe(
            userMessage: "I want to make solitaire online so people can play in the browser",
            response: "For an online multiplayer game you'll need a game server to synchronise moves between players.");

        Assert.IsNotNull(probe);
        StringAssert.Contains(probe, "single-player");
    }

    [TestMethod]
    public void WhenUserExplicitlySaidMultiplayer_MultiplayerResponseDoesNotTrigger()
    {
        var probe = GetProbe(
            userMessage: "I want to make a multiplayer game online",
            response: "For multiplayer you'll need a game server and WebSockets.");

        Assert.IsNull(probe);
    }

    // ── Generic feature question trigger ─────────────────────────────────────

    [TestMethod]
    public void WhenResponseAsksWhatFeaturesDoYouWant_InjectsYouRecommend()
    {
        var probe = GetProbe(
            userMessage: "tell me about the fishing game",
            response: "What features do you want to include in the fishing game?");

        Assert.IsNotNull(probe);
        StringAssert.Contains(probe, "recommend");
    }

    [TestMethod]
    public void WhenResponseAsksWhatFunctionalityDoYouNeed_InjectsYouRecommend()
    {
        var probe = GetProbe(
            userMessage: "I have an idea for a recipe app",
            response: "Great! What functionality do you need in the app?");

        Assert.IsNotNull(probe);
        StringAssert.Contains(probe, "recommend");
    }

    [TestMethod]
    public void WhenResponseDoesNotAskForFeatures_NoTrigger()
    {
        var probe = GetProbe(
            userMessage: "tell me about the fishing game",
            response: "I'd recommend starting with the fish difficulty curve as your first slice.");

        Assert.IsNull(probe);
    }

    // ── Save confirmation trigger ─────────────────────────────────────────────

    [TestMethod]
    public void WhenResponseAsksDoYouWantToSave_InjectsYes()
    {
        var probe = GetProbe(
            userMessage: "ok, sounds good",
            response: "Do you want to save this as a discussion document?");

        Assert.IsNotNull(probe);
        Assert.AreEqual("yes", probe);
    }

    [TestMethod]
    public void WhenResponseAsksShallISave_InjectsYes()
    {
        var probe = GetProbe(
            userMessage: "that sounds right",
            response: "Shall I save this discussion for you?");

        Assert.IsNotNull(probe);
        Assert.AreEqual("yes", probe);
    }

    // ── Asks about decisions already in context ───────────────────────────────

    [TestMethod]
    public void WhenResponseAsksWhatDecisionsHaveBeenMadeAndContextHasContent_InjectsReminder()
    {
        var prior = new[]
        {
            MakeTurn("I recommend SQLite for storage and a simple daily progression system to increase fish difficulty.")
        };

        var probe = GetProbe(
            userMessage: "create architecture doc",
            response: "What decisions have already been made? Could you summarise what we decided?",
            prior);

        Assert.IsNotNull(probe);
        Assert.IsTrue(probe.Contains("already told") || probe.Contains("discussed"));
    }

    [TestMethod]
    public void WhenResponseAsksDecisionsButNoContext_NoTrigger()
    {
        var probe = GetProbe(
            userMessage: "create architecture doc",
            response: "What decisions have already been made?",
            []);

        // First turn — no prior context — trigger requires at least 2 prior turns
        Assert.IsNull(probe);
    }

    // ── Framework over-ask trigger ────────────────────────────────────────────

    [TestMethod]
    public void WhenUserSaidRulesFirstAndResponseAsksAboutFramework_InjectsReminder()
    {
        var probe = GetProbe(
            userMessage: "rules first, lets do that",
            response: "What framework would you like to use for this project?");

        Assert.IsNotNull(probe);
        StringAssert.Contains(probe, "rules first");
    }

    // ── Recommendation redirect trigger ───────────────────────────────────────

    [TestMethod]
    public void WhenUserAsksForRecommendationAndResponseAsksThemToChoose_InjectsJustPickOne()
    {
        var probe = GetProbe(
            userMessage: "what do you recommend for the database",
            response: "It depends on your preference. Would you prefer SQLite or PostgreSQL?");

        Assert.IsNotNull(probe);
        StringAssert.Contains(probe, "pick");
    }

    [TestMethod]
    public void WhenUserAsksYouRecommendAndResponsePicks_NoTrigger()
    {
        var probe = GetProbe(
            userMessage: "you recommend",
            response: "I recommend SQLite for a local single-player game like this. It's simple, fast, and needs no server.");

        Assert.IsNull(probe);
    }

    // ── MaxAdaptiveProbes limit ───────────────────────────────────────────────

    [TestMethod]
    public void WhenMaxAdaptiveProbesReached_NoProbeReturned()
    {
        var options = new ProbeRunOptions { MaxAdaptiveProbes = 2 };
        var probe   = _adaptive.GetAdaptiveProbe(
            userMessage:          "tell me about the fishing game",
            assistantResponse:    "What features do you want to include?",
            previousTurns:        [],
            adaptiveProbesUsed:   2,  // already at limit
            options:              options);

        Assert.IsNull(probe);
    }

    [TestMethod]
    public void WhenUnderMaxAdaptiveProbes_TriggerStillFires()
    {
        var options = new ProbeRunOptions { MaxAdaptiveProbes = 3 };
        var probe   = _adaptive.GetAdaptiveProbe(
            userMessage:        "tell me about the fishing game",
            assistantResponse:  "What features do you want to include?",
            previousTurns:      [],
            adaptiveProbesUsed: 1,  // under limit
            options:            options);

        Assert.IsNotNull(probe);
    }

    // ── Non-triggering happy path ─────────────────────────────────────────────

    [TestMethod]
    public void WhenResponseIsCleanAndHelpful_NoProbeReturned()
    {
        var probe = GetProbe(
            userMessage: "what would the first slice be",
            response: "I'd recommend starting with the core fish difficulty mechanic. " +
                      "First, implement a simple daily counter that increases fish evasion probability. " +
                      "This gives you a testable game loop without UI complexity.");

        Assert.IsNull(probe);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProbeTurnResult MakeTurn(string response) =>
        new() { TurnNumber = 1, UserMessage = "user", AssistantResponse = response };
}
