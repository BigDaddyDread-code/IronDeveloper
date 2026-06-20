using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBFPostDeployVerificationRollbackSeparationTests
{
    private static readonly string CandidateCommitSha = new('d', 40);
    private static readonly string ArtifactSha256 = new('b', 64);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockBF_Package_CreatesVerifiedPostDeployPackage()
    {
        var artifacts = Build();

        Assert.AreEqual(PostDeployVerificationPackageVerdict.DeploymentVerified, artifacts.Package.PackageVerdict);
        Assert.IsTrue(artifacts.Package.DeploymentVerified);
        Assert.IsFalse(artifacts.Package.CanProceedToRollbackDecision);
        Assert.AreEqual("deployment_exec_receipt_bf", artifacts.Package.SourceDeploymentExecutionReceiptId);
        Assert.AreEqual("owner/repo", artifacts.Package.Repository);
        Assert.AreEqual(CandidateCommitSha, artifacts.Package.CandidateCommitSha);
        Assert.AreEqual("1.2.3", artifacts.Package.CandidateVersion);
        Assert.AreEqual("v1.2.3", artifacts.Package.CandidateTagName);
        Assert.AreEqual("Stable", artifacts.Package.ReleaseChannel);
        Assert.AreEqual("production", artifacts.Package.DeploymentTarget);
        Assert.AreEqual("prod-west", artifacts.Package.DeploymentEnvironment);
        Assert.AreEqual("artifact.zip", artifacts.Package.ExpectedArtifactName);
        Assert.AreEqual(ArtifactSha256, artifacts.Package.ExpectedArtifactSha256);
        Assert.AreEqual("1.2.3", artifacts.Package.ObservedVersion);
        Assert.AreEqual(CandidateCommitSha, artifacts.Package.ObservedCommitSha);
        Assert.AreEqual("artifact.zip", artifacts.Package.ObservedArtifactName);
        Assert.AreEqual(ArtifactSha256, artifacts.Package.ObservedArtifactSha256);
        Assert.IsTrue(artifacts.Receipt.BoundaryStatements.Any(statement => statement.Contains("Rollback consideration is not rollback decision", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBF_Package_RequiresDeploymentExecutionReceipt()
    {
        var artifacts = Build(CreateInput() with { DeploymentExecutionReceipt = null });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.VerificationIncomplete, artifacts.Package.PackageVerdict);
        Assert.IsFalse(artifacts.Package.DeploymentVerified);
        Assert.IsFalse(artifacts.Package.CanProceedToRollbackDecision);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.MissingDeploymentExecutionReceipt);
    }

    [TestMethod]
    public void BlockBF_Package_RequiresPostDeployObservation()
    {
        var artifacts = Build(CreateInput() with { Observation = null });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.VerificationIncomplete, artifacts.Package.PackageVerdict);
        Assert.IsFalse(artifacts.Package.DeploymentVerified);
        Assert.IsFalse(artifacts.Package.CanProceedToRollbackDecision);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.MissingPostDeployObservation);
    }

    [TestMethod]
    public void BlockBF_Package_BlocksReceiptIdentityMismatch()
    {
        var cases = new (string Name, PostDeployVerificationPackageInput Input, PostDeployVerificationBlockReason Reason)[]
        {
            ("repo", CreateInput() with { Repository = "other/repo", Observation = CreateObservation(repository: "other/repo") }, PostDeployVerificationBlockReason.RepositoryMismatch),
            ("commit", CreateInput() with { CandidateCommitSha = new string('e', 40), Observation = CreateObservation(commit: new string('e', 40)) }, PostDeployVerificationBlockReason.CandidateCommitMismatch),
            ("version", CreateInput() with { CandidateVersion = "1.2.4", Observation = CreateObservation(version: "1.2.4", observedVersion: "1.2.4") }, PostDeployVerificationBlockReason.CandidateVersionMismatch),
            ("tag", CreateInput() with { CandidateTagName = "v1.2.4", Observation = CreateObservation(tag: "v1.2.4") }, PostDeployVerificationBlockReason.CandidateTagMismatch),
            ("channel", CreateInput() with { ReleaseChannel = "Preview", Observation = CreateObservation(channel: "Preview") }, PostDeployVerificationBlockReason.ReleaseChannelMismatch),
            ("target", CreateInput() with { DeploymentTarget = "staging", Observation = CreateObservation(target: "staging") }, PostDeployVerificationBlockReason.DeploymentTargetMismatch),
            ("environment", CreateInput() with { DeploymentEnvironment = "prod-east", Observation = CreateObservation(environment: "prod-east") }, PostDeployVerificationBlockReason.DeploymentEnvironmentMismatch),
            ("artifact-name", CreateInput() with { ExpectedArtifactName = "other.zip", Observation = CreateObservation(artifactName: "other.zip") }, PostDeployVerificationBlockReason.DeploymentArtifactMismatch),
            ("artifact-checksum", CreateInput() with { ExpectedArtifactSha256 = new string('c', 64), Observation = CreateObservation(artifactSha256: new string('c', 64)) }, PostDeployVerificationBlockReason.DeploymentArtifactChecksumMismatch)
        };

        foreach (var item in cases)
        {
            var artifacts = Build(item.Input);

            Assert.AreEqual(PostDeployVerificationPackageVerdict.VerificationBlocked, artifacts.Package.PackageVerdict, item.Name);
            AssertContainsReason(artifacts.Package, item.Reason);
        }
    }

    [TestMethod]
    public void BlockBF_Package_BlocksObservationIdentityMismatch()
    {
        var cases = new (string Name, PostDeployObservationEvidence Observation, PostDeployVerificationBlockReason Reason)[]
        {
            ("repo", CreateObservation(repository: "other/repo"), PostDeployVerificationBlockReason.RepositoryMismatch),
            ("commit", CreateObservation(commit: new string('e', 40)), PostDeployVerificationBlockReason.CandidateCommitMismatch),
            ("version", CreateObservation(version: "1.2.4"), PostDeployVerificationBlockReason.CandidateVersionMismatch),
            ("tag", CreateObservation(tag: "v1.2.4"), PostDeployVerificationBlockReason.CandidateTagMismatch),
            ("channel", CreateObservation(channel: "Preview"), PostDeployVerificationBlockReason.ReleaseChannelMismatch),
            ("target", CreateObservation(target: "staging"), PostDeployVerificationBlockReason.DeploymentTargetMismatch),
            ("environment", CreateObservation(environment: "prod-east"), PostDeployVerificationBlockReason.DeploymentEnvironmentMismatch)
        };

        foreach (var item in cases)
        {
            var artifacts = Build(CreateInput() with { Observation = item.Observation });

            Assert.AreEqual(PostDeployVerificationPackageVerdict.VerificationBlocked, artifacts.Package.PackageVerdict, item.Name);
            AssertContainsReason(artifacts.Package, item.Reason);
        }
    }

    [TestMethod]
    public void BlockBF_Package_BlocksFailedObservationWithIdentityMismatch()
    {
        var cases = new (string Name, PostDeployObservationEvidence Observation, PostDeployVerificationBlockReason Reason)[]
        {
            ("repo", CreateObservation(repository: "other/repo", observationSucceeded: false), PostDeployVerificationBlockReason.RepositoryMismatch),
            ("target", CreateObservation(target: "staging", observationSucceeded: false), PostDeployVerificationBlockReason.DeploymentTargetMismatch),
            ("environment", CreateObservation(environment: "prod-east", observationSucceeded: false), PostDeployVerificationBlockReason.DeploymentEnvironmentMismatch)
        };

        foreach (var item in cases)
        {
            var artifacts = Build(CreateInput() with { Observation = item.Observation });

            Assert.AreEqual(PostDeployVerificationPackageVerdict.VerificationBlocked, artifacts.Package.PackageVerdict, item.Name);
            AssertContainsReason(artifacts.Package, item.Reason);
            AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.PostDeployObservationFailed);
        }
    }

    [TestMethod]
    public void BlockBF_Package_BlocksReceiptBoundaryWithRollbackAuthority()
    {
        var receipt = CreateReceipt() with { Boundary = DeploymentExecutionBoundary.Executor with { CanExecuteRollback = true } };

        var artifacts = Build(CreateInput() with { DeploymentExecutionReceipt = receipt });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.VerificationBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentExecutionReceiptBoundaryViolation);
    }

    [TestMethod]
    public void BlockBF_Package_BlocksReceiptBoundaryWithWorkflowContinuationAuthority()
    {
        var receipt = CreateReceipt() with { Boundary = DeploymentExecutionBoundary.Executor with { CanContinueWorkflow = true } };

        var artifacts = Build(CreateInput() with { DeploymentExecutionReceipt = receipt });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.VerificationBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentExecutionReceiptBoundaryViolation);
    }

    [TestMethod]
    public void BlockBF_Package_BlocksReceiptBoundaryWithMemoryPromotionAuthority()
    {
        var receipt = CreateReceipt() with { Boundary = DeploymentExecutionBoundary.Executor with { CanPromoteMemory = true } };

        var artifacts = Build(CreateInput() with { DeploymentExecutionReceipt = receipt });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.VerificationBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentExecutionReceiptBoundaryViolation);
    }

    [TestMethod]
    public void BlockBF_Package_BlocksReceiptBoundaryWithSourceMutationAuthority()
    {
        var receipt = CreateReceipt() with { Boundary = DeploymentExecutionBoundary.Executor with { CanMutateSource = true } };

        var artifacts = Build(CreateInput() with { DeploymentExecutionReceipt = receipt });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.VerificationBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentExecutionReceiptBoundaryViolation);
    }

    [TestMethod]
    public void BlockBF_Package_VerifiedRequiresExecutedAndVerifiedReceipt()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentExecutionReceipt = CreateReceipt() with { ExecutionVerdict = DeploymentExecutionVerdict.Failed }
        });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        Assert.IsFalse(artifacts.Package.DeploymentVerified);
        Assert.IsTrue(artifacts.Package.CanProceedToRollbackDecision);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentExecutionReceiptFailed);
    }

    [TestMethod]
    public void BlockBF_Package_VerifiedRequiresPreAndPostStateVerified()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentExecutionReceipt = CreateReceipt() with { PreDeploymentStateVerified = false, PostDeploymentStateVerified = false }
        });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        Assert.IsFalse(artifacts.Package.DeploymentVerified);
        Assert.IsTrue(artifacts.Package.CanProceedToRollbackDecision);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentExecutionReceiptUnverified);
    }

    [TestMethod]
    public void BlockBF_Package_VerifiedRequiresDeploymentAttemptedAndAccepted()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentExecutionReceipt = CreateReceipt() with { DeploymentAttempted = false, DeploymentAccepted = false }
        });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        Assert.IsFalse(artifacts.Package.DeploymentVerified);
        Assert.IsTrue(artifacts.Package.CanProceedToRollbackDecision);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentExecutionReceiptUnverified);
    }

    [TestMethod]
    public void BlockBF_Package_VerifiedRequiresObservedVersionMatch()
    {
        var artifacts = Build(CreateInput() with { Observation = CreateObservation(observedVersion: "9.9.9") });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.CandidateVersionMismatch);
    }

    [TestMethod]
    public void BlockBF_Package_VerifiedRequiresObservedCommitMatch()
    {
        var artifacts = Build(CreateInput() with { Observation = CreateObservation(observedCommit: new string('e', 40)) });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.CandidateCommitMismatch);
    }

    [TestMethod]
    public void BlockBF_Package_VerifiedRequiresObservedArtifactNameMatch()
    {
        var artifacts = Build(CreateInput() with { Observation = CreateObservation(observedArtifactName: "other.zip") });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentArtifactMismatch);
    }

    [TestMethod]
    public void BlockBF_Package_VerifiedRequiresObservedArtifactChecksumMatch()
    {
        var artifacts = Build(CreateInput() with { Observation = CreateObservation(observedArtifactSha256: new string('c', 64)) });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentArtifactChecksumMismatch);
    }

    [TestMethod]
    public void BlockBF_Package_VerifiedRequiresHealthCheckSuccess()
    {
        var artifacts = Build(CreateInput() with { Observation = CreateObservation(healthCheckSucceeded: false) });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.HealthCheckFailed);
    }

    [TestMethod]
    public void BlockBF_Package_FailedObservationCreatesRollbackConsiderationEvidence()
    {
        var artifacts = Build(CreateInput() with
        {
            Observation = CreateObservation(observationSucceeded: false, observationError: "health endpoint unavailable")
        });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        Assert.IsTrue(artifacts.Package.CanProceedToRollbackDecision);
        Assert.IsFalse(artifacts.Package.DeploymentVerified);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.PostDeployObservationFailed);
    }

    [TestMethod]
    public void BlockBF_Package_PartialDeploymentCreatesRollbackConsiderationEvidence()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentExecutionReceipt = CreateReceipt() with
            {
                ExecutionVerdict = DeploymentExecutionVerdict.PartiallyExecuted,
                PostDeploymentStateVerified = false
            }
        });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        Assert.IsTrue(artifacts.Package.CanProceedToRollbackDecision);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentExecutionReceiptPartiallyExecuted);
    }

    [TestMethod]
    public void BlockBF_Package_PostStateMismatchCreatesRollbackConsiderationEvidence()
    {
        var artifacts = Build(CreateInput() with
        {
            DeploymentExecutionReceipt = CreateReceipt() with { PostDeploymentStateVerified = false }
        });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        Assert.IsTrue(artifacts.Package.CanProceedToRollbackDecision);
        AssertContainsReason(artifacts.Package, PostDeployVerificationBlockReason.DeploymentExecutionReceiptUnverified);
    }

    [TestMethod]
    public void BlockBF_Package_HealthCheckFailureCreatesRollbackConsiderationEvidence()
    {
        var artifacts = Build(CreateInput() with { Observation = CreateObservation(healthCheckSucceeded: false) });

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, artifacts.Package.PackageVerdict);
        Assert.IsTrue(artifacts.Package.CanProceedToRollbackDecision);
        Assert.IsFalse(artifacts.Package.Boundary.CanExecuteRollback);
    }

    [TestMethod]
    public void BlockBF_Package_RollbackConsiderationDoesNotExecuteRollback()
    {
        var package = Build(CreateInput() with { Observation = CreateObservation(healthCheckSucceeded: false) }).Package;

        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, package.PackageVerdict);
        Assert.IsTrue(package.CanProceedToRollbackDecision);
        Assert.IsFalse(package.Boundary.CanDecideRollback);
        Assert.IsFalse(package.Boundary.CanExecuteRollback);
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanDecideRollback(package));
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanExecuteRollback(package));
    }

    [TestMethod]
    public void BlockBF_Package_DoesNotDeployAgain()
    {
        var package = Build(CreateInput() with { Observation = CreateObservation(healthCheckSucceeded: false) }).Package;

        Assert.IsFalse(package.Boundary.CanDeploy);
        Assert.IsFalse(package.Boundary.CanRetryDeployment);
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanRetryDeployment(package));
    }

    [TestMethod]
    public void BlockBF_Package_DoesNotPublishPromoteContinueMutateSourceOrDispatchPipeline()
    {
        var package = Build().Package;

        Assert.IsFalse(package.Boundary.CanPublishPackages);
        Assert.IsFalse(package.Boundary.CanPromoteMemory);
        Assert.IsFalse(package.Boundary.CanContinueWorkflow);
        Assert.IsFalse(package.Boundary.CanMutateEnvironment);
        Assert.IsFalse(package.Boundary.CanMutateSource);
        Assert.IsFalse(package.Boundary.CanDispatchPipeline);
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanPublishPackages(package));
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanPromoteMemory(package));
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanContinueWorkflow(package));
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanMutateEnvironment(package));
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanMutateSource(package));
        Assert.IsFalse(PostDeployVerificationBypassEvaluator.CanDispatchPipeline(package));
    }

    [TestMethod]
    public void BlockBF_Boundary_RemainsEvidenceOnly()
    {
        var boundary = PostDeployVerificationBoundary.Evidence;

        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanRetryDeployment);
        Assert.IsFalse(boundary.CanExecuteRollback);
        Assert.IsFalse(boundary.CanDecideRollback);
        Assert.IsFalse(boundary.CanPublishPackages);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanMutateEnvironment);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanDispatchPipeline);
    }

    [TestMethod]
    public async Task BlockBF_Cli_ReturnsZeroForVerifiedPackage()
    {
        var receiptPath = WriteJsonFile("deployment-execution-receipt", CreateReceipt());
        var observationPath = WriteJsonFile("post-deploy-observation", CreateObservation());
        var outDir = Path.Combine(Path.GetTempPath(), $"bf-verified-{Guid.NewGuid():N}");

        var result = await RunCliAsync(PackageArgs(receiptPath, observationPath, outDir)).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "post-deploy-verification-package.json")));
        var events = File.ReadAllText(Path.Combine(outDir, FileBackedGovernanceEventStore.ArtifactName));
        StringAssert.Contains(events, "PostDeployVerificationPackageCreated");
    }

    [TestMethod]
    public async Task BlockBF_Cli_ReturnsZeroForRollbackConsiderationPackageWithoutRollbackExecution()
    {
        var receiptPath = WriteJsonFile("deployment-execution-receipt", CreateReceipt());
        var observationPath = WriteJsonFile("post-deploy-observation", CreateObservation(healthCheckSucceeded: false));
        var outDir = Path.Combine(Path.GetTempPath(), $"bf-rollback-consideration-{Guid.NewGuid():N}");

        var result = await RunCliAsync(PackageArgs(receiptPath, observationPath, outDir)).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        var package = JsonSerializer.Deserialize<PostDeployVerificationPackage>(
            File.ReadAllText(Path.Combine(outDir, "post-deploy-verification-package.json")),
            JsonOptions);
        Assert.IsNotNull(package);
        Assert.AreEqual(PostDeployVerificationPackageVerdict.RollbackConsiderationRequired, package!.PackageVerdict);
        Assert.IsTrue(package.CanProceedToRollbackDecision);
        Assert.IsFalse(package.Boundary.CanExecuteRollback);
        var events = File.ReadAllText(Path.Combine(outDir, FileBackedGovernanceEventStore.ArtifactName));
        Assert.IsFalse(events.Contains("RollbackExecuted", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task BlockBF_Cli_ReturnsNonZeroForIncompleteOrBlockedPackage()
    {
        var receiptPath = WriteJsonFile("deployment-execution-receipt", CreateReceipt());
        var observationPath = WriteJsonFile("post-deploy-observation", CreateObservation(repository: "other/repo"));
        var outDir = Path.Combine(Path.GetTempPath(), $"bf-blocked-{Guid.NewGuid():N}");

        var blocked = await RunCliAsync(PackageArgs(receiptPath, observationPath, outDir)).ConfigureAwait(false);

        Assert.AreEqual(1, blocked.ExitCode, blocked.Output + blocked.Error);

        var usage = await RunCliAsync("post-deploy-verification", "package", "--deployment-execution-receipt", receiptPath).ConfigureAwait(false);
        Assert.AreEqual(2, usage.ExitCode, usage.Output + usage.Error);
    }

    [TestMethod]
    public async Task BlockBF_Cli_RejectsDeployRollbackPublishPromoteContinueCommitPushMergeTagReleaseVerbs()
    {
        foreach (var forbidden in new[]
        {
            "deploy",
            "retry-deployment",
            "rollback",
            "rollback-execute",
            "rollback-decision",
            "publish",
            "publish-package",
            "promote-memory",
            "continue",
            "continue-workflow",
            "dispatch",
            "trigger-pipeline",
            "commit",
            "push",
            "merge",
            "source-apply",
            "tag",
            "release"
        })
        {
            var result = await RunCliAsync("post-deploy-verification", forbidden, "--package", "post-deploy-verification-package.json").ConfigureAwait(false);

            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public async Task BlockBF_Cli_ReadOnlyCommandsUseReadOnlyBoundary()
    {
        var package = Build().Package;
        var path = WriteJsonFile("post-deploy-verification-package", package);

        var result = await RunCliAsync("post-deploy-verification", "status", "--package", path, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var boundary = document.RootElement.GetProperty("boundary");
        Assert.IsTrue(boundary.GetProperty("evidenceOnly").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canDeploy").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canRetryDeployment").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canExecuteRollback").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canDecideRollback").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canContinueWorkflow").GetBoolean());
    }

    [TestMethod]
    public void BlockBF_StaticBoundary_NoRollbackExecutorOrDeploymentMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliPostDeployVerification.cs"));
        var model = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "PostDeployVerificationPackage.cs"));
        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "BF_POST_DEPLOY_VERIFICATION_ROLLBACK_SEPARATION.md"));

        Assert.IsFalse(cli.Contains("ProcessStartInfo", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("DeployApprovedArtifactAsync", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("kubectl", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("terraform apply", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("az webapp", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("docker push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("dotnet nuget push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("npm publish", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh release", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh api", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(model, "CanProceedToRollbackDecision");
        StringAssert.Contains(model, "CanExecuteRollback(object? evidence) => false");
        StringAssert.Contains(model, "CanDecideRollback(object? evidence) => false");
        StringAssert.Contains(receipt, "Failed verification is not rollback approval.");
        StringAssert.Contains(receipt, "Rollback consideration is not rollback decision.");
        StringAssert.Contains(receipt, "Rollback decision is not rollback execution.");
        StringAssert.Contains(receipt, "CanProceedToRollbackDecision is not rollback execution.");
    }

    private static PostDeployVerificationPackageArtifacts Build(PostDeployVerificationPackageInput? input = null) =>
        PostDeployVerificationPackageBuilder.Build(input ?? CreateInput());

    private static PostDeployVerificationPackageInput CreateInput() => new()
    {
        DeploymentExecutionReceipt = CreateReceipt(),
        Observation = CreateObservation(),
        Repository = "owner/repo",
        CandidateCommitSha = CandidateCommitSha,
        CandidateVersion = "1.2.3",
        CandidateTagName = "v1.2.3",
        ReleaseChannel = "Stable",
        DeploymentTarget = "production",
        DeploymentEnvironment = "prod-west",
        ExpectedArtifactName = "artifact.zip",
        ExpectedArtifactSha256 = ArtifactSha256,
        CreatedBy = "deployment-captain",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T12:40:00Z")
    };

    private static DeploymentExecutionReceipt CreateReceipt() => new()
    {
        DeploymentExecutionReceiptId = "deployment_exec_receipt_bf",
        DeploymentExecutionRequestId = "deployment_exec_request_bf",
        DeploymentReadinessDecisionPackageId = "deployment_readiness_decision_pkg_bf",
        Repository = "owner/repo",
        CandidateCommitSha = CandidateCommitSha,
        CandidateVersion = "1.2.3",
        CandidateTagName = "v1.2.3",
        ReleaseChannel = "Stable",
        DeploymentTarget = "production",
        DeploymentEnvironment = "prod-west",
        DeployedArtifactName = "artifact.zip",
        DeployedArtifactSha256 = ArtifactSha256,
        ApprovedActions = [DeploymentExecutionAction.DeployApprovedArtifact],
        AttemptedActions = [DeploymentExecutionAction.DeployApprovedArtifact],
        CompletedActions = [DeploymentExecutionAction.DeployApprovedArtifact],
        PreDeploymentState = new DeploymentTargetObservedState
        {
            DeploymentTarget = "production",
            DeploymentEnvironment = "prod-west",
            DeploymentInProgress = false,
            DeploymentTargetLocked = false,
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T12:25:30Z"),
            ObservationSource = "test",
            ObservationSucceeded = true
        },
        PostDeploymentState = new DeploymentTargetObservedState
        {
            DeploymentTarget = "production",
            DeploymentEnvironment = "prod-west",
            CurrentlyDeployedVersion = "1.2.3",
            CurrentlyDeployedCommitSha = CandidateCommitSha,
            CurrentlyDeployedArtifactSha256 = ArtifactSha256,
            DeploymentInProgress = false,
            DeploymentTargetLocked = false,
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T12:26:00Z"),
            ObservationSource = "test",
            ObservationSucceeded = true
        },
        MutationResults =
        [
            new DeploymentExecutionMutationResult
            {
                Action = DeploymentExecutionAction.DeployApprovedArtifact,
                Attempted = true,
                Accepted = true,
                Provider = "FakeTestGateway",
                MutationName = "DeployApprovedArtifact",
                DeploymentTarget = "production",
                DeploymentEnvironment = "prod-west",
                CompletedAtUtc = DateTimeOffset.Parse("2026-06-20T12:25:45Z")
            }
        ],
        PreDeploymentStateVerified = true,
        DeploymentAttempted = true,
        DeploymentAccepted = true,
        PostDeploymentStateVerified = true,
        PackagePublicationAttempted = false,
        MemoryPromotionAttempted = false,
        WorkflowContinuationAttempted = false,
        RollbackExecutionAttempted = false,
        SourceMutationAttempted = false,
        ExecutionVerdict = DeploymentExecutionVerdict.ExecutedAndVerified,
        FailureClassification = DeploymentExecutionFailureKind.None,
        RequestedBy = "deployment-captain",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T12:25:00Z"),
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T12:26:00Z"),
        Boundary = DeploymentExecutionBoundary.Executor
    };

    private static PostDeployObservationEvidence CreateObservation(
        string? repository = null,
        string? commit = null,
        string? version = null,
        string? tag = null,
        string? channel = null,
        string? target = null,
        string? environment = null,
        string? artifactName = null,
        string? artifactSha256 = null,
        string? observedVersion = null,
        string? observedCommit = null,
        string? observedArtifactName = null,
        string? observedArtifactSha256 = null,
        bool observationSucceeded = true,
        string? observationError = null,
        bool healthCheckSucceeded = true,
        string? healthCheckSummary = "health check passed") => new()
        {
            ObservationId = "post_deploy_observation_bf",
            Repository = repository ?? "owner/repo",
            CandidateCommitSha = commit ?? CandidateCommitSha,
            CandidateVersion = version ?? "1.2.3",
            CandidateTagName = tag ?? "v1.2.3",
            ReleaseChannel = channel ?? "Stable",
            DeploymentTarget = target ?? "production",
            DeploymentEnvironment = environment ?? "prod-west",
            ObservedVersion = observedVersion ?? version ?? "1.2.3",
            ObservedCommitSha = observedCommit ?? commit ?? CandidateCommitSha,
            ObservedArtifactName = observedArtifactName ?? artifactName ?? "artifact.zip",
            ObservedArtifactSha256 = observedArtifactSha256 ?? artifactSha256 ?? ArtifactSha256,
            ObservationSucceeded = observationSucceeded,
            ObservationError = observationError,
            HealthCheckSucceeded = healthCheckSucceeded,
            HealthCheckSummary = healthCheckSummary,
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T12:35:00Z"),
            ObservationSource = "test"
        };

    private static string[] PackageArgs(string receiptPath, string observationPath, string outDir) =>
    [
        "post-deploy-verification",
        "package",
        "--deployment-execution-receipt",
        receiptPath,
        "--observation",
        observationPath,
        "--repo",
        "owner/repo",
        "--candidate-commit",
        CandidateCommitSha,
        "--version",
        "1.2.3",
        "--tag",
        "v1.2.3",
        "--channel",
        "Stable",
        "--deployment-target",
        "production",
        "--deployment-environment",
        "prod-west",
        "--artifact-name",
        "artifact.zip",
        "--artifact-sha256",
        ArtifactSha256,
        "--created-by",
        "deployment-captain",
        "--out",
        outDir,
        "--json"
    ];

    private static void AssertContainsReason(
        PostDeployVerificationPackage package,
        PostDeployVerificationBlockReason reason) =>
        Assert.IsTrue(package.BlockReasons.Contains(reason), $"Expected {reason}; actual: {string.Join(", ", package.BlockReasons)}");

    private static string WriteJsonFile<T>(string prefix, T value)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        return path;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
