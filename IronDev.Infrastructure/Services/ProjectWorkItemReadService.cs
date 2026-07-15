using IronDev.Core.Builder;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Runs;
using IronDev.Core.WorkItems;
using IronDev.Services;
using IronDev.Core.Auth;
using Microsoft.Extensions.Configuration;
using IronDev.Core.RunReadiness;

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
    private readonly IProjectRunReadinessService? _runReadiness;

    public ProjectWorkItemReadService(
        IWorkItemIdentityService identity,
        ITicketService tickets,
        IRunStore runs,
        ITicketSkeletonRunService skeletonRuns,
        IBuilderReadinessService readiness,
        IProjectWorkItemCollaborationService collaboration,
        IProjectMemberDirectoryService members,
        ICurrentTenantContext tenant,
        IConfiguration configuration,
        IProjectRunReadinessService? runReadiness = null)
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
        _runReadiness = runReadiness;
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
        var runReadinessTask = _runReadiness?.EvaluateAsync(projectId, cancellationToken);
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

        var readiness = await readinessTask.ConfigureAwait(false);
        if (runReadinessTask is not null)
            ApplyRunReadiness(readiness, await runReadinessTask.ConfigureAwait(false));

        return ProjectWorkItemProjector.Build(
            ticket,
            latestRun,
            report,
            readiness,
            DateTimeOffset.UtcNow,
            await _collaboration.GetAsync(_tenant.TenantId, projectId, identity.WorkItemId, cancellationToken).ConfigureAwait(false),
            members,
            currentUserId,
            ReadSoloApprovalExceptionAllowed(),
            identity);
    }

    public static void ApplyRunReadiness(BuildReadinessResult result, ProjectRunReadiness runReadiness)
    {
        result.RunReadiness = runReadiness;
        if (runReadiness.ReadyToRun || !result.IsReady)
            return;

        result.Status = BuildReadinessStatus.Error;
        result.Message = runReadiness.State == ProjectRunReadinessStates.RunConfigurationRequired
            ? $"Run configuration required · {runReadiness.BlockedCount} agent blockers."
            : "Project setup is incomplete.";
        result.BlockingIssues = runReadiness.State == ProjectRunReadinessStates.RunConfigurationRequired
            ? runReadiness.Blockers.Select(blocker => $"{SkeletonAgentRoles.DisplayName(blocker.Role)}: {blocker.Reason}").ToList()
            : runReadiness.Provisioning?.Checks.Where(check => check.Blocking).Select(check => check.Evidence).ToList() ?? [];
    }

    private bool ReadSoloApprovalExceptionAllowed() =>
        string.Equals(_configuration["SkeletonAuthority:AllowSoloApproval"], "true", StringComparison.OrdinalIgnoreCase);
}
