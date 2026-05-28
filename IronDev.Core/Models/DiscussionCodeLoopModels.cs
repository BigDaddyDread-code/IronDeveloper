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
    public IReadOnlyList<ScenarioVerification> Verifications { get; init; } = [];
}

public sealed record GeneratedCodeFile
{
    public required string RelativePath { get; init; }
    public required string Content { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record CodeRunProfile
{
    public string RuntimeProfileId { get; init; } = "dotnet.console";
    public required string WorkingDirectory { get; init; }
    public required string BuildCommand { get; init; }
    public required string RunCommand { get; init; }
}

public sealed record CodeRunProfileDefinition
{
    public required string RuntimeProfileId { get; init; }
    public required string DisplayName { get; init; }
    public required string BuildCommand { get; init; }
    public required string RunCommand { get; init; }
    public int TimeoutSeconds { get; init; } = 120;
    public int MaxFileCount { get; init; } = 12;
    public int MaxFileBytes { get; init; } = 64_000;
    public IReadOnlyList<string> AllowedVerificationKinds { get; init; } = [];
}

public sealed record CodeProposalValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public CodeRunProfileDefinition? RuntimeProfile { get; init; }
}

public sealed record BuildScenario
{
    public required string ScenarioId { get; init; }
    public required string Name { get; init; }
    public required string DiscussionText { get; init; }
    public required string RuntimeProfileId { get; init; }
    public IReadOnlyList<ScenarioVerification> Verifications { get; init; } = [];
}

public sealed record ScenarioVerification
{
    public required string Kind { get; init; }
    public required string Description { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

public sealed record StartDisposableCodeRunRequest
{
    public required string ReviewId { get; init; }
    public string ScenarioId { get; init; } = string.Empty;
    public string ExpectedOutput { get; init; } = string.Empty;
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
    public IReadOnlyList<OutputVerificationEvidence> OutputVerifications { get; init; } = [];
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
