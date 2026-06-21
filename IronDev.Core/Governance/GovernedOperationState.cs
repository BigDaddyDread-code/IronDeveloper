namespace IronDev.Core.Governance;

public enum GovernedOperationState
{
    Eligible = 1,
    Blocked = 2,
    Running = 3,
    Completed = 4,
    Failed = 5,
    Expired = 6
}
