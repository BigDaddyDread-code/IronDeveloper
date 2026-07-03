using System.Text.Json;
using IronDev.Core.Interfaces;
using IronDev.Core.Runs;
using IronDev.Core.RunReports;
using IronDev.Core.Tools;
using IronDev.Core.Workspaces;
using IronDev.Core.Workflow;
using IronDev.Data.Models;
using IronDev.Infrastructure.Tools.CodeStandards;
using Microsoft.Extensions.Configuration;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class TicketBuildRunService : ITicketBuildRunService
{
    private readonly ITicketService _tickets;
    private readonly IProjectService _projects;
    private readonly IDisposableWorkspaceExecutionService _workspaces;
    private readonly IGovernedToolRegistry _tools;
    private readonly IConfiguration _configuration;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly IRunReportService _reports;
    private readonly IRunEvidenceService _evidence;

    public TicketBuildRunService(
        ITicketService tickets,
        IProjectService projects,
        IDisposableWorkspaceExecutionService workspaces,
        IGovernedToolRegistry tools,
        IConfiguration configuration,
        IRunStore runs,
        IRunEventStore events,
        IRunReportService reports,
        IRunEvidenceService evidence)
    {
        _tickets = tickets;
        _projects = projects;
        _workspaces = workspaces;
        _tools = tools;
        _configuration = configuration;
        _runs = runs;
        _events = events;
        _reports = reports;
        _evidence = evidence;
    }

    public async Task<TicketBuildRunDto?> StartDisposableAsync(
        int projectId,
        long ticketId,
        StartTicketBuildRunRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!await TicketBelongsToProjectAsync(projectId, ticketId, cancellationToken).ConfigureAwait(false))
            return null;

        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || project is null)
            return null;

        var run = await _runs.CreateAsync(new CreateRunRequest
        {
            RunId = request?.WorkflowRunId?.ToString("D"),
            ProjectId = projectId,
            TicketId = ticketId,
            IsDisposable = true,
            Summary = $"Disposable ticket build run created for ticket {ticketId}."
        }, cancellationToken).ConfigureAwait(false);

        await PublishAsync(run.RunId, "RunStarted", $"Disposable ticket build run started for ticket {ticketId}.", projectId, ticketId, new Dictionary<string, string>
        {
            ["status"] = RunLifecycleState.Created.ToString(),
            ["currentNode"] = "DisposableWorkspaceExecution"
        }, cancellationToken).ConfigureAwait(false);

        var evidenceRoot = ResolveEvidenceRoot();
        Directory.CreateDirectory(evidenceRoot);

        if (string.IsNullOrWhiteSpace(project.LocalPath) || !Directory.Exists(project.LocalPath))
        {
            var message = "Project local path is not configured or does not exist.";
            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = run.RunId,
                State = RunLifecycleState.Failed,
                Summary = message,
                FailureReason = message
            }, cancellationToken).ConfigureAwait(false);
            await PublishAsync(run.RunId, "RunFailed", message, projectId, ticketId, new Dictionary<string, string>
            {
                ["status"] = RunLifecycleState.Failed.ToString(),
                ["failureReason"] = message
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RunCodeStandardsAsync(run.RunId, projectId, ticket, evidenceRoot, cancellationToken).ConfigureAwait(false);

            await _workspaces.RunAsync(new DisposableWorkspaceRunRequest
            {
                RunId = run.RunId,
                SourcePath = project.LocalPath,
                WorkspaceRoot = ResolveWorkspaceRoot(),
                EvidenceRoot = evidenceRoot,
                CleanWorkspaceOnSuccess = true,
                PreserveWorkspaceOnFailure = true,
                PreserveWorkspaceOnCancellation = true,
                Commands = BuildBackendOwnedCommandProfile(project.LocalPath)
            }, cancellationToken).ConfigureAwait(false);
        }

        var updated = await _runs.GetAsync(run.RunId, cancellationToken).ConfigureAwait(false) ?? run;
        return new TicketBuildRunDto
        {
            RunId = updated.RunId,
            ProjectId = projectId,
            TicketId = ticketId,
            Status = updated.State.ToString(),
            CurrentNode = "DisposableWorkspaceExecution",
            RequiresHumanApproval = updated.State == RunLifecycleState.PausedForApproval,
            Message = updated.FailureReason ?? updated.Summary
        };
    }

    public async Task<IReadOnlyList<TicketBuildRunSummaryDto>?> GetRunsAsync(
        int projectId,
        long ticketId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!await TicketBelongsToProjectAsync(projectId, ticketId, cancellationToken).ConfigureAwait(false))
            return null;

        var runs = new List<TicketBuildRunSummaryDto>();
        var durableRuns = await _runs.GetRecentAsync(take <= 0 ? 50 : take, cancellationToken).ConfigureAwait(false);
        foreach (var run in durableRuns.Where(run =>
            run.ProjectId == projectId &&
            run.TicketId == ticketId))
        {
            var events = await _events.GetEventsAsync(run.RunId, cancellationToken).ConfigureAwait(false);
            runs.Add(ToSummary(run, events));
        }

        return runs;
    }

    public async Task<TicketBuildRunDetailDto?> GetRunAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        if (!await TicketBelongsToProjectAsync(projectId, ticketId, cancellationToken).ConfigureAwait(false))
            return null;

        var run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != projectId || run.TicketId != ticketId)
            return null;

        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        var summary = ToSummary(run, events);
        var report = await _reports.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
        var evidence = report?.Evidence;
        if (evidence is null || evidence.Count == 0)
            evidence = await _evidence.GetEvidenceAsync(runId, cancellationToken).ConfigureAwait(false);

        return new TicketBuildRunDetailDto
        {
            RunId = summary.RunId,
            ProjectId = summary.ProjectId,
            TicketId = summary.TicketId,
            Status = summary.Status,
            CurrentNode = summary.CurrentNode,
            RequiresHumanApproval = summary.RequiresHumanApproval,
            IsDisposable = summary.IsDisposable,
            StartedUtc = summary.StartedUtc,
            CompletedUtc = summary.CompletedUtc,
            Summary = !string.IsNullOrWhiteSpace(report?.Summary) ? report.Summary : summary.Summary,
            FailureReason = summary.FailureReason,
            ReportPath = report?.ReportPath,
            TracePath = report?.TraceId is null ? null : $"trace:{report.TraceId}",
            LogPath = report?.ReportPath,
            Events = events,
            Evidence = evidence
        };
    }

    private async Task<bool> TicketBelongsToProjectAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        return ticket is not null && ticket.ProjectId == projectId;
    }

    private static TicketBuildRunSummaryDto ToSummary(
        RunRecord run,
        IReadOnlyList<RunEventDto> events)
    {
        var first = events.FirstOrDefault();
        var last = events.LastOrDefault();
        var status = run.State.ToString();
        var currentNode = last is null ? string.Empty : ReadPayload(last, "currentNode") ?? ReadPayload(last, "node") ?? string.Empty;
        var failure = events.LastOrDefault(IsFailureEvent)?.Message;

        return new TicketBuildRunSummaryDto
        {
            RunId = run.RunId,
            ProjectId = run.ProjectId ?? 0,
            TicketId = run.TicketId ?? 0,
            Status = status,
            CurrentNode = currentNode,
            RequiresHumanApproval = string.Equals(last?.EventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase) ||
                                    run.State == RunLifecycleState.PausedForApproval,
            IsDisposable = run.IsDisposable || events.Any(IsDisposableRunEvent),
            StartedUtc = run.StartedUtc ?? first?.TimestampUtc,
            CompletedUtc = run.CompletedUtc ?? (last is not null && IsTerminal(last.EventType) ? last.TimestampUtc : null),
            Summary = !string.IsNullOrWhiteSpace(last?.Message)
                ? last.Message
                : string.IsNullOrWhiteSpace(run.Summary) ? $"Run {run.RunId} is {status}." : run.Summary,
            FailureReason = run.FailureReason ?? failure
        };
    }

    private static bool BelongsToTicket(IReadOnlyList<RunEventDto> events, int projectId, long ticketId) =>
        events.Count > 0 &&
        events.Any(runEvent =>
            string.Equals(ReadPayload(runEvent, "projectId"), projectId.ToString(), StringComparison.Ordinal) &&
            string.Equals(ReadPayload(runEvent, "ticketId"), ticketId.ToString(), StringComparison.Ordinal));

    private static bool IsDisposableRunEvent(RunEventDto runEvent) =>
        string.Equals(ReadPayload(runEvent, "disposableRun"), "true", StringComparison.OrdinalIgnoreCase);

    private static string? ReadPayload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool IsFailureEvent(RunEventDto runEvent) =>
        string.Equals(runEvent.EventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(runEvent.EventType, "Error", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(string eventType) =>
        string.Equals(eventType, "RunCompleted", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase);

    private async Task RunCodeStandardsAsync(
        string runId,
        int projectId,
        ProjectTicket ticket,
        string evidenceRoot,
        CancellationToken cancellationToken)
    {
        var result = await _tools.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            new GovernedToolRequest<CodeStandardsAnalysisInput>
            {
                RequestId = $"ticket-disposable-code-standards-{runId}",
                ToolName = CodeStandardsAnalysisTool.ToolName,
                RequestedBy = "BuilderAgent",
                Input = new CodeStandardsAnalysisInput
                {
                    PatchText = BuildTicketContextPacket(ticket),
                    ChangedFiles =
                    [
                        new CodeStandardsChangedFile
                        {
                            Path = $"ticket-{ticket.Id}.md",
                            Content = BuildTicketContextPacket(ticket)
                        }
                    ]
                },
                Reason = "Run read-only code standards gate before disposable ticket build execution."
            },
            cancellationToken).ConfigureAwait(false);

        var runEvidenceRoot = Path.Combine(evidenceRoot, runId, "evidence");
        Directory.CreateDirectory(runEvidenceRoot);
        var evidencePath = Path.Combine(runEvidenceRoot, "code-standards.json");
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        }), cancellationToken).ConfigureAwait(false);

        await PublishAsync(runId, "CodeStandardsCompleted", result.Summary, projectId, ticket.Id, new Dictionary<string, string>
        {
            ["status"] = result.Status.ToString(),
            ["toolName"] = CodeStandardsAnalysisTool.ToolName,
            ["evidencePath"] = evidencePath
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildTicketContextPacket(ProjectTicket ticket) =>
        string.Join(Environment.NewLine, new[]
        {
            $"# Ticket {ticket.Id}: {ticket.Title}",
            ticket.Summary,
            ticket.Problem,
            ticket.AcceptanceCriteria,
            ticket.TechnicalNotes
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private IReadOnlyList<DisposableWorkspaceCommand> BuildBackendOwnedCommandProfile(string projectPath) =>
        Workspaces.DotNetCommandProfile.BuildAndTest(
            projectPath,
            ReadTimeoutSeconds("BuildTimeoutSeconds", 120),
            ReadTimeoutSeconds("TestTimeoutSeconds", 120));

    private int ReadTimeoutSeconds(string key, int fallback)
    {
        var value = _configuration[$"DisposableBuild:{key}"];
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private string ResolveWorkspaceRoot()
    {
        var configured = _configuration["DisposableBuild:WorkspaceRoot"] ?? _configuration["LocalTest:WorkspaceRoot"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableWorkspaces")
            : configured;
    }

    private string ResolveEvidenceRoot()
    {
        var configured = _configuration["DisposableBuild:EvidenceRoot"] ?? _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "runs");
    }

    private Task PublishAsync(
        string runId,
        string eventType,
        string message,
        int projectId,
        long ticketId,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, string>(payload, StringComparer.OrdinalIgnoreCase)
        {
            ["projectId"] = projectId.ToString(),
            ["ticketId"] = ticketId.ToString(),
            ["disposableRun"] = "true"
        };

        return _events.PublishAsync(new RunEventDto
        {
            RunId = runId,
            EventType = eventType,
            Message = message,
            Payload = merged
        }, cancellationToken);
    }
}
