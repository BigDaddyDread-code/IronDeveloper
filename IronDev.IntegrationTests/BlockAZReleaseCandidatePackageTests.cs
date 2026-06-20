using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAZReleaseCandidatePackageTests
{
    private static readonly string HeadSha = new('a', 40);
    private static readonly string BaseSha = new('b', 40);
    private static readonly string CandidateCommitSha = new('c', 40);

    [TestMethod]
    public void BlockAZ_Package_CreatesEligibleReleaseCandidatePackageWithoutMutation()
    {
        var artifacts = ReleaseCandidatePackageBuilder.Build(CreateInput());
        var package = artifacts.Package;

        Assert.AreEqual(ReleaseCandidatePackageVerdict.PackageReadyForReleaseExecutor, package.PackageVerdict);
        Assert.IsTrue(package.CanReleaseForExecutor);
        Assert.AreEqual(CandidateCommitSha, package.CandidateCommitSha);
        Assert.AreEqual("1.2.3", package.CandidateVersion);
        Assert.AreEqual("v1.2.3", package.CandidateTagName);
        Assert.AreEqual(ReleaseCandidatePackageVerdict.PackageReadyForReleaseExecutor, artifacts.Receipt.Verdict);
        Assert.IsTrue(artifacts.Receipt.CanReleaseForExecutor);
        AssertBoundary(package.Boundary);
        Assert.IsFalse(package.Boundary.CanRelease);
        Assert.IsFalse(package.Boundary.CanDeploy);
        Assert.IsFalse(package.Boundary.CanTag);
        Assert.IsFalse(package.Boundary.CanPublish);
        Assert.IsFalse(package.Boundary.CanPromoteMemory);
        Assert.IsFalse(package.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void BlockAZ_Package_BlocksMissingOrFailedMergeExecutionReceipt()
    {
        var receipt = CreateMergeReceipt();
        var cases = new (string Name, MergeExecutionReceipt? Receipt, string CandidateCommit, string Repo, string Branch, ReleaseCandidatePackageBlockReason Expected)[]
        {
            ("missing", null, CandidateCommitSha, "owner/repo", "main", ReleaseCandidatePackageBlockReason.MissingMergeExecutionReceipt),
            ("blocked", receipt with { ExecutionVerdict = MergeExecutionVerdict.Blocked }, CandidateCommitSha, "owner/repo", "main", ReleaseCandidatePackageBlockReason.MergeExecutionNotExecuted),
            ("failed", receipt with { ExecutionVerdict = MergeExecutionVerdict.Failed }, CandidateCommitSha, "owner/repo", "main", ReleaseCandidatePackageBlockReason.MergeExecutionNotExecuted),
            ("not-attempted", receipt with { MergeAttempted = false }, CandidateCommitSha, "owner/repo", "main", ReleaseCandidatePackageBlockReason.MergeExecutionNotExecuted),
            ("not-accepted", receipt with { MergeAccepted = false }, CandidateCommitSha, "owner/repo", "main", ReleaseCandidatePackageBlockReason.MergeExecutionNotExecuted),
            ("post-not-verified", receipt with { PostStateVerified = false }, CandidateCommitSha, "owner/repo", "main", ReleaseCandidatePackageBlockReason.MergeExecutionPostStateNotVerified),
            ("missing-commit", receipt with { MergeCommitSha = null }, CandidateCommitSha, "owner/repo", "main", ReleaseCandidatePackageBlockReason.MissingMergeCommitSha),
            ("wrong-repo", receipt, CandidateCommitSha, "other/repo", "main", ReleaseCandidatePackageBlockReason.MergeReceiptRepositoryMismatch),
            ("wrong-commit", receipt, new string('d', 40), "owner/repo", "main", ReleaseCandidatePackageBlockReason.MergeReceiptCommitMismatch),
            ("wrong-base", receipt, CandidateCommitSha, "owner/repo", "release/1.2", ReleaseCandidatePackageBlockReason.MergeReceiptBaseBranchMismatch),
            ("boundary-release", receipt with { Boundary = receipt.Boundary with { CanRelease = true } }, CandidateCommitSha, "owner/repo", "main", ReleaseCandidatePackageBlockReason.MergeReceiptBoundaryAuthorityViolation)
        };

        foreach (var item in cases)
        {
            var package = ReleaseCandidatePackageBuilder.Build(CreateInput() with
            {
                MergeExecutionReceipt = item.Receipt,
                Repository = item.Repo,
                CandidateCommitSha = item.CandidateCommit,
                ReleaseSourceBranch = item.Branch,
                ObservedReleaseSourceState = CreateSourceState(item.CandidateCommit, item.Repo, item.Branch),
                ReleaseValidationEvidence = CreateValidationEvidence(item.CandidateCommit),
                ArtifactManifestEvidence = CreateArtifactManifest(item.CandidateCommit),
                ReleaseCandidateDecision = CreateDecision(item.CandidateCommit, "1.2.3", item.Repo, item.Branch)
            }).Package;

            Assert.AreNotEqual(ReleaseCandidatePackageVerdict.PackageReadyForReleaseExecutor, package.PackageVerdict, item.Name);
            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAZ_Package_BlocksStaleReleaseSourceState()
    {
        var cases = new (string Name, ReleaseSourceObservedState? State, ReleaseCandidatePackageBlockReason Expected)[]
        {
            ("missing", null, ReleaseCandidatePackageBlockReason.MissingReleaseSourceState),
            ("observation-failed", CreateSourceState() with { ObservationSucceeded = false, ObservationError = "failed" }, ReleaseCandidatePackageBlockReason.ReleaseSourceObservationFailed),
            ("candidate-commit-mismatch", CreateSourceState() with { ExpectedMergeCommitSha = new string('d', 40) }, ReleaseCandidatePackageBlockReason.ReleaseSourceCommitMismatch),
            ("source-branch-mismatch", CreateSourceState() with { ReleaseSourceBranch = "release/1.2" }, ReleaseCandidatePackageBlockReason.ReleaseSourceBranchMismatch),
            ("commit-not-present", CreateSourceState() with { CommitPresentOnReleaseSource = false }, ReleaseCandidatePackageBlockReason.ReleaseSourceCommitNotPresent),
            ("source-head-mismatch", CreateSourceState() with { ReleaseSourceHeadSha = new string('e', 40) }, ReleaseCandidatePackageBlockReason.ReleaseSourceCommitMismatch),
            ("stale-observation", CreateSourceState() with { ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T08:00:00Z") }, ReleaseCandidatePackageBlockReason.ReleaseSourceStateStale)
        };

        foreach (var item in cases)
        {
            var package = ReleaseCandidatePackageBuilder.Build(CreateInput() with { ObservedReleaseSourceState = item.State }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAZ_Package_BlocksMissingStaleOrFailedReleaseValidationEvidence()
    {
        var cases = new (string Name, ReleaseValidationEvidence? Evidence, ReleaseCandidatePackageBlockReason Expected)[]
        {
            ("missing", null, ReleaseCandidatePackageBlockReason.MissingReleaseValidationEvidence),
            ("stale", CreateValidationEvidence(commitSha: new string('d', 40)), ReleaseCandidatePackageBlockReason.ReleaseValidationEvidenceStale),
            ("failed", CreateValidationEvidence() with { Verdict = ValidationRunVerdict.Failed, FailedLaneNames = ["Build"] }, ReleaseCandidatePackageBlockReason.ReleaseValidationFailed),
            ("build-missing-result", CreateValidationEvidence(resultFamilies: ReleaseCandidatePackageBuilder.RequiredValidationFamilies.Where(item => item != "Build").ToArray()), ReleaseCandidatePackageBlockReason.RequiredReleaseValidationMissing),
            ("stable-missing-result", CreateValidationEvidence(resultFamilies: ReleaseCandidatePackageBuilder.RequiredValidationFamilies.Where(item => item != "StablePhase").ToArray()), ReleaseCandidatePackageBlockReason.RequiredReleaseValidationMissing),
            ("authority-missing-result", CreateValidationEvidence(resultFamilies: ReleaseCandidatePackageBuilder.RequiredValidationFamilies.Where(item => item != "ReleaseCandidateAuthority").ToArray()), ReleaseCandidatePackageBlockReason.RequiredReleaseValidationMissing),
            ("packaging-missing-result", CreateValidationEvidence(resultFamilies: ReleaseCandidatePackageBuilder.RequiredValidationFamilies.Where(item => item != "Packaging").ToArray()), ReleaseCandidatePackageBlockReason.RequiredReleaseValidationMissing),
            ("regression-missing-result", CreateValidationEvidence(resultFamilies: ReleaseCandidatePackageBuilder.RequiredValidationFamilies.Where(item => item != "Regression").ToArray()), ReleaseCandidatePackageBlockReason.RequiredReleaseValidationMissing)
        };

        foreach (var item in cases)
        {
            var package = ReleaseCandidatePackageBuilder.Build(CreateInput() with { ReleaseValidationEvidence = item.Evidence }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAZ_Package_BlocksNotApplicableReleaseValidationWithoutReason()
    {
        var cases = new (string Name, ReleaseValidationEvidence Evidence)[]
        {
            ("packaging-without-reason", CreateValidationEvidence(
                resultFamilies: ReleaseCandidatePackageBuilder.RequiredValidationFamilies.Where(item => item != "Packaging").ToArray()) with
            {
                NotApplicableLaneNames = ["Packaging"],
                NotApplicableLaneReasons = []
            }),
            ("regression-without-reason", CreateValidationEvidence(
                resultFamilies: ReleaseCandidatePackageBuilder.RequiredValidationFamilies.Where(item => item != "Regression").ToArray()) with
            {
                NotApplicableLaneNames = ["Regression"],
                NotApplicableLaneReasons = [" "]
            })
        };

        foreach (var item in cases)
        {
            var package = ReleaseCandidatePackageBuilder.Build(CreateInput() with { ReleaseValidationEvidence = item.Evidence }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, ReleaseCandidatePackageBlockReason.RequiredReleaseValidationMissing, item.Name);
            Assert.IsTrue(package.PackageIssues.Any(issue => issue.Contains("NotApplicableReasonMissing", StringComparison.OrdinalIgnoreCase)), item.Name);
        }
    }

    [TestMethod]
    public void BlockAZ_Package_AllowsReasonedNotApplicablePackagingAndRegressionLanes()
    {
        var evidence = CreateValidationEvidence(
            resultFamilies: ReleaseCandidatePackageBuilder.RequiredValidationFamilies
                .Where(item => item != "Packaging" && item != "Regression")
                .ToArray()) with
        {
            NotApplicableLaneNames = ["Packaging", "Regression"],
            NotApplicableLaneReasons =
            [
                "No package artifact is produced for this release-candidate evidence slice.",
                "Regression is covered by the stable phase lane for this candidate."
            ]
        };

        var package = ReleaseCandidatePackageBuilder.Build(CreateInput() with { ReleaseValidationEvidence = evidence }).Package;

        Assert.AreEqual(ReleaseCandidatePackageVerdict.PackageReadyForReleaseExecutor, package.PackageVerdict);
        Assert.IsTrue(package.CanReleaseForExecutor);
        CollectionAssert.DoesNotContain(package.BlockReasons, ReleaseCandidatePackageBlockReason.RequiredReleaseValidationMissing);
    }

    [TestMethod]
    public void BlockAZ_Package_BlocksInvalidVersionOrExistingRelease()
    {
        var cases = new (string Name, ReleaseVersionEvidence? Evidence, ReleaseCandidatePackageBlockReason Expected)[]
        {
            ("missing", null, ReleaseCandidatePackageBlockReason.MissingVersionEvidence),
            ("invalid-version", CreateVersionEvidence() with { CandidateVersion = "not semver" }, ReleaseCandidatePackageBlockReason.InvalidCandidateVersion),
            ("missing-tag", CreateVersionEvidence() with { TagName = " " }, ReleaseCandidatePackageBlockReason.MissingCandidateTagName),
            ("existing-tag", CreateVersionEvidence() with { ExistingTagFound = true }, ReleaseCandidatePackageBlockReason.CandidateTagAlreadyExists),
            ("existing-release", CreateVersionEvidence() with { ExistingReleaseFound = true }, ReleaseCandidatePackageBlockReason.CandidateReleaseAlreadyExists),
            ("missing-maker", CreateVersionEvidence() with { VersionDecisionBy = " " }, ReleaseCandidatePackageBlockReason.MissingVersionDecisionMaker),
            ("missing-rationale", CreateVersionEvidence() with { VersionRationale = " " }, ReleaseCandidatePackageBlockReason.MissingVersionRationale)
        };

        foreach (var item in cases)
        {
            var package = ReleaseCandidatePackageBuilder.Build(CreateInput() with { ReleaseVersionEvidence = item.Evidence }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
            AssertBoundary(package.Boundary);
        }
    }

    [TestMethod]
    public void BlockAZ_Package_RequiresReleaseNotesEvidence()
    {
        var cases = new (string Name, ReleaseNotesEvidence? Evidence, ReleaseCandidatePackageBlockReason Expected)[]
        {
            ("missing", null, ReleaseCandidatePackageBlockReason.MissingReleaseNotesEvidence),
            ("empty", CreateReleaseNotes() with { ReleaseNotesSummary = " " }, ReleaseCandidatePackageBlockReason.EmptyReleaseNotes),
            ("migration-missing", CreateReleaseNotes() with { MigrationsPresent = true, MigrationNotes = [] }, ReleaseCandidatePackageBlockReason.MissingMigrationNotes),
            ("known-issues-missing", CreateReleaseNotes() with { KnownIssuesPresent = true, KnownIssues = [] }, ReleaseCandidatePackageBlockReason.MissingKnownIssuesRecord)
        };

        foreach (var item in cases)
        {
            var package = ReleaseCandidatePackageBuilder.Build(CreateInput() with { ReleaseNotesEvidence = item.Evidence }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAZ_Package_RequiresExplicitReleaseCandidateDecision()
    {
        var cases = new (string Name, ReleaseCandidateDecisionRecord? Decision, string Channel, ReleaseCandidatePackageBlockReason Expected)[]
        {
            ("missing", null, "Stable", ReleaseCandidatePackageBlockReason.MissingReleaseCandidateDecision),
            ("blocked", CreateDecision() with { Decision = ReleaseCandidateDecision.Blocked }, "Stable", ReleaseCandidatePackageBlockReason.ReleaseCandidateDecisionBlocked),
            ("rejected", CreateDecision() with { Decision = ReleaseCandidateDecision.Rejected }, "Stable", ReleaseCandidatePackageBlockReason.ReleaseCandidateDecisionRejected),
            ("missing-maker", CreateDecision() with { DecisionMadeBy = " " }, "Stable", ReleaseCandidatePackageBlockReason.MissingDecisionMaker),
            ("stale-commit", CreateDecision(commitSha: new string('d', 40)), "Stable", ReleaseCandidatePackageBlockReason.ReleaseCandidateDecisionStale),
            ("stale-version", CreateDecision(version: "2.0.0"), "Stable", ReleaseCandidatePackageBlockReason.ReleaseCandidateDecisionStale),
            ("missing-rationale", CreateDecision() with { DecisionRationale = " " }, "Stable", ReleaseCandidatePackageBlockReason.MissingDecisionRationale),
            ("unsupported-channel", CreateDecision(), "production", ReleaseCandidatePackageBlockReason.UnsupportedReleaseChannel)
        };

        foreach (var item in cases)
        {
            var package = ReleaseCandidatePackageBuilder.Build(CreateInput() with
            {
                ReleaseCandidateDecision = item.Decision,
                ReleaseChannel = item.Channel
            }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
            AssertBoundary(package.Boundary);
        }
    }

    [TestMethod]
    public void BlockAZ_Boundary_RemainsEvidenceOnly()
    {
        var package = ReleaseCandidatePackageBuilder.Build(CreateInput()).Package;

        Assert.IsTrue(package.CanReleaseForExecutor);
        AssertBoundary(package.Boundary);
    }

    [TestMethod]
    public async Task BlockAZ_Cli_BlocksReleaseDeployTagPublishAndContinuationVerbs()
    {
        foreach (var forbidden in new[] { "execute", "release", "tag", "publish", "deploy", "promote-memory", "continue", "merge", "push", "commit" })
        {
            var result = await RunCliAsync("release-candidate", forbidden, "--package", "release-candidate-package.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAZ_StaticBoundary_ProvesNoReleaseDeployMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReleaseCandidatePackage.cs"));
        Assert.IsFalse(cli.Contains("gh release create", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh release upload", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git tag", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("dotnet nuget push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("npm publish", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("docker push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("kubectl", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("az webapp", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("terraform apply", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AZ_RELEASE_CANDIDATE_PACKAGE.md"));
        StringAssert.Contains(receipt, "Merge execution is not release readiness.");
        StringAssert.Contains(receipt, "Release candidate package is not release execution.");
        StringAssert.Contains(receipt, "Release execution is not deployment.");
        StringAssert.Contains(receipt, "Release is not deployment.");
        StringAssert.Contains(receipt, "Validation evidence is not release authority.");
        StringAssert.Contains(receipt, "Release notes are not release authority.");
        StringAssert.Contains(receipt, "Version selection is not tag creation.");
        StringAssert.Contains(receipt, "No hidden publication.");
        StringAssert.Contains(receipt, "No hidden deployment.");
        StringAssert.Contains(receipt, "No hidden workflow continuation.");
    }

    [TestMethod]
    public void BlockAZ_ReleaseCandidatePackageDoesNotBecomeReleaseDeployAuthority()
    {
        var package = ReleaseCandidatePackageBuilder.Build(CreateInput()).Package;

        Assert.IsTrue(package.CanReleaseForExecutor);
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanRelease(package));
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanTag(package));
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanPublish(package));
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanPromoteMemory(package));
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanContinueWorkflow(package));
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanCommit(package));
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanPush(package));
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanMutateSource(package));
        Assert.IsFalse(ReleaseCandidatePackageBypassEvaluator.CanMutateWorkspace(package));
    }

    [TestMethod]
    public void BlockAZ_MergeExecutionDoesNotBecomeReleaseCandidateDecision()
    {
        var package = ReleaseCandidatePackageBuilder.Build(CreateInput() with { ReleaseCandidateDecision = null }).Package;

        Assert.AreNotEqual(ReleaseCandidatePackageVerdict.PackageReadyForReleaseExecutor, package.PackageVerdict);
        Assert.IsFalse(package.CanReleaseForExecutor);
        CollectionAssert.Contains(package.BlockReasons, ReleaseCandidatePackageBlockReason.MissingReleaseCandidateDecision);
        Assert.IsNotNull(package.ReleaseValidationEvidence);
        Assert.IsNotNull(package.ReleaseVersionEvidence);
        Assert.IsNotNull(package.ReleaseNotesEvidence);
    }

    private static ReleaseCandidatePackageInput CreateInput() => new()
    {
        MergeExecutionReceipt = CreateMergeReceipt(),
        ObservedReleaseSourceState = CreateSourceState(),
        ReleaseValidationEvidence = CreateValidationEvidence(),
        ReleaseVersionEvidence = CreateVersionEvidence(),
        ReleaseNotesEvidence = CreateReleaseNotes(),
        ArtifactManifestEvidence = CreateArtifactManifest(),
        ArtifactManifestRequired = true,
        ReleaseCandidateDecision = CreateDecision(),
        Repository = "owner/repo",
        ReleaseSourceBranch = "main",
        CandidateCommitSha = CandidateCommitSha,
        ReleaseChannel = "Stable",
        CreatedBy = "release-manager",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T09:30:00Z")
    };

    private static MergeExecutionReceipt CreateMergeReceipt() => new()
    {
        MergeExecutionId = "merge_exec_az",
        MergeDecisionPackageId = "merge_decision_pkg_az",
        Repository = "owner/repo",
        PullRequestNumber = 473,
        PullRequestUrl = "https://github.com/owner/repo/pull/473",
        ExpectedHeadBranch = "az/release-candidate-package",
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "main",
        ExpectedBaseSha = BaseSha,
        SelectedMergeStrategy = "Squash",
        MergeCommitSha = CandidateCommitSha,
        MergeAttempted = true,
        MergeAccepted = true,
        PostStateVerified = true,
        ExecutionVerdict = MergeExecutionVerdict.Executed,
        FailureClassification = MergeExecutionFailureKind.None,
        RequestedBy = "merge-captain",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T09:00:00Z"),
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T09:01:00Z"),
        Boundary = MergeExecutionBoundary.Executor
    };

    private static ReleaseSourceObservedState CreateSourceState(
        string commitSha = "",
        string repository = "owner/repo",
        string branch = "main") => new()
    {
        Repository = repository,
        ReleaseSourceBranch = branch,
        ReleaseSourceHeadSha = string.IsNullOrWhiteSpace(commitSha) ? CandidateCommitSha : commitSha,
        ExpectedMergeCommitSha = string.IsNullOrWhiteSpace(commitSha) ? CandidateCommitSha : commitSha,
        DefaultBranch = branch,
        DefaultBranchHeadSha = string.IsNullOrWhiteSpace(commitSha) ? CandidateCommitSha : commitSha,
        CommitPresentOnReleaseSource = true,
        CommitPresentOnDefaultBranch = true,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T09:05:00Z"),
        ObservationSource = "test",
        ObservationSucceeded = true
    };

    private static ReleaseValidationEvidence CreateValidationEvidence(
        string? commitSha = null,
        string[]? resultFamilies = null) => new()
    {
        ValidationRunId = "validation_run_az",
        ValidationPlanId = "validation_plan_az",
        CommitSha = commitSha ?? CandidateCommitSha,
        Verdict = ValidationRunVerdict.Passed,
        RequiredLaneNames = ReleaseCandidatePackageBuilder.RequiredValidationFamilies,
        ResultLaneNames = resultFamilies ?? ReleaseCandidatePackageBuilder.RequiredValidationFamilies,
        MissingLaneNames = [],
        FailedLaneNames = [],
        StartedAtUtc = DateTimeOffset.Parse("2026-06-20T09:10:00Z"),
        FinishedAtUtc = DateTimeOffset.Parse("2026-06-20T09:15:00Z"),
        ValidationEvidenceReceiptId = "validation_run_az"
    };

    private static ReleaseVersionEvidence CreateVersionEvidence() => new()
    {
        CandidateVersion = "1.2.3",
        VersionScheme = "SemVer",
        PreviousVersion = "1.2.2",
        VersionSource = "release-manager",
        VersionDecisionBy = "release-manager",
        VersionDecisionAtUtc = DateTimeOffset.Parse("2026-06-20T09:16:00Z"),
        VersionRationale = "Candidate version follows the next SemVer patch.",
        TagName = "v1.2.3",
        ExistingTagFound = false,
        ExistingReleaseFound = false
    };

    private static ReleaseNotesEvidence CreateReleaseNotes() => new()
    {
        ReleaseNotesPath = "Docs/releases/1.2.3.md",
        ReleaseNotesSummary = "Release candidate includes the controlled merge separation path.",
        ChangelogPath = "CHANGELOG.md",
        IncludedPullRequests = ["473"],
        IncludedCommits = [CandidateCommitSha],
        KnownIssues = ["none"],
        BreakingChanges = [],
        MigrationNotes = ["none"],
        KnownIssuesPresent = true,
        MigrationsPresent = true,
        GeneratedAtUtc = DateTimeOffset.Parse("2026-06-20T09:17:00Z"),
        GeneratedBy = "release-manager"
    };

    private static ArtifactManifestEvidence CreateArtifactManifest(string? commitSha = null) => new()
    {
        ArtifactManifestId = "artifact_manifest_az",
        BuildRunId = "build_run_az",
        CommitSha = commitSha ?? CandidateCommitSha,
        Artifacts = ["IronDev.zip"],
        Checksums = ["sha256:abcdef"],
        StorageLocation = "artifact-store://release-candidates/1.2.3",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T09:18:00Z")
    };

    private static ReleaseCandidateDecisionRecord CreateDecision(
        string? commitSha = null,
        string version = "1.2.3",
        string repository = "owner/repo",
        string branch = "main") => new()
    {
        ReleaseCandidateDecisionId = "release_candidate_decision_az",
        Decision = ReleaseCandidateDecision.ApprovedForReleaseExecutor,
        DecisionMadeBy = "release-manager",
        DecisionMadeAtUtc = DateTimeOffset.Parse("2026-06-20T09:20:00Z"),
        DecisionRationale = "Release candidate package is ready for the future release executor.",
        ExpectedRepository = repository,
        ExpectedCommitSha = commitSha ?? CandidateCommitSha,
        ExpectedVersion = version,
        ExpectedReleaseSourceBranch = branch,
        ExpectedReleaseChannel = "Stable"
    };

    private static void AssertBoundary(ReleaseCandidatePackageBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanRemoveReviewers);
        Assert.IsFalse(boundary.CanResolveReviewThreads);
        Assert.IsFalse(boundary.CanReplyToReviewThreads);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanSubmitReview);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanAutoMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanTag);
        Assert.IsFalse(boundary.CanPublish);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
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
