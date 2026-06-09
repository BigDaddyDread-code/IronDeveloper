namespace IronDev.Core.AgentMemory;

public enum AgentLocalMemoryEventType
{
    Created = 1,
    Superseded = 2,
    Expired = 3,
    Invalidated = 4,
    ProposedForReview = 5,
    Rejected = 6,
    Accepted = 7
}
