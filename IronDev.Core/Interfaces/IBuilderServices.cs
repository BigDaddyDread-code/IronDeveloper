using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Orchestrates the full Build Ticket flow:
/// context assembly → proposal generation → apply → build → memory save.
/// Each step is separate and gated by developer approval.
/// </summary>
public interface ITicketBuildOrchestrator
{
    /// <summary>
    /// Assembles project context and calls the AI to generate a code change proposal.
    /// Does NOT modify any files.
    /// </summary>
    Task<TicketBuildPreview> CreateBuildPreviewAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the developer-approved proposal, runs the build, and saves the result.
    /// Only call after explicit developer approval.
    /// </summary>
    Task<TicketBuildResult> ApplyAndBuildAsync(
        TicketBuildApproval approval,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Assembles the TicketBuildContext: ticket data, linked plan, decisions,
/// Weaviate snippets (Phase 2+), and project path.
/// </summary>
public interface IBuilderContextService
{
    Task<TicketBuildContext> AssembleContextAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends the builder prompt to the LLM and parses the JSON response into a
/// CodeChangeProposal. No files are written.
/// </summary>
public interface ICodeChangeProposalService
{
    Task<CodeChangeProposal> GenerateProposalAsync(
        TicketBuildContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies file patches to disk after developer approval.
/// Performs Git status check before any write.
/// </summary>
public interface ICodePatchService
{
    /// <summary>Check for uncommitted changes in the project's Git working tree.</summary>
    Task<GitStatus> GetGitStatusAsync(string projectPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates each FileChangeProposal without writing any files.
    /// Checks path safety, file existence, and snippet uniqueness.
    /// </summary>
    Task<PatchValidationResult> DryRunValidateAsync(
        string projectPath,
        System.Collections.Generic.IReadOnlyList<FileChangeProposal> changes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply all FileChangeProposals to disk via BeforeSnippet→AfterSnippet replacement.
    /// Only writes if the before snippet is found exactly once.
    /// </summary>
    Task<PatchApplyResult> ApplyPatchesAsync(
        string projectPath,
        System.Collections.Generic.IReadOnlyList<FileChangeProposal> changes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs 'dotnet build' as a subprocess and captures the result.
/// </summary>
public interface IDotNetBuildService
{
    Task<DotNetBuildResult> BuildAsync(
        string projectOrSolutionPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists build attempt results to storage (Phase 1: SQL only; Phase 2: + Weaviate).
/// </summary>
public interface IBuildResultMemoryService
{
    Task SaveAsync(
        TicketBuildResult result,
        TicketBuildContext context,
        CancellationToken cancellationToken = default);
}
