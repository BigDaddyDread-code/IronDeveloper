using IronDev.Core.ChatProbe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.ChatProbe;

[TestClass]
public sealed class ProbeScenarioCatalogTests
{
    // ── GetAll ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetAll_ReturnsAtLeastTenScenarios()
    {
        Assert.IsTrue(ProbeScenarioCatalog.GetAll().Count >= 10);
    }

    [TestMethod]
    public void GetAll_AllScenariosHaveUniqueIds()
    {
        var ids = ProbeScenarioCatalog.GetAll().Select(s => s.ScenarioId).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Scenario IDs must be unique.");
    }

    [TestMethod]
    public void GetAll_AllScenariosHaveNonEmptyProjectIdea()
    {
        foreach (var s in ProbeScenarioCatalog.GetAll())
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.ProjectIdea),
                $"Scenario {s.ScenarioId} has empty ProjectIdea.");
    }

    [TestMethod]
    public void GetAll_AllScenariosHaveAtLeastTwoSteps()
    {
        foreach (var s in ProbeScenarioCatalog.GetAll())
            Assert.IsTrue(s.Steps.Count >= 2,
                $"Scenario {s.ScenarioId} has only {s.Steps.Count} steps (minimum 2).");
    }

    [TestMethod]
    public void GetAll_AllStepsHaveNonEmptyUserMessage()
    {
        foreach (var s in ProbeScenarioCatalog.GetAll())
        foreach (var step in s.Steps)
            Assert.IsFalse(string.IsNullOrWhiteSpace(step.UserMessage),
                $"Scenario {s.ScenarioId} step {step.Order} has empty UserMessage.");
    }

    [TestMethod]
    public void GetAll_AllStepOrdersArePositive()
    {
        foreach (var s in ProbeScenarioCatalog.GetAll())
        foreach (var step in s.Steps)
            Assert.IsTrue(step.Order >= 1,
                $"Scenario {s.ScenarioId} step has Order < 1: {step.Order}.");
    }

    [TestMethod]
    public void GetAll_CategoriesAreAssigned()
    {
        var categories = ProbeScenarioCatalog.GetAll()
            .Select(s => s.Category)
            .Distinct()
            .ToHashSet();

        Assert.IsTrue(categories.Contains(ProjectCategory.Game), "Missing Game category.");
        Assert.IsTrue(categories.Contains(ProjectCategory.BusinessApp), "Missing BusinessApp category.");
        Assert.IsTrue(categories.Contains(ProjectCategory.DeveloperTool), "Missing DeveloperTool category.");
        Assert.IsTrue(categories.Contains(ProjectCategory.ConsumerApp), "Missing ConsumerApp category.");
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetById_ReturnsCorrectScenario()
    {
        var scenario = ProbeScenarioCatalog.GetById("fishing-game");
        Assert.IsNotNull(scenario);
        Assert.AreEqual("fishing-game", scenario.ScenarioId);
    }

    [TestMethod]
    public void GetById_IsCaseInsensitive()
    {
        var scenario = ProbeScenarioCatalog.GetById("FISHING-GAME");
        Assert.IsNotNull(scenario);
    }

    [TestMethod]
    public void GetById_ReturnsNullForUnknownId()
    {
        var scenario = ProbeScenarioCatalog.GetById("does-not-exist-xyz");
        Assert.IsNull(scenario);
    }

    // ── GetFromSeed ───────────────────────────────────────────────────────────

    [TestMethod]
    public void GetFromSeed_IsDeterministic()
    {
        var first  = ProbeScenarioCatalog.GetFromSeed(42);
        var second = ProbeScenarioCatalog.GetFromSeed(42);
        Assert.AreEqual(first.ScenarioId, second.ScenarioId);
    }

    [TestMethod]
    public void GetFromSeed_DifferentSeedsProduceDifferentScenarios()
    {
        var count = ProbeScenarioCatalog.GetAll().Count;
        var ids   = Enumerable.Range(0, count).Select(i => ProbeScenarioCatalog.GetFromSeed(i).ScenarioId).ToList();
        Assert.AreEqual(count, ids.Distinct().Count(),
            $"Seeds 0-{count - 1} should cover all {count} scenarios.");
    }

    [TestMethod]
    public void GetFromSeed_HandleNegativeSeed()
    {
        // Should not throw — negative seeds must resolve deterministically
        var scenario = ProbeScenarioCatalog.GetFromSeed(-1);
        Assert.IsNotNull(scenario);
    }

    // ── GetBatch ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetBatch_ReturnsCorrectCount()
    {
        var batch = ProbeScenarioCatalog.GetBatch(5, seed: 0);
        Assert.AreEqual(5, batch.Count);
    }

    [TestMethod]
    public void GetBatch_IsReproducible()
    {
        var first  = ProbeScenarioCatalog.GetBatch(5, seed: 7);
        var second = ProbeScenarioCatalog.GetBatch(5, seed: 7);
        var firstIds  = first.Select(b => b.Scenario.ScenarioId).ToList();
        var secondIds = second.Select(b => b.Scenario.ScenarioId).ToList();
        CollectionAssert.AreEqual(firstIds, secondIds);
    }

    [TestMethod]
    public void GetBatch_DifferentSeedProducesDifferentOrder()
    {
        var batchA = ProbeScenarioCatalog.GetBatch(10, seed: 0)
            .Select(b => b.Scenario.ScenarioId).ToList();
        var batchB = ProbeScenarioCatalog.GetBatch(10, seed: 100)
            .Select(b => b.Scenario.ScenarioId).ToList();
        // They should differ somewhere (extremely unlikely to be identical)
        Assert.IsFalse(batchA.SequenceEqual(batchB),
            "Different seeds should produce different ordering.");
    }

    [TestMethod]
    public void GetBatch_AllReturnedScenariosAreNonNull()
    {
        var batch = ProbeScenarioCatalog.GetBatch(20, seed: 0);
        foreach (var (scenario, _) in batch)
            Assert.IsNotNull(scenario);
    }

    // ── Specific scenarios ────────────────────────────────────────────────────

    [TestMethod]
    public void FishingGame_HasFormalizationStep()
    {
        var scenario = ProbeScenarioCatalog.GetById("fishing-game");
        Assert.IsNotNull(scenario);
        Assert.IsTrue(scenario.Steps.Any(s => s.Kind == ProbeKind.Formalize),
            "fishing-game should have a Formalize step.");
    }

    [TestMethod]
    public void FishingGame_HasArchitectureDocStep()
    {
        var scenario = ProbeScenarioCatalog.GetById("fishing-game");
        Assert.IsNotNull(scenario);
        Assert.IsTrue(scenario.Steps.Any(s => s.Kind == ProbeKind.AskArchitectureDoc),
            "fishing-game should have an AskArchitectureDoc step.");
    }

    [TestMethod]
    public void NaturalLanguagePowershell_IsInDeveloperToolCategory()
    {
        var scenario = ProbeScenarioCatalog.GetById("natural-language-powershell");
        Assert.IsNotNull(scenario);
        Assert.AreEqual(ProjectCategory.DeveloperTool, scenario.Category);
    }

    [TestMethod]
    public void SolitaireOnline_HasTopicCorrectionStep()
    {
        var scenario = ProbeScenarioCatalog.GetById("solitaire-online");
        Assert.IsNotNull(scenario);
        Assert.IsTrue(scenario.Steps.Any(s => s.Kind == ProbeKind.TopicCorrection),
            "solitaire-online should have a TopicCorrection step to correct multiplayer scope.");
    }
}
