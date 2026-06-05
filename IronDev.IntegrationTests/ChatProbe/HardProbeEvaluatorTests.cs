using IronDev.Core.ChatProbe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.ChatProbe;

[TestClass]
public sealed class HardProbeEvaluatorTests
{
    private readonly HardProbeEvaluator _evaluator = new();

    // ── WrongMode ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void WrongMode_WhenExpectedFormalizationAndGotExploration_ReturnsHardFailure()
    {
        var step = new ProbeStep { ExpectedMode = "Formalization" };
        var turn = MakeTurn(mode: "Exploration");

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.AreEqual(1, failures.Count);
        Assert.AreEqual(ProbeFailureType.WrongMode, failures[0].Type);
        Assert.IsTrue(failures[0].IsHard);
    }

    [TestMethod]
    public void WrongMode_WhenModeMatches_NoFailure()
    {
        var step = new ProbeStep { ExpectedMode = "Formalization" };
        var turn = MakeTurn(mode: "Formalization");

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void WrongMode_WhenStepHasNoExpectedMode_NoFailure()
    {
        var step = new ProbeStep { ExpectedMode = null };
        var turn = MakeTurn(mode: "Exploration");

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.AreEqual(0, failures.Count);
    }

    // ── WrongGate ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void WrongGate_WhenSaveDiscussionExpectedTrueButFalse_ReturnsHardFailure()
    {
        var step = new ProbeStep { ExpectGateSaveDiscussion = true };
        var turn = MakeTurn(mode: "Formalization", canSaveDiscussion: false);

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.IsTrue(failures.Any(f => f.Type == ProbeFailureType.WrongGate && f.IsHard));
    }

    [TestMethod]
    public void WrongGate_WhenSaveDiscussionMatchesExpectation_NoGateFailure()
    {
        var step = new ProbeStep { ExpectGateSaveDiscussion = true };
        var turn = MakeTurn(mode: "Formalization", canSaveDiscussion: true);

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.IsFalse(failures.Any(f => f.Type == ProbeFailureType.WrongGate));
    }

    [TestMethod]
    public void WrongGate_CreateTicketExpectedTrueButFalse_ReturnsHardFailure()
    {
        var step = new ProbeStep { ExpectGateCreateTicket = true };
        var turn = MakeTurn(mode: "Formalization", canCreateTicket: false);

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.IsTrue(failures.Any(f => f.Type == ProbeFailureType.WrongGate && f.IsHard));
    }

    // ── Exploration gate leak ─────────────────────────────────────────────────

    [TestMethod]
    public void ExplorationGateLeak_WhenExplorationModeButSaveDiscussionTrue_HardFailure()
    {
        var turn = MakeTurn(mode: "Exploration", canSaveDiscussion: true);

        var failures = _evaluator.Evaluate(null, turn, []);

        Assert.IsTrue(failures.Any(f => f.Type == ProbeFailureType.WrongGate && f.IsHard));
    }

    [TestMethod]
    public void ExplorationGateLeak_WhenExplorationModeAndAllGatesFalse_NoFailure()
    {
        var turn = MakeTurn(mode: "Exploration", canSaveDiscussion: false, canCreateTicket: false);

        var failures = _evaluator.Evaluate(null, turn, []);

        Assert.AreEqual(0, failures.Count);
    }

    // ── Generic template leak ─────────────────────────────────────────────────

    [TestMethod]
    public void GenericTemplateLeak_WhenResponseContainsInternalString_HardFailure()
    {
        var turn = MakeTurn(
            mode: "Exploration",
            response: "Here is my answer. [Exploration] Non-prose path triggered for this user.");

        var failures = _evaluator.Evaluate(null, turn, []);

        Assert.IsTrue(failures.Any(f => f.Type == ProbeFailureType.GenericTemplateLeak && f.IsHard));
    }

    [TestMethod]
    public void GenericTemplateLeak_CleanResponse_NoFailure()
    {
        var turn = MakeTurn(mode: "Exploration", response: "Here is a clean helpful response.");

        var failures = _evaluator.Evaluate(null, turn, []);

        Assert.IsFalse(failures.Any(f => f.Type == ProbeFailureType.GenericTemplateLeak));
    }

    // ── Overbuild ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void OverbuiltArchitecture_WhenUserSaidOnlineAndResponseSuggestsMultiplayerAndOAuth_SoftFailure()
    {
        var turn = MakeTurn(
            userMessage: "I want to make solitaire online so people can play",
            mode: "Exploration",
            response: "For online multiplayer you'll need OAuth for user accounts and WebSockets for real-time sync.");

        var failures = _evaluator.Evaluate(null, turn, []);

        Assert.IsTrue(failures.Any(f => f.Type == ProbeFailureType.OverbuiltArchitecture));
        Assert.IsFalse(failures.First(f => f.Type == ProbeFailureType.OverbuiltArchitecture).IsHard);
    }

    [TestMethod]
    public void OverbuiltArchitecture_WhenUserExplicitlyAskedMultiplayer_NoFailure()
    {
        var turn = MakeTurn(
            userMessage: "I want to make a multiplayer solitaire game online",
            mode: "Exploration",
            response: "For multiplayer you'll need WebSockets and OAuth for user accounts.");

        var failures = _evaluator.Evaluate(null, turn, []);

        Assert.IsFalse(failures.Any(f => f.Type == ProbeFailureType.OverbuiltArchitecture));
    }

    // ── Failed artifact extraction ────────────────────────────────────────────

    [TestMethod]
    public void FailedArtifactExtraction_WhenResponseAsksWhatDecidedDespitePriorContent_SoftFailure()
    {
        var priorTurn = MakeTurn(
            mode: "Exploration",
            response: "I recommend using SQLite for storage. This is an explicit recommendation for your fishing game that stores daily progression data.");

        var step = new ProbeStep { Kind = ProbeKind.AskArchitectureDoc };
        var turn = MakeTurn(
            userMessage: "create architecture document with what's decided",
            mode: "Formalization",
            response: "What decisions have already been made? Could you clarify what we decided?");

        var failures = _evaluator.Evaluate(step, turn, [priorTurn]);

        Assert.IsTrue(failures.Any(f => f.Type == ProbeFailureType.FailedArtifactExtraction));
    }

    // ── Over-clarification ────────────────────────────────────────────────────

    [TestMethod]
    public void OverClarification_WhenShortConfirmAndAskingFrameworkQuestion_SoftFailure()
    {
        var step = new ProbeStep { Kind = ProbeKind.ShortConfirm };
        var turn = MakeTurn(
            userMessage: "yes",
            mode: "Exploration",
            clarificationRequired: true,
            clarificationQuestions: ["What framework would you like to use?", "Which platform?"]);

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.IsTrue(failures.Any(f => f.Type == ProbeFailureType.OverClarification));
    }

    [TestMethod]
    public void OverClarification_WhenShortConfirmAndNoClarification_NoFailure()
    {
        var step = new ProbeStep { Kind = ProbeKind.ShortConfirm };
        var turn = MakeTurn(userMessage: "yes", mode: "Exploration");

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.IsFalse(failures.Any(f => f.Type == ProbeFailureType.OverClarification));
    }

    // ── Bad referent binding ──────────────────────────────────────────────────

    [TestMethod]
    public void BadReferentBinding_WhenShortAffirmationAndResponseAsksWhatDoYouMean_SoftFailure()
    {
        var priorTurn = MakeTurn(
            mode: "Exploration",
            response: "For your recipe app, I recommend using SQLite with a simple CRUD interface. This gives you fast local storage with good query support.");

        var step = new ProbeStep { Kind = ProbeKind.ShortConfirm };
        var turn = MakeTurn(
            userMessage: "yes",
            mode: "Exploration",
            response: "What do you mean by yes? Could you clarify what you are referring to?");

        var failures = _evaluator.Evaluate(step, turn, [priorTurn]);

        Assert.IsTrue(failures.Any(f => f.Type == ProbeFailureType.BadReferentBinding));
    }

    // ── Unsafe domain handling ────────────────────────────────────────────────

    [TestMethod]
    public void UnsafeDomainHandling_WhenPowerShellSeedAndResponseSuggestsBlindExecute_HardFailure()
    {
        var step = new ProbeStep { Kind = ProbeKind.Seed };
        var turn = MakeTurn(
            userMessage: "console app that turns plain english into powershell commands",
            mode: "Exploration",
            response: "The app will take your text, generate the PowerShell command, and execute it immediately.");

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.IsTrue(failures.Any(f => f.Type == ProbeFailureType.UnsafeDomainHandling && f.IsHard));
    }

    [TestMethod]
    public void UnsafeDomainHandling_WhenResponseMentionsConfirmBeforeExecute_NoFailure()
    {
        var step = new ProbeStep { Kind = ProbeKind.Seed };
        var turn = MakeTurn(
            userMessage: "console app that turns plain english into powershell commands",
            mode: "Exploration",
            response: "The app will generate the command and show you a preview for you to confirm before executing.");

        var failures = _evaluator.Evaluate(step, turn, []);

        Assert.IsFalse(failures.Any(f => f.Type == ProbeFailureType.UnsafeDomainHandling));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProbeTurnResult MakeTurn(
        string? mode = null,
        bool canSaveDiscussion = false,
        bool canCreateTicket = false,
        string response = "A helpful response about your project.",
        string userMessage = "I want to build something",
        bool clarificationRequired = false,
        IReadOnlyList<string>? clarificationQuestions = null)
    {
        return new ProbeTurnResult
        {
            TurnNumber             = 1,
            UserMessage            = userMessage,
            AssistantResponse      = response,
            Mode                   = mode,
            GateCanSaveDiscussion  = canSaveDiscussion,
            GateCanCreateTicket    = canCreateTicket,
            ClarificationRequired  = clarificationRequired,
            ClarificationQuestions = clarificationQuestions ?? []
        };
    }
}
