using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public class ProjectContextExportService : IProjectContextExportService
{
    private readonly IProjectService _projectService;
    private readonly IProjectProfileService _profileService;
    private readonly IProjectMemoryService _memoryService;
    private readonly ITicketService _ticketService;
    private readonly ICodeIndexService _codeIndexService;

    public ProjectContextExportService(
        IProjectService projectService,
        IProjectProfileService profileService,
        IProjectMemoryService memoryService,
        ITicketService ticketService,
        ICodeIndexService codeIndexService)
    {
        _projectService = projectService;
        _profileService = profileService;
        _memoryService = memoryService;
        _ticketService = ticketService;
        _codeIndexService = codeIndexService;
    }

    public async Task<string> ExportProjectContextPackAsync(int projectId)
    {
        var project = await _projectService.GetByIdAsync(projectId);
        if (project == null) return "# Project Not Found";

        var profile = await _profileService.GetProjectProfileAsync(projectId);
        var commands = await _profileService.GetProjectCommandsAsync(projectId);
        var decisions = await _memoryService.GetRecentDecisionsAsync(projectId, 100);
        var latestSummary = await _memoryService.GetLatestSummaryAsync(projectId);
        var rules = await _memoryService.GetProjectRulesAsync(projectId);
        var tickets = await _ticketService.GetRecentTicketsAsync(projectId, 100);
        var plans = await _memoryService.GetRecentPlansAsync(projectId, 100);
        var indexedFiles = await _codeIndexService.GetIndexedFileCountAsync(projectId);

        var sb = new StringBuilder();
        sb.AppendLine($"# Project Context Pack: {project.Name}");
        sb.AppendLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        sb.AppendLine("## Project Details");
        sb.AppendLine($"- **Project ID:** {project.Id}");
        sb.AppendLine($"- **Path:** {Scrub(project.LocalPath)}");
        sb.AppendLine($"- **Index Status:** {project.IndexingStatus ?? "Ready"}");
        sb.AppendLine($"- **Indexed Files:** {indexedFiles}");
        sb.AppendLine();

        if (profile != null)
        {
            sb.AppendLine("## Project Profile");
            sb.AppendLine($"- **App Type:** {profile.ApplicationType}");
            sb.AppendLine($"- **Primary Language:** {profile.PrimaryLanguage}");
            sb.AppendLine($"- **Framework:** {profile.Framework} ({profile.RuntimeVersion})");
            sb.AppendLine($"- **Test Framework:** {profile.TestFramework}");
            if (!string.IsNullOrEmpty(profile.ProfileNotes))
            {
                sb.AppendLine();
                sb.AppendLine("### Profile Notes");
                sb.AppendLine(Scrub(profile.ProfileNotes));
            }
            sb.AppendLine();
        }

        if (commands.Any())
        {
            sb.AppendLine("## Project Commands");
            foreach (var cmd in commands.Where(c => c.IsEnabled))
            {
                sb.AppendLine($"- **{cmd.CommandType}:** `{Scrub(cmd.CommandText)}` (Default: {cmd.IsDefault})");
            }
            sb.AppendLine();
        }

        if (latestSummary != null)
        {
            sb.AppendLine("## Latest Product Summary");
            sb.AppendLine(Scrub(latestSummary.Summary));
            sb.AppendLine();
        }

        if (rules.Any())
        {
            sb.AppendLine("## Project Standards & Rules");
            foreach (var r in rules)
            {
                sb.AppendLine($"### {r.Name} ({r.Type})");
                sb.AppendLine($"- **Level:** {r.EnforcementLevel}");
                sb.AppendLine($"- **Applies To:** {r.AppliesTo}");
                sb.AppendLine();
                sb.AppendLine(Scrub(r.Description));
                if (!string.IsNullOrEmpty(r.ValidationHint))
                {
                    sb.AppendLine();
                    sb.AppendLine($"*Hint: {Scrub(r.ValidationHint)}*");
                }
                sb.AppendLine();
            }
        }

        if (decisions.Any())
        {
            sb.AppendLine("## Architecture Decisions");
            foreach (var d in decisions)
            {
                sb.AppendLine($"### {d.Title} ({d.Status})");
                sb.AppendLine($"- **Category:** {d.Category}");
                sb.AppendLine($"- **Date:** {d.CreatedDate:yyyy-MM-dd}");
                sb.AppendLine();
                sb.AppendLine("#### Detail");
                sb.AppendLine(Scrub(d.Detail));
                sb.AppendLine();
                sb.AppendLine("#### Reason");
                sb.AppendLine(Scrub(d.Reason));
                sb.AppendLine();
            }
        }

        if (tickets.Any())
        {
            sb.AppendLine("## Recent Tickets");
            foreach (var t in tickets)
            {
                sb.AppendLine($"### [{t.Status}] {t.Title}");
                sb.AppendLine($"- **Type:** {t.TicketType}");
                sb.AppendLine($"- **Priority:** {t.Priority}");
                sb.AppendLine();
                sb.AppendLine(Scrub(t.Summary));
                sb.AppendLine();
                
                var ticketPlan = plans.FirstOrDefault(p => p.TicketId == t.Id);
                if (ticketPlan != null)
                {
                    sb.AppendLine("#### Implementation Plan");
                    sb.AppendLine($"- **Plan Status:** {ticketPlan.Status}");
                    sb.AppendLine($"- **Goal:** {Scrub(ticketPlan.Goal)}");
                    sb.AppendLine();
                    sb.AppendLine("##### Steps");
                    sb.AppendLine(Scrub(ticketPlan.ProposedSteps));
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    private string Scrub(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Basic scrubbing for common secret patterns
        // Replace potential API keys or secrets with [REDACTED]
        // This is a simplified version; in production, use a more robust regex or scanning library.
        
        var scrubbed = input;

        // Scrub potential GUIDs/UUIDs that look like keys
        scrubbed = Regex.Replace(scrubbed, @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}", "[REDACTED-ID]");
        
        // Scrub common assignment patterns: key=..., secret=..., password=...
        string[] sensitivePatterns = { "api[_-]?key", "secret", "password", "token", "auth", "credential" };
        foreach (var pattern in sensitivePatterns)
        {
            // Match pattern followed by whitespace, colon, or equals, then the value
            // This is a naive regex but helps catch common cases
            scrubbed = Regex.Replace(scrubbed, $@"({pattern})\s*[:=]\s*[^\s,;]+", "$1: [REDACTED]", RegexOptions.IgnoreCase);
        }

        return scrubbed;
    }
}
