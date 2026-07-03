using System.Text.Json;
using IronDev.Core.Builder;
using IronDev.Core.Governance;
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
/// Walking-skeleton orchestrator. Chains existing governed services into one run:
/// readiness (enforced inside proposal generation) → proposal (persisted as evidence
/// with an id) → disposable workspace → declarative file writes in-workspace →
/// dotnet build + dotnet test → evidence packaged → critic package prepared →
/// halted for approval.
///
/// Boundary: no new authority — composition only. Blocked states are explicit;
/// a successful run halts PausedForApproval, and the ONLY unblock is a live
/// AcceptedApprovalRecord matching the run's requirement exactly (target kind,
/// run id, critic-package hash, capability code) and unexpired — evaluated through
/// the approval satisfaction and workflow halt evaluators. This service consumes
/// approval evidence; it can never create, grant, or simulate approval, and
/// continuation is not apply permission. Halt is not approval.
/// </summary>
public sealed class TicketSkeletonRunService : ITicketSkeletonRunService
{
    // Canonical governance vocabulary: a skeleton continuation is a workflow
    // continuation request, approved as workflow-continuation input.
    public const string ApprovalTargetKind = AcceptedApprovalTargetKinds.WorkflowContinuationRequest;
    public const string ContinueCapabilityCode = "skeleton-run.continue";

    private readonly ITicketService _tickets;
    private readonly IProjectService _projects;
    private readonly IBuilderProposalService _proposals;
    private readonly IDisposableWorkspaceExecutionService _workspaces;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly IAcceptedApprovalStore _approvals;
    private readonly IApprovalSatisfactionEvaluator _approvalSatisfaction;
    private readonly IWorkflowApprovalHaltEvaluator _approvalHalt;
    private readonly IConfiguration _configuration;

    public TicketSkeletonRunService(
        ITicketService tickets,
        IProjectService projects,
        IBuilderProposalService proposals,
        IDisposableWorkspaceExecutionService workspaces,
        IRunStore runs,
        IRunEventStore events,
        IAcceptedApprovalStore approvals,
        IApprovalSatisfactionEvaluator approvalSatisfaction,
        IWorkflowApprovalHaltEvaluator approvalHalt,
        IConfiguration configuration)
    {
        _tickets = tickets;
        _projects = projects;
        _proposals = proposals;
        _workspaces = workspaces;
        _runs = runs;
        _events = events;
        _approvals = approvals;
        _approvalSatisfaction = approvalSatisfaction;
        _approvalHalt = approvalHalt;
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
        // proposal generation. A readiness block becomes an explicit blocked run state;
        // any other proposal failure is classified separately so the next safe action
        // points at the right person (user resolves the ticket vs operator investigates).
        BuilderProposal proposal;
        try
        {
            proposal = await _proposals.GenerateProposalAsync(ticketId, cancellationToken).ConfigureAwait(false);
        }
        catch (BuildReadinessBlockedException exception)
        {
            return await BlockAsync(run.RunId, projectId, ticketId,
                "ReadinessBlocked",
                exception.Message,
                "Resolve the blocking issues on the ticket, then start a new skeleton run.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await BlockAsync(run.RunId, projectId, ticketId,
                "ProposalGenerationFailed",
                exception.Message,
                "This is a service failure, not a ticket problem. Check the proposal service and model provider, then start a new skeleton run.",
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

        // P0-2: prepare the critic's review package at full fidelity. The package is
        // review material only — the run does not create, request, or simulate the
        // review itself; that belongs to the critic through its own governed surface.
        var packagePath = await PersistCriticPackageAsync(
            run.RunId, evidenceRoot, proposalId, ticket, proposal, workspaceResult, cancellationToken).ConfigureAwait(false);

        var packageHash = ComputeSha256(await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false));

        await PublishAsync(run.RunId, "CriticReviewPackageReady",
            "Critic review package prepared. A package is review material, not a review: the independent critic reviews it through its own governed surface, and nothing here is approval.",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["packageId"] = $"critic-pkg-{run.RunId}",
                ["packagePath"] = packagePath,
                ["packageSha256"] = packageHash,
                ["proposalId"] = proposalId,
                ["currentNode"] = "SkeletonRun"
            }, cancellationToken).ConfigureAwait(false);

        // P0-3: a successful run halts for approval. The halt names the exact approval
        // requirement a human must record through the accepted-approvals surface —
        // bound to the critic-package hash so approval attaches to precisely what was
        // reviewed. Halt is not approval.
        if (workspaceResult.Succeeded)
        {
            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = run.RunId,
                State = RunLifecycleState.PausedForApproval,
                Summary = "Halted for approval. Halt is not approval: continuation requires a live accepted approval matching this run's requirement."
            }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(run.RunId, "ApprovalRequiredHalt",
                "Halted for approval. Halt is not approval: record an accepted approval matching this requirement, then request continuation.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["approvalProjectId"] = ApprovalProjectGuid(projectId).ToString("D"),
                    ["approvalTargetKind"] = ApprovalTargetKind,
                    ["approvalTargetId"] = run.RunId,
                    ["approvalTargetHash"] = packageHash,
                    ["capabilityCode"] = ContinueCapabilityCode,
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
        }

        var updated = await _runs.GetAsync(run.RunId, cancellationToken).ConfigureAwait(false) ?? run;
        return ToDto(updated, projectId, ticketId, requiresHumanApproval: updated.State == RunLifecycleState.PausedForApproval);
    }

    public async Task<TicketBuildRunDto?> ContinueAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != projectId || run.TicketId != ticketId)
            return null;

        if (run.State != RunLifecycleState.PausedForApproval)
        {
            await PublishAsync(runId, "ContinuationRefused",
                $"Continuation refused: the run is {run.State}, not awaiting approval. Continuation applies only to approval halts.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["refusedReason"] = "NotAwaitingApproval",
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
            return ToDto(run, projectId, ticketId, requiresHumanApproval: false);
        }

        // The requirement is recomputed from durable evidence, never trusted from the
        // request: the package hash on disk is what the approval must have bound to.
        var packagePath = CriticPackagePath(ResolveEvidenceRoot(), runId);
        if (!File.Exists(packagePath))
        {
            await PublishAsync(runId, "ContinuationRefused",
                "Continuation refused: the critic package evidence is missing, so the approval requirement cannot be recomputed.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["refusedReason"] = "CriticPackageEvidenceMissing",
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
            return ToDto(run, projectId, ticketId, requiresHumanApproval: true);
        }

        var packageHash = ComputeSha256(await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false));
        var requirement = new ApprovalRequirement
        {
            ProjectId = ApprovalProjectGuid(projectId),
            ApprovalTargetKind = ApprovalTargetKind,
            ApprovalTargetId = runId,
            ApprovalTargetHash = packageHash,
            CapabilityCode = ContinueCapabilityCode,
            ApprovalPurpose = AcceptedApprovalPurposes.WorkflowContinuationInput,
            EvaluatedAtUtc = DateTimeOffset.UtcNow
        };

        // Live store query is the only unblock: no cached flag, no request field.
        var candidates = await _approvals.ListByTargetAsync(
            requirement.ProjectId, ApprovalTargetKind, runId, cancellationToken).ConfigureAwait(false);

        var satisfied = candidates
            .Select(candidate => _approvalSatisfaction.Evaluate(requirement, candidate))
            .FirstOrDefault(evaluation => evaluation.IsSatisfied);

        var haltState = _approvalHalt.Evaluate(new WorkflowApprovalHaltEvaluationRequest
        {
            WorkflowStepId = $"skeleton-continue-{runId}",
            RequiredApprovals =
            [
                new WorkflowApprovalHaltRequirement
                {
                    Kind = WorkflowApprovalRequirementKind.HumanApprovalReference,
                    RequirementId = $"skeleton-continue-{runId}",
                    SafeSummary = "A live accepted approval matching this run's requirement is required."
                }
            ],
            AvailableApprovalEvidence = satisfied is null
                ? []
                :
                [
                    new WorkflowApprovalEvidenceReference
                    {
                        Kind = WorkflowApprovalRequirementKind.HumanApprovalReference,
                        ReferenceId = $"skeleton-continue-{runId}",
                        SafeSummary = $"Accepted approval {satisfied.AcceptedApprovalId} verified against the run's requirement."
                    }
                ]
        });

        if (haltState.Status != WorkflowApprovalHaltStatus.ApprovalEvidencePresentForFutureExecution)
        {
            await PublishAsync(runId, "ApprovalRequiredHalt",
                "Continuation refused: no live accepted approval satisfies this run's requirement. Halt is not approval.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["refusedReason"] = "MissingOrUnsatisfiedApproval",
                    ["candidateCount"] = candidates.Count.ToString(),
                    ["approvalTargetHash"] = packageHash,
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
            var still = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false) ?? run;
            return ToDto(still, projectId, ticketId, requiresHumanApproval: true);
        }

        await _runs.TransitionAsync(new RunStateTransition
        {
            RunId = runId,
            State = RunLifecycleState.Completed,
            Summary = $"Continuation allowed by accepted approval {satisfied!.AcceptedApprovalId}. Approval evidence was verified; it is not apply permission — controlled apply remains a separate governed step."
        }, cancellationToken).ConfigureAwait(false);

        await PublishAsync(runId, "SkeletonContinuationUnblocked",
            "Continuation allowed: a live accepted approval matched this run's requirement exactly. Approval is not apply permission; controlled apply remains a separate governed step.",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["acceptedApprovalId"] = satisfied.AcceptedApprovalId?.ToString("D") ?? string.Empty,
                ["approvalTargetHash"] = packageHash,
                ["currentNode"] = "SkeletonRun"
            }, cancellationToken).ConfigureAwait(false);

        var updated = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false) ?? run;
        return ToDto(updated, projectId, ticketId, requiresHumanApproval: false);
    }

    /// <summary>
    /// Deterministic governance-scope Guid for an int project id, so approvals recorded
    /// through the Guid-scoped accepted-approvals surface can address skeleton runs.
    /// </summary>
    public static Guid ApprovalProjectGuid(int projectId) =>
        Guid.ParseExact($"{projectId:D8}-0000-0000-0000-000000000000", "D");

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    public async Task<SkeletonCriticPackage?> GetCriticPackageAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != projectId || run.TicketId != ticketId)
            return null;

        var packagePath = CriticPackagePath(ResolveEvidenceRoot(), runId);
        if (!File.Exists(packagePath))
            return null;

        var json = await File.ReadAllTextAsync(packagePath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SkeletonCriticPackage>(json, JsonOptions);
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static string CriticPackagePath(string evidenceRoot, string runId) =>
        Path.Combine(evidenceRoot, runId, "evidence", "critic-package.json");

    private static async Task<string> PersistCriticPackageAsync(
        string runId,
        string evidenceRoot,
        string proposalId,
        IronDev.Data.Models.ProjectTicket ticket,
        BuilderProposal proposal,
        DisposableWorkspaceRunResult workspaceResult,
        CancellationToken cancellationToken)
    {
        var commandResults = workspaceResult.Commands
            .Select(command => new SkeletonCriticPackageCommandResult
            {
                DisplayName = command.DisplayName,
                ExitCode = command.ExitCode,
                TimedOut = command.TimedOut,
                DurationMs = command.DurationMs,
                StandardOutputRef = command.StandardOutputPath,
                StandardErrorRef = command.StandardErrorPath
            })
            .ToList();

        var evidenceRefs = new List<string>
        {
            Path.Combine(evidenceRoot, runId, "evidence", "proposal.json"),
            workspaceResult.EvidencePath
        };

        var package = SkeletonCriticPackageBuilder.Build(
            runId,
            proposalId,
            ticket.Id,
            ticket.ProjectId,
            ticket.Title ?? string.Empty,
            ticket.AcceptanceCriteria,
            proposal,
            commandResults,
            evidenceRefs,
            workspaceResult.Succeeded);

        var packagePath = CriticPackagePath(evidenceRoot, runId);
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(packagePath, JsonSerializer.Serialize(package, JsonOptions), cancellationToken).ConfigureAwait(false);
        return packagePath;
    }

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

    private static TicketBuildRunDto ToDto(RunRecord run, int projectId, long ticketId, bool requiresHumanApproval = false) => new()
    {
        RunId = run.RunId,
        ProjectId = projectId,
        TicketId = ticketId,
        Status = run.State.ToString(),
        CurrentNode = "SkeletonRun",
        RequiresHumanApproval = requiresHumanApproval,
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
