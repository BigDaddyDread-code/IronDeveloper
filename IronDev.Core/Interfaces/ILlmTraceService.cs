using System.Collections.Generic;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface ILlmTraceService
{
    void AddTrace(LlmTraceEntry trace);
    IReadOnlyList<LlmTraceEntry> GetRecentTraces(int max = 100);
    void Clear();
    string ExportTrace(LlmTraceEntry trace);
    string ExportAll();
}
