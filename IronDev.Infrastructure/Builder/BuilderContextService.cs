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
    private readonly IProjectDocumentService? _documentService;

    public BuilderContextService(
        ITicketService        ticketService,
        IProjectService       projectService,
        IProjectMemoryService memoryService,
        IProjectProfileService profileService)
        : this(ticketService, projectService, memoryService, profileService, null)
    {
    }

    public BuilderContextService(
        ITicketService ticketService,
        IProjectService projectService,
        IProjectMemoryService memoryService,
        IProjectProfileService profileService,
        IProjectDocumentService? documentService)
    {
        _ticketService  = ticketService;
        _projectService = projectService;
        _memoryService  = memoryService;
        _profileService = profileService;
        _documentService = documentService;
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
            // Plan lookup is optional; continue without linked plan context.
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

        try
        {
            var contextDocs = await _memoryService.GetRelevantContextDocumentsAsync(
                projectId,
                $"{ticket.Title} {ticket.Summary} {ticket.Problem} {ticket.TechnicalNotes}",
                take: 10,
                cancellationToken);

            foreach (var doc in contextDocs)
            {
                var text = string.IsNullOrWhiteSpace(doc.Summary) ? doc.Content : doc.Summary;
                decisionStrings.Add($"[{doc.AuthorityLevel}] {doc.DocumentType}: {doc.Title} - {text}");
            }
        }
        catch
        {
            // Context documents are additive context. Never fail the build loop over them.
        }

        // ── 5. Load Profile & Commands ─────────────────────────────────────
        var profile = await _profileService.GetProjectProfileAsync(projectId, cancellationToken);
        var buildCmd = await _profileService.GetDefaultCommandAsync(projectId, "Build", cancellationToken);
        var testCmd = await _profileService.GetDefaultCommandAsync(projectId, "Test", cancellationToken);

        // ── 6. Resolve affected files ──────────────────────────────────────
        var affectedFiles = ResolveAffectedFiles(ticket, plan);
        var sourceDocument = await ResolveSourceDocumentAsync(ticket, projectId, cancellationToken);

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

            SourceDocumentId               = sourceDocument.Document?.Id,
            SourceDocumentVersionId        = sourceDocument.Version?.Id ?? ticket.SourceDocumentVersionId,
            SourceDocumentTitle            = sourceDocument.Document?.Title,
            SourceDocumentVersionLabel     = sourceDocument.Version?.VersionLabel,
            SourceDocumentMarkdownExcerpt  = sourceDocument.MarkdownExcerpt,
            SourceDocumentResolutionStatus = sourceDocument.Status,
            SourceDocumentResolutionDetail = sourceDocument.Detail,
            SourceLinkEvidence             = sourceDocument.Evidence,

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
    private async Task<SourceDocumentResolution> ResolveSourceDocumentAsync(
        IronDev.Data.Models.ProjectTicket ticket,
        int projectId,
        CancellationToken cancellationToken)
    {
        if (ticket.SourceDocumentVersionId is null)
        {
            return new SourceDocumentResolution
            {
                Status = "missing_source_document_version",
                Detail = "Ticket does not have SourceDocumentVersionId."
            };
        }

        if (_documentService is null)
        {
            return new SourceDocumentResolution
            {
                Status = "document_service_unavailable",
                Detail = "Builder context service was created without IProjectDocumentService."
            };
        }

        var version = await _documentService.GetVersionAsync(ticket.SourceDocumentVersionId.Value, cancellationToken);
        if (version is null)
        {
            return new SourceDocumentResolution
            {
                Status = "source_document_version_not_found",
                Detail = $"ProjectDocumentVersion {ticket.SourceDocumentVersionId.Value} could not be resolved."
            };
        }

        var document = await _documentService.GetDocumentAsync(version.DocumentId, cancellationToken);
        if (document is null)
        {
            return new SourceDocumentResolution
            {
                Version = version,
                Status = "source_document_not_found",
                Detail = $"ProjectDocument {version.DocumentId} could not be resolved."
            };
        }

        if (document.ProjectId != projectId)
        {
            return new SourceDocumentResolution
            {
                Document = document,
                Version = version,
                Status = "source_document_wrong_project",
                Detail = $"ProjectDocument {document.Id} belongs to project {document.ProjectId}, not {projectId}."
            };
        }

        var links = await _documentService.GetLinksForVersionAsync(version.Id, cancellationToken);
        var isHistorical = version.Status.Equals("Superseded", StringComparison.OrdinalIgnoreCase);

        return new SourceDocumentResolution
        {
            Document = document,
            Version = version,
            MarkdownExcerpt = CreateSafeExcerpt(version.ContentMarkdown),
            Status = isHistorical
                ? "resolved_historical_source_document_version"
                : "resolved_source_document_version",
            Detail = isHistorical
                ? "Source ProjectDocumentVersion resolved but is marked historical/superseded."
                : "Source ProjectDocumentVersion resolved for builder context.",
            Evidence = links
                .Select(link => $"{link.LinkType}:{link.LinkedEntityType}:{link.LinkedEntityId}")
                .ToArray()
        };
    }

    private static string CreateSafeExcerpt(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        const int maxLength = 1800;
        var normalized = markdown.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "\n\n[Excerpt truncated for builder context smoke.]";
    }

    private sealed class SourceDocumentResolution
    {
        public IronDev.Data.Models.ProjectDocument? Document { get; init; }
        public IronDev.Data.Models.ProjectDocumentVersion? Version { get; init; }
        public string? MarkdownExcerpt { get; init; }
        public string Status { get; init; } = "not_requested";
        public string? Detail { get; init; }
        public IReadOnlyList<string> Evidence { get; init; } = [];
    }

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
