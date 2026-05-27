using IronDev.Core.Tools;

namespace IronDev.Infrastructure.Tools;

public sealed class GovernedToolPolicyEvaluator
{
    public GovernedToolPolicyDecision Evaluate<TInput>(
        GovernedToolDefinition definition,
        GovernedToolRequest<TInput> request)
        where TInput : notnull
    {
        if (!string.Equals(definition.Name, request.ToolName, StringComparison.OrdinalIgnoreCase))
        {
            return GovernedToolPolicyDecision.Rejected(
                $"Request tool '{request.ToolName}' does not match registered tool '{definition.Name}'.");
        }

        if (definition.MutatesState)
        {
            return GovernedToolPolicyDecision.Rejected(
                $"Tool '{definition.Name}' is mutation-capable and cannot run in the governed read-only tool path.");
        }

        if (definition.AllowsFileWrites)
        {
            return GovernedToolPolicyDecision.Rejected(
                $"Tool '{definition.Name}' allows file writes and cannot run in the governed read-only tool path.");
        }

        if (definition.AllowsProcessExecution)
        {
            return GovernedToolPolicyDecision.Rejected(
                $"Tool '{definition.Name}' allows process execution and cannot run in the governed read-only tool path.");
        }

        if (definition.AllowsNetworkAccess)
        {
            return GovernedToolPolicyDecision.Rejected(
                $"Tool '{definition.Name}' allows network access and cannot run in the governed read-only tool path.");
        }

        if (definition.AllowsWorkspaceMutation)
        {
            return GovernedToolPolicyDecision.Rejected(
                $"Tool '{definition.Name}' allows workspace mutation and cannot run in the governed read-only tool path.");
        }

        if (!definition.AllowedCallers.Contains(request.RequestedBy, StringComparer.OrdinalIgnoreCase))
        {
            return GovernedToolPolicyDecision.Rejected(
                $"Caller '{request.RequestedBy}' is not allowed to run governed tool '{definition.Name}'.");
        }

        if (!definition.AllowsNestedCalls &&
            (request.NestedCallDepth > 0 || !string.IsNullOrWhiteSpace(request.ParentRequestId)))
        {
            return GovernedToolPolicyDecision.Rejected(
                $"Nested governed tool call '{definition.Name}' was rejected.");
        }

        return GovernedToolPolicyDecision.Allowed();
    }
}

public sealed record GovernedToolPolicyDecision
{
    public required bool IsAllowed { get; init; }
    public required string Reason { get; init; }

    public static GovernedToolPolicyDecision Allowed() =>
        new()
        {
            IsAllowed = true,
            Reason = "Governed tool policy allowed this read-only call."
        };

    public static GovernedToolPolicyDecision Rejected(string reason) =>
        new()
        {
            IsAllowed = false,
            Reason = reason
        };
}
