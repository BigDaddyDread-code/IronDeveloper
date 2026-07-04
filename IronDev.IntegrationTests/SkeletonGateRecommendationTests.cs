using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Workflow;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// P2-6 — risk-tiered gating, recommendation-only. Protected boundaries:
/// - the P1-6 catch-rate is a HARD input: no fresh, verified, clean, fully-armed
///   measurement → no recommendation beyond human judgment;
/// - every check lands in Reasons, pass or fail — advice with unnamed reasons is
///   a hunch;
/// - policy cannot click: the service reads reports, packages, and measurements
///   and can do nothing else (the harness stub throws on every acting verb).
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class SkeletonGateRecommendationTests
{
    private const int ProjectId = 7;
    private const long TicketId = 42;
    private const string RunId = "run-1";

    [TestMethod]
    public async Task ACleanRun_WithAPerfectFreshMeasurement_GetsTheAdvisoryLowTier()
    {
        var harness = GateHarness.Create();

        var recommendation = await harness.Service.RecommendAsync(ProjectId, TicketId, RunId);

        Assert.AreEqual(SkeletonGateRiskTier.Low, recommendation!.Tier);
        Assert.AreEqual(SkeletonGateRecommendationKinds.PolicyWouldApprove, recommendation.Recommendation);
        StringAssert.Contains(recommendation.Recommendation, "advisory-only",
            "The value itself says what it is — nothing downstream can mistake advice for approval.");
        StringAssert.Contains(recommendation.Boundary, "policy cannot click");
        Assert.IsTrue(recommendation.Reasons.All(reason => reason.StartsWith("[pass]")),
            "A Low tier means every named check passed — and each one is on the record.");
        Assert.AreEqual("measure-1", recommendation.MeasurementInput!.MeasurementId,
            "The hard input is shown, so the human can weigh the advice by its evidence.");
    }

    [TestMethod]
    public async Task NoCriticReview_CannotReceiveLowRiskRecommendation()
    {
        var harness = GateHarness.Create();
        harness.Report = harness.Report with
        {
            CriticReviews = [],
            FindingDispositions = []
        };

        var recommendation = await harness.Service.RecommendAsync(ProjectId, TicketId, RunId);

        Assert.IsNotNull(recommendation);
        Assert.AreEqual(SkeletonGateRiskTier.HumanRequired, recommendation.Tier);
        Assert.AreEqual(SkeletonGateRecommendationKinds.HumanJudgmentRequired, recommendation.Recommendation);
        Assert.IsFalse(recommendation.Reasons.All(reason => reason.StartsWith("[pass]")),
            "An empty critic-review set cannot pass every named low-risk check.");
        Assert.IsTrue(recommendation.Reasons.Any(reason => reason.Contains("No critic review", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(recommendation.Reasons.Any(reason => reason.Contains("critic never reviewed", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(recommendation.Reasons.Any(reason =>
                reason.StartsWith("[pass]", StringComparison.OrdinalIgnoreCase)
                && reason.Contains("Every critic finding carries", StringComparison.OrdinalIgnoreCase)),
            "Finding-disposition checks must not pass vacuously when the critic never reviewed the run.");
        Assert.IsFalse(recommendation.Reasons.Any(reason =>
                reason.StartsWith("[pass]", StringComparison.OrdinalIgnoreCase)
                && reason.Contains("No critic review recorded a blocking finding", StringComparison.OrdinalIgnoreCase)),
            "No blocking finding only means something after a critic review exists.");
        StringAssert.Contains(recommendation.Boundary, "policy cannot click");
    }

    [TestMethod]
    public async Task EmptyCriticReviews_AreNotEquivalentToCleanCriticReviews()
    {
        var clean = GateHarness.Create();
        var cleanRecommendation = await clean.Service.RecommendAsync(ProjectId, TicketId, RunId);

        var unreviewed = GateHarness.Create();
        unreviewed.Report = unreviewed.Report with { CriticReviews = [] };
        var unreviewedRecommendation = await unreviewed.Service.RecommendAsync(ProjectId, TicketId, RunId);

        Assert.AreEqual(SkeletonGateRiskTier.Low, cleanRecommendation!.Tier);
        Assert.AreEqual(SkeletonGateRecommendationKinds.PolicyWouldApprove, cleanRecommendation.Recommendation);
        Assert.IsTrue(cleanRecommendation.Reasons.Any(reason =>
            reason.Contains("At least one critic review is recorded", StringComparison.OrdinalIgnoreCase)));

        Assert.AreEqual(SkeletonGateRiskTier.HumanRequired, unreviewedRecommendation!.Tier);
        Assert.AreEqual(SkeletonGateRecommendationKinds.HumanJudgmentRequired, unreviewedRecommendation.Recommendation);
        Assert.IsTrue(unreviewedRecommendation.Reasons.Any(reason =>
            reason.Contains("Policy cannot advise on work the critic never reviewed", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task NoMeasurement_NoRecommendation_EvalEarnsAutonomy()
    {
        var harness = GateHarness.Create(noMeasurement: true);

        var recommendation = await harness.Service.RecommendAsync(ProjectId, TicketId, RunId);

        Assert.AreEqual(SkeletonGateRiskTier.HumanRequired, recommendation!.Tier);
        Assert.IsTrue(recommendation.Reasons.Any(reason => reason.Contains("no eval, no recommendation")),
            "Without a measured net, policy has no basis to advise anything but human judgment.");
        Assert.IsNull(recommendation.MeasurementInput);
    }

    [TestMethod]
    public async Task ADegradedOrStaleOrTamperedMeasurement_CannotUnderwriteARecommendation()
    {
        foreach (var (mutate, expectedReason) in new (Func<SkeletonCanaryMeasurementSummary, SkeletonCanaryMeasurementSummary>, string)[]
        {
            (measurement => measurement with { CatchRate = 0.8 }, "below the required"),
            (measurement => measurement with { ControlClean = false }, "flags everything catches nothing"),
            (measurement => measurement with { ReExecutionAvailable = false }, "WITHOUT re-execution"),
            (measurement => measurement with { Verified = false }, "broken seal advises nothing"),
            (measurement => measurement with { MeasuredAtUtc = DateTimeOffset.UtcNow.AddDays(-3) }, "stale")
        })
        {
            var harness = GateHarness.Create(measurement: mutate(GateHarness.PerfectMeasurement()));
            var recommendation = await harness.Service.RecommendAsync(ProjectId, TicketId, RunId);

            Assert.AreEqual(SkeletonGateRiskTier.HumanRequired, recommendation!.Tier, expectedReason);
            Assert.IsTrue(recommendation.Reasons.Any(reason => reason.Contains(expectedReason)),
                $"The failing measurement precondition is named: {expectedReason}");
        }
    }

    [TestMethod]
    public async Task RunLevelRisk_EachNamedConditionForcesHumanJudgment()
    {
        foreach (var (mutateHarness, expectedReason) in new (Action<GateHarness>, string)[]
        {
            (harness => harness.Report = harness.Report with
            {
                CriticPackage = harness.Report.CriticPackage! with { UncoveredCriterionCount = 1 }
            }, "coverage hole is a human decision"),
            (harness => harness.Report = harness.Report with
            {
                CriticReviews = [GateHarness.Review() with { FindingIds = ["f-1"], FindingCount = 1 }]
            }, "await a human disposition"),
            (harness => harness.Report = harness.Report with
            {
                CriticReviews = [GateHarness.Review() with { BlockingFindingCount = 1 }]
            }, "critic's strongest objection"),
            (harness => harness.Report = harness.Report with
            {
                CriticReviews = [GateHarness.Review() with { GroundTruthMismatchCount = 1 }]
            }, "disagrees with its courier"),
            (harness => harness.Report = harness.Report with
            {
                Gaps = ["Run upstream-1 applied changes... describes a source that no longer exists."]
            }, "stale after an upstream apply"),
            (harness => harness.Report = harness.Report with
            {
                CriticPackage = harness.Report.CriticPackage! with { HashVerified = false }
            }, "does not verify against the halt announcement")
        })
        {
            var harness = GateHarness.Create();
            mutateHarness(harness);

            var recommendation = await harness.Service.RecommendAsync(ProjectId, TicketId, RunId);

            Assert.AreEqual(SkeletonGateRiskTier.HumanRequired, recommendation!.Tier, expectedReason);
            Assert.IsTrue(recommendation.Reasons.Any(reason => reason.Contains(expectedReason)),
                $"The risk is named, never vague: {expectedReason}");
        }
    }

    [TestMethod]
    public async Task AWideOrSensitiveFootprint_IsAlwaysAHumanDecision()
    {
        var wide = GateHarness.Create();
        wide.PackageFootprint = ["src/A.cs", "src/B.cs", "src/C.cs", "src/D.cs", "src/E.cs", "src/F.cs"];
        var wideResult = await wide.Service.RecommendAsync(ProjectId, TicketId, RunId);
        Assert.AreEqual(SkeletonGateRiskTier.HumanRequired, wideResult!.Tier);
        Assert.IsTrue(wideResult.Reasons.Any(reason => reason.Contains("above the low-risk limit")));

        var sensitive = GateHarness.Create();
        sensitive.PackageFootprint = ["Database/migrate_something.sql"];
        var sensitiveResult = await sensitive.Service.RecommendAsync(ProjectId, TicketId, RunId);
        Assert.AreEqual(SkeletonGateRiskTier.HumanRequired, sensitiveResult!.Tier);
        Assert.IsTrue(sensitiveResult.Reasons.Any(reason => reason.Contains("Database/migrate_something.sql")),
            "The sensitive path is named — always a human decision.");
    }

    [TestMethod]
    public async Task UnknownRun_ReturnsNull()
    {
        var harness = GateHarness.Create();

        Assert.IsNull(await harness.Service.RecommendAsync(ProjectId, TicketId, "no-such-run"));
    }

    [TestMethod]
    public void RecommendationService_CannotClick()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "SkeletonGateRecommendationService.cs"));

        foreach (var forbidden in new[]
        {
            "AcceptedApproval",
            "SatisfyPolicy",
            "StartAsync",
            "ContinueAsync",
            "ApplyAsync",
            "TransitionAsync",
            "PublishAsync",
            "RecordAsync",
            "RecordApproval",
            "RequestCriticReview",
            "RunCritic",
            "CreateCriticReview",
            "AutoApprove",
            "AutoContinue",
            "AutoApply",
            "PolicyCanClick",
            "PolicyApproved",
            "ReleaseReady",
            "DeploymentReady"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Policy advises; it can never act: {forbidden}");
        }

        StringAssert.Contains(source, "advice, not approval");
        StringAssert.Contains(source, "policy cannot click");
        StringAssert.Contains(source, "Eval earns");
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed class GateHarness
    {
        public required SkeletonGateRecommendationService Service { get; init; }
        public required StubSkeletonRunReads Reads { get; init; }

        public SkeletonRunReport Report
        {
            get => Reads.Report;
            set => Reads.Report = value;
        }

        public IReadOnlyList<string> PackageFootprint
        {
            set => Reads.Footprint = value;
        }

        public static SkeletonCanaryMeasurementSummary PerfectMeasurement() => new()
        {
            MeasurementId = "measure-1",
            MeasuredAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            CatchRate = 1.0,
            CanaryCount = 5,
            CaughtCount = 5,
            ControlClean = true,
            ReExecutionAvailable = true,
            Verified = true
        };

        public static SkeletonRunCriticReviewTrace Review() => new()
        {
            CriticAgentRunId = "critic-run-1",
            ReviewId = "critic-review-1",
            Verdict = "NoObjection",
            FindingCount = 0,
            BlockingFindingCount = 0,
            FindingIds = [],
            PackageSha256 = "hash",
            GroundTruthCheckCount = 5,
            GroundTruthMismatchCount = 0
        };

        public static GateHarness Create(SkeletonCanaryMeasurementSummary? measurement = null, bool noMeasurement = false)
        {
            var reads = new StubSkeletonRunReads
            {
                Report = new SkeletonRunReport
                {
                    RunId = RunId,
                    ProjectId = ProjectId,
                    TicketId = TicketId,
                    Status = "PausedForApproval",
                    Summary = "Halted for approval.",
                    CriticPackage = new SkeletonRunCriticPackageTrace
                    {
                        PackageId = $"critic-pkg-{RunId}",
                        PackagePath = "evidence/critic-package.json",
                        ExistsOnDisk = true,
                        AnnouncedSha256 = "hash",
                        Sha256OnDisk = "hash",
                        HashVerified = true,
                        CriterionCount = 1,
                        UncoveredCriterionCount = 0
                    },
                    CriticReviews = [Review()],
                    Gaps = [],
                    LoopComplete = false
                },
                Footprint = ["src/SortOptions.cs"]
            };

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var measurements = new StubMeasurements(noMeasurement ? null : measurement ?? PerfectMeasurement());

            return new GateHarness
            {
                Service = new SkeletonGateRecommendationService(reads, measurements, configuration),
                Reads = reads
            };
        }
    }

    /// <summary>Read-only stubs: the report and package the service derives from. Every acting verb throws — policy cannot click.</summary>
    private sealed class StubSkeletonRunReads : ITicketSkeletonRunService
    {
        public required SkeletonRunReport Report { get; set; }
        public IReadOnlyList<string> Footprint { get; set; } = [];

        public Task<SkeletonRunReport?> GetRunReportAsync(int projectId, long ticketId, string runId, CancellationToken ct = default) =>
            Task.FromResult<SkeletonRunReport?>(runId == Report.RunId ? Report : null);

        public Task<SkeletonCriticPackage?> GetCriticPackageAsync(int projectId, long ticketId, string runId, CancellationToken ct = default) =>
            Task.FromResult<SkeletonCriticPackage?>(runId != Report.RunId ? null : new SkeletonCriticPackage
            {
                PackageId = $"critic-pkg-{runId}",
                RunId = runId,
                ProposalId = $"prop-{runId}",
                TicketId = TicketId,
                ProjectId = ProjectId,
                TicketTitle = "Add book sorting",
                Changes = Footprint
                    .Select(path => new SkeletonCriticPackageChange { FilePath = path, FullContentAfter = "x", IsNewFile = true })
                    .ToList()
            });

        public Task<TicketBuildRunDto?> StartAsync(int projectId, long ticketId, CancellationToken ct = default) =>
            throw new NotSupportedException("Policy cannot start runs.");

        public Task<TicketBuildRunDto?> ContinueAsync(int projectId, long ticketId, string runId, CancellationToken ct = default) =>
            throw new NotSupportedException("Policy cannot continue runs — policy cannot click.");

        public Task<TicketBuildRunDto?> ApplyAsync(int projectId, long ticketId, string runId, CancellationToken ct = default) =>
            throw new NotSupportedException("Policy cannot apply runs — policy cannot click.");
    }

    private sealed class StubMeasurements(SkeletonCanaryMeasurementSummary? latest) : ISkeletonCanaryMeasurementService
    {
        public Task<SkeletonCanaryMeasurement> MeasureAsync(string requestedByUserId, CancellationToken ct = default) =>
            throw new NotSupportedException("Policy reads measurements; it never runs them.");

        public Task<SkeletonCanaryMeasurementRecord?> GetAsync(string measurementId, CancellationToken ct = default) =>
            throw new NotSupportedException("The recommendation uses the latest summary only.");

        public Task<IReadOnlyList<SkeletonCanaryMeasurementSummary>> ListAsync(int take = 20, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SkeletonCanaryMeasurementSummary>>(latest is null ? [] : [latest]);
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
