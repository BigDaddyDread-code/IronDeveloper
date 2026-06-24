namespace IronDev.Core.Governance;

public static class GovernedOperationTimelineAssembler
{
    public static GovernedOperationTimelineAssemblyResult Assemble(
        string tenantId,
        string projectId,
        string operationId,
        IReadOnlyList<GovernedOperationTimelineEntry> entries)
    {
        var validation = GovernedOperationTimelineValidator.ValidateReadModel(
            tenantId,
            projectId,
            operationId,
            entries);

        if (!validation.IsValid)
        {
            return new GovernedOperationTimelineAssemblyResult
            {
                IsValid = false,
                ReadModel = null,
                Issues = validation.Issues,
                ForbiddenAuthorityImplications = GovernedOperationTimelineValidator.ForbiddenAuthorityImplications
            };
        }

        var orderedEntries = entries
            .OrderBy(static entry => entry.OccurredAtUtc)
            .ThenBy(static entry => entry.RecordedAtUtc)
            .ThenBy(static entry => entry.TimelineEventId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.SurfaceKind)
            .ThenBy(static entry => entry.SurfaceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var readModel = new GovernedOperationTimelineReadModel
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            Entries = orderedEntries,
            Warnings =
            [
                "timeline ordering is display order only",
                "timeline does not calculate current status",
                "timeline does not choose next safe action",
                "redaction is not deletion"
            ],
            ForbiddenAuthorityImplications = GovernedOperationTimelineValidator.ForbiddenAuthorityImplications
        };

        return new GovernedOperationTimelineAssemblyResult
        {
            IsValid = true,
            ReadModel = readModel,
            Issues = [],
            ForbiddenAuthorityImplications = GovernedOperationTimelineValidator.ForbiddenAuthorityImplications
        };
    }
}
