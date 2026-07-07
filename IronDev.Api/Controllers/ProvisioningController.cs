using IronDev.Core.Provisioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

/// <summary>
/// PROJECT-3: provisioning readiness, for real. This route was born as an honest 501
/// in PlannedSurfacesController; the slice that owned it replaces the stub with truth.
/// Readiness is computed server-side from stored truth plus scan evidence — a client
/// can read it and act on the named remedies; it can never assert it.
/// </summary>
[ApiController]
[Authorize]
public sealed class ProvisioningController : ControllerBase
{
    private readonly IProjectProvisioningReadinessService _readiness;

    public ProvisioningController(IProjectProvisioningReadinessService readiness)
    {
        _readiness = readiness;
    }

    [HttpGet("api/projects/{projectId:int}/provisioning/readiness")]
    public async Task<ActionResult<ProjectProvisioningReadiness>> GetReadiness(int projectId, CancellationToken ct)
    {
        var result = await _readiness.EvaluateAsync(projectId, ct);
        if (result is null)
        {
            return NotFound();
        }
        return result;
    }
}
