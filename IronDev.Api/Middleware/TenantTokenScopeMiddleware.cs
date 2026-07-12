using System.Security.Claims;
using IronDev.Core.Governance;

namespace IronDev.Api.Middleware;

public sealed class TenantTokenScopeMiddleware(RequestDelegate next)
{
    public const string TenantSelectionRequiredReasonCode = "tenant_selection_required";
    public const string TenantRouteMismatchReasonCode = "tenant_route_scope_not_found";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !context.Request.Path.StartsWithSegments("/api") ||
            IsBaseTokenEndpoint(context.Request))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var tenantClaims = context.User.FindAll("tenant_id").Select(claim => claim.Value).ToArray();
        if (tenantClaims.Length != 1 || !int.TryParse(tenantClaims[0], out var selectedTenantId) || selectedTenantId <= 0)
        {
            await RefuseAsync(
                context,
                StatusCodes.Status403Forbidden,
                GovernedRefusal.Create(
                    TenantSelectionRequiredReasonCode,
                    "Select an accessible tenant before opening product data.",
                    CorrelationId(context),
                    blockedReasons: ["The authenticated token does not identify one selected tenant."],
                    nextSafeActions: ["List accessible tenants and select one through the tenant selection endpoint."],
                    forbiddenActions: ["Read or mutate tenant or project product data."])).ConfigureAwait(false);
            return;
        }

        if (context.Request.RouteValues.TryGetValue("tenantId", out var rawRouteTenant) &&
            int.TryParse(rawRouteTenant?.ToString(), out var routeTenantId) &&
            routeTenantId != selectedTenantId)
        {
            await RefuseAsync(
                context,
                StatusCodes.Status404NotFound,
                GovernedRefusal.Create(
                    TenantRouteMismatchReasonCode,
                    "The requested resource was not found in the selected tenant.",
                    CorrelationId(context),
                    blockedReasons: ["The route is outside the selected tenant scope."],
                    nextSafeActions: ["Select the intended tenant through the tenant selection endpoint, if accessible."],
                    forbiddenActions: ["Read or mutate a route outside the selected tenant."])).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsBaseTokenEndpoint(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        return (HttpMethods.IsGet(request.Method) && path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase)) ||
               (HttpMethods.IsPost(request.Method) && path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase)) ||
               (HttpMethods.IsGet(request.Method) && path.Equals("/api/tenants", StringComparison.OrdinalIgnoreCase)) ||
               (HttpMethods.IsPost(request.Method) && path.Equals("/api/tenants/select", StringComparison.OrdinalIgnoreCase)) ||
               (HttpMethods.IsGet(request.Method) && path.Equals("/api/environment", StringComparison.OrdinalIgnoreCase));
    }

    private static string CorrelationId(HttpContext context) =>
        context.Items[RequestTracingMiddleware.CorrelationHeaderName]?.ToString() ?? context.TraceIdentifier;

    private static async Task RefuseAsync(HttpContext context, int statusCode, GovernedRefusalEnvelope refusal)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(refusal, context.RequestAborted).ConfigureAwait(false);
    }
}
