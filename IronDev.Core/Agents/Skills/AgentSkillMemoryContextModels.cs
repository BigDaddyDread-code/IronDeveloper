namespace IronDev.Core.Agents.Skills;

public static class AgentSkillMemorySourceKinds
{
    public const string ProjectDocument = "project_document";
    public const string Decision = "decision";
    public const string Ticket = "ticket";
    public const string RunEvidence = "run_evidence";
    public const string WorkspaceEvidence = "workspace_evidence";
    public const string CodeSummary = "code_summary";
    public const string ManualNote = "manual_note";
    public const string Unknown = "unknown";
}

public sealed record AgentSkillMemoryContext
{
    public required bool MemoryContextAvailable { get; init; }
    public required string BindingId { get; init; }
    public required string ProjectId { get; init; }
    public required string SkillId { get; init; }
    public required string Query { get; init; }
    public IReadOnlyList<AgentSkillMemoryContextItem> Items { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public required bool CanApprove { get; init; }
    public required bool CanExecute { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanMutateWorkspace { get; init; }
    public required bool CanWriteMemory { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanUseExternalSystem { get; init; }
}

public sealed record AgentSkillMemoryContextItem
{
    public required string ItemId { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceId { get; init; }
    public string? SourcePath { get; init; }
    public string? Title { get; init; }
    public required string Summary { get; init; }
    public double? Score { get; init; }
    public DateTimeOffset? CreatedUtc { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public required bool IsStale { get; init; }
    public required bool IsAuthoritative { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record AgentSkillMemoryContextBindingRequest
{
    public required string ProjectId { get; init; }
    public required string SkillId { get; init; }
    public required string Purpose { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public int MaxItems { get; init; } = 5;
}

public sealed record AgentSkillMemorySearchRequest
{
    public required string ProjectId { get; init; }
    public required string SkillId { get; init; }
    public required string Query { get; init; }
    public required int MaxItems { get; init; }
}

public sealed record AgentSkillMemorySearchResult
{
    public required bool Available { get; init; }
    public IReadOnlyList<AgentSkillMemorySearchItem> Items { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record AgentSkillMemorySearchItem
{
    public required string ItemId { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceId { get; init; }
    public string? SourcePath { get; init; }
    public string? Title { get; init; }
    public required string Summary { get; init; }
    public double? Score { get; init; }
    public DateTimeOffset? CreatedUtc { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public required bool IsAuthoritative { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IAgentSkillMemoryContextBinder
{
    Task<AgentSkillMemoryContext> BindAsync(
        AgentSkillMemoryContextBindingRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAgentSkillMemorySearchService
{
    Task<AgentSkillMemorySearchResult> SearchAsync(
        AgentSkillMemorySearchRequest request,
        CancellationToken cancellationToken = default);
}
