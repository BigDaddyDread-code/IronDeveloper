using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Workbench;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkbenchAgentRunSubmissionAvailabilityTests
{
    [TestMethod]
    public async Task InvalidProviderConfiguration_FailsClosedWithoutInvokingProvider()
    {
        var profile = AnalystProfile();
        var profiles = Profiles(profile);
        var resolver = new Mock<IAgentLlmResolver>(MockBehavior.Strict);
        resolver.Setup(value => value.ResolveAsync(
                profile,
                3,
                7,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unknown provider"));

        var availability = await Service(profiles.Object, resolver.Object).CheckAsync(3, 7);

        Assert.IsFalse(availability.IsAvailable);
        Assert.AreEqual(WorkbenchAgentRunFailureCategories.Configuration, availability.FailureCategory);
    }

    [TestMethod]
    public async Task LegacyStringOnlyProvider_FailsClosedBeforeSubmission()
    {
        var profile = AnalystProfile();
        var profiles = Profiles(profile);
        var resolver = new Mock<IAgentLlmResolver>(MockBehavior.Strict);
        resolver.Setup(value => value.ResolveAsync(
                profile,
                3,
                7,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkeletonAgentLlm
            {
                Role = SkeletonAgentRole.Analyst,
                Provider = "legacy",
                Model = "legacy-model",
                TimeoutSeconds = 30,
                Llm = new LegacyLlm()
            });

        var availability = await Service(profiles.Object, resolver.Object).CheckAsync(3, 7);

        Assert.IsFalse(availability.IsAvailable);
        Assert.AreEqual(WorkbenchAgentRunFailureCategories.Configuration, availability.FailureCategory);
    }

    [TestMethod]
    public async Task RoleAwareConfiguredProvider_IsAvailableWithoutNetworkCall()
    {
        var profile = AnalystProfile();
        var profiles = Profiles(profile);
        var provider = new RoleAwareLlm();
        var resolver = new Mock<IAgentLlmResolver>(MockBehavior.Strict);
        resolver.Setup(value => value.ResolveAsync(
                profile,
                3,
                7,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkeletonAgentLlm
            {
                Role = SkeletonAgentRole.Analyst,
                Provider = "openai",
                Model = "configured-model",
                TimeoutSeconds = 30,
                Llm = provider
            });

        var availability = await Service(profiles.Object, resolver.Object).CheckAsync(3, 7);

        Assert.IsTrue(availability.IsAvailable);
        Assert.IsNull(availability.FailureCategory);
        Assert.AreEqual(0, provider.InvocationCount);
    }

    [TestMethod]
    public async Task ExplicitLocalTestDeterministicMode_UsesTheGatewayBypassWithoutResolver()
    {
        var profile = AnalystProfile();
        var profiles = Profiles(profile);
        var resolver = new Mock<IAgentLlmResolver>(MockBehavior.Strict);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkbenchBusinessAnalyst:LocalTestDeterministicEnabled"] = "true"
            })
            .Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns("LocalTest");

        var service = new ConfigurationWorkbenchAgentRunSubmissionAvailability(
            true,
            profiles.Object,
            resolver.Object,
            configuration,
            environment.Object);

        var availability = await service.CheckAsync(3, 7);

        Assert.IsTrue(availability.IsAvailable);
        resolver.VerifyNoOtherCalls();
    }

    private static ConfigurationWorkbenchAgentRunSubmissionAvailability Service(
        ISkeletonAgentProfileService profiles,
        IAgentLlmResolver resolver)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns("Production");
        return new ConfigurationWorkbenchAgentRunSubmissionAvailability(
            true,
            profiles,
            resolver,
            new ConfigurationBuilder().Build(),
            environment.Object);
    }

    private static Mock<ISkeletonAgentProfileService> Profiles(EffectiveSkeletonAgentProfile profile)
    {
        var profiles = new Mock<ISkeletonAgentProfileService>(MockBehavior.Strict);
        profiles.Setup(value => value.ListEffectiveAsync(3, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync([profile]);
        return profiles;
    }

    private static EffectiveSkeletonAgentProfile AnalystProfile() => new()
    {
        Role = SkeletonAgentRole.Analyst,
        DisplayName = "Analyst",
        Provider = "openai",
        Model = "configured-model",
        TimeoutSeconds = 30,
        EffectiveSkill = string.Empty,
        EffectivePersonality = string.Empty,
        EffectiveHash = "effective-analyst-hash"
    };

    private sealed class LegacyLlm : ILLMService
    {
        public Task<string> GetResponseAsync(string prompt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Readiness must not invoke the provider.");
    }

    private sealed class RoleAwareLlm : ILLMService, IWorkbenchBusinessAnalystRoleAwareLlmService
    {
        public int InvocationCount { get; private set; }

        public Task<string> GetResponseAsync(string prompt, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            throw new InvalidOperationException("Readiness must not invoke the provider.");
        }

        public Task<WorkbenchBusinessAnalystProviderResponse> GetResponseAsync(
            WorkbenchBusinessAnalystProviderEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            throw new InvalidOperationException("Readiness must not invoke the provider.");
        }
    }
}
