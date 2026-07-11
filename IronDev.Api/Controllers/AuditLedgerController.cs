using IronDev.Api.Auth;
using IronDev.Core.Audit;
using IronDev.Core.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/audit/ledger")]
public sealed class AuditLedgerController : ControllerBase
{
    private readonly IAuditLedgerReadService _ledger;
    private readonly ICurrentTenantContext _tenant;

    public AuditLedgerController(IAuditLedgerReadService ledger, ICurrentTenantContext tenant)
    {
        _ledger = ledger;
        _tenant = tenant;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AuditLedgerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuditLedgerResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuditLedgerResponse>> Search(
        [FromQuery] int? projectId,
        [FromQuery] long? workItemId,
        [FromQuery] string? actor,
        [FromQuery] string? @event,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var currentUser = new CurrentUserContext(
            HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        var response = await _ledger.SearchAsync(
            _tenant.TenantId,
            currentUser.UserId,
            new AuditLedgerQuery
            {
                ProjectId = projectId,
                WorkItemId = workItemId,
                Actor = actor,
                Event = @event,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Take = take
            },
            cancellationToken).ConfigureAwait(false);

        return response.Issues.Count > 0 ? BadRequest(response) : Ok(response);
    }
}
