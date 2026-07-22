using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Sandbox;

public static class SandboxPolicyVersions
{
    public const string WorkbenchV01 = "irondev-workbench-sandbox-v0.1-policy-2";
}

public static class SandboxIsolationModes
{
    public const string HcsHyperV = "HcsHyperV";
}

public static class SandboxCapabilityStates
{
    public const string Available = "Available";
    public const string Disabled = "Disabled";
    public const string UnsupportedHost = "UnsupportedHost";
    public const string Unavailable = "Unavailable";
    public const string Unsafe = "Unsafe";
}

public static class SandboxReasonCodes
{
    public const string Ready = "SandboxReady";
    public const string Disabled = "SandboxDisabled";
    public const string WindowsX64Required = "SandboxWindowsX64Required";
    public const string RuntimeUnavailable = "SandboxRuntimeUnavailable";
    public const string ProfileInvalid = "SandboxProfileInvalid";
    public const string ImageNotDigestPinned = "SandboxImageNotDigestPinned";
    public const string OfflineFeedUnavailable = "SandboxOfflineFeedUnavailable";
    public const string OfflineFeedIntegrityFailed = "SandboxOfflineFeedIntegrityFailed";
    public const string UnsafeHostPath = "SandboxUnsafeHostPath";
    public const string PolicyIntegrityFailed = "SandboxPolicyIntegrityFailed";
    public const string ExecutionRejected = "SandboxExecutionRejected";
    public const string IsolationInspectionFailed = "SandboxIsolationInspectionFailed";
    public const string CleanupFailed = "SandboxCleanupFailed";
    public const string HostRestartRecovered = "SandboxHostRestartRecovered";
}

public static class SandboxQualificationStates
{
    public const string Running = "Running";
    public const string Passed = "Passed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Recovered = "Recovered";
}

public enum SandboxExecutionStage
{
    Restore = 1,
    Build = 2,
    Test = 3
}

public enum SandboxExecutionStatus
{
    Succeeded = 1,
    Failed = 2,
    TimedOut = 3,
    Cancelled = 4,
    Rejected = 5
}

/// <summary>
/// The exact, versioned v0.1 resource envelope. Callers cannot widen it.
/// </summary>
public sealed record SandboxResourcePolicy(
    int VirtualCpuCount,
    int MemoryMaximumMiB,
    int WritableScratchMaximumGiB,
    int MaximumUntrustedWorkloadProcessCount,
    int RestoreTimeoutSeconds,
    int BuildTimeoutSeconds,
    int TestTimeoutSeconds,
    int TotalExecutionTimeoutSeconds,
    int NetworkEndpointCount,
    int HostWritableMountCount)
{
    public static SandboxResourcePolicy WorkbenchV01 { get; } = new(
        VirtualCpuCount: 2,
        MemoryMaximumMiB: 4096,
        WritableScratchMaximumGiB: 12,
        MaximumUntrustedWorkloadProcessCount: 64,
        RestoreTimeoutSeconds: 5 * 60,
        BuildTimeoutSeconds: 5 * 60,
        TestTimeoutSeconds: 10 * 60,
        TotalExecutionTimeoutSeconds: 20 * 60,
        NetworkEndpointCount: 0,
        HostWritableMountCount: 0);
}

/// <summary>
/// Server-owned execution-profile material used to resolve an immutable sandbox policy.
/// This is deliberately not a browser command model.
/// </summary>
public sealed record SandboxExecutionProfileBinding
{
    public required string ProfileDefinitionId { get; init; }
    public required int ProfileDescriptorRevision { get; init; }
    public required string DescriptorSha256 { get; init; }
    public required string TemplateBundleSha256 { get; init; }
    public required string ToolchainManifestId { get; init; }
    public required string ExecutionImageReference { get; init; }
    public required string RestoreCommand { get; init; }
    public required string BuildCommand { get; init; }
    public required string TestCommand { get; init; }
}

public sealed record SandboxCommandPolicy(
    SandboxExecutionStage Stage,
    string CommandText,
    string CommandSha256,
    int TimeoutSeconds);

/// <summary>
/// A resolved policy is a complete immutable execution authority. Repository/feed mounts,
/// commands, environment names, image and limits are selected by trusted server code only.
/// </summary>
public sealed record SandboxRuntimePolicy
{
    public required int SchemaVersion { get; init; }
    public required string PolicyVersion { get; init; }
    public required string IsolationMode { get; init; }
    public required string ProfileDefinitionId { get; init; }
    public required int ProfileDescriptorRevision { get; init; }
    public required string DescriptorSha256 { get; init; }
    public required string TemplateBundleSha256 { get; init; }
    public required string ToolchainManifestId { get; init; }
    public required string ContainerImageReference { get; init; }
    public required string ContainerImageDigest { get; init; }
    public required string OfflineFeedPath { get; init; }
    public required string OfflineFeedManifestSha256 { get; init; }
    public required bool RepositoryInputReadOnly { get; init; }
    public required bool OfflineFeedReadOnly { get; init; }
    public required string TrustedSupervisorVersion { get; init; }
    public required string TrustedSupervisorSha256 { get; init; }
    public required SandboxResourcePolicy Resources { get; init; }
    public required SandboxCommandPolicy Restore { get; init; }
    public required SandboxCommandPolicy Build { get; init; }
    public required SandboxCommandPolicy Test { get; init; }
    public IReadOnlyList<string> EnvironmentAllowList { get; init; } = [];
    public required string PolicySha256 { get; init; }
}

/// <summary>
/// Product-safe capability projection. It intentionally contains no host paths, image
/// references, mount topology, environment values or resource controls.
/// </summary>
public sealed record SandboxCapability(
    string State,
    string ReasonCode,
    string Message,
    string PolicyVersion,
    string? PolicySha256);

public sealed record SandboxPolicyResolution(
    SandboxCapability Capability,
    SandboxRuntimePolicy? Policy)
{
    public bool IsAvailable =>
        string.Equals(Capability.State, SandboxCapabilityStates.Available, StringComparison.Ordinal) &&
        Policy is not null;
}

public interface ISandboxRuntimePolicyCatalog
{
    SandboxPolicyResolution Resolve(SandboxExecutionProfileBinding profile);
}

/// <summary>
/// Low-level server-owned request. It is assembled from repository authority and trusted
/// configuration; API clients never supply paths, commands, mounts, image or environment.
/// </summary>
public sealed record SandboxExecutionRequest
{
    public required Guid ExecutionId { get; init; }
    public required int ProjectId { get; init; }
    public required Guid RepositoryBindingId { get; init; }
    public required long RepositoryBindingRevision { get; init; }
    public required string BaselineCommit { get; init; }
    public required string WorktreeFingerprint { get; init; }
    public required Guid ProjectExecutionProfileId { get; init; }
    public required long ProjectExecutionProfileRevision { get; init; }
    public required string SourceSnapshotPath { get; init; }
    public required string EvidenceOutputPath { get; init; }
    public required SandboxRuntimePolicy Policy { get; init; }
}

public sealed record SandboxRuntimeInspection
{
    public required string RuntimeName { get; init; }
    public required string IsolationMode { get; init; }
    public required string ActualContainerImageDigest { get; init; }
    public required int VirtualCpuCount { get; init; }
    public required int MemoryMaximumMiB { get; init; }
    public required int WritableScratchMaximumGiB { get; init; }
    public required int MaximumUntrustedWorkloadProcessCount { get; init; }
    public required string UntrustedWorkloadProcessScope { get; init; }
    public required string TrustedSupervisorVersion { get; init; }
    public required string TrustedSupervisorSha256 { get; init; }
    public required bool SuspendedAssignmentBeforeResumeProven { get; init; }
    public required bool UntrustedWorkloadProcessLimitProven { get; init; }
    public required bool RestrictedLowIntegrityWorkloadIdentityProven { get; init; }
    public required bool SupervisorHandleIsolationProven { get; init; }
    public required bool WorkloadScratchAndEvidenceBoundaryProven { get; init; }
    public required bool BrokerLaunchDenialProven { get; init; }
    public required bool ProjectBytesCopiedAfterPreflightProven { get; init; }
    public required int NetworkEndpointCount { get; init; }
    public required int HostWritableMountCount { get; init; }
    public required bool RepositoryInputReadOnly { get; init; }
    public required bool OfflineFeedReadOnly { get; init; }
    public required bool WasDestroyed { get; init; }
    public required DateTimeOffset InspectedAtUtc { get; init; }
}

public sealed record SandboxStageEvidence
{
    public required SandboxExecutionStage Stage { get; init; }
    public required string CommandSha256 { get; init; }
    public required int ExitCode { get; init; }
    public required bool TimedOut { get; init; }
    public required long DurationMilliseconds { get; init; }
    public required string StandardOutputSha256 { get; init; }
    public required string StandardErrorSha256 { get; init; }
    public required bool StandardOutputTruncated { get; init; }
    public required bool StandardErrorTruncated { get; init; }
}

public sealed record SandboxEvidenceArtifact
{
    public required string Kind { get; init; }
    public required string RelativePath { get; init; }
    public required long LengthBytes { get; init; }
    public required string ContentSha256 { get; init; }
}

public sealed record SandboxEvidenceManifest
{
    public required int SchemaVersion { get; init; }
    public required Guid ExecutionId { get; init; }
    public required int ProjectId { get; init; }
    public required Guid RepositoryBindingId { get; init; }
    public required long RepositoryBindingRevision { get; init; }
    public required string BaselineCommit { get; init; }
    public required string WorktreeFingerprint { get; init; }
    public required Guid ProjectExecutionProfileId { get; init; }
    public required long ProjectExecutionProfileRevision { get; init; }
    public required string ProfileDefinitionId { get; init; }
    public required int ProfileDescriptorRevision { get; init; }
    public required string DescriptorSha256 { get; init; }
    public required string TemplateBundleSha256 { get; init; }
    public required string ToolchainManifestId { get; init; }
    public required string ContainerImageDigest { get; init; }
    public required string SandboxPolicyVersion { get; init; }
    public required string SandboxPolicySha256 { get; init; }
    public required string TrustedSupervisorVersion { get; init; }
    public required string TrustedSupervisorSha256 { get; init; }
    public required string OfflineFeedManifestSha256 { get; init; }
    public required SandboxExecutionStatus Status { get; init; }
    public required string ReasonCode { get; init; }
    public required string SafeSummary { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required SandboxRuntimeInspection Inspection { get; init; }
    public IReadOnlyList<SandboxStageEvidence> Stages { get; init; } = [];
    public IReadOnlyList<SandboxEvidenceArtifact> Artifacts { get; init; } = [];
}

public sealed record SandboxExecutionResult
{
    public required Guid ExecutionId { get; init; }
    public required SandboxExecutionStatus Status { get; init; }
    public required string ReasonCode { get; init; }
    public required string SafeSummary { get; init; }
    public required bool CleanedUp { get; init; }
    public required SandboxEvidenceManifest EvidenceManifest { get; init; }
    public required string EvidenceManifestJson { get; init; }
    public required string EvidenceManifestSha256 { get; init; }
}

public sealed record SandboxRecoveryResult(
    int CandidatesFound,
    int SandboxesRemoved,
    int SandboxesRemaining,
    bool Succeeded,
    string SafeSummary);

/// <summary>
/// Exact durable authority used only to recover a final evidence manifest after the
/// sandbox was destroyed but the database transaction did not complete.
/// </summary>
public sealed record SandboxCompletedEvidenceRecoveryRequest
{
    public required Guid ExecutionId { get; init; }
    public required int ProjectId { get; init; }
    public required Guid RepositoryBindingId { get; init; }
    public required long RepositoryBindingRevision { get; init; }
    public required string BaselineCommit { get; init; }
    public required string WorktreeFingerprint { get; init; }
    public required Guid ProjectExecutionProfileId { get; init; }
    public required long ProjectExecutionProfileRevision { get; init; }
    public required string ProfileDefinitionId { get; init; }
    public required int ProfileDescriptorRevision { get; init; }
    public required string DescriptorSha256 { get; init; }
    public required string TemplateBundleSha256 { get; init; }
    public required string ToolchainManifestId { get; init; }
    public required string ContainerImageReference { get; init; }
    public required string SandboxPolicyVersion { get; init; }
    public required string SandboxPolicySha256 { get; init; }
    public required string TrustedSupervisorVersion { get; init; }
    public required string TrustedSupervisorSha256 { get; init; }
    public required string OfflineFeedManifestSha256 { get; init; }
    public required string EvidenceOutputPath { get; init; }
}

public sealed record SandboxExecutionCleanupRequest(
    Guid ExecutionId,
    string SandboxPolicySha256,
    string TrustedSupervisorVersion,
    string TrustedSupervisorSha256,
    string EvidenceOutputPath);

public sealed record SandboxExecutionCleanupResult(
    bool ContainerCleanupConfirmed,
    bool EvidenceCleanupConfirmed,
    string SafeSummary)
{
    public bool CleanupConfirmed => ContainerCleanupConfirmed && EvidenceCleanupConfirmed;
}

public interface ISandboxExecutionService
{
    Task<SandboxCapability> GetCapabilityAsync(CancellationToken cancellationToken = default);

    Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken = default);

    Task<SandboxExecutionResult?> TryRecoverCompletedAsync(
        SandboxCompletedEvidenceRecoveryRequest request,
        CancellationToken cancellationToken = default);

    Task<SandboxExecutionCleanupResult> RecoverExecutionAsync(
        SandboxExecutionCleanupRequest request,
        CancellationToken cancellationToken = default);

    Task<SandboxRecoveryResult> RecoverAsync(CancellationToken cancellationToken = default);
}

public sealed record WorkbenchSandboxRepositoryAuthority(
    Guid RepositoryBindingId,
    long RepositoryBindingRevision,
    Guid ProjectExecutionProfileId,
    long ProjectExecutionProfileRevision,
    string BaselineCommit);

public sealed record SandboxQualificationAttemptSnapshot(
    Guid AttemptId,
    Guid ClientOperationId,
    string State,
    Guid RepositoryBindingId,
    long ExpectedRepositoryBindingRevision,
    Guid ProjectExecutionProfileId,
    long ExpectedExecutionProfileRevision,
    string BaselineCommit,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? EvidenceManifestSha256,
    string? FailureCode,
    string? SafeSummary,
    bool CanRecover);

public sealed record WorkbenchSandboxContext(
    int ProjectId,
    string ProjectLifecyclePhase,
    string ExecutionReadiness,
    WorkbenchSandboxRepositoryAuthority? RepositoryAuthority,
    SandboxCapability Capability,
    SandboxQualificationAttemptSnapshot? LatestAttempt);

public sealed record GetWorkbenchSandboxContextQuery(
    int TenantId,
    int ActorUserId,
    int ProjectId);

/// <summary>
/// The complete browser-facing qualification mutation. Execution details are resolved
/// exclusively on the server from the fenced project/repository/profile authority.
/// </summary>
public sealed record StartWorkbenchSandboxQualificationCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    long ExpectedRepositoryBindingRevision,
    long ExpectedExecutionProfileRevision);

public sealed record WorkbenchSandboxQualificationResult(
    int ProjectId,
    Guid ClientOperationId,
    bool IsReplay,
    SandboxCapability Capability,
    SandboxQualificationAttemptSnapshot Attempt);

public interface IWorkbenchSandboxQualificationService
{
    Task<WorkbenchSandboxContext> GetContextAsync(
        GetWorkbenchSandboxContextQuery query,
        CancellationToken cancellationToken = default);

    Task<WorkbenchSandboxQualificationResult> StartAsync(
        StartWorkbenchSandboxQualificationCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class SandboxContractValidationException(string message) : Exception(message);

public static class SandboxCanonicalJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string NormalizeSha256(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new SandboxContractValidationException($"{fieldName} is required.");

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.StartsWith("sha256:", StringComparison.Ordinal))
            normalized = normalized[7..];
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new SandboxContractValidationException($"{fieldName} must be a SHA-256 value.");
        return normalized;
    }
}

public static class SandboxRuntimePolicyCodec
{
    public static string ComputeHash(SandboxRuntimePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return SandboxCanonicalJson.Sha256(SerializeCanonical(policy));
    }

    public static string SerializeCanonical(SandboxRuntimePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return SandboxCanonicalJson.Serialize(new
        {
            policy.SchemaVersion,
            policy.PolicyVersion,
            policy.IsolationMode,
            policy.ProfileDefinitionId,
            policy.ProfileDescriptorRevision,
            DescriptorSha256 = SandboxCanonicalJson.NormalizeSha256(policy.DescriptorSha256, nameof(policy.DescriptorSha256)),
            TemplateBundleSha256 = SandboxCanonicalJson.NormalizeSha256(policy.TemplateBundleSha256, nameof(policy.TemplateBundleSha256)),
            policy.ToolchainManifestId,
            policy.ContainerImageReference,
            ContainerImageDigest = SandboxCanonicalJson.NormalizeSha256(policy.ContainerImageDigest, nameof(policy.ContainerImageDigest)),
            OfflineFeedManifestSha256 = SandboxCanonicalJson.NormalizeSha256(policy.OfflineFeedManifestSha256, nameof(policy.OfflineFeedManifestSha256)),
            policy.RepositoryInputReadOnly,
            policy.OfflineFeedReadOnly,
            policy.TrustedSupervisorVersion,
            TrustedSupervisorSha256 = SandboxCanonicalJson.NormalizeSha256(
                policy.TrustedSupervisorSha256,
                nameof(policy.TrustedSupervisorSha256)),
            policy.Resources,
            Restore = CanonicalCommand(policy.Restore),
            Build = CanonicalCommand(policy.Build),
            Test = CanonicalCommand(policy.Test),
            EnvironmentAllowList = policy.EnvironmentAllowList.Order(StringComparer.Ordinal).ToArray()
        });
    }

    private static object CanonicalCommand(SandboxCommandPolicy command) => new
    {
        Stage = command.Stage.ToString(),
        command.CommandText,
        CommandSha256 = SandboxCanonicalJson.NormalizeSha256(command.CommandSha256, nameof(command.CommandSha256)),
        command.TimeoutSeconds
    };
}

public static class SandboxEvidenceManifestCodec
{
    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    public static string ComputeHash(SandboxEvidenceManifest manifest) =>
        SandboxCanonicalJson.Sha256(SerializeCanonical(manifest));

    public static string SerializeCanonical(SandboxEvidenceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ExecutionId == Guid.Empty || manifest.ProjectId <= 0 || manifest.RepositoryBindingId == Guid.Empty)
            throw new SandboxContractValidationException("Evidence must bind an execution, project, and repository.");
        if (manifest.CompletedAtUtc < manifest.StartedAtUtc)
            throw new SandboxContractValidationException("Evidence completion cannot precede execution start.");

        return SandboxCanonicalJson.Serialize(new
        {
            manifest.SchemaVersion,
            manifest.ExecutionId,
            manifest.ProjectId,
            manifest.RepositoryBindingId,
            manifest.RepositoryBindingRevision,
            manifest.BaselineCommit,
            manifest.WorktreeFingerprint,
            manifest.ProjectExecutionProfileId,
            manifest.ProjectExecutionProfileRevision,
            manifest.ProfileDefinitionId,
            manifest.ProfileDescriptorRevision,
            DescriptorSha256 = Hash(manifest.DescriptorSha256, nameof(manifest.DescriptorSha256)),
            TemplateBundleSha256 = Hash(manifest.TemplateBundleSha256, nameof(manifest.TemplateBundleSha256)),
            manifest.ToolchainManifestId,
            ContainerImageDigest = Hash(manifest.ContainerImageDigest, nameof(manifest.ContainerImageDigest)),
            manifest.SandboxPolicyVersion,
            SandboxPolicySha256 = Hash(manifest.SandboxPolicySha256, nameof(manifest.SandboxPolicySha256)),
            manifest.TrustedSupervisorVersion,
            TrustedSupervisorSha256 = Hash(
                manifest.TrustedSupervisorSha256,
                nameof(manifest.TrustedSupervisorSha256)),
            OfflineFeedManifestSha256 = Hash(manifest.OfflineFeedManifestSha256, nameof(manifest.OfflineFeedManifestSha256)),
            Status = manifest.Status.ToString(),
            manifest.ReasonCode,
            manifest.SafeSummary,
            manifest.StartedAtUtc,
            manifest.CompletedAtUtc,
            Inspection = CanonicalInspection(manifest.Inspection),
            Stages = manifest.Stages
                .OrderBy(stage => stage.Stage)
                .Select(stage => new
                {
                    Stage = stage.Stage.ToString(),
                    CommandSha256 = Hash(stage.CommandSha256, nameof(stage.CommandSha256)),
                    stage.ExitCode,
                    stage.TimedOut,
                    stage.DurationMilliseconds,
                    StandardOutputSha256 = Hash(stage.StandardOutputSha256, nameof(stage.StandardOutputSha256)),
                    StandardErrorSha256 = Hash(stage.StandardErrorSha256, nameof(stage.StandardErrorSha256)),
                    stage.StandardOutputTruncated,
                    stage.StandardErrorTruncated
                })
                .ToArray(),
            Artifacts = manifest.Artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .ThenBy(artifact => artifact.Kind, StringComparer.Ordinal)
                .Select(artifact => new
                {
                    artifact.Kind,
                    artifact.RelativePath,
                    artifact.LengthBytes,
                    ContentSha256 = Hash(artifact.ContentSha256, nameof(artifact.ContentSha256))
                })
                .ToArray()
        });
    }

    public static SandboxEvidenceManifest DeserializeCanonical(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new SandboxContractValidationException("The sandbox evidence manifest is empty.");
        try
        {
            var manifest = JsonSerializer.Deserialize<SandboxEvidenceManifest>(json, ReadOptions)
                ?? throw new SandboxContractValidationException("The sandbox evidence manifest is unreadable.");
            if (!string.Equals(json, SerializeCanonical(manifest), StringComparison.Ordinal))
                throw new SandboxContractValidationException("The sandbox evidence manifest is not canonical.");
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new SandboxContractValidationException(
                $"The sandbox evidence manifest is unreadable: {exception.Message}");
        }
    }

    private static object CanonicalInspection(SandboxRuntimeInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return new
        {
            inspection.RuntimeName,
            inspection.IsolationMode,
            ActualContainerImageDigest = Hash(inspection.ActualContainerImageDigest, nameof(inspection.ActualContainerImageDigest)),
            inspection.VirtualCpuCount,
            inspection.MemoryMaximumMiB,
            inspection.WritableScratchMaximumGiB,
            inspection.MaximumUntrustedWorkloadProcessCount,
            inspection.UntrustedWorkloadProcessScope,
            inspection.TrustedSupervisorVersion,
            TrustedSupervisorSha256 = Hash(
                inspection.TrustedSupervisorSha256,
                nameof(inspection.TrustedSupervisorSha256)),
            inspection.SuspendedAssignmentBeforeResumeProven,
            inspection.UntrustedWorkloadProcessLimitProven,
            inspection.RestrictedLowIntegrityWorkloadIdentityProven,
            inspection.SupervisorHandleIsolationProven,
            inspection.WorkloadScratchAndEvidenceBoundaryProven,
            inspection.BrokerLaunchDenialProven,
            inspection.ProjectBytesCopiedAfterPreflightProven,
            inspection.NetworkEndpointCount,
            inspection.HostWritableMountCount,
            inspection.RepositoryInputReadOnly,
            inspection.OfflineFeedReadOnly,
            inspection.WasDestroyed,
            inspection.InspectedAtUtc
        };
    }

    private static string Hash(string value, string fieldName) =>
        SandboxCanonicalJson.NormalizeSha256(value, fieldName);

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
