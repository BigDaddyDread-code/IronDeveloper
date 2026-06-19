using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockASControlledPrBranchUpdateExecutorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockAS_Executor_CommitsAndPushesOnlyEligiblePackageEnvelope()
    {
        var package = CreateReadyPackage(expectedDiffHash: "sha256:expected-diff");
        var client = new FakePrBranchUpdateClient
        {
            Observations =
            [
                GoodObserved(package, "sha256:expected-diff"),
                CleanObserved(package, "commit-as", package.Target.ExpectedCurrentHeadSha),
                CleanObserved(package, "commit-as", "commit-as")
            ]
        };

        var result = await PrBranchUpdateExecutor.ExecuteAsync(new PrBranchUpdateExecutionRequest
        {
            Package = package,
            ExpectedPullRequestNumber = package.Target.PrNumber,
            TargetRemote = "origin",
            WorkspacePath = "workspace",
            RequestedBy = "tests",
            RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T01:00:00Z")
        }, client).ConfigureAwait(false);

        Assert.AreEqual(PrBranchUpdateExecutionVerdict.Executed, result.Verdict);
        Assert.AreEqual(PrBranchUpdateFailureKind.None, result.FailureKind);
        Assert.AreEqual(1, client.StageCalls);
        Assert.AreEqual(1, client.CommitCalls);
        Assert.AreEqual(1, client.PushCalls);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(package.PrUpdatePackageId, result.Receipt!.PackageId);
        Assert.AreEqual("commit-as", result.Receipt.CommitSha);
        Assert.IsTrue(result.Receipt.Pushed);
        CollectionAssert.AreEquivalent(package.ExpectedState.ExpectedChangedFiles, result.Receipt.ActualFilesChanged);
        AssertBoundary(result.Receipt.Boundary);
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanMarkReadyForReview(result.Receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanRequestReviewers(result.Receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanMerge(result.Receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanRelease(result.Receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanContinueWorkflow(result.Receipt));
    }

    [TestMethod]
    public async Task BlockAS_Executor_FailsWhenPostCommitObservationLeavesExtraDirtyFile()
    {
        var package = CreateReadyPackage(expectedDiffHash: "sha256:expected-diff");
        var client = new FakePrBranchUpdateClient
        {
            Observations =
            [
                GoodObserved(package, "sha256:expected-diff"),
                CleanObserved(package, "commit-as", package.Target.ExpectedCurrentHeadSha) with
                {
                    DirtyFiles = ["Docs/post-commit-drift.md"]
                }
            ]
        };

        var result = await PrBranchUpdateExecutor.ExecuteAsync(new PrBranchUpdateExecutionRequest
        {
            Package = package,
            ExpectedPullRequestNumber = package.Target.PrNumber,
            TargetRemote = "origin",
            WorkspacePath = "workspace",
            RequestedBy = "tests",
            RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T01:05:00Z")
        }, client).ConfigureAwait(false);

        Assert.AreEqual(PrBranchUpdateExecutionVerdict.Failed, result.Verdict);
        Assert.AreEqual(PrBranchUpdateFailureKind.PostCommitDirtyWorktree, result.FailureKind);
        Assert.AreEqual(1, client.StageCalls);
        Assert.AreEqual(1, client.CommitCalls);
        Assert.AreEqual(0, client.PushCalls);
        Assert.IsNotNull(result.Receipt);
        Assert.IsFalse(result.Receipt!.Pushed);
        Assert.IsTrue(result.Receipt.DirtyWorktreeAfter);
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("PostCommitDirtyWorktree", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task BlockAS_Executor_BlocksRequestRemoteOverrideBeforeMutation()
    {
        var package = CreateReadyPackage(expectedDiffHash: "sha256:expected-diff");
        var client = new FakePrBranchUpdateClient
        {
            Observed = GoodObserved(package, "sha256:expected-diff")
        };

        var result = await PrBranchUpdateExecutor.ExecuteAsync(new PrBranchUpdateExecutionRequest
        {
            Package = package,
            ExpectedPullRequestNumber = package.Target.PrNumber,
            TargetRemote = "upstream",
            WorkspacePath = "workspace",
            RequestedBy = "tests",
            RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T01:06:00Z")
        }, client).ConfigureAwait(false);

        Assert.AreEqual(PrBranchUpdateExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(PrBranchUpdateFailureKind.PushNotAllowed, result.FailureKind);
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("TargetRemoteMismatch", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, client.StageCalls);
        Assert.AreEqual(0, client.CommitCalls);
        Assert.AreEqual(0, client.PushCalls);
    }

    [TestMethod]
    public async Task BlockAS_Executor_RejectsMissingAndIneligiblePackagesWithoutMutation()
    {
        var missingClient = new FakePrBranchUpdateClient();
        var missing = await PrBranchUpdateExecutor.ExecuteAsync(new PrBranchUpdateExecutionRequest
        {
            Package = null,
            RequestedBy = "tests"
        }, missingClient).ConfigureAwait(false);

        Assert.AreEqual(PrBranchUpdateExecutionVerdict.Blocked, missing.Verdict);
        Assert.AreEqual(PrBranchUpdateFailureKind.MissingPackage, missing.FailureKind);
        Assert.AreEqual(0, missingClient.StageCalls + missingClient.CommitCalls + missingClient.PushCalls);

        var package = CreateReadyPackage(expectedDiffHash: "sha256:expected-diff") with
        {
            Verdict = PrUpdatePackageVerdict.PackageIncomplete,
            ExecutionEligibility = PrUpdateExecutionEligibility.NotEligible,
            CanExecuteBranchUpdate = false
        };
        var ineligibleClient = new FakePrBranchUpdateClient { Observed = GoodObserved(package, "sha256:expected-diff") };
        var ineligible = await PrBranchUpdateExecutor.ExecuteAsync(new PrBranchUpdateExecutionRequest
        {
            Package = package,
            ExpectedPullRequestNumber = package.Target.PrNumber,
            RequestedBy = "tests"
        }, ineligibleClient).ConfigureAwait(false);

        Assert.AreEqual(PrBranchUpdateExecutionVerdict.Blocked, ineligible.Verdict);
        Assert.AreEqual(PrBranchUpdateFailureKind.PackageIneligible, ineligible.FailureKind);
        Assert.AreEqual(0, ineligibleClient.StageCalls + ineligibleClient.CommitCalls + ineligibleClient.PushCalls);
    }

    [TestMethod]
    public async Task BlockAS_Executor_BlocksBranchHeadDiffFileAndGeneratedArtifactDrift()
    {
        var package = CreateReadyPackage(expectedDiffHash: "sha256:expected-diff");
        var cases = new (string Name, PrBranchUpdateObservedState Observed, string ExpectedIssue)[]
        {
            ("wrong-branch", GoodObserved(package, "sha256:expected-diff") with { Branch = "wrong/branch" }, "WrongBranch"),
            ("stale-head", GoodObserved(package, "sha256:expected-diff") with { HeadSha = new string('c', 40) }, "ExpectedHeadMismatch"),
            ("remote-stale", GoodObserved(package, "sha256:expected-diff") with { RemoteHeadSha = new string('d', 40) }, "RemoteHeadMismatch"),
            ("wrong-pr", GoodObserved(package, "sha256:expected-diff"), "WrongPullRequest"),
            ("extra-file", GoodObserved(package, "sha256:expected-diff") with { DirtyFiles = [.. package.ExpectedState.ExpectedChangedFiles, "Docs/unexpected.md"] }, "UndeclaredFileChange:Docs/unexpected.md"),
            ("generated", GoodObserved(package, "sha256:expected-diff") with { DirtyFiles = [.. package.ExpectedState.ExpectedChangedFiles, "IronDev.Core/obj/project.assets.json"] }, "GeneratedRestoreArtifact:IronDev.Core/obj/project.assets.json"),
            ("diff", GoodObserved(package, "sha256:different") , "DiffHashMismatch")
        };

        foreach (var item in cases)
        {
            var client = new FakePrBranchUpdateClient { Observed = item.Observed };
            var result = await PrBranchUpdateExecutor.ExecuteAsync(new PrBranchUpdateExecutionRequest
            {
                Package = package,
                ExpectedPullRequestNumber = item.Name == "wrong-pr" ? 999 : package.Target.PrNumber,
                RequestedBy = "tests"
            }, client).ConfigureAwait(false);

            Assert.AreEqual(PrBranchUpdateExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains(item.ExpectedIssue, StringComparison.OrdinalIgnoreCase)), item.Name);
            Assert.AreEqual(0, client.StageCalls + client.CommitCalls + client.PushCalls, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockAS_Cli_ExecuteCommitsAndPushesExpectedBranchOnly()
    {
        var root = CreateTempRoot();
        try
        {
            var remote = Path.Combine(root, "remote.git");
            var work = Path.Combine(root, "work");
            AssertGitOk(Git("init", ["--bare", remote], root), "git init bare");
            AssertGitOk(Git("clone", [remote, work], root), "git clone");
            AssertGitOk(Git("config", ["user.email", "as-tests@example.invalid"], work), "git config email");
            AssertGitOk(Git("config", ["user.name", "AS Tests"], work), "git config name");
            File.WriteAllText(Path.Combine(work, "README.md"), "original\n");
            AssertGitOk(Git("add", ["README.md"], work), "git add initial");
            AssertGitOk(Git("commit", ["-m", "initial"], work), "git commit initial");
            var branch = "as/controlled-pr-branch-update-executor";
            AssertGitOk(Git("checkout", ["-B", branch], work), "git checkout branch");
            AssertGitOk(Git("push", ["origin", $"HEAD:refs/heads/{branch}"], work), "git push initial branch");
            var preHead = Git("rev-parse", ["HEAD"], work).Stdout.Trim();

            File.WriteAllText(Path.Combine(work, "README.md"), "updated by AS\n");
            var diff = Git("diff", ["--binary", "--", "README.md"], work).Stdout;
            var expectedDiffHash = PrBranchUpdateExecutor.ComputeDiffHash(diff);
            var package = CreateReadyPackage(preHead, branch, expectedDiffHash, "README.md");
            var packagePath = Path.Combine(root, "pr-update-package.json");
            var outPath = Path.Combine(root, "as-out");
            WriteJson(packagePath, package);

            var result = await RunCliAsync(
                "pr-branch-update", "execute",
                "--package", packagePath,
                "--workspace", work,
                "--out", outPath,
                "--remote", "origin",
                "--expected-pr", package.Target.PrNumber.ToString(),
                "--json").ConfigureAwait(false);

            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            Assert.AreEqual(string.Empty, Git("status", ["--porcelain=v1"], work).Stdout.Trim());
            var receipt = ReadJson<PrBranchUpdateExecutionReceipt>(Path.Combine(outPath, "pr-branch-update-execution-receipt.json"));
            Assert.IsNotNull(receipt);
            Assert.AreEqual(PrBranchUpdateExecutionVerdict.Executed, receipt!.ExecutionVerdict);
            Assert.IsTrue(receipt.Pushed);
            Assert.AreEqual(branch, receipt.PushBranch);
            Assert.AreEqual(receipt.CommitSha, Git("rev-parse", ["HEAD"], work).Stdout.Trim());
            Assert.AreEqual(receipt.CommitSha, Git("--git-dir", [remote, "rev-parse", $"refs/heads/{branch}"], root).Stdout.Trim());
            CollectionAssert.AreEquivalent(new[] { "README.md" }, receipt.ActualFilesChanged);
            Assert.IsFalse(result.Output.Contains("Ready for review", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(result.Output.Contains("Reviewers requested", StringComparison.OrdinalIgnoreCase));

            var status = await RunCliAsync("pr-branch-update", "status", "--receipt", Path.Combine(outPath, "pr-branch-update-execution-receipt.json"), "--json").ConfigureAwait(false);
            Assert.AreEqual(0, status.ExitCode, status.Output + status.Error);
            var rollback = await RunCliAsync("pr-branch-update", "rollback-plan", "--receipt", Path.Combine(outPath, "pr-branch-update-execution-receipt.json")).ConfigureAwait(false);
            Assert.AreEqual(0, rollback.ExitCode, rollback.Output + rollback.Error);
            StringAssert.Contains(rollback.Output, "read-only");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAS_Cli_BlocksForbiddenReviewMergeReleaseAndContinuationVerbs()
    {
        foreach (var forbidden in new[] { "approve", "ready", "request-reviewers", "resolve-comments", "merge", "auto-merge", "release", "deploy", "tag", "publish", "promote-memory", "continue" })
        {
            var result = await RunCliAsync("pr-branch-update", forbidden, "--receipt", "receipt.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAS_StaticBoundaryAndReceipt_ProveNoReviewMergeReleaseAuthority()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliPrBranchUpdate.cs"));
        Assert.IsFalse(cli.Contains("gh pr ready", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr review", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh release", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("--force", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AS_CONTROLLED_PR_BRANCH_UPDATE_EXECUTOR.md"));
        StringAssert.Contains(receipt, "AS1 AR package required.");
        StringAssert.Contains(receipt, "AS2 branch and head guard.");
        StringAssert.Contains(receipt, "AS3 file and diff guard.");
        StringAssert.Contains(receipt, "AS4 controlled commit.");
        StringAssert.Contains(receipt, "AS5 controlled push.");
        StringAssert.Contains(receipt, "AS6 execution receipt and bypass tests.");
        StringAssert.Contains(receipt, "PR branch update is not review transition.");
        StringAssert.Contains(receipt, "It does not mark ready.");
        StringAssert.Contains(receipt, "It does not merge.");
        StringAssert.Contains(receipt, "It does not continue workflow.");

        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanMarkReadyForReview(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanRequestReviewers(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanMerge(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanRelease(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanDeploy(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanContinueWorkflow(receipt));
    }

    private static ControlledPrUpdatePackage CreateReadyPackage(string expectedHead, string branch, string expectedDiffHash, params string[] expectedFiles)
    {
        var proposal = new FeedbackPatchProposal
        {
            PatchProposalId = "patch_proposal_as",
            SourcePackageId = "feedback_pkg_as",
            TargetPrNumber = 464,
            TargetHeadSha = expectedHead,
            BaseSha = new string('b', 40),
            ProposedFiles =
            [
                new FeedbackPatchFileProposal
                {
                    FilePath = expectedFiles[0],
                    ChangeKind = FeedbackPatchChangeKind.Modify,
                    RemediationCandidateIds = ["feedback_remed_as"],
                    Rationale = "AS test remediation.",
                    PatchHunks = [],
                    ExpectedTests = ["focused-as"],
                    AuthorityRisk = true
                }
            ],
            ProposedHunks = [],
            RemediationCandidateIds = ["feedback_remed_as"],
            ExpectedChangedFiles = expectedFiles,
            ExpectedValidationLanes = ["FocusedCurrentBlock", "ImpactedArea", "FastAuthorityInvariant", "Build", "DiffCheck"],
            RiskLevel = FeedbackRiskLevel.High,
            Verdict = FeedbackPatchProposalVerdict.ProposalCreated,
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T01:00:00Z")
        };
        var target = new PrUpdateTarget
        {
            Repository = "owner/repo",
            PrNumber = 464,
            PrUrl = "https://github.com/owner/repo/pull/464",
            PrState = "open",
            PrDraftState = true,
            TargetBranch = branch,
            ExpectedCurrentHeadSha = expectedHead,
            BaseBranch = "phase/close-feedback-loop",
            BaseSha = new string('b', 40),
            PackageCreatedAtUtc = DateTimeOffset.Parse("2026-06-20T01:01:00Z"),
            PackageCreatedBy = "tests"
        };
        var sourceApply = new PrUpdateSourceApplyEvidence
        {
            SourceApplyReceiptId = Guid.NewGuid().ToString("D"),
            SourceApplyReceiptHash = Hash("source-apply"),
            SourceApplyRequestHash = Hash("source-apply-request"),
            SourceApplyDryRunReceiptHash = Hash("source-apply-dry-run"),
            RollbackSupportReceiptHash = Hash("rollback-support"),
            ExpectedBranch = branch,
            ObservedBranch = branch,
            ApplySucceeded = true,
            MutationOccurred = true,
            PartialApplyOccurred = false,
            AppliedFiles = expectedFiles,
            AppliedAtUtc = DateTimeOffset.Parse("2026-06-20T01:02:00Z")
        };
        var postHead = new string('f', 40);
        return ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = proposal,
            Target = target,
            ValidationReceipts = [CreateValidationReceipt(postHead)],
            SourceApplyEvidence = sourceApply,
            SourceApplyPending = false,
            ExpectedPostUpdateHeadSha = postHead,
            ExpectedDiffHash = expectedDiffHash,
            ExpectedCommitMessage = "docs/test(feedback): apply AS branch update",
            ExpectedCommitBody = "Controlled AS branch update.",
            CommitAllowed = true,
            PushAllowed = true,
            TargetRemote = "origin"
        }).Package;
    }

    private static ControlledPrUpdatePackage CreateReadyPackage(string expectedDiffHash) =>
        CreateReadyPackage(new string('a', 40), "as/controlled-pr-branch-update-executor", expectedDiffHash, "IronDev.Core/Governance/ControlledPrBranchUpdateExecutor.cs");

    private static PrBranchUpdateObservedState GoodObserved(ControlledPrUpdatePackage package, string diffHash) => new()
    {
        RepositoryAvailable = true,
        Repository = package.Target.Repository,
        Branch = package.Target.TargetBranch,
        HeadSha = package.Target.ExpectedCurrentHeadSha,
        RemoteHeadSha = package.Target.ExpectedCurrentHeadSha,
        DirtyFiles = package.ExpectedState.ExpectedChangedFiles,
        StagedFiles = [],
        GeneratedRestoreArtifacts = [],
        DiffHash = diffHash,
        ForcePushRequired = false
    };

    private static PrBranchUpdateObservedState CleanObserved(ControlledPrUpdatePackage package, string headSha, string remoteHeadSha) => new()
    {
        RepositoryAvailable = true,
        Repository = package.Target.Repository,
        Branch = package.Target.TargetBranch,
        HeadSha = headSha,
        RemoteHeadSha = remoteHeadSha,
        DirtyFiles = [],
        StagedFiles = [],
        GeneratedRestoreArtifacts = [],
        DiffHash = PrBranchUpdateExecutor.ComputeDiffHash(string.Empty),
        ForcePushRequired = false
    };

    private static ValidationRunReceipt CreateValidationReceipt(string commitSha)
    {
        var lanes = new[]
        {
            Lane("focused-as"),
            Lane("impacted-governance-tests"),
            Lane("fast-authority-invariants"),
            Lane("build", ValidationCommandKind.Build),
            Lane("diff-check", ValidationCommandKind.DiffCheck)
        };
        return new ValidationRunReceipt
        {
            ValidationRunId = "validation_run_as_" + Guid.NewGuid().ToString("N")[..8],
            ValidationPlanId = "validation_plan_as",
            Branch = "as/controlled-pr-branch-update-executor",
            CommitSha = commitSha,
            ChangedFilesHash = Hash("changed-files"),
            StartedUtc = DateTimeOffset.Parse("2026-06-20T01:03:00Z"),
            FinishedUtc = DateTimeOffset.Parse("2026-06-20T01:04:00Z"),
            Verdict = ValidationRunVerdict.Passed,
            RequiredLanes = lanes,
            Results = lanes.Select(Result).ToArray(),
            SkippedLanes = [],
            SkippedLaneReasons = [],
            WorktreeCleanBefore = true,
            WorktreeCleanAfter = true,
            CachePolicy = new ValidationCachePolicy(),
            Boundary = ValidationRuntimeBoundary.Evidence
        };
    }

    private static ValidationLane Lane(string name, ValidationCommandKind kind = ValidationCommandKind.Test) => new()
    {
        Name = name,
        Reason = $"Required AS lane {name}.",
        Requirement = ValidationLaneRequirement.Required,
        Timeout = TimeSpan.FromMinutes(5),
        CommandKind = kind,
        Commands = [name],
        SafeToParallelize = true,
        ParallelismGroup = "as",
        CacheCategory = kind == ValidationCommandKind.Build ? "build" : kind == ValidationCommandKind.DiffCheck ? "diff" : "test"
    };

    private static ValidationProcessResult Result(ValidationLane lane) => new()
    {
        LaneName = lane.Name,
        Command = lane.Name,
        Arguments = [],
        WorkingDirectory = "repo",
        StartedUtc = DateTimeOffset.Parse("2026-06-20T01:03:00Z"),
        FinishedUtc = DateTimeOffset.Parse("2026-06-20T01:03:10Z"),
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

    private static void AssertBoundary(PrBranchUpdateBoundary boundary)
    {
        Assert.IsTrue(boundary.CanMutatePullRequestBranch);
        Assert.IsTrue(boundary.CanStage);
        Assert.IsTrue(boundary.CanCommit);
        Assert.IsTrue(boundary.CanPush);
        Assert.IsFalse(boundary.CanForcePush);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanResolveReviewThreads);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanTag);
        Assert.IsFalse(boundary.CanPublish);
        Assert.IsFalse(boundary.CanPromoteMemory);
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

    private static ProcessResult Git(string command, string[] args, string workingDirectory) =>
        RunProcess("git", [command, .. args], workingDirectory);

    private static ProcessResult RunProcess(string fileName, string[] args, string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static void AssertGitOk(ProcessResult result, string label) =>
        Assert.AreEqual(0, result.ExitCode, $"{label}\nSTDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}");

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-as-" + Guid.NewGuid().ToString("N"));
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
        "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private sealed class FakePrBranchUpdateClient : IPrBranchUpdateCommandClient
    {
        public PrBranchUpdateObservedState? Observed { get; init; }
        public PrBranchUpdateObservedState[] Observations { get; init; } = [];
        public int StageCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public int PushCalls { get; private set; }
        private int _observeIndex;

        public Task<PrBranchUpdateObservedState> ObserveAsync(PrBranchUpdateExecutionRequest request, CancellationToken cancellationToken)
        {
            if (_observeIndex < Observations.Length)
                return Task.FromResult(Observations[_observeIndex++]);

            return Task.FromResult(Observed ?? Observations.LastOrDefault() ?? new PrBranchUpdateObservedState
            {
                RepositoryAvailable = false,
                Repository = string.Empty,
                Branch = string.Empty,
                HeadSha = string.Empty,
                RemoteHeadSha = string.Empty,
                DiffHash = string.Empty
            });
        }

        public Task<PrBranchUpdateCommandResult> StageAsync(ControlledPrUpdatePackage package, string[] expectedFiles, CancellationToken cancellationToken)
        {
            StageCalls++;
            return Task.FromResult(new PrBranchUpdateCommandResult { Succeeded = true, StagedFiles = expectedFiles });
        }

        public Task<PrBranchUpdateCommandResult> CommitAsync(ControlledPrUpdatePackage package, CancellationToken cancellationToken)
        {
            CommitCalls++;
            return Task.FromResult(new PrBranchUpdateCommandResult
            {
                Succeeded = true,
                CommitSha = "commit-as",
                PostHeadSha = "commit-as",
                ChangedFiles = package.ExpectedState.ExpectedChangedFiles
            });
        }

        public Task<PrBranchUpdateCommandResult> PushAsync(ControlledPrUpdatePackage package, string remote, string branch, CancellationToken cancellationToken)
        {
            PushCalls++;
            return Task.FromResult(new PrBranchUpdateCommandResult { Succeeded = true, RemoteHeadSha = "commit-as" });
        }
    }
}
