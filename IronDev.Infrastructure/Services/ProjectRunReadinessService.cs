using IronDev.Core.Agents;
using IronDev.Core.AiConnections;
using IronDev.Core.Auth;
using IronDev.Core.Provisioning;
using IronDev.Core.RunReadiness;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectRunReadinessService : IProjectRunReadinessService
{
    private static readonly SkeletonAgentRole[] RequiredExecutionRoles =
    [
        SkeletonAgentRole.Analyst,
        SkeletonAgentRole.Builder,
        SkeletonAgentRole.Tester,
        SkeletonAgentRole.Critic
    ];

    private readonly IProjectProvisioningReadinessService _provisioning;
    private readonly ISkeletonAgentProfileService _profiles;
    private readonly IAiConnectionCatalogService _connections;
    private readonly ICurrentTenantContext _tenant;

    public ProjectRunReadinessService(
        IProjectProvisioningReadinessService provisioning,
        ISkeletonAgentProfileService profiles,
        IAiConnectionCatalogService connections,
        ICurrentTenantContext tenant)
    {
        _provisioning = provisioning;
        _profiles = profiles;
        _connections = connections;
        _tenant = tenant;
    }

    public Task<ProjectRunReadiness> EvaluateAsync(int projectId, CancellationToken cancellationToken = default) =>
        EvaluateForPurposeAsync(projectId, ProjectRunPurposes.ProjectFeatureWork, cancellationToken);

    public async Task<ProjectRunReadiness> EvaluateForPurposeAsync(
        int projectId,
        string requiredPurpose,
        CancellationToken cancellationToken = default)
    {
        if (!ProjectRunPurposes.IsSupported(requiredPurpose))
            throw new ArgumentOutOfRangeException(nameof(requiredPurpose), requiredPurpose, "Unknown governed run purpose.");

        var provisioningTask = _provisioning.EvaluateAsync(projectId, cancellationToken);
        var profilesTask = _profiles.ListEffectiveAsync(_tenant.TenantId, projectId, cancellationToken);
        var connectionsTask = _connections.ListAsync(_tenant.TenantId, userId: 0, cancellationToken);
        await Task.WhenAll(provisioningTask, profilesTask, connectionsTask).ConfigureAwait(false);

        var provisioning = await provisioningTask.ConfigureAwait(false);
        var profiles = await profilesTask.ConfigureAwait(false);
        var connections = await connectionsTask.ConfigureAwait(false);
        var agents = RequiredExecutionRoles.Select(role => EvaluateAgent(role, profiles, connections, requiredPurpose)).ToArray();
        var blockers = agents.SelectMany(agent => agent.Blockers).ToArray();
        var setupReady = provisioning?.IsReady == true;
        var executionReady = blockers.Length == 0;
        var ready = setupReady && executionReady;
        var state = !setupReady
            ? ProjectRunReadinessStates.SetupIncomplete
            : executionReady
                ? ProjectRunReadinessStates.ReadyToRun
                : ProjectRunReadinessStates.RunConfigurationRequired;

        return new ProjectRunReadiness
        {
            ProjectId = projectId,
            RequiredPurpose = requiredPurpose,
            ProjectSetupReady = setupReady,
            ExecutionReady = executionReady,
            ReadyToRun = ready,
            State = state,
            BlockedCount = agents.Count(agent => !agent.IsReady),
            Provisioning = provisioning,
            Agents = agents,
            Blockers = blockers,
            NextAction = NextAction(projectId, state, provisioning, blockers)
        };
    }

    public static ProjectRunAgentReadiness EvaluateAgent(
        SkeletonAgentRole role,
        IReadOnlyList<EffectiveSkeletonAgentProfile> profiles,
        IReadOnlyList<AiConnectionMetadata> connections,
        string requiredPurpose = ProjectRunPurposes.ProjectFeatureWork)
    {
        var profile = profiles.FirstOrDefault(item => item.Role == role);
        if (profile is null)
            return Agent(role, string.Empty, string.Empty, string.Empty, string.Empty, "NotTested",
                Blocker(role, string.Empty, string.Empty, string.Empty, string.Empty,
                    ProjectRunReadinessReasonCodes.RunAgentProfileMissing,
                    $"No effective published profile resolves for {SkeletonAgentRoles.DisplayName(role)}.",
                    "Open project agent profiles, save and test a draft, then publish it."));

        var source = profile.FieldSources
            .FirstOrDefault(field => field.Field.Equals(nameof(SkeletonAgentProfile.AiConnectionId), StringComparison.OrdinalIgnoreCase))
            ?.SourceLayer ?? profile.PublishedScopeLayer;
        var connection = connections.FirstOrDefault(item =>
            item.Id.Equals(profile.AiConnectionId, StringComparison.OrdinalIgnoreCase));
        var provider = connection?.ProviderKind?.Trim() ?? profile.Provider.Trim();
        var common = new List<ProjectRunReadinessBlocker>();

        if (string.IsNullOrWhiteSpace(profile.AiConnectionId) || connection is null)
            common.Add(Blocker(role, provider, profile.Model, profile.AiConnectionId, source,
                ProjectRunReadinessReasonCodes.RunAgentConnectionMissing,
                $"{SkeletonAgentRoles.DisplayName(role)} references an AI connection that does not exist.",
                "Open AI Connections, choose an available connection, then publish the project agent profile."));
        else
        {
            if (!connection.Enabled)
                common.Add(Blocker(role, provider, profile.Model, connection.Id, source,
                    ProjectRunReadinessReasonCodes.RunAgentConnectionDisabled,
                    $"AI connection '{connection.DisplayName}' is disabled for {SkeletonAgentRoles.DisplayName(role)}.",
                    "Open AI Connections and enable or replace the connection, then publish the project agent profile."));
            if (!connection.TenantAvailable)
                common.Add(Blocker(role, provider, profile.Model, connection.Id, source,
                    ProjectRunReadinessReasonCodes.RunAgentConnectionUnavailableForTenant,
                    $"AI connection '{connection.DisplayName}' is unavailable to this tenant.",
                    "Open AI Connections and select a tenant-available connection."));
            if (!connection.ProjectAvailable)
                common.Add(Blocker(role, provider, profile.Model, connection.Id, source,
                    ProjectRunReadinessReasonCodes.RunAgentConnectionUnavailableForProject,
                    $"AI connection '{connection.DisplayName}' is unavailable to this project.",
                    "Open AI Connections and select a project-available connection."));
            if (ProjectRunProviders.IsExecutable(provider) &&
                !connection.SupportedPurposes.Contains(requiredPurpose, StringComparer.Ordinal))
                common.Add(Blocker(role, provider, profile.Model, connection.Id, source,
                    ProjectRunReadinessReasonCodes.RunAgentConnectionPurposeMismatch,
                    connection.ProviderKind.Equals(ProjectRunProviders.LocalTestDeterministic, StringComparison.OrdinalIgnoreCase)
                        ? "LocalTest deterministic is a fixed smoke-test connection. It can exercise the governed workflow, but it cannot implement this Work Item. Configure an executable project-work connection to continue."
                        : $"AI connection '{connection.DisplayName}' does not support governed run purpose '{requiredPurpose}'.",
                    requiredPurpose == ProjectRunPurposes.ProjectFeatureWork
                        ? "Configure an executable project-work connection, then deliberately publish it on the project agent profile."
                        : "Select a connection that explicitly supports workflow smoke simulation."));
            if (!ProjectRunProviders.IsRecognized(provider))
                common.Add(Blocker(role, provider, profile.Model, connection.Id, source,
                    ProjectRunReadinessReasonCodes.RunAgentProviderUnsupported,
                    $"Provider '{provider}' is not recognized by the governed run resolver.",
                    "Open AI Connections and select a supported executable provider."));
            else if (!ProjectRunProviders.IsExecutable(provider))
                common.Add(Blocker(role, provider, profile.Model, connection.Id, source,
                    ProjectRunReadinessReasonCodes.RunAgentProviderNotExecutable,
                    $"Provider '{provider}' cannot execute {SkeletonAgentRoles.DisplayName(role)}; its runtime service always refuses model calls.",
                    "Open AI Connections, test an executable connection, then publish it on this project agent profile."));
            if (ProjectRunProviders.RequiresCredential(provider) && !connection.CredentialConfigured)
                common.Add(Blocker(role, provider, profile.Model, connection.Id, source,
                    ProjectRunReadinessReasonCodes.RunAgentCredentialMissing,
                    $"AI connection '{connection.DisplayName}' has no configured credential.",
                    "Open AI Connections, configure the credential, and test the connection."));
        }

        if (string.IsNullOrWhiteSpace(profile.Model))
            common.Add(Blocker(role, provider, profile.Model, profile.AiConnectionId, source,
                ProjectRunReadinessReasonCodes.RunAgentModelMissing,
                $"{SkeletonAgentRoles.DisplayName(role)} has no effective model.",
                "Open project agent profiles, select a model, save and test the draft, then publish it."));

        return Agent(role, provider, profile.Model, profile.AiConnectionId, source, Health(connection), common.ToArray());
    }

    private static ProjectRunAgentReadiness Agent(
        SkeletonAgentRole role,
        string provider,
        string model,
        string connectionId,
        string source,
        string health,
        params ProjectRunReadinessBlocker[] blockers) => new()
    {
        Role = role,
        IsReady = blockers.Length == 0,
        EffectiveProvider = provider,
        EffectiveModel = model,
        ConnectionId = connectionId,
        SourceLayer = source,
        ConnectionHealth = health,
        Blockers = blockers
    };

    private static ProjectRunReadinessBlocker Blocker(
        SkeletonAgentRole role,
        string provider,
        string model,
        string connectionId,
        string source,
        string reasonCode,
        string reason,
        string nextSafeAction) => new()
    {
        Role = role,
        EffectiveProvider = provider,
        EffectiveModel = model,
        ConnectionId = connectionId,
        SourceLayer = source,
        ReasonCode = reasonCode,
        Reason = reason,
        NextSafeAction = nextSafeAction
    };

    private static string Health(AiConnectionMetadata? connection)
    {
        if (connection is null) return "NotTested";
        if (connection.LastSuccessfulTestUtc is null && connection.LastFailedTestUtc is null) return "NotTested";
        return connection.LastSuccessfulTestUtc is not null &&
               (connection.LastFailedTestUtc is null || connection.LastSuccessfulTestUtc >= connection.LastFailedTestUtc)
            ? "Passed"
            : "Failed";
    }

    private static ProjectRunReadinessNextAction NextAction(
        int projectId,
        string state,
        ProjectProvisioningReadiness? provisioning,
        IReadOnlyList<ProjectRunReadinessBlocker> blockers) => state switch
    {
        ProjectRunReadinessStates.SetupIncomplete => new ProjectRunReadinessNextAction
        {
            Kind = "ResolveProjectSetup",
            Label = provisioning?.NextAction.Label ?? "Resolve project setup",
            NextSafeAction = provisioning?.NextAction.NextSafeAction ?? "Complete project setup and re-check readiness.",
            TargetProductRoute = $"/projects/{projectId}/setup"
        },
        ProjectRunReadinessStates.RunConfigurationRequired => new ProjectRunReadinessNextAction
        {
            Kind = blockers.Any(blocker => blocker.ReasonCode == ProjectRunReadinessReasonCodes.RunAgentConnectionPurposeMismatch)
                ? "ConfigureProjectWorkConnection"
                : "ConfigureRunAgents",
            Label = blockers.Any(blocker => blocker.ReasonCode == ProjectRunReadinessReasonCodes.RunAgentConnectionPurposeMismatch)
                ? "Configure project-work connection"
                : "Configure run agents",
            NextSafeAction = blockers.Any(blocker => blocker.ReasonCode == ProjectRunReadinessReasonCodes.RunAgentConnectionPurposeMismatch)
                ? "Configure and test an executable project-work connection. Publishing project profiles remains a separate deliberate action."
                : "Open AI Connections, test an executable connection, then save, test, and publish each project agent profile before re-checking.",
            TargetProductRoute = blockers.Any(blocker => blocker.ReasonCode == ProjectRunReadinessReasonCodes.RunAgentConnectionPurposeMismatch)
                ? $"/projects/{projectId}/library/settings/ai-connections"
                : $"/projects/{projectId}/library/settings/agents"
        },
        _ => new ProjectRunReadinessNextAction
        {
            Kind = "StartRun",
            Label = "Ready to run",
            NextSafeAction = "Open a Work Item and start a governed run."
        }
    };
}
