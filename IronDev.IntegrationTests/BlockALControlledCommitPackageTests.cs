using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockALControlledCommitPackageTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockAL_Cli_WritesCommitPackageArtifactsWithoutStagingCommittingPushingOrCreatingPr()
    {
        var root = CreateTempRoot();
        try
        {
            var sourceRepo = Path.Combine(root, "repo");
            var runPath = Path.Combine(root, "run-al");
            var baseHead = await CreateSourceRepoAsync(sourceRepo).ConfigureAwait(false);
            WriteCompleteRunArtifacts(runPath, baseHead, ["README.md"]);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "README.md"), "changed by controlled commit package test");
            var headBefore = await GitValueAsync(sourceRepo, "rev-parse", "HEAD").ConfigureAwait(false);

            foreach (var args in new[]
                     {
                         new[] { "commit-package", "request", "--run", runPath, "--source-repo", sourceRepo, "--json" },
                         new[] { "commit-package", "manifest", "--run", runPath, "--source-repo", sourceRepo, "--json" },
                         new[] { "commit-package", "evidence", "--run", runPath, "--json" },
                         new[] { "commit-package", "message", "--run", runPath, "--json" },
                         new[] { "commit-package", "review", "--run", runPath, "--json" },
                         new[] { "commit-package", "status", "--run", runPath, "--json" }
                     })
            {
                var result = await RunCliAsync(args).ConfigureAwait(false);
                Assert.AreEqual(0, result.ExitCode, string.Join(" ", args) + Environment.NewLine + result.Output + result.Error);
            }

            foreach (var artifact in new[]
                     {
                         "commit-package-request.json",
                         "commit-package-request.md",
                         "commit-file-manifest.json",
                         "commit-file-manifest.md",
                         "commit-staging-plan.json",
                         "commit-staging-plan.md",
                         "commit-evidence-bundle.json",
                         "commit-evidence-bundle.md",
                         "commit-message-proposal.json",
                         "commit-message-proposal.md",
                         "commit-readiness-review.json",
                         "commit-readiness-review.md",
                         "commit-package-risk-report.json",
                         "commit-package-risk-report.md",
                         "commit-package-boundary-report.json",
                         "commit-package-boundary-report.md",
                         "commit-package-bypass-report.json",
                         "commit-package-bypass-report.md",
                         "governance-events.jsonl"
                     })
            {
                Assert.IsTrue(File.Exists(Path.Combine(runPath, artifact)), artifact);
            }

            var request = ReadJson<CommitPackageRequest>(Path.Combine(runPath, "commit-package-request.json"));
            Assert.AreEqual(baseHead, request!.BaseCommit);
            Assert.AreEqual(headBefore, request.CurrentHeadCommit);
            Assert.AreEqual("source_apply_receipt_al", request.SourceApplyReceiptId);
            AssertBoundary(request.Boundary);

            var manifest = ReadJson<CommitFileManifest>(Path.Combine(runPath, "commit-file-manifest.json"));
            CollectionAssert.Contains(manifest!.IncludedFiles, "README.md");
            Assert.AreEqual(0, manifest.UnexpectedFiles.Length);
            Assert.IsFalse(string.IsNullOrWhiteSpace(manifest.FileHashes.Single(item => item.Path == "README.md").ContentHash));
            AssertBoundary(manifest.Boundary);

            var stagingPlan = ReadJson<CommitStagingPlan>(Path.Combine(runPath, "commit-staging-plan.json"));
            Assert.IsTrue(stagingPlan!.StagingCommandsForHuman.Any(item => item.Contains("git add -- README.md", StringComparison.OrdinalIgnoreCase)));
            AssertBoundary(stagingPlan.Boundary);

            var bundle = ReadJson<CommitEvidenceBundle>(Path.Combine(runPath, "commit-evidence-bundle.json"));
            Assert.AreEqual(0, bundle!.MissingEvidence.Length, string.Join(",", bundle.MissingEvidence));
            AssertBoundary(bundle.Boundary);

            var message = ReadJson<CommitMessageProposal>(Path.Combine(runPath, "commit-message-proposal.json"));
            Assert.IsTrue(message!.HumanEditRequired);
            StringAssert.Contains(message.ProposedBody, "Manual review remains required");
            AssertBoundary(message.Boundary);

            var review = ReadJson<CommitReadinessReview>(Path.Combine(runPath, "commit-readiness-review.json"));
            Assert.AreEqual(CommitReadinessDecision.ReadyForHumanCommitReview, review!.Decision);
            Assert.IsFalse(review.CanStageFiles);
            Assert.IsFalse(review.CanCreateCommit);
            Assert.IsFalse(review.CanPush);
            AssertBoundary(review.Boundary);

            var bypass = ReadJson<CommitPackageBypassReport>(Path.Combine(runPath, "commit-package-bypass-report.json"));
            Assert.IsFalse(bypass!.FilesStaged);
            Assert.IsFalse(bypass.CommitCreated);
            Assert.IsFalse(bypass.PushPerformed);
            Assert.IsFalse(bypass.PullRequestCreated);
            Assert.IsFalse(bypass.MergePerformed);
            Assert.IsFalse(bypass.ReleasePerformed);
            Assert.IsFalse(bypass.DeployPerformed);
            Assert.IsFalse(bypass.WorkflowContinued);
            AssertBoundary(bypass.Boundary);

            var staged = await GitValueAsync(sourceRepo, "diff", "--cached", "--name-only").ConfigureAwait(false);
            var headAfter = await GitValueAsync(sourceRepo, "rev-parse", "HEAD").ConfigureAwait(false);
            Assert.AreEqual(string.Empty, staged);
            Assert.AreEqual(headBefore, headAfter);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAL_ManifestAndReview_FailClosedForUnexpectedChangedFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var sourceRepo = Path.Combine(root, "repo");
            var runPath = Path.Combine(root, "run-al-unexpected");
            var baseHead = await CreateSourceRepoAsync(sourceRepo).ConfigureAwait(false);
            WriteCompleteRunArtifacts(runPath, baseHead, ["README.md"]);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "README.md"), "expected change");
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "unexpected.txt"), "unexpected change");

            Assert.AreEqual(0, (await RunCliAsync("commit-package", "request", "--run", runPath, "--source-repo", sourceRepo, "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(0, (await RunCliAsync("commit-package", "manifest", "--run", runPath, "--source-repo", sourceRepo, "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(0, (await RunCliAsync("commit-package", "evidence", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(0, (await RunCliAsync("commit-package", "message", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(0, (await RunCliAsync("commit-package", "review", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);

            var manifest = ReadJson<CommitFileManifest>(Path.Combine(runPath, "commit-file-manifest.json"));
            CollectionAssert.Contains(manifest!.UnexpectedFiles, "unexpected.txt");
            Assert.IsFalse(manifest.IncludedFiles.Contains("unexpected.txt", StringComparer.OrdinalIgnoreCase));

            var review = ReadJson<CommitReadinessReview>(Path.Combine(runPath, "commit-readiness-review.json"));
            Assert.AreEqual(CommitReadinessDecision.NeedsMoreEvidence, review!.Decision);
            CollectionAssert.Contains(review.Findings, "UnexpectedFilesRequireReview");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockAL_EvidenceBundleAndReadinessReview_BlockMissingEvidenceAndUnsafeMaterial()
    {
        var request = CommitPackageRequestWriter.Create(new CommitPackageRequestInput
        {
            RunId = "run-al-core",
            ProjectId = "project-al",
            SourceRepoIdentity = "repo",
            SourceRepoPath = "repo",
            BaseCommit = "base",
            CurrentHeadCommit = "head",
            SourceApplyReceiptId = "receipt",
            PatchHash = "patch",
            PostApplyDiffHash = "diff",
            RequestedBy = "tester",
            Reason = "Prepare controlled commit package"
        });
        var manifest = CommitFileManifestBuilder.Build(new CommitFileManifestInput
        {
            RunId = request.RunId,
            SourceRepoIdentity = request.SourceRepoIdentity,
            SourceRepoPath = request.SourceRepoPath,
            BaseCommit = request.BaseCommit,
            CurrentHeadCommit = request.CurrentHeadCommit,
            KnownChangedFiles = ["README.md"],
            ActualChangedFiles = ["README.md"],
            FileHashes = [new CommitFileHash { Path = "README.md", ContentHash = new string('a', 64) }],
            DiffHash = "diff"
        });
        var staging = CommitFileManifestBuilder.BuildStagingPlan(manifest);
        var missingBundle = CommitEvidenceBundleBuilder.Build(new CommitEvidenceBundleInput
        {
            RunId = request.RunId,
            CommitPackageRequestId = request.CommitPackageRequestId,
            CommitFileManifestId = manifest.CommitFileManifestId,
            AvailableArtifactNames = []
        });
        var missingMessage = CommitMessageProposalBuilder.Build(missingBundle, manifest, request.Reason);
        var needsEvidence = CommitReadinessReviewer.Review(request, manifest, staging, missingBundle, missingMessage, []);

        Assert.AreEqual(CommitReadinessDecision.NeedsMoreEvidence, needsEvidence.Decision);
        Assert.IsTrue(missingBundle.MissingEvidence.Length > 0);
        Assert.IsTrue(needsEvidence.Findings.Any(item => item.StartsWith("MissingEvidence:", StringComparison.OrdinalIgnoreCase)));

        var unsafeReview = CommitReadinessReviewer.Review(request, manifest, staging, missingBundle, missingMessage, ["unsafe material"]);
        Assert.AreEqual(CommitReadinessDecision.Blocked, unsafeReview.Decision);
        CollectionAssert.Contains(unsafeReview.Findings, "UnsafeMaterialFindingsBlockCommitPackage");
    }

    [TestMethod]
    public void BlockAL_MessageProposal_SanitizesAuthorityLanguageAndRequiresHumanEdit()
    {
        var manifest = CommitFileManifestBuilder.Build(new CommitFileManifestInput
        {
            RunId = "run-al-message",
            SourceRepoIdentity = "repo",
            SourceRepoPath = "repo",
            BaseCommit = "base",
            CurrentHeadCommit = "head",
            KnownChangedFiles = ["README.md"],
            ActualChangedFiles = ["README.md"],
            FileHashes = [new CommitFileHash { Path = "README.md", ContentHash = new string('a', 64) }],
            DiffHash = "diff"
        });
        var bundle = CommitEvidenceBundleBuilder.Build(new CommitEvidenceBundleInput
        {
            RunId = "run-al-message",
            CommitPackageRequestId = "req",
            CommitFileManifestId = manifest.CommitFileManifestId,
            AvailableArtifactNames = ["patch.diff", "changed-files.txt", "test-results.txt", "build-results.txt", "artifact-consistency-report.json", "unsafe-material-report.json", "source-post-apply-state.json", "governance-events.jsonl"]
        });

        var message = CommitMessageProposalBuilder.Build(bundle, manifest, "approved and ready to merge deploy now");

        Assert.IsTrue(message.HumanEditRequired);
        Assert.IsFalse(CommitMessageProposalBuilder.ContainsForbiddenPhrase(message.ProposedTitle));
        Assert.IsFalse(CommitMessageProposalBuilder.ContainsForbiddenPhrase(message.ProposedBody));
        AssertBoundary(message.Boundary);
    }

    [TestMethod]
    public async Task BlockAL_Cli_BlocksAuthorityShapedSubcommands()
    {
        foreach (var forbidden in new[] { "stage", "commit", "push", "pr", "merge", "release", "deploy", "continue", "approve", "execute", "apply", "rollback", "promote-memory" })
        {
            var result = await RunCliAsync("commit-package", forbidden, "--run", "run-al").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAL_StaticBoundaryAndReceipt_ProveCommitPackageDoesNotAddAuthority()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliCommitPackage.cs"));
        foreach (var forbiddenCall in new[]
                 {
                     "RunGitAsync(sourceRepoPath, [\"add\"",
                     "RunGitAsync(sourceRepoPath, [\"commit\"",
                     "RunGitAsync(sourceRepoPath, [\"push\"",
                     "RunGitAsync(sourceRepoPath, [\"merge\"",
                     "RunProcessAsync(\"gh\"",
                     "RunProcessAsync(\"dotnet\""
                 })
        {
            Assert.IsFalse(cli.Contains(forbiddenCall, StringComparison.OrdinalIgnoreCase), forbiddenCall);
        }

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "PR309_314_CONTROLLED_COMMIT_PACKAGE.md"));
        StringAssert.Contains(receipt, "AL1 creates commit package request.");
        StringAssert.Contains(receipt, "AL2 builds commit file manifest.");
        StringAssert.Contains(receipt, "AL3 collects commit evidence bundle.");
        StringAssert.Contains(receipt, "AL4 creates commit message proposal.");
        StringAssert.Contains(receipt, "AL5 performs Killjoy commit readiness review.");
        StringAssert.Contains(receipt, "AL6 proves commit evidence cannot bypass authority.");
        StringAssert.Contains(receipt, "It does not stage files.");
        StringAssert.Contains(receipt, "It does not create commits.");
        StringAssert.Contains(receipt, "It does not push.");
        StringAssert.Contains(receipt, "It does not create pull requests.");
        StringAssert.Contains(receipt, "It does not merge.");
        StringAssert.Contains(receipt, "It does not release.");
        StringAssert.Contains(receipt, "It does not deploy.");
        StringAssert.Contains(receipt, "It does not continue workflow.");
    }

    private static void WriteCompleteRunArtifacts(string runPath, string baseCommit, string[] changedFiles)
    {
        Directory.CreateDirectory(runPath);
        File.WriteAllText(Path.Combine(runPath, "run.json"), JsonSerializer.Serialize(new
        {
            projectId = "project-al",
            baseCommit,
            taskSummary = "docs/test(governance): add AL controlled commit package"
        }, JsonOptions));
        File.WriteAllText(Path.Combine(runPath, "patch.diff"), "diff --git a/README.md b/README.md");
        File.WriteAllLines(Path.Combine(runPath, "changed-files.txt"), changedFiles);
        File.WriteAllText(Path.Combine(runPath, "source-apply-receipt.json"), JsonSerializer.Serialize(new { sourceApplyReceiptId = "source_apply_receipt_al" }, JsonOptions));
        File.WriteAllText(Path.Combine(runPath, "source-post-apply-state.json"), "{}");
        File.WriteAllText(Path.Combine(runPath, "test-results.txt"), "Focused AL test passed.");
        File.WriteAllText(Path.Combine(runPath, "build-results.txt"), "Build passed.");
        File.WriteAllText(Path.Combine(runPath, "review-summary.md"), "Human review remains required.");
        File.WriteAllText(Path.Combine(runPath, "known-risks.md"), "Commit package evidence can drift if source changes after review.");
        File.WriteAllText(Path.Combine(runPath, "manual-apply-instructions.md"), "Manual review remains required.");
        File.WriteAllText(Path.Combine(runPath, "artifact-consistency-report.json"), "{}");
        File.WriteAllText(Path.Combine(runPath, "unsafe-material-report.json"), "{\"findings\":[]}");
        File.WriteAllText(Path.Combine(runPath, "planner-context.json"), "{}");
        File.WriteAllText(Path.Combine(runPath, "memory-informed-plan.json"), "{}");
        File.WriteAllText(Path.Combine(runPath, "killjoy-plan-review.json"), "{}");
    }

    private static async Task<string> CreateSourceRepoAsync(string sourceRepo)
    {
        Directory.CreateDirectory(sourceRepo);
        await RunGitAsync(sourceRepo, "init").ConfigureAwait(false);
        await RunGitAsync(sourceRepo, "config", "user.email", "irondev-tests@example.invalid").ConfigureAwait(false);
        await RunGitAsync(sourceRepo, "config", "user.name", "IronDev Tests").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(sourceRepo, "README.md"), "initial");
        await RunGitAsync(sourceRepo, "add", "README.md").ConfigureAwait(false);
        await RunGitAsync(sourceRepo, "commit", "-m", "initial").ConfigureAwait(false);
        return await GitValueAsync(sourceRepo, "rev-parse", "HEAD").ConfigureAwait(false);
    }

    private static void AssertBoundary(CommitPackageBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanApproveCommit);
        Assert.IsFalse(boundary.CanStageFiles);
        Assert.IsFalse(boundary.CanCreateCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
    }

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static async Task<string> GitValueAsync(string workingDirectory, params string[] args)
    {
        var result = await RunGitAsync(workingDirectory, args).ConfigureAwait(false);
        return result.Output.Trim();
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunGitAsync(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo);
        Assert.IsNotNull(process);
        var output = await process!.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        Assert.AreEqual(0, process.ExitCode, string.Join(" ", args) + Environment.NewLine + output + error);
        return (process.ExitCode, output, error);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-al-" + Guid.NewGuid().ToString("N"));
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
            // Best-effort test cleanup for Windows file handles.
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
}
