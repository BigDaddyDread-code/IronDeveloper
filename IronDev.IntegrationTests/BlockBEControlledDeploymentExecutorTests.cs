using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBEControlledDeploymentExecutorTests
{
    private static readonly string CandidateCommitSha = new('d', 40);
    private static readonly string ArtifactSha256 = new('b', 64);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockBE_Executor_ExecutesApprovedDeploymentAndWritesReceipt()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package));

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.ExecutedAndVerified, result.Verdict);
        Assert.AreEqual(DeploymentExecutionFailureKind.None, result.FailureKind);
        Assert.IsNotNull(result.Receipt);
        Assert.IsTrue(result.Receipt!.PreDeploymentStateVerified);
        Assert.IsTrue(result.Receipt.DeploymentAttempted);
        Assert.IsTrue(result.Receipt.DeploymentAccepted);
        Assert.IsTrue(result.Receipt.PostDeploymentStateVerified);
        Assert.AreEqual("artifact.zip", result.Receipt.DeployedArtifactName);
        Assert.AreEqual(ArtifactSha256, result.Receipt.DeployedArtifactSha256);
        Assert.AreEqual(2, gateway.ObserveCalls);
        Assert.AreEqual(1, gateway.DeployCalls);
    }

    [TestMethod]
    public async Task BlockBE_Executor_RequiresDeploymentReadinessDecisionPackage()
    {
        var ready = CreatePackage();
        var cases = new (string Name, DeploymentReadinessDecisionPackage? Package)[]
        {
            ("missing", null),
            ("incomplete", ready with { PackageVerdict = DeploymentReadinessDecisionPackageVerdict.PackageIncomplete }),
            ("blocked", ready with { PackageVerdict = DeploymentReadinessDecisionPackageVerdict.PackageBlocked, BlockReasons = [DeploymentReadinessDecisionPackageBlockReason.BoundaryViolation] }),
            ("rejected", ready with { PackageVerdict = DeploymentReadinessDecisionPackageVerdict.PackageRejected }),
            ("cannot-proceed", ready with { CanProceedToControlledDeploymentExecutor = false }),
            ("boundary-deploy", ready with { Boundary = ready.Boundary with { CanDeploy = true } }),
            ("boundary-continue", ready with { Boundary = ready.Boundary with { CanContinueWorkflow = true } })
        };

        foreach (var item in cases)
        {
            var gateway = new FakeDeploymentExecutionGateway();
            var result = await ControlledDeploymentExecutor.ExecuteAsync(item.Package, CreateRequest(ready), gateway).ConfigureAwait(false);

            Assert.AreNotEqual(DeploymentExecutionVerdict.ExecutedAndVerified, result.Verdict, item.Name);
            Assert.AreEqual(0, gateway.ObserveCalls, item.Name);
            Assert.AreEqual(0, gateway.DeployCalls, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockBE_Executor_BlocksBCSeparationPackageDirectUse()
    {
        var wrongPackagePath = WriteJsonFile("deployment-readiness-decision-package", CreateSeparationPackage());
        var requestPath = WriteJsonFile("deployment-execution-request", CreateRequest(CreatePackage()));
        var outDir = Path.Combine(Path.GetTempPath(), $"be-bc-direct-{Guid.NewGuid():N}");

        var result = await RunCliAsync(
            "deployment-execution",
            "execute",
            "--deployment-readiness-decision-package",
            wrongPackagePath,
            "--request",
            requestPath,
            "--out",
            outDir,
            "--json").ConfigureAwait(false);

        Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
        Assert.IsFalse(File.Exists(Path.Combine(outDir, "deployment-target-state.json")));
        Assert.IsFalse(File.Exists(Path.Combine(outDir, "deployment-execution-receipt.json")));
    }

    [TestMethod]
    public async Task BlockBE_Executor_BlocksReleaseReceiptDirectUse()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var wrongPackagePath = WriteJsonFile("release-execution-receipt", CreateReleaseExecutionReceipt(package, request));
        var requestPath = WriteJsonFile("deployment-execution-request", request);
        var outDir = Path.Combine(Path.GetTempPath(), $"be-release-direct-{Guid.NewGuid():N}");

        var result = await RunCliAsync(
            "deployment-execution",
            "execute",
            "--deployment-readiness-decision-package",
            wrongPackagePath,
            "--request",
            requestPath,
            "--out",
            outDir,
            "--json").ConfigureAwait(false);

        Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
        Assert.IsFalse(File.Exists(Path.Combine(outDir, "deployment-target-state.json")));
        Assert.IsFalse(File.Exists(Path.Combine(outDir, "deployment-execution-receipt.json")));
    }

    [TestMethod]
    public async Task BlockBE_Executor_RequiresExplicitDeploymentExecutionRequest()
    {
        var gateway = new FakeDeploymentExecutionGateway();

        var result = await ControlledDeploymentExecutor.ExecuteAsync(CreatePackage(), null, gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(DeploymentExecutionFailureKind.MissingDeploymentExecutionRequest, result.FailureKind);
        Assert.IsNull(result.Receipt);
        Assert.AreEqual(0, gateway.ObserveCalls);
        Assert.AreEqual(0, gateway.DeployCalls);
    }

    [TestMethod]
    public async Task BlockBE_Executor_BlocksWhenRequestIdentityDoesNotMatchDecisionPackage()
    {
        var package = CreatePackage();
        var cases = new (string Name, DeploymentExecutionRequest Request, DeploymentExecutionFailureKind Expected)[]
        {
            ("package-id", CreateRequest(package) with { DeploymentReadinessDecisionPackageId = "other-package" }, DeploymentExecutionFailureKind.RequestPackageMismatch),
            ("repo", CreateRequest(package) with { Repository = "other/repo" }, DeploymentExecutionFailureKind.RepositoryMismatch),
            ("commit", CreateRequest(package) with { CandidateCommitSha = new string('e', 40) }, DeploymentExecutionFailureKind.CandidateCommitMismatch),
            ("version", CreateRequest(package) with { CandidateVersion = "1.2.4" }, DeploymentExecutionFailureKind.CandidateVersionMismatch),
            ("tag", CreateRequest(package) with { CandidateTagName = "v1.2.4" }, DeploymentExecutionFailureKind.CandidateTagMismatch),
            ("channel", CreateRequest(package) with { ReleaseChannel = "Preview" }, DeploymentExecutionFailureKind.ReleaseChannelMismatch),
            ("target", CreateRequest(package) with { DeploymentTarget = "staging" }, DeploymentExecutionFailureKind.DeploymentTargetMismatch),
            ("environment", CreateRequest(package) with { DeploymentEnvironment = "prod-east" }, DeploymentExecutionFailureKind.DeploymentEnvironmentMismatch),
            ("not-confirmed", CreateRequest(package) with { ConfirmDeploymentExecution = false }, DeploymentExecutionFailureKind.DeploymentExecutionNotConfirmed)
        };

        foreach (var item in cases)
        {
            var gateway = new FakeDeploymentExecutionGateway();
            var result = await ControlledDeploymentExecutor.ExecuteAsync(package, item.Request, gateway).ConfigureAwait(false);

            Assert.AreEqual(DeploymentExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.AreEqual(item.Expected, result.FailureKind, item.Name);
            Assert.AreEqual(0, gateway.ObserveCalls, item.Name);
            Assert.AreEqual(0, gateway.DeployCalls, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockBE_Executor_BlocksArtifactMismatch()
    {
        var package = CreatePackage();
        var cases = new (string Name, DeploymentExecutionRequest Request, DeploymentExecutionFailureKind Expected)[]
        {
            ("artifact-name", CreateRequest(package) with { DeploymentArtifactName = "other.zip" }, DeploymentExecutionFailureKind.DeploymentArtifactMismatch),
            ("artifact-checksum", CreateRequest(package) with { DeploymentArtifactSha256 = new string('c', 64) }, DeploymentExecutionFailureKind.DeploymentArtifactChecksumMismatch)
        };

        foreach (var item in cases)
        {
            var gateway = new FakeDeploymentExecutionGateway();
            var result = await ControlledDeploymentExecutor.ExecuteAsync(package, item.Request, gateway).ConfigureAwait(false);

            Assert.AreEqual(DeploymentExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.AreEqual(item.Expected, result.FailureKind, item.Name);
            Assert.AreEqual(0, gateway.DeployCalls, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockBE_Executor_ReobservesDeploymentTargetBeforeMutation()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package));

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.ExecutedAndVerified, result.Verdict);
        CollectionAssert.AreEqual(new[] { "observe", "deploy", "observe" }, gateway.CallLog);
        Assert.IsTrue(result.Receipt!.PreDeploymentStateVerified);
    }

    [TestMethod]
    public async Task BlockBE_Executor_BlocksTargetObservationFailure()
    {
        var package = CreatePackage();
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(FailedObservation(package, "target unavailable"));

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, CreateRequest(package), gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(DeploymentExecutionFailureKind.DeploymentTargetObservationFailed, result.FailureKind);
        Assert.IsFalse(result.Receipt!.DeploymentAttempted);
        Assert.AreEqual(1, gateway.ObserveCalls);
        Assert.AreEqual(0, gateway.DeployCalls);
    }

    [TestMethod]
    public async Task BlockBE_Executor_BlocksDeploymentTargetLocked()
    {
        var package = CreatePackage();
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package) with { DeploymentTargetLocked = true });

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, CreateRequest(package), gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(DeploymentExecutionFailureKind.DeploymentTargetLocked, result.FailureKind);
        Assert.AreEqual(0, gateway.DeployCalls);
    }

    [TestMethod]
    public async Task BlockBE_Executor_BlocksDeploymentAlreadyInProgress()
    {
        var package = CreatePackage();
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package) with { DeploymentInProgress = true });

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, CreateRequest(package), gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(DeploymentExecutionFailureKind.DeploymentInProgress, result.FailureKind);
        Assert.AreEqual(0, gateway.DeployCalls);
    }

    [TestMethod]
    public async Task BlockBE_Executor_BlocksUnsupportedDeploymentAction()
    {
        var package = CreatePackage();
        var request = CreateRequest(package) with { ApprovedActions = [(DeploymentExecutionAction)99] };
        var gateway = new FakeDeploymentExecutionGateway();

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(DeploymentExecutionFailureKind.UnsupportedDeploymentAction, result.FailureKind);
        Assert.AreEqual(0, gateway.ObserveCalls);
        Assert.AreEqual(0, gateway.DeployCalls);
    }

    [TestMethod]
    public async Task BlockBE_Executor_ExecutesOnlyDeployApprovedArtifact()
    {
        var package = CreatePackage();
        var request = CreateRequest(package) with
        {
            ApprovedActions =
            [
                DeploymentExecutionAction.DeployApprovedArtifact,
                DeploymentExecutionAction.DeployApprovedArtifact
            ]
        };
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package));

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.ExecutedAndVerified, result.Verdict);
        Assert.AreEqual(1, gateway.DeployCalls);
        CollectionAssert.AreEqual(new[] { DeploymentExecutionAction.DeployApprovedArtifact }, result.Receipt!.CompletedActions);
    }

    [TestMethod]
    public async Task BlockBE_Executor_PostVerifiesDeploymentState()
    {
        var package = CreatePackage();
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package));

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, CreateRequest(package), gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.ExecutedAndVerified, result.Verdict);
        Assert.AreEqual(CandidateCommitSha, result.Receipt!.PostDeploymentState!.CurrentlyDeployedCommitSha);
        Assert.AreEqual(ArtifactSha256, result.Receipt.PostDeploymentState.CurrentlyDeployedArtifactSha256);
        Assert.IsTrue(result.Receipt.PostDeploymentStateVerified);
    }

    [TestMethod]
    public async Task BlockBE_Executor_FailsWhenPostDeploymentStateDoesNotMatchExpectedArtifact()
    {
        var package = CreatePackage();
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package) with { CurrentlyDeployedArtifactSha256 = new string('0', 64) });

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, CreateRequest(package), gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.PartiallyExecuted, result.Verdict);
        Assert.AreEqual(DeploymentExecutionFailureKind.PostDeploymentVerificationFailed, result.FailureKind);
        Assert.IsTrue(result.Receipt!.DeploymentAttempted);
        Assert.IsTrue(result.Receipt.DeploymentAccepted);
        Assert.IsFalse(result.Receipt.PostDeploymentStateVerified);
    }

    [TestMethod]
    public async Task BlockBE_Executor_RecordsPartialExecutionWithoutRollbackOrContinuation()
    {
        var package = CreatePackage();
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package) with { CurrentlyDeployedVersion = "0.0.1" });

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, CreateRequest(package), gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.PartiallyExecuted, result.Verdict);
        Assert.IsTrue(result.Receipt!.DeploymentAttempted);
        Assert.IsFalse(result.Receipt.PostDeploymentStateVerified);
        Assert.IsFalse(result.Receipt.RollbackExecutionAttempted);
        Assert.IsFalse(result.Receipt.WorkflowContinuationAttempted);
        Assert.AreEqual(0, gateway.RollbackExecutionCalls);
        Assert.AreEqual(0, gateway.ContinueWorkflowCalls);
    }

    [TestMethod]
    public async Task BlockBE_Executor_DoesNotPublishPackagesPromoteMemoryContinueWorkflowOrMutateSource()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var gateway = new FakeDeploymentExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package));

        var result = await ControlledDeploymentExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(DeploymentExecutionVerdict.ExecutedAndVerified, result.Verdict);
        Assert.IsFalse(result.Receipt!.Boundary.CanPublishPackages);
        Assert.IsFalse(result.Receipt.Boundary.CanPromoteMemory);
        Assert.IsFalse(result.Receipt.Boundary.CanContinueWorkflow);
        Assert.IsFalse(result.Receipt.Boundary.CanMutateSource);
        Assert.IsFalse(result.Receipt.Boundary.CanExecuteRollback);
        Assert.IsFalse(result.Receipt.Boundary.CanCreateTag);
        Assert.IsFalse(result.Receipt.Boundary.CanCreateGitHubRelease);
        Assert.IsFalse(result.Receipt.PackagePublicationAttempted);
        Assert.IsFalse(result.Receipt.MemoryPromotionAttempted);
        Assert.IsFalse(result.Receipt.WorkflowContinuationAttempted);
        Assert.IsFalse(result.Receipt.SourceMutationAttempted);
        Assert.IsFalse(result.Receipt.RollbackExecutionAttempted);
        Assert.AreEqual(0, gateway.PublishPackageCalls);
        Assert.AreEqual(0, gateway.MemoryPromotionCalls);
        Assert.AreEqual(0, gateway.ContinueWorkflowCalls);
        Assert.AreEqual(0, gateway.SourceMutationCalls);
        Assert.AreEqual(0, gateway.RollbackExecutionCalls);
    }

    [TestMethod]
    public async Task BlockBE_Cli_ReturnsZeroOnlyForExecutedAndVerified()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var packagePath = WriteJsonFile("deployment-readiness-decision-package", package);
        var requestPath = WriteJsonFile("deployment-execution-request", request);
        var outDir = Path.Combine(Path.GetTempPath(), $"be-ready-{Guid.NewGuid():N}");

        var ready = await RunCliAsync(
            "deployment-execution",
            "execute",
            "--deployment-readiness-decision-package",
            packagePath,
            "--request",
            requestPath,
            "--out",
            outDir,
            "--json").ConfigureAwait(false);

        Assert.AreEqual(0, ready.ExitCode, ready.Output + ready.Error);
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "deployment-execution-receipt.json")));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "deployment-target-state.json")));
        var events = File.ReadAllText(Path.Combine(outDir, FileBackedGovernanceEventStore.ArtifactName));
        StringAssert.Contains(events, "DeploymentExecuted");

        var blockedRequestPath = WriteJsonFile("deployment-execution-request", request with { ConfirmDeploymentExecution = false });
        var blockedOut = Path.Combine(Path.GetTempPath(), $"be-blocked-{Guid.NewGuid():N}");
        var blocked = await RunCliAsync(
            "deployment-execution",
            "execute",
            "--deployment-readiness-decision-package",
            packagePath,
            "--request",
            blockedRequestPath,
            "--out",
            blockedOut,
            "--json").ConfigureAwait(false);

        Assert.AreEqual(1, blocked.ExitCode, blocked.Output + blocked.Error);
    }

    [TestMethod]
    public async Task BlockBE_Cli_RejectsPublishPromoteContinueCommitPushMergeRollbackTagReleaseVerbs()
    {
        foreach (var forbidden in new[]
        {
            "publish",
            "publish-package",
            "promote-memory",
            "continue",
            "continue-workflow",
            "commit",
            "push",
            "merge",
            "source-apply",
            "tag",
            "release",
            "rollback",
            "rollback-execute",
            "dispatch",
            "trigger-pipeline"
        })
        {
            var result = await RunCliAsync("deployment-execution", forbidden, "--receipt", "deployment-execution-receipt.json").ConfigureAwait(false);

            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public async Task BlockBE_Cli_ReadOnlyCommandsUseReadOnlyBoundary()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var receipt = CreateExecutedReceipt(package, request);
        var path = WriteJsonFile("deployment-execution-receipt", receipt);

        var result = await RunCliAsync("deployment-execution", "status", "--receipt", path, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var boundary = document.RootElement.GetProperty("boundary");
        Assert.IsFalse(boundary.GetProperty("canDeployApprovedArtifact").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canPublishPackages").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canContinueWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canExecuteRollback").GetBoolean());
    }

    [TestMethod]
    public void BlockBE_StaticBoundary_DeploymentSurfaceIsOnlyApprovedArtifactDeployment()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliDeploymentExecution.cs"));
        var model = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "DeploymentExecutionModels.cs"));
        var executor = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ControlledDeploymentExecutor.cs"));
        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "BE_CONTROLLED_DEPLOYMENT_EXECUTOR.md"));

        Assert.IsFalse(cli.Contains("ProcessStartInfo", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("kubectl", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("terraform apply", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("az webapp", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("docker push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("dotnet nuget push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("npm publish", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh release", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh api", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(model, "CanDeployApprovedArtifact");
        StringAssert.Contains(model, "CanPublishPackages(object? evidence) => false");
        StringAssert.Contains(executor, "DeployApprovedArtifactAsync");
        StringAssert.Contains(receipt, "BC package is not deployment execution authority.");
        StringAssert.Contains(receipt, "Release execution receipt is not deployment execution authority.");
        StringAssert.Contains(receipt, "Deployment readiness decision package is not deployment execution by itself.");
        StringAssert.Contains(receipt, "Deployment execution is not package publication.");
        StringAssert.Contains(receipt, "Deployment execution is not workflow continuation.");
        StringAssert.Contains(receipt, "Deployment execution is not memory promotion.");
        StringAssert.Contains(receipt, "Deployment execution is not rollback execution.");
        StringAssert.Contains(receipt, "Partial deployment is non-success.");
        StringAssert.Contains(receipt, "Post-deployment verification failure is non-success.");
    }

    private static DeploymentReadinessDecisionPackage CreatePackage() => new()
    {
        DeploymentReadinessDecisionPackageId = "deployment_readiness_decision_pkg_be",
        SourceDeploymentReadinessSeparationPackageId = "deployment_readiness_sep_be",
        Repository = "owner/repo",
        CandidateCommitSha = CandidateCommitSha,
        CandidateVersion = "1.2.3",
        CandidateTagName = "v1.2.3",
        ReleaseChannel = "Stable",
        DeploymentTarget = "production",
        DeploymentEnvironment = "prod-west",
        DeploymentArtifactName = "artifact.zip",
        DeploymentArtifactSha256 = ArtifactSha256,
        Decision = DeploymentReadinessDecision.ApprovedForControlledDeploymentExecutor,
        DecisionMadeBy = "deployment-captain",
        DecisionMadeAtUtc = DateTimeOffset.Parse("2026-06-20T12:15:00Z"),
        DecisionRationale = "BD package is eligible for controlled deployment execution.",
        PackageVerdict = DeploymentReadinessDecisionPackageVerdict.PackageReadyForControlledDeploymentExecutor,
        CanProceedToControlledDeploymentExecutor = true,
        CreatedBy = "deployment-captain",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T12:20:00Z"),
        Boundary = DeploymentReadinessDecisionPackageBoundary.Evidence
    };

    private static DeploymentExecutionRequest CreateRequest(DeploymentReadinessDecisionPackage package) => new()
    {
        DeploymentExecutionRequestId = "deployment_exec_request_be",
        DeploymentReadinessDecisionPackageId = package.DeploymentReadinessDecisionPackageId,
        Repository = package.Repository,
        CandidateCommitSha = package.CandidateCommitSha,
        CandidateVersion = package.CandidateVersion,
        CandidateTagName = package.CandidateTagName,
        ReleaseChannel = package.ReleaseChannel,
        DeploymentTarget = package.DeploymentTarget,
        DeploymentEnvironment = package.DeploymentEnvironment,
        DeploymentArtifactName = package.DeploymentArtifactName,
        DeploymentArtifactSha256 = package.DeploymentArtifactSha256,
        ApprovedActions = [DeploymentExecutionAction.DeployApprovedArtifact],
        ConfirmDeploymentExecution = true,
        RequestedBy = "deployment-captain",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T12:25:00Z")
    };

    private static DeploymentReadinessSeparationPackage CreateSeparationPackage() => new()
    {
        DeploymentReadinessSeparationPackageId = "deployment_readiness_sep_be",
        SourceReleaseExecutionReceiptId = "release_exec_be",
        Repository = "owner/repo",
        CandidateCommitSha = CandidateCommitSha,
        CandidateVersion = "1.2.3",
        CandidateTagName = "v1.2.3",
        ReleaseChannel = "Stable",
        DeploymentTarget = "production",
        DeploymentReadinessScope = "release-v1.2.3",
        PackageVerdict = DeploymentReadinessSeparationVerdict.PackageReadyForDeploymentReadinessDecision,
        CanProceedToDeploymentReadinessDecision = true,
        CreatedBy = "release-captain",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T12:10:00Z"),
        Boundary = DeploymentReadinessSeparationBoundary.Evidence
    };

    private static DeploymentTargetObservedState GoodPreState(DeploymentReadinessDecisionPackage package) => new()
    {
        DeploymentTarget = package.DeploymentTarget,
        DeploymentEnvironment = package.DeploymentEnvironment,
        DeploymentInProgress = false,
        DeploymentTargetLocked = false,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T12:25:30Z"),
        ObservationSource = "test",
        ObservationSucceeded = true
    };

    private static DeploymentTargetObservedState GoodPostState(DeploymentReadinessDecisionPackage package) => GoodPreState(package) with
    {
        CurrentlyDeployedVersion = package.CandidateVersion,
        CurrentlyDeployedCommitSha = package.CandidateCommitSha,
        CurrentlyDeployedArtifactSha256 = package.DeploymentArtifactSha256,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T12:26:00Z")
    };

    private static DeploymentTargetObservedState FailedObservation(
        DeploymentReadinessDecisionPackage package,
        string error) => GoodPreState(package) with
        {
            ObservationSucceeded = false,
            ObservationError = error
        };

    private static DeploymentExecutionReceipt CreateExecutedReceipt(
        DeploymentReadinessDecisionPackage package,
        DeploymentExecutionRequest request) => new()
        {
            DeploymentExecutionReceiptId = "deployment_exec_receipt_be",
            DeploymentExecutionRequestId = request.DeploymentExecutionRequestId,
            DeploymentReadinessDecisionPackageId = package.DeploymentReadinessDecisionPackageId,
            Repository = package.Repository,
            CandidateCommitSha = package.CandidateCommitSha,
            CandidateVersion = package.CandidateVersion,
            CandidateTagName = package.CandidateTagName,
            ReleaseChannel = package.ReleaseChannel,
            DeploymentTarget = package.DeploymentTarget,
            DeploymentEnvironment = package.DeploymentEnvironment,
            DeployedArtifactName = package.DeploymentArtifactName,
            DeployedArtifactSha256 = package.DeploymentArtifactSha256,
            ApprovedActions = request.ApprovedActions,
            AttemptedActions = request.ApprovedActions,
            CompletedActions = request.ApprovedActions,
            PreDeploymentState = GoodPreState(package),
            PostDeploymentState = GoodPostState(package),
            MutationResults =
            [
                new DeploymentExecutionMutationResult
                {
                    Action = DeploymentExecutionAction.DeployApprovedArtifact,
                    Attempted = true,
                    Accepted = true,
                    Provider = "FakeTestGateway",
                    MutationName = "DeployApprovedArtifact",
                    DeploymentTarget = package.DeploymentTarget,
                    DeploymentEnvironment = package.DeploymentEnvironment,
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
            RequestedBy = request.RequestedBy,
            RequestedAtUtc = request.RequestedAtUtc!.Value,
            ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T12:26:00Z"),
            Boundary = DeploymentExecutionBoundary.Executor
        };

    private static ReleaseExecutionReceipt CreateReleaseExecutionReceipt(
        DeploymentReadinessDecisionPackage package,
        DeploymentExecutionRequest request) => new()
        {
            ReleaseExecutionId = "release_exec_direct_be",
            ReleaseExecutionRequestId = "release_exec_request_be",
            ReleaseReadinessDecisionPackageId = "release_readiness_pkg_be",
            Repository = package.Repository,
            ReleaseSourceBranch = "main",
            CandidateCommitSha = package.CandidateCommitSha,
            CandidateVersion = package.CandidateVersion,
            CandidateTagName = package.CandidateTagName,
            ReleaseChannel = package.ReleaseChannel,
            PreState = null,
            PostState = null,
            ApprovedActions = [ReleaseExecutionAction.CreateTag],
            CompletedActions = [ReleaseExecutionAction.CreateTag],
            PreStateVerified = true,
            TagCreated = true,
            GitHubReleaseCreated = false,
            ReleaseArtifactsUploaded = false,
            PostStateVerified = true,
            DeploymentAttempted = false,
            PackagePublicationAttempted = false,
            MemoryPromotionAttempted = false,
            WorkflowContinuationAttempted = false,
            RollbackExecutionAttempted = false,
            ExecutionVerdict = ReleaseExecutionVerdict.ExecutedAndVerified,
            FailureClassification = ReleaseExecutionFailureKind.None,
            RequestedBy = request.RequestedBy,
            RequestedAtUtc = request.RequestedAtUtc!.Value,
            ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T12:24:00Z"),
            Boundary = ReleaseExecutionBoundary.Executor
        };

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

    private sealed class FakeDeploymentExecutionGateway : IDeploymentExecutionGateway
    {
        public Queue<DeploymentTargetObservedState> Observations { get; } = new();
        public DeploymentExecutionMutationResult? DeployResult { get; init; }
        public int ObserveCalls { get; private set; }
        public int DeployCalls { get; private set; }
        public string[] CallLog => _callLog.ToArray();
        public int PublishPackageCalls { get; }
        public int MemoryPromotionCalls { get; }
        public int ContinueWorkflowCalls { get; }
        public int SourceMutationCalls { get; }
        public int RollbackExecutionCalls { get; }

        private readonly List<string> _callLog = [];

        public Task<DeploymentTargetObservedState> ObserveAsync(
            DeploymentReadinessDecisionPackage package,
            DeploymentExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ObserveCalls++;
            _callLog.Add("observe");
            return Task.FromResult(Observations.Count > 0 ? Observations.Dequeue() : GoodPostState(package));
        }

        public Task<DeploymentExecutionMutationResult> DeployApprovedArtifactAsync(
            DeploymentReadinessDecisionPackage package,
            DeploymentExecutionRequest request,
            CancellationToken cancellationToken)
        {
            DeployCalls++;
            _callLog.Add("deploy");
            return Task.FromResult(DeployResult ?? new DeploymentExecutionMutationResult
            {
                Action = DeploymentExecutionAction.DeployApprovedArtifact,
                Attempted = true,
                Accepted = true,
                Provider = "FakeTestGateway",
                MutationName = "DeployApprovedArtifact",
                DeploymentTarget = package.DeploymentTarget,
                DeploymentEnvironment = package.DeploymentEnvironment,
                DeploymentId = "deployment-123",
                Message = "accepted",
                CompletedAtUtc = DateTimeOffset.Parse("2026-06-20T12:25:45Z")
            });
        }
    }
}
