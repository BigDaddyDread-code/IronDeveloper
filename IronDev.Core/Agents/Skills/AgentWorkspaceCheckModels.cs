namespace IronDev.Core.Agents.Skills;

public sealed record AgentWorkspaceCheckRequest
{
    public required string ProjectId { get; init; }

    public required string RunId { get; init; }

    public required string WorkspacePath { get; init; }

    public required string SourceRepo { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
}

public sealed record AgentWorkspaceCheckResult
{
    public required bool CheckAvailable { get; init; }

    public required string ProjectId { get; init; }

    public required string RunId { get; init; }

    public required string WorkspacePath { get; init; }

    public required string SourceRepo { get; init; }

    public required bool SourceRepoExists { get; init; }

    public required bool WorkspacePathExists { get; init; }

    public required bool WorkspaceInsideAllowedRoot { get; init; }

    public required bool SourceAndWorkspaceAreDistinct { get; init; }

    public required bool ReadyForPrepare { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> Blockers { get; init; } = [];
}

public sealed record AgentSkillWorkspaceCheckExecutionPayload
{
    public required bool CheckAvailable { get; init; }

    public required string ProjectId { get; init; }

    public required string RunId { get; init; }

    public required string WorkspacePath { get; init; }

    public required string SourceRepo { get; init; }

    public required bool SourceRepoExists { get; init; }

    public required bool WorkspacePathExists { get; init; }

    public required bool WorkspaceInsideAllowedRoot { get; init; }

    public required bool SourceAndWorkspaceAreDistinct { get; init; }

    public required bool ReadyForPrepare { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> Blockers { get; init; } = [];
}

public interface IAgentWorkspaceCheckService
{
    Task<AgentWorkspaceCheckResult> CheckAsync(
        AgentWorkspaceCheckRequest request,
        CancellationToken cancellationToken = default);
}
