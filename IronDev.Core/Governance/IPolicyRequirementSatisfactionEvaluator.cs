namespace IronDev.Core.Governance;

public interface IPolicyRequirementSatisfactionEvaluator
{
    PolicyRequirementSatisfactionEvaluation Evaluate(
        PolicyRequirement? requirement,
        ApprovalSatisfactionEvaluation? approvalEvaluation);
}
