using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Agents;
using IronDev.Core.Sandbox;

namespace IronDev.Core.Workbench;

public static class BuilderExecutionOperationKinds
{
    public const string Execute = "ExecuteBuilderAgentRun";
}

public static class BuilderAgentRunTerminalStates
{
    public const string Invoking = "Invoking";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public static class BuilderExecutionFailureCodes
{
    public const string PreparedInputChanged = "PreparedInputChanged";
    public const string RepositoryBaselineChanged = "RepositoryBaselineChanged";
    public const string BuilderProfileChanged = "BuilderProfileChanged";
    public const string SandboxPolicyChanged = "SandboxPolicyChanged";
    public const string AlreadyInvoked = "AlreadyInvoked";
    public const string ProviderFailed = "ProviderFailed";
    public const string OutputInvalid = "OutputInvalid";
    public const string ScopeViolation = "ScopeViolation";
    public const string TestRewriteRejected = "TestRewriteRejected";
    public const string SandboxFailed = "SandboxFailed";
}

public static class BuilderExecutionContract
{
    public const int MaximumAttempts = 3;
    public const int MaximumFiles = 1_000;
    public const int MaximumFileUtf8Bytes = 2 * 1024 * 1024;
    public const int MaximumOutputUtf8Bytes = 12 * 1024 * 1024;
}

public sealed record ExecuteBuilderAgentRunCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    Guid BuilderAgentRunId,
    string ExpectedProviderInputSha256);

public sealed record BuilderProposedFile(string RelativePath, string Content, string ContentSha256);
public sealed record BuilderChangedFile(string RelativePath, string ContentSha256, long Utf8ByteLength);

public sealed record BuilderToolCallEvidence(
    int Ordinal,
    string ToolName,
    string ToolVersion,
    string InputSha256,
    string OutputSha256,
    string Status);

public sealed record BuilderExecutionResult
{
    public required Guid BuilderAgentRunId { get; init; }
    public required string Status { get; init; }
    public required int AttemptCount { get; init; }
    public required IReadOnlyList<BuilderProposedFile> ProposedFiles { get; init; }
    public required IReadOnlyList<BuilderChangedFile> ChangedFiles { get; init; }
    public required string RawPatch { get; init; }
    public required string RawPatchSha256 { get; init; }
    public required IReadOnlyList<BuilderToolCallEvidence> ToolCalls { get; init; }
    public string? SandboxEvidenceManifestSha256 { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureEvidence { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public bool IsReplay { get; init; }
}

public sealed record BuilderProviderEnvelope(
    int EnvelopeVersion,
    string SafeRequestId,
    string SystemPrompt,
    string RoleContextJson,
    string ToolManifestJson,
    string OutputSchemaVersion,
    int AttemptNumber,
    string? RepairEvidence);

public sealed record BuilderProviderResponse(
    string Output,
    string SafeRequestId,
    string? ProviderRequestId,
    AgentModelUsage Usage,
    bool UsageReported,
    long DurationMilliseconds);

public interface IWorkbenchBuilderRoleAwareLlmService
{
    Task<BuilderProviderResponse> GetBuilderResponseAsync(
        BuilderProviderEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public sealed record BuilderProviderMessage(AgentModelRole Role, string Content);

public static class BuilderProviderMessageMapper
{
    public static IReadOnlyList<BuilderProviderMessage> Map(BuilderProviderEnvelope envelope)
    {
        if (envelope.EnvelopeVersion != 1 || string.IsNullOrWhiteSpace(envelope.SafeRequestId) ||
            string.IsNullOrWhiteSpace(envelope.SystemPrompt) ||
            envelope.OutputSchemaVersion != BuilderRoleContract.OutputSchemaVersion ||
            envelope.AttemptNumber is < 1 or > BuilderExecutionContract.MaximumAttempts)
            throw new BuilderExecutionValidationException("The Builder provider envelope is incomplete.");
        var input = JsonSerializer.Serialize(new
        {
            exactRoleContext = JsonSerializer.Deserialize<JsonElement>(envelope.RoleContextJson),
            declaredTools = JsonSerializer.Deserialize<JsonElement>(envelope.ToolManifestJson),
            envelope.OutputSchemaVersion,
            envelope.AttemptNumber,
            repairEvidence = envelope.RepairEvidence,
            requiredOutput = new
            {
                schemaVersion = BuilderRoleContract.OutputSchemaVersion,
                proposedFiles = new[] { new { relativePath = "path/from/permitted/files", content = "complete file content" } }
            }
        });
        return
        [
            new BuilderProviderMessage(AgentModelRole.System, envelope.SystemPrompt),
            new BuilderProviderMessage(AgentModelRole.User, input)
        ];
    }
}

public sealed record BuilderPreparedExecutionInput(
    Guid BuilderAgentRunId,
    int TenantId,
    int ProjectId,
    string EffectiveProfileJson,
    string EffectiveProfileSha256,
    string SystemPrompt,
    string RoleContextJson,
    string ToolManifestJson,
    string ProviderInputSha256,
    BuilderWorkPackageCore WorkPackageCore);

public interface IWorkbenchBuilderModelGateway
{
    Task<BuilderProviderResponse> InvokeAsync(
        BuilderPreparedExecutionInput input,
        int attemptNumber,
        string? repairEvidence,
        CancellationToken cancellationToken = default);
}

public sealed record BuilderSandboxValidationRequest(
    Guid ExecutionId,
    BuilderWorkPackageCore WorkPackageCore,
    IReadOnlyList<BuilderProposedFile> ProposedFiles);

public interface IWorkbenchBuilderSandboxRunner
{
    Task<SandboxExecutionResult> ValidateAsync(
        BuilderSandboxValidationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchBuilderExecutionService
{
    Task<BuilderExecutionResult> ExecuteAsync(
        ExecuteBuilderAgentRunCommand command,
        CancellationToken cancellationToken = default);
}

public static class BuilderOutputValidator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public static IReadOnlyList<BuilderProposedFile> Validate(
        string output,
        BuilderWorkPackageCore core)
    {
        if (string.IsNullOrWhiteSpace(output) ||
            Encoding.UTF8.GetByteCount(output) > BuilderExecutionContract.MaximumOutputUtf8Bytes)
            throw new BuilderOutputValidationException(
                BuilderExecutionFailureCodes.OutputInvalid, "Builder output is empty or exceeds its fixed bound.");
        BuilderOutputDocument document;
        try
        {
            document = JsonSerializer.Deserialize<BuilderOutputDocument>(output, Options)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new BuilderOutputValidationException(
                BuilderExecutionFailureCodes.OutputInvalid, "Builder output is not strict output-schema JSON.");
        }
        if (document.SchemaVersion != BuilderRoleContract.OutputSchemaVersion ||
            document.ProposedFiles is null ||
            document.ProposedFiles.Count is < 1 or > BuilderExecutionContract.MaximumFiles)
            throw new BuilderOutputValidationException(
                BuilderExecutionFailureCodes.OutputInvalid, "Builder output has the wrong schema or file count.");

        var permitted = core.Tickets.SelectMany(ticket => ticket.PermittedFiles)
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BuilderProposedFile>(document.ProposedFiles.Count);
        foreach (var file in document.ProposedFiles)
        {
            var path = NormalizePath(file.RelativePath);
            if (!seen.Add(path) || !permitted.Contains(path))
                throw new BuilderOutputValidationException(
                    BuilderExecutionFailureCodes.ScopeViolation, "Builder proposed a duplicate or unauthorized file.");
            if (IsTestPath(path))
                throw new BuilderOutputValidationException(
                    BuilderExecutionFailureCodes.TestRewriteRejected, "Builder may not author or rewrite test files.");
            var content = file.Content ?? throw new BuilderOutputValidationException(
                BuilderExecutionFailureCodes.OutputInvalid, "Builder proposed a file without content.");
            if (Encoding.UTF8.GetByteCount(content) > BuilderExecutionContract.MaximumFileUtf8Bytes)
                throw new BuilderOutputValidationException(
                    BuilderExecutionFailureCodes.OutputInvalid, "A proposed file exceeds the fixed size bound.");
            result.Add(new BuilderProposedFile(path, content, Sha256(content)));
        }
        return result.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static string NormalizePath(string value)
    {
        var path = value?.Trim().Replace('\\', '/') ?? string.Empty;
        if (path.Length is 0 or > BuilderWorkPackageCoreContract.MaximumRelativePathLength ||
            Path.IsPathRooted(path) || path.Contains(':') ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new BuilderOutputValidationException(
                BuilderExecutionFailureCodes.ScopeViolation, "Builder proposed an unsafe repository-relative path.");
        return path;
    }

    private static bool IsTestPath(string path) =>
        path.Split('/').Any(segment =>
            segment.Equals("test", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
            segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)) ||
        Path.GetFileNameWithoutExtension(path).EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileNameWithoutExtension(path).EndsWith("Test", StringComparison.OrdinalIgnoreCase);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record BuilderOutputDocument
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public IReadOnlyList<BuilderOutputFile>? ProposedFiles { get; init; }
    }

    private sealed record BuilderOutputFile
    {
        public string RelativePath { get; init; } = string.Empty;
        public string? Content { get; init; }
    }
}

public sealed class BuilderOutputValidationException(string failureCode, string message) : Exception(message)
{
    public string FailureCode { get; } = failureCode;
}

public sealed class BuilderExecutionValidationException(string message) : Exception(message)
{
    public const string ErrorCode = "builder_execution_invalid";
}

public sealed class BuilderExecutionConflictException(string reasonCode, string message) : Exception(message)
{
    public const string ErrorCode = "builder_execution_refused";
    public string ReasonCode { get; } = reasonCode;
}

public sealed class BuilderExecutionIntegrityException(string message) : Exception(message)
{
    public const string ErrorCode = "builder_execution_integrity_failed";
}
