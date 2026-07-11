using IronDev.Core.Board;
using IronDev.Core.Provisioning;
using IronDev.Core.Runs;
using IronDev.Services;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectBoardReadService : IProjectBoardReadService
{
    private readonly IProjectService _projects;
    private readonly ITicketService _tickets;
    private readonly IRunStore _runs;
    private readonly IProjectProvisioningReadinessService _readiness;
    private readonly IProjectWorkItemCollaborationService _collaboration;
    private readonly ICurrentTenantContext _tenant;

    public ProjectBoardReadService(
        IProjectService projects,
        ITicketService tickets,
        IRunStore runs,
        IProjectProvisioningReadinessService readiness,
        IProjectWorkItemCollaborationService collaboration,
        ICurrentTenantContext tenant)
    {
        _projects = projects;
        _tickets = tickets;
        _runs = runs;
        _readiness = readiness;
        _collaboration = collaboration;
        _tenant = tenant;
    }

    public async Task<ProjectBoardReadModel?> GetAsync(
        int projectId,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        var boundedTake = Math.Clamp(take, 1, 500);
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var readinessTask = _readiness.EvaluateAsync(projectId, cancellationToken);
        var ticketsTask = _tickets.GetRecentTicketsAsync(projectId, boundedTake, cancellationToken);
        var runsTask = _runs.GetRecentForProjectAsync(projectId, Math.Max(200, boundedTake * 4), cancellationToken);
        var collaborationTask = _collaboration.GetForProjectAsync(_tenant.TenantId, projectId, cancellationToken);

        await Task.WhenAll(readinessTask, ticketsTask, runsTask, collaborationTask).ConfigureAwait(false);
        var readiness = await readinessTask.ConfigureAwait(false);
        if (readiness is null)
            return null;

        return ProjectBoardProjector.Build(
            project,
            readiness,
            await ticketsTask.ConfigureAwait(false),
            await runsTask.ConfigureAwait(false),
            DateTimeOffset.UtcNow,
            await collaborationTask.ConfigureAwait(false));
    }
}
