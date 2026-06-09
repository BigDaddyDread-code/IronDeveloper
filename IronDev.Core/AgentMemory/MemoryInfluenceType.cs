namespace IronDev.Core.AgentMemory;

public enum MemoryInfluenceType
{
    AvoidedAction = 1,
    SelectedAction = 2,
    ToolCallJustified = 3,
    HandoffIncluded = 4,
    ProposalCreated = 5,
    EscalationTriggered = 6,
    CriticFindingAffected = 7
}
