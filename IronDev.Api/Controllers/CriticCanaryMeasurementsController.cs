using System.Security.Claims;
using IronDev.Core.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

/// <summary>
/// P1-6 — the catch-rate as durable evidence. POST runs the canary corpus
/// through the real critic path and persists the hash-sealed measurement;
/// GET reads it back with integrity re-verified.
///
/// Boundary: a measurement is evidence, not authority. Running it grants
/// nothing, widens no autonomy envelope, and feeds no gate — the autonomy dial
/// is turned by a separate governed human decision made with this record in hand.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/critic-canary-measurements")]
public sealed class CriticCanaryMeasurementsController : ControllerBase
{
    private readonly ISkeletonCanaryMeasurementService _measurements;

    public CriticCanaryMeasurementsController(ISkeletonCanaryMeasurementService measurements)
    {
        _measurements = measurements;
    }

    /// <summary>Runs the corpus now and records the measurement. Synchronous and deliberately heavy — measuring the net is worth the wait.</summary>
    [HttpPost]
    public async Task<ActionResult<SkeletonCanaryMeasurement>> Measure(CancellationToken ct)
    {
        var requestedBy = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? "unknown-user";
        var measurement = await _measurements.MeasureAsync(requestedBy, ct);
        return Ok(measurement);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SkeletonCanaryMeasurementSummary>>> List(
        [FromQuery] int take = 20,
        CancellationToken ct = default)
        => Ok(await _measurements.ListAsync(Math.Clamp(take, 1, 100), ct));

    [HttpGet("{measurementId}")]
    public async Task<ActionResult<SkeletonCanaryMeasurementRecord>> Get(string measurementId, CancellationToken ct)
    {
        var record = await _measurements.GetAsync(measurementId, ct);
        return record is null ? NotFound() : Ok(record);
    }
}
