using System.Security.Cryptography;
using System.Text;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBBControlledReleaseExecutorTests
{
    private static readonly string CandidateCommitSha = new('c', 40);
    private const string ReleaseNotesBody = "Release notes for the controlled BB release executor.";

    [TestMethod]
    public async Task BlockBB_Executor_ExecutesApprovedTagReleaseArtifactActionsAndWritesReceipt()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var gateway = new FakeReleaseExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package, request));

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.ExecutedAndVerified, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.None, result.FailureKind);
        Assert.IsNotNull(result.Receipt);
        Assert.IsTrue(result.Receipt!.PreStateVerified);
        Assert.IsTrue(result.Receipt.TagCreated);
        Assert.IsTrue(result.Receipt.GitHubReleaseCreated);
        Assert.IsTrue(result.Receipt.ReleaseArtifactsUploaded);
        Assert.IsTrue(result.Receipt.PostStateVerified);
        Assert.AreEqual(2, gateway.ObserveCalls);
        Assert.AreEqual(1, gateway.CreateTagCalls);
        Assert.AreEqual(1, gateway.CreateReleaseCalls);
        Assert.AreEqual(1, gateway.UploadArtifactCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_RequiresEligibleBAReleaseReadinessPackage()
    {
        var ready = CreatePackage();
        var cases = new (string Name, ReleaseReadinessDecisionPackage? Package)[]
        {
            ("missing", null),
            ("incomplete", ready with { PackageVerdict = ReleaseReadinessDecisionPackageVerdict.PackageIncomplete }),
            ("blocked", ready with { PackageVerdict = ReleaseReadinessDecisionPackageVerdict.PackageBlocked, BlockReasons = [ReleaseReadinessDecisionPackageBlockReason.FinalReleaseValidationFailed] }),
            ("rejected", ready with { PackageVerdict = ReleaseReadinessDecisionPackageVerdict.PackageRejected }),
            ("cannot-release", ready with { CanReleaseForExecutor = false }),
            ("boundary-release", ready with { Boundary = ready.Boundary with { CanRelease = true } }),
            ("boundary-deploy", ready with { Boundary = ready.Boundary with { CanDeploy = true } })
        };

        foreach (var item in cases)
        {
            var gateway = new FakeReleaseExecutionGateway();
            var result = await ControlledReleaseExecutor.ExecuteAsync(item.Package, CreateRequest(ready), gateway).ConfigureAwait(false);

            Assert.AreNotEqual(ReleaseExecutionVerdict.ExecutedAndVerified, result.Verdict, item.Name);
            Assert.AreEqual(0, gateway.ObserveCalls, item.Name);
            Assert.AreEqual(0, gateway.TotalMutationCalls, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockBB_Executor_RequiresExplicitExecutionRequest()
    {
        var gateway = new FakeReleaseExecutionGateway();

        var result = await ControlledReleaseExecutor.ExecuteAsync(CreatePackage(), null, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.MissingExecutionRequest, result.FailureKind);
        Assert.IsNull(result.Receipt);
        Assert.AreEqual(0, gateway.ObserveCalls);
        Assert.AreEqual(0, gateway.TotalMutationCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_BlocksWhenRequestIdentityDoesNotMatchBAPackage()
    {
        var package = CreatePackage();
        var cases = new (string Name, ReleaseExecutionRequest Request, ReleaseExecutionFailureKind Expected)[]
        {
            ("package-id", CreateRequest(package) with { ReleaseReadinessDecisionPackageId = "other-package" }, ReleaseExecutionFailureKind.RequestPackageMismatch),
            ("repo", CreateRequest(package) with { Repository = "other/repo" }, ReleaseExecutionFailureKind.RepositoryMismatch),
            ("commit", CreateRequest(package) with { CandidateCommitSha = new string('d', 40) }, ReleaseExecutionFailureKind.CandidateCommitMismatch),
            ("version", CreateRequest(package) with { CandidateVersion = "1.2.4" }, ReleaseExecutionFailureKind.CandidateVersionMismatch),
            ("tag", CreateRequest(package) with { CandidateTagName = "v1.2.4" }, ReleaseExecutionFailureKind.CandidateTagMismatch),
            ("branch", CreateRequest(package) with { ReleaseSourceBranch = "release/1.2" }, ReleaseExecutionFailureKind.ReleaseSourceBranchMismatch),
            ("channel", CreateRequest(package) with { ReleaseChannel = "Preview" }, ReleaseExecutionFailureKind.ReleaseChannelMismatch),
            ("not-confirmed", CreateRequest(package) with { ConfirmReleaseExecution = false }, ReleaseExecutionFailureKind.ReleaseExecutionNotConfirmed)
        };

        foreach (var item in cases)
        {
            var gateway = new FakeReleaseExecutionGateway();
            var result = await ControlledReleaseExecutor.ExecuteAsync(package, item.Request, gateway).ConfigureAwait(false);

            Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.AreEqual(item.Expected, result.FailureKind, item.Name);
            Assert.AreEqual(0, gateway.ObserveCalls, item.Name);
            Assert.AreEqual(0, gateway.TotalMutationCalls, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockBB_Executor_ReobservesCurrentSourceBeforeMutation()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var gateway = new FakeReleaseExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package, request));

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.ExecutedAndVerified, result.Verdict);
        Assert.AreEqual(2, gateway.ObserveCalls);
        Assert.AreEqual(1, gateway.CreateTagCalls);
        Assert.IsTrue(result.Receipt!.PreStateVerified);
    }

    [TestMethod]
    public async Task BlockBB_Executor_BlocksWhenSourceBranchMoved()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var gateway = new FakeReleaseExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package) with { ReleaseSourceHeadSha = new string('d', 40), CommitPresentOnReleaseSource = false });

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.SourceBranchMoved, result.FailureKind);
        Assert.IsFalse(result.Receipt!.TagCreated);
        Assert.AreEqual(1, gateway.ObserveCalls);
        Assert.AreEqual(0, gateway.TotalMutationCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_BlocksExistingCandidateTag()
    {
        var package = CreatePackage();
        var gateway = new FakeReleaseExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package) with { ExistingTagFound = true, ExistingTagSha = CandidateCommitSha });

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, CreateRequest(package), gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.CandidateTagAlreadyExists, result.FailureKind);
        Assert.AreEqual(0, gateway.TotalMutationCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_BlocksExistingCandidateRelease()
    {
        var package = CreatePackage();
        var gateway = new FakeReleaseExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package) with { ExistingReleaseFound = true, ExistingReleaseId = "release-1" });

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, CreateRequest(package), gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.CandidateReleaseAlreadyExists, result.FailureKind);
        Assert.AreEqual(0, gateway.TotalMutationCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_BlocksReleaseCreationWithoutExplicitTagCreation()
    {
        var package = CreatePackage();
        var request = CreateRequest(package) with { ApprovedActions = [ReleaseExecutionAction.CreateGitHubRelease] };
        var gateway = new FakeReleaseExecutionGateway();

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.ReleaseCreationRequiresTagCreation, result.FailureKind);
        Assert.AreEqual(0, gateway.ObserveCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_BlocksArtifactUploadWithoutReleaseCreation()
    {
        var package = CreatePackage();
        var request = CreateRequest(package) with { ApprovedActions = [ReleaseExecutionAction.CreateTag, ReleaseExecutionAction.UploadReleaseArtifacts] };
        var gateway = new FakeReleaseExecutionGateway();

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.ArtifactUploadRequiresReleaseCreation, result.FailureKind);
        Assert.AreEqual(0, gateway.ObserveCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_BlocksMissingReleaseNotesForGitHubRelease()
    {
        var package = CreatePackage();
        var request = CreateRequest(package) with { ReleaseNotesBody = null, ReleaseNotesPath = null, ReleaseNotesSha256 = null };
        var gateway = new FakeReleaseExecutionGateway();

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.MissingReleaseNotes, result.FailureKind);
        Assert.AreEqual(0, gateway.ObserveCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_BlocksMissingArtifact()
    {
        var package = CreatePackage();
        var request = CreateRequest(package) with
        {
            Artifacts = [new ReleaseExecutionArtifact { Name = "missing.zip", Path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.zip"), Sha256 = new string('f', 64) }]
        };
        var gateway = new FakeReleaseExecutionGateway();

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.MissingArtifact, result.FailureKind);
        Assert.AreEqual(0, gateway.ObserveCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_BlocksArtifactChecksumMismatch()
    {
        var package = CreatePackage();
        var artifact = CreateArtifact();
        var request = CreateRequest(package) with
        {
            Artifacts = [artifact with { Sha256 = new string('0', 64) }]
        };
        var gateway = new FakeReleaseExecutionGateway();

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.ArtifactChecksumMismatch, result.FailureKind);
        Assert.AreEqual(0, gateway.ObserveCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_PostVerifiesCreatedTagReleaseAndArtifacts()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var gateway = new FakeReleaseExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package, request) with { ExistingReleaseFound = false, ExistingReleaseArtifactNames = [] });

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.Failed, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.PostStateVerificationFailed, result.FailureKind);
        Assert.IsFalse(result.Receipt!.PostStateVerified);
        Assert.AreEqual(2, gateway.ObserveCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_RecordsPartialExecutionWithoutRollbackOrContinuation()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var gateway = new FakeReleaseExecutionGateway
        {
            CreateReleaseResult = FailedMutation(ReleaseExecutionAction.CreateGitHubRelease, "release creation failed")
        };
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPreState(package) with { ExistingTagFound = true, ExistingTagSha = CandidateCommitSha });

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.PartiallyExecuted, result.Verdict);
        Assert.AreEqual(ReleaseExecutionFailureKind.ReleaseMutationFailed, result.FailureKind);
        Assert.IsTrue(result.Receipt!.TagCreated);
        Assert.IsFalse(result.Receipt.GitHubReleaseCreated);
        Assert.IsFalse(result.Receipt.RollbackExecutionAttempted);
        Assert.IsFalse(result.Receipt.WorkflowContinuationAttempted);
        Assert.AreEqual(1, gateway.CreateTagCalls);
        Assert.AreEqual(1, gateway.CreateReleaseCalls);
        Assert.AreEqual(0, gateway.UploadArtifactCalls);
    }

    [TestMethod]
    public async Task BlockBB_Executor_DoesNotDeployPublishPackagesPromoteMemoryOrContinueWorkflow()
    {
        var package = CreatePackage();
        var request = CreateRequest(package);
        var gateway = new FakeReleaseExecutionGateway();
        gateway.Observations.Enqueue(GoodPreState(package));
        gateway.Observations.Enqueue(GoodPostState(package, request));

        var result = await ControlledReleaseExecutor.ExecuteAsync(package, request, gateway).ConfigureAwait(false);

        Assert.AreEqual(ReleaseExecutionVerdict.ExecutedAndVerified, result.Verdict);
        Assert.IsFalse(result.Receipt!.Boundary.CanDeploy);
        Assert.IsFalse(result.Receipt.Boundary.CanPublishPackages);
        Assert.IsFalse(result.Receipt.Boundary.CanPromoteMemory);
        Assert.IsFalse(result.Receipt.Boundary.CanContinueWorkflow);
        Assert.IsFalse(result.Receipt.Boundary.CanCommit);
        Assert.IsFalse(result.Receipt.Boundary.CanPush);
        Assert.IsFalse(result.Receipt.Boundary.CanMerge);
        Assert.IsFalse(result.Receipt.Boundary.CanExecuteRollback);
        Assert.IsFalse(result.Receipt.DeploymentAttempted);
        Assert.IsFalse(result.Receipt.PackagePublicationAttempted);
        Assert.IsFalse(result.Receipt.MemoryPromotionAttempted);
        Assert.IsFalse(result.Receipt.WorkflowContinuationAttempted);
        Assert.AreEqual(0, gateway.DeployCalls);
        Assert.AreEqual(0, gateway.PublishPackageCalls);
        Assert.AreEqual(0, gateway.MemoryPromotionCalls);
        Assert.AreEqual(0, gateway.ContinueWorkflowCalls);
        Assert.AreEqual(0, gateway.CommitCalls);
        Assert.AreEqual(0, gateway.PushCalls);
        Assert.AreEqual(0, gateway.MergeCalls);
        Assert.AreEqual(0, gateway.RollbackExecutionCalls);
    }

    [TestMethod]
    public void BlockBB_Cli_ReturnsZeroOnlyForExecutedAndVerified()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReleaseExecution.cs"));

        StringAssert.Contains(cli, "result.Verdict == ReleaseExecutionVerdict.ExecutedAndVerified ? 0 : 1");
    }

    [TestMethod]
    public async Task BlockBB_Cli_RejectsDeployPublishPackagePromoteMemoryContinueCommitPushMergeRollbackVerbs()
    {
        foreach (var forbidden in new[] { "deploy", "publish-package", "promote-memory", "continue", "continue-workflow", "commit", "push", "merge", "source-apply", "rollback", "rollback-execute" })
        {
            var result = await RunCliAsync("release-execution", forbidden, "--release-readiness-package", "release-readiness-decision-package.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockBB_StaticBoundary_ReleaseExecutionSurfaceIsOnlyTagReleaseArtifact()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReleaseExecution.cs"));
        Assert.IsFalse(cli.Contains("dotnet nuget push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("npm publish", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("docker push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("kubectl", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("az webapp", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("terraform apply", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git commit", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr merge", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(cli, "repos/{request.Repository}/git/refs");
        StringAssert.Contains(cli, "repos/{request.Repository}/releases");
        StringAssert.Contains(cli, "\"release\", \"upload\"");

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "BB_CONTROLLED_RELEASE_EXECUTOR.md"));
        StringAssert.Contains(receipt, "Release readiness decision package is not release execution.");
        StringAssert.Contains(receipt, "Release execution is not deployment.");
        StringAssert.Contains(receipt, "Release execution is not package publication.");
        StringAssert.Contains(receipt, "Release execution receipt is not deployment authority.");
        StringAssert.Contains(receipt, "Release execution receipt is not package publication authority.");
        StringAssert.Contains(receipt, "Release execution receipt is not workflow continuation authority.");
        StringAssert.Contains(receipt, "No implicit tag creation through release creation.");
        StringAssert.Contains(receipt, "No hidden deployment.");
        StringAssert.Contains(receipt, "No hidden package publication.");
        StringAssert.Contains(receipt, "No hidden memory promotion.");
        StringAssert.Contains(receipt, "No hidden workflow continuation.");
    }

    private static ReleaseReadinessDecisionPackage CreatePackage() => new()
    {
        ReleaseReadinessDecisionPackageId = "release_readiness_pkg_bb",
        Repository = "owner/repo",
        ReleaseSourceBranch = "main",
        CandidateCommitSha = CandidateCommitSha,
        CandidateVersion = "1.2.3",
        CandidateTagName = "v1.2.3",
        ReleaseChannel = "Stable",
        SourceReleaseCandidatePackageId = "release_candidate_pkg_bb",
        SourceMergeExecutionReceiptId = "merge_exec_bb",
        SourceMergeDecisionPackageId = "merge_decision_pkg_bb",
        CurrentReleaseSourceState = new CurrentReleaseSourceState
        {
            Repository = "owner/repo",
            ReleaseSourceBranch = "main",
            ReleaseSourceHeadSha = CandidateCommitSha,
            CandidateCommitSha = CandidateCommitSha,
            DefaultBranch = "main",
            DefaultBranchHeadSha = CandidateCommitSha,
            CommitPresentOnReleaseSource = true,
            CommitPresentOnDefaultBranch = true,
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T11:00:00Z"),
            ObservationSource = "test",
            ObservationSucceeded = true
        },
        CurrentTagReleaseState = new CurrentTagReleaseState
        {
            Repository = "owner/repo",
            CandidateVersion = "1.2.3",
            CandidateTagName = "v1.2.3",
            ExistingTagFound = false,
            ExistingReleaseFound = false,
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T11:01:00Z"),
            ObservationSource = "test",
            ObservationSucceeded = true
        },
        FinalReleaseValidationEvidence = new FinalReleaseValidationEvidence
        {
            ValidationRunId = "validation_run_bb",
            ValidationPlanId = "validation_plan_bb",
            CommitSha = CandidateCommitSha,
            Verdict = ValidationRunVerdict.Passed,
            RequiredLaneNames = ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies,
            ResultLaneNames = ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies,
            MissingLaneNames = [],
            FailedLaneNames = [],
            StartedAtUtc = DateTimeOffset.Parse("2026-06-20T11:02:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-06-20T11:03:00Z"),
            ValidationEvidenceReceiptId = "validation_run_bb"
        },
        ReleaseArtifactReadinessEvidence = new ReleaseArtifactReadinessEvidence
        {
            ArtifactManifestId = "artifact_manifest_bb",
            BuildRunId = "build_bb",
            CommitSha = CandidateCommitSha,
            Artifacts = ["artifact.zip"],
            Checksums = [new string('f', 64)],
            StorageLocation = "local-test",
            ArtifactsRequired = true,
            ArtifactsReady = true,
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T11:04:00Z")
        },
        ReleaseReadinessDecision = new ReleaseReadinessDecisionEvidence
        {
            ReleaseReadinessDecisionId = "release_readiness_decision_bb",
            Decision = ReleaseReadinessDecision.ApprovedForReleaseExecutor,
            DecisionMadeBy = "release-captain",
            DecisionMadeAtUtc = DateTimeOffset.Parse("2026-06-20T11:05:00Z"),
            DecisionRationale = "Current release candidate evidence is ready for controlled release execution.",
            ExpectedRepository = "owner/repo",
            ExpectedCandidateCommitSha = CandidateCommitSha,
            ExpectedVersion = "1.2.3",
            ExpectedTagName = "v1.2.3",
            ExpectedReleaseSourceBranch = "main",
            ExpectedReleaseChannel = "Stable",
            ExpectedArtifactManifestId = "artifact_manifest_bb",
            ExpectedReleaseCandidatePackageId = "release_candidate_pkg_bb"
        },
        PackageVerdict = ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor,
        CanReleaseForExecutor = true,
        CreatedBy = "release-captain",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T11:06:00Z"),
        Boundary = ReleaseReadinessDecisionPackageBoundary.Evidence
    };

    private static ReleaseExecutionRequest CreateRequest(ReleaseReadinessDecisionPackage package)
    {
        var artifact = CreateArtifact();
        return new ReleaseExecutionRequest
        {
            ReleaseExecutionRequestId = "release_exec_request_bb",
            ReleaseReadinessDecisionPackageId = package.ReleaseReadinessDecisionPackageId,
            Repository = package.Repository,
            ReleaseSourceBranch = package.ReleaseSourceBranch,
            CandidateCommitSha = package.CandidateCommitSha,
            CandidateVersion = package.CandidateVersion,
            CandidateTagName = package.CandidateTagName,
            ReleaseChannel = package.ReleaseChannel,
            ApprovedActions =
            [
                ReleaseExecutionAction.CreateTag,
                ReleaseExecutionAction.CreateGitHubRelease,
                ReleaseExecutionAction.UploadReleaseArtifacts
            ],
            ConfirmReleaseExecution = true,
            ReleaseName = "IronDev 1.2.3",
            ReleaseNotesBody = ReleaseNotesBody,
            ReleaseNotesSha256 = HashText(ReleaseNotesBody),
            Artifacts = [artifact],
            RequestedBy = "release-captain",
            RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T11:10:00Z")
        };
    }

    private static ReleaseExecutionArtifact CreateArtifact()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bb-artifact-{Guid.NewGuid():N}.zip");
        File.WriteAllText(path, "controlled release artifact");
        return new ReleaseExecutionArtifact
        {
            Name = "artifact.zip",
            Path = path,
            Sha256 = HashFile(path),
            ContentType = "application/zip"
        };
    }

    private static ReleaseExecutionObservedState GoodPreState(ReleaseReadinessDecisionPackage package) => new()
    {
        Repository = package.Repository,
        ReleaseSourceBranch = package.ReleaseSourceBranch,
        ReleaseSourceHeadSha = package.CandidateCommitSha,
        CandidateCommitSha = package.CandidateCommitSha,
        CommitPresentOnReleaseSource = true,
        CandidateTagName = package.CandidateTagName,
        ExistingTagFound = false,
        ExistingReleaseFound = false,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T11:10:30Z"),
        ObservationSource = "test",
        ObservationSucceeded = true
    };

    private static ReleaseExecutionObservedState GoodPostState(
        ReleaseReadinessDecisionPackage package,
        ReleaseExecutionRequest request) => GoodPreState(package) with
        {
            ExistingTagFound = true,
            ExistingTagSha = package.CandidateCommitSha,
            ExistingReleaseFound = true,
            ExistingReleaseId = "release-123",
            ExistingReleaseUrl = "https://github.com/owner/repo/releases/tag/v1.2.3",
            ExistingReleaseArtifactNames = request.Artifacts.Select(item => item.Name).ToArray(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T11:11:00Z")
        };

    private static ReleaseExecutionMutationResult FailedMutation(ReleaseExecutionAction action, string error) => new()
    {
        Action = action,
        Attempted = true,
        Accepted = false,
        Provider = "FakeTestGateway",
        CommandOrMutationName = action.ToString(),
        Target = "v1.2.3",
        Error = error,
        CompletedAtUtc = DateTimeOffset.Parse("2026-06-20T11:10:45Z")
    };

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

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

    private sealed class FakeReleaseExecutionGateway : IReleaseExecutionGateway
    {
        public Queue<ReleaseExecutionObservedState> Observations { get; } = new();
        public ReleaseExecutionMutationResult? CreateTagResult { get; init; }
        public ReleaseExecutionMutationResult? CreateReleaseResult { get; init; }
        public ReleaseExecutionMutationResult? UploadArtifactResult { get; init; }
        public int ObserveCalls { get; private set; }
        public int CreateTagCalls { get; private set; }
        public int CreateReleaseCalls { get; private set; }
        public int UploadArtifactCalls { get; private set; }
        public int TotalMutationCalls => CreateTagCalls + CreateReleaseCalls + UploadArtifactCalls;
        public int DeployCalls { get; }
        public int PublishPackageCalls { get; }
        public int MemoryPromotionCalls { get; }
        public int ContinueWorkflowCalls { get; }
        public int CommitCalls { get; }
        public int PushCalls { get; }
        public int MergeCalls { get; }
        public int RollbackExecutionCalls { get; }

        public Task<ReleaseExecutionObservedState> ObserveAsync(
            ReleaseReadinessDecisionPackage package,
            ReleaseExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ObserveCalls++;
            return Task.FromResult(Observations.Count > 0 ? Observations.Dequeue() : GoodPostState(package, request));
        }

        public Task<ReleaseExecutionMutationResult> CreateTagAsync(
            ReleaseReadinessDecisionPackage package,
            ReleaseExecutionRequest request,
            CancellationToken cancellationToken)
        {
            CreateTagCalls++;
            return Task.FromResult(CreateTagResult ?? Accepted(ReleaseExecutionAction.CreateTag, request.CandidateTagName, request.CandidateCommitSha, null, []));
        }

        public Task<ReleaseExecutionMutationResult> CreateGitHubReleaseAsync(
            ReleaseReadinessDecisionPackage package,
            ReleaseExecutionRequest request,
            CancellationToken cancellationToken)
        {
            CreateReleaseCalls++;
            return Task.FromResult(CreateReleaseResult ?? Accepted(ReleaseExecutionAction.CreateGitHubRelease, request.CandidateTagName, "release-123", "https://github.com/owner/repo/releases/tag/v1.2.3", []));
        }

        public Task<ReleaseExecutionMutationResult> UploadReleaseArtifactsAsync(
            ReleaseReadinessDecisionPackage package,
            ReleaseExecutionRequest request,
            CancellationToken cancellationToken)
        {
            UploadArtifactCalls++;
            return Task.FromResult(UploadArtifactResult ?? Accepted(ReleaseExecutionAction.UploadReleaseArtifacts, request.CandidateTagName, null, null, request.Artifacts.Select(item => item.Name).ToArray()));
        }

        private static ReleaseExecutionMutationResult Accepted(
            ReleaseExecutionAction action,
            string target,
            string? resourceId,
            string? resourceUrl,
            string[] uploadedArtifacts) => new()
            {
                Action = action,
                Attempted = true,
                Accepted = true,
                Provider = "FakeTestGateway",
                CommandOrMutationName = action.ToString(),
                Target = target,
                ResourceId = resourceId,
                ResourceUrl = resourceUrl,
                UploadedArtifacts = uploadedArtifacts,
                Message = "accepted",
                CompletedAtUtc = DateTimeOffset.Parse("2026-06-20T11:10:45Z")
            };
    }
}
