using IronDev.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Route("api/localtest/preflight")]
public sealed class LocalTestPreflightController : ControllerBase
{
    private readonly ILocalTestPreflightService _preflight;

    public LocalTestPreflightController(ILocalTestPreflightService preflight)
    {
        _preflight = preflight;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<LocalTestPreflightResponse>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _preflight.CheckAsync(cancellationToken));
    }
}
