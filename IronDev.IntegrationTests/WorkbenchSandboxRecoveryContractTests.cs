using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
using IronDev.Infrastructure.Services.Sandbox;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("WorkbenchSandbox")]
public sealed class WorkbenchSandboxRecoveryContractTests
{
    [TestMethod]
    public async Task CompletedEvidenceRecovery_ReturnsTheCanonicalResultWithoutExecutingAgain()
    {
        using var files = RecoveryEvidenceFiles.Create();
        var runtime = new RecoveryRuntime(files.EvidencePath, files.ManifestJson);
        var service = new HcsHyperVSandboxExecutionService(
            runtime,
            new UnusedPolicyCatalog(),
            new UnusedProfileCatalog());

        var result = await service.TryRecoverCompletedAsync(files.Request);

        Assert.IsNotNull(result);
        Assert.AreEqual(files.Request.ExecutionId, result.ExecutionId);
        Assert.AreEqual(files.ManifestJson, result.EvidenceManifestJson);
        Assert.AreEqual(SandboxCanonicalJson.Sha256(files.ManifestJson), result.EvidenceManifestSha256);
        Assert.AreEqual(0, runtime.ExecuteCalls);
        Assert.IsTrue(File.Exists(Path.Combine(files.EvidencePath, "sandbox-evidence-manifest.json")));
    }

    [TestMethod]
    public async Task CompletedEvidenceRecovery_RejectsAuthorityDriftWithoutExecutingOrDeletingEvidence()
    {
        using var files = RecoveryEvidenceFiles.Create();
        var runtime = new RecoveryRuntime(files.EvidencePath, files.ManifestJson);
        var service = new HcsHyperVSandboxExecutionService(
            runtime,
            new UnusedPolicyCatalog(),
            new UnusedProfileCatalog());

        await Assert.ThrowsAsync<SandboxContractValidationException>(() =>
            service.TryRecoverCompletedAsync(files.Request with
            {
                RepositoryBindingRevision = files.Request.RepositoryBindingRevision + 1
            }));

        Assert.AreEqual(0, runtime.ExecuteCalls);
        Assert.IsTrue(File.Exists(Path.Combine(files.EvidencePath, "sandbox-evidence-manifest.json")));
    }

    [TestMethod]
    public async Task CompletedEvidenceRecovery_RejectsDurableSupervisorIdentityDrift()
    {
        using var files = RecoveryEvidenceFiles.Create();
        var runtime = new RecoveryRuntime(files.EvidencePath, files.ManifestJson);
        var service = new HcsHyperVSandboxExecutionService(
            runtime,
            new UnusedPolicyCatalog(),
            new UnusedProfileCatalog());

        await Assert.ThrowsAsync<SandboxContractValidationException>(() =>
            service.TryRecoverCompletedAsync(files.Request with
            {
                TrustedSupervisorVersion = files.Request.TrustedSupervisorVersion + "-drift"
            }));
        await Assert.ThrowsAsync<SandboxContractValidationException>(() =>
            service.TryRecoverCompletedAsync(files.Request with
            {
                TrustedSupervisorSha256 = new string('2', 64)
            }));

        Assert.AreEqual(0, runtime.ExecuteCalls);
        Assert.IsTrue(File.Exists(Path.Combine(files.EvidencePath, "sandbox-evidence-manifest.json")));
    }

    [TestMethod]
    public async Task CompletedEvidenceRecovery_RehashesEveryArtifactBeforeMaterialization()
    {
        using var files = RecoveryEvidenceFiles.Create();
        var runtime = new RecoveryRuntime(files.EvidencePath, files.ManifestJson);
        var service = new HcsHyperVSandboxExecutionService(
            runtime,
            new UnusedPolicyCatalog(),
            new UnusedProfileCatalog());
        File.AppendAllText(Path.Combine(files.EvidencePath, "restore.stdout.log"), "tampered");

        await Assert.ThrowsAsync<SandboxContractValidationException>(() =>
            service.TryRecoverCompletedAsync(files.Request));

        Assert.AreEqual(0, runtime.ExecuteCalls);
    }

    [TestMethod]
    public void RecoveryCoordinator_IsBoundedAppLockFencedAndNeverTerminalizesUnconfirmedCleanup()
    {
        var source = Read("IronDev.Infrastructure/Services/Sandbox/WorkbenchSandboxRecoveryService.cs");

        StringAssert.Contains(source, "TOP (@MaximumCandidates)");
        StringAssert.Contains(source, "sys.sp_getapplock");
        StringAssert.Contains(source, "@LockOwner=N'Session'");
        StringAssert.Contains(source, "CASE WHEN LastRecoveryAttemptAtUtc IS NULL THEN 0 ELSE 1 END");
        StringAssert.Contains(source, "LastRecoveryAttemptAtUtc, StartedAtUtc, AttemptNumber, Id");
        StringAssert.Contains(source, "SET LastRecoveryAttemptAtUtc=SYSUTCDATETIME()");
        StringAssert.Contains(source, "if (!outcome.CleanupConfirmed)");
        StringAssert.Contains(source, "WHERE State=N'Running'");
        StringAssert.Contains(source, "AND State=N'Running';");
        StringAssert.Contains(source, "ResultSandboxQualificationAttemptId=@AttemptId");
    }

    [TestMethod]
    public void ExistingPendingOperation_IsRecoveredOrMaterializedAndNeverSilentlyReexecuted()
    {
        var source = Read("IronDev.Infrastructure/Services/Sandbox/WorkbenchSandboxQualificationService.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var completedEvidenceRecovery = Between(
            source,
            "if (claim.IsExistingPending)",
            "completedEvidenceRecovered = execution is not null;");
        var executionNullBranch = Between(
            source,
            "if (execution is null)",
            "\n            catch (Exception exception)");
        var normalizedExecutionNullBranch = executionNullBranch.Replace("\r\n", "\n", StringComparison.Ordinal);
        var freshExecutionStart = normalizedExecutionNullBranch.IndexOf(
            "else\n                    {\n                        snapshot = await _snapshots.CreateOrRecoverAsync(",
            StringComparison.Ordinal);

        StringAssert.Contains(completedEvidenceRecovery, "TryRecoverCompletedAsync");
        Assert.IsFalse(completedEvidenceRecovery.Contains("ExecuteAsync", StringComparison.Ordinal));
        Assert.IsTrue(freshExecutionStart > 0);
        var pendingRecovery = normalizedExecutionNullBranch[..freshExecutionStart];
        StringAssert.Contains(pendingRecovery, "pendingAttemptRecovered = true;");
        StringAssert.Contains(pendingRecovery, "RecoverExecutionAsync");
        Assert.IsFalse(pendingRecovery.Contains("_sandbox.ExecuteAsync", StringComparison.Ordinal));
        StringAssert.Contains(normalizedExecutionNullBranch[freshExecutionStart..], "_sandbox.ExecuteAsync");
        StringAssert.Contains(source, "IsExistingPending: false");
        StringAssert.Contains(source, "IsExistingPending: true");
        StringAssert.Contains(source, "throw new SandboxQualificationInProgressException();");
    }

    [TestMethod]
    public void QualificationReceiptQueries_SeparateCurrentSelectionFromExactAttemptRecoveryAuthority()
    {
        var source = Read("IronDev.Infrastructure/Services/Sandbox/WorkbenchSandboxQualificationService.cs");
        var currentReceiptQuery = Between(
            source,
            "private static Task<ProvisioningReceiptRow?> ReadProvisioningReceiptAsync(",
            "private static Task<ProvisioningReceiptRow?> ReadProvisioningReceiptForAttemptAsync(");
        var attemptReceiptQuery = Between(
            source,
            "private static Task<ProvisioningReceiptRow?> ReadProvisioningReceiptForAttemptAsync(",
            "private static Task<ClientOperationRow?> ReadOperationAsync(");

        StringAssert.Contains(currentReceiptQuery, "SELECT TOP (1) Id, BaselineCommit, ManifestSha256, GitTreeId, ManifestJson");
        StringAssert.Contains(currentReceiptQuery, "AND RepositoryBindingId=@RepositoryBindingId");
        StringAssert.Contains(currentReceiptQuery, "AND ProjectExecutionProfileId=@ProjectExecutionProfileId");
        StringAssert.Contains(currentReceiptQuery, "AND BaselineCommit=@BaselineCommit");
        StringAssert.Contains(currentReceiptQuery, "ORDER BY RecordedAtUtc DESC, Id DESC;");
        Assert.IsFalse(currentReceiptQuery.Contains("@RepositoryProvisioningReceiptId", StringComparison.Ordinal));
        Assert.IsFalse(currentReceiptQuery.Contains("@SourceManifestSha256", StringComparison.Ordinal));
        Assert.IsFalse(currentReceiptQuery.Contains("@SourceGitTreeId", StringComparison.Ordinal));

        StringAssert.Contains(attemptReceiptQuery, "WHERE Id=@RepositoryProvisioningReceiptId");
        StringAssert.Contains(attemptReceiptQuery, "AND RepositoryBindingId=@RepositoryBindingId");
        StringAssert.Contains(attemptReceiptQuery, "AND ProjectExecutionProfileId=@ProjectExecutionProfileId");
        StringAssert.Contains(attemptReceiptQuery, "AND BaselineCommit=@BaselineCommit");
        StringAssert.Contains(attemptReceiptQuery, "AND ManifestSha256=@SourceManifestSha256");
        StringAssert.Contains(attemptReceiptQuery, "AND GitTreeId=@SourceGitTreeId;");
        Assert.IsFalse(attemptReceiptQuery.Contains("TOP (1)", StringComparison.Ordinal));
        Assert.IsFalse(attemptReceiptQuery.Contains("ORDER BY", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DurableSupervisorAuthority_IsInsertedReadAndRequiredByBothRecoveryCoordinators()
    {
        var migration = Read("Database/migrate_workbench_sandbox_qualification.sql");
        var qualification = Read(
            "IronDev.Infrastructure/Services/Sandbox/WorkbenchSandboxQualificationService.cs");
        var recovery = Read(
            "IronDev.Infrastructure/Services/Sandbox/WorkbenchSandboxRecoveryService.cs");
        var verifier = Read("Database/verify-migrations.ps1");

        StringAssert.Contains(migration, "TrustedSupervisorVersion NVARCHAR(100) NOT NULL");
        StringAssert.Contains(migration, "TrustedSupervisorSha256 CHAR(64) NOT NULL");
        StringAssert.Contains(migration, "CK_SandboxQualificationAttempts_TrustedSupervisorVersion");
        StringAssert.Contains(migration, "CK_SandboxQualificationAttempts_TrustedSupervisorSha256");
        StringAssert.Contains(migration, "UPDATE(TrustedSupervisorVersion)");
        StringAssert.Contains(migration, "UPDATE(TrustedSupervisorSha256)");
        StringAssert.Contains(
            migration,
            "Existing sandbox attempts cannot be assigned an inferred trusted-supervisor authority.");

        var attemptInsert = Between(
            qualification,
            "INSERT dbo.SandboxQualificationAttempts",
            "await InsertOutboxAsync(");
        StringAssert.Contains(attemptInsert, "TrustedSupervisorVersion, TrustedSupervisorSha256");
        StringAssert.Contains(attemptInsert, "@TrustedSupervisorVersion, @TrustedSupervisorSha256");
        StringAssert.Contains(attemptInsert, "resolution.Policy.TrustedSupervisorVersion");
        StringAssert.Contains(attemptInsert, "resolution.Policy.TrustedSupervisorSha256");
        var attemptRead = Between(
            qualification,
            "private static Task<AttemptRow?> ReadAttemptByOperationAsync(",
            "private static Task<AttemptRow?> ReadLatestAttemptAsync(");
        StringAssert.Contains(attemptRead, "TrustedSupervisorVersion, TrustedSupervisorSha256");
        StringAssert.Contains(qualification, "TrustedSupervisorVersion: claim.Policy.TrustedSupervisorVersion");
        StringAssert.Contains(qualification, "TrustedSupervisorSha256: claim.Policy.TrustedSupervisorSha256");
        StringAssert.Contains(
            qualification,
            "execution.EvidenceManifest.TrustedSupervisorVersion");
        StringAssert.Contains(
            qualification,
            "execution.EvidenceManifest.TrustedSupervisorSha256");
        StringAssert.Contains(
            qualification,
            "execution.EvidenceManifest.Inspection.TrustedSupervisorVersion");
        StringAssert.Contains(
            qualification,
            "execution.EvidenceManifest.Inspection.TrustedSupervisorSha256");

        var recoveryClaimRead = Between(
            recovery,
            "private static async Task<RecoveryClaim?> ReadClaimAsync(",
            "private SandboxCompletedEvidenceRecoveryRequest CompletedEvidenceRequest(");
        StringAssert.Contains(
            recoveryClaimRead,
            "attempt.SandboxPolicySha256, attempt.TrustedSupervisorVersion");
        StringAssert.Contains(recoveryClaimRead, "attempt.TrustedSupervisorSha256");
        var recoveryCompletion = Between(
            recovery,
            "private async Task CompleteAsync(",
            "private async Task<IReadOnlyList<RecoveryCandidate>> ReadCandidatesAsync(");
        StringAssert.Contains(
            recoveryCompletion,
            "attempt.TrustedSupervisorVersion, attempt.TrustedSupervisorSha256");
        StringAssert.Contains(recovery, "TrustedSupervisorVersion: claim.TrustedSupervisorVersion");
        StringAssert.Contains(recovery, "TrustedSupervisorSha256: claim.TrustedSupervisorSha256");
        StringAssert.Contains(recovery, "TrustedSupervisorVersion = claim.TrustedSupervisorVersion");
        StringAssert.Contains(recovery, "TrustedSupervisorSha256 = claim.TrustedSupervisorSha256");
        StringAssert.Contains(
            recovery,
            "execution.EvidenceManifest.TrustedSupervisorVersion");
        StringAssert.Contains(
            recovery,
            "execution.EvidenceManifest.TrustedSupervisorSha256");
        StringAssert.Contains(
            recovery,
            "execution.EvidenceManifest.Inspection.TrustedSupervisorVersion");
        StringAssert.Contains(
            recovery,
            "execution.EvidenceManifest.Inspection.TrustedSupervisorSha256");

        StringAssert.Contains(verifier, "Sandbox durable trusted-supervisor authority columns");
        StringAssert.Contains(verifier, "Sandbox trusted-supervisor version constraint");
        StringAssert.Contains(verifier, "Sandbox trusted-supervisor hash constraint");
    }

    [TestMethod]
    public void CompletedEvidenceRecovery_UsesTheDurableSupervisorIdentityNotPolicyHashAlone()
    {
        var source = Read(
            "IronDev.Infrastructure/Services/Sandbox/HcsHyperVSandboxExecutionService.cs");
        var validation = Between(
            source,
            "private static void ValidateRecoveryRequest(",
            "private static void ValidateRecoveredManifest(");
        var manifestValidation = Between(
            source,
            "private static void ValidateRecoveredManifest(",
            "private static bool ArtifactEquals(");

        StringAssert.Contains(
            validation,
            "request.TrustedSupervisorVersion");
        StringAssert.Contains(
            validation,
            "request.TrustedSupervisorSha256");
        StringAssert.Contains(
            manifestValidation,
            "manifest.TrustedSupervisorVersion, request.TrustedSupervisorVersion");
        StringAssert.Contains(
            manifestValidation,
            "manifest.TrustedSupervisorSha256, request.TrustedSupervisorSha256");
        StringAssert.Contains(
            manifestValidation,
            "inspection.TrustedSupervisorVersion, request.TrustedSupervisorVersion");
        StringAssert.Contains(
            manifestValidation,
            "inspection.TrustedSupervisorSha256, request.TrustedSupervisorSha256");
    }

    [TestMethod]
    public void CanonicalPolicyHash_BindsSupervisorVersionAndEveryCompositeArtifactByte()
    {
        var independentlyComputed = SandboxCanonicalJson.Sha256(
            $"{WindowsJobSupervisorContract.BootstrapSha256}:" +
            $"{WindowsJobSupervisorContract.LoaderSha256}:" +
            WindowsJobSupervisorContract.SourceSha256);
        Assert.AreEqual(WindowsJobSupervisorContract.Sha256, independentlyComputed);

        var policy = PolicyForSupervisor(
            WindowsJobSupervisorContract.Version,
            WindowsJobSupervisorContract.Sha256);
        var versionChanged = policy with
        {
            TrustedSupervisorVersion = policy.TrustedSupervisorVersion + "-changed"
        };
        Assert.AreNotEqual(
            SandboxRuntimePolicyCodec.ComputeHash(policy),
            SandboxRuntimePolicyCodec.ComputeHash(versionChanged));

        var components = new[]
        {
            (Bytes: WindowsJobSupervisorContract.GetBootstrapBytes(), Index: 0),
            (Bytes: WindowsJobSupervisorContract.GetLoaderBytes(), Index: 1),
            (Bytes: WindowsJobSupervisorContract.GetSourceBytes(), Index: 2)
        };
        foreach (var component in components)
        {
            var changedBytes = component.Bytes.ToArray();
            changedBytes[0] ^= 0x01;
            var changedHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(changedBytes)).ToLowerInvariant();
            var hashes = new[]
            {
                WindowsJobSupervisorContract.BootstrapSha256,
                WindowsJobSupervisorContract.LoaderSha256,
                WindowsJobSupervisorContract.SourceSha256
            };
            hashes[component.Index] = changedHash;
            var changedCompositeSha = SandboxCanonicalJson.Sha256(
                $"{hashes[0]}:{hashes[1]}:{hashes[2]}");
            Assert.AreNotEqual(WindowsJobSupervisorContract.Sha256, changedCompositeSha);
            Assert.AreNotEqual(
                SandboxRuntimePolicyCodec.ComputeHash(policy),
                SandboxRuntimePolicyCodec.ComputeHash(policy with
                {
                    TrustedSupervisorSha256 = changedCompositeSha
                }));
        }
    }

    [TestMethod]
    public async Task LowLevelRecovery_RejectsSupervisorDriftBeforeEvidenceReadOrContainerDelete()
    {
        using var files = RecoveryEvidenceFiles.Create();
        var dockerPath = Path.Combine(files.RootPath, "docker.exe");
        File.WriteAllBytes(dockerPath, [0x4d, 0x5a]);
        var dockerConfig = Directory.CreateDirectory(Path.Combine(files.RootPath, "docker-config")).FullName;
        var evidenceRoot = Path.Combine(files.RootPath, "evidence");
        var runner = new CountingDockerRunner();
        var runtime = new DockerHcsContainerRuntime(
            new HcsContainerRuntimeConfiguration
            {
                DockerExecutablePath = dockerPath,
                DockerEngineHost = HcsContainerRuntimeConstants.DockerEngineHost,
                DockerConfigDirectory = dockerConfig,
                OwnerLabelValue = "irondev-recovery-authority-tests",
                EvidenceRootPath = evidenceRoot,
                DockerControlCommandTimeout = TimeSpan.FromSeconds(5),
                ContainerInitializationTimeout = TimeSpan.FromSeconds(5)
            },
            runner);
        var manifestPath = Path.Combine(files.EvidencePath, "sandbox-evidence-manifest.json");
        var manifestBefore = File.ReadAllText(manifestPath);
        var driftedAuthorities = new[]
        {
            (Version: WindowsJobSupervisorContract.Version + "-drift",
             Sha256: WindowsJobSupervisorContract.Sha256),
            (Version: WindowsJobSupervisorContract.Version,
             Sha256: FixedHash('2'))
        };

        foreach (var authority in driftedAuthorities)
        {
            var evidenceException = await Assert.ThrowsAsync<HcsContainerRuntimeException>(() =>
                runtime.TryReadCompletedEvidenceAsync(new HcsCompletedEvidenceRequest(
                    files.Request.ExecutionId,
                    files.Request.SandboxPolicySha256,
                    authority.Version,
                    authority.Sha256,
                    files.EvidencePath)));
            Assert.AreEqual(SandboxReasonCodes.ExecutionRejected, evidenceException.ReasonCode);
            StringAssert.Contains(evidenceException.Message, "trusted workload supervisor authority");

            var cleanupException = await Assert.ThrowsAsync<HcsContainerRuntimeException>(() =>
                runtime.RecoverExecutionAsync(new HcsExecutionCleanupRequest(
                    files.Request.ExecutionId,
                    files.Request.SandboxPolicySha256,
                    authority.Version,
                    authority.Sha256,
                    files.EvidencePath)));
            Assert.AreEqual(SandboxReasonCodes.ExecutionRejected, cleanupException.ReasonCode);
            StringAssert.Contains(cleanupException.Message, "trusted workload supervisor authority");
        }

        Assert.AreEqual(0, runner.Calls);
        Assert.IsTrue(File.Exists(manifestPath));
        Assert.AreEqual(manifestBefore, File.ReadAllText(manifestPath));
    }

    [TestMethod]
    public async Task LowLevelRecovery_CleansOnlyExactSupervisorStagingResiduals()
    {
        using var files = RecoveryEvidenceFiles.Create();
        var runner = new AbsentContainerDockerRunner();
        var runtime = CreateRecoveryDockerRuntime(files, runner);
        var evidencePath = Path.Combine(files.RootPath, "evidence", "cleanup-target");
        var stagingPath = SupervisorStagingPath(files, files.Request.ExecutionId);
        var ownerPath = stagingPath + WindowsJobSupervisorContract.HostStagingOwnerSuffix;
        void WriteExactResidual()
        {
            WriteSupervisorOwner(ownerPath, files.Request);
            Directory.CreateDirectory(stagingPath);
            File.WriteAllBytes(
                Path.Combine(stagingPath, WindowsJobSupervisorContract.LoaderFileName),
                WindowsJobSupervisorContract.GetLoaderBytes());
            File.WriteAllBytes(
                Path.Combine(stagingPath, WindowsJobSupervisorContract.SourceFileName),
                WindowsJobSupervisorContract.GetSourceBytes());
        }

        WriteExactResidual();
        var recoveredEvidence = await runtime.TryReadCompletedEvidenceAsync(new HcsCompletedEvidenceRequest(
            files.Request.ExecutionId,
            files.Request.SandboxPolicySha256,
            files.Request.TrustedSupervisorVersion,
            files.Request.TrustedSupervisorSha256,
            evidencePath));
        Assert.IsNull(recoveredEvidence);
        Assert.IsFalse(Directory.Exists(stagingPath));
        Assert.IsFalse(File.Exists(ownerPath));

        WriteExactResidual();

        var cleaned = await runtime.RecoverExecutionAsync(new HcsExecutionCleanupRequest(
            files.Request.ExecutionId,
            files.Request.SandboxPolicySha256,
            files.Request.TrustedSupervisorVersion,
            files.Request.TrustedSupervisorSha256,
            evidencePath));

        Assert.IsTrue(cleaned.ContainerCleanupConfirmed);
        Assert.IsTrue(cleaned.EvidenceCleanupConfirmed);
        Assert.IsFalse(Directory.Exists(stagingPath));
        Assert.IsFalse(File.Exists(ownerPath));

        WriteSupervisorOwner(ownerPath, files.Request);
        File.WriteAllText(stagingPath, "unowned collision");
        var refused = await runtime.RecoverExecutionAsync(new HcsExecutionCleanupRequest(
            files.Request.ExecutionId,
            files.Request.SandboxPolicySha256,
            files.Request.TrustedSupervisorVersion,
            files.Request.TrustedSupervisorSha256,
            evidencePath));

        Assert.IsFalse(refused.EvidenceCleanupConfirmed);
        Assert.IsTrue(File.Exists(stagingPath));
        Assert.IsTrue(File.Exists(ownerPath));

        File.Delete(stagingPath);
        File.Delete(ownerPath);
        WriteSupervisorOwner(ownerPath, files.Request);
        Directory.CreateDirectory(stagingPath);
        var alteredLoader = WindowsJobSupervisorContract.GetLoaderBytes();
        alteredLoader[0] ^= 0x01;
        File.WriteAllBytes(
            Path.Combine(stagingPath, WindowsJobSupervisorContract.LoaderFileName),
            alteredLoader);
        File.WriteAllBytes(
            Path.Combine(stagingPath, WindowsJobSupervisorContract.SourceFileName),
            WindowsJobSupervisorContract.GetSourceBytes());

        await Assert.ThrowsAsync<HcsContainerRuntimeException>(() =>
            runtime.TryReadCompletedEvidenceAsync(new HcsCompletedEvidenceRequest(
                files.Request.ExecutionId,
                files.Request.SandboxPolicySha256,
                files.Request.TrustedSupervisorVersion,
                files.Request.TrustedSupervisorSha256,
                evidencePath)));
        Assert.IsTrue(Directory.Exists(stagingPath));
        Assert.IsTrue(File.Exists(ownerPath));
        Assert.IsTrue(File.Exists(Path.Combine(
            stagingPath,
            WindowsJobSupervisorContract.LoaderFileName)));

        var source = Read(
            "IronDev.Infrastructure/Services/Sandbox/DockerHcsContainerRuntime.cs");
        var exactCleanup = Between(
            source,
            "private bool CleanupSupervisorStagingArtifacts(",
            "private async Task<SupervisorPreflightProof> RunSupervisorPreflightAsync(");
        Assert.IsFalse(exactCleanup.Contains("recursive: true", StringComparison.Ordinal));
        StringAssert.Contains(exactCleanup, "File.Delete(entry)");
        StringAssert.Contains(exactCleanup, "Directory.Delete(stagingPath, recursive: false)");
        StringAssert.Contains(exactCleanup, "File.Exists(stagingPath)");
        StringAssert.Contains(exactCleanup, "FileInfo(entry)");
    }

    [TestMethod]
    public async Task LowLevelRecovery_CleansOnlyBoundedPendingSupervisorStagingResiduals()
    {
        using var files = RecoveryEvidenceFiles.Create();
        var runner = new AbsentContainerDockerRunner();
        var runtime = CreateRecoveryDockerRuntime(files, runner);
        var evidencePath = Path.Combine(files.RootPath, "evidence", "pending-cleanup-target");
        var stagingPath = SupervisorStagingPath(files, files.Request.ExecutionId);
        var ownerPath = stagingPath + WindowsJobSupervisorContract.HostStagingOwnerSuffix;
        var ownerPendingPath = ownerPath + ".pending";

        File.WriteAllText(ownerPendingPath, "{\"ExecutionId\":");
        var ownerPendingCleaned = await runtime.RecoverExecutionAsync(new HcsExecutionCleanupRequest(
            files.Request.ExecutionId,
            files.Request.SandboxPolicySha256,
            files.Request.TrustedSupervisorVersion,
            files.Request.TrustedSupervisorSha256,
            evidencePath));

        Assert.IsTrue(ownerPendingCleaned.ContainerCleanupConfirmed);
        Assert.IsTrue(ownerPendingCleaned.EvidenceCleanupConfirmed);
        Assert.IsFalse(File.Exists(ownerPendingPath));

        WriteSupervisorOwner(ownerPath, files.Request);
        Directory.CreateDirectory(stagingPath);
        File.WriteAllBytes(
            Path.Combine(stagingPath, WindowsJobSupervisorContract.LoaderFileName + ".pending"),
            WindowsJobSupervisorContract.GetLoaderBytes().Take(17).ToArray());
        File.WriteAllBytes(
            Path.Combine(stagingPath, WindowsJobSupervisorContract.SourceFileName + ".pending"),
            WindowsJobSupervisorContract.GetSourceBytes().Take(23).ToArray());

        var stagedPendingCleaned = await runtime.RecoverExecutionAsync(new HcsExecutionCleanupRequest(
            files.Request.ExecutionId,
            files.Request.SandboxPolicySha256,
            files.Request.TrustedSupervisorVersion,
            files.Request.TrustedSupervisorSha256,
            evidencePath));

        Assert.IsTrue(stagedPendingCleaned.ContainerCleanupConfirmed);
        Assert.IsTrue(stagedPendingCleaned.EvidenceCleanupConfirmed);
        Assert.IsFalse(Directory.Exists(stagingPath));
        Assert.IsFalse(File.Exists(ownerPath));

        File.WriteAllBytes(ownerPendingPath, new byte[4097]);
        var oversizedOwnerRefused = await runtime.RecoverExecutionAsync(new HcsExecutionCleanupRequest(
            files.Request.ExecutionId,
            files.Request.SandboxPolicySha256,
            files.Request.TrustedSupervisorVersion,
            files.Request.TrustedSupervisorSha256,
            evidencePath));

        Assert.IsFalse(oversizedOwnerRefused.EvidenceCleanupConfirmed);
        Assert.IsTrue(File.Exists(ownerPendingPath));
        File.Delete(ownerPendingPath);

        WriteSupervisorOwner(ownerPath, files.Request);
        Directory.CreateDirectory(stagingPath);
        File.WriteAllBytes(
            Path.Combine(stagingPath, WindowsJobSupervisorContract.LoaderFileName + ".pending"),
            new byte[WindowsJobSupervisorContract.GetLoaderBytes().Length + 1]);
        var oversizedArtifactRefused = await runtime.RecoverExecutionAsync(new HcsExecutionCleanupRequest(
            files.Request.ExecutionId,
            files.Request.SandboxPolicySha256,
            files.Request.TrustedSupervisorVersion,
            files.Request.TrustedSupervisorSha256,
            evidencePath));

        Assert.IsFalse(oversizedArtifactRefused.EvidenceCleanupConfirmed);
        Assert.IsTrue(Directory.Exists(stagingPath));
        Assert.IsTrue(File.Exists(ownerPath));
        Assert.IsTrue(File.Exists(Path.Combine(
            stagingPath,
            WindowsJobSupervisorContract.LoaderFileName + ".pending")));
    }

    [TestMethod]
    public void RecoveryWorker_IsStartupWiredAndRepeatsBoundedPasses()
    {
        var program = Read("IronDev.Api/Program.cs");
        var worker = Read("IronDev.Api/WorkbenchSandboxRecoveryWorker.cs");

        StringAssert.Contains(program, "AddHostedService<WorkbenchSandboxRecoveryWorker>()");
        Assert.IsFalse(program.Contains(
            "if (workbenchSandboxOptions.Enabled)\n    builder.Services.AddHostedService<WorkbenchSandboxRecoveryWorker>()",
            StringComparison.Ordinal));
        StringAssert.Contains(program, "production Windows sandbox cleanup runtime configuration");
        StringAssert.Contains(worker, "RecoverStaleAttemptsAsync(_batchSize");
        StringAssert.Contains(worker, "Task.Delay(_interval");
    }

    private static string Between(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, startIndex);
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.IsGreaterThan(startIndex, endIndex);
        return value[startIndex..endIndex];
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static SandboxRuntimePolicy PolicyForSupervisor(string version, string sha256)
    {
        var resources = SandboxResourcePolicy.WorkbenchV01;
        static SandboxCommandPolicy Command(
            SandboxExecutionStage stage,
            string text,
            int timeout) => new(stage, text, SandboxCanonicalJson.Sha256(text), timeout);
        return new SandboxRuntimePolicy
        {
            SchemaVersion = 1,
            PolicyVersion = SandboxPolicyVersions.WorkbenchV01,
            IsolationMode = SandboxIsolationModes.HcsHyperV,
            ProfileDefinitionId = "greenfield-winforms-net10-mstest-v1",
            ProfileDescriptorRevision = 1,
            DescriptorSha256 = FixedHash('a'),
            TemplateBundleSha256 = FixedHash('b'),
            ToolchainManifestId = "dotnet-sdk-10.0.302-runtime-10.0.10-v1",
            ContainerImageReference = "mcr.microsoft.com/dotnet/sdk@sha256:" + FixedHash('c'),
            ContainerImageDigest = FixedHash('c'),
            OfflineFeedPath = @"C:\IronDev\Feed",
            OfflineFeedManifestSha256 = FixedHash('d'),
            RepositoryInputReadOnly = true,
            OfflineFeedReadOnly = true,
            TrustedSupervisorVersion = version,
            TrustedSupervisorSha256 = sha256,
            Resources = resources,
            Restore = Command(SandboxExecutionStage.Restore, "restore", resources.RestoreTimeoutSeconds),
            Build = Command(SandboxExecutionStage.Build, "build", resources.BuildTimeoutSeconds),
            Test = Command(SandboxExecutionStage.Test, "test", resources.TestTimeoutSeconds),
            EnvironmentAllowList = ["DOTNET_NOLOGO"],
            PolicySha256 = string.Empty
        };
    }

    private static string FixedHash(char value) => new(char.ToLowerInvariant(value), 64);

    private static DockerHcsContainerRuntime CreateRecoveryDockerRuntime(
        RecoveryEvidenceFiles files,
        IDockerCommandRunner runner)
    {
        var dockerPath = Path.Combine(files.RootPath, "docker.exe");
        if (!File.Exists(dockerPath))
            File.WriteAllBytes(dockerPath, [0x4d, 0x5a]);
        var dockerConfig = Directory.CreateDirectory(Path.Combine(files.RootPath, "docker-config")).FullName;
        return new DockerHcsContainerRuntime(
            new HcsContainerRuntimeConfiguration
            {
                DockerExecutablePath = dockerPath,
                DockerEngineHost = HcsContainerRuntimeConstants.DockerEngineHost,
                DockerConfigDirectory = dockerConfig,
                OwnerLabelValue = "irondev-recovery-authority-tests",
                EvidenceRootPath = Path.Combine(files.RootPath, "evidence"),
                DockerControlCommandTimeout = TimeSpan.FromSeconds(5),
                ContainerInitializationTimeout = TimeSpan.FromSeconds(5)
            },
            runner);
    }

    private static string SupervisorStagingPath(
        RecoveryEvidenceFiles files,
        Guid executionId) => Path.Combine(
        files.RootPath,
        "evidence",
        $".supervisor-{executionId:N}");

    private static void WriteSupervisorOwner(
        string ownerPath,
        SandboxCompletedEvidenceRecoveryRequest request)
    {
        File.WriteAllText(
            ownerPath,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                request.ExecutionId,
                PolicySha256 = request.SandboxPolicySha256,
                request.TrustedSupervisorVersion,
                request.TrustedSupervisorSha256,
                WindowsJobSupervisorContract.LoaderSha256,
                WindowsJobSupervisorContract.SourceSha256
            }));
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the IronDev repository root.");
    }

    private sealed class RecoveryRuntime(string evidencePath, string manifestJson) : IHcsContainerRuntime
    {
        public int ExecuteCalls { get; private set; }

        public Task<HcsContainerProbeResult> ProbeAsync(
            HcsContainerProbeRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<HcsContainerRuntimeResult> ExecuteAsync(
            HcsContainerRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            throw new NotSupportedException();
        }

        public Task<HcsCompletedEvidence?> TryReadCompletedEvidenceAsync(
            HcsCompletedEvidenceRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult<HcsCompletedEvidence?>(
            new HcsCompletedEvidence(evidencePath, manifestJson));

        public Task<HcsExecutionCleanupResult> RecoverExecutionAsync(
            HcsExecutionCleanupRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<HcsContainerRecoveryResult> RecoverOwnedContainersAsync(
            HcsContainerRecoveryRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CountingDockerRunner : IDockerCommandRunner
    {
        public int Calls { get; private set; }

        public Task<DockerCommandResult> RunAsync(
            DockerCommandRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException(
                "Supervisor authority drift must be rejected before invoking Docker.");
        }
    }

    private sealed class AbsentContainerDockerRunner : IDockerCommandRunner
    {
        public Task<DockerCommandResult> RunAsync(
            DockerCommandRequest request,
            CancellationToken cancellationToken = default)
        {
            var arguments = request.Arguments;
            var isInspect = arguments.Contains("container") && arguments.Contains("inspect");
            return Task.FromResult(new DockerCommandResult
            {
                ExitCode = isInspect ? 1 : 0,
                StandardOutput = string.Empty,
                StandardError = isInspect ? "No such container" : string.Empty,
                TimedOut = false,
                StandardOutputTruncated = false,
                StandardErrorTruncated = false,
                DurationMilliseconds = 1
            });
        }
    }

    private sealed class UnusedPolicyCatalog : ISandboxRuntimePolicyCatalog
    {
        public SandboxPolicyResolution Resolve(SandboxExecutionProfileBinding profile) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedProfileCatalog : IRepositorySetupProfileCatalog
    {
        public IReadOnlyList<RepositorySetupProfileDescriptor> GetAll() => [];

        public RepositorySetupProfileDescriptor? Find(
            string profileDefinitionId,
            int? revision = null,
            string? descriptorSha256 = null) => null;
    }

    private sealed class RecoveryEvidenceFiles : IDisposable
    {
        private RecoveryEvidenceFiles(string rootPath)
        {
            RootPath = rootPath;
            EvidencePath = Directory.CreateDirectory(Path.Combine(rootPath, "evidence", "attempt")).FullName;
            var artifactPath = Path.Combine(EvidencePath, "restore.stdout.log");
            File.WriteAllText(artifactPath, "bounded output");
            var artifact = new FileInfo(artifactPath);
            using var artifactStream = artifact.OpenRead();
            var artifactHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(artifactStream)).ToLowerInvariant();
            var executionId = Guid.Parse("91000000-0000-0000-0000-000000000001");
            var bindingId = Guid.Parse("92000000-0000-0000-0000-000000000002");
            var profileId = Guid.Parse("93000000-0000-0000-0000-000000000003");
            var started = new DateTimeOffset(2026, 7, 22, 1, 2, 3, TimeSpan.Zero);
            var manifest = new SandboxEvidenceManifest
            {
                SchemaVersion = 1,
                ExecutionId = executionId,
                ProjectId = 42,
                RepositoryBindingId = bindingId,
                RepositoryBindingRevision = 7,
                BaselineCommit = new string('a', 40),
                WorktreeFingerprint = Hash('b'),
                ProjectExecutionProfileId = profileId,
                ProjectExecutionProfileRevision = 9,
                ProfileDefinitionId = "greenfield-winforms-net10-mstest-v1",
                ProfileDescriptorRevision = 1,
                DescriptorSha256 = Hash('c'),
                TemplateBundleSha256 = Hash('d'),
                ToolchainManifestId = "dotnet-sdk-10.0.302-runtime-10.0.10-v1",
                ContainerImageDigest = Hash('e'),
                SandboxPolicyVersion = SandboxPolicyVersions.WorkbenchV01,
                SandboxPolicySha256 = Hash('f'),
                TrustedSupervisorVersion = WindowsJobSupervisorContract.Version,
                TrustedSupervisorSha256 = WindowsJobSupervisorContract.Sha256,
                OfflineFeedManifestSha256 = Hash('1'),
                Status = SandboxExecutionStatus.Succeeded,
                ReasonCode = SandboxReasonCodes.Ready,
                SafeSummary = "The isolated Windows container execution completed.",
                StartedAtUtc = started,
                CompletedAtUtc = started.AddMinutes(3),
                Inspection = new SandboxRuntimeInspection
                {
                    RuntimeName = HcsContainerRuntimeConstants.RuntimeName,
                    IsolationMode = SandboxIsolationModes.HcsHyperV,
                    ActualContainerImageDigest = Hash('e'),
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
                    InspectedAtUtc = started.AddMinutes(3)
                },
                Stages = [],
                Artifacts =
                [
                    new SandboxEvidenceArtifact
                    {
                        Kind = "CommandLog",
                        RelativePath = artifact.Name,
                        LengthBytes = artifact.Length,
                        ContentSha256 = artifactHash
                    }
                ]
            };
            ManifestJson = SandboxEvidenceManifestCodec.SerializeCanonical(manifest);
            File.WriteAllText(Path.Combine(EvidencePath, "sandbox-evidence-manifest.json"), ManifestJson);
            Request = new SandboxCompletedEvidenceRecoveryRequest
            {
                ExecutionId = executionId,
                ProjectId = manifest.ProjectId,
                RepositoryBindingId = bindingId,
                RepositoryBindingRevision = manifest.RepositoryBindingRevision,
                BaselineCommit = manifest.BaselineCommit,
                WorktreeFingerprint = manifest.WorktreeFingerprint,
                ProjectExecutionProfileId = profileId,
                ProjectExecutionProfileRevision = manifest.ProjectExecutionProfileRevision,
                ProfileDefinitionId = manifest.ProfileDefinitionId,
                ProfileDescriptorRevision = manifest.ProfileDescriptorRevision,
                DescriptorSha256 = manifest.DescriptorSha256,
                TemplateBundleSha256 = manifest.TemplateBundleSha256,
                ToolchainManifestId = manifest.ToolchainManifestId,
                ContainerImageReference = "mcr.microsoft.com/dotnet/sdk@sha256:" + manifest.ContainerImageDigest,
                SandboxPolicyVersion = manifest.SandboxPolicyVersion,
                SandboxPolicySha256 = manifest.SandboxPolicySha256,
                TrustedSupervisorVersion = manifest.Inspection.TrustedSupervisorVersion,
                TrustedSupervisorSha256 = manifest.Inspection.TrustedSupervisorSha256,
                OfflineFeedManifestSha256 = manifest.OfflineFeedManifestSha256,
                EvidenceOutputPath = EvidencePath
            };
        }

        public string RootPath { get; }
        public string EvidencePath { get; }
        public string ManifestJson { get; }
        public SandboxCompletedEvidenceRecoveryRequest Request { get; }

        public static RecoveryEvidenceFiles Create() => new(
            Directory.CreateTempSubdirectory("irondev-sandbox-recovery-").FullName);

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }

        private static string Hash(char value) => new(char.ToLowerInvariant(value), 64);
    }
}
