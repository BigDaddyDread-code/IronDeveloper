using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAOMergeAndReleaseSeparationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockAO_Cli_RequestAndStatus_WriteEvidenceOnlyArtifacts()
    {
        var root = CreateTempRoot();
        try
        {
            var runPath = Path.Combine(root, "run-ao");
            var receipt = WritePullRequestArtifacts(runPath);
            WriteFeedbackReadiness(runPath, receipt, FeedbackReadinessOutcome.NoKnownBlockingFeedback);

            var result = await RunCliAsync("merge-release", "request", "--run", runPath, "--repo", receipt.RepositoryFullName, "--pr", receipt.PullRequestNumber.ToString(), "--expected-head", receipt.ExpectedHeadSha, "--json").ConfigureAwait(false);
            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            Assert.AreEqual(0, (await RunCliAsync("merge-release", "status", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);

            foreach (var artifact in new[] { "merge-release-separation-request.json", "merge-release-separation-request.md", "merge-release-bypass-report.json", "merge-release-bypass-report.md", "governance-events.jsonl" })
                Assert.IsTrue(File.Exists(Path.Combine(runPath, artifact)), artifact);

            var request = ReadJson<MergeReleaseSeparationRequest>(Path.Combine(runPath, "merge-release-separation-request.json"));
            Assert.AreEqual(receipt.RepositoryFullName, request!.RepositoryFullName);
            Assert.AreEqual(receipt.PullRequestNumber, request.PullRequestNumber);
            Assert.AreEqual(receipt.ExpectedHeadSha, request.ExpectedHeadSha);
            Assert.AreEqual(receipt.PullRequestCreationReceiptId, request.PullRequestCreationReceiptId);
            AssertBoundary(request.Boundary);

            var missingRun = Path.Combine(root, "missing-pr");
            Directory.CreateDirectory(missingRun);
            var missing = await RunCliAsync("merge-release", "request", "--run", missingRun, "--repo", "owner/repo", "--pr", "1", "--expected-head", receipt.ExpectedHeadSha).ConfigureAwait(false);
            Assert.AreEqual(1, missing.ExitCode);
            StringAssert.Contains(missing.Error, "pull-request-created-receipt.json is required");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAO_Cli_EndToEnd_WritesSeparationArtifactsWithoutMergeOrRelease()
    {
        var root = CreateTempRoot();
        try
        {
            var runPath = Path.Combine(root, "run-ao-flow");
            var receipt = WritePullRequestArtifacts(runPath);
            WriteMergeEvidenceArtifacts(runPath, receipt, FeedbackCiState.Passed, requestedChanges: 0);
            WriteReleaseEvidenceArtifacts(runPath, receipt, merged: false);

            Assert.AreEqual(0, (await RunCliAsync("merge-release", "request", "--run", runPath, "--repo", receipt.RepositoryFullName, "--pr", receipt.PullRequestNumber.ToString(), "--expected-head", receipt.ExpectedHeadSha, "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(0, (await RunCliAsync("merge-release", "merge-evidence", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(1, (await RunCliAsync("merge-release", "release-evidence", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(0, (await RunCliAsync("merge-release", "boundary-map", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(0, (await RunCliAsync("merge-release", "records", "--run", runPath, "--reviewed-by", "tester", "--json").ConfigureAwait(false)).ExitCode);
            Assert.AreEqual(0, (await RunCliAsync("merge-release", "status", "--run", runPath, "--json").ConfigureAwait(false)).ExitCode);

            var merge = ReadJson<MergeReadinessEvidencePackage>(Path.Combine(runPath, "merge-readiness-evidence-package.json"));
            var release = ReadJson<ReleaseReadinessEvidencePackage>(Path.Combine(runPath, "release-readiness-evidence-package.json"));
            var report = ReadJson<MergeReleaseSeparationReport>(Path.Combine(runPath, "merge-release-separation-report.json"));
            Assert.AreEqual(MergeReadinessOutcome.ReadyForMergeDecision, merge!.Outcome);
            Assert.AreEqual(ReleaseReadinessEvidenceOutcome.NotApplicableBeforeMerge, release!.Outcome);
            Assert.AreEqual(MergeSeparationReadinessOutcome.MergeDecisionCandidate, report!.MergeOutcome);
            Assert.AreEqual(ReleaseSeparationReadinessOutcome.NotApplicableBeforeMerge, report.ReleaseOutcome);
            AssertBoundary(merge.Boundary);
            AssertBoundary(release.Boundary);
            AssertBoundary(report.Boundary);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockAO_MergeEvidence_OutcomesDoNotCreateMergeAuthority()
    {
        var request = CreateRequest();
        var ready = MergeReadinessEvidencePackager.Build(CreateMergeInput(request));
        Assert.AreEqual(MergeReadinessOutcome.ReadyForMergeDecision, ready.Outcome);
        AssertBoundary(ready.Boundary);

        Assert.AreEqual(MergeReadinessOutcome.BlockedByCi, MergeReadinessEvidencePackager.Build(CreateMergeInput(request) with { CiState = FeedbackCiState.Failed }).Outcome);
        Assert.AreEqual(MergeReadinessOutcome.BlockedByFeedback, MergeReadinessEvidencePackager.Build(CreateMergeInput(request) with { RequestedChangeCount = 1 }).Outcome);
        Assert.AreEqual(MergeReadinessOutcome.BlockedByUnsafeMaterial, MergeReadinessEvidencePackager.Build(CreateMergeInput(request) with { UnsafeMaterialFindings = 1 }).Outcome);
        Assert.AreEqual(MergeReadinessOutcome.BlockedByArtifactMismatch, MergeReadinessEvidencePackager.Build(CreateMergeInput(request) with { ArtifactConsistencyBlockers = 1 }).Outcome);
        Assert.AreEqual(MergeReadinessOutcome.HeadChanged, MergeReadinessEvidencePackager.Build(CreateMergeInput(request) with { ObservedHeadSha = new string('b', 40) }).Outcome);
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanMerge(ready));
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanRelease(ready));
    }

    [TestMethod]
    public void BlockAO_ReleaseEvidence_IsSeparateAndDoesNotReleaseDeployTagOrPublish()
    {
        var request = CreateRequest();
        var ready = ReleaseReadinessEvidencePackager.Build(CreateReleaseInput(request));
        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.ReadyForReleaseDecision, ready.Outcome);
        AssertBoundary(ready.Boundary);

        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.NeedsMoreReleaseEvidence, ReleaseReadinessEvidencePackager.Build(CreateReleaseInput(request) with { ReleaseReadinessReportExists = false }).Outcome);
        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.NotApplicableBeforeMerge, ReleaseReadinessEvidencePackager.Build(CreateReleaseInput(request) with { PullRequestMerged = false }).Outcome);
        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.BlockedByUnsafeMaterial, ReleaseReadinessEvidencePackager.Build(CreateReleaseInput(request) with { UnsafeMaterialFindings = 1 }).Outcome);
        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.BlockedByArtifactMismatch, ReleaseReadinessEvidencePackager.Build(CreateReleaseInput(request) with { ArtifactConsistencyBlockers = 1 }).Outcome);
        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.BlockedByMissingRecoveryEvidence, ReleaseReadinessEvidencePackager.Build(CreateReleaseInput(request) with { RecoveryEvidenceExists = false }).Outcome);
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanRelease(ready));
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanDeploy(ready));
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanTag(ready));
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanPublish(ready));
    }

    [TestMethod]
    public void BlockAO_BoundaryMap_BlocksEvidenceFamilySmuggling()
    {
        var map = MergeReleaseBoundaryMapper.Build(
            "run-ao",
            ["commit-readiness-review.json", "ci-observation-snapshot.json", "review-feedback-snapshot.json", "release-readiness-report.json", "artifact-consistency-report.json", "unsafe-material-report.json"],
            claimedReleaseEvidence: ["CI pass", "review approval", "merge-readiness-evidence-package.json", "feedback-readiness-report.json", "no known blocking feedback"],
            claimedMergeEvidence: ["release-readiness-evidence-package.json"]);

        Assert.IsTrue(map.MergeEvidenceRefs.Contains("ci-observation-snapshot.json"));
        Assert.IsTrue(map.ReleaseEvidenceRefs.Contains("release-readiness-report.json"));
        Assert.IsTrue(map.SharedEvidenceRefs.Contains("artifact-consistency-report.json"));
        Assert.IsTrue(map.ForbiddenCrossUseFindings.Any(item => item.Contains("CI pass", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(map.ForbiddenCrossUseFindings.Any(item => item.Contains("release-readiness-evidence-package.json", StringComparison.OrdinalIgnoreCase)));
        AssertBoundary(map.Boundary);
    }

    [TestMethod]
    public void BlockAO_SeparationRecords_DoNotLetOneCandidateImplyTheOther()
    {
        var request = CreateRequest();
        var merge = MergeReadinessEvidencePackager.Build(CreateMergeInput(request));
        var release = ReleaseReadinessEvidencePackager.Build(CreateReleaseInput(request) with { PullRequestMerged = false });
        var map = MergeReleaseBoundaryMapper.Build(request.RunId, ["merge-readiness-evidence-package.json", "release-readiness-evidence-package.json"]);
        var records = MergeReleaseSeparationRecordBuilder.Build(request, merge, release, map, "tester");

        Assert.AreEqual(MergeSeparationReadinessOutcome.MergeDecisionCandidate, records.MergeRecord.Outcome);
        Assert.AreEqual(ReleaseSeparationReadinessOutcome.NotApplicableBeforeMerge, records.ReleaseRecord.Outcome);
        Assert.AreNotEqual(ReleaseSeparationReadinessOutcome.ReleaseDecisionCandidate, records.ReleaseRecord.Outcome);
        StringAssert.Contains(string.Join(" ", records.CombinedReport.BoundaryStatements), "This report does not merge.");
        StringAssert.Contains(string.Join(" ", records.CombinedReport.BoundaryStatements), "This report does not release.");
        AssertBoundary(records.MergeRecord.Boundary);
        AssertBoundary(records.ReleaseRecord.Boundary);
        AssertBoundary(records.CombinedReport.Boundary);
    }

    [TestMethod]
    public async Task BlockAO_StaticBoundaryAndReceipt_ProveNoMergeReleaseExecutionSurface()
    {
        foreach (var forbidden in new[] { "merge", "auto-merge", "enable-auto-merge", "release", "deploy", "tag", "publish", "continue" })
        {
            var result = await RunCliAsync("merge-release", forbidden, "--run", "run-ao").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }

        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliMergeRelease.cs"));
        Assert.IsFalse(cli.Contains("RunProcessAsync(\"gh\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh release create", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git tag", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git push --tags", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("dotnet nuget push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("merge_pull_request", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("enable_auto_merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("ContinueWorkflow", StringComparison.Ordinal));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "PR325_330_MERGE_AND_RELEASE_SEPARATION.md"));
        StringAssert.Contains(receipt, "AO1 merge/release separation request.");
        StringAssert.Contains(receipt, "AO2 merge readiness evidence package.");
        StringAssert.Contains(receipt, "AO3 release readiness evidence package.");
        StringAssert.Contains(receipt, "AO4 merge-to-release boundary map.");
        StringAssert.Contains(receipt, "AO5 separation readiness records.");
        StringAssert.Contains(receipt, "AO6 bypass tests and receipt.");
        StringAssert.Contains(receipt, "CI pass is not merge permission.");
        StringAssert.Contains(receipt, "Review approval is not merge permission.");
        StringAssert.Contains(receipt, "Merge readiness is not release readiness.");
        StringAssert.Contains(receipt, "Release readiness is not release execution.");
    }

    private static MergeReleaseSeparationRequest CreateRequest()
    {
        var headSha = new string('a', 40);
        return MergeReleaseSeparationRequestWriter.Create(new MergeReleaseSeparationRequestInput
        {
            RunId = "run-ao-core",
            ProjectId = "project-ao",
            RepositoryFullName = "owner/repo",
            PullRequestNumber = 459,
            PullRequestUrl = "https://github.com/owner/repo/pull/459",
            BaseBranch = "main",
            HeadBranch = "feature/ao",
            ExpectedHeadSha = headSha,
            PullRequestCreationReceiptId = "pr_receipt_ao",
            FeedbackReadinessReportId = "feedback_ready_ao",
            RequestedBy = "tester",
            Reason = "separate merge and release",
            EvidenceRefs = ["pull-request-created-receipt.json", "feedback-readiness-report.json"]
        });
    }

    private static MergeReadinessEvidenceInput CreateMergeInput(MergeReleaseSeparationRequest request) => new()
    {
        Request = request,
        PullRequestReceiptExists = true,
        PullRequestStatusExists = true,
        ObservedHeadSha = request.ExpectedHeadSha,
        PullRequestDraft = true,
        CommitReadinessReviewExists = true,
        CommitReadinessDecision = CommitReadinessDecision.ReadyForHumanCommitReview,
        CiObservationExists = true,
        CiState = FeedbackCiState.Passed,
        ReviewFeedbackSnapshotExists = true,
        RequestedChangeCount = 0,
        FeedbackReadinessReportExists = true,
        FeedbackReadinessOutcome = FeedbackReadinessOutcome.NoKnownBlockingFeedback,
        ArtifactConsistencyReportExists = true,
        UnsafeMaterialReportExists = true,
        EvidenceRefs = ["commit-readiness-review.json", "ci-observation-snapshot.json", "review-feedback-snapshot.json", "feedback-readiness-report.json"]
    };

    private static ReleaseReadinessEvidenceInput CreateReleaseInput(MergeReleaseSeparationRequest request) => new()
    {
        Request = request,
        PullRequestStatusExists = true,
        PullRequestMerged = true,
        ReleaseCandidateRef = "merge-commit-sha",
        ProductHardeningEvidenceExists = true,
        ProductHardeningPassed = true,
        ReleaseReadinessReportExists = true,
        ReleaseReadinessReportOutcome = nameof(ProductReleaseReadinessOutcome.ReadyForDecision),
        ReleaseReadinessDecisionRecordExists = true,
        ArtifactConsistencyReportExists = true,
        UnsafeMaterialReportExists = true,
        KnownRisksDocumented = true,
        RecoveryEvidenceExists = true,
        EvidenceRefs = ["dogfood-run.json", "release-readiness-report.json", "release-readiness-decision-record.json", "resume-report.json"]
    };

    private static PullRequestCreationReceipt WritePullRequestArtifacts(string runPath)
    {
        Directory.CreateDirectory(runPath);
        var headSha = new string('a', 40);
        var receipt = new PullRequestCreationReceipt
        {
            PullRequestCreationReceiptId = "pr_receipt_ao",
            RunId = Path.GetFileName(runPath),
            RepositoryFullName = "owner/repo",
            PullRequestNumber = 459,
            PullRequestUrl = "https://github.com/owner/repo/pull/459",
            BaseBranch = "main",
            HeadBranch = "feature/ao",
            ExpectedHeadSha = headSha,
            ObservedHeadSha = headSha,
            Draft = true,
            CreatedBy = "tester",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            GateDecisionId = "pr_gate_ao",
            EvidenceRefs = ["pull-request-creation-gate.json"]
        };
        WriteJson(Path.Combine(runPath, "pull-request-created-receipt.json"), receipt);
        WriteJson(Path.Combine(runPath, "pull-request-status.json"), new PullRequestStatusReport
        {
            PullRequestStatusReportId = "pr_status_ao",
            RunId = receipt.RunId,
            PullRequestNumber = receipt.PullRequestNumber,
            PullRequestUrl = receipt.PullRequestUrl,
            Draft = true
        });
        WriteJson(Path.Combine(runPath, "pull-request-creation-request.json"), PullRequestCreationRequestWriter.Create(new PullRequestCreationRequestInput
        {
            RunId = receipt.RunId,
            ProjectId = "project-ao",
            RepositoryFullName = receipt.RepositoryFullName,
            BaseBranch = receipt.BaseBranch,
            HeadBranch = receipt.HeadBranch,
            ExpectedHeadSha = receipt.ExpectedHeadSha,
            CommitPackageRequestId = "commit_req_ao",
            CommitReadinessReviewId = "commit_review_ao",
            RequestedBy = "tester",
            Reason = "controlled draft PR"
        }));
        return receipt;
    }

    private static void WriteMergeEvidenceArtifacts(string runPath, PullRequestCreationReceipt receipt, FeedbackCiState ciState, int requestedChanges)
    {
        WriteJson(Path.Combine(runPath, "commit-readiness-review.json"), new CommitReadinessReview
        {
            CommitReadinessReviewId = "commit_review_ao",
            RunId = receipt.RunId,
            Decision = CommitReadinessDecision.ReadyForHumanCommitReview,
            Findings = ["ready for human commit review"],
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        var feedbackRequest = FeedbackLoopRequestWriter.Create(new FeedbackLoopRequestInput
        {
            RunId = receipt.RunId,
            ProjectId = "project-ao",
            RepositoryFullName = receipt.RepositoryFullName,
            PullRequestNumber = receipt.PullRequestNumber,
            PullRequestUrl = receipt.PullRequestUrl,
            BaseBranch = receipt.BaseBranch,
            HeadBranch = receipt.HeadBranch,
            ExpectedHeadSha = receipt.ExpectedHeadSha,
            PullRequestCreationReceiptId = receipt.PullRequestCreationReceiptId,
            RequestedBy = "tester",
            Reason = "observe feedback",
            EvidenceRefs = ["pull-request-created-receipt.json"]
        });
        var ci = CiObservationBuilder.Build(feedbackRequest, receipt.ExpectedHeadSha, [new CiCheckRunSummary { Name = "build", Status = "completed", Conclusion = ciState == FeedbackCiState.Passed ? "success" : "failure", HeadSha = receipt.ExpectedHeadSha }], []);
        WriteJson(Path.Combine(runPath, "ci-observation-snapshot.json"), ci);
        var review = ReviewFeedbackSnapshotBuilder.Build(
            feedbackRequest,
            receipt.ExpectedHeadSha,
            requestedChanges > 0 ? [new ReviewSubmissionSummary { Author = "reviewer", State = ReviewFeedbackState.RequestedChanges, BodySummary = "Please change this.", HeadSha = receipt.ExpectedHeadSha }] : [],
            [],
            []);
        WriteJson(Path.Combine(runPath, "review-feedback-snapshot.json"), review);
        WriteFeedbackReadiness(runPath, receipt, requestedChanges > 0 ? FeedbackReadinessOutcome.NeedsGovernedPatchRun : FeedbackReadinessOutcome.NoKnownBlockingFeedback);
        WriteJson(Path.Combine(runPath, "artifact-consistency-report.json"), new { outcome = "Pass", issues = Array.Empty<object>() });
        WriteJson(Path.Combine(runPath, "unsafe-material-report.json"), new { outcome = "Pass", findings = Array.Empty<object>() });
    }

    private static void WriteFeedbackReadiness(string runPath, PullRequestCreationReceipt receipt, FeedbackReadinessOutcome outcome)
    {
        WriteJson(Path.Combine(runPath, "feedback-readiness-report.json"), new FeedbackReadinessReport
        {
            FeedbackReadinessReportId = "feedback_ready_ao",
            RunId = receipt.RunId,
            RepositoryFullName = receipt.RepositoryFullName,
            PullRequestNumber = receipt.PullRequestNumber,
            ExpectedHeadSha = receipt.ExpectedHeadSha,
            ObservedHeadSha = receipt.ExpectedHeadSha,
            Outcome = outcome,
            SuggestedNextCommand = "irondev feedback status --run run-ao",
            KnownRisks = ["feedback readiness is not merge permission"],
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static void WriteReleaseEvidenceArtifacts(string runPath, PullRequestCreationReceipt receipt, bool merged)
    {
        WriteJson(Path.Combine(runPath, "dogfood-run.json"), ProductHardeningDogfood.CreateRun(new ProductDogfoodRunRequest
        {
            RunId = receipt.RunId,
            ProjectId = "project-ao",
            TaskSummary = "AO release evidence"
        }));
        var readiness = ProductReleaseReadinessEvaluator.Evaluate(new ProductReleaseReadinessEvaluationRequest
        {
            RunId = receipt.RunId,
            ProjectId = "project-ao",
            FocusedBlockValidationRecorded = true,
            StableBandValidationRecorded = true,
            BuildResultRecorded = true,
            DiffCheckRecorded = true,
            ArtifactConsistencyReportExists = true,
            ArtifactConsistencyPassed = true,
            UnsafeMaterialReportExists = true,
            UnsafeMaterialPassed = true,
            ResumeReportBehaviorExists = true,
            KnownRisksDocumented = true,
            AuthorityBoundaryDocumented = true,
            NoUnsupportedMutationSurfacesPresent = true,
            EvidenceRefs = ["focused", "stable", "build", "diff"]
        });
        WriteJson(Path.Combine(runPath, "release-readiness-report.json"), readiness);
        WriteJson(Path.Combine(runPath, "release-readiness-decision-record.json"), new { releaseReadinessDecisionId = "release_decision_ao", decision = readiness.Outcome, releases = false, deploys = false, merges = false });
        File.WriteAllText(Path.Combine(runPath, "dogfood-known-risks.md"), "Release evidence is not release execution.");
        WriteJson(Path.Combine(runPath, "resume-report.json"), new { resumeReportId = "resume_ao", continuesWorkflow = false });
        var status = ReadJson<PullRequestStatusReport>(Path.Combine(runPath, "pull-request-status.json"))!;
        WriteJson(Path.Combine(runPath, "pull-request-status.json"), status with { Merged = merged, Draft = !merged });
    }

    private static void AssertBoundary(MergeReleaseSeparationBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanTag);
        Assert.IsFalse(boundary.CanPublish);
        Assert.IsFalse(boundary.CanUpdatePullRequest);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanRerunCi);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
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
        var path = Path.Combine(Path.GetTempPath(), "irondev-ao-" + Guid.NewGuid().ToString("N"));
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
