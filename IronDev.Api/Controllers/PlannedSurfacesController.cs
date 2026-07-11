using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

/// <summary>
/// AFFORDANCE-1: honest 501 surfaces. Every planned-but-unbuilt product surface is a real
/// route on a real controller returning a real refusal envelope, never a hidden nav item,
/// never mock data. NotImplemented is a refusal reason like any other; the UI renders it
/// through the same panel discipline as governed refusals.
/// Boundary: a 501 grants nothing and hides nothing. It names the roadmap slice that owns
/// the surface and the next safe action available today. When a slice builds the real
/// endpoint, its route replaces the stub here and the frontend panel goes stale loudly.
/// </summary>
[ApiController]
[Authorize]
public sealed class PlannedSurfacesController : ControllerBase
{
    // PROJECT-3 note: the provisioning-readiness stub that lived here graduated to
    // ProvisioningController. Stubs are meant to die this way.

    /// <summary>Invite flow (full-ux-map section 8.3 step 2). Direct user creation exists; invite is gated on TEAM-0.</summary>
    [HttpPost("api/tenants/{tenantId:int}/users/invite")]
    public IActionResult InviteTenantUser(int tenantId) => Planned(
        surface: "Tenant user invite",
        plannedSlice: "TEAM-0 (tenant-scope proof + role/visibility matrix)",
        detail: $"Tenant {tenantId} supports direct user creation by an admin today; the invite/pending/accept flow is gated on tenant-scope proof and the role/visibility matrix.",
        nextSafeAction: "Admins can add users directly in Settings > Users and roles.");

    /// <summary>Human-intervention dial (full-ux-map section 9.6). The cockpit's dial is a labeled local draft until AUTH-0.</summary>
    [HttpGet("api/projects/{projectId:int}/authority/intervention-dial")]
    public IActionResult GetInterventionDial(int projectId) => Planned(
        surface: "Human-intervention dial",
        plannedSlice: "AUTH-0 (approval profile contract)",
        detail: $"Project {projectId} runs hands-on: no delegated approval exists, and every continuation/apply requires explicit human approval. The dial configuration in Settings is a labeled local draft that requests policy; the backend contract arrives with AUTH-0.",
        nextSafeAction: "Review the policy draft in Settings; the backend's gates remain the only authority regardless of the draft.");

    private ObjectResult Planned(string surface, string plannedSlice, string detail, string nextSafeAction) =>
        StatusCode(StatusCodes.Status501NotImplemented, new PlannedSurfaceEnvelope
        {
            Surface = surface,
            PlannedSlice = plannedSlice,
            Detail = detail,
            NextSafeAction = nextSafeAction,
            CorrelationId = HttpContext?.TraceIdentifier ?? string.Empty
        });
}

/// <summary>
/// The refusal envelope for a planned-but-unbuilt surface. Shaped like a governed refusal
/// on purpose: Allowed is always false and Reason is always NotImplemented, so clients
/// render it through the same refusal path as any other blocked action.
/// </summary>
public sealed record PlannedSurfaceEnvelope
{
    public const string BoundaryText =
        "A planned surface refuses honestly. No UI workaround exists; the backend owns when this becomes real.";

    public bool Allowed { get; init; }

    public string Reason { get; init; } = "NotImplemented";

    /// <summary>Human name of the surface that refused.</summary>
    public string Surface { get; init; } = string.Empty;

    /// <summary>What exists today and what is missing: honest and specific.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>The roadmap slice that owns building this surface.</summary>
    public string PlannedSlice { get; init; } = string.Empty;

    /// <summary>What the user can safely do today instead.</summary>
    public string NextSafeAction { get; init; } = string.Empty;

    public string Boundary { get; init; } = BoundaryText;

    public string CorrelationId { get; init; } = string.Empty;
}
