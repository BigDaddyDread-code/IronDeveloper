using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAPControlledFeedbackRemediationPackageTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockAP_Packager_SeparatesFeedbackAndGroupsDuplicatesWithoutAuthority()
    {
        var head = new string('a', 40);
        var oldHead = new string('b', 40);
        var artifacts = FeedbackRemediationPackager.Build(new FeedbackRemediationPackageInput
        {
            RunId = "run-ap",
            RepositoryFullName = "owner/repo",
            PullRequestNumber = 462,
            CurrentHeadSha = head,
            PullRequestUrl = "https://github.com/owner/repo/pull/462",
            EvidenceRefs = ["review-feedback-snapshot.json", "ci-observation-snapshot.json"],
            Items =
            [
                Item(FeedbackSourceKind.GitHubReviewComment, "inline-1", head, "IronDev.Core/Governance/GovernedActionKernel.cs", 44, "Please change the governed gate binding."),
                Item(FeedbackSourceKind.GitHubReviewComment, "inline-2", head, "IronDev.Core/Governance/GovernedActionKernel.cs", 44, "Please change the governed gate binding."),
                Item(FeedbackSourceKind.GitHubReviewComment, "old-inline", oldHead, "IronDev.Core/Governance/GovernedActionKernel.cs", 45, "Please change this old review finding."),
                Item(FeedbackSourceKind.GitHubCheckRun, "nuget", head, null, null, "NuGet.Config access denied while restoring."),
                Item(FeedbackSourceKind.LocalValidationReceipt, "timeout", head, null, null, "Process timeout without product failure."),
                Item(FeedbackSourceKind.GitHubIssueComment, "ready", head, null, null, "Please mark ready and merge after this."),
                Item(FeedbackSourceKind.GitHubIssueComment, "resolved", head, null, null, "Resolved requested change.", isResolved: true),
                Item(FeedbackSourceKind.GitHubIssueComment, "thanks", head, null, null, "Looks good, thanks."),
                Item(FeedbackSourceKind.GitHubReviewComment, "question", head, "README.md", 5, "Should this wording move elsewhere?")
            ]
        });

        var package = artifacts.Package;
        Assert.AreEqual(9, package.FeedbackItems.Length);
        Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.ActionableGovernanceChange));
        Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.DuplicateFeedback));
        Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.StaleFeedback));
        Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.EnvironmentFailure));
        Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.ValidationHarnessFailure));
        Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.SecurityOrAuthorityRisk));
        Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.ResolvedFeedback));
        Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.NonActionableComment));
        Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.NeedsHumanDecision));

        var duplicateCandidate = package.RemediationCandidates.Single(item => item.FeedbackItemIds.Length == 2);
        Assert.AreEqual(FeedbackDisposition.Remediate, duplicateCandidate.Disposition);
        Assert.IsTrue(duplicateCandidate.LikelyFiles.Contains("IronDev.Core/Governance/GovernedActionKernel.cs"));
        Assert.IsTrue(duplicateCandidate.SuggestedValidationLanes.Contains("FastAuthorityInvariant"));

        Assert.IsTrue(package.RemediationCandidates.Any(item => item.Disposition == FeedbackDisposition.Stale));
        Assert.IsTrue(package.RemediationCandidates.Any(item => item.Disposition == FeedbackDisposition.Blocked && item.BlockedReason!.Contains("Environment failure", StringComparison.Ordinal)));
        Assert.IsTrue(package.RemediationCandidates.Any(item => item.AuthorityRisk && item.RequiresHumanDecision));
        Assert.IsFalse(package.KnownRisks.Any(item => item.Contains("patch diff", StringComparison.OrdinalIgnoreCase)));
        AssertBoundary(package.Boundary);
        AssertBoundary(artifacts.Receipt.Boundary);
        Assert.AreEqual(FeedbackRemediationPackageVerdict.Blocked, artifacts.Receipt.Verdict);
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanProposePatch(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanUpdatePullRequest(package));
    }

    [TestMethod]
    public async Task BlockAP_Cli_PackagesValidationReceiptAndReadsStatusRecords()
    {
        var root = CreateTempRoot();
        try
        {
            var receiptPath = Path.Combine(root, "validation-receipt.json");
            WriteJson(receiptPath, CreateValidationReceipt());
            var outPath = Path.Combine(root, "feedback");

            var packageResult = await RunCliAsync("feedback", "package", "--from-receipt", receiptPath, "--out", outPath, "--json").ConfigureAwait(false);
            Assert.AreEqual(0, packageResult.ExitCode, packageResult.Output + packageResult.Error);
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "feedback-remediation-package.json")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "feedback-remediation-package-receipt.json")));
            Assert.IsTrue(File.Exists(Path.Combine(outPath, "feedback-remediation-summary.md")));

            var package = ReadJson<FeedbackRemediationPackage>(Path.Combine(outPath, "feedback-remediation-package.json"));
            Assert.IsNotNull(package);
            Assert.IsTrue(package!.FeedbackItems.Any(item => item.Classification == FeedbackClassification.EnvironmentFailure));
            Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.ValidationHarnessFailure));
            Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.ActionableCodeChange));
            Assert.IsTrue(package.FeedbackItems.Any(item => item.Classification == FeedbackClassification.ActionableTestChange));
            AssertBoundary(package.Boundary);

            var status = await RunCliAsync("feedback", "package", "--status", "--package", Path.Combine(outPath, "feedback-remediation-package.json"), "--json").ConfigureAwait(false);
            Assert.AreEqual(0, status.ExitCode, status.Output + status.Error);
            var records = await RunCliAsync("feedback", "package", "--records", "--package", Path.Combine(outPath, "feedback-remediation-package.json")).ConfigureAwait(false);
            Assert.AreEqual(0, records.ExitCode, records.Output + records.Error);
            StringAssert.Contains(records.Output, "Blocked");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAP_Cli_BlocksAuthorityShapedSubcommands()
    {
        foreach (var forbidden in new[] { "approve", "apply", "fix", "commit", "push", "ready", "request-reviewers", "merge", "release", "deploy", "continue" })
        {
            var result = await RunCliAsync("feedback", forbidden, "--run", "run-ap").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAP_StaticBoundaryAndReceipt_ProvePackageCannotMutate()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliFeedback.cs"));
        Assert.IsFalse(cli.Contains("\"git\", [\"add\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\", [\"commit\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\", [\"push\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"pr\", \"ready\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"pr\", \"review\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"pr\", \"merge\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"pr\", \"comment\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"run\", \"rerun\"", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AP_CONTROLLED_FEEDBACK_REMEDIATION_PACKAGE.md"));
        StringAssert.Contains(receipt, "AP1 feedback source identity.");
        StringAssert.Contains(receipt, "AP2 feedback classification.");
        StringAssert.Contains(receipt, "AP3 remediation candidates.");
        StringAssert.Contains(receipt, "AP4 staleness and duplicate handling.");
        StringAssert.Contains(receipt, "AP5 evidence-only package receipt.");
        StringAssert.Contains(receipt, "AP6 authority bypass tests.");
        StringAssert.Contains(receipt, "Feedback evidence is not accepted remediation.");
        StringAssert.Contains(receipt, "It does not propose patches.");
        StringAssert.Contains(receipt, "It does not update PR branches.");
        StringAssert.Contains(receipt, "It does not continue workflow.");
    }

    private static FeedbackItemInput Item(
        FeedbackSourceKind sourceKind,
        string sourceId,
        string commitSha,
        string? filePath,
        int? line,
        string rawExcerpt,
        bool isResolved = false) => new()
    {
        SourceKind = sourceKind,
        SourceId = sourceId,
        SourceUrl = $"https://example.test/{sourceId}",
        Author = "reviewer",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-19T00:00:00Z"),
        UpdatedAtUtc = DateTimeOffset.Parse("2026-06-19T00:01:00Z"),
        CommitSha = commitSha,
        FilePath = filePath,
        Line = line,
        ThreadId = filePath is null ? null : "thread-1",
        RawExcerpt = rawExcerpt,
        IsResolved = isResolved
    };

    private static ValidationRunReceipt CreateValidationReceipt()
    {
        var now = DateTimeOffset.Parse("2026-06-19T00:00:00Z");
        return new ValidationRunReceipt
        {
            ValidationRunId = "validation_run_ap",
            ValidationPlanId = "validation_plan_ap",
            Branch = "ap/controlled-feedback-remediation-package",
            CommitSha = new string('c', 40),
            ChangedFilesHash = "changed",
            StartedUtc = now,
            FinishedUtc = now.AddSeconds(5),
            Verdict = ValidationRunVerdict.Failed,
            FailureClassifications =
            [
                ValidationFailureKind.EnvironmentAccessDenied,
                ValidationFailureKind.Timeout,
                ValidationFailureKind.BuildFailed,
                ValidationFailureKind.TestFailed
            ],
            DirtyChangedFiles =
            [
                new ValidationChangedFileClassification
                {
                    Path = "IronDev.Core/obj/project.assets.json",
                    Kind = ValidationChangedFileKind.GeneratedRestoreArtifact,
                    Reason = "Restore generated artifact was dirty after validation."
                }
            ],
            WorktreeCleanBefore = true,
            WorktreeCleanAfter = false
        };
    }

    private static void AssertBoundary(FeedbackAuthorityBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanProposePatch);
        Assert.IsFalse(boundary.CanApplySource);
        Assert.IsFalse(boundary.CanUpdatePullRequest);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanReplyToReviewComments);
        Assert.IsFalse(boundary.CanResolveReviewThreads);
        Assert.IsFalse(boundary.CanRerunCi);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanTag);
        Assert.IsFalse(boundary.CanPublish);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
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
        var path = Path.Combine(Path.GetTempPath(), "irondev-ap-" + Guid.NewGuid().ToString("N"));
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
}
