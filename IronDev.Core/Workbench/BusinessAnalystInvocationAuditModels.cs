using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Agents;

namespace IronDev.Core.Workbench;

public enum WorkbenchBusinessAnalystInvocationOutcome
{
    Succeeded = 1,
    Failed = 2
}

public sealed record WorkbenchBusinessAnalystInvocationAudit
{
    public required Guid AgentRunId { get; init; }
    public required Guid ClaimToken { get; init; }
    public required int AttemptNumber { get; init; }
    public required string SafeRequestId { get; init; }
    public string? ProviderRequestId { get; init; }
    public bool UsageReported { get; init; }
    public AgentModelUsage Usage { get; init; } = new();
    public required long DurationMilliseconds { get; init; }
    public required WorkbenchBusinessAnalystInvocationOutcome Outcome { get; init; }
    public string? FailureCategory { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public enum WorkbenchBusinessAnalystInvocationAuditWriteStatus
{
    Recorded = 1,
    AlreadyExists = 2
}

public sealed record WorkbenchBusinessAnalystInvocationAuditWriteResult
{
    public required WorkbenchBusinessAnalystInvocationAuditWriteStatus Status { get; init; }
    public required string InvocationHash { get; init; }
}

public interface IWorkbenchBusinessAnalystInvocationAuditStore
{
    Task<WorkbenchBusinessAnalystInvocationAuditWriteResult> RecordAsync(
        WorkbenchBusinessAnalystInvocationAudit audit,
        CancellationToken cancellationToken = default);
}

public static class WorkbenchBusinessAnalystInvocationAuditCanonicalizer
{
    private const int CanonicalizationVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static WorkbenchBusinessAnalystInvocationAudit NormalizeAndValidate(
        WorkbenchBusinessAnalystInvocationAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.AgentRunId == Guid.Empty || audit.ClaimToken == Guid.Empty || audit.AttemptNumber <= 0)
            throw Invalid("An exact agent run, claim token, and positive attempt number are required.");
        if (!Enum.IsDefined(audit.Outcome))
            throw Invalid("Invocation outcome must be Succeeded or Failed.");
        if (audit.DurationMilliseconds is < 0 or > 3_600_000)
            throw Invalid("Invocation duration must be between zero and one hour.");
        if (audit.Usage.InputTokens < 0 || audit.Usage.OutputTokens < 0)
            throw Invalid("Invocation usage cannot be negative.");
        if (!audit.UsageReported && (audit.Usage.InputTokens != 0 || audit.Usage.OutputTokens != 0))
            throw Invalid("Unreported usage cannot contain token counts.");
        if (audit.CompletedAtUtc == default || audit.CompletedAtUtc.Offset != TimeSpan.Zero)
            throw Invalid("CompletedAtUtc must be a non-default UTC timestamp.");

        var failureCategory = NormalizeOptionalIdentifier(
            audit.FailureCategory,
            100,
            nameof(audit.FailureCategory));
        if ((audit.Outcome == WorkbenchBusinessAnalystInvocationOutcome.Succeeded && failureCategory is not null) ||
            (audit.Outcome == WorkbenchBusinessAnalystInvocationOutcome.Failed && failureCategory is null))
            throw Invalid("Only failed invocations require a failure category.");

        return audit with
        {
            SafeRequestId = NormalizeIdentifier(audit.SafeRequestId, 100, nameof(audit.SafeRequestId)),
            ProviderRequestId = NormalizeOptionalIdentifier(
                audit.ProviderRequestId,
                200,
                nameof(audit.ProviderRequestId)),
            FailureCategory = failureCategory,
            CompletedAtUtc = audit.CompletedAtUtc.ToUniversalTime()
        };
    }

    public static string ComputeHash(WorkbenchBusinessAnalystInvocationAudit audit)
    {
        var normalized = NormalizeAndValidate(audit);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(
                    new
                    {
                        CanonicalizationVersion,
                        normalized.AgentRunId,
                        normalized.ClaimToken,
                        normalized.AttemptNumber,
                        normalized.SafeRequestId,
                        normalized.ProviderRequestId,
                        normalized.UsageReported,
                        normalized.Usage.InputTokens,
                        normalized.Usage.OutputTokens,
                        normalized.DurationMilliseconds,
                        Outcome = normalized.Outcome.ToString(),
                        normalized.FailureCategory,
                        normalized.CompletedAtUtc
                    },
                    JsonOptions))))
            .ToLowerInvariant();
    }

    private static string NormalizeIdentifier(string value, int maximumLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid($"{fieldName} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximumLength || normalized.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not '.' and not '-' and not '_' and not '/' and not ':'))
            throw Invalid($"{fieldName} must be a bounded sanitized identifier.");
        return normalized;
    }

    private static string? NormalizeOptionalIdentifier(
        string? value,
        int maximumLength,
        string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeIdentifier(value, maximumLength, fieldName);

    private static WorkbenchBusinessAnalystInvocationAuditValidationException Invalid(
        string message) => new(message);
}

public sealed class WorkbenchBusinessAnalystInvocationAuditValidationException(string message)
    : Exception(message);

public sealed class WorkbenchBusinessAnalystInvocationAuditConflictException(string message)
    : Exception(message);
