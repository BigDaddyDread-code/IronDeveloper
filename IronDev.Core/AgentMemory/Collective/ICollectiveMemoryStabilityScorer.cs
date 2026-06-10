namespace IronDev.Core.AgentMemory.Collective;

public interface ICollectiveMemoryStabilityScorer
{
    CollectiveMemoryStabilityScore Score(CollectiveMemoryStabilityInput? input);
}
