using System.Text;
using IronDev.Core.Runs;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private readonly IChatHistoryService _chat;
    private readonly IChatFeedbackService _feedback;
    private readonly IProjectService _projects;
    private readonly ITicketService _tickets;
    private readonly IProjectMemoryService _memory;
    private readonly IRunStore _runs;

    public ChatController(
        IChatHistoryService chat,
        IChatFeedbackService feedback,
        IProjectService projects,
        ITicketService tickets,
        IProjectMemoryService memory,
        IRunStore runs)
    {
        _chat = chat;
        _feedback = feedback;
        _projects = projects;
        _tickets = tickets;
        _memory = memory;
        _runs = runs;
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions")]
    public Task<IReadOnlyList<ProjectChatSession>> GetSessions(int projectId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        _chat.GetRecentSessionsAsync(projectId, take, ct);

    [HttpGet("api/chat/sessions/{sessionId:long}")]
    public Task<ProjectChatSession?> GetSession(long sessionId, CancellationToken ct) =>
        _chat.GetSessionByIdAsync(sessionId, ct);

    [HttpPost("api/projects/{projectId:int}/chat/sessions")]
    public Task<long> SaveSession(ProjectChatSession session, CancellationToken ct) =>
        _chat.SaveSessionAsync(session, ct);

    [HttpDelete("api/chat/sessions/{sessionId:long}")]
    public async Task<IActionResult> DeleteSession(long sessionId, CancellationToken ct)
    {
        await _chat.DeleteSessionAsync(sessionId, ct);
        return NoContent();
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public Task<IReadOnlyList<ChatMessage>> GetMessages(int projectId, long sessionId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        _chat.GetRecentMessagesAsync(projectId, sessionId, take, ct);

    [HttpPost("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public Task<long> SaveMessage(ChatMessage message, CancellationToken ct) =>
        _chat.SaveMessageAsync(message, ct);

    [HttpPost("api/projects/{projectId:int}/chat/complete")]
    public async Task<ActionResult<ChatCompletionResponse>> Complete(
        int projectId,
        ChatCompletionRequest request,
        CancellationToken ct)
    {
        if (request.ProjectId != 0 && request.ProjectId != projectId)
            return BadRequest(new { message = "Request project id must match the route project id." });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "Prompt is required." });

        var project = await _projects.GetByIdAsync(projectId, ct);
        if (project is null)
            return NotFound();

        var summary = await _memory.GetLatestSummaryAsync(projectId, ct);
        var tickets = await _tickets.GetRecentTicketsAsync(projectId, 5, ct);
        var decisions = await _memory.GetRecentDecisionsAsync(projectId, 5, ct);
        var documents = await _memory.GetContextDocumentsAsync(projectId, status: "Active", take: 5, cancellationToken: ct);
        var runs = (await _runs.GetRecentAsync(25, ct))
            .Where(run => run.ProjectId == projectId)
            .OrderByDescending(run => run.UpdatedUtc)
            .Take(5)
            .ToList();

        var risks = BuildRisks(tickets, decisions, runs);
        var actions = BuildRecommendedActions(tickets, decisions, runs);
        var linkedFiles = DistinctDelimited(tickets.Select(t => t.LinkedFilePaths).Concat(decisions.Select(d => d.LinkedFilePaths)));
        var linkedSymbols = DistinctDelimited(tickets.Select(t => t.LinkedSymbols).Concat(decisions.Select(d => d.LinkedSymbols)));

        var response = BuildProjectStateResponse(
            project,
            summary,
            tickets,
            decisions,
            documents,
            runs,
            risks,
            actions);

        var contextSummary =
            $"Grounded from {tickets.Count} ticket(s), {decisions.Count} decision(s), {documents.Count} knowledge document(s), and {runs.Count} run(s).";

        return Ok(new ChatCompletionResponse(
            response,
            contextSummary,
            string.Join(Environment.NewLine, linkedFiles),
            string.Join(Environment.NewLine, linkedSymbols),
            null));
    }

    public sealed record ChatCompletionRequest(int ProjectId, long? SessionId, string Prompt, string? ActiveModel);
    public sealed record ChatCompletionResponse(string Response, string? ContextSummary, string? LinkedFilePaths, string? LinkedSymbols, long? TraceId);

    [HttpPost("api/projects/{projectId:int}/chat/feedback")]
    public Task<long> SaveFeedback(ChatMessageFeedback feedback, CancellationToken ct) =>
        _feedback.SaveFeedbackAsync(feedback, ct);

    private static string BuildProjectStateResponse(
        Project project,
        ProjectSummary? summary,
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<ProjectDecision> decisions,
        IReadOnlyList<ProjectContextDocument> documents,
        IReadOnlyList<RunRecord> runs,
        IReadOnlyList<string> risks,
        IReadOnlyList<string> actions)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Project state: {project.Name}");

        if (!string.IsNullOrWhiteSpace(project.Description))
            sb.AppendLine($"Description: {project.Description}");

        if (!string.IsNullOrWhiteSpace(summary?.Summary))
            sb.AppendLine($"Current summary: {summary.Summary}");

        sb.AppendLine();
        sb.AppendLine("Recent tickets:");
        AppendItems(sb, tickets, ticket =>
            $"#{ticket.Id} {ticket.Title} ({ticket.Status}, {ticket.Priority}){FormatSuffix(ticket.Summary)}");

        sb.AppendLine();
        sb.AppendLine("Recent decisions:");
        AppendItems(sb, decisions, decision =>
            $"{decision.Title} ({decision.Status}){FormatSuffix(decision.Detail)}");

        sb.AppendLine();
        sb.AppendLine("Recent runs:");
        AppendItems(sb, runs, run =>
            $"{run.RunId} ({run.State}){FormatSuffix(run.Summary)}");

        sb.AppendLine();
        sb.AppendLine("Context used:");
        AppendItems(sb, documents, document =>
            $"{document.Title} ({document.DocumentType}, {document.AuthorityLevel}){FormatSuffix(document.Summary)}");

        sb.AppendLine();
        sb.AppendLine("Risks and blockers:");
        AppendItems(sb, risks, risk => risk);

        sb.AppendLine();
        sb.AppendLine("Recommended next actions:");
        AppendItems(sb, actions, action => action);

        return sb.ToString().Trim();
    }

    private static IReadOnlyList<string> BuildRisks(
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<ProjectDecision> decisions,
        IReadOnlyList<RunRecord> runs)
    {
        var risks = new List<string>();

        foreach (var ticket in tickets.Where(ticket =>
                     IsOneOf(ticket.Priority, "High", "Critical") ||
                     ContainsAny(ticket.Status, "blocked", "failed")))
        {
            risks.Add($"Ticket #{ticket.Id} needs attention: {ticket.Title} ({ticket.Status}, {ticket.Priority}).");
        }

        foreach (var run in runs.Where(run => run.State == RunLifecycleState.Failed))
        {
            risks.Add($"Run {run.RunId} failed{FormatSuffix(run.FailureReason)}");
        }

        if (decisions.Count == 0)
            risks.Add("No recent decisions were found for this project.");

        if (runs.Count == 0)
            risks.Add("No recent build or sandbox run evidence was found.");

        return risks.Count == 0
            ? ["No immediate blockers were found in recent project evidence."]
            : risks;
    }

    private static IReadOnlyList<string> BuildRecommendedActions(
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<ProjectDecision> decisions,
        IReadOnlyList<RunRecord> runs)
    {
        var actions = new List<string>();
        var failedRun = runs.FirstOrDefault(run => run.State == RunLifecycleState.Failed);
        var pausedRun = runs.FirstOrDefault(run => run.State == RunLifecycleState.PausedForApproval);
        var activeTicket = tickets.FirstOrDefault(ticket => !ContainsAny(ticket.Status, "done", "closed", "archived"));

        if (failedRun is not null)
            actions.Add($"Review failed run {failedRun.RunId} and inspect its evidence before starting another sandbox run.");

        if (pausedRun is not null)
            actions.Add($"Review run {pausedRun.RunId}; it is paused for human approval.");

        if (activeTicket is not null)
            actions.Add($"Open ticket #{activeTicket.Id} ({activeTicket.Title}) and check Build Readiness.");

        if (decisions.Count == 0)
            actions.Add("Capture any current architecture or product decisions before generating more work.");

        if (actions.Count == 0)
            actions.Add("Select the next ticket, refresh Build Readiness, then start a sandbox run when the evidence is sufficient.");

        return actions;
    }

    private static void AppendItems<T>(StringBuilder sb, IReadOnlyCollection<T> items, Func<T, string> format)
    {
        if (items.Count == 0)
        {
            sb.AppendLine("- None found.");
            return;
        }

        foreach (var item in items)
            sb.AppendLine($"- {format(item)}");
    }

    private static string FormatSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return $": {normalized}";
    }

    private static bool IsOneOf(string? value, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

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
