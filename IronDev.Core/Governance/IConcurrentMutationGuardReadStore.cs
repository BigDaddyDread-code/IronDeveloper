namespace IronDev.Core.Governance;

public interface IConcurrentMutationGuardReadStore
{
    Task<ConcurrentMutationGuardReadResult> FindPotentialConflictsAsync(
        ConcurrentMutationGuardRequest request,
        CancellationToken cancellationToken);
}
