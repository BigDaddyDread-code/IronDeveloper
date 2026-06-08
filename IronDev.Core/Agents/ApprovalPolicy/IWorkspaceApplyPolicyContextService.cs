namespace IronDev.Core.Agents.ApprovalPolicy;

public interface IWorkspaceApplyPolicyContextService
{
    WorkspaceApplyPolicyContext Create(WorkspaceApplyPolicyContextInput input);
}
