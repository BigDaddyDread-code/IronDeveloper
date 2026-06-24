namespace IronDev.Core.Governance;

public static class OperationStatusPaginationValidator
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static readonly IReadOnlyList<string> Warnings =
    [
        "operation status pagination is read-only",
        "filtered operation status is not authority",
        "selected page rows are not action candidates",
        "matching status is not permission",
        "cursor is not authority"
    ];

    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "operation status pagination is metadata-only",
        "operation status pagination uses supplied rows only",
        "operation status pagination does not fetch operation status from stores",
        "operation status pagination does not invoke diagnostic resolvers",
        "operation status pagination is not approval",
        "operation status pagination is not policy satisfaction",
        "operation status pagination is not next-safe-action formatting",
        "operation status pagination is not source apply",
        "operation status pagination is not rollback execution",
        "operation status pagination is not recovery execution",
        "operation status pagination is not retry permission",
        "operation status pagination is not resume permission",
        "operation status pagination is not commit",
        "operation status pagination is not push",
        "operation status pagination is not pull request creation",
        "operation status pagination is not merge readiness",
        "operation status pagination is not release readiness",
        "operation status pagination is not deployment readiness",
        "operation status pagination is not memory promotion",
        "operation status pagination is not workflow continuation",
        "listed operation is not action allowed",
        "filtered operation is not approved",
        "selected page rows are not action candidates",
        "matching status is not permission",
        "cursor is not authority",
        "page selection is not workflow selection",
        "empty page is not denial",
        "full page is not approval queue"
    ];

    public static IReadOnlyList<string> Validate(OperationStatusPageRequest? request)
    {
        if (request is null)
        {
            return ["OperationStatusPageRequestRequired"];
        }

        var issues = new List<string>();

        AddScopeIssues(request.TenantId, "OperationStatusPageTenantIdRequired", "OperationStatusPageTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "OperationStatusPageProjectIdRequired", "OperationStatusPageProjectIdInvalid", issues);

        if (request.AsOfUtc == default)
        {
            issues.Add("OperationStatusPageAsOfUtcRequired");
        }

        if (request.PageSize <= 0)
        {
            issues.Add("OperationStatusPageSizeRequired");
        }
        else if (request.PageSize > MaxPageSize)
        {
            issues.Add("OperationStatusPageSizeExceedsMaximum");
        }

        if (request.SortField == OperationStatusPageSortField.Unknown ||
            !Enum.IsDefined(request.SortField))
        {
            issues.Add("OperationStatusPageSortFieldRequired");
        }

        if (request.SortDirection == OperationStatusPageSortDirection.Unknown ||
            !Enum.IsDefined(request.SortDirection))
        {
            issues.Add("OperationStatusPageSortDirectionRequired");
        }

        ValidateFilter(request.Filter, issues);
        ValidateCursor(request, issues);

        if (request.Rows is null)
        {
            issues.Add("OperationStatusPageRowsRequired");
            return issues.Distinct(StringComparer.Ordinal).ToArray();
        }

        foreach (var row in request.Rows)
        {
            ValidateRow(request, row, issues);
        }

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateFilter(OperationStatusPageFilter? filter, ICollection<string> issues)
    {
        if (filter is null)
        {
            issues.Add("OperationStatusPageFilterRequired");
            return;
        }

        if (!string.IsNullOrWhiteSpace(filter.OperationId))
        {
            foreach (var issue in OperationIdentityValidator.ValidateOperationId(filter.OperationId).Issues)
            {
                issues.Add($"OperationStatusPageFilter:{issue}");
            }
        }

        AddCorrelationFilterIssues(filter.CorrelationId, issues);
        AddOptionalFilterValueIssues(filter.SurfaceId, "OperationStatusPageFilterSurfaceIdInvalid", issues);
        AddOptionalFilterValueIssues(filter.ReferenceId, "OperationStatusPageFilterReferenceIdInvalid", issues);

        ValidateSurfacePair(filter.SurfaceKind, filter.SurfaceId, "OperationStatusPageFilter", issues);
        ValidateReferencePair(filter.ReferenceKind, filter.ReferenceId, "OperationStatusPageFilter", issues);

        AddRangeIssues(filter.CreatedFromUtc, filter.CreatedToUtc, "OperationStatusPageFilterCreatedRangeInvalid", issues);
        AddRangeIssues(filter.UpdatedFromUtc, filter.UpdatedToUtc, "OperationStatusPageFilterUpdatedRangeInvalid", issues);
        AddRangeIssues(filter.LastEventFromUtc, filter.LastEventToUtc, "OperationStatusPageFilterLastEventRangeInvalid", issues);

        AddEnumListIssues(filter.ProjectedStatuses, "OperationStatusPageFilterProjectedStatusInvalid", issues, disallowUnknown: true);
        AddEnumListIssues(filter.MissingEvidenceStatuses, "OperationStatusPageFilterMissingEvidenceStatusInvalid", issues);
        AddEnumListIssues(filter.ForbiddenActionStatuses, "OperationStatusPageFilterForbiddenActionStatusInvalid", issues);
        AddEnumListIssues(filter.ReceiptResolutionStatuses, "OperationStatusPageFilterReceiptResolutionStatusInvalid", issues);
        AddEnumListIssues(filter.EvidenceResolutionStatuses, "OperationStatusPageFilterEvidenceResolutionStatusInvalid", issues);
        AddEnumListIssues(filter.ValidationStalenessStatuses, "OperationStatusPageFilterValidationStalenessStatusInvalid", issues);
        AddEnumListIssues(filter.PatchBaseFreshnessStatuses, "OperationStatusPageFilterPatchBaseFreshnessStatusInvalid", issues);
        AddEnumListIssues(filter.WorktreeBaseHeadFreshnessStatuses, "OperationStatusPageFilterWorktreeBaseHeadFreshnessStatusInvalid", issues);
        AddEnumListIssues(filter.InterruptedRunStatuses, "OperationStatusPageFilterInterruptedRunStatusInvalid", issues);
        AddEnumListIssues(filter.RollbackRecoveryStatuses, "OperationStatusPageFilterRollbackRecoveryStatusInvalid", issues);
    }

    private static void ValidateCursor(OperationStatusPageRequest request, ICollection<string> issues)
    {
        var cursor = request.Cursor;
        if (cursor is null)
        {
            return;
        }

        if (cursor.SortField != request.SortField ||
            cursor.SortDirection != request.SortDirection)
        {
            issues.Add("OperationStatusPageCursorSortMismatch");
        }

        if (string.IsNullOrWhiteSpace(cursor.LastSortValue) || ContainsUnsafeText(cursor.LastSortValue))
        {
            issues.Add("OperationStatusPageCursorLastSortValueInvalid");
        }

        foreach (var issue in OperationIdentityValidator.ValidateOperationId(cursor.LastOperationId).Issues)
        {
            issues.Add($"OperationStatusPageCursor:{issue}");
        }

        if (string.IsNullOrWhiteSpace(cursor.LastCorrelationId))
        {
            issues.Add("OperationStatusPageCursorCorrelationIdRequired");
        }
        else
        {
            AddCorrelationFilterIssues(cursor.LastCorrelationId, issues, "OperationStatusPageCursorCorrelationIdInvalid");
        }
    }

    private static void ValidateRow(
        OperationStatusPageRequest request,
        OperationStatusSummaryRow? row,
        ICollection<string> issues)
    {
        if (row is null)
        {
            issues.Add("OperationStatusPageRowRequired");
            return;
        }

        if (!Same(request.TenantId, row.TenantId))
        {
            issues.Add("OperationStatusPageRowTenantMismatch");
        }

        if (!Same(request.ProjectId, row.ProjectId))
        {
            issues.Add("OperationStatusPageRowProjectMismatch");
        }

        AddScopeIssues(row.TenantId, "OperationStatusPageRowTenantIdRequired", "OperationStatusPageRowTenantIdInvalid", issues);
        AddScopeIssues(row.ProjectId, "OperationStatusPageRowProjectIdRequired", "OperationStatusPageRowProjectIdInvalid", issues);

        foreach (var issue in OperationIdentityValidator.ValidateOperationId(row.OperationId).Issues)
        {
            issues.Add($"OperationStatusPageRow:{issue}");
        }

        AddCorrelationFilterIssues(row.CorrelationId, issues, "OperationStatusPageRowCorrelationIdInvalid");

        if (row.ProjectedStatus == OperationProjectedStatusKind.Unknown ||
            !Enum.IsDefined(row.ProjectedStatus))
        {
            issues.Add("OperationStatusPageRowProjectedStatusRequired");
        }

        if (row.CreatedAtUtc == default)
        {
            issues.Add("OperationStatusPageRowCreatedAtRequired");
        }

        if (row.UpdatedAtUtc == default)
        {
            issues.Add("OperationStatusPageRowUpdatedAtRequired");
        }

        if (row.LastEventAtUtc == default)
        {
            issues.Add("OperationStatusPageRowLastEventAtRequired");
        }

        if (row.CreatedAtUtc != default &&
            row.UpdatedAtUtc != default &&
            row.UpdatedAtUtc < row.CreatedAtUtc)
        {
            issues.Add("OperationStatusPageRowUpdatedBeforeCreated");
        }

        if (row.CreatedAtUtc != default &&
            row.LastEventAtUtc != default &&
            row.LastEventAtUtc < row.CreatedAtUtc)
        {
            issues.Add("OperationStatusPageRowLastEventBeforeCreated");
        }

        if (row.TimelineEventCount < 0)
        {
            issues.Add("OperationStatusPageRowTimelineEventCountInvalid");
        }

        ValidateSurfacePair(row.SurfaceKind, row.SurfaceId, "OperationStatusPageRow", issues);
        ValidateReferencePair(row.ReferenceKind, row.ReferenceId, "OperationStatusPageRow", issues);
        AddOptionalFilterValueIssues(row.SurfaceId, "OperationStatusPageRowSurfaceIdInvalid", issues);
        AddOptionalFilterValueIssues(row.ReferenceId, "OperationStatusPageRowReferenceIdInvalid", issues);

        if (string.IsNullOrWhiteSpace(row.Source))
        {
            issues.Add("OperationStatusPageRowSourceRequired");
        }
        else if (ContainsUnsafeText(row.Source))
        {
            issues.Add("OperationStatusPageRowSourceInvalid");
        }

        if (row.IsRedacted)
        {
            if (string.IsNullOrWhiteSpace(row.RedactionReason))
            {
                issues.Add("OperationStatusPageRowRedactionReasonRequired");
            }
            else if (ContainsUnsafeText(row.RedactionReason))
            {
                issues.Add("OperationStatusPageRowRedactionReasonInvalid");
            }
        }

        AddEnumIssues(row.MissingEvidenceStatus, "OperationStatusPageRowMissingEvidenceStatusInvalid", issues);
        AddEnumIssues(row.ForbiddenActionStatus, "OperationStatusPageRowForbiddenActionStatusInvalid", issues);
        AddEnumIssues(row.ReceiptResolutionStatus, "OperationStatusPageRowReceiptResolutionStatusInvalid", issues);
        AddEnumIssues(row.EvidenceResolutionStatus, "OperationStatusPageRowEvidenceResolutionStatusInvalid", issues);
        AddEnumIssues(row.ValidationStalenessStatus, "OperationStatusPageRowValidationStalenessStatusInvalid", issues);
        AddEnumIssues(row.PatchBaseFreshnessStatus, "OperationStatusPageRowPatchBaseFreshnessStatusInvalid", issues);
        AddEnumIssues(row.WorktreeBaseHeadFreshnessStatus, "OperationStatusPageRowWorktreeBaseHeadFreshnessStatusInvalid", issues);
        AddEnumIssues(row.InterruptedRunStatus, "OperationStatusPageRowInterruptedRunStatusInvalid", issues);
        AddEnumIssues(row.RollbackRecoveryStatus, "OperationStatusPageRowRollbackRecoveryStatusInvalid", issues);
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

        if (value.Any(char.IsWhiteSpace) || ContainsUnsafeText(value) || ContainsAuthorityText(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddCorrelationFilterIssues(
        string? correlationId,
        ICollection<string> issues,
        string invalidIssue = "OperationStatusPageFilterCorrelationIdInvalid")
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return;
        }

        if (correlationId.Any(char.IsWhiteSpace) ||
            correlationId.Contains('/') ||
            correlationId.Contains('\\') ||
            ContainsUnsafeText(correlationId) ||
            ContainsAuthorityText(correlationId) ||
            !correlationId.StartsWith("corr_", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void ValidateSurfacePair(
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        string prefix,
        ICollection<string> issues)
    {
        if (!string.IsNullOrWhiteSpace(surfaceId) &&
            (surfaceKind == OperationCorrelationSurfaceKind.Unknown || !Enum.IsDefined(surfaceKind)))
        {
            issues.Add($"{prefix}SurfaceKindRequired");
        }

        if (surfaceKind != OperationCorrelationSurfaceKind.Unknown &&
            !Enum.IsDefined(surfaceKind))
        {
            issues.Add($"{prefix}SurfaceKindInvalid");
        }
    }

    private static void ValidateReferencePair(
        OperationReferenceKind referenceKind,
        string? referenceId,
        string prefix,
        ICollection<string> issues)
    {
        var hasReferenceId = !string.IsNullOrWhiteSpace(referenceId);
        var hasReferenceKind = referenceKind != OperationReferenceKind.Unknown;

        if (hasReferenceKind && !Enum.IsDefined(referenceKind))
        {
            issues.Add($"{prefix}ReferenceKindInvalid");
        }

        if (hasReferenceKind && !hasReferenceId)
        {
            issues.Add($"{prefix}ReferenceIdRequired");
        }

        if (!hasReferenceKind && hasReferenceId)
        {
            issues.Add($"{prefix}ReferenceKindRequired");
        }
    }

    private static void AddOptionalFilterValueIssues(
        string? value,
        string issue,
        ICollection<string> issues)
    {
        if (!string.IsNullOrWhiteSpace(value) && ContainsUnsafeText(value))
        {
            issues.Add(issue);
        }
    }

    private static void AddRangeIssues(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string issue,
        ICollection<string> issues)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            issues.Add(issue);
        }
    }

    private static void AddEnumListIssues<TEnum>(
        IReadOnlyList<TEnum>? values,
        string issue,
        ICollection<string> issues,
        bool disallowUnknown = false)
        where TEnum : struct, Enum
    {
        foreach (var value in values ?? [])
        {
            if (!Enum.IsDefined(value) ||
                (disallowUnknown && Convert.ToInt32(value) == 0))
            {
                issues.Add(issue);
            }
        }
    }

    private static void AddEnumIssues<TEnum>(
        TEnum value,
        string issue,
        ICollection<string> issues)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            issues.Add(issue);
        }
    }

    private static bool ContainsUnsafeText(string value) =>
        value.Any(char.IsControl) ||
        value.Length > 256 ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("raw payload", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("diff --git", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("connection string", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAuthorityText(string value)
    {
        var markers = new[]
        {
            "approve",
            "approved",
            "policy satisfied",
            "authority",
            "ready for review",
            "merge",
            "release",
            "deploy",
            "continue",
            "rollback",
            "recover",
            "retry",
            "resume",
            "ship it"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
