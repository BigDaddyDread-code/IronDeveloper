using System.Globalization;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IronDev.Api.Filters;

public sealed class RouteBodyScopeBindingFilter : IAsyncActionFilter
{
    public const string ProjectMismatchReasonCode = "route_body_project_scope_mismatch";
    public const string TenantMismatchReasonCode = "route_body_tenant_scope_mismatch";

    private static readonly HashSet<string> WriteMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!WriteMethods.Contains(context.HttpContext.Request.Method))
        {
            await next();
            return;
        }

        foreach (var scope in new[]
        {
            new ScopeRoute("projectId", "ProjectId", ProjectMismatchReasonCode),
            new ScopeRoute("tenantId", "TenantId", TenantMismatchReasonCode)
        })
        {
            if (!TryRouteValue(context, scope.RouteName, out var routeValue))
                continue;

            foreach (var argument in context.ActionArguments)
            {
                if (argument.Key.Equals(scope.RouteName, StringComparison.OrdinalIgnoreCase) || argument.Value is null)
                    continue;

                if (TryFindMismatch(
                    argument.Value,
                    scope.BodyName,
                    routeValue,
                    new HashSet<object>(ReferenceEqualityComparer.Instance),
                    depth: 0,
                    out var bodyValue))
                {
                    context.Result = new ObjectResult(new RouteBodyScopeMismatchResponse
                    {
                        ReasonCode = scope.ReasonCode,
                        Message = $"Body {scope.BodyName} must be omitted or match route {scope.RouteName}.",
                        BlockedReasons = [$"Route {scope.RouteName} is authoritative for this write."],
                        CorrelationId = context.HttpContext.TraceIdentifier,
                        RouteValue = routeValue,
                        BodyValue = bodyValue
                    })
                    {
                        StatusCode = StatusCodes.Status400BadRequest
                    };
                    return;
                }
            }
        }

        await next();
    }

    private static bool TryRouteValue(ActionExecutingContext context, string name, out string value)
    {
        value = string.Empty;
        if (!context.RouteData.Values.TryGetValue(name, out var raw) || raw is null)
            return false;

        value = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryFindMismatch(
        object? value,
        string propertyName,
        string routeValue,
        HashSet<object> visited,
        int depth,
        out string bodyValue)
    {
        bodyValue = string.Empty;
        if (value is null || depth > 6)
            return false;

        if (value is JsonElement json)
            return TryFindJsonMismatch(json, propertyName, routeValue, out bodyValue);

        var type = value.GetType();
        if (IsScalar(type))
            return false;
        if (!type.IsValueType && !visited.Add(value))
            return false;

        if (value is IEnumerable items)
        {
            foreach (var item in items)
            {
                if (TryFindMismatch(item, propertyName, routeValue, visited, depth + 1, out bodyValue))
                    return true;
            }
            return false;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = ScalarText(propertyValue);
                if (!IsAbsent(candidate) && !ScopeValuesEqual(routeValue, candidate))
                {
                    bodyValue = candidate;
                    return true;
                }
            }

            if (TryFindMismatch(propertyValue, propertyName, routeValue, visited, depth + 1, out bodyValue))
                return true;
        }

        return false;
    }

    private static bool TryFindJsonMismatch(JsonElement element, string propertyName, string routeValue, out string bodyValue)
    {
        bodyValue = string.Empty;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        var candidate = ScalarText(property.Value);
                        if (!IsAbsent(candidate) && !ScopeValuesEqual(routeValue, candidate))
                        {
                            bodyValue = candidate;
                            return true;
                        }
                    }

                    if (TryFindJsonMismatch(property.Value, propertyName, routeValue, out bodyValue))
                        return true;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindJsonMismatch(item, propertyName, routeValue, out bodyValue))
                        return true;
                }
                break;
        }

        return false;
    }

    private static string ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText()
    };

    private static string ScalarText(object? value) => value switch
    {
        null => string.Empty,
        JsonElement json => ScalarText(json),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
    };

    private static bool IsScalar(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(Guid) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan) ||
        type == typeof(CancellationToken);

    private static bool IsAbsent(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value == "0" ||
        value.Equals(Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool ScopeValuesEqual(string routeValue, string bodyValue)
    {
        if (Guid.TryParse(routeValue, out var routeGuid) && Guid.TryParse(bodyValue, out var bodyGuid))
            return routeGuid == bodyGuid;

        if (long.TryParse(routeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var routeNumber) &&
            long.TryParse(bodyValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bodyNumber))
            return routeNumber == bodyNumber;

        return routeValue.Equals(bodyValue, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ScopeRoute(string RouteName, string BodyName, string ReasonCode);
}

public sealed record RouteBodyScopeMismatchResponse
{
    public bool Allowed { get; init; }
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];
    public required string CorrelationId { get; init; }
    public required string RouteValue { get; init; }
    public required string BodyValue { get; init; }
}
