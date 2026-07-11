using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace IronDev.Api.OpenApi;

public sealed class GovernedJsonRequestBodyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requestType = ResolveRequestType(context);
        if (requestType is null || operation.RequestBody?.Content is null)
            return;

        var schema = context.SchemaGenerator.GenerateSchema(requestType, context.SchemaRepository);
        foreach (var mediaType in operation.RequestBody.Content.Values)
            mediaType.Schema = schema;
    }

    private static Type? ResolveRequestType(OperationFilterContext context)
    {
        var declaringType = context.MethodInfo.DeclaringType;
        var methodName = context.MethodInfo.Name;

        if (declaringType == typeof(AcceptedApprovalsV1Controller) &&
            methodName == nameof(AcceptedApprovalsV1Controller.Create))
            return typeof(CreateAcceptedApprovalRequest);

        if (declaringType == typeof(PolicySatisfactionsV1Controller) &&
            methodName == nameof(PolicySatisfactionsV1Controller.Create))
            return typeof(PolicySatisfactionCreateRequest);

        return null;
    }
}
