namespace IronDev.Core.Governance;

public static class EvidenceResolver
{
    public static EvidenceResolverResult Resolve(EvidenceResolverRequest? request)
    {
        var validation = EvidenceResolverValidator.ValidateRequest(request);
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
                EvidenceResolutionStatus.NoReferences,
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
                EvidenceResolutionStatus.AmbiguousEvidence,
                [],
                [],
                ambiguity,
                [],
                Warnings());
        }

        var resolved = new List<ResolvedEvidenceReference>();
        var unresolved = new List<UnresolvedEvidenceReference>();
        var redactionIssues = new List<string>();

        foreach (var requested in request.RequestedReferences)
        {
            var match = request.AvailableEvidence.SingleOrDefault(available => IsMatch(requested, available));
            if (match is null)
            {
                unresolved.Add(new UnresolvedEvidenceReference
                {
                    EvidenceReferenceId = requested.EvidenceReferenceId,
                    EvidenceKind = requested.EvidenceKind,
                    ReferenceKind = requested.ReferenceKind,
                    ReferenceId = requested.ReferenceId,
                    Reason = "MatchingEvidenceMetadataNotFound"
                });
                continue;
            }

            var preview = requested.RequestRedactedPreview
                ? BuildPreview(match, request.SuppliedPayloadsForRedaction, redactionIssues)
                : null;

            resolved.Add(ToResolved(requested, match, preview));
        }

        if (redactionIssues.Count > 0)
        {
            return Result(
                request,
                EvidenceResolutionStatus.RedactionFailed,
                [],
                [],
                [],
                redactionIssues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
                []);
        }

        var status = (resolved.Count, unresolved.Count) switch
        {
            (0, > 0) => EvidenceResolutionStatus.NotFound,
            (> 0, 0) => EvidenceResolutionStatus.Resolved,
            _ => EvidenceResolutionStatus.PartiallyResolved
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

    private static IReadOnlyList<string> FindAmbiguity(EvidenceResolverRequest request)
    {
        var ambiguous = new List<string>();

        AddDuplicateIds(
            request.RequestedReferences.Select(static item => item.EvidenceReferenceId),
            "DuplicateRequestedEvidenceReferenceId",
            ambiguous);
        AddDuplicateIds(
            request.AvailableEvidence.Select(static item => item.EvidenceId),
            "DuplicateAvailableEvidenceId",
            ambiguous);
        AddDuplicateIds(
            request.SuppliedPayloadsForRedaction.Select(static item => item.EvidenceId),
            "DuplicateSuppliedEvidencePayloadId",
            ambiguous);

        foreach (var group in request.AvailableEvidence.GroupBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(AvailableFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingAvailableEvidenceMetadata:{group.Key}");
            }
        }

        foreach (var group in request.SuppliedPayloadsForRedaction.GroupBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(PayloadFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingSuppliedEvidencePayloadMetadata:{group.Key}");
            }
        }

        foreach (var group in request.RequestedReferences
            .Where(static item => item.ReferenceKind != OperationReferenceKind.Unknown &&
                !string.IsNullOrWhiteSpace(item.ReferenceId))
            .GroupBy(
                static item => $"{item.EvidenceKind}:{item.ReferenceKind}:{item.ReferenceId}:{item.CorrelationId}",
                StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"IndistinguishableRequestedEvidenceReferences:{group.Key}");
            }
        }

        foreach (var requested in request.RequestedReferences)
        {
            var matches = request.AvailableEvidence.Where(available => IsMatch(requested, available)).ToArray();
            if (matches.Length > 1)
            {
                ambiguous.Add($"MultipleAvailableEvidenceMatchReference:{requested.EvidenceReferenceId}");
            }
        }

        foreach (var available in request.AvailableEvidence)
        {
            var matches = request.RequestedReferences.Where(requested => IsMatch(requested, available)).ToArray();
            if (matches.Length > 1)
            {
                ambiguous.Add($"AmbiguousEvidenceAssignment:{available.EvidenceId}");
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
        EvidenceReferenceRequestItem requested,
        AvailableEvidenceMetadata available)
    {
        if (!Same(requested.TenantId, available.TenantId) ||
            !Same(requested.ProjectId, available.ProjectId) ||
            !Same(requested.OperationId, available.OperationId) ||
            !Same(requested.CorrelationId, available.CorrelationId) ||
            requested.EvidenceKind != available.EvidenceKind)
        {
            return false;
        }

        var directEvidenceIdMatch = Same(requested.EvidenceReferenceId, available.EvidenceId);
        var requestedHasReferencePair = requested.ReferenceKind != OperationReferenceKind.Unknown &&
            !string.IsNullOrWhiteSpace(requested.ReferenceId);

        if (!directEvidenceIdMatch && !requestedHasReferencePair)
        {
            return false;
        }

        if (!requestedHasReferencePair)
        {
            return directEvidenceIdMatch;
        }

        return available.ReferenceKind == requested.ReferenceKind &&
            Same(available.ReferenceId, requested.ReferenceId) &&
            (directEvidenceIdMatch || !string.IsNullOrWhiteSpace(available.ReferenceId));
    }

    private static RedactedEvidencePreview? BuildPreview(
        AvailableEvidenceMetadata evidence,
        IReadOnlyList<SuppliedEvidencePayloadForRedaction> payloads,
        ICollection<string> issues)
    {
        var matchingPayloads = payloads
            .Where(payload => Same(payload.EvidenceId, evidence.EvidenceId))
            .ToArray();

        if (matchingPayloads.Length == 0)
        {
            return null;
        }

        if (matchingPayloads.Length > 1)
        {
            issues.Add($"AmbiguousSuppliedEvidencePayload:{evidence.EvidenceId}");
            return null;
        }

        return EvidencePayloadRedactor.Redact(matchingPayloads[0]);
    }

    private static ResolvedEvidenceReference ToResolved(
        EvidenceReferenceRequestItem requested,
        AvailableEvidenceMetadata available,
        RedactedEvidencePreview? preview) =>
        new()
        {
            EvidenceReferenceId = requested.EvidenceReferenceId,
            EvidenceId = available.EvidenceId,
            EvidenceKind = available.EvidenceKind,
            SurfaceKind = available.SurfaceKind,
            SurfaceId = available.SurfaceId,
            ReferenceKind = available.ReferenceKind,
            ReferenceId = available.ReferenceId,
            CreatedAtUtc = available.CreatedAtUtc,
            Source = available.Source,
            PayloadState = preview?.PayloadState ?? available.PayloadState,
            IsRedacted = available.IsRedacted || preview?.WasRedacted == true,
            RedactionReason = available.RedactionReason,
            RedactedPreview = preview
        };

    private static string AvailableFingerprint(AvailableEvidenceMetadata evidence) =>
        string.Join(
            "|",
            evidence.TenantId,
            evidence.ProjectId,
            evidence.OperationId,
            evidence.CorrelationId,
            evidence.EvidenceKind,
            evidence.SurfaceKind,
            evidence.SurfaceId,
            evidence.ReferenceKind,
            evidence.ReferenceId,
            evidence.CreatedAtUtc.UtcDateTime.Ticks,
            evidence.Source,
            evidence.PayloadState,
            evidence.IsRedacted,
            evidence.RedactionReason);

    private static string PayloadFingerprint(SuppliedEvidencePayloadForRedaction payload) =>
        string.Join(
            "|",
            payload.TenantId,
            payload.ProjectId,
            payload.OperationId,
            payload.EvidenceId,
            payload.PayloadContentType,
            payload.Source,
            payload.SuppliedAtUtc.UtcDateTime.Ticks);

    private static IReadOnlyList<ResolvedEvidenceReference> SortResolved(IEnumerable<ResolvedEvidenceReference> items) =>
        items
            .OrderBy(static item => item.EvidenceKind)
            .ThenBy(static item => item.EvidenceReferenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<UnresolvedEvidenceReference> SortUnresolved(IEnumerable<UnresolvedEvidenceReference> items) =>
        items
            .OrderBy(static item => item.EvidenceKind)
            .ThenBy(static item => item.EvidenceReferenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ReferenceKind)
            .ThenBy(static item => item.ReferenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static EvidenceResolverResult InvalidResult(
        string tenantId,
        string projectId,
        string operationId,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = EvidenceResolutionStatus.InvalidRequest,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            ResolvedEvidence = [],
            UnresolvedEvidence = [],
            AmbiguousEvidence = [],
            Issues = issues,
            Warnings = [],
            ForbiddenAuthorityImplications = EvidenceResolverValidator.ForbiddenAuthorityImplications
        };

    private static EvidenceResolverResult Result(
        EvidenceResolverRequest request,
        EvidenceResolutionStatus status,
        IReadOnlyList<ResolvedEvidenceReference> resolved,
        IReadOnlyList<UnresolvedEvidenceReference> unresolved,
        IReadOnlyList<string> ambiguous,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> warnings) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? status : EvidenceResolutionStatus.RedactionFailed,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            OperationId = request.OperationId,
            ResolvedEvidence = resolved,
            UnresolvedEvidence = unresolved,
            AmbiguousEvidence = ambiguous,
            Issues = issues,
            Warnings = warnings,
            ForbiddenAuthorityImplications = EvidenceResolverValidator.ForbiddenAuthorityImplications
        };

    private static IReadOnlyList<string> Warnings() =>
    [
        "evidence resolution is metadata-first",
        "evidence found is not authority",
        "complete evidence resolution is not action allowed",
        "missing evidence does not choose next safe action",
        "ambiguous evidence does not choose a winner",
        "redacted evidence preview is not raw payload"
    ];

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
