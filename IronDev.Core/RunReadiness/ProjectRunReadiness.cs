using IronDev.Core.Agents;
using IronDev.Core.Provisioning;

namespace IronDev.Core.RunReadiness;

public static class ProjectRunReadinessStates
{
    public const string SetupIncomplete = "SetupIncomplete";
    public const string RunConfigurationRequired = "RunConfigurationRequired";
    public const string ProjectWorkSessionRequired = "ProjectWorkSessionRequired";
    public const string ReadyToRun = "ReadyToRun";
}

public static class ProjectApplyCapabilityReasonCodes
{
    public const string Ready = "ProjectApplyCapabilityReady";
    public const string NotRequired = "ProjectApplyCapabilityNotRequired";
    public const string ProjectApplyCapabilityDisabled = "ProjectApplyCapabilityDisabled";
    public const string ProjectApplyLauncherCapabilityMissing = "ProjectApplyLauncherCapabilityMissing";
    public const string ProjectApplySessionIdentityMismatch = "ProjectApplySessionIdentityMismatch";
    public const string ProjectApplySandboxRootMissing = "ProjectApplySandboxRootMissing";
    public const string ProjectApplySandboxRootUnsafe = "ProjectApplySandboxRootUnsafe";
    public const string ProjectApplyPathOutsideSandbox = "ProjectApplyPathOutsideSandbox";
    public const string ProjectApplyPathIsRoot = "ProjectApplyPathIsRoot";
    public const string ProjectApplyPathReparsePoint = "ProjectApplyPathReparsePoint";
    public const string ProjectApplyProjectNotDisposable = "ProjectApplyProjectNotDisposable";
    public const string ProjectApplyQualificationAuthorityMissing = "ProjectApplyQualificationAuthorityMissing";
    public const string ProjectApplyQualificationMissing = "ProjectApplyQualificationMissing";
    public const string ProjectApplyQualificationInvalid = "ProjectApplyQualificationInvalid";
    public const string ProjectApplyQualificationBindingMismatch = "ProjectApplyQualificationBindingMismatch";
    public const string ProjectApplyQualificationMarkerMissing = "ProjectApplyQualificationMarkerMissing";
    public const string ProjectApplyQualificationMarkerMismatch = "ProjectApplyQualificationMarkerMismatch";
}

public static class ProjectApplyCapabilityCommands
{
    public const string RestartInSandboxApplyMode =
        @".\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset -EnableSandboxApply";
}

public sealed record ProjectApplyCapability
{
    public int ProjectId { get; init; }
    public bool IsReady { get; init; }
    public string State { get; init; } = "Disabled";
    public string ReasonCode { get; init; } = ProjectApplyCapabilityReasonCodes.ProjectApplyCapabilityDisabled;
    public string Reason { get; init; } = string.Empty;
    public string NextSafeAction { get; init; } = ProjectApplyCapabilityCommands.RestartInSandboxApplyMode;
    public string SessionMode { get; init; } = string.Empty;
    public string LauncherSessionId { get; init; } = string.Empty;
    public string RepositoryCommit { get; init; } = string.Empty;
    public string SandboxRoot { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public string SandboxRootFingerprint { get; init; } = string.Empty;
    public string ProjectPathFingerprint { get; init; } = string.Empty;
    public string QualificationId { get; init; } = string.Empty;
    public string QualificationFingerprint { get; init; } = string.Empty;
    public string ReadinessEvidenceHash { get; init; } = string.Empty;
}

public interface IProjectApplyCapabilityService
{
    Task<ProjectApplyCapability> EvaluateAsync(int projectId, CancellationToken cancellationToken = default);

    Task<ProjectApplyCapability> QualifyDisposableProjectAsync(
        int projectId,
        int qualifyingActorUserId,
        CancellationToken cancellationToken = default);
}

public static class ProjectRunReadinessReasonCodes
{
    public const string RunAgentProfileMissing = "RunAgentProfileMissing";
    public const string RunAgentConnectionMissing = "RunAgentConnectionMissing";
    public const string RunAgentConnectionDisabled = "RunAgentConnectionDisabled";
    public const string RunAgentConnectionUnavailableForTenant = "RunAgentConnectionUnavailableForTenant";
    public const string RunAgentConnectionUnavailableForProject = "RunAgentConnectionUnavailableForProject";
    public const string RunAgentConnectionPurposeMismatch = "RunAgentConnectionPurposeMismatch";
    public const string RunAgentCredentialMissing = "RunAgentCredentialMissing";
    public const string RunAgentProviderUnsupported = "RunAgentProviderUnsupported";
    public const string RunAgentProviderNotExecutable = "RunAgentProviderNotExecutable";
    public const string RunAgentModelMissing = "RunAgentModelMissing";
}

public static class ProjectRunPurposes
{
    public const string SmokeSimulation = "SmokeSimulation";
    public const string ProjectFeatureWork = "ProjectFeatureWork";

    public static bool IsSupported(string purpose) => purpose is SmokeSimulation or ProjectFeatureWork;
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
    public string Command { get; init; } = string.Empty;
}

public sealed record ProjectRunReadiness
{
    public const string BoundaryText =
        "Run readiness is computed from project setup, effective published agent profiles, non-secret AI connection metadata, executable provider support, and the purpose-specific completion capability. " +
        "It starts no run, publishes no profile, changes no credential, and grants no authority.";

    public int ProjectId { get; init; }
    public string RequiredPurpose { get; init; } = ProjectRunPurposes.ProjectFeatureWork;
    public bool ProjectSetupReady { get; init; }
    public bool ExecutionReady { get; init; }
    public bool CompletionCapabilityReady { get; init; }
    public bool ReadyToRun { get; init; }
    public string State { get; init; } = ProjectRunReadinessStates.SetupIncomplete;
    public int BlockedCount { get; init; }
    public ProjectProvisioningReadiness? Provisioning { get; init; }
    public IReadOnlyList<ProjectRunAgentReadiness> Agents { get; init; } = [];
    public IReadOnlyList<ProjectRunReadinessBlocker> Blockers { get; init; } = [];
    public ProjectApplyCapability? CompletionCapability { get; init; }
    public ProjectRunReadinessNextAction NextAction { get; init; } = new();
    public string Boundary { get; init; } = BoundaryText;
}

public interface IProjectRunReadinessService
{
    Task<ProjectRunReadiness> EvaluateAsync(int projectId, CancellationToken cancellationToken = default);

    Task<ProjectRunReadiness> EvaluateForPurposeAsync(
        int projectId,
        string requiredPurpose,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(requiredPurpose, ProjectRunPurposes.ProjectFeatureWork, StringComparison.Ordinal))
            throw new NotSupportedException($"Run purpose '{requiredPurpose}' is not supported by this readiness service.");

        return EvaluateAsync(projectId, cancellationToken);
    }
}

public sealed class ProjectRunReadinessBlockedException(ProjectRunReadiness readiness)
    : InvalidOperationException("The project is not ready to start a governed run.")
{
    public ProjectRunReadiness Readiness { get; } = readiness;
}
