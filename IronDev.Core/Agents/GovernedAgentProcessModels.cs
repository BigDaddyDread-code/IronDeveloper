using System;
using System.Collections.Generic;

namespace IronDev.Core.Agents;

public interface IGovernedAgentProcessExecutor
{
    Task<GovernedAgentProcessResult> ExecuteAsync(
        GovernedAgentProcessRequest request,
        CancellationToken ct = default);
}

public sealed record GovernedAgentProcessRequest
{
    public required string ToolCallId { get; init; }
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; } = [];
    public required string WorkingDirectory { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public TimeSpan? Timeout { get; init; }
    public string? EvidenceRoot { get; init; }
    public IReadOnlyDictionary<string, string> EvidenceMetadata { get; init; } = new Dictionary<string, string>();
}

public sealed record GovernedAgentProcessResult
{
    public required string ToolCallId { get; init; }
    public required string Command { get; init; }
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public long DurationMs { get; init; }
}
