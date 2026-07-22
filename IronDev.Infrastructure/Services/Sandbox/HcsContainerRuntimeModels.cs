using IronDev.Core.Sandbox;

namespace IronDev.Infrastructure.Services.Sandbox;

public static class HcsContainerRuntimeConstants
{
    public const string RuntimeName = "DockerEngineHcsHyperV";
    public const string DockerEngineHost = "npipe:////./pipe/docker_engine";
    public const string OwnerLabel = "com.irondev.sandbox.owner";
    public const string ExecutionLabel = "com.irondev.sandbox.execution-id";
    public const string PolicyLabel = "com.irondev.sandbox.policy-sha256";
    public const string RuntimeLabel = "com.irondev.sandbox.runtime";
    public const string RuntimeLabelValue = "pr06a-hcs-hyperv-v1";
    public const string SourceContainerPath = @"C:\IronDev\Input\Source";
    public const string FeedContainerPath = @"C:\IronDev\Feed";
    public const string NuGetConfigContainerPath = @"C:\IronDev\NuGet.Config";
    public const string ScratchContainerPath = @"C:\IronDev\Scratch\Source";
    public const string EvidenceContainerPath = @"C:\IronDev\Evidence";
    public const string EvidenceOwnerMarkerFileName = ".irondev-sandbox-owner.json";
}

/// <summary>
/// Host-owned values needed to talk to the Windows container engine. These values are
/// resolved by trusted server configuration, never by a browser request.
/// </summary>
public sealed record HcsContainerRuntimeConfiguration
{
    public required string DockerExecutablePath { get; init; }
    public required string DockerEngineHost { get; init; }
    public required string DockerConfigDirectory { get; init; }
    public required string OwnerLabelValue { get; init; }
    public required string EvidenceRootPath { get; init; }
    public IReadOnlyList<string> AllowedSourceRoots { get; init; } = [];
    public IReadOnlyList<string> AllowedOfflineFeedRoots { get; init; } = [];
    public IReadOnlyList<string> AllowedImageReferences { get; init; } = [];
    public IReadOnlyDictionary<string, string> DockerHostEnvironment { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public int MaximumCapturedOutputCharacters { get; init; } = 1_048_576;
    public TimeSpan DockerControlCommandTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ContainerInitializationTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// A structured command run directly in the isolated container. No host or container
/// command shell parses CommandPath or Arguments.
/// </summary>
public sealed record HcsContainerCommand
{
    public required SandboxExecutionStage Stage { get; init; }
    public required string CommandPath { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public required string CommandSha256 { get; init; }
    public required TimeSpan Timeout { get; init; }
}

public sealed record HcsContainerRuntimeRequest
{
    public required Guid ExecutionId { get; init; }
    public required string SourceSnapshotPath { get; init; }
    public required string OfflineFeedPath { get; init; }
    public required string EvidenceOutputPath { get; init; }
    public required string ImageReference { get; init; }
    public required string ExpectedImageDigest { get; init; }
    public required string PolicySha256 { get; init; }
    public required string TrustedSupervisorVersion { get; init; }
    public required string TrustedSupervisorSha256 { get; init; }
    public required SandboxResourcePolicy Resources { get; init; }
    public IReadOnlyList<HcsContainerCommand> Commands { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> EnvironmentAllowList { get; init; } = [];
}

public sealed record HcsContainerStageResult
{
    public required SandboxExecutionStage Stage { get; init; }
    public required string CommandSha256 { get; init; }
    public required int ExitCode { get; init; }
    public required bool TimedOut { get; init; }
    public required long DurationMilliseconds { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required bool StandardOutputTruncated { get; init; }
    public required bool StandardErrorTruncated { get; init; }
}

public sealed record HcsContainerRuntimeResult
{
    public required SandboxExecutionStatus Status { get; init; }
    public required string ReasonCode { get; init; }
    public required string SafeSummary { get; init; }
    public required SandboxRuntimeInspection Inspection { get; init; }
    public IReadOnlyList<HcsContainerStageResult> Stages { get; init; } = [];
    public required string EvidencePath { get; init; }
    public required bool CleanedUp { get; init; }
}

public sealed record HcsContainerRecoveryRequest
{
    public DateTimeOffset? CreatedBeforeUtc { get; init; }
    public IReadOnlyList<Guid> ExcludedExecutionIds { get; init; } = [];
}

public sealed record HcsContainerRecoveryResult(
    int CandidatesFound,
    int ContainersRemoved,
    int ContainersRemaining,
    bool Succeeded,
    string SafeSummary);

public sealed record HcsCompletedEvidenceRequest(
    Guid ExecutionId,
    string PolicySha256,
    string TrustedSupervisorVersion,
    string TrustedSupervisorSha256,
    string EvidenceOutputPath);

public sealed record HcsCompletedEvidence(
    string EvidencePath,
    string ManifestJson);

public sealed record HcsExecutionCleanupRequest(
    Guid ExecutionId,
    string PolicySha256,
    string TrustedSupervisorVersion,
    string TrustedSupervisorSha256,
    string EvidenceOutputPath);

public sealed record HcsExecutionCleanupResult(
    bool ContainerCleanupConfirmed,
    bool EvidenceCleanupConfirmed,
    string SafeSummary);

public sealed record HcsContainerProbeRequest(
    string ImageReference,
    string ExpectedImageDigest);

/// <summary>Safe runtime capability only; it intentionally exposes no host or engine details.</summary>
public sealed record HcsContainerProbeResult(
    bool IsAvailable,
    string CapabilityState,
    string ReasonCode,
    string SafeSummary);

public sealed class HcsContainerRuntimeException(string reasonCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record DockerCommandRequest
{
    public required string ExecutablePath { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public required string WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public required TimeSpan Timeout { get; init; }
    public required int MaximumOutputCharacters { get; init; }
}

public sealed record DockerCommandResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required bool TimedOut { get; init; }
    public required bool StandardOutputTruncated { get; init; }
    public required bool StandardErrorTruncated { get; init; }
    public required long DurationMilliseconds { get; init; }
}
