using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using IronDev.Core.Sandbox;
using IronDev.Infrastructure.Services.Sandbox;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("WorkbenchSandbox")]
public sealed class WorkbenchProductionSandboxContractTests
{
    private const string MigrationPath = "Database/migrate_workbench_sandbox_qualification.sql";

    [TestMethod]
    public void WorkbenchV01Policy_HasTheExactNonWidenableResourceEnvelope()
    {
        var policy = SandboxResourcePolicy.WorkbenchV01;

        Assert.AreEqual(2, policy.VirtualCpuCount);
        Assert.AreEqual(4096, policy.MemoryMaximumMiB);
        Assert.AreEqual(12, policy.WritableScratchMaximumGiB);
        Assert.AreEqual(64, policy.MaximumUntrustedWorkloadProcessCount);
        Assert.AreEqual(5 * 60, policy.RestoreTimeoutSeconds);
        Assert.AreEqual(5 * 60, policy.BuildTimeoutSeconds);
        Assert.AreEqual(10 * 60, policy.TestTimeoutSeconds);
        Assert.AreEqual(20 * 60, policy.TotalExecutionTimeoutSeconds);
        Assert.AreEqual(0, policy.NetworkEndpointCount);
        Assert.AreEqual(0, policy.HostWritableMountCount);
    }

    [TestMethod]
    public void SandboxContext_ReadsTheCanonicalExecutionReadinessColumn()
    {
        var service = Read(
            "IronDev.Infrastructure/Services/Sandbox/WorkbenchSandboxQualificationService.cs");

        StringAssert.Contains(service, "INNER JOIN dbo.vw_WorkbenchEffectiveProjectReadiness readiness");
        StringAssert.Contains(service, "readiness.ExecutionReadiness");
        Assert.IsFalse(service.Contains("value.Readiness", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("readiness.Readiness", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("FROM dbo.ProjectReadinessAssessments value", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EvidenceManifestHash_IsCanonicalAcrossCollectionOrderAndHashFormatting()
    {
        var first = ValidEvidenceManifest();
        var equivalent = first with
        {
            DescriptorSha256 = "SHA256:" + Hash('A'),
            TemplateBundleSha256 = Hash('B').ToUpperInvariant(),
            ContainerImageDigest = "sha256:" + Hash('C').ToUpperInvariant(),
            SandboxPolicySha256 = Hash('D').ToUpperInvariant(),
            OfflineFeedManifestSha256 = "SHA256:" + Hash('E'),
            Stages = first.Stages.Reverse().ToArray(),
            Artifacts = first.Artifacts.Reverse().ToArray(),
            Inspection = first.Inspection with
            {
                ActualContainerImageDigest = "SHA256:" + Hash('C').ToUpperInvariant()
            }
        };

        var firstJson = SandboxEvidenceManifestCodec.SerializeCanonical(first);
        var equivalentJson = SandboxEvidenceManifestCodec.SerializeCanonical(equivalent);

        Assert.AreEqual(firstJson, equivalentJson);
        Assert.AreEqual(
            SandboxEvidenceManifestCodec.ComputeHash(first),
            SandboxEvidenceManifestCodec.ComputeHash(equivalent));
        Assert.AreEqual(64, SandboxEvidenceManifestCodec.ComputeHash(first).Length);
    }

    [TestMethod]
    public void EvidenceManifestHash_ChangesWhenExactRepositoryProvenanceChanges()
    {
        var original = ValidEvidenceManifest();
        var changed = original with { RepositoryBindingRevision = original.RepositoryBindingRevision + 1 };

        Assert.AreNotEqual(
            SandboxEvidenceManifestCodec.ComputeHash(original),
            SandboxEvidenceManifestCodec.ComputeHash(changed));
    }

    [TestMethod]
    public void PolicyCatalog_FloatingImageTagFailsClosedBeforeExecutionAuthorityExists()
    {
        using var files = SandboxPolicyFiles.Create();
        var catalog = files.CreateCatalog(
            "mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2025");

        var result = catalog.Resolve(ValidProfile());

        Assert.IsFalse(result.IsAvailable);
        Assert.IsNull(result.Policy);
        Assert.AreEqual(SandboxCapabilityStates.Unavailable, result.Capability.State);
        Assert.AreEqual(SandboxReasonCodes.ImageNotDigestPinned, result.Capability.ReasonCode);
        Assert.IsNull(result.Capability.PolicySha256);
    }

    [TestMethod]
    public void PolicyCatalog_MissingOfflineFeedFailsClosedBeforeExecutionAuthorityExists()
    {
        using var files = SandboxPolicyFiles.Create();
        Directory.Delete(files.FeedPath, recursive: true);
        var catalog = files.CreateCatalog();

        var result = catalog.Resolve(ValidProfile());

        Assert.IsFalse(result.IsAvailable);
        Assert.IsNull(result.Policy);
        Assert.AreEqual(SandboxCapabilityStates.Unavailable, result.Capability.State);
        Assert.AreEqual(SandboxReasonCodes.OfflineFeedUnavailable, result.Capability.ReasonCode);
        Assert.IsNull(result.Capability.PolicySha256);
    }

    [TestMethod]
    public void PolicyCatalog_AvailablePolicyUsesOnlyTheFixedCredentialFreeEnvironmentAllowList()
    {
        using var files = SandboxPolicyFiles.Create();

        var result = files.CreateCatalog().Resolve(ValidProfile());

        Assert.IsTrue(result.IsAvailable);
        Assert.IsNotNull(result.Policy);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "DOTNET_CLI_TELEMETRY_OPTOUT",
                "DOTNET_NOLOGO",
                "NUGET_HTTP_CACHE_PATH",
                "NUGET_PACKAGES",
                "TEMP",
                "TMP"
            },
            result.Policy.EnvironmentAllowList.ToArray());
        Assert.IsFalse(result.Policy.EnvironmentAllowList.Any(name =>
            name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("KEY", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GIT", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PROXY", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AZURE", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AWS", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(SandboxIsolationModes.HcsHyperV, result.Policy.IsolationMode);
        Assert.IsTrue(result.Policy.RepositoryInputReadOnly);
        Assert.IsTrue(result.Policy.OfflineFeedReadOnly);
        Assert.AreEqual(SandboxRuntimePolicyCodec.ComputeHash(result.Policy), result.Policy.PolicySha256);
        Assert.AreEqual(WindowsJobSupervisorContract.Version, result.Policy.TrustedSupervisorVersion);
        Assert.AreEqual(WindowsJobSupervisorContract.Sha256, result.Policy.TrustedSupervisorSha256);
    }

    [TestMethod]
    public void PolicyHash_ChangesWhenEitherTrustedSupervisorIdentityFieldChanges()
    {
        using var files = SandboxPolicyFiles.Create();
        var policy = files.CreateCatalog().Resolve(ValidProfile()).Policy!;

        var versionChanged = policy with { TrustedSupervisorVersion = policy.TrustedSupervisorVersion + "-drift" };
        var hashChanged = policy with { TrustedSupervisorSha256 = Hash('9') };

        Assert.AreNotEqual(
            SandboxRuntimePolicyCodec.ComputeHash(policy),
            SandboxRuntimePolicyCodec.ComputeHash(versionChanged));
        Assert.AreNotEqual(
            SandboxRuntimePolicyCodec.ComputeHash(policy),
            SandboxRuntimePolicyCodec.ComputeHash(hashChanged));
    }

    [TestMethod]
    public void TrustedSupervisorSource_CompilesAsCSharp5AndArtifactBytesStayBounded()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            WindowsJobSupervisorContract.SupervisorSource,
            new CSharpParseOptions(LanguageVersion.CSharp5));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "IronDevSupervisorContract",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);

        Assert.IsTrue(
            emitted.Success,
            string.Join(Environment.NewLine, emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
        Assert.IsTrue(WindowsJobSupervisorContract.GetBootstrapBytes().Length < 12_000);
        Assert.IsTrue(WindowsJobSupervisorContract.GetLoaderBytes().Length < 65_536);
        Assert.IsTrue(WindowsJobSupervisorContract.GetSourceBytes().Length < 131_072);
        Assert.IsTrue(WindowsJobSupervisorContract.BrokerProbeEncodedCommandLength < 24_000);
        StringAssert.Contains(WindowsJobSupervisorContract.SupervisorSource, "CreateRestrictedToken");
        StringAssert.Contains(WindowsJobSupervisorContract.SupervisorSource, "CreateProcessAsUserW");
        StringAssert.Contains(WindowsJobSupervisorContract.SupervisorSource, "CREATE_SUSPENDED");
        StringAssert.Contains(WindowsJobSupervisorContract.SupervisorSource, "PROC_THREAD_ATTRIBUTE_HANDLE_LIST");
        StringAssert.Contains(WindowsJobSupervisorContract.LoaderScript, "IRONDEV_BROKER_DENIAL_PROVEN");
        StringAssert.Contains(WindowsJobSupervisorContract.LoaderScript, "projectBytesCopiedAfterPreflightProven");
    }

    [TestMethod]
    public void DockerCommandRunner_ClearsTheInheritedHostEnvironmentBeforeAddingExplicitValues()
    {
        var source = Read("IronDev.Infrastructure/Services/Sandbox/DockerCommandRunner.cs");

        StringAssert.Contains(source, "startInfo.Environment.Clear();");
        StringAssert.Contains(
            source,
            "foreach (var item in request.Environment.OrderBy(item => item.Key, StringComparer.Ordinal))");
        StringAssert.Contains(source, "startInfo.Environment[item.Key] = item.Value;");
        StringAssert.Contains(source, "UseShellExecute = false");
        StringAssert.Contains(source, "startInfo.ArgumentList.Add(argument);");
    }

    [TestMethod]
    public async Task Runtime_CreatesOnlyTheInspectedHyperVNetworklessReadOnlyResourceEnvelope()
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files);
        var runtime = files.CreateRuntime(runner);

        var result = await runtime.ExecuteAsync(files.ValidRequest());

        Assert.AreEqual(SandboxExecutionStatus.Succeeded, result.Status);
        Assert.IsTrue(result.CleanedUp);
        Assert.IsTrue(result.Inspection.WasDestroyed);
        Assert.AreEqual(SandboxIsolationModes.HcsHyperV, result.Inspection.IsolationMode);
        Assert.AreEqual(0, result.Inspection.NetworkEndpointCount);
        Assert.AreEqual(0, result.Inspection.HostWritableMountCount);
        Assert.AreEqual(64, result.Inspection.MaximumUntrustedWorkloadProcessCount);
        Assert.AreEqual(
            WindowsJobSupervisorContract.WorkloadProcessScope,
            result.Inspection.UntrustedWorkloadProcessScope);
        Assert.AreEqual(WindowsJobSupervisorContract.Version, result.Inspection.TrustedSupervisorVersion);
        Assert.AreEqual(WindowsJobSupervisorContract.Sha256, result.Inspection.TrustedSupervisorSha256);
        Assert.IsTrue(result.Inspection.SuspendedAssignmentBeforeResumeProven);
        Assert.IsTrue(result.Inspection.UntrustedWorkloadProcessLimitProven);
        Assert.IsTrue(result.Inspection.RestrictedLowIntegrityWorkloadIdentityProven);
        Assert.IsTrue(result.Inspection.SupervisorHandleIsolationProven);
        Assert.IsTrue(result.Inspection.WorkloadScratchAndEvidenceBoundaryProven);
        Assert.IsTrue(result.Inspection.BrokerLaunchDenialProven);
        Assert.IsTrue(result.Inspection.ProjectBytesCopiedAfterPreflightProven);

        var create = runner.Requests.Single(request => DockerArguments(request)[0] == "create");
        var arguments = DockerArguments(create);
        CollectionAssert.Contains(arguments, "--pull=never");
        CollectionAssert.Contains(arguments, "--isolation=hyperv");
        CollectionAssert.Contains(arguments, "--network=none");
        CollectionAssert.Contains(arguments, "--restart=no");
        CollectionAssert.Contains(arguments, "--no-healthcheck");
        Assert.AreEqual("ContainerAdministrator", ValueAfter(arguments, "--user"));
        Assert.AreEqual("2", ValueAfter(arguments, "--cpu-count"));
        Assert.AreEqual((4096L * 1024 * 1024).ToString(), ValueAfter(arguments, "--memory"));
        Assert.AreEqual($"size={12L * 1024 * 1024 * 1024}", ValueAfter(arguments, "--storage-opt"));
        CollectionAssert.DoesNotContain(arguments, "--pids-limit");
        Assert.IsFalse(Read("IronDev.Infrastructure/Services/Sandbox/DockerHcsContainerRuntime.cs")
            .Contains("PidsLimit", StringComparison.Ordinal));

        var mounts = arguments
            .Select((argument, index) => (argument, index))
            .Where(item => item.argument == "--mount")
            .Select(item => arguments[item.index + 1])
            .ToArray();
        Assert.AreEqual(3, mounts.Length);
        Assert.IsTrue(mounts.All(mount => mount.EndsWith(",readonly", StringComparison.Ordinal)));
        Assert.IsTrue(mounts.Any(mount => mount.Contains(files.SourcePath, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(mounts.Any(mount => mount.Contains(files.FeedPath, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(mounts.Any(mount =>
            mount.Contains(Path.Combine(files.FeedPath, "NuGet.Config"), StringComparison.OrdinalIgnoreCase) &&
            mount.Contains(HcsContainerRuntimeConstants.NuGetConfigContainerPath, StringComparison.OrdinalIgnoreCase)));

        Assert.IsTrue(runner.Requests.All(request =>
            request.Environment.Keys.All(name => name is "SystemRoot" or "TEMP")));
        Assert.IsFalse(runner.Requests.Any(request => request.Environment.Keys.Any(name =>
            name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PROXY", StringComparison.OrdinalIgnoreCase))));

        var ordered = runner.Requests.Select(DockerArguments).ToArray();
        var createIndex = Array.FindIndex(ordered, current => current[0] == "create");
        var stoppedInspectIndex = Array.FindIndex(
            ordered,
            createIndex + 1,
            current => current[0] == "container" && current.ElementAtOrDefault(1) == "inspect");
        var supervisorCopyIndex = Array.FindIndex(
            ordered,
            current => current[0] == "cp" && current[^1].StartsWith(RuntimeTestFiles.ContainerId + ":", StringComparison.Ordinal));
        var startIndex = Array.FindIndex(ordered, arguments => arguments[0] == "start");
        var firstExecIndex = Array.FindIndex(ordered, arguments => arguments[0] == "exec");
        Assert.IsTrue(createIndex >= 0 && stoppedInspectIndex > createIndex);
        Assert.IsTrue(supervisorCopyIndex > stoppedInspectIndex && startIndex > supervisorCopyIndex);
        Assert.IsTrue(firstExecIndex > startIndex);
        Assert.IsTrue(ordered
            .Skip(startIndex + 1)
            .Take(firstExecIndex - startIndex - 1)
            .Any(arguments => arguments[0] == "container" && arguments.ElementAtOrDefault(1) == "inspect"),
            "The active container must be reinspected after start and before source copy or project execution.");
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(files.EvidenceRootPath)
            .Any(path => Path.GetFileName(path).StartsWith(".supervisor-", StringComparison.Ordinal)));

        var supervisorExecs = ordered.Where(current => current[0] == "exec" && current.Contains("--workdir")).ToArray();
        Assert.IsTrue(supervisorExecs.Length >= 4);
        Assert.IsTrue(supervisorExecs.All(current =>
            string.Equals(ValueAfter(current, "--workdir"), @"C:\Windows\System32", StringComparison.Ordinal)));
        var bootstrapEncoded = ValueAfter(supervisorExecs[0], "-EncodedCommand");
        Assert.IsTrue(bootstrapEncoded.Length < 24_000);
        Assert.AreEqual(
            WindowsJobSupervisorContract.BootstrapScript,
            System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(bootstrapEncoded)));
        Assert.IsTrue(supervisorExecs.All(current =>
            System.Text.Encoding.Unicode.GetString(
                Convert.FromBase64String(ValueAfter(current, "-EncodedCommand"))) ==
            WindowsJobSupervisorContract.BootstrapScript));
        Assert.IsFalse(supervisorExecs.Any(current => current.Contains(
            @"C:\Program Files\dotnet\dotnet.exe",
            StringComparer.OrdinalIgnoreCase)));
        Assert.IsTrue(WindowsJobSupervisorContract.BrokerProbeEncodedCommandLength < 24_000);
    }

    [TestMethod]
    [DataRow("isolation")]
    [DataRow("network")]
    [DataRow("endpoint")]
    [DataRow("missing-network-topology")]
    [DataRow("writable-mount")]
    [DataRow("resource")]
    public async Task Runtime_RejectsInspectionDriftBeforeAnyProjectCommandAndStillCleansUp(string drift)
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files) { InspectionDrift = drift };
        var runtime = files.CreateRuntime(runner);

        var exception = await Assert.ThrowsAsync<HcsContainerRuntimeException>(
            () => runtime.ExecuteAsync(files.ValidRequest()));

        Assert.AreEqual(SandboxReasonCodes.IsolationInspectionFailed, exception.ReasonCode);
        Assert.IsFalse(runner.Requests.Any(IsStageExecution));
        CollectionAssert.Contains(runner.RemovedContainerIds, RuntimeTestFiles.ContainerId);
    }

    [TestMethod]
    public async Task Runtime_RejectsMissingSupervisorProofBeforeAnyProjectStage()
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files) { OmitSupervisorProof = true };

        var exception = await Assert.ThrowsAsync<HcsContainerRuntimeException>(
            () => files.CreateRuntime(runner).ExecuteAsync(files.ValidRequest()));

        Assert.AreEqual(SandboxReasonCodes.IsolationInspectionFailed, exception.ReasonCode);
        Assert.IsFalse(runner.Requests.Any(IsStageExecution));
        CollectionAssert.Contains(runner.RemovedContainerIds, RuntimeTestFiles.ContainerId);
    }

    [TestMethod]
    public async Task Runtime_RejectsSupervisorAuthorityDriftBeforeEvidenceReadOrCleanup()
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files);
        var runtime = files.CreateRuntime(runner);
        var request = files.ValidRequest();

        await Assert.ThrowsAsync<HcsContainerRuntimeException>(() =>
            runtime.TryReadCompletedEvidenceAsync(new HcsCompletedEvidenceRequest(
                request.ExecutionId,
                request.PolicySha256,
                request.TrustedSupervisorVersion,
                Hash('9'),
                request.EvidenceOutputPath)));
        await Assert.ThrowsAsync<HcsContainerRuntimeException>(() =>
            runtime.RecoverExecutionAsync(new HcsExecutionCleanupRequest(
                request.ExecutionId,
                request.PolicySha256,
                request.TrustedSupervisorVersion + "-drift",
                request.TrustedSupervisorSha256,
                request.EvidenceOutputPath)));

        Assert.AreEqual(0, runner.Requests.Count);
    }

    [TestMethod]
    [DataRow(0, false, (int)SandboxExecutionStatus.Succeeded)]
    [DataRow(9, false, (int)SandboxExecutionStatus.Failed)]
    [DataRow(-1, true, (int)SandboxExecutionStatus.TimedOut)]
    public async Task Runtime_CleansUpAfterSuccessStageFailureAndStageTimeout(
        int firstStageExitCode,
        bool firstStageTimedOut,
        int expectedStatus)
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files)
        {
            FirstStageExitCode = firstStageExitCode,
            FirstStageTimedOut = firstStageTimedOut
        };

        var result = await files.CreateRuntime(runner).ExecuteAsync(files.ValidRequest());

        Assert.AreEqual((SandboxExecutionStatus)expectedStatus, result.Status);
        Assert.IsTrue(result.CleanedUp);
        Assert.IsTrue(result.Inspection.WasDestroyed);
        Assert.IsTrue(Directory.Exists(result.EvidencePath));
        CollectionAssert.Contains(runner.RemovedContainerIds, RuntimeTestFiles.ContainerId);
    }

    [TestMethod]
    public async Task Runtime_CallerCancellationReturnsCancelledEvidenceAndRemovesTheOwnedContainer()
    {
        using var files = RuntimeTestFiles.Create();
        using var caller = new CancellationTokenSource();
        var runner = new RecordingDockerRunner(files)
        {
            CancelAtFirstStage = caller
        };

        var result = await files.CreateRuntime(runner).ExecuteAsync(files.ValidRequest(), caller.Token);

        Assert.AreEqual(SandboxExecutionStatus.Cancelled, result.Status);
        Assert.IsTrue(result.CleanedUp);
        Assert.IsTrue(result.Inspection.WasDestroyed);
        CollectionAssert.Contains(runner.RemovedContainerIds, RuntimeTestFiles.ContainerId);
    }

    [DataTestMethod]
    [DataRow("timeout")]
    [DataRow("malformed-id")]
    public async Task Runtime_CreateAmbiguityRemovesOnlyTheExactOwnedDeterministicOrphan(string outcome)
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files) { CreateOutcome = outcome };

        await Assert.ThrowsAsync<HcsContainerRuntimeException>(
            () => files.CreateRuntime(runner).ExecuteAsync(files.ValidRequest()));

        CollectionAssert.Contains(runner.RemovedContainerIds, RuntimeTestFiles.ContainerId);
    }

    [TestMethod]
    public async Task Runtime_CreateAmbiguityNeverRemovesAMismatchedDeterministicNameCollision()
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files)
        {
            CreateOutcome = "malformed-id",
            OrphanOwnerLabel = "another-runtime-owner"
        };

        await Assert.ThrowsAsync<HcsContainerRuntimeException>(
            () => files.CreateRuntime(runner).ExecuteAsync(files.ValidRequest()));

        CollectionAssert.DoesNotContain(runner.RemovedContainerIds, RuntimeTestFiles.ContainerId);
        Assert.IsFalse(runner.Requests
            .Where(request => DockerArguments(request).FirstOrDefault() == "rm")
            .Any(request => DockerArguments(request).Last() == RuntimeTestFiles.ContainerId));
    }

    [TestMethod]
    public async Task Runtime_ReplayRemovesOnlyEvidenceOwnedByTheExactExecutionAndPolicy()
    {
        using var files = RuntimeTestFiles.Create();
        var request = files.ValidRequest();
        var first = await files.CreateRuntime(new RecordingDockerRunner(files)).ExecuteAsync(request);
        File.WriteAllText(
            Path.Combine(first.EvidencePath, "sandbox-evidence-manifest.json"),
            JsonSerializer.Serialize(new
            {
                executionId = request.ExecutionId,
                sandboxPolicySha256 = request.PolicySha256
            }));

        var replayRunner = new RecordingDockerRunner(files);
        var replay = await files.CreateRuntime(replayRunner).ExecuteAsync(request);

        Assert.AreEqual(SandboxExecutionStatus.Succeeded, replay.Status);
        Assert.IsTrue(Directory.Exists(replay.EvidencePath));
        Assert.IsTrue(File.Exists(Path.Combine(
            replay.EvidencePath,
            HcsContainerRuntimeConstants.EvidenceOwnerMarkerFileName)));
        Assert.IsTrue(replayRunner.Requests.Any(request => DockerArguments(request).FirstOrDefault() == "create"));
    }

    [TestMethod]
    public async Task Runtime_ReplayNeverRemovesMismatchedEvidence()
    {
        using var files = RuntimeTestFiles.Create();
        var request = files.ValidRequest();
        var first = await files.CreateRuntime(new RecordingDockerRunner(files)).ExecuteAsync(request);
        File.WriteAllText(
            Path.Combine(first.EvidencePath, HcsContainerRuntimeConstants.EvidenceOwnerMarkerFileName),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                executionId = request.ExecutionId,
                policySha256 = Hash('e'),
                owner = "irondev-tests"
            }));
        var replayRunner = new RecordingDockerRunner(files);

        await Assert.ThrowsAsync<HcsContainerRuntimeException>(
            () => files.CreateRuntime(replayRunner).ExecuteAsync(request));

        Assert.IsTrue(Directory.Exists(first.EvidencePath));
        Assert.AreEqual(0, replayRunner.Requests.Count);
    }

    [TestMethod]
    public async Task Runtime_RejectsSecretEnvironmentNamesBeforeCallingTheContainerEngine()
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files);
        var request = files.ValidRequest() with
        {
            Environment = new Dictionary<string, string> { ["GITHUB_TOKEN"] = "must-not-cross" },
            EnvironmentAllowList = ["GITHUB_TOKEN"]
        };

        var exception = await Assert.ThrowsAsync<HcsContainerRuntimeException>(
            () => files.CreateRuntime(runner).ExecuteAsync(request));

        Assert.AreEqual(SandboxReasonCodes.ExecutionRejected, exception.ReasonCode);
        Assert.AreEqual(0, runner.Requests.Count);
    }

    [TestMethod]
    public async Task Recovery_RemovesOnlyResourcesWhoseExactOwnerRuntimeAndExecutionLabelsVerify()
    {
        using var files = RuntimeTestFiles.Create();
        var ownedId = Hash('a');
        var unknownId = Hash('b');
        var ownedExecution = Guid.Parse("70000000-0000-0000-0000-000000000007");
        var runner = new RecordingDockerRunner(files);
        runner.RecoveryCandidates.Add(ownedId);
        runner.RecoveryCandidates.Add(unknownId);
        runner.RecoveryOwnership[ownedId] = new RecoveryLabels(
            "irondev-tests",
            HcsContainerRuntimeConstants.RuntimeLabelValue,
            ownedExecution);
        runner.RecoveryOwnership[unknownId] = new RecoveryLabels(
            "some-other-owner",
            HcsContainerRuntimeConstants.RuntimeLabelValue,
            Guid.Parse("80000000-0000-0000-0000-000000000008"));

        var result = await files.CreateRuntime(runner).RecoverOwnedContainersAsync(
            new HcsContainerRecoveryRequest());

        Assert.AreEqual(2, result.CandidatesFound);
        Assert.AreEqual(1, result.ContainersRemoved);
        CollectionAssert.Contains(runner.RemovedContainerIds, ownedId);
        CollectionAssert.DoesNotContain(runner.RemovedContainerIds, unknownId);
        Assert.IsTrue(runner.Requests
            .Where(request => DockerArguments(request).FirstOrDefault() == "rm")
            .All(request => DockerArguments(request).Last() != unknownId));
    }

    [TestMethod]
    [DataRow("owner-pending")]
    [DataRow("loader-pending")]
    [DataRow("source-pending")]
    [DataRow("exact-final")]
    public async Task ExactRecovery_CleansOnlyRecognizedSupervisorCrashResidue(string residue)
    {
        using var files = RuntimeTestFiles.Create();
        var request = files.ValidRequest();
        var (stagingPath, ownerPath) = SupervisorStagingPaths(files, request);
        if (residue == "owner-pending")
        {
            File.WriteAllBytes(ownerPath + ".pending", [1, 2, 3]);
        }
        else
        {
            File.WriteAllText(ownerPath, SupervisorOwnerJson(request));
            Directory.CreateDirectory(stagingPath);
            if (residue == "loader-pending")
                File.WriteAllBytes(Path.Combine(stagingPath, WindowsJobSupervisorContract.LoaderFileName + ".pending"), [1, 2, 3]);
            else if (residue == "source-pending")
                File.WriteAllBytes(Path.Combine(stagingPath, WindowsJobSupervisorContract.SourceFileName + ".pending"), [1, 2, 3]);
            else
            {
                File.WriteAllBytes(
                    Path.Combine(stagingPath, WindowsJobSupervisorContract.LoaderFileName),
                    WindowsJobSupervisorContract.GetLoaderBytes());
                File.WriteAllBytes(
                    Path.Combine(stagingPath, WindowsJobSupervisorContract.SourceFileName),
                    WindowsJobSupervisorContract.GetSourceBytes());
            }
        }

        var result = await files.CreateRuntime(new RecordingDockerRunner(files))
            .RecoverExecutionAsync(CleanupRequest(request));

        Assert.IsTrue(result.ContainerCleanupConfirmed);
        Assert.IsTrue(result.EvidenceCleanupConfirmed);
        Assert.IsFalse(Directory.Exists(stagingPath));
        Assert.IsFalse(File.Exists(ownerPath));
        Assert.IsFalse(File.Exists(ownerPath + ".pending"));
    }

    [TestMethod]
    [DataRow("tampered-final")]
    [DataRow("oversized-pending")]
    [DataRow("unexpected-entry")]
    public async Task ExactRecovery_PreservesUnknownOrTamperedSupervisorResidue(string residue)
    {
        using var files = RuntimeTestFiles.Create();
        var request = files.ValidRequest();
        var (stagingPath, ownerPath) = SupervisorStagingPaths(files, request);
        File.WriteAllText(ownerPath, SupervisorOwnerJson(request));
        Directory.CreateDirectory(stagingPath);
        if (residue == "tampered-final")
        {
            var bytes = WindowsJobSupervisorContract.GetLoaderBytes();
            bytes[0] ^= 0xff;
            File.WriteAllBytes(Path.Combine(stagingPath, WindowsJobSupervisorContract.LoaderFileName), bytes);
        }
        else if (residue == "oversized-pending")
        {
            File.WriteAllBytes(
                Path.Combine(stagingPath, WindowsJobSupervisorContract.LoaderFileName + ".pending"),
                new byte[WindowsJobSupervisorContract.GetLoaderBytes().Length + 1]);
        }
        else
        {
            File.WriteAllText(Path.Combine(stagingPath, "unknown.bin"), "unknown");
        }

        var result = await files.CreateRuntime(new RecordingDockerRunner(files))
            .RecoverExecutionAsync(CleanupRequest(request));

        Assert.IsFalse(result.EvidenceCleanupConfirmed);
        Assert.IsTrue(Directory.Exists(stagingPath));
        Assert.IsTrue(File.Exists(ownerPath));
    }

    [TestMethod]
    public async Task ExactRecovery_DoesNotClaimAbsenceWhenTheContainerControlPlaneIsUnavailable()
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files) { InspectionControlPlaneUnavailable = true };
        var request = files.ValidRequest();

        var exception = await Assert.ThrowsAsync<HcsContainerRuntimeException>(() =>
            files.CreateRuntime(runner).RecoverExecutionAsync(new HcsExecutionCleanupRequest(
                request.ExecutionId,
                request.PolicySha256,
                request.TrustedSupervisorVersion,
                request.TrustedSupervisorSha256,
                request.EvidenceOutputPath)));

        Assert.AreEqual(SandboxReasonCodes.RuntimeUnavailable, exception.ReasonCode);
        Assert.IsFalse(runner.RemovedContainerIds.Any());
    }

    [TestMethod]
    public async Task Runtime_RefusesEvidenceCopyWhenStoppedStateCannotBeConfirmed()
    {
        using var files = RuntimeTestFiles.Create();
        var runner = new RecordingDockerRunner(files) { StopStateRemainsRunning = true };

        await Assert.ThrowsAsync<HcsContainerRuntimeException>(() =>
            files.CreateRuntime(runner).ExecuteAsync(files.ValidRequest()));

        Assert.IsFalse(runner.Requests.Any(request =>
        {
            var arguments = DockerArguments(request);
            return arguments.FirstOrDefault() == "cp" &&
                   arguments.ElementAtOrDefault(1)?.StartsWith(
                       RuntimeTestFiles.ContainerId + ":" + HcsContainerRuntimeConstants.EvidenceContainerPath,
                       StringComparison.OrdinalIgnoreCase) == true;
        }));
        CollectionAssert.Contains(runner.RemovedContainerIds, RuntimeTestFiles.ContainerId);
    }

    [TestMethod]
    public void Migration_BindsAttemptsAndEvidenceToExactRepositoryAndProfileRevisions()
    {
        var sql = Read(MigrationPath);

        StringAssert.Contains(sql, "FK_SandboxQualificationAttempts_BindingRevision");
        StringAssert.Contains(sql, "(TenantId, ProjectId, RepositoryBindingId, ExpectedBindingRevision)");
        StringAssert.Contains(sql, "dbo.RepositoryBindingRevisions");
        StringAssert.Contains(sql, "FK_SandboxQualificationAttempts_ProfileRevision");
        StringAssert.Contains(sql, "ExpectedExecutionProfileRevision");
        StringAssert.Contains(sql, "dbo.ProjectExecutionProfileRevisions");
        StringAssert.Contains(sql, "FK_SandboxEvidenceManifests_AttemptAuthority");
        StringAssert.Contains(sql, "FK_SandboxQualificationAttempts_SourceAuthority");
        StringAssert.Contains(sql, "RepositoryProvisioningReceiptId");
        StringAssert.Contains(sql, "SourceManifestSha256");
        StringAssert.Contains(sql, "SourceGitTreeId");
        StringAssert.Contains(sql, "RepositoryBindingRevision");
        StringAssert.Contains(sql, "ProjectExecutionProfileRevision");
    }

    [TestMethod]
    public void Migration_BindsEachMutationToExactClientOperationAndWorkbenchFence()
    {
        var sql = Read(MigrationPath);

        StringAssert.Contains(sql, "ClientOperationRecordId BIGINT NOT NULL");
        StringAssert.Contains(sql, "ClientOperationId UNIQUEIDENTIFIER NOT NULL");
        StringAssert.Contains(sql, "ClientOperationKind NVARCHAR(100) NOT NULL");
        StringAssert.Contains(sql, "ClientOperationResourceScopeId NVARCHAR(200) NOT NULL");
        StringAssert.Contains(sql, "WorkbenchSessionId BIGINT NOT NULL");
        StringAssert.Contains(sql, "LeaseEpoch BIGINT NOT NULL");
        StringAssert.Contains(sql, "FK_SandboxQualificationAttempts_ClientOperationAuthority");
        StringAssert.Contains(sql, "ClientOperationKind=N'QualifyProductionSandbox'");
        StringAssert.Contains(sql, "N'project:' + CONVERT(NVARCHAR(20), ProjectId) + N':sandbox-qualification'");
        StringAssert.Contains(sql, "FK_SandboxQualificationAttempts_Fence");
        StringAssert.Contains(sql, "(TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)");
        StringAssert.Contains(sql, "dbo.WorkbenchWriteLeases");
    }

    [TestMethod]
    public void MutationAuthority_IsActorScopedAndUnavailableRuntimeIsRejectedBeforeClaimInsert()
    {
        var sql = Read(MigrationPath);
        StringAssert.Contains(
            sql,
            "(TenantId, ProjectId, ActorUserId, ClientOperationId)");
        StringAssert.Contains(
            sql,
            "(TenantId, ActorUserId, OperationKind, ResourceScopeId, ClientOperationId)");

        var source = Read(
            "IronDev.Infrastructure/Services/Sandbox/WorkbenchSandboxQualificationService.cs");
        StringAssert.Contains(source, "AND ResourceScopeId=@ResourceScopeId AND ActorUserId=@ActorUserId");
        var runtimePreflight = source.IndexOf(
            "effectiveCapability = await CombineRuntimeCapabilityAsync",
            StringComparison.Ordinal);
        var claimInsert = source.IndexOf("INSERT dbo.ClientOperations", StringComparison.Ordinal);
        Assert.IsTrue(runtimePreflight >= 0 && runtimePreflight < claimInsert,
            "Unsafe or unavailable runtime capability must fail before a durable operation is claimed.");
    }

    [TestMethod]
    public void AttemptProjection_ExposesTheExactRepositoryProfileAndBaselineAuthority()
    {
        var repositoryBindingId = Guid.Parse("41000000-0000-4000-8000-000000000001");
        var projectExecutionProfileId = Guid.Parse("42000000-0000-4000-8000-000000000002");
        var baselineCommit = new string('a', 40);
        var snapshot = new SandboxQualificationAttemptSnapshot(
            Guid.Parse("43000000-0000-4000-8000-000000000003"),
            Guid.Parse("44000000-0000-4000-8000-000000000004"),
            SandboxQualificationStates.Passed,
            repositoryBindingId,
            7,
            projectExecutionProfileId,
            9,
            baselineCommit,
            new DateTimeOffset(2026, 7, 22, 1, 2, 3, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 22, 1, 5, 3, TimeSpan.Zero),
            Hash('e'),
            null,
            "The exact qualification passed.",
            CanRecover: false);

        using var json = JsonDocument.Parse(SandboxCanonicalJson.Serialize(snapshot));
        Assert.AreEqual(repositoryBindingId, json.RootElement.GetProperty("repositoryBindingId").GetGuid());
        Assert.AreEqual(projectExecutionProfileId, json.RootElement.GetProperty("projectExecutionProfileId").GetGuid());
        Assert.AreEqual(baselineCommit, json.RootElement.GetProperty("baselineCommit").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("canRecover").GetBoolean());

        var service = Read("IronDev.Infrastructure/Services/Sandbox/WorkbenchSandboxQualificationService.cs");
        StringAssert.Contains(service, "attempt.RepositoryBindingId,");
        StringAssert.Contains(service, "attempt.ProjectExecutionProfileId,");
        StringAssert.Contains(service, "attempt.BaselineCommit,");
    }

    [TestMethod]
    public void Migration_AllowsOnlyOneRunningAttemptAndOneRunningToTerminalTransition()
    {
        var sql = Read(MigrationPath);

        StringAssert.Contains(sql, "UX_SandboxQualificationAttempts_OneRunningPerProject");
        StringAssert.Contains(sql, "WHERE State=N'Running'");
        StringAssert.Contains(sql, "TR_SandboxQualificationAttempts_TerminalImmutable");
        StringAssert.Contains(sql, "d.State<>N''Running'' OR");
        StringAssert.Contains(sql, "i.LastRecoveryAttemptAtUtc<=d.LastRecoveryAttemptAtUtc");
        StringAssert.Contains(
            sql,
            "State IN (N'Failed', N'Cancelled', N'Recovered') AND");
        StringAssert.Contains(sql, "CleanupConfirmed=1");
        StringAssert.Contains(
            sql,
            "Sandbox qualification attempts allow only advancing recovery scheduling or one Running-to-terminal transition.");
    }

    [TestMethod]
    public void Migration_MakesEvidenceAppendOnlyWithoutChangingReadinessOrGrantingBuilderAuthority()
    {
        var sql = Read(MigrationPath);

        StringAssert.Contains(sql, "TR_SandboxEvidenceManifests_AppendOnly");
        StringAssert.Contains(sql, "AFTER UPDATE, DELETE");
        StringAssert.Contains(sql, "Sandbox evidence manifests are append-only.");
        StringAssert.Contains(sql, "EvidenceManifestSha256");
        StringAssert.Contains(sql, "ManifestSha256");

        Assert.IsFalse(
            sql.Contains("ProjectReadinessAssessments", StringComparison.OrdinalIgnoreCase),
            "PR-06A qualification evidence must not update or bind project readiness; PR-06B owns readiness.");
        Assert.IsFalse(
            sql.Contains("Builder", StringComparison.OrdinalIgnoreCase),
            "Sandbox qualification must not create or consume Builder execution authority.");
    }

    [TestMethod]
    public void Migration_IsRegisteredInBothManifestsWithItsBoundedAuthorityDeclared()
    {
        using var migrations = JsonDocument.Parse(Read("Database/migrations.json"));
        var migration = migrations.RootElement.GetProperty("migrations")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("path").GetString() == MigrationPath);

        Assert.AreEqual("2026-07-workbench-pr06a-sandbox-qualification", migration.GetProperty("id").GetString());
        var description = migration.GetProperty("description").GetString()!;
        StringAssert.Contains(description, "exact repository/profile/policy provenance");
        StringAssert.Contains(description, "without changing project readiness");

        using var inventory = JsonDocument.Parse(Read("Database/sql-inventory.json"));
        var entry = inventory.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Single(item => item.GetProperty("path").GetString() == MigrationPath);

        Assert.AreEqual("database.migrate-workbench-sandbox-qualification", entry.GetProperty("id").GetString());
        Assert.IsTrue(entry.GetProperty("appliedByManifest").GetBoolean());
        Assert.IsTrue(entry.GetProperty("verifiedByScript").GetBoolean());

        var objectsCalled = entry.GetProperty("objectsCalled")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        CollectionAssert.Contains(objectsCalled, "dbo.RepositoryBindingRevisions");
        CollectionAssert.Contains(objectsCalled, "dbo.ProjectExecutionProfileRevisions");
        CollectionAssert.Contains(objectsCalled, "dbo.WorkbenchWriteLeases");
        CollectionAssert.Contains(objectsCalled, "dbo.ClientOperations");
        CollectionAssert.DoesNotContain(objectsCalled, "dbo.ProjectReadinessAssessments");

        var notes = entry.GetProperty("notes").GetString()!;
        StringAssert.Contains(notes, "preserving NotConfigured readiness");
        StringAssert.Contains(notes, "qualification grants no Builder authority");

        var verifier = Read("Database/verify-migrations.ps1");
        StringAssert.Contains(verifier, "Sandbox qualification attempt table");
        StringAssert.Contains(verifier, "Sandbox actor-scoped operation key");
        StringAssert.Contains(verifier, "Sandbox one-running-attempt index");
        StringAssert.Contains(verifier, "Sandbox evidence append-only trigger");
        StringAssert.Contains(verifier, "Sandbox terminal attempt immutability trigger");
    }

    private static SandboxEvidenceManifest ValidEvidenceManifest()
    {
        var started = new DateTimeOffset(2026, 7, 22, 1, 2, 3, TimeSpan.Zero);
        return new SandboxEvidenceManifest
        {
            SchemaVersion = 1,
            ExecutionId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            ProjectId = 42,
            RepositoryBindingId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            RepositoryBindingRevision = 7,
            BaselineCommit = new string('f', 40),
            WorktreeFingerprint = Hash('1'),
            ProjectExecutionProfileId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
            ProjectExecutionProfileRevision = 9,
            ProfileDefinitionId = "greenfield-winforms-net10-mstest-v1",
            ProfileDescriptorRevision = 1,
            DescriptorSha256 = Hash('a'),
            TemplateBundleSha256 = Hash('b'),
            ToolchainManifestId = "dotnet-sdk-10.0.302-runtime-10.0.10-v1",
            ContainerImageDigest = Hash('c'),
            SandboxPolicyVersion = SandboxPolicyVersions.WorkbenchV01,
            SandboxPolicySha256 = Hash('d'),
            TrustedSupervisorVersion = WindowsJobSupervisorContract.Version,
            TrustedSupervisorSha256 = WindowsJobSupervisorContract.Sha256,
            OfflineFeedManifestSha256 = Hash('e'),
            Status = SandboxExecutionStatus.Succeeded,
            ReasonCode = SandboxReasonCodes.Ready,
            SafeSummary = "The isolated Windows container execution completed.",
            StartedAtUtc = started,
            CompletedAtUtc = started.AddMinutes(3),
            Inspection = new SandboxRuntimeInspection
            {
                RuntimeName = "DockerHcs",
                IsolationMode = SandboxIsolationModes.HcsHyperV,
                ActualContainerImageDigest = Hash('c'),
                VirtualCpuCount = 2,
                MemoryMaximumMiB = 4096,
                WritableScratchMaximumGiB = 12,
                MaximumUntrustedWorkloadProcessCount = 64,
                UntrustedWorkloadProcessScope = WindowsJobSupervisorContract.WorkloadProcessScope,
                TrustedSupervisorVersion = WindowsJobSupervisorContract.Version,
                TrustedSupervisorSha256 = WindowsJobSupervisorContract.Sha256,
                SuspendedAssignmentBeforeResumeProven = true,
                UntrustedWorkloadProcessLimitProven = true,
                RestrictedLowIntegrityWorkloadIdentityProven = true,
                SupervisorHandleIsolationProven = true,
                WorkloadScratchAndEvidenceBoundaryProven = true,
                BrokerLaunchDenialProven = true,
                ProjectBytesCopiedAfterPreflightProven = true,
                NetworkEndpointCount = 0,
                HostWritableMountCount = 0,
                RepositoryInputReadOnly = true,
                OfflineFeedReadOnly = true,
                WasDestroyed = true,
                InspectedAtUtc = started.AddSeconds(2)
            },
            Stages =
            [
                Stage(SandboxExecutionStage.Test, '3'),
                Stage(SandboxExecutionStage.Restore, '1'),
                Stage(SandboxExecutionStage.Build, '2')
            ],
            Artifacts =
            [
                new SandboxEvidenceArtifact
                {
                    Kind = "test-results",
                    RelativePath = "test/results.trx",
                    LengthBytes = 200,
                    ContentSha256 = Hash('8')
                },
                new SandboxEvidenceArtifact
                {
                    Kind = "build-log",
                    RelativePath = "build/build.binlog",
                    LengthBytes = 100,
                    ContentSha256 = Hash('7')
                }
            ]
        };
    }

    private static SandboxExecutionProfileBinding ValidProfile() => new()
    {
        ProfileDefinitionId = "greenfield-winforms-net10-mstest-v1",
        ProfileDescriptorRevision = 1,
        DescriptorSha256 = Hash('a'),
        TemplateBundleSha256 = Hash('b'),
        ToolchainManifestId = "dotnet-sdk-10.0.302-runtime-10.0.10-v1",
        ExecutionImageReference = "mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2025",
        RestoreCommand = @"dotnet restore IronDev.slnx --configfile C:\IronDev\NuGet.Config --locked-mode",
        BuildCommand = "dotnet build IronDev.slnx --configuration Release --no-restore",
        TestCommand = "dotnet test Tests.csproj --configuration Release --no-restore --no-build"
    };

    private static SandboxStageEvidence Stage(SandboxExecutionStage stage, char hashCharacter) => new()
    {
        Stage = stage,
        CommandSha256 = Hash(hashCharacter),
        ExitCode = 0,
        TimedOut = false,
        DurationMilliseconds = (long)stage * 1000,
        StandardOutputSha256 = Hash('4'),
        StandardErrorSha256 = Hash('5'),
        StandardOutputTruncated = false,
        StandardErrorTruncated = false
    };

    private static string Hash(char character) => new(character, 64);

    private static (string StagingPath, string OwnerPath) SupervisorStagingPaths(
        RuntimeTestFiles files,
        HcsContainerRuntimeRequest request)
    {
        var staging = Path.Combine(files.EvidenceRootPath, $".supervisor-{request.ExecutionId:N}");
        return (staging, staging + WindowsJobSupervisorContract.HostStagingOwnerSuffix);
    }

    private static string SupervisorOwnerJson(HcsContainerRuntimeRequest request) => JsonSerializer.Serialize(new
    {
        request.ExecutionId,
        PolicySha256 = request.PolicySha256,
        request.TrustedSupervisorVersion,
        request.TrustedSupervisorSha256,
        LoaderSha256 = WindowsJobSupervisorContract.LoaderSha256,
        SourceSha256 = WindowsJobSupervisorContract.SourceSha256
    });

    private static HcsExecutionCleanupRequest CleanupRequest(HcsContainerRuntimeRequest request) => new(
        request.ExecutionId,
        request.PolicySha256,
        request.TrustedSupervisorVersion,
        request.TrustedSupervisorSha256,
        request.EvidenceOutputPath);

    private static string[] DockerArguments(DockerCommandRequest request) =>
        request.Arguments.Skip(4).ToArray();

    private static bool IsStageExecution(DockerCommandRequest request)
    {
        var arguments = DockerArguments(request);
        return arguments.FirstOrDefault() == "exec" &&
               SupervisorMode(arguments) == "stage";
    }

    private static string? SupervisorMode(IReadOnlyList<string> arguments) => arguments
        .Select((value, index) => (value, index))
        .Where(item => item.value == "--env" && item.index + 1 < arguments.Count)
        .Select(item => arguments[item.index + 1])
        .FirstOrDefault(value => value.StartsWith("IRONDEV_SUPERVISOR_MODE=", StringComparison.Ordinal))?
        .Split('=', 2)[1];

    private static string ValueAfter(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == name)
                return arguments[index + 1];
        }

        Assert.Fail($"Docker arguments did not contain {name} with a value.");
        return string.Empty;
    }

    private sealed class RuntimeTestFiles : IDisposable
    {
        public static readonly string ContainerId = Hash('f');
        public static readonly string ImageId = "sha256:" + Hash('9');
        public static readonly string ImageDigest = Hash('c');
        public static readonly string ImageReference =
            "mcr.microsoft.com/dotnet/sdk@sha256:" + ImageDigest;

        private RuntimeTestFiles(string rootPath)
        {
            RootPath = rootPath;
            SourcePath = Directory.CreateDirectory(Path.Combine(rootPath, "source", "snapshot")).FullName;
            FeedPath = Directory.CreateDirectory(Path.Combine(rootPath, "feed", "content")).FullName;
            File.WriteAllText(
                Path.Combine(FeedPath, "NuGet.Config"),
                "<configuration><packageSources><clear/><add key=\"offline\" value=\"C:\\IronDev\\Feed\"/>" +
                "</packageSources></configuration>");
            EvidenceRootPath = Directory.CreateDirectory(Path.Combine(rootPath, "evidence")).FullName;
            DockerConfigPath = Directory.CreateDirectory(Path.Combine(rootPath, "docker-config")).FullName;
            DockerPath = Path.Combine(rootPath, "docker.exe");
            File.WriteAllBytes(DockerPath, [0x4d, 0x5a]);
        }

        public string RootPath { get; }
        public string SourcePath { get; }
        public string FeedPath { get; }
        public string EvidenceRootPath { get; }
        public string DockerConfigPath { get; }
        public string DockerPath { get; }

        public static RuntimeTestFiles Create() =>
            new(Directory.CreateTempSubdirectory("irondev-pr06a-runtime-").FullName);

        public DockerHcsContainerRuntime CreateRuntime(IDockerCommandRunner runner) => new(
            new HcsContainerRuntimeConfiguration
            {
                DockerExecutablePath = DockerPath,
                DockerEngineHost = "npipe:////./pipe/docker_engine",
                DockerConfigDirectory = DockerConfigPath,
                OwnerLabelValue = "irondev-tests",
                EvidenceRootPath = EvidenceRootPath,
                AllowedSourceRoots = [Path.GetDirectoryName(SourcePath)!],
                AllowedOfflineFeedRoots = [Path.GetDirectoryName(FeedPath)!],
                AllowedImageReferences = [ImageReference],
                DockerHostEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SystemRoot"] = @"C:\Windows",
                    ["TEMP"] = RootPath
                },
                DockerControlCommandTimeout = TimeSpan.FromSeconds(30),
                ContainerInitializationTimeout = TimeSpan.FromMinutes(2)
            },
            runner);

        public HcsContainerRuntimeRequest ValidRequest() => new()
        {
            ExecutionId = Guid.Parse("60000000-0000-0000-0000-000000000006"),
            SourceSnapshotPath = SourcePath,
            OfflineFeedPath = FeedPath,
            EvidenceOutputPath = Path.Combine(EvidenceRootPath, "execution-evidence"),
            ImageReference = ImageReference,
            ExpectedImageDigest = ImageDigest,
            PolicySha256 = Hash('d'),
            TrustedSupervisorVersion = WindowsJobSupervisorContract.Version,
            TrustedSupervisorSha256 = WindowsJobSupervisorContract.Sha256,
            Resources = SandboxResourcePolicy.WorkbenchV01,
            Commands =
            [
                Command(SandboxExecutionStage.Test, '3'),
                Command(SandboxExecutionStage.Restore, '1'),
                Command(SandboxExecutionStage.Build, '2')
            ],
            Environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_NOLOGO"] = "1",
                ["TEMP"] = @"C:\IronDev\Temp"
            },
            EnvironmentAllowList = ["DOTNET_NOLOGO", "TEMP"]
        };

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }

        private static HcsContainerCommand Command(SandboxExecutionStage stage, char hashCharacter) => new()
        {
            Stage = stage,
            CommandPath = @"C:\Program Files\dotnet\dotnet.exe",
            Arguments = [stage.ToString().ToLowerInvariant()],
            CommandSha256 = Hash(hashCharacter),
            Timeout = TimeSpan.FromSeconds(stage switch
            {
                SandboxExecutionStage.Restore => SandboxResourcePolicy.WorkbenchV01.RestoreTimeoutSeconds,
                SandboxExecutionStage.Build => SandboxResourcePolicy.WorkbenchV01.BuildTimeoutSeconds,
                SandboxExecutionStage.Test => SandboxResourcePolicy.WorkbenchV01.TestTimeoutSeconds,
                _ => throw new ArgumentOutOfRangeException(nameof(stage))
            })
        };
    }

    private sealed record RecoveryLabels(string Owner, string Runtime, Guid ExecutionId);

    private sealed class RecordingDockerRunner(RuntimeTestFiles files) : IDockerCommandRunner
    {
        private int _stageCalls;
        private bool _createAttempted;
        private bool _containerStarted;

        public List<DockerCommandRequest> Requests { get; } = [];
        public List<string> RemovedContainerIds { get; } = [];
        public List<string> RecoveryCandidates { get; } = [];
        public Dictionary<string, RecoveryLabels> RecoveryOwnership { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string? InspectionDrift { get; init; }
        public int FirstStageExitCode { get; init; }
        public bool FirstStageTimedOut { get; init; }
        public CancellationTokenSource? CancelAtFirstStage { get; init; }
        public string? CreateOutcome { get; init; }
        public string OrphanOwnerLabel { get; init; } = "irondev-tests";
        public bool InspectionControlPlaneUnavailable { get; init; }
        public bool StopStateRemainsRunning { get; init; }
        public bool OmitSupervisorProof { get; init; }

        public Task<DockerCommandResult> RunAsync(
            DockerCommandRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var arguments = DockerArguments(request);
            if (arguments.Length == 0)
                return Completed(Failure("missing command"));

            if (arguments[0] == "image" && arguments.ElementAtOrDefault(1) == "inspect")
                return Completed(Success(ImageInspectionJson()));
            if (arguments[0] == "create")
            {
                _createAttempted = true;
                return CreateOutcome switch
                {
                    "timeout" => Completed(Result(-1, true)),
                    "malformed-id" => Completed(Success("not-a-container-id")),
                    _ => Completed(Success(RuntimeTestFiles.ContainerId))
                };
            }
            if (arguments[0] == "container" && arguments.ElementAtOrDefault(1) == "inspect")
            {
                if (InspectionControlPlaneUnavailable)
                    return Completed(Failure("container control plane unavailable"));
                var id = arguments[^1];
                if (RemovedContainerIds.Contains(id, StringComparer.OrdinalIgnoreCase) ||
                    (_createAttempted && id.StartsWith("irondev-", StringComparison.OrdinalIgnoreCase) &&
                     RemovedContainerIds.Contains(RuntimeTestFiles.ContainerId, StringComparer.OrdinalIgnoreCase)))
                    return Completed(Failure("container not found"));
                if (arguments.Contains("{{json .State}}", StringComparer.Ordinal))
                    return Completed(Success(JsonSerializer.Serialize(new { Running = StopStateRemainsRunning })));
                if (RecoveryOwnership.TryGetValue(id, out var labels))
                    return Completed(Success(OwnershipInspectionJson(id, labels)));
                if (string.Equals(id, RuntimeTestFiles.ContainerId, StringComparison.OrdinalIgnoreCase))
                    return Completed(Success(ContainerInspectionJson(InspectionDrift)));
                if (_createAttempted && id.StartsWith("irondev-", StringComparison.OrdinalIgnoreCase))
                    return Completed(Success(ContainerInspectionJson(InspectionDrift, OrphanOwnerLabel)));
                return Completed(Failure("container not found"));
            }
            if (arguments[0] == "exec" && SupervisorMode(arguments) == "preflight")
                return Completed(Success(OmitSupervisorProof ? "proof omitted" : SupervisorProofOutput()));
            if (arguments[0] == "exec" && SupervisorMode(arguments) == "stage")
            {
                _stageCalls++;
                if (_stageCalls == 1 && CancelAtFirstStage is not null)
                {
                    CancelAtFirstStage.Cancel();
                    return Task.FromCanceled<DockerCommandResult>(CancelAtFirstStage.Token);
                }

                return Completed(_stageCalls == 1
                    ? Result(FirstStageExitCode, FirstStageTimedOut)
                    : Success());
            }
            if (arguments[0] == "cp")
            {
                if (arguments[^1].StartsWith(RuntimeTestFiles.ContainerId + ":", StringComparison.Ordinal))
                    return Completed(Success());
                File.WriteAllText(Path.Combine(arguments[^1], "restore.stdout.log"), "sanitized evidence");
                return Completed(Success());
            }
            if (arguments[0] == "start")
            {
                _containerStarted = true;
                return Completed(Success());
            }
            if (arguments[0] is "exec" or "stop")
                return Completed(Success());
            if (arguments[0] == "rm")
            {
                RemovedContainerIds.Add(arguments[^1]);
                return Completed(Success());
            }
            if (arguments[0] == "ps")
            {
                if (InspectionControlPlaneUnavailable)
                    return Completed(Failure("container control plane unavailable"));
                var remaining = RecoveryCandidates
                    .Where(id => !RemovedContainerIds.Contains(id, StringComparer.OrdinalIgnoreCase));
                return Completed(Success(string.Join(Environment.NewLine, remaining)));
            }

            return Completed(Failure("unexpected Docker command"));
        }

        private static Task<DockerCommandResult> Completed(DockerCommandResult result) =>
            Task.FromResult(result);

        private static DockerCommandResult Success(string stdout = "") => Result(0, false, stdout);

        private static DockerCommandResult Failure(string stderr) => Result(1, false, stderr: stderr);

        private static string SupervisorProofOutput()
        {
            var proof = JsonSerializer.Serialize(new
            {
                trustedSupervisorVersion = WindowsJobSupervisorContract.Version,
                trustedSupervisorSha256 = WindowsJobSupervisorContract.Sha256,
                maximumUntrustedWorkloadProcessCount = 64,
                untrustedWorkloadProcessScope = WindowsJobSupervisorContract.WorkloadProcessScope,
                suspendedAssignmentBeforeResumeProven = true,
                untrustedWorkloadProcessLimitProven = true,
                restrictedLowIntegrityWorkloadIdentityProven = true,
                supervisorHandleIsolationProven = true,
                workloadScratchAndEvidenceBoundaryProven = true,
                brokerLaunchDenialProven = true,
                projectBytesCopiedAfterPreflightProven = true
            });
            return WindowsJobSupervisorContract.ProofMarker +
                   Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(proof));
        }

        private static DockerCommandResult Result(
            int exitCode,
            bool timedOut,
            string stdout = "",
            string stderr = "") => new()
        {
            ExitCode = exitCode,
            StandardOutput = stdout,
            StandardError = stderr,
            TimedOut = timedOut,
            StandardOutputTruncated = false,
            StandardErrorTruncated = false,
            DurationMilliseconds = 12
        };

        private static string ImageInspectionJson() => JsonSerializer.Serialize(new
        {
            Id = RuntimeTestFiles.ImageId,
            RepoDigests = new[] { RuntimeTestFiles.ImageReference }
        });

        private string ContainerInspectionJson(string? drift, string ownerLabel = "irondev-tests")
        {
            var networks = new Dictionary<string, object>();
            if (drift == "endpoint")
                networks["none"] = new { EndpointID = "unexpected-endpoint" };

            return JsonSerializer.Serialize(new
            {
                Id = RuntimeTestFiles.ContainerId,
                Image = RuntimeTestFiles.ImageId,
                Created = "2026-07-22T01:02:03Z",
                State = new { Running = _containerStarted },
                Config = new
                {
                    User = "ContainerAdministrator",
                    Healthcheck = new { Test = new[] { "NONE" } },
                    Labels = new Dictionary<string, string>
                    {
                        [HcsContainerRuntimeConstants.OwnerLabel] = ownerLabel,
                        [HcsContainerRuntimeConstants.ExecutionLabel] =
                            "60000000-0000-0000-0000-000000000006",
                        [HcsContainerRuntimeConstants.PolicyLabel] = Hash('d'),
                        [HcsContainerRuntimeConstants.RuntimeLabel] =
                            HcsContainerRuntimeConstants.RuntimeLabelValue
                    }
                },
                HostConfig = new
                {
                    Isolation = drift == "isolation" ? "process" : "hyperv",
                    NetworkMode = drift == "network" ? "nat" : "none",
                    CpuCount = drift == "resource" ? 3 : 2,
                    Memory = 4096L * 1024 * 1024,
                    StorageOpt = new Dictionary<string, string>
                    {
                        ["size"] = (12L * 1024 * 1024 * 1024).ToString()
                    },
                    RestartPolicy = new { Name = "no" }
                },
                Mounts = new[]
                {
                    new
                    {
                        Type = "bind",
                        Source = files.SourcePath,
                        Destination = HcsContainerRuntimeConstants.SourceContainerPath,
                        RW = drift == "writable-mount"
                    },
                    new
                    {
                        Type = "bind",
                        Source = files.FeedPath,
                        Destination = HcsContainerRuntimeConstants.FeedContainerPath,
                        RW = false
                    },
                    new
                    {
                        Type = "bind",
                        Source = Path.Combine(files.FeedPath, "NuGet.Config"),
                        Destination = HcsContainerRuntimeConstants.NuGetConfigContainerPath,
                        RW = false
                    }
                },
                NetworkSettings = drift == "missing-network-topology" ? null : new { Networks = networks }
            });
        }

        private static string OwnershipInspectionJson(string id, RecoveryLabels labels) =>
            JsonSerializer.Serialize(new
            {
                Id = id,
                Created = "2026-07-21T01:02:03Z",
                Config = new
                {
                    Labels = new Dictionary<string, string>
                    {
                        [HcsContainerRuntimeConstants.OwnerLabel] = labels.Owner,
                        [HcsContainerRuntimeConstants.RuntimeLabel] = labels.Runtime,
                        [HcsContainerRuntimeConstants.ExecutionLabel] = labels.ExecutionId.ToString("D")
                    }
                }
            });
    }

    private sealed class SandboxPolicyFiles : IDisposable
    {
        private SandboxPolicyFiles(
            string rootPath,
            string runtimePath,
            string feedPath,
            string feedManifestSha256)
        {
            RootPath = rootPath;
            RuntimePath = runtimePath;
            FeedPath = feedPath;
            FeedManifestSha256 = feedManifestSha256;
        }

        public string RootPath { get; }
        public string RuntimePath { get; }
        public string FeedPath { get; }
        public string FeedManifestSha256 { get; }

        public static SandboxPolicyFiles Create()
        {
            var root = Directory.CreateTempSubdirectory("irondev-pr06a-policy-").FullName;
            var runtimePath = Path.Combine(root, "trusted-runtime.exe");
            File.WriteAllBytes(runtimePath, [0x4d, 0x5a]);

            const string package = "deterministic offline package fixture";
            var packageHash = SandboxCanonicalJson.Sha256(package);
            var manifest =
                "{\"schemaVersion\":1,\"packages\":[{\"relativePath\":\"packages/test.1.0.0.nupkg\"," +
                $"\"lengthBytes\":{package.Length},\"sha256\":\"{packageHash}\"}}]}}";
            var manifestHash = SandboxCanonicalJson.Sha256(manifest);
            var feedPath = Directory.CreateDirectory(Path.Combine(root, manifestHash)).FullName;
            var packagesPath = Directory.CreateDirectory(Path.Combine(feedPath, "packages")).FullName;
            File.WriteAllText(Path.Combine(packagesPath, "test.1.0.0.nupkg"), package);
            File.WriteAllText(
                Path.Combine(feedPath, "NuGet.Config"),
                "<configuration><packageSources><clear/><add key=\"offline\" value=\"C:\\IronDev\\Feed\"/>" +
                "</packageSources></configuration>");
            File.WriteAllText(
                Path.Combine(feedPath, WindowsSandboxPolicyCatalog.OfflineFeedManifestFileName),
                manifest);

            return new SandboxPolicyFiles(root, runtimePath, feedPath, manifestHash);
        }

        public WindowsSandboxPolicyCatalog CreateCatalog(string? imageReference = null) => new(
            new WindowsSandboxOptions
            {
                Enabled = true,
                RuntimeExecutablePath = RuntimePath,
                ContainerImageDigestReference = imageReference ??
                    "mcr.microsoft.com/dotnet/sdk@sha256:" + Hash('c'),
                OfflineFeedPath = FeedPath,
                OfflineFeedManifestSha256 = FeedManifestSha256
            },
            new WindowsSandboxHostPlatform(IsWindows: true, Architecture.X64));

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the IronDev repository root.");
    }
}
