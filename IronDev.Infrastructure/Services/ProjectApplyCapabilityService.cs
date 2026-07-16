using IronDev.Core.Auth;
using IronDev.Core.RunReadiness;
using IronDev.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// The single product-facing owner of the project apply decision. Pure safety
/// evaluation and signed evidence persistence are delegated to independently
/// testable collaborators.
/// </summary>
public sealed class ProjectApplyCapabilityService : IProjectApplyCapabilityService
{
    public const string DisposableMarkerFileName = ProjectApplyQualificationStore.DisposableMarkerFileName;
    public const int QualificationContractVersion = ProjectApplyQualificationStore.QualificationContractVersion;

    private readonly IProjectService _projects;
    private readonly ICurrentTenantContext _tenant;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IProjectApplyQualificationStore _qualifications;

    public ProjectApplyCapabilityService(
        IProjectService projects,
        ICurrentTenantContext tenant,
        IConfiguration configuration,
        IHostEnvironment environment,
        IProjectApplyQualificationStore qualifications)
    {
        _projects = projects;
        _tenant = tenant;
        _configuration = configuration;
        _environment = environment;
        _qualifications = qualifications;
    }

    public async Task<ProjectApplyCapability> EvaluateAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        var input = BuildInput(projectId, project?.TenantId ?? 0, project?.LocalPath);
        return ProjectApplyCapabilityEvaluator.Evaluate(input with
        {
            Qualification = _qualifications.Read(input)
        });
    }

    public async Task<ProjectApplyCapability> QualifyDisposableProjectAsync(
        int projectId,
        int qualifyingActorUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
            var input = BuildInput(projectId, project?.TenantId ?? 0, project?.LocalPath);
            var candidate = ProjectApplyCapabilityEvaluator.EvaluatePreconditions(input);
            if (!candidate.IsReady) return candidate;

            if (qualifyingActorUserId <= 0)
            {
                return ProjectApplyCapabilityEvaluator.Refuse(candidate,
                    ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationInvalid,
                    "Disposable qualification requires an authenticated qualifying actor.");
            }

            var evidence = await _qualifications
                .IssueAsync(input, qualifyingActorUserId, cancellationToken)
                .ConfigureAwait(false);
            return ProjectApplyCapabilityEvaluator.Evaluate(input with { Qualification = evidence });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Project creation and path selection have already succeeded before this
            // bounded side effect runs. Report a fail-closed capability instead of
            // turning a qualification failure into an invitation to create a duplicate.
            return ProjectApplyCapabilityEvaluator.Refuse(
                new ProjectApplyCapability { ProjectId = projectId },
                ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationInvalid,
                $"The server-owned disposable qualification could not be completed: {exception.Message}");
        }
    }

    private ProjectApplyCapabilityInput BuildInput(int projectId, int projectTenantId, string? projectPath)
    {
        var configuredConnection = _configuration.GetConnectionString("IronDeveloperDb");
        var databaseName = string.IsNullOrWhiteSpace(configuredConnection)
            ? string.Empty
            : new SqlConnectionStringBuilder(configuredConnection).InitialCatalog;
        var apiSessionId = Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_SESSION_ID")?.Trim() ?? string.Empty;
        return new ProjectApplyCapabilityInput
        {
            ProjectId = projectId,
            TenantId = _tenant.TenantId,
            ProjectTenantId = projectTenantId,
            EnvironmentName = _environment.EnvironmentName,
            DatabaseName = databaseName,
            ExpectedDatabaseName = "IronDeveloper_Test",
            ApplyEnabled = IsTrue(_configuration["SkeletonApply:Enabled"]),
            LauncherCapabilityDeclared = IsTrue(_configuration["SkeletonApply:LauncherCapabilityDeclared"]),
            LauncherSessionId = _configuration["SkeletonApply:LauncherSessionId"]?.Trim() ?? string.Empty,
            ApiSessionId = apiSessionId,
            SessionMode = Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_SESSION_MODE")?.Trim() ?? string.Empty,
            RepositoryCommit = Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_REPOSITORY_COMMIT")?.Trim() ?? string.Empty,
            SandboxRoot = _configuration["SkeletonApply:SandboxRoot"]?.Trim() ?? string.Empty,
            ProjectPath = projectPath?.Trim() ?? string.Empty,
            QualificationAuthorityConfigured = _qualifications.IsAuthorityConfigured(apiSessionId)
        };
    }

    private static bool IsTrue(string? value) =>
        value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
}
