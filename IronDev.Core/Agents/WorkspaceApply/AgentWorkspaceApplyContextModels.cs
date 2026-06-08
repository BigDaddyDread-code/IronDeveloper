using IronDev.Core.Agents.ApprovalPolicy;

namespace IronDev.Core.Agents.WorkspaceApply;

public sealed record AgentWorkspaceApplyContextRequest
{
    public required string ProjectId { get; init; }
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public ProjectApprovalPolicy? Policy { get; init; }
}

public sealed record AgentWorkspaceApplyContext
{
    public required string ProjectId { get; init; }
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public WorkspaceApplyReportSummary? WorkspaceApply { get; init; }
    public WorkspaceApplyRecommendation? WorkspaceApplyRecommendation { get; init; }
    public WorkspaceApplyActionRequest? WorkspaceApplyActionRequest { get; init; }
    public WorkspaceApplyActionReview? WorkspaceApplyActionReview { get; init; }
    public WorkspaceApplyPolicyContext? WorkspaceApplyPolicyContext { get; init; }
    public required bool ContextAvailable { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
}
