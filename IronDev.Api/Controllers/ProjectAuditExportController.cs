using IronDev.Api.Auth;
using IronDev.Core.Audit;
using IronDev.Core.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/audit/export")]
public sealed class ProjectAuditExportController : ControllerBase
{
    private readonly IProjectAuditExportService _exports;
    private readonly ICurrentTenantContext _tenant;

    public ProjectAuditExportController(IProjectAuditExportService exports, ICurrentTenantContext tenant)
    {
        _exports = exports;
        _tenant = tenant;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ProjectAuditExport), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProjectAuditExportOutcome), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectAuditExport>> Get(
        int projectId,
        [FromQuery] long? workItemId,
        [FromQuery] string? actor,
        [FromQuery] string? @event,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int take = 250,
        CancellationToken cancellationToken = default)
    {
        var currentUser = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        var outcome = await _exports.ExportAsync(
            _tenant.TenantId,
            projectId,
            currentUser.UserId,
            new ProjectAuditExportFilters
            {
                WorkItemId = workItemId,
                Actor = actor,
                Event = @event,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Take = take
            },
            cancellationToken).ConfigureAwait(false);

        return outcome.Status switch
        {
            ProjectAuditExportStatuses.NotFound => NotFound(),
            ProjectAuditExportStatuses.Forbidden => Forbid(),
            ProjectAuditExportStatuses.ValidationError => BadRequest(outcome),
            _ => Ok(outcome.Export)
        };
    }
}
