namespace IronDev.Core.Governance;

public static class OperationIdentityLookupResolver
{
    public static OperationIdentityLookupResult Resolve(
        OperationIdentityLookupRequest? request,
        IReadOnlyCollection<OperationIdentityRecord> records)
    {
        var validation = OperationIdentityLookupValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return Result(
                OperationIdentityLookupStatus.InvalidRequest,
                request,
                [],
                validation.Issues);
        }

        var matches = new List<OperationIdentityLookupMatch>();
        var issues = new List<string>();

        foreach (var record in records ?? [])
        {
            if (!Same(record.TenantId, request.TenantId) ||
                !Same(record.ProjectId, request.ProjectId))
            {
                continue;
            }

            var matchingReferences = (record.References ?? [])
                .Where(reference => reference.ReferenceKind == request.ReferenceKind &&
                    Same(reference.ReferenceId, request.ReferenceId))
                .ToArray();

            if (matchingReferences.Length == 0)
            {
                continue;
            }

            var recordValidation = OperationIdentityValidator.ValidateRecord(record);
            if (!recordValidation.IsValid)
            {
                issues.Add("MatchedOperationIdentityRecordInvalid");
                issues.AddRange(recordValidation.Issues.Select(issue => $"MatchedOperationIdentityRecord:{issue}"));
                continue;
            }

            matches.AddRange(matchingReferences.Select(reference => new OperationIdentityLookupMatch
            {
                OperationId = record.OperationId,
                LifecycleState = record.LifecycleState,
                CreatedAtUtc = record.CreatedAtUtc,
                MatchedReferenceKind = reference.ReferenceKind,
                MatchedReferenceId = reference.ReferenceId,
                MatchedReferenceObservedAtUtc = reference.ObservedAtUtc,
                MatchedReferenceSource = reference.Source,
                CorrelationId = record.CorrelationId
            }));
        }

        if (issues.Count > 0)
        {
            return Result(
                OperationIdentityLookupStatus.InvalidRequest,
                request,
                [],
                issues.Distinct(StringComparer.Ordinal).ToArray());
        }

        var sortedMatches = matches
            .OrderBy(static match => match.CreatedAtUtc)
            .ThenBy(static match => match.OperationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static match => match.MatchedReferenceObservedAtUtc)
            .ThenBy(static match => match.MatchedReferenceSource, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var status = sortedMatches.Length switch
        {
            0 => OperationIdentityLookupStatus.NotFound,
            1 => OperationIdentityLookupStatus.FoundOne,
            _ => OperationIdentityLookupStatus.FoundMultiple
        };

        return Result(status, request, sortedMatches, []);
    }

    private static OperationIdentityLookupResult Result(
        OperationIdentityLookupStatus status,
        OperationIdentityLookupRequest? request,
        IReadOnlyList<OperationIdentityLookupMatch> matches,
        IReadOnlyList<string> issues) =>
        new()
        {
            LookupStatus = status,
            TenantId = request?.TenantId ?? string.Empty,
            ProjectId = request?.ProjectId ?? string.Empty,
            ReferenceKind = request?.ReferenceKind ?? OperationReferenceKind.Unknown,
            ReferenceId = request?.ReferenceId ?? string.Empty,
            Matches = matches,
            Issues = issues,
            ForbiddenAuthorityImplications = OperationIdentityLookupValidator.ForbiddenAuthorityImplications
        };

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
