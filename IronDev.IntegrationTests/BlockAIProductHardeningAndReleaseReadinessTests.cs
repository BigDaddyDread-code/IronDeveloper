using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAIProductHardeningAndReleaseReadinessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockAI_DogfoodCli_WritesProductHardeningArtifactsWithoutAuthority()
    {
        var root = CreateTempRoot();
        try
        {
            var runPath = Path.Combine(root, "ai-run");
            var task = Path.Combine(root, "task.md");
            await File.WriteAllTextAsync(task, "Dogfood the product-hardening path.");

            var result = await RunCliAsync("product-hardening", "dogfood", "--run", runPath, "--project", "project-ai", "--task", task, "--json");

            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            foreach (var artifact in new[]
                     {
                         "dogfood-run.json",
                         "dogfood-run.md",
                         "dogfood-artifact-checklist.json",
                         "dogfood-artifact-checklist.md",
                         "dogfood-known-risks.md",
                         "artifact-consistency-report.json",
                         "artifact-consistency-report.md",
                         "artifact-consistency-issues.jsonl",
                         "unsafe-material-report.json",
                         "unsafe-material-report.md",
                         "unsafe-material-findings.jsonl",
                         "resume-report.json",
                         "resume-report.md",
                         "failure-summary.json",
                         "failure-summary.md",
                         "release-readiness-report.json",
                         "release-readiness-report.md",
                         "release-readiness-checklist.json",
                         "release-readiness-blockers.jsonl",
                         "release-readiness-decision-record.json",
                         "release-readiness-decision-record.md",
                         "product-hardening-bypass-report.json",
                         "product-hardening-bypass-report.md"
                     })
            {
                Assert.IsTrue(File.Exists(Path.Combine(runPath, artifact)), artifact);
            }

            var dogfood = ReadJson<ProductDogfoodRun>(Path.Combine(runPath, "dogfood-run.json"));
            Assert.AreEqual(ProductHardeningAuditOutcome.Pass, dogfood!.Outcome);
            Assert.IsFalse(dogfood.SourceMutated);
            Assert.IsFalse(dogfood.CommitCreated);
            Assert.IsFalse(dogfood.PushPerformed);
            Assert.IsFalse(dogfood.PullRequestCreated);
            Assert.IsFalse(dogfood.MergePerformed);
            Assert.IsFalse(dogfood.ReleasePerformed);
            Assert.IsFalse(dogfood.DeployPerformed);
            Assert.IsFalse(dogfood.WorkflowContinued);
            AssertBoundary(dogfood.Boundary);

            var readiness = ReadJson<ProductReleaseReadinessReport>(Path.Combine(runPath, "release-readiness-report.json"));
            Assert.AreEqual(ProductReleaseReadinessOutcome.ReadyForDecision, readiness!.Outcome);
            Assert.IsFalse(readiness.CanMerge);
            Assert.IsFalse(readiness.CanRelease);
            Assert.IsFalse(readiness.CanDeploy);
            Assert.IsFalse(readiness.CanContinueWorkflow);

            var decision = ReadJson<ProductReleaseReadinessDecisionRecord>(Path.Combine(runPath, "release-readiness-decision-record.json"));
            Assert.AreEqual(ProductReleaseReadinessOutcome.ReadyForDecision, decision!.Decision);
            AssertDecisionBoundary(decision);

            var bypass = ReadJson<ProductHardeningBypassReport>(Path.Combine(runPath, "product-hardening-bypass-report.json"));
            Assert.IsFalse(bypass!.MergeAuthorized);
            Assert.IsFalse(bypass.ReleaseAuthorized);
            Assert.IsFalse(bypass.DeployAuthorized);
            Assert.IsFalse(bypass.SourceMutationAuthorized);
            Assert.IsFalse(bypass.WorkflowContinuationAuthorized);

            Assert.IsTrue(File.Exists(Path.Combine(FindRepositoryRoot(), "tools", "dogfood", "Invoke-ProductHardeningDogfood.ps1")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAI_DogfoodFailure_WritesMissingArtifactRecoveryReport()
    {
        var root = CreateTempRoot();
        try
        {
            var runPath = Path.Combine(root, "ai-run-failure");
            var result = await RunCliAsync(
                "product-hardening",
                "dogfood",
                "--run",
                runPath,
                "--project",
                "project-ai",
                "--task",
                "Dogfood with a missing artifact.",
                "--simulate-missing-artifact",
                "planner-context.json",
                "--json");

            Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
            var dogfood = ReadJson<ProductDogfoodRun>(Path.Combine(runPath, "dogfood-run.json"));
            CollectionAssert.Contains(dogfood!.MissingArtifacts, "planner-context.json");
            var resume = ReadJson<ProductResumeReport>(Path.Combine(runPath, "resume-report.json"));
            CollectionAssert.Contains(resume!.MissingArtifacts, "planner-context.json");
            Assert.IsFalse(resume.ContinuesWorkflow);
            Assert.IsFalse(resume.ExecutesCommands);
            Assert.IsFalse(resume.MutatesSource);
            Assert.IsFalse(resume.PromotesMemory);
            Assert.IsFalse(resume.MarksReleaseReady);
            var resumeText = await File.ReadAllTextAsync(Path.Combine(runPath, "resume-report.md"));
            StringAssert.Contains(resumeText, "planner-context.json");
            StringAssert.Contains(resumeText, "not resume execution");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockAI_ArtifactConsistencyAuditor_ReportsMismatchWithoutRepairAuthority()
    {
        var complete = ProductArtifactConsistencyAuditor.Audit(new ProductArtifactConsistencyAuditRequest
        {
            RunId = "run-ai",
            ProjectId = "project-ai",
            RequiredArtifacts = ["dogfood-run.json", "planner-context.json"],
            Artifacts =
            [
                Descriptor("dogfood-run.json", runId: "run-ai", patchHash: "patch-1", baseCommit: "base-1", memoryHash: "mem-1"),
                Descriptor("planner-context.json", runId: "run-ai", patchHash: "patch-1", baseCommit: "base-1", memoryHash: "mem-1") with
                {
                    GovernanceEventRefs = ["event-1"],
                    ThoughtLedgerRefs = ["thought-1"],
                    ConscienceRefs = ["conscience-1"]
                }
            ]
        });
        Assert.AreEqual(ProductHardeningAuditOutcome.Pass, complete.Outcome);
        Assert.IsFalse(complete.CanApprove);
        Assert.IsFalse(complete.CanExecute);
        Assert.IsFalse(complete.CanRepairArtifacts);
        AssertBoundary(complete.Boundary);

        var mismatched = ProductArtifactConsistencyAuditor.Audit(new ProductArtifactConsistencyAuditRequest
        {
            RunId = "run-ai",
            ProjectId = "project-ai",
            RequiredArtifacts = ["dogfood-run.json", "planner-context.json", "missing.json"],
            Artifacts =
            [
                Descriptor("dogfood-run.json", runId: "run-ai", patchHash: "patch-1", baseCommit: "base-1", memoryHash: "mem-1"),
                Descriptor("planner-context.json", runId: "run-other", patchHash: "patch-2", baseCommit: "base-2", memoryHash: "mem-2")
            ]
        });

        Assert.AreEqual(ProductHardeningAuditOutcome.NotReady, mismatched.Outcome);
        Assert.IsTrue(mismatched.Issues.Any(issue => issue.Code == "MissingArtifact"));
        Assert.IsTrue(mismatched.Issues.Any(issue => issue.Code == "RunIdMismatch"));
        Assert.IsTrue(mismatched.Issues.Any(issue => issue.Code == "PatchHashMismatch"));
        Assert.IsTrue(mismatched.Issues.Any(issue => issue.Code == "BaseCommitMismatch"));
        Assert.IsTrue(mismatched.Issues.Any(issue => issue.Code == "MemoryCitationHashMismatch"));
    }

    [TestMethod]
    public void BlockAI_UnsafeMaterialScanner_RedactsFindingsAndDoesNotApprove()
    {
        var unsafeReport = UnsafeMaterialScanner.Scan(new UnsafeMaterialScanRequest
        {
            RunId = "run-ai",
            ProjectId = "project-ai",
            ArtifactText = new Dictionary<string, string>
            {
                ["secret.txt"] = "api_key=super-secret-value",
                ["reasoning.md"] = "hidden reasoning should never be stored",
                ["authority.md"] = "release approved and safe to deploy"
            }
        });

        Assert.AreEqual(ProductHardeningAuditOutcome.NotReady, unsafeReport.Outcome);
        Assert.IsTrue(unsafeReport.Findings.Any(finding => finding.Kind == "SecretShape"));
        Assert.IsTrue(unsafeReport.Findings.Any(finding => finding.Kind == "HiddenReasoning"));
        Assert.IsTrue(unsafeReport.Findings.Any(finding => finding.Kind == "AuthorityClaim"));
        Assert.IsFalse(unsafeReport.Findings.Any(finding => finding.RedactedPreview.Contains("super-secret-value", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(unsafeReport.CanApprove);
        Assert.IsFalse(unsafeReport.CanExecute);
        Assert.IsFalse(unsafeReport.MutatesArtifacts);
        AssertBoundary(unsafeReport.Boundary);

        var safeReport = UnsafeMaterialScanner.Scan(new UnsafeMaterialScanRequest
        {
            RunId = "run-ai",
            ProjectId = "project-ai",
            ArtifactText = new Dictionary<string, string> { ["safe.md"] = "Readiness evidence requires human review." }
        });
        Assert.AreEqual(ProductHardeningAuditOutcome.Pass, safeReport.Outcome);
    }

    [TestMethod]
    public void BlockAI_ResumeAndReadinessReports_RemainEvidenceOnly()
    {
        var resume = ProductResumeReportBuilder.Build(new ProductResumeReportRequest
        {
            RunId = "run-ai",
            ProjectId = "project-ai",
            Steps =
            [
                new ProductDogfoodStep { StepId = "one", Name = "manual patch proposal", Status = ProductHardeningStepStatus.Completed },
                new ProductDogfoodStep { StepId = "two", Name = "artifact consistency auditor", Status = ProductHardeningStepStatus.Failed }
            ],
            ArtifactRefs = ["dogfood-run.json"],
            MissingArtifacts = ["artifact-consistency-report.json"],
            FailedArtifact = "artifact-consistency-report.json"
        });
        Assert.AreEqual("manual patch proposal", resume.LastCompletedProductStep);
        Assert.AreEqual("dogfood-run.json", resume.LastSafeArtifact);
        Assert.IsFalse(resume.ContinuesWorkflow);
        Assert.IsFalse(resume.ExecutesCommands);
        Assert.IsFalse(resume.MutatesSource);
        Assert.IsFalse(resume.PromotesMemory);
        Assert.IsFalse(resume.MarksReleaseReady);

        var ready = ProductReleaseReadinessEvaluator.Evaluate(ReadyRequest());
        Assert.AreEqual(ProductReleaseReadinessOutcome.ReadyForDecision, ready.Outcome);
        Assert.IsFalse(ready.CanMerge);
        Assert.IsFalse(ready.CanRelease);
        Assert.IsFalse(ready.CanDeploy);
        Assert.IsFalse(ready.CanContinueWorkflow);

        Assert.AreEqual(ProductReleaseReadinessOutcome.NeedsMoreEvidence, ProductReleaseReadinessEvaluator.Evaluate(ReadyRequest() with { FocusedBlockValidationRecorded = false }).Outcome);
        Assert.AreEqual(ProductReleaseReadinessOutcome.NeedsMoreEvidence, ProductReleaseReadinessEvaluator.Evaluate(ReadyRequest() with { BuildResultRecorded = false }).Outcome);
        Assert.AreEqual(ProductReleaseReadinessOutcome.NotReady, ProductReleaseReadinessEvaluator.Evaluate(ReadyRequest() with { UnsafeMaterialPassed = false }).Outcome);
        Assert.AreEqual(ProductReleaseReadinessOutcome.NotReady, ProductReleaseReadinessEvaluator.Evaluate(ReadyRequest() with { ArtifactConsistencyPassed = false }).Outcome);
    }

    [TestMethod]
    public void BlockAI_ReadinessDecisionRecord_IsBoundedAndValidated()
    {
        var report = ProductReleaseReadinessEvaluator.Evaluate(ReadyRequest());
        foreach (var decision in new[] { ProductReleaseReadinessOutcome.ReadyForDecision, ProductReleaseReadinessOutcome.NotReady, ProductReleaseReadinessOutcome.NeedsMoreEvidence })
        {
            var result = ProductReleaseReadinessDecisionRecorder.Create(new ProductReleaseReadinessDecisionRequest
            {
                Report = report,
                Decision = decision,
                Reasons = [$"status {decision}"],
                EvidenceRefs = ["release-readiness-report.json"],
                ReviewedBy = "human-reviewer"
            });

            Assert.IsTrue(result.IsValid, string.Join(",", result.Issues));
            Assert.AreEqual(decision, result.Record!.Decision);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Record.DecisionHash));
            AssertDecisionBoundary(result.Record);
        }

        CollectionAssert.Contains(ProductReleaseReadinessDecisionRecorder.Create(new ProductReleaseReadinessDecisionRequest
        {
            Report = null,
            Decision = ProductReleaseReadinessOutcome.NeedsMoreEvidence,
            ReviewedBy = "human-reviewer"
        }).Issues, "MissingReadinessReport");
        CollectionAssert.Contains(ProductReleaseReadinessDecisionRecorder.Create(new ProductReleaseReadinessDecisionRequest
        {
            Report = report,
            Decision = ProductReleaseReadinessOutcome.NeedsMoreEvidence,
            ReviewedBy = ""
        }).Issues, "MissingReviewer");
        CollectionAssert.Contains(ProductReleaseReadinessDecisionRecorder.Create(new ProductReleaseReadinessDecisionRequest
        {
            Report = report with { ReadinessReportHash = "" },
            Decision = ProductReleaseReadinessOutcome.NeedsMoreEvidence,
            ReviewedBy = "human-reviewer"
        }).Issues, "MissingReadinessReportHash");
    }

    [TestMethod]
    public async Task BlockAI_BypassTestsAndReceipt_ProveEvidenceCannotBecomeAuthority()
    {
        var bypass = ProductHardeningBypassEvaluator.Evaluate("run-ai", ["dogfood success", "artifact audit pass", "unsafe scan pass", "resume report", "readiness report", "decision record", "test pass", "build pass", "diff-check pass"]);
        Assert.IsFalse(bypass.MergeAuthorized);
        Assert.IsFalse(bypass.ReleaseAuthorized);
        Assert.IsFalse(bypass.DeployAuthorized);
        Assert.IsFalse(bypass.SourceMutationAuthorized);
        Assert.IsFalse(bypass.WorkflowContinuationAuthorized);
        Assert.IsFalse(ProductHardeningBypassEvaluator.CanAuthorizeShipping(new object()));
        AssertBoundary(bypass.Boundary);

        foreach (var forbidden in new[] { "approve", "execute", "apply", "continue", "promote-memory", "release", "deploy", "merge", "commit", "push", "pull-request" })
        {
            var result = await RunCliAsync("product-hardening", forbidden, "--run", "run-ai");
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }

        var receipt = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PR302_308_PRODUCT_HARDENING_AND_RELEASE_READINESS.md"));
        StringAssert.Contains(receipt, "AI1 dogfood script");
        StringAssert.Contains(receipt, "AI2 artifact consistency auditor");
        StringAssert.Contains(receipt, "AI3 secrets/unsafe material hardening");
        StringAssert.Contains(receipt, "AI4 error recovery/resume hardening");
        StringAssert.Contains(receipt, "AI5 release-readiness evaluator");
        StringAssert.Contains(receipt, "AI6 release-readiness decision record");
        StringAssert.Contains(receipt, "AI7 bypass tests and receipt");
        StringAssert.Contains(receipt, "No merge/release/deploy authority is added.");
    }

    private static ProductArtifactDescriptor Descriptor(string artifact, string runId, string patchHash, string baseCommit, string memoryHash) => new()
    {
        ArtifactName = artifact,
        Exists = true,
        RunId = runId,
        ProjectId = "project-ai",
        PatchHash = patchHash,
        BaseCommit = baseCommit,
        SourceRepoIdentity = "repo-ai",
        ChangedFiles = ["README.md"],
        MemoryCitationHashes = [memoryHash],
        GovernanceEventRefs = ["event-1"],
        ThoughtLedgerRefs = ["thought-1"],
        ConscienceRefs = ["conscience-1"]
    };

    private static ProductReleaseReadinessEvaluationRequest ReadyRequest() => new()
    {
        RunId = "run-ai",
        ProjectId = "project-ai",
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
        EvidenceRefs = ["focused-ai", "stable-z-ai", "build", "diff-check"]
    };

    private static void AssertBoundary(ProductHardeningBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanPromoteMemory);
    }

    private static void AssertDecisionBoundary(ProductReleaseReadinessDecisionRecord record)
    {
        StringAssert.Contains(record.Boundary, "does not release");
        StringAssert.Contains(record.Boundary, "does not deploy");
        StringAssert.Contains(record.Boundary, "does not merge");
        StringAssert.Contains(record.Boundary, "does not continue workflow");
        StringAssert.Contains(record.Boundary, "does not approve source mutation");
        Assert.IsFalse(record.Releases);
        Assert.IsFalse(record.Deploys);
        Assert.IsFalse(record.Merges);
        Assert.IsFalse(record.ContinuesWorkflow);
        Assert.IsFalse(record.ApprovesSourceMutation);
    }

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-ai-" + Guid.NewGuid().ToString("N"));
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
            // Best-effort cleanup for Windows file handles in test runs.
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
