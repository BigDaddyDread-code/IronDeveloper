using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBAReleaseReadinessDecisionPackageTests
{
    private static readonly string HeadSha = new('a', 40);
    private static readonly string BaseSha = new('b', 40);
    private static readonly string CandidateCommitSha = new('c', 40);

    [TestMethod]
    public void BlockBA_Package_CreatesEligibleReleaseReadinessDecisionPackageWithoutMutation()
    {
        var artifacts = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput());
        var package = artifacts.Package;

        Assert.AreEqual(ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor, package.PackageVerdict);
        Assert.IsTrue(package.CanReleaseForExecutor);
        Assert.AreEqual(CandidateCommitSha, package.CandidateCommitSha);
        Assert.AreEqual("1.2.3", package.CandidateVersion);
        Assert.AreEqual("v1.2.3", package.CandidateTagName);
        Assert.AreEqual(ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor, artifacts.Receipt.Verdict);
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
    public void BlockBA_Package_BlocksMissingOrInvalidReleaseCandidatePackage()
    {
        var ready = CreateAzPackage();
        var cases = new (string Name, ReleaseCandidatePackage? Package, string Repo, string Commit, string Version, string Tag, string Branch, string Channel, ReleaseReadinessDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.MissingReleaseCandidatePackage),
            ("incomplete", ready with { PackageVerdict = ReleaseCandidatePackageVerdict.PackageIncomplete, CanReleaseForExecutor = false }, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidatePackageNotReady),
            ("blocked", ready with { PackageVerdict = ReleaseCandidatePackageVerdict.PackageBlocked, BlockReasons = [ReleaseCandidatePackageBlockReason.ReleaseValidationFailed] }, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidatePackageBlocked),
            ("rejected", ready with { PackageVerdict = ReleaseCandidatePackageVerdict.PackageRejected }, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidatePackageRejected),
            ("not-ready", ready with { CanReleaseForExecutor = false }, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidatePackageNotReady),
            ("boundary-release", ready with { Boundary = ready.Boundary with { CanRelease = true } }, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidateBoundaryAuthorityViolation),
            ("boundary-deploy", ready with { Boundary = ready.Boundary with { CanDeploy = true } }, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidateBoundaryAuthorityViolation),
            ("boundary-tag", ready with { Boundary = ready.Boundary with { CanTag = true } }, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidateBoundaryAuthorityViolation),
            ("boundary-publish", ready with { Boundary = ready.Boundary with { CanPublish = true } }, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidateBoundaryAuthorityViolation),
            ("boundary-continue", ready with { Boundary = ready.Boundary with { CanContinueWorkflow = true } }, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidateBoundaryAuthorityViolation),
            ("wrong-repo", ready, "other/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.RepositoryMismatch),
            ("wrong-commit", ready, "owner/repo", new string('d', 40), "1.2.3", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.CandidateCommitMismatch),
            ("wrong-version", ready, "owner/repo", CandidateCommitSha, "1.2.4", "v1.2.3", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.CandidateVersionMismatch),
            ("wrong-tag", ready, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.4", "main", "Stable", ReleaseReadinessDecisionPackageBlockReason.CandidateTagMismatch),
            ("wrong-branch", ready, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "release/1.2", "Stable", ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceBranchMismatch),
            ("wrong-channel", ready, "owner/repo", CandidateCommitSha, "1.2.3", "v1.2.3", "main", "Preview", ReleaseReadinessDecisionPackageBlockReason.ReleaseChannelMismatch)
        };

        foreach (var item in cases)
        {
            var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput(item.Repo, item.Commit, item.Version, item.Tag, item.Branch, item.Channel) with
            {
                ReleaseCandidatePackage = item.Package,
                CurrentReleaseSourceState = CreateSourceState(item.Commit, item.Repo, item.Branch),
                CurrentTagReleaseState = CreateTagReleaseState(item.Repo, item.Version, item.Tag),
                FinalReleaseValidationEvidence = CreateValidationEvidence(item.Commit),
                ReleaseArtifactReadinessEvidence = CreateArtifactReadiness(item.Commit),
                ReleaseReadinessDecision = CreateDecision(item.Package?.ReleaseCandidatePackageId ?? "missing", item.Commit, item.Version, item.Tag, item.Repo, item.Branch, item.Channel)
            }).Package;

            Assert.AreNotEqual(ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor, package.PackageVerdict, item.Name);
            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockBA_Package_BlocksStaleCurrentReleaseSourceState()
    {
        var cases = new (string Name, CurrentReleaseSourceState? State, ReleaseReadinessDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, ReleaseReadinessDecisionPackageBlockReason.MissingCurrentReleaseSourceState),
            ("observation-failed", CreateSourceState() with { ObservationSucceeded = false, ObservationError = "failed" }, ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceObservationFailed),
            ("candidate-commit-mismatch", CreateSourceState() with { CandidateCommitSha = new string('d', 40) }, ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceCommitMismatch),
            ("commit-not-present", CreateSourceState() with { CommitPresentOnReleaseSource = false }, ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceCommitNotPresent),
            ("source-branch-mismatch", CreateSourceState() with { ReleaseSourceBranch = "release/1.2" }, ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceBranchMismatch),
            ("stale-observation", CreateSourceState() with { ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T09:00:00Z") }, ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceStateStale)
        };

        foreach (var item in cases)
        {
            var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput() with { CurrentReleaseSourceState = item.State }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockBA_Package_BlocksExistingTagOrRelease()
    {
        var cases = new (string Name, CurrentTagReleaseState? State, ReleaseReadinessDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, ReleaseReadinessDecisionPackageBlockReason.MissingCurrentTagReleaseState),
            ("observation-failed", CreateTagReleaseState() with { ObservationSucceeded = false, ObservationError = "failed" }, ReleaseReadinessDecisionPackageBlockReason.TagReleaseObservationFailed),
            ("existing-tag", CreateTagReleaseState() with { ExistingTagFound = true, ExistingTagSha = CandidateCommitSha }, ReleaseReadinessDecisionPackageBlockReason.CandidateTagAlreadyExists),
            ("existing-release", CreateTagReleaseState() with { ExistingReleaseFound = true, ExistingReleaseId = "release-1" }, ReleaseReadinessDecisionPackageBlockReason.CandidateReleaseAlreadyExists),
            ("stale-observation", CreateTagReleaseState() with { ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T09:00:00Z") }, ReleaseReadinessDecisionPackageBlockReason.TagReleaseStateStale)
        };

        foreach (var item in cases)
        {
            var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput() with { CurrentTagReleaseState = item.State }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockBA_Package_BlocksMissingStaleOrFailedFinalReleaseValidation()
    {
        var cases = new (string Name, FinalReleaseValidationEvidence? Evidence, ReleaseReadinessDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, ReleaseReadinessDecisionPackageBlockReason.MissingFinalReleaseValidationEvidence),
            ("stale-commit", CreateValidationEvidence(commitSha: new string('d', 40)), ReleaseReadinessDecisionPackageBlockReason.FinalReleaseValidationEvidenceStale),
            ("failed", CreateValidationEvidence() with { Verdict = ValidationRunVerdict.Failed, FailedLaneNames = ["Build"] }, ReleaseReadinessDecisionPackageBlockReason.FinalReleaseValidationFailed),
            ("build-missing-result", CreateValidationEvidence(resultFamilies: ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "Build").ToArray()), ReleaseReadinessDecisionPackageBlockReason.RequiredFinalReleaseValidationMissing),
            ("stable-missing-result", CreateValidationEvidence(resultFamilies: ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "StablePhase").ToArray()), ReleaseReadinessDecisionPackageBlockReason.RequiredFinalReleaseValidationMissing),
            ("candidate-authority-missing-result", CreateValidationEvidence(resultFamilies: ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "ReleaseCandidateAuthority").ToArray()), ReleaseReadinessDecisionPackageBlockReason.RequiredFinalReleaseValidationMissing),
            ("readiness-authority-missing-result", CreateValidationEvidence(resultFamilies: ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "ReleaseReadinessAuthority").ToArray()), ReleaseReadinessDecisionPackageBlockReason.RequiredFinalReleaseValidationMissing),
            ("packaging-missing-without-reason", CreateValidationEvidence(resultFamilies: ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "Packaging").ToArray()), ReleaseReadinessDecisionPackageBlockReason.RequiredFinalReleaseValidationMissing),
            ("regression-missing-without-reason", CreateValidationEvidence(resultFamilies: ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "Regression").ToArray()), ReleaseReadinessDecisionPackageBlockReason.RequiredFinalReleaseValidationMissing),
            ("not-applicable-without-reason", CreateValidationEvidence(resultFamilies: ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "Packaging").ToArray()) with { NotApplicableLaneNames = ["Packaging"], NotApplicableLaneReasons = [] }, ReleaseReadinessDecisionPackageBlockReason.NotApplicableValidationReasonMissing)
        };

        foreach (var item in cases)
        {
            var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput() with { FinalReleaseValidationEvidence = item.Evidence }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockBA_Package_AllowsReasonedNotApplicableValidationLanes()
    {
        var evidence = CreateValidationEvidence(
            resultFamilies: ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies
                .Where(item => item != "Packaging" && item != "Regression")
                .ToArray()) with
        {
            NotApplicableLaneNames = ["Packaging", "Regression"],
            NotApplicableLaneReasons =
            [
                "No package artifact lane is applicable for this readiness decision.",
                "Regression is covered by the stable phase evidence for this candidate."
            ]
        };

        var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput() with { FinalReleaseValidationEvidence = evidence }).Package;

        Assert.AreEqual(ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor, package.PackageVerdict);
        Assert.IsTrue(package.CanReleaseForExecutor);
    }

    [TestMethod]
    public void BlockBA_Package_BlocksInvalidArtifactReadiness()
    {
        var cases = new (string Name, ReleaseArtifactReadinessEvidence? Evidence, ReleaseReadinessDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, ReleaseReadinessDecisionPackageBlockReason.MissingArtifactReadinessEvidence),
            ("commit-mismatch", CreateArtifactReadiness(commitSha: new string('d', 40)), ReleaseReadinessDecisionPackageBlockReason.ArtifactManifestCommitMismatch),
            ("required-no-manifest", CreateArtifactReadiness() with { ArtifactManifestId = null }, ReleaseReadinessDecisionPackageBlockReason.ArtifactManifestMissingArtifacts),
            ("required-empty-artifacts", CreateArtifactReadiness() with { Artifacts = [] }, ReleaseReadinessDecisionPackageBlockReason.ArtifactManifestMissingArtifacts),
            ("checksum-missing", CreateArtifactReadiness() with { Checksums = [] }, ReleaseReadinessDecisionPackageBlockReason.ArtifactChecksumMissing),
            ("storage-missing", CreateArtifactReadiness() with { StorageLocation = " " }, ReleaseReadinessDecisionPackageBlockReason.ArtifactStorageLocationMissing),
            ("not-required-missing-reason", CreateArtifactReadiness() with { ArtifactsRequired = false, ArtifactsReady = false, Artifacts = [], Checksums = [], StorageLocation = null, NotApplicableReason = " " }, ReleaseReadinessDecisionPackageBlockReason.ArtifactNotApplicableReasonMissing)
        };

        foreach (var item in cases)
        {
            var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput() with
            {
                ReleaseArtifactReadinessEvidence = item.Evidence,
                ReleaseReadinessDecision = CreateDecision(artifactManifestId: item.Evidence?.ArtifactManifestId)
            }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
            AssertBoundary(package.Boundary);
        }
    }

    [TestMethod]
    public void BlockBA_Package_RequiresExplicitReleaseReadinessDecision()
    {
        var cases = new (string Name, ReleaseReadinessDecisionEvidence? Decision, ReleaseReadinessDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, ReleaseReadinessDecisionPackageBlockReason.MissingReleaseReadinessDecision),
            ("blocked", CreateDecision() with { Decision = ReleaseReadinessDecision.Blocked }, ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionBlocked),
            ("rejected", CreateDecision() with { Decision = ReleaseReadinessDecision.Rejected }, ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionRejected),
            ("missing-maker", CreateDecision() with { DecisionMadeBy = " " }, ReleaseReadinessDecisionPackageBlockReason.MissingDecisionMaker),
            ("stale-commit", CreateDecision(commitSha: new string('d', 40)), ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionStale),
            ("stale-version", CreateDecision(version: "2.0.0"), ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionStale),
            ("stale-tag", CreateDecision(tag: "v2.0.0"), ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionStale),
            ("stale-branch", CreateDecision(branch: "release/1.2"), ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionStale),
            ("stale-channel", CreateDecision(channel: "Preview"), ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionStale),
            ("missing-rationale", CreateDecision() with { DecisionRationale = " " }, ReleaseReadinessDecisionPackageBlockReason.MissingDecisionRationale),
            ("maker-is-az-creator", CreateDecision(decisionBy: "release-manager"), ReleaseReadinessDecisionPackageBlockReason.DecisionMakerNotAllowed)
        };

        foreach (var item in cases)
        {
            var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput() with { ReleaseReadinessDecision = item.Decision }).Package;

            Assert.IsFalse(package.CanReleaseForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
            AssertBoundary(package.Boundary);
        }
    }

    [TestMethod]
    public void BlockBA_Boundary_RemainsEvidenceOnly()
    {
        var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput()).Package;

        Assert.IsTrue(package.CanReleaseForExecutor);
        AssertBoundary(package.Boundary);
    }

    [TestMethod]
    public async Task BlockBA_Cli_BlocksReleaseDeployTagPublishAndContinuationVerbs()
    {
        foreach (var forbidden in new[] { "execute", "release", "tag", "publish", "deploy", "promote-memory", "continue", "merge", "push", "commit" })
        {
            var result = await RunCliAsync("release-readiness", forbidden, "--package", "release-readiness-decision-package.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockBA_StaticBoundary_ProvesNoReleaseDeployMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReleaseReadinessDecisionPackage.cs"));
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

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "BA_RELEASE_READINESS_DECISION_PACKAGE.md"));
        StringAssert.Contains(receipt, "Release candidate package is not release readiness decision.");
        StringAssert.Contains(receipt, "Release readiness decision package is not release execution.");
        StringAssert.Contains(receipt, "Release execution is not deployment.");
        StringAssert.Contains(receipt, "Release is not deployment.");
        StringAssert.Contains(receipt, "Validation evidence is not release authority.");
        StringAssert.Contains(receipt, "Release notes are not release authority.");
        StringAssert.Contains(receipt, "Version selection is not tag creation.");
        StringAssert.Contains(receipt, "Artifact readiness is not publication.");
        StringAssert.Contains(receipt, "No hidden tag creation.");
        StringAssert.Contains(receipt, "No hidden release creation.");
        StringAssert.Contains(receipt, "No hidden publication.");
        StringAssert.Contains(receipt, "No hidden deployment.");
        StringAssert.Contains(receipt, "No hidden memory promotion.");
        StringAssert.Contains(receipt, "No hidden workflow continuation.");
    }

    [TestMethod]
    public void BlockBA_ReleaseReadinessDecisionPackageDoesNotBecomeReleaseDeployAuthority()
    {
        var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput()).Package;

        Assert.IsTrue(package.CanReleaseForExecutor);
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanRelease(package));
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanTag(package));
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanPublish(package));
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanPromoteMemory(package));
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanContinueWorkflow(package));
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanCommit(package));
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanPush(package));
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanMutateSource(package));
        Assert.IsFalse(ReleaseReadinessDecisionPackageBypassEvaluator.CanMutateWorkspace(package));
    }

    [TestMethod]
    public void BlockBA_ReleaseCandidatePackageDoesNotBecomeReleaseReadinessDecision()
    {
        var package = ReleaseReadinessDecisionPackageBuilder.Build(CreateInput() with { ReleaseReadinessDecision = null }).Package;

        Assert.AreNotEqual(ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor, package.PackageVerdict);
        Assert.IsFalse(package.CanReleaseForExecutor);
        CollectionAssert.Contains(package.BlockReasons, ReleaseReadinessDecisionPackageBlockReason.MissingReleaseReadinessDecision);
        Assert.AreEqual(CreateAzPackage().ReleaseCandidatePackageId, package.SourceReleaseCandidatePackageId);
    }

    private static ReleaseReadinessDecisionPackageInput CreateInput(
        string repository = "owner/repo",
        string commitSha = "",
        string version = "1.2.3",
        string tag = "v1.2.3",
        string branch = "main",
        string channel = "Stable")
    {
        var candidateCommit = string.IsNullOrWhiteSpace(commitSha) ? CandidateCommitSha : commitSha;
        var releaseCandidatePackage = CreateAzPackage();
        return new ReleaseReadinessDecisionPackageInput
        {
            ReleaseCandidatePackage = releaseCandidatePackage,
            CurrentReleaseSourceState = CreateSourceState(candidateCommit, repository, branch),
            CurrentTagReleaseState = CreateTagReleaseState(repository, version, tag),
            FinalReleaseValidationEvidence = CreateValidationEvidence(candidateCommit),
            ReleaseArtifactReadinessEvidence = CreateArtifactReadiness(candidateCommit),
            ReleaseReadinessDecision = CreateDecision(releaseCandidatePackage.ReleaseCandidatePackageId, candidateCommit, version, tag, repository, branch, channel),
            Repository = repository,
            ReleaseSourceBranch = branch,
            CandidateCommitSha = candidateCommit,
            CandidateVersion = version,
            CandidateTagName = tag,
            ReleaseChannel = channel,
            CreatedBy = "release-captain",
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T10:00:00Z")
        };
    }

    private static ReleaseCandidatePackage CreateAzPackage() => ReleaseCandidatePackageBuilder.Build(CreateAzInput()).Package;

    private static ReleaseCandidatePackageInput CreateAzInput() => new()
    {
        MergeExecutionReceipt = CreateMergeReceipt(),
        ObservedReleaseSourceState = CreateAzSourceState(),
        ReleaseValidationEvidence = CreateAzValidationEvidence(),
        ReleaseVersionEvidence = CreateVersionEvidence(),
        ReleaseNotesEvidence = CreateReleaseNotes(),
        ArtifactManifestEvidence = CreateArtifactManifest(),
        ArtifactManifestRequired = true,
        ReleaseCandidateDecision = CreateAzDecision(),
        Repository = "owner/repo",
        ReleaseSourceBranch = "main",
        CandidateCommitSha = CandidateCommitSha,
        ReleaseChannel = "Stable",
        CreatedBy = "release-manager",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T09:30:00Z")
    };

    private static MergeExecutionReceipt CreateMergeReceipt() => new()
    {
        MergeExecutionId = "merge_exec_ba",
        MergeDecisionPackageId = "merge_decision_pkg_ba",
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

    private static ReleaseSourceObservedState CreateAzSourceState() => new()
    {
        Repository = "owner/repo",
        ReleaseSourceBranch = "main",
        ReleaseSourceHeadSha = CandidateCommitSha,
        ExpectedMergeCommitSha = CandidateCommitSha,
        DefaultBranch = "main",
        DefaultBranchHeadSha = CandidateCommitSha,
        CommitPresentOnReleaseSource = true,
        CommitPresentOnDefaultBranch = true,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T09:05:00Z"),
        ObservationSource = "test",
        ObservationSucceeded = true
    };

    private static CurrentReleaseSourceState CreateSourceState(
        string commitSha = "",
        string repository = "owner/repo",
        string branch = "main") => new()
    {
        Repository = repository,
        ReleaseSourceBranch = branch,
        ReleaseSourceHeadSha = string.IsNullOrWhiteSpace(commitSha) ? CandidateCommitSha : commitSha,
        CandidateCommitSha = string.IsNullOrWhiteSpace(commitSha) ? CandidateCommitSha : commitSha,
        DefaultBranch = branch,
        DefaultBranchHeadSha = string.IsNullOrWhiteSpace(commitSha) ? CandidateCommitSha : commitSha,
        CommitPresentOnReleaseSource = true,
        CommitPresentOnDefaultBranch = true,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T09:35:00Z"),
        ObservationSource = "test",
        ObservationSucceeded = true
    };

    private static CurrentTagReleaseState CreateTagReleaseState(
        string repository = "owner/repo",
        string version = "1.2.3",
        string tag = "v1.2.3") => new()
    {
        Repository = repository,
        CandidateVersion = version,
        CandidateTagName = tag,
        ExistingTagFound = false,
        ExistingReleaseFound = false,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T09:36:00Z"),
        ObservationSource = "test",
        ObservationSucceeded = true
    };

    private static ReleaseValidationEvidence CreateAzValidationEvidence() => new()
    {
        ValidationRunId = "validation_run_az_for_ba",
        ValidationPlanId = "validation_plan_az_for_ba",
        CommitSha = CandidateCommitSha,
        Verdict = ValidationRunVerdict.Passed,
        RequiredLaneNames = ReleaseCandidatePackageBuilder.RequiredValidationFamilies,
        ResultLaneNames = ReleaseCandidatePackageBuilder.RequiredValidationFamilies,
        MissingLaneNames = [],
        FailedLaneNames = [],
        StartedAtUtc = DateTimeOffset.Parse("2026-06-20T09:10:00Z"),
        FinishedAtUtc = DateTimeOffset.Parse("2026-06-20T09:15:00Z"),
        ValidationEvidenceReceiptId = "validation_run_az_for_ba"
    };

    private static FinalReleaseValidationEvidence CreateValidationEvidence(
        string? commitSha = null,
        string[]? resultFamilies = null) => new()
    {
        ValidationRunId = "validation_run_ba",
        ValidationPlanId = "validation_plan_ba",
        CommitSha = commitSha ?? CandidateCommitSha,
        Verdict = ValidationRunVerdict.Passed,
        RequiredLaneNames = ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies,
        ResultLaneNames = resultFamilies ?? ReleaseReadinessDecisionPackageBuilder.RequiredValidationFamilies,
        MissingLaneNames = [],
        FailedLaneNames = [],
        StartedAtUtc = DateTimeOffset.Parse("2026-06-20T09:40:00Z"),
        FinishedAtUtc = DateTimeOffset.Parse("2026-06-20T09:45:00Z"),
        ValidationEvidenceReceiptId = "validation_run_ba"
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
        ReleaseNotesSummary = "Release candidate includes the controlled release package path.",
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

    private static ArtifactManifestEvidence CreateArtifactManifest() => new()
    {
        ArtifactManifestId = "artifact_manifest_az",
        BuildRunId = "build_run_az",
        CommitSha = CandidateCommitSha,
        Artifacts = ["IronDev.zip"],
        Checksums = ["sha256:abcdef"],
        StorageLocation = "artifact-store://release-candidates/1.2.3",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T09:18:00Z")
    };

    private static ReleaseArtifactReadinessEvidence CreateArtifactReadiness(
        string? commitSha = null,
        bool artifactsRequired = true) => new()
    {
        ArtifactManifestId = artifactsRequired ? "artifact_manifest_ba" : null,
        BuildRunId = artifactsRequired ? "build_run_ba" : null,
        CommitSha = commitSha ?? CandidateCommitSha,
        Artifacts = artifactsRequired ? ["IronDev.zip"] : [],
        Checksums = artifactsRequired ? ["sha256:abcdef"] : [],
        StorageLocation = artifactsRequired ? "artifact-store://release-readiness/1.2.3" : null,
        ArtifactPolicy = artifactsRequired ? "required" : "not-required",
        ArtifactsRequired = artifactsRequired,
        ArtifactsReady = artifactsRequired,
        NotApplicableReason = artifactsRequired ? null : "No artifact publication is required for this readiness decision.",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T09:46:00Z")
    };

    private static ReleaseCandidateDecisionRecord CreateAzDecision() => new()
    {
        ReleaseCandidateDecisionId = "release_candidate_decision_ba",
        Decision = ReleaseCandidateDecision.ApprovedForReleaseExecutor,
        DecisionMadeBy = "release-manager",
        DecisionMadeAtUtc = DateTimeOffset.Parse("2026-06-20T09:20:00Z"),
        DecisionRationale = "Release candidate package is ready for the future release executor.",
        ExpectedRepository = "owner/repo",
        ExpectedCommitSha = CandidateCommitSha,
        ExpectedVersion = "1.2.3",
        ExpectedReleaseSourceBranch = "main",
        ExpectedReleaseChannel = "Stable"
    };

    private static ReleaseReadinessDecisionEvidence CreateDecision(
        string releaseCandidatePackageId = "release_candidate_pkg_ba",
        string? commitSha = null,
        string version = "1.2.3",
        string tag = "v1.2.3",
        string repository = "owner/repo",
        string branch = "main",
        string channel = "Stable",
        string decisionBy = "release-captain",
        string? artifactManifestId = "artifact_manifest_ba") => new()
    {
        ReleaseReadinessDecisionId = "release_readiness_decision_ba",
        Decision = ReleaseReadinessDecision.ApprovedForReleaseExecutor,
        DecisionMadeBy = decisionBy,
        DecisionMadeAtUtc = DateTimeOffset.Parse("2026-06-20T09:50:00Z"),
        DecisionRationale = "Release readiness decision package is ready for the future release executor.",
        ExpectedRepository = repository,
        ExpectedCandidateCommitSha = commitSha ?? CandidateCommitSha,
        ExpectedVersion = version,
        ExpectedTagName = tag,
        ExpectedReleaseSourceBranch = branch,
        ExpectedReleaseChannel = channel,
        ExpectedArtifactManifestId = artifactManifestId,
        ExpectedReleaseCandidatePackageId = releaseCandidatePackageId
    };

    private static void AssertBoundary(ReleaseReadinessDecisionPackageBoundary boundary)
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
