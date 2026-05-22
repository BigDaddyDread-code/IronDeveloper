using System;
using System.Collections.Generic;

namespace IronDev.Core.Models;

public sealed class DiscussionSeedRequest
{
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string ProjectSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> ExistingDiscussionTitles { get; init; } = [];
}

public sealed class DiscussionSeedResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public IReadOnlyList<GeneratedDiscussionDocument> Discussions { get; init; } = [];
}

public sealed class GeneratedDiscussionDocument
{
    public string Title { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public IReadOnlyList<string> Prompts { get; init; } = [];
    public IReadOnlyList<string> PossibleOutputs { get; init; } = [];
    public string SuggestedArea { get; init; } = string.Empty;
    public int SuggestedOrder { get; init; }
}

public sealed class DiscussionResolverRequest
{
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public long? SourceDiscussionDocumentId { get; init; }
    public string DiscussionTitle { get; init; } = string.Empty;
    public string DiscussionPrompt { get; init; } = string.Empty;
    public string DiscussionNotes { get; init; } = string.Empty;
    public string ProjectSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> ExistingDecisionTitles { get; init; } = [];
    public IReadOnlyList<string> ExistingTicketTitles { get; init; } = [];
}

public sealed class DiscussionResolutionResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string ResolutionSummary { get; init; } = string.Empty;
    public IReadOnlyList<ArtefactProposal> Proposals { get; init; } = [];
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];
    public IReadOnlyList<string> BuildOrder { get; init; } = [];
    public int ConfidenceScore { get; init; }
}

public enum ArtefactProposalKind
{
    Decision,
    ArchitectureDocument,
    ProjectDocument,
    Requirement,
    Risk,
    OpenQuestion,
    Ticket
}

public enum ArtefactProposalStatus
{
    Proposed,
    Accepted,
    Rejected,
    Applied,
    Failed
}

public sealed class ArtefactProposal
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ArtefactProposalKind Kind { get; init; }
    public ArtefactProposalStatus Status { get; init; } = ArtefactProposalStatus.Proposed;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Priority { get; init; } = "Medium";
    public string RiskLevel { get; init; } = "Medium";
    public int SuggestedBuildOrder { get; init; }
    public int ConfidenceScore { get; init; }
    public long? SourceDiscussionDocumentId { get; init; }
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];
    public IReadOnlyList<string> TestSuggestions { get; init; } = [];
    public IReadOnlyList<string> AffectedFiles { get; init; } = [];
    public IReadOnlyList<string> AffectedSymbols { get; init; } = [];
    public IReadOnlyList<string> GroundingWarnings { get; init; } = [];
}

public sealed class ArtefactApplyRequest
{
    public int ProjectId { get; init; }
    public IReadOnlyList<ArtefactProposal> Proposals { get; init; } = [];
}

public sealed class ArtefactApplyResult
{
    public int AppliedCount { get; init; }
    public IReadOnlyList<AppliedArtefactResult> Results { get; init; } = [];
}

public sealed class AppliedArtefactResult
{
    public Guid ProposalId { get; init; }
    public ArtefactProposalKind Kind { get; init; }
    public bool Success { get; init; }
    public long? ArtifactId { get; init; }
    public string ArtifactType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
