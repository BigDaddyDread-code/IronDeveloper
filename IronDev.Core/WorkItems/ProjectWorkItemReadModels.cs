using IronDev.Core.Builder;
using IronDev.Core.Models;
using IronDev.Core.Runs;
using IronDev.Data.Models;

namespace IronDev.Core.WorkItems;

public static class ProjectWorkItemStages
{
    public const string Shape = "Shape";
    public const string Ticket = "Ticket";
    public const string Build = "Build";
    public const string Review = "Review";
    public const string Done = "Done";
}

public static class ProjectWorkItemActionKinds
{
    public const string ResolveReadiness = "ResolveReadiness";
    public const string StartRun = "StartRun";
    public const string RefreshRun = "RefreshRun";
    public const string Review = "Review";
    public const string Apply = "Apply";
    public const string RecoverApply = "RecoverApply";
    public const string RepairOrRetry = "RepairOrRetry";
    public const string ViewOutcome = "ViewOutcome";
}

public static class ProjectWorkItemApplyRecoveryStatuses
{
    public const string NotRequired = "NotRequired";
    public const string ApplyRefused = "ApplyRefused";
    public const string ApplyInProgress = "ApplyInProgress";
    public const string RecoveryEvidenceMissing = "RecoveryEvidenceMissing";
    public const string RetryReady = "RetryReady";
    public const string Interrupted = "Interrupted";
    public const string ManualReviewRequired = "ManualReviewRequired";
    public const string Abandoned = "Abandoned";
    public const string Applied = "Applied";
}

public static class ProjectWorkItemExecutionProofStatuses
{
    public const string NoRun = "NoRun";
    public const string InProgress = "InProgress";
    public const string ProofMissing = "ProofMissing";
    public const string ExecutionObserved = "ExecutionObserved";
    public const string LoopVerified = "LoopVerified";
}

public sealed record ProjectWorkItemReadModel
{
    public int ProjectId { get; init; }
    public long WorkItemId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Stage { get; init; } = ProjectWorkItemStages.Shape;
    public string State { get; init; } = string.Empty;
    public string StatusSummary { get; init; } = string.Empty;
    public DateTimeOffset LastMeaningfulEventUtc { get; init; }
    public required ProjectTicket Ticket { get; init; }
    public required ProjectWorkItemContractReadModel Contract { get; init; }
    public required ProjectWorkItemCollaborationReadModel Collaboration { get; init; }
    public ProjectWorkItemRunReadModel? LatestRun { get; init; }
    public required ProjectWorkItemGateReadModel Gate { get; init; }
    public required ProjectWorkItemActionReadModel PrimaryAction { get; init; }
    public required ProjectWorkItemApplyRecoveryReadModel ApplyRecovery { get; init; }
    public required ProjectWorkItemExecutionProofReadModel ExecutionProof { get; init; }
    public required ProjectWorkItemEvidenceLinksReadModel EvidenceLinks { get; init; }
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "The Work Item projection reports ticket, run, gate, and evidence truth. It starts no run, grants no approval, " +
        "continues no workflow, and applies no source. Missing collaboration data remains empty rather than inferred.";
}

public sealed record ProjectWorkItemExecutionProofReadModel
{
    public string Status { get; init; } = ProjectWorkItemExecutionProofStatuses.NoRun;
    public bool HasRunRecord { get; init; }
    public bool ExecutionStarted { get; init; }
    public bool ExecutionCompleted { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public int DurableExecutionEventCount { get; init; }
    public IReadOnlyList<string> DurableExecutionEvents { get; init; } = [];
    public bool BuildAndTestExecutionObserved { get; init; }
    public bool ApplyExecutionObserved { get; init; }
    public bool LoopVerified { get; init; }
    public bool ArtifactEvidenceObserved { get; init; }
    public bool ArtifactEvidenceProvesExecution { get; init; }
    public IReadOnlyList<string> Gaps { get; init; } = [];
    public string Reason { get; init; } = string.Empty;
    public string NextSafeAction { get; init; } = string.Empty;
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "Execution proof requires a durable run record and durable execution events. Proposals, packages, files, " +
        "receipts, timestamps, and selected tests do not prove execution by themselves.";
}

public sealed record ProjectWorkItemApplyRecoveryReadModel
{
    public string Status { get; init; } = ProjectWorkItemApplyRecoveryStatuses.NotRequired;
    public bool Required { get; init; }
    public bool ApplyAttemptObserved { get; init; }
    public bool PartialMutationPossible { get; init; }
    public int SucceededStageCount { get; init; }
    public int FailedStageCount { get; init; }
    public IReadOnlyList<string> FailedStages { get; init; } = [];
    public IReadOnlyList<string> TechnicalDetails { get; init; } = [];
    public int ExistingReceiptCount { get; init; }
    public int MissingReceiptCount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string NextSafeAction { get; init; } = string.Empty;
    public bool RetryAllowed { get; init; }
    public bool HumanReviewRequired { get; init; }
    public string ApplyAttemptId { get; init; } = string.Empty;
    public int ApplyAttemptNumber { get; init; }
    public string AttemptStatus { get; init; } = string.Empty;
    public string MutationState { get; init; } = SkeletonApplyMutationStates.NotObserved;
    public IReadOnlyList<string> AvailableActions { get; init; } = [];
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "Apply recovery actions are constrained by durable attempt and stage evidence. A retry or resume always creates " +
        "a fresh attempt; uncertain source mutation requires manual review or abandon and is never retried automatically.";
}

public sealed record ProjectWorkItemContractReadModel
{
    public int AcceptanceCriterionCount { get; init; }
    public int AffectedFileCount { get; init; }
    public bool HasAcceptanceCriteria { get; init; }
    public IReadOnlyList<string> AffectedFiles { get; init; } = [];
    public long? SourceChatSessionId { get; init; }
    public long? SourceChatMessageId { get; init; }
    public long? SourceDocumentVersionId { get; init; }
}

public sealed record ProjectWorkItemCollaborationReadModel
{
    public long Revision { get; init; }
    public ProjectWorkItemActorReadModel? Assignee { get; init; }
    public IReadOnlyList<ProjectWorkItemActorReadModel> Followers { get; init; } = [];
    public ProjectWorkItemActorReadModel? WaitingOn { get; init; }
    public long? LinkedChatSessionId { get; init; }
    public IReadOnlyList<ProjectWorkItemActivityReadModel> RecentActivity { get; init; } = [];
}

public sealed record ProjectWorkItemActorReadModel
{
    public string Kind { get; init; } = string.Empty;
    public int? UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}

public sealed record ProjectWorkItemActivityReadModel
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public ProjectWorkItemActorReadModel? Actor { get; init; }
}

public sealed record ProjectWorkItemRunReadModel
{
    public string RunId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public int RepairAttemptCount { get; init; }
    public int FindingCount { get; init; }
    public int UnresolvedFindingCount { get; init; }
    public bool ApprovalHaltObserved { get; init; }
    public bool ContinuationUnblocked { get; init; }
    public bool Applied { get; init; }
    public bool LoopComplete { get; init; }
    public int EvidenceGapCount { get; init; }
}

public sealed record ProjectWorkItemGateReadModel
{
    public string State { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string NextSafeAction { get; init; } = string.Empty;
    public IReadOnlyList<string> TechnicalDetails { get; init; } = [];
}

public sealed record ProjectWorkItemActionReadModel
{
    public string Kind { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool Allowed { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record ProjectWorkItemEvidenceLinksReadModel
{
    public string? RunReportApiPath { get; init; }
    public string? CriticPackageApiPath { get; init; }
    public string GovernanceLibraryPath { get; init; } = string.Empty;
}

public interface IProjectWorkItemReadService
{
    Task<ProjectWorkItemReadModel?> GetAsync(
        int projectId,
        long workItemId,
        CancellationToken cancellationToken = default);
}

public static class ProjectWorkItemProjector
{
    public static ProjectWorkItemReadModel Build(
        ProjectTicket ticket,
        RunRecord? latestRun,
        SkeletonRunReport? report,
        BuildReadinessResult readiness,
        DateTimeOffset generatedUtc,
        ProjectWorkItemCollaborationSnapshot? collaboration = null)
    {
        var stage = StageFor(ticket, latestRun);
        var waitingOn = WaitingOn(ticket, latestRun, readiness);
        var gate = GateFor(ticket, latestRun, report, readiness);
        var action = ActionFor(latestRun, readiness, report);
        var affectedFiles = SplitValues(ticket.LinkedFilePaths);
        var runActivity = (report?.Timeline ?? [])
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(8)
            .Select(entry => new ProjectWorkItemActivityReadModel
            {
                TimestampUtc = entry.TimestampUtc,
                Kind = entry.EventType,
                Summary = entry.Message,
                Actor = null
            })
            .ToArray();
        var collaborationActivity = (collaboration?.RecentActivity ?? [])
            .Select(entry => new ProjectWorkItemActivityReadModel
            {
                TimestampUtc = entry.TimestampUtc,
                Kind = entry.Kind,
                Summary = entry.Summary,
                Actor = MapActor(entry.Actor)
            });
        var activity = collaborationActivity.Concat(runActivity)
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(8)
            .ToArray();

        return new ProjectWorkItemReadModel
        {
            ProjectId = ticket.ProjectId,
            WorkItemId = ticket.Id,
            Title = string.IsNullOrWhiteSpace(ticket.Title) ? $"Work item {ticket.Id}" : ticket.Title,
            Stage = stage,
            State = latestRun?.State.ToString() ?? Normalize(ticket.Status, "Unknown"),
            StatusSummary = StatusSummary(ticket, latestRun),
            LastMeaningfulEventUtc = latestRun?.UpdatedUtc ?? ToUtc(ticket.CreatedDate, generatedUtc),
            Ticket = ticket,
            Contract = new ProjectWorkItemContractReadModel
            {
                AcceptanceCriterionCount = CountCriteria(ticket.AcceptanceCriteria),
                AffectedFileCount = affectedFiles.Count,
                HasAcceptanceCriteria = !string.IsNullOrWhiteSpace(ticket.AcceptanceCriteria),
                AffectedFiles = affectedFiles,
                SourceChatSessionId = ticket.SourceChatSessionId,
                SourceChatMessageId = ticket.SourceChatMessageId,
                SourceDocumentVersionId = ticket.SourceDocumentVersionId
            },
            Collaboration = new ProjectWorkItemCollaborationReadModel
            {
                Revision = collaboration?.Revision ?? 0,
                Assignee = MapActor(collaboration?.Assignee),
                Followers = (collaboration?.Followers ?? []).Select(MapActor).Where(actor => actor is not null).Cast<ProjectWorkItemActorReadModel>().ToArray(),
                WaitingOn = MapActor(collaboration?.WaitingOn) ?? waitingOn,
                LinkedChatSessionId = ticket.SourceChatSessionId,
                RecentActivity = activity
            },
            LatestRun = RunModel(latestRun, report),
            Gate = gate,
            PrimaryAction = action,
            ApplyRecovery = ApplyRecoveryFor(report),
            ExecutionProof = ExecutionProofFor(latestRun, report),
            EvidenceLinks = new ProjectWorkItemEvidenceLinksReadModel
            {
                RunReportApiPath = latestRun is null
                    ? null
                    : $"/api/projects/{ticket.ProjectId}/tickets/{ticket.Id}/skeleton-runs/{Uri.EscapeDataString(latestRun.RunId)}/report",
                CriticPackageApiPath = latestRun is null || report?.CriticPackage is null
                    ? null
                    : $"/api/projects/{ticket.ProjectId}/tickets/{ticket.Id}/skeleton-runs/{Uri.EscapeDataString(latestRun.RunId)}/critic-package",
                GovernanceLibraryPath = $"/projects/{ticket.ProjectId}/library/governance"
            }
        };
    }

    private static ProjectWorkItemActorReadModel? MapActor(ProjectWorkItemCollaborator? actor) => actor is null
        ? null
        : new ProjectWorkItemActorReadModel { Kind = actor.Kind, UserId = actor.UserId, DisplayName = actor.DisplayName };

    private static ProjectWorkItemExecutionProofReadModel ExecutionProofFor(RunRecord? run, SkeletonRunReport? report)
    {
        if (run is null)
        {
            return new ProjectWorkItemExecutionProofReadModel
            {
                Status = ProjectWorkItemExecutionProofStatuses.NoRun,
                Reason = "No durable run record exists for this Work Item.",
                NextSafeAction = "Start a governed run when build readiness allows it."
            };
        }

        var executionEventNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "SkeletonEvidencePackaged",
            "SkeletonApplyStarted",
            "SkeletonApplyStage",
            "SkeletonApplied"
        };
        var executionEvents = (report?.Timeline ?? [])
            .Where(entry => executionEventNames.Contains(entry.EventType))
            .Select(entry => entry.EventType)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var buildAndTestObserved = executionEvents.Contains("SkeletonEvidencePackaged", StringComparer.Ordinal);
        var applyObserved = executionEvents.Any(entry => entry is "SkeletonApplyStarted" or "SkeletonApplyStage" or "SkeletonApplied") ||
            report?.Apply?.Stages.Count > 0 || report?.Apply?.Applied == true;
        var artifactObserved = report is not null &&
            (report.Proposal is not null || report.CriticPackage is not null || report.Apply?.Receipts.Count > 0);
        var loopVerified = run.State == RunLifecycleState.Applied && report?.LoopComplete == true;
        var inProgress = run.State is RunLifecycleState.Created or RunLifecycleState.Running;
        var executionStarted = run.StartedUtc.HasValue || inProgress || executionEvents.Length > 0;
        var executionCompleted = run.CompletedUtc.HasValue || run.State is
            RunLifecycleState.Failed or RunLifecycleState.Cancelled or RunLifecycleState.Applied;
        var gaps = report?.Gaps ?? [];
        var status = loopVerified
            ? ProjectWorkItemExecutionProofStatuses.LoopVerified
            : inProgress
                ? ProjectWorkItemExecutionProofStatuses.InProgress
                : executionEvents.Length > 0
                    ? ProjectWorkItemExecutionProofStatuses.ExecutionObserved
                    : ProjectWorkItemExecutionProofStatuses.ProofMissing;

        var reason = status switch
        {
            ProjectWorkItemExecutionProofStatuses.LoopVerified =>
                "The durable run reached Applied and the report verified every required loop link.",
            ProjectWorkItemExecutionProofStatuses.InProgress =>
                "A durable run is in progress; terminal execution proof is not available yet.",
            ProjectWorkItemExecutionProofStatuses.ExecutionObserved when gaps.Count > 0 =>
                $"Durable execution events were observed, but the report names {gaps.Count} evidence gap(s).",
            ProjectWorkItemExecutionProofStatuses.ExecutionObserved =>
                "Durable execution events were observed. The governed loop is not yet fully verified.",
            _ => artifactObserved
                ? "Artifacts were observed, but no durable execution event proves they were executed."
                : "The run record has no durable execution event in the current report."
        };
        var nextSafeAction = status switch
        {
            ProjectWorkItemExecutionProofStatuses.LoopVerified => "Inspect the final report and receipts.",
            ProjectWorkItemExecutionProofStatuses.InProgress => "Wait for the run to publish its next durable state.",
            ProjectWorkItemExecutionProofStatuses.ExecutionObserved when gaps.Count > 0 =>
                "Inspect and resolve the named evidence gaps before treating the loop as verified.",
            ProjectWorkItemExecutionProofStatuses.ExecutionObserved =>
                "Continue through the current backend gate; execution observed is not loop completion.",
            _ => "Refresh the run report or inspect the missing durable execution evidence."
        };

        return new ProjectWorkItemExecutionProofReadModel
        {
            Status = status,
            HasRunRecord = true,
            ExecutionStarted = executionStarted,
            ExecutionCompleted = executionCompleted,
            StartedUtc = run.StartedUtc,
            CompletedUtc = run.CompletedUtc,
            DurableExecutionEventCount = executionEvents.Length,
            DurableExecutionEvents = executionEvents,
            BuildAndTestExecutionObserved = buildAndTestObserved,
            ApplyExecutionObserved = applyObserved,
            LoopVerified = loopVerified,
            ArtifactEvidenceObserved = artifactObserved,
            ArtifactEvidenceProvesExecution = false,
            Gaps = gaps,
            Reason = reason,
            NextSafeAction = nextSafeAction
        };
    }

    private static ProjectWorkItemApplyRecoveryReadModel ApplyRecoveryFor(SkeletonRunReport? report)
    {
        var apply = report?.Apply;
        if (apply is null)
        {
            return Recovery(
                ProjectWorkItemApplyRecoveryStatuses.NotRequired,
                "No apply attempt or refusal was recorded for the latest run.",
                "No apply recovery action is required.");
        }

        var latestAttempt = apply.Attempts.LastOrDefault();
        if (latestAttempt is not null)
        {
            if (latestAttempt.Status == SkeletonApplyAttemptStatuses.Applied)
            {
                return AttemptRecovery(
                    latestAttempt,
                    ProjectWorkItemApplyRecoveryStatuses.Applied,
                    "Controlled apply completed; failed-apply recovery is not active.",
                    "Inspect the final report and receipts.");
            }

            if (latestAttempt.Status == SkeletonApplyAttemptStatuses.Abandoned)
            {
                return AttemptRecovery(
                    latestAttempt,
                    ProjectWorkItemApplyRecoveryStatuses.Abandoned,
                    "The acting human abandoned this apply path. The preserved attempt remains evidence.",
                    "Start a new governed run if the work should be reconsidered.");
            }

            if (latestAttempt.Status == SkeletonApplyAttemptStatuses.InProgress)
            {
                return AttemptRecovery(
                    latestAttempt,
                    ProjectWorkItemApplyRecoveryStatuses.ApplyInProgress,
                    "The latest apply attempt has started and has no durable terminal event yet.",
                    "Wait for the attempt to finish or refresh backend truth.");
            }

            var mutationUncertain = latestAttempt.MutationState != SkeletonApplyMutationStates.NotObserved;
            var interrupted = latestAttempt.Status == SkeletonApplyAttemptStatuses.Interrupted;
            var retryAllowed = latestAttempt.AvailableActions.Contains(SkeletonApplyRecoveryActions.Retry, StringComparer.Ordinal);
            var reason = mutationUncertain
                ? "Source mutation may have begun. The backend cannot prove a clean retry boundary from durable stage evidence."
                : interrupted
                    ? "The apply attempt did not reach a durable terminal state before source mutation was observed."
                    : TextOr(latestAttempt.RefusedReason, "The apply attempt failed before source mutation was observed.");
            var nextSafeAction = mutationUncertain
                ? "Inspect the source repository and attempt evidence, then record manual review or abandon."
                : interrupted
                    ? "Choose resume or retry to create a fresh attempt, or record manual review or abandon."
                    : "Choose retry to create a fresh attempt, or record manual review or abandon.";
            var status = mutationUncertain
                ? ProjectWorkItemApplyRecoveryStatuses.ManualReviewRequired
                : interrupted
                    ? ProjectWorkItemApplyRecoveryStatuses.Interrupted
                    : ProjectWorkItemApplyRecoveryStatuses.RetryReady;

            return AttemptRecovery(latestAttempt, status, reason, nextSafeAction, retryAllowed, true);
        }

        if (apply.Applied)
        {
            return Recovery(
                ProjectWorkItemApplyRecoveryStatuses.Applied,
                "Controlled apply completed; failed-apply recovery is not active.",
                "Inspect the final report and receipts.");
        }

        var failedStages = apply.Stages.Where(stage => !stage.Succeeded).ToArray();
        var succeededStages = apply.Stages.Count(stage => stage.Succeeded);
        var existingReceipts = apply.Receipts.Count(receipt => receipt.ExistsOnDisk);
        var missingReceipts = apply.Receipts.Count - existingReceipts;
        if (failedStages.Length == 0)
        {
            return Recovery(
                ProjectWorkItemApplyRecoveryStatuses.ApplyRefused,
                TextOr(apply.RefusedReason, "Apply was refused before a failed apply stage was recorded."),
                "Resolve the refusal and request a fresh preflight before any apply attempt.",
                humanReviewRequired: true);
        }

        return new ProjectWorkItemApplyRecoveryReadModel
        {
            Status = ProjectWorkItemApplyRecoveryStatuses.RecoveryEvidenceMissing,
            Required = true,
            ApplyAttemptObserved = true,
            PartialMutationPossible = succeededStages > 0,
            SucceededStageCount = succeededStages,
            FailedStageCount = failedStages.Length,
            FailedStages = failedStages.Select(stage => stage.Stage).Where(stage => !string.IsNullOrWhiteSpace(stage)).ToArray(),
            TechnicalDetails = failedStages
                .Select(stage => TextOr(stage.Errors, $"{TextOr(stage.Stage, "Apply stage")} failed."))
                .ToArray(),
            ExistingReceiptCount = existingReceipts,
            MissingReceiptCount = missingReceipts,
            Reason = succeededStages > 0
                ? "A partial apply is possible because at least one apply stage succeeded before a later stage failed."
                : "The apply attempt recorded one or more failed stages.",
            NextSafeAction =
                "Inspect failed stages and source state. Supply rollback and rollback-audit evidence before retrying apply.",
            RetryAllowed = false,
            HumanReviewRequired = true
        };
    }

    private static ProjectWorkItemApplyRecoveryReadModel AttemptRecovery(
        SkeletonRunApplyAttemptTrace attempt,
        string status,
        string reason,
        string nextSafeAction,
        bool retryAllowed = false,
        bool required = false)
    {
        var failedStages = attempt.Stages.Where(stage => !stage.Succeeded).ToArray();
        return new ProjectWorkItemApplyRecoveryReadModel
        {
            Status = status,
            Required = required,
            ApplyAttemptObserved = true,
            PartialMutationPossible = attempt.MutationState != SkeletonApplyMutationStates.NotObserved,
            SucceededStageCount = attempt.Stages.Count(stage => stage.Succeeded),
            FailedStageCount = failedStages.Length,
            FailedStages = failedStages.Select(stage => stage.Stage).ToArray(),
            TechnicalDetails = failedStages.Select(stage => TextOr(stage.Errors, $"{stage.Stage} failed.")).ToArray(),
            ExistingReceiptCount = attempt.Receipts.Count(receipt => receipt.ExistsOnDisk),
            MissingReceiptCount = attempt.Receipts.Count(receipt => !receipt.ExistsOnDisk),
            Reason = reason,
            NextSafeAction = nextSafeAction,
            RetryAllowed = retryAllowed,
            HumanReviewRequired = required,
            ApplyAttemptId = attempt.AttemptId,
            ApplyAttemptNumber = attempt.AttemptNumber,
            AttemptStatus = attempt.Status,
            MutationState = attempt.MutationState,
            AvailableActions = attempt.AvailableActions
        };
    }

    private static ProjectWorkItemApplyRecoveryReadModel Recovery(
        string status,
        string reason,
        string nextSafeAction,
        bool humanReviewRequired = false) => new()
    {
        Status = status,
        Reason = reason,
        NextSafeAction = nextSafeAction,
        RetryAllowed = false,
        HumanReviewRequired = humanReviewRequired
    };

    private static ProjectWorkItemRunReadModel? RunModel(RunRecord? run, SkeletonRunReport? report)
    {
        if (run is null)
            return null;

        var findingIds = (report?.CriticReviews ?? [])
            .SelectMany(review => review.FindingIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var dispositioned = (report?.FindingDispositions ?? [])
            .Select(disposition => disposition.FindingId)
            .ToHashSet(StringComparer.Ordinal);

        return new ProjectWorkItemRunReadModel
        {
            RunId = run.RunId,
            Status = run.State.ToString(),
            Summary = run.Summary,
            FailureReason = run.FailureReason,
            CreatedUtc = run.CreatedUtc,
            UpdatedUtc = run.UpdatedUtc,
            StartedUtc = run.StartedUtc,
            CompletedUtc = run.CompletedUtc,
            RepairAttemptCount = report?.RepairAttempts.Count ?? 0,
            FindingCount = findingIds.Length,
            UnresolvedFindingCount = findingIds.Count(findingId => !dispositioned.Contains(findingId)),
            ApprovalHaltObserved = report?.Approval?.HaltObserved == true,
            ContinuationUnblocked = report?.Approval?.ContinuationUnblocked == true,
            Applied = report?.Apply?.Applied == true || run.State == RunLifecycleState.Applied,
            LoopComplete = report?.LoopComplete == true,
            EvidenceGapCount = report?.Gaps.Count ?? 0
        };
    }

    private static string StageFor(ProjectTicket ticket, RunRecord? run)
    {
        if (run is not null)
        {
            return run.State switch
            {
                RunLifecycleState.Applied => ProjectWorkItemStages.Done,
                RunLifecycleState.PausedForApproval or RunLifecycleState.Completed or RunLifecycleState.Promoted => ProjectWorkItemStages.Review,
                _ => ProjectWorkItemStages.Build
            };
        }

        if (ContainsAny(ticket.Status, "applied", "done", "closed")) return ProjectWorkItemStages.Done;
        if (ContainsAny(ticket.Status, "approval", "review")) return ProjectWorkItemStages.Review;
        if (ContainsAny(ticket.Status, "build", "progress", "failed", "blocked")) return ProjectWorkItemStages.Build;
        if (ContainsAny(ticket.Status, "draft", "shape")) return ProjectWorkItemStages.Shape;
        return ProjectWorkItemStages.Ticket;
    }

    private static ProjectWorkItemActorReadModel? WaitingOn(
        ProjectTicket ticket,
        RunRecord? run,
        BuildReadinessResult readiness)
    {
        if (run is null)
        {
            if (!readiness.IsReady || string.IsNullOrWhiteSpace(ticket.AcceptanceCriteria))
                return Actor("Role", "Project team");
            return null;
        }

        return run.State switch
        {
            RunLifecycleState.Created or RunLifecycleState.Running => Actor("System", "IronDev run"),
            RunLifecycleState.PausedForApproval => Actor("Role", "Eligible reviewer"),
            RunLifecycleState.Completed => Actor("Role", "Controlled apply decision"),
            RunLifecycleState.Promoted => Actor("Role", "Controlled apply"),
            RunLifecycleState.Failed or RunLifecycleState.Cancelled => Actor("Role", "Project team"),
            _ => null
        };
    }

    private static ProjectWorkItemGateReadModel GateFor(
        ProjectTicket ticket,
        RunRecord? run,
        SkeletonRunReport? report,
        BuildReadinessResult readiness)
    {
        if (run is null)
        {
            return readiness.IsReady
                ? Gate("Open", readiness.Message, "Start a governed run when you are ready.", readiness.Warnings)
                : Gate("Blocked", readiness.Message, "Resolve build readiness before starting a governed run.", readiness.BlockingIssues);
        }

        return run.State switch
        {
            RunLifecycleState.Created or RunLifecycleState.Running => Gate(
                "InProgress",
                TextOr(run.Summary, "The governed run is in progress."),
                "Wait for the run to report its next durable state.",
                []),
            RunLifecycleState.PausedForApproval => Gate(
                "Blocked",
                TextOr(run.Summary, "The run is paused for human review."),
                "Review findings, record dispositions, and complete the approval ceremony.",
                report?.Gaps ?? []),
            RunLifecycleState.Completed or RunLifecycleState.Promoted => Gate(
                "Open",
                TextOr(run.Summary, "Continuation is complete and controlled apply remains separate."),
                "Review apply preflight, then request controlled apply.",
                report?.Gaps ?? []),
            RunLifecycleState.Failed => Gate(
                "Blocked",
                TextOr(run.FailureReason, TextOr(run.Summary, "The latest run failed.")),
                "Inspect failure evidence, then repair or retry from this Work Item.",
                report?.Gaps ?? []),
            RunLifecycleState.Cancelled => Gate(
                "Blocked",
                TextOr(run.Summary, "The latest run was cancelled."),
                "Review the cancellation before deciding whether another run is safe.",
                report?.Gaps ?? []),
            RunLifecycleState.Applied => Gate(
                "Satisfied",
                report?.LoopComplete == true
                    ? "Controlled apply completed and the evidence chain is complete."
                    : "Controlled apply completed, but the report names evidence gaps.",
                "Inspect the final report and receipts.",
                report?.Gaps ?? []),
            _ => Gate("Blocked", "The run returned an unsupported lifecycle state.", "Refresh backend state.", [])
        };
    }

    private static ProjectWorkItemActionReadModel ActionFor(
        RunRecord? run,
        BuildReadinessResult readiness,
        SkeletonRunReport? report)
    {
        if (run is null)
        {
            return readiness.IsReady
                ? Action(ProjectWorkItemActionKinds.StartRun, "Start governed run", true, readiness.Message)
                : Action(ProjectWorkItemActionKinds.ResolveReadiness, "Resolve build readiness", false, readiness.Message);
        }

        return run.State switch
        {
            RunLifecycleState.Created or RunLifecycleState.Running => Action(
                ProjectWorkItemActionKinds.RefreshRun, "Refresh run", true, "The run remains backend-owned."),
            RunLifecycleState.PausedForApproval => Action(
                ProjectWorkItemActionKinds.Review, "Review waiting work", true, "Human review is required."),
            RunLifecycleState.Completed or RunLifecycleState.Promoted when report?.Apply?.Attempts.LastOrDefault()?.Status == SkeletonApplyAttemptStatuses.InProgress => Action(
                    ProjectWorkItemActionKinds.RefreshRun,
                    "Refresh apply attempt",
                    true,
                    "The apply attempt has not published a durable terminal event."),
            RunLifecycleState.Completed or RunLifecycleState.Promoted when report?.Apply?.Attempts.LastOrDefault() is { } attempt &&
                attempt.Status != SkeletonApplyAttemptStatuses.Applied => Action(
                    ProjectWorkItemActionKinds.RecoverApply,
                    attempt.MutationState == SkeletonApplyMutationStates.NotObserved ? "Recover apply" : "Review uncertain apply",
                    attempt.AvailableActions.Count > 0,
                    "The backend has constrained recovery to actions justified by durable attempt evidence."),
            RunLifecycleState.Completed or RunLifecycleState.Promoted => Action(
                ProjectWorkItemActionKinds.Apply, "Review controlled apply", true, "Apply remains a separate governed action."),
            RunLifecycleState.Cancelled when report?.Apply?.Attempts.LastOrDefault()?.Status == SkeletonApplyAttemptStatuses.Abandoned => Action(
                ProjectWorkItemActionKinds.ViewOutcome, "View abandoned apply", true, "The apply path was explicitly abandoned and no retry was executed."),
            RunLifecycleState.Failed or RunLifecycleState.Cancelled => Action(
                ProjectWorkItemActionKinds.RepairOrRetry, "Review failure", true, "A human must inspect evidence before retry."),
            RunLifecycleState.Applied => Action(
                ProjectWorkItemActionKinds.ViewOutcome, "View outcome", true, "The run reports an applied outcome."),
            _ => Action(ProjectWorkItemActionKinds.RefreshRun, "Refresh run", false, "Unsupported lifecycle state.")
        };
    }

    private static ProjectWorkItemGateReadModel Gate(
        string state,
        string reason,
        string nextSafeAction,
        IReadOnlyList<string> technicalDetails) => new()
    {
        State = state,
        Reason = reason,
        NextSafeAction = nextSafeAction,
        TechnicalDetails = technicalDetails
    };

    private static ProjectWorkItemActionReadModel Action(string kind, string label, bool allowed, string reason) => new()
    {
        Kind = kind,
        Label = label,
        Allowed = allowed,
        Reason = reason
    };

    private static ProjectWorkItemActorReadModel Actor(string kind, string displayName) => new()
    {
        Kind = kind,
        DisplayName = displayName
    };

    private static string StatusSummary(ProjectTicket ticket, RunRecord? run) => run is null
        ? TextOr(ticket.Summary, $"Ticket state is {Normalize(ticket.Status, "Unknown")}.")
        : TextOr(run.FailureReason, TextOr(run.Summary, $"Run {run.RunId} is {run.State}."));

    private static int CountCriteria(string? criteria) => string.IsNullOrWhiteSpace(criteria)
        ? 0
        : criteria.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Count(line => !string.IsNullOrWhiteSpace(line.TrimStart('-', '*', ' ', '\t')));

    private static IReadOnlyList<string> SplitValues(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(['\r', '\n', ';', '|', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool ContainsAny(string? value, params string[] needles) =>
        !string.IsNullOrWhiteSpace(value) && needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string TextOr(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static DateTimeOffset ToUtc(DateTime value, DateTimeOffset fallback) => value == default
        ? fallback
        : value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value),
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
        };
}
