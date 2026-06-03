using System.Text;
using IronDev.Core.Runs;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public interface IProjectChatResponseService
{
    Task<ProjectChatResponseResult?> RespondAsync(
        int projectId,
        string prompt,
        ProjectConversationMode mode = ProjectConversationMode.Exploration,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectChatResponseResult(
    string Response,
    string ContextSummary,
    string LinkedFilePaths,
    string LinkedSymbols,
    string Mode,
    bool ShowGovernanceActions,
    IReadOnlyList<string> GovernanceActions,
    IReadOnlyList<string> ReasoningTrace,
    string? DisambiguationQuestion,
    string? ReasoningSummary,
    string? DogfoodTraceId = null,
    string? DogfoodTracePath = null,
    long? TraceId = null);

public enum ProjectConversationMode
{
    Exploration,
    Formalization,
    Confirmation
}

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
        ProjectConversationMode mode = ProjectConversationMode.Exploration,
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

        var normalizedPrompt = prompt.ReplaceLineEndings(" ").Trim();
        var response = BuildResponse(project, normalizedPrompt, mode, tickets, decisions, documents, runs);
        var contextSummary =
            $"Answered from prompt plus project context: {tickets.Count} ticket(s), {decisions.Count} decision(s), {documents.Count} knowledge document(s), {runs.Count} run(s).";
        var linkedFiles = DistinctDelimited(tickets.Select(t => t.LinkedFilePaths).Concat(decisions.Select(d => d.LinkedFilePaths)));
        var linkedSymbols = DistinctDelimited(tickets.Select(t => t.LinkedSymbols).Concat(decisions.Select(d => d.LinkedSymbols)));
        var reasoningTrace = BuildReasoningTrace(mode, normalizedPrompt, project, tickets, decisions, documents, runs);

        var governanceActions = mode == ProjectConversationMode.Formalization
            ? new[]
            {
                "Save this response as a Discussion.",
                "Create a Ticket from the saved Discussion."
            }
            : Array.Empty<string>();

        var disambiguationQuestion = mode == ProjectConversationMode.Confirmation
            ? "That mixes exploration and commitment language. Do you want exploration reasoning or formalization handoff first?"
            : null;

        return new ProjectChatResponseResult(
            response,
            contextSummary,
            string.Join(Environment.NewLine, linkedFiles),
            string.Join(Environment.NewLine, linkedSymbols),
            mode.ToString(),
            mode == ProjectConversationMode.Formalization,
            governanceActions,
            reasoningTrace,
            disambiguationQuestion,
            BuildReasoningSummary(mode, reasoningTrace));
    }

    private static string BuildResponse(
        Project project,
        string prompt,
        ProjectConversationMode mode,
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<ProjectDecision> decisions,
        IReadOnlyList<ProjectContextDocument> documents,
        IReadOnlyList<RunRecord> runs)
    {
        var lower = prompt.ToLowerInvariant();
        var sb = new StringBuilder();

        if (mode == ProjectConversationMode.Formalization)
        {
            sb.AppendLine("## Formalization mode");
            sb.AppendLine();
            sb.AppendLine($"Objective draft: {prompt}");
            sb.AppendLine();
            sb.AppendLine("Delivery-first framing:");
            sb.AppendLine("- Keep scope to one verifiable behavior slice.");
            sb.AppendLine("- Define acceptance criteria before build.");
            sb.AppendLine("- Include failure and verification intent, not only happy-path behavior.");
            sb.AppendLine();
            sb.AppendLine("Suggested handoff artifacts:");
            sb.AppendLine("- Save this response as a Discussion.");
            sb.AppendLine("- Create a Ticket from the Discussion.");
            sb.AppendLine("- Move into Build only after Build Readiness indicates green review gates.");
        }
        else if (mode == ProjectConversationMode.Confirmation)
        {
            sb.AppendLine("## Clarification gate");
            sb.AppendLine();
            sb.AppendLine($"You asked: {prompt}");
            sb.AppendLine();
            sb.AppendLine("You are currently sending both exploration and commitment signals.");
            sb.AppendLine("- Exploration lane: unpack assumptions, options, trade-offs, risks.");
            sb.AppendLine("- Formalization lane: capture a handoff-ready plan and handoff actions.");
            sb.AppendLine();
            sb.AppendLine("Tell me the lane you want me to lock into.");
        }
        else
        {
            sb.AppendLine("## Exploration mode");
            sb.AppendLine();
            sb.AppendLine($"You asked: {prompt}");
            sb.AppendLine();
            sb.AppendLine("I am treating this as open reasoning mode and will keep the chain explicit.");
            sb.AppendLine();
            sb.AppendLine("### Inferred options");
            sb.AppendLine("- Option A: clarify scope and constraints first.");
            sb.AppendLine("- Option B: compare implementation approaches.");
            sb.AppendLine("- Option C: keep it lightweight and run only after a readiness gate.");

            if (ContainsAny(lower, "minesweeper", "mine sweeper"))
            {
                sb.AppendLine();
                sb.AppendLine("Minesweeper-specific angles:");
                sb.AppendLine("- Rule semantics (first-click behavior, win/lose condition, deterministic state transitions).");
                sb.AppendLine("- UX edge cases (reveal propagation, flag interactions, repeated clicks).");
                sb.AppendLine("- Boundary risk (performance on large boards, local deterministic randomness).");
            }

            sb.AppendLine();
            sb.AppendLine("### Risks / assumptions surfaced");
            sb.AppendLine("- Storage model and persistence assumptions are not selected yet.");
            sb.AppendLine("- Testability strategy and failure expectations are not yet fixed.");
            sb.AppendLine("- Build command profile is not selected until this is formalized.");
            sb.AppendLine();
            sb.AppendLine("### Next choice");
            sb.AppendLine("- If you want an explicit handoff, say \"make this a ticket\".");
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

    private static IReadOnlyList<string> BuildReasoningTrace(
        ProjectConversationMode mode,
        string prompt,
        Project project,
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<ProjectDecision> decisions,
        IReadOnlyList<ProjectContextDocument> documents,
        IReadOnlyList<RunRecord> runs)
    {
        var trace = new List<string>
        {
            "Prompt normalized and mode selected.",
            $"Project: {project.Name}.",
            $"Context: tickets={tickets.Count}, decisions={decisions.Count}, documents={documents.Count}, runs={runs.Count}.",
            $"Prompt length: {prompt.Length} chars."
        };

        if (mode == ProjectConversationMode.Formalization)
        {
            trace.Add("Formalization selected: returning handoff-friendly text and Governance action suggestions.");
        }
        else if (mode == ProjectConversationMode.Confirmation)
        {
            trace.Add("Mixed intent detected: confirmation prompt requires one explicit lane.");
            trace.Add("No governance action is auto-enabled before user confirms lane.");
        }
        else
        {
            trace.Add("Exploration selected: no direct governance actions are surfaced yet.");
        }

        if (ContainsAny(project.Description, "risk", "regulatory", "security", "safety"))
            trace.Add("Project description includes safety-risk vocabulary; conservative posture retained.");

        if (decisions.Count == 0)
            trace.Add("No recent project decisions were found to lock policy constraints.");

        return trace;
    }

    private static string BuildReasoningSummary(ProjectConversationMode mode, IReadOnlyList<string> trace)
    {
        var reason = mode switch
        {
            ProjectConversationMode.Formalization => "Formalization selected; handoff actions are available.",
            ProjectConversationMode.Confirmation => "Intent ambiguous; user confirmation required before governance actions.",
            _ => "Exploration selected; reasoning stays open and no governance actions are auto-exposed."
        };

        return $"{reason} Trace entries: {trace.Count}.";
    }
}
