using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IronDev.Core.Sandbox;

namespace IronDev.Infrastructure.Services.Sandbox;

/// <summary>
/// Production Windows-container boundary. Docker is used only as a client for the local
/// Windows engine; the engine creates an HCS-managed Hyper-V-isolated compute system.
/// Every claimed isolation property is inspected after creation and execution fails
/// closed before untrusted project code starts when any property drifts.
/// </summary>
public sealed partial class DockerHcsContainerRuntime : IHcsContainerRuntime
{
    private const int MaximumValidatedInputEntries = 250_000;
    private const int MaximumValidatedInputDepth = 64;
    private const long MaximumEvidenceTotalBytes = 12L * 1024 * 1024;
    private const long EvidenceMarkerReserveBytes = 4_096;
    private const long MaximumEvidenceLogCopyBytes =
        (MaximumEvidenceTotalBytes - EvidenceMarkerReserveBytes) / 6;

    private static readonly HashSet<string> AllowedDockerHostEnvironmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SystemRoot", "WINDIR", "TEMP", "TMP", "ComSpec", "ProgramData"
    };

    private readonly HcsContainerRuntimeConfiguration _configuration;
    private readonly IDockerCommandRunner _commandRunner;

    public DockerHcsContainerRuntime(
        HcsContainerRuntimeConfiguration configuration,
        IDockerCommandRunner commandRunner)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public async Task<HcsContainerProbeResult> ProbeAsync(
        HcsContainerProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            ValidateConfiguration(_configuration);
            var expected = ParseDigestReference(request.ImageReference);
            if (!string.Equals(expected, NormalizeDigest(request.ExpectedImageDigest), StringComparison.Ordinal) ||
                !_configuration.AllowedImageReferences.Contains(request.ImageReference, StringComparer.OrdinalIgnoreCase))
                return UnavailableProbe(SandboxReasonCodes.ImageNotDigestPinned,
                    "The configured sandbox image is not an approved immutable digest reference.");

            var context = await RequireSuccessAsync(
                ["context", "show"],
                _configuration.DockerControlCommandTimeout,
                "The Windows container engine context is unavailable.",
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(context.StandardOutput.Trim(), "default", StringComparison.Ordinal))
                return UnavailableProbe(SandboxReasonCodes.RuntimeUnavailable,
                    "The sandbox runtime refuses a user-selected container-engine context.");

            var version = await RequireSuccessAsync(
                ["version", "--format", "{{json .Server}}"],
                _configuration.DockerControlCommandTimeout,
                "The Windows container engine is unavailable.",
                cancellationToken).ConfigureAwait(false);
            using (var document = ParseDockerObject(version.StandardOutput, "container-engine version inspection"))
            {
                var root = document.RootElement;
                if (!TryGetString(root, "Os", out var operatingSystem) ||
                    !string.Equals(operatingSystem, "windows", StringComparison.OrdinalIgnoreCase) ||
                    !TryGetString(root, "Arch", out var architecture) ||
                    !(string.Equals(architecture, "amd64", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(architecture, "x86_64", StringComparison.OrdinalIgnoreCase)))
                    return UnavailableProbe(SandboxReasonCodes.WindowsX64Required,
                        "The production sandbox requires a Windows x64 container engine.");
            }

            await InspectPinnedImageAsync(request.ImageReference, request.ExpectedImageDigest, cancellationToken)
                .ConfigureAwait(false);
            return new HcsContainerProbeResult(
                true,
                SandboxCapabilityStates.Available,
                SandboxReasonCodes.Ready,
                "The HCS-managed Hyper-V sandbox runtime is available.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HcsContainerRuntimeException exception)
        {
            return UnavailableProbe(exception.ReasonCode, exception.Message);
        }
        catch
        {
            return UnavailableProbe(SandboxReasonCodes.RuntimeUnavailable,
                "The Windows container runtime capability could not be verified.");
        }
    }

    public async Task<HcsContainerRuntimeResult> ExecuteAsync(
        HcsContainerRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(_configuration);
        ValidateRequest(request);

        var containerName = BuildContainerName(request.ExecutionId);
        string? containerId = null;
        SandboxRuntimeInspection? inspection = null;
        var stages = new List<HcsContainerStageResult>();
        var status = SandboxExecutionStatus.Succeeded;
        var reasonCode = SandboxReasonCodes.Ready;
        var safeSummary = "The isolated Windows container execution completed.";
        var evidencePath = NormalizeOwnedEvidencePath(request.EvidenceOutputPath);
        cancellationToken.ThrowIfCancellationRequested();
        PrepareReplayEvidencePath(evidencePath, request);
        var copiedEvidence = false;
        var cleanedUp = false;
        var createAttempted = false;

        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalTimeout.CancelAfter(TimeSpan.FromSeconds(request.Resources.TotalExecutionTimeoutSeconds));

        try
        {
            await RemoveReplayContainerIfOwnedAsync(containerName, request, totalTimeout.Token).ConfigureAwait(false);
            var imageId = await InspectPinnedImageAsync(request, totalTimeout.Token).ConfigureAwait(false);
            createAttempted = true;
            containerId = await CreateContainerAsync(request, containerName, totalTimeout.Token).ConfigureAwait(false);
            inspection = await InspectAndVerifyContainerAsync(
                    containerId, imageId, request, expectedRunning: false, totalTimeout.Token)
                .ConfigureAwait(false);
            await StageSupervisorArtifactsAsync(containerId, request, totalTimeout.Token).ConfigureAwait(false);

            await RequireSuccessAsync(
                ["start", containerId],
                _configuration.DockerControlCommandTimeout,
                "The isolated Windows container could not be started.",
                totalTimeout.Token).ConfigureAwait(false);

            // Docker create reports the requested HostConfig, but the effective HCS/HNS
            // topology is not observable until the compute system has started. Reinspect
            // the running container before copying or executing any project-controlled
            // bytes so a missing or late-attached endpoint fails closed.
            inspection = await InspectAndVerifyContainerAsync(
                    containerId, imageId, request, expectedRunning: true, totalTimeout.Token)
                .ConfigureAwait(false);

            var supervisorProof = await RunSupervisorPreflightAsync(containerId, request, totalTimeout.Token)
                .ConfigureAwait(false);
            inspection = inspection with
            {
                MaximumUntrustedWorkloadProcessCount = supervisorProof.MaximumUntrustedWorkloadProcessCount,
                UntrustedWorkloadProcessScope = supervisorProof.UntrustedWorkloadProcessScope,
                TrustedSupervisorVersion = supervisorProof.TrustedSupervisorVersion,
                TrustedSupervisorSha256 = supervisorProof.TrustedSupervisorSha256,
                SuspendedAssignmentBeforeResumeProven = supervisorProof.SuspendedAssignmentBeforeResumeProven,
                UntrustedWorkloadProcessLimitProven = supervisorProof.UntrustedWorkloadProcessLimitProven,
                RestrictedLowIntegrityWorkloadIdentityProven = supervisorProof.RestrictedLowIntegrityWorkloadIdentityProven,
                SupervisorHandleIsolationProven = supervisorProof.SupervisorHandleIsolationProven,
                WorkloadScratchAndEvidenceBoundaryProven = supervisorProof.WorkloadScratchAndEvidenceBoundaryProven,
                BrokerLaunchDenialProven = supervisorProof.BrokerLaunchDenialProven,
                ProjectBytesCopiedAfterPreflightProven = supervisorProof.ProjectBytesCopiedAfterPreflightProven
            };
            foreach (var command in request.Commands.OrderBy(command => command.Stage))
            {
                var result = await ExecuteStageAsync(containerId, command, request, totalTimeout.Token)
                    .ConfigureAwait(false);
                stages.Add(result);
                if (result.TimedOut)
                {
                    status = SandboxExecutionStatus.TimedOut;
                    reasonCode = SandboxReasonCodes.ExecutionRejected;
                    safeSummary = $"The {command.Stage} stage exceeded its fixed timeout.";
                    break;
                }

                if (result.ExitCode != 0)
                {
                    status = SandboxExecutionStatus.Failed;
                    reasonCode = SandboxReasonCodes.ExecutionRejected;
                    safeSummary = $"The {command.Stage} stage failed inside the isolated Windows container.";
                    break;
                }
            }

            copiedEvidence = await CopyEvidenceOutAsync(
                    containerId, request.ExecutionId, request.PolicySha256, evidencePath, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            status = SandboxExecutionStatus.TimedOut;
            reasonCode = SandboxReasonCodes.ExecutionRejected;
            safeSummary = "The isolated Windows container exceeded the fixed total execution timeout.";
            if (containerId is not null)
            {
                copiedEvidence = await CopyEvidenceOutAsync(
                        containerId, request.ExecutionId, request.PolicySha256, evidencePath, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            status = SandboxExecutionStatus.Cancelled;
            reasonCode = SandboxReasonCodes.ExecutionRejected;
            safeSummary = "The isolated Windows container execution was cancelled.";
        }
        finally
        {
            if (containerId is not null)
            {
                if (inspection is not null && !copiedEvidence)
                {
                    copiedEvidence = await CopyEvidenceOutAsync(
                            containerId, request.ExecutionId, request.PolicySha256, evidencePath, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                cleanedUp = await RemoveIfOwnedAsync(containerId, request.ExecutionId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else if (createAttempted)
            {
                // docker create can reach the daemon before a client timeout or malformed
                // response. Recover only this deterministic attempt and only after all
                // immutable ownership labels match; a name collision is never deleted.
                cleanedUp = await RemoveDeterministicAttemptIfExactAsync(
                    containerName,
                    request,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (inspection is null)
            throw new HcsContainerRuntimeException(
                SandboxReasonCodes.IsolationInspectionFailed,
                "The isolated Windows container did not produce a verified runtime inspection.");
        if (!copiedEvidence)
            throw new HcsContainerRuntimeException(
                SandboxReasonCodes.ExecutionRejected,
                "Sandbox evidence could not be copied to the owned evidence directory.");

        if (!cleanedUp)
        {
            status = SandboxExecutionStatus.Failed;
            reasonCode = SandboxReasonCodes.CleanupFailed;
            safeSummary = "The sandbox ran, but its owned container could not be destroyed safely.";
        }

        return new HcsContainerRuntimeResult
        {
            Status = status,
            ReasonCode = reasonCode,
            SafeSummary = safeSummary,
            Inspection = inspection with { WasDestroyed = cleanedUp },
            Stages = stages,
            EvidencePath = evidencePath,
            CleanedUp = cleanedUp
        };
    }

    public Task<HcsCompletedEvidence?> TryReadCompletedEvidenceAsync(
        HcsCompletedEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRecoveryConfiguration(_configuration);
        if (request.ExecutionId == Guid.Empty)
            throw Rejected("An execution identifier is required for evidence recovery.");
        NormalizeDigest(request.PolicySha256);
        ValidateSupervisorAuthority(request.TrustedSupervisorVersion, request.TrustedSupervisorSha256);
        if (!CleanupSupervisorStagingArtifacts(
                request.ExecutionId,
                request.PolicySha256,
                request.TrustedSupervisorVersion,
                request.TrustedSupervisorSha256))
            throw IsolationFailure("The exact host supervisor staging residue is not safely owned.");
        var evidencePath = NormalizeOwnedEvidencePath(request.EvidenceOutputPath);
        if (!Directory.Exists(evidencePath))
            return Task.FromResult<HcsCompletedEvidence?>(null);
        if (!IsExactOwnedEvidenceDirectory(
                evidencePath, request.ExecutionId, request.PolicySha256, allowFinalManifest: true))
            throw IsolationFailure("Existing recovery evidence is not owned by the exact execution and policy.");
        var manifestPath = Path.Combine(evidencePath, "sandbox-evidence-manifest.json");
        if (!File.Exists(manifestPath))
            return Task.FromResult<HcsCompletedEvidence?>(null);
        var manifest = new FileInfo(manifestPath);
        if ((manifest.Attributes & FileAttributes.ReparsePoint) != 0 || manifest.Length is < 2 or > 2_097_152)
            throw IsolationFailure("The completed recovery manifest is unsafe or exceeds its fixed bound.");
        return Task.FromResult<HcsCompletedEvidence?>(new HcsCompletedEvidence(
            evidencePath,
            File.ReadAllText(manifestPath)));
    }

    public async Task<HcsExecutionCleanupResult> RecoverExecutionAsync(
        HcsExecutionCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRecoveryConfiguration(_configuration);
        if (request.ExecutionId == Guid.Empty)
            throw Rejected("An execution identifier is required for sandbox recovery.");
        NormalizeDigest(request.PolicySha256);
        ValidateSupervisorAuthority(request.TrustedSupervisorVersion, request.TrustedSupervisorSha256);
        var evidencePath = NormalizeOwnedEvidencePath(request.EvidenceOutputPath);
        var containerCleaned = await RemoveExactContainerIfOwnedAsync(
            BuildContainerName(request.ExecutionId),
            request.ExecutionId,
            request.PolicySha256,
            cancellationToken).ConfigureAwait(false);
        var stagingCleaned = CleanupSupervisorStagingArtifacts(
            request.ExecutionId,
            request.PolicySha256,
            request.TrustedSupervisorVersion,
            request.TrustedSupervisorSha256);
        var evidenceCleaned = stagingCleaned && CleanupIncompleteEvidencePaths(
            evidencePath,
            request.ExecutionId,
            request.PolicySha256);
        return new HcsExecutionCleanupResult(
            containerCleaned,
            evidenceCleaned,
            containerCleaned && evidenceCleaned
                ? "The exact stale sandbox resources were removed."
                : "One or more exact stale sandbox resources could not be safely removed.");
    }

    public async Task<HcsContainerRecoveryResult> RecoverOwnedContainersAsync(
        HcsContainerRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRecoveryConfiguration(_configuration);
        var excluded = request.ExcludedExecutionIds.ToHashSet();
        var candidates = await ListOwnedContainerIdsAsync(cancellationToken).ConfigureAwait(false);
        var removed = 0;

        foreach (var candidate in candidates)
        {
            var owned = await TryInspectOwnershipAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (owned is null || excluded.Contains(owned.ExecutionId))
                continue;
            if (request.CreatedBeforeUtc is not null && owned.CreatedAtUtc >= request.CreatedBeforeUtc.Value)
                continue;
            if (await RemoveIfOwnedAsync(candidate, owned.ExecutionId, cancellationToken).ConfigureAwait(false))
                removed++;
        }

        var remaining = (await ListOwnedContainerIdsAsync(cancellationToken).ConfigureAwait(false)).Count;
        return new HcsContainerRecoveryResult(
            candidates.Count,
            removed,
            remaining,
            remaining == 0,
            remaining == 0
                ? "Owned stale sandbox containers were recovered."
                : "One or more owned sandbox containers remain; unknown resources were not touched.");
    }

    private async Task RemoveReplayContainerIfOwnedAsync(
        string containerName,
        HcsContainerRuntimeRequest request,
        CancellationToken cancellationToken) =>
        _ = await RemoveExactContainerIfOwnedAsync(
            containerName,
            request.ExecutionId,
            request.PolicySha256,
            cancellationToken).ConfigureAwait(false);

    private async Task<bool> RemoveExactContainerIfOwnedAsync(
        string containerName,
        Guid executionId,
        string policySha256,
        CancellationToken cancellationToken)
    {
        var result = await RunDockerAsync(
            ["container", "inspect", "--format", "{{json .}}", containerName],
            _configuration.DockerControlCommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "The Windows container runtime did not answer the replay-safety inspection.");
        if (result.ExitCode != 0)
        {
            if (await ConfirmContainerAbsentAsync(containerName, cancellationToken).ConfigureAwait(false))
                return true;
            throw new HcsContainerRuntimeException(
                SandboxReasonCodes.RuntimeUnavailable,
                "The exact sandbox container could not be inspected or positively confirmed absent.");
        }

        using var document = ParseDockerObject(result.StandardOutput, "replay container inspection");
        var root = document.RootElement;
        var config = GetRequiredObject(root, "Config", "replay container inspection");
        var labels = GetRequiredObject(config, "Labels", "replay container inspection");
        var owned = TryGetString(labels, HcsContainerRuntimeConstants.OwnerLabel, out var owner) &&
                    string.Equals(owner, _configuration.OwnerLabelValue, StringComparison.Ordinal) &&
                    TryGetString(labels, HcsContainerRuntimeConstants.RuntimeLabel, out var runtime) &&
                    string.Equals(runtime, HcsContainerRuntimeConstants.RuntimeLabelValue, StringComparison.Ordinal) &&
                    TryGetString(labels, HcsContainerRuntimeConstants.ExecutionLabel, out var execution) &&
                    string.Equals(execution, executionId.ToString("D"), StringComparison.Ordinal) &&
                    TryGetString(labels, HcsContainerRuntimeConstants.PolicyLabel, out var policy) &&
                    string.Equals(policy, NormalizeDigest(policySha256), StringComparison.Ordinal);
        if (!owned)
            throw IsolationFailure("The deterministic sandbox name is occupied by an unknown or mismatched resource.");

        var id = GetRequiredString(root, "Id", "replay container inspection");
        var removed = await RunDockerAsync(
            ["rm", "--force", id],
            _configuration.DockerControlCommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (removed.TimedOut || removed.ExitCode != 0)
            throw new HcsContainerRuntimeException(SandboxReasonCodes.CleanupFailed,
                "An owned replay sandbox could not be removed safely.");
        var confirm = await RunDockerAsync(
            ["container", "inspect", "--format", "{{json .}}", id],
            _configuration.DockerControlCommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (confirm.TimedOut || confirm.ExitCode == 0 ||
            !await ConfirmContainerAbsentAsync(containerName, cancellationToken).ConfigureAwait(false))
            throw new HcsContainerRuntimeException(SandboxReasonCodes.CleanupFailed,
                "An owned replay sandbox still exists after removal.");
        return true;
    }

    private async Task<string> InspectPinnedImageAsync(
        HcsContainerRuntimeRequest request,
        CancellationToken cancellationToken) =>
        await InspectPinnedImageAsync(request.ImageReference, request.ExpectedImageDigest, cancellationToken)
            .ConfigureAwait(false);

    private async Task<string> InspectPinnedImageAsync(
        string imageReference,
        string expectedImageDigest,
        CancellationToken cancellationToken)
    {
        var result = await RequireSuccessAsync(
            ["image", "inspect", "--format", "{{json .}}", imageReference],
            _configuration.DockerControlCommandTimeout,
            "The digest-pinned sandbox image is unavailable locally; pulling is forbidden.",
            cancellationToken).ConfigureAwait(false);
        using var document = ParseDockerObject(result.StandardOutput, "image inspection");
        var root = document.RootElement;
        var id = GetRequiredString(root, "Id", "image inspection");
        var expectedDigest = NormalizeDigest(expectedImageDigest);
        var digestFound = TryGet(root, "RepoDigests", out var repoDigests) &&
            repoDigests.ValueKind == JsonValueKind.Array &&
            repoDigests.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String &&
                item.GetString()!.EndsWith("@sha256:" + expectedDigest, StringComparison.OrdinalIgnoreCase));
        if (!digestFound || !Sha256IdRegex().IsMatch(id))
            throw IsolationFailure("The local image does not match the approved immutable digest.");
        return id.ToLowerInvariant();
    }

    private async Task<string> CreateContainerAsync(
        HcsContainerRuntimeRequest request,
        string containerName,
        CancellationToken cancellationToken)
    {
        var resources = request.Resources;
        var source = NormalizeExistingDirectory(request.SourceSnapshotPath);
        var feed = NormalizeExistingDirectory(request.OfflineFeedPath);
        ValidateNoReparsePoints(source, "repository snapshot");
        ValidateNoReparsePoints(feed, "offline package feed");
        var nuGetConfig = NormalizeExistingFile(Path.Combine(feed, "NuGet.Config"));
        if ((File.GetAttributes(nuGetConfig) & FileAttributes.ReparsePoint) != 0)
            throw Rejected("The offline package-feed NuGet.Config cannot be a reparse point.");
        var arguments = new List<string>
        {
            "create",
            "--name", containerName,
            "--label", $"{HcsContainerRuntimeConstants.OwnerLabel}={_configuration.OwnerLabelValue}",
            "--label", $"{HcsContainerRuntimeConstants.ExecutionLabel}={request.ExecutionId:D}",
            "--label", $"{HcsContainerRuntimeConstants.PolicyLabel}={NormalizeDigest(request.PolicySha256)}",
            "--label", $"{HcsContainerRuntimeConstants.RuntimeLabel}={HcsContainerRuntimeConstants.RuntimeLabelValue}",
            "--pull=never",
            "--isolation=hyperv",
            "--network=none",
            "--restart=no",
            "--no-healthcheck",
            "--user", "ContainerAdministrator",
            "--cpu-count", resources.VirtualCpuCount.ToString(CultureInfo.InvariantCulture),
            "--memory", (resources.MemoryMaximumMiB * 1024L * 1024L).ToString(CultureInfo.InvariantCulture),
            "--storage-opt", $"size={resources.WritableScratchMaximumGiB * 1024L * 1024L * 1024L}",
            "--mount", BuildReadOnlyMount(source, HcsContainerRuntimeConstants.SourceContainerPath),
            "--mount", BuildReadOnlyMount(feed, HcsContainerRuntimeConstants.FeedContainerPath),
            "--mount", BuildReadOnlyMount(nuGetConfig, HcsContainerRuntimeConstants.NuGetConfigContainerPath),
            "--workdir", @"C:\Windows\System32",
            request.ImageReference,
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", EncodePowerShell(KeepAliveScript)
        };
        var result = await RequireSuccessAsync(
            arguments,
            _configuration.DockerControlCommandTimeout,
            "The HCS-managed Hyper-V container could not be created with the fixed policy.",
            cancellationToken).ConfigureAwait(false);
        var id = result.StandardOutput.Trim();
        if (!ContainerIdRegex().IsMatch(id))
            throw IsolationFailure("The container engine did not return a valid container identifier.");
        return id;
    }

    private async Task<SandboxRuntimeInspection> InspectAndVerifyContainerAsync(
        string containerId,
        string imageId,
        HcsContainerRuntimeRequest request,
        bool expectedRunning,
        CancellationToken cancellationToken)
    {
        var result = await RequireSuccessAsync(
            ["container", "inspect", "--format", "{{json .}}", containerId],
            _configuration.DockerControlCommandTimeout,
            "The container isolation policy could not be inspected.",
            cancellationToken).ConfigureAwait(false);
        using var document = ParseDockerObject(result.StandardOutput, "container inspection");
        var root = document.RootElement;
        VerifyIdentityAndLabels(root, containerId, request);
        var state = GetRequiredObject(root, "State", "container inspection");
        if (!TryGet(state, "Running", out var running) ||
            (running.ValueKind != JsonValueKind.True && running.ValueKind != JsonValueKind.False) ||
            running.GetBoolean() != expectedRunning)
            throw IsolationFailure(
                expectedRunning
                    ? "The active container was not confirmed running before supervisor preflight."
                    : "The container was not confirmed stopped before supervisor artifact staging.");

        var actualImageId = GetRequiredString(root, "Image", "container inspection");
        var containerConfig = GetRequiredObject(root, "Config", "container inspection");
        var hostConfig = GetRequiredObject(root, "HostConfig", "container inspection");
        if (!string.Equals(actualImageId, imageId, StringComparison.OrdinalIgnoreCase))
            throw IsolationFailure("The created container image identity drifted from the inspected digest-pinned image.");
        if (!string.Equals(GetRequiredString(hostConfig, "Isolation", "host configuration"), "hyperv", StringComparison.OrdinalIgnoreCase))
            throw IsolationFailure("The container engine did not apply Hyper-V isolation.");
        if (!string.Equals(GetRequiredString(hostConfig, "NetworkMode", "host configuration"), "none", StringComparison.OrdinalIgnoreCase))
            throw IsolationFailure("The container engine attached a network mode.");
        if (!string.Equals(
                GetRequiredString(containerConfig, "User", "container configuration"),
                "ContainerAdministrator",
                StringComparison.Ordinal))
            throw IsolationFailure("The trusted supervisor identity is not the fixed ContainerAdministrator account.");
        VerifyHealthcheckDisabled(containerConfig);

        var resources = request.Resources;
        RequireInt64(hostConfig, "CpuCount", resources.VirtualCpuCount, "virtual CPU count");
        RequireInt64(hostConfig, "Memory", resources.MemoryMaximumMiB * 1024L * 1024L, "memory maximum");
        VerifyScratchLimit(hostConfig, resources.WritableScratchMaximumGiB * 1024L * 1024L * 1024L);
        VerifyRestartDisabled(hostConfig);
        VerifyMounts(root, request);
        VerifyNoNetworkEndpoints(root);

        return new SandboxRuntimeInspection
        {
            RuntimeName = HcsContainerRuntimeConstants.RuntimeName,
            IsolationMode = SandboxIsolationModes.HcsHyperV,
            ActualContainerImageDigest = NormalizeDigest(request.ExpectedImageDigest),
            VirtualCpuCount = resources.VirtualCpuCount,
            MemoryMaximumMiB = resources.MemoryMaximumMiB,
            WritableScratchMaximumGiB = resources.WritableScratchMaximumGiB,
            MaximumUntrustedWorkloadProcessCount = resources.MaximumUntrustedWorkloadProcessCount,
            UntrustedWorkloadProcessScope = WindowsJobSupervisorContract.WorkloadProcessScope,
            TrustedSupervisorVersion = request.TrustedSupervisorVersion,
            TrustedSupervisorSha256 = NormalizeDigest(request.TrustedSupervisorSha256),
            SuspendedAssignmentBeforeResumeProven = false,
            UntrustedWorkloadProcessLimitProven = false,
            RestrictedLowIntegrityWorkloadIdentityProven = false,
            SupervisorHandleIsolationProven = false,
            WorkloadScratchAndEvidenceBoundaryProven = false,
            BrokerLaunchDenialProven = false,
            ProjectBytesCopiedAfterPreflightProven = false,
            NetworkEndpointCount = 0,
            HostWritableMountCount = 0,
            RepositoryInputReadOnly = true,
            OfflineFeedReadOnly = true,
            WasDestroyed = false,
            InspectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task StageSupervisorArtifactsAsync(
        string containerId,
        HcsContainerRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        var evidenceRoot = Path.GetFullPath(_configuration.EvidenceRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var stagingPath = Path.Combine(evidenceRoot, $".supervisor-{request.ExecutionId:N}");
        var ownerPath = stagingPath + WindowsJobSupervisorContract.HostStagingOwnerSuffix;
        if (!CleanupSupervisorStagingArtifacts(
                request.ExecutionId,
                request.PolicySha256,
                request.TrustedSupervisorVersion,
                request.TrustedSupervisorSha256))
            throw IsolationFailure("The exact host supervisor staging path is occupied by unknown material.");

        var staged = false;
        try
        {
            var owner = new SupervisorStagingOwner
            {
                ExecutionId = request.ExecutionId,
                PolicySha256 = NormalizeDigest(request.PolicySha256),
                TrustedSupervisorVersion = request.TrustedSupervisorVersion,
                TrustedSupervisorSha256 = NormalizeDigest(request.TrustedSupervisorSha256),
                LoaderSha256 = WindowsJobSupervisorContract.LoaderSha256,
                SourceSha256 = WindowsJobSupervisorContract.SourceSha256
            };
            WriteExactArtifactAtomically(
                ownerPath,
                new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(owner)));
            Directory.CreateDirectory(stagingPath);
            if ((File.GetAttributes(stagingPath) & FileAttributes.ReparsePoint) != 0)
                throw IsolationFailure("The host supervisor staging path is a reparse point.");
            WriteExactArtifactAtomically(
                Path.Combine(stagingPath, WindowsJobSupervisorContract.LoaderFileName),
                WindowsJobSupervisorContract.GetLoaderBytes());
            WriteExactArtifactAtomically(
                Path.Combine(stagingPath, WindowsJobSupervisorContract.SourceFileName),
                WindowsJobSupervisorContract.GetSourceBytes());

            var copy = await RunDockerAsync(
                ["cp", stagingPath, $"{containerId}:{WindowsJobSupervisorContract.ContainerDirectory}"],
                _configuration.ContainerInitializationTimeout,
                cancellationToken).ConfigureAwait(false);
            if (copy.TimedOut || copy.ExitCode != 0)
                throw IsolationFailure(
                    "The exact trusted workload supervisor artifacts could not be staged in the stopped container.");
            staged = true;
        }
        finally
        {
            if (!CleanupSupervisorStagingArtifacts(
                    request.ExecutionId,
                    request.PolicySha256,
                    request.TrustedSupervisorVersion,
                    request.TrustedSupervisorSha256))
                throw new HcsContainerRuntimeException(
                    SandboxReasonCodes.CleanupFailed,
                    "The exact host supervisor staging artifacts could not be removed safely.");
        }

        if (!staged)
            throw IsolationFailure("The trusted workload supervisor staging was not confirmed.");
    }

    private static void WriteExactArtifactAtomically(string path, byte[] bytes)
    {
        var pendingPath = path + ".pending";
        using var stream = new FileStream(
            pendingPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        stream.Dispose();
        File.Move(pendingPath, path);
    }

    private bool CleanupSupervisorStagingArtifacts(
        Guid executionId,
        string policySha256,
        string supervisorVersion,
        string supervisorSha256)
    {
        var root = Path.GetFullPath(_configuration.EvidenceRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var stagingPath = Path.Combine(root, $".supervisor-{executionId:N}");
        var ownerPath = stagingPath + WindowsJobSupervisorContract.HostStagingOwnerSuffix;
        var ownerPendingPath = ownerPath + ".pending";
        if (!Directory.Exists(stagingPath) && !File.Exists(ownerPath))
        {
            if (File.Exists(stagingPath))
                return false;
            if (!File.Exists(ownerPendingPath))
                return true;
            try
            {
                var pendingAttributes = File.GetAttributes(ownerPendingPath);
                var pendingLength = new FileInfo(ownerPendingPath).Length;
                if ((pendingAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                    pendingLength > 4096)
                    return false;
                File.Delete(ownerPendingPath);
                return !File.Exists(ownerPendingPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        try
        {
            if (File.Exists(stagingPath))
                return false;
            if (File.Exists(ownerPendingPath))
                return false;
            if (!File.Exists(ownerPath) ||
                (File.GetAttributes(ownerPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return false;
            var ownerInfo = new FileInfo(ownerPath);
            if (ownerInfo.Length is < 2 or > 4096)
                return false;
            var ownerJson = File.ReadAllText(ownerPath);
            var owner = JsonSerializer.Deserialize<SupervisorStagingOwner>(ownerJson);
            if (owner is null || owner.ExecutionId != executionId ||
                !string.Equals(owner.PolicySha256, NormalizeDigest(policySha256), StringComparison.Ordinal) ||
                !string.Equals(owner.TrustedSupervisorVersion, supervisorVersion, StringComparison.Ordinal) ||
                !string.Equals(owner.TrustedSupervisorSha256, NormalizeDigest(supervisorSha256), StringComparison.Ordinal) ||
                !string.Equals(owner.LoaderSha256, WindowsJobSupervisorContract.LoaderSha256, StringComparison.Ordinal) ||
                !string.Equals(owner.SourceSha256, WindowsJobSupervisorContract.SourceSha256, StringComparison.Ordinal) ||
                !string.Equals(ownerJson, JsonSerializer.Serialize(owner), StringComparison.Ordinal))
                return false;

            if (Directory.Exists(stagingPath))
            {
                if ((File.GetAttributes(stagingPath) & FileAttributes.ReparsePoint) != 0)
                    return false;
                var entries = Directory.EnumerateFileSystemEntries(stagingPath).ToArray();
                var allowed = new Dictionary<string, (string Hash, long Length, bool Pending)>(StringComparer.Ordinal)
                {
                    [WindowsJobSupervisorContract.LoaderFileName] = (
                        WindowsJobSupervisorContract.LoaderSha256,
                        WindowsJobSupervisorContract.GetLoaderBytes().LongLength,
                        false),
                    [WindowsJobSupervisorContract.LoaderFileName + ".pending"] = (
                        WindowsJobSupervisorContract.LoaderSha256,
                        WindowsJobSupervisorContract.GetLoaderBytes().LongLength,
                        true),
                    [WindowsJobSupervisorContract.SourceFileName] = (
                        WindowsJobSupervisorContract.SourceSha256,
                        WindowsJobSupervisorContract.GetSourceBytes().LongLength,
                        false),
                    [WindowsJobSupervisorContract.SourceFileName + ".pending"] = (
                        WindowsJobSupervisorContract.SourceSha256,
                        WindowsJobSupervisorContract.GetSourceBytes().LongLength,
                        true)
                };
                foreach (var entry in entries)
                {
                    var name = Path.GetFileName(entry);
                    var attributes = File.GetAttributes(entry);
                    if (!allowed.TryGetValue(name, out var expected) ||
                        (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                        return false;
                    var file = new FileInfo(entry);
                    if (expected.Pending)
                    {
                        if (file.Length > expected.Length)
                            return false;
                        continue;
                    }
                    if (file.Length != expected.Length)
                        return false;
                    using var stream = new FileStream(
                        entry, FileMode.Open, FileAccess.Read, FileShare.None, 16 * 1024, FileOptions.SequentialScan);
                    var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    if (!string.Equals(actualHash, expected.Hash, StringComparison.Ordinal))
                        return false;
                }
                foreach (var entry in entries)
                    File.Delete(entry);
                Directory.Delete(stagingPath, recursive: false);
            }
            File.Delete(ownerPath);
            return !Directory.Exists(stagingPath) &&
                   !File.Exists(stagingPath) &&
                   !File.Exists(ownerPath) &&
                   !File.Exists(ownerPendingPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private async Task<SupervisorPreflightProof> RunSupervisorPreflightAsync(
        string containerId,
        HcsContainerRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = BuildSupervisorExecArguments(containerId, request, "preflight", stagePayload: null);
        var result = await RunDockerAsync(
            arguments,
            _configuration.ContainerInitializationTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut || result.ExitCode != 0)
            throw IsolationFailure(
                "The restricted workload supervisor preflight did not complete before project bytes were copied.");

        var markerIndex = result.StandardOutput.LastIndexOf(
            WindowsJobSupervisorContract.ProofMarker,
            StringComparison.Ordinal);
        if (markerIndex < 0)
            throw IsolationFailure("The restricted workload supervisor omitted its preflight proof.");
        var encoded = result.StandardOutput[
                (markerIndex + WindowsJobSupervisorContract.ProofMarker.Length)..]
            .Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(encoded))
            throw IsolationFailure("The restricted workload supervisor preflight proof is empty.");

        SupervisorPreflightProof? proof;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            proof = JsonSerializer.Deserialize<SupervisorPreflightProof>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw IsolationFailure("The restricted workload supervisor preflight proof is malformed.", exception);
        }

        var resources = request.Resources;
        if (proof is null ||
            !string.Equals(proof.TrustedSupervisorVersion, request.TrustedSupervisorVersion, StringComparison.Ordinal) ||
            !string.Equals(
                NormalizeDigest(proof.TrustedSupervisorSha256),
                NormalizeDigest(request.TrustedSupervisorSha256),
                StringComparison.Ordinal) ||
            proof.MaximumUntrustedWorkloadProcessCount != resources.MaximumUntrustedWorkloadProcessCount ||
            !string.Equals(
                proof.UntrustedWorkloadProcessScope,
                WindowsJobSupervisorContract.WorkloadProcessScope,
                StringComparison.Ordinal) ||
            !proof.SuspendedAssignmentBeforeResumeProven ||
            !proof.UntrustedWorkloadProcessLimitProven ||
            !proof.RestrictedLowIntegrityWorkloadIdentityProven ||
            !proof.SupervisorHandleIsolationProven ||
            !proof.WorkloadScratchAndEvidenceBoundaryProven ||
            !proof.BrokerLaunchDenialProven ||
            !proof.ProjectBytesCopiedAfterPreflightProven)
            throw IsolationFailure("The restricted workload supervisor preflight proof drifted from policy.");
        return proof;
    }

    private async Task<HcsContainerStageResult> ExecuteStageAsync(
        string containerId,
        HcsContainerCommand command,
        HcsContainerRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            commandPath = command.CommandPath,
            arguments = command.Arguments,
            environment = request.Environment.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            stage = command.Stage.ToString().ToLowerInvariant(),
            timeoutMilliseconds = checked((int)command.Timeout.TotalMilliseconds)
        });
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var result = await RunDockerAsync(
            BuildSupervisorExecArguments(containerId, request, "stage", payloadBase64),
            command.Timeout + TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        var timedOut = result.TimedOut ||
            result.StandardError.Contains("IRONDEV_WORKLOAD_TIMEOUT", StringComparison.Ordinal);
        return new HcsContainerStageResult
        {
            Stage = command.Stage,
            CommandSha256 = NormalizeDigest(command.CommandSha256),
            ExitCode = result.ExitCode,
            TimedOut = timedOut,
            DurationMilliseconds = result.DurationMilliseconds,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            StandardOutputTruncated = result.StandardOutputTruncated,
            StandardErrorTruncated = result.StandardErrorTruncated
        };
    }

    private static IReadOnlyList<string> BuildSupervisorExecArguments(
        string containerId,
        HcsContainerRuntimeRequest request,
        string mode,
        string? stagePayload)
    {
        var arguments = new List<string>
        {
            "exec",
            "--user", "ContainerAdministrator",
            "--workdir", @"C:\Windows\System32",
            "--env", $"IRONDEV_SUPERVISOR_MODE={mode}",
            "--env", $"IRONDEV_SUPERVISOR_VERSION={request.TrustedSupervisorVersion}",
            "--env", $"IRONDEV_SUPERVISOR_SHA256={NormalizeDigest(request.TrustedSupervisorSha256)}",
            "--env", $"IRONDEV_BOOTSTRAP_SHA256={WindowsJobSupervisorContract.BootstrapSha256}",
            "--env", $"IRONDEV_SUPERVISOR_LOADER_SHA256={WindowsJobSupervisorContract.LoaderSha256}",
            "--env", $"IRONDEV_SUPERVISOR_SOURCE_SHA256={WindowsJobSupervisorContract.SourceSha256}",
            "--env", $"IRONDEV_WORKLOAD_PROCESS_LIMIT={request.Resources.MaximumUntrustedWorkloadProcessCount.ToString(CultureInfo.InvariantCulture)}"
        };
        if (stagePayload is not null)
        {
            arguments.Add("--env");
            arguments.Add($"IRONDEV_STAGE_PAYLOAD={stagePayload}");
        }
        arguments.Add(containerId);
        arguments.Add(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
        arguments.Add("-NoLogo");
        arguments.Add("-NoProfile");
        arguments.Add("-NonInteractive");
        arguments.Add("-EncodedCommand");
        arguments.Add(EncodePowerShell(WindowsJobSupervisorContract.BootstrapScript));
        var commandLength = arguments.Sum(argument => argument.Length + 3);
        if (commandLength > 24_000)
            throw Rejected("The fixed supervisor invocation exceeds its Windows command-line bound.");
        return arguments;
    }

    private async Task<bool> CopyEvidenceOutAsync(
        string containerId,
        Guid executionId,
        string policySha256,
        string evidencePath,
        CancellationToken cancellationToken)
    {
        string? stagingPath = null;
        try
        {
            if (Directory.Exists(evidencePath) || File.Exists(evidencePath))
                return false;
            var owned = await TryInspectOwnershipAsync(containerId, cancellationToken).ConfigureAwait(false);
            if (owned?.ExecutionId != executionId)
                return false;

            // A timed-out docker exec may leave its process alive. Stop/start terminates all
            // project processes while preserving the container layer, then run only the fixed
            // evidence sanitizer before stopping again for copy-out.
            if (!await StopOwnedContainerAsync(containerId, executionId, cancellationToken).ConfigureAwait(false))
                return false;
            var started = await RunDockerAsync(
                ["start", containerId],
                _configuration.DockerControlCommandTimeout,
                cancellationToken).ConfigureAwait(false);
            if (started.TimedOut || started.ExitCode != 0)
                return false;
            var sanitizer = await RunDockerAsync(
                ["exec", containerId,
                    @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                    "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                    EncodePowerShell(BuildEvidenceSanitizerScript())],
                _configuration.ContainerInitializationTimeout,
                cancellationToken).ConfigureAwait(false);
            if (sanitizer.TimedOut || sanitizer.ExitCode != 0)
                return false;
            if (!await StopOwnedContainerAsync(containerId, executionId, cancellationToken).ConfigureAwait(false))
                return false;

            var parent = Directory.GetParent(evidencePath);
            if (parent is null)
                return false;
            Directory.CreateDirectory(parent.FullName);
            stagingPath = evidencePath + ".partial-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stagingPath);
            WriteEvidenceOwnerMarker(stagingPath, executionId, policySha256);
            var result = await RunDockerAsync(
                ["cp", $"{containerId}:C:\\IronDev\\SanitizedEvidence\\.", stagingPath],
                _configuration.DockerControlCommandTimeout,
                cancellationToken).ConfigureAwait(false);
            if (result.TimedOut || result.ExitCode != 0)
                return false;
            if (!ValidateCopiedEvidence(stagingPath, executionId, policySha256))
                return false;
            Directory.Move(stagingPath, evidencePath);
            stagingPath = null;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (stagingPath is not null && Directory.Exists(stagingPath) &&
                IsOwnedPartialEvidencePath(stagingPath, evidencePath) &&
                IsExactOwnedEvidenceDirectory(
                    stagingPath, executionId, policySha256, allowFinalManifest: false))
            {
                try
                {
                    DeleteValidatedEvidenceDirectory(stagingPath);
                }
                catch
                {
                    // A failed bounded copy remains fail-closed; startup evidence cleanup may retry.
                }
            }
        }
    }

    private string BuildEvidenceSanitizerScript()
    {
        var maximumBytes = Math.Min(
            checked(_configuration.MaximumCapturedOutputCharacters * 4L),
            MaximumEvidenceLogCopyBytes);
        return $$"""
            $ErrorActionPreference = 'Stop'
            $sourceRoot = '{{HcsContainerRuntimeConstants.EvidenceContainerPath}}'
            $targetRoot = 'C:\IronDev\SanitizedEvidence'
            if (Test-Path -LiteralPath $targetRoot) { Remove-Item -LiteralPath $targetRoot -Recurse -Force }
            New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
            $allowed = @(
                'restore.stdout.log', 'restore.stderr.log',
                'build.stdout.log', 'build.stderr.log',
                'test.stdout.log', 'test.stderr.log'
            )
            foreach ($name in $allowed) {
                $source = Join-Path $sourceRoot $name
                if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
                $item = Get-Item -LiteralPath $source -Force
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Evidence reparse points are forbidden.' }
                $target = Join-Path $targetRoot $name
                $input = [IO.File]::Open($source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                try {
                    $output = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try {
                        $remaining = [int64]{{maximumBytes}}
                        $buffer = New-Object byte[] 65536
                        while ($remaining -gt 0) {
                            $read = $input.Read($buffer, 0, [Math]::Min($buffer.Length, [int]$remaining))
                            if ($read -le 0) { break }
                            $output.Write($buffer, 0, $read)
                            $remaining -= $read
                        }
                    }
                    finally { $output.Dispose() }
                }
                finally { $input.Dispose() }
            }
            """;
    }

    private bool ValidateCopiedEvidence(
        string stagingPath,
        Guid executionId,
        string policySha256)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "restore.stdout.log", "restore.stderr.log",
            "build.stdout.log", "build.stderr.log",
            "test.stdout.log", "test.stderr.log",
            HcsContainerRuntimeConstants.EvidenceOwnerMarkerFileName
        };
        var maximumFileBytes = Math.Min(
            checked(_configuration.MaximumCapturedOutputCharacters * 4L),
            MaximumEvidenceLogCopyBytes);
        var maximumTotalBytes = checked(maximumFileBytes * 6 + 4_096L);
        long total = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(stagingPath, "*", SearchOption.TopDirectoryOnly))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                !string.Equals(Path.GetDirectoryName(entry), stagingPath, StringComparison.OrdinalIgnoreCase) ||
                !allowed.Contains(Path.GetFileName(entry)))
                return false;
            var length = new FileInfo(entry).Length;
            var maximum = string.Equals(
                Path.GetFileName(entry),
                HcsContainerRuntimeConstants.EvidenceOwnerMarkerFileName,
                StringComparison.OrdinalIgnoreCase)
                ? 4_096L
                : maximumFileBytes;
            if (length > maximum || (total += length) > maximumTotalBytes)
                return false;
        }
        return HasExactEvidenceOwnerMarker(stagingPath, executionId, policySha256);
    }

    private void WriteEvidenceOwnerMarker(string stagingPath, Guid executionId, string policySha256)
    {
        var markerPath = Path.Combine(stagingPath, HcsContainerRuntimeConstants.EvidenceOwnerMarkerFileName);
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            executionId,
            policySha256 = NormalizeDigest(policySha256),
            owner = _configuration.OwnerLabelValue
        });
        PublishOwnerMarkerAtomically(markerPath, json);
    }

    private void PrepareReplayEvidencePath(string evidencePath, HcsContainerRuntimeRequest request)
    {
        var parent = Directory.GetParent(evidencePath)
            ?? throw Rejected("The owned evidence path has no parent directory.");
        if (!parent.Exists)
            return;
        var partialPrefix = Path.GetFileName(evidencePath) + ".partial-";
        var inspectedPartials = 0;
        foreach (var candidate in Directory.EnumerateDirectories(parent.FullName))
        {
            if (!Path.GetFileName(candidate).StartsWith(partialPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (++inspectedPartials > 1_000)
                throw Rejected("The owned evidence root contains too many partial replay candidates.");
            if (IsExactOwnedEvidenceDirectory(
                    candidate, request.ExecutionId, request.PolicySha256, allowFinalManifest: false))
                DeleteValidatedEvidenceDirectory(candidate);
        }

        if (File.Exists(evidencePath))
            throw Rejected("The evidence output path is occupied by an unknown file.");
        if (!Directory.Exists(evidencePath))
            return;
        if (!IsExactOwnedEvidenceDirectory(
                evidencePath, request.ExecutionId, request.PolicySha256, allowFinalManifest: true))
            throw Rejected("Existing evidence is not owned by this exact execution and policy replay.");
        DeleteValidatedEvidenceDirectory(evidencePath);
    }

    private bool IsExactOwnedEvidenceDirectory(
        string path,
        Guid executionId,
        string policySha256,
        bool allowFinalManifest)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
            var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "restore.stdout.log", "restore.stderr.log",
                "build.stdout.log", "build.stderr.log",
                "test.stdout.log", "test.stderr.log",
                HcsContainerRuntimeConstants.EvidenceOwnerMarkerFileName,
                HcsContainerRuntimeConstants.EvidenceOwnerMarkerFileName + ".pending",
                "sandbox-evidence-manifest.json.pending"
            };
            if (allowFinalManifest)
                allowedNames.Add("sandbox-evidence-manifest.json");

            var markerPath = ExistingOwnerMarkerPath(path);
            var marker = new FileInfo(markerPath);
            if (!marker.Exists || marker.Length is < 2 or > 4_096 ||
                (marker.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !HasExactEvidenceOwnerMarker(path, executionId, policySha256))
                return false;

            var maximumLogBytes = Math.Min(
                checked(_configuration.MaximumCapturedOutputCharacters * 4L),
                MaximumEvidenceLogCopyBytes);
            var maximumTotalBytes = checked(maximumLogBytes * 6 + 2_101_248L);
            long totalBytes = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                var name = Path.GetFileName(entry);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                    !allowedNames.Contains(name))
                    return false;
                var length = new FileInfo(entry).Length;
                var maximum = name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                    ? maximumLogBytes
                    : name.Equals(HcsContainerRuntimeConstants.EvidenceOwnerMarkerFileName, StringComparison.OrdinalIgnoreCase)
                        ? 4_096L
                        : 2_097_152L;
                if (length > maximum || (totalBytes += length) > maximumTotalBytes)
                    return false;
            }

            var manifestPath = Path.Combine(path, "sandbox-evidence-manifest.json");
            if (File.Exists(manifestPath))
            {
                if (!allowFinalManifest)
                    return false;
                using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = manifest.RootElement;
                if (!TryGetString(root, "executionId", out var execution) ||
                    !Guid.TryParse(execution, out var manifestExecutionId) || manifestExecutionId != executionId ||
                    !TryGetString(root, "sandboxPolicySha256", out var policy) ||
                    !string.Equals(NormalizeDigest(policy), NormalizeDigest(policySha256), StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool HasExactEvidenceOwnerMarker(string path, Guid executionId, string policySha256)
    {
        try
        {
            var markerPath = ExistingOwnerMarkerPath(path);
            var marker = new FileInfo(markerPath);
            if (!marker.Exists || marker.Length is < 2 or > 4_096 ||
                (marker.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
            using var markerDocument = JsonDocument.Parse(File.ReadAllText(markerPath));
            var root = markerDocument.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 4 &&
                   TryGet(root, "schemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number &&
                   schema.TryGetInt32(out var schemaVersion) && schemaVersion == 1 &&
                   TryGetString(root, "executionId", out var execution) &&
                   Guid.TryParseExact(execution, "D", out var markerExecutionId) && markerExecutionId == executionId &&
                   TryGetString(root, "policySha256", out var policy) &&
                   string.Equals(NormalizeDigest(policy), NormalizeDigest(policySha256), StringComparison.Ordinal) &&
                   TryGetString(root, "owner", out var owner) &&
                   string.Equals(owner, _configuration.OwnerLabelValue, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string ExistingOwnerMarkerPath(string path)
    {
        var marker = Path.Combine(path, HcsContainerRuntimeConstants.EvidenceOwnerMarkerFileName);
        return File.Exists(marker) ? marker : marker + ".pending";
    }

    private static void PublishOwnerMarkerAtomically(string markerPath, string json)
    {
        var pending = markerPath + ".pending";
        if (File.Exists(markerPath) || File.Exists(pending))
            throw Rejected("The evidence owner-marker path is already occupied.");
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        using (var stream = new FileStream(
                   pending,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   4_096,
                   FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(pending, markerPath);
    }

    private bool CleanupIncompleteEvidencePaths(
        string evidencePath,
        Guid executionId,
        string policySha256)
    {
        try
        {
            var parent = Directory.GetParent(evidencePath);
            if (parent is null || !parent.Exists)
                return true;
            var confirmed = true;
            var partialPrefix = Path.GetFileName(evidencePath) + ".partial-";
            var inspected = 0;
            foreach (var candidate in Directory.EnumerateDirectories(parent.FullName))
            {
                if (!Path.GetFileName(candidate).StartsWith(partialPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (++inspected > 1_000)
                    return false;
                if (IsExactOwnedEvidenceDirectory(candidate, executionId, policySha256, allowFinalManifest: false))
                    DeleteValidatedEvidenceDirectory(candidate);
                else
                    confirmed = false;
            }

            if (!Directory.Exists(evidencePath))
                return confirmed;
            var manifestPath = Path.Combine(evidencePath, "sandbox-evidence-manifest.json");
            if (File.Exists(manifestPath))
                return false;
            if (!IsExactOwnedEvidenceDirectory(
                    evidencePath, executionId, policySha256, allowFinalManifest: false))
                return false;
            DeleteValidatedEvidenceDirectory(evidencePath);
            return confirmed && !Directory.Exists(evidencePath);
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteValidatedEvidenceDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw Rejected("Owned replay evidence changed during cleanup.");
        foreach (var file in directory.EnumerateFiles())
        {
            file.Refresh();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw Rejected("Owned replay evidence changed during cleanup.");
            file.Delete();
        }
        directory.Refresh();
        if (directory.EnumerateFileSystemInfos().Any())
            throw Rejected("Owned replay evidence changed during cleanup.");
        directory.Delete(recursive: false);
    }

    private static bool IsOwnedPartialEvidencePath(string stagingPath, string evidencePath)
    {
        var normalizedStaging = Path.GetFullPath(stagingPath);
        var prefix = Path.GetFullPath(evidencePath) + ".partial-";
        return normalizedStaging.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Path.GetDirectoryName(normalizedStaging), Path.GetDirectoryName(prefix), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> StopOwnedContainerAsync(
        string containerId,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        var owned = await TryInspectOwnershipAsync(containerId, cancellationToken).ConfigureAwait(false);
        if (owned?.ExecutionId != executionId)
            return false;
        var stopped = await RunDockerAsync(
            ["stop", "--time", "0", containerId],
            _configuration.DockerControlCommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (stopped.TimedOut || stopped.ExitCode != 0)
            return false;

        var inspection = await RunDockerAsync(
            ["container", "inspect", "--format", "{{json .State}}", containerId],
            _configuration.DockerControlCommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (inspection.TimedOut || inspection.ExitCode != 0)
            return false;
        using var document = ParseDockerObject(inspection.StandardOutput, "stopped container inspection");
        return TryGet(document.RootElement, "Running", out var running) &&
               running.ValueKind == JsonValueKind.False;
    }

    private async Task<bool> RemoveIfOwnedAsync(
        string containerId,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var owned = await TryInspectOwnershipAsync(containerId, cancellationToken).ConfigureAwait(false);
            if (owned?.ExecutionId != executionId)
                return false;
            var result = await RunDockerAsync(
                ["rm", "--force", containerId],
                _configuration.DockerControlCommandTimeout,
                cancellationToken).ConfigureAwait(false);
            if (result.TimedOut || result.ExitCode != 0)
                return false;
            return await ConfirmContainerAbsentAsync(containerId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> RemoveDeterministicAttemptIfExactAsync(
        string containerName,
        HcsContainerRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var inspection = await RunDockerAsync(
                    ["container", "inspect", "--format", "{{json .}}", containerName],
                    _configuration.DockerControlCommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (inspection.TimedOut)
                    return false;
                if (inspection.ExitCode != 0)
                {
                    if (attempt < 2)
                        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using var document = ParseDockerObject(inspection.StandardOutput, "orphan cleanup inspection");
                var root = document.RootElement;
                var config = GetRequiredObject(root, "Config", "orphan cleanup inspection");
                var labels = GetRequiredObject(config, "Labels", "orphan cleanup inspection");
                var exact = TryGetString(labels, HcsContainerRuntimeConstants.OwnerLabel, out var owner) &&
                            string.Equals(owner, _configuration.OwnerLabelValue, StringComparison.Ordinal) &&
                            TryGetString(labels, HcsContainerRuntimeConstants.RuntimeLabel, out var runtime) &&
                            string.Equals(runtime, HcsContainerRuntimeConstants.RuntimeLabelValue, StringComparison.Ordinal) &&
                            TryGetString(labels, HcsContainerRuntimeConstants.ExecutionLabel, out var execution) &&
                            string.Equals(execution, request.ExecutionId.ToString("D"), StringComparison.Ordinal) &&
                            TryGetString(labels, HcsContainerRuntimeConstants.PolicyLabel, out var policy) &&
                            string.Equals(policy, NormalizeDigest(request.PolicySha256), StringComparison.Ordinal);
                if (!exact)
                    return false;

                var id = GetRequiredString(root, "Id", "orphan cleanup inspection");
                var removed = await RunDockerAsync(
                    ["rm", "--force", id],
                    _configuration.DockerControlCommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                return !removed.TimedOut && removed.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        // A failed inspect is not evidence of absence: the daemon or authorization may
        // have failed after accepting docker create. Require a separate successful list
        // query that positively excludes the deterministic name.
        try
        {
            return await ConfirmContainerAbsentAsync(containerName, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ConfirmContainerAbsentAsync(
        string containerNameOrId,
        CancellationToken cancellationToken)
    {
        var result = await RunDockerAsync(
            ["ps", "--all", "--no-trunc", "--filter", $"id={containerNameOrId}", "--format", "{{.ID}} {{.Names}}"],
            _configuration.DockerControlCommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut || result.ExitCode != 0 || result.StandardOutputTruncated ||
            result.StandardErrorTruncated)
            return false;

        var rows = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (containerNameOrId.StartsWith("irondev-", StringComparison.OrdinalIgnoreCase))
        {
            // Docker's id filter does not accept names. A successful exact-name query is
            // needed before absence of a deterministic container name can be claimed.
            result = await RunDockerAsync(
                ["ps", "--all", "--no-trunc", "--filter", $"name=^/{containerNameOrId}$", "--format", "{{.ID}} {{.Names}}"],
                _configuration.DockerControlCommandTimeout,
                cancellationToken).ConfigureAwait(false);
            if (result.TimedOut || result.ExitCode != 0 || result.StandardOutputTruncated ||
                result.StandardErrorTruncated)
                return false;
            rows = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return !rows.Any(row =>
            {
                var separator = row.IndexOf(' ');
                return separator >= 0 && string.Equals(
                    row[(separator + 1)..].Trim(),
                    containerNameOrId,
                    StringComparison.OrdinalIgnoreCase);
            });
        }

        return !rows.Any(row =>
        {
            var separator = row.IndexOf(' ');
            var id = separator < 0 ? row : row[..separator];
            return id.StartsWith(containerNameOrId, StringComparison.OrdinalIgnoreCase) ||
                   containerNameOrId.StartsWith(id, StringComparison.OrdinalIgnoreCase);
        });
    }

    private async Task<OwnedContainer?> TryInspectOwnershipAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        var result = await RunDockerAsync(
            ["container", "inspect", "--format", "{{json .}}", containerId],
            _configuration.DockerControlCommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut || result.ExitCode != 0)
            return null;
        using var document = ParseDockerObject(result.StandardOutput, "container ownership inspection");
        var root = document.RootElement;
        var config = GetRequiredObject(root, "Config", "container ownership inspection");
        var labels = GetRequiredObject(config, "Labels", "container ownership inspection");
        if (!TryGetString(labels, HcsContainerRuntimeConstants.OwnerLabel, out var owner) ||
            !string.Equals(owner, _configuration.OwnerLabelValue, StringComparison.Ordinal) ||
            !TryGetString(labels, HcsContainerRuntimeConstants.RuntimeLabel, out var runtime) ||
            !string.Equals(runtime, HcsContainerRuntimeConstants.RuntimeLabelValue, StringComparison.Ordinal) ||
            !TryGetString(labels, HcsContainerRuntimeConstants.ExecutionLabel, out var execution) ||
            !Guid.TryParseExact(execution, "D", out var executionId))
            return null;
        var createdAt = TryGetString(root, "Created", out var created) &&
                        DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
        return new OwnedContainer(executionId, createdAt);
    }

    private async Task<IReadOnlyList<string>> ListOwnedContainerIdsAsync(CancellationToken cancellationToken)
    {
        var result = await RequireSuccessAsync(
            ["ps", "--all",
                "--filter", $"label={HcsContainerRuntimeConstants.OwnerLabel}={_configuration.OwnerLabelValue}",
                "--filter", $"label={HcsContainerRuntimeConstants.RuntimeLabel}={HcsContainerRuntimeConstants.RuntimeLabelValue}",
                "--format", "{{.ID}}"],
            _configuration.DockerControlCommandTimeout,
            "Owned sandbox containers could not be enumerated.",
            cancellationToken).ConfigureAwait(false);
        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => ContainerIdRegex().IsMatch(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<DockerCommandResult> RequireSuccessAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string safeFailure,
        CancellationToken cancellationToken)
    {
        var result = await RunDockerAsync(arguments, timeout, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut || result.ExitCode != 0)
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable, safeFailure);
        return result;
    }

    private Task<DockerCommandResult> RunDockerAsync(
        IReadOnlyList<string> commandArguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "--host", _configuration.DockerEngineHost,
            "--config", Path.GetFullPath(_configuration.DockerConfigDirectory)
        };
        arguments.AddRange(commandArguments);
        return _commandRunner.RunAsync(new DockerCommandRequest
        {
            ExecutablePath = Path.GetFullPath(_configuration.DockerExecutablePath),
            Arguments = arguments,
            WorkingDirectory = Path.GetFullPath(_configuration.DockerConfigDirectory),
            Environment = _configuration.DockerHostEnvironment,
            Timeout = timeout,
            MaximumOutputCharacters = _configuration.MaximumCapturedOutputCharacters
        }, cancellationToken);
    }

    private void VerifyIdentityAndLabels(JsonElement root, string containerId, HcsContainerRuntimeRequest request)
    {
        var actualId = GetRequiredString(root, "Id", "container inspection");
        if (!actualId.StartsWith(containerId, StringComparison.OrdinalIgnoreCase) &&
            !containerId.StartsWith(actualId, StringComparison.OrdinalIgnoreCase))
            throw IsolationFailure("The inspected container identity does not match the created container.");
        var config = GetRequiredObject(root, "Config", "container inspection");
        var labels = GetRequiredObject(config, "Labels", "container inspection");
        RequireLabel(labels, HcsContainerRuntimeConstants.OwnerLabel, _configuration.OwnerLabelValue);
        RequireLabel(labels, HcsContainerRuntimeConstants.ExecutionLabel, request.ExecutionId.ToString("D"));
        RequireLabel(labels, HcsContainerRuntimeConstants.PolicyLabel, NormalizeDigest(request.PolicySha256));
        RequireLabel(labels, HcsContainerRuntimeConstants.RuntimeLabel, HcsContainerRuntimeConstants.RuntimeLabelValue);
    }

    private static void VerifyMounts(JsonElement root, HcsContainerRuntimeRequest request)
    {
        if (!TryGet(root, "Mounts", out var mounts) || mounts.ValueKind != JsonValueKind.Array)
            throw IsolationFailure("The container mount topology was not reported.");
        var actual = mounts.EnumerateArray().ToArray();
        if (actual.Length != 3)
            throw IsolationFailure("The container must have exactly three approved read-only host mounts.");
        VerifyMount(actual, NormalizeExistingDirectory(request.SourceSnapshotPath), HcsContainerRuntimeConstants.SourceContainerPath);
        VerifyMount(actual, NormalizeExistingDirectory(request.OfflineFeedPath), HcsContainerRuntimeConstants.FeedContainerPath);
        VerifyMount(actual, NormalizeExistingFile(Path.Combine(request.OfflineFeedPath, "NuGet.Config")), HcsContainerRuntimeConstants.NuGetConfigContainerPath);
    }

    private static void VerifyMount(JsonElement[] mounts, string source, string destination)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        var match = mounts.SingleOrDefault(mount =>
            TryGetString(mount, "Destination", out var actualDestination) &&
            string.Equals(NormalizeContainerPath(actualDestination), NormalizeContainerPath(destination), comparison));
        if (match.ValueKind == JsonValueKind.Undefined ||
            !TryGetString(match, "Type", out var type) || !string.Equals(type, "bind", comparison) ||
            !TryGetString(match, "Source", out var actualSource) ||
            !string.Equals(Path.GetFullPath(actualSource), source, comparison) ||
            !TryGet(match, "RW", out var writable) || writable.ValueKind != JsonValueKind.False)
            throw IsolationFailure("A required input mount is missing, writable, or points to an unapproved host path.");
    }

    private static void VerifyNoNetworkEndpoints(JsonElement root)
    {
        if (!TryGet(root, "NetworkSettings", out var settings) || settings.ValueKind != JsonValueKind.Object ||
            !TryGet(settings, "Networks", out var networks))
            throw IsolationFailure("The active container network topology was not reported.");
        if (networks.ValueKind != JsonValueKind.Object)
            throw IsolationFailure("The container network topology is invalid.");
        if (networks.EnumerateObject().Any())
            throw IsolationFailure("The active container has a network attachment despite the zero-endpoint policy.");
    }

    private static void VerifyScratchLimit(JsonElement hostConfig, long expectedBytes)
    {
        var storage = GetRequiredObject(hostConfig, "StorageOpt", "host configuration");
        if (!TryGetString(storage, "size", out var size) && !TryGetString(storage, "Size", out size))
            throw IsolationFailure("The writable scratch limit was not applied.");
        if (!long.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var actual) || actual != expectedBytes)
            throw IsolationFailure("The writable scratch limit drifted from the fixed policy.");
    }

    private static void VerifyRestartDisabled(JsonElement hostConfig)
    {
        var restart = GetRequiredObject(hostConfig, "RestartPolicy", "host configuration");
        if (!TryGetString(restart, "Name", out var name) ||
            !(string.Equals(name, "no", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(name)))
            throw IsolationFailure("Automatic container restart is forbidden.");
    }

    private static void VerifyHealthcheckDisabled(JsonElement containerConfig)
    {
        if (!TryGet(containerConfig, "Healthcheck", out var healthcheck) ||
            healthcheck.ValueKind != JsonValueKind.Object ||
            !TryGet(healthcheck, "Test", out var test) ||
            test.ValueKind != JsonValueKind.Array)
            throw IsolationFailure("The trusted supervisor container healthcheck override was not inspected.");
        var values = test.EnumerateArray().ToArray();
        if (values.Length != 1 || values[0].ValueKind != JsonValueKind.String ||
            !string.Equals(values[0].GetString(), "NONE", StringComparison.Ordinal))
            throw IsolationFailure("A container healthcheck could start a process outside the workload supervisor.");
    }

    private static void RequireInt64(JsonElement element, string property, long expected, string label)
    {
        if (!TryGet(element, property, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var actual) || actual != expected)
            throw IsolationFailure($"The inspected {label} drifted from the fixed policy.");
    }

    private static void RequireLabel(JsonElement labels, string name, string expected)
    {
        if (!TryGetString(labels, name, out var actual) || !string.Equals(actual, expected, StringComparison.Ordinal))
            throw IsolationFailure("The sandbox ownership or policy labels are missing or invalid.");
    }

    private static JsonDocument ParseDockerObject(string json, string label)
    {
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document;
            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() == 1)
            {
                var copy = JsonDocument.Parse(document.RootElement[0].GetRawText());
                document.Dispose();
                return copy;
            }
            document.Dispose();
        }
        catch (JsonException exception)
        {
            throw IsolationFailure($"The {label} returned malformed JSON.", exception);
        }
        throw IsolationFailure($"The {label} did not return one object.");
    }

    private static JsonElement GetRequiredObject(JsonElement element, string property, string label)
    {
        if (!TryGet(element, property, out var value) || value.ValueKind != JsonValueKind.Object)
            throw IsolationFailure($"The {label} omitted {property}.");
        return value;
    }

    private static string GetRequiredString(JsonElement element, string property, string label)
    {
        if (!TryGetString(element, property, out var value) || string.IsNullOrWhiteSpace(value))
            throw IsolationFailure($"The {label} omitted {property}.");
        return value;
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!TryGet(element, property, out var found) || found.ValueKind != JsonValueKind.String)
            return false;
        value = found.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGet(JsonElement element, string property, out JsonElement value)
    {
        foreach (var item in element.EnumerateObject())
        {
            if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static void ValidateConfiguration(HcsContainerRuntimeConfiguration configuration)
    {
        ValidateRecoveryConfiguration(configuration);
        if (configuration.AllowedSourceRoots.Count == 0 || configuration.AllowedOfflineFeedRoots.Count == 0 ||
            configuration.AllowedImageReferences.Count == 0)
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "Approved source roots, offline-feed roots, and digest-pinned images are required.");
    }

    private static void ValidateRecoveryConfiguration(HcsContainerRuntimeConfiguration configuration)
    {
        if (!Path.IsPathFullyQualified(configuration.DockerExecutablePath) ||
            !string.Equals(Path.GetFileName(configuration.DockerExecutablePath), "docker.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(configuration.DockerExecutablePath))
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "The Windows sandbox runtime must use an absolute trusted docker.exe path.");
        if (!string.Equals(
                configuration.DockerEngineHost,
                HcsContainerRuntimeConstants.DockerEngineHost,
                StringComparison.Ordinal))
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "The Windows sandbox runtime must use an explicit local named-pipe engine endpoint.");
        if (!Path.IsPathFullyQualified(configuration.DockerConfigDirectory) ||
            !Path.IsPathFullyQualified(configuration.EvidenceRootPath))
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "Docker client configuration and evidence roots must be absolute paths.");
        if (!Directory.Exists(configuration.DockerConfigDirectory) ||
            (File.GetAttributes(configuration.DockerConfigDirectory) & FileAttributes.ReparsePoint) != 0 ||
            !Directory.Exists(configuration.EvidenceRootPath) ||
            (File.GetAttributes(configuration.EvidenceRootPath) & FileAttributes.ReparsePoint) != 0)
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "Docker client configuration and evidence roots must be existing non-reparse directories.");
        if (File.Exists(configuration.DockerExecutablePath) &&
            (File.GetAttributes(configuration.DockerExecutablePath) & FileAttributes.ReparsePoint) != 0)
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "The trusted Docker executable cannot be a reparse point.");
        if (string.IsNullOrWhiteSpace(configuration.OwnerLabelValue) ||
            !SafeLabelRegex().IsMatch(configuration.OwnerLabelValue))
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "The sandbox owner label is invalid.");
        if (configuration.MaximumCapturedOutputCharacters is < 1 or > 16_777_216 ||
            configuration.DockerControlCommandTimeout <= TimeSpan.Zero ||
            configuration.ContainerInitializationTimeout <= TimeSpan.Zero)
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "The Docker client execution bounds are invalid.");
        if (configuration.DockerHostEnvironment.Keys.Any(name => !AllowedDockerHostEnvironmentNames.Contains(name)))
            throw new HcsContainerRuntimeException(SandboxReasonCodes.RuntimeUnavailable,
                "The Docker client host environment contains a non-approved variable.");
    }

    private void ValidateRequest(HcsContainerRuntimeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExecutionId == Guid.Empty)
            throw Rejected("A sandbox execution identifier is required.");
        var imageDigest = ParseDigestReference(request.ImageReference);
        if (!string.Equals(imageDigest, NormalizeDigest(request.ExpectedImageDigest), StringComparison.Ordinal) ||
            !_configuration.AllowedImageReferences.Contains(request.ImageReference, StringComparer.OrdinalIgnoreCase))
            throw Rejected("The requested sandbox image is not an approved immutable digest reference.");
        EnsurePathUnderAllowedRoot(request.SourceSnapshotPath, _configuration.AllowedSourceRoots, "repository snapshot");
        EnsurePathUnderAllowedRoot(request.OfflineFeedPath, _configuration.AllowedOfflineFeedRoots, "offline package feed");
        NormalizeOwnedEvidencePath(request.EvidenceOutputPath);
        ValidateFixedResources(request.Resources);
        ValidateEnvironment(request.Environment, request.EnvironmentAllowList);
        ValidateCommands(request.Commands, request.Resources);
        NormalizeDigest(request.PolicySha256);
        ValidateSupervisorAuthority(request.TrustedSupervisorVersion, request.TrustedSupervisorSha256);
    }

    private static void ValidateSupervisorAuthority(string version, string sha256)
    {
        if (!string.Equals(version, WindowsJobSupervisorContract.Version, StringComparison.Ordinal) ||
            !string.Equals(
                NormalizeDigest(sha256),
                WindowsJobSupervisorContract.Sha256,
                StringComparison.Ordinal))
            throw Rejected("The trusted workload supervisor authority does not match the fixed v0.1 artifact.");
    }

    private static void ValidateFixedResources(SandboxResourcePolicy resources)
    {
        if (resources != SandboxResourcePolicy.WorkbenchV01)
            throw Rejected("The requested resource policy does not equal the fixed Workbench v0.1 policy.");
    }

    private static void ValidateEnvironment(
        IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> allowList)
    {
        var allowed = allowList.ToHashSet(StringComparer.Ordinal);
        if (allowed.Count != allowList.Count || environment.Keys.Any(name => !allowed.Contains(name)))
            throw Rejected("The sandbox environment contains a variable outside the resolved allow-list.");
        foreach (var item in environment)
        {
            if (!EnvironmentNameRegex().IsMatch(item.Key) || ForbiddenEnvironmentNameRegex().IsMatch(item.Key) ||
                item.Value.Any(character => character is '\0' or '\r' or '\n'))
                throw Rejected("The sandbox environment contains a forbidden name or value.");
        }
    }

    private static void ValidateCommands(
        IReadOnlyList<HcsContainerCommand> commands,
        SandboxResourcePolicy resources)
    {
        if (commands.Count != 3 || commands.Select(command => command.Stage).Distinct().Count() != 3 ||
            !commands.Any(command => command.Stage == SandboxExecutionStage.Restore) ||
            !commands.Any(command => command.Stage == SandboxExecutionStage.Build) ||
            !commands.Any(command => command.Stage == SandboxExecutionStage.Test))
            throw Rejected("The sandbox requires exactly one Restore, Build, and Test command.");
        foreach (var command in commands)
        {
            if (!WindowsAbsolutePathRegex().IsMatch(command.CommandPath) || command.CommandPath.Contains("..", StringComparison.Ordinal) ||
                command.Arguments.Any(value => value.Any(character => character is '\0' or '\r' or '\n')))
                throw Rejected("A sandbox command path or argument is unsafe.");
            var expected = command.Stage switch
            {
                SandboxExecutionStage.Restore => resources.RestoreTimeoutSeconds,
                SandboxExecutionStage.Build => resources.BuildTimeoutSeconds,
                SandboxExecutionStage.Test => resources.TestTimeoutSeconds,
                _ => 0
            };
            if (command.Timeout != TimeSpan.FromSeconds(expected))
                throw Rejected("A sandbox stage timeout drifted from the fixed policy.");
            NormalizeDigest(command.CommandSha256);
        }
    }

    private void EnsurePathUnderAllowedRoot(string value, IReadOnlyList<string> allowedRoots, string label)
    {
        var path = NormalizeExistingDirectory(value);
        if (path.Contains(',', StringComparison.Ordinal))
            throw Rejected($"The {label} path contains a character unsupported by the fixed mount syntax.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var allowedRoot = allowedRoots
            .Select(NormalizeExistingDirectory)
            .FirstOrDefault(root => IsSameOrDescendant(path, root, comparison));
        if (allowedRoot is null)
            throw Rejected($"The {label} is outside approved host roots.");
        ValidateAncestorChainHasNoReparsePoint(path, allowedRoot, label, comparison);
    }

    private string NormalizeOwnedEvidencePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Rejected("An owned evidence output path is required.");
        var root = Path.GetFullPath(_configuration.EvidenceRootPath);
        var path = Path.GetFullPath(value);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!IsSameOrDescendant(path, root, comparison) || string.Equals(path, root, comparison))
            throw Rejected("The evidence output path is outside the owned evidence root.");
        for (var current = Directory.GetParent(path); current is not null; current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw Rejected("The evidence output path crosses a reparse point.");
            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar), comparison))
                break;
        }
        return path;
    }

    private static string NormalizeExistingDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw Rejected("A sandbox host path must be absolute.");
        var path = Path.GetFullPath(value);
        if (!Directory.Exists(path))
            throw Rejected("A required sandbox host directory is unavailable.");
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeExistingFile(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw Rejected("A sandbox host file path must be absolute.");
        var path = Path.GetFullPath(value);
        if (!File.Exists(path))
            throw Rejected("A required sandbox host file is unavailable.");
        return path;
    }

    private static void ValidateAncestorChainHasNoReparsePoint(
        string path,
        string allowedRoot,
        string label,
        StringComparison comparison)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw Rejected($"The {label} path crosses a reparse point and cannot be mounted.");
            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    allowedRoot.TrimEnd(Path.DirectorySeparatorChar), comparison))
                return;
        }
        throw Rejected($"The {label} path could not be anchored to its approved root.");
    }

    private static void ValidateNoReparsePoints(string root, string label)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var visited = 0;
        try
        {
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (current.Depth > MaximumValidatedInputDepth)
                    throw Rejected($"The {label} exceeds the fixed directory-depth bound.");
                foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path))
                {
                    if (++visited > MaximumValidatedInputEntries)
                        throw Rejected($"The {label} exceeds the fixed entry-count bound.");
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw Rejected($"The {label} contains a reparse point and cannot be mounted.");
                    if ((attributes & FileAttributes.Directory) != 0)
                        pending.Push((entry, current.Depth + 1));
                }
            }
        }
        catch (HcsContainerRuntimeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new HcsContainerRuntimeException(
                SandboxReasonCodes.UnsafeHostPath,
                $"The {label} could not be validated as a closed host-path tree.",
                exception);
        }
    }

    private static bool IsSameOrDescendant(string path, string root, StringComparison comparison) =>
        string.Equals(path, root, comparison) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);

    private static string ParseDigestReference(string imageReference)
    {
        var match = DigestReferenceRegex().Match(imageReference ?? string.Empty);
        if (!match.Success)
            throw Rejected("The sandbox image reference must be pinned by SHA-256 digest.");
        return match.Groups[1].Value.ToLowerInvariant();
    }

    private static string NormalizeDigest(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.StartsWith("sha256:", StringComparison.Ordinal))
            normalized = normalized[7..];
        if (!DigestRegex().IsMatch(normalized))
            throw Rejected("A required SHA-256 value is invalid.");
        return normalized;
    }

    private static string BuildReadOnlyMount(string source, string target) =>
        $"type=bind,source={source},target={target},readonly";

    private string BuildContainerName(Guid executionId)
    {
        var owner = NonNameRegex().Replace(_configuration.OwnerLabelValue.ToLowerInvariant(), "-").Trim('-');
        if (owner.Length > 20)
            owner = owner[..20];
        return $"irondev-{owner}-{executionId:N}";
    }

    private static string NormalizeContainerPath(string value) => value.TrimEnd('\\', '/');

    private static string EncodePowerShell(string script) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private const string KeepAliveScript = "while ($true) { Start-Sleep -Seconds 3600 }";

    private static HcsContainerRuntimeException IsolationFailure(string message, Exception? inner = null) =>
        new(SandboxReasonCodes.IsolationInspectionFailed, message, inner);

    private static HcsContainerRuntimeException Rejected(string message) =>
        new(SandboxReasonCodes.ExecutionRejected, message);

    private static HcsContainerProbeResult UnavailableProbe(string reasonCode, string safeSummary) =>
        new(false, SandboxCapabilityStates.Unavailable, reasonCode, safeSummary);

    private sealed record OwnedContainer(Guid ExecutionId, DateTimeOffset CreatedAtUtc);

    private sealed record SupervisorStagingOwner
    {
        public required Guid ExecutionId { get; init; }
        public required string PolicySha256 { get; init; }
        public required string TrustedSupervisorVersion { get; init; }
        public required string TrustedSupervisorSha256 { get; init; }
        public required string LoaderSha256 { get; init; }
        public required string SourceSha256 { get; init; }
    }

    private sealed record SupervisorPreflightProof
    {
        public required string TrustedSupervisorVersion { get; init; }
        public required string TrustedSupervisorSha256 { get; init; }
        public required int MaximumUntrustedWorkloadProcessCount { get; init; }
        public required string UntrustedWorkloadProcessScope { get; init; }
        public required bool SuspendedAssignmentBeforeResumeProven { get; init; }
        public required bool UntrustedWorkloadProcessLimitProven { get; init; }
        public required bool RestrictedLowIntegrityWorkloadIdentityProven { get; init; }
        public required bool SupervisorHandleIsolationProven { get; init; }
        public required bool WorkloadScratchAndEvidenceBoundaryProven { get; init; }
        public required bool BrokerLaunchDenialProven { get; init; }
        public required bool ProjectBytesCopiedAfterPreflightProven { get; init; }
    }

    [GeneratedRegex("^[a-fA-F0-9]{12,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContainerIdRegex();

    [GeneratedRegex("^sha256:[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256IdRegex();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestRegex();

    [GeneratedRegex("@sha256:([a-fA-F0-9]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestReferenceRegex();

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeLabelRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentNameRegex();

    [GeneratedRegex("(TOKEN|SECRET|PASSWORD|CREDENTIAL|AUTH|API_KEY|PRIVATE_KEY|PROXY|AWS|AZURE|GITHUB|GITLAB)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenEnvironmentNameRegex();

    [GeneratedRegex("^[A-Za-z]:\\\\", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsAbsolutePathRegex();

    [GeneratedRegex("[^a-z0-9_.-]", RegexOptions.CultureInvariant)]
    private static partial Regex NonNameRegex();
}
