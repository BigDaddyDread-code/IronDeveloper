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
    private readonly ISkeletonTestAuthoringService _testAuthoring;
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
        ISkeletonTestAuthoringService testAuthoring,
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
        _testAuthoring = testAuthoring;
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

        // P0-5: the Tester authors test files from the acceptance criteria. Its input
        // contract carries no field for the proposal or diff — independence by
        // contract: authored tests check what was asked for, not what was built.
        // Authoring failure degrades explicitly; it is never a silent skip.
        var authoring = await _testAuthoring.AuthorTestsAsync(new SkeletonTestAuthoringRequest
        {
            TicketId = ticketId,
            ProjectId = projectId,
            TicketTitle = ticket.Title ?? string.Empty,
            AcceptanceCriteria = ticket.AcceptanceCriteria ?? string.Empty,
            Problem = ticket.Problem ?? string.Empty
        }, cancellationToken).ConfigureAwait(false);

        var authoredTests = authoring.Succeeded ? SandboxTestPaths(authoring.Tests) : [];
        if (authoring.Succeeded)
        {
            await PublishAsync(run.RunId, "TestsAuthored",
                $"{authoredTests.Count} test file(s) authored from acceptance criteria, blind to the builder's diff.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["authoredTestCount"] = authoredTests.Count.ToString(),
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PublishAsync(run.RunId, "TestAuthoringSkipped",
                $"Test authoring skipped: {authoring.FailureReason} The run continues without authored tests — the criterion-to-test matrix has no cells and the critic package says so.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["skippedReason"] = authoring.FailureReason,
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
        }

        var allWrites = fileWrites
            .Concat(authoredTests.Select(test => new DisposableWorkspaceFileWrite
            {
                RelativePath = test.RelativePath,
                Content = test.Content
            }))
            .ToList();

        var workspaceResult = await _workspaces.RunAsync(new DisposableWorkspaceRunRequest
        {
            RunId = run.RunId,
            SourcePath = project.LocalPath,
            WorkspaceRoot = ResolveWorkspaceRoot(),
            EvidenceRoot = evidenceRoot,
            CleanWorkspaceOnSuccess = true,
            PreserveWorkspaceOnFailure = true,
            PreserveWorkspaceOnCancellation = true,
            FileWrites = allWrites,
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
            run.RunId, evidenceRoot, proposalId, ticket, proposal, authoredTests, workspaceResult, cancellationToken).ConfigureAwait(false);

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

    public async Task<TicketBuildRunDto?> ApplyAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != projectId || run.TicketId != ticketId)
            return null;

        // Explicit sandbox opt-in: applying to a source repository is off by default.
        if (!string.Equals(_configuration["SkeletonApply:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                "ApplyDisabled",
                "Skeleton apply is disabled. Set SkeletonApply:Enabled=true for sandbox projects only — the spine is copy-only and must never point at a repository you cannot discard.",
                cancellationToken).ConfigureAwait(false);
        }

        // Continuation must have been unblocked, proven from durable events —
        // never from the request or a cached flag.
        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run.State != RunLifecycleState.Completed ||
            !events.Any(runEvent => string.Equals(runEvent.EventType, "SkeletonContinuationUnblocked", StringComparison.Ordinal)))
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                "ContinuationNotUnblocked",
                "Apply requires a continuation unblocked by an accepted approval. Request continuation first.",
                cancellationToken).ConfigureAwait(false);
        }

        // The approval is re-verified LIVE at the mutation step — an approval that
        // expired or no longer matches the package hash refuses the apply.
        var evidenceRoot = ResolveEvidenceRoot();
        var packagePath = CriticPackagePath(evidenceRoot, runId);
        if (!File.Exists(packagePath))
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                "CriticPackageEvidenceMissing",
                "The critic package evidence is missing, so the approval cannot be re-verified.",
                cancellationToken).ConfigureAwait(false);
        }

        var packageBytes = await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false);
        var packageHash = ComputeSha256(packageBytes);
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
        var candidates = await _approvals.ListByTargetAsync(requirement.ProjectId, ApprovalTargetKind, runId, cancellationToken).ConfigureAwait(false);
        var satisfied = candidates
            .Select(candidate => _approvalSatisfaction.Evaluate(requirement, candidate))
            .FirstOrDefault(evaluation => evaluation.IsSatisfied);
        if (satisfied is null)
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                "ApprovalNoLongerSatisfied",
                "No live accepted approval satisfies this run's requirement at apply time. Record a fresh approval, then request continuation again.",
                cancellationToken).ConfigureAwait(false);
        }

        var approvedByActorId = candidates.First(candidate => candidate.AcceptedApprovalId == satisfied.AcceptedApprovalId).ApprovedByActorId;
        var package = JsonSerializer.Deserialize<SkeletonCriticPackage>(System.Text.Encoding.UTF8.GetString(packageBytes), JsonOptions)!;

        await PublishAsync(runId, "SkeletonApplyStarted",
            "Governed apply spine started: copy-only, evidence-chained, into and out of a prepared workspace. No commit, no push, no release.",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["acceptedApprovalId"] = satisfied.AcceptedApprovalId?.ToString("D") ?? string.Empty,
                ["approvalTargetHash"] = packageHash,
                ["currentNode"] = "SkeletonApply"
            }, cancellationToken).ConfigureAwait(false);

        var spineWorkspaceRoot = ResolveWorkspaceRoot();
        Directory.CreateDirectory(spineWorkspaceRoot);

        var spineResult = await new SkeletonApplySpine().RunAsync(
            applyRunId: $"{runId}-apply",
            sourceRepo: project.LocalPath!,
            workspaceRoot: spineWorkspaceRoot,
            approvedPackage: package,
            approvedByActorId: approvedByActorId,
            approvalReason: $"Mirrors AcceptedApprovalRecord {satisfied.AcceptedApprovalId:D} bound to package hash {packageHash}. This stage records the decision; it did not make it.",
            cancellationToken).ConfigureAwait(false);

        foreach (var stage in spineResult.Stages)
        {
            await PublishAsync(runId, "SkeletonApplyStage",
                $"{stage.Stage}: {(stage.Succeeded ? "completed" : "blocked")} — {stage.Summary}",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["stage"] = stage.Stage,
                    ["succeeded"] = stage.Succeeded.ToString().ToLowerInvariant(),
                    ["errors"] = string.Join(" | ", stage.Errors),
                    ["currentNode"] = "SkeletonApply"
                }, cancellationToken).ConfigureAwait(false);
        }

        if (!spineResult.Succeeded)
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                $"SpineBlocked:{spineResult.FailedStage}",
                $"The governed apply spine blocked at '{spineResult.FailedStage}'. The workspace evidence chain records why; nothing was silently applied.",
                cancellationToken).ConfigureAwait(false);
        }

        await _runs.TransitionAsync(new RunStateTransition
        {
            RunId = runId,
            State = RunLifecycleState.Applied,
            Summary = $"Applied through the governed workspace spine under accepted approval {satisfied.AcceptedApprovalId:D}. Copy-only: commit, push, and release remain separate governed steps.",
            WorkspacePath = spineResult.WorkspacePath
        }, cancellationToken).ConfigureAwait(false);

        await PublishAsync(runId, "SkeletonApplied",
            "Applied through the governed workspace spine. The workspace evidence chain is the receipt. Copy-only: commit, push, and release remain separate governed steps.",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["acceptedApprovalId"] = satisfied.AcceptedApprovalId?.ToString("D") ?? string.Empty,
                ["workspacePath"] = spineResult.WorkspacePath,
                ["evidenceChain"] = string.Join(",", spineResult.Stages.Select(stage => stage.Stage)),
                ["currentNode"] = "SkeletonApply"
            }, cancellationToken).ConfigureAwait(false);

        var updated = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false) ?? run;
        return ToDto(updated, projectId, ticketId, requiresHumanApproval: false);
    }

    private async Task<TicketBuildRunDto> RefuseApplyAsync(
        RunRecord run,
        int projectId,
        long ticketId,
        string refusedReason,
        string message,
        CancellationToken cancellationToken)
    {
        await PublishAsync(run.RunId, "SkeletonApplyRefused",
            $"Apply refused: {message}",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["refusedReason"] = refusedReason,
                ["currentNode"] = "SkeletonApply"
            }, cancellationToken).ConfigureAwait(false);

        var current = await _runs.GetAsync(run.RunId, cancellationToken).ConfigureAwait(false) ?? run;
        return ToDto(current, projectId, ticketId, requiresHumanApproval: current.State == RunLifecycleState.PausedForApproval);
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

    // The apply spine's evidence chain, in stage order. apply-copy refuses to run
    // unless the upstream chain exists — so for an Applied run, every one of these
    // receipts must be present on disk, and the report checks each of them.
    private static readonly string[] ApplyReceiptChain =
    [
        "promotion-package.json",
        "promotion-approval.json",
        "apply-preflight.json",
        "apply-dry-run.json",
        "apply-copy.json"
    ];

    /// <summary>
    /// P0-6 — trace completeness. Reconstructs the whole loop from durable evidence
    /// only: the run record, published events, and files on disk. Read-only: this
    /// method publishes no events, transitions no state, and queries no approval
    /// store — what it reports is what the run durably recorded, re-verified where
    /// verification is possible (package hash recomputed, receipts checked on disk).
    /// </summary>
    public async Task<SkeletonRunReport?> GetRunReportAsync(
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

        var events = (await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false))
            .OrderBy(runEvent => runEvent.TimestampUtc)
            .ToList();

        var gaps = new List<string>();
        var evidenceRoot = ResolveEvidenceRoot();

        // Proposal link.
        SkeletonRunProposalTrace? proposalTrace = null;
        var proposalEvent = events.FirstOrDefault(runEvent => runEvent.EventType == "ProposalGenerated");
        if (proposalEvent is not null)
        {
            var proposalPath = Path.Combine(evidenceRoot, runId, "evidence", "proposal.json");
            var proposalOnDisk = File.Exists(proposalPath);
            proposalTrace = new SkeletonRunProposalTrace
            {
                ProposalId = Payload(proposalEvent, "proposalId"),
                FileChangeCount = int.TryParse(Payload(proposalEvent, "fileChangeCount"), out var count) ? count : 0,
                EvidenceRef = proposalPath,
                EvidenceExistsOnDisk = proposalOnDisk
            };
            if (!proposalOnDisk)
                gaps.Add("Proposal evidence file is missing from disk.");
        }

        // Test authoring link (P0-5) — an explicit skip is a recorded outcome, not a gap.
        SkeletonRunTestAuthoringTrace? testAuthoringTrace = null;
        var authoredEvent = events.FirstOrDefault(runEvent => runEvent.EventType == "TestsAuthored");
        var authoringSkippedEvent = events.FirstOrDefault(runEvent => runEvent.EventType == "TestAuthoringSkipped");
        if (authoredEvent is not null)
        {
            testAuthoringTrace = new SkeletonRunTestAuthoringTrace
            {
                Authored = true,
                AuthoredTestCount = int.TryParse(Payload(authoredEvent, "authoredTestCount"), out var testCount) ? testCount : 0
            };
        }
        else if (authoringSkippedEvent is not null)
        {
            testAuthoringTrace = new SkeletonRunTestAuthoringTrace
            {
                Authored = false,
                SkippedReason = Payload(authoringSkippedEvent, "skippedReason")
            };
        }

        // Critic package link — the hash is recomputed from disk, never just recited.
        SkeletonRunCriticPackageTrace? packageTrace = null;
        var packageEvent = events.FirstOrDefault(runEvent => runEvent.EventType == "CriticReviewPackageReady");
        if (packageEvent is not null)
        {
            var packagePath = CriticPackagePath(evidenceRoot, runId);
            var announcedHash = Payload(packageEvent, "packageSha256");
            var existsOnDisk = File.Exists(packagePath);
            var hashOnDisk = existsOnDisk
                ? ComputeSha256(await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false))
                : string.Empty;

            var hashVerified = existsOnDisk && !string.IsNullOrEmpty(announcedHash) &&
                string.Equals(announcedHash, hashOnDisk, StringComparison.Ordinal);

            packageTrace = new SkeletonRunCriticPackageTrace
            {
                PackageId = Payload(packageEvent, "packageId"),
                PackagePath = packagePath,
                ExistsOnDisk = existsOnDisk,
                AnnouncedSha256 = announcedHash,
                Sha256OnDisk = hashOnDisk,
                HashVerified = hashVerified
            };

            if (!existsOnDisk)
                gaps.Add("Critic package evidence is missing from disk.");
            else if (!hashVerified)
                gaps.Add("Critic package on disk no longer matches the hash announced at halt — the file changed after any approval bound to it.");
        }

        // Approval link: the requirement the run halted on, and the continuation that
        // consumed a verified accepted approval.
        SkeletonRunApprovalTrace? approvalTrace = null;
        var haltEvent = events.FirstOrDefault(runEvent =>
            runEvent.EventType == "ApprovalRequiredHalt" && !string.IsNullOrEmpty(Payload(runEvent, "approvalTargetKind")));
        var unblockedEvent = events.FirstOrDefault(runEvent => runEvent.EventType == "SkeletonContinuationUnblocked");
        if (haltEvent is not null || unblockedEvent is not null)
        {
            approvalTrace = new SkeletonRunApprovalTrace
            {
                TargetKind = haltEvent is null ? string.Empty : Payload(haltEvent, "approvalTargetKind"),
                TargetId = haltEvent is null ? string.Empty : Payload(haltEvent, "approvalTargetId"),
                TargetHash = haltEvent is null ? string.Empty : Payload(haltEvent, "approvalTargetHash"),
                CapabilityCode = haltEvent is null ? string.Empty : Payload(haltEvent, "capabilityCode"),
                HaltObserved = haltEvent is not null,
                ContinuationUnblocked = unblockedEvent is not null,
                AcceptedApprovalId = unblockedEvent is null ? string.Empty : Payload(unblockedEvent, "acceptedApprovalId")
            };

            if (unblockedEvent is not null && haltEvent is null)
                gaps.Add("Continuation was unblocked but no approval halt was recorded first.");
            if (unblockedEvent is not null && string.IsNullOrEmpty(Payload(unblockedEvent, "acceptedApprovalId")))
                gaps.Add("Continuation was unblocked but the consumed accepted-approval id was not recorded.");
        }

        // Apply link (P0-4): stages from durable events, receipts checked on disk.
        SkeletonRunApplyTrace? applyTrace = null;
        var appliedEvent = events.FirstOrDefault(runEvent => runEvent.EventType == "SkeletonApplied");
        var refusedEvent = events.LastOrDefault(runEvent => runEvent.EventType == "SkeletonApplyRefused");
        var stageEvents = events.Where(runEvent => runEvent.EventType == "SkeletonApplyStage").ToList();
        if (appliedEvent is not null || refusedEvent is not null || stageEvents.Count > 0)
        {
            var workspacePath = appliedEvent is null
                ? run.WorkspacePath ?? string.Empty
                : Payload(appliedEvent, "workspacePath");

            var receipts = new List<SkeletonRunReceiptRef>();
            if (appliedEvent is not null && !string.IsNullOrEmpty(workspacePath))
            {
                var receiptDir = Path.Combine(workspacePath, ".irondev", "runs", $"{runId}-apply");
                foreach (var receiptName in ApplyReceiptChain)
                {
                    var receiptPath = Path.Combine(receiptDir, receiptName);
                    var receiptExists = File.Exists(receiptPath);
                    receipts.Add(new SkeletonRunReceiptRef
                    {
                        Name = receiptName,
                        Path = receiptPath,
                        ExistsOnDisk = receiptExists
                    });
                    if (!receiptExists)
                        gaps.Add($"Apply receipt '{receiptName}' is missing from the workspace evidence chain.");
                }
            }

            applyTrace = new SkeletonRunApplyTrace
            {
                Applied = appliedEvent is not null,
                WorkspacePath = workspacePath,
                RefusedReason = appliedEvent is null && refusedEvent is not null ? Payload(refusedEvent, "refusedReason") : string.Empty,
                Stages = stageEvents
                    .Select(stageEvent => new SkeletonRunApplyStageTrace
                    {
                        Stage = Payload(stageEvent, "stage"),
                        Succeeded = string.Equals(Payload(stageEvent, "succeeded"), "true", StringComparison.OrdinalIgnoreCase),
                        Errors = Payload(stageEvent, "errors")
                    })
                    .ToList(),
                Receipts = receipts
            };
        }

        if (run.State == RunLifecycleState.Applied)
        {
            if (approvalTrace is not { ContinuationUnblocked: true })
                gaps.Add("The run is Applied but no continuation-unblocked event was recorded.");
            if (applyTrace is not { Applied: true })
                gaps.Add("The run is Applied but no applied event was recorded.");
        }

        var loopComplete = run.State == RunLifecycleState.Applied &&
            gaps.Count == 0 &&
            packageTrace is { HashVerified: true } &&
            approvalTrace is { HaltObserved: true, ContinuationUnblocked: true } &&
            applyTrace is { Applied: true } && applyTrace.Receipts.Count > 0;

        return new SkeletonRunReport
        {
            RunId = runId,
            ProjectId = projectId,
            TicketId = ticketId,
            Status = run.State.ToString(),
            Summary = run.FailureReason ?? run.Summary,
            Timeline = events
                .Select(runEvent => new SkeletonRunTimelineEntry
                {
                    TimestampUtc = runEvent.TimestampUtc,
                    EventType = runEvent.EventType,
                    Message = runEvent.Message
                })
                .ToList(),
            Proposal = proposalTrace,
            TestAuthoring = testAuthoringTrace,
            CriticPackage = packageTrace,
            Approval = approvalTrace,
            Apply = applyTrace,
            Gaps = gaps,
            LoopComplete = loopComplete
        };
    }

    private static string Payload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) ? value : string.Empty;

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

    /// <summary>
    /// Confines authored tests to the tests/ folder: paths outside it are re-rooted
    /// under tests/skeleton/ so authored tests can never collide with, or overwrite,
    /// the builder's proposed changes.
    /// </summary>
    private static IReadOnlyList<SkeletonAuthoredTest> SandboxTestPaths(IReadOnlyList<SkeletonAuthoredTest> tests) =>
        tests
            .Select(test =>
            {
                var normalized = test.RelativePath.Replace('\\', '/').TrimStart('/');
                return normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) && !normalized.Contains("..", StringComparison.Ordinal)
                    ? test with { RelativePath = normalized }
                    : test with { RelativePath = $"tests/skeleton/{Path.GetFileName(normalized)}" };
            })
            .ToList();

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
        IReadOnlyList<SkeletonAuthoredTest> authoredTests,
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
            authoredTests,
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
