namespace IronDev.Core.Governance;

public static class GovernedOperationTimelineValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "timeline is read-only",
        "timeline is not operation identity",
        "timeline is not operation lookup",
        "timeline is not correlation authority",
        "timeline is not event-store projection",
        "timeline is not status projection",
        "timeline is not current status",
        "timeline is not evidence resolution",
        "timeline is not receipt resolution",
        "timeline is not missing evidence resolution",
        "timeline is not forbidden action resolution",
        "timeline is not validation freshness",
        "timeline is not blocked-state explanation",
        "timeline is not next-safe-action formatting",
        "timeline is not authority warning formatting",
        "timeline is not approval",
        "timeline is not policy satisfaction",
        "timeline is not source apply",
        "timeline is not rollback",
        "timeline is not retry permission",
        "timeline is not commit",
        "timeline is not push",
        "timeline is not pull request creation",
        "timeline is not merge readiness",
        "timeline is not release readiness",
        "timeline is not deployment readiness",
        "timeline is not memory promotion",
        "timeline is not workflow continuation",
        "timeline event order is not authority order",
        "redaction is not deletion",
        "displayed event is not permission"
    ];

    public static GovernedOperationTimelineAssemblyResult ValidateEntry(GovernedOperationTimelineEntry? entry)
    {
        if (entry is null)
        {
            return Invalid(["GovernedOperationTimelineEntryRequired"]);
        }

        var issues = new List<string>();

        AddScopeIssues(entry.TenantId, "GovernedOperationTimelineTenantIdRequired", "GovernedOperationTimelineTenantIdInvalid", issues);
        AddScopeIssues(entry.ProjectId, "GovernedOperationTimelineProjectIdRequired", "GovernedOperationTimelineProjectIdInvalid", issues);
        AddOperationIdIssues(entry.OperationId, issues);
        AddCorrelationIssues(entry, issues);
        AddEventIssues(entry, issues);
        AddSourceIssues(entry.Source, issues);
        AddSurfaceIssues(entry, issues);
        AddReferenceIssues(entry, issues);
        AddDisplayIssues(entry.DisplayTitle, "GovernedOperationTimelineDisplayTitleRequired", "GovernedOperationTimelineDisplayTitleInvalid", issues);
        AddDisplayIssues(entry.DisplaySummary, "GovernedOperationTimelineDisplaySummaryRequired", "GovernedOperationTimelineDisplaySummaryInvalid", issues);
        AddRedactionIssues(entry, issues);

        return Result(issues);
    }

    public static GovernedOperationTimelineAssemblyResult ValidateReadModel(
        string? tenantId,
        string? projectId,
        string? operationId,
        IReadOnlyList<GovernedOperationTimelineEntry>? entries)
    {
        var issues = new List<string>();
        AddScopeIssues(tenantId, "GovernedOperationTimelineTenantIdRequired", "GovernedOperationTimelineTenantIdInvalid", issues);
        AddScopeIssues(projectId, "GovernedOperationTimelineProjectIdRequired", "GovernedOperationTimelineProjectIdInvalid", issues);
        AddOperationIdIssues(operationId, issues);

        var timelineEntries = entries ?? [];
        if (timelineEntries.Count == 0)
        {
            issues.Add("GovernedOperationTimelineEntriesRequired");
        }

        var eventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in timelineEntries)
        {
            var entryValidation = ValidateEntry(entry);
            if (!entryValidation.IsValid)
            {
                issues.Add("GovernedOperationTimelineEntryInvalid");
                issues.AddRange(entryValidation.Issues.Select(static issue => $"GovernedOperationTimelineEntry:{issue}"));
            }

            if (!Same(tenantId, entry.TenantId))
            {
                issues.Add("GovernedOperationTimelineTenantMismatch");
            }

            if (!Same(projectId, entry.ProjectId))
            {
                issues.Add("GovernedOperationTimelineProjectMismatch");
            }

            if (!Same(operationId, entry.OperationId))
            {
                issues.Add("GovernedOperationTimelineOperationMismatch");
            }

            if (!string.IsNullOrWhiteSpace(entry.TimelineEventId) &&
                !eventIds.Add(entry.TimelineEventId))
            {
                issues.Add("GovernedOperationTimelineDuplicateEventId");
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
        GovernedOperationTimelineEntry entry,
        ICollection<string> issues)
    {
        var link = new OperationCorrelationLink
        {
            TenantId = entry.TenantId,
            ProjectId = entry.ProjectId,
            OperationId = entry.OperationId,
            CorrelationId = entry.CorrelationId,
            SurfaceKind = entry.SurfaceKind,
            SurfaceId = entry.SurfaceId,
            ObservedAtUtc = entry.OccurredAtUtc,
            Source = entry.Source
        };

        var result = OperationCorrelationValidator.ValidateLink(link);
        foreach (var issue in result.Issues)
        {
            issues.Add($"GovernedOperationTimelineCorrelation:{issue}");
        }
    }

    private static void AddEventIssues(GovernedOperationTimelineEntry entry, ICollection<string> issues)
    {
        if (entry.EventKind == GovernedOperationTimelineEventKind.Unknown ||
            !Enum.IsDefined(entry.EventKind))
        {
            issues.Add("GovernedOperationTimelineEventKindRequired");
        }

        if (string.IsNullOrWhiteSpace(entry.TimelineEventId))
        {
            issues.Add("GovernedOperationTimelineEventIdRequired");
        }
        else
        {
            if (ContainsUnsafeText(entry.TimelineEventId) ||
                entry.TimelineEventId.Any(char.IsWhiteSpace) ||
                IsUrl(entry.TimelineEventId) ||
                ContainsAuthorityText(entry.TimelineEventId))
            {
                issues.Add("GovernedOperationTimelineEventIdInvalid");
            }
        }

        if (entry.OccurredAtUtc == default)
        {
            issues.Add("GovernedOperationTimelineOccurredAtRequired");
        }

        if (entry.RecordedAtUtc == default)
        {
            issues.Add("GovernedOperationTimelineRecordedAtRequired");
        }
    }

    private static void AddSourceIssues(string? source, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            issues.Add("GovernedOperationTimelineSourceRequired");
            return;
        }

        if (ContainsUnsafeText(source) ||
            source.Any(char.IsWhiteSpace) ||
            ContainsAuthorityText(source))
        {
            issues.Add("GovernedOperationTimelineSourceInvalid");
        }
    }

    private static void AddSurfaceIssues(GovernedOperationTimelineEntry entry, ICollection<string> issues)
    {
        if (entry.SurfaceKind == OperationCorrelationSurfaceKind.Unknown ||
            !Enum.IsDefined(entry.SurfaceKind))
        {
            issues.Add("GovernedOperationTimelineSurfaceKindRequired");
        }

        if (string.IsNullOrWhiteSpace(entry.SurfaceId))
        {
            issues.Add("GovernedOperationTimelineSurfaceIdRequired");
            return;
        }

        if (ContainsUnsafeText(entry.SurfaceId) ||
            entry.SurfaceId.Any(char.IsWhiteSpace))
        {
            issues.Add("GovernedOperationTimelineSurfaceIdInvalid");
        }
    }

    private static void AddReferenceIssues(GovernedOperationTimelineEntry entry, ICollection<string> issues)
    {
        if (entry.ReferenceKind == OperationReferenceKind.Unknown &&
            string.IsNullOrWhiteSpace(entry.ReferenceId))
        {
            return;
        }

        if (entry.ReferenceKind == OperationReferenceKind.Unknown ||
            !Enum.IsDefined(entry.ReferenceKind))
        {
            issues.Add("GovernedOperationTimelineReferenceKindInvalid");
        }

        if (string.IsNullOrWhiteSpace(entry.ReferenceId))
        {
            issues.Add("GovernedOperationTimelineReferenceIdRequired");
            return;
        }

        if (ContainsUnsafeText(entry.ReferenceId) ||
            entry.ReferenceId.Any(char.IsWhiteSpace))
        {
            issues.Add("GovernedOperationTimelineReferenceIdInvalid");
        }
    }

    private static void AddDisplayIssues(
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
            ContainsAuthorityText(value) ||
            ContainsRawPayloadMarker(value) ||
            ContainsSecretMarker(value) ||
            ContainsPrivateReasoningMarker(value) ||
            ContainsPatchMarker(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddRedactionIssues(
        GovernedOperationTimelineEntry entry,
        ICollection<string> issues)
    {
        if (entry.IsRedacted && string.IsNullOrWhiteSpace(entry.RedactionReason))
        {
            issues.Add("GovernedOperationTimelineRedactionReasonRequired");
        }

        if (!string.IsNullOrWhiteSpace(entry.RedactionReason) &&
            (ContainsUnsafeText(entry.RedactionReason) ||
                ContainsAuthorityText(entry.RedactionReason) ||
                ContainsSecretMarker(entry.RedactionReason)))
        {
            issues.Add("GovernedOperationTimelineRedactionReasonInvalid");
        }
    }

    private static GovernedOperationTimelineAssemblyResult Invalid(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ReadModel = null,
            Issues = issues,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    private static GovernedOperationTimelineAssemblyResult Result(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = issues.Count == 0,
            ReadModel = null,
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

    private static bool ContainsRawPayloadMarker(string value)
    {
        var markers = new[]
        {
            "raw evidence payload",
            "raw receipt payload",
            "raw validation log",
            "raw request body",
            "raw response body",
            "raw model prompt",
            "raw model response"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSecretMarker(string value)
    {
        var markers = new[]
        {
            "authorization:",
            "authorization header",
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

    private static bool ContainsPrivateReasoningMarker(string value)
    {
        var markers = new[]
        {
            "chain of thought",
            "hidden reasoning",
            "private reasoning",
            "scratchpad"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsPatchMarker(string value)
    {
        var markers = new[]
        {
            "diff --git",
            "@@",
            "+++ ",
            "--- ",
            "full patch",
            "full diff"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUrl(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
}
