namespace IronDev.Core.Agents;

public interface IAgentDefinitionValidator
{
    IReadOnlyList<AgentDefinitionValidationIssue> Validate(AgentDefinition definition);
}
