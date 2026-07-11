using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Runs;
using IronDev.Core.WorkItems;
using IronDev.Services;
using IronDev.Core.Auth;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectWorkItemReadService : IProjectWorkItemReadService
{
    private readonly IWorkItemIdentityService _identity;
    private readonly ITicketService _tickets;
    private readonly IRunStore _runs;
    private readonly ITicketSkeletonRunService _skeletonRuns;
    private readonly IBuilderReadinessService _readiness;
    private readonly IProjectWorkItemCollaborationService _collaboration;
    private readonly IProjectMemberDirectoryService _members;
    private readonly ICurrentTenantContext _tenant;
    private readonly IConfiguration _configuration;

    public ProjectWorkItemReadService(
        IWorkItemIdentityService identity,
        ITicketService tickets,
        IRunStore runs,
        ITicketSkeletonRunService skeletonRuns,
        IBuilderReadinessService readiness,
        IProjectWorkItemCollaborationService collaboration,
        IProjectMemberDirectoryService members,
        ICurrentTenantContext tenant,
        IConfiguration configuration)
    {
        _identity = identity;
        _tickets = tickets;
        _runs = runs;
        _skeletonRuns = skeletonRuns;
        _readiness = readiness;
        _collaboration = collaboration;
        _members = members;
        _tenant = tenant;
        _configuration = configuration;
    }

    public async Task<ProjectWorkItemReadModel?> GetAsync(
        int projectId,
        long workItemId,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        var identity = await _identity.GetByWorkItemIdAsync(projectId, workItemId, cancellationToken).ConfigureAwait(false);
        if (identity is null || identity.LegacyTicketId is not long legacyTicketId)
            return null;

        var ticket = await _tickets.GetTicketByIdAsync(legacyTicketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var readinessTask = _readiness.EvaluateReadinessAsync(projectId, legacyTicketId, cancellationToken);
        var projectRuns = await _runs.GetRecentForProjectAsync(projectId, 500, cancellationToken).ConfigureAwait(false);
        var latestRun = projectRuns
            .Where(run => run.TicketId == legacyTicketId)
            .OrderByDescending(run => run.UpdatedUtc)
            .ThenByDescending(run => run.CreatedUtc)
            .FirstOrDefault();

        SkeletonRunReport? report = null;
        if (latestRun is not null)
        {
            report = await _skeletonRuns
                .GetRunReportAsync(projectId, legacyTicketId, latestRun.RunId, cancellationToken)
                .ConfigureAwait(false);
        }

        var members = await _members.GetDirectoryAsync(projectId, currentUserId, cancellationToken).ConfigureAwait(false);

        return ProjectWorkItemProjector.Build(
            ticket,
            latestRun,
            report,
            await readinessTask.ConfigureAwait(false),
            DateTimeOffset.UtcNow,
            await _collaboration.GetAsync(_tenant.TenantId, projectId, identity.WorkItemId, cancellationToken).ConfigureAwait(false),
            members,
            currentUserId,
            ReadSoloApprovalExceptionAllowed(),
            identity);
    }

    private bool ReadSoloApprovalExceptionAllowed() =>
        string.Equals(_configuration["SkeletonAuthority:AllowSoloApproval"], "true", StringComparison.OrdinalIgnoreCase);
}
