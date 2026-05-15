using System;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Service responsible for deciding the Context Agent route using LLM judgement 
/// mixed with deterministic safety guards.
/// </summary>
public interface IContextAgentRouteJudge
{
    Task<ContextAgentRouteDecision> DecideRouteAsync(
        ContextAgentRouteRequest request,
        CancellationToken cancellationToken = default);
}
