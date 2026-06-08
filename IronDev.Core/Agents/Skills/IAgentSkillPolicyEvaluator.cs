namespace IronDev.Core.Agents.Skills;

public interface IAgentSkillPolicyEvaluator
{
    AgentSkillPolicyEvaluation Evaluate(AgentSkillPolicyEvaluationRequest request);
}
