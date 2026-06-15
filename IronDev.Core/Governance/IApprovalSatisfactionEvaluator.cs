namespace IronDev.Core.Governance;

public interface IApprovalSatisfactionEvaluator
{
    ApprovalSatisfactionEvaluation Evaluate(
        ApprovalRequirement requirement,
        AcceptedApprovalRecord? acceptedApproval);
}
