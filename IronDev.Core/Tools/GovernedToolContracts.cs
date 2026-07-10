namespace IronDev.Core.Tools;

public enum GovernedToolStatus
{
    Succeeded,
    Rejected,
    Failed
}

public enum GovernedToolConnectionRequirement
{
    None,
    Tenant
}

public sealed record GovernedToolDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string DefinitionVersion { get; init; }
    public required string Description { get; init; }
    public required GovernedToolConnectionRequirement ConnectionRequirement { get; init; }
    public required Type InputType { get; init; }
    public required Type OutputType { get; init; }
    public IReadOnlyList<string> AllowedCallers { get; init; } = [];
    public bool MutatesState { get; init; }
    public bool AllowsNestedCalls { get; init; }
    public bool AllowsFileWrites { get; init; }
    public bool AllowsProcessExecution { get; init; }
    public bool AllowsNetworkAccess { get; init; }
    public bool AllowsWorkspaceMutation { get; init; }
    public IReadOnlyList<string> EvidenceKinds { get; init; } = [];
    public string Boundary { get; init; } = string.Empty;
}

public sealed record GovernedToolRequest<TInput>
    where TInput : notnull
{
    public required string RequestId { get; init; }
    public required string ToolName { get; init; }
    public required string RequestedBy { get; init; }
    public required TInput Input { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? ParentRequestId { get; init; }
    public int NestedCallDepth { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record GovernedToolResult<TOutput>
{
    public required string RequestId { get; init; }
    public required string ToolName { get; init; }
    public required GovernedToolStatus Status { get; init; }
    public required string Summary { get; init; }
    public TOutput? Output { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> BlockedActions { get; init; } = [];
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public double ExecutionDurationMs => Math.Max(0, (CompletedAtUtc - StartedAtUtc).TotalMilliseconds);
    public string Boundary { get; init; } = string.Empty;

    public static GovernedToolResult<TOutput> Rejected(
        GovernedToolRequestMarker request,
        string summary,
        IReadOnlyList<string>? blockedActions = null,
        DateTimeOffset? startedAtUtc = null) =>
        new()
        {
            RequestId = request.RequestId,
            ToolName = request.ToolName,
            Status = GovernedToolStatus.Rejected,
            Summary = summary,
            BlockedActions = blockedActions ?? [summary],
            StartedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = "Governed tool execution failed closed before any tool body ran."
        };

    public static GovernedToolResult<TOutput> Failed(
        GovernedToolRequestMarker request,
        string summary,
        IReadOnlyList<string>? blockedActions = null,
        DateTimeOffset? startedAtUtc = null,
        string? boundary = null) =>
        new()
        {
            RequestId = request.RequestId,
            ToolName = request.ToolName,
            Status = GovernedToolStatus.Failed,
            Summary = summary,
            BlockedActions = blockedActions ?? [summary],
            StartedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = boundary ?? "Governed tool execution failed after policy allowed the request."
        };
}

public sealed record GovernedToolRequestMarker
{
    public required string RequestId { get; init; }
    public required string ToolName { get; init; }
}

public interface IGovernedTool<TInput, TOutput>
    where TInput : notnull
{
    GovernedToolDefinition Definition { get; }

    Task<GovernedToolResult<TOutput>> ExecuteAsync(
        GovernedToolRequest<TInput> request,
        CancellationToken cancellationToken = default);
}

public interface IGovernedToolRegistration
{
    GovernedToolDefinition Definition { get; }
}

public interface IGovernedToolRegistry
{
    IReadOnlyList<GovernedToolDefinition> ListTools();

    bool IsRegistered(string toolName);

    Task<GovernedToolResult<TOutput>> RunAsync<TInput, TOutput>(
        GovernedToolRequest<TInput> request,
        CancellationToken cancellationToken = default)
        where TInput : notnull;
}

public sealed record GovernedToolThoughtLedgerEntry
{
    public required string RequestId { get; init; }
    public required string ToolName { get; init; }
    public required string RequestedBy { get; init; }
    public required GovernedToolStatus Status { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> BlockedActions { get; init; } = [];
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public double ExecutionDurationMs { get; init; }
    public string Boundary { get; init; } = string.Empty;
}

public interface IGovernedToolThoughtLedger
{
    Task RecordAsync(
        GovernedToolThoughtLedgerEntry entry,
        CancellationToken cancellationToken = default);
}
