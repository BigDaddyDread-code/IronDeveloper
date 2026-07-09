using System.Reflection;
using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Agents.Concrete;
using IronDev.Core.Builder;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.Workspaces;
using IronDev.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// P1-1 — the critic actually reviews. Protected boundaries:
/// - the critic PULLS the package from durable evidence; the requester cannot hand it
///   a curated copy, and the request contract has no channel for memory or narrative;
/// - the review is recorded through the stored manual-critic path, whose review-only
///   validation rejects authority claims — even when the model produces them;
/// - findings are advisory: the service holds no reference to approvals or executors,
///   and a failed review is an explicit failure, never a silently absent review.
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class SkeletonCriticReviewTests
{
    private const int ProjectId = 7;
    private const long TicketId = 42;
    private const string RunId = "run-critic-1";

    // ── Blind by contract ─────────────────────────────────────────────────────

    [TestMethod]
    public void CriticReviewRequestContract_HasNoChannelForMemoryOrACuratedPackage()
    {
        var propertyNames = typeof(SkeletonCriticReviewRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "ProjectId", "TicketId", "RunId", "RequestedByUserId" },
            propertyNames,
            "The critic review request names the run and the requesting human — nothing else. " +
            "The critic pulls the package itself from durable evidence.");

        foreach (var forbidden in new[]
        {
            // No curated work: the requester cannot supply what the critic reviews.
            "Package", "Diff", "Content", "Change", "Finding", "Verdict",
            // No memory or narrative: outside memory is not outside evidence.
            "Memory", "Collective", "Global", "Recall", "History", "Conversation",
            "Reasoning", "Narrative", "Belief", "Prior", "Context", "Scratchpad"
        })
        {
            Assert.IsFalse(
                propertyNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"The critic request surface must not carry: {forbidden}");
        }
    }

    [TestMethod]
    public void CriticReviewService_HoldsNoApprovalExecutorOrMemorySurface()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "SkeletonCriticReviewService.cs"));

        foreach (var forbidden in new[]
        {
            "AcceptedApproval",
            "SatisfyPolicy",
            "ApprovalGranted",
            "ControlledSourceApply",
            "ControlledCommitExecutor",
            "ControlledPushExecutor",
            "IAgentMemory",
            "CollectiveMemory",
            "MemoryPack",
            "IChatHistory"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"The critic reviews; it cannot approve, execute, or remember: {forbidden}");
        }

        StringAssert.Contains(source, "advisory");
        StringAssert.Contains(source, "not approval");
        StringAssert.Contains(source, "not a veto");
        StringAssert.Contains(source, "pulls its subject from durable evidence");
    }

    [TestMethod]
    public void GroundTruthVerifier_IsHarnessNotAgent_AndHoldsNoApprovalSurface()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "SkeletonCriticGroundTruthVerifier.cs"));

        // P1-2: the verifier re-executes evidence, so it may use the disposable
        // workspace service — but it must never touch approvals, executors, or
        // memory, and it must state that it is the harness around the critic,
        // not the boxed review-only agent.
        foreach (var forbidden in new[]
        {
            "AcceptedApproval",
            "SatisfyPolicy",
            "ApprovalGranted",
            "ControlledSourceApply",
            "ControlledCommitExecutor",
            "ControlledPushExecutor",
            "IAgentMemory",
            "CollectiveMemory",
            "MemoryPack"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"The verifier establishes ground truth; it cannot approve, execute controlled mutations, or remember: {forbidden}");
        }

        StringAssert.Contains(source, "not the critic agent");
        StringAssert.Contains(source, "grants nothing");
        StringAssert.Contains(source, "trust but verify");
    }

    // ── The critic reviews and the link is durable ────────────────────────────

    [TestMethod]
    public async Task ReviewAsync_RecordsTheReview_AndPublishesTheRunLink()
    {
        using var harness = CriticHarness.Create(llmResponse: () => """
            {"verdict":"RequestChanges","findings":[{"severity":"High","title":"Sort ignores culture",
            "problem":"The diff compares titles ordinally.","whyItMatters":"Criterion says alphabetical for users.",
            "requiredFix":"Use culture-aware comparison.","blocksMerge":false}]}
            """);

        var outcome = await harness.Service.ReviewAsync(Request());

        Assert.IsNotNull(outcome);
        Assert.IsTrue(outcome!.Succeeded, outcome.FailureReason);
        Assert.AreEqual("RequestChanges", outcome.Verdict);
        Assert.AreEqual(1, outcome.Findings.Count);
        StringAssert.Contains(outcome.Boundary, "not a veto");

        var recorded = harness.Events.Single("SkeletonCriticReviewRecorded");
        Assert.AreEqual(outcome.CriticAgentRunId, recorded.Payload["criticAgentRunId"]);
        Assert.AreEqual(outcome.ReviewId, recorded.Payload["reviewId"]);
        Assert.AreEqual("RequestChanges", recorded.Payload["verdict"]);
        Assert.AreEqual(harness.PackageSha256, recorded.Payload["packageSha256"],
            "The run link names the exact package hash the critic reviewed.");

        var stored = harness.StoredCritic.LastRequest;
        Assert.IsNotNull(stored);
        Assert.AreEqual(CriticReviewSubjectType.WorkPackage, stored!.SubjectType);
        Assert.AreEqual(RunId, stored.RunId, "The audit record is scoped to the skeleton run.");
        Assert.IsTrue(stored.FindingDrafts.All(finding => finding.RequiresHumanReview),
            "Every model-drafted finding requires human review.");
        Assert.IsTrue(stored.FindingDrafts[0].EvidenceRefs.Any(evidenceRef => evidenceRef.Contains(harness.PackageSha256)),
            "Findings are evidence-bound to the reviewed package.");
    }

    [TestMethod]
    public async Task ReviewAsync_PackageMissing_FailsExplicitly_AndRecordsNothing()
    {
        using var harness = CriticHarness.Create(writePackage: false);

        var outcome = await harness.Service.ReviewAsync(Request());

        Assert.IsFalse(outcome!.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "critic package evidence is missing");
        Assert.IsNull(harness.StoredCritic.LastRequest, "Nothing reaches the review store without a package.");
        harness.Events.Single("SkeletonCriticReviewFailed");
    }

    [TestMethod]
    public async Task ReviewAsync_GarbageModelResponse_FailsExplicitly_NeverRecordsAPartialReview()
    {
        using var harness = CriticHarness.Create(llmResponse: () => "I think this code looks pretty good overall!");

        var outcome = await harness.Service.ReviewAsync(Request());

        Assert.IsFalse(outcome!.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "not valid JSON");
        Assert.IsNull(harness.StoredCritic.LastRequest);
        harness.Events.Single("SkeletonCriticReviewFailed");
    }

    [TestMethod]
    public async Task ReviewAsync_ModelSmugglesAnApprovalClaim_TheReviewOnlySurfaceRejectsIt()
    {
        // The model tries to put authority language into a finding. The stored
        // manual-critic validation surface must reject the whole review — the
        // critic's output channel cannot carry approval even by quotation.
        using var harness = CriticHarness.Create(llmResponse: () => """
            {"verdict":"RequestChanges","findings":[{"severity":"Low","title":"Note",
            "problem":"approval granted for this change","whyItMatters":"x","requiredFix":"x","blocksMerge":false}]}
            """);

        var outcome = await harness.Service.ReviewAsync(Request());

        Assert.IsFalse(outcome!.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "review-only validation");
        harness.Events.Single("SkeletonCriticReviewFailed");
        Assert.AreEqual(0, harness.Events.All("SkeletonCriticReviewRecorded").Count,
            "An authority-smuggling review is never recorded.");
    }

    [TestMethod]
    public async Task ReviewAsync_VerdictFindingInconsistency_IsRejectedByTheExistingValidator()
    {
        // NoObjection with a blocking finding is a contradiction the manual-critic
        // validator already refuses — the skeleton path inherits it, not re-implements it.
        using var harness = CriticHarness.Create(llmResponse: () => """
            {"verdict":"NoObjection","findings":[{"severity":"High","title":"Broken",
            "problem":"It fails.","whyItMatters":"It matters.","requiredFix":"Fix it.","blocksMerge":true}]}
            """);

        var outcome = await harness.Service.ReviewAsync(Request());

        Assert.IsFalse(outcome!.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "review-only validation");
    }

    [TestMethod]
    public async Task ReviewAsync_IdentityMismatch_ReturnsNull()
    {
        using var harness = CriticHarness.Create();

        Assert.IsNull(await harness.Service.ReviewAsync(Request() with { TicketId = TicketId + 1 }));
        Assert.IsNull(await harness.Service.ReviewAsync(Request() with { RunId = "no-such-run" }));
    }

    // ── P1-2: trust but verify — mismatches are findings by construction ─────

    [TestMethod]
    public async Task ReviewAsync_BlockingGroundTruthMismatch_OverridesAnAgreeableModel()
    {
        // The model waves the work through; the evidence says the package was
        // tampered with. Evidence wins: the mismatch is a blocking finding and
        // the verdict floor is RecommendBlock, regardless of the model's mood.
        using var harness = CriticHarness.Create(
            llmResponse: () => "{\"verdict\":\"NoObjection\",\"findings\":[]}",
            groundTruth: () => new SkeletonGroundTruthVerification
            {
                Checks =
                [
                    new SkeletonGroundTruthCheck
                    {
                        CheckName = SkeletonCriticGroundTruthVerifier.PackageHashCheck,
                        Passed = false,
                        Expected = "aaaa",
                        Actual = "bbbb",
                        Detail = "The package on disk is NOT the package the run announced at halt — it changed after the fact.",
                        BlocksMerge = true
                    }
                ]
            });

        var outcome = await harness.Service.ReviewAsync(Request());

        Assert.IsTrue(outcome!.Succeeded, outcome.FailureReason);
        Assert.AreEqual("RecommendBlock", outcome.Verdict,
            "A blocking ground-truth mismatch sets the verdict floor — an agreeable model cannot wave through tampered evidence.");
        Assert.IsTrue(outcome.Findings.Any(finding =>
                finding.Title.Contains("Ground truth mismatch", StringComparison.Ordinal) && finding.BlocksMerge),
            "The mismatch becomes a blocking finding by construction, not by judgment.");

        var recorded = harness.Events.Single("SkeletonCriticReviewRecorded");
        Assert.AreEqual("1", recorded.Payload["groundTruthMismatchCount"]);
    }

    [TestMethod]
    public async Task ReviewAsync_NonBlockingMismatch_ForbidsACleanVerdict()
    {
        using var harness = CriticHarness.Create(
            llmResponse: () => "{\"verdict\":\"NoObjection\",\"findings\":[]}",
            groundTruth: () => new SkeletonGroundTruthVerification
            {
                Checks =
                [
                    new SkeletonGroundTruthCheck
                    {
                        CheckName = SkeletonCriticGroundTruthVerifier.ReExecutionCheck,
                        Passed = false,
                        Expected = "an independent re-execution",
                        Actual = "re-execution unavailable: the project's local path is missing",
                        Detail = "The package's claims could not be independently reproduced.",
                        BlocksMerge = false
                    }
                ]
            });

        var outcome = await harness.Service.ReviewAsync(Request());

        Assert.AreEqual("RequestChanges", outcome!.Verdict,
            "Unverifiable claims are weaker evidence: the review cannot come back clean.");
        Assert.IsTrue(outcome.Findings.Any(finding => !finding.BlocksMerge &&
            finding.Problem.Contains("could not be independently reproduced", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ReviewAsync_AllChecksPass_TheModelVerdictStands_AndThePromptCarriesTheGroundTruth()
    {
        using var harness = CriticHarness.Create(llmResponse: () => "{\"verdict\":\"NoObjection\",\"findings\":[]}");

        var outcome = await harness.Service.ReviewAsync(Request());

        Assert.AreEqual("NoObjection", outcome!.Verdict, "Verified claims leave the model's judgment untouched.");
        Assert.AreEqual(4, outcome.GroundTruth!.Checks.Count);
        Assert.AreEqual(0, outcome.GroundTruth.Mismatches.Count);
        Assert.AreEqual("0", harness.Events.Single("SkeletonCriticReviewRecorded").Payload["groundTruthMismatchCount"]);
        StringAssert.Contains(harness.Llm.LastPrompt, "INDEPENDENT GROUND-TRUTH VERIFICATION",
            "The critic model consumes the ground truth as evidence — it reviews with the hood open.");
    }

    // ── P1-2: the verifier itself, against real evidence ─────────────────────

    [TestMethod]
    public async Task Verifier_PackageHash_ComparedToTheHaltAnnouncement()
    {
        var events = new RecordingEventStore();
        await events.PublishAsync(new RunEventDto
        {
            RunId = RunId,
            EventType = "CriticReviewPackageReady",
            Payload = new Dictionary<string, string> { ["packageSha256"] = "aaaa" }
        });
        var verifier = BuildVerifier(events);

        var match = await verifier.VerifyAsync(RunId, MinimalPackage(), "pkg.json", "aaaa");
        Assert.IsTrue(match.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.PackageHashCheck).Passed);

        var tampered = await verifier.VerifyAsync(RunId, MinimalPackage(), "pkg.json", "bbbb");
        var hashCheck = tampered.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.PackageHashCheck);
        Assert.IsFalse(hashCheck.Passed);
        Assert.IsTrue(hashCheck.BlocksMerge, "A package changed after halt is not the package that halted.");
    }

    [TestMethod]
    public async Task Verifier_PackageHash_AfterRevision_ComparesTheCurrentAnnouncement()
    {
        // REVISE-1 / DOGFOOD-2 finding F-I: a green revision re-prepares the
        // package and announces the NEW hash. The verifier must compare against
        // the current announcement — not the superseded pre-revision one, which
        // made every revised run a permanent blocking mismatch.
        var events = new RecordingEventStore();
        await events.PublishAsync(new RunEventDto
        {
            RunId = RunId,
            EventType = "CriticReviewPackageReady",
            Payload = new Dictionary<string, string> { ["packageSha256"] = "aaaa" }
        });
        await events.PublishAsync(new RunEventDto
        {
            RunId = RunId,
            EventType = "CriticReviewPackageReady",
            Payload = new Dictionary<string, string> { ["packageSha256"] = "bbbb" }
        });
        var verifier = BuildVerifier(events);

        var current = await verifier.VerifyAsync(RunId, MinimalPackage(), "pkg.json", "bbbb");
        Assert.IsTrue(current.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.PackageHashCheck).Passed,
            "The revised package matches the CURRENT halt announcement.");

        var superseded = await verifier.VerifyAsync(RunId, MinimalPackage(), "pkg.json", "aaaa");
        var supersededCheck = superseded.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.PackageHashCheck);
        Assert.IsFalse(supersededCheck.Passed,
            "The superseded pre-revision package no longer matches — history is not the gate package.");
    }

    [TestMethod]
    public async Task Verifier_InternalContradictions_AreMismatches()
    {
        var verifier = BuildVerifier(new RecordingEventStore());

        // Claims success while its own recorded command failed.
        var contradictory = MinimalPackage() with
        {
            WorkspaceRunSucceeded = true,
            CommandResults = [new SkeletonCriticPackageCommandResult { DisplayName = "dotnet test", ExitCode = 1 }]
        };
        var verification = await verifier.VerifyAsync(RunId, contradictory, "pkg.json", "h");
        var consistency = verification.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.InternalConsistencyCheck);
        Assert.IsFalse(consistency.Passed);
        Assert.IsTrue(consistency.BlocksMerge, "A package that contradicts itself needs no external evidence to be wrong.");
        StringAssert.Contains(consistency.Actual, "dotnet test");
    }

    [TestMethod]
    public async Task Verifier_MissingClaimedCommandEvidence_IsAMismatch()
    {
        var verifier = BuildVerifier(new RecordingEventStore());
        var package = MinimalPackage() with
        {
            CommandResults =
            [
                new SkeletonCriticPackageCommandResult
                {
                    DisplayName = "dotnet build",
                    ExitCode = 0,
                    StandardOutputRef = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt")
                }
            ]
        };

        var verification = await verifier.VerifyAsync(RunId, package, "pkg.json", "h");

        var evidence = verification.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.CommandEvidenceCheck);
        Assert.IsFalse(evidence.Passed, "A claim whose receipt is missing is a claim, not evidence.");
    }

    [TestMethod]
    public async Task Verifier_HonestCoverageRecord_Passes_EvenWithAnUncoveredCriterion()
    {
        // A coverage HOLE is honest review material for the human gate;
        // only a record that disagrees with recomputation is a forgery.
        var verifier = BuildVerifier(new RecordingEventStore());
        var holed = MinimalPackage() with
        {
            AcceptanceCriteria = "- Catalog sorts by title\n- Catalog paging keeps sort order",
            AuthoredTests = [new SkeletonAuthoredTest { RelativePath = "tests/S.cs", Content = "class S {}", CoversCriterion = "Catalog sorts by title" }]
        };
        holed = holed with { CriterionCoverage = SkeletonCriterionCoverageCalculator.Compute(holed.AcceptanceCriteria, holed.AuthoredTests) };

        var verification = await verifier.VerifyAsync(RunId, holed, "pkg.json", "h");

        var coverage = verification.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.CriterionCoverageCheck);
        Assert.IsTrue(coverage.Passed, coverage.Actual);
    }

    [TestMethod]
    public async Task Verifier_ForgedCoverageRecord_IsABlockingMismatch()
    {
        // The matrix says "covered"; the package's own tests say otherwise.
        // Someone edited the matrix instead of writing the tests.
        var verifier = BuildVerifier(new RecordingEventStore());
        var forged = MinimalPackage() with
        {
            AcceptanceCriteria = "Catalog paging keeps sort order",
            AuthoredTests = [],
            CriterionCoverage =
            [
                new SkeletonCriterionCoverage
                {
                    Criterion = "Catalog paging keeps sort order",
                    Covered = true,
                    CoveringTests = ["tests/Phantom.cs"]
                }
            ]
        };

        var verification = await verifier.VerifyAsync(RunId, forged, "pkg.json", "h");

        var coverage = verification.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.CriterionCoverageCheck);
        Assert.IsFalse(coverage.Passed);
        Assert.IsTrue(coverage.BlocksMerge, "A forged coverage record is tampering, not a judgment call.");
        StringAssert.Contains(coverage.Detail, "edited instead of the tests being written");
    }

    [TestMethod]
    public async Task ReviewAsync_ThePromptShowsUncoveredCriteria_Explicitly()
    {
        using var harness = CriticHarness.Create(packageCriteria: "- Catalog sorts by title ascending\n- Catalog paging keeps sort order");

        await harness.Service.ReviewAsync(Request());

        StringAssert.Contains(harness.Llm.LastPrompt, "UNCOVERED: Catalog paging keeps sort order",
            "The critic reviews the coverage hole by name — absence is evidence.");
        StringAssert.Contains(harness.Llm.LastPrompt, "COVERED: Catalog sorts by title ascending");
    }

    [TestMethod]
    public async Task Verifier_ReExecution_CatchesAClaimThatDoesNotReproduce()
    {
        // The decisive check, against a real sandbox: the package CLAIMS the
        // workspace run succeeded, but its own contents do not compile. The
        // independent re-execution catches the lie.
        using var repo = TempSandboxRepo.Create();
        var verifier = BuildVerifier(new RecordingEventStore(), repo.Path);
        var lyingPackage = MinimalPackage() with
        {
            WorkspaceRunSucceeded = true,
            Changes =
            [
                new SkeletonCriticPackageChange
                {
                    FilePath = "src/Broken.cs",
                    IsNewFile = true,
                    Diff = "+public enum Broken {",
                    FullContentAfter = "public enum Broken {"
                }
            ]
        };

        var verification = await verifier.VerifyAsync(RunId, lyingPackage, "pkg.json", "h");

        var reExecution = verification.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.ReExecutionCheck);
        Assert.IsFalse(reExecution.Passed, "The claim must not survive contact with a fresh workspace.");
        Assert.IsTrue(reExecution.BlocksMerge);
        StringAssert.Contains(reExecution.Detail, "does NOT reproduce");
    }

    [TestMethod]
    public async Task Verifier_ReExecution_ConfirmsAnHonestClaim()
    {
        using var repo = TempSandboxRepo.Create();
        var verifier = BuildVerifier(new RecordingEventStore(), repo.Path);
        var honestPackage = MinimalPackage() with
        {
            WorkspaceRunSucceeded = true,
            Changes =
            [
                new SkeletonCriticPackageChange
                {
                    FilePath = "src/SortOptions.cs",
                    IsNewFile = true,
                    Diff = "+public enum SortOptions { Title }",
                    FullContentAfter = "public enum SortOptions { Title }"
                }
            ],
            AuthoredTests =
            [
                new SkeletonAuthoredTest
                {
                    RelativePath = "tests/skeleton/SortTests.cs",
                    Content = "public class SortTests { }",
                    CoversCriterion = "Catalog sorts by title"
                }
            ]
        };

        var verification = await verifier.VerifyAsync(RunId, honestPackage, "pkg.json", "h");

        var reExecution = verification.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.ReExecutionCheck);
        Assert.IsTrue(reExecution.Passed, $"An honest claim reproduces: {reExecution.Actual}");
    }

    [TestMethod]
    public async Task Verifier_ReExecutionUnavailable_IsANamedNonBlockingMismatch_NeverASilentPass()
    {
        var verifier = BuildVerifier(new RecordingEventStore(), localPath: null);

        var verification = await verifier.VerifyAsync(RunId, MinimalPackage(), "pkg.json", "h");

        var reExecution = verification.Checks.Single(check => check.CheckName == SkeletonCriticGroundTruthVerifier.ReExecutionCheck);
        Assert.IsFalse(reExecution.Passed, "What cannot be verified is not treated as verified.");
        Assert.IsFalse(reExecution.BlocksMerge, "Degraded verifiability informs the human; it does not manufacture a block.");
    }

    // ── P1-3: dispositions — decisions about findings, never approval ────────

    private static SkeletonFindingDispositionService BuildDispositionService(RecordingEventStore events) =>
        new(
            new StubTicketService(new ProjectTicket { Id = TicketId, ProjectId = ProjectId, TenantId = 3, Title = "Add book sorting" }),
            new StubRunStore(new RunRecord { RunId = RunId, ProjectId = ProjectId, TicketId = TicketId, State = RunLifecycleState.PausedForApproval }),
            events);

    private static Task SeedReviewWithFindings(RecordingEventStore events, string findingIds) =>
        events.PublishAsync(new RunEventDto
        {
            RunId = RunId,
            EventType = "SkeletonCriticReviewRecorded",
            Payload = new Dictionary<string, string> { ["findingIds"] = findingIds }
        });

    private static SkeletonFindingDispositionRequest DispositionRequest(
        string findingId = "f-1",
        SkeletonFindingDispositionKind kind = SkeletonFindingDispositionKind.AcceptRisk,
        string reason = "Risk owned: sort locale nuance acceptable for the sandbox catalog.") => new()
    {
        ProjectId = ProjectId,
        TicketId = TicketId,
        RunId = RunId,
        FindingId = findingId,
        Disposition = kind,
        Reason = reason,
        DecidedByUserId = "user-9"
    };

    [TestMethod]
    public async Task Disposition_RecordsADurableDecision_ThatIsNotApproval()
    {
        var events = new RecordingEventStore();
        await SeedReviewWithFindings(events, "f-1,f-2");
        var service = BuildDispositionService(events);

        var outcome = await service.RecordAsync(DispositionRequest("f-1"));

        Assert.IsTrue(outcome!.Succeeded, outcome.FailureReason);
        StringAssert.Contains(outcome.Boundary, "not approval");

        var recorded = events.Single("SkeletonFindingDispositionRecorded");
        Assert.AreEqual("f-1", recorded.Payload["findingId"]);
        Assert.AreEqual("AcceptRisk", recorded.Payload["disposition"]);
        Assert.AreEqual("user-9", recorded.Payload["decidedByUserId"]);
        StringAssert.Contains(recorded.Message, "not approval");
    }

    [TestMethod]
    public async Task Disposition_ForAFindingTheCriticNeverMade_IsRefused()
    {
        var events = new RecordingEventStore();
        await SeedReviewWithFindings(events, "f-1");
        var service = BuildDispositionService(events);

        var outcome = await service.RecordAsync(DispositionRequest("f-invented"));

        Assert.IsFalse(outcome!.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "critic actually made");
        Assert.AreEqual(0, events.All("SkeletonFindingDispositionRecorded").Count);
    }

    [TestMethod]
    public async Task Disposition_WithoutAReason_IsRefused()
    {
        var events = new RecordingEventStore();
        await SeedReviewWithFindings(events, "f-1");
        var service = BuildDispositionService(events);

        var outcome = await service.RecordAsync(DispositionRequest(reason: "  "));

        Assert.IsFalse(outcome!.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "dismissals are not decisions");
    }

    [TestMethod]
    public void DispositionService_HoldsNoApprovalSurface()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "SkeletonFindingDispositionService.cs"));

        foreach (var forbidden in new[]
        {
            "AcceptedApproval",
            "SatisfyPolicy",
            "ApprovalGranted",
            "ControlledSourceApply",
            "TransitionAsync"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"A disposition records a decision about a finding; it cannot approve, transition runs, or touch executors: {forbidden}");
        }

        StringAssert.Contains(source, "not approval");
        StringAssert.Contains(source, "grants nothing");
    }

    // ── Parser ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Parser_ToleratesFences_RejectsUnknownVerdictsAndPartialFindings()
    {
        var fenced = SkeletonCriticReviewService.TryParse(
            "```json\n{\"verdict\":\"NoObjection\",\"findings\":[]}\n```");
        Assert.IsTrue(fenced.Succeeded);
        Assert.AreEqual(CriticReviewVerdict.NoObjection, fenced.Verdict);

        var unknownVerdict = SkeletonCriticReviewService.TryParse("{\"verdict\":\"Approved\",\"findings\":[]}");
        Assert.IsFalse(unknownVerdict.Succeeded, "The critic vocabulary has no 'Approved' — approval is not the critic's word.");

        var partial = SkeletonCriticReviewService.TryParse(
            "{\"verdict\":\"RequestChanges\",\"findings\":[{\"severity\":\"High\",\"title\":\"x\",\"problem\":\"\",\"whyItMatters\":\"y\",\"requiredFix\":\"z\"}]}");
        Assert.IsFalse(partial.Succeeded, "Partial findings are not recorded.");
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private static SkeletonCriticGroundTruthVerifier BuildVerifier(RecordingEventStore events, string? localPath = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DisposableBuild:WorkspaceRoot"] = Path.Combine(Path.GetTempPath(), $"irondev-critic-verify-ws-{Guid.NewGuid():N}"),
                ["DisposableBuild:EvidenceRoot"] = Path.Combine(Path.GetTempPath(), $"irondev-critic-verify-ev-{Guid.NewGuid():N}")
            })
            .Build();

        return new SkeletonCriticGroundTruthVerifier(
            events,
            new StubProjectService(new Project { Id = ProjectId, LocalPath = localPath }),
            new DisposableWorkspaceExecutionService(new LiteRunStore(), new RecordingEventStore()),
            configuration);
    }

    private static SkeletonCriticPackage MinimalPackage() => new()
    {
        PackageId = $"critic-pkg-{RunId}",
        RunId = RunId,
        ProposalId = $"prop-{RunId}",
        TicketId = TicketId,
        ProjectId = ProjectId,
        TicketTitle = "Add book sorting",
        WorkspaceRunSucceeded = true
    };

    private static SkeletonCriticReviewRequest Request() => new()
    {
        ProjectId = ProjectId,
        TicketId = TicketId,
        RunId = RunId,
        RequestedByUserId = "user-9"
    };

    private static SkeletonGroundTruthVerification AllChecksPass() => new()
    {
        Checks =
        [
            new SkeletonGroundTruthCheck { CheckName = SkeletonCriticGroundTruthVerifier.PackageHashCheck, Passed = true, Detail = "The package on disk is byte-identical to the one announced at halt." },
            new SkeletonGroundTruthCheck { CheckName = SkeletonCriticGroundTruthVerifier.InternalConsistencyCheck, Passed = true, Detail = "No contradictions." },
            new SkeletonGroundTruthCheck { CheckName = SkeletonCriticGroundTruthVerifier.CommandEvidenceCheck, Passed = true, Detail = "All present." },
            new SkeletonGroundTruthCheck { CheckName = SkeletonCriticGroundTruthVerifier.ReExecutionCheck, Passed = true, Detail = "Claims reproduce." }
        ]
    };

    private sealed class CriticHarness : IDisposable
    {
        public required SkeletonCriticReviewService Service { get; init; }
        public required RecordingEventStore Events { get; init; }
        public required StubStoredCriticService StoredCritic { get; init; }
        public required StubLlm Llm { get; init; }
        public required string PackageSha256 { get; init; }
        public required string EvidenceRoot { get; init; }

        public static CriticHarness Create(
            Func<string>? llmResponse = null,
            bool writePackage = true,
            Func<SkeletonGroundTruthVerification>? groundTruth = null,
            string packageCriteria = "Catalog sorts by title ascending")
        {
            var evidenceRoot = Path.Combine(Path.GetTempPath(), $"irondev-critic-{Guid.NewGuid():N}");
            var packageHash = string.Empty;

            if (writePackage)
            {
                var package = new SkeletonCriticPackage
                {
                    PackageId = $"critic-pkg-{RunId}",
                    RunId = RunId,
                    ProposalId = $"prop-{RunId}",
                    TicketId = TicketId,
                    ProjectId = ProjectId,
                    TicketTitle = "Add book sorting",
                    AcceptanceCriteria = packageCriteria,
                    ProposalSummary = "Adds a sort option.",
                    Changes =
                    [
                        new SkeletonCriticPackageChange
                        {
                            FilePath = "src/Sort.cs",
                            Diff = "+public enum SortOptions { Title }",
                            FullContentAfter = "public enum SortOptions { Title }",
                            IsNewFile = true
                        }
                    ],
                    AuthoredTests =
                    [
                        new SkeletonAuthoredTest
                        {
                            RelativePath = "tests/skeleton/SortTests.cs",
                            Content = "public class SortTests { }",
                            CoversCriterion = "Catalog sorts by title ascending"
                        }
                    ],
                    WorkspaceRunSucceeded = true
                };
                package = package with
                {
                    CriterionCoverage = SkeletonCriterionCoverageCalculator.Compute(package.AcceptanceCriteria, package.AuthoredTests)
                };

                var packageDir = Path.Combine(evidenceRoot, "runs", RunId, "evidence");
                Directory.CreateDirectory(packageDir);
                var json = JsonSerializer.Serialize(package, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                File.WriteAllText(Path.Combine(packageDir, "critic-package.json"), json);
                packageHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(packageDir, "critic-package.json")))).ToLowerInvariant();
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DisposableBuild:EvidenceRoot"] = evidenceRoot
                })
                .Build();

            var events = new RecordingEventStore();
            var storedCritic = new StubStoredCriticService();
            var llm = new StubLlm(llmResponse ?? (() => "{\"verdict\":\"NoObjection\",\"findings\":[]}"));
            var service = new SkeletonCriticReviewService(
                new StubTicketService(new ProjectTicket { Id = TicketId, ProjectId = ProjectId, TenantId = 3, Title = "Add book sorting" }),
                new StubRunStore(new RunRecord { RunId = RunId, ProjectId = ProjectId, TicketId = TicketId, State = RunLifecycleState.PausedForApproval }),
                events,
                new StubAgentLlmResolver(llm),
                new SkeletonAgentProfileService(configuration),
                storedCritic,
                new StubGroundTruthVerifier(groundTruth ?? AllChecksPass),
                configuration);

            return new CriticHarness
            {
                Service = service,
                Events = events,
                StoredCritic = storedCritic,
                Llm = llm,
                PackageSha256 = packageHash,
                EvidenceRoot = evidenceRoot
            };
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(EvidenceRoot))
                    Directory.Delete(EvidenceRoot, recursive: true);
            }
            catch (IOException)
            {
                // Temp cleanup is best-effort.
            }
        }
    }

    /// <summary>Runs the REAL manual-critic validation chain, so the review-only surface is exercised, not mocked.</summary>
    private sealed class StubStoredCriticService : IStoredManualIndependentCriticAgentService
    {
        public ManualCriticReviewRequest? LastRequest { get; private set; }

        public StoredManualAgentExecutionResult<CriticReviewResult> ExecuteAndStore(
            ManualCriticReviewRequest request,
            ManualAgentExecutionSpecialisationSelection specialisation,
            DateTimeOffset executedAtUtc)
        {
            var result = new ManualIndependentCriticAgentService().Review(request, executedAtUtc);
            if (!result.Succeeded)
            {
                return new StoredManualAgentExecutionResult<CriticReviewResult>
                {
                    Status = StoredManualAgentExecutionStatus.Rejected,
                    AgentRunId = result.ManualCriticRunId,
                    AgentId = "independent-critic",
                    SpecialisationId = specialisation.SpecialisationId,
                    Issues = result.Issues
                        .Select(issue => new StoredManualAgentExecutionIssue
                        {
                            Code = issue.Code,
                            Severity = issue.Severity,
                            Message = issue.Message,
                            Field = issue.Field
                        })
                        .ToList()
                };
            }

            LastRequest = request;
            return new StoredManualAgentExecutionResult<CriticReviewResult>
            {
                Status = StoredManualAgentExecutionStatus.Stored,
                AgentRunId = result.ManualCriticRunId,
                AgentId = "independent-critic",
                SpecialisationId = specialisation.SpecialisationId,
                Output = result.CriticReviewResult,
                AuditEnvelope = result.AuditEnvelope
            };
        }
    }

    private sealed class StubLlm(Func<string> behavior) : ILLMService
    {
        public string? LastPrompt { get; private set; }

        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
        {
            LastPrompt = prompt;
            return Task.FromResult(behavior());
        }
    }

    private sealed class StubAgentLlmResolver(ILLMService llm) : IronDev.Core.Agents.IAgentLlmResolver
    {
        public Task<IronDev.Core.Agents.SkeletonAgentLlm> ResolveAsync(
            IronDev.Core.Agents.SkeletonAgentRole role,
            CancellationToken ct = default) =>
            Task.FromResult(new IronDev.Core.Agents.SkeletonAgentLlm
            {
                Role = role,
                Llm = llm,
                Provider = "fake",
                Model = "stub-model"
            });
    }

    private sealed class StubGroundTruthVerifier(Func<SkeletonGroundTruthVerification> behavior) : ISkeletonCriticGroundTruthVerifier
    {
        public Task<SkeletonGroundTruthVerification> VerifyAsync(
            string runId,
            SkeletonCriticPackage package,
            string packagePath,
            string packageSha256,
            CancellationToken ct = default) => Task.FromResult(behavior());
    }

    private sealed class StubTicketService(ProjectTicket ticket) : ITicketService
    {
        public Task<long> SaveTicketAsync(ProjectTicket toSave, CancellationToken ct = default) => Task.FromResult(toSave.Id);
        public Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProjectTicket>>([ticket]);
        public Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult<ProjectTicket?>(ticketId == ticket.Id ? ticket : null);
        public Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class StubRunStore(RunRecord run) : IRunStore
    {
        public Task<RunRecord> CreateAsync(CreateRunRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("The critic never creates runs.");
        public Task<RunRecord?> GetAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<RunRecord?>(runId == run.RunId ? run : null);
        public Task<IReadOnlyList<RunRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunRecord>>([run]);
        public Task<RunRecord?> TransitionAsync(RunStateTransition transition, CancellationToken ct = default) =>
            throw new NotSupportedException("The critic never transitions runs.");
    }

    private sealed class RecordingEventStore : IRunEventStore
    {
        private readonly List<RunEventDto> _events = [];

        public Task PublishAsync(RunEventDto runEvent, CancellationToken ct = default)
        {
            _events.Add(runEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RunEventDto>> GetEventsAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunEventDto>>(_events.Where(runEvent => runEvent.RunId == runId).ToList());

        public Task<IReadOnlyList<string>> GetRecentRunIdsAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(_events.Select(runEvent => runEvent.RunId).Distinct().Take(limit).ToList());

        public async IAsyncEnumerable<RunEventDto> StreamEventsAsync(
            string runId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var runEvent in _events.Where(candidate => candidate.RunId == runId))
                yield return runEvent;
            await Task.CompletedTask;
        }

        public RunEventDto Single(string eventType)
        {
            var matches = All(eventType);
            Assert.AreEqual(1, matches.Count, $"Expected exactly one '{eventType}' event, found {matches.Count}.");
            return matches[0];
        }

        public IReadOnlyList<RunEventDto> All(string eventType) =>
            _events.Where(runEvent => string.Equals(runEvent.EventType, eventType, StringComparison.Ordinal)).ToList();
    }

    private sealed class StubProjectService(Project project) : IProjectService
    {
        public Task<int> CreateProjectAsync(Project toCreate, CancellationToken ct = default) => Task.FromResult(toCreate.Id);
        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Project>>([project]);
        public Task<Project?> GetByIdAsync(int projectId, CancellationToken ct = default) =>
            Task.FromResult<Project?>(projectId == project.Id ? project : null);
        public Task<Project?> UpdateProjectAsync(int projectId, Project toUpdate, CancellationToken ct = default) =>
            Task.FromResult<Project?>(project);
        public Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class LiteRunStore : IRunStore
    {
        private readonly Dictionary<string, RunRecord> _runs = [];

        public Task<RunRecord> CreateAsync(CreateRunRequest request, CancellationToken ct = default)
        {
            var run = new RunRecord
            {
                RunId = request.RunId ?? Guid.NewGuid().ToString("N"),
                ProjectId = request.ProjectId,
                TicketId = request.TicketId,
                State = RunLifecycleState.Created,
                IsDisposable = request.IsDisposable,
                Summary = request.Summary
            };
            _runs[run.RunId] = run;
            return Task.FromResult(run);
        }

        public Task<RunRecord?> GetAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult(_runs.TryGetValue(runId, out var run) ? run : null);

        public Task<IReadOnlyList<RunRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunRecord>>(_runs.Values.Take(limit).ToList());

        public Task<RunRecord?> TransitionAsync(RunStateTransition transition, CancellationToken ct = default)
        {
            if (!_runs.TryGetValue(transition.RunId, out var run))
                return Task.FromResult<RunRecord?>(null);
            var updated = run with { State = transition.State, Summary = transition.Summary, FailureReason = transition.FailureReason };
            _runs[transition.RunId] = updated;
            return Task.FromResult<RunRecord?>(updated);
        }
    }

    private sealed class TempSandboxRepo : IDisposable
    {
        public required string Path { get; init; }

        public static TempSandboxRepo Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "irondev-critic-repo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            RunTool(path, "git", "init");
            RunTool(path, "git", "config user.email critic@irondev.local");
            RunTool(path, "git", "config user.name Critic");
            File.WriteAllText(System.IO.Path.Combine(path, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <MSBuildProjectExtensionsPath>.assets/</MSBuildProjectExtensionsPath>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(System.IO.Path.Combine(path, "Sandbox.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            Directory.CreateDirectory(System.IO.Path.Combine(path, "src"));
            File.WriteAllText(System.IO.Path.Combine(path, "src", "Existing.cs"), "namespace Sandbox; public static class Existing { }");
            RunTool(path, "dotnet", "restore");
            RunTool(path, "git", "add .");
            RunTool(path, "git", "commit -m initial");
            return new TempSandboxRepo { Path = path };
        }

        private static void RunTool(string workingDirectory, string fileName, string arguments)
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, $"{fileName} {arguments} failed: {process.StandardError.ReadToEnd()}{process.StandardOutput.ReadToEnd()}");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup of temp repos
            }
        }
    }

    private static string RepositoryFile(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "IronDev.slnx")))
            root = Path.GetDirectoryName(root);
        Assert.IsNotNull(root, "Repository root not found.");
        return Path.Combine(root!, Path.Combine(parts));
    }
}
