using System.Text;
using IronDev.Core.Runs;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public interface IProjectChatResponseService
{
    Task<ProjectChatResponseResult?> RespondAsync(int projectId, string prompt, CancellationToken cancellationToken = default);
}

public sealed record ProjectChatResponseResult(
    string Response,
    string ContextSummary,
    string LinkedFilePaths,
    string LinkedSymbols);

public sealed class ProjectChatResponseService : IProjectChatResponseService
{
    private readonly IProjectService _projects;
    private readonly ITicketService _tickets;
    private readonly IProjectMemoryService _memory;
    private readonly IRunStore _runs;

    public ProjectChatResponseService(
        IProjectService projects,
        ITicketService tickets,
        IProjectMemoryService memory,
        IRunStore runs)
    {
        _projects = projects;
        _tickets = tickets;
        _memory = memory;
        _runs = runs;
    }

    public async Task<ProjectChatResponseResult?> RespondAsync(
        int projectId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var tickets = await _tickets.GetRecentTicketsAsync(projectId, 5, cancellationToken).ConfigureAwait(false);
        var decisions = await _memory.GetRecentDecisionsAsync(projectId, 5, cancellationToken).ConfigureAwait(false);
        var documents = await _memory.GetContextDocumentsAsync(projectId, status: "Active", take: 5, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runs = (await _runs.GetRecentAsync(25, cancellationToken).ConfigureAwait(false))
            .Where(run => run.ProjectId == projectId)
            .OrderByDescending(run => run.UpdatedUtc)
            .Take(5)
            .ToList();

        var response = BuildResponse(project, prompt, tickets, decisions, documents, runs);
        var contextSummary = $"Answered from prompt plus project context: {tickets.Count} ticket(s), {decisions.Count} decision(s), {documents.Count} knowledge document(s), {runs.Count} run(s).";
        var linkedFiles = DistinctDelimited(tickets.Select(t => t.LinkedFilePaths).Concat(decisions.Select(d => d.LinkedFilePaths)));
        var linkedSymbols = DistinctDelimited(tickets.Select(t => t.LinkedSymbols).Concat(decisions.Select(d => d.LinkedSymbols)));

        return new ProjectChatResponseResult(
            response,
            contextSummary,
            string.Join(Environment.NewLine, linkedFiles),
            string.Join(Environment.NewLine, linkedSymbols));
    }

    private static string BuildResponse(
        Project project,
        string prompt,
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<ProjectDecision> decisions,
        IReadOnlyList<ProjectContextDocument> documents,
        IReadOnlyList<RunRecord> runs)
    {
        var normalized = prompt.ReplaceLineEndings(" ").Trim();
        var lower = normalized.ToLowerInvariant();
        var sb = new StringBuilder();

        if (ContainsAny(lower, "minesweeper", "mine sweeper"))
        {
            sb.AppendLine("I can turn this into buildable work.");
            sb.AppendLine();
            sb.AppendLine("Suggested first slice:");
            sb.AppendLine("- Create a basic Minesweeper game with a grid, mine placement, reveal logic, flagging, win/loss detection, and a simple UI.");
            sb.AppendLine();
            sb.AppendLine("Acceptance shape:");
            sb.AppendLine("- The game creates a minefield with a predictable width, height, and mine count.");
            sb.AppendLine("- A player can reveal cells and flag suspected mines.");
            sb.AppendLine("- Revealing a mine ends the game.");
            sb.AppendLine("- Revealing all safe cells wins the game.");
            sb.AppendLine("- The first implementation should stay small enough for a sandbox run and review package.");
            sb.AppendLine();
            sb.AppendLine("Next useful actions:");
            sb.AppendLine("- Save this as a Discussion.");
            sb.AppendLine("- Create a Ticket from the saved discussion when you are happy with the slice.");
            sb.AppendLine("- Use Build only after there is a ticket or buildable plan.");
        }
        else if (ContainsAny(lower, "build me", "create", "make", "implement", "add"))
        {
            sb.AppendLine("I can shape this into buildable work.");
            sb.AppendLine();
            sb.AppendLine($"Discussion summary: {normalized}");
            sb.AppendLine();
            sb.AppendLine("Suggested first slice:");
            sb.AppendLine("- Define the smallest useful behaviour.");
            sb.AppendLine("- Capture acceptance criteria before starting execution.");
            sb.AppendLine("- Keep the first sandbox run narrow enough to review clearly.");
            sb.AppendLine();
            sb.AppendLine("Next useful actions:");
            sb.AppendLine("- Save this as a Discussion.");
            sb.AppendLine("- Create a Ticket from the saved discussion.");
            sb.AppendLine("- Review Build Readiness before starting a sandbox run.");
        }
        else
        {
            sb.AppendLine("I can help shape that into project work.");
            sb.AppendLine();
            sb.AppendLine($"You asked: {normalized}");
            sb.AppendLine();
            sb.AppendLine("A good next step is to turn the idea into a saved Discussion, then decide whether it should become a Ticket, Document, or Decision.");
        }

        sb.AppendLine();
        sb.AppendLine("Project context:");
        sb.AppendLine($"- Project: {project.Name}");
        AppendRecentTicketHint(sb, tickets);
        AppendRecentRunHint(sb, runs);
        if (decisions.Count == 0 && documents.Count == 0)
            sb.AppendLine("- No recent decisions or knowledge documents were found for this answer.");

        return sb.ToString().Trim();
    }

    private static void AppendRecentTicketHint(StringBuilder sb, IReadOnlyList<ProjectTicket> tickets)
    {
        var active = tickets.FirstOrDefault(ticket => !ContainsAny(ticket.Status, "done", "closed", "archived"));
        if (active is null)
            return;

        sb.AppendLine($"- Nearest active ticket: #{active.Id} {active.Title} ({active.Status}).");
    }

    private static void AppendRecentRunHint(StringBuilder sb, IReadOnlyList<RunRecord> runs)
    {
        var latest = runs.FirstOrDefault();
        if (latest is null)
            return;

        sb.AppendLine($"- Latest run: {latest.RunId} ({latest.State}).");
    }

    private static bool ContainsAny(string? value, params string[] needles) =>
        !string.IsNullOrWhiteSpace(value) &&
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> DistinctDelimited(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(['\r', '\n', ';', '|', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
