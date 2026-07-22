namespace IronDev.Infrastructure.Services.Sandbox;

public interface IHcsContainerRuntime
{
    Task<HcsContainerProbeResult> ProbeAsync(
        HcsContainerProbeRequest request,
        CancellationToken cancellationToken = default);

    Task<HcsContainerRuntimeResult> ExecuteAsync(
        HcsContainerRuntimeRequest request,
        CancellationToken cancellationToken = default);

    Task<HcsCompletedEvidence?> TryReadCompletedEvidenceAsync(
        HcsCompletedEvidenceRequest request,
        CancellationToken cancellationToken = default);

    Task<HcsExecutionCleanupResult> RecoverExecutionAsync(
        HcsExecutionCleanupRequest request,
        CancellationToken cancellationToken = default);

    Task<HcsContainerRecoveryResult> RecoverOwnedContainersAsync(
        HcsContainerRecoveryRequest request,
        CancellationToken cancellationToken = default);
}

public interface IDockerCommandRunner
{
    Task<DockerCommandResult> RunAsync(
        DockerCommandRequest request,
        CancellationToken cancellationToken = default);
}
