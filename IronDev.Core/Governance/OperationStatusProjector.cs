namespace IronDev.Core.Governance;

public static class OperationStatusProjector
{
    public static OperationStatusProjectionResult Project(OperationStatusProjectionRequest? request)
    {
        var validation = OperationStatusProjectionValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return new OperationStatusProjectionResult
            {
                IsValid = false,
                ProjectedStatus = null,
                Issues = validation.Issues,
                ForbiddenAuthorityImplications = OperationStatusProjectionValidator.ForbiddenAuthorityImplications
            };
        }

        var orderedEvents = request.Events
            .OrderBy(static projectionEvent => projectionEvent.AppendPosition)
            .ThenBy(static projectionEvent => projectionEvent.RecordedAtUtc)
            .ThenBy(static projectionEvent => projectionEvent.ProjectionEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var projectedStatusKind = OperationProjectedStatusKind.NoEvents;
        string? lastStatusChangingEventId = null;
        OperationStatusProjectionEventKind? lastStatusChangingEventKind = null;
        DateTimeOffset? lastStatusChangedAtUtc = null;
        DateTimeOffset? lastRecordedAtUtc = null;

        foreach (var projectionEvent in orderedEvents)
        {
            if (!TryMapStatus(projectionEvent.EventKind, out var mappedStatusKind))
            {
                continue;
            }

            projectedStatusKind = mappedStatusKind;
            lastStatusChangingEventId = projectionEvent.ProjectionEventId;
            lastStatusChangingEventKind = projectionEvent.EventKind;
            lastStatusChangedAtUtc = projectionEvent.OccurredAtUtc;
            lastRecordedAtUtc = projectionEvent.RecordedAtUtc;
        }

        var projectedStatus = new OperationProjectedStatus
        {
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            OperationId = request.OperationId,
            ProjectionVersion = request.ProjectionVersion,
            ProjectedStatusKind = projectedStatusKind,
            LastStatusChangingEventId = lastStatusChangingEventId,
            LastStatusChangingEventKind = lastStatusChangingEventKind,
            LastStatusChangedAtUtc = lastStatusChangedAtUtc,
            LastRecordedAtUtc = lastRecordedAtUtc,
            SourceEventIds = orderedEvents.Select(static projectionEvent => projectionEvent.ProjectionEventId).ToArray(),
            Warnings =
            [
                "projection is display status only",
                "metadata-only events do not change projected status",
                "projection ordering is not authority order",
                "projected status does not choose next safe action"
            ],
            ForbiddenAuthorityImplications = OperationStatusProjectionValidator.ForbiddenAuthorityImplications
        };

        return new OperationStatusProjectionResult
        {
            IsValid = true,
            ProjectedStatus = projectedStatus,
            Issues = [],
            ForbiddenAuthorityImplications = OperationStatusProjectionValidator.ForbiddenAuthorityImplications
        };
    }

    private static bool TryMapStatus(
        OperationStatusProjectionEventKind eventKind,
        out OperationProjectedStatusKind projectedStatusKind)
    {
        projectedStatusKind = eventKind switch
        {
            OperationStatusProjectionEventKind.OperationMinted => OperationProjectedStatusKind.Minted,
            OperationStatusProjectionEventKind.RunStarted => OperationProjectedStatusKind.RunObserved,
            OperationStatusProjectionEventKind.RunLinked => OperationProjectedStatusKind.RunObserved,
            OperationStatusProjectionEventKind.PatchArtifactCreated => OperationProjectedStatusKind.PatchArtifactObserved,
            OperationStatusProjectionEventKind.PatchArtifactLinked => OperationProjectedStatusKind.PatchArtifactObserved,
            OperationStatusProjectionEventKind.SourceApplyStarted => OperationProjectedStatusKind.SourceApplyObserved,
            OperationStatusProjectionEventKind.SourceApplyObserved => OperationProjectedStatusKind.SourceApplyObserved,
            OperationStatusProjectionEventKind.CommitPackageCreated => OperationProjectedStatusKind.CommitPackageObserved,
            OperationStatusProjectionEventKind.CommitObserved => OperationProjectedStatusKind.CommitObserved,
            OperationStatusProjectionEventKind.PushObserved => OperationProjectedStatusKind.PushObserved,
            OperationStatusProjectionEventKind.PullRequestObserved => OperationProjectedStatusKind.PullRequestObserved,
            OperationStatusProjectionEventKind.BlockedObserved => OperationProjectedStatusKind.BlockedObserved,
            OperationStatusProjectionEventKind.InterruptedObserved => OperationProjectedStatusKind.InterruptedObserved,
            OperationStatusProjectionEventKind.RecoveryObserved => OperationProjectedStatusKind.RecoveryObserved,
            OperationStatusProjectionEventKind.RollbackObserved => OperationProjectedStatusKind.RollbackObserved,
            OperationStatusProjectionEventKind.FailedObserved => OperationProjectedStatusKind.FailedObserved,
            OperationStatusProjectionEventKind.CompletedObserved => OperationProjectedStatusKind.CompletedObserved,
            _ => OperationProjectedStatusKind.Unknown
        };

        return projectedStatusKind != OperationProjectedStatusKind.Unknown;
    }
}
