using System.Collections.Generic;

namespace IronDev.Core.Models;

public class BuildArchitectureReconciliation
{
    public string FailureCategory { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string CurrentProfileValue { get; set; } = string.Empty;
    public string DetectedProjectFacts { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public bool RequiresUserApproval { get; set; }
    
    public List<string> ProposedProfileUpdates { get; set; } = new();
    public List<string> ProposedArchitectureDecisions { get; set; } = new();
    public List<string> Questions { get; set; } = new();
    public List<ReconciliationAction> AvailableActions { get; set; } = new();
}

public class ReconciliationAction
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty; // e.g. "AddPackage", "RegenerateTests", "UpdateProfileOnly"
}
