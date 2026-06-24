namespace IronDev.Core.Governance;

public static class OperationStatusProjectionValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "projection is read-only",
        "projection is deterministic display status",
        "projection is not operation identity",
        "projection is not operation lookup",
        "projection is not correlation authority",
        "projection is not timeline assembly",
        "projection is not event-store implementation",
        "projection is not durable projection persistence",
        "projection is not approval",
        "projection is not policy satisfaction",
        "projection is not validation freshness",
        "projection is not evidence resolution",
        "projection is not receipt resolution",
        "projection is not missing evidence resolution",
        "projection is not forbidden action resolution",
        "projection is not blocked-state explanation",
        "projection is not next-safe-action formatting",
        "projection is not authority-warning formatting",
        "projection is not source apply",
        "projection is not rollback",
        "projection is not retry permission",
        "projection is not commit",
        "projection is not push",
        "projection is not PR creation",
        "projection is not merge readiness",
        "projection is not release readiness",
        "projection is not deployment readiness",
        "projection is not memory promotion",
        "projection is not workflow continuation",
        "projection order is not authority order",
        "projected status is not permission",
        "projected status is not a gate override",
        "projected status is display truth only"
    ];

    public static OperationStatusProjectionResult ValidateEvent(OperationStatusProjectionEvent? projectionEvent)
    {
        if (projectionEvent is null)
        {
            return Invalid(["OperationStatusProjectionEventRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(projectionEvent.TenantId, "OperationStatusProjectionTenantIdRequired", "OperationStatusProjectionTenantIdInvalid", issues);
        AddScopeIssues(projectionEvent.ProjectId, "OperationStatusProjectionProjectIdRequired", "OperationStatusProjectionProjectIdInvalid", issues);
        AddOperationIdIssues(projectionEvent.OperationId, issues);
        AddCorrelationIssues(projectionEvent, issues);
        AddProjectionEventIssues(projectionEvent, issues);
        AddSourceIssues(projectionEvent.Source, issues);
        AddSurfaceIssues(projectionEvent, issues);
        AddReferenceIssues(projectionEvent, issues);
        AddRedactionIssues(projectionEvent, issues);

        return Result(issues);
    }

    public static OperationStatusProjectionResult ValidateRequest(OperationStatusProjectionRequest? request)
    {
        if (request is null)
        {
            return Invalid(["OperationStatusProjectionRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "OperationStatusProjectionTenantIdRequired", "OperationStatusProjectionTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "OperationStatusProjectionProjectIdRequired", "OperationStatusProjectionProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);
        AddProjectionVersionIssues(request.ProjectionVersion, issues);

        var events = request.Events;
        if (events is null)
        {
            issues.Add("OperationStatusProjectionEventsRequired");
            return Result(issues);
        }

        var eventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appendPositions = new HashSet<long>();
        foreach (var projectionEvent in events)
        {
            var eventValidation = ValidateEvent(projectionEvent);
            if (!eventValidation.IsValid)
            {
                issues.Add("OperationStatusProjectionEventInvalid");
                issues.AddRange(eventValidation.Issues.Select(static issue => $"OperationStatusProjectionEvent:{issue}"));
            }

            if (projectionEvent is null)
            {
                continue;
            }

            if (!Same(request.TenantId, projectionEvent.TenantId))
            {
                issues.Add("OperationStatusProjectionTenantMismatch");
            }

            if (!Same(request.ProjectId, projectionEvent.ProjectId))
            {
                issues.Add("OperationStatusProjectionProjectMismatch");
            }

            if (!Same(request.OperationId, projectionEvent.OperationId))
            {
                issues.Add("OperationStatusProjectionOperationMismatch");
            }

            if (!string.IsNullOrWhiteSpace(projectionEvent.ProjectionEventId) &&
                !eventIds.Add(projectionEvent.ProjectionEventId))
            {
                issues.Add("OperationStatusProjectionDuplicateEventId");
            }

            if (projectionEvent.AppendPosition >= 0 &&
                !appendPositions.Add(projectionEvent.AppendPosition))
            {
                issues.Add("OperationStatusProjectionDuplicateAppendPosition");
            }
        }

        return Result(issues);
    }

    private static void AddScopeIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace) ||
            ContainsAuthorityText(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddOperationIdIssues(string? operationId, ICollection<string> issues)
    {
        var result = OperationIdentityValidator.ValidateOperationId(operationId);
        foreach (var issue in result.Issues)
        {
            issues.Add(issue);
        }
    }

    private static void AddCorrelationIssues(
        OperationStatusProjectionEvent projectionEvent,
        ICollection<string> issues)
    {
        var link = new OperationCorrelationLink
        {
            TenantId = projectionEvent.TenantId,
            ProjectId = projectionEvent.ProjectId,
            OperationId = projectionEvent.OperationId,
            CorrelationId = projectionEvent.CorrelationId,
            SurfaceKind = projectionEvent.SurfaceKind,
            SurfaceId = projectionEvent.SurfaceId,
            ObservedAtUtc = projectionEvent.OccurredAtUtc,
            Source = projectionEvent.Source
        };

        var result = OperationCorrelationValidator.ValidateLink(link);
        foreach (var issue in result.Issues)
        {
            issues.Add($"OperationStatusProjectionCorrelation:{issue}");
        }
    }

    private static void AddProjectionEventIssues(
        OperationStatusProjectionEvent projectionEvent,
        ICollection<string> issues)
    {
        if (projectionEvent.EventKind == OperationStatusProjectionEventKind.Unknown ||
            !Enum.IsDefined(projectionEvent.EventKind))
        {
            issues.Add("OperationStatusProjectionEventKindRequired");
        }

        if (string.IsNullOrWhiteSpace(projectionEvent.ProjectionEventId))
        {
            issues.Add("OperationStatusProjectionEventIdRequired");
        }
        else if (ContainsUnsafeText(projectionEvent.ProjectionEventId) ||
            projectionEvent.ProjectionEventId.Any(char.IsWhiteSpace) ||
            IsUrl(projectionEvent.ProjectionEventId) ||
            ContainsAuthorityText(projectionEvent.ProjectionEventId))
        {
            issues.Add("OperationStatusProjectionEventIdInvalid");
        }

        if (projectionEvent.AppendPosition < 0)
        {
            issues.Add("OperationStatusProjectionAppendPositionInvalid");
        }

        if (projectionEvent.OccurredAtUtc == default)
        {
            issues.Add("OperationStatusProjectionOccurredAtRequired");
        }

        if (projectionEvent.RecordedAtUtc == default)
        {
            issues.Add("OperationStatusProjectionRecordedAtRequired");
        }
    }

    private static void AddProjectionVersionIssues(string? projectionVersion, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(projectionVersion))
        {
            issues.Add("OperationStatusProjectionVersionRequired");
            return;
        }

        if (ContainsUnsafeText(projectionVersion) ||
            projectionVersion.Any(char.IsWhiteSpace) ||
            ContainsAuthorityText(projectionVersion))
        {
            issues.Add("OperationStatusProjectionVersionInvalid");
        }
    }

    private static void AddSourceIssues(string? source, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            issues.Add("OperationStatusProjectionSourceRequired");
            return;
        }

        if (ContainsUnsafeText(source) ||
            source.Any(char.IsWhiteSpace) ||
            ContainsAuthorityText(source))
        {
            issues.Add("OperationStatusProjectionSourceInvalid");
        }
    }

    private static void AddSurfaceIssues(
        OperationStatusProjectionEvent projectionEvent,
        ICollection<string> issues)
    {
        if (projectionEvent.SurfaceKind == OperationCorrelationSurfaceKind.Unknown ||
            !Enum.IsDefined(projectionEvent.SurfaceKind))
        {
            issues.Add("OperationStatusProjectionSurfaceKindRequired");
        }

        if (string.IsNullOrWhiteSpace(projectionEvent.SurfaceId))
        {
            issues.Add("OperationStatusProjectionSurfaceIdRequired");
            return;
        }

        if (ContainsUnsafeText(projectionEvent.SurfaceId) ||
            projectionEvent.SurfaceId.Any(char.IsWhiteSpace))
        {
            issues.Add("OperationStatusProjectionSurfaceIdInvalid");
        }
    }

    private static void AddReferenceIssues(
        OperationStatusProjectionEvent projectionEvent,
        ICollection<string> issues)
    {
        if (projectionEvent.ReferenceKind == OperationReferenceKind.Unknown &&
            string.IsNullOrWhiteSpace(projectionEvent.ReferenceId))
        {
            return;
        }

        if (projectionEvent.ReferenceKind == OperationReferenceKind.Unknown ||
            !Enum.IsDefined(projectionEvent.ReferenceKind))
        {
            issues.Add("OperationStatusProjectionReferenceKindInvalid");
        }

        if (string.IsNullOrWhiteSpace(projectionEvent.ReferenceId))
        {
            issues.Add("OperationStatusProjectionReferenceIdRequired");
            return;
        }

        if (ContainsUnsafeText(projectionEvent.ReferenceId) ||
            projectionEvent.ReferenceId.Any(char.IsWhiteSpace))
        {
            issues.Add("OperationStatusProjectionReferenceIdInvalid");
        }
    }

    private static void AddRedactionIssues(
        OperationStatusProjectionEvent projectionEvent,
        ICollection<string> issues)
    {
        if (projectionEvent.IsRedacted && string.IsNullOrWhiteSpace(projectionEvent.RedactionReason))
        {
            issues.Add("OperationStatusProjectionRedactionReasonRequired");
        }

        if (!string.IsNullOrWhiteSpace(projectionEvent.RedactionReason) &&
            (ContainsUnsafeText(projectionEvent.RedactionReason) ||
                ContainsAuthorityText(projectionEvent.RedactionReason) ||
                ContainsSecretMarker(projectionEvent.RedactionReason)))
        {
            issues.Add("OperationStatusProjectionRedactionReasonInvalid");
        }
    }

    private static OperationStatusProjectionResult Invalid(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ProjectedStatus = null,
            Issues = issues,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    private static OperationStatusProjectionResult Result(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = issues.Count == 0,
            ProjectedStatus = null,
            Issues = issues.Distinct(StringComparer.Ordinal).ToArray(),
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUnsafeText(string value) =>
        value.Any(char.IsControl) || value.Length > 512;

    private static bool ContainsAuthorityText(string value)
    {
        var markers = new[]
        {
            "approval granted",
            "approved for",
            "policy satisfied",
            "authority granted",
            "ready for review",
            "merge ready",
            "release ready",
            "deploy now",
            "continue workflow",
            "retry authorized",
            "rollback authorized",
            "ship it"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSecretMarker(string value)
    {
        var markers = new[]
        {
            "authorization:",
            "bearer ",
            "api key",
            "apikey",
            "password",
            "secret",
            "token=",
            "connection string",
            "private key"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUrl(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
}
