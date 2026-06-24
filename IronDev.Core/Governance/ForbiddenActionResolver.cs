namespace IronDev.Core.Governance;

public static class ForbiddenActionResolver
{
    public static ForbiddenActionResolutionResult Resolve(ForbiddenActionResolverRequest? request)
    {
        var validation = ForbiddenActionResolverValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return InvalidResult(
                request?.TenantId ?? string.Empty,
                request?.ProjectId ?? string.Empty,
                request?.OperationId ?? string.Empty,
                request?.ActionKind ?? ForbiddenActionKind.Unknown,
                validation.Issues);
        }

        if (request.ActionKind == ForbiddenActionKind.Unknown)
        {
            return Result(
                request,
                ForbiddenActionResolutionStatus.NoActionRequested,
                [],
                [],
                [],
                Warnings());
        }

        var ambiguity = FindAmbiguity(request.Facts);
        if (ambiguity.Count > 0)
        {
            return Result(
                request,
                ForbiddenActionResolutionStatus.AmbiguousFacts,
                [],
                ambiguity,
                [],
                Warnings());
        }

        var findings = request.Facts
            .Where(static fact => fact.IsBlocking)
            .Select(static fact => new ForbiddenActionFinding
            {
                FactId = fact.FactId,
                FactKind = fact.FactKind,
                Severity = fact.Severity,
                Reason = $"SuppliedBlockingFact:{fact.FactKind}",
                Source = fact.Source,
                SurfaceKind = fact.SurfaceKind,
                SurfaceId = fact.SurfaceId,
                ReferenceKind = fact.ReferenceKind,
                ReferenceId = fact.ReferenceId,
                IsRedacted = fact.IsRedacted
            })
            .OrderByDescending(static finding => finding.Severity)
            .ThenBy(static finding => finding.FactKind)
            .ThenBy(static finding => finding.FactId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Result(
            request,
            findings.Length > 0
                ? ForbiddenActionResolutionStatus.Forbidden
                : ForbiddenActionResolutionStatus.NoForbiddenFactsObserved,
            findings,
            [],
            [],
            Warnings());
    }

    private static IReadOnlyList<string> FindAmbiguity(IReadOnlyList<ForbiddenActionInputFact> facts)
    {
        var ambiguous = new List<string>();

        foreach (var group in facts
            .Where(static fact => !string.IsNullOrWhiteSpace(fact.FactId))
            .GroupBy(static fact => fact.FactId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"DuplicateForbiddenActionFactId:{group.Key}");
            }

            if (group.Select(Fingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingForbiddenActionFactMetadata:{group.Key}");
            }
        }

        foreach (var fact in facts.Where(static fact => fact.FactKind == ForbiddenActionFactKind.AmbiguousEvidence))
        {
            ambiguous.Add($"SuppliedAmbiguousEvidenceFact:{fact.FactId}");
        }

        return ambiguous
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Fingerprint(ForbiddenActionInputFact fact) =>
        string.Join(
            "|",
            fact.FactKind,
            fact.Severity,
            fact.IsBlocking,
            fact.Source,
            fact.SurfaceKind,
            fact.SurfaceId,
            fact.ReferenceKind,
            fact.ReferenceId,
            fact.IsRedacted);

    private static ForbiddenActionResolutionResult InvalidResult(
        string tenantId,
        string projectId,
        string operationId,
        ForbiddenActionKind actionKind,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = ForbiddenActionResolutionStatus.InvalidRequest,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            ActionKind = actionKind,
            Findings = [],
            AmbiguousFacts = [],
            Issues = issues,
            Warnings = [],
            ForbiddenAuthorityImplications = ForbiddenActionResolverValidator.ForbiddenAuthorityImplications
        };

    private static ForbiddenActionResolutionResult Result(
        ForbiddenActionResolverRequest request,
        ForbiddenActionResolutionStatus status,
        IReadOnlyList<ForbiddenActionFinding> findings,
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
            ActionKind = request.ActionKind,
            Findings = findings,
            AmbiguousFacts = ambiguous,
            Issues = issues,
            Warnings = warnings,
            ForbiddenAuthorityImplications = ForbiddenActionResolverValidator.ForbiddenAuthorityImplications
        };

    private static IReadOnlyList<string> Warnings() =>
    [
        "no forbidden facts observed is not action permission",
        "projected status is not permission",
        "missing evidence status is not permission",
        "forbidden action finding is diagnostic only",
        "ambiguous facts do not choose a winner"
    ];
}
