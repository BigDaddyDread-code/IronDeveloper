namespace IronDev.Core.Agents.Skills;

public interface IAgentSkillRegistry
{
    IReadOnlyList<AgentSkillDefinition> List();

    AgentSkillDefinition? Find(string skillId);
}
