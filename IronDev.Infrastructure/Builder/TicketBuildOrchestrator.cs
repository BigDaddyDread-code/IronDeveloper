using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Builder;

/// <summary>
/// Phase 3 orchestrator:
///   1. Calls IBuilderContextService.AssembleContextAsync — real SQL data
///   2. Calls ICodeChangeProposalService.GenerateProposalAsync — real LLM
///   3. Returns a TicketBuildPreview with real proposal + ContextSummary
///   4. No files written, no dotnet build, no Weaviate
///
/// Phase 4 will implement ApplyAndBuildAsync.
/// </summary>
public sealed class TicketBuildOrchestrator : ITicketBuildOrchestrator
{
    private readonly IBuilderContextService      _contextService;
    private readonly ICodeChangeProposalService  _proposalService;

    public TicketBuildOrchestrator(
        IBuilderContextService     contextService,
        ICodeChangeProposalService proposalService)
    {
        _contextService  = contextService;
        _proposalService = proposalService;
    }

    public async Task<TicketBuildPreview> CreateBuildPreviewAsync(
        int               projectId,
        long              ticketId,
        CancellationToken cancellationToken = default)
    {
        // ── Step 1: assemble real context from SQL ────────────────────────────
        var context = await _contextService.AssembleContextAsync(
            projectId, ticketId, cancellationToken);

        // ── Step 2: build context summary ─────────────────────────────────────
        var contextSummary = BuildContextSummary(context);

        // ── Step 3: call LLM and parse proposal ───────────────────────────────
        var proposal = await _proposalService.GenerateProposalAsync(context, cancellationToken);

        // Ensure ticketId is always set correctly (LLM may return 0 or wrong value)
        proposal.TicketId = ticketId;

        return new TicketBuildPreview
        {
            TicketId       = ticketId,
            TicketTitle    = context.TicketTitle,
            Proposal       = proposal,
            ContextSummary = contextSummary,
        };
    }

    public Task<TicketBuildResult> ApplyAndBuildAsync(
        TicketBuildApproval approval,
        CancellationToken   cancellationToken = default)
    {
        // Phase 4 will implement this.
        return Task.FromResult(new TicketBuildResult
        {
            TicketId     = approval.TicketId,
            ErrorMessage = "Apply and Build is not implemented yet — Phase 4 required.",
            CompletedUtc = DateTime.UtcNow
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildContextSummary(TicketBuildContext ctx)
    {
        var sb = new StringBuilder();

        sb.Append($"Loaded ticket \"{ctx.TicketTitle}\"");
        sb.Append($", project \"{ctx.ProjectName}\"");

        sb.Append(ctx.PlanTitle != null ? ", 1 linked plan" : ", no linked plan");
        sb.Append($", {ctx.Decisions.Count} decision{(ctx.Decisions.Count == 1 ? "" : "s")}");
        sb.Append($", {ctx.AffectedFiles.Count} affected file hint{(ctx.AffectedFiles.Count == 1 ? "" : "s")}");
        sb.Append(". Sending to LLM for proposal generation.");

        return sb.ToString();
    }
}
