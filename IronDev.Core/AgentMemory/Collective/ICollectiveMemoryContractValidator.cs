namespace IronDev.Core.AgentMemory.Collective;

public interface ICollectiveMemoryContractValidator
{
    IReadOnlyList<CollectiveMemoryValidationIssue> Validate(CollectiveMemoryItem item);
}
