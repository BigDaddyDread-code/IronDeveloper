using System.Globalization;

namespace IronDev.Core.Governance;

public static class OperationStatusPaginator
{
    public static OperationStatusPageResult Page(OperationStatusPageRequest? request)
    {
        var issues = OperationStatusPaginationValidator.Validate(request);
        if (request is null)
        {
            return Invalid("", "", default, 0, issues);
        }

        if (issues.Count > 0)
        {
            return Invalid(request.TenantId, request.ProjectId, request.AsOfUtc, request.PageSize, issues);
        }

        var rows = request.Rows ?? [];
        if (rows.Count == 0)
        {
            return Result(
                request,
                OperationStatusPageResolutionStatus.NoRows,
                [],
                nextCursor: null,
                hasMore: false,
                matchedCount: 0,
                scannedCount: 0,
                issues: []);
        }

        var filtered = rows
            .Where(row => Matches(request.Filter, row))
            .ToArray();

        if (filtered.Length == 0)
        {
            return Result(
                request,
                OperationStatusPageResolutionStatus.NoMatches,
                [],
                nextCursor: null,
                hasMore: false,
                matchedCount: 0,
                scannedCount: rows.Count,
                issues: []);
        }

        var sorted = Sort(filtered, request.SortField, request.SortDirection).ToArray();
        var pageSource = sorted;
        var matchedCount = sorted.Length;

        if (request.Cursor is not null)
        {
            var matchingCursorRows = sorted
                .Select((row, index) => new { Row = row, Index = index })
                .Where(item => CursorMatches(request.Cursor, request.SortField, item.Row))
                .ToArray();

            if (matchingCursorRows.Length > 1)
            {
                return Result(
                    request,
                    OperationStatusPageResolutionStatus.AmbiguousCursor,
                    [],
                    nextCursor: null,
                    hasMore: false,
                    matchedCount,
                    rows.Count,
                    ["OperationStatusPageCursorAmbiguous"]);
            }

            if (matchingCursorRows.Length == 0 ||
                matchingCursorRows[0].Index >= sorted.Length - 1)
            {
                return Result(
                    request,
                    OperationStatusPageResolutionStatus.CursorExhausted,
                    [],
                    nextCursor: null,
                    hasMore: false,
                    matchedCount,
                    rows.Count,
                    []);
            }

            pageSource = sorted[(matchingCursorRows[0].Index + 1)..];
        }

        var pageRows = pageSource
            .Take(request.PageSize)
            .ToArray();
        var hasMore = pageSource.Length > request.PageSize;
        var nextCursor = hasMore
            ? ToCursor(pageRows[^1], request.SortField, request.SortDirection)
            : null;

        return Result(
            request,
            OperationStatusPageResolutionStatus.PageReturned,
            pageRows.Select(row => ToItem(row, request.Filter)).ToArray(),
            nextCursor,
            hasMore,
            matchedCount,
            rows.Count,
            []);
    }

    private static OperationStatusPageResult Invalid(
        string tenantId,
        string projectId,
        DateTimeOffset asOfUtc,
        int pageSize,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = OperationStatusPageResolutionStatus.InvalidRequest,
            TenantId = tenantId ?? "",
            ProjectId = projectId ?? "",
            AsOfUtc = asOfUtc,
            PageSize = pageSize,
            Items = [],
            NextCursor = null,
            HasMore = false,
            MatchedCount = 0,
            ScannedCount = 0,
            Issues = issues,
            Warnings = OperationStatusPaginationValidator.Warnings,
            ForbiddenAuthorityImplications = OperationStatusPaginationValidator.ForbiddenAuthorityImplications
        };

    private static OperationStatusPageResult Result(
        OperationStatusPageRequest request,
        OperationStatusPageResolutionStatus status,
        IReadOnlyList<OperationStatusPageItem> items,
        OperationStatusPageCursor? nextCursor,
        bool hasMore,
        int matchedCount,
        int scannedCount,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = issues.Count == 0 &&
                status is not OperationStatusPageResolutionStatus.InvalidRequest and
                    not OperationStatusPageResolutionStatus.AmbiguousCursor and
                    not OperationStatusPageResolutionStatus.Unassessable,
            ResolutionStatus = status,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            AsOfUtc = request.AsOfUtc,
            PageSize = request.PageSize,
            Items = items,
            NextCursor = nextCursor,
            HasMore = hasMore,
            MatchedCount = matchedCount,
            ScannedCount = scannedCount,
            Issues = issues,
            Warnings = OperationStatusPaginationValidator.Warnings,
            ForbiddenAuthorityImplications = OperationStatusPaginationValidator.ForbiddenAuthorityImplications
        };

    private static IEnumerable<OperationStatusSummaryRow> Sort(
        IReadOnlyList<OperationStatusSummaryRow> rows,
        OperationStatusPageSortField sortField,
        OperationStatusPageSortDirection sortDirection)
    {
        return sortDirection == OperationStatusPageSortDirection.Ascending
            ? rows
                .OrderBy(row => SortObject(row, sortField))
                .ThenBy(static row => row.OperationId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.CorrelationId, StringComparer.OrdinalIgnoreCase)
            : rows
                .OrderByDescending(row => SortObject(row, sortField))
                .ThenBy(static row => row.OperationId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.CorrelationId, StringComparer.OrdinalIgnoreCase);
    }

    private static IComparable SortObject(
        OperationStatusSummaryRow row,
        OperationStatusPageSortField sortField) =>
        sortField switch
        {
            OperationStatusPageSortField.CreatedAtUtc => row.CreatedAtUtc,
            OperationStatusPageSortField.UpdatedAtUtc => row.UpdatedAtUtc,
            OperationStatusPageSortField.LastEventAtUtc => row.LastEventAtUtc,
            OperationStatusPageSortField.OperationId => row.OperationId,
            OperationStatusPageSortField.CorrelationId => row.CorrelationId,
            OperationStatusPageSortField.ProjectedStatus => (int)row.ProjectedStatus,
            _ => row.OperationId
        };

    private static string SortValue(
        OperationStatusSummaryRow row,
        OperationStatusPageSortField sortField) =>
        sortField switch
        {
            OperationStatusPageSortField.CreatedAtUtc => row.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            OperationStatusPageSortField.UpdatedAtUtc => row.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            OperationStatusPageSortField.LastEventAtUtc => row.LastEventAtUtc.ToString("O", CultureInfo.InvariantCulture),
            OperationStatusPageSortField.OperationId => row.OperationId,
            OperationStatusPageSortField.CorrelationId => row.CorrelationId,
            OperationStatusPageSortField.ProjectedStatus => ((int)row.ProjectedStatus).ToString(CultureInfo.InvariantCulture),
            _ => row.OperationId
        };

    private static bool CursorMatches(
        OperationStatusPageCursor cursor,
        OperationStatusPageSortField sortField,
        OperationStatusSummaryRow row) =>
        string.Equals(cursor.LastSortValue, SortValue(row, sortField), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(cursor.LastOperationId, row.OperationId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(cursor.LastCorrelationId, row.CorrelationId, StringComparison.OrdinalIgnoreCase);

    private static OperationStatusPageCursor ToCursor(
        OperationStatusSummaryRow row,
        OperationStatusPageSortField sortField,
        OperationStatusPageSortDirection sortDirection) =>
        new()
        {
            SortField = sortField,
            SortDirection = sortDirection,
            LastSortValue = SortValue(row, sortField),
            LastOperationId = row.OperationId,
            LastCorrelationId = row.CorrelationId
        };

    private static bool Matches(
        OperationStatusPageFilter filter,
        OperationStatusSummaryRow row) =>
        MatchesScalar(filter.OperationId, row.OperationId) &&
        MatchesScalar(filter.CorrelationId, row.CorrelationId) &&
        MatchesList(filter.ProjectedStatuses, row.ProjectedStatus) &&
        MatchesSurface(filter, row) &&
        MatchesReference(filter, row) &&
        InRange(row.CreatedAtUtc, filter.CreatedFromUtc, filter.CreatedToUtc) &&
        InRange(row.UpdatedAtUtc, filter.UpdatedFromUtc, filter.UpdatedToUtc) &&
        InRange(row.LastEventAtUtc, filter.LastEventFromUtc, filter.LastEventToUtc) &&
        MatchesList(filter.MissingEvidenceStatuses, row.MissingEvidenceStatus) &&
        MatchesList(filter.ForbiddenActionStatuses, row.ForbiddenActionStatus) &&
        MatchesList(filter.ReceiptResolutionStatuses, row.ReceiptResolutionStatus) &&
        MatchesList(filter.EvidenceResolutionStatuses, row.EvidenceResolutionStatus) &&
        MatchesList(filter.ValidationStalenessStatuses, row.ValidationStalenessStatus) &&
        MatchesList(filter.PatchBaseFreshnessStatuses, row.PatchBaseFreshnessStatus) &&
        MatchesList(filter.WorktreeBaseHeadFreshnessStatuses, row.WorktreeBaseHeadFreshnessStatus) &&
        MatchesList(filter.InterruptedRunStatuses, row.InterruptedRunStatus) &&
        MatchesList(filter.RollbackRecoveryStatuses, row.RollbackRecoveryStatus) &&
        (filter.IncludeRedacted || !row.IsRedacted);

    private static bool MatchesScalar(string? filterValue, string rowValue) =>
        string.IsNullOrWhiteSpace(filterValue) ||
        string.Equals(filterValue, rowValue, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesList<TEnum>(
        IReadOnlyList<TEnum> values,
        TEnum rowValue)
        where TEnum : struct, Enum =>
        values.Count == 0 || values.Contains(rowValue);

    private static bool MatchesSurface(
        OperationStatusPageFilter filter,
        OperationStatusSummaryRow row)
    {
        if (filter.SurfaceKind == OperationCorrelationSurfaceKind.Unknown &&
            string.IsNullOrWhiteSpace(filter.SurfaceId))
        {
            return true;
        }

        return filter.SurfaceKind == row.SurfaceKind &&
            string.Equals(filter.SurfaceId, row.SurfaceId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesReference(
        OperationStatusPageFilter filter,
        OperationStatusSummaryRow row)
    {
        if (filter.ReferenceKind == OperationReferenceKind.Unknown &&
            string.IsNullOrWhiteSpace(filter.ReferenceId))
        {
            return true;
        }

        return filter.ReferenceKind == row.ReferenceKind &&
            string.Equals(filter.ReferenceId, row.ReferenceId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool InRange(
        DateTimeOffset value,
        DateTimeOffset? from,
        DateTimeOffset? to) =>
        (!from.HasValue || value >= from.Value) &&
        (!to.HasValue || value <= to.Value);

    private static OperationStatusPageItem ToItem(
        OperationStatusSummaryRow row,
        OperationStatusPageFilter filter) =>
        new()
        {
            TenantId = row.TenantId,
            ProjectId = row.ProjectId,
            OperationId = row.OperationId,
            CorrelationId = row.CorrelationId,
            ProjectedStatus = row.ProjectedStatus,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            LastEventAtUtc = row.LastEventAtUtc,
            TimelineEventCount = row.TimelineEventCount,
            SurfaceKind = row.SurfaceKind,
            SurfaceId = row.SurfaceId,
            ReferenceKind = row.ReferenceKind,
            ReferenceId = row.ReferenceId,
            MissingEvidenceStatus = row.MissingEvidenceStatus,
            ForbiddenActionStatus = row.ForbiddenActionStatus,
            ReceiptResolutionStatus = row.ReceiptResolutionStatus,
            EvidenceResolutionStatus = row.EvidenceResolutionStatus,
            ValidationStalenessStatus = row.ValidationStalenessStatus,
            PatchBaseFreshnessStatus = row.PatchBaseFreshnessStatus,
            WorktreeBaseHeadFreshnessStatus = row.WorktreeBaseHeadFreshnessStatus,
            InterruptedRunStatus = row.InterruptedRunStatus,
            RollbackRecoveryStatus = row.RollbackRecoveryStatus,
            Source = row.Source,
            IsRedacted = row.IsRedacted,
            RedactionReason = row.RedactionReason,
            MatchedFilterReasons = MatchedFilterReasons(row, filter)
        };

    private static IReadOnlyList<string> MatchedFilterReasons(
        OperationStatusSummaryRow row,
        OperationStatusPageFilter filter)
    {
        var reasons = new List<string>();

        AddReason(!string.IsNullOrWhiteSpace(filter.OperationId), "OperationId", reasons);
        AddReason(!string.IsNullOrWhiteSpace(filter.CorrelationId), "CorrelationId", reasons);
        AddReason(filter.ProjectedStatuses.Count > 0, "ProjectedStatus", reasons);
        AddReason(filter.SurfaceKind != OperationCorrelationSurfaceKind.Unknown || !string.IsNullOrWhiteSpace(filter.SurfaceId), "Surface", reasons);
        AddReason(filter.ReferenceKind != OperationReferenceKind.Unknown || !string.IsNullOrWhiteSpace(filter.ReferenceId), "Reference", reasons);
        AddReason(filter.CreatedFromUtc.HasValue || filter.CreatedToUtc.HasValue, "CreatedAtUtc", reasons);
        AddReason(filter.UpdatedFromUtc.HasValue || filter.UpdatedToUtc.HasValue, "UpdatedAtUtc", reasons);
        AddReason(filter.LastEventFromUtc.HasValue || filter.LastEventToUtc.HasValue, "LastEventAtUtc", reasons);
        AddReason(filter.MissingEvidenceStatuses.Count > 0, "MissingEvidenceStatus", reasons);
        AddReason(filter.ForbiddenActionStatuses.Count > 0, "ForbiddenActionStatus", reasons);
        AddReason(filter.ReceiptResolutionStatuses.Count > 0, "ReceiptResolutionStatus", reasons);
        AddReason(filter.EvidenceResolutionStatuses.Count > 0, "EvidenceResolutionStatus", reasons);
        AddReason(filter.ValidationStalenessStatuses.Count > 0, "ValidationStalenessStatus", reasons);
        AddReason(filter.PatchBaseFreshnessStatuses.Count > 0, "PatchBaseFreshnessStatus", reasons);
        AddReason(filter.WorktreeBaseHeadFreshnessStatuses.Count > 0, "WorktreeBaseHeadFreshnessStatus", reasons);
        AddReason(filter.InterruptedRunStatuses.Count > 0, "InterruptedRunStatus", reasons);
        AddReason(filter.RollbackRecoveryStatuses.Count > 0, "RollbackRecoveryStatus", reasons);
        AddReason(filter.IncludeRedacted && row.IsRedacted, "RedactedIncluded", reasons);

        return reasons;
    }

    private static void AddReason(
        bool active,
        string reason,
        ICollection<string> reasons)
    {
        if (active)
        {
            reasons.Add(reason);
        }
    }
}
