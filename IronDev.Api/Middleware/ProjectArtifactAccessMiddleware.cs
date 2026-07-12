using System.Security.Claims;
using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Core.Interfaces;

namespace IronDev.Api.Middleware;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireProjectArtifactAccessAttribute(
    ProjectArtifactKind artifactKind,
    string routeValueName) : Attribute
{
    public ProjectArtifactKind ArtifactKind { get; } = artifactKind;
    public string RouteValueName { get; } = routeValueName;
}

public sealed class ProjectArtifactAccessMiddleware(RequestDelegate next)
{
    public const string ArtifactScopeNotFoundReasonCode = "project_artifact_scope_not_found";

    public async Task InvokeAsync(
        HttpContext context,
        IProjectArtifactAccessService artifacts,
        ICurrentTenantContext tenant)
    {
        var requirements = context.GetEndpoint()?.Metadata
            .GetOrderedMetadata<RequireProjectArtifactAccessAttribute>() ?? [];

        if (requirements.Count == 0 || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!int.TryParse(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub"),
                out var userId))
        {
            await RefuseAsync(context).ConfigureAwait(false);
            return;
        }

        foreach (var requirement in requirements)
        {
            if (!context.Request.RouteValues.TryGetValue(requirement.RouteValueName, out var rawArtifactId) ||
                string.IsNullOrWhiteSpace(rawArtifactId?.ToString()) ||
                !await artifacts.HasAccessAsync(
                    tenant.TenantId,
                    userId,
                    requirement.ArtifactKind,
                    rawArtifactId.ToString()!,
                    context.RequestAborted).ConfigureAwait(false))
            {
                await RefuseAsync(context).ConfigureAwait(false);
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }

    private static async Task RefuseAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        var refusal = GovernedRefusal.Create(
            ArtifactScopeNotFoundReasonCode,
            "The requested project artifact was not found or is not visible.",
            context.Items[RequestTracingMiddleware.CorrelationHeaderName]?.ToString() ?? context.TraceIdentifier,
            blockedReasons: ["The artifact is outside the selected project access scope or does not exist."],
            nextSafeActions: ["Open the artifact from an accessible project route."],
            forbiddenActions: ["Read or mutate an artifact outside active project membership."]);
        await context.Response.WriteAsJsonAsync(refusal, context.RequestAborted).ConfigureAwait(false);
    }
}
