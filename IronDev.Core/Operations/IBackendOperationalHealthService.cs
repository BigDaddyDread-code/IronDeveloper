namespace IronDev.Core.Operations;

public interface IBackendOperationalHealthService
{
    Task<BackendOperationalHealthResponse> GetHealthAsync(
        BackendOperationalHealthRequest request,
        CancellationToken cancellationToken = default);
}
