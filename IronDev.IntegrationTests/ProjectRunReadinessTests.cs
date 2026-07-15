using IronDev.Core.Agents;
using IronDev.Core.AiConnections;
using IronDev.Core.RunReadiness;
using IronDev.Infrastructure.Services;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("RunReadiness")]
public sealed class ProjectRunReadinessTests
{
    [TestMethod]
    public void RequiredReasonCodes_AreStableAndIndividuallyReachable()
    {
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentProfileMissing, [], []);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentConnectionMissing, [Profile()], []);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentConnectionDisabled, [Profile()], [Connection(enabled: false)]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentConnectionUnavailableForTenant, [Profile()], [Connection(tenantAvailable: false)]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentConnectionUnavailableForProject, [Profile()], [Connection(projectAvailable: false)]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentCredentialMissing, [Profile(provider: "openai")], [Connection(provider: "openai", credentialConfigured: false)]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentProviderUnsupported, [Profile(provider: "mystery")], [Connection(provider: "mystery")]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentProviderNotExecutable, [Profile(provider: "fake")], [Connection(provider: "fake")]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentModelMissing, [Profile(model: string.Empty)], [Connection()]);
    }

    [TestMethod]
    public void FakeConnection_IsNotExecutable_ButExplicitLocalTestDeterministicConnectionIs()
    {
        var fake = ProjectRunReadinessService.EvaluateAgent(
            SkeletonAgentRole.Builder,
            [Profile(provider: "fake")],
            [Connection(provider: "fake")]);
        var deterministic = ProjectRunReadinessService.EvaluateAgent(
            SkeletonAgentRole.Builder,
            [Profile(provider: "custom", model: "localtest-deterministic")],
            [Connection(provider: ProjectRunProviders.LocalTestDeterministic)]);

        Assert.IsFalse(fake.IsReady);
        Assert.AreEqual(ProjectRunReadinessReasonCodes.RunAgentProviderNotExecutable, fake.Blockers.Single().ReasonCode);
        Assert.IsTrue(deterministic.IsReady, string.Join("; ", deterministic.Blockers.Select(blocker => blocker.Reason)));
    }

    [TestMethod]
    public void Blocker_CarriesRoleProviderModelConnectionSourceAndRepairAction()
    {
        var result = ProjectRunReadinessService.EvaluateAgent(
            SkeletonAgentRole.Critic,
            [Profile(role: SkeletonAgentRole.Critic, provider: "fake")],
            [Connection(provider: "fake")]);
        var blocker = result.Blockers.Single();

        Assert.AreEqual(SkeletonAgentRole.Critic, blocker.Role);
        Assert.AreEqual("fake", blocker.EffectiveProvider);
        Assert.AreEqual("model-1", blocker.EffectiveModel);
        Assert.AreEqual("connection-1", blocker.ConnectionId);
        Assert.AreEqual("Project", blocker.SourceLayer);
        Assert.IsFalse(string.IsNullOrWhiteSpace(blocker.Reason));
        Assert.IsFalse(string.IsNullOrWhiteSpace(blocker.NextSafeAction));
    }

    private static void AssertCode(
        string expected,
        IReadOnlyList<EffectiveSkeletonAgentProfile> profiles,
        IReadOnlyList<AiConnectionMetadata> connections)
    {
        var result = ProjectRunReadinessService.EvaluateAgent(SkeletonAgentRole.Builder, profiles, connections);
        Assert.IsTrue(result.Blockers.Any(blocker => blocker.ReasonCode == expected),
            $"Expected {expected}; actual: {string.Join(", ", result.Blockers.Select(blocker => blocker.ReasonCode))}");
    }

    private static EffectiveSkeletonAgentProfile Profile(
        SkeletonAgentRole role = SkeletonAgentRole.Builder,
        string provider = "custom",
        string model = "model-1") => new()
    {
        Role = role,
        DisplayName = role.ToString(),
        AiConnectionId = "connection-1",
        Provider = provider,
        Model = model,
        TimeoutSeconds = 30,
        EffectiveSkill = string.Empty,
        EffectivePersonality = string.Empty,
        EffectiveHash = "hash",
        PublishedScopeLayer = "Project",
        FieldSources =
        [
            new SkeletonAgentProfileFieldSource
            {
                Field = nameof(SkeletonAgentProfile.AiConnectionId),
                SourceLayer = "Project",
                SourceLabel = "Project override",
                Inherited = false
            }
        ]
    };

    private static AiConnectionMetadata Connection(
        string provider = "custom",
        bool enabled = true,
        bool tenantAvailable = true,
        bool projectAvailable = true,
        bool credentialConfigured = true) => new()
    {
        Id = "connection-1",
        TenantId = 1,
        DisplayName = "Connection 1",
        ProviderKind = provider,
        ControlledEndpointId = "controlled-1",
        ControlledEndpoint = "deployment-configured",
        CredentialConfigured = credentialConfigured,
        CredentialStatus = credentialConfigured ? "Configured" : "Missing",
        Enabled = enabled,
        TenantAvailable = tenantAvailable,
        ProjectAvailable = projectAvailable,
        CreatedByUserId = 1,
        UpdatedByUserId = 1,
        Version = "test",
        Boundary = "non-secret"
    };
}
