using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.AiConnections;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("SkeletonRun")]
public sealed class AgentConfigurationPackContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "irondev-config-pack-tests", Guid.NewGuid().ToString("N"));

    [TestMethod]
    public async Task ExportImport_MapsLogicalConnectionAndCreatesDraftWithoutPublishing()
    {
        var profiles = Profiles();
        var source = new SkeletonAgentProfileScope { TenantId = 10 };
        var target = new SkeletonAgentProfileScope { TenantId = 20 };
        var sourceDraft = await profiles.GetDraftAsync(SkeletonAgentRole.Analyst, source);
        var saved = await profiles.SaveDraftAsync(SkeletonAgentRole.Analyst, source, new SkeletonAgentProfileDraftWriteRequest
        {
            ExpectedRevision = sourceDraft.Revision,
            AiConnectionId = "source-openai",
            Provider = "openai",
            Model = "gpt-test",
            TimeoutSeconds = 20,
            Skill = "Inspect the project.",
            Personality = "Direct and concise."
        });
        Assert.IsTrue(saved.Succeeded);
        var published = await profiles.PublishDraftAsync(SkeletonAgentRole.Analyst, source, new SkeletonAgentProfilePublishRequest
        {
            ExpectedRevision = saved.CurrentRevision,
            Reason = "portable baseline"
        }, 101);
        Assert.IsTrue(published.Succeeded);

        var service = new AgentConfigurationPackService(profiles, new TenantConnectionCatalog());
        var pack = await service.ExportAsync(10, 101, source);
        var json = JsonSerializer.Serialize(pack);
        Assert.IsFalse(json.Contains("credential", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("controlledEndpoint", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, pack.Profiles.Count);
        Assert.AreEqual("OpenAI shared", pack.Profiles[0].LogicalConnectionName);

        var preview = await service.PreviewAsync(20, 202, target, pack);
        Assert.IsTrue(preview.Succeeded);
        Assert.IsTrue(preview.DraftOnly);
        Assert.IsTrue(preview.Differences.Any(item => item.Field == "aiConnectionId" && item.ImportedValue == "target-openai"));

        var outcome = await service.ImportAsync(20, 202, target, new AgentConfigurationPackImportRequest
        {
            Pack = pack,
            ExpectedRevisions = preview.ExpectedRevisions
        });
        Assert.IsTrue(outcome.Succeeded);
        Assert.IsFalse(outcome.Published);
        Assert.AreEqual(1, outcome.CreatedDrafts.Count);
        Assert.AreEqual("target-openai", outcome.CreatedDrafts[0].Values.AiConnectionId);
        Assert.AreEqual(0, (await profiles.ListHistoryAsync(SkeletonAgentRole.Analyst, target)).Count);
    }

    [TestMethod]
    public async Task Preview_RefusesConnectionThatIsNotAvailableInTargetTenant()
    {
        var profiles = Profiles();
        var service = new AgentConfigurationPackService(profiles, new TenantConnectionCatalog(targetEnabled: false));
        var pack = new AgentConfigurationPack
        {
            PackId = "pack-1",
            ExportedAtUtc = DateTimeOffset.UtcNow,
            SourceScope = "Tenant",
            SourceTenantId = 10,
            Profiles =
            [
                new AgentConfigurationPackEntry
                {
                    Role = SkeletonAgentRole.Builder,
                    Values = new SkeletonAgentProfileUpdate
                    {
                        AiConnectionId = "source-openai",
                        Provider = "openai",
                        Model = "gpt-test",
                        TimeoutSeconds = 20,
                        Skill = "Build.",
                        Personality = "Careful."
                    },
                    LogicalConnectionName = "OpenAI shared",
                    BuiltInDefaultVersion = "v1",
                    SourcePublishedVersion = 1
                }
            ]
        };

        var preview = await service.PreviewAsync(20, 202, new SkeletonAgentProfileScope { TenantId = 20 }, pack);
        Assert.IsFalse(preview.Succeeded);
        Assert.AreEqual("AiConnectionUnavailable", preview.Code);
        Assert.AreEqual(0, (await profiles.ListHistoryAsync(SkeletonAgentRole.Builder, new SkeletonAgentProfileScope { TenantId = 20 })).Count);
    }

    private SkeletonAgentProfileService Profiles()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentProfiles:Root"] = _root,
            ["Ai:Provider"] = "openai",
            ["Ai:Model"] = "gpt-default",
            ["Ai:TimeoutSeconds"] = "20"
        }).Build();
        return new SkeletonAgentProfileService(configuration);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TenantConnectionCatalog(bool targetEnabled = true) : IAiConnectionCatalogService
    {
        public Task<IReadOnlyList<AiConnectionMetadata>> ListAsync(int tenantId, int userId, CancellationToken cancellationToken = default)
        {
            var source = tenantId == 10;
            var enabled = source || targetEnabled;
            IReadOnlyList<AiConnectionMetadata> connections =
            [
                new AiConnectionMetadata
                {
                    Id = source ? "source-openai" : "target-openai",
                    TenantId = tenantId,
                    DisplayName = "OpenAI shared",
                    ProviderKind = "openai",
                    ControlledEndpointId = "openai-default",
                    ControlledEndpoint = "provider-default:openai",
                    CredentialConfigured = true,
                    CredentialStatus = "Configured",
                    Enabled = enabled,
                    TenantAvailable = enabled,
                    ProjectAvailable = enabled,
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId,
                    Version = "1",
                    Boundary = "non-secret"
                }
            ];
            return Task.FromResult(connections);
        }
    }
}
