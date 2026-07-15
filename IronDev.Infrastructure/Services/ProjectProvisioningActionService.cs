using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Provisioning;
using IronDev.Services;
using Microsoft.Extensions.Logging;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Product-level setup actions. These operations consume server-owned project truth,
/// enforce project safety authority, and return freshly computed readiness. They do
/// not accept filesystem scope or readiness claims from a client.
/// </summary>
public sealed class ProjectProvisioningActionService : IProjectProvisioningActionService
{
    private readonly IProjectService _projects;
    private readonly IProjectMembershipService _memberships;
    private readonly ICurrentTenantContext _tenant;
    private readonly ICodeIndexService _codeIndex;
    private readonly IProjectProfileService _profiles;
    private readonly IProjectProvisioningReadinessService _readiness;
    private readonly ILogger<ProjectProvisioningActionService> _logger;

    public ProjectProvisioningActionService(
        IProjectService projects,
        IProjectMembershipService memberships,
        ICurrentTenantContext tenant,
        ICodeIndexService codeIndex,
        IProjectProfileService profiles,
        IProjectProvisioningReadinessService readiness,
        ILogger<ProjectProvisioningActionService> logger)
    {
        _projects = projects;
        _memberships = memberships;
        _tenant = tenant;
        _codeIndex = codeIndex;
        _profiles = profiles;
        _readiness = readiness;
        _logger = logger;
    }

    public async Task<ProjectProvisioningActionResult> IndexProjectAsync(
        int projectId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return Refused(
                ProjectProvisioningActionStatuses.ProjectNotFound,
                ProjectProvisioningActionReasonCodes.ProjectNotFound,
                "The project does not exist in the selected tenant.");
        }

        if (!await HasSafetyCapabilityAsync(projectId, actorUserId, cancellationToken))
        {
            return CapabilityRefusal();
        }

        var repositoryPath = project.LocalPath?.Trim();
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            return Refused(
                ProjectProvisioningActionStatuses.MissingRepositoryPath,
                ProjectProvisioningActionReasonCodes.RepositoryPathMissing,
                "The configured repository path is missing or cannot be accessed.");
        }

        var (isSafe, detail) = ProjectProvisioningReadinessService.CheckRootSafety(repositoryPath);
        if (!isSafe)
        {
            return Refused(
                ProjectProvisioningActionStatuses.UnsafeRepositoryPath,
                ProjectProvisioningActionReasonCodes.RepositoryPathUnsafe,
                detail);
        }

        try
        {
            // The only path crossing this boundary came from the tenant-scoped project row.
            var indexResult = await _codeIndex.IndexDirectoryAsync(projectId, repositoryPath, cancellationToken);
            var readiness = await _readiness.EvaluateAsync(projectId, cancellationToken);
            if (indexResult.DirectoryNotFound || !string.IsNullOrWhiteSpace(indexResult.ErrorMessage))
            {
                return Refused(
                    ProjectProvisioningActionStatuses.IndexFailed,
                    ProjectProvisioningActionReasonCodes.CodeIndexFailed,
                    indexResult.ErrorMessage ?? "The configured repository could not be indexed.") with
                {
                    IndexResult = indexResult,
                    Readiness = readiness
                };
            }

            return new ProjectProvisioningActionResult
            {
                Allowed = true,
                Status = ProjectProvisioningActionStatuses.Succeeded,
                Message = $"Indexed {indexResult.StoredFileCount} files.",
                Changed = true,
                IndexResult = indexResult,
                Readiness = readiness
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Governed project indexing failed for tenant {TenantId}, project {ProjectId}, actor {ActorUserId}",
                _tenant.TenantId,
                projectId,
                actorUserId);
            return Refused(
                ProjectProvisioningActionStatuses.IndexFailed,
                ProjectProvisioningActionReasonCodes.CodeIndexFailed,
                "The configured repository could not be indexed. Retry the governed setup action.");
        }
    }

    public async Task<ProjectProvisioningActionResult> SetBuilderWorkspacePermissionAsync(
        int projectId,
        int actorUserId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return Refused(
                ProjectProvisioningActionStatuses.ProjectNotFound,
                ProjectProvisioningActionReasonCodes.ProjectNotFound,
                "The project does not exist in the selected tenant.");
        }

        if (!await HasSafetyCapabilityAsync(projectId, actorUserId, cancellationToken))
        {
            return CapabilityRefusal();
        }

        var update = await _profiles.SetBuilderApplyPermissionAsync(projectId, enabled, cancellationToken);
        if (update is null)
        {
            return Refused(
                ProjectProvisioningActionStatuses.MissingProjectProfile,
                ProjectProvisioningActionReasonCodes.ProjectProfileMissing,
                "Confirm the project profile before changing Builder workspace permission.");
        }

        return new ProjectProvisioningActionResult
        {
            Allowed = true,
            Status = ProjectProvisioningActionStatuses.Succeeded,
            Message = enabled
                ? "Governed Builder workspace writes are enabled."
                : "Governed Builder workspace writes are disabled.",
            Changed = update.Changed,
            Profile = update.Profile,
            Readiness = await _readiness.EvaluateAsync(projectId, cancellationToken)
        };
    }

    private async Task<bool> HasSafetyCapabilityAsync(
        int projectId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var members = await _memberships.GetMembersAsync(
            _tenant.TenantId,
            projectId,
            actorUserId,
            cancellationToken);
        var actor = members.FirstOrDefault(member => member.UserId == actorUserId);
        return actor is not null &&
               (string.Equals(actor.ProjectRole, ProjectMemberRoles.Owner, StringComparison.Ordinal) ||
                string.Equals(actor.ProjectRole, ProjectMemberRoles.Contributor, StringComparison.Ordinal));
    }

    private static ProjectProvisioningActionResult CapabilityRefusal() => Refused(
        ProjectProvisioningActionStatuses.Forbidden,
        ProjectProvisioningActionReasonCodes.CapabilityRequired,
        $"The {ProjectSetupCapabilities.ManageProjectSafety} capability is required. Project Owners and Contributors have this capability; Viewers do not.");

    private static ProjectProvisioningActionResult Refused(string status, string reasonCode, string message) => new()
    {
        Allowed = false,
        Status = status,
        ReasonCode = reasonCode,
        Message = message,
        Changed = false
    };
}
