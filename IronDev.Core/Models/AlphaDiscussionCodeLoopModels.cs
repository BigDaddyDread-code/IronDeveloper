namespace IronDev.Core.Models;

public sealed record SaveDiscussionRequest
{
    public required string Title { get; init; }
    public required string Content { get; init; }
}

public sealed record SaveDiscussionResponse
{
    public required long DocumentId { get; init; }
    public required long DocumentVersionId { get; init; }
}

public sealed record CreateTicketFromDocumentRequest
{
    public string? RequestedTitle { get; init; }
}

public sealed record CreateTicketFromDocumentResponse
{
    public required long TicketId { get; init; }
    public required long SourceDocumentVersionId { get; init; }
}

public sealed record RunTicketDebateRequest
{
    public bool UseLiveModel { get; init; }
}

public sealed record RunTicketDebateResponse
{
    public required string DebateId { get; init; }
    public required AgentDebateResult Result { get; init; }
}

public sealed record AgentDebateResult
{
    public required string DebateId { get; init; }
    public required int ProjectId { get; init; }
    public required long TicketId { get; init; }
    public required IReadOnlyList<AgentDebateContribution> Contributions { get; init; }
    public required AgentDebateDecision Decision { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AgentDebateContribution
{
    public required string Role { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> Concerns { get; init; } = [];
    public IReadOnlyList<string> Recommendations { get; init; } = [];
}

public sealed record AgentDebateDecision
{
    public required bool Proceed { get; init; }
    public required string RecommendedNextStep { get; init; }
    public IReadOnlyList<string> Guardrails { get; init; } = [];
}

public sealed record StartAlphaDisposableCodeRunRequest
{
    public required string DebateId { get; init; }
    public string ExpectedOutput { get; init; } = "Hello from IronDev Alpha";
}

public sealed record StartAlphaDisposableCodeRunResponse
{
    public required string RunId { get; init; }
    public required string State { get; init; }
    public required bool IsDisposable { get; init; }
}

public sealed record GeneratedAlphaProject
{
    public required string WorkspacePath { get; init; }
    public required string ProjectPath { get; init; }
    public required string ProgramPath { get; init; }
    public required string CsprojPath { get; init; }
    public required string ProgramText { get; init; }
    public required string CsprojText { get; init; }
}
