using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockARControlledPrUpdatePackageTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockAR_Builder_CreatesPendingPackageWithoutPrMutationAuthority()
    {
        var proposal = CreatePatchProposal();
        var target = CreateTarget();
        var validation = CreateValidationReceipt(target.ExpectedCurrentHeadSha);

        var artifacts = ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = proposal,
            Target = target,
            ValidationReceipts = [validation],
            SourceApplyPending = true
        });

        var package = artifacts.Package;
        Assert.AreEqual(PrUpdatePackageVerdict.PackageIncomplete, package.Verdict);
        Assert.AreEqual(PrUpdateExecutionEligibility.NotEligible, package.ExecutionEligibility);
        Assert.IsFalse(package.CanExecuteBranchUpdate);
        Assert.IsTrue(package.ExpectedState.SourceApplyPending);
        Assert.IsTrue(package.PackageIssues.Contains("SourceApplyPending"));
        Assert.AreEqual(target.ExpectedCurrentHeadSha, package.RollbackPlan.PreUpdateHeadSha);
        Assert.IsTrue(package.RollbackPlan.RollbackAvailable);
        Assert.AreEqual(0, package.MissingValidationFamilies.Length);
        AssertBoundary(package.Boundary);
        AssertBoundary(artifacts.Receipt.Boundary);
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanUpdatePullRequest(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanPush(package));
    }

    [TestMethod]
    public void BlockAR_Builder_FailsClosedForWrongPrBranchHeadBaseAndClosedPr()
    {
        var proposal = CreatePatchProposal();
        var target = CreateTarget();
        var validation = CreateValidationReceipt(target.ExpectedCurrentHeadSha);

        var wrongPr = Build(proposal, target with { PrNumber = 999 }, validation).Package;
        Assert.AreEqual(PrUpdatePackageVerdict.PackageRejected, wrongPr.Verdict);
        Assert.IsTrue(wrongPr.PackageIssues.Contains("PatchProposalPrMismatch"));

        var wrongHead = Build(proposal, target with { ExpectedCurrentHeadSha = new string('c', 40) }, CreateValidationReceipt(new string('c', 40))).Package;
        Assert.AreEqual(PrUpdatePackageVerdict.PackageBlocked, wrongHead.Verdict);
        Assert.IsTrue(wrongHead.PackageIssues.Contains("PatchProposalHeadMismatch"));

        var wrongBase = Build(proposal, target with { BaseSha = new string('d', 40) }, validation).Package;
        Assert.AreEqual(PrUpdatePackageVerdict.PackageRejected, wrongBase.Verdict);
        Assert.IsTrue(wrongBase.PackageIssues.Contains("PatchProposalBaseShaMismatch"));

        var closed = Build(proposal, target with { PrState = "closed" }, validation).Package;
        Assert.AreEqual(PrUpdatePackageVerdict.PackageBlocked, closed.Verdict);
        Assert.IsTrue(closed.PackageIssues.Contains("PullRequestNotOpen"));

        var wrongValidationSha = Build(proposal, target, CreateValidationReceipt(new string('e', 40))).Package;
        Assert.AreEqual(PrUpdatePackageVerdict.PackageBlocked, wrongValidationSha.Verdict);
        Assert.IsTrue(wrongValidationSha.PackageIssues.Any(item => item.StartsWith("ValidationReceiptShaMismatch:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void BlockAR_Builder_RequiresValidationFamiliesAndBlocksSkippedRequiredLanes()
    {
        var proposal = CreatePatchProposal();
        var target = CreateTarget();
        var partialValidation = CreateValidationReceipt(
            target.ExpectedCurrentHeadSha,
            lanes: [Lane("focused-ar"), Lane("fast-authority-invariants"), Lane("build", ValidationCommandKind.Build)]);
        var partial = ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = proposal,
            Target = target,
            ValidationReceipts = [partialValidation],
            SourceApplyPending = true
        }).Package;

        Assert.AreEqual(PrUpdatePackageVerdict.PackageIncomplete, partial.Verdict);
        CollectionAssert.Contains(partial.MissingValidationFamilies, PrUpdateValidationFamily.ImpactedArea);
        CollectionAssert.Contains(partial.MissingValidationFamilies, PrUpdateValidationFamily.DiffCheck);

        var skippedValidation = CreateValidationReceipt(target.ExpectedCurrentHeadSha, skippedLanes: ["build"]);
        var skipped = ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = proposal,
            Target = target,
            ValidationReceipts = [skippedValidation],
            SourceApplyPending = true
        }).Package;

        Assert.AreEqual(PrUpdatePackageVerdict.PackageBlocked, skipped.Verdict);
        Assert.IsTrue(skipped.PackageIssues.Any(item => item.StartsWith("ValidationSkippedRequiredLane:", StringComparison.Ordinal)));
        Assert.IsFalse(skipped.CanExecuteBranchUpdate);
    }

    [TestMethod]
    public void BlockAR_Builder_CanBecomeExecutorReadyOnlyWithSourceApplyEvidenceAndValidation()
    {
        var proposal = CreatePatchProposal();
        var target = CreateTarget();
        var postHead = new string('f', 40);
        var sourceApply = CreateSourceApplyEvidence(target, proposal);
        var validation = CreateValidationReceipt(postHead);

        var package = ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = proposal,
            Target = target,
            ValidationReceipts = [validation],
            SourceApplyEvidence = sourceApply,
            SourceApplyPending = false,
            ExpectedPostUpdateHeadSha = postHead
        }).Package;

        Assert.AreEqual(PrUpdatePackageVerdict.PackageReadyForExecutor, package.Verdict);
        Assert.AreEqual(PrUpdateExecutionEligibility.Eligible, package.ExecutionEligibility);
        Assert.IsTrue(package.CanExecuteBranchUpdate);
        Assert.IsFalse(package.ExpectedState.SourceApplyPending);
        Assert.AreEqual(postHead, package.ExpectedState.ExpectedPostUpdateHeadSha);
        Assert.AreEqual(0, package.PackageIssues.Length);
        AssertBoundary(package.Boundary);
    }

    [TestMethod]
    public void BlockAR_Builder_BlocksSourceApplyEvidenceWithUnexpectedFiles()
    {
        var proposal = CreatePatchProposal();
        var target = CreateTarget();
        var postHead = new string('f', 40);
        var sourceApply = CreateSourceApplyEvidence(target, proposal) with
        {
            AppliedFiles =
            [
                .. proposal.ExpectedChangedFiles,
                "IronDev.Core/Governance/SomeOtherAuthorityFile.cs",
                "Docs/receipts/unrelated.md"
            ]
        };
        var validation = CreateValidationReceipt(postHead);

        var package = ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = proposal,
            Target = target,
            ValidationReceipts = [validation],
            SourceApplyEvidence = sourceApply,
            SourceApplyPending = false,
            ExpectedPostUpdateHeadSha = postHead
        }).Package;

        Assert.AreEqual(PrUpdatePackageVerdict.PackageBlocked, package.Verdict);
        Assert.AreEqual(PrUpdateExecutionEligibility.NotEligible, package.ExecutionEligibility);
        Assert.IsFalse(package.CanExecuteBranchUpdate);
        Assert.IsTrue(package.PackageIssues.Contains("SourceApplyUnexpectedFile:IronDev.Core/Governance/SomeOtherAuthorityFile.cs"));
        Assert.IsTrue(package.PackageIssues.Contains("SourceApplyUnexpectedFile:Docs/receipts/unrelated.md"));

        var emptyExpectedProposal = proposal with { ExpectedChangedFiles = [] };
        var emptyExpectedPackage = ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = emptyExpectedProposal,
            Target = target,
            ValidationReceipts = [validation],
            SourceApplyEvidence = sourceApply with { AppliedFiles = ["Docs/receipts/unrelated.md"] },
            SourceApplyPending = false,
            ExpectedPostUpdateHeadSha = postHead
        }).Package;

        Assert.AreEqual(PrUpdatePackageVerdict.PackageBlocked, emptyExpectedPackage.Verdict);
        Assert.IsFalse(emptyExpectedPackage.CanExecuteBranchUpdate);
        Assert.IsTrue(emptyExpectedPackage.PackageIssues.Contains("SourceApplyUnexpectedFile:Docs/receipts/unrelated.md"));
    }

    [TestMethod]
    public async Task BlockAR_Cli_PackageInspectStatusRecords_WithoutMutatingSource()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "source.txt");
            File.WriteAllText(sourcePath, "original");
            var proposal = CreatePatchProposal();
            var target = CreateTarget();
            var proposalPath = Path.Combine(root, "feedback-patch-proposal.json");
            var validationPath = Path.Combine(root, "validation-receipt.json");
            WriteJson(proposalPath, proposal);
            WriteJson(validationPath, CreateValidationReceipt(target.ExpectedCurrentHeadSha));
            var outPath = Path.Combine(root, "pr-update");

            var result = await RunCliAsync(
                "pr-update", "package",
                "--proposal", proposalPath,
                "--validation", validationPath,
                "--out", outPath,
                "--repo", target.Repository,
                "--pr", target.PrNumber.ToString(),
                "--pr-url", target.PrUrl,
                "--state", target.PrState,
                "--draft", target.PrDraftState.ToString(),
                "--target-branch", target.TargetBranch,
                "--expected-head", target.ExpectedCurrentHeadSha,
                "--base-branch", target.BaseBranch,
                "--base-sha", target.BaseSha,
                "--json").ConfigureAwait(false);

            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            Assert.AreEqual("original", File.ReadAllText(sourcePath));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "pr-update-package.json")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "pr-update-package-receipt.json")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "pr-update-package-summary.md")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "pr-update-validation-records.jsonl")));

            var package = ReadJson<ControlledPrUpdatePackage>(Path.Combine(outPath, "pr-update-package.json"));
            Assert.AreEqual(PrUpdatePackageVerdict.PackageIncomplete, package!.Verdict);
            Assert.IsFalse(package.CanExecuteBranchUpdate);
            AssertBoundary(package.Boundary);

            var inspect = await RunCliAsync("pr-update", "inspect", "--package", Path.Combine(outPath, "pr-update-package.json"), "--json").ConfigureAwait(false);
            Assert.AreEqual(0, inspect.ExitCode, inspect.Output + inspect.Error);
            var status = await RunCliAsync("pr-update", "status", "--package", Path.Combine(outPath, "pr-update-package.json")).ConfigureAwait(false);
            Assert.AreEqual(0, status.ExitCode, status.Output + status.Error);
            var records = await RunCliAsync("pr-update", "records", "--package", Path.Combine(outPath, "pr-update-package.json")).ConfigureAwait(false);
            Assert.AreEqual(0, records.ExitCode, records.Output + records.Error);
            StringAssert.Contains(records.Output, target.TargetBranch);

            var missing = await RunCliAsync(
                "pr-update", "package",
                "--proposal", Path.Combine(root, "missing.json"),
                "--validation", validationPath,
                "--out", outPath,
                "--repo", target.Repository,
                "--pr", target.PrNumber.ToString(),
                "--pr-url", target.PrUrl,
                "--state", target.PrState,
                "--draft", target.PrDraftState.ToString(),
                "--target-branch", target.TargetBranch,
                "--expected-head", target.ExpectedCurrentHeadSha,
                "--base-branch", target.BaseBranch,
                "--base-sha", target.BaseSha).ConfigureAwait(false);
            Assert.AreEqual(1, missing.ExitCode);
            StringAssert.Contains(missing.Error, "feedback patch proposal is missing or invalid");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAR_Cli_BlocksAuthorityShapedSubcommands()
    {
        foreach (var forbidden in new[] { "apply", "commit", "push", "execute", "update-branch", "ready", "request-reviewers", "approve", "merge", "release", "deploy", "continue" })
        {
            var result = await RunCliAsync("pr-update", forbidden, "--package", "package.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAR_StaticBoundaryAndReceipt_ProveNoPrMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliPrUpdatePackage.cs"));
        Assert.IsFalse(cli.Contains("RunProcessAsync", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"gh\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("merge_pull_request", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("enable_auto_merge", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AR_CONTROLLED_PR_UPDATE_PACKAGE.md"));
        StringAssert.Contains(receipt, "AR1 AQ proposal required.");
        StringAssert.Contains(receipt, "AR2 PR identity binding.");
        StringAssert.Contains(receipt, "AR3 validation evidence binding.");
        StringAssert.Contains(receipt, "AR4 source-apply and rollback posture.");
        StringAssert.Contains(receipt, "AR5 evidence-only package receipt.");
        StringAssert.Contains(receipt, "AR6 authority bypass tests.");
        StringAssert.Contains(receipt, "PR update package is not PR branch mutation.");
        StringAssert.Contains(receipt, "It does not apply patches.");
        StringAssert.Contains(receipt, "It does not push.");
        StringAssert.Contains(receipt, "It does not continue workflow.");

        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanApplyPatch(receipt));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanCommit(receipt));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanPush(receipt));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanUpdatePullRequest(receipt));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanMerge(receipt));
    }

    private static ControlledPrUpdatePackageArtifacts Build(FeedbackPatchProposal proposal, PrUpdateTarget target, ValidationRunReceipt validation) =>
        ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = proposal,
            Target = target,
            ValidationReceipts = [validation],
            SourceApplyPending = true
        });

    private static FeedbackPatchProposal CreatePatchProposal()
    {
        var package = new FeedbackRemediationPackage
        {
            FeedbackRemediationPackageId = "feedback_pkg_ar",
            RunId = "run-ar",
            RepositoryFullName = "owner/repo",
            PullRequestNumber = 463,
            CurrentHeadSha = new string('a', 40),
            PullRequestUrl = "https://github.com/owner/repo/pull/463",
            FeedbackItems = [],
            RemediationCandidates =
            [
                new FeedbackRemediationCandidate
                {
                    RemediationId = "feedback_remed_ar",
                    FeedbackItemIds = ["feedback-item-ar"],
                    Disposition = FeedbackDisposition.Remediate,
                    Rationale = "Rationale for AR.",
                    AffectedAreas = ["governance"],
                    LikelyFiles = ["IronDev.Core/Governance/ControlledPrUpdatePackage.cs"],
                    RiskLevel = FeedbackRiskLevel.High,
                    AuthorityRisk = true,
                    SuggestedValidationLanes = ["FocusedCurrentBlock", "ImpactedArea", "FastAuthorityInvariant", "Build", "DiffCheck"],
                    RequiresHumanDecision = false
                }
            ],
            EvidenceRefs = ["feedback-remediation-package.json"],
            ValidationExpectations = ["FocusedCurrentBlock", "ImpactedArea", "FastAuthorityInvariant", "Build", "DiffCheck"],
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T00:00:00Z")
        };

        return FeedbackPatchProposalBuilder.Build(new FeedbackPatchProposalInput
        {
            Package = package,
            ExpectedPrNumber = 463,
            ExpectedHeadSha = new string('a', 40),
            BaseSha = new string('b', 40),
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T00:00:00Z")
        }).Proposal;
    }

    private static PrUpdateTarget CreateTarget() => new()
    {
        Repository = "owner/repo",
        PrNumber = 463,
        PrUrl = "https://github.com/owner/repo/pull/463",
        PrState = "open",
        PrDraftState = true,
        TargetBranch = "ar/controlled-pr-update-package",
        ExpectedCurrentHeadSha = new string('a', 40),
        BaseBranch = "phase/close-feedback-loop",
        BaseSha = new string('b', 40),
        PackageCreatedAtUtc = DateTimeOffset.Parse("2026-06-20T00:01:00Z"),
        PackageCreatedBy = "tests"
    };

    private static PrUpdateSourceApplyEvidence CreateSourceApplyEvidence(PrUpdateTarget target, FeedbackPatchProposal proposal) => new()
    {
        SourceApplyReceiptId = Guid.NewGuid().ToString("D"),
        SourceApplyReceiptHash = Hash("source-apply"),
        SourceApplyRequestHash = Hash("source-apply-request"),
        SourceApplyDryRunReceiptHash = Hash("source-apply-dry-run"),
        RollbackSupportReceiptHash = Hash("rollback-support"),
        ExpectedBranch = target.TargetBranch,
        ObservedBranch = target.TargetBranch,
        ApplySucceeded = true,
        MutationOccurred = true,
        PartialApplyOccurred = false,
        AppliedFiles = proposal.ExpectedChangedFiles,
        AppliedAtUtc = DateTimeOffset.Parse("2026-06-20T00:02:00Z")
    };

    private static ValidationRunReceipt CreateValidationReceipt(string commitSha, ValidationLane[]? lanes = null, string[]? skippedLanes = null)
    {
        var required = lanes ??
        [
            Lane("focused-ar"),
            Lane("impacted-governance-tests"),
            Lane("fast-authority-invariants"),
            Lane("build", ValidationCommandKind.Build),
            Lane("diff-check", ValidationCommandKind.DiffCheck)
        ];
        var skipped = skippedLanes ?? [];
        var results = required
            .Where(lane => !skipped.Contains(lane.Name, StringComparer.OrdinalIgnoreCase))
            .Select(Result)
            .ToArray();
        return new ValidationRunReceipt
        {
            ValidationRunId = "validation_run_ar_" + Guid.NewGuid().ToString("N")[..8],
            ValidationPlanId = "validation_plan_ar",
            Branch = "ar/controlled-pr-update-package",
            CommitSha = commitSha,
            ChangedFilesHash = Hash("changed-files"),
            StartedUtc = DateTimeOffset.Parse("2026-06-20T00:03:00Z"),
            FinishedUtc = DateTimeOffset.Parse("2026-06-20T00:04:00Z"),
            Verdict = skipped.Length == 0 ? ValidationRunVerdict.Passed : ValidationRunVerdict.Incomplete,
            RequiredLanes = required,
            Results = results,
            SkippedLanes = skipped,
            SkippedLaneReasons = skipped.Select(lane => $"Skipped {lane}").ToArray(),
            WorktreeCleanBefore = true,
            WorktreeCleanAfter = true,
            CachePolicy = new ValidationCachePolicy(),
            Boundary = ValidationRuntimeBoundary.Evidence
        };
    }

    private static ValidationLane Lane(string name, ValidationCommandKind kind = ValidationCommandKind.Test) => new()
    {
        Name = name,
        Reason = $"Required AR lane {name}.",
        Requirement = ValidationLaneRequirement.Required,
        Timeout = TimeSpan.FromMinutes(5),
        CommandKind = kind,
        Commands = [name],
        SafeToParallelize = true,
        ParallelismGroup = "ar",
        CacheCategory = kind == ValidationCommandKind.Build ? "build" : kind == ValidationCommandKind.DiffCheck ? "diff" : "test"
    };

    private static ValidationProcessResult Result(ValidationLane lane) => new()
    {
        LaneName = lane.Name,
        Command = lane.Name,
        Arguments = [],
        WorkingDirectory = "repo",
        StartedUtc = DateTimeOffset.Parse("2026-06-20T00:03:00Z"),
        FinishedUtc = DateTimeOffset.Parse("2026-06-20T00:03:10Z"),
        DurationMs = 10000,
        ExitCode = 0,
        TimedOut = false,
        Cancelled = false,
        ProcessTreeKillAttempted = false,
        ProcessTreeKillSucceeded = false,
        StdoutPath = "stdout.log",
        StderrPath = "stderr.log",
        FailureClassification = ValidationFailureKind.Passed
    };

    private static void AssertBoundary(PrUpdateBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanApplyPatch);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanStage);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanUpdatePullRequest);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-ar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for Windows file handles.
        }
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

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
