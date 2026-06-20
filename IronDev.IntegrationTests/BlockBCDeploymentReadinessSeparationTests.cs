using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBCDeploymentReadinessSeparationTests
{
    private static readonly string CandidateCommitSha = new('d', 40);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockBC_Package_CreatesDeploymentReadinessSeparationPackage()
    {
        var artifacts = Build();

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageReadyForDeploymentReadinessDecision, artifacts.Package.PackageVerdict);
        Assert.IsTrue(artifacts.Package.CanProceedToDeploymentReadinessDecision);
        Assert.AreEqual("release_exec_bc", artifacts.Package.SourceReleaseExecutionReceiptId);
        Assert.AreEqual("owner/repo", artifacts.Package.Repository);
        Assert.AreEqual(CandidateCommitSha, artifacts.Package.CandidateCommitSha);
        Assert.AreEqual("1.2.3", artifacts.Package.CandidateVersion);
        Assert.AreEqual("v1.2.3", artifacts.Package.CandidateTagName);
        Assert.AreEqual("Stable", artifacts.Package.ReleaseChannel);
        Assert.AreEqual("production", artifacts.Package.DeploymentTarget);
        Assert.AreEqual("release-v1.2.3", artifacts.Package.DeploymentReadinessScope);
        Assert.AreEqual(DeploymentReadinessSeparationBoundary.Evidence, artifacts.Package.Boundary);
        Assert.IsTrue(artifacts.Receipt.BoundaryStatements.Any(statement => statement.Contains("Release execution is not deployment readiness", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBC_Package_RequiresReleaseExecutionReceipt()
    {
        var artifacts = Build(CreateInput() with { ReleaseExecutionReceipt = null });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageIncomplete, artifacts.Package.PackageVerdict);
        Assert.IsFalse(artifacts.Package.CanProceedToDeploymentReadinessDecision);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.MissingReleaseExecutionReceipt);
    }

    [TestMethod]
    public void BlockBC_Package_RequiresExecutedAndVerifiedRelease()
    {
        var artifacts = BuildWithReceipt(CreateExecutedReceipt() with { ExecutionVerdict = ReleaseExecutionVerdict.Blocked });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.ReleaseExecutionNotVerified);
    }

    [TestMethod]
    public void BlockBC_Package_BlocksFailedReleaseExecution()
    {
        var artifacts = BuildWithReceipt(CreateExecutedReceipt() with
        {
            ExecutionVerdict = ReleaseExecutionVerdict.Failed,
            FailureClassification = ReleaseExecutionFailureKind.ReleaseMutationFailed
        });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.ReleaseExecutionFailed);
    }

    [TestMethod]
    public void BlockBC_Package_BlocksPartialReleaseExecution()
    {
        var artifacts = BuildWithReceipt(CreateExecutedReceipt() with { ExecutionVerdict = ReleaseExecutionVerdict.PartiallyExecuted });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.ReleaseExecutionPartial);
    }

    [TestMethod]
    public void BlockBC_Package_BlocksUnverifiedPostState()
    {
        var artifacts = BuildWithReceipt(CreateExecutedReceipt() with { PostStateVerified = false });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.ReleaseExecutionNotVerified);
    }

    [TestMethod]
    public void BlockBC_Package_BlocksIdentityMismatch()
    {
        var cases = new (string Name, DeploymentReadinessSeparationInput Input, DeploymentReadinessSeparationBlockReason Reason)[]
        {
            ("repo", CreateInput() with { Repository = "other/repo" }, DeploymentReadinessSeparationBlockReason.RepositoryMismatch),
            ("commit", CreateInput() with { CandidateCommitSha = new string('e', 40) }, DeploymentReadinessSeparationBlockReason.CandidateCommitMismatch),
            ("version", CreateInput() with { CandidateVersion = "1.2.4" }, DeploymentReadinessSeparationBlockReason.CandidateVersionMismatch),
            ("tag", CreateInput() with { CandidateTagName = "v1.2.4" }, DeploymentReadinessSeparationBlockReason.CandidateTagMismatch),
            ("channel", CreateInput() with { ReleaseChannel = "Preview" }, DeploymentReadinessSeparationBlockReason.ReleaseChannelMismatch)
        };

        foreach (var item in cases)
        {
            var artifacts = Build(item.Input);
            Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict, item.Name);
            AssertContainsReason(artifacts.Package, item.Reason);
        }
    }

    [TestMethod]
    public void BlockBC_Package_BlocksDeploymentAlreadyAttempted()
    {
        var artifacts = BuildWithReceipt(CreateExecutedReceipt() with { DeploymentAttempted = true });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.DeploymentAlreadyAttempted);
    }

    [TestMethod]
    public void BlockBC_Package_BlocksPackagePublicationAlreadyAttempted()
    {
        var artifacts = BuildWithReceipt(CreateExecutedReceipt() with { PackagePublicationAttempted = true });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.PackagePublicationAlreadyAttempted);
    }

    [TestMethod]
    public void BlockBC_Package_BlocksMemoryPromotionAlreadyAttempted()
    {
        var artifacts = BuildWithReceipt(CreateExecutedReceipt() with { MemoryPromotionAttempted = true });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.MemoryPromotionAlreadyAttempted);
    }

    [TestMethod]
    public void BlockBC_Package_BlocksWorkflowContinuationAlreadyAttempted()
    {
        var artifacts = BuildWithReceipt(CreateExecutedReceipt() with { WorkflowContinuationAttempted = true });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.WorkflowContinuationAlreadyAttempted);
    }

    [TestMethod]
    public void BlockBC_Package_BlocksRollbackExecutionAlreadyAttempted()
    {
        var artifacts = BuildWithReceipt(CreateExecutedReceipt() with { RollbackExecutionAttempted = true });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.RollbackExecutionAlreadyAttempted);
    }

    [TestMethod]
    public void BlockBC_Package_BlocksReleaseExecutionBoundaryWithDeploymentAuthority()
    {
        var cases = new (string Name, ReleaseExecutionBoundary Boundary, DeploymentReadinessSeparationBlockReason Reason)[]
        {
            ("deploy", ReleaseExecutionBoundary.Executor with { CanDeploy = true }, DeploymentReadinessSeparationBlockReason.DeploymentMutationNotAllowed),
            ("publish", ReleaseExecutionBoundary.Executor with { CanPublishPackages = true }, DeploymentReadinessSeparationBlockReason.PublishPackagesNotAllowed),
            ("continue", ReleaseExecutionBoundary.Executor with { CanContinueWorkflow = true }, DeploymentReadinessSeparationBlockReason.WorkflowContinuationNotAllowed),
            ("rollback", ReleaseExecutionBoundary.Executor with { CanExecuteRollback = true }, DeploymentReadinessSeparationBlockReason.RollbackExecutionNotAllowed)
        };

        foreach (var item in cases)
        {
            var artifacts = BuildWithReceipt(CreateExecutedReceipt() with { Boundary = item.Boundary });

            Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageBlocked, artifacts.Package.PackageVerdict, item.Name);
            AssertContainsReason(artifacts.Package, item.Reason);
        }
    }

    [TestMethod]
    public void BlockBC_Package_RequiresDeploymentTarget()
    {
        var artifacts = Build(CreateInput() with { DeploymentTarget = " " });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageIncomplete, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.MissingDeploymentTargetDeclaration);
    }

    [TestMethod]
    public void BlockBC_Package_RequiresDeploymentReadinessScope()
    {
        var artifacts = Build(CreateInput() with { DeploymentReadinessScope = " " });

        Assert.AreEqual(DeploymentReadinessSeparationVerdict.PackageIncomplete, artifacts.Package.PackageVerdict);
        AssertContainsReason(artifacts.Package, DeploymentReadinessSeparationBlockReason.MissingDeploymentReadinessScope);
    }

    [TestMethod]
    public void BlockBC_Boundary_RemainsEvidenceOnly()
    {
        var boundary = DeploymentReadinessSeparationBoundary.Evidence;

        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanDecideDeploymentReadiness);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanPublishPackages);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanExecuteRollback);
        Assert.IsFalse(boundary.CanDispatchPipeline);
        Assert.IsFalse(boundary.CanMutateEnvironment);
    }

    [TestMethod]
    public void BlockBC_DoesNotBecomeDeploymentReadinessDecision()
    {
        var package = Build().Package;

        Assert.IsTrue(package.CanProceedToDeploymentReadinessDecision);
        Assert.IsFalse(package.Boundary.CanDecideDeploymentReadiness);
        Assert.IsFalse(DeploymentReadinessSeparationBypassEvaluator.CanDecideDeploymentReadiness(package));
        Assert.IsFalse(DeploymentReadinessSeparationBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(DeploymentReadinessSeparationBypassEvaluator.CanPublishPackages(package));
        Assert.IsFalse(DeploymentReadinessSeparationBypassEvaluator.CanContinueWorkflow(package));
    }

    [TestMethod]
    public async Task BlockBC_Cli_ReturnsZeroOnlyForReadySeparationPackage()
    {
        var receiptPath = WriteReceipt(CreateExecutedReceipt());
        var readyOut = Path.Combine(Path.GetTempPath(), $"bc-ready-{Guid.NewGuid():N}");

        var ready = await RunCliAsync(PackageArgs(receiptPath, readyOut)).ConfigureAwait(false);

        Assert.AreEqual(0, ready.ExitCode, ready.Error);
        Assert.IsTrue(File.Exists(Path.Combine(readyOut, "deployment-readiness-separation-package.json")));
        var events = File.ReadAllText(Path.Combine(readyOut, FileBackedGovernanceEventStore.ArtifactName));
        StringAssert.Contains(events, "DeploymentReadinessSeparationPackageCreated");

        var blockedReceiptPath = WriteReceipt(CreateExecutedReceipt() with { DeploymentAttempted = true });
        var blockedOut = Path.Combine(Path.GetTempPath(), $"bc-blocked-{Guid.NewGuid():N}");

        var blocked = await RunCliAsync(PackageArgs(blockedReceiptPath, blockedOut)).ConfigureAwait(false);

        Assert.AreEqual(1, blocked.ExitCode, blocked.Output + blocked.Error);

        var invalid = await RunCliAsync("deployment-readiness-separation", "package", "--release-execution-receipt", receiptPath).ConfigureAwait(false);
        Assert.AreEqual(2, invalid.ExitCode, invalid.Output + invalid.Error);
    }

    [TestMethod]
    public async Task BlockBC_Cli_RejectsDeployPublishPromoteContinueCommitPushRollbackVerbs()
    {
        foreach (var forbidden in new[]
        {
            "deploy",
            "execute",
            "publish",
            "publish-package",
            "promote",
            "promote-memory",
            "continue",
            "continue-workflow",
            "dispatch",
            "trigger-pipeline",
            "commit",
            "push",
            "merge",
            "source-apply",
            "rollback",
            "rollback-execute"
        })
        {
            var result = await RunCliAsync("deployment-readiness-separation", forbidden, "--package", "deployment-readiness-separation-package.json").ConfigureAwait(false);

            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockBC_StaticBoundary_NoDeploymentPipelineOrPublishSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliDeploymentReadinessSeparation.cs"));
        var model = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "DeploymentReadinessSeparation.cs"));
        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "BC_DEPLOYMENT_READINESS_SEPARATION.md"));

        Assert.IsFalse(cli.Contains("RunProcessAsync", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("ProcessStartInfo", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh api", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("kubectl", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("terraform apply", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("az webapp", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("docker push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("dotnet nuget push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("npm publish", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(model, "CanProceedToDeploymentReadinessDecision");
        StringAssert.Contains(model, "CanDeploy(object? evidence) => false");
        StringAssert.Contains(model, "CanDispatchPipeline(object? evidence) => false");
        StringAssert.Contains(receipt, "Release execution is not deployment readiness.");
        StringAssert.Contains(receipt, "Release execution receipt is not deployment authority.");
        StringAssert.Contains(receipt, "Deployment readiness separation is not deployment readiness decision.");
        StringAssert.Contains(receipt, "BC does not deploy.");
        StringAssert.Contains(receipt, "BC does not dispatch deployment pipelines.");
        StringAssert.Contains(receipt, "BC does not execute rollback.");
    }

    private static DeploymentReadinessSeparationArtifacts Build(DeploymentReadinessSeparationInput? input = null) =>
        DeploymentReadinessSeparationPackageBuilder.Build(input ?? CreateInput());

    private static DeploymentReadinessSeparationArtifacts BuildWithReceipt(ReleaseExecutionReceipt receipt) =>
        Build(CreateInput() with { ReleaseExecutionReceipt = receipt });

    private static DeploymentReadinessSeparationInput CreateInput() => new()
    {
        ReleaseExecutionReceipt = CreateExecutedReceipt(),
        Repository = "owner/repo",
        CandidateCommitSha = CandidateCommitSha,
        CandidateVersion = "1.2.3",
        CandidateTagName = "v1.2.3",
        ReleaseChannel = "Stable",
        DeploymentTarget = "production",
        DeploymentReadinessScope = "release-v1.2.3",
        CreatedBy = "release-captain",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T12:00:00Z")
    };

    private static ReleaseExecutionReceipt CreateExecutedReceipt() => new()
    {
        ReleaseExecutionId = "release_exec_bc",
        ReleaseExecutionRequestId = "release_exec_request_bc",
        ReleaseReadinessDecisionPackageId = "release_readiness_pkg_bc",
        Repository = "owner/repo",
        ReleaseSourceBranch = "main",
        CandidateCommitSha = CandidateCommitSha,
        CandidateVersion = "1.2.3",
        CandidateTagName = "v1.2.3",
        ReleaseChannel = "Stable",
        PreState = ObservedState(existingRelease: false),
        PostState = ObservedState(existingRelease: true),
        ApprovedActions =
        [
            ReleaseExecutionAction.CreateTag,
            ReleaseExecutionAction.CreateGitHubRelease,
            ReleaseExecutionAction.UploadReleaseArtifacts
        ],
        CompletedActions =
        [
            ReleaseExecutionAction.CreateTag,
            ReleaseExecutionAction.CreateGitHubRelease,
            ReleaseExecutionAction.UploadReleaseArtifacts
        ],
        MutationResults = [],
        PreStateVerified = true,
        TagCreated = true,
        GitHubReleaseCreated = true,
        ReleaseArtifactsUploaded = true,
        CreatedTagSha = CandidateCommitSha,
        GitHubReleaseId = "release-123",
        GitHubReleaseUrl = "https://github.com/owner/repo/releases/tag/v1.2.3",
        UploadedArtifacts = ["artifact.zip"],
        PostStateVerified = true,
        DeploymentAttempted = false,
        PackagePublicationAttempted = false,
        MemoryPromotionAttempted = false,
        WorkflowContinuationAttempted = false,
        RollbackExecutionAttempted = false,
        ExecutionVerdict = ReleaseExecutionVerdict.ExecutedAndVerified,
        FailureClassification = ReleaseExecutionFailureKind.None,
        Issues = [],
        RequestedBy = "release-captain",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T11:10:00Z"),
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T11:11:00Z"),
        Boundary = ReleaseExecutionBoundary.Executor
    };

    private static ReleaseExecutionObservedState ObservedState(bool existingRelease) => new()
    {
        Repository = "owner/repo",
        ReleaseSourceBranch = "main",
        ReleaseSourceHeadSha = CandidateCommitSha,
        CandidateCommitSha = CandidateCommitSha,
        CommitPresentOnReleaseSource = true,
        CandidateTagName = "v1.2.3",
        ExistingTagFound = existingRelease,
        ExistingTagSha = existingRelease ? CandidateCommitSha : null,
        ExistingReleaseFound = existingRelease,
        ExistingReleaseId = existingRelease ? "release-123" : null,
        ExistingReleaseUrl = existingRelease ? "https://github.com/owner/repo/releases/tag/v1.2.3" : null,
        ExistingReleaseArtifactNames = existingRelease ? ["artifact.zip"] : [],
        ObservedAtUtc = existingRelease
            ? DateTimeOffset.Parse("2026-06-20T11:11:00Z")
            : DateTimeOffset.Parse("2026-06-20T11:10:00Z"),
        ObservationSource = "test",
        ObservationSucceeded = true
    };

    private static void AssertContainsReason(
        DeploymentReadinessSeparationPackage package,
        DeploymentReadinessSeparationBlockReason reason) =>
        Assert.IsTrue(package.BlockReasons.Contains(reason), $"Expected {reason}; actual: {string.Join(", ", package.BlockReasons)}");

    private static string WriteReceipt(ReleaseExecutionReceipt receipt)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bc-release-execution-receipt-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, JsonOptions));
        return path;
    }

    private static string[] PackageArgs(string receiptPath, string outDir) =>
    [
        "deployment-readiness-separation",
        "package",
        "--release-execution-receipt",
        receiptPath,
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
        "--scope",
        "release-v1.2.3",
        "--created-by",
        "release-captain",
        "--out",
        outDir,
        "--json"
    ];

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
