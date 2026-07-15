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
    public async Task<IActionResult> SaveProfile(int projectId, ProjectProfile profile, CancellationToken ct)
    {
        // The global route/body scope filter refuses a conflicting non-zero body id.
        // The route remains authoritative when the body omits the id.
        profile.ProjectId = projectId;
        await _profiles.SaveProjectProfileAsync(profile, ct);
        return Ok();
    }

    [HttpGet("api/projects/{projectId:int}/profile/commands")]
    public Task<List<ProjectCommand>> GetCommands(int projectId, CancellationToken ct) =>
        _profiles.GetProjectCommandsAsync(projectId, ct);

    [HttpPost("api/projects/{projectId:int}/profile/commands")]
    public async Task<IActionResult> SaveCommand(int projectId, ProjectCommand command, CancellationToken ct)
    {
        // DOGFOOD-2 finding F-C: a malformed body (wrong field name) used to bind
        // to an EMPTY command and return 200 OK, silently poisoning the wizard.
        // The refusal names the fields; the route owns the project id.
        command.ProjectId = projectId;
        if (string.IsNullOrWhiteSpace(command.CommandType) || string.IsNullOrWhiteSpace(command.CommandText))
        {
            return BadRequest(new
            {
                error = "A command requires commandType (Build, Test, Run, Lint, Format) and a non-empty commandText.",
                remedy = "POST { \"commandType\": \"Build\", \"commandText\": \"<the command line>\", \"isDefault\": true }."
            });
        }

        await _profiles.SaveProjectCommandAsync(command, ct);
        return Ok();
    }

    /// <summary>
    /// DELETE profile/commands/{projectCommandId} — DOGFOOD-2 finding F-D: a stored
    /// command row had no product path out; a poisoned wizard could only be repaired
    /// with direct SQL. Deletion is configuration repair, not authority.
    /// </summary>
    [HttpDelete("api/projects/{projectId:int}/profile/commands/{projectCommandId:long}")]
    public async Task<IActionResult> DeleteCommand(int projectId, long projectCommandId, CancellationToken ct)
    {
        var removed = await _profiles.DeleteProjectCommandAsync(projectId, projectCommandId, ct);
        return removed ? Ok() : NotFound();
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
