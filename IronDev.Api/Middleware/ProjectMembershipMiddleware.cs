using System.Security.Claims;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;

namespace IronDev.Api.Middleware;

public sealed class ProjectMembershipMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IProjectMembershipService memberships,
        ICurrentTenantContext tenant)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            context.Request.RouteValues.TryGetValue("projectId", out var rawProjectId) &&
            int.TryParse(rawProjectId?.ToString(), out var projectId) &&
            int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub"), out var userId) &&
            !await memberships.HasAccessAsync(tenant.TenantId, projectId, userId, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Project not found or you no longer have access." }, context.RequestAborted);
            return;
        }

        await next(context);
    }
}
