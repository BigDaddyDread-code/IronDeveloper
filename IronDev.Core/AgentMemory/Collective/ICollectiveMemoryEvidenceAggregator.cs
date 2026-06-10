namespace IronDev.Core.AgentMemory.Collective;

public interface ICollectiveMemoryEvidenceAggregator
{
    CollectiveMemoryAggregationResult Aggregate(CollectiveMemoryAggregationInput input);
}
