using IronDev.Core.Sandbox;

namespace IronDev.Infrastructure.Services.Sandbox;

/// <summary>
/// Keeps project shaping and repository setup available when privileged HCS runtime
/// configuration is absent or invalid. It never falls back to host execution.
/// </summary>
public sealed class UnavailableHcsContainerRuntime(string safeReason) : IHcsContainerRuntime
{
    private readonly string _safeReason = string.IsNullOrWhiteSpace(safeReason)
        ? "The production Windows sandbox runtime is not configured."
        : safeReason;

    public Task<HcsContainerProbeResult> ProbeAsync(
        HcsContainerProbeRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(new HcsContainerProbeResult(
            false,
            SandboxCapabilityStates.Unavailable,
            SandboxReasonCodes.RuntimeUnavailable,
            _safeReason));

    public Task<HcsContainerRuntimeResult> ExecuteAsync(
        HcsContainerRuntimeRequest request,
        CancellationToken cancellationToken = default) => Task.FromException<HcsContainerRuntimeResult>(
            new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable, _safeReason));

    public Task<HcsCompletedEvidence?> TryReadCompletedEvidenceAsync(
        HcsCompletedEvidenceRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult<HcsCompletedEvidence?>(null);

    public Task<HcsExecutionCleanupResult> RecoverExecutionAsync(
        HcsExecutionCleanupRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(new HcsExecutionCleanupResult(
            ContainerCleanupConfirmed: false,
            EvidenceCleanupConfirmed: false,
            _safeReason));

    public Task<HcsContainerRecoveryResult> RecoverOwnedContainersAsync(
        HcsContainerRecoveryRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(new HcsContainerRecoveryResult(
            0,
            0,
            0,
            true,
            "No configured sandbox runtime resources were eligible for recovery."));
}
