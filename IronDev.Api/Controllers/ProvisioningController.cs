using IronDev.Api.Auth;
using IronDev.Api.Middleware;
using IronDev.Core.Governance;
using IronDev.Core.Provisioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    private readonly IProjectProvisioningActionService _actions;

    public ProvisioningController(
        IProjectProvisioningReadinessService readiness,
        IProjectProvisioningActionService actions)
    {
        _readiness = readiness;
        _actions = actions;
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

    [HttpPost("api/projects/{projectId:int}/provisioning/code-index")]
    [EnableRateLimiting("SensitiveApiPolicy")]
    [ProducesResponseType(typeof(ProjectProvisioningActionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GovernedRefusalEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GovernedRefusalEnvelope), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GovernedRefusalEnvelope), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GovernedRefusalEnvelope), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GovernedRefusalEnvelope), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> IndexProject(int projectId, CancellationToken ct)
    {
        if (Request.ContentLength is > 0)
        {
            return BadRequest(GovernedRefusal.Create(
                "project_setup_request_body_forbidden",
                "Index project accepts an empty request body. The configured project path is server-owned.",
                CorrelationId(),
                blockedReasons: ["Browser-supplied filesystem scope is not accepted by this action."],
                nextSafeActions: ["Retry Index project without a request body."],
                forbiddenActions: ["Supply a directoryPath or projectId in the request body."]));
        }

        var result = await _actions.IndexProjectAsync(projectId, CurrentUser().UserId, ct);
        return ActionResponse(result);
    }

    [HttpPut("api/projects/{projectId:int}/provisioning/builder-workspace-permission")]
    [EnableRateLimiting("SensitiveApiPolicy")]
    [ProducesResponseType(typeof(ProjectProvisioningActionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GovernedRefusalEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GovernedRefusalEnvelope), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GovernedRefusalEnvelope), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GovernedRefusalEnvelope), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetBuilderWorkspacePermission(
        int projectId,
        BuilderWorkspacePermissionRequest request,
        CancellationToken ct)
    {
        if (!request.Enabled.HasValue)
        {
            return BadRequest(GovernedRefusal.Create(
                "project_setup_enabled_required",
                "Builder workspace permission requires an explicit enabled value.",
                CorrelationId(),
                blockedReasons: ["The requested permission state was omitted."],
                nextSafeActions: ["Retry with enabled set explicitly to true or false."],
                forbiddenActions: ["Infer a project-safety permission change from an omitted value."]));
        }

        var result = await _actions.SetBuilderWorkspacePermissionAsync(
            projectId,
            CurrentUser().UserId,
            request.Enabled.Value,
            ct);
        return ActionResponse(result);
    }

    private IActionResult ActionResponse(ProjectProvisioningActionResult result)
    {
        var correlationId = CorrelationId();
        if (result.Allowed)
        {
            return Ok(result with { CorrelationId = correlationId });
        }

        var statusCode = result.Status switch
        {
            ProjectProvisioningActionStatuses.ProjectNotFound => StatusCodes.Status404NotFound,
            ProjectProvisioningActionStatuses.Forbidden => StatusCodes.Status403Forbidden,
            ProjectProvisioningActionStatuses.IndexFailed => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status409Conflict
        };
        return StatusCode(statusCode, GovernedRefusal.Create(
            result.ReasonCode ?? "project_setup_action_refused",
            result.Message,
            correlationId,
            blockedReasons: [result.Message],
            nextSafeActions: [NextSafeAction(result.Status)],
            forbiddenActions: ["Bypass the governed project setup action with browser-supplied scope."]));
    }

    private static string NextSafeAction(string status) => status switch
    {
        ProjectProvisioningActionStatuses.Forbidden =>
            "Ask a project Owner or Contributor to perform this project-safety action.",
        ProjectProvisioningActionStatuses.MissingRepositoryPath =>
            "Select the intended repository in Project Setup, then retry.",
        ProjectProvisioningActionStatuses.UnsafeRepositoryPath =>
            "Select a safe, project-specific repository root, then retry.",
        ProjectProvisioningActionStatuses.MissingProjectProfile =>
            "Confirm the detected project profile first, then retry.",
        ProjectProvisioningActionStatuses.IndexFailed =>
            "Review the named indexing failure and retry Index project.",
        _ => "Return to Project Setup and re-evaluate readiness."
    };

    private CurrentUserContext CurrentUser() => new(
        HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

    private string CorrelationId() =>
        HttpContext.Items[RequestTracingMiddleware.CorrelationHeaderName]?.ToString()
        ?? HttpContext.TraceIdentifier;

    public sealed record BuilderWorkspacePermissionRequest(bool? Enabled);
}
