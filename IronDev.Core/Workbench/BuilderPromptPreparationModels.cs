using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Core.Workbench;

public static class BuilderPromptPreparationOperationKinds
{
    public const string PrepareAgentRun = "PrepareBuilderAgentRun";
}

public static class BuilderAgentRunStates
{
    public const string Prepared = "Prepared";
}

public static class BuilderPromptPreparationReasonCodes
{
    public const string AuthorizationExpired = "AuthorizationExpired";
    public const string AuthorizationRevoked = "AuthorizationRevoked";
    public const string AuthorizationConsumed = "AuthorizationConsumed";
    public const string AuthorizationScopeMismatch = "AuthorizationScopeMismatch";
    public const string WorkPackageChanged = "WorkPackageChanged";
    public const string RepositoryBaselineChanged = "RepositoryBaselineChanged";
    public const string ReadinessChanged = "ReadinessChanged";
    public const string BuilderProfileChanged = "BuilderProfileChanged";
    public const string SandboxPolicyChanged = "SandboxPolicyChanged";
}

public sealed record PrepareBuilderAgentRunCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    Guid BuilderExecutionAuthorizationId,
    Guid BuilderWorkPackageCoreId,
    string ExpectedCoreSha256);

public sealed record PreparedBuilderAgentRun
{
    public required Guid BuilderAgentRunId { get; init; }
    public required int ProjectId { get; init; }
    public required Guid BuilderExecutionAuthorizationId { get; init; }
    public required Guid BuilderWorkPackageCoreId { get; init; }
    public required string BuilderWorkPackageCoreSha256 { get; init; }
    public required string Status { get; init; }
    public required string BuilderAgentVersion { get; init; }
    public required string PromptVersion { get; init; }
    public required string ToolPolicyVersion { get; init; }
    public required string ContextSchemaVersion { get; init; }
    public required string OutputSchemaVersion { get; init; }
    public required string EffectiveProfileSha256 { get; init; }
    public required string RoleContextSha256 { get; init; }
    public required string PromptSha256 { get; init; }
    public required string ToolManifestSha256 { get; init; }
    public required string ProviderInputSha256 { get; init; }
    public required DateTimeOffset PreparedAtUtc { get; init; }
    public required DateTimeOffset ProviderInvocationPermittedAtUtc { get; init; }
    public required Guid ClientOperationId { get; init; }
    public bool IsReplay { get; init; }
}

public sealed record BuilderToolContract(
    string Name,
    string Version,
    string Authority,
    bool MayReadRepository,
    bool MayWriteSandbox,
    bool MayExecuteProcess,
    bool MayUseNetwork,
    bool MayWriteActiveRepository);

public sealed record BuilderPreparedPromptMaterial
{
    public required string SystemPrompt { get; init; }
    public required string RoleContextJson { get; init; }
    public required string ToolManifestJson { get; init; }
    public required string ProviderInputJson { get; init; }
    public required string EffectiveProfileJson { get; init; }
    public required string EffectiveProfileSha256 { get; init; }
    public required string RoleContextSha256 { get; init; }
    public required string PromptSha256 { get; init; }
    public required string ToolManifestSha256 { get; init; }
    public required string ProviderInputSha256 { get; init; }
}

public interface IWorkbenchBuilderPromptPreparationService
{
    Task<PreparedBuilderAgentRun> PrepareAsync(
        PrepareBuilderAgentRunCommand command,
        CancellationToken cancellationToken = default);
}

public static class BuilderPromptContract
{
    private const int MaterializationVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private const string SystemPromptV1 =
        """
        You are the IronDev Builder. Implement only the exact immutable BuilderWorkPackage supplied as role context.
        The package is the complete source of task identity, acceptance criteria, repository baseline, evidence, file authority, model profile, sandbox authority, and output contract.
        Do not retrieve additional task authority, infer a broader scope, edit the active repository, edit ticket contracts, create authorization, rewrite tests to obtain green, approve a patch, or apply a patch.
        You may operate only through the declared Builder tools inside the qualified sandbox and only on permitted files.
        Return only output conforming to the pinned Builder output schema.
        """;

    private static readonly IReadOnlyList<BuilderToolContract> Tools =
    [
        new("builder.sandbox.files.read", "v1", "Read package-bound repository files inside the qualified sandbox.", true, false, false, false, false),
        new("builder.sandbox.files.propose", "v1", "Create proposed files only inside the qualified sandbox and permitted-file scope.", false, true, false, false, false),
        new("builder.sandbox.process.run", "v1", "Run only policy-approved restore, build, and test commands inside the qualified sandbox.", false, false, true, false, false)
    ];

    public static BuilderPreparedPromptMaterial Materialize(
        BuilderWorkPackage package,
        Guid builderAgentRunId,
        DateTimeOffset preparedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (builderAgentRunId == Guid.Empty || preparedAtUtc == default ||
            preparedAtUtc.Offset != TimeSpan.Zero || !package.SingleUse)
            throw new BuilderPromptPreparationValidationException(
                "An exact run, UTC preparation time, and single-use Builder package are required.");

        var core = BuilderWorkPackageCoreCodec.NormalizeAndValidate(package.Core);
        var coreHash = BuilderWorkPackageCoreCodec.ComputeHash(core);
        if (!string.Equals(coreHash, NormalizeHash(package.CoreSha256), StringComparison.Ordinal))
            throw new BuilderPromptPreparationIntegrityException(
                "The Builder package core hash does not match its canonical content.");
        if (package.SingleUseAuthorizationId == Guid.Empty ||
            package.AuthorizedAtUtc == default || package.AuthorizedAtUtc.Offset != TimeSpan.Zero ||
            package.ExpiresAtUtc == default || package.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            package.ExpiresAtUtc <= preparedAtUtc)
            throw new BuilderPromptPreparationValidationException(
                "The Builder package authorization is missing or expired.");

        var roleContextJson = JsonSerializer.Serialize(new
        {
            materializationVersion = MaterializationVersion,
            builderAgentRunId,
            package
        }, JsonOptions);
        var effectiveProfileJson = JsonSerializer.Serialize(core.EffectiveProfile, JsonOptions);
        var toolManifestJson = JsonSerializer.Serialize(new
        {
            toolPolicyVersion = BuilderRoleContract.ToolPolicyVersion,
            tools = Tools
        }, JsonOptions);
        var roleContextSha256 = Sha256(roleContextJson);
        var toolManifestSha256 = Sha256(toolManifestJson);
        var providerInputJson = JsonSerializer.Serialize(new
        {
            materializationVersion = MaterializationVersion,
            builderAgentVersion = BuilderRoleContract.BuilderAgentVersion,
            promptVersion = BuilderRoleContract.PromptVersion,
            contextSchemaVersion = BuilderRoleContract.ContextSchemaVersion,
            outputSchemaVersion = BuilderRoleContract.OutputSchemaVersion,
            systemPrompt = SystemPromptV1,
            roleContextSha256,
            toolManifestSha256
        }, JsonOptions);

        return new BuilderPreparedPromptMaterial
        {
            SystemPrompt = SystemPromptV1,
            RoleContextJson = roleContextJson,
            ToolManifestJson = toolManifestJson,
            ProviderInputJson = providerInputJson,
            EffectiveProfileJson = effectiveProfileJson,
            EffectiveProfileSha256 = Sha256(effectiveProfileJson),
            RoleContextSha256 = roleContextSha256,
            PromptSha256 = Sha256(SystemPromptV1),
            ToolManifestSha256 = toolManifestSha256,
            ProviderInputSha256 = Sha256(providerInputJson)
        };
    }

    private static string NormalizeHash(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.StartsWith("sha256:", StringComparison.Ordinal))
            normalized = normalized[7..];
        if (normalized.Length != 64 || normalized.Any(static value => !Uri.IsHexDigit(value)))
            throw new BuilderPromptPreparationValidationException("ExpectedCoreSha256 must be a SHA-256 value.");
        return normalized;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class BuilderPromptPreparationValidationException(string message) : Exception(message)
{
    public const string ErrorCode = "builder_prompt_preparation_invalid";
}

public sealed class BuilderPromptPreparationConflictException(string reasonCode, string message)
    : Exception(message)
{
    public const string ErrorCode = "builder_prompt_preparation_refused";
    public string ReasonCode { get; } = reasonCode;
}

public sealed class BuilderPromptPreparationOperationMismatchException()
    : Exception("The client operation ID was already used with a different Builder preparation payload.")
{
    public const string ErrorCode = "operation_id_payload_mismatch";
}

public sealed class BuilderPromptPreparationIntegrityException(string message) : Exception(message)
{
    public const string ErrorCode = "builder_prompt_preparation_integrity_failed";
}
