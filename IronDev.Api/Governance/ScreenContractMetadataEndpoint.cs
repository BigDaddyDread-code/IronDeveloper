using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Governance;

[ApiController]
[Authorize]
[Route("api/governance/screen-contract-metadata")]
public sealed class ScreenContractMetadataEndpointController : ControllerBase
{
    private static readonly ScreenContractMetadataService Service = new();

    [HttpGet]
    [ProducesResponseType(typeof(ScreenContractMetadataResponse), StatusCodes.Status200OK)]
    public ActionResult<ScreenContractMetadataResponse> Get([FromQuery] string? screenKey = null) =>
        Ok(Service.GetMetadata(screenKey));
}
