using System.Security.Claims;
using IronDev.Core.Audit;

namespace IronDev.Api.Middleware;

public sealed class UserMutationAttributionMiddleware
{
    public const string CausationHeaderName = "X-Causation-ID";
    public const string SourceSurfaceHeaderName = "X-IronDev-Source-Surface";
    public const string SourceClientHeaderName = "X-IronDev-Source-Client";

    private static readonly HashSet<string> WriteMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<UserMutationAttributionMiddleware> _logger;

    public UserMutationAttributionMiddleware(
        RequestDelegate next,
        ILogger<UserMutationAttributionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserMutationAttributionStore store)
    {
        if (!ShouldRecord(context, out var actorUserId))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var basis = BuildRecord(context, actorUserId, "Attempted", null);
        await store.AppendAsync(basis, context.RequestAborted).ConfigureAwait(false);

        Exception? dispatchException = null;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            dispatchException = ex;
            throw;
        }
        finally
        {
            var phase = dispatchException is not null
                ? "Failed"
                : context.Response.StatusCode >= StatusCodes.Status400BadRequest
                    ? "Refused"
                    : "Completed";
            var statusCode = dispatchException is null
                ? context.Response.StatusCode
                : StatusCodes.Status500InternalServerError;

            try
            {
                await store.AppendAsync(BuildRecord(context, actorUserId, phase, statusCode), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception attributionException)
            {
                _logger.LogCritical(
                    attributionException,
                    "Mutation attribution completion failed for correlation {CorrelationId}; the durable attempt remains recorded.",
                    basis.CorrelationId);
            }
        }
    }

    private static bool ShouldRecord(HttpContext context, out int actorUserId)
    {
        actorUserId = 0;
        if (!WriteMethods.Contains(context.Request.Method) || context.User.Identity?.IsAuthenticated != true)
            return false;

        return int.TryParse(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub"),
            out actorUserId) && actorUserId > 0;
    }

    private static UserMutationAttributionRecord BuildRecord(
        HttpContext context,
        int actorUserId,
        string phase,
        int? statusCode)
    {
        var correlationId = context.Items[RequestTracingMiddleware.CorrelationHeaderName]?.ToString()
            ?? context.TraceIdentifier;

        return new UserMutationAttributionRecord
        {
            ActorUserId = actorUserId,
            TenantId = ResolveTenantId(context),
            ProjectId = RouteValue(context, "projectId"),
            CorrelationId = correlationId,
            CausationId = HeaderValue(context, CausationHeaderName),
            TimestampUtc = DateTimeOffset.UtcNow,
            SourceSurface = HeaderValue(context, SourceSurfaceHeaderName) ?? "api",
            SourceClient = HeaderValue(context, SourceClientHeaderName) ?? InferClient(context),
            Method = context.Request.Method,
            Route = context.Request.Path.Value ?? "/",
            Phase = phase,
            StatusCode = statusCode
        };
    }

    private static int? ResolveTenantId(HttpContext context)
    {
        var routeTenant = RouteValue(context, "tenantId");
        var candidate = routeTenant ?? context.User.FindFirstValue("tenant_id");
        return int.TryParse(candidate, out var tenantId) && tenantId > 0 ? tenantId : null;
    }

    private static string? RouteValue(HttpContext context, string key) =>
        context.Request.RouteValues.TryGetValue(key, out var value) && value is not null
            ? value.ToString()
            : null;

    private static string? HeaderValue(HttpContext context, string name)
    {
        var value = context.Request.Headers[name].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string InferClient(HttpContext context)
    {
        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (userAgent.Contains("Tauri", StringComparison.OrdinalIgnoreCase))
            return "tauri";
        if (userAgent.Contains("Mozilla", StringComparison.OrdinalIgnoreCase))
            return "browser";
        return "api-client";
    }
}
