using System.Reflection;
using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Agents.Concrete;
using IronDev.Core.Builder;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
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

    private static SkeletonCriticReviewRequest Request() => new()
    {
        ProjectId = ProjectId,
        TicketId = TicketId,
        RunId = RunId,
        RequestedByUserId = "user-9"
    };

    private sealed class CriticHarness : IDisposable
    {
        public required SkeletonCriticReviewService Service { get; init; }
        public required RecordingEventStore Events { get; init; }
        public required StubStoredCriticService StoredCritic { get; init; }
        public required string PackageSha256 { get; init; }
        public required string EvidenceRoot { get; init; }

        public static CriticHarness Create(Func<string>? llmResponse = null, bool writePackage = true)
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
                    AcceptanceCriteria = "Catalog sorts by title ascending",
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
            var service = new SkeletonCriticReviewService(
                new StubTicketService(new ProjectTicket { Id = TicketId, ProjectId = ProjectId, TenantId = 3, Title = "Add book sorting" }),
                new StubRunStore(new RunRecord { RunId = RunId, ProjectId = ProjectId, TicketId = TicketId, State = RunLifecycleState.PausedForApproval }),
                events,
                new StubLlm(llmResponse ?? (() => "{\"verdict\":\"NoObjection\",\"findings\":[]}")),
                storedCritic,
                configuration);

            return new CriticHarness
            {
                Service = service,
                Events = events,
                StoredCritic = storedCritic,
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
        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult(behavior());
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

    private static string RepositoryFile(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "IronDev.slnx")))
            root = Path.GetDirectoryName(root);
        Assert.IsNotNull(root, "Repository root not found.");
        return Path.Combine(root!, Path.Combine(parts));
    }
}
