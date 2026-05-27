using IronDev.Core.Tools;

namespace IronDev.Infrastructure.Tools;

public sealed class GovernedToolRegistry : IGovernedToolRegistry
{
    private readonly IReadOnlyDictionary<string, IGovernedToolRegistration> _tools;
    private readonly GovernedToolPolicyEvaluator _policyEvaluator;
    private readonly IGovernedToolThoughtLedger _thoughtLedger;

    public GovernedToolRegistry(
        IEnumerable<IGovernedToolRegistration> tools,
        GovernedToolPolicyEvaluator policyEvaluator,
        IGovernedToolThoughtLedger? thoughtLedger = null)
    {
        _tools = tools.ToDictionary(tool => tool.Definition.Name, StringComparer.OrdinalIgnoreCase);
        _policyEvaluator = policyEvaluator;
        _thoughtLedger = thoughtLedger ?? NullGovernedToolThoughtLedger.Instance;
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
            return await RecordAndReturnAsync(
                Rejected<TInput, TOutput>(
                    request,
                    $"Unknown governed tool '{request.ToolName}'.",
                    started),
                request.RequestedBy,
                cancellationToken);
        }

        var definition = registration.Definition;
        if (definition.InputType != typeof(TInput) || definition.OutputType != typeof(TOutput))
        {
            return await RecordAndReturnAsync(
                Rejected<TInput, TOutput>(
                    request,
                    $"Governed tool '{definition.Name}' expects input '{definition.InputType.Name}' and output '{definition.OutputType.Name}', not '{typeof(TInput).Name}' and '{typeof(TOutput).Name}'.",
                    started),
                request.RequestedBy,
                cancellationToken);
        }

        var policy = _policyEvaluator.Evaluate(definition, request);
        if (!policy.IsAllowed)
        {
            return await RecordAndReturnAsync(
                Rejected<TInput, TOutput>(request, policy.Reason, started),
                request.RequestedBy,
                cancellationToken);
        }

        if (registration is not IGovernedTool<TInput, TOutput> typedTool)
        {
            return await RecordAndReturnAsync(
                Rejected<TInput, TOutput>(
                    request,
                    $"Governed tool '{definition.Name}' registration does not implement its declared typed contract.",
                    started),
                request.RequestedBy,
                cancellationToken);
        }

        try
        {
            return await RecordAndReturnAsync(
                await typedTool.ExecuteAsync(request, cancellationToken),
                request.RequestedBy,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await RecordAndReturnAsync(
                GovernedToolResult<TOutput>.Failed(
                    Marker(request),
                    $"Governed tool '{definition.Name}' failed: {ex.Message}",
                    [ex.Message],
                    started,
                    definition.Boundary),
                request.RequestedBy,
                cancellationToken);
        }
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

    private async Task<GovernedToolResult<TOutput>> RecordAndReturnAsync<TOutput>(
        GovernedToolResult<TOutput> result,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        await _thoughtLedger.RecordAsync(
            new GovernedToolThoughtLedgerEntry
            {
                RequestId = result.RequestId,
                ToolName = result.ToolName,
                RequestedBy = requestedBy,
                Status = result.Status,
                Summary = result.Summary,
                EvidenceRefs = result.EvidenceRefs,
                BlockedActions = result.BlockedActions,
                StartedAtUtc = result.StartedAtUtc,
                CompletedAtUtc = result.CompletedAtUtc,
                ExecutionDurationMs = result.ExecutionDurationMs,
                Boundary = result.Boundary
            },
            cancellationToken);

        return result;
    }

    private static GovernedToolRequestMarker Marker<TInput>(GovernedToolRequest<TInput> request)
        where TInput : notnull =>
        new()
        {
            RequestId = request.RequestId,
            ToolName = request.ToolName
        };
}
