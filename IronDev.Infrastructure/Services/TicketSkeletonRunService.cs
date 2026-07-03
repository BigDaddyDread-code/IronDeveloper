using System.Text.Json;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.Workflow;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.Workspaces;
using Microsoft.Extensions.Configuration;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P0-1 walking-skeleton orchestrator. Chains existing governed services into one run:
/// readiness (enforced inside proposal generation) → proposal (persisted as evidence
/// with an id) → disposable workspace → declarative file writes in-workspace →
/// dotnet build + dotnet test → evidence packaged.
///
/// Boundary: no new authority — composition only. Blocked states are explicit and
/// terminal for the run; there is no approval shortcut because there is no approval
/// surface here at all. The run ends at "evidence packaged" — critic review, human
/// approval, and any apply remain separate governed steps.
/// </summary>
public sealed class TicketSkeletonRunService : ITicketSkeletonRunService
{
    private readonly ITicketService _tickets;
    private readonly IProjectService _projects;
    private readonly IBuilderProposalService _proposals;
    private readonly IDisposableWorkspaceExecutionService _workspaces;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly IConfiguration _configuration;

    public TicketSkeletonRunService(
        ITicketService tickets,
        IProjectService projects,
        IBuilderProposalService proposals,
        IDisposableWorkspaceExecutionService workspaces,
        IRunStore runs,
        IRunEventStore events,
        IConfiguration configuration)
    {
        _tickets = tickets;
        _projects = projects;
        _proposals = proposals;
        _workspaces = workspaces;
        _runs = runs;
        _events = events;
        _configuration = configuration;
    }

    public async Task<TicketBuildRunDto?> StartAsync(int projectId, long ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var run = await _runs.CreateAsync(new CreateRunRequest
        {
            ProjectId = projectId,
            TicketId = ticketId,
            IsDisposable = true,
            Summary = $"Skeleton run created for ticket {ticketId}."
        }, cancellationToken).ConfigureAwait(false);

        await PublishAsync(run.RunId, "RunStarted", $"Skeleton run started for ticket {ticketId}.", projectId, ticketId, new Dictionary<string, string>
        {
            ["status"] = RunLifecycleState.Created.ToString(),
            ["currentNode"] = "SkeletonRun"
        }, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(project.LocalPath) || !Directory.Exists(project.LocalPath))
        {
            return await BlockAsync(run.RunId, projectId, ticketId,
                "ProjectPathMissing",
                "Project local path is not configured or does not exist.",
                "Configure the project's local path, then start a new skeleton run.",
                cancellationToken).ConfigureAwait(false);
        }

        // Gate stays at its owning step: readiness is evaluated (and enforced) inside
        // proposal generation. A readiness block becomes an explicit blocked run state.
        BuilderProposal proposal;
        try
        {
            proposal = await _proposals.GenerateProposalAsync(ticketId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await BlockAsync(run.RunId, projectId, ticketId,
                "ReadinessBlocked",
                exception.Message,
                "Resolve the blocking issues on the ticket, then start a new skeleton run.",
                cancellationToken).ConfigureAwait(false);
        }

        var fileWrites = MapFileWrites(proposal);
        if (fileWrites.Count == 0)
        {
            return await BlockAsync(run.RunId, projectId, ticketId,
                "ProposalEmpty",
                "The proposal produced no valid file changes to exercise.",
                "Refine the ticket so the builder can propose concrete file changes, then start a new skeleton run.",
                cancellationToken).ConfigureAwait(false);
        }

        var evidenceRoot = ResolveEvidenceRoot();
        var proposalId = await PersistProposalEvidenceAsync(run.RunId, evidenceRoot, proposal, cancellationToken).ConfigureAwait(false);

        await PublishAsync(run.RunId, "ProposalGenerated", $"Proposal {proposalId} generated with {fileWrites.Count} file change(s).", projectId, ticketId, new Dictionary<string, string>
        {
            ["proposalId"] = proposalId,
            ["fileChangeCount"] = fileWrites.Count.ToString(),
            ["currentNode"] = "SkeletonRun"
        }, cancellationToken).ConfigureAwait(false);

        var workspaceResult = await _workspaces.RunAsync(new DisposableWorkspaceRunRequest
        {
            RunId = run.RunId,
            SourcePath = project.LocalPath,
            WorkspaceRoot = ResolveWorkspaceRoot(),
            EvidenceRoot = evidenceRoot,
            CleanWorkspaceOnSuccess = true,
            PreserveWorkspaceOnFailure = true,
            PreserveWorkspaceOnCancellation = true,
            FileWrites = fileWrites,
            Commands = DotNetCommandProfile.BuildAndTest(project.LocalPath, ReadTimeout("BuildTimeoutSeconds"), ReadTimeout("TestTimeoutSeconds"))
        }, cancellationToken).ConfigureAwait(false);

        await PublishAsync(run.RunId, "SkeletonEvidencePackaged",
            "Evidence packaged. This run grants nothing: critic review, human approval, and any apply remain separate governed steps.",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["proposalId"] = proposalId,
                ["succeeded"] = workspaceResult.Succeeded.ToString().ToLowerInvariant(),
                ["evidencePath"] = workspaceResult.EvidencePath,
                ["commandCount"] = workspaceResult.Commands.Count.ToString(),
                ["currentNode"] = "SkeletonRun"
            }, cancellationToken).ConfigureAwait(false);

        var updated = await _runs.GetAsync(run.RunId, cancellationToken).ConfigureAwait(false) ?? run;
        return ToDto(updated, projectId, ticketId);
    }

    private async Task<TicketBuildRunDto> BlockAsync(
        string runId,
        int projectId,
        long ticketId,
        string blockedReason,
        string message,
        string nextSafeAction,
        CancellationToken cancellationToken)
    {
        var summary = $"Blocked: {blockedReason} — {message}";

        await _runs.TransitionAsync(new RunStateTransition
        {
            RunId = runId,
            State = RunLifecycleState.Failed,
            Summary = summary,
            FailureReason = summary
        }, cancellationToken).ConfigureAwait(false);

        await PublishAsync(runId, "SkeletonRunBlocked", summary, projectId, ticketId, new Dictionary<string, string>
        {
            ["status"] = RunLifecycleState.Failed.ToString(),
            ["blockedReason"] = blockedReason,
            ["nextSafeAction"] = nextSafeAction,
            ["currentNode"] = "SkeletonRun"
        }, cancellationToken).ConfigureAwait(false);

        var updated = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        return ToDto(updated ?? new RunRecord { RunId = runId, State = RunLifecycleState.Failed, FailureReason = summary }, projectId, ticketId);
    }

    private static IReadOnlyList<DisposableWorkspaceFileWrite> MapFileWrites(BuilderProposal proposal) =>
        (proposal.Changes ?? [])
            .Where(change => change.IsValid && !string.IsNullOrWhiteSpace(change.FilePath))
            .Where(change => change.IsDeletion || change.FullContentAfter is not null)
            .Select(change => new DisposableWorkspaceFileWrite
            {
                RelativePath = change.FilePath,
                Content = change.FullContentAfter,
                IsDeletion = change.IsDeletion
            })
            .ToList();

    private static async Task<string> PersistProposalEvidenceAsync(
        string runId,
        string evidenceRoot,
        BuilderProposal proposal,
        CancellationToken cancellationToken)
    {
        var proposalId = $"prop-{runId}";
        var runEvidenceRoot = Path.Combine(evidenceRoot, runId, "evidence");
        Directory.CreateDirectory(runEvidenceRoot);
        var proposalPath = Path.Combine(runEvidenceRoot, "proposal.json");

        await File.WriteAllTextAsync(proposalPath, JsonSerializer.Serialize(new
        {
            proposalId,
            proposal.TicketId,
            proposal.ProjectId,
            proposal.Summary,
            proposal.Rationale,
            changes = (proposal.Changes ?? []).Select(change => new
            {
                change.FilePath,
                change.Description,
                change.IsNewFile,
                change.IsDeletion,
                change.IsValid,
                change.Diff
            }),
            boundary = "A proposal is review material. It is not approval, apply permission, or source mutation."
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);

        return proposalId;
    }

    private static TicketBuildRunDto ToDto(RunRecord run, int projectId, long ticketId) => new()
    {
        RunId = run.RunId,
        ProjectId = projectId,
        TicketId = ticketId,
        Status = run.State.ToString(),
        CurrentNode = "SkeletonRun",
        RequiresHumanApproval = false,
        Message = run.FailureReason ?? run.Summary
    };

    private int ReadTimeout(string key)
    {
        var value = _configuration[$"DisposableBuild:{key}"];
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 120;
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
            ["disposableRun"] = "true",
            ["skeletonRun"] = "true"
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
