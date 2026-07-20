using System.Text;
using System.Text.Json;
using IronDev.Core.Agents;

namespace IronDev.Core.Workbench;

public static class WorkbenchBusinessAnalystProviderContract
{
    public const int EnvelopeVersion = 1;
    public const string ContextBudgetPolicyVersion = "workbench-ba-context-budget-v1";

    public const int MaximumConversationMessageCharacters = 20_000;
    public const int MaximumConversationCharacters = 160_000;
    public const int MaximumImmutablePolicyUtf8Bytes = 48_000;
    public const int MaximumAnalystProfileUtf8Bytes = 32_000;
    public const int MaximumSnapshotUtf8Bytes = 96_000;
    public const int MaximumRequestUtf8Bytes = 104_000;

    public const int MaximumContextWindowTokens = 128_000;
    public const int ReservedOutputTokens = 16_000;
    public const int SafetyMarginTokens = 8_000;
    // In the absence of a provider tokenizer, one UTF-8 byte per token is the
    // conservative upper bound used on both sides of the request boundary.
    public const int MaximumOutputUtf8Bytes = ReservedOutputTokens;
    public const int MaximumAssistantMessageCharacters = ReservedOutputTokens;
    public const int MaximumEstimatedInputTokens =
        MaximumContextWindowTokens - ReservedOutputTokens - SafetyMarginTokens;
}

public sealed record WorkbenchBusinessAnalystPromptParts
{
    public required string ImmutableCodePolicy { get; init; }
    public required string UntrustedSnapshot { get; init; }
}

public sealed record WorkbenchBusinessAnalystProviderEnvelope
{
    public required int EnvelopeVersion { get; init; }
    public required string SafeRequestId { get; init; }
    public required string ImmutableCodePolicy { get; init; }
    public required string ConstrainedAnalystProfile { get; init; }
    public required string UntrustedSnapshot { get; init; }
    public required string ContextBudgetPolicyVersion { get; init; }
    public required int ReservedOutputTokens { get; init; }
}

public sealed record WorkbenchBusinessAnalystContextBudgetMeasurement
{
    public required string PolicyVersion { get; init; }
    public required int ConversationCharacters { get; init; }
    public required int MaximumConversationMessageCharacters { get; init; }
    public required int ImmutablePolicyUtf8Bytes { get; init; }
    public required int AnalystProfileUtf8Bytes { get; init; }
    public required int SnapshotUtf8Bytes { get; init; }
    public required int CompleteRequestUtf8Bytes { get; init; }
    public required int EstimatedInputTokens { get; init; }
    public required int ReservedOutputTokens { get; init; }
    public required int SafetyMarginTokens { get; init; }
    public required int MaximumContextWindowTokens { get; init; }
}

public sealed record WorkbenchBusinessAnalystProviderResponse
{
    public required string Output { get; init; }
    public required string SafeRequestId { get; init; }
    public string? ProviderRequestId { get; init; }
    public AgentModelUsage Usage { get; init; } = new();
    public bool UsageReported { get; init; }
    public long DurationMilliseconds { get; init; }
}

public interface IWorkbenchBusinessAnalystRoleAwareLlmService
{
    Task<WorkbenchBusinessAnalystProviderResponse> GetResponseAsync(
        WorkbenchBusinessAnalystProviderEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public sealed record WorkbenchBusinessAnalystProviderMessage
{
    public required AgentModelRole Role { get; init; }
    public required string Content { get; init; }
}

public static class WorkbenchBusinessAnalystProviderMessageMapper
{
    public static IReadOnlyList<WorkbenchBusinessAnalystProviderMessage> ForOpenAi(
        WorkbenchBusinessAnalystProviderEnvelope envelope) =>
        ValidateAndMap(envelope, supportsDeveloperRole: true);

    public static IReadOnlyList<WorkbenchBusinessAnalystProviderMessage> ForSystemUserProvider(
        WorkbenchBusinessAnalystProviderEnvelope envelope) =>
        ValidateAndMap(envelope, supportsDeveloperRole: false);

    private static IReadOnlyList<WorkbenchBusinessAnalystProviderMessage> ValidateAndMap(
        WorkbenchBusinessAnalystProviderEnvelope envelope,
        bool supportsDeveloperRole)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EnvelopeVersion != WorkbenchBusinessAnalystProviderContract.EnvelopeVersion ||
            !string.Equals(
                envelope.ContextBudgetPolicyVersion,
                WorkbenchBusinessAnalystProviderContract.ContextBudgetPolicyVersion,
                StringComparison.Ordinal) ||
            envelope.ReservedOutputTokens != WorkbenchBusinessAnalystProviderContract.ReservedOutputTokens ||
            string.IsNullOrWhiteSpace(envelope.SafeRequestId) ||
            string.IsNullOrWhiteSpace(envelope.ImmutableCodePolicy) ||
            string.IsNullOrWhiteSpace(envelope.ConstrainedAnalystProfile) ||
            string.IsNullOrWhiteSpace(envelope.UntrustedSnapshot))
        {
            throw new WorkbenchBusinessAnalystProviderEnvelopeException(
                "The Business Analyst provider envelope is incomplete or has an unsupported version.");
        }

        if (supportsDeveloperRole)
        {
            return
            [
                Message(AgentModelRole.System, envelope.ImmutableCodePolicy),
                Message(AgentModelRole.Developer, envelope.ConstrainedAnalystProfile),
                Message(AgentModelRole.User, envelope.UntrustedSnapshot)
            ];
        }

        // Providers without a developer role may safely demote the advisory profile to
        // user authority. It must never be promoted into the immutable system policy.
        return
        [
            Message(AgentModelRole.System, envelope.ImmutableCodePolicy),
            Message(AgentModelRole.User, envelope.ConstrainedAnalystProfile),
            Message(AgentModelRole.User, envelope.UntrustedSnapshot)
        ];
    }

    private static WorkbenchBusinessAnalystProviderMessage Message(
        AgentModelRole role,
        string content) =>
        new() { Role = role, Content = content };
}

public static class WorkbenchBusinessAnalystContextBudget
{
    private static readonly JsonSerializerOptions RequestJsonOptions =
        new(JsonSerializerDefaults.Web);

    public static WorkbenchBusinessAnalystContextBudgetMeasurement MeasureAndValidate(
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystProviderEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);

        var messages = context.Messages ?? [];
        var maximumMessageCharacters = messages.Count == 0
            ? 0
            : messages.Max(message => message.Message?.Length ?? 0);
        var conversationCharacters = messages.Sum(message => (long)(message.Message?.Length ?? 0));
        EnsureWithin(
            "conversation_message_characters",
            maximumMessageCharacters,
            WorkbenchBusinessAnalystProviderContract.MaximumConversationMessageCharacters);
        EnsureWithin(
            "conversation_characters",
            conversationCharacters,
            WorkbenchBusinessAnalystProviderContract.MaximumConversationCharacters);

        var immutablePolicyBytes = Encoding.UTF8.GetByteCount(envelope.ImmutableCodePolicy);
        var analystProfileBytes = Encoding.UTF8.GetByteCount(envelope.ConstrainedAnalystProfile);
        var snapshotBytes = Encoding.UTF8.GetByteCount(envelope.UntrustedSnapshot);
        EnsureWithin(
            "immutable_policy_utf8_bytes",
            immutablePolicyBytes,
            WorkbenchBusinessAnalystProviderContract.MaximumImmutablePolicyUtf8Bytes);
        EnsureWithin(
            "analyst_profile_utf8_bytes",
            analystProfileBytes,
            WorkbenchBusinessAnalystProviderContract.MaximumAnalystProfileUtf8Bytes);
        EnsureWithin(
            "snapshot_utf8_bytes",
            snapshotBytes,
            WorkbenchBusinessAnalystProviderContract.MaximumSnapshotUtf8Bytes);

        // Budget the fully encoded role-aware request frame, not just the decoded
        // component strings. JSON escaping can materially expand hostile or highly
        // structured input on the wire, so a fixed framing allowance would undercount.
        var completeRequestBytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                envelope.EnvelopeVersion,
                envelope.ContextBudgetPolicyVersion,
                envelope.ReservedOutputTokens,
                Messages = WorkbenchBusinessAnalystProviderMessageMapper
                    .ForOpenAi(envelope)
                    .Select(message => new
                    {
                        Role = message.Role.ToString(),
                        message.Content
                    })
                    .ToArray()
            },
            RequestJsonOptions).Length;
        EnsureWithin(
            "complete_request_utf8_bytes",
            completeRequestBytes,
            WorkbenchBusinessAnalystProviderContract.MaximumRequestUtf8Bytes);

        // Without a provider tokenizer, one token per UTF-8 byte is a safe upper bound.
        // Typical prose will use fewer tokens; adversarial/random input cannot make this
        // estimate smaller than the encoded request that is actually sent.
        var estimatedInputTokens = completeRequestBytes;
        EnsureWithin(
            "estimated_input_tokens",
            estimatedInputTokens,
            WorkbenchBusinessAnalystProviderContract.MaximumEstimatedInputTokens);
        var aggregateTokens = checked(
            estimatedInputTokens +
            WorkbenchBusinessAnalystProviderContract.ReservedOutputTokens +
            WorkbenchBusinessAnalystProviderContract.SafetyMarginTokens);
        EnsureWithin(
            "aggregate_context_tokens",
            aggregateTokens,
            WorkbenchBusinessAnalystProviderContract.MaximumContextWindowTokens);

        return new WorkbenchBusinessAnalystContextBudgetMeasurement
        {
            PolicyVersion = WorkbenchBusinessAnalystProviderContract.ContextBudgetPolicyVersion,
            ConversationCharacters = checked((int)conversationCharacters),
            MaximumConversationMessageCharacters = maximumMessageCharacters,
            ImmutablePolicyUtf8Bytes = immutablePolicyBytes,
            AnalystProfileUtf8Bytes = analystProfileBytes,
            SnapshotUtf8Bytes = snapshotBytes,
            CompleteRequestUtf8Bytes = completeRequestBytes,
            EstimatedInputTokens = estimatedInputTokens,
            ReservedOutputTokens = WorkbenchBusinessAnalystProviderContract.ReservedOutputTokens,
            SafetyMarginTokens = WorkbenchBusinessAnalystProviderContract.SafetyMarginTokens,
            MaximumContextWindowTokens = WorkbenchBusinessAnalystProviderContract.MaximumContextWindowTokens
        };
    }

    private static void EnsureWithin(string dimension, long actual, long maximum)
    {
        if (actual > maximum)
            throw new WorkbenchBusinessAnalystContextTooLargeException(dimension, actual, maximum);
    }
}

public sealed class WorkbenchBusinessAnalystContextTooLargeException : Exception
{
    public const string ErrorCode = "agent_context_too_large";

    public WorkbenchBusinessAnalystContextTooLargeException(
        string dimension,
        long actual,
        long maximum)
        : base(
            $"The Business Analyst context exceeds '{dimension}' under " +
            $"{WorkbenchBusinessAnalystProviderContract.ContextBudgetPolicyVersion}.")
    {
        Dimension = dimension;
        Actual = actual;
        Maximum = maximum;
    }

    public string Dimension { get; }
    public long Actual { get; }
    public long Maximum { get; }
}

public sealed class WorkbenchBusinessAnalystProviderEnvelopeException(string message)
    : Exception(message);

public sealed class WorkbenchBusinessAnalystRoleAwareProviderRequiredException : Exception
{
    public const string ErrorCode = "agent_provider_role_hierarchy_unsupported";

    public WorkbenchBusinessAnalystRoleAwareProviderRequiredException(string provider)
        : base(
            $"Provider '{provider}' cannot preserve the Workbench Business Analyst instruction hierarchy.")
    {
    }
}
