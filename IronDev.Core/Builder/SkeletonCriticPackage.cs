using IronDev.Core.Models;

namespace IronDev.Core.Builder;

/// <summary>
/// The review package a skeleton run prepares for the independent critic.
///
/// Boundary: a package is review material, not a review. It carries the exact diffs
/// and full proposed contents (the critic must never infer from a thin manifest),
/// plus references to on-disk command evidence — and no verdict, finding, approval,
/// or authority field of any kind. The orchestrator packages work; the critic reviews
/// it; only the human gate approves it.
/// </summary>
public sealed record SkeletonCriticPackage
{
    public required string PackageId { get; init; }
    public required string RunId { get; init; }
    public required string ProposalId { get; init; }
    public required long TicketId { get; init; }
    public required int ProjectId { get; init; }
    public required string TicketTitle { get; init; }
    public string AcceptanceCriteria { get; init; } = string.Empty;
    public string ProposalSummary { get; init; } = string.Empty;
    public string ProposalRationale { get; init; } = string.Empty;
    public IReadOnlyList<SkeletonCriticPackageChange> Changes { get; init; } = [];

    /// <summary>
    /// Tests authored from the acceptance criteria, blind by contract to the builder's
    /// diff — the criterion→test matrix at full fidelity. Authored tests are workspace
    /// material only: they are never applied to the source repository.
    /// </summary>
    public IReadOnlyList<SkeletonAuthoredTest> AuthoredTests { get; init; } = [];

    /// <summary>
    /// P1-4 — the criterion→test coverage invariant. One row per acceptance
    /// criterion, computed deterministically from the criteria and the authored
    /// tests: covered by named tests, or explicitly UNCOVERED. A silent coverage
    /// hole is impossible by construction; a visible one is something the human
    /// consciously approves at the gate. The ground-truth verifier recomputes
    /// this record — a forged one is tampering.
    /// </summary>
    public IReadOnlyList<SkeletonCriterionCoverage> CriterionCoverage { get; init; } = [];

    public IReadOnlyList<SkeletonCriticPackageCommandResult> CommandResults { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool WorkspaceRunSucceeded { get; init; }
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "This package is review material for the independent critic. It is not a review, " +
        "not approval, and grants nothing. Critic review and human approval remain separate governed steps.";
}

/// <summary>One proposed change at full fidelity: the exact diff and the complete proposed content.</summary>
public sealed record SkeletonCriticPackageChange
{
    public required string FilePath { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsNewFile { get; init; }
    public bool IsDeletion { get; init; }
    public string Diff { get; init; } = string.Empty;
    public string? FullContentAfter { get; init; }
}

/// <summary>
/// Build/test outcome with references to the exact on-disk output evidence.
/// Output travels as evidence references, not inline dumps.
/// </summary>
public sealed record SkeletonCriticPackageCommandResult
{
    public required string DisplayName { get; init; }
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public long DurationMs { get; init; }
    public string? StandardOutputRef { get; init; }
    public string? StandardErrorRef { get; init; }
}

public static class SkeletonCriticPackageBuilder
{
    public static SkeletonCriticPackage Build(
        string runId,
        string proposalId,
        long ticketId,
        int projectId,
        string ticketTitle,
        string? acceptanceCriteria,
        BuilderProposal proposal,
        IReadOnlyList<SkeletonAuthoredTest> authoredTests,
        IReadOnlyList<SkeletonCriticPackageCommandResult> commandResults,
        IReadOnlyList<string> evidenceRefs,
        bool workspaceRunSucceeded) =>
        new()
        {
            PackageId = $"critic-pkg-{runId}",
            RunId = runId,
            ProposalId = proposalId,
            TicketId = ticketId,
            ProjectId = projectId,
            TicketTitle = ticketTitle,
            AcceptanceCriteria = acceptanceCriteria ?? string.Empty,
            ProposalSummary = proposal.Summary,
            ProposalRationale = proposal.Rationale,
            Changes = proposal.Changes
                .Where(change => change.IsValid)
                .Select(change => new SkeletonCriticPackageChange
                {
                    FilePath = change.FilePath,
                    Description = change.Description,
                    IsNewFile = change.IsNewFile,
                    IsDeletion = change.IsDeletion,
                    Diff = change.Diff,
                    FullContentAfter = change.FullContentAfter
                })
                .ToList(),
            AuthoredTests = authoredTests,
            CriterionCoverage = SkeletonCriterionCoverageCalculator.Compute(acceptanceCriteria, authoredTests),
            CommandResults = commandResults,
            EvidenceRefs = evidenceRefs,
            WorkspaceRunSucceeded = workspaceRunSucceeded
        };
}
