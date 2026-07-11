using IronDev.Api.Auth;
using IronDev.Core.AiConnections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronDev.Api.Controllers;

/// <summary>
/// V25-06 - AI connections are tenant-scoped metadata records. This read surface
/// exposes controlled endpoint identity and credential status, never credential
/// values, raw secret references, or arbitrary per-agent URLs.
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("SensitiveApiPolicy")]
[Route("api/v1/ai-connections")]
public sealed class AiConnectionsController : ControllerBase
{
    private readonly IAiConnectionCatalogService _connections;

    public AiConnectionsController(IAiConnectionCatalogService connections)
    {
        _connections = connections;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AiConnectionMetadata>>> List(CancellationToken cancellationToken)
    {
        var context = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        if (context.TenantId is null || context.UserId <= 0)
        {
            return Forbid();
        }

        return Ok(await _connections.ListAsync(context.TenantId.Value, context.UserId, cancellationToken));
    }
}
