using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockANCIAndReviewFeedbackLoopTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockAN_Cli_RequestAndStatus_WriteEvidenceOnlyArtifacts()
    {
        var root = CreateTempRoot();
        try
        {
            var runPath = Path.Combine(root, "run-an");
            var receipt = WritePullRequestArtifacts(runPath);
            var result = await RunCliAsync("feedback", "request", "--run", runPath, "--repo", receipt.RepositoryFullName, "--pr", receipt.PullRequestNumber.ToString(), "--expected-head", receipt.ExpectedHeadSha, "--json").ConfigureAwait(false);
            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            Assert.AreEqual(0, (await RunCliAsync("feedback", "status", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);

            foreach (var artifact in new[]
                     {
                         "feedback-loop-request.json",
                         "feedback-loop-request.md",
                         "feedback-loop-bypass-report.json",
                         "feedback-loop-bypass-report.md",
                         "governance-events.jsonl"
                     })
            {
                Assert.IsTrue(File.Exists(Path.Combine(runPath, artifact)), artifact);
            }

            var request = ReadJson<FeedbackLoopRequest>(Path.Combine(runPath, "feedback-loop-request.json"));
            Assert.AreEqual(receipt.RepositoryFullName, request!.RepositoryFullName);
            Assert.AreEqual(receipt.PullRequestNumber, request.PullRequestNumber);
            Assert.AreEqual(receipt.ExpectedHeadSha, request.ExpectedHeadSha);
            Assert.AreEqual(receipt.PullRequestCreationReceiptId, request.PullRequestCreationReceiptId);
            AssertBoundary(request.Boundary);
            Assert.IsFalse(File.Exists(Path.Combine(runPath, "feedback-readiness-report.json")));

            var missingRun = Path.Combine(root, "missing-receipt");
            Directory.CreateDirectory(missingRun);
            var missing = await RunCliAsync("feedback", "request", "--run", missingRun, "--repo", "owner/repo", "--pr", "1", "--expected-head", receipt.ExpectedHeadSha).ConfigureAwait(false);
            Assert.AreEqual(1, missing.ExitCode);
            StringAssert.Contains(missing.Error, "pull-request-created-receipt.json is required");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockAN_Cli_ReviewObservation_ReadsInlineReviewCommentsWithoutReviewAuthority()
    {
        var request = CreateRequest();
        var inlineComments = IronDevCliFeedbackReviewParser.ParseInlineReviewComments($$"""
            [
              {
                "user": { "login": "inline-reviewer" },
                "body": "Why does this branch skip inline feedback?",
                "commit_id": "{{request.ExpectedHeadSha}}",
                "html_url": "https://github.com/owner/repo/pull/458#discussion_r1",
                "path": "tools/IronDev.Cli/CliFeedback.cs",
                "line": 387,
                "created_at": "2026-06-19T00:02:00Z"
              },
              {
                "user": { "login": "inline-reviewer" },
                "body": "Please change this parser to include inline review comments.",
                "commit_id": "{{request.ExpectedHeadSha}}",
                "html_url": "https://github.com/owner/repo/pull/458#discussion_r2",
                "path": "tools/IronDev.Cli/CliFeedback.cs",
                "original_line": 388,
                "created_at": "2026-06-19T00:03:00Z"
              }
            ]
            """, request.ExpectedHeadSha);

        var snapshot = ReviewFeedbackSnapshotBuilder.Build(
            request,
            request.ExpectedHeadSha,
            [],
            inlineComments,
            [new ReviewCommentSummary { Author = "lead", BodySummary = "Top-level note.", HeadSha = request.ExpectedHeadSha }]);
        Assert.AreEqual(2, snapshot.InlineComments.Length);
        Assert.AreEqual(1, snapshot.TopLevelComments.Length);
        Assert.IsTrue(snapshot.InlineComments.Any(item => item.BodySummary.Contains("Why does this branch skip inline feedback?", StringComparison.Ordinal)));
        Assert.IsTrue(snapshot.RequestedChanges.Any(item => item.Message.Contains("Please change this parser", StringComparison.Ordinal)));
        Assert.IsTrue(snapshot.RequestedChanges.Any(item => item.AffectedFiles.Contains("tools/IronDev.Cli/CliFeedback.cs")));
        AssertBoundary(snapshot.Boundary);

        var report = FeedbackClassifier.Classify(request, null, snapshot);
        Assert.IsTrue(report.Findings.Any(item => item.Category == FeedbackCategory.ReviewQuestion && item.Actionability == FeedbackActionability.RequiresHumanAnswer && item.Message.Contains("Why does this branch", StringComparison.Ordinal)));
        Assert.IsTrue(report.Findings.Any(item => item.Category == FeedbackCategory.ReviewRequestedChange && item.Actionability == FeedbackActionability.RequiresGovernedPatchRun && item.Message.Contains("Please change this parser", StringComparison.Ordinal)));
        AssertBoundary(report.Boundary);
    }

    [TestMethod]
    public void BlockAN_CiObservation_ClassifiesCheckStatesWithoutCiAuthority()
    {
        var request = CreateRequest();
        var passed = CiObservationBuilder.Build(request, request.ExpectedHeadSha, [new CiCheckRunSummary { Name = "build", Status = "completed", Conclusion = "success", HeadSha = request.ExpectedHeadSha }], []);
        Assert.AreEqual(FeedbackCiState.Passed, passed.OverallCiState);
        AssertBoundary(passed.Boundary);

        var failed = CiObservationBuilder.Build(request, request.ExpectedHeadSha, [new CiCheckRunSummary { Name = "test suite", Status = "completed", Conclusion = "failure", HeadSha = request.ExpectedHeadSha }], []);
        Assert.AreEqual(FeedbackCiState.Failed, failed.OverallCiState);
        Assert.AreEqual(1, failed.Failures.Length);
        AssertBoundary(failed.Boundary);

        var pending = CiObservationBuilder.Build(request, request.ExpectedHeadSha, [new CiCheckRunSummary { Name = "build", Status = "in_progress", HeadSha = request.ExpectedHeadSha }], []);
        Assert.AreEqual(FeedbackCiState.Pending, pending.OverallCiState);

        var missing = CiObservationBuilder.Build(request, request.ExpectedHeadSha, [], []);
        Assert.AreEqual(FeedbackCiState.Missing, missing.OverallCiState);

        var stale = CiObservationBuilder.Build(request, new string('d', 40), [new CiCheckRunSummary { Name = "build", Status = "completed", Conclusion = "success", HeadSha = new string('d', 40) }], []);
        Assert.AreEqual(FeedbackCiState.Stale, stale.OverallCiState);
        Assert.IsTrue(stale.StaleObservations.Length > 0);
        Assert.IsFalse(stale.Boundary.CanRerunCi);
        Assert.IsFalse(stale.Boundary.CanPush);
        Assert.IsFalse(stale.Boundary.CanMarkReadyForReview);
    }

    [TestMethod]
    public void BlockAN_ReviewFeedbackObservation_CapturesFeedbackWithoutParticipation()
    {
        var request = CreateRequest();
        var snapshot = ReviewFeedbackSnapshotBuilder.Build(
            request,
            request.ExpectedHeadSha,
            [
                new ReviewSubmissionSummary { Author = "reviewer", State = ReviewFeedbackState.RequestedChanges, BodySummary = "Please change the gate binding.", HeadSha = request.ExpectedHeadSha },
                new ReviewSubmissionSummary { Author = "lead", State = ReviewFeedbackState.ApprovedButNonAuthoritative, BodySummary = "Looks good.", HeadSha = request.ExpectedHeadSha }
            ],
            [new ReviewCommentSummary { Author = "reviewer", Path = "src/file.cs", Line = 12, BodySummary = "Could this be clearer?", HeadSha = request.ExpectedHeadSha }],
            [new ReviewCommentSummary { Author = "lead", BodySummary = "Thanks for the update.", HeadSha = request.ExpectedHeadSha }],
            [new ReviewThreadSummary { ThreadId = "thread-1", Summary = "Open requested change.", HeadSha = request.ExpectedHeadSha }]);

        Assert.AreEqual(2, snapshot.ReviewSubmissions.Length);
        Assert.AreEqual(1, snapshot.RequestedChanges.Length);
        Assert.AreEqual(ReviewFeedbackState.ApprovedButNonAuthoritative, snapshot.ReviewSubmissions[1].State);
        Assert.AreEqual(1, snapshot.UnresolvedThreads.Length);
        AssertBoundary(snapshot.Boundary);
        Assert.IsFalse(snapshot.Boundary.CanReplyToReviewComments);
        Assert.IsFalse(snapshot.Boundary.CanResolveReviewThreads);
        Assert.IsFalse(snapshot.Boundary.CanRequestReviewers);
    }

    [TestMethod]
    public void BlockAN_FeedbackClassification_ProducesAdvisoryFindingsOnly()
    {
        var request = CreateRequest();
        var ci = CiObservationBuilder.Build(request, request.ExpectedHeadSha, [new CiCheckRunSummary { Name = "build", Status = "completed", Conclusion = "failure", HeadSha = request.ExpectedHeadSha }], []);
        var review = ReviewFeedbackSnapshotBuilder.Build(
            request,
            request.ExpectedHeadSha,
            [new ReviewSubmissionSummary { Author = "reviewer", State = ReviewFeedbackState.RequestedChanges, BodySummary = "Please change this.", HeadSha = request.ExpectedHeadSha }],
            [new ReviewCommentSummary { Author = "reviewer", Path = "README.md", BodySummary = "Why this wording?", HeadSha = request.ExpectedHeadSha }],
            []);
        var report = FeedbackClassifier.Classify(request, ci, review);

        Assert.IsTrue(report.Findings.Any(item => item.Category == FeedbackCategory.BuildFailure && item.Actionability == FeedbackActionability.RequiresGovernedPatchRun));
        Assert.IsTrue(report.Findings.Any(item => item.Category == FeedbackCategory.ReviewRequestedChange));
        Assert.IsTrue(report.Findings.Any(item => item.Category == FeedbackCategory.ReviewQuestion && item.Actionability == FeedbackActionability.RequiresHumanAnswer));
        AssertBoundary(report.Boundary);
        Assert.IsTrue(report.Findings.All(item => !item.Boundary.CanMutateSource && !item.Boundary.CanReplyToReviewComments));

        var staleCi = CiObservationBuilder.Build(request, new string('e', 40), [new CiCheckRunSummary { Name = "old test", Status = "completed", Conclusion = "failure", HeadSha = new string('e', 40) }], []);
        var staleReport = FeedbackClassifier.Classify(request, staleCi, ReviewFeedbackSnapshotBuilder.Build(request, request.ExpectedHeadSha, [], [], []));
        Assert.IsFalse(staleReport.Findings.Any(item => item.Category == FeedbackCategory.HeadShaDrift && item.Actionability == FeedbackActionability.RequiresGovernedPatchRun));
    }

    [TestMethod]
    public void BlockAN_RemediationPlanAndReadiness_DoNotAdvanceWorkflow()
    {
        var request = CreateRequest();
        var ci = CiObservationBuilder.Build(request, request.ExpectedHeadSha, [new CiCheckRunSummary { Name = "test suite", Status = "completed", Conclusion = "failure", HeadSha = request.ExpectedHeadSha }], []);
        var review = ReviewFeedbackSnapshotBuilder.Build(
            request,
            request.ExpectedHeadSha,
            [],
            [new ReviewCommentSummary { Author = "reviewer", Path = "README.md", BodySummary = "What does this mean?", HeadSha = request.ExpectedHeadSha }],
            []);
        var classification = FeedbackClassifier.Classify(request, ci, review);
        var plan = FeedbackRemediationPlanner.Propose(request, classification, ["Do not patch outside governed flow."]);
        Assert.IsTrue(plan.ProposedPatchScope.Length > 0);
        Assert.IsTrue(plan.SuggestedTests.Length > 0);
        Assert.IsTrue(plan.HumanQuestions.Length > 0);
        Assert.IsTrue(plan.NonAuthorityBoundary.Contains("This remediation plan is not a patch.", StringComparison.Ordinal));
        AssertBoundary(plan.Boundary);

        var readiness = FeedbackReadinessReporter.Report(request, ci, review, classification, plan, plan.KnownRisks);
        Assert.AreEqual(FeedbackReadinessOutcome.NeedsGovernedPatchRun, readiness.Outcome);
        Assert.IsTrue(readiness.Blockers.Length > 0);
        AssertBoundary(readiness.Boundary);
        Assert.IsFalse(readiness.Boundary.CanMarkReadyForReview);
        Assert.IsFalse(readiness.Boundary.CanMerge);
        Assert.IsFalse(readiness.Boundary.CanContinueWorkflow);

        var questionOnly = FeedbackClassifier.Classify(request, CiObservationBuilder.Build(request, request.ExpectedHeadSha, [new CiCheckRunSummary { Name = "build", Status = "completed", Conclusion = "success", HeadSha = request.ExpectedHeadSha }], []), review);
        var questionPlan = FeedbackRemediationPlanner.Propose(request, questionOnly);
        Assert.AreEqual(FeedbackReadinessOutcome.NeedsHumanTriage, FeedbackReadinessReporter.Report(request, ci with { OverallCiState = FeedbackCiState.Passed, Failures = [] }, review, questionOnly, questionPlan).Outcome);

        Assert.AreEqual(FeedbackReadinessOutcome.NeedsEvidenceRefresh, FeedbackReadinessReporter.Report(request, null, review, questionOnly, questionPlan).Outcome);

        var clearReview = ReviewFeedbackSnapshotBuilder.Build(request, request.ExpectedHeadSha, [new ReviewSubmissionSummary { Author = "lead", State = ReviewFeedbackState.ApprovedButNonAuthoritative, BodySummary = "Approved for review only.", HeadSha = request.ExpectedHeadSha }], [], []);
        var clearCi = CiObservationBuilder.Build(request, request.ExpectedHeadSha, [new CiCheckRunSummary { Name = "build", Status = "completed", Conclusion = "success", HeadSha = request.ExpectedHeadSha }], []);
        var clearClassification = FeedbackClassifier.Classify(request, clearCi, clearReview);
        var clearPlan = FeedbackRemediationPlanner.Propose(request, clearClassification);
        Assert.AreEqual(FeedbackReadinessOutcome.NoKnownBlockingFeedback, FeedbackReadinessReporter.Report(request, clearCi, clearReview, clearClassification, clearPlan).Outcome);
    }

    [TestMethod]
    public async Task BlockAN_Cli_BlocksAuthorityShapedSubcommands()
    {
        foreach (var forbidden in new[] { "fix", "apply", "commit", "push", "reply", "resolve", "rerun-ci", "ready", "request-reviewers", "merge", "release", "deploy", "continue" })
        {
            var result = await RunCliAsync("feedback", forbidden, "--run", "run-an").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAN_BypassAndStaticBoundary_ProveFeedbackCannotBecomeAuthority()
    {
        var bypass = FeedbackLoopBypassEvaluator.Evaluate("run-an", ["feedback loop request", "CI observation snapshot", "review feedback snapshot", "feedback classification report", "remediation plan", "feedback readiness report", "test success", "build success", "review approval", "no known blocking feedback", "human-looking approval text", "AI review text", "memory plan text", "release readiness report"]);
        Assert.IsFalse(bypass.CommitCreated);
        Assert.IsFalse(bypass.PushPerformed);
        Assert.IsFalse(bypass.PullRequestUpdated);
        Assert.IsFalse(bypass.ReviewCommentReplied);
        Assert.IsFalse(bypass.ReviewThreadResolved);
        Assert.IsFalse(bypass.CiRerunRequested);
        Assert.IsFalse(bypass.ReadyForReviewMarked);
        Assert.IsFalse(bypass.Merged);
        Assert.IsFalse(bypass.Released);
        Assert.IsFalse(bypass.Deployed);
        Assert.IsFalse(bypass.WorkflowContinued);
        Assert.IsFalse(FeedbackLoopBypassEvaluator.CanUpdatePullRequest(new object()));
        Assert.IsFalse(FeedbackLoopBypassEvaluator.CanRerunCi(new object()));
        Assert.IsFalse(FeedbackLoopBypassEvaluator.CanContinueWorkflow(new object()));
        AssertBoundary(bypass.Boundary);

        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliFeedback.cs"));
        Assert.IsTrue(cli.Contains("\"gh\", [\"pr\", \"view\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(cli.Contains("\"gh\", [\"pr\", \"checks\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(cli.Contains("\"gh\", [\"api\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\", [\"add\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\", [\"commit\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("\"git\", [\"push\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"pr\", \"ready\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"pr\", \"review\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"pr\", \"merge\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"pr\", \"comment\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\", [\"run\", \"rerun\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("resolveReviewThread", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "PR319_324_CI_AND_REVIEW_FEEDBACK_LOOP.md"));
        StringAssert.Contains(receipt, "AN1 feedback loop request.");
        StringAssert.Contains(receipt, "AN2 CI status observation.");
        StringAssert.Contains(receipt, "AN3 review feedback observation.");
        StringAssert.Contains(receipt, "AN4 feedback classification.");
        StringAssert.Contains(receipt, "AN5 remediation plan proposal.");
        StringAssert.Contains(receipt, "AN6 feedback readiness report.");
        StringAssert.Contains(receipt, "AN7 bypass tests and receipt.");
        StringAssert.Contains(receipt, "It does not update PRs.");
        StringAssert.Contains(receipt, "It does not reply to comments.");
        StringAssert.Contains(receipt, "It does not rerun CI.");
        StringAssert.Contains(receipt, "It does not continue workflow.");
    }

    private static FeedbackLoopRequest CreateRequest()
    {
        var headSha = new string('a', 40);
        return FeedbackLoopRequestWriter.Create(new FeedbackLoopRequestInput
        {
            RunId = "run-an-core",
            ProjectId = "project-an",
            RepositoryFullName = "owner/repo",
            PullRequestNumber = 458,
            PullRequestUrl = "https://github.com/owner/repo/pull/458",
            BaseBranch = "main",
            HeadBranch = "feature/an",
            ExpectedHeadSha = headSha,
            PullRequestCreationReceiptId = "pr_receipt_an",
            RequestedBy = "tester",
            Reason = "observe feedback",
            EvidenceRefs = ["pull-request-created-receipt.json"]
        });
    }

    private static PullRequestCreationReceipt WritePullRequestArtifacts(string runPath)
    {
        Directory.CreateDirectory(runPath);
        var headSha = new string('a', 40);
        var receipt = new PullRequestCreationReceipt
        {
            PullRequestCreationReceiptId = "pr_receipt_an",
            RunId = Path.GetFileName(runPath),
            RepositoryFullName = "owner/repo",
            PullRequestNumber = 458,
            PullRequestUrl = "https://github.com/owner/repo/pull/458",
            BaseBranch = "main",
            HeadBranch = "feature/an",
            ExpectedHeadSha = headSha,
            ObservedHeadSha = headSha,
            Draft = true,
            CreatedBy = "tester",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            GateDecisionId = "pr_gate_an",
            EvidenceRefs = ["pull-request-creation-gate.json"]
        };
        WriteJson(Path.Combine(runPath, "pull-request-created-receipt.json"), receipt);
        WriteJson(Path.Combine(runPath, "pull-request-status.json"), new PullRequestStatusReport
        {
            PullRequestStatusReportId = "pr_status_an",
            RunId = receipt.RunId,
            PullRequestNumber = receipt.PullRequestNumber,
            PullRequestUrl = receipt.PullRequestUrl,
            Draft = true
        });
        WriteJson(Path.Combine(runPath, "pull-request-creation-request.json"), PullRequestCreationRequestWriter.Create(new PullRequestCreationRequestInput
        {
            RunId = receipt.RunId,
            ProjectId = "project-an",
            RepositoryFullName = receipt.RepositoryFullName,
            BaseBranch = receipt.BaseBranch,
            HeadBranch = receipt.HeadBranch,
            ExpectedHeadSha = receipt.ExpectedHeadSha,
            CommitPackageRequestId = "commit_req_an",
            CommitReadinessReviewId = "commit_review_an",
            RequestedBy = "tester",
            Reason = "controlled draft PR"
        }));
        new FileBackedGovernanceEventStore(runPath).Append(receipt.RunId, receipt.PullRequestCreationReceiptId, GovernanceKernelEventKind.DraftPullRequestCreated, "PullRequestCreationReceipt", receipt.PullRequestCreationReceiptId, "Draft PR was created.", ["pull-request-created-receipt.json"]);
        return receipt;
    }

    private static void AssertBoundary(FeedbackLoopBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
        Assert.IsFalse(boundary.CanUpdatePullRequest);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanReplyToReviewComments);
        Assert.IsFalse(boundary.CanResolveReviewThreads);
        Assert.IsFalse(boundary.CanRerunCi);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
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
        var path = Path.Combine(Path.GetTempPath(), "irondev-an-" + Guid.NewGuid().ToString("N"));
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
