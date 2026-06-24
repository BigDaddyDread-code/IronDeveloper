namespace IronDev.Core.Governance;

public static class MissingEvidenceResolver
{
    public static MissingEvidenceResolutionResult Resolve(MissingEvidenceResolverRequest? request)
    {
        var validation = MissingEvidenceResolverValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return InvalidResult(
                request?.TenantId ?? string.Empty,
                request?.ProjectId ?? string.Empty,
                request?.OperationId ?? string.Empty,
                validation.Issues);
        }

        if (request.Requirements.Count == 0)
        {
            return Result(
                request,
                MissingEvidenceResolutionStatus.NoRequirements,
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
                MissingEvidenceResolutionStatus.AmbiguousEvidence,
                [],
                [],
                ambiguity,
                [],
                Warnings());
        }

        var missing = new List<MissingEvidenceItem>();
        var satisfied = new List<SatisfiedEvidenceItem>();
        foreach (var requirement in request.Requirements)
        {
            var expectedKind = MapRequirementKind(requirement.RequirementKind);
            var observed = request.ObservedEvidence.SingleOrDefault(item => item.EvidenceKind == expectedKind);
            if (observed is null)
            {
                missing.Add(new MissingEvidenceItem
                {
                    RequirementId = requirement.RequirementId,
                    RequirementKind = requirement.RequirementKind,
                    RequiredLabel = requirement.RequiredLabel,
                    RequiredFor = requirement.RequiredFor,
                    Severity = requirement.Severity,
                    MissingReason = $"MissingObservedEvidenceKind:{requirement.RequirementKind}"
                });
                continue;
            }

            satisfied.Add(new SatisfiedEvidenceItem
            {
                RequirementId = requirement.RequirementId,
                RequirementKind = requirement.RequirementKind,
                ObservedEvidenceId = observed.ObservedEvidenceId,
                SurfaceKind = observed.SurfaceKind,
                SurfaceId = observed.SurfaceId,
                ReferenceKind = observed.ReferenceKind,
                ReferenceId = observed.ReferenceId,
                IsRedacted = observed.IsRedacted
            });
        }

        return Result(
            request,
            missing.Count == 0 ? MissingEvidenceResolutionStatus.Complete : MissingEvidenceResolutionStatus.MissingEvidence,
            SortMissing(missing),
            SortSatisfied(satisfied),
            [],
            [],
            Warnings());
    }

    private static IReadOnlyList<string> FindAmbiguity(MissingEvidenceResolverRequest request)
    {
        var ambiguous = new List<string>();

        AddDuplicateIds(
            request.Requirements.Select(static requirement => requirement.RequirementId),
            "DuplicateMissingEvidenceRequirementId",
            ambiguous);
        AddDuplicateIds(
            request.ObservedEvidence.Select(static observed => observed.ObservedEvidenceId),
            "DuplicateObservedEvidenceId",
            ambiguous);

        foreach (var group in request.ObservedEvidence.GroupBy(static item => item.ObservedEvidenceId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(ObservedReferenceFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingObservedEvidenceReferenceMetadata:{group.Key}");
            }
        }

        foreach (var group in request.Requirements.GroupBy(
            static item => $"{item.RequirementKind}:{item.RequiredLabel}:{item.Source}",
            StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"IndistinguishableMissingEvidenceRequirements:{group.Key}");
            }
        }

        foreach (var group in request.Requirements.GroupBy(static item => item.RequirementKind))
        {
            var expectedKind = MapRequirementKind(group.Key);
            var observedCount = request.ObservedEvidence.Count(item => item.EvidenceKind == expectedKind);
            if (observedCount > 1)
            {
                ambiguous.Add($"AmbiguousObservedEvidenceForRequirementKind:{group.Key}");
            }

            if (group.Count() > 1 && observedCount > 0)
            {
                ambiguous.Add($"AmbiguousRequirementAssignment:{group.Key}");
            }
        }

        return ambiguous.Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal).ToArray();
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

    private static string ObservedReferenceFingerprint(ObservedEvidenceReference observed) =>
        $"{observed.ReferenceKind}:{observed.ReferenceId}:{observed.SurfaceKind}:{observed.SurfaceId}";

    private static ObservedEvidenceKind MapRequirementKind(MissingEvidenceRequirementKind requirementKind) =>
        Enum.TryParse<ObservedEvidenceKind>(requirementKind.ToString(), out var observedKind)
            ? observedKind
            : ObservedEvidenceKind.Unknown;

    private static IReadOnlyList<MissingEvidenceItem> SortMissing(IEnumerable<MissingEvidenceItem> items) =>
        items
            .OrderByDescending(static item => item.Severity)
            .ThenBy(static item => item.RequirementKind)
            .ThenBy(static item => item.RequirementId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<SatisfiedEvidenceItem> SortSatisfied(IEnumerable<SatisfiedEvidenceItem> items) =>
        items
            .OrderBy(static item => item.RequirementKind)
            .ThenBy(static item => item.RequirementId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ObservedEvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static MissingEvidenceResolutionResult InvalidResult(
        string tenantId,
        string projectId,
        string operationId,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = MissingEvidenceResolutionStatus.InvalidRequest,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            MissingEvidence = [],
            SatisfiedEvidence = [],
            AmbiguousEvidence = [],
            Issues = issues,
            Warnings = [],
            ForbiddenAuthorityImplications = MissingEvidenceResolverValidator.ForbiddenAuthorityImplications
        };

    private static MissingEvidenceResolutionResult Result(
        MissingEvidenceResolverRequest request,
        MissingEvidenceResolutionStatus status,
        IReadOnlyList<MissingEvidenceItem> missing,
        IReadOnlyList<SatisfiedEvidenceItem> satisfied,
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
            MissingEvidence = missing,
            SatisfiedEvidence = satisfied,
            AmbiguousEvidence = ambiguous,
            Issues = issues,
            Warnings = warnings,
            ForbiddenAuthorityImplications = MissingEvidenceResolverValidator.ForbiddenAuthorityImplications
        };

    private static IReadOnlyList<string> Warnings() =>
    [
        "missing evidence explains absence only",
        "evidence present is not action allowed",
        "missing evidence is not a policy decision",
        "ambiguous evidence does not choose a winner",
        "redacted metadata is not raw payload"
    ];
}
