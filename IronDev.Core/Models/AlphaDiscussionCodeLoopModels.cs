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

public sealed record RunTicketReviewRequest
{
    public bool UseLiveModel { get; init; }
}

public sealed record RunTicketReviewResponse
{
    public required string ReviewId { get; init; }
    public required TicketReviewResult Result { get; init; }
}

public sealed record TicketReviewResult
{
    public required string ReviewId { get; init; }
    public required int ProjectId { get; init; }
    public required long TicketId { get; init; }
    public required string ScenarioId { get; init; }
    public required IReadOnlyList<TicketReviewContribution> Contributions { get; init; }
    public required TicketReviewDecision Decision { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TicketReviewContribution
{
    public required string Role { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> Concerns { get; init; } = [];
    public IReadOnlyList<string> Recommendations { get; init; } = [];
}

public sealed record TicketReviewDecision
{
    public required bool Proceed { get; init; }
    public required string RecommendedNextStep { get; init; }
    public IReadOnlyList<string> Guardrails { get; init; } = [];
}

public sealed record CodeProposal
{
    public required string ProposalId { get; init; }
    public required int ProjectId { get; init; }
    public required long TicketId { get; init; }
    public required string ReviewId { get; init; }
    public required string ScenarioId { get; init; }
    public required string ExpectedOutput { get; init; }
    public required IReadOnlyList<GeneratedCodeFile> Files { get; init; }
    public required CodeRunProfile RunProfile { get; init; }
}

public sealed record GeneratedCodeFile
{
    public required string RelativePath { get; init; }
    public required string Content { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record CodeRunProfile
{
    public required string WorkingDirectory { get; init; }
    public required string BuildCommand { get; init; }
    public required string RunCommand { get; init; }
}

public sealed record StartDisposableCodeRunRequest
{
    public required string ReviewId { get; init; }
    public string ScenarioId { get; init; } = "hello-world-alpha";
    public string ExpectedOutput { get; init; } = "Hello from IronDev Alpha";
}

public sealed record StartDisposableCodeRunResponse
{
    public required string RunId { get; init; }
    public required string State { get; init; }
    public required bool IsDisposable { get; init; }
}

public sealed record RunReviewPackage
{
    public required string RunId { get; init; }
    public required int ProjectId { get; init; }
    public required long TicketId { get; init; }
    public required string State { get; init; }
    public required IReadOnlyList<GeneratedCodeFile> GeneratedFiles { get; init; }
    public required IReadOnlyList<CommandEvidence> CommandEvidence { get; init; }
    public required OutputVerificationEvidence OutputVerification { get; init; }
    public required CodeStandardsEvidence CodeStandards { get; init; }
    public required string FileSetHash { get; init; }
    public required IReadOnlyList<string> Risks { get; init; }
    public required IReadOnlyList<string> HumanReviewChecklist { get; init; }
    public required IReadOnlyList<RunEventSummary> Events { get; init; }
}

public sealed record CommandEvidence
{
    public required string Command { get; init; }
    public string? ExitCode { get; init; }
    public string? StdoutPath { get; init; }
    public string? StderrPath { get; init; }
    public string? DurationMs { get; init; }
}

public sealed record OutputVerificationEvidence
{
    public required string Expected { get; init; }
    public string Actual { get; init; } = string.Empty;
    public bool Verified { get; init; }
    public string? EvidencePath { get; init; }
}

public sealed record CodeStandardsEvidence
{
    public string Status { get; init; } = "Unavailable";
    public string Summary { get; init; } = "Code standards evidence was not found.";
    public string? EvidencePath { get; init; }
}

public sealed record RunEventSummary
{
    public required string EventType { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
}
