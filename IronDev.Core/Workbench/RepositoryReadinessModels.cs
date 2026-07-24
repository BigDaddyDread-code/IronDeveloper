using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Workbench;

public static class RepositoryWorktreeStates
{
    public const string Clean = "Clean";
    public const string Dirty = "Dirty";
    public const string Unknown = "Unknown";
}

public static class RepositoryCodeIndexStates
{
    public const string Current = "Current";
    public const string Failed = "Failed";
}

public static class RepositorySandboxQualificationEvidenceStates
{
    public const string Passed = "Passed";
    public const string Failed = "Failed";
}

public static class ExecutionAvailabilityStates
{
    public const string Available = "Available";
    public const string Unavailable = "Unavailable";
}

public static class RepositoryReadinessReasonCodes
{
    public const string Ready = "RepositoryTechnicalReadinessCurrent";
    public const string RepositoryNotConfigured = "RepositoryBindingNotQualified";
    public const string ExecutionProfileNotConfigured = "ExecutionProfileNotPinned";
    public const string RepositoryObservationRequired = "RepositoryObservationRequired";
    public const string RepositoryObservationStale = "RepositoryObservationStale";
    public const string RestoreValidationRequired = "RestoreValidationRequired";
    public const string BuildValidationRequired = "BuildValidationRequired";
    public const string TestValidationRequired = "TestValidationRequired";
    public const string CodeIndexRequired = "CodeIndexRequired";
    public const string SandboxQualificationRequired = "SandboxQualificationRequired";
    public const string BuilderModelConfigurationRequired = "BuilderModelConfigurationRequired";
}

public enum RepositoryValidationOutcome
{
    Passed = 1,
    Failed = 2,
    TimedOut = 3
}

/// <summary>
/// Current server-owned repository and execution-profile configuration. It deliberately contains
/// no observation or index identity, so a newly qualified repository is configured even before
/// its first validation refresh.
/// </summary>
public sealed record RepositoryReadinessAuthority
{
    public required int ProjectId { get; init; }
    public required Guid RepositoryBindingId { get; init; }
    public required long RepositoryBindingRevision { get; init; }
    public required string BaselineCommit { get; init; }
    public required Guid ProjectExecutionProfileId { get; init; }
    public required long ProjectExecutionProfileRevision { get; init; }
    public required string ProfileDefinitionId { get; init; }
    public required int ProfileDescriptorRevision { get; init; }
    public required string ProfileDescriptorSha256 { get; init; }
    public required string RestoreCommandSha256 { get; init; }
    public required string BuildCommandSha256 { get; init; }
    public required string TestCommandSha256 { get; init; }
    public required string SdkToolchainManifestId { get; init; }
    public string? ContainerImageDigest { get; init; }
    public string? SandboxPolicyVersion { get; init; }
    public string? SandboxPolicySha256 { get; init; }
    public string? OfflineFeedManifestSha256 { get; init; }
    public required string TemplateBundleSha256 { get; init; }
}

/// <summary>
/// The universal, content-addressed binding carried by build, test, index, and sandbox evidence.
/// Code-index identity is null for evidence produced before indexing and exact for index evidence.
/// </summary>
public sealed record RepositoryReadinessEvidenceBinding
{
    public required int ProjectId { get; init; }
    public required Guid RepositoryBindingId { get; init; }
    public required long RepositoryBindingRevision { get; init; }
    public required string BaselineCommit { get; init; }
    public required Guid RepositoryStateObservationId { get; init; }
    public required string WorktreeFingerprint { get; init; }
    public required Guid ProjectExecutionProfileId { get; init; }
    public required long ProjectExecutionProfileRevision { get; init; }
    public required string ProfileDefinitionId { get; init; }
    public required int ProfileDescriptorRevision { get; init; }
    public required string ProfileDescriptorSha256 { get; init; }
    public required string RestoreCommandSha256 { get; init; }
    public required string BuildCommandSha256 { get; init; }
    public required string TestCommandSha256 { get; init; }
    public required string SdkToolchainManifestId { get; init; }
    public required string ContainerImageDigest { get; init; }
    public required string SandboxPolicyVersion { get; init; }
    public required string SandboxPolicySha256 { get; init; }
    public required string OfflineFeedManifestSha256 { get; init; }
    public required string TemplateBundleSha256 { get; init; }
    public Guid? CodeIndexSnapshotId { get; init; }
    public long? CodeIndexSnapshotRevision { get; init; }

    public RepositoryReadinessEvidenceBinding WithoutCodeIndex() => this with
    {
        CodeIndexSnapshotId = null,
        CodeIndexSnapshotRevision = null
    };
}

/// <summary>
/// An immutable observation of one repository binding. ObservedAtUtc is audit data and is never
/// sufficient to make mismatched evidence current.
/// </summary>
public sealed record RepositoryStateObservation
{
    public required Guid Id { get; init; }
    public required Guid RepositoryBindingId { get; init; }
    public required long RepositoryBindingRevision { get; init; }
    public required string BaselineCommit { get; init; }
    public required string HeadCommit { get; init; }
    public required string GitTreeId { get; init; }
    public required string WorktreeState { get; init; }
    public required string WorktreeFingerprint { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string EvidenceHash { get; init; }
}

public sealed record RepositoryValidationCommandResult
{
    public required string CommandSha256 { get; init; }
    public required RepositoryValidationOutcome Outcome { get; init; }
    public required int ExitCode { get; init; }
    public required bool TimedOut { get; init; }
    public required long DurationMilliseconds { get; init; }
    public required string StandardOutputSha256 { get; init; }
    public required string StandardErrorSha256 { get; init; }
}

public sealed record BuildValidationRecord
{
    public required Guid Id { get; init; }
    public required long Revision { get; init; }
    public required RepositoryReadinessEvidenceBinding Binding { get; init; }
    public required RepositoryValidationCommandResult RestoreResult { get; init; }
    public required RepositoryValidationCommandResult BuildResult { get; init; }
    public required DateTimeOffset ValidatedAtUtc { get; init; }
    public required string EvidenceManifestSha256 { get; init; }
}

public sealed record TestValidationRecord
{
    public required Guid Id { get; init; }
    public required long Revision { get; init; }
    public required RepositoryReadinessEvidenceBinding Binding { get; init; }
    public required RepositoryValidationCommandResult TestResult { get; init; }
    public required DateTimeOffset ValidatedAtUtc { get; init; }
    public required string EvidenceManifestSha256 { get; init; }
}

/// <summary>
/// One ordered source fingerprint in an immutable index snapshot. Ordinal is semantic: codecs do
/// not sort entries, so changing order changes the canonical JSON and hash.
/// </summary>
public sealed record CodeIndexSourceFingerprint(
    int Ordinal,
    string RelativePath,
    string ContentSha256);

public sealed record CodeIndexSnapshot
{
    public required Guid Id { get; init; }
    public required long Revision { get; init; }
    public required RepositoryReadinessEvidenceBinding Binding { get; init; }
    public required string State { get; init; }
    public required string IndexFormatVersion { get; init; }
    public required IReadOnlyList<CodeIndexSourceFingerprint> Sources { get; init; }
    public required string IndexedContentSha256 { get; init; }
    public required DateTimeOffset IndexedAtUtc { get; init; }
    public required string EvidenceManifestSha256 { get; init; }
}

public sealed record RepositorySandboxQualificationEvidence
{
    public required Guid QualificationAttemptId { get; init; }
    public required long Revision { get; init; }
    public required RepositoryReadinessEvidenceBinding Binding { get; init; }
    public required string State { get; init; }
    public required DateTimeOffset ValidatedAtUtc { get; init; }
    public required string EvidenceManifestSha256 { get; init; }
}

public sealed record BuilderStableConfigurationBinding
{
    public required Guid ConfigurationId { get; init; }
    public required long Revision { get; init; }
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required string ConfigurationSha256 { get; init; }
}

public sealed record BuilderStableConfigurationEvidence
{
    public required BuilderStableConfigurationBinding Binding { get; init; }
    public required bool IsConfigured { get; init; }
    public required DateTimeOffset ValidatedAtUtc { get; init; }
    public required string EvidenceManifestSha256 { get; init; }
}

public sealed record ExecutionAvailabilityCheck
{
    public required string State { get; init; }
    public required string ReasonCode { get; init; }
    public required string SafeMessage { get; init; }
    public required DateTimeOffset CheckedAtUtc { get; init; }

    public bool IsAvailable => string.Equals(State, ExecutionAvailabilityStates.Available, StringComparison.Ordinal);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RepositoryReadinessGateName
{
    RepositoryBindingQualified = 1,
    RepositoryCleanAtBaseline = 2,
    ExecutionProfilePinned = 3,
    RestorePassed = 4,
    BuildPassed = 5,
    TestCommandPassed = 6,
    CodeIndexCurrent = 7,
    SandboxQualified = 8,
    BuilderModelConfigured = 9
}

public sealed record RepositoryReadinessGateResult(
    RepositoryReadinessGateName Gate,
    bool Passed,
    string ReasonCode);

/// <summary>
/// Server-owned inputs to the pure evaluator. Availability is deliberately present for display
/// but is not a durable readiness gate and cannot affect EvaluationState.
/// </summary>
public sealed record RepositoryReadinessEvaluationContext
{
    public required int ProjectId { get; init; }
    public required string RepositoryBindingState { get; init; }
    public required RepositoryReadinessAuthority? CurrentAuthority { get; init; }
    public required RepositoryStateObservation? RepositoryObservation { get; init; }
    public required BuildValidationRecord? BuildValidation { get; init; }
    public required TestValidationRecord? TestValidation { get; init; }
    public required CodeIndexSnapshot? CodeIndex { get; init; }
    public required RepositorySandboxQualificationEvidence? SandboxQualification { get; init; }
    public required BuilderStableConfigurationBinding? CurrentBuilderConfiguration { get; init; }
    public required BuilderStableConfigurationEvidence? BuilderConfigurationEvidence { get; init; }
    public ExecutionAvailabilityCheck? Availability { get; init; }
}

public sealed record RepositoryReadinessEvaluationResult(
    string ExecutionReadiness,
    string ReasonCode,
    string? CurrentAuthoritySha256,
    IReadOnlyList<RepositoryReadinessGateResult> Gates,
    ExecutionAvailabilityCheck? Availability)
{
    public bool IsReady => string.Equals(
        ExecutionReadiness,
        ProjectExecutionReadinessStates.Ready,
        StringComparison.Ordinal);
}

public sealed record GetWorkbenchRepositoryReadinessContextQuery(
    int TenantId,
    int ActorUserId,
    int ProjectId);

public sealed record WorkbenchRepositoryReadinessContext(
    int ProjectId,
    string ProjectLifecyclePhase,
    RepositoryReadinessEvaluationResult Evaluation);

public sealed record RefreshRepositoryReadinessCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    long ExpectedRepositoryBindingRevision,
    long ExpectedExecutionProfileRevision);

public sealed record RefreshRepositoryReadinessResult(
    int ProjectId,
    Guid ClientOperationId,
    bool IsReplay,
    Guid RepositoryStateObservationId,
    Guid BuildValidationRecordId,
    Guid TestValidationRecordId,
    Guid CodeIndexSnapshotId,
    RepositoryReadinessEvaluationResult Evaluation);

public sealed record ObserveRepositoryStateRequest(
    int ProjectId,
    Guid RepositoryBindingId,
    long RepositoryBindingRevision,
    string CanonicalRepositoryPath,
    string BaselineCommit,
    string? ProvisioningManifestJson = null,
    string? ProvisioningManifestSha256 = null);

public sealed record RepositoryObservationResult(
    RepositoryStateObservation Observation,
    IReadOnlyList<CodeIndexSourceFingerprint> Sources);

public sealed record ExecutionAvailabilityRequest(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    Guid BuilderConfigurationId,
    long BuilderConfigurationRevision);

public interface IWorkbenchRepositoryReadinessService
{
    Task<WorkbenchRepositoryReadinessContext> GetContextAsync(
        GetWorkbenchRepositoryReadinessContextQuery query,
        CancellationToken cancellationToken = default);

    Task<RefreshRepositoryReadinessResult> RefreshAsync(
        RefreshRepositoryReadinessCommand command,
        CancellationToken cancellationToken = default);
}

public interface IRepositoryReadinessObserver
{
    Task<RepositoryObservationResult> ObserveAsync(
        ObserveRepositoryStateRequest request,
        CancellationToken cancellationToken = default);
}

public interface IBuilderStableConfigurationProvider
{
    Task<BuilderStableConfigurationBinding?> GetCurrentAsync(
        int tenantId,
        int projectId,
        CancellationToken cancellationToken = default);
}

public interface IExecutionAvailabilityChecker
{
    Task<ExecutionAvailabilityCheck> CheckAsync(
        ExecutionAvailabilityRequest request,
        CancellationToken cancellationToken = default);
}

public enum RepositoryReadinessRefreshFailurePoint
{
    ClientOperationCreated = 1,
    RepositoryObservationCreated = 2,
    BuildValidationCreated = 3,
    TestValidationCreated = 4,
    CodeIndexSnapshotCreated = 5,
    ReadinessAssessmentCreated = 6,
    OutboxEventsCreated = 7,
    CompletionCommitted = 8
}

public interface IRepositoryReadinessRefreshFailureInjector
{
    void ThrowIfRequested(RepositoryReadinessRefreshFailurePoint point);
}

public sealed class NoOpRepositoryReadinessRefreshFailureInjector : IRepositoryReadinessRefreshFailureInjector
{
    public void ThrowIfRequested(RepositoryReadinessRefreshFailurePoint point)
    {
    }
}

public sealed class RepositoryReadinessValidationException(string message) : Exception(message);

public sealed class RepositoryReadinessStaleConfigurationException : Exception
{
    public const string ErrorCode = "repository_readiness_configuration_stale";

    public RepositoryReadinessStaleConfigurationException()
        : base("Repository configuration changed. Refresh the Repository surface before retrying readiness validation.")
    {
    }
}

public sealed class RepositoryReadinessOperationMismatchException : Exception
{
    public const string ErrorCode = "operation_id_payload_mismatch";

    public RepositoryReadinessOperationMismatchException()
        : base("The client operation ID was already used with a different readiness payload.")
    {
    }
}

public static class RepositoryReadinessCanonicalJson
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
        var normalized = NormalizeHash(value, fieldName, allowDigestPrefix: true);
        return normalized.StartsWith("sha256:", StringComparison.Ordinal)
            ? normalized[7..]
            : normalized;
    }

    public static string NormalizeContainerDigest(string value, string fieldName)
    {
        var normalized = NormalizeHash(value, fieldName, allowDigestPrefix: true);
        return normalized.StartsWith("sha256:", StringComparison.Ordinal)
            ? normalized
            : $"sha256:{normalized}";
    }

    public static string NormalizeGitObjectId(string value, string fieldName)
    {
        var normalized = Required(value, fieldName).ToLowerInvariant();
        if (normalized.Length is not 40 and not 64 || normalized.Any(static character => !IsLowerHex(character)))
            throw Invalid($"{fieldName} must be a 40- or 64-character hexadecimal Git object ID.");
        return normalized;
    }

    public static string NormalizeIdentifier(string value, string fieldName, int maximumLength = 200)
    {
        var normalized = Required(value, fieldName);
        if (normalized.Length > maximumLength || normalized.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_' and not '/' and not ':'))
            throw Invalid($"{fieldName} must be a bounded sanitized identifier.");
        return normalized;
    }

    public static DateTimeOffset NormalizeUtc(DateTimeOffset value, string fieldName)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
            throw Invalid($"{fieldName} must be a non-default UTC timestamp.");
        return value;
    }

    private static string NormalizeHash(string value, string fieldName, bool allowDigestPrefix)
    {
        var normalized = Required(value, fieldName).ToLowerInvariant();
        if (allowDigestPrefix && normalized.StartsWith("sha256:", StringComparison.Ordinal))
            normalized = normalized[7..];
        if (normalized.Length != 64 || normalized.Any(static character => !IsLowerHex(character)))
            throw Invalid($"{fieldName} must be a SHA-256 value.");
        return normalized;
    }

    private static string Required(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw Invalid($"{fieldName} is required and must not contain surrounding whitespace.");
        return value;
    }

    private static bool IsLowerHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static RepositoryReadinessValidationException Invalid(string message) => new(message);
}

public static class RepositoryReadinessAuthorityCodec
{
    public static string SerializeCanonical(RepositoryReadinessAuthority authority)
    {
        var value = NormalizeAndValidate(authority);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            value.ProjectId,
            value.RepositoryBindingId,
            value.RepositoryBindingRevision,
            value.BaselineCommit,
            value.ProjectExecutionProfileId,
            value.ProjectExecutionProfileRevision,
            value.ProfileDefinitionId,
            value.ProfileDescriptorRevision,
            value.ProfileDescriptorSha256,
            value.RestoreCommandSha256,
            value.BuildCommandSha256,
            value.TestCommandSha256,
            value.SdkToolchainManifestId,
            value.ContainerImageDigest,
            value.SandboxPolicyVersion,
            value.SandboxPolicySha256,
            value.OfflineFeedManifestSha256,
            value.TemplateBundleSha256
        });
    }

    public static string ComputeHash(RepositoryReadinessAuthority authority) =>
        RepositoryReadinessCanonicalJson.Sha256(SerializeCanonical(authority));

    public static bool ExactMatch(
        RepositoryReadinessAuthority authority,
        RepositoryReadinessEvidenceBinding evidenceBinding) =>
        string.Equals(
            SerializeCanonical(authority),
            SerializeCanonical(ToAuthority(evidenceBinding)),
            StringComparison.Ordinal);

    public static RepositoryReadinessAuthority ToAuthority(
        RepositoryReadinessEvidenceBinding evidenceBinding)
    {
        var value = RepositoryReadinessEvidenceBindingCodec.NormalizeAndValidate(evidenceBinding);
        return new RepositoryReadinessAuthority
        {
            ProjectId = value.ProjectId,
            RepositoryBindingId = value.RepositoryBindingId,
            RepositoryBindingRevision = value.RepositoryBindingRevision,
            BaselineCommit = value.BaselineCommit,
            ProjectExecutionProfileId = value.ProjectExecutionProfileId,
            ProjectExecutionProfileRevision = value.ProjectExecutionProfileRevision,
            ProfileDefinitionId = value.ProfileDefinitionId,
            ProfileDescriptorRevision = value.ProfileDescriptorRevision,
            ProfileDescriptorSha256 = value.ProfileDescriptorSha256,
            RestoreCommandSha256 = value.RestoreCommandSha256,
            BuildCommandSha256 = value.BuildCommandSha256,
            TestCommandSha256 = value.TestCommandSha256,
            SdkToolchainManifestId = value.SdkToolchainManifestId,
            ContainerImageDigest = value.ContainerImageDigest,
            SandboxPolicyVersion = value.SandboxPolicyVersion,
            SandboxPolicySha256 = value.SandboxPolicySha256,
            OfflineFeedManifestSha256 = value.OfflineFeedManifestSha256,
            TemplateBundleSha256 = value.TemplateBundleSha256
        };
    }

    public static RepositoryReadinessAuthority NormalizeAndValidate(RepositoryReadinessAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.ProjectId <= 0 || authority.RepositoryBindingId == Guid.Empty ||
            authority.RepositoryBindingRevision <= 0 || authority.ProjectExecutionProfileId == Guid.Empty ||
            authority.ProjectExecutionProfileRevision <= 0 || authority.ProfileDescriptorRevision <= 0)
            throw new RepositoryReadinessValidationException(
                "Current readiness authority requires positive project/repository/profile revisions and non-empty IDs.");
        return authority with
        {
            BaselineCommit = RepositoryReadinessCanonicalJson.NormalizeGitObjectId(
                authority.BaselineCommit,
                nameof(authority.BaselineCommit)),
            ProfileDefinitionId = RepositoryReadinessCanonicalJson.NormalizeIdentifier(
                authority.ProfileDefinitionId,
                nameof(authority.ProfileDefinitionId)),
            ProfileDescriptorSha256 = Hash(authority.ProfileDescriptorSha256, nameof(authority.ProfileDescriptorSha256)),
            RestoreCommandSha256 = Hash(authority.RestoreCommandSha256, nameof(authority.RestoreCommandSha256)),
            BuildCommandSha256 = Hash(authority.BuildCommandSha256, nameof(authority.BuildCommandSha256)),
            TestCommandSha256 = Hash(authority.TestCommandSha256, nameof(authority.TestCommandSha256)),
            SdkToolchainManifestId = RepositoryReadinessCanonicalJson.NormalizeIdentifier(
                authority.SdkToolchainManifestId,
                nameof(authority.SdkToolchainManifestId)),
            ContainerImageDigest = OptionalContainerDigest(
                authority.ContainerImageDigest,
                nameof(authority.ContainerImageDigest)),
            SandboxPolicyVersion = OptionalIdentifier(
                authority.SandboxPolicyVersion,
                nameof(authority.SandboxPolicyVersion)),
            SandboxPolicySha256 = OptionalHash(authority.SandboxPolicySha256, nameof(authority.SandboxPolicySha256)),
            OfflineFeedManifestSha256 = OptionalHash(
                authority.OfflineFeedManifestSha256,
                nameof(authority.OfflineFeedManifestSha256)),
            TemplateBundleSha256 = Hash(authority.TemplateBundleSha256, nameof(authority.TemplateBundleSha256))
        };
    }

    private static string Hash(string value, string fieldName) =>
        RepositoryReadinessCanonicalJson.NormalizeSha256(value, fieldName);

    private static string? OptionalHash(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value) ? null : Hash(value, fieldName);

    private static string? OptionalIdentifier(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : RepositoryReadinessCanonicalJson.NormalizeIdentifier(value, fieldName);

    private static string? OptionalContainerDigest(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : RepositoryReadinessCanonicalJson.NormalizeContainerDigest(value, fieldName);
}

public static class RepositoryReadinessEvidenceBindingCodec
{
    public static string SerializeCanonical(RepositoryReadinessEvidenceBinding binding)
    {
        var value = NormalizeAndValidate(binding);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            value.ProjectId,
            value.RepositoryBindingId,
            value.RepositoryBindingRevision,
            value.BaselineCommit,
            value.RepositoryStateObservationId,
            value.WorktreeFingerprint,
            value.ProjectExecutionProfileId,
            value.ProjectExecutionProfileRevision,
            value.ProfileDefinitionId,
            value.ProfileDescriptorRevision,
            value.ProfileDescriptorSha256,
            value.RestoreCommandSha256,
            value.BuildCommandSha256,
            value.TestCommandSha256,
            value.SdkToolchainManifestId,
            value.ContainerImageDigest,
            value.SandboxPolicyVersion,
            value.SandboxPolicySha256,
            value.OfflineFeedManifestSha256,
            value.TemplateBundleSha256,
            value.CodeIndexSnapshotId,
            value.CodeIndexSnapshotRevision
        });
    }

    public static string ComputeHash(RepositoryReadinessEvidenceBinding binding) =>
        RepositoryReadinessCanonicalJson.Sha256(SerializeCanonical(binding));

    public static bool ExactMatch(
        RepositoryReadinessEvidenceBinding expected,
        RepositoryReadinessEvidenceBinding actual) =>
        string.Equals(SerializeCanonical(expected), SerializeCanonical(actual), StringComparison.Ordinal);

    public static RepositoryReadinessEvidenceBinding NormalizeAndValidate(
        RepositoryReadinessEvidenceBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.ProjectId <= 0 || binding.RepositoryBindingId == Guid.Empty ||
            binding.RepositoryBindingRevision <= 0 || binding.RepositoryStateObservationId == Guid.Empty ||
            binding.ProjectExecutionProfileId == Guid.Empty || binding.ProjectExecutionProfileRevision <= 0 ||
            binding.ProfileDescriptorRevision <= 0)
            throw new RepositoryReadinessValidationException(
                "Readiness evidence must bind positive project/repository/profile revisions and non-empty authority IDs.");
        if (binding.CodeIndexSnapshotId.HasValue != binding.CodeIndexSnapshotRevision.HasValue ||
            binding.CodeIndexSnapshotId == Guid.Empty || binding.CodeIndexSnapshotRevision is <= 0)
            throw new RepositoryReadinessValidationException(
                "Code-index identity and revision must either both be absent or both be valid.");

        return binding with
        {
            BaselineCommit = RepositoryReadinessCanonicalJson.NormalizeGitObjectId(
                binding.BaselineCommit,
                nameof(binding.BaselineCommit)),
            WorktreeFingerprint = RepositoryReadinessCanonicalJson.NormalizeSha256(
                binding.WorktreeFingerprint,
                nameof(binding.WorktreeFingerprint)),
            ProfileDefinitionId = RepositoryReadinessCanonicalJson.NormalizeIdentifier(
                binding.ProfileDefinitionId,
                nameof(binding.ProfileDefinitionId)),
            ProfileDescriptorSha256 = Hash(binding.ProfileDescriptorSha256, nameof(binding.ProfileDescriptorSha256)),
            RestoreCommandSha256 = Hash(binding.RestoreCommandSha256, nameof(binding.RestoreCommandSha256)),
            BuildCommandSha256 = Hash(binding.BuildCommandSha256, nameof(binding.BuildCommandSha256)),
            TestCommandSha256 = Hash(binding.TestCommandSha256, nameof(binding.TestCommandSha256)),
            SdkToolchainManifestId = RepositoryReadinessCanonicalJson.NormalizeIdentifier(
                binding.SdkToolchainManifestId,
                nameof(binding.SdkToolchainManifestId)),
            ContainerImageDigest = RepositoryReadinessCanonicalJson.NormalizeContainerDigest(
                binding.ContainerImageDigest,
                nameof(binding.ContainerImageDigest)),
            SandboxPolicyVersion = RepositoryReadinessCanonicalJson.NormalizeIdentifier(
                binding.SandboxPolicyVersion,
                nameof(binding.SandboxPolicyVersion)),
            SandboxPolicySha256 = Hash(binding.SandboxPolicySha256, nameof(binding.SandboxPolicySha256)),
            OfflineFeedManifestSha256 = Hash(
                binding.OfflineFeedManifestSha256,
                nameof(binding.OfflineFeedManifestSha256)),
            TemplateBundleSha256 = Hash(binding.TemplateBundleSha256, nameof(binding.TemplateBundleSha256))
        };
    }

    private static string Hash(string value, string fieldName) =>
        RepositoryReadinessCanonicalJson.NormalizeSha256(value, fieldName);
}

public static class RepositoryStateObservationCodec
{
    public static string ComputeEvidenceHash(RepositoryStateObservation observation) =>
        RepositoryReadinessCanonicalJson.Sha256(SerializeEvidenceMaterial(observation));

    public static string SerializeCanonical(RepositoryStateObservation observation)
    {
        var value = NormalizeAndValidate(observation, requireEvidenceHashMatch: true);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            value.Id,
            value.RepositoryBindingId,
            value.RepositoryBindingRevision,
            value.BaselineCommit,
            value.HeadCommit,
            value.GitTreeId,
            value.WorktreeState,
            value.WorktreeFingerprint,
            value.ObservedAtUtc,
            value.EvidenceHash
        });
    }

    public static RepositoryStateObservation NormalizeAndValidate(
        RepositoryStateObservation observation,
        bool requireEvidenceHashMatch = true)
    {
        var value = NormalizeWithoutEvidenceCheck(observation);
        var evidenceHash = RepositoryReadinessCanonicalJson.NormalizeSha256(
            observation.EvidenceHash,
            nameof(observation.EvidenceHash));
        value = value with { EvidenceHash = evidenceHash };
        if (requireEvidenceHashMatch && !string.Equals(
                evidenceHash,
                ComputeEvidenceHash(value),
                StringComparison.Ordinal))
            throw new RepositoryReadinessValidationException("Repository observation evidence hash does not match its content.");
        return value;
    }

    private static string SerializeEvidenceMaterial(RepositoryStateObservation observation)
    {
        var value = NormalizeWithoutEvidenceCheck(observation);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            value.Id,
            value.RepositoryBindingId,
            value.RepositoryBindingRevision,
            value.BaselineCommit,
            value.HeadCommit,
            value.GitTreeId,
            value.WorktreeState,
            value.WorktreeFingerprint,
            value.ObservedAtUtc
        });
    }

    private static RepositoryStateObservation NormalizeWithoutEvidenceCheck(RepositoryStateObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Id == Guid.Empty || observation.RepositoryBindingId == Guid.Empty ||
            observation.RepositoryBindingRevision <= 0)
            throw new RepositoryReadinessValidationException(
                "Repository observation must bind a non-empty repository authority and positive revision.");
        if (observation.WorktreeState is not RepositoryWorktreeStates.Clean and
            not RepositoryWorktreeStates.Dirty and not RepositoryWorktreeStates.Unknown)
            throw new RepositoryReadinessValidationException("Repository observation worktree state is invalid.");
        return observation with
        {
            BaselineCommit = RepositoryReadinessCanonicalJson.NormalizeGitObjectId(
                observation.BaselineCommit,
                nameof(observation.BaselineCommit)),
            HeadCommit = RepositoryReadinessCanonicalJson.NormalizeGitObjectId(
                observation.HeadCommit,
                nameof(observation.HeadCommit)),
            GitTreeId = RepositoryReadinessCanonicalJson.NormalizeGitObjectId(
                observation.GitTreeId,
                nameof(observation.GitTreeId)),
            WorktreeFingerprint = RepositoryReadinessCanonicalJson.NormalizeSha256(
                observation.WorktreeFingerprint,
                nameof(observation.WorktreeFingerprint)),
            ObservedAtUtc = RepositoryReadinessCanonicalJson.NormalizeUtc(
                observation.ObservedAtUtc,
                nameof(observation.ObservedAtUtc))
        };
    }
}

public static class RepositoryValidationRecordCodec
{
    public static string SerializeCanonical(BuildValidationRecord record)
    {
        var value = NormalizeAndValidate(record);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            value.Id,
            value.Revision,
            Binding = CanonicalBinding(value.Binding),
            RestoreResult = CanonicalCommandResult(value.RestoreResult),
            BuildResult = CanonicalCommandResult(value.BuildResult),
            value.ValidatedAtUtc,
            value.EvidenceManifestSha256
        });
    }

    public static string SerializeCanonical(TestValidationRecord record)
    {
        var value = NormalizeAndValidate(record);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            value.Id,
            value.Revision,
            Binding = CanonicalBinding(value.Binding),
            TestResult = CanonicalCommandResult(value.TestResult),
            value.ValidatedAtUtc,
            value.EvidenceManifestSha256
        });
    }

    public static string ComputeHash(BuildValidationRecord record) =>
        RepositoryReadinessCanonicalJson.Sha256(SerializeCanonical(record));

    public static string ComputeHash(TestValidationRecord record) =>
        RepositoryReadinessCanonicalJson.Sha256(SerializeCanonical(record));

    public static BuildValidationRecord NormalizeAndValidate(BuildValidationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecordIdentity(record.Id, record.Revision);
        var binding = RepositoryReadinessEvidenceBindingCodec.NormalizeAndValidate(record.Binding);
        RequireNoIndex(binding);
        var restore = NormalizeCommandResult(record.RestoreResult);
        var build = NormalizeCommandResult(record.BuildResult);
        if (!string.Equals(restore.CommandSha256, binding.RestoreCommandSha256, StringComparison.Ordinal) ||
            !string.Equals(build.CommandSha256, binding.BuildCommandSha256, StringComparison.Ordinal))
            throw new RepositoryReadinessValidationException(
                "Build validation command results do not match their exact evidence binding.");
        return record with
        {
            Binding = binding,
            RestoreResult = restore,
            BuildResult = build,
            ValidatedAtUtc = RepositoryReadinessCanonicalJson.NormalizeUtc(record.ValidatedAtUtc, nameof(record.ValidatedAtUtc)),
            EvidenceManifestSha256 = Hash(record.EvidenceManifestSha256, nameof(record.EvidenceManifestSha256))
        };
    }

    public static TestValidationRecord NormalizeAndValidate(TestValidationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecordIdentity(record.Id, record.Revision);
        var binding = RepositoryReadinessEvidenceBindingCodec.NormalizeAndValidate(record.Binding);
        RequireNoIndex(binding);
        var test = NormalizeCommandResult(record.TestResult);
        if (!string.Equals(test.CommandSha256, binding.TestCommandSha256, StringComparison.Ordinal))
            throw new RepositoryReadinessValidationException(
                "Test validation command result does not match its exact evidence binding.");
        return record with
        {
            Binding = binding,
            TestResult = test,
            ValidatedAtUtc = RepositoryReadinessCanonicalJson.NormalizeUtc(record.ValidatedAtUtc, nameof(record.ValidatedAtUtc)),
            EvidenceManifestSha256 = Hash(record.EvidenceManifestSha256, nameof(record.EvidenceManifestSha256))
        };
    }

    private static RepositoryValidationCommandResult NormalizeCommandResult(
        RepositoryValidationCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Outcome) || result.DurationMilliseconds < 0 ||
            (result.Outcome == RepositoryValidationOutcome.Passed && (result.ExitCode != 0 || result.TimedOut)) ||
            (result.Outcome == RepositoryValidationOutcome.TimedOut && !result.TimedOut))
            throw new RepositoryReadinessValidationException("Validation command outcome is internally inconsistent.");
        return result with
        {
            CommandSha256 = Hash(result.CommandSha256, nameof(result.CommandSha256)),
            StandardOutputSha256 = Hash(result.StandardOutputSha256, nameof(result.StandardOutputSha256)),
            StandardErrorSha256 = Hash(result.StandardErrorSha256, nameof(result.StandardErrorSha256))
        };
    }

    private static object CanonicalCommandResult(RepositoryValidationCommandResult result) => new
    {
        result.CommandSha256,
        Outcome = result.Outcome.ToString(),
        result.ExitCode,
        result.TimedOut,
        result.DurationMilliseconds,
        result.StandardOutputSha256,
        result.StandardErrorSha256
    };

    private static object CanonicalBinding(RepositoryReadinessEvidenceBinding binding) =>
        JsonSerializer.Deserialize<JsonElement>(RepositoryReadinessEvidenceBindingCodec.SerializeCanonical(binding));

    private static void ValidateRecordIdentity(Guid id, long revision)
    {
        if (id == Guid.Empty || revision <= 0)
            throw new RepositoryReadinessValidationException("Validation record identity and revision are required.");
    }

    private static void RequireNoIndex(RepositoryReadinessEvidenceBinding binding)
    {
        if (binding.CodeIndexSnapshotId.HasValue || binding.CodeIndexSnapshotRevision.HasValue)
            throw new RepositoryReadinessValidationException(
                "Build and test evidence must use the universal pre-index binding.");
    }

    private static string Hash(string value, string fieldName) =>
        RepositoryReadinessCanonicalJson.NormalizeSha256(value, fieldName);
}

public static class CodeIndexSnapshotCodec
{
    public const int MaximumSources = 4_096;
    public const int MaximumRelativePathLength = 1_000;

    public static string ComputeIndexedContentHash(IReadOnlyList<CodeIndexSourceFingerprint> sources) =>
        RepositoryReadinessCanonicalJson.Sha256(SerializeSources(sources));

    public static string ComputeHash(CodeIndexSnapshot snapshot) =>
        RepositoryReadinessCanonicalJson.Sha256(SerializeCanonical(snapshot));

    public static string SerializeCanonical(CodeIndexSnapshot snapshot)
    {
        var value = NormalizeAndValidate(snapshot);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            value.Id,
            value.Revision,
            Binding = JsonSerializer.Deserialize<JsonElement>(
                RepositoryReadinessEvidenceBindingCodec.SerializeCanonical(value.Binding)),
            value.State,
            value.IndexFormatVersion,
            Sources = value.Sources.Select(static source => new
            {
                source.Ordinal,
                source.RelativePath,
                source.ContentSha256
            }).ToArray(),
            value.IndexedContentSha256,
            value.IndexedAtUtc,
            value.EvidenceManifestSha256
        });
    }

    public static CodeIndexSnapshot NormalizeAndValidate(CodeIndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Id == Guid.Empty || snapshot.Revision <= 0 ||
            snapshot.State is not RepositoryCodeIndexStates.Current and not RepositoryCodeIndexStates.Failed)
            throw new RepositoryReadinessValidationException("Code-index identity, revision, and state are invalid.");
        var binding = RepositoryReadinessEvidenceBindingCodec.NormalizeAndValidate(snapshot.Binding);
        if (binding.CodeIndexSnapshotId != snapshot.Id || binding.CodeIndexSnapshotRevision != snapshot.Revision)
            throw new RepositoryReadinessValidationException("Code-index evidence does not bind its own exact identity and revision.");
        var sources = NormalizeSources(snapshot.Sources);
        var indexedContentHash = RepositoryReadinessCanonicalJson.NormalizeSha256(
            snapshot.IndexedContentSha256,
            nameof(snapshot.IndexedContentSha256));
        if (!string.Equals(indexedContentHash, ComputeIndexedContentHash(sources), StringComparison.Ordinal))
            throw new RepositoryReadinessValidationException("Code-index content hash does not match its ordered sources.");
        return snapshot with
        {
            Binding = binding,
            IndexFormatVersion = RepositoryReadinessCanonicalJson.NormalizeIdentifier(
                snapshot.IndexFormatVersion,
                nameof(snapshot.IndexFormatVersion)),
            Sources = sources,
            IndexedContentSha256 = indexedContentHash,
            IndexedAtUtc = RepositoryReadinessCanonicalJson.NormalizeUtc(snapshot.IndexedAtUtc, nameof(snapshot.IndexedAtUtc)),
            EvidenceManifestSha256 = RepositoryReadinessCanonicalJson.NormalizeSha256(
                snapshot.EvidenceManifestSha256,
                nameof(snapshot.EvidenceManifestSha256))
        };
    }

    private static string SerializeSources(IReadOnlyList<CodeIndexSourceFingerprint> sources)
    {
        var normalized = NormalizeSources(sources);
        return RepositoryReadinessCanonicalJson.Serialize(normalized.Select(static source => new
        {
            source.Ordinal,
            source.RelativePath,
            source.ContentSha256
        }).ToArray());
    }

    private static IReadOnlyList<CodeIndexSourceFingerprint> NormalizeSources(
        IReadOnlyList<CodeIndexSourceFingerprint> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count > MaximumSources)
            throw new RepositoryReadinessValidationException(
                $"Code-index snapshots cannot contain more than {MaximumSources} sources.");
        var normalized = new CodeIndexSourceFingerprint[sources.Count];
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index] ?? throw new RepositoryReadinessValidationException(
                "Code-index source entries cannot be null.");
            if (source.Ordinal != index + 1 || string.IsNullOrWhiteSpace(source.RelativePath) ||
                source.RelativePath.Length > MaximumRelativePathLength ||
                source.RelativePath != source.RelativePath.Trim() || Path.IsPathRooted(source.RelativePath) ||
                source.RelativePath.Contains('\\') || source.RelativePath.Split('/').Any(static segment => segment is "" or "." or ".."))
                throw new RepositoryReadinessValidationException(
                    "Code-index source paths and semantic ordinals must be safe and contiguous.");
            normalized[index] = source with
            {
                ContentSha256 = RepositoryReadinessCanonicalJson.NormalizeSha256(
                    source.ContentSha256,
                    nameof(source.ContentSha256))
            };
        }
        return normalized;
    }
}

public static class RepositorySandboxQualificationEvidenceCodec
{
    public static string ComputeHash(RepositorySandboxQualificationEvidence evidence) =>
        RepositoryReadinessCanonicalJson.Sha256(SerializeCanonical(evidence));

    public static string SerializeCanonical(RepositorySandboxQualificationEvidence evidence)
    {
        var value = NormalizeAndValidate(evidence);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            value.QualificationAttemptId,
            value.Revision,
            Binding = JsonSerializer.Deserialize<JsonElement>(
                RepositoryReadinessEvidenceBindingCodec.SerializeCanonical(value.Binding)),
            value.State,
            value.ValidatedAtUtc,
            value.EvidenceManifestSha256
        });
    }

    public static RepositorySandboxQualificationEvidence NormalizeAndValidate(
        RepositorySandboxQualificationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.QualificationAttemptId == Guid.Empty || evidence.Revision <= 0 ||
            evidence.State is not RepositorySandboxQualificationEvidenceStates.Passed and
            not RepositorySandboxQualificationEvidenceStates.Failed)
            throw new RepositoryReadinessValidationException("Sandbox qualification evidence identity or state is invalid.");
        var binding = RepositoryReadinessEvidenceBindingCodec.NormalizeAndValidate(evidence.Binding);
        if (binding.CodeIndexSnapshotId.HasValue || binding.CodeIndexSnapshotRevision.HasValue)
            throw new RepositoryReadinessValidationException("Sandbox evidence must use the universal pre-index binding.");
        return evidence with
        {
            Binding = binding,
            ValidatedAtUtc = RepositoryReadinessCanonicalJson.NormalizeUtc(evidence.ValidatedAtUtc, nameof(evidence.ValidatedAtUtc)),
            EvidenceManifestSha256 = RepositoryReadinessCanonicalJson.NormalizeSha256(
                evidence.EvidenceManifestSha256,
                nameof(evidence.EvidenceManifestSha256))
        };
    }
}

public static class BuilderStableConfigurationEvidenceCodec
{
    public static string SerializeCanonical(BuilderStableConfigurationBinding binding)
    {
        var value = NormalizeAndValidate(binding);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            value.ConfigurationId,
            value.Revision,
            value.ProviderId,
            value.ModelId,
            value.ConfigurationSha256
        });
    }

    public static string SerializeCanonical(BuilderStableConfigurationEvidence evidence)
    {
        var value = NormalizeAndValidate(evidence);
        return RepositoryReadinessCanonicalJson.Serialize(new
        {
            Binding = JsonSerializer.Deserialize<JsonElement>(SerializeCanonical(value.Binding)),
            value.IsConfigured,
            value.ValidatedAtUtc,
            value.EvidenceManifestSha256
        });
    }

    public static string ComputeHash(BuilderStableConfigurationEvidence evidence) =>
        RepositoryReadinessCanonicalJson.Sha256(SerializeCanonical(evidence));

    public static BuilderStableConfigurationBinding NormalizeAndValidate(
        BuilderStableConfigurationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.ConfigurationId == Guid.Empty || binding.Revision <= 0)
            throw new RepositoryReadinessValidationException("Builder configuration identity and revision are required.");
        return binding with
        {
            ProviderId = RepositoryReadinessCanonicalJson.NormalizeIdentifier(binding.ProviderId, nameof(binding.ProviderId)),
            ModelId = RepositoryReadinessCanonicalJson.NormalizeIdentifier(binding.ModelId, nameof(binding.ModelId)),
            ConfigurationSha256 = RepositoryReadinessCanonicalJson.NormalizeSha256(
                binding.ConfigurationSha256,
                nameof(binding.ConfigurationSha256))
        };
    }

    public static BuilderStableConfigurationEvidence NormalizeAndValidate(
        BuilderStableConfigurationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence with
        {
            Binding = NormalizeAndValidate(evidence.Binding),
            ValidatedAtUtc = RepositoryReadinessCanonicalJson.NormalizeUtc(evidence.ValidatedAtUtc, nameof(evidence.ValidatedAtUtc)),
            EvidenceManifestSha256 = RepositoryReadinessCanonicalJson.NormalizeSha256(
                evidence.EvidenceManifestSha256,
                nameof(evidence.EvidenceManifestSha256))
        };
    }
}

/// <summary>
/// Pure, timestamp-independent readiness evaluator. It neither checks nor grants Builder
/// authorization and deliberately ignores transient provider availability when projecting state.
/// </summary>
public static class RepositoryReadinessEvaluator
{
    public static readonly IReadOnlyList<RepositoryReadinessGateName> NormativeGates =
        Enum.GetValues<RepositoryReadinessGateName>();

    public static RepositoryReadinessEvaluationResult Evaluate(RepositoryReadinessEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ProjectId <= 0)
            throw new RepositoryReadinessValidationException("A positive project identity is required.");

        RepositoryReadinessAuthority? authority = null;
        var currentAuthorityValid = Try(() =>
        {
            authority = RepositoryReadinessAuthorityCodec.NormalizeAndValidate(context.CurrentAuthority!);
            return authority.ProjectId == context.ProjectId;
        });
        var repositoryQualified = currentAuthorityValid && string.Equals(
            context.RepositoryBindingState,
            RepositoryBindingStates.Qualified,
            StringComparison.Ordinal);
        var profilePinned = currentAuthorityValid;

        RepositoryStateObservation? observation = null;
        var observationCurrent = currentAuthorityValid && Try(() =>
        {
            observation = RepositoryStateObservationCodec.NormalizeAndValidate(context.RepositoryObservation!);
            return observation.RepositoryBindingId == authority!.RepositoryBindingId &&
                   observation.RepositoryBindingRevision == authority.RepositoryBindingRevision &&
                   string.Equals(observation.BaselineCommit, authority.BaselineCommit, StringComparison.Ordinal);
        });
        var clean = observationCurrent &&
                    string.Equals(observation!.HeadCommit, authority!.BaselineCommit, StringComparison.Ordinal) &&
                    string.Equals(observation.WorktreeState, RepositoryWorktreeStates.Clean, StringComparison.Ordinal);

        BuildValidationRecord? build = null;
        var buildBindingCurrent = observationCurrent && Try(() =>
        {
            build = RepositoryValidationRecordCodec.NormalizeAndValidate(context.BuildValidation!);
            return BindingMatchesCurrentAuthority(authority!, observation!, build.Binding, null, null);
        });
        var restorePassed = buildBindingCurrent && IsPassed(build!.RestoreResult);
        var buildPassed = buildBindingCurrent && IsPassed(build!.BuildResult);

        TestValidationRecord? test = null;
        var testBindingCurrent = observationCurrent && Try(() =>
        {
            test = RepositoryValidationRecordCodec.NormalizeAndValidate(context.TestValidation!);
            return BindingMatchesCurrentAuthority(authority!, observation!, test.Binding, null, null);
        });
        var testPassed = testBindingCurrent && IsPassed(test!.TestResult);

        var indexCurrent = observationCurrent && Try(() =>
        {
            var index = CodeIndexSnapshotCodec.NormalizeAndValidate(context.CodeIndex!);
            return string.Equals(index.State, RepositoryCodeIndexStates.Current, StringComparison.Ordinal) &&
                   BindingMatchesCurrentAuthority(
                       authority!,
                       observation!,
                       index.Binding,
                       index.Id,
                       index.Revision);
        });

        var sandboxCurrent = observationCurrent && Try(() =>
        {
            var sandbox = RepositorySandboxQualificationEvidenceCodec.NormalizeAndValidate(context.SandboxQualification!);
            return string.Equals(
                       sandbox.State,
                       RepositorySandboxQualificationEvidenceStates.Passed,
                       StringComparison.Ordinal) &&
                   BindingMatchesCurrentAuthority(authority!, observation!, sandbox.Binding, null, null);
        });

        var builderConfigured = Try(() =>
        {
            var expected = BuilderStableConfigurationEvidenceCodec.NormalizeAndValidate(
                context.CurrentBuilderConfiguration!);
            var evidence = BuilderStableConfigurationEvidenceCodec.NormalizeAndValidate(
                context.BuilderConfigurationEvidence!);
            return evidence.IsConfigured && string.Equals(
                BuilderStableConfigurationEvidenceCodec.SerializeCanonical(expected),
                BuilderStableConfigurationEvidenceCodec.SerializeCanonical(evidence.Binding),
                StringComparison.Ordinal);
        });

        var gates = new[]
        {
            Gate(RepositoryReadinessGateName.RepositoryBindingQualified, repositoryQualified,
                RepositoryReadinessReasonCodes.RepositoryNotConfigured),
            Gate(RepositoryReadinessGateName.RepositoryCleanAtBaseline, clean,
                context.RepositoryObservation is null
                    ? RepositoryReadinessReasonCodes.RepositoryObservationRequired
                    : RepositoryReadinessReasonCodes.RepositoryObservationStale),
            Gate(RepositoryReadinessGateName.ExecutionProfilePinned, profilePinned,
                RepositoryReadinessReasonCodes.ExecutionProfileNotConfigured),
            Gate(RepositoryReadinessGateName.RestorePassed, restorePassed,
                RepositoryReadinessReasonCodes.RestoreValidationRequired),
            Gate(RepositoryReadinessGateName.BuildPassed, buildPassed,
                RepositoryReadinessReasonCodes.BuildValidationRequired),
            Gate(RepositoryReadinessGateName.TestCommandPassed, testPassed,
                RepositoryReadinessReasonCodes.TestValidationRequired),
            Gate(RepositoryReadinessGateName.CodeIndexCurrent, indexCurrent,
                RepositoryReadinessReasonCodes.CodeIndexRequired),
            Gate(RepositoryReadinessGateName.SandboxQualified, sandboxCurrent,
                RepositoryReadinessReasonCodes.SandboxQualificationRequired),
            Gate(RepositoryReadinessGateName.BuilderModelConfigured, builderConfigured,
                RepositoryReadinessReasonCodes.BuilderModelConfigurationRequired)
        };

        var readiness = !repositoryQualified || !profilePinned
            ? ProjectExecutionReadinessStates.NotConfigured
            : gates.All(static gate => gate.Passed)
                ? ProjectExecutionReadinessStates.Ready
                : ProjectExecutionReadinessStates.ValidationRequired;
        var reason = gates.FirstOrDefault(static gate => !gate.Passed)?.ReasonCode ??
                     RepositoryReadinessReasonCodes.Ready;
        string? currentAuthorityHash = currentAuthorityValid
            ? RepositoryReadinessAuthorityCodec.ComputeHash(authority!)
            : null;

        return new RepositoryReadinessEvaluationResult(
            readiness,
            reason,
            currentAuthorityHash,
            gates,
            context.Availability);
    }

    private static bool BindingMatchesCurrentAuthority(
        RepositoryReadinessAuthority authority,
        RepositoryStateObservation observation,
        RepositoryReadinessEvidenceBinding binding,
        Guid? codeIndexSnapshotId,
        long? codeIndexSnapshotRevision)
    {
        var value = RepositoryReadinessEvidenceBindingCodec.NormalizeAndValidate(binding);
        return RepositoryReadinessAuthorityCodec.ExactMatch(authority, value) &&
               value.RepositoryStateObservationId == observation.Id &&
               string.Equals(value.WorktreeFingerprint, observation.WorktreeFingerprint, StringComparison.Ordinal) &&
               value.CodeIndexSnapshotId == codeIndexSnapshotId &&
               value.CodeIndexSnapshotRevision == codeIndexSnapshotRevision;
    }

    private static RepositoryReadinessGateResult Gate(
        RepositoryReadinessGateName name,
        bool passed,
        string failureReason) => new(
            name,
            passed,
            passed ? RepositoryReadinessReasonCodes.Ready : failureReason);

    private static bool IsPassed(RepositoryValidationCommandResult result) =>
        result.Outcome == RepositoryValidationOutcome.Passed && result.ExitCode == 0 && !result.TimedOut;

    private static bool Try(Func<bool> evaluation)
    {
        try
        {
            return evaluation();
        }
        catch (Exception exception) when (exception is
                   ArgumentNullException or RepositoryReadinessValidationException)
        {
            return false;
        }
    }
}
