using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Core.Workbench;

public enum WorkbenchBusinessAnalystToolCallAuditStatus
{
    Completed = 1,
    Rejected = 2,
    Failed = 3
}

public sealed record WorkbenchBusinessAnalystToolCallAudit
{
    public required string ToolName { get; init; }
    public required string DefinitionVersion { get; init; }
    public required string PolicyVersion { get; init; }
    public required WorkbenchBusinessAnalystToolCallAuditStatus Status { get; init; }
    public required string InputHash { get; init; }
    public required string OutputHash { get; init; }
    public required string SafeSummary { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed record WorkbenchBusinessAnalystPreparationProvenance
{
    public required Guid AgentRunId { get; init; }
    public required Guid ClaimToken { get; init; }
    public required int AttemptNumber { get; init; }
    public required string EffectiveAnalystProfileHash { get; init; }
    public long? AnalystProfilePublishedVersion { get; init; }
    public required string ActualProvider { get; init; }
    public required string ActualModel { get; init; }
    public required int ProviderTimeoutSeconds { get; init; }
    public required string PromptHash { get; init; }
    public required string ToolManifestHash { get; init; }
    public required DateTimeOffset PreparedAtUtc { get; init; }
    public IReadOnlyList<WorkbenchBusinessAnalystToolCallAudit> ToolCalls { get; init; } = [];
}

public enum WorkbenchBusinessAnalystPreparationWriteStatus
{
    Recorded = 1,
    AlreadyExists = 2
}

public sealed record WorkbenchBusinessAnalystPreparationWriteResult
{
    public required WorkbenchBusinessAnalystPreparationWriteStatus Status { get; init; }
    public required string PreparationHash { get; init; }
    public IReadOnlyDictionary<string, string> ToolCallHashes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public interface IWorkbenchBusinessAnalystPreparationAuditStore
{
    Task<WorkbenchBusinessAnalystPreparationWriteResult> RecordAsync(
        WorkbenchBusinessAnalystPreparationProvenance provenance,
        CancellationToken cancellationToken = default);
}

public sealed class WorkbenchBusinessAnalystPreparationAuditValidationException(string message)
    : Exception(message);

public sealed class WorkbenchBusinessAnalystPreparationAuditConflictException(string message)
    : Exception(message);

public static class WorkbenchBusinessAnalystPreparationAuditCanonicalizer
{
    private const int CanonicalizationVersion = 1;
    private const int MaximumProviderLength = 80;
    private const int MaximumModelLength = 200;
    private const int MaximumVersionLength = 100;
    private const int MaximumToolNameLength = 160;
    private const int MaximumSafeSummaryLength = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly string[] UnsafeSummaryMarkers =
    [
        "raw prompt",
        "raw completion",
        "raw tool input",
        "raw tool output",
        "chain-of-thought",
        "chain of thought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "authorization:",
        "bearer ",
        "api_key",
        "apikey",
        "password=",
        "secret="
    ];

    public static WorkbenchBusinessAnalystPreparationProvenance NormalizeAndValidate(
        WorkbenchBusinessAnalystPreparationProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        if (provenance.AgentRunId == Guid.Empty || provenance.ClaimToken == Guid.Empty || provenance.AttemptNumber <= 0)
            throw Invalid("An exact agent run, claim token, and positive attempt number are required.");
        if (provenance.AnalystProfilePublishedVersion is <= 0)
            throw Invalid("AnalystProfilePublishedVersion must be positive when supplied.");
        if (provenance.ProviderTimeoutSeconds is < 1 or > 3600)
            throw Invalid("ProviderTimeoutSeconds must be between 1 and 3600.");
        EnsureUtc(provenance.PreparedAtUtc, nameof(provenance.PreparedAtUtc));

        var calls = provenance.ToolCalls ?? throw Invalid("ToolCalls are required.");
        if (calls.Count == 0)
            throw Invalid("At least one fixed read-only snapshot tool call is required.");
        if (calls.Count > 32)
            throw Invalid("ToolCalls exceeds the bounded maximum of 32.");

        var normalizedCalls = calls.Select(NormalizeAndValidate).ToArray();
        if (normalizedCalls.Select(call => call.ToolName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedCalls.Length)
            throw Invalid("Each fixed read-only snapshot tool may be recorded only once per preparation.");
        if (normalizedCalls.Any(call => call.CompletedAtUtc > provenance.PreparedAtUtc))
            throw Invalid("Tool call completion cannot occur after preparation completion.");

        return provenance with
        {
            EffectiveAnalystProfileHash = NormalizeSha256(provenance.EffectiveAnalystProfileHash, nameof(provenance.EffectiveAnalystProfileHash)),
            ActualProvider = NormalizeIdentifier(provenance.ActualProvider, MaximumProviderLength, nameof(provenance.ActualProvider)),
            ActualModel = NormalizeIdentifier(provenance.ActualModel, MaximumModelLength, nameof(provenance.ActualModel)),
            PromptHash = NormalizeSha256(provenance.PromptHash, nameof(provenance.PromptHash)),
            ToolManifestHash = NormalizeSha256(provenance.ToolManifestHash, nameof(provenance.ToolManifestHash)),
            PreparedAtUtc = provenance.PreparedAtUtc.ToUniversalTime(),
            ToolCalls = normalizedCalls.OrderBy(call => call.ToolName, StringComparer.Ordinal).ToArray()
        };
    }

    public static string ComputeToolCallHash(WorkbenchBusinessAnalystToolCallAudit call)
    {
        var normalized = NormalizeAndValidate(call);
        return Sha256(JsonSerializer.Serialize(new ToolCallCanonicalV1(
            CanonicalizationVersion,
            normalized.ToolName,
            normalized.DefinitionVersion,
            normalized.PolicyVersion,
            normalized.Status.ToString(),
            normalized.InputHash,
            normalized.OutputHash,
            normalized.SafeSummary,
            normalized.StartedAtUtc,
            normalized.CompletedAtUtc), JsonOptions));
    }

    public static string ComputePreparationHash(WorkbenchBusinessAnalystPreparationProvenance provenance)
    {
        var normalized = NormalizeAndValidate(provenance);
        var toolCallHashes = normalized.ToolCalls
            .Select(call => new ToolCallHashCanonicalV1(call.ToolName, ComputeToolCallHash(call)))
            .ToArray();
        return Sha256(JsonSerializer.Serialize(new PreparationCanonicalV1(
            CanonicalizationVersion,
            normalized.AgentRunId,
            normalized.ClaimToken,
            normalized.AttemptNumber,
            normalized.EffectiveAnalystProfileHash,
            normalized.AnalystProfilePublishedVersion,
            normalized.ActualProvider,
            normalized.ActualModel,
            normalized.ProviderTimeoutSeconds,
            normalized.PromptHash,
            normalized.ToolManifestHash,
            normalized.PreparedAtUtc,
            toolCallHashes), JsonOptions));
    }

    public static string ComputeToolManifestHash(
        WorkbenchBusinessAnalystExecutableContractDescriptor contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(contract.Key);
        ArgumentNullException.ThrowIfNull(contract.SnapshotTools);
        return Sha256(JsonSerializer.Serialize(
            new
            {
                contract.Key.ToolPolicyVersion,
                Tools = contract.SnapshotTools.Select(tool => new
                {
                    tool.Name,
                    tool.Version,
                    tool.OutputContract,
                    tool.MutatesState,
                    tool.AllowsFileSystemAccess,
                    tool.AllowsProcessExecution,
                    tool.AllowsNetworkAccess,
                    tool.AllowsWorkspaceMutation,
                    tool.AllowsBuilderAccess,
                    tool.AcceptsCallerScope
                })
            },
            JsonOptions));
    }

    public static string NormalizeSha256(string value, string fieldName)
    {
        var normalized = NormalizeText(value, 71, fieldName).ToLowerInvariant();
        if (normalized.StartsWith("sha256:", StringComparison.Ordinal))
            normalized = normalized[7..];
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw Invalid($"{fieldName} must be a SHA-256 value.");
        return normalized;
    }

    private static WorkbenchBusinessAnalystToolCallAudit NormalizeAndValidate(
        WorkbenchBusinessAnalystToolCallAudit call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (!Enum.IsDefined(call.Status))
            throw Invalid("Tool call status must be Completed, Rejected, or Failed.");
        EnsureUtc(call.StartedAtUtc, nameof(call.StartedAtUtc));
        EnsureUtc(call.CompletedAtUtc, nameof(call.CompletedAtUtc));
        if (call.CompletedAtUtc < call.StartedAtUtc)
            throw Invalid("Tool call completion cannot precede its start.");

        var summary = NormalizeText(call.SafeSummary, MaximumSafeSummaryLength, nameof(call.SafeSummary));
        if (summary.Contains('\r') || summary.Contains('\n'))
            throw Invalid("SafeSummary must be a bounded single-line summary.");
        if (UnsafeSummaryMarkers.Any(marker => summary.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            throw Invalid("SafeSummary contains raw, private, or secret-bearing material.");

        return call with
        {
            ToolName = NormalizeIdentifier(call.ToolName, MaximumToolNameLength, nameof(call.ToolName)),
            DefinitionVersion = NormalizeIdentifier(call.DefinitionVersion, MaximumVersionLength, nameof(call.DefinitionVersion)),
            PolicyVersion = NormalizeIdentifier(call.PolicyVersion, MaximumVersionLength, nameof(call.PolicyVersion)),
            InputHash = NormalizeSha256(call.InputHash, nameof(call.InputHash)),
            OutputHash = NormalizeSha256(call.OutputHash, nameof(call.OutputHash)),
            SafeSummary = summary,
            StartedAtUtc = call.StartedAtUtc.ToUniversalTime(),
            CompletedAtUtc = call.CompletedAtUtc.ToUniversalTime()
        };
    }

    private static void EnsureUtc(DateTimeOffset value, string fieldName)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
            throw Invalid($"{fieldName} must be a non-default UTC timestamp.");
    }

    private static string NormalizeText(string value, int maximumLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid($"{fieldName} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw Invalid($"{fieldName} exceeds the {maximumLength} character limit.");
        return normalized;
    }

    private static string NormalizeIdentifier(string value, int maximumLength, string fieldName)
    {
        var normalized = NormalizeText(value, maximumLength, fieldName);
        if (normalized.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not '.' and not '-' and not '_' and not '/' and not ':' and not '@' and not '+'))
        {
            throw Invalid($"{fieldName} must be a sanitized identifier.");
        }

        return normalized;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static WorkbenchBusinessAnalystPreparationAuditValidationException Invalid(string message) => new(message);

    private sealed record ToolCallCanonicalV1(
        int CanonicalizationVersion,
        string ToolName,
        string DefinitionVersion,
        string PolicyVersion,
        string Status,
        string InputHash,
        string OutputHash,
        string SafeSummary,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc);

    private sealed record ToolCallHashCanonicalV1(string ToolName, string ToolCallHash);

    private sealed record PreparationCanonicalV1(
        int CanonicalizationVersion,
        Guid AgentRunId,
        Guid ClaimToken,
        int AttemptNumber,
        string EffectiveAnalystProfileHash,
        long? AnalystProfilePublishedVersion,
        string ActualProvider,
        string ActualModel,
        int ProviderTimeoutSeconds,
        string PromptHash,
        string ToolManifestHash,
        DateTimeOffset PreparedAtUtc,
        IReadOnlyList<ToolCallHashCanonicalV1> ToolCalls);
}
