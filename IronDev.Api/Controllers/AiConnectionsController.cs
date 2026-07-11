using IronDev.Api.Auth;
using IronDev.Core.AiConnections;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronDev.Api.Controllers;

/// <summary>
/// V25-06/07 - AI connections are tenant-scoped metadata records with write-only
/// credential lifecycle actions. Read surfaces expose controlled endpoint identity
/// and credential status, never credential values, raw references, or arbitrary
/// per-agent URLs.
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("SensitiveApiPolicy")]
[Route("api/v1/ai-connections")]
public sealed class AiConnectionsController : ControllerBase
{
    private readonly IAiConnectionCatalogService _connections;
    private readonly IAiConnectionCredentialService _credentials;
    private readonly IUserService _userService;

    public AiConnectionsController(
        IAiConnectionCatalogService connections,
        IAiConnectionCredentialService credentials,
        IUserService userService)
    {
        _connections = connections;
        _credentials = credentials;
        _userService = userService;
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

    [HttpPut("{connectionId}/credential")]
    public async Task<ActionResult<AiConnectionCredentialMutationOutcome>> ConfigureCredential(
        string connectionId,
        [FromBody] AiConnectionCredentialWriteRequest request,
        CancellationToken cancellationToken)
    {
        var context = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        if (context.TenantId is null || context.UserId <= 0)
        {
            return Forbid();
        }

        var callerRole = await _userService.GetTenantRoleAsync(context.UserId, context.TenantId.Value, cancellationToken);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
        {
            return Forbid();
        }

        var outcome = await _credentials.ConfigureAsync(
            context.TenantId.Value,
            context.UserId,
            connectionId,
            request,
            cancellationToken);

        return outcome.Succeeded ? Ok(outcome) : BadRequest(outcome);
    }

    [HttpPost("{connectionId}/credential/revoke")]
    public async Task<ActionResult<AiConnectionCredentialMutationOutcome>> RevokeCredential(
        string connectionId,
        [FromBody] AiConnectionCredentialRevokeRequest request,
        CancellationToken cancellationToken)
    {
        var context = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        if (context.TenantId is null || context.UserId <= 0)
        {
            return Forbid();
        }

        var callerRole = await _userService.GetTenantRoleAsync(context.UserId, context.TenantId.Value, cancellationToken);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
        {
            return Forbid();
        }

        var outcome = await _credentials.RevokeAsync(
            context.TenantId.Value,
            context.UserId,
            connectionId,
            request,
            cancellationToken);

        return outcome.Succeeded ? Ok(outcome) : BadRequest(outcome);
    }
}
