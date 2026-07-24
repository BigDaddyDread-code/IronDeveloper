using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Workbench;

/// <summary>
/// Code-owned Builder role contract. These versions are authority, not client choices.
/// A semantic change requires a new pinned version.
/// </summary>
public static class BuilderRoleContract
{
    public const string BuilderAgentVersion = "irondev-builder-v1";
    public const string PromptVersion = "irondev-builder-prompt-v1";
    public const string ToolPolicyVersion = "irondev-builder-tools-v1";
    public const string ContextSchemaVersion = "irondev-builder-context-v1";
    public const string OutputSchemaVersion = "irondev-builder-output-v1";
}

public static class BuilderWorkPackageCoreContract
{
    public const int CanonicalizationVersion1 = 1;
    public const int MaximumTickets = 100;
    public const int MaximumGoverningArtifacts = 100;
    public const int MaximumPermittedFilesPerTicket = 1_000;
    public const int MaximumCodeIndexSources = 4_096;
    public const int MaximumTextLength = 1_048_576;
    public const int MaximumRelativePathLength = 1_000;
    public const int MaximumIdentifierLength = 200;
    public const int MaximumBranchNameLength = 250;
    public const int MaximumArtifactKindLength = 50;
}

public static class BuilderWorkPackageGoverningArtifactKinds
{
    public const string ProjectUnderstanding = "ProjectUnderstanding";
}

/// <summary>
/// One exact Work Item/ticket contract. The Builder receives the acceptance criteria and
/// permitted files here and has no authority to retrieve or widen them.
/// </summary>
public sealed record BuilderWorkPackageTicketReference
{
    public required int Ordinal { get; init; }
    public required long WorkItemId { get; init; }
    public required long WorkItemVersion { get; init; }
    public required long WorkItemContractId { get; init; }
    public required int WorkItemContractRevision { get; init; }
    public required string WorkItemContractSha256 { get; init; }
    public required long TicketId { get; init; }
    public required long TicketRevision { get; init; }
    public required string AcceptanceCriteria { get; init; }
    public required IReadOnlyList<string> PermittedFiles { get; init; }
}

public sealed record BuilderWorkPackageArtifactReference(
    int Ordinal,
    string ArtifactKind,
    long ArtifactReferenceId,
    long Revision);

public sealed record BuilderRepositoryObservationSnapshot
{
    public required Guid Id { get; init; }
    public required string EvidenceSha256 { get; init; }
    public required string HeadCommit { get; init; }
    public required string GitTreeId { get; init; }
    public required string WorktreeState { get; init; }
    public required string WorktreeFingerprint { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record BuilderCodeIndexSourceSnapshot(
    int Ordinal,
    string RelativePath,
    string ContentSha256);

public sealed record BuilderCodeIndexSnapshot
{
    public required Guid Id { get; init; }
    public required long Revision { get; init; }
    public required string EvidenceSha256 { get; init; }
    public required int SchemaVersion { get; init; }
    public required string IndexerVersion { get; init; }
    public required string IndexedContentSha256 { get; init; }
    public required IReadOnlyList<BuilderCodeIndexSourceSnapshot> Sources { get; init; }
    public required DateTimeOffset IndexedAtUtc { get; init; }
}

public sealed record BuilderReadinessAssessmentSnapshot
{
    public required long Id { get; init; }
    public required long Revision { get; init; }
    public required Guid TechnicalEvidenceId { get; init; }
    public required string EvidenceSha256 { get; init; }
    public required DateTimeOffset AssessedAtUtc { get; init; }
}

public sealed record BuilderEffectiveProfileSnapshot
{
    public required Guid ProjectExecutionProfileId { get; init; }
    public required long ProjectExecutionProfileRevision { get; init; }
    public required string ProfileDefinitionId { get; init; }
    public required int ProfileDescriptorRevision { get; init; }
    public required string ProfileDescriptorSha256 { get; init; }
    public required Guid BuilderConfigurationId { get; init; }
    public required long BuilderConfigurationRevision { get; init; }
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required string BuilderConfigurationSha256 { get; init; }
}

public sealed record BuilderSandboxAuthoritySnapshot
{
    public required Guid QualificationAttemptId { get; init; }
    public required Guid EvidenceManifestId { get; init; }
    public required string EvidenceManifestSha256 { get; init; }
    public required string PolicyVersion { get; init; }
    public required string PolicySha256 { get; init; }
    public required string QualifiedImageDigest { get; init; }
    public required string ToolchainManifestId { get; init; }
    public required string ToolchainManifestSha256 { get; init; }
    public required string OfflineFeedManifestSha256 { get; init; }
    public required string TemplateBundleSha256 { get; init; }
}

/// <summary>
/// Complete authorization-free input for one exact Builder execution. It contains all role
/// context and authority bounds. Actor, lease, expiry, revocation, and consumption state live
/// in the single-use authorization envelope and cannot change this core hash.
/// </summary>
public sealed record BuilderWorkPackageCore
{
    public required Guid Id { get; init; }
    public required int CanonicalizationVersion { get; init; }
    public required int TenantId { get; init; }
    public required int ProjectId { get; init; }
    public required IReadOnlyList<BuilderWorkPackageTicketReference> Tickets { get; init; }
    public required IReadOnlyList<BuilderWorkPackageArtifactReference> GoverningArtifacts { get; init; }
    public required Guid RepositoryBindingId { get; init; }
    public required long RepositoryBindingRevision { get; init; }
    public required string BranchName { get; init; }
    public required string BaselineCommit { get; init; }
    public required BuilderReadinessAssessmentSnapshot ReadinessAssessment { get; init; }
    public required BuilderRepositoryObservationSnapshot RepositoryObservation { get; init; }
    public required BuilderCodeIndexSnapshot CodeIndex { get; init; }
    public required string RestoreCommandSha256 { get; init; }
    public required string BuildCommandSha256 { get; init; }
    public required string TestCommandSha256 { get; init; }
    public required string BuilderAgentVersion { get; init; }
    public required string PromptVersion { get; init; }
    public required string ToolPolicyVersion { get; init; }
    public required string ContextSchemaVersion { get; init; }
    public required string OutputSchemaVersion { get; init; }
    public required BuilderEffectiveProfileSnapshot EffectiveProfile { get; init; }
    public required BuilderSandboxAuthoritySnapshot Sandbox { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>
/// The package PR-07B will consume. Authorization identity and expiry are frozen beside the
/// immutable core without becoming inputs to its content hash.
/// </summary>
public sealed record BuilderWorkPackage
{
    public required BuilderWorkPackageCore Core { get; init; }
    public required string CoreSha256 { get; init; }
    public required Guid SingleUseAuthorizationId { get; init; }
    public required DateTimeOffset AuthorizedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public required bool SingleUse { get; init; }
}

public static class BuilderWorkPackageCoreCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string SerializeCanonical(BuilderWorkPackageCore core)
    {
        var value = NormalizeAndValidate(core);
        return JsonSerializer.Serialize(new
        {
            value.Id,
            value.CanonicalizationVersion,
            value.TenantId,
            value.ProjectId,
            Tickets = value.Tickets.Select(static ticket => new
            {
                ticket.Ordinal,
                ticket.WorkItemId,
                ticket.WorkItemVersion,
                ticket.WorkItemContractId,
                ticket.WorkItemContractRevision,
                ticket.WorkItemContractSha256,
                ticket.TicketId,
                ticket.TicketRevision,
                ticket.AcceptanceCriteria,
                ticket.PermittedFiles
            }),
            GoverningArtifacts = value.GoverningArtifacts.Select(static artifact => new
            {
                artifact.Ordinal,
                artifact.ArtifactKind,
                artifact.ArtifactReferenceId,
                artifact.Revision
            }),
            value.RepositoryBindingId,
            value.RepositoryBindingRevision,
            value.BranchName,
            value.BaselineCommit,
            value.ReadinessAssessment,
            value.RepositoryObservation,
            value.CodeIndex,
            value.RestoreCommandSha256,
            value.BuildCommandSha256,
            value.TestCommandSha256,
            value.BuilderAgentVersion,
            value.PromptVersion,
            value.ToolPolicyVersion,
            value.ContextSchemaVersion,
            value.OutputSchemaVersion,
            value.EffectiveProfile,
            value.Sandbox,
            value.CreatedAtUtc
        }, JsonOptions);
    }

    public static string ComputeHash(BuilderWorkPackageCore core) =>
        Sha256(SerializeCanonical(core));

    public static BuilderWorkPackageCore DeserializeCanonical(string canonicalJson)
    {
        if (string.IsNullOrWhiteSpace(canonicalJson))
            throw Invalid("The canonical Builder work-package core JSON is empty.");
        BuilderWorkPackageCore core;
        try
        {
            core = JsonSerializer.Deserialize<BuilderWorkPackageCore>(canonicalJson, JsonOptions)
                ?? throw Invalid("The canonical Builder work-package core JSON could not be read.");
        }
        catch (JsonException)
        {
            throw Invalid("The canonical Builder work-package core JSON is invalid.");
        }
        var normalized = NormalizeAndValidate(core);
        if (!string.Equals(canonicalJson, SerializeCanonical(normalized), StringComparison.Ordinal))
            throw Invalid("The Builder work-package core JSON is not canonical v1.");
        return normalized;
    }

    public static BuilderWorkPackageCore NormalizeAndValidate(BuilderWorkPackageCore core)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (core.CanonicalizationVersion != BuilderWorkPackageCoreContract.CanonicalizationVersion1)
            throw Invalid($"Unsupported Builder work-package canonicalization version '{core.CanonicalizationVersion}'.");
        if (core.Id == Guid.Empty || core.TenantId <= 0 || core.ProjectId <= 0 ||
            core.RepositoryBindingId == Guid.Empty || core.RepositoryBindingRevision <= 0)
            throw Invalid("Builder work-package tenant, project, package, and repository identities are required.");

        return core with
        {
            Tickets = NormalizeTickets(core.Tickets),
            GoverningArtifacts = NormalizeArtifacts(core.GoverningArtifacts),
            BranchName = NormalizeBranchName(core.BranchName),
            BaselineCommit = GitObject(core.BaselineCommit, nameof(core.BaselineCommit)),
            ReadinessAssessment = NormalizeReadiness(core.ReadinessAssessment),
            RepositoryObservation = NormalizeObservation(core.RepositoryObservation),
            CodeIndex = NormalizeCodeIndex(core.CodeIndex),
            RestoreCommandSha256 = Hash(core.RestoreCommandSha256, nameof(core.RestoreCommandSha256)),
            BuildCommandSha256 = Hash(core.BuildCommandSha256, nameof(core.BuildCommandSha256)),
            TestCommandSha256 = Hash(core.TestCommandSha256, nameof(core.TestCommandSha256)),
            BuilderAgentVersion = ExactVersion(core.BuilderAgentVersion, BuilderRoleContract.BuilderAgentVersion, nameof(core.BuilderAgentVersion)),
            PromptVersion = ExactVersion(core.PromptVersion, BuilderRoleContract.PromptVersion, nameof(core.PromptVersion)),
            ToolPolicyVersion = ExactVersion(core.ToolPolicyVersion, BuilderRoleContract.ToolPolicyVersion, nameof(core.ToolPolicyVersion)),
            ContextSchemaVersion = ExactVersion(core.ContextSchemaVersion, BuilderRoleContract.ContextSchemaVersion, nameof(core.ContextSchemaVersion)),
            OutputSchemaVersion = ExactVersion(core.OutputSchemaVersion, BuilderRoleContract.OutputSchemaVersion, nameof(core.OutputSchemaVersion)),
            EffectiveProfile = NormalizeProfile(core.EffectiveProfile),
            Sandbox = NormalizeSandbox(core.Sandbox),
            CreatedAtUtc = Utc(core.CreatedAtUtc, nameof(core.CreatedAtUtc))
        };
    }

    private static IReadOnlyList<BuilderWorkPackageTicketReference> NormalizeTickets(
        IReadOnlyList<BuilderWorkPackageTicketReference> tickets)
    {
        if (tickets is null || tickets.Count is 0 or > BuilderWorkPackageCoreContract.MaximumTickets)
            throw Invalid($"Builder work packages require between 1 and {BuilderWorkPackageCoreContract.MaximumTickets} tickets.");
        var normalized = new BuilderWorkPackageTicketReference[tickets.Count];
        var ticketIds = new HashSet<long>();
        var workItemIds = new HashSet<long>();
        for (var index = 0; index < tickets.Count; index++)
        {
            var ticket = tickets[index] ?? throw Invalid("Builder ticket references cannot be null.");
            if (ticket.Ordinal != index + 1 || ticket.WorkItemId <= 0 || ticket.WorkItemVersion <= 0 ||
                ticket.WorkItemContractId <= 0 || ticket.WorkItemContractRevision <= 0 ||
                ticket.TicketId <= 0 || ticket.TicketRevision <= 0)
                throw Invalid("Builder ticket and Work Item identities, revisions, and contiguous ordinals are required.");
            if (!ticketIds.Add(ticket.TicketId) || !workItemIds.Add(ticket.WorkItemId))
                throw Invalid("Builder packages cannot contain duplicate ticket or Work Item identities.");
            var criteria = Text(ticket.AcceptanceCriteria, nameof(ticket.AcceptanceCriteria));
            var files = NormalizePaths(ticket.PermittedFiles, requireNonEmpty: true);
            normalized[index] = ticket with
            {
                WorkItemContractSha256 = Hash(ticket.WorkItemContractSha256, nameof(ticket.WorkItemContractSha256)),
                AcceptanceCriteria = criteria,
                PermittedFiles = files
            };
        }
        return normalized;
    }

    private static IReadOnlyList<BuilderWorkPackageArtifactReference> NormalizeArtifacts(
        IReadOnlyList<BuilderWorkPackageArtifactReference> artifacts)
    {
        if (artifacts is null || artifacts.Count is 0 or > BuilderWorkPackageCoreContract.MaximumGoverningArtifacts)
            throw Invalid("Builder work packages require governing artifacts.");
        var normalized = new BuilderWorkPackageArtifactReference[artifacts.Count];
        var identities = new HashSet<(string, long)>();
        for (var index = 0; index < artifacts.Count; index++)
        {
            var artifact = artifacts[index] ?? throw Invalid("Governing artifact references cannot be null.");
            var kind = Identifier(artifact.ArtifactKind, nameof(artifact.ArtifactKind), BuilderWorkPackageCoreContract.MaximumArtifactKindLength);
            if (artifact.Ordinal != index + 1 || artifact.ArtifactReferenceId <= 0 || artifact.Revision <= 0 ||
                kind != BuilderWorkPackageGoverningArtifactKinds.ProjectUnderstanding ||
                !identities.Add((kind, artifact.ArtifactReferenceId)))
                throw Invalid("Governing artifact identities, revisions, and contiguous ordinals must be exact.");
            normalized[index] = artifact with { ArtifactKind = kind };
        }
        return normalized;
    }

    private static BuilderReadinessAssessmentSnapshot NormalizeReadiness(BuilderReadinessAssessmentSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Id <= 0 || value.Revision <= 0 || value.TechnicalEvidenceId == Guid.Empty)
            throw Invalid("Readiness assessment identity, revision, and technical evidence identity are required.");
        return value with
        {
            EvidenceSha256 = Hash(value.EvidenceSha256, nameof(value.EvidenceSha256)),
            AssessedAtUtc = Utc(value.AssessedAtUtc, nameof(value.AssessedAtUtc))
        };
    }

    private static BuilderRepositoryObservationSnapshot NormalizeObservation(BuilderRepositoryObservationSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Id == Guid.Empty)
            throw Invalid("Repository observation identity is required.");
        return value with
        {
            EvidenceSha256 = Hash(value.EvidenceSha256, nameof(value.EvidenceSha256)),
            HeadCommit = GitObject(value.HeadCommit, nameof(value.HeadCommit)),
            GitTreeId = GitObject(value.GitTreeId, nameof(value.GitTreeId)),
            WorktreeState = ExactVersion(
                value.WorktreeState,
                RepositoryWorktreeStates.Clean,
                nameof(value.WorktreeState)),
            WorktreeFingerprint = Hash(value.WorktreeFingerprint, nameof(value.WorktreeFingerprint)),
            ObservedAtUtc = Utc(value.ObservedAtUtc, nameof(value.ObservedAtUtc))
        };
    }

    private static BuilderCodeIndexSnapshot NormalizeCodeIndex(BuilderCodeIndexSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Id == Guid.Empty || value.Revision <= 0 || value.SchemaVersion <= 0)
            throw Invalid("Code-index identity, revision, and schema version are required.");
        var sources = value.Sources;
        if (sources is null || sources.Count > BuilderWorkPackageCoreContract.MaximumCodeIndexSources)
            throw Invalid("Code-index source count exceeds the Builder contract.");
        var normalized = new BuilderCodeIndexSourceSnapshot[sources.Count];
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index] ?? throw Invalid("Code-index sources cannot be null.");
            if (source.Ordinal != index + 1)
                throw Invalid("Code-index ordinals must be contiguous and match array order.");
            normalized[index] = source with
            {
                RelativePath = RelativePath(source.RelativePath),
                ContentSha256 = Hash(source.ContentSha256, nameof(source.ContentSha256))
            };
        }
        return value with
        {
            EvidenceSha256 = Hash(value.EvidenceSha256, nameof(value.EvidenceSha256)),
            IndexerVersion = Identifier(value.IndexerVersion, nameof(value.IndexerVersion)),
            IndexedContentSha256 = Hash(value.IndexedContentSha256, nameof(value.IndexedContentSha256)),
            Sources = normalized,
            IndexedAtUtc = Utc(value.IndexedAtUtc, nameof(value.IndexedAtUtc))
        };
    }

    private static BuilderEffectiveProfileSnapshot NormalizeProfile(BuilderEffectiveProfileSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ProjectExecutionProfileId == Guid.Empty || value.ProjectExecutionProfileRevision <= 0 ||
            value.ProfileDescriptorRevision <= 0 || value.BuilderConfigurationId == Guid.Empty ||
            value.BuilderConfigurationRevision <= 0)
            throw Invalid("Effective execution and Builder model profile identities and revisions are required.");
        return value with
        {
            ProfileDefinitionId = Identifier(value.ProfileDefinitionId, nameof(value.ProfileDefinitionId)),
            ProfileDescriptorSha256 = Hash(value.ProfileDescriptorSha256, nameof(value.ProfileDescriptorSha256)),
            ProviderId = Identifier(value.ProviderId, nameof(value.ProviderId)),
            ModelId = Identifier(value.ModelId, nameof(value.ModelId)),
            BuilderConfigurationSha256 = Hash(value.BuilderConfigurationSha256, nameof(value.BuilderConfigurationSha256))
        };
    }

    private static BuilderSandboxAuthoritySnapshot NormalizeSandbox(BuilderSandboxAuthoritySnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.QualificationAttemptId == Guid.Empty || value.EvidenceManifestId == Guid.Empty)
            throw Invalid("Sandbox qualification and evidence manifest identities are required.");
        return value with
        {
            EvidenceManifestSha256 = Hash(value.EvidenceManifestSha256, nameof(value.EvidenceManifestSha256)),
            PolicyVersion = Identifier(value.PolicyVersion, nameof(value.PolicyVersion)),
            PolicySha256 = Hash(value.PolicySha256, nameof(value.PolicySha256)),
            QualifiedImageDigest = Hash(value.QualifiedImageDigest, nameof(value.QualifiedImageDigest)),
            ToolchainManifestId = Identifier(value.ToolchainManifestId, nameof(value.ToolchainManifestId)),
            ToolchainManifestSha256 = Hash(value.ToolchainManifestSha256, nameof(value.ToolchainManifestSha256)),
            OfflineFeedManifestSha256 = Hash(value.OfflineFeedManifestSha256, nameof(value.OfflineFeedManifestSha256)),
            TemplateBundleSha256 = Hash(value.TemplateBundleSha256, nameof(value.TemplateBundleSha256))
        };
    }

    private static IReadOnlyList<string> NormalizePaths(IReadOnlyList<string> paths, bool requireNonEmpty)
    {
        if (paths is null || (requireNonEmpty && paths.Count == 0) ||
            paths.Count > BuilderWorkPackageCoreContract.MaximumPermittedFilesPerTicket)
            throw Invalid("A bounded non-empty permitted-file scope is required.");
        var normalized = paths.Select(RelativePath).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw Invalid("Permitted-file paths must be unique using ordinal comparison.");
        return normalized;
    }

    private static string RelativePath(string value)
    {
        var path = Required(value, nameof(value), BuilderWorkPackageCoreContract.MaximumRelativePathLength);
        if (Path.IsPathRooted(path) || path.Contains('\\') ||
            path.Split('/').Any(static segment => segment is "" or "." or ".."))
            throw Invalid("Builder file scope and code-index paths must be normalized repository-relative paths.");
        return path;
    }

    private static string NormalizeBranchName(string value)
    {
        var branch = Required(value, nameof(BuilderWorkPackageCore.BranchName), BuilderWorkPackageCoreContract.MaximumBranchNameLength);
        if (branch is "@" || branch.StartsWith('/') || branch.EndsWith('/') || branch.EndsWith('.') ||
            branch.Contains("//", StringComparison.Ordinal) || branch.Contains("..", StringComparison.Ordinal) ||
            branch.Contains("@{", StringComparison.Ordinal) ||
            branch.Any(static c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\') ||
            branch.Split('/').Any(static component => component.Length == 0 || component.StartsWith('.') || component.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
            throw Invalid("BranchName must be a normalized Git branch name.");
        return branch;
    }

    private static string ExactVersion(string value, string expected, string fieldName)
    {
        var normalized = Identifier(value, fieldName);
        if (!string.Equals(normalized, expected, StringComparison.Ordinal))
            throw Invalid($"{fieldName} is not the code-owned Builder contract version.");
        return normalized;
    }

    private static string Text(string value, string fieldName) =>
        Required(value, fieldName, BuilderWorkPackageCoreContract.MaximumTextLength);

    private static string Identifier(string value, string fieldName, int maximumLength = BuilderWorkPackageCoreContract.MaximumIdentifierLength)
    {
        var normalized = Required(value, fieldName, maximumLength);
        if (normalized.Any(static c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_' and not '/' and not ':'))
            throw Invalid($"{fieldName} must be a bounded sanitized identifier.");
        return normalized;
    }

    private static string GitObject(string value, string fieldName)
    {
        var normalized = Required(value, fieldName, 40).ToLowerInvariant();
        if (normalized.Length != 40 || normalized.Any(static c => !IsLowerHex(c)))
            throw Invalid($"{fieldName} must be a 40-character hexadecimal Git object ID.");
        return normalized;
    }

    private static string Hash(string value, string fieldName)
    {
        var normalized = Required(value, fieldName, 71).ToLowerInvariant();
        if (normalized.StartsWith("sha256:", StringComparison.Ordinal))
            normalized = normalized[7..];
        if (normalized.Length != 64 || normalized.Any(static c => !IsLowerHex(c)))
            throw Invalid($"{fieldName} must be a SHA-256 value.");
        return normalized;
    }

    private static DateTimeOffset Utc(DateTimeOffset value, string fieldName)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
            throw Invalid($"{fieldName} must be a non-default UTC timestamp.");
        return value;
    }

    private static string Required(string value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximumLength)
            throw Invalid($"{fieldName} is required, bounded, and must not contain surrounding whitespace.");
        return value;
    }

    private static bool IsLowerHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';
    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static BuilderWorkPackageCoreValidationException Invalid(string message) => new(message);
}

public sealed class BuilderWorkPackageCoreValidationException(string message) : Exception(message);
