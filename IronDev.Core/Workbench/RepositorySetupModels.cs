using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IronDev.Core.Workbench;

public static class RepositorySetupProfileIds
{
    public const string GreenfieldWinFormsNet10MstestV1 = "greenfield-winforms-net10-mstest-v1";
}

public static class RepositorySetupPinnedProfileHashes
{
    public const string GreenfieldWinFormsNet10MstestV1DescriptorRevision1 =
        "454cd776e530863304dac99203eba05c68d97a4823430ac41d1a58fe6e8655db";
    public const string GreenfieldWinFormsNet10MstestV1TemplateBundleRevision1 =
        "795621280226d02dfa95f5c4e0f0be84313bfd026050951fe9222d2324c41bde";
}

public static class RepositorySetupPreviewStates
{
    public const string ReadyForConfirmation = "ReadyForConfirmation";
    public const string UnsupportedProfile = "UnsupportedProfile";
    public const string EnvironmentUnavailable = "EnvironmentUnavailable";
    public const string NeedsConfirmation = "NeedsConfirmation";
}

public static class RepositoryProfileCompatibilityStates
{
    public const string Compatible = "Compatible";
    public const string Incompatible = "Incompatible";
    public const string NeedsConfirmation = "NeedsConfirmation";
    public const string NoPreference = "NoPreference";
}

public static class RepositoryPlanningReadinessStates
{
    public const string PreviewPlanningOnly = "PreviewPlanningOnly";
}

public static class RepositoryProfileCertificationStates
{
    public const string NotCertificationReady = "NotCertificationReady";
}

public static class RepositoryKinds
{
    public const string Greenfield = "Greenfield";
    public const string Existing = "Existing";
}

public static class RepositoryBindingStates
{
    public const string SetupConfirmed = "SetupConfirmed";
    public const string LegacyUnverified = "LegacyUnverified";
    public const string Provisioning = "Provisioning";
    public const string Qualified = "Qualified";
    public const string ProvisioningFailed = "ProvisioningFailed";
}

public static class RepositorySetupEnvironmentCapabilityStates
{
    public const string Available = "Available";
    public const string Unavailable = "Unavailable";
    public const string Unsafe = "Unsafe";
}

public static class RepositorySetupReasonCodes
{
    public const string Ready = "RepositorySetupReadyForConfirmation";
    public const string UnknownProfile = "RepositorySetupProfileUnknown";
    public const string IncompatibleProfile = "RepositorySetupProfileIncompatible";
    public const string PreferenceNeedsConfirmation = "RepositorySetupPreferenceNeedsConfirmation";
    public const string WorkspaceRootNotConfigured = "RepositorySetupWorkspaceRootNotConfigured";
    public const string WorkspaceRootUnavailable = "RepositorySetupWorkspaceRootUnavailable";
    public const string RepositoryProvisioningPending = "RepositoryProvisioningPending";
}

public static class RepositorySetupOperationKinds
{
    public const string Confirm = "ConfirmRepositorySetup";
}

public sealed record RepositorySetupProfileDescriptor(
    string ProfileDefinitionId,
    int Revision,
    string DisplayName,
    string TargetFramework,
    string Language,
    string ApplicationKind,
    string TestFramework,
    string SdkVersion,
    string RuntimeVersion,
    string ToolchainManifestId,
    string ExecutionImageReference,
    string SolutionPathTemplate,
    string AppProjectPathTemplate,
    string TestProjectPathTemplate,
    string RestoreCommandTemplate,
    string BuildCommandTemplate,
    string TestCommandTemplate,
    string PlanningReadiness,
    string CertificationState,
    RepositorySetupTemplateBundle TemplateBundle,
    string DescriptorSha256,
    string TemplateBundleSha256);

public sealed record RepositorySetupTemplateFileDefinition(
    int Order,
    string RelativePath,
    string Utf8Content);

public sealed record RepositorySetupTemplateBundle(
    int SchemaVersion,
    string ProfileDefinitionId,
    IReadOnlyList<RepositorySetupTemplateFileDefinition> Files);

public sealed record RepositorySetupProfileSummary(
    string ProfileDefinitionId,
    string DisplayName,
    string Compatibility,
    string CompatibilityReason,
    string PlanningReadiness,
    string CertificationState,
    string DescriptorSha256,
    string TemplateBundleSha256);

public sealed record RepositoryBindingSnapshot(
    Guid Id,
    int ProjectId,
    long Revision,
    string RepositoryKind,
    string CanonicalPath,
    string BindingState,
    string? DefaultBranch,
    string? BaselineCommit,
    int? CreatedByActorUserId,
    DateTime? ConfirmedAtUtc);

/// <summary>
/// Produces the one compatibility projection permitted for legacy Projects.LocalPath data.
/// This projection is descriptive only: it never upgrades an unverified path into repository
/// authority and never inspects the filesystem.
/// </summary>
public static class RepositoryBindingProjection
{
    public static RepositoryBindingSnapshot? CreateLegacy(int projectId, string? localPath)
    {
        if (projectId <= 0 || string.IsNullOrWhiteSpace(localPath))
            return null;

        var canonicalPath = localPath.Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"legacy-repository-binding-v1\n{projectId}\n{canonicalPath}"));
        var guidBytes = bytes[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0f) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);

        return new RepositoryBindingSnapshot(
            new Guid(guidBytes),
            projectId,
            1,
            RepositoryKinds.Existing,
            canonicalPath,
            RepositoryBindingStates.LegacyUnverified,
            DefaultBranch: null,
            BaselineCommit: null,
            CreatedByActorUserId: null,
            ConfirmedAtUtc: null);
    }
}

public sealed record ProjectExecutionProfileSnapshot(
    Guid Id,
    int ProjectId,
    long Revision,
    Guid RepositoryBindingId,
    string ProfileDefinitionId,
    int ProfileDescriptorRevision,
    string DescriptorSha256,
    string TemplateBundleSha256,
    string PlanningBundleSha256,
    string TargetFramework,
    string Language,
    string ApplicationKind,
    string TestFramework,
    string SdkVersion,
    string RuntimeVersion,
    string SolutionPath,
    string AppProjectPath,
    string TestProjectPath,
    string RestoreCommand,
    string BuildCommand,
    string TestCommand,
    string ToolchainManifestId,
    string ExecutionImageReference,
    string PlanningReadiness,
    string CertificationState);

public sealed record RepositorySetupEnvironmentCapability(
    string State,
    string ReasonCode,
    string Message,
    string SuggestedTarget);

public sealed record RepositorySetupConfirmationSnapshot(
    Guid ConfirmationId,
    string PlanHash,
    DateTime ConfirmedAtUtc,
    Guid ClientOperationId,
    long WorkbenchSessionId,
    long LeaseEpoch);

public sealed record RepositorySetupContext(
    int ProjectId,
    int TenantId,
    string ProjectName,
    string ProjectLifecyclePhase,
    string ExecutionReadiness,
    string ReadinessReasonCode,
    RepositoryBindingSnapshot? RepositoryBinding,
    ProjectExecutionProfileSnapshot? ExecutionProfile,
    RepositorySetupConfirmationSnapshot? LatestConfirmation,
    RepositorySetupEnvironmentCapability EnvironmentCapability,
    IReadOnlyList<RepositorySetupProfileSummary> AvailableProfiles);

public sealed record RepositorySetupPlanPreview(
    int SchemaVersion,
    string Source,
    int ProjectId,
    string CanonicalProjectName,
    long WorkbenchSessionId,
    long LeaseEpoch,
    long BasedOnUnderstandingRevision,
    string BasedOnUnderstandingHash,
    int ProfileDescriptorRevision,
    string ProfileDescriptorSha256,
    string State,
    string ReasonCode,
    string Message,
    RepositorySetupProfileSummary Profile,
    string TargetPath,
    string SolutionName,
    string AppProjectName,
    string TestProjectName,
    string SolutionPath,
    string AppProjectPath,
    string TestProjectPath,
    string TemplateBundleSha256,
    string PlanningBundleSha256,
    string TargetFramework,
    string Language,
    string ApplicationKind,
    string TestFramework,
    string SdkVersion,
    string RuntimeVersion,
    string RestoreCommand,
    string BuildCommand,
    string TestCommand,
    string ToolchainManifestId,
    string ExecutionImageReference,
    string DefaultBranch,
    bool InitializeGit,
    bool IndexAfterProvisioning,
    string SandboxValidation,
    string ResourcePolicy,
    string PlanHash);

public sealed record RepositorySetupConfirmationResult(
    int ProjectId,
    Guid ConfirmationId,
    Guid ClientOperationId,
    bool IsReplay,
    string ProjectLifecyclePhase,
    string ExecutionReadiness,
    string ReadinessReasonCode,
    RepositoryBindingSnapshot RepositoryBinding,
    ProjectExecutionProfileSnapshot ExecutionProfile,
    RepositorySetupPlanPreview SetupPlan);

public sealed record GetRepositorySetupContextQuery(
    int TenantId,
    int ActorUserId,
    int ProjectId);

public sealed record CreateRepositorySetupPlanCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    string ProfileDefinitionId);

public sealed record ConfirmRepositorySetupCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    string ExpectedPlanHash);

public interface IWorkbenchRepositorySetupService
{
    Task<RepositorySetupContext> GetContextAsync(
        GetRepositorySetupContextQuery query,
        CancellationToken cancellationToken = default);

    Task<RepositorySetupPlanPreview> PreviewAsync(
        CreateRepositorySetupPlanCommand command,
        CancellationToken cancellationToken = default);

    Task<RepositorySetupConfirmationResult> ConfirmAsync(
        ConfirmRepositorySetupCommand command,
        CancellationToken cancellationToken = default);
}

public interface IRepositorySetupProfileCatalog
{
    IReadOnlyList<RepositorySetupProfileDescriptor> GetAll();
    RepositorySetupProfileDescriptor? Find(string profileDefinitionId, int? revision = null, string? descriptorSha256 = null);
}

public interface IRepositorySetupTemplateBundleCatalog
{
    RepositorySetupTemplateBundle? FindBundle(
        string profileDefinitionId,
        int revision,
        string descriptorSha256);
}

public sealed record RepositorySetupPathAssessment(
    bool IsAvailable,
    bool IsUnsafe,
    string ReasonCode,
    string Message,
    string ApprovedWorkspaceRoot,
    string TargetPath);

public interface IRepositorySetupPathPolicy
{
    RepositorySetupPathAssessment Assess(
        string approvedWorkspaceRoot,
        string directChildName,
        bool inspectEnvironment);
}

public interface IRepositorySetupFileSystemInspector
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    FileAttributes GetAttributes(string path);
}

public interface IRepositorySetupForbiddenRootCatalog
{
    IReadOnlyList<string> GetForbiddenRoots();
}

public enum RepositorySetupConfirmationFailurePoint
{
    ClientOperationCreated,
    BindingCreated,
    ExecutionProfileCreated,
    ReadinessCreated,
    ConfirmationCreated,
    OutboxCreated
}

public interface IRepositorySetupConfirmationFailureInjector
{
    void ThrowIfRequested(RepositorySetupConfirmationFailurePoint point);
}

public sealed class NoOpRepositorySetupConfirmationFailureInjector : IRepositorySetupConfirmationFailureInjector
{
    public void ThrowIfRequested(RepositorySetupConfirmationFailurePoint point)
    {
    }
}

public static class RepositorySetupCanonicalJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class RepositorySetupTemplateBundleCodec
{
    private static readonly Regex TokenPattern = new("\\{\\{[A-Z0-9_]+\\}\\}", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> AllowedTokens = new(StringComparer.Ordinal)
    {
        "{{SOLUTION_NAME}}", "{{APP_PROJECT_NAME}}", "{{APP_PROJECT_NAME_LOWER}}",
        "{{TEST_PROJECT_NAME}}"
    };
    private static readonly HashSet<string> WindowsReservedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5",
        "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
        "LPT6", "LPT7", "LPT8", "LPT9"
    };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string ComputeHash(RepositorySetupTemplateBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.SchemaVersion != 1 || string.IsNullOrWhiteSpace(bundle.ProfileDefinitionId) ||
            bundle.Files is null || bundle.Files.Count == 0)
            throw new RepositorySetupIntegrityException("The repository template bundle is incomplete.");

        var orders = new HashSet<int>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canonicalFiles = bundle.Files
            .OrderBy(value => value.Order)
            .Select(value =>
            {
                if (value.Order <= 0 || !orders.Add(value.Order) ||
                    !IsSafeRelativeTemplatePath(value.RelativePath) || !paths.Add(value.RelativePath) ||
                    value.Utf8Content is null || value.Utf8Content.Contains('\r') ||
                    value.Utf8Content.StartsWith('\uFEFF') ||
                    !value.Utf8Content.EndsWith('\n') ||
                    !IsValidUnicode(value.Utf8Content) ||
                    ContainsUnknownToken(value.RelativePath) || ContainsUnknownToken(value.Utf8Content))
                    throw new RepositorySetupIntegrityException(
                        "The repository template bundle contains an unsafe or duplicate file definition.");
                return new
                {
                    value.Order,
                    value.RelativePath,
                    contentSha256 = RepositorySetupCanonicalJson.Sha256(value.Utf8Content),
                    utf8ByteLength = Encoding.UTF8.GetByteCount(value.Utf8Content)
                };
            })
            .ToArray();
        var canonical = RepositorySetupCanonicalJson.Serialize(new
        {
            bundle.SchemaVersion,
            bundle.ProfileDefinitionId,
            files = canonicalFiles
        });
        return RepositorySetupCanonicalJson.Sha256(canonical);
    }

    private static bool IsSafeRelativeTemplatePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() ||
            value.Contains('\\') || value.Contains(':') || value.Contains('\r') || value.Contains('\n') ||
            value.IndexOfAny(['*', '?', '<', '>', '|', '\0']) >= 0 ||
            value.Any(character => character < 0x20 || character > 0x7e) ||
            value.StartsWith('/') || value.EndsWith('/') || Path.IsPathRooted(value))
            return false;

        foreach (var segment in value.Split('/'))
        {
            if (segment is "" or "." or ".." || segment != segment.TrimEnd(' ', '.') ||
                string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase))
                return false;
            var baseName = segment.Split('.', 2)[0];
            if (!baseName.StartsWith("{{", StringComparison.Ordinal) &&
                WindowsReservedSegments.Contains(baseName))
                return false;
        }
        return true;
    }

    private static bool ContainsUnknownToken(string value)
    {
        foreach (Match match in TokenPattern.Matches(value))
        {
            if (!AllowedTokens.Contains(match.Value))
                return true;
        }
        var withoutKnownTokens = value;
        foreach (var token in AllowedTokens)
            withoutKnownTokens = withoutKnownTokens.Replace(token, string.Empty, StringComparison.Ordinal);
        return withoutKnownTokens.Contains("{{", StringComparison.Ordinal) ||
               withoutKnownTokens.Contains("}}", StringComparison.Ordinal);
    }

    private static bool IsValidUnicode(string value)
    {
        try
        {
            _ = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}

public static class RepositorySetupTemplateBundleRenderer
{
    public static UTF8Encoding Utf8NoBomStrict { get; } = new(false, true);

    public static RepositorySetupTemplateBundle Render(
        RepositorySetupTemplateBundle tokenizedBundle,
        RepositorySetupPlanPreview confirmedPlan)
    {
        ArgumentNullException.ThrowIfNull(tokenizedBundle);
        ArgumentNullException.ThrowIfNull(confirmedPlan);
        if (!string.Equals(
                tokenizedBundle.ProfileDefinitionId,
                confirmedPlan.Profile.ProfileDefinitionId,
                StringComparison.Ordinal) ||
            !string.Equals(
                RepositorySetupTemplateBundleCodec.ComputeHash(tokenizedBundle),
                confirmedPlan.TemplateBundleSha256,
                StringComparison.Ordinal))
            throw new RepositorySetupIntegrityException(
                "The template bundle does not match the confirmed repository setup plan.");

        ValidateIdentifier(confirmedPlan.SolutionName, nameof(confirmedPlan.SolutionName));
        ValidateIdentifier(confirmedPlan.AppProjectName, nameof(confirmedPlan.AppProjectName));
        ValidateIdentifier(confirmedPlan.TestProjectName, nameof(confirmedPlan.TestProjectName));
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{SOLUTION_NAME}}"] = confirmedPlan.SolutionName,
            ["{{APP_PROJECT_NAME}}"] = confirmedPlan.AppProjectName,
            ["{{APP_PROJECT_NAME_LOWER}}"] = confirmedPlan.AppProjectName.ToLowerInvariant(),
            ["{{TEST_PROJECT_NAME}}"] = confirmedPlan.TestProjectName
        };
        string Replace(string value)
        {
            foreach (var replacement in replacements)
                value = value.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
            if (value.Contains("{{", StringComparison.Ordinal) || value.Contains("}}", StringComparison.Ordinal))
                throw new RepositorySetupIntegrityException(
                    "The repository template bundle contains an unresolved token.");
            return value;
        }

        var rendered = new RepositorySetupTemplateBundle(
            tokenizedBundle.SchemaVersion,
            tokenizedBundle.ProfileDefinitionId,
            tokenizedBundle.Files
                .Select(value => new RepositorySetupTemplateFileDefinition(
                    value.Order,
                    Replace(value.RelativePath),
                    Replace(value.Utf8Content)))
                .ToArray());
        _ = RepositorySetupTemplateBundleCodec.ComputeHash(rendered);
        return rendered;
    }

    private static void ValidateIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 ||
            !(char.IsLetter(value[0]) || value[0] == '_') ||
            value.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '.')) ||
            value.Contains("..", StringComparison.Ordinal))
            throw new RepositorySetupIntegrityException(
                $"The confirmed {name} is not a safe server-derived template identifier.");
    }
}

public static class RepositorySetupPlanCodec
{
    public static string ComputeHash(RepositorySetupPlanPreview plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var canonical = RepositorySetupCanonicalJson.Serialize(new
        {
            plan.SchemaVersion,
            plan.Source,
            plan.ProjectId,
            plan.CanonicalProjectName,
            plan.WorkbenchSessionId,
            plan.LeaseEpoch,
            plan.BasedOnUnderstandingRevision,
            plan.BasedOnUnderstandingHash,
            plan.ProfileDescriptorRevision,
            plan.ProfileDescriptorSha256,
            plan.State,
            plan.ReasonCode,
            plan.Message,
            plan.Profile,
            plan.TargetPath,
            plan.SolutionName,
            plan.AppProjectName,
            plan.TestProjectName,
            plan.SolutionPath,
            plan.AppProjectPath,
            plan.TestProjectPath,
            plan.TemplateBundleSha256,
            plan.PlanningBundleSha256,
            plan.TargetFramework,
            plan.Language,
            plan.ApplicationKind,
            plan.TestFramework,
            plan.SdkVersion,
            plan.RuntimeVersion,
            plan.RestoreCommand,
            plan.BuildCommand,
            plan.TestCommand,
            plan.ToolchainManifestId,
            plan.ExecutionImageReference,
            plan.DefaultBranch,
            plan.InitializeGit,
            plan.IndexAfterProvisioning,
            plan.SandboxValidation,
            plan.ResourcePolicy
        });
        return RepositorySetupCanonicalJson.Sha256(canonical);
    }
}

public sealed class RepositorySetupValidationException(string message) : Exception(message);

public sealed class RepositorySetupUnsafePathException(string message) : Exception(message)
{
    public const string ErrorCode = "repository_setup_path_unsafe";
}

public sealed class RepositorySetupUnsupportedProfileException(string profileDefinitionId) : Exception(
    $"Repository setup profile '{profileDefinitionId}' is not in the pinned Workbench v0.1 catalog.")
{
    public const string ErrorCode = "repository_setup_profile_unsupported";
}

public sealed class RepositorySetupPlanNotConfirmableException(string message) : Exception(message)
{
    public const string ErrorCode = "repository_setup_plan_not_confirmable";
}

public sealed class RepositorySetupPlanChangedException : Exception
{
    public const string ErrorCode = "repository_setup_plan_changed";

    public RepositorySetupPlanChangedException()
        : base("The repository setup plan changed. Refresh and review the current plan before confirming.")
    {
    }
}

public sealed class RepositorySetupAlreadyBoundException : Exception
{
    public const string ErrorCode = "repository_setup_already_bound";

    public RepositorySetupAlreadyBoundException()
        : base("This project already has a repository binding.")
    {
    }
}

public sealed class RepositorySetupForbiddenException : Exception
{
    public const string ErrorCode = "repository_setup_forbidden";

    public RepositorySetupForbiddenException()
        : base("Only an active project Owner or Contributor may confirm repository setup.")
    {
    }
}

public sealed class RepositorySetupIntegrityException(string message) : Exception(message);
