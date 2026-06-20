using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockATReadyForReviewSeparationTests
{
    private static readonly string PreHead = new('a', 40);
    private static readonly string BaseSha = new('b', 40);
    private static readonly string ReadyHead = new('c', 40);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockAT_Package_CreatesEligibleReadyForReviewPackageWithoutMutation()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "source.txt");
            File.WriteAllText(sourcePath, "original");
            var asReceiptPath = Path.Combine(root, "as-receipt.json");
            var validationPath = Path.Combine(root, "validation-receipt.json");
            var phaseReceiptPath = Path.Combine(root, "phase-receipt.md");
            var outPath = Path.Combine(root, "ready");
            WriteJson(asReceiptPath, CreateBranchUpdateReceipt());
            WriteJson(validationPath, CreateValidationReceipt(ReadyHead));
            File.WriteAllText(phaseReceiptPath, PhaseReceiptText());

            var result = await RunCliAsync(
                "ready", "package",
                "--repo", "owner/repo",
                "--pr", "466",
                "--state", "open",
                "--draft", "true",
                "--branch", "phase/close-feedback-loop",
                "--head", ReadyHead,
                "--observed-head", ReadyHead,
                "--base", "main",
                "--base-sha", BaseSha,
                "--as-receipt", asReceiptPath,
                "--validation", validationPath,
                "--phase-receipt", phaseReceiptPath,
                "--out", outPath,
                "--json").ConfigureAwait(false);

            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            Assert.AreEqual("original", File.ReadAllText(sourcePath));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "ready-for-review-package.json")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "ready-for-review-separation-receipt.json")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "ready-for-review-summary.md")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "ready-for-review-validation-records.jsonl")));

            var package = ReadJson<ReadyForReviewEligibilityPackage>(Path.Combine(outPath, "ready-for-review-package.json"));
            Assert.IsNotNull(package);
            Assert.AreEqual(ReadyForReviewEligibilityVerdict.EligibleForReadyExecutor, package!.Verdict);
            Assert.IsTrue(package.CanMarkReadyForReview);
            Assert.AreEqual(0, package.BlockReasons.Length);
            AssertBoundary(package.Boundary);
            Assert.IsFalse(ReadyForReviewBypassEvaluator.CanMarkReadyForReview(package));

            var inspect = await RunCliAsync("ready", "inspect", "--package", Path.Combine(outPath, "ready-for-review-package.json"), "--json").ConfigureAwait(false);
            Assert.AreEqual(0, inspect.ExitCode, inspect.Output + inspect.Error);
            var status = await RunCliAsync("ready", "status", "--package", Path.Combine(outPath, "ready-for-review-package.json")).ConfigureAwait(false);
            Assert.AreEqual(0, status.ExitCode, status.Output + status.Error);
            var records = await RunCliAsync("ready", "records", "--package", Path.Combine(outPath, "ready-for-review-package.json")).ConfigureAwait(false);
            Assert.AreEqual(0, records.ExitCode, records.Output + records.Error);
            StringAssert.Contains(records.Output, "phase/close-feedback-loop");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAT_Cli_RequiresObservedPrStateEvidenceBeforeEligibility()
    {
        var root = CreateTempRoot();
        try
        {
            var asReceiptPath = Path.Combine(root, "as-receipt.json");
            var validationPath = Path.Combine(root, "validation-receipt.json");
            var phaseReceiptPath = Path.Combine(root, "phase-receipt.md");
            WriteJson(asReceiptPath, CreateBranchUpdateReceipt());
            WriteJson(validationPath, CreateValidationReceipt(ReadyHead));
            File.WriteAllText(phaseReceiptPath, PhaseReceiptText());

            var missingObservedOut = Path.Combine(root, "missing-observed");
            var missingObserved = await RunCliAsync(ReadyPackageArgs(missingObservedOut, asReceiptPath, validationPath, phaseReceiptPath, observedHead: null)).ConfigureAwait(false);
            Assert.AreEqual(1, missingObserved.ExitCode);
            StringAssert.Contains(missingObserved.Error, "Missing required option: --observed-head");
            Assert.IsFalse(File.Exists(Path.Combine(missingObservedOut, "ready-for-review-package.json")));

            var missingStateOut = Path.Combine(root, "missing-state");
            var missingState = await RunCliAsync(ReadyPackageArgs(missingStateOut, asReceiptPath, validationPath, phaseReceiptPath, state: null)).ConfigureAwait(false);
            Assert.AreEqual(1, missingState.ExitCode);
            StringAssert.Contains(missingState.Error, "Missing required option: --state");
            Assert.IsFalse(File.Exists(Path.Combine(missingStateOut, "ready-for-review-package.json")));

            var missingDraftOut = Path.Combine(root, "missing-draft");
            var missingDraft = await RunCliAsync(ReadyPackageArgs(missingDraftOut, asReceiptPath, validationPath, phaseReceiptPath, draft: null)).ConfigureAwait(false);
            Assert.AreEqual(1, missingDraft.ExitCode);
            StringAssert.Contains(missingDraft.Error, "Missing required option: --draft");
            Assert.IsFalse(File.Exists(Path.Combine(missingDraftOut, "ready-for-review-package.json")));

            var driftOut = Path.Combine(root, "head-drift");
            var drift = await RunCliAsync(ReadyPackageArgs(driftOut, asReceiptPath, validationPath, phaseReceiptPath, observedHead: new string('d', 40))).ConfigureAwait(false);
            Assert.AreEqual(1, drift.ExitCode);
            var driftPackage = ReadJson<ReadyForReviewEligibilityPackage>(Path.Combine(driftOut, "ready-for-review-package.json"));
            Assert.IsNotNull(driftPackage);
            Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, driftPackage!.Verdict);
            Assert.IsFalse(driftPackage.CanMarkReadyForReview);
            CollectionAssert.Contains(driftPackage.BlockReasons, ReadyForReviewBlockReason.HeadShaMismatch);

            var readyOut = Path.Combine(root, "already-ready");
            var ready = await RunCliAsync(ReadyPackageArgs(readyOut, asReceiptPath, validationPath, phaseReceiptPath, draft: "false")).ConfigureAwait(false);
            Assert.AreEqual(1, ready.ExitCode);
            var readyPackage = ReadJson<ReadyForReviewEligibilityPackage>(Path.Combine(readyOut, "ready-for-review-package.json"));
            Assert.IsNotNull(readyPackage);
            Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, readyPackage!.Verdict);
            Assert.IsFalse(readyPackage.CanMarkReadyForReview);
            CollectionAssert.Contains(readyPackage.BlockReasons, ReadyForReviewBlockReason.PullRequestNotDraft);
            CollectionAssert.Contains(readyPackage.BlockReasons, ReadyForReviewBlockReason.PullRequestAlreadyReady);

            var closedOut = Path.Combine(root, "closed");
            var closed = await RunCliAsync(ReadyPackageArgs(closedOut, asReceiptPath, validationPath, phaseReceiptPath, state: "closed")).ConfigureAwait(false);
            Assert.AreEqual(1, closed.ExitCode);
            var closedPackage = ReadJson<ReadyForReviewEligibilityPackage>(Path.Combine(closedOut, "ready-for-review-package.json"));
            Assert.IsNotNull(closedPackage);
            Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, closedPackage!.Verdict);
            Assert.IsFalse(closedPackage.CanMarkReadyForReview);
            CollectionAssert.Contains(closedPackage.BlockReasons, ReadyForReviewBlockReason.PullRequestNotOpen);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockAT_Package_BlocksMissingOrFailedAsReceipt()
    {
        var missing = ReadyForReviewSeparationBuilder.Build(CreateInput(includeBranchReceipt: false)).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Incomplete, missing.Verdict);
        CollectionAssert.Contains(missing.BlockReasons, ReadyForReviewBlockReason.MissingBranchUpdateEvidence);
        Assert.IsFalse(missing.CanMarkReadyForReview);

        var failed = ReadyForReviewSeparationBuilder.Build(CreateInput(CreateBranchUpdateReceipt() with
        {
            ExecutionVerdict = PrBranchUpdateExecutionVerdict.Failed,
            Pushed = false
        })).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, failed.Verdict);
        CollectionAssert.Contains(failed.BlockReasons, ReadyForReviewBlockReason.BranchUpdateNotExecuted);
        CollectionAssert.Contains(failed.BlockReasons, ReadyForReviewBlockReason.BranchUpdateReceiptNotPushed);

        var wrongPr = ReadyForReviewSeparationBuilder.Build(CreateInput(CreateBranchUpdateReceipt() with { PrNumber = 999 })).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, wrongPr.Verdict);
        CollectionAssert.Contains(wrongPr.BlockReasons, ReadyForReviewBlockReason.BranchUpdateReceiptPrMismatch);

        var wrongHead = ReadyForReviewSeparationBuilder.Build(CreateInput(CreateBranchUpdateReceipt() with
        {
            PostExecutionHeadSha = new string('d', 40),
            CommitSha = new string('d', 40)
        })).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, wrongHead.Verdict);
        CollectionAssert.Contains(wrongHead.BlockReasons, ReadyForReviewBlockReason.BranchUpdateReceiptHeadMismatch);
    }

    [TestMethod]
    public void BlockAT_Package_BlocksStaleOrMissingValidation()
    {
        var missing = ReadyForReviewSeparationBuilder.Build(CreateInput(validationReceipts: [])).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Incomplete, missing.Verdict);
        CollectionAssert.Contains(missing.BlockReasons, ReadyForReviewBlockReason.MissingValidationEvidence);

        var stale = ReadyForReviewSeparationBuilder.Build(CreateInput(validationReceipts: [CreateValidationReceipt(new string('d', 40))])).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, stale.Verdict);
        CollectionAssert.Contains(stale.BlockReasons, ReadyForReviewBlockReason.ValidationReceiptShaMismatch);

        var failed = ReadyForReviewSeparationBuilder.Build(CreateInput(validationReceipts: [CreateValidationReceipt(ReadyHead) with { Verdict = ValidationRunVerdict.Failed }])).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, failed.Verdict);
        CollectionAssert.Contains(failed.BlockReasons, ReadyForReviewBlockReason.ValidationReceiptNotPassed);

        var missingFamily = ReadyForReviewSeparationBuilder.Build(CreateInput(validationReceipts:
        [
            CreateValidationReceipt(
                ReadyHead,
                lanes:
                [
                    Lane("focused-at"),
                    Lane("impacted-governance-tests"),
                    Lane("build", ValidationCommandKind.Build),
                    Lane("diff-check", ValidationCommandKind.DiffCheck)
                ])
        ])).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Incomplete, missingFamily.Verdict);
        CollectionAssert.Contains(missingFamily.MissingValidationFamilies, ReadyForReviewValidationFamily.FastAuthorityInvariant);
        CollectionAssert.Contains(missingFamily.MissingValidationFamilies, ReadyForReviewValidationFamily.PhaseAuthority);

        var skipped = ReadyForReviewSeparationBuilder.Build(CreateInput(validationReceipts: [CreateValidationReceipt(ReadyHead, skippedLanes: ["build"])])).Package;
        Assert.IsTrue(skipped.BlockReasons.Contains(ReadyForReviewBlockReason.SkippedRequiredValidationLane));
        Assert.IsFalse(skipped.CanMarkReadyForReview);
    }

    [TestMethod]
    public void BlockAT_Package_BlocksWrongPrState()
    {
        var closed = ReadyForReviewSeparationBuilder.Build(CreateInput() with { PullRequestState = "closed" }).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, closed.Verdict);
        CollectionAssert.Contains(closed.BlockReasons, ReadyForReviewBlockReason.PullRequestNotOpen);

        var ready = ReadyForReviewSeparationBuilder.Build(CreateInput() with { PullRequestDraft = false }).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, ready.Verdict);
        CollectionAssert.Contains(ready.BlockReasons, ReadyForReviewBlockReason.PullRequestNotDraft);
        CollectionAssert.Contains(ready.BlockReasons, ReadyForReviewBlockReason.PullRequestAlreadyReady);

        var headDrift = ReadyForReviewSeparationBuilder.Build(CreateInput() with { ObservedHeadSha = new string('d', 40) }).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, headDrift.Verdict);
        CollectionAssert.Contains(headDrift.BlockReasons, ReadyForReviewBlockReason.HeadShaMismatch);

        var wrongBaseBranch = ReadyForReviewSeparationBuilder.Build(CreateInput() with { BaseBranch = "develop", ExpectedBaseBranch = "main" }).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, wrongBaseBranch.Verdict);
        CollectionAssert.Contains(wrongBaseBranch.BlockReasons, ReadyForReviewBlockReason.BaseBranchMismatch);

        var wrongBaseSha = ReadyForReviewSeparationBuilder.Build(CreateInput() with { BaseSha = new string('e', 40), ExpectedBaseSha = BaseSha }).Package;
        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Blocked, wrongBaseSha.Verdict);
        CollectionAssert.Contains(wrongBaseSha.BlockReasons, ReadyForReviewBlockReason.BaseShaMismatch);
    }

    [TestMethod]
    public async Task BlockAT_Cli_BlocksAuthorityShapedSubcommands()
    {
        foreach (var forbidden in new[] { "mark-ready", "request-reviewers", "approve", "resolve-comments", "merge", "auto-merge", "release", "deploy", "tag", "publish", "promote-memory", "continue" })
        {
            var result = await RunCliAsync("ready", forbidden, "--package", "ready-package.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAT_StaticBoundary_ProvesNoReadyReviewMergeReleaseMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReadyForReview.cs"));
        Assert.IsFalse(cli.Contains("RunProcessAsync", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"gh\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("mark_pull_request_ready_for_review", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("request_pull_request_reviewers", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("merge_pull_request", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("enable_auto_merge", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AT_READY_FOR_REVIEW_SEPARATION.md"));
        StringAssert.Contains(receipt, "PR branch update is not ready-for-review.");
        StringAssert.Contains(receipt, "Ready-for-review is not reviewer request.");
        StringAssert.Contains(receipt, "Reviewer request is not approval.");
        StringAssert.Contains(receipt, "Approval is not merge.");
        StringAssert.Contains(receipt, "Merge is not release.");
        StringAssert.Contains(receipt, "Release is not deployment.");
        StringAssert.Contains(receipt, "Validation evidence is not approval.");
        StringAssert.Contains(receipt, "Ready-for-review package is not ready-for-review execution.");

        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanMarkReadyForReview(receipt));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanRequestReviewers(receipt));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanMerge(receipt));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanRelease(receipt));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanContinueWorkflow(receipt));
    }

    [TestMethod]
    public void BlockAT_ReadyPackageDoesNotRequestReviewers()
    {
        var package = ReadyForReviewSeparationBuilder.Build(CreateInput()).Package;

        Assert.AreEqual(ReadyForReviewEligibilityVerdict.EligibleForReadyExecutor, package.Verdict);
        Assert.IsTrue(package.CanMarkReadyForReview);
        AssertBoundary(package.Boundary);
        Assert.IsFalse(package.Boundary.CanRequestReviewers);
        Assert.IsFalse(package.Boundary.CanResolveReviewThreads);
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanMarkReadyForReview(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanRequestReviewers(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanResolveReviewThreads(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanApprove(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanMerge(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanRelease(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanContinueWorkflow(package));
    }

    private static ReadyForReviewSeparationInput CreateInput(
        PrBranchUpdateExecutionReceipt? branchReceipt = null,
        ValidationRunReceipt[]? validationReceipts = null,
        bool includeBranchReceipt = true) => new()
    {
        Repository = "owner/repo",
        PullRequestNumber = 466,
        PullRequestUrl = "https://github.com/owner/repo/pull/466",
        PullRequestState = "open",
        PullRequestDraft = true,
        HeadBranch = "phase/close-feedback-loop",
        ExpectedHeadSha = ReadyHead,
        ObservedHeadSha = ReadyHead,
        BaseBranch = "main",
        BaseSha = BaseSha,
        ExpectedBaseBranch = "main",
        ExpectedBaseSha = BaseSha,
        BranchUpdateReceipt = includeBranchReceipt ? branchReceipt ?? CreateBranchUpdateReceipt() : null,
        ValidationReceipts = validationReceipts ?? [CreateValidationReceipt(ReadyHead)],
        PhaseAuthorityReceiptId = "PHASE1_CLOSE_FEEDBACK_LOOP",
        PhaseAuthorityReceiptText = PhaseReceiptText(),
        PackageCreatedBy = "tests",
        PackageCreatedAtUtc = DateTimeOffset.Parse("2026-06-20T03:00:00Z")
    };

    private static PrBranchUpdateExecutionReceipt CreateBranchUpdateReceipt() => new()
    {
        ExecutionId = "pr_branch_update_exec_at",
        PackageId = "pr_update_pkg_at",
        Repository = "owner/repo",
        PrNumber = 466,
        Branch = "phase/close-feedback-loop",
        PreExecutionHeadSha = PreHead,
        PostExecutionHeadSha = ReadyHead,
        CommitSha = ReadyHead,
        Pushed = true,
        PushRemote = "origin",
        PushBranch = "phase/close-feedback-loop",
        SourceApplyReceipt = "source_apply_at",
        ValidationReceipts = ["validation_at"],
        DirtyWorktreeBefore = true,
        DirtyWorktreeAfter = false,
        ExpectedFilesChanged = ["IronDev.Core/Governance/ReadyForReviewSeparation.cs"],
        ActualFilesChanged = ["IronDev.Core/Governance/ReadyForReviewSeparation.cs"],
        RollbackAvailable = true,
        RollbackInstructions = "Rollback plan is not rollback execution.",
        ExecutionVerdict = PrBranchUpdateExecutionVerdict.Executed,
        FailureClassification = PrBranchUpdateFailureKind.None,
        Issues = [],
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T03:01:00Z"),
        Boundary = PrBranchUpdateBoundary.Executor
    };

    private static ValidationRunReceipt CreateValidationReceipt(string commitSha, ValidationLane[]? lanes = null, string[]? skippedLanes = null)
    {
        var required = lanes ??
        [
            Lane("focused-at"),
            Lane("impacted-governance-tests"),
            Lane("fast-authority-invariants"),
            Lane("build", ValidationCommandKind.Build),
            Lane("diff-check", ValidationCommandKind.DiffCheck),
            Lane("phase-authority")
        ];
        var skipped = skippedLanes ?? [];
        var results = required
            .Where(lane => !skipped.Contains(lane.Name, StringComparer.OrdinalIgnoreCase))
            .Select(Result)
            .ToArray();
        return new ValidationRunReceipt
        {
            ValidationRunId = "validation_run_at_" + Guid.NewGuid().ToString("N")[..8],
            ValidationPlanId = "validation_plan_at",
            Branch = "at/ready-for-review-separation",
            CommitSha = commitSha,
            ChangedFilesHash = Hash("changed-files"),
            StartedUtc = DateTimeOffset.Parse("2026-06-20T03:02:00Z"),
            FinishedUtc = DateTimeOffset.Parse("2026-06-20T03:03:00Z"),
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
        Reason = $"Required AT lane {name}.",
        Requirement = ValidationLaneRequirement.Required,
        Timeout = TimeSpan.FromMinutes(5),
        CommandKind = kind,
        Commands = [name],
        SafeToParallelize = true,
        ParallelismGroup = "at",
        CacheCategory = kind == ValidationCommandKind.Build ? "build" : kind == ValidationCommandKind.DiffCheck ? "diff" : "test"
    };

    private static ValidationProcessResult Result(ValidationLane lane) => new()
    {
        LaneName = lane.Name,
        Command = lane.Name,
        Arguments = [],
        WorkingDirectory = "repo",
        StartedUtc = DateTimeOffset.Parse("2026-06-20T03:02:00Z"),
        FinishedUtc = DateTimeOffset.Parse("2026-06-20T03:02:10Z"),
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

    private static string PhaseReceiptText() => """
        # Phase 1 Close Feedback Loop

        Phase 1 closes the feedback loop.

        PR branch update is not ready-for-review.

        Validation evidence is not approval.
        """;

    private static void AssertBoundary(ReadyForReviewBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanResolveReviewThreads);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanAutoMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanTag);
        Assert.IsFalse(boundary.CanPublish);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
    }

    private static string[] ReadyPackageArgs(
        string outPath,
        string asReceiptPath,
        string validationPath,
        string phaseReceiptPath,
        string? state = "open",
        string? draft = "true",
        string? observedHead = "__ready_head__")
    {
        var args = new List<string>
        {
            "ready",
            "package",
            "--repo",
            "owner/repo",
            "--pr",
            "466",
            "--branch",
            "phase/close-feedback-loop",
            "--head",
            ReadyHead,
            "--base",
            "main",
            "--base-sha",
            BaseSha,
            "--as-receipt",
            asReceiptPath,
            "--validation",
            validationPath,
            "--phase-receipt",
            phaseReceiptPath,
            "--out",
            outPath
        };

        if (state is not null)
            args.AddRange(["--state", state]);
        if (draft is not null)
            args.AddRange(["--draft", draft]);
        if (observedHead is not null)
            args.AddRange(["--observed-head", observedHead == "__ready_head__" ? ReadyHead : observedHead]);

        return args.ToArray();
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
        var path = Path.Combine(Path.GetTempPath(), "irondev-at-" + Guid.NewGuid().ToString("N"));
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
