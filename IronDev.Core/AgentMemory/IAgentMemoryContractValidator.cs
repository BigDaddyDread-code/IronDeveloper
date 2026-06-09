namespace IronDev.Core.AgentMemory;

public interface IAgentMemoryContractValidator
{
    MemoryValidationResult Validate(AgentLocalMemoryItem item);

    MemoryValidationResult Validate(MemoryInfluenceRecord influence);

    MemoryValidationResult Validate(HandoffMemorySlice handoffSlice);
}
