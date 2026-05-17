using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using Microsoft.Extensions.Logging;

namespace IronDev.Infrastructure.Services;

public class LlmTraceService : ILlmTraceService
{
    private readonly List<LlmTraceEntry> _traces = new();
    private readonly object _lock = new();
    private readonly ILogger<LlmTraceService>? _logger;

    public LlmTraceService(ILogger<LlmTraceService>? logger = null)
    {
        _logger = logger;
    }

    // ── ILlmTraceService ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public event EventHandler<LlmTraceEntry>? TraceAdded;

    /// <inheritdoc/>
    public bool IsTracingEnabled { get; set; } = true;   // default ON

    public void AddTrace(LlmTraceEntry trace)
    {
        if (trace == null || !IsTracingEnabled) return;

        // Redact sensitive info before storing
        RedactTrace(trace);

        lock (_lock)
        {
            _traces.Insert(0, trace);
            if (_traces.Count > 500) // Keep a reasonable amount in memory
            {
                _traces.RemoveAt(_traces.Count - 1);
            }
        }

        LogTraceSummary(trace);

        // Raise outside the lock so subscribers don't deadlock
        TraceAdded?.Invoke(this, trace);
    }

    private void LogTraceSummary(LlmTraceEntry trace)
    {
        if (trace.WasSuccessful || string.IsNullOrWhiteSpace(trace.ErrorMessage))
        {
            _logger?.LogInformation(
                "LLM trace captured: {FeatureName} model={Model} projectId={ProjectId} ticketId={TicketId} traceGroupId={TraceGroupId} durationMs={DurationMs} tokens={TokenUsageTotal}",
                trace.FeatureName,
                trace.Model,
                trace.ProjectId,
                trace.TicketId,
                trace.TraceGroupId,
                trace.DurationMs,
                trace.TokenUsageTotal ?? trace.EstimatedTokens);
            return;
        }

        _logger?.LogWarning(
            "LLM trace captured with failure: {FeatureName} model={Model} projectId={ProjectId} ticketId={TicketId} traceGroupId={TraceGroupId} durationMs={DurationMs} error={ErrorMessage}",
            trace.FeatureName,
            trace.Model,
            trace.ProjectId,
            trace.TicketId,
            trace.TraceGroupId,
            trace.DurationMs,
            trace.ErrorMessage);
    }

    public IReadOnlyList<LlmTraceEntry> GetRecentTraces(int max = 100)
    {
        lock (_lock)
        {
            return _traces.Take(max).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _traces.Clear();
        }
    }

    public string ExportTrace(LlmTraceEntry trace)
    {
        if (trace == null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("=== LLM TRACE ===");
        sb.AppendLine($"IronDev: {AppBuildInfo.DisplayName} ({AppBuildInfo.Version})");
        sb.AppendLine($"Feature: {trace.FeatureName}");
        sb.AppendLine($"Model: {trace.Model}");
        sb.AppendLine($"Created: {trace.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Duration: {trace.DurationMs}ms");
        sb.AppendLine($"Estimated tokens: {trace.EstimatedTokens}");
        sb.AppendLine($"Success: {trace.WasSuccessful}");
        if (!string.IsNullOrEmpty(trace.TraceGroupId))
            sb.AppendLine($"TraceGroupId: {trace.TraceGroupId}");
        if (trace.ParentTraceId.HasValue)
            sb.AppendLine($"ParentTraceId: {trace.ParentTraceId}");
        if (!string.IsNullOrEmpty(trace.ErrorMessage))
            sb.AppendLine($"Error: {trace.ErrorMessage}");
        if (!string.IsNullOrEmpty(trace.Warnings))
            sb.AppendLine($"Warnings: {trace.Warnings}");
        sb.AppendLine();

        sb.AppendLine("=== CONTEXT SUMMARY ===");
        sb.AppendLine(string.IsNullOrEmpty(trace.ContextSummary) ? "(not captured)" : trace.ContextSummary);
        sb.AppendLine();

        sb.AppendLine("=== PROMPT SENT ===");
        sb.AppendLine(trace.RequestText ?? "N/A");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(trace.RawRequestJson))
        {
            sb.AppendLine("=== RAW REQUEST (JSON) ===");
            sb.AppendLine(trace.RawRequestJson);
            sb.AppendLine();
        }

        sb.AppendLine("=== RAW RESPONSE ===");
        sb.AppendLine(trace.RawResponseText ?? "N/A");
        sb.AppendLine();

        sb.AppendLine("=== PARSED RESULT ===");
        sb.AppendLine(trace.ParsedResponseSummary ?? "N/A");
        sb.AppendLine();

        return sb.ToString();
    }

    public string ExportAll()
    {
        var sb = new StringBuilder();
        List<LlmTraceEntry> snapshot;
        lock (_lock)
        {
            snapshot = _traces.ToList();
        }

        foreach (var trace in snapshot)
        {
            sb.AppendLine(ExportTrace(trace));
            sb.AppendLine("--------------------------------------------------------------------------------");
        }

        return sb.ToString();
    }

    private void RedactTrace(LlmTraceEntry trace)
    {
        trace.RequestText    = Redact(trace.RequestText);
        trace.RawRequestJson = Redact(trace.RawRequestJson);
        trace.RawResponseText= Redact(trace.RawResponseText);
        trace.ErrorMessage   = Redact(trace.ErrorMessage);
    }

    private static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var patterns = new[]
        {
            @"(?i)(api[-_]?key|secret|password|token|auth|authorization|bearer)[\s=:""']+[A-Za-z0-9\-_.~%]+",
            @"(?i)Server=[^;]+;Database=[^;]+;User Id=[^;]+;Password=[^;]+;"
        };

        foreach (var pattern in patterns)
        {
            text = Regex.Replace(text, pattern, m =>
            {
                var key = m.Value.Split(new[] { ':', '=', ' ', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)[0];
                return $"{key}: [REDACTED]";
            });
        }

        return text;
    }
}
