using IronDev.Core.ChatProbe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.ChatProbe;

[TestClass]
public sealed class PersonaEngineTests
{
    // ── GetAll ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetAll_Returns6Personas()
    {
        var personas = PersonaEngine.GetAll();
        Assert.AreEqual(6, personas.Count);
    }

    [TestMethod]
    public void GetAll_AllPersonasHaveNonEmptyName()
    {
        foreach (var p in PersonaEngine.GetAll())
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.Name), $"Persona {p.Id} has empty name");
    }

    [TestMethod]
    public void GetAll_AllPersonasHaveNonEmptyDescription()
    {
        foreach (var p in PersonaEngine.GetAll())
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.Description), $"Persona {p.Id} has empty description");
    }

    // ── MessyRob ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void MessyRob_TransformsArchitectureToTypo()
    {
        var persona = PersonaEngine.GetById(PersonaId.MessyRob);
        var result  = persona.TextTransform("I want to create an architecture document");
        StringAssert.Contains(result, "artecture");
    }

    [TestMethod]
    public void MessyRob_TransformsRecommendationToTypo()
    {
        var persona = PersonaEngine.GetById(PersonaId.MessyRob);
        var result  = persona.TextTransform("give me a recommendation for the database");
        StringAssert.Contains(result, "recomenation");
    }

    [TestMethod]
    public void MessyRob_TransformsSqlServerToTypo()
    {
        var persona = PersonaEngine.GetById(PersonaId.MessyRob);
        var result  = persona.TextTransform("I want to use SQL Server for this");
        StringAssert.Contains(result, "sql sever");
    }

    [TestMethod]
    public void MessyRob_ProducesNonEmptyOutputForAnyInput()
    {
        var persona  = PersonaEngine.GetById(PersonaId.MessyRob);
        var messages = new[] { "yes", "ok that one", "what slice be", "artecture doc", "can save this" };
        foreach (var msg in messages)
        {
            var result = persona.TextTransform(msg);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result), $"MessyRob returned empty for: {msg}");
        }
    }

    // ── ShortcutUser ──────────────────────────────────────────────────────────

    [TestMethod]
    public void ShortcutUser_CollapsesYesAgreementToYes()
    {
        var persona = PersonaEngine.GetById(PersonaId.ShortcutUser);
        var result  = persona.TextTransform("yes that sounds great");
        Assert.AreEqual("yes", result);
    }

    [TestMethod]
    public void ShortcutUser_CollapsesOkayToOk()
    {
        var persona = PersonaEngine.GetById(PersonaId.ShortcutUser);
        var result  = persona.TextTransform("okay let's do that one");
        Assert.AreEqual("ok", result);
    }

    [TestMethod]
    public void ShortcutUser_CollapsesThatOneReference()
    {
        var persona = PersonaEngine.GetById(PersonaId.ShortcutUser);
        var result  = persona.TextTransform("that first option you mentioned");
        Assert.AreEqual("that one", result);
    }

    [TestMethod]
    public void ShortcutUser_LongerNonConfirmationPassesThrough()
    {
        var persona  = PersonaEngine.GetById(PersonaId.ShortcutUser);
        var original = "I want to build a fishing game with daily difficulty";
        var result   = persona.TextTransform(original);
        // Not a confirmation — should pass through unchanged
        Assert.AreEqual(original, result);
    }

    // ── ScopeCreeper ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ScopeCreeper_AppendsScopeSuffix()
    {
        var persona = PersonaEngine.GetById(PersonaId.ScopeCreeper);
        var result  = persona.TextTransform("I want to build a recipe app");
        // Should be longer than original with a scope-creep suffix
        Assert.IsTrue(result.Length > "I want to build a recipe app".Length,
            $"Expected appended scope but got: {result}");
    }

    [TestMethod]
    public void ScopeCreeper_DoesNotAppendToShortAffirmations()
    {
        var persona = PersonaEngine.GetById(PersonaId.ScopeCreeper);
        var result  = persona.TextTransform("yes");
        Assert.AreEqual("yes", result);
    }

    // ── Contradictor ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Contradictor_PrefixesContradictionMarker()
    {
        var persona = PersonaEngine.GetById(PersonaId.Contradictor);
        var result  = persona.TextTransform("I want to keep it offline");
        var hasPrefix = result.StartsWith("no,") ||
                        result.StartsWith("not that,") ||
                        result.StartsWith("wait") ||
                        result.StartsWith("hmm no,");
        Assert.IsTrue(hasPrefix, $"Expected contradiction prefix but got: {result}");
    }

    [TestMethod]
    public void Contradictor_DoesNotDoublePrefix_WhenMessageAlreadyStartsWithNo()
    {
        var persona  = PersonaEngine.GetById(PersonaId.Contradictor);
        var original = "not that, keep it simple";
        var result   = persona.TextTransform(original);
        Assert.AreEqual(original, result);
    }

    // ── Formalizer ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Formalizer_AppendsFormalizeIntent()
    {
        var persona = PersonaEngine.GetById(PersonaId.Formalizer);
        var result  = persona.TextTransform("we've talked about the rules");
        var hasTail = result.Contains("save this") ||
                      result.Contains("make a doc") ||
                      result.Contains("break into tickets");
        Assert.IsTrue(hasTail, $"Expected formalization tail but got: {result}");
    }

    [TestMethod]
    public void Formalizer_DoesNotDuplicate_WhenAlreadyContainsSave()
    {
        var persona  = PersonaEngine.GetById(PersonaId.Formalizer);
        var original = "save this as a discussion document";
        var result   = persona.TextTransform(original);
        Assert.AreEqual(original, result);
    }

    // ── ParseName ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ParseName_MatchesByIdString()
    {
        var persona = PersonaEngine.ParseName("MessyRob");
        Assert.IsNotNull(persona);
        Assert.AreEqual(PersonaId.MessyRob, persona.Id);
    }

    [TestMethod]
    public void ParseName_MatchesByDisplayName()
    {
        var persona = PersonaEngine.ParseName("Messy Rob");
        Assert.IsNotNull(persona);
        Assert.AreEqual(PersonaId.MessyRob, persona.Id);
    }

    [TestMethod]
    public void ParseName_ReturnsNullForUnknownName()
    {
        var persona = PersonaEngine.ParseName("UnknownPersona");
        Assert.IsNull(persona);
    }

    // ── GetFromSeed ───────────────────────────────────────────────────────────

    [TestMethod]
    public void GetFromSeed_IsDeterministic()
    {
        var first  = PersonaEngine.GetFromSeed(42);
        var second = PersonaEngine.GetFromSeed(42);
        Assert.AreEqual(first.Id, second.Id);
    }

    [TestMethod]
    public void GetFromSeed_DifferentSeedsProduceDifferentPersonas()
    {
        // With 6 personas, seeds 0-5 should produce distinct personas
        var ids = Enumerable.Range(0, 6).Select(i => PersonaEngine.GetFromSeed(i).Id).ToList();
        Assert.AreEqual(6, ids.Distinct().Count(), "Seeds 0-5 should cover all 6 personas");
    }
}
