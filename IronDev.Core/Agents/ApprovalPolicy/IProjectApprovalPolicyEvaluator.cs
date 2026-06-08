namespace IronDev.Core.Agents.ApprovalPolicy;

public interface IProjectApprovalPolicyEvaluator
{
    ProjectApprovalEvaluationResult Evaluate(ProjectApprovalEvaluationRequest request);
}
