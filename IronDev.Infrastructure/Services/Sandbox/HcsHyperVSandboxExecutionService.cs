using System.Security.Cryptography;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;

namespace IronDev.Infrastructure.Services.Sandbox;

/// <summary>
/// Adapts the immutable Workbench profile/policy into the HCS-backed runtime. Host
/// process execution is never a fallback: all profile commands run inside the inspected
/// Hyper-V-isolated Windows container.
/// </summary>
public sealed class HcsHyperVSandboxExecutionService : ISandboxExecutionService
{
    private const string DotNetContainerPath = @"C:\Program Files\dotnet\dotnet.exe";
    private const int MaximumEvidenceFiles = 32;
    private const long MaximumEvidenceFileBytes = 2 * 1024 * 1024;
    private const long MaximumEvidenceTotalBytes = 12 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> FixedEnvironment =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["NUGET_HTTP_CACHE_PATH"] = @"C:\IronDev\Scratch\NuGetHttpCache",
            ["NUGET_PACKAGES"] = @"C:\IronDev\Scratch\NuGetPackages",
            ["TEMP"] = @"C:\IronDev\Scratch\Temp",
            ["TMP"] = @"C:\IronDev\Scratch\Temp"
        };

    private readonly IHcsContainerRuntime _runtime;
    private readonly ISandboxRuntimePolicyCatalog _policies;
    private readonly IRepositorySetupProfileCatalog _profiles;

    public HcsHyperVSandboxExecutionService(
        IHcsContainerRuntime runtime,
        ISandboxRuntimePolicyCatalog policies,
        IRepositorySetupProfileCatalog profiles)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    public async Task<SandboxCapability> GetCapabilityAsync(CancellationToken cancellationToken = default)
    {
        var descriptor = _profiles.GetAll().SingleOrDefault(profile =>
            string.Equals(
                profile.ProfileDefinitionId,
                RepositorySetupProfileIds.GreenfieldWinFormsNet10MstestV1,
                StringComparison.Ordinal));
        if (descriptor is null)
            return Unavailable(SandboxReasonCodes.ProfileInvalid,
                "The pinned Workbench v0.1 execution profile is unavailable.");

        var resolution = _policies.Resolve(ToBinding(descriptor));
        if (!resolution.IsAvailable)
            return resolution.Capability;

        try
        {
            var probe = await _runtime.ProbeAsync(
                new HcsContainerProbeRequest(
                    resolution.Policy!.ContainerImageReference,
                    resolution.Policy.ContainerImageDigest),
                cancellationToken).ConfigureAwait(false);
            return probe.IsAvailable
                ? resolution.Capability
                : new SandboxCapability(
                    probe.CapabilityState,
                    probe.ReasonCode,
                    probe.SafeSummary,
                    resolution.Policy.PolicyVersion,
                    resolution.Policy.PolicySha256);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HcsContainerRuntimeException exception)
        {
            return new SandboxCapability(
                SandboxCapabilityStates.Unavailable,
                exception.ReasonCode,
                "The production Windows sandbox runtime is unavailable.",
                resolution.Policy!.PolicyVersion,
                resolution.Policy.PolicySha256);
        }
    }

    public async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var canonicalPolicy = ResolveAndValidatePolicy(request.Policy);
        var commands = BuildCommands(canonicalPolicy);
        var startedAt = DateTimeOffset.UtcNow;
        var runtimeResult = await _runtime.ExecuteAsync(
            new HcsContainerRuntimeRequest
            {
                ExecutionId = request.ExecutionId,
                SourceSnapshotPath = request.SourceSnapshotPath,
                OfflineFeedPath = canonicalPolicy.OfflineFeedPath,
                EvidenceOutputPath = request.EvidenceOutputPath,
                ImageReference = canonicalPolicy.ContainerImageReference,
                ExpectedImageDigest = canonicalPolicy.ContainerImageDigest,
                PolicySha256 = canonicalPolicy.PolicySha256,
                TrustedSupervisorVersion = canonicalPolicy.TrustedSupervisorVersion,
                TrustedSupervisorSha256 = canonicalPolicy.TrustedSupervisorSha256,
                Resources = canonicalPolicy.Resources,
                Commands = commands,
                Environment = FixedEnvironment,
                EnvironmentAllowList = canonicalPolicy.EnvironmentAllowList
            },
            cancellationToken).ConfigureAwait(false);
        var completedAt = DateTimeOffset.UtcNow;

        if (!runtimeResult.CleanedUp || !runtimeResult.Inspection.WasDestroyed)
            throw new HcsContainerRuntimeException(
                SandboxReasonCodes.CleanupFailed,
                "The sandbox result was withheld because teardown was not confirmed.");

        var artifacts = ReadEvidenceArtifacts(runtimeResult.EvidencePath);
        var stageEvidence = runtimeResult.Stages
            .OrderBy(stage => stage.Stage)
            .Select(stage => new SandboxStageEvidence
            {
                Stage = stage.Stage,
                CommandSha256 = stage.CommandSha256,
                ExitCode = stage.ExitCode,
                TimedOut = stage.TimedOut,
                DurationMilliseconds = stage.DurationMilliseconds,
                StandardOutputSha256 = SandboxCanonicalJson.Sha256(stage.StandardOutput),
                StandardErrorSha256 = SandboxCanonicalJson.Sha256(stage.StandardError),
                StandardOutputTruncated = stage.StandardOutputTruncated,
                StandardErrorTruncated = stage.StandardErrorTruncated
            })
            .ToArray();
        var manifest = new SandboxEvidenceManifest
        {
            SchemaVersion = 1,
            ExecutionId = request.ExecutionId,
            ProjectId = request.ProjectId,
            RepositoryBindingId = request.RepositoryBindingId,
            RepositoryBindingRevision = request.RepositoryBindingRevision,
            BaselineCommit = request.BaselineCommit,
            WorktreeFingerprint = request.WorktreeFingerprint,
            ProjectExecutionProfileId = request.ProjectExecutionProfileId,
            ProjectExecutionProfileRevision = request.ProjectExecutionProfileRevision,
            ProfileDefinitionId = canonicalPolicy.ProfileDefinitionId,
            ProfileDescriptorRevision = canonicalPolicy.ProfileDescriptorRevision,
            DescriptorSha256 = canonicalPolicy.DescriptorSha256,
            TemplateBundleSha256 = canonicalPolicy.TemplateBundleSha256,
            ToolchainManifestId = canonicalPolicy.ToolchainManifestId,
            ContainerImageDigest = canonicalPolicy.ContainerImageDigest,
            SandboxPolicyVersion = canonicalPolicy.PolicyVersion,
            SandboxPolicySha256 = canonicalPolicy.PolicySha256,
            TrustedSupervisorVersion = canonicalPolicy.TrustedSupervisorVersion,
            TrustedSupervisorSha256 = canonicalPolicy.TrustedSupervisorSha256,
            OfflineFeedManifestSha256 = canonicalPolicy.OfflineFeedManifestSha256,
            Status = runtimeResult.Status,
            ReasonCode = runtimeResult.ReasonCode,
            SafeSummary = runtimeResult.SafeSummary,
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt,
            Inspection = runtimeResult.Inspection,
            Stages = stageEvidence,
            Artifacts = artifacts
        };
        var manifestJson = SandboxEvidenceManifestCodec.SerializeCanonical(manifest);
        var manifestHash = SandboxCanonicalJson.Sha256(manifestJson);
        var manifestPath = Path.Combine(runtimeResult.EvidencePath, "sandbox-evidence-manifest.json");
        PublishAtomically(manifestPath, manifestJson);

        return new SandboxExecutionResult
        {
            ExecutionId = request.ExecutionId,
            Status = runtimeResult.Status,
            ReasonCode = runtimeResult.ReasonCode,
            SafeSummary = runtimeResult.SafeSummary,
            CleanedUp = true,
            EvidenceManifest = manifest,
            EvidenceManifestJson = manifestJson,
            EvidenceManifestSha256 = manifestHash
        };
    }

    public async Task<SandboxExecutionResult?> TryRecoverCompletedAsync(
        SandboxCompletedEvidenceRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRecoveryRequest(request);
        var recovered = await _runtime.TryReadCompletedEvidenceAsync(
            new HcsCompletedEvidenceRequest(
                request.ExecutionId,
                request.SandboxPolicySha256,
                request.TrustedSupervisorVersion,
                request.TrustedSupervisorSha256,
                request.EvidenceOutputPath),
            cancellationToken).ConfigureAwait(false);
        if (recovered is null)
            return null;

        var manifest = SandboxEvidenceManifestCodec.DeserializeCanonical(recovered.ManifestJson);
        ValidateRecoveredManifest(request, manifest, recovered.EvidencePath);
        return new SandboxExecutionResult
        {
            ExecutionId = manifest.ExecutionId,
            Status = manifest.Status,
            ReasonCode = manifest.ReasonCode,
            SafeSummary = manifest.SafeSummary,
            CleanedUp = true,
            EvidenceManifest = manifest,
            EvidenceManifestJson = recovered.ManifestJson,
            EvidenceManifestSha256 = SandboxCanonicalJson.Sha256(recovered.ManifestJson)
        };
    }

    public async Task<SandboxExecutionCleanupResult> RecoverExecutionAsync(
        SandboxExecutionCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var recovered = await _runtime.RecoverExecutionAsync(
            new HcsExecutionCleanupRequest(
                request.ExecutionId,
                request.SandboxPolicySha256,
                request.TrustedSupervisorVersion,
                request.TrustedSupervisorSha256,
                request.EvidenceOutputPath),
            cancellationToken).ConfigureAwait(false);
        return new SandboxExecutionCleanupResult(
            recovered.ContainerCleanupConfirmed,
            recovered.EvidenceCleanupConfirmed,
            recovered.SafeSummary);
    }

    public async Task<SandboxRecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var recovered = await _runtime.RecoverOwnedContainersAsync(
            new HcsContainerRecoveryRequest(),
            cancellationToken).ConfigureAwait(false);
        return new SandboxRecoveryResult(
            recovered.CandidatesFound,
            recovered.ContainersRemoved,
            recovered.ContainersRemaining,
            recovered.Succeeded,
            recovered.SafeSummary);
    }

    private SandboxRuntimePolicy ResolveAndValidatePolicy(SandboxRuntimePolicy supplied)
    {
        if (!string.Equals(
                supplied.PolicySha256,
                SandboxRuntimePolicyCodec.ComputeHash(supplied),
                StringComparison.Ordinal))
            throw new SandboxContractValidationException("The supplied sandbox policy failed its canonical hash check.");

        var descriptor = _profiles.Find(
            supplied.ProfileDefinitionId,
            supplied.ProfileDescriptorRevision,
            supplied.DescriptorSha256)
            ?? throw new SandboxContractValidationException(
                "The sandbox policy is not bound to a current immutable execution profile.");
        if (!string.Equals(descriptor.TemplateBundleSha256, supplied.TemplateBundleSha256, StringComparison.Ordinal) ||
            !string.Equals(descriptor.ToolchainManifestId, supplied.ToolchainManifestId, StringComparison.Ordinal))
            throw new SandboxContractValidationException(
                "The sandbox policy template or toolchain authority does not match the execution profile.");

        var resolution = _policies.Resolve(new SandboxExecutionProfileBinding
        {
            ProfileDefinitionId = descriptor.ProfileDefinitionId,
            ProfileDescriptorRevision = descriptor.Revision,
            DescriptorSha256 = descriptor.DescriptorSha256,
            TemplateBundleSha256 = descriptor.TemplateBundleSha256,
            ToolchainManifestId = descriptor.ToolchainManifestId,
            ExecutionImageReference = descriptor.ExecutionImageReference,
            RestoreCommand = supplied.Restore.CommandText,
            BuildCommand = supplied.Build.CommandText,
            TestCommand = supplied.Test.CommandText
        });
        if (!resolution.IsAvailable || resolution.Policy is null)
            throw new SandboxContractValidationException(
                "The production sandbox policy is no longer available in this environment.");
        if (!string.Equals(resolution.Policy.PolicySha256, supplied.PolicySha256, StringComparison.Ordinal))
            throw new SandboxContractValidationException(
                "The supplied sandbox policy is stale or differs from the current reviewed release policy.");
        return resolution.Policy;
    }

    private static IReadOnlyList<HcsContainerCommand> BuildCommands(SandboxRuntimePolicy policy) =>
    [
        BuildCommand(policy.Restore, SandboxExecutionStage.Restore),
        BuildCommand(policy.Build, SandboxExecutionStage.Build),
        BuildCommand(policy.Test, SandboxExecutionStage.Test)
    ];

    private static HcsContainerCommand BuildCommand(
        SandboxCommandPolicy command,
        SandboxExecutionStage expectedStage)
    {
        if (command.Stage != expectedStage ||
            !string.Equals(command.CommandSha256, SandboxCanonicalJson.Sha256(command.CommandText), StringComparison.Ordinal))
            throw new SandboxContractValidationException("A sandbox command failed its immutable hash check.");

        var tokens = TokenizeFixedCommand(command.CommandText);
        ValidateCommandShape(tokens, expectedStage);
        return new HcsContainerCommand
        {
            Stage = expectedStage,
            CommandPath = DotNetContainerPath,
            Arguments = tokens.Skip(1).ToArray(),
            CommandSha256 = command.CommandSha256,
            Timeout = TimeSpan.FromSeconds(command.TimeoutSeconds)
        };
    }

    private static IReadOnlyList<string> TokenizeFixedCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Any(character => character is '\0' or '\r' or '\n'))
            throw new SandboxContractValidationException("A fixed sandbox command is empty or contains control characters.");

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < command.Length; index++)
        {
            var character = command[index];
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (!quoted && char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(character);
        }
        if (quoted)
            throw new SandboxContractValidationException("A fixed sandbox command contains an unmatched quote.");
        if (current.Length > 0)
            tokens.Add(current.ToString());
        if (tokens.Count == 0 || tokens.Any(token => token.IndexOfAny(['&', '|', '<', '>', '^', '\r', '\n', '\0']) >= 0))
            throw new SandboxContractValidationException("A fixed sandbox command contains unsupported shell syntax.");
        return tokens;
    }

    private static void ValidateCommandShape(IReadOnlyList<string> tokens, SandboxExecutionStage stage)
    {
        if (tokens.Count == 0 ||
            !string.Equals(tokens[0], "dotnet", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(tokens[0], "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new SandboxContractValidationException("Only the pinned dotnet command is allowed in the production sandbox.");

        var valid = stage switch
        {
            SandboxExecutionStage.Restore =>
                tokens.Count == 6 && Eq(tokens[1], "restore") && SafeRelative(tokens[2]) &&
                Eq(tokens[3], "--configfile") && Eq(tokens[4], @"C:\IronDev\NuGet.Config") &&
                Eq(tokens[5], "--locked-mode"),
            SandboxExecutionStage.Build =>
                tokens.Count == 6 && Eq(tokens[1], "build") && SafeRelative(tokens[2]) &&
                Eq(tokens[3], "--configuration") && Eq(tokens[4], "Release") &&
                Eq(tokens[5], "--no-restore"),
            SandboxExecutionStage.Test =>
                tokens.Count == 7 && Eq(tokens[1], "test") && SafeRelative(tokens[2]) &&
                Eq(tokens[3], "--configuration") && Eq(tokens[4], "Release") &&
                Eq(tokens[5], "--no-restore") && Eq(tokens[6], "--no-build"),
            _ => false
        };
        if (!valid)
            throw new SandboxContractValidationException(
                $"The pinned {stage} command does not match the fixed Workbench v0.1 command shape.");
    }

    private static bool SafeRelative(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathFullyQualified(value) || value.Contains(':'))
            return false;
        var normalized = value.Replace('\\', '/');
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not ("." or "..") && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    private static IReadOnlyList<SandboxEvidenceArtifact> ReadEvidenceArtifacts(string evidencePath)
    {
        var root = Path.GetFullPath(evidencePath);
        if (!Directory.Exists(root))
            throw new SandboxContractValidationException("The runtime evidence directory is unavailable.");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(
                Path.GetFileName(file),
                "sandbox-evidence-manifest.json",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (files.Length > MaximumEvidenceFiles)
            throw new SandboxContractValidationException("The runtime returned too many evidence files.");

        var total = 0L;
        var result = new List<SandboxEvidenceArtifact>(files.Length);
        foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(file);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > MaximumEvidenceFileBytes ||
                (total += info.Length) > MaximumEvidenceTotalBytes)
                throw new SandboxContractValidationException("The runtime evidence exceeds its controlled copy-out bounds.");
            using var stream = File.OpenRead(file);
            result.Add(new SandboxEvidenceArtifact
            {
                Kind = Path.GetExtension(info.Name).Equals(".log", StringComparison.OrdinalIgnoreCase)
                    ? "CommandLog"
                    : "RuntimeEvidence",
                RelativePath = info.Name,
                LengthBytes = info.Length,
                ContentSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
            });
        }
        return result;
    }

    private static void ValidateRecoveryRequest(SandboxCompletedEvidenceRecoveryRequest request)
    {
        if (request.ExecutionId == Guid.Empty || request.ProjectId <= 0 ||
            request.RepositoryBindingId == Guid.Empty || request.RepositoryBindingRevision <= 0 ||
            request.ProjectExecutionProfileId == Guid.Empty || request.ProjectExecutionProfileRevision <= 0 ||
            request.ProfileDescriptorRevision <= 0 ||
            string.IsNullOrWhiteSpace(request.TrustedSupervisorVersion) ||
            request.TrustedSupervisorVersion.Length > 100 ||
            !string.Equals(
                request.TrustedSupervisorVersion,
                request.TrustedSupervisorVersion.Trim(),
                StringComparison.Ordinal) ||
            !IsLowerHex(request.BaselineCommit, 40) || !IsLowerHex(request.WorktreeFingerprint, 64) ||
            string.IsNullOrWhiteSpace(request.EvidenceOutputPath))
            throw new SandboxContractValidationException(
                "Completed evidence recovery requires exact execution, repository, profile, baseline, and evidence authority.");
        SandboxCanonicalJson.NormalizeSha256(request.DescriptorSha256, nameof(request.DescriptorSha256));
        SandboxCanonicalJson.NormalizeSha256(request.TemplateBundleSha256, nameof(request.TemplateBundleSha256));
        SandboxCanonicalJson.NormalizeSha256(request.SandboxPolicySha256, nameof(request.SandboxPolicySha256));
        SandboxCanonicalJson.NormalizeSha256(
            request.TrustedSupervisorSha256,
            nameof(request.TrustedSupervisorSha256));
        SandboxCanonicalJson.NormalizeSha256(
            request.OfflineFeedManifestSha256,
            nameof(request.OfflineFeedManifestSha256));
    }

    private static void ValidateRecoveredManifest(
        SandboxCompletedEvidenceRecoveryRequest request,
        SandboxEvidenceManifest manifest,
        string evidencePath)
    {
        var separator = request.ContainerImageReference.LastIndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        if (separator <= 0)
            throw new SandboxContractValidationException(
                "The recovered sandbox authority has no immutable image digest.");
        var imageDigest = SandboxCanonicalJson.NormalizeSha256(
            request.ContainerImageReference[(separator + "@sha256:".Length)..],
            nameof(request.ContainerImageReference));
        var resources = SandboxResourcePolicy.WorkbenchV01;
        var inspection = manifest.Inspection;
        if (manifest.SchemaVersion != 1 || manifest.ExecutionId != request.ExecutionId ||
            manifest.ProjectId != request.ProjectId ||
            manifest.RepositoryBindingId != request.RepositoryBindingId ||
            manifest.RepositoryBindingRevision != request.RepositoryBindingRevision ||
            !Eq(manifest.BaselineCommit, request.BaselineCommit) ||
            !Eq(manifest.WorktreeFingerprint, request.WorktreeFingerprint) ||
            manifest.ProjectExecutionProfileId != request.ProjectExecutionProfileId ||
            manifest.ProjectExecutionProfileRevision != request.ProjectExecutionProfileRevision ||
            !Eq(manifest.ProfileDefinitionId, request.ProfileDefinitionId) ||
            manifest.ProfileDescriptorRevision != request.ProfileDescriptorRevision ||
            !Eq(manifest.DescriptorSha256, request.DescriptorSha256) ||
            !Eq(manifest.TemplateBundleSha256, request.TemplateBundleSha256) ||
            !Eq(manifest.ToolchainManifestId, request.ToolchainManifestId) ||
            !Eq(manifest.ContainerImageDigest, imageDigest) ||
            !Eq(manifest.SandboxPolicyVersion, request.SandboxPolicyVersion) ||
            !Eq(manifest.SandboxPolicySha256, request.SandboxPolicySha256) ||
            !Eq(manifest.TrustedSupervisorVersion, request.TrustedSupervisorVersion) ||
            !Eq(manifest.TrustedSupervisorSha256, request.TrustedSupervisorSha256) ||
            !Eq(manifest.OfflineFeedManifestSha256, request.OfflineFeedManifestSha256) ||
            string.IsNullOrWhiteSpace(manifest.ReasonCode) || manifest.ReasonCode.Length > 100 ||
            string.IsNullOrWhiteSpace(manifest.SafeSummary) || manifest.SafeSummary.Length > 1_000 ||
            !Eq(inspection.RuntimeName, HcsContainerRuntimeConstants.RuntimeName) ||
            !Eq(inspection.IsolationMode, SandboxIsolationModes.HcsHyperV) ||
            !Eq(inspection.ActualContainerImageDigest, imageDigest) ||
            inspection.VirtualCpuCount != resources.VirtualCpuCount ||
            inspection.MemoryMaximumMiB != resources.MemoryMaximumMiB ||
            inspection.WritableScratchMaximumGiB != resources.WritableScratchMaximumGiB ||
            inspection.MaximumUntrustedWorkloadProcessCount != resources.MaximumUntrustedWorkloadProcessCount ||
            !Eq(inspection.UntrustedWorkloadProcessScope, WindowsJobSupervisorContract.WorkloadProcessScope) ||
            !Eq(inspection.TrustedSupervisorVersion, request.TrustedSupervisorVersion) ||
            !Eq(inspection.TrustedSupervisorSha256, request.TrustedSupervisorSha256) ||
            !inspection.SuspendedAssignmentBeforeResumeProven ||
            !inspection.UntrustedWorkloadProcessLimitProven ||
            !inspection.RestrictedLowIntegrityWorkloadIdentityProven ||
            !inspection.SupervisorHandleIsolationProven ||
            !inspection.WorkloadScratchAndEvidenceBoundaryProven ||
            !inspection.BrokerLaunchDenialProven ||
            !inspection.ProjectBytesCopiedAfterPreflightProven ||
            inspection.NetworkEndpointCount != 0 || inspection.HostWritableMountCount != 0 ||
            !inspection.RepositoryInputReadOnly || !inspection.OfflineFeedReadOnly || !inspection.WasDestroyed ||
            (manifest.Status == SandboxExecutionStatus.Succeeded &&
             !Eq(manifest.ReasonCode, SandboxReasonCodes.Ready)) ||
            (manifest.Status != SandboxExecutionStatus.Succeeded &&
             Eq(manifest.ReasonCode, SandboxReasonCodes.Ready)))
            throw new SandboxContractValidationException(
                "The completed sandbox evidence does not match its exact durable authority.");

        var actualArtifacts = ReadEvidenceArtifacts(evidencePath);
        var expectedArtifacts = manifest.Artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.Kind, StringComparer.Ordinal)
            .ToArray();
        var actualOrdered = actualArtifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.Kind, StringComparer.Ordinal)
            .ToArray();
        if (expectedArtifacts.Length != actualOrdered.Length ||
            expectedArtifacts.Where((artifact, index) => !ArtifactEquals(artifact, actualOrdered[index])).Any())
            throw new SandboxContractValidationException(
                "The completed sandbox evidence artifacts failed their immutable hash check.");
    }

    private static bool ArtifactEquals(SandboxEvidenceArtifact left, SandboxEvidenceArtifact right) =>
        Eq(left.Kind, right.Kind) && Eq(left.RelativePath, right.RelativePath) &&
        left.LengthBytes == right.LengthBytes && Eq(left.ContentSha256, right.ContentSha256);

    private static void PublishAtomically(string path, string content)
    {
        var pending = path + ".pending";
        if (File.Exists(path) || File.Exists(pending))
            throw new SandboxContractValidationException(
                "The completed sandbox evidence manifest path is already occupied.");
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(content);
        using (var stream = new FileStream(
                   pending,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   16 * 1024,
                   FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(pending, path);
    }

    private static SandboxExecutionProfileBinding ToBinding(RepositorySetupProfileDescriptor profile) => new()
    {
        ProfileDefinitionId = profile.ProfileDefinitionId,
        ProfileDescriptorRevision = profile.Revision,
        DescriptorSha256 = profile.DescriptorSha256,
        TemplateBundleSha256 = profile.TemplateBundleSha256,
        ToolchainManifestId = profile.ToolchainManifestId,
        ExecutionImageReference = profile.ExecutionImageReference,
        RestoreCommand = profile.RestoreCommandTemplate,
        BuildCommand = profile.BuildCommandTemplate,
        TestCommand = profile.TestCommandTemplate
    };

    private static void ValidateRequest(SandboxExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExecutionId == Guid.Empty || request.ProjectId <= 0 ||
            request.RepositoryBindingId == Guid.Empty || request.RepositoryBindingRevision <= 0 ||
            request.ProjectExecutionProfileId == Guid.Empty || request.ProjectExecutionProfileRevision <= 0 ||
            !IsLowerHex(request.BaselineCommit, 40) || !IsLowerHex(request.WorktreeFingerprint, 64) ||
            string.IsNullOrWhiteSpace(request.SourceSnapshotPath) ||
            string.IsNullOrWhiteSpace(request.EvidenceOutputPath))
            throw new SandboxContractValidationException(
                "Sandbox execution requires exact project, repository, profile, baseline, snapshot, and evidence authority.");
    }

    private static bool Eq(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsLowerHex(string value, int length) =>
        value?.Length == length && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static SandboxCapability Unavailable(string reasonCode, string message) => new(
        SandboxCapabilityStates.Unavailable,
        reasonCode,
        message,
        SandboxPolicyVersions.WorkbenchV01,
        PolicySha256: null);
}
