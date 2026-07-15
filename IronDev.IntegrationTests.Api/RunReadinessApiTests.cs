using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IronDev.Core.Audit;
using IronDev.Core.Builder;
using IronDev.Core.Governance;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReadiness;
using IronDev.Core.Workflow;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class RunReadinessApiTests
{
    [TestMethod]
    public async Task StartRun_WhenExecutionReadinessIsBlocked_ReturnsCanonical409()
    {
        const string jwtKey = "irondev-run-readiness-api-test-key-32chars";
        Environment.SetEnvironmentVariable("IRONDEV_JWT_KEY", jwtKey);
        try
        {
            var blockedRuns = new BlockedSkeletonRunService();
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("Jwt:Issuer", "irondev-api");
                builder.UseSetting("Jwt:Audience", "irondev-client");
                builder.UseSetting("Ai:Provider", "fake");
                builder.UseSetting("Ai:Model", "test-model");
                builder.UseSetting("LocalTest:WorkspaceRoot", Path.Combine(Path.GetTempPath(), "IronDevTestWorkspaces"));
                builder.UseSetting("LocalTest:LogsRoot", Path.Combine(Path.GetTempPath(), "IronDevTestLogs"));
                builder.UseSetting("ConnectionStrings:IronDeveloperDb", "Server=127.0.0.1,1;Database=IronDev_RunReadiness_Test;Integrated Security=True;Encrypt=False;Connection Timeout=1;");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ai:Provider"] = "fake",
                    ["Ai:Model"] = "test-model",
                    ["Jwt:Key"] = jwtKey,
                    ["ConnectionStrings:IronDeveloperDb"] = "Server=127.0.0.1,1;Database=IronDev_RunReadiness_Test;Integrated Security=True;Encrypt=False;Connection Timeout=1;"
                }));
                builder.ConfigureServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                    services.RemoveAll<IProjectMembershipService>();
                    services.AddSingleton<IProjectMembershipService, AllowProjectMembershipService>();
                    services.RemoveAll<ITicketSkeletonRunService>();
                    services.AddSingleton<ITicketSkeletonRunService>(blockedRuns);
                    services.RemoveAll<IUserMutationAttributionStore>();
                    services.AddSingleton<IUserMutationAttributionStore, NoOpUserMutationAttributionStore>();
                });
            });
            using var client = factory.CreateClient();

            var response = await client.PostAsync("/api/projects/7/tickets/42/skeleton-runs", content: null);
            var refusal = await response.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();

            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
            Assert.IsNotNull(refusal);
            Assert.AreEqual("ProjectRunNotReady", refusal!.ReasonCode);
            Assert.AreEqual("/projects/7/library/settings/agents", refusal.TargetProductRoute);
            Assert.AreEqual(4, refusal.Blockers.Count);
            Assert.IsTrue(refusal.Blockers.All(blocker => blocker.ReasonCode == ProjectRunReadinessReasonCodes.RunAgentProviderNotExecutable));
            Assert.AreEqual(1, blockedRuns.StartAttempts);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_JWT_KEY", null);
        }
    }

    private sealed class BlockedSkeletonRunService : ITicketSkeletonRunService
    {
        public int StartAttempts { get; private set; }

        public Task<TicketBuildRunDto?> StartAsync(int projectId, long ticketId, CancellationToken cancellationToken = default)
        {
            StartAttempts++;
            var blockers = new[] { "Analyst", "Builder", "Tester", "Critic" }
                .Select(role => new ProjectRunReadinessBlocker
                {
                    Role = Enum.Parse<IronDev.Core.Agents.SkeletonAgentRole>(role),
                    EffectiveProvider = "fake",
                    EffectiveModel = "gpt-4o",
                    ConnectionId = "deployment-default",
                    SourceLayer = "BuiltIn",
                    ReasonCode = ProjectRunReadinessReasonCodes.RunAgentProviderNotExecutable,
                    Reason = $"Provider 'fake' cannot execute {role}.",
                    NextSafeAction = "Test an executable connection and publish the project profile."
                })
                .ToArray();
            throw new ProjectRunReadinessBlockedException(new ProjectRunReadiness
            {
                ProjectId = projectId,
                ProjectSetupReady = true,
                ExecutionReady = false,
                ReadyToRun = false,
                State = ProjectRunReadinessStates.RunConfigurationRequired,
                BlockedCount = 4,
                Blockers = blockers,
                NextAction = new ProjectRunReadinessNextAction
                {
                    Kind = "ConfigureRunAgents",
                    Label = "Configure run agents",
                    NextSafeAction = "Configure run agents.",
                    TargetProductRoute = $"/projects/{projectId}/library/settings/agents"
                }
            });
        }

        public Task<SkeletonCriticPackage?> GetCriticPackageAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default) => Task.FromResult<SkeletonCriticPackage?>(null);
        public Task<TicketBuildRunDto?> ContinueAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default) => Task.FromResult<TicketBuildRunDto?>(null);
        public Task<TicketBuildRunDto?> ReviseAsync(int projectId, long ticketId, string runId, SkeletonRunRevisionRequest request, CancellationToken cancellationToken = default) => Task.FromResult<TicketBuildRunDto?>(null);
        public Task<TicketBuildRunDto?> ApplyAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default) => Task.FromResult<TicketBuildRunDto?>(null);
        public Task<SkeletonRunReport?> GetRunReportAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default) => Task.FromResult<SkeletonRunReport?>(null);
    }

    private sealed class AllowProjectMembershipService : IProjectMembershipService
    {
        public Task<bool> HasAccessAsync(int tenantId, int projectId, int userId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlySet<int>> GetAccessibleProjectIdsAsync(int tenantId, int userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<int>>(new HashSet<int> { 7 });
        public Task<IReadOnlyList<ProjectMembershipEntry>> GetMembersAsync(int tenantId, int projectId, int currentUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectMembershipEntry>>([]);
        public Task<ProjectMembershipMutationStatus> SetMemberAsync(int tenantId, int projectId, int userId, int actorUserId, string projectRole, CancellationToken cancellationToken = default) => Task.FromResult(ProjectMembershipMutationStatus.Succeeded);
        public Task<ProjectMembershipMutationStatus> RemoveMemberAsync(int tenantId, int projectId, int userId, int actorUserId, CancellationToken cancellationToken = default) => Task.FromResult(ProjectMembershipMutationStatus.Succeeded);
    }

    private sealed class NoOpUserMutationAttributionStore : IUserMutationAttributionStore
    {
        public Task AppendAsync(UserMutationAttributionRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "RunReadinessTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Claim[] claims =
            [
                new(ClaimTypes.NameIdentifier, "7"),
                new("sub", "7"),
                new("tenant_id", "1")
            ];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
