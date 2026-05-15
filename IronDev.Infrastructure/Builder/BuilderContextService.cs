using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Services;

namespace IronDev.Infrastructure.Builder;

/// <summary>
/// Assembles a <see cref="TicketBuildContext"/> from existing SQL-backed services.
///
/// Phase 2 scope:
///   - loads ticket from ITicketService
///   - loads project from IProjectService
///   - loads linked plan via GetPlanByTicketIdAsync (safe: null-tolerant)
///   - loads recent decisions (up to 5) formatted as plain strings
///   - resolves affected file hints from ticket + plan fields
///   - no Weaviate, no LLM, no file writes
/// </summary>
public sealed class BuilderContextService : IBuilderContextService
{
    private readonly ITicketService         _ticketService;
    private readonly IProjectService        _projectService;
    private readonly IProjectMemoryService  _memoryService;
    private readonly IProjectProfileService _profileService;

    public BuilderContextService(
        ITicketService        ticketService,
        IProjectService       projectService,
        IProjectMemoryService memoryService,
        IProjectProfileService profileService)
    {
        _ticketService  = ticketService;
        _projectService = projectService;
        _memoryService  = memoryService;
        _profileService = profileService;
    }

    public async Task<TicketBuildContext> AssembleContextAsync(
        int               projectId,
        long              ticketId,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Load ticket ────────────────────────────────────────────────────
        var ticket = await _ticketService.GetTicketByIdAsync(ticketId, cancellationToken)
            ?? throw new InvalidOperationException($"Ticket {ticketId} not found.");

        // ── 2. Load project ───────────────────────────────────────────────────
        var project = await _projectService.GetByIdAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException($"Project {projectId} not found.");

        // ── 3. Load linked plan (graceful — null is fine) ──────────────────
        IronDev.Data.Models.ProjectImplementationPlan? plan = null;
        try
        {
            plan = await _memoryService.GetPlanByTicketIdAsync(ticketId, cancellationToken);
        }
        catch (NotImplementedException)
        {
            // Phase 2: stub implementation — safe to continue without plan
        }

        // ── 4. Load recent decisions (limit 5, graceful) ───────────────────
        var decisionStrings = new List<string>();
        try
        {
            var decisions = await _memoryService.GetRecentDecisionsAsync(projectId, take: 5, cancellationToken);
            foreach (var d in decisions)
            {
                var parts = new List<string> { d.Title };
                if (!string.IsNullOrWhiteSpace(d.Reason))
                    parts.Add($"Reason: {d.Reason}");
                if (!string.IsNullOrWhiteSpace(d.Category))
                    parts.Add($"[{d.Category}]");
                if (!string.IsNullOrWhiteSpace(d.Status))
                    parts.Add($"({d.Status})");
                decisionStrings.Add(string.Join(" — ", parts));
            }
        }
        catch
        {
            // Decisions are additive context. Never fail the build loop over them.
        }

        // ── 5. Load Profile & Commands ─────────────────────────────────────
        var profile = await _profileService.GetProjectProfileAsync(projectId, cancellationToken);
        var buildCmd = await _profileService.GetDefaultCommandAsync(projectId, "Build", cancellationToken);
        var testCmd = await _profileService.GetDefaultCommandAsync(projectId, "Test", cancellationToken);

        // ── 6. Resolve affected files ──────────────────────────────────────
        var affectedFiles = ResolveAffectedFiles(ticket, plan);

        // ── 7. Assemble ───────────────────────────────────────────────────────
        var ctx = new TicketBuildContext
        {
            ProjectId    = projectId,
            TicketId     = ticketId,
            ProjectName  = project.Name,
            ProjectPath  = project.LocalPath ?? string.Empty,
            
            BuildCommand = buildCmd?.CommandText ?? "dotnet build",
            TestCommand  = testCmd?.CommandText ?? "dotnet test",

            ApplicationType   = profile?.ApplicationType,
            PrimaryLanguage   = profile?.PrimaryLanguage,
            Framework         = profile?.Framework,
            DatabaseEngine    = profile?.DatabaseEngine,
            DataAccessStyle   = profile?.DataAccessStyle,
            TestFramework     = profile?.TestFramework,
            SolutionFile      = profile?.SolutionFile,
            IsExternalProject = profile?.IsExternalProject ?? false,

            TicketTitle               = ticket.Title,
            TicketSummary             = ticket.Summary ?? string.Empty,
            TicketAcceptanceCriteria  = ticket.AcceptanceCriteria,
            TicketImplementationNotes = ticket.TechnicalNotes,
            TicketTestPlan            = ticket.TechnicalNotes,
            TicketBackground          = ticket.Background,
            TicketProblem             = ticket.Problem,

            PlanTitle         = plan?.Title,
            PlanId            = plan?.Id,
            PlanGoal          = plan?.Goal,
            PlanSteps         = plan?.ProposedSteps,
            PlanAffectedFiles = plan?.AffectedContext,
            PlanRisksNotes    = plan?.RisksNotes,

            Decisions         = decisionStrings.AsReadOnly(),
            AffectedFiles     = affectedFiles,

            // Standards
            Standards         = (await _memoryService.GetProjectRulesAsync(projectId, cancellationToken))
                                    .Where(r => r.AppliesTo == "Both" || r.AppliesTo == "Build")
                                    .OrderBy(r => r.EnforcementLevel == "Blocking" ? 0 : r.EnforcementLevel == "Required" ? 1 : 2)
                                    .ToList().AsReadOnly(),

            // Phase 3+: Load full file contents for affected files
            RetrievedSnippets = await LoadFullFileContentsAsync(project.LocalPath ?? string.Empty, affectedFiles, cancellationToken),
            PastBuildFailures = [],
        };

        return ctx;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects file path hints from ticket.LinkedFilePaths, ticket.TechnicalNotes,
    /// plan.AffectedContext, and plan.LinkedFilePaths.
    /// Splits on newline / comma / semicolon, trims, and deduplicates.
    /// Does not validate file existence.
    /// </summary>
    private static IReadOnlyList<string> ResolveAffectedFiles(
        IronDev.Data.Models.ProjectTicket           ticket,
        IronDev.Data.Models.ProjectImplementationPlan? plan)
    {
        var raw = new List<string?>(4)
        {
            ticket.LinkedFilePaths,
            plan?.AffectedContext,
            plan?.LinkedFilePaths,
            // TechnicalNotes may contain file path references on their own lines
            ticket.TechnicalNotes,
        };

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static bool LooksLikePath(string s) =>
            s.Contains('\\') || s.Contains('/') || s.Contains('.');

        foreach (var source in raw)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;

            var parts = source.Split(
                ['\n', '\r', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part) && LooksLikePath(part))
                    result.Add(part);
            }
        }

        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    private static async Task<IReadOnlyList<string>> LoadFullFileContentsAsync(string projectRoot, IReadOnlyList<string> filePaths, CancellationToken ct)
    {
        var snippets = new List<string>();
        foreach (var relativePath in filePaths)
        {
            try
            {
                var fullPath = Path.Combine(projectRoot, relativePath);
                if (File.Exists(fullPath))
                {
                    var content = await File.ReadAllTextAsync(fullPath, ct);
                    snippets.Add($"--- FILE: {relativePath} ---\n{content}");
                }
            }
            catch
            {
                // Ignore missing or locked files for context assembly
            }
        }
        return snippets.AsReadOnly();
    }
}
