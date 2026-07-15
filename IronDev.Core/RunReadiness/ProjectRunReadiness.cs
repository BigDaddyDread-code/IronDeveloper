using IronDev.Core.Agents;
using IronDev.Core.Provisioning;

namespace IronDev.Core.RunReadiness;

public static class ProjectRunReadinessStates
{
    public const string SetupIncomplete = "SetupIncomplete";
    public const string RunConfigurationRequired = "RunConfigurationRequired";
    public const string ReadyToRun = "ReadyToRun";
}

public static class ProjectRunReadinessReasonCodes
{
    public const string RunAgentProfileMissing = "RunAgentProfileMissing";
    public const string RunAgentConnectionMissing = "RunAgentConnectionMissing";
    public const string RunAgentConnectionDisabled = "RunAgentConnectionDisabled";
    public const string RunAgentConnectionUnavailableForTenant = "RunAgentConnectionUnavailableForTenant";
    public const string RunAgentConnectionUnavailableForProject = "RunAgentConnectionUnavailableForProject";
    public const string RunAgentCredentialMissing = "RunAgentCredentialMissing";
    public const string RunAgentProviderUnsupported = "RunAgentProviderUnsupported";
    public const string RunAgentProviderNotExecutable = "RunAgentProviderNotExecutable";
    public const string RunAgentModelMissing = "RunAgentModelMissing";
}

public static class ProjectRunProviders
{
    public const string LocalTestDeterministic = "alpha-smoke-deterministic";

    public static bool IsRecognized(string provider) => provider.Trim().ToLowerInvariant() is
        "openai" or "localopenai" or "ollama" or "custom" or SkeletonAgentProviders.Fake or LocalTestDeterministic;

    public static bool IsExecutable(string provider) =>
        IsRecognized(provider) && !provider.Trim().Equals(SkeletonAgentProviders.Fake, StringComparison.OrdinalIgnoreCase);

    public static bool RequiresCredential(string provider) => provider.Trim().ToLowerInvariant() is "openai" or "custom";
}

public sealed record ProjectRunReadinessBlocker
{
    public required SkeletonAgentRole Role { get; init; }
    public string EffectiveProvider { get; init; } = string.Empty;
    public string EffectiveModel { get; init; } = string.Empty;
    public string ConnectionId { get; init; } = string.Empty;
    public string SourceLayer { get; init; } = string.Empty;
    public required string ReasonCode { get; init; }
    public required string Reason { get; init; }
    public required string NextSafeAction { get; init; }
}

public sealed record ProjectRunAgentReadiness
{
    public required SkeletonAgentRole Role { get; init; }
    public required bool IsReady { get; init; }
    public string EffectiveProvider { get; init; } = string.Empty;
    public string EffectiveModel { get; init; } = string.Empty;
    public string ConnectionId { get; init; } = string.Empty;
    public string SourceLayer { get; init; } = string.Empty;
    public string ConnectionHealth { get; init; } = "NotTested";
    public IReadOnlyList<ProjectRunReadinessBlocker> Blockers { get; init; } = [];
}

public sealed record ProjectRunReadinessNextAction
{
    public string Kind { get; init; } = "None";
    public string Label { get; init; } = string.Empty;
    public string NextSafeAction { get; init; } = string.Empty;
    public string TargetProductRoute { get; init; } = string.Empty;
}

public sealed record ProjectRunReadiness
{
    public const string BoundaryText =
        "Run readiness is computed from project setup, effective published agent profiles, non-secret AI connection metadata, and executable provider support. " +
        "It starts no run, publishes no profile, changes no credential, and grants no authority.";

    public int ProjectId { get; init; }
    public bool ProjectSetupReady { get; init; }
    public bool ExecutionReady { get; init; }
    public bool ReadyToRun { get; init; }
    public string State { get; init; } = ProjectRunReadinessStates.SetupIncomplete;
    public int BlockedCount { get; init; }
    public ProjectProvisioningReadiness? Provisioning { get; init; }
    public IReadOnlyList<ProjectRunAgentReadiness> Agents { get; init; } = [];
    public IReadOnlyList<ProjectRunReadinessBlocker> Blockers { get; init; } = [];
    public ProjectRunReadinessNextAction NextAction { get; init; } = new();
    public string Boundary { get; init; } = BoundaryText;
}

public interface IProjectRunReadinessService
{
    Task<ProjectRunReadiness> EvaluateAsync(int projectId, CancellationToken cancellationToken = default);
}

public sealed class ProjectRunReadinessBlockedException(ProjectRunReadiness readiness)
    : InvalidOperationException("The project is not ready to start a governed run.")
{
    public ProjectRunReadiness Readiness { get; } = readiness;
}
