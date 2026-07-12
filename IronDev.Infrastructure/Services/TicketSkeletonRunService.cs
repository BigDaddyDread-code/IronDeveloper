using System.Text;
using System.Text.Json;
using IronDev.Core.Agents;
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
    private readonly ISkeletonMutationLeaseService _mutationLeases;
    private readonly IProjectMembershipService _projectMemberships;
    private readonly ISkeletonAgentProfileService _agentProfiles;
    private readonly SkeletonRunDriftDetector _driftDetector;
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
        ISkeletonMutationLeaseService mutationLeases,
        IProjectMembershipService projectMemberships,
        ISkeletonAgentProfileService agentProfiles,
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
        _mutationLeases = mutationLeases;
        _projectMemberships = projectMemberships;
        _agentProfiles = agentProfiles;
        _driftDetector = new SkeletonRunDriftDetector(events);
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

        try
        {
            await CaptureAgentConfigurationSnapshotsAsync(run.RunId, project.TenantId, projectId, ticketId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await BlockAsync(run.RunId, projectId, ticketId,
                "AgentConfigurationSnapshotFailed",
                exception.Message,
                "Resolve agent configuration and start a new run. No model-driven work was started.",
                cancellationToken).ConfigureAwait(false);
        }

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
            // AG-6: which configured model the Builder ran on.
            ["modelProvider"] = proposal.ModelProvider ?? string.Empty,
            ["modelName"] = proposal.ModelName ?? string.Empty,
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
                    // AG-2: which model authored the tests.
                    ["modelProvider"] = authoring.ModelProvider,
                    ["modelName"] = authoring.ModelName,
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

        // The orchestrator owns the run lifecycle: it moves Created → Running as the
        // build/test begins (the workspace no longer does this — see OwnsRunLifecycle),
        // and will move Running → PausedForApproval once evidence is packaged.
        await _runs.TransitionAsync(new RunStateTransition
        {
            RunId = run.RunId,
            State = RunLifecycleState.Running,
            Summary = "Skeleton build and test running in a disposable workspace."
        }, cancellationToken).ConfigureAwait(false);

        // REPAIR-1: bounded repair. A failed build/test is evidence; with an explicit
        // attempt budget (SkeletonRepair:MaxAttempts, default 0 = off) the orchestrator
        // directs the Builder to repair its proposal — new proposal, fresh attempt-scoped
        // workspace, failure evidence in context. A repair attempt is proposal-shaped
        // work, never authority: the gate, approval, continuation, and apply are
        // unchanged, and every attempt's evidence and events are preserved.
        var maxRepairAttempts = ReadMaxRepairAttempts();
        var attemptNumber = 1;
        // The CURRENT proposal's evidence file. When a repair succeeds, the repaired
        // proposal IS the proposal under review — the critic package and the gate
        // must bind to it, never to the original failed attempt with a side-note.
        var proposalEvidenceFileName = "proposal.json";
        DisposableWorkspaceRunResult workspaceResult;
        while (true)
        {
            var allWrites = fileWrites
                .Concat(authoredTests.Select(test => new DisposableWorkspaceFileWrite
                {
                    RelativePath = test.RelativePath,
                    Content = test.Content
                }))
                .ToList();

            workspaceResult = await _workspaces.RunAsync(new DisposableWorkspaceRunRequest
            {
                RunId = run.RunId,
                SourcePath = project.LocalPath,
                WorkspaceRoot = ResolveWorkspaceRoot(),
                EvidenceRoot = evidenceRoot,
                CleanWorkspaceOnSuccess = true,
                PreserveWorkspaceOnFailure = true,
                PreserveWorkspaceOnCancellation = true,
                // The orchestrator owns this run's lifecycle — the workspace supplies
                // evidence and a result, but must not drive the run to Completed while
                // the orchestrator still intends to pause it at the human gate.
                OwnsRunLifecycle = false,
                // Attempt-scoped paths: a repair rerun never erases a previous attempt.
                AttemptLabel = attemptNumber == 1 ? string.Empty : $"repair-{attemptNumber}",
                FileWrites = allWrites,
                Commands = DotNetCommandProfile.BuildAndTest(project.LocalPath, ReadTimeout("BuildTimeoutSeconds"), ReadTimeout("TestTimeoutSeconds"))
            }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(run.RunId, "SkeletonEvidencePackaged",
                "Evidence packaged. This run grants nothing: critic review, human approval, and any apply remain separate governed steps.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["proposalId"] = proposalId,
                    ["attemptNumber"] = attemptNumber.ToString(),
                    ["succeeded"] = workspaceResult.Succeeded.ToString().ToLowerInvariant(),
                    ["evidencePath"] = workspaceResult.EvidencePath,
                    ["commandCount"] = workspaceResult.Commands.Count.ToString(),
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);

            if (workspaceResult.Succeeded)
                break;

            var classification = SkeletonBuildFailureClassifier.Classify(workspaceResult.Commands);

            if (attemptNumber > maxRepairAttempts)
            {
                // Terminal, named, and honest — a run that cannot build is Failed,
                // never left silently Running. A failed run grants nothing and there
                // is nothing at the gate to approve.
                return await BlockAsync(run.RunId, projectId, ticketId,
                    maxRepairAttempts == 0 ? classification.Kind.ToString() : "RepairBudgetExhausted",
                    $"{classification.Kind} on '{classification.FailedCommand}' after {attemptNumber} attempt(s) " +
                    $"(repair budget: {maxRepairAttempts}). The failed attempt's workspace and evidence are preserved.",
                    "Read the preserved failure evidence, refine the ticket or the repair budget, then start a new skeleton run.",
                    cancellationToken).ConfigureAwait(false);
            }

            attemptNumber++;

            await PublishAsync(run.RunId, "SkeletonRepairAttemptStarted",
                $"Repair attempt {attemptNumber} of {maxRepairAttempts + 1} total attempts: {classification.Kind} on '{classification.FailedCommand}'. " +
                "A repair attempt is a new proposal, not authority — the human gate, approval, continuation, and apply are unchanged, and every attempt's evidence is preserved.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["attemptNumber"] = attemptNumber.ToString(),
                    ["maxRepairAttempts"] = maxRepairAttempts.ToString(),
                    ["failureKind"] = classification.Kind.ToString(),
                    ["failedCommand"] = classification.FailedCommand,
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);

            try
            {
                proposal = await _proposals.GenerateRepairProposalAsync(ticketId, new SkeletonRepairContext
                {
                    AttemptNumber = attemptNumber,
                    Classification = classification,
                    PreviousProposal = proposal
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await BlockAsync(run.RunId, projectId, ticketId,
                    "RepairProposalGenerationFailed",
                    exception.Message,
                    "This is a service failure, not a ticket problem. Check the proposal service and model provider, then start a new skeleton run.",
                    cancellationToken).ConfigureAwait(false);
            }

            fileWrites = MapFileWrites(proposal);
            if (fileWrites.Count == 0)
            {
                return await BlockAsync(run.RunId, projectId, ticketId,
                    "RepairProposalEmpty",
                    "The repair proposal produced no valid file changes to exercise.",
                    "Read the preserved failure evidence and refine the ticket, then start a new skeleton run.",
                    cancellationToken).ConfigureAwait(false);
            }

            proposalId = await PersistProposalEvidenceAsync(run.RunId, evidenceRoot, proposal, cancellationToken, $"repair-{attemptNumber}").ConfigureAwait(false);
            proposalEvidenceFileName = $"proposal-repair-{attemptNumber}.json";

            await PublishAsync(run.RunId, "SkeletonRepairProposalGenerated",
                $"Repair proposal {proposalId} generated with {fileWrites.Count} file change(s). A proposal is review material, not approval.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["attemptNumber"] = attemptNumber.ToString(),
                    ["proposalId"] = proposalId,
                    ["fileChangeCount"] = fileWrites.Count.ToString(),
                    ["modelProvider"] = proposal.ModelProvider ?? string.Empty,
                    ["modelName"] = proposal.ModelName ?? string.Empty,
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
        }

        // P0-2: prepare the critic's review package at full fidelity. The package is
        // review material only — the run does not create, request, or simulate the
        // review itself; that belongs to the critic through its own governed surface.
        var (packagePath, criterionCount, uncoveredCriterionCount) = await PersistCriticPackageAsync(
            run.RunId, evidenceRoot, proposalId, proposalEvidenceFileName, ticket, proposal, authoredTests, workspaceResult, cancellationToken).ConfigureAwait(false);

        var packageHash = ComputeSha256(await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false));

        await PublishAsync(run.RunId, "CriticReviewPackageReady",
            "Critic review package prepared. A package is review material, not a review: the independent critic reviews it through its own governed surface, and nothing here is approval." +
            (uncoveredCriterionCount > 0
                ? $" {uncoveredCriterionCount} of {criterionCount} acceptance criteria have NO covering test — the coverage hole is explicit in the package."
                : string.Empty),
            projectId, ticketId, new Dictionary<string, string>
            {
                ["packageId"] = $"critic-pkg-{run.RunId}",
                ["packagePath"] = packagePath,
                ["packageSha256"] = packageHash,
                ["proposalId"] = proposalId,
                ["proposalEvidenceFile"] = proposalEvidenceFileName,
                ["criterionCount"] = criterionCount.ToString(),
                ["uncoveredCriterionCount"] = uncoveredCriterionCount.ToString(),
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
                "Halted for approval. Halt is not approval: record an accepted approval matching this requirement, then request continuation." +
                (uncoveredCriterionCount > 0
                    ? $" NOTE: {uncoveredCriterionCount} of {criterionCount} acceptance criteria have no covering test — approving this run includes consciously owning that coverage hole."
                    : string.Empty),
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["approvalProjectId"] = ApprovalProjectGuid(projectId).ToString("D"),
                    ["approvalTargetKind"] = ApprovalTargetKind,
                    ["approvalTargetId"] = run.RunId,
                    ["approvalTargetHash"] = packageHash,
                    ["capabilityCode"] = ContinueCapabilityCode,
                    ["criterionCount"] = criterionCount.ToString(),
                    ["uncoveredCriterionCount"] = uncoveredCriterionCount.ToString(),
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
        }

        var updated = await _runs.GetAsync(run.RunId, cancellationToken).ConfigureAwait(false) ?? run;
        return ToDto(updated, projectId, ticketId, requiresHumanApproval: updated.State == RunLifecycleState.PausedForApproval);
    }

    public Task<TicketBuildRunDto?> ContinueAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default) =>
        ContinueAsAsync(projectId, ticketId, runId, "unknown-user", cancellationToken);

    public async Task<TicketBuildRunDto?> ContinueAsAsync(
        int projectId,
        long ticketId,
        string runId,
        string requestedByUserId,
        CancellationToken cancellationToken = default)
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

        // P2-5, checked first: the world may have moved. If an upstream run applied
        // overlapping changes after this package was prepared, this run's evidence
        // describes a source that no longer exists — no point evaluating findings
        // or approvals on a run the world already invalidated.
        var staleBecause = await _driftDetector.DetectAsync(projectId, runId, ResolveEvidenceRoot(), cancellationToken).ConfigureAwait(false);
        if (staleBecause is not null)
        {
            await PublishAsync(runId, "ContinuationRefused",
                $"Continuation refused: {staleBecause}",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["refusedReason"] = "StaleAfterUpstreamApply",
                    ["staleBecause"] = staleBecause,
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
            return ToDto(run, projectId, ticketId, requiresHumanApproval: true);
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

        // P1-3 / REVISE-1: the review must bind to the CURRENT package hash. A
        // review of a superseded package (the world before a revision replaced
        // the gate evidence) satisfies nothing — the revised work needs its own
        // critic review. Evaluated from durable events alone.
        var continueEvents = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        if (!HasRecordedCriticReviewForPackage(continueEvents, packageHash))
        {
            await PublishAsync(runId, "ContinuationRefused",
                "Continuation refused: no critic review is recorded for this run's current critic package. " +
                "A human cannot continue work the critic never reviewed — and after a revision, the revised package needs its own review.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["refusedReason"] = "CriticReviewMissing",
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
            return ToDto(run, projectId, ticketId, requiresHumanApproval: true);
        }

        var undispositioned = UndispositionedFindingIds(continueEvents);
        if (undispositioned.Count > 0)
        {
            await PublishAsync(runId, "ContinuationRefused",
                $"Continuation refused: {undispositioned.Count} critic finding(s) have no human disposition. " +
                "A finding is not a veto, but it cannot be ignored — record a disposition for every finding, then request continuation again.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["refusedReason"] = "UndispositionedFindings",
                    ["undispositionedFindingIds"] = string.Join(",", undispositioned),
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
            return ToDto(run, projectId, ticketId, requiresHumanApproval: true);
        }
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

        var acceptedApproval = candidates.First(candidate => candidate.AcceptedApprovalId == satisfied!.AcceptedApprovalId);
        var authority = await EvaluateContinuationAuthorityAsync(
            project.TenantId,
            projectId,
            requestedByUserId,
            acceptedApproval,
            cancellationToken).ConfigureAwait(false);

        if (!authority.IsAllowed)
        {
            await PublishAsync(runId, "ContinuationRefused",
                $"Continuation refused: {authority.RefusedBecause}",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["refusedReason"] = authority.RefusedReason,
                    ["requestedByUserId"] = NormalizeActorId(requestedByUserId),
                    ["approvedByActorId"] = acceptedApproval.ApprovedByActorId,
                    ["eligibleUserIds"] = string.Join(",", authority.EligibleUserIds),
                    ["soloApprovalExceptionAllowed"] = authority.SoloApprovalExceptionAllowed.ToString().ToLowerInvariant(),
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
                ["approvedByActorId"] = acceptedApproval.ApprovedByActorId,
                ["approvedByActorDisplayName"] = acceptedApproval.ApprovedByActorDisplayName ?? string.Empty,
                ["requestedByUserId"] = NormalizeActorId(requestedByUserId),
                ["soloApprovalExceptionUsed"] = authority.SoloApprovalExceptionUsed.ToString().ToLowerInvariant(),
                ["currentNode"] = "SkeletonRun"
            }, cancellationToken).ConfigureAwait(false);

        var updated = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false) ?? run;
        return ToDto(updated, projectId, ticketId, requiresHumanApproval: false);
    }

    /// <summary>
    /// REVISE-1 — finding-driven revision. The human at the gate directs the
    /// Builder to revise the proposal under review instead of approving it. A
    /// revision is human-directed, proposal-shaped work, never authority: the
    /// revised package needs its own critic review, dispositions, and hash-bound
    /// approval, and a failed revision leaves the previous gate package canonical.
    /// Bounded by SkeletonRevision:MaxAttempts (default 0 = off, clamped to 3).
    /// </summary>
    public async Task<TicketBuildRunDto?> ReviseAsync(int projectId, long ticketId, string runId, SkeletonRunRevisionRequest request, CancellationToken cancellationToken = default)
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

        async Task<TicketBuildRunDto> RefuseAsync(string refusedReason, string message, IReadOnlyDictionary<string, string>? details = null)
        {
            var payload = new Dictionary<string, string>(details ?? new Dictionary<string, string>())
            {
                ["refusedReason"] = refusedReason,
                ["currentNode"] = "SkeletonRun"
            };
            await PublishAsync(runId, "SkeletonRevisionRefused", $"Revision refused: {message}", projectId, ticketId, payload, cancellationToken).ConfigureAwait(false);
            var current = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false) ?? run;
            return ToDto(current, projectId, ticketId, requiresHumanApproval: current.State == RunLifecycleState.PausedForApproval);
        }

        if (run.State != RunLifecycleState.PausedForApproval)
        {
            return await RefuseAsync("NotAwaitingApproval",
                $"the run is {run.State}, not halted at the human gate. A revision answers the gate; it cannot revive or redirect a run elsewhere.").ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return await RefuseAsync("RevisionReasonMissing",
                "a revision requires the human's written instruction. The Builder revises from that instruction — a revision without one is a dismissal, and dismissals are not decisions.").ConfigureAwait(false);
        }

        var citedFindingIds = (request.FindingIds ?? [])
            .Select(findingId => findingId?.Trim() ?? string.Empty)
            .Where(findingId => findingId.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (citedFindingIds.Count == 0)
        {
            return await RefuseAsync("RevisionFindingsMissing",
                "a revision is driven by named critic findings. Cite the finding(s) this revision answers — to change course without findings, disposition and approve, or start a new run.").ConfigureAwait(false);
        }

        var evidenceRoot = ResolveEvidenceRoot();
        var packagePath = CriticPackagePath(evidenceRoot, runId);
        if (!File.Exists(packagePath))
        {
            return await RefuseAsync("CriticPackageEvidenceMissing",
                "the critic package evidence is missing, so there is nothing at the gate to revise.").ConfigureAwait(false);
        }

        var packageBytes = await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false);
        var currentPackageHash = ComputeSha256(packageBytes);

        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        var currentReviewFindingIds = events
            .Where(runEvent => runEvent.EventType == "SkeletonCriticReviewRecorded" &&
                string.Equals(Payload(runEvent, "packageSha256"), currentPackageHash, StringComparison.Ordinal))
            .SelectMany(runEvent => Payload(runEvent, "findingIds")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);

        if (!HasRecordedCriticReviewForPackage(events, currentPackageHash))
        {
            return await RefuseAsync("CriticReviewMissing",
                "no critic review is recorded for this run's current critic package. A revision answers the critic's findings — request a critic review first.").ConfigureAwait(false);
        }

        var dispositionedFindingIds = events
            .Where(runEvent => runEvent.EventType == "SkeletonFindingDispositionRecorded")
            .Select(runEvent => Payload(runEvent, "findingId"))
            .Where(findingId => !string.IsNullOrEmpty(findingId))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = citedFindingIds.Where(findingId => !currentReviewFindingIds.Contains(findingId)).ToList();
        if (unknown.Count > 0)
        {
            return await RefuseAsync("UnknownFinding",
                $"finding(s) [{string.Join(", ", unknown)}] are not on any critic review of the current package. A revision can only answer findings the critic actually made against what is at the gate.").ConfigureAwait(false);
        }

        var alreadyDecided = citedFindingIds.Where(dispositionedFindingIds.Contains).ToList();
        if (alreadyDecided.Count > 0)
        {
            return await RefuseAsync("FindingAlreadyDispositioned",
                $"finding(s) [{string.Join(", ", alreadyDecided)}] already carry a human disposition. A decision was made; a revision cannot silently unmake it.").ConfigureAwait(false);
        }

        var unansweredUncited = UndispositionedFindingIds(events)
            .Where(findingId => !citedFindingIds.Contains(findingId, StringComparer.Ordinal))
            .ToList();
        if (unansweredUncited.Count > 0)
        {
            return await RefuseAsync("UndispositionedFindingsNotCited",
                $"finding(s) [{string.Join(", ", unansweredUncited)}] have no disposition and are not cited by this revision. A revision may not leave any finding unanswered behind it — cite them or disposition them first.").ConfigureAwait(false);
        }

        var maxRevisionAttempts = ReadMaxRevisionAttempts();
        var priorAttempts = events.Count(runEvent => runEvent.EventType == "SkeletonRevisionAttemptStarted");
        if (maxRevisionAttempts == 0)
        {
            return await RefuseAsync("RevisionDisabled",
                "revision is disabled. Set SkeletonRevision:MaxAttempts explicitly to allow bounded, human-directed revisions — the gate's disposition and approval path remains fully available.").ConfigureAwait(false);
        }
        if (priorAttempts >= maxRevisionAttempts)
        {
            return await RefuseAsync("RevisionBudgetExhausted",
                $"the revision budget ({maxRevisionAttempts}) is spent after {priorAttempts} attempt(s). The gate's disposition and approval path remains fully available, or start a new run.").ConfigureAwait(false);
        }

        var attemptNumber = priorAttempts + 1;
        await PublishAsync(runId, "SkeletonRevisionAttemptStarted",
            $"Revision attempt {attemptNumber} of {maxRevisionAttempts}: the human at the gate directed a revision answering {citedFindingIds.Count} cited finding(s). " +
            "A revision is human-directed, proposal-shaped work, never authority — the revised package needs its own critic review, dispositions, and hash-bound approval.",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["attemptNumber"] = attemptNumber.ToString(),
                ["maxRevisionAttempts"] = maxRevisionAttempts.ToString(),
                ["findingIds"] = string.Join(",", citedFindingIds),
                ["reason"] = request.Reason.Trim(),
                ["requestedByUserId"] = request.RequestedByUserId,
                ["supersedesPackageSha256"] = currentPackageHash,
                ["currentNode"] = "SkeletonRun"
            }, cancellationToken).ConfigureAwait(false);

        await _runs.TransitionAsync(new RunStateTransition
        {
            RunId = runId,
            State = RunLifecycleState.Running,
            Summary = $"Revision attempt {attemptNumber} building and testing in a fresh disposable workspace."
        }, cancellationToken).ConfigureAwait(false);

        async Task<TicketBuildRunDto> FailAttemptAsync(string failureKind, string failedCommand, string message)
        {
            await PublishAsync(runId, "SkeletonRevisionAttemptFailed",
                $"Revision attempt {attemptNumber} failed: {message} The previous gate package remains canonical and the run returns to the same human gate.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["attemptNumber"] = attemptNumber.ToString(),
                    ["failureKind"] = failureKind,
                    ["failedCommand"] = failedCommand,
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);

            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = runId,
                State = RunLifecycleState.PausedForApproval,
                Summary = $"Revision attempt {attemptNumber} failed; the previous gate package remains canonical. Halt is not approval."
            }, cancellationToken).ConfigureAwait(false);

            var current = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false) ?? run;
            return ToDto(current, projectId, ticketId, requiresHumanApproval: true);
        }

        var package = JsonSerializer.Deserialize<SkeletonCriticPackage>(System.Text.Encoding.UTF8.GetString(packageBytes), JsonOptions)!;

        BuilderProposal proposal;
        try
        {
            proposal = await _proposals.GenerateRevisionProposalAsync(ticketId, new SkeletonRevisionContext
            {
                AttemptNumber = attemptNumber,
                FindingIds = citedFindingIds,
                Instruction = request.Reason.Trim(),
                PreviousChanges = package.Changes
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await FailAttemptAsync("RevisionProposalGenerationFailed", string.Empty, exception.Message).ConfigureAwait(false);
        }

        var fileWrites = MapFileWrites(proposal);
        if (fileWrites.Count == 0)
        {
            return await FailAttemptAsync("RevisionProposalEmpty", string.Empty,
                "the revision proposal produced no valid file changes to exercise.").ConfigureAwait(false);
        }

        var proposalId = await PersistProposalEvidenceAsync(runId, evidenceRoot, proposal, cancellationToken, $"revise-{attemptNumber}").ConfigureAwait(false);
        var proposalEvidenceFileName = $"proposal-revise-{attemptNumber}.json";

        await PublishAsync(runId, "SkeletonRevisionProposalGenerated",
            $"Revision proposal {proposalId} generated with {fileWrites.Count} file change(s). A proposal is review material, not approval.",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["attemptNumber"] = attemptNumber.ToString(),
                ["proposalId"] = proposalId,
                ["fileChangeCount"] = fileWrites.Count.ToString(),
                ["modelProvider"] = proposal.ModelProvider ?? string.Empty,
                ["modelName"] = proposal.ModelName ?? string.Empty,
                ["currentNode"] = "SkeletonRun"
            }, cancellationToken).ConfigureAwait(false);

        // The Tester re-authors from the unchanged acceptance criteria — still
        // blind to the revised diff by contract. The revised package's coverage
        // matrix must be as honest as the original's.
        var authoring = await _testAuthoring.AuthorTestsAsync(new SkeletonTestAuthoringRequest
        {
            TicketId = ticketId,
            ProjectId = projectId,
            TicketTitle = ticket.Title ?? string.Empty,
            AcceptanceCriteria = ticket.AcceptanceCriteria ?? string.Empty,
            Problem = ticket.Problem ?? string.Empty
        }, cancellationToken).ConfigureAwait(false);
        var authoredTests = authoring.Succeeded ? SandboxTestPaths(authoring.Tests) : [];

        var allWrites = fileWrites
            .Concat(authoredTests.Select(test => new DisposableWorkspaceFileWrite
            {
                RelativePath = test.RelativePath,
                Content = test.Content
            }))
            .ToList();

        var workspaceResult = await _workspaces.RunAsync(new DisposableWorkspaceRunRequest
        {
            RunId = runId,
            SourcePath = project.LocalPath!,
            WorkspaceRoot = ResolveWorkspaceRoot(),
            EvidenceRoot = evidenceRoot,
            CleanWorkspaceOnSuccess = true,
            PreserveWorkspaceOnFailure = true,
            PreserveWorkspaceOnCancellation = true,
            OwnsRunLifecycle = false,
            // Attempt-scoped paths: a revision never erases a previous attempt.
            AttemptLabel = $"revise-{attemptNumber}",
            FileWrites = allWrites,
            Commands = DotNetCommandProfile.BuildAndTest(project.LocalPath!, ReadTimeout("BuildTimeoutSeconds"), ReadTimeout("TestTimeoutSeconds"))
        }, cancellationToken).ConfigureAwait(false);

        await PublishAsync(runId, "SkeletonEvidencePackaged",
            "Evidence packaged for the revision attempt. This run grants nothing: critic review, human approval, and any apply remain separate governed steps.",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["proposalId"] = proposalId,
                ["revisionAttempt"] = attemptNumber.ToString(),
                ["succeeded"] = workspaceResult.Succeeded.ToString().ToLowerInvariant(),
                ["evidencePath"] = workspaceResult.EvidencePath,
                ["commandCount"] = workspaceResult.Commands.Count.ToString(),
                ["currentNode"] = "SkeletonRun"
            }, cancellationToken).ConfigureAwait(false);

        if (!workspaceResult.Succeeded)
        {
            var classification = SkeletonBuildFailureClassifier.Classify(workspaceResult.Commands);
            return await FailAttemptAsync(classification.Kind.ToString(), classification.FailedCommand,
                $"{classification.Kind} on '{classification.FailedCommand}'. The failed attempt's workspace and evidence are preserved.").ConfigureAwait(false);
        }

        // The superseded package stays on disk as history — never erased. Only a
        // GREEN revision replaces the canonical gate package.
        File.Copy(packagePath, Path.Combine(Path.GetDirectoryName(packagePath)!, $"critic-package-superseded-{attemptNumber}.json"), overwrite: true);

        var (newPackagePath, criterionCount, uncoveredCriterionCount) = await PersistCriticPackageAsync(
            runId, evidenceRoot, proposalId, proposalEvidenceFileName, ticket, proposal, authoredTests, workspaceResult, cancellationToken).ConfigureAwait(false);
        var newPackageHash = ComputeSha256(await File.ReadAllBytesAsync(newPackagePath, cancellationToken).ConfigureAwait(false));

        await PublishAsync(runId, "CriticReviewPackageReady",
            "Revised critic review package prepared. A package is review material, not a review: the revised work needs its OWN critic review — the superseded package's review satisfies nothing." +
            (uncoveredCriterionCount > 0
                ? $" {uncoveredCriterionCount} of {criterionCount} acceptance criteria have NO covering test — the coverage hole is explicit in the package."
                : string.Empty),
            projectId, ticketId, new Dictionary<string, string>
            {
                ["packageId"] = $"critic-pkg-{runId}",
                ["packagePath"] = newPackagePath,
                ["packageSha256"] = newPackageHash,
                ["proposalId"] = proposalId,
                ["proposalEvidenceFile"] = proposalEvidenceFileName,
                ["criterionCount"] = criterionCount.ToString(),
                ["uncoveredCriterionCount"] = uncoveredCriterionCount.ToString(),
                ["revisionAttempt"] = attemptNumber.ToString(),
                ["currentNode"] = "SkeletonRun"
            }, cancellationToken).ConfigureAwait(false);

        // The cited findings are now answered: AddressedByRevision, decided by
        // the requesting human, recorded ONLY here — after the revision built
        // green and the revised package became canonical. A failed revision
        // records nothing, and the findings keep blocking.
        foreach (var findingId in citedFindingIds)
        {
            await _events.PublishAsync(new RunEventDto
            {
                RunId = runId,
                EventType = "SkeletonFindingDispositionRecorded",
                Message =
                    $"Finding {findingId} dispositioned as AddressedByRevision by the human who directed revision attempt {attemptNumber}. " +
                    "A disposition is a human decision about a finding; it is not approval — the revised package still needs its own critic review and accepted approval.",
                Payload = new Dictionary<string, string>
                {
                    ["findingId"] = findingId,
                    ["disposition"] = SkeletonFindingDispositionKind.AddressedByRevision.ToString(),
                    ["reason"] = $"Addressed by revision attempt {attemptNumber}: {request.Reason.Trim()}",
                    ["decidedByUserId"] = request.RequestedByUserId,
                    ["projectId"] = projectId.ToString(),
                    ["ticketId"] = ticketId.ToString(),
                    ["skeletonRun"] = "true",
                    ["currentNode"] = "SkeletonFindingDisposition"
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        await _runs.TransitionAsync(new RunStateTransition
        {
            RunId = runId,
            State = RunLifecycleState.PausedForApproval,
            Summary = "Halted for approval after revision. Halt is not approval: continuation requires a live accepted approval matching the REVISED package's requirement."
        }, cancellationToken).ConfigureAwait(false);

        await PublishAsync(runId, "ApprovalRequiredHalt",
            "Halted for approval after revision. The gate is exactly as hard as before: record a critic review of the revised package, disposition its findings, then record an accepted approval matching this requirement." +
            (uncoveredCriterionCount > 0
                ? $" NOTE: {uncoveredCriterionCount} of {criterionCount} acceptance criteria have no covering test — approving this run includes consciously owning that coverage hole."
                : string.Empty),
            projectId, ticketId, new Dictionary<string, string>
            {
                ["approvalProjectId"] = ApprovalProjectGuid(projectId).ToString("D"),
                ["approvalTargetKind"] = ApprovalTargetKind,
                ["approvalTargetId"] = runId,
                ["approvalTargetHash"] = newPackageHash,
                ["capabilityCode"] = ContinueCapabilityCode,
                ["criterionCount"] = criterionCount.ToString(),
                ["uncoveredCriterionCount"] = uncoveredCriterionCount.ToString(),
                ["currentNode"] = "SkeletonRun"
            }, cancellationToken).ConfigureAwait(false);

        var revised = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false) ?? run;
        return ToDto(revised, projectId, ticketId, requiresHumanApproval: revised.State == RunLifecycleState.PausedForApproval);
    }

    public Task<TicketBuildRunDto?> ApplyAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default) =>
        ApplyAsAsync(projectId, ticketId, runId, "unknown-user", cancellationToken);

    public Task<TicketBuildRunDto?> ApplyAsAsync(
        int projectId,
        long ticketId,
        string runId,
        string requestedByUserId,
        CancellationToken cancellationToken = default) =>
        ApplyAttemptAsync(projectId, ticketId, runId, "Start", "Initial governed apply request.", requestedByUserId, false, cancellationToken);

    public async Task<TicketBuildRunDto?> RecoverApplyAsync(
        int projectId,
        long ticketId,
        string runId,
        SkeletonApplyRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != projectId || run.TicketId != ticketId)
            return null;

        var action = request.Action?.Trim() ?? string.Empty;
        var reason = request.Reason?.Trim() ?? string.Empty;
        var actor = string.IsNullOrWhiteSpace(request.RequestedByUserId) ? "unknown-user" : request.RequestedByUserId.Trim();
        if (!SkeletonApplyRecoveryActions.IsSupported(action) || reason.Length == 0)
        {
            return await RefuseRecoveryAsync(run, projectId, ticketId, action, actor,
                "Recovery requires a supported action and the acting human's reason.", cancellationToken).ConfigureAwait(false);
        }

        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        var latest = SkeletonApplyAttemptProjector.Build(events).LastOrDefault();
        if (latest is null)
        {
            return await RefuseRecoveryAsync(run, projectId, ticketId, action, actor,
                "No attempt-scoped apply evidence exists to recover.", cancellationToken).ConfigureAwait(false);
        }

        if (!latest.AvailableActions.Contains(action, StringComparer.Ordinal))
        {
            return await RefuseRecoveryAsync(run, projectId, ticketId, action, actor,
                $"{action} is not safe for attempt {latest.AttemptId}. Mutation state is {latest.MutationState}; available actions are {string.Join(", ", latest.AvailableActions)}.",
                cancellationToken,
                latest).ConfigureAwait(false);
        }

        await PublishAsync(runId, "SkeletonApplyRecoveryDecision",
            $"{action} selected for apply attempt {latest.AttemptId}: {reason}",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["applyAttemptId"] = latest.AttemptId,
                ["attemptNumber"] = latest.AttemptNumber.ToString(),
                ["recoveryAction"] = action,
                ["reason"] = reason,
                ["requestedByUserId"] = actor,
                ["mutationState"] = latest.MutationState,
                ["currentNode"] = "SkeletonApplyRecovery"
            }, cancellationToken).ConfigureAwait(false);

        if (action == SkeletonApplyRecoveryActions.ManualReview)
            return ToDto(run, projectId, ticketId, requiresHumanApproval: false);

        if (action == SkeletonApplyRecoveryActions.Abandon)
        {
            await PublishAsync(runId, "SkeletonApplyAttemptAbandoned",
                $"Apply attempt {latest.AttemptId} was abandoned by {actor}. No retry was executed.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["applyAttemptId"] = latest.AttemptId,
                    ["attemptNumber"] = latest.AttemptNumber.ToString(),
                    ["reason"] = reason,
                    ["requestedByUserId"] = actor,
                    ["currentNode"] = "SkeletonApplyRecovery"
                }, cancellationToken).ConfigureAwait(false);
            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = runId,
                State = RunLifecycleState.Cancelled,
                Summary = $"Apply recovery abandoned by {actor}. Preserved attempt: {latest.AttemptId}."
            }, cancellationToken).ConfigureAwait(false);
            var abandoned = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false) ?? run;
            return ToDto(abandoned, projectId, ticketId, requiresHumanApproval: false);
        }

        return await ApplyAttemptAsync(projectId, ticketId, runId, action, reason, actor, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TicketBuildRunDto?> ApplyAttemptAsync(
        int projectId,
        long ticketId,
        string runId,
        string requestedAction,
        string recoveryReason,
        string requestedByUserId,
        bool isRecovery,
        CancellationToken cancellationToken)
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
        if (!isRecovery && SkeletonApplyAttemptProjector.Build(events).Count > 0)
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                "RecoveryDecisionRequired",
                "A prior apply attempt exists. Choose a backend-offered recovery action instead of starting apply again.",
                cancellationToken).ConfigureAwait(false);
        }
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

        // P1-3 / REVISE-1, re-checked at the mutation step: the review must bind
        // to the CURRENT package hash — a review of a superseded pre-revision
        // package satisfies nothing.
        if (!HasRecordedCriticReviewForPackage(events, packageHash))
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                "CriticReviewMissing",
                "no critic review is recorded for this run's current critic package. Source mutation cannot proceed on work the critic never reviewed.",
                cancellationToken).ConfigureAwait(false);
        }

        // P2-5, re-checked live at the mutation step: an upstream that applied
        // between continuation and apply still invalidates this run's evidence.
        var staleAtApply = await _driftDetector.DetectAsync(projectId, runId, ResolveEvidenceRoot(), cancellationToken).ConfigureAwait(false);
        if (staleAtApply is not null)
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                "StaleAfterUpstreamApply",
                staleAtApply,
                cancellationToken).ConfigureAwait(false);
        }

        // P1-3, re-checked live at the mutation step: a critic review recorded
        // AFTER continuation still binds — its findings must be dispositioned
        // before anything lands.
        var undispositionedAtApply = UndispositionedFindingIds(events);
        if (undispositionedAtApply.Count > 0)
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                "UndispositionedFindings",
                $"{undispositionedAtApply.Count} critic finding(s) have no human disposition. A finding is not a veto, but it cannot be ignored — record dispositions, then request apply again.",
                cancellationToken).ConfigureAwait(false);
        }
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

        var acceptedApproval = candidates.First(candidate => candidate.AcceptedApprovalId == satisfied.AcceptedApprovalId);
        var approvedByActorId = acceptedApproval.ApprovedByActorId;
        var applyAuthority = await EvaluateApplyAuthorityAsync(
            project.TenantId,
            projectId,
            requestedByUserId,
            acceptedApproval,
            cancellationToken).ConfigureAwait(false);
        if (!applyAuthority.IsAllowed)
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                applyAuthority.RefusedReason,
                applyAuthority.RefusedBecause,
                cancellationToken).ConfigureAwait(false);
        }

        var package = JsonSerializer.Deserialize<SkeletonCriticPackage>(System.Text.Encoding.UTF8.GetString(packageBytes), JsonOptions)!;

        // P2-4: overlapping applies take turns. The lease covers the approved
        // package's footprint; a refusal names the holder. A lease is a
        // concurrency guard, not authority — every gate above was checked
        // exactly as before, and holding the lease grants nothing.
        var lease = await _mutationLeases.TryAcquireAsync(new SkeletonMutationLeaseRequest
        {
            ProjectId = projectId,
            RunId = runId,
            TicketId = ticketId,
            FootprintPaths = package.Changes.Select(change => change.FilePath).ToList(),
            HolderRef = $"skeleton-run {runId} (ticket {ticketId})"
        }, cancellationToken).ConfigureAwait(false);

        if (!lease.Acquired)
        {
            return await RefuseApplyAsync(run, projectId, ticketId,
                "MutationLeaseHeld",
                lease.RefusedBecause,
                cancellationToken).ConfigureAwait(false);
        }

        var attemptNumber = SkeletonApplyAttemptProjector.Build(events).Count + 1;
        var applyAttemptId = $"{runId}-apply-{attemptNumber:D3}";
        var spineWorkspaceRoot = ResolveWorkspaceRoot();
        var attemptWorkspacePath = Path.Combine(spineWorkspaceRoot, applyAttemptId);
        var currentStage = string.Empty;

        try
        {
            await PublishAsync(runId, "SkeletonApplyAttemptStarted",
                $"Apply attempt {attemptNumber} started as a fresh preserved workspace ({requestedAction}).",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["applyAttemptId"] = applyAttemptId,
                    ["attemptNumber"] = attemptNumber.ToString(),
                    ["requestedAction"] = requestedAction,
                    ["requestedByUserId"] = requestedByUserId,
                    ["reason"] = recoveryReason,
                    ["workspacePath"] = attemptWorkspacePath,
                    ["currentNode"] = "SkeletonApply"
                }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(runId, "SkeletonApplyStarted",
                "Governed apply spine started: copy-only, evidence-chained, into and out of a prepared workspace. No commit, no push, no release.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["applyAttemptId"] = applyAttemptId,
                    ["attemptNumber"] = attemptNumber.ToString(),
                    ["acceptedApprovalId"] = satisfied.AcceptedApprovalId?.ToString("D") ?? string.Empty,
                    ["approvedByActorId"] = approvedByActorId,
                    ["requestedByUserId"] = NormalizeActorId(requestedByUserId),
                    ["approvalTargetHash"] = packageHash,
                    ["mutationLeaseId"] = lease.LeaseId,
                    ["currentNode"] = "SkeletonApply"
                }, cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(spineWorkspaceRoot);

            SkeletonApplySpine.SpineResult spineResult;
            try
            {
                spineResult = await new SkeletonApplySpine().RunAsync(
                    applyRunId: applyAttemptId,
                    sourceRepo: project.LocalPath!,
                    workspaceRoot: spineWorkspaceRoot,
                    approvedPackage: package,
                    approvedByActorId: approvedByActorId,
                    approvalReason: $"Mirrors AcceptedApprovalRecord {satisfied.AcceptedApprovalId:D} bound to package hash {packageHash}. This stage records the decision; it did not make it.",
                    onStageStarted: async stage =>
                    {
                        currentStage = stage;
                        await PublishAsync(runId, "SkeletonApplyStageStarted",
                            $"Apply attempt {attemptNumber} entered stage {stage}.",
                            projectId, ticketId, new Dictionary<string, string>
                            {
                                ["applyAttemptId"] = applyAttemptId,
                                ["attemptNumber"] = attemptNumber.ToString(),
                                ["stage"] = stage,
                                ["currentNode"] = "SkeletonApply"
                            }, cancellationToken).ConfigureAwait(false);
                    },
                    onStageCompleted: stage => PublishAsync(runId, "SkeletonApplyStage",
                        $"{stage.Stage}: {(stage.Succeeded ? "completed" : "blocked")} - {stage.Summary}",
                        projectId, ticketId, new Dictionary<string, string>
                        {
                            ["applyAttemptId"] = applyAttemptId,
                            ["attemptNumber"] = attemptNumber.ToString(),
                            ["stage"] = stage.Stage,
                            ["succeeded"] = stage.Succeeded.ToString().ToLowerInvariant(),
                            ["errors"] = string.Join(" | ", stage.Errors),
                            ["currentNode"] = "SkeletonApply"
                        }, cancellationToken),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await PublishInterruptedAsync(run, projectId, ticketId, applyAttemptId, attemptNumber, currentStage,
                    "The apply request was interrupted or cancelled. Reload backend truth before choosing recovery.").ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await PublishInterruptedAsync(run, projectId, ticketId, applyAttemptId, attemptNumber, currentStage,
                    $"The apply process stopped unexpectedly: {ex.Message}").ConfigureAwait(false);
                var interrupted = await _runs.GetAsync(runId, CancellationToken.None).ConfigureAwait(false) ?? run;
                return ToDto(interrupted, projectId, ticketId, requiresHumanApproval: false);
            }

            if (!spineResult.Succeeded)
            {
                return await RefuseApplyAsync(run, projectId, ticketId,
                    $"SpineBlocked:{spineResult.FailedStage}",
                    $"The governed apply spine blocked at '{spineResult.FailedStage}'. The workspace evidence chain records why; nothing was silently applied.",
                    cancellationToken,
                    applyAttemptId,
                    attemptNumber).ConfigureAwait(false);
            }

            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = runId,
                State = RunLifecycleState.Promoted,
                Summary = $"Apply spine succeeded under accepted approval {satisfied.AcceptedApprovalId:D}. The run is promoted to the canonical apply boundary.",
                WorkspacePath = spineResult.WorkspacePath
            }, cancellationToken).ConfigureAwait(false);

            await PublishAsync(runId, "SkeletonApplyPromoted",
                "Apply spine succeeded and the run was promoted to the canonical apply boundary. Promotion is not commit, push, release, or deployment authority.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["applyAttemptId"] = applyAttemptId,
                    ["attemptNumber"] = attemptNumber.ToString(),
                    ["acceptedApprovalId"] = satisfied.AcceptedApprovalId?.ToString("D") ?? string.Empty,
                    ["approvedByActorId"] = approvedByActorId,
                    ["requestedByUserId"] = NormalizeActorId(requestedByUserId),
                    ["workspacePath"] = spineResult.WorkspacePath,
                    ["currentNode"] = "SkeletonApply"
                }, cancellationToken).ConfigureAwait(false);

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
                    ["applyAttemptId"] = applyAttemptId,
                    ["attemptNumber"] = attemptNumber.ToString(),
                    ["acceptedApprovalId"] = satisfied.AcceptedApprovalId?.ToString("D") ?? string.Empty,
                    ["approvedByActorId"] = approvedByActorId,
                    ["requestedByUserId"] = NormalizeActorId(requestedByUserId),
                    ["workspacePath"] = spineResult.WorkspacePath,
                    ["evidenceChain"] = string.Join(",", spineResult.Stages.Select(stage => stage.Stage)),
                    ["currentNode"] = "SkeletonApply"
                }, cancellationToken).ConfigureAwait(false);

            var updated = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false) ?? run;
            return ToDto(updated, projectId, ticketId, requiresHumanApproval: false);
        }
        finally
        {
            // The lease releases no matter how the apply ends — a blocked apply
            // must never wedge the batch behind a dead lock.
            await _mutationLeases.ReleaseAsync(projectId, lease.LeaseId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<TicketBuildRunDto> RefuseApplyAsync(
        RunRecord run,
        int projectId,
        long ticketId,
        string refusedReason,
        string message,
        CancellationToken cancellationToken,
        string? applyAttemptId = null,
        int? attemptNumber = null)
    {
        var payload = new Dictionary<string, string>
        {
            ["refusedReason"] = refusedReason,
            ["currentNode"] = "SkeletonApply"
        };
        if (!string.IsNullOrWhiteSpace(applyAttemptId))
        {
            payload["applyAttemptId"] = applyAttemptId;
            payload["attemptNumber"] = attemptNumber?.ToString() ?? string.Empty;
        }

        await PublishAsync(run.RunId, "SkeletonApplyRefused",
            $"Apply refused: {message}",
            projectId, ticketId, payload, cancellationToken).ConfigureAwait(false);

        var current = await _runs.GetAsync(run.RunId, cancellationToken).ConfigureAwait(false) ?? run;
        return ToDto(current, projectId, ticketId, requiresHumanApproval: current.State == RunLifecycleState.PausedForApproval);
    }

    private async Task<TicketBuildRunDto> RefuseRecoveryAsync(
        RunRecord run,
        int projectId,
        long ticketId,
        string action,
        string actor,
        string message,
        CancellationToken cancellationToken,
        SkeletonRunApplyAttemptTrace? attempt = null)
    {
        await PublishAsync(run.RunId, "SkeletonApplyRecoveryRefused",
            $"Apply recovery refused: {message}",
            projectId, ticketId, new Dictionary<string, string>
            {
                ["recoveryAction"] = action,
                ["requestedByUserId"] = actor,
                ["refusedReason"] = message,
                ["currentNode"] = "SkeletonApplyRecovery"
            }, cancellationToken).ConfigureAwait(false);
        throw new SkeletonApplyRecoveryRefusedException(
            message,
            attempt?.AttemptId ?? string.Empty,
            attempt?.MutationState ?? string.Empty,
            attempt?.AvailableActions);
    }

    private Task PublishInterruptedAsync(
        RunRecord run,
        int projectId,
        long ticketId,
        string applyAttemptId,
        int attemptNumber,
        string stage,
        string message) =>
        PublishAsync(run.RunId, "SkeletonApplyInterrupted", message,
            projectId, ticketId, new Dictionary<string, string>
            {
                ["applyAttemptId"] = applyAttemptId,
                ["attemptNumber"] = attemptNumber.ToString(),
                ["stage"] = stage,
                ["currentNode"] = "SkeletonApplyRecovery"
            }, CancellationToken.None);

    private async Task<SkeletonAuthorityEvaluation> EvaluateContinuationAuthorityAsync(
        int tenantId,
        int projectId,
        string requestedByUserId,
        AcceptedApprovalRecord acceptedApproval,
        CancellationToken cancellationToken)
    {
        var requestedActorId = NormalizeActorId(requestedByUserId);
        var members = await _projectMemberships.GetMembersAsync(
            tenantId,
            projectId,
            TryParseActorUserId(requestedActorId),
            cancellationToken).ConfigureAwait(false);
        var eligible = members.Where(IsEligibleAuthorityMember).ToArray();
        var eligibleUserIds = eligible.Select(member => member.UserId.ToString()).ToArray();
        var continuationActor = eligible.FirstOrDefault(member => IsActor(member, requestedActorId));
        if (continuationActor is null)
        {
            return SkeletonAuthorityEvaluation.Refused(
                "ContinuationActorNotEligible",
                "the acting human is not an active Owner or Contributor on this project.",
                eligibleUserIds,
                ReadSoloApprovalExceptionAllowed());
        }

        var approvingActor = eligible.FirstOrDefault(member => IsActor(member, acceptedApproval.ApprovedByActorId));
        if (approvingActor is null)
        {
            return SkeletonAuthorityEvaluation.Refused(
                "AcceptedApprovalActorNotEligible",
                "the accepted approval was recorded by a user who is no longer an active Owner or Contributor on this project.",
                eligibleUserIds,
                ReadSoloApprovalExceptionAllowed());
        }

        var sameHuman = continuationActor.UserId == approvingActor.UserId;
        var allowSolo = ReadSoloApprovalExceptionAllowed();
        if (sameHuman && !allowSolo)
        {
            return SkeletonAuthorityEvaluation.Refused(
                "SelfApprovalRefused",
                "the same eligible human recorded the accepted approval and requested continuation. A different eligible human is required unless the solo exception is explicitly enabled.",
                eligibleUserIds,
                allowSolo);
        }

        return SkeletonAuthorityEvaluation.Allowed(eligibleUserIds, allowSolo, sameHuman && allowSolo);
    }

    private async Task<SkeletonAuthorityEvaluation> EvaluateApplyAuthorityAsync(
        int tenantId,
        int projectId,
        string requestedByUserId,
        AcceptedApprovalRecord acceptedApproval,
        CancellationToken cancellationToken)
    {
        var requestedActorId = NormalizeActorId(requestedByUserId);
        var members = await _projectMemberships.GetMembersAsync(
            tenantId,
            projectId,
            TryParseActorUserId(requestedActorId),
            cancellationToken).ConfigureAwait(false);
        var eligible = members.Where(IsEligibleAuthorityMember).ToArray();
        var eligibleUserIds = eligible.Select(member => member.UserId.ToString()).ToArray();

        if (!eligible.Any(member => IsActor(member, requestedActorId)))
        {
            return SkeletonAuthorityEvaluation.Refused(
                "ApplyActorNotEligible",
                "the acting human is not an active Owner or Contributor on this project.",
                eligibleUserIds,
                ReadSoloApprovalExceptionAllowed());
        }

        if (!eligible.Any(member => IsActor(member, acceptedApproval.ApprovedByActorId)))
        {
            return SkeletonAuthorityEvaluation.Refused(
                "AcceptedApprovalActorNotEligible",
                "the accepted approval was recorded by a user who is no longer an active Owner or Contributor on this project.",
                eligibleUserIds,
                ReadSoloApprovalExceptionAllowed());
        }

        return SkeletonAuthorityEvaluation.Allowed(eligibleUserIds, ReadSoloApprovalExceptionAllowed(), false);
    }

    private static bool IsEligibleAuthorityMember(ProjectMembershipEntry member) =>
        string.Equals(member.ProjectRole, ProjectMemberRoles.Owner, StringComparison.Ordinal) ||
        string.Equals(member.ProjectRole, ProjectMemberRoles.Contributor, StringComparison.Ordinal);

    private static bool IsActor(ProjectMembershipEntry member, string actorId) =>
        string.Equals(member.UserId.ToString(), NormalizeActorId(actorId), StringComparison.Ordinal) ||
        string.Equals(member.Email, actorId, StringComparison.OrdinalIgnoreCase);

    private static int TryParseActorUserId(string actorId) =>
        int.TryParse(actorId, out var userId) ? userId : 0;

    private static string NormalizeActorId(string? actorId) =>
        string.IsNullOrWhiteSpace(actorId) ? "unknown-user" : actorId.Trim();

    private bool ReadSoloApprovalExceptionAllowed() =>
        string.Equals(_configuration["SkeletonAuthority:AllowSoloApproval"], "true", StringComparison.OrdinalIgnoreCase);

    private sealed record SkeletonAuthorityEvaluation(
        bool IsAllowed,
        string RefusedReason,
        string RefusedBecause,
        IReadOnlyList<string> EligibleUserIds,
        bool SoloApprovalExceptionAllowed,
        bool SoloApprovalExceptionUsed)
    {
        public static SkeletonAuthorityEvaluation Allowed(
            IReadOnlyList<string> eligibleUserIds,
            bool soloApprovalExceptionAllowed,
            bool soloApprovalExceptionUsed) =>
            new(true, string.Empty, string.Empty, eligibleUserIds, soloApprovalExceptionAllowed, soloApprovalExceptionUsed);

        public static SkeletonAuthorityEvaluation Refused(
            string refusedReason,
            string refusedBecause,
            IReadOnlyList<string> eligibleUserIds,
            bool soloApprovalExceptionAllowed) =>
            new(false, refusedReason, refusedBecause, eligibleUserIds, soloApprovalExceptionAllowed, false);
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

        var agentConfigurations = events
            .Where(runEvent => runEvent.EventType == "AgentConfigurationSnapshotted")
            .Select(runEvent => new SkeletonRunAgentConfigurationSnapshot
            {
                SnapshotId = Payload(runEvent, "snapshotId"),
                WorkItemId = long.TryParse(Payload(runEvent, "workItemId"), out var workItemId) ? workItemId : ticketId,
                RunId = runId,
                Role = Payload(runEvent, "role"),
                ConnectionId = Payload(runEvent, "connectionId"),
                Provider = Payload(runEvent, "provider"),
                ControlledEndpointIdentity = Payload(runEvent, "controlledEndpointIdentity"),
                Model = Payload(runEvent, "model"),
                TimeoutSeconds = int.TryParse(Payload(runEvent, "timeoutSeconds"), out var timeout) ? timeout : 0,
                InputTokenLimit = int.TryParse(Payload(runEvent, "inputTokenLimit"), out var inputLimit) ? inputLimit : null,
                OutputTokenLimit = int.TryParse(Payload(runEvent, "outputTokenLimit"), out var outputLimit) ? outputLimit : null,
                Temperature = double.TryParse(Payload(runEvent, "temperature"), out var temperature) ? temperature : null,
                SkillVersion = Payload(runEvent, "skillVersion"),
                SkillHash = Payload(runEvent, "skillHash"),
                PersonalityVersion = Payload(runEvent, "personalityVersion"),
                PersonalityHash = Payload(runEvent, "personalityHash"),
                EffectiveProfileHash = Payload(runEvent, "effectiveProfileHash"),
                CreatedUtc = DateTimeOffset.TryParse(Payload(runEvent, "createdUtc"), out var createdUtc) ? createdUtc : runEvent.TimestampUtc,
                Boundary = Payload(runEvent, "boundary")
            })
            .ToArray();

        // Proposal link. `Proposal` is the FINAL/CURRENT proposal — the one the gate,
        // critic package, and approval hash bind to. When bounded repair or a
        // human-directed revision replaced the initial proposal, the original is
        // preserved separately as `InitialProposal`: history, never the gate proposal.
        SkeletonRunProposalTrace? proposalTrace = null;
        SkeletonRunProposalTrace? initialProposalTrace = null;
        var initialProposalEvent = events.FirstOrDefault(runEvent => runEvent.EventType == "ProposalGenerated");
        var lastRepairProposalEvent = events.LastOrDefault(runEvent => runEvent.EventType == "SkeletonRepairProposalGenerated");
        // REVISE-1: only a revision that built GREEN replaced the canonical gate
        // package — a failed attempt's proposal is history, never the gate proposal.
        var lastSuccessfulRevisionEvent = events.LastOrDefault(runEvent =>
            runEvent.EventType == "SkeletonRevisionProposalGenerated" &&
            !events.Any(failedEvent => failedEvent.EventType == "SkeletonRevisionAttemptFailed" &&
                Payload(failedEvent, "attemptNumber") == Payload(runEvent, "attemptNumber")));

        SkeletonRunProposalTrace BuildProposalTrace(RunEventDto sourceEvent, string evidenceFileName)
        {
            var proposalPath = Path.Combine(evidenceRoot, runId, "evidence", evidenceFileName);
            return new SkeletonRunProposalTrace
            {
                ProposalId = Payload(sourceEvent, "proposalId"),
                FileChangeCount = int.TryParse(Payload(sourceEvent, "fileChangeCount"), out var count) ? count : 0,
                EvidenceRef = proposalPath,
                EvidenceExistsOnDisk = File.Exists(proposalPath),
                ModelProvider = Payload(sourceEvent, "modelProvider"),
                ModelName = Payload(sourceEvent, "modelName")
            };
        }

        if (lastSuccessfulRevisionEvent is not null)
        {
            var revisionAttemptNumber = Payload(lastSuccessfulRevisionEvent, "attemptNumber");
            proposalTrace = BuildProposalTrace(lastSuccessfulRevisionEvent, $"proposal-revise-{revisionAttemptNumber}.json");
            if (initialProposalEvent is not null)
            {
                initialProposalTrace = BuildProposalTrace(initialProposalEvent, "proposal.json");
                if (!initialProposalTrace.EvidenceExistsOnDisk)
                    gaps.Add("Initial (pre-revision) proposal evidence file is missing from disk.");
            }
        }
        else if (lastRepairProposalEvent is not null)
        {
            var repairAttemptNumber = Payload(lastRepairProposalEvent, "attemptNumber");
            proposalTrace = BuildProposalTrace(lastRepairProposalEvent, $"proposal-repair-{repairAttemptNumber}.json");
            if (initialProposalEvent is not null)
            {
                initialProposalTrace = BuildProposalTrace(initialProposalEvent, "proposal.json");
                if (!initialProposalTrace.EvidenceExistsOnDisk)
                    gaps.Add("Initial (pre-repair) proposal evidence file is missing from disk.");
            }
        }
        else if (initialProposalEvent is not null)
        {
            proposalTrace = BuildProposalTrace(initialProposalEvent, "proposal.json");
        }

        if (proposalTrace is not null && !proposalTrace.EvidenceExistsOnDisk)
            gaps.Add("Proposal evidence file is missing from disk.");

        // Test authoring link (P0-5) — an explicit skip is a recorded outcome, not a gap.
        SkeletonRunTestAuthoringTrace? testAuthoringTrace = null;
        var authoredEvent = events.FirstOrDefault(runEvent => runEvent.EventType == "TestsAuthored");
        var authoringSkippedEvent = events.FirstOrDefault(runEvent => runEvent.EventType == "TestAuthoringSkipped");
        if (authoredEvent is not null)
        {
            testAuthoringTrace = new SkeletonRunTestAuthoringTrace
            {
                Authored = true,
                AuthoredTestCount = int.TryParse(Payload(authoredEvent, "authoredTestCount"), out var testCount) ? testCount : 0,
                ModelProvider = Payload(authoredEvent, "modelProvider"),
                ModelName = Payload(authoredEvent, "modelName")
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

        // REPAIR-1: bounded repair attempts, in order, from durable events. History,
        // not judgment — a run that needed repair says so.
        var repairAttempts = events
            .Where(runEvent => runEvent.EventType == "SkeletonRepairAttemptStarted")
            .Select(startEvent =>
            {
                var attemptNumber = int.TryParse(Payload(startEvent, "attemptNumber"), out var number) ? number : 0;
                var generatedEvent = events.FirstOrDefault(runEvent =>
                    runEvent.EventType == "SkeletonRepairProposalGenerated" &&
                    Payload(runEvent, "attemptNumber") == attemptNumber.ToString());
                var repairProposalPath = Path.Combine(evidenceRoot, runId, "evidence", $"proposal-repair-{attemptNumber}.json");
                return new SkeletonRunRepairAttemptTrace
                {
                    AttemptNumber = attemptNumber,
                    FailureKind = Payload(startEvent, "failureKind"),
                    FailedCommand = Payload(startEvent, "failedCommand"),
                    RepairProposalId = generatedEvent is null ? string.Empty : Payload(generatedEvent, "proposalId"),
                    ModelProvider = generatedEvent is null ? string.Empty : Payload(generatedEvent, "modelProvider"),
                    ModelName = generatedEvent is null ? string.Empty : Payload(generatedEvent, "modelName"),
                    RepairProposalEvidenceExistsOnDisk = File.Exists(repairProposalPath)
                };
            })
            .OrderBy(trace => trace.AttemptNumber)
            .ToList();

        // REVISE-1: every human-directed revision attempt, in order, from durable
        // events. History, not judgment — a failed attempt left the previous gate
        // package canonical, and the report says a revision was tried.
        var revisionAttempts = events
            .Where(runEvent => runEvent.EventType == "SkeletonRevisionAttemptStarted")
            .Select(startEvent =>
            {
                var attemptNumber = int.TryParse(Payload(startEvent, "attemptNumber"), out var number) ? number : 0;
                var generatedEvent = events.FirstOrDefault(runEvent =>
                    runEvent.EventType == "SkeletonRevisionProposalGenerated" &&
                    Payload(runEvent, "attemptNumber") == attemptNumber.ToString());
                var failedEvent = events.FirstOrDefault(runEvent =>
                    runEvent.EventType == "SkeletonRevisionAttemptFailed" &&
                    Payload(runEvent, "attemptNumber") == attemptNumber.ToString());
                var revisionProposalPath = Path.Combine(evidenceRoot, runId, "evidence", $"proposal-revise-{attemptNumber}.json");
                return new SkeletonRunRevisionAttemptTrace
                {
                    AttemptNumber = attemptNumber,
                    FindingIds = Payload(startEvent, "findingIds")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Reason = Payload(startEvent, "reason"),
                    RequestedByUserId = Payload(startEvent, "requestedByUserId"),
                    RevisionProposalId = generatedEvent is null ? string.Empty : Payload(generatedEvent, "proposalId"),
                    ModelProvider = generatedEvent is null ? string.Empty : Payload(generatedEvent, "modelProvider"),
                    ModelName = generatedEvent is null ? string.Empty : Payload(generatedEvent, "modelName"),
                    RevisionProposalEvidenceExistsOnDisk = File.Exists(revisionProposalPath),
                    Failed = failedEvent is not null,
                    FailureKind = failedEvent is null ? string.Empty : Payload(failedEvent, "failureKind"),
                    FailedCommand = failedEvent is null ? string.Empty : Payload(failedEvent, "failedCommand")
                };
            })
            .OrderBy(trace => trace.AttemptNumber)
            .ToList();

        // Critic package link — the hash is recomputed from disk, never just recited.
        // The LAST package-ready event announced the CURRENT canonical package: a
        // green revision replaces the gate package and announces it again.
        SkeletonRunCriticPackageTrace? packageTrace = null;
        var packageEvent = events.LastOrDefault(runEvent => runEvent.EventType == "CriticReviewPackageReady");
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
                HashVerified = hashVerified,
                CriterionCount = int.TryParse(Payload(packageEvent, "criterionCount"), out var criterionCount) ? criterionCount : 0,
                UncoveredCriterionCount = int.TryParse(Payload(packageEvent, "uncoveredCriterionCount"), out var uncoveredCount) ? uncoveredCount : 0
            };

            if (!existsOnDisk)
                gaps.Add("Critic package evidence is missing from disk.");
            else if (!hashVerified)
                gaps.Add("Critic package on disk no longer matches the hash announced at halt — the file changed after any approval bound to it.");
        }

        // Approval link: the requirement the run halted on, and the continuation that
        // consumed a verified accepted approval.
        SkeletonRunApprovalTrace? approvalTrace = null;
        // The LAST halt names the CURRENT requirement — after a revision, the gate
        // re-halted on the revised package's hash.
        var haltEvent = events.LastOrDefault(runEvent =>
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
                AcceptedApprovalId = unblockedEvent is null ? string.Empty : Payload(unblockedEvent, "acceptedApprovalId"),
                ApprovedByActorId = unblockedEvent is null ? string.Empty : Payload(unblockedEvent, "approvedByActorId"),
                ApprovedByActorDisplayName = unblockedEvent is null ? string.Empty : Payload(unblockedEvent, "approvedByActorDisplayName"),
                ContinuationRequestedByUserId = unblockedEvent is null ? string.Empty : Payload(unblockedEvent, "requestedByUserId"),
                SoloApprovalExceptionUsed = unblockedEvent is not null &&
                    string.Equals(Payload(unblockedEvent, "soloApprovalExceptionUsed"), "true", StringComparison.OrdinalIgnoreCase)
            };

            if (unblockedEvent is not null && haltEvent is null)
                gaps.Add("Continuation was unblocked but no approval halt was recorded first.");
            if (unblockedEvent is not null && string.IsNullOrEmpty(Payload(unblockedEvent, "acceptedApprovalId")))
                gaps.Add("Continuation was unblocked but the consumed accepted-approval id was not recorded.");
        }

        // Critic review links (P1-1): recorded reviews enter the report by
        // reference. Advisory only — their presence or absence grants nothing.
        var criticReviews = events
            .Where(runEvent => runEvent.EventType == "SkeletonCriticReviewRecorded")
            .Select(runEvent => new SkeletonRunCriticReviewTrace
            {
                CriticAgentRunId = Payload(runEvent, "criticAgentRunId"),
                ReviewId = Payload(runEvent, "reviewId"),
                Verdict = Payload(runEvent, "verdict"),
                FindingCount = int.TryParse(Payload(runEvent, "findingCount"), out var findingCount) ? findingCount : 0,
                BlockingFindingCount = int.TryParse(Payload(runEvent, "blockingFindingCount"), out var blockingCount) ? blockingCount : 0,
                FindingIds = Payload(runEvent, "findingIds")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                PackageSha256 = Payload(runEvent, "packageSha256"),
                GroundTruthCheckCount = int.TryParse(Payload(runEvent, "groundTruthCheckCount"), out var checkCount) ? checkCount : 0,
                GroundTruthMismatchCount = int.TryParse(Payload(runEvent, "groundTruthMismatchCount"), out var mismatchCount) ? mismatchCount : 0,
                ModelProvider = Payload(runEvent, "modelProvider"),
                ModelName = Payload(runEvent, "modelName")
            })
            .ToList();

        // Finding dispositions (P1-3): the human decisions recorded against findings.
        var findingDispositions = events
            .Where(runEvent => runEvent.EventType == "SkeletonFindingDispositionRecorded")
            .Select(runEvent => new SkeletonRunFindingDispositionTrace
            {
                FindingId = Payload(runEvent, "findingId"),
                Disposition = Payload(runEvent, "disposition"),
                Reason = Payload(runEvent, "reason"),
                DecidedByUserId = Payload(runEvent, "decidedByUserId")
            })
            .ToList();

        // Apply link (P0-4): stages from durable events, receipts checked on disk.
        SkeletonRunApplyTrace? applyTrace = null;
        var applyAttempts = SkeletonApplyAttemptProjector.Build(events)
            .Select(attempt => attempt with
            {
                Receipts = string.IsNullOrWhiteSpace(attempt.WorkspacePath)
                    ? []
                    : ApplyReceiptChain.Select(receiptName =>
                    {
                        var receiptPath = Path.Combine(attempt.WorkspacePath, ".irondev", "runs", attempt.AttemptId, receiptName);
                        return new SkeletonRunReceiptRef
                        {
                            Name = receiptName,
                            Path = receiptPath,
                            ExistsOnDisk = File.Exists(receiptPath)
                        };
                    }).ToArray()
            })
            .ToArray();
        var latestApplyAttempt = applyAttempts.LastOrDefault();
        var appliedEvent = events.LastOrDefault(runEvent => runEvent.EventType == "SkeletonApplied");
        var refusedEvent = events.LastOrDefault(runEvent => runEvent.EventType == "SkeletonApplyRefused");
        var stageEvents = events
            .Where(runEvent => runEvent.EventType == "SkeletonApplyStage" &&
                (latestApplyAttempt is null || string.Equals(Payload(runEvent, "applyAttemptId"), latestApplyAttempt.AttemptId, StringComparison.Ordinal)))
            .ToList();
        if (appliedEvent is not null || refusedEvent is not null || stageEvents.Count > 0 || latestApplyAttempt is not null)
        {
            var workspacePath = latestApplyAttempt?.WorkspacePath ?? (appliedEvent is null
                ? run.WorkspacePath ?? string.Empty
                : Payload(appliedEvent, "workspacePath"));

            var receipts = latestApplyAttempt?.Receipts.ToList() ?? [];
            if (appliedEvent is not null && receipts.Count > 0)
            {
                foreach (var receipt in receipts)
                {
                    if (!receipt.ExistsOnDisk)
                        gaps.Add($"Apply receipt '{receipt.Name}' is missing from the workspace evidence chain.");
                }
            }
            else if (appliedEvent is not null && latestApplyAttempt is null && !string.IsNullOrEmpty(workspacePath))
            {
                var receiptDir = Path.Combine(workspacePath, ".irondev", "runs", $"{runId}-apply");
                foreach (var receiptName in ApplyReceiptChain)
                {
                    var receiptPath = Path.Combine(receiptDir, receiptName);
                    var receiptExists = File.Exists(receiptPath);
                    receipts.Add(new SkeletonRunReceiptRef { Name = receiptName, Path = receiptPath, ExistsOnDisk = receiptExists });
                    if (!receiptExists)
                        gaps.Add($"Apply receipt '{receiptName}' is missing from the workspace evidence chain.");
                }
            }

            applyTrace = new SkeletonRunApplyTrace
            {
                Applied = latestApplyAttempt?.Status == SkeletonApplyAttemptStatuses.Applied ||
                    (latestApplyAttempt is null && appliedEvent is not null),
                WorkspacePath = workspacePath,
                RefusedReason = latestApplyAttempt?.RefusedReason ??
                    (appliedEvent is null && refusedEvent is not null ? Payload(refusedEvent, "refusedReason") : string.Empty),
                Stages = stageEvents
                    .Select(stageEvent => new SkeletonRunApplyStageTrace
                    {
                        Stage = Payload(stageEvent, "stage"),
                        Succeeded = string.Equals(Payload(stageEvent, "succeeded"), "true", StringComparison.OrdinalIgnoreCase),
                        Errors = Payload(stageEvent, "errors")
                    })
                    .ToList(),
                Receipts = receipts,
                Attempts = applyAttempts
            };
        }

        if (run.State == RunLifecycleState.Applied)
        {
            if (approvalTrace is not { ContinuationUnblocked: true })
                gaps.Add("The run is Applied but no continuation-unblocked event was recorded.");
            if (applyTrace is not { Applied: true })
                gaps.Add("The run is Applied but no applied event was recorded.");
        }

        // P2-5: a halted or continued run whose evidence predates an overlapping
        // upstream apply is stale — named in the report so the human sees it
        // before spending an approval on a run the world already invalidated.
        if (run.State is RunLifecycleState.PausedForApproval or RunLifecycleState.Completed)
        {
            var staleReason = await _driftDetector.DetectAsync(projectId, runId, ResolveEvidenceRoot(), cancellationToken).ConfigureAwait(false);
            if (staleReason is not null)
                gaps.Add(staleReason);
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
            AgentConfigurations = agentConfigurations,
            Proposal = proposalTrace,
            InitialProposal = initialProposalTrace,
            TestAuthoring = testAuthoringTrace,
            CriticPackage = packageTrace,
            Approval = approvalTrace,
            CriticReviews = criticReviews,
            FindingDispositions = findingDispositions,
            RepairAttempts = repairAttempts,
            RevisionAttempts = revisionAttempts,
            Apply = applyTrace,
            Gaps = gaps,
            LoopComplete = loopComplete
        };
    }

    private static string Payload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) ? value : string.Empty;

    /// <summary>
    /// P1-3: finding ids from recorded critic reviews minus recorded dispositions,
    /// computed over durable event strings alone — the orchestrator enforces the
    /// invariant without ever touching critic types.
    /// </summary>
    private static IReadOnlyList<string> UndispositionedFindingIds(IReadOnlyList<RunEventDto> events)
    {
        var dispositioned = events
            .Where(runEvent => runEvent.EventType == "SkeletonFindingDispositionRecorded")
            .Select(runEvent => Payload(runEvent, "findingId"))
            .Where(findingId => !string.IsNullOrEmpty(findingId))
            .ToHashSet(StringComparer.Ordinal);

        return events
            .Where(runEvent => runEvent.EventType == "SkeletonCriticReviewRecorded")
            .SelectMany(runEvent => Payload(runEvent, "findingIds")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .Where(findingId => !dispositioned.Contains(findingId))
            .ToList();
    }

    /// <summary>
    /// REVISE-1: a review counts only when it binds to the CURRENT package hash —
    /// a review of a superseded pre-revision package satisfies nothing.
    /// </summary>
    private static bool HasRecordedCriticReviewForPackage(IReadOnlyList<RunEventDto> events, string packageHash) =>
        events.Any(runEvent =>
            string.Equals(runEvent.EventType, "SkeletonCriticReviewRecorded", StringComparison.Ordinal) &&
            string.Equals(Payload(runEvent, "packageSha256"), packageHash, StringComparison.Ordinal));

    private async Task CaptureAgentConfigurationSnapshotsAsync(
        string runId,
        int tenantId,
        int projectId,
        long ticketId,
        CancellationToken cancellationToken)
    {
        var profiles = await _agentProfiles.ListEffectiveAsync(tenantId, projectId, cancellationToken).ConfigureAwait(false);
        foreach (var profile in profiles.Where(item => item.Role != SkeletonAgentRole.Orchestrator))
        {
            var createdUtc = DateTimeOffset.UtcNow;
            var connectionId = profile.AiConnectionId?.Trim() ?? string.Empty;
            await PublishAsync(runId, "AgentConfigurationSnapshotted",
                $"Immutable non-secret {SkeletonAgentRoles.DisplayName(profile.Role)} configuration captured for this run.",
                projectId, ticketId, new Dictionary<string, string>
                {
                    ["snapshotId"] = Guid.NewGuid().ToString("N"),
                    ["workItemId"] = ticketId.ToString(),
                    ["role"] = profile.Role.ToString(),
                    ["connectionId"] = connectionId,
                    ["provider"] = profile.Provider,
                    ["controlledEndpointIdentity"] = string.IsNullOrWhiteSpace(connectionId) ? "not-configured" : connectionId,
                    ["model"] = profile.Model,
                    ["timeoutSeconds"] = profile.TimeoutSeconds.ToString(),
                    ["inputTokenLimit"] = string.Empty,
                    ["outputTokenLimit"] = string.Empty,
                    ["temperature"] = string.Empty,
                    ["skillVersion"] = ProfileFieldVersion(profile, "effectiveSkill"),
                    ["skillHash"] = TextHash(profile.EffectiveSkill),
                    ["personalityVersion"] = ProfileFieldVersion(profile, "effectivePersonality"),
                    ["personalityHash"] = TextHash(profile.EffectivePersonality),
                    ["effectiveProfileHash"] = profile.EffectiveHash,
                    ["createdUtc"] = createdUtc.ToString("O"),
                    ["boundary"] = SkeletonRunAgentConfigurationSnapshot.BoundaryText,
                    ["currentNode"] = "SkeletonRun"
                }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ProfileFieldVersion(EffectiveSkeletonAgentProfile profile, string field)
    {
        var source = profile.FieldSources.FirstOrDefault(item => string.Equals(item.Field, field, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(source?.Version))
            return source.Version;
        if (!string.IsNullOrWhiteSpace(profile.ProjectProfileVersion))
            return profile.ProjectProfileVersion;
        if (!string.IsNullOrWhiteSpace(profile.TenantProfileVersion))
            return profile.TenantProfileVersion;
        return string.Equals(source?.SourceLayer, "RoleOverride", StringComparison.Ordinal)
            ? "unversioned-role-override"
            : profile.BuiltInDefaultVersion;
    }

    private static string TextHash(string value) =>
        "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

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

    private static async Task<(string PackagePath, int CriterionCount, int UncoveredCriterionCount)> PersistCriticPackageAsync(
        string runId,
        string evidenceRoot,
        string proposalId,
        string proposalEvidenceFileName,
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

        // The package references the CURRENT proposal's evidence — after a repair,
        // that is the repaired proposal that actually built green and reached the
        // gate, never the original failed attempt by default.
        var evidenceRefs = new List<string>
        {
            Path.Combine(evidenceRoot, runId, "evidence", proposalEvidenceFileName),
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
        return (
            packagePath,
            package.CriterionCoverage.Count,
            package.CriterionCoverage.Count(coverage => !coverage.Covered));
    }

    /// <summary>
    /// SkeletonRepair:MaxAttempts — how many bounded repair attempts a failed
    /// build/test may trigger. Default 0: repair is off unless explicitly
    /// configured, and a failure is a terminal named state. Clamped to 3 so no
    /// configuration can turn bounded repair into an unbounded retry loop.
    /// </summary>
    private int ReadMaxRepairAttempts() =>
        int.TryParse(_configuration["SkeletonRepair:MaxAttempts"], out var configured)
            ? Math.Clamp(configured, 0, 3)
            : 0;

    /// <summary>
    /// SkeletonRevision:MaxAttempts — how many human-directed revision attempts
    /// a halted run may make. Default 0: revision is off unless explicitly
    /// configured. Clamped to 3 so no configuration can turn bounded revision
    /// into an unbounded rework loop.
    /// </summary>
    private int ReadMaxRevisionAttempts() =>
        int.TryParse(_configuration["SkeletonRevision:MaxAttempts"], out var configured)
            ? Math.Clamp(configured, 0, 3)
            : 0;

    private static async Task<string> PersistProposalEvidenceAsync(
        string runId,
        string evidenceRoot,
        BuilderProposal proposal,
        CancellationToken cancellationToken,
        string attemptLabel = "")
    {
        // Attempt-suffixed evidence: a repair proposal never overwrites the
        // original attempt's proposal.json — attempt history is never erased.
        var suffix = string.IsNullOrWhiteSpace(attemptLabel) ? string.Empty : $"-{attemptLabel}";
        var proposalId = $"prop-{runId}{suffix}";
        var runEvidenceRoot = Path.Combine(evidenceRoot, runId, "evidence");
        Directory.CreateDirectory(runEvidenceRoot);
        var proposalPath = Path.Combine(runEvidenceRoot, $"proposal{suffix}.json");

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
