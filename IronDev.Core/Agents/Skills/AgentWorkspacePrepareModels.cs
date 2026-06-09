namespace IronDev.Core.Agents.Skills;

public sealed record AgentWorkspacePrepareRequest
{
    public required string ProjectId { get; init; }

    public required string RunId { get; init; }

    public required string WorkspacePath { get; init; }

    public required string SourceRepo { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
}

public sealed record AgentWorkspacePrepareResult
{
    public required string Status { get; init; }

    public required bool PrepareAttempted { get; init; }

    public required bool Prepared { get; init; }

    public required string ProjectId { get; init; }

    public required string RunId { get; init; }

    public required string WorkspacePath { get; init; }

    public required string SourceRepo { get; init; }

    public required bool SourceRepoExists { get; init; }

    public required bool WorkspacePathExists { get; init; }

    public required bool SourceAndWorkspaceAreDistinct { get; init; }

    public required int FilesCopied { get; init; }

    public required int DirectoriesCreated { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> Blockers { get; init; } = [];
}

public sealed record AgentSkillWorkspacePrepareExecutionPayload
{
    public required bool PrepareAttempted { get; init; }

    public required bool Prepared { get; init; }

    public required string ProjectId { get; init; }

    public required string RunId { get; init; }

    public required string WorkspacePath { get; init; }

    public required string SourceRepo { get; init; }

    public required bool SourceRepoExists { get; init; }

    public required bool WorkspacePathExists { get; init; }

    public required bool SourceAndWorkspaceAreDistinct { get; init; }

    public required int FilesCopied { get; init; }

    public required int DirectoriesCreated { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> Blockers { get; init; } = [];
}

public interface IAgentWorkspacePrepareService
{
    Task<AgentWorkspacePrepareResult> PrepareAsync(
        AgentWorkspacePrepareRequest request,
        CancellationToken cancellationToken = default);
}
