using System;
using System.Collections.Generic;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface ILlmTraceService
{
    // ── Core operations ───────────────────────────────────────────────────
    void AddTrace(LlmTraceEntry trace);
    IReadOnlyList<LlmTraceEntry> GetRecentTraces(int max = 100);
    void Clear();
    string ExportTrace(LlmTraceEntry trace);
    string ExportAll();

    // ── Live update ───────────────────────────────────────────────────────
    /// <summary>Raised on the thread that called AddTrace when a new trace is stored.</summary>
    event EventHandler<LlmTraceEntry>? TraceAdded;

    // ── Global on/off ─────────────────────────────────────────────────────
    /// <summary>When false, AddTrace is a no-op and no events are raised. Default: true.</summary>
    bool IsTracingEnabled { get; set; }
}
