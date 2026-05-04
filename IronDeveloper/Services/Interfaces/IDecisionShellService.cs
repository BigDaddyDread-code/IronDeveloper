using System.Collections.Generic;
using IronDev.Agent.Models;

namespace IronDev.Agent.Services.Interfaces;

public interface IDecisionShellService
{
    IReadOnlyList<DecisionSummary> GetDecisions();
}
