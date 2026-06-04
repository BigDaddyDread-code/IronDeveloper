using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Client.Traces;

public sealed class InMemoryTraceClient : ITraceApiClient
{
    private readonly List<LlmTraceEntry> _traces = [];

    public bool IsTracingEnabled { get; set; } = true;
    public event EventHandler<LlmTraceEntry>? TraceAdded;

    public void AddTrace(LlmTraceEntry entry)
    {
        if (!IsTracingEnabled)
            return;

        _traces.Insert(0, entry);
        TraceAdded?.Invoke(this, entry);
    }

    public IReadOnlyList<LlmTraceEntry> GetRecentTraces(int take = 100) => _traces.Take(take).ToList();
    public IReadOnlyList<LlmTraceEntry> GetTracesByGroupId(string traceGroupId, int take = 100) =>
        string.IsNullOrWhiteSpace(traceGroupId)
            ? []
            : _traces
                .Where(trace => string.Equals(trace.TraceGroupId, traceGroupId, StringComparison.Ordinal))
                .Take(take)
                .ToList();

    public void Clear() => _traces.Clear();

    public string ExportTrace(LlmTraceEntry entry) => System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public string ExportAll() => System.Text.Json.JsonSerializer.Serialize(_traces, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}
