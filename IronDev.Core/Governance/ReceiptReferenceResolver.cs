namespace IronDev.Core.Governance;

public static class ReceiptReferenceResolver
{
    public static ReceiptReferenceResolverResult Resolve(ReceiptReferenceResolverRequest? request)
    {
        var validation = ReceiptReferenceResolverValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return InvalidResult(
                request?.TenantId ?? string.Empty,
                request?.ProjectId ?? string.Empty,
                request?.OperationId ?? string.Empty,
                validation.Issues);
        }

        if (request.RequestedReferences.Count == 0)
        {
            return Result(
                request,
                ReceiptReferenceResolutionStatus.NoReferences,
                [],
                [],
                [],
                [],
                Warnings());
        }

        var ambiguity = FindAmbiguity(request);
        if (ambiguity.Count > 0)
        {
            return Result(
                request,
                ReceiptReferenceResolutionStatus.AmbiguousReferences,
                [],
                [],
                ambiguity,
                [],
                Warnings());
        }

        var resolved = new List<ResolvedReceiptReference>();
        var unresolved = new List<UnresolvedReceiptReference>();

        foreach (var requested in request.RequestedReferences)
        {
            var match = request.AvailableReceipts.SingleOrDefault(available => IsMatch(requested, available));
            if (match is null)
            {
                unresolved.Add(new UnresolvedReceiptReference
                {
                    ReceiptReferenceId = requested.ReceiptReferenceId,
                    ReceiptKind = requested.ReceiptKind,
                    ReferenceKind = requested.ReferenceKind,
                    ReferenceId = requested.ReferenceId,
                    Reason = "MatchingReceiptMetadataNotFound"
                });
                continue;
            }

            resolved.Add(ToResolved(requested, match));
        }

        var status = (resolved.Count, unresolved.Count) switch
        {
            (0, > 0) => ReceiptReferenceResolutionStatus.NotFound,
            (> 0, 0) => ReceiptReferenceResolutionStatus.Resolved,
            _ => ReceiptReferenceResolutionStatus.PartiallyResolved
        };

        return Result(
            request,
            status,
            SortResolved(resolved),
            SortUnresolved(unresolved),
            [],
            [],
            Warnings());
    }

    private static IReadOnlyList<string> FindAmbiguity(ReceiptReferenceResolverRequest request)
    {
        var ambiguous = new List<string>();

        AddDuplicateIds(
            request.RequestedReferences.Select(static item => item.ReceiptReferenceId),
            "DuplicateRequestedReceiptReferenceId",
            ambiguous);
        AddDuplicateIds(
            request.AvailableReceipts.Select(static item => item.ReceiptId),
            "DuplicateAvailableReceiptId",
            ambiguous);

        foreach (var group in request.AvailableReceipts.GroupBy(static item => item.ReceiptId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(AvailableFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingAvailableReceiptMetadata:{group.Key}");
            }
        }

        foreach (var group in request.RequestedReferences
            .Where(static item => item.ReferenceKind != OperationReferenceKind.Unknown &&
                !string.IsNullOrWhiteSpace(item.ReferenceId))
            .GroupBy(
                static item => $"{item.ReceiptKind}:{item.ReferenceKind}:{item.ReferenceId}:{item.CorrelationId}",
                StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"IndistinguishableRequestedReceiptReferences:{group.Key}");
            }
        }

        foreach (var requested in request.RequestedReferences)
        {
            var matches = request.AvailableReceipts.Where(available => IsMatch(requested, available)).ToArray();
            if (matches.Length > 1)
            {
                ambiguous.Add($"MultipleAvailableReceiptsMatchReference:{requested.ReceiptReferenceId}");
            }
        }

        foreach (var available in request.AvailableReceipts)
        {
            var matches = request.RequestedReferences.Where(requested => IsMatch(requested, available)).ToArray();
            if (matches.Length > 1)
            {
                ambiguous.Add($"AmbiguousReceiptAssignment:{available.ReceiptId}");
            }
        }

        return ambiguous
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddDuplicateIds(
        IEnumerable<string> ids,
        string issue,
        ICollection<string> ambiguous)
    {
        foreach (var duplicate in ids
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key))
        {
            ambiguous.Add($"{issue}:{duplicate}");
        }
    }

    private static bool IsMatch(
        ReceiptReferenceRequestItem requested,
        AvailableReceiptMetadata available)
    {
        if (!Same(requested.TenantId, available.TenantId) ||
            !Same(requested.ProjectId, available.ProjectId) ||
            !Same(requested.OperationId, available.OperationId) ||
            !Same(requested.CorrelationId, available.CorrelationId) ||
            requested.ReceiptKind != available.ReceiptKind)
        {
            return false;
        }

        var directReceiptIdMatch = Same(requested.ReceiptReferenceId, available.ReceiptId);
        var requestedHasReferencePair = requested.ReferenceKind != OperationReferenceKind.Unknown &&
            !string.IsNullOrWhiteSpace(requested.ReferenceId);

        if (!directReceiptIdMatch && !requestedHasReferencePair)
        {
            return false;
        }

        if (!requestedHasReferencePair)
        {
            return directReceiptIdMatch;
        }

        return available.ReferenceKind == requested.ReferenceKind &&
            Same(available.ReferenceId, requested.ReferenceId) &&
            (directReceiptIdMatch || !string.IsNullOrWhiteSpace(available.ReferenceId));
    }

    private static ResolvedReceiptReference ToResolved(
        ReceiptReferenceRequestItem requested,
        AvailableReceiptMetadata available) =>
        new()
        {
            ReceiptReferenceId = requested.ReceiptReferenceId,
            ReceiptId = available.ReceiptId,
            ReceiptKind = available.ReceiptKind,
            SurfaceKind = available.SurfaceKind,
            SurfaceId = available.SurfaceId,
            ReferenceKind = available.ReferenceKind,
            ReferenceId = available.ReferenceId,
            CreatedAtUtc = available.CreatedAtUtc,
            Source = available.Source,
            IsRedacted = available.IsRedacted,
            RedactionReason = available.RedactionReason
        };

    private static string AvailableFingerprint(AvailableReceiptMetadata receipt) =>
        string.Join(
            "|",
            receipt.TenantId,
            receipt.ProjectId,
            receipt.OperationId,
            receipt.CorrelationId,
            receipt.ReceiptKind,
            receipt.SurfaceKind,
            receipt.SurfaceId,
            receipt.ReferenceKind,
            receipt.ReferenceId,
            receipt.CreatedAtUtc.UtcDateTime.Ticks,
            receipt.Source,
            receipt.IsRedacted,
            receipt.RedactionReason);

    private static IReadOnlyList<ResolvedReceiptReference> SortResolved(IEnumerable<ResolvedReceiptReference> items) =>
        items
            .OrderBy(static item => item.ReceiptKind)
            .ThenBy(static item => item.ReceiptReferenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ReceiptId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<UnresolvedReceiptReference> SortUnresolved(IEnumerable<UnresolvedReceiptReference> items) =>
        items
            .OrderBy(static item => item.ReceiptKind)
            .ThenBy(static item => item.ReceiptReferenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ReferenceKind)
            .ThenBy(static item => item.ReferenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static ReceiptReferenceResolverResult InvalidResult(
        string tenantId,
        string projectId,
        string operationId,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = ReceiptReferenceResolutionStatus.InvalidRequest,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            ResolvedReceipts = [],
            UnresolvedReceipts = [],
            AmbiguousReceipts = [],
            Issues = issues,
            Warnings = [],
            ForbiddenAuthorityImplications = ReceiptReferenceResolverValidator.ForbiddenAuthorityImplications
        };

    private static ReceiptReferenceResolverResult Result(
        ReceiptReferenceResolverRequest request,
        ReceiptReferenceResolutionStatus status,
        IReadOnlyList<ResolvedReceiptReference> resolved,
        IReadOnlyList<UnresolvedReceiptReference> unresolved,
        IReadOnlyList<string> ambiguous,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> warnings) =>
        new()
        {
            IsValid = true,
            ResolutionStatus = status,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            OperationId = request.OperationId,
            ResolvedReceipts = resolved,
            UnresolvedReceipts = unresolved,
            AmbiguousReceipts = ambiguous,
            Issues = issues,
            Warnings = warnings,
            ForbiddenAuthorityImplications = ReceiptReferenceResolverValidator.ForbiddenAuthorityImplications
        };

    private static IReadOnlyList<string> Warnings() =>
    [
        "receipt reference resolution is metadata-only",
        "receipt found is not authority",
        "complete receipt resolution is not action allowed",
        "missing receipt does not choose next safe action",
        "ambiguous receipt references do not choose a winner",
        "redacted receipt metadata is not raw payload"
    ];

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
