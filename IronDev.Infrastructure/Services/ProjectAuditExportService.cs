using IronDev.Core.Audit;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectAuditExportService : IProjectAuditExportService
{
    private readonly IAuditLedgerReadService _ledger;
    private readonly IProjectMemberDirectoryService _members;

    public ProjectAuditExportService(
        IAuditLedgerReadService ledger,
        IProjectMemberDirectoryService members)
    {
        _ledger = ledger;
        _members = members;
    }

    public async Task<ProjectAuditExportOutcome> ExportAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        ProjectAuditExportFilters filters,
        CancellationToken cancellationToken = default)
    {
        var directory = await _members.GetDirectoryAsync(projectId, currentUserId, cancellationToken).ConfigureAwait(false);
        if (directory is null)
            return new ProjectAuditExportOutcome { Status = ProjectAuditExportStatuses.NotFound };

        var currentMember = directory.Members.SingleOrDefault(member => member.UserId == currentUserId);
        if (currentMember is not { IsActive: true, IsProjectMember: true })
            return new ProjectAuditExportOutcome { Status = ProjectAuditExportStatuses.Forbidden };

        var take = Math.Clamp(filters.Take <= 0 ? 250 : filters.Take, 1, 250);
        var ledger = await _ledger.SearchAsync(
            tenantId,
            currentUserId,
            new AuditLedgerQuery
            {
                ProjectId = projectId,
                WorkItemId = filters.WorkItemId,
                Actor = filters.Actor,
                Event = filters.Event,
                FromUtc = filters.FromUtc,
                ToUtc = filters.ToUtc,
                Take = take
            },
            cancellationToken).ConfigureAwait(false);

        if (ledger.Issues.Count > 0)
        {
            return new ProjectAuditExportOutcome
            {
                Status = ProjectAuditExportStatuses.ValidationError,
                Issues = ledger.Issues
            };
        }

        return new ProjectAuditExportOutcome
        {
            Export = ProjectAuditExportProjector.Build(
                projectId,
                directory.ProjectName,
                filters with { Take = take },
                ledger,
                DateTimeOffset.UtcNow)
        };
    }
}
