using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Models;

// Note: ChatTicketContext is a legacy UI compatibility model; forward clients use API DTOs.
// IDraftTicketService is defined here in Core to keep the interface layer clean;
// the concrete service in Infrastructure references the Agent assembly.
// The ViewModel (also in Agent) depends only on this interface.

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
/// Workbench-level service for generating structured BuilderProposals.
/// Reuses ICodeChangeProposalService and IBuilderContextService.
/// </summary>
public interface IBuilderProposalService
{
    Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default);
    Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default);
    Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default);
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
/// Runs 'dotnet test' as a subprocess and captures the result.
/// </summary>
public interface IDotNetTestService
{
    Task<DotNetTestResult> TestAsync(
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

/// <summary>
/// Generates a DraftTicket from a chat context.
/// Does NOT persist anything — the draft is held in ViewModel memory
/// until the user explicitly approves via ApproveDraftCommand.
///
/// Phase 1-3: stub implementation (no LLM).
/// Phase 4: real LLM implementation.
/// </summary>
public interface IDraftTicketService
{
    /// <summary>
    /// Generates a complete DraftTicket from the supplied chat context.
    /// Stub: returns a deterministic draft derived from the context fields.
    /// LLM phase: calls ILLMService and parses structured JSON response.
    /// </summary>
    Task<DraftTicket> GenerateDraftAsync(
        int    projectId,
        string projectName,
        string proposedTitle,
        string messageText,
        string? linkedFilePaths,
        string? linkedSymbols,
        long?   sessionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Regenerates only the test sub-fields (UnitTests … BuildValidation).
    /// Other DraftTicket fields from <paramref name="current"/> are returned unchanged.
    /// </summary>
    Task<DraftTicket> RegenerateTestsAsync(
        int        projectId,
        DraftTicket current,
        CancellationToken ct = default);

    /// <summary>
    /// Generates an implementation plan from the existing ticket draft fields.
    /// Returns the updated draft with ImplementationPlan populated.
    /// </summary>
    Task<DraftTicket> GeneratePlanAsync(
        int        projectId,
        DraftTicket current,
        CancellationToken ct = default);
}

/// <summary>
/// Classifies build output errors into common project-knowledge missing categories.
/// </summary>
public interface IBuildErrorClassifierService
{
    Task<BuildArchitectureReconciliation?> ClassifyBuildFailureAsync(
        DotNetBuildResult buildResult,
        IronDev.Data.Models.ProjectProfile profile,
        string projectPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Evaluates if a project and ticket are ready for the Builder cycle.
/// </summary>
public interface IBuilderReadinessService
{
    Task<BuildReadinessResult> EvaluateReadinessAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a proposal specifically for architectural consistency 
    /// (e.g. test framework mismatch) before apply.
    /// </summary>
    Task<BuildReadinessResult> ValidateProposalArchitectureAsync(
        BuilderProposal proposal,
        CancellationToken cancellationToken = default);
}
