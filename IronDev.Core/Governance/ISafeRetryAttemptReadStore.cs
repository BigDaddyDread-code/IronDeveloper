namespace IronDev.Core.Governance;

public interface ISafeRetryAttemptReadStore
{
    Task<SafeRetryLineageReadResult?> FindRetryLineageAsync(
        SafeRetryAssessmentRequest request,
        CancellationToken cancellationToken);
}
