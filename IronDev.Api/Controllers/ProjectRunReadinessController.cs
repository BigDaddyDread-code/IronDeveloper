using IronDev.Core.RunReadiness;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ProjectRunReadinessController(IProjectRunReadinessService readiness) : ControllerBase
{
    [HttpGet("api/projects/{projectId:int}/run-readiness")]
    public Task<ProjectRunReadiness> Get(int projectId, CancellationToken cancellationToken) =>
        readiness.EvaluateAsync(projectId, cancellationToken);
}
