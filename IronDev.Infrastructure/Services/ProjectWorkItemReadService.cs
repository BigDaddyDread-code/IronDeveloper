using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Runs;
using IronDev.Core.WorkItems;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectWorkItemReadService : IProjectWorkItemReadService
{
    private readonly ITicketService _tickets;
    private readonly IRunStore _runs;
    private readonly ITicketSkeletonRunService _skeletonRuns;
    private readonly IBuilderReadinessService _readiness;

    public ProjectWorkItemReadService(
        ITicketService tickets,
        IRunStore runs,
        ITicketSkeletonRunService skeletonRuns,
        IBuilderReadinessService readiness)
    {
        _tickets = tickets;
        _runs = runs;
        _skeletonRuns = skeletonRuns;
        _readiness = readiness;
    }

    public async Task<ProjectWorkItemReadModel?> GetAsync(
        int projectId,
        long workItemId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(workItemId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var readinessTask = _readiness.EvaluateReadinessAsync(projectId, workItemId, cancellationToken);
        var projectRuns = await _runs.GetRecentForProjectAsync(projectId, 500, cancellationToken).ConfigureAwait(false);
        var latestRun = projectRuns
            .Where(run => run.TicketId == workItemId)
            .OrderByDescending(run => run.UpdatedUtc)
            .ThenByDescending(run => run.CreatedUtc)
            .FirstOrDefault();

        SkeletonRunReport? report = null;
        if (latestRun is not null)
        {
            report = await _skeletonRuns
                .GetRunReportAsync(projectId, workItemId, latestRun.RunId, cancellationToken)
                .ConfigureAwait(false);
        }

        return ProjectWorkItemProjector.Build(
            ticket,
            latestRun,
            report,
            await readinessTask.ConfigureAwait(false),
            DateTimeOffset.UtcNow);
    }
}
