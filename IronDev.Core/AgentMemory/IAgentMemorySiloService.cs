namespace IronDev.Core.AgentMemory;

public interface IAgentMemorySiloService
{
    IAgentMemorySilo Open(AgentMemorySiloContext context);
}
