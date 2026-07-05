using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Builder;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Smoke;

/// <summary>
/// D-2a: the deterministic alpha-smoke provider fakes model words only, is off
/// by default, and its output is parsed by the real proposal/tester/critic
/// services. The runtime provider is generic; demo answers live in fixtures.
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class DeterministicAlphaSmokeProviderTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static AgentLlmResolver ResolverWith(params (string Key, string Value)[] settings)
    {
        var root = Path.Combine(Path.GetTempPath(), $"irondev-alpha-prov-{Guid.NewGuid():N}");
        var dict = new Dictionary<string, string?>
        {
            ["AgentProfiles:Root"] = root,
            ["Ai:Provider"] = "fake",
            ["Ai:Model"] = "fake-model",
            ["AlphaSmoke:ResponseSetRoot"] = FixtureResponsesRoot()
        };

        foreach (var (key, value) in settings)
            dict[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new AgentLlmResolver(new SkeletonAgentProfileService(configuration), configuration);
    }

    private static DeterministicAlphaSmokeLlmService ProviderFor(SkeletonAgentRole role) =>
        new(role, FixtureConfiguration());

    private static IConfiguration FixtureConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AlphaSmoke:ResponseSetRoot"] = FixtureResponsesRoot()
            })
            .Build();

    [TestMethod]
    public async Task DeterministicProvider_DefaultDisabled()
    {
        var resolver = ResolverWith();
        var agent = await resolver.ResolveAsync(SkeletonAgentRole.Builder);
        Assert.AreNotEqual(DeterministicAlphaSmokeLlmService.ProviderName, agent.Provider,
            "With no AlphaSmoke config, the deterministic provider must never be selected.");
        Assert.AreEqual("fake", agent.Provider, "The negative gate test must use a safe fake fallback, not live OpenAI config.");
    }

    [TestMethod]
    public async Task DeterministicProvider_RequiresBothEnabledAndDeterministicMode()
    {
        var enabledOnly = ResolverWith(("AlphaSmoke:Enabled", "true"));
        var a1 = await enabledOnly.ResolveAsync(SkeletonAgentRole.Builder);
        Assert.AreNotEqual(DeterministicAlphaSmokeLlmService.ProviderName, a1.Provider,
            "Enabled without ModelMode=Deterministic must not activate the deterministic provider.");
        Assert.AreEqual("fake", a1.Provider, "The negative gate path must not require external provider config.");

        var modeOnly = ResolverWith(("AlphaSmoke:ModelMode", "Deterministic"));
        var a2 = await modeOnly.ResolveAsync(SkeletonAgentRole.Builder);
        Assert.AreNotEqual(DeterministicAlphaSmokeLlmService.ProviderName, a2.Provider,
            "ModelMode without Enabled must not activate the deterministic provider.");
        Assert.AreEqual("fake", a2.Provider, "The negative gate path must not require external provider config.");
    }

    [TestMethod]
    public async Task DeterministicMode_ReturnsDeterministicProviderForEveryRole()
    {
        var resolver = ResolverWith(("AlphaSmoke:Enabled", "true"), ("AlphaSmoke:ModelMode", "Deterministic"));
        foreach (var role in Enum.GetValues<SkeletonAgentRole>())
        {
            var agent = await resolver.ResolveAsync(role);
            Assert.AreEqual(DeterministicAlphaSmokeLlmService.ProviderName, agent.Provider, $"Role {role} should resolve deterministic.");
            Assert.IsInstanceOfType<DeterministicAlphaSmokeLlmService>(agent.Llm);
        }
    }

    [TestMethod]
    public async Task DeterministicProvider_LoadsConfiguredRoleResponseSet()
    {
        var json = await ProviderFor(SkeletonAgentRole.Builder).GetResponseAsync("builder");

        StringAssert.Contains(json, "Validate a Book at construction.");
        StringAssert.Contains(json, "src/BookSeller.Domain/Book.cs");
    }

    [TestMethod]
    public async Task Builder_Output_ParsesThroughTheRealProposalService()
    {
        var json = await ProviderFor(SkeletonAgentRole.Builder).GetResponseAsync("builder");
        var proposal = CodeChangeProposalService.ParseProposal(json, ticketId: 1);

        Assert.AreEqual(1, proposal.FileChanges.Count, "The validate-book proposal changes exactly one file.");
        Assert.AreEqual("src/BookSeller.Domain/Book.cs", proposal.FileChanges[0].FilePath);
        StringAssert.Contains(proposal.FileChanges[0].FullContentAfter, "ArgumentException",
            "The proposed Book.cs must add validation for the fixture ticket.");
    }

    [TestMethod]
    public async Task Critic_Output_ParsesThroughTheRealCriticService_AsCleanNoObjection()
    {
        var json = await ProviderFor(SkeletonAgentRole.Critic).GetResponseAsync("critic");
        var parsed = SkeletonCriticReviewService.TryParse(json);

        Assert.IsTrue(parsed.Succeeded, parsed.FailureReason);
        Assert.AreEqual(0, parsed.Findings.Count, "The deterministic clean review carries no findings.");
    }

    [TestMethod]
    public async Task Tester_Output_ParsesAsAuthoredTests()
    {
        var json = await ProviderFor(SkeletonAgentRole.Tester).GetResponseAsync("tester");
        var tests = JsonSerializer.Deserialize<List<SkeletonAuthoredTest>>(json, WebJson);

        Assert.IsNotNull(tests);
        Assert.IsTrue(tests!.Count >= 1, "The tester authors at least one covering test.");
        StringAssert.Contains(tests[0].RelativePath, "BookSeller.Domain.Tests");
        StringAssert.Contains(tests[0].Content, "[TestClass]");
    }

    [TestMethod]
    public async Task DeterministicProvider_ProducesNoAuthorityOrFinalState()
    {
        foreach (var role in Enum.GetValues<SkeletonAgentRole>())
        {
            var output = await ProviderFor(role).GetResponseAsync("x");
            foreach (var forbidden in new[]
                     {
                         "acceptedApproval", "policySatisfaction", "policySatisfied", "dryRunReceipt",
                         "patchArtifact", "sourceApply", "\"Applied\"", "evidenceHash", "disposition",
                         "\"approved\"", "release", "deploy"
                     })
            {
                Assert.IsFalse(output.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"The deterministic provider ({role}) must fake model words only; it must never emit '{forbidden}'.");
            }
        }
    }

    [TestMethod]
    public void DeterministicProvider_IsAnLlmServiceOnly_GrantsNothing()
    {
        var source = File.ReadAllText(RepoFile("IronDev.Infrastructure", "Services", "DeterministicAlphaSmokeLlmService.cs"));
        foreach (var forbidden in new[] { "IAcceptedApprovalStore", "IRunStore", "TransitionAsync", "SaveAsync", "SatisfyPolicy", "ApplyAsync" })
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal),
                $"The deterministic provider must not reference {forbidden}; it fakes words, not the loop.");
    }

    [TestMethod]
    public void RuntimeProvider_ContainsNoDemoAnswerMaterial()
    {
        var source = File.ReadAllText(RepoFile("IronDev.Infrastructure", "Services", "DeterministicAlphaSmokeLlmService.cs"));
        foreach (var forbidden in new[] { "BookSeller", "BookSeller.Domain", "ValidateBookAuthoredTests", "src/BookSeller.Domain/Book.cs" })
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Runtime infrastructure must not contain demo answer material: {forbidden}");

        Assert.IsTrue(Directory.Exists(FixtureResponsesRoot()), "Demo-specific deterministic responses must live in fixture data.");
    }

    private static string FixtureResponsesRoot() =>
        RepoFile("TestFixtures", "BookSeller", "alpha-smoke", "responses");

    private static string RepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            current = current.Parent;
        Assert.IsNotNull(current, "Repo root not found.");
        return Path.Combine(current!.FullName, Path.Combine(parts));
    }
}
