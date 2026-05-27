using IronDev.Core.Tools;

namespace IronDev.Infrastructure.Tools;

public sealed class GovernedToolRegistry : IGovernedToolRegistry
{
    private readonly IReadOnlyDictionary<string, IGovernedToolRegistration> _tools;
    private readonly GovernedToolPolicyEvaluator _policyEvaluator;

    public GovernedToolRegistry(
        IEnumerable<IGovernedToolRegistration> tools,
        GovernedToolPolicyEvaluator policyEvaluator)
    {
        _tools = tools.ToDictionary(tool => tool.Definition.Name, StringComparer.OrdinalIgnoreCase);
        _policyEvaluator = policyEvaluator;
    }

    public IReadOnlyList<GovernedToolDefinition> ListTools() =>
        _tools.Values
            .Select(tool => tool.Definition)
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool IsRegistered(string toolName) => _tools.ContainsKey(toolName);

    public async Task<GovernedToolResult<TOutput>> RunAsync<TInput, TOutput>(
        GovernedToolRequest<TInput> request,
        CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        var started = DateTimeOffset.UtcNow;
        if (!_tools.TryGetValue(request.ToolName, out var registration))
        {
            return Rejected<TInput, TOutput>(
                request,
                $"Unknown governed tool '{request.ToolName}'.",
                started);
        }

        var definition = registration.Definition;
        if (definition.InputType != typeof(TInput) || definition.OutputType != typeof(TOutput))
        {
            return Rejected<TInput, TOutput>(
                request,
                $"Governed tool '{definition.Name}' expects input '{definition.InputType.Name}' and output '{definition.OutputType.Name}', not '{typeof(TInput).Name}' and '{typeof(TOutput).Name}'.",
                started);
        }

        var policy = _policyEvaluator.Evaluate(definition, request);
        if (!policy.IsAllowed)
        {
            return Rejected<TInput, TOutput>(request, policy.Reason, started);
        }

        if (registration is not IGovernedTool<TInput, TOutput> typedTool)
        {
            return Rejected<TInput, TOutput>(
                request,
                $"Governed tool '{definition.Name}' registration does not implement its declared typed contract.",
                started);
        }

        return await typedTool.ExecuteAsync(request, cancellationToken);
    }

    private static GovernedToolResult<TOutput> Rejected<TInput, TOutput>(
        GovernedToolRequest<TInput> request,
        string summary,
        DateTimeOffset started)
        where TInput : notnull =>
        GovernedToolResult<TOutput>.Rejected(
            new GovernedToolRequestMarker
            {
                RequestId = request.RequestId,
                ToolName = request.ToolName
            },
            summary,
            [summary],
            started);
}
