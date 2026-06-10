namespace IronDev.Core.AgentMemory.Collective;

public interface ICollectiveMemoryRetrievalService
{
    Task<CollectiveMemoryRetrievalResult> RetrieveAsync(
        CollectiveMemoryRetrievalQuery query,
        CancellationToken cancellationToken = default);
}
