using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Workbench;

public static class WorkbenchAgentRunStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string NeedsInput = "NeedsInput";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Superseded = "Superseded";
    public const string Stale = "Stale";

    public static bool IsTerminal(string status) => status is
        NeedsInput or Completed or Failed or Cancelled or Superseded or Stale;
}

public static class WorkbenchAgentRunOperationKinds
{
    public const string Submit = "SubmitWorkbenchAgentRun";
    public const string Cancel = "CancelWorkbenchAgentRun";
}

public static class WorkbenchBusinessAnalystContract
{
    public const string AgentVersion = "business-analyst-v0.1";
    public const string PromptVersion = "workbench-shaping-v1";
    public const string ToolPolicyVersion = "workbench-ba-readonly-v1";
    public const int ContextSchemaVersion1 = 1;
    public const int ContextCanonicalizationVersion1 = 1;
    public const int ContextSchemaVersion2 = 2;
    public const int ContextCanonicalizationVersion2 = 2;
    public const int OutputSchemaVersion1 = 1;
    public const int ContextSchemaVersion = ContextSchemaVersion2;
    public const int ContextCanonicalizationVersion = ContextCanonicalizationVersion2;
    public const int OutputSchemaVersion = OutputSchemaVersion1;
}

public enum WorkbenchAgentRunFailurePoint
{
    UserMessagePersisted,
    AgentRunCreated,
    OutboxEnqueued
}

public interface IWorkbenchAgentRunFailureInjector
{
    void ThrowIfRequested(WorkbenchAgentRunFailurePoint point);
}

public sealed class NoOpWorkbenchAgentRunFailureInjector : IWorkbenchAgentRunFailureInjector
{
    public void ThrowIfRequested(WorkbenchAgentRunFailurePoint point)
    {
    }
}

public sealed record SubmitWorkbenchAgentRunCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    long ChatSessionId,
    string Message);

public sealed record SubmitWorkbenchAgentRunResult(
    Guid AgentRunId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    long ChatSessionId,
    long UserMessageId,
    string Status,
    Guid ClientOperationId,
    DateTime CreatedAtUtc,
    bool IsReplay);

public sealed record CancelWorkbenchAgentRunCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid AgentRunId,
    Guid ClientOperationId);

public sealed record CancelWorkbenchAgentRunResult(
    Guid AgentRunId,
    string Status,
    bool CancellationRequested,
    Guid ClientOperationId,
    bool IsReplay);

public sealed record WorkbenchAgentRunSnapshot(
    Guid AgentRunId,
    int TenantId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    int ActorUserId,
    long ChatSessionId,
    long SourceUserMessageId,
    string Status,
    int AttemptCount,
    long? AssistantMessageId,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancellationRequestedAtUtc);

public sealed record WorkbenchAgentRunClaim(
    Guid AgentRunId,
    Guid ClaimToken,
    int TenantId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    int ActorUserId,
    long ChatSessionId,
    long SourceUserMessageId,
    int AttemptCount,
    string AgentVersion,
    string PromptVersion,
    string ToolPolicyVersion,
    int ContextSchemaVersion,
    int ContextCanonicalizationVersion,
    int OutputSchemaVersion);

public sealed record WorkbenchAgentContextMessage(
    long MessageId,
    string Role,
    string Message,
    DateTime CreatedAtUtc);

public sealed record WorkbenchBusinessAnalystContext(
    Guid AgentRunId,
    int TenantId,
    int ProjectId,
    string ProjectName,
    long WorkbenchSessionId,
    long LeaseEpoch,
    long ChatSessionId,
    long SourceUserMessageId,
    long UnderstandingRevision,
    string UnderstandingJson,
    IReadOnlyList<WorkbenchAgentContextMessage> Messages,
    string AgentVersion,
    string PromptVersion,
    string ToolPolicyVersion,
    int ContextSchemaVersion,
    int ContextCanonicalizationVersion,
    int OutputSchemaVersion,
    string ContextHash);

public sealed record WorkbenchBusinessAnalystOutput(
    int OutputSchemaVersion,
    string ContextHash,
    long BasedOnUnderstandingRevision,
    string Outcome,
    string AssistantMessage);

public sealed record WorkbenchAgentRunMaterializationResult(
    Guid AgentRunId,
    string Status,
    bool Materialized,
    long? AssistantMessageId,
    bool IsReplay,
    string? RejectionReason = null);

public sealed record WorkbenchAgentRunOutboxItem(
    long OutboxEventId,
    Guid AgentRunId);

public interface IWorkbenchAgentRunService
{
    Task<SubmitWorkbenchAgentRunResult> SubmitAsync(
        SubmitWorkbenchAgentRunCommand command,
        CancellationToken cancellationToken = default);

    Task<CancelWorkbenchAgentRunResult> CancelAsync(
        CancelWorkbenchAgentRunCommand command,
        CancellationToken cancellationToken = default);

    Task<WorkbenchAgentRunSnapshot> GetAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        Guid agentRunId,
        CancellationToken cancellationToken = default);

    Task<WorkbenchAgentRunClaim?> ClaimAsync(
        Guid agentRunId,
        string workerId,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default);

    Task<bool> AuthorizeInvocationAsync(
        WorkbenchAgentRunClaim claim,
        TimeSpan renewedClaimDuration,
        CancellationToken cancellationToken = default);

    Task<WorkbenchAgentRunMaterializationResult> MaterializeAsync(
        WorkbenchAgentRunClaim claim,
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystOutput output,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        WorkbenchAgentRunClaim claim,
        string diagnosticCode,
        string diagnosticHash,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchAgentContextAssembler
{
    Task<WorkbenchBusinessAnalystContext> AssembleAsync(
        WorkbenchAgentRunClaim claim,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchBusinessAnalystAgent
{
    Task<IWorkbenchBusinessAnalystPreparedInvocation> PrepareAsync(
        WorkbenchAgentRunClaim claim,
        WorkbenchBusinessAnalystContext context,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchBusinessAnalystPreparedInvocation
{
    TimeSpan ProviderTimeout { get; }

    Task<string> InvokeProviderAsync(CancellationToken cancellationToken = default);
}

public interface IWorkbenchAgentRunOutbox
{
    Task<IReadOnlyList<WorkbenchAgentRunOutboxItem>> ReadPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    Task MarkPublishedAsync(
        long outboxEventId,
        CancellationToken cancellationToken = default);
}

public interface IWorkbenchAgentRunProcessor
{
    Task ProcessAsync(
        WorkbenchAgentRunOutboxItem item,
        string workerId,
        CancellationToken cancellationToken = default);
}

public static class WorkbenchBusinessAnalystOutputValidator
{
    private static readonly JsonSerializerOptions StrictJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static WorkbenchBusinessAnalystOutput DeserializeAndValidate(
        string json,
        WorkbenchBusinessAnalystContext context)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new WorkbenchAgentOutputValidationException("The Business Analyst output is empty.");

        try
        {
            var output = JsonSerializer.Deserialize<WorkbenchBusinessAnalystOutput>(json, StrictJsonOptions)
                ?? throw new WorkbenchAgentOutputValidationException("The Business Analyst output is empty.");
            Validate(output, context);
            return output;
        }
        catch (JsonException exception)
        {
            throw new WorkbenchAgentOutputValidationException(
                "The Business Analyst output does not match schema version 1.",
                exception);
        }
    }

    public static void Validate(
        WorkbenchBusinessAnalystOutput output,
        WorkbenchBusinessAnalystContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);

        if (context.OutputSchemaVersion != WorkbenchBusinessAnalystContract.OutputSchemaVersion1)
            throw new WorkbenchAgentOutputValidationException(
                $"Unsupported output schema version {context.OutputSchemaVersion} for this agent run.");

        if (output.OutputSchemaVersion != WorkbenchBusinessAnalystContract.OutputSchemaVersion1)
            throw new WorkbenchAgentOutputValidationException(
                $"outputSchemaVersion must be {WorkbenchBusinessAnalystContract.OutputSchemaVersion1} for this agent run.");

        if (string.IsNullOrWhiteSpace(output.ContextHash) ||
            output.ContextHash.Length != 64 ||
            !string.Equals(output.ContextHash, context.ContextHash, StringComparison.OrdinalIgnoreCase))
            throw new WorkbenchAgentOutputValidationException("contextHash does not match the server-owned context snapshot.");

        if (output.BasedOnUnderstandingRevision != context.UnderstandingRevision)
            throw new WorkbenchAgentOutputValidationException(
                "basedOnUnderstandingRevision does not match the server-owned context snapshot.");

        if (output.Outcome is not (WorkbenchAgentRunStates.Completed or WorkbenchAgentRunStates.NeedsInput))
            throw new WorkbenchAgentOutputValidationException(
                "outcome must be Completed or NeedsInput.");

        if (string.IsNullOrWhiteSpace(output.AssistantMessage))
            throw new WorkbenchAgentOutputValidationException("assistantMessage is required.");

        if (output.AssistantMessage.Length > 100_000)
            throw new WorkbenchAgentOutputValidationException("assistantMessage exceeds the 100000 character limit.");
    }
}

public sealed class WorkbenchAgentRunValidationException(string message) : Exception(message);

public sealed class WorkbenchChatSessionBindingException : Exception
{
    public const string ErrorCode = "workbench_chat_session_mismatch";

    public WorkbenchChatSessionBindingException()
        : base("This Workbench session is already bound to a different chat session.")
    {
    }
}

public sealed class WorkbenchAgentRunAlreadyActiveException : Exception
{
    public const string ErrorCode = "workbench_agent_run_active";

    public WorkbenchAgentRunAlreadyActiveException()
        : base("This Workbench session already has a pending or running Business Analyst turn.")
    {
    }
}

public sealed class WorkbenchAgentRunNotFoundException : Exception;

public sealed class WorkbenchAgentOutputValidationException : Exception
{
    public WorkbenchAgentOutputValidationException(string message)
        : base(message)
    {
    }

    public WorkbenchAgentOutputValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WorkbenchAgentProviderTimeoutException : Exception
{
    public WorkbenchAgentProviderTimeoutException()
        : base("The Business Analyst provider did not complete within the bounded invocation timeout.")
    {
    }
}

public sealed class WorkbenchAgentProviderTimeoutConfigurationException : Exception
{
    public WorkbenchAgentProviderTimeoutConfigurationException(string message)
        : base(message)
    {
    }
}
