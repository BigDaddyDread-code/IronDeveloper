using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ProfilesController : ControllerBase
{
    private readonly IProjectProfileService _profiles;
    private readonly IProjectProfileDetectionService _detector;

    public ProfilesController(IProjectProfileService profiles, IProjectProfileDetectionService detector)
    {
        _profiles = profiles;
        _detector = detector;
    }

    [HttpGet("api/projects/{projectId:int}/profile")]
    public Task<ProjectProfile?> GetProfile(int projectId, CancellationToken ct) =>
        _profiles.GetProjectProfileAsync(projectId, ct);

    [HttpPost("api/projects/{projectId:int}/profile")]
    public async Task<IActionResult> SaveProfile(ProjectProfile profile, CancellationToken ct)
    {
        await _profiles.SaveProjectProfileAsync(profile, ct);
        return Ok();
    }

    [HttpGet("api/projects/{projectId:int}/profile/commands")]
    public Task<List<ProjectCommand>> GetCommands(int projectId, CancellationToken ct) =>
        _profiles.GetProjectCommandsAsync(projectId, ct);

    [HttpPost("api/projects/{projectId:int}/profile/commands")]
    public async Task<IActionResult> SaveCommand(ProjectCommand command, CancellationToken ct)
    {
        await _profiles.SaveProjectCommandAsync(command, ct);
        return Ok();
    }

    [HttpGet("api/projects/{projectId:int}/profile/commands/default/{commandType}")]
    public Task<ProjectCommand?> GetDefaultCommand(int projectId, string commandType, CancellationToken ct) =>
        _profiles.GetDefaultCommandAsync(projectId, commandType, ct);

    [HttpGet("api/profile/options/{category}")]
    public Task<List<ProjectProfileOption>> GetOptions(string category, CancellationToken ct) =>
        _profiles.GetOptionsByCategoryAsync(category, ct);

    [HttpPost("api/profile/detect")]
    public Task<ProjectProfileDetectionResult> Detect(DetectProfileRequest request, CancellationToken ct) =>
        _detector.DetectAsync(request.ProjectRoot, request.ProjectId, ct);

    public sealed record DetectProfileRequest(string ProjectRoot, int ProjectId);
}
