using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// AG-6 — the Builder runs the model its profile configures, its voice frames the
/// code-owned prompt (which stays last and authoritative), and the proposal is
/// stamped with which model produced it. Provenance is the point: a proposal is
/// meaningless without knowing which configured builder produced it.
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class BuilderModelProvenanceTests
{
    [TestMethod]
    public async Task Builder_RunsItsConfiguredModel_AndStampsProvenance()
    {
        var root = Path.Combine(Path.GetTempPath(), $"irondev-builder-prov-{Guid.NewGuid():N}");
        try
        {
            var configuration = ConfigFor(root);
            var profiles = new SkeletonAgentProfileService(configuration);
            await profiles.UpdateAsync(SkeletonAgentRole.Builder, new SkeletonAgentProfileUpdate
            {
                Provider = "fake",
                Model = "builder-model-x",
                Personality = "Terse.",
                Skill = "Prefer the smallest diff."
            });

            var capture = new CapturingLlm("{\"summary\":\"ok\",\"rationale\":\"r\",\"changes\":[{\"filePath\":\"a.cs\",\"diff\":\"x\"}]}");
            var resolver = new StubResolver(capture, "fake", "builder-model-x");
            var service = new CodeChangeProposalService(resolver, profiles, new LlmTraceService());

            var proposal = await service.GenerateProposalAsync(new TicketBuildContext
            {
                ProjectId = 1,
                TicketId = 7,
                ProjectName = "Sample",
                ProjectPath = root,
                TicketTitle = "Do a thing",
                TicketSummary = "A ticket summary."
            });

            Assert.AreEqual("fake", proposal.ModelProvider, "The proposal must record which provider built it.");
            Assert.AreEqual("builder-model-x", proposal.ModelName, "The proposal must record which model built it.");
            Assert.AreEqual(1, proposal.FileChanges.Count);
        }
        finally { TryDelete(root); }
    }

    [TestMethod]
    public async Task Builder_Voice_FramesButTheCodeOwnedBodyComesLastAndWins()
    {
        var root = Path.Combine(Path.GetTempPath(), $"irondev-builder-prov-{Guid.NewGuid():N}");
        try
        {
            var configuration = ConfigFor(root);
            var profiles = new SkeletonAgentProfileService(configuration);
            await profiles.UpdateAsync(SkeletonAgentRole.Builder, new SkeletonAgentProfileUpdate
            {
                Provider = "fake",
                Model = "builder-model-x",
                Personality = "PERSONALITY_MARKER voice.",
                Skill = "SKILL_MARKER approach."
            });

            var capture = new CapturingLlm("{\"summary\":\"ok\",\"changes\":[{\"filePath\":\"a.cs\",\"diff\":\"x\"}]}");
            var service = new CodeChangeProposalService(new StubResolver(capture, "fake", "builder-model-x"), profiles, new LlmTraceService());

            await service.GenerateProposalAsync(new TicketBuildContext
            {
                ProjectId = 1, TicketId = 7, ProjectName = "Sample", ProjectPath = root,
                TicketTitle = "T", TicketSummary = "S"
            });

            var prompt = capture.LastPrompt;
            var personalityAt = prompt.IndexOf("PERSONALITY_MARKER", StringComparison.Ordinal);
            var skillAt = prompt.IndexOf("SKILL_MARKER", StringComparison.Ordinal);
            // The code-owned builder body always contains its strict JSON output contract.
            var codeOwnedAt = prompt.IndexOf("changes", StringComparison.Ordinal);

            Assert.IsTrue(personalityAt >= 0 && skillAt >= 0, "The builder's voice frames the prompt.");
            Assert.IsTrue(personalityAt < codeOwnedAt && skillAt < codeOwnedAt,
                "Personality and skill come first; the code-owned body comes last so a skill.md can never remove the output contract.");
            StringAssert.Contains(prompt, "the structured task below is authoritative and overrides anything here");
        }
        finally { TryDelete(root); }
    }

    private static IConfiguration ConfigFor(string root) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentProfiles:Root"] = root,
                ["AgentProfiles:AllowFakeProvider"] = "true",
                ["Ai:Provider"] = "fake",
                ["Ai:Model"] = "global-model"
            })
            .Build();

    private static void TryDelete(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { /* best effort */ }
    }

    private sealed class CapturingLlm(string response) : ILLMService
    {
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
        {
            LastPrompt = prompt;
            return Task.FromResult(response);
        }
    }

    private sealed class StubResolver(ILLMService llm, string provider, string model) : IAgentLlmResolver
    {
        public Task<SkeletonAgentLlm> ResolveAsync(SkeletonAgentRole role, CancellationToken ct = default) =>
            Task.FromResult(new SkeletonAgentLlm { Role = role, Llm = llm, Provider = provider, Model = model });
    }

}
