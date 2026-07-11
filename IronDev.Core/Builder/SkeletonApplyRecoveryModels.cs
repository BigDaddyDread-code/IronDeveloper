using IronDev.Core.RunReports;

namespace IronDev.Core.Builder;

public static class SkeletonApplyRecoveryActions
{
    public const string Resume = "Resume";
    public const string Retry = "Retry";
    public const string Abandon = "Abandon";
    public const string ManualReview = "ManualReview";

    public static bool IsSupported(string? action) => action is Resume or Retry or Abandon or ManualReview;
}

public static class SkeletonApplyAttemptStatuses
{
    public const string InProgress = "InProgress";
    public const string Failed = "Failed";
    public const string Interrupted = "Interrupted";
    public const string Applied = "Applied";
    public const string Abandoned = "Abandoned";
}

public static class SkeletonApplyMutationStates
{
    public const string NotObserved = "NotObserved";
    public const string Uncertain = "Uncertain";
    public const string Observed = "Observed";
}

public sealed record SkeletonApplyRecoveryRequest
{
    public string Action { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string RequestedByUserId { get; init; } = string.Empty;
}

public sealed class SkeletonApplyRecoveryRefusedException : InvalidOperationException
{
    public SkeletonApplyRecoveryRefusedException(
        string message,
        string applyAttemptId = "",
        string mutationState = "",
        IReadOnlyList<string>? availableActions = null) : base(message)
    {
        ApplyAttemptId = applyAttemptId;
        MutationState = mutationState;
        AvailableActions = availableActions ?? [];
    }

    public string ApplyAttemptId { get; }
    public string MutationState { get; }
    public IReadOnlyList<string> AvailableActions { get; }
}

public static class SkeletonApplyAttemptProjector
{
    public static IReadOnlyList<SkeletonRunApplyAttemptTrace> Build(IReadOnlyList<RunEventDto> events)
    {
        var starts = events
            .Where(runEvent => runEvent.EventType == "SkeletonApplyAttemptStarted")
            .ToArray();

        return starts.Select(start => BuildAttempt(start, events)).ToArray();
    }

    private static SkeletonRunApplyAttemptTrace BuildAttempt(RunEventDto start, IReadOnlyList<RunEventDto> events)
    {
        var attemptId = Payload(start, "applyAttemptId");
        var attemptEvents = events
            .Where(runEvent => string.Equals(Payload(runEvent, "applyAttemptId"), attemptId, StringComparison.Ordinal))
            .ToArray();
        var completedStages = attemptEvents
            .Where(runEvent => runEvent.EventType == "SkeletonApplyStage")
            .Select(runEvent => new SkeletonRunApplyStageTrace
            {
                Stage = Payload(runEvent, "stage"),
                Succeeded = string.Equals(Payload(runEvent, "succeeded"), "true", StringComparison.OrdinalIgnoreCase),
                Errors = Payload(runEvent, "errors")
            })
            .ToArray();
        var lastStartedStage = attemptEvents.LastOrDefault(runEvent => runEvent.EventType == "SkeletonApplyStageStarted");
        var applied = attemptEvents.LastOrDefault(runEvent => runEvent.EventType == "SkeletonApplied");
        var abandoned = attemptEvents.LastOrDefault(runEvent => runEvent.EventType == "SkeletonApplyAttemptAbandoned");
        var interrupted = attemptEvents.LastOrDefault(runEvent => runEvent.EventType == "SkeletonApplyInterrupted");
        var refused = attemptEvents.LastOrDefault(runEvent => runEvent.EventType == "SkeletonApplyRefused");
        var terminal = applied ?? abandoned ?? interrupted ?? refused;
        var mutationState = MutationState(lastStartedStage, completedStages);
        var status = applied is not null
            ? SkeletonApplyAttemptStatuses.Applied
            : abandoned is not null
                ? SkeletonApplyAttemptStatuses.Abandoned
                : interrupted is not null
                    ? SkeletonApplyAttemptStatuses.Interrupted
                    : refused is not null
                        ? SkeletonApplyAttemptStatuses.Failed
                        : SkeletonApplyAttemptStatuses.InProgress;

        return new SkeletonRunApplyAttemptTrace
        {
            AttemptId = attemptId,
            AttemptNumber = int.TryParse(Payload(start, "attemptNumber"), out var number) ? number : 0,
            RequestedAction = Payload(start, "requestedAction"),
            RequestedByUserId = Payload(start, "requestedByUserId"),
            Reason = Payload(start, "reason"),
            Status = status,
            StartedUtc = start.TimestampUtc,
            CompletedUtc = terminal?.TimestampUtc,
            WorkspacePath = Payload(applied ?? start, "workspacePath"),
            InterruptedStage = interrupted is null ? string.Empty : Payload(interrupted, "stage"),
            RefusedReason = refused is null ? string.Empty : Payload(refused, "refusedReason"),
            MutationState = mutationState,
            Stages = completedStages,
            AvailableActions = AvailableActions(status, mutationState)
        };
    }

    private static string MutationState(RunEventDto? lastStartedStage, IReadOnlyList<SkeletonRunApplyStageTrace> completedStages)
    {
        if (completedStages.Any(stage => stage.Stage == "apply-copy" && stage.Succeeded))
            return SkeletonApplyMutationStates.Observed;

        if (completedStages.Any(stage => stage.Stage == "apply-copy") ||
            string.Equals(Payload(lastStartedStage, "stage"), "apply-copy", StringComparison.Ordinal) ||
            string.Equals(Payload(lastStartedStage, "stage"), "apply-verify", StringComparison.Ordinal))
            return SkeletonApplyMutationStates.Uncertain;

        return SkeletonApplyMutationStates.NotObserved;
    }

    private static IReadOnlyList<string> AvailableActions(string status, string mutationState)
    {
        if (status is SkeletonApplyAttemptStatuses.Applied or SkeletonApplyAttemptStatuses.Abandoned)
            return [];

        if (mutationState != SkeletonApplyMutationStates.NotObserved)
            return [SkeletonApplyRecoveryActions.ManualReview, SkeletonApplyRecoveryActions.Abandon];

        return status switch
        {
            SkeletonApplyAttemptStatuses.Interrupted =>
                [SkeletonApplyRecoveryActions.Resume, SkeletonApplyRecoveryActions.Retry, SkeletonApplyRecoveryActions.ManualReview, SkeletonApplyRecoveryActions.Abandon],
            SkeletonApplyAttemptStatuses.Failed =>
                [SkeletonApplyRecoveryActions.Retry, SkeletonApplyRecoveryActions.ManualReview, SkeletonApplyRecoveryActions.Abandon],
            _ => []
        };
    }

    private static string Payload(RunEventDto? runEvent, string key) =>
        runEvent is not null && runEvent.Payload.TryGetValue(key, out var value) ? value : string.Empty;
}
