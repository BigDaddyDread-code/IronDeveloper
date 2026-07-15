using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.AiConnections;
using IronDev.Core.Builder;
using IronDev.Core.RunReadiness;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("SkeletonRun")]
public sealed class AgentRunConfigurationBindingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "irondev-run-binding", Guid.NewGuid().ToString("N"));

    [TestMethod]
    public async Task ScopedResolver_UsesPublishedProjectProfileAndControlledConnectionTruth()
    {
        var (profiles, configuration) = Harness();
        await PublishBuilderAsync(profiles);
        var credentials = new RecordingCredentialStore();
        var resolver = new AgentLlmResolver(profiles, configuration, new ControlledCatalog(), credentials);

        var resolved = await resolver.ResolveAsync(SkeletonAgentRole.Builder, tenantId: 7, projectId: 11);

        Assert.AreEqual("fake", resolved.Provider, "The controlled connection provider is runtime truth.");
        Assert.AreEqual("project-builder-model", resolved.Model);
        Assert.IsInstanceOfType<FakeLlmService>(resolved.Llm);
        Assert.AreEqual(7, credentials.ReadTenantId);
        Assert.AreEqual("controlled-builder", credentials.ReadConnectionId);
    }

    [TestMethod]
    public async Task ScopedResolver_ExplicitDeterministicConnection_ResolvesActualDeterministicService()
    {
        var (profiles, configuration) = Harness();
        await PublishBuilderAsync(profiles);
        var resolver = new AgentLlmResolver(
            profiles,
            configuration,
            new ControlledCatalog(ProjectRunProviders.LocalTestDeterministic),
            new RecordingCredentialStore());

        var resolved = await resolver.ResolveAsync(SkeletonAgentRole.Builder, tenantId: 7, projectId: 11);

        Assert.AreEqual(ProjectRunProviders.LocalTestDeterministic, resolved.Provider);
        Assert.IsInstanceOfType<DeterministicAlphaSmokeLlmService>(resolved.Llm);
        Assert.IsFalse(resolved.Llm is FakeLlmService);
    }

    [TestMethod]
    public async Task BuilderOperation_UsesTheSameScopedVoiceAndCoordinatesAsTheRunSnapshot()
    {
        var (profiles, _) = Harness();
        await PublishBuilderAsync(profiles);
        var llm = new CapturingLlm("{\"summary\":\"ok\",\"changes\":[{\"filePath\":\"a.cs\",\"diff\":\"x\"}]}");
        var resolver = new RecordingResolver(llm);
        var service = new CodeChangeProposalService(resolver, profiles, new LlmTraceService());

        var proposal = await service.GenerateProposalAsync(new TicketBuildContext
        {
            TenantId = 7,
            ProjectId = 11,
            TicketId = 42,
            ProjectName = "Scoped project",
            ProjectPath = _root,
            TicketTitle = "Change one file",
            TicketSummary = "Use the project profile."
        });

        Assert.AreEqual(7, resolver.TenantId);
        Assert.AreEqual(11, resolver.ProjectId);
        StringAssert.Contains(llm.LastPrompt, "PROJECT_SKILL_MARKER");
        StringAssert.Contains(llm.LastPrompt, "PROJECT_PERSONALITY_MARKER");
        Assert.AreEqual("project-builder-model", proposal.ModelName);
    }

    private (SkeletonAgentProfileService Profiles, IConfiguration Configuration) Harness()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentProfiles:Root"] = _root,
            ["AgentProfiles:AllowFakeProvider"] = "true",
            ["Ai:Provider"] = "openai",
            ["Ai:Model"] = "global-model",
            ["Ai:TimeoutSeconds"] = "60"
        }).Build();
        return (new SkeletonAgentProfileService(configuration), configuration);
    }

    private static async Task PublishBuilderAsync(SkeletonAgentProfileService profiles)
    {
        var scope = new SkeletonAgentProfileScope { TenantId = 7, ProjectId = 11 };
        var draft = await profiles.GetDraftAsync(SkeletonAgentRole.Builder, scope);
        var saved = await profiles.SaveDraftAsync(SkeletonAgentRole.Builder, scope, new SkeletonAgentProfileDraftWriteRequest
        {
            ExpectedRevision = draft.Revision,
            AiConnectionId = "controlled-builder",
            Provider = "openai",
            Model = "project-builder-model",
            TimeoutSeconds = 25,
            Skill = "PROJECT_SKILL_MARKER",
            Personality = "PROJECT_PERSONALITY_MARKER"
        });
        Assert.IsTrue(saved.Succeeded);
        var published = await profiles.PublishDraftAsync(SkeletonAgentRole.Builder, scope, new SkeletonAgentProfilePublishRequest
        {
            ExpectedRevision = saved.CurrentRevision,
            Reason = "Bind runtime to project profile"
        }, actorUserId: 3);
        Assert.IsTrue(published.Succeeded);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private sealed class ControlledCatalog(string provider = "fake") : IAiConnectionCatalogService
    {
        public Task<IReadOnlyList<AiConnectionMetadata>> ListAsync(int tenantId, int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiConnectionMetadata>>
            ([
                new AiConnectionMetadata
                {
                    Id = "controlled-builder",
                    TenantId = tenantId,
                    DisplayName = "Controlled builder",
                    ProviderKind = provider,
                    ControlledEndpointId = "local-fake",
                    ControlledEndpoint = "provider-default:fake",
                    CredentialConfigured = true,
                    CredentialStatus = "Configured",
                    Enabled = true,
                    TenantAvailable = true,
                    ProjectAvailable = true,
                    CreatedByUserId = 1,
                    UpdatedByUserId = 1,
                    Version = "1",
                    Boundary = "non-secret"
                }
            ]);
    }

    private sealed class RecordingCredentialStore : IAiConnectionCredentialStore
    {
        public int ReadTenantId { get; private set; }
        public string ReadConnectionId { get; private set; } = string.Empty;
        public Task<AiConnectionCredentialStoredMetadata?> GetMetadataAsync(int tenantId, string connectionId, CancellationToken cancellationToken = default) => Task.FromResult<AiConnectionCredentialStoredMetadata?>(null);
        public Task<string?> GetCredentialForUseAsync(int tenantId, string connectionId, CancellationToken cancellationToken = default)
        {
            ReadTenantId = tenantId;
            ReadConnectionId = connectionId;
            return Task.FromResult<string?>("internal-only-test-value");
        }
        public Task StoreAsync(int tenantId, string connectionId, string credential, int userId, string? reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeAsync(int tenantId, string connectionId, int userId, string? reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CapturingLlm(string response) : ILLMService
    {
        public string LastPrompt { get; private set; } = string.Empty;
        public Task<string> GetResponseAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingResolver(ILLMService llm) : IAgentLlmResolver
    {
        public int TenantId { get; private set; }
        public int ProjectId { get; private set; }
        public Task<SkeletonAgentLlm> ResolveAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SkeletonAgentLlm { Role = role, Llm = llm, Provider = "legacy", Model = "legacy" });
        public Task<SkeletonAgentLlm> ResolveAsync(SkeletonAgentRole role, int tenantId, int projectId, CancellationToken cancellationToken = default)
        {
            TenantId = tenantId;
            ProjectId = projectId;
            return Task.FromResult(new SkeletonAgentLlm { Role = role, Llm = llm, Provider = "fake", Model = "project-builder-model" });
        }
    }
}
