namespace IronDev.Core.Agents.Skills;

public interface IAgentSkillRequestContextService
{
    AgentSkillRequestContext Create(AgentSkillRequestContextInput input);
}
