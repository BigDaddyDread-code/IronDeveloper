using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA05OperationTimelineReadAdapterTests
{
    private const string OperationId = "operation-a05";
    private const string EntryId = "timeline-entry:a05:1";
    private const string EvidenceRef = "timeline-evidence:a05";
    private const string ReceiptRef = "timeline-receipt:a05";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-23T03:00:00Z");

    [TestMethod]
    public void OperationTimelineRepository_ReturnsTimelineByOperationId()
    {
        var result = Repository(Event()).GetByOperationId(OperationId, Scope());

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Timeline);
        Assert.AreEqual(1, result.Timeline.Entries.Count);
    }

    [TestMethod]
    public void OperationTimelineRepository_ReturnsNotFoundForMissingOperationId()
    {
        var result = Repository().GetByOperationId("missing-operation", Scope());

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Timeline);
        AssertContains(result.Issues, "OperationTimelineNotFound");
    }

    [TestMethod]
    public void OperationTimelineRepository_PreservesOperationId()
    {
        var result = Repository(Event()).GetByOperationId(OperationId, Scope());

        Assert.AreEqual(OperationId, result.Timeline!.OperationId);
    }

    [TestMethod]
    public void OperationTimelineRepository_PreservesEntryId()
    {
        var entry = SingleEntry(Repository(Event()).GetByOperationId(OperationId, Scope()));

        Assert.AreEqual(EntryId, entry.EntryId);
    }

    [TestMethod]
    public void OperationTimelineRepository_PreservesEventKind()
    {
        var entry = SingleEntry(Repository(Event(eventKind: "SourceApplyCompleted")).GetByOperationId(OperationId, Scope()));

        Assert.AreEqual("SourceApplyCompleted", entry.EventKind);
    }

    [TestMethod]
    public void OperationTimelineRepository_PreservesSanitizedSummary()
    {
        var entry = SingleEntry(Repository(Event(summary: "Source apply completed for review.")).GetByOperationId(OperationId, Scope()));

        Assert.AreEqual("Source apply completed for review.", entry.Summary);
    }

    [TestMethod]
    public void OperationTimelineRepository_PreservesEvidenceRefs()
    {
        var entry = SingleEntry(Repository(Event()).GetByOperationId(OperationId, Scope()));

        AssertContains(entry.EvidenceRefs, EvidenceRef);
    }

    [TestMethod]
    public void OperationTimelineRepository_PreservesReceiptRefs()
    {
        var entry = SingleEntry(Repository(Event()).GetByOperationId(OperationId, Scope()));

        AssertContains(entry.ReceiptRefs, ReceiptRef);
    }

    [TestMethod]
    public void OperationTimelineRepository_PreservesObservedAtUtc()
    {
        var entry = SingleEntry(Repository(Event()).GetByOperationId(OperationId, Scope()));

        Assert.AreEqual(ObservedAtUtc, entry.ObservedAtUtc);
    }

    [TestMethod]
    public void OperationTimelineRepository_DoesNotReplaceMissingTimestampWithUtcNow()
    {
        var result = Repository(Event(observedAtUtc: DateTimeOffset.MinValue)).GetByOperationId(OperationId, Scope());

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "TimelineEventObservedAtRequired");
        var entry = SingleEntry(result);
        Assert.AreEqual(DateTimeOffset.UnixEpoch, entry.ObservedAtUtc);
        Assert.AreEqual("RedactedTimelineEvent", entry.EventKind);
    }

    [TestMethod]
    public void OperationTimelineRepository_OrdersEventsDeterministically()
    {
        var first = Event(entryId: "timeline-entry:a05:a", observedAtUtc: ObservedAtUtc.AddMinutes(1));
        var second = Event(entryId: "timeline-entry:a05:b", observedAtUtc: ObservedAtUtc);
        var third = Event(entryId: "timeline-entry:a05:c", observedAtUtc: ObservedAtUtc);

        var entries = Repository(first, third, second).GetByOperationId(OperationId, Scope()).Timeline!.Entries.ToArray();

        CollectionAssert.AreEqual(
            new[] { "timeline-entry:a05:b", "timeline-entry:a05:c", "timeline-entry:a05:a" },
            entries.Select(entry => entry.EntryId).ToArray());
    }

    [TestMethod]
    public void OperationTimelineRepository_ForcesReadOnlyBoundary()
    {
        var result = Repository(Event()).GetByOperationId(OperationId, Scope());

        AssertReadOnly(result.Timeline!.Boundary);
        AssertReadOnly(result.Boundary);
    }

    [TestMethod]
    public void OperationTimelineRepository_RawPayloadFailsClosedOrRedacts()
    {
        var result = Repository(Event(containsRawPayload: true)).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventRawPayloadBlocked");
        AssertRedacted(SingleEntry(result));
    }

    [TestMethod]
    public void OperationTimelineRepository_PrivateMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Event(summary: "safe summary", containsPrivateMaterial: true)).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventPrivateMaterialBlocked");
        var entry = SingleEntry(result);
        AssertRedacted(entry);
        Assert.IsFalse(entry.Summary.Contains("safe summary", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void OperationTimelineRepository_HiddenMaterialFailsClosedOrRedacts()
    {
        var result = Repository(Event(containsHiddenMaterial: true)).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventHiddenMaterialBlocked");
        AssertRedacted(SingleEntry(result));
    }

    [TestMethod]
    public void OperationTimelineRepository_RawPatchOrFullDiffFailsClosedOrRedacts()
    {
        var result = Repository(Event(containsPatchPayload: true)).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventPatchPayloadBlocked");
        AssertRedacted(SingleEntry(result));
    }

    [TestMethod]
    public void OperationTimelineRepository_AuthorityClaimFailsClosedOrRedacts()
    {
        var result = Repository(Event(claimsAuthority: true)).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventAuthorityClaimBlocked");
        AssertRedacted(SingleEntry(result));
    }

    [TestMethod]
    public void OperationTimelineRepository_WorkflowContinuationClaimFailsClosedOrRedacts()
    {
        var result = Repository(Event(claimsContinuation: true)).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventContinuationClaimBlocked");
        AssertRedacted(SingleEntry(result));
    }

    [TestMethod]
    public void OperationTimelineRepository_ApprovalClaimFailsClosedOrRedacts()
    {
        var result = Repository(Event(claimsApproval: true)).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventApprovalClaimBlocked");
        AssertRedacted(SingleEntry(result));
    }

    [TestMethod]
    public void OperationTimelineRepository_PolicySatisfactionClaimFailsClosedOrRedacts()
    {
        var result = Repository(Event(claimsPolicySatisfaction: true)).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventPolicySatisfactionClaimBlocked");
        AssertRedacted(SingleEntry(result));
    }

    [TestMethod]
    public void OperationTimelineRepository_ExecutionClaimFailsClosedOrRedacts()
    {
        var result = Repository(Event(claimsExecution: true)).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventExecutionClaimBlocked");
        AssertRedacted(SingleEntry(result));
    }

    [TestMethod]
    public void OperationTimelineRepository_InvalidStoredEventFailsClosed()
    {
        var result = Repository(Event(eventKind: " ")).GetByOperationId(OperationId, Scope());

        AssertContains(result.Issues, "TimelineEventKindRequired");
        AssertRedacted(SingleEntry(result));
    }

    [TestMethod]
    public void OperationTimelineRepository_AllowsMatchingTenant()
    {
        var result = Repository(Event(tenantId: 42)).GetByOperationId(OperationId, Scope(42));

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Timeline);
    }

    [TestMethod]
    public void OperationTimelineRepository_RejectsWrongTenant()
    {
        var result = Repository(Event(tenantId: 42)).GetByOperationId(OperationId, Scope(41));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Timeline);
        AssertContains(result.Issues, "TimelineEventTenantMismatch");
    }

    [TestMethod]
    public void OperationTimelineRepository_RejectsTenantlessTenantScopedEvent()
    {
        var result = Repository(Event(tenantId: null)).GetByOperationId(OperationId, Scope(42));

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Timeline);
        AssertContains(result.Issues, "TenantScopedTimelineEventRecordTenantRequired");
    }

    [TestMethod]
    public void OperationTimelineRepository_RejectsUnscopedTenantRead()
    {
        var result = Repository(Event(tenantId: 42)).GetByOperationId(OperationId, FrontendReadinessReadScope.Unscoped);

        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Timeline);
        AssertContains(result.Issues, "TenantScopedTimelineEventRequiresTenantScope");
    }

    [TestMethod]
    public void OperationTimelineRepository_AllowsExplicitGlobalEvent()
    {
        var result = Repository(Event(tenantId: null, isTenantScoped: false))
            .GetByOperationId(OperationId, FrontendReadinessReadScope.Unscoped);

        Assert.IsTrue(result.Found);
        Assert.IsNotNull(result.Timeline);
    }

    [TestMethod]
    public void OperationTimelineRepository_MixedTenantEventsReturnOnlyVisibleEntries()
    {
        var visible = Event(entryId: "timeline-entry:a05:visible", tenantId: 42);
        var hidden = Event(entryId: "timeline-entry:a05:hidden", tenantId: 41);

        var result = Repository(visible, hidden).GetByOperationId(OperationId, Scope(42));

        Assert.IsTrue(result.Found);
        AssertContains(result.Issues, "TimelineEventTenantMismatch");
        var entry = SingleEntry(result);
        Assert.AreEqual("timeline-entry:a05:visible", entry.EntryId);
    }

    [TestMethod]
    public void FrontendReadiness_OperationTimelineUsesRepositoryBeforeRunReport()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Program.cs"));
        Assert.IsTrue(
            program.IndexOf("OperationTimelineFrontendReadinessBackendTruthSource", StringComparison.Ordinal) <
            program.IndexOf("RunReportFrontendReadinessBackendTruthSource", StringComparison.Ordinal),
            "Operation timeline source must be registered before run reports.");

        var api = Api(
            Repository(Event(eventKind: "CanonicalTimelineEvent")),
            new SeededTimelineBackendTruthSource(Timeline(eventKind: "RunReportTimelineEvent")));

        var model = api.GetOperationTimeline(OperationId);

        Assert.IsNotNull(model);
        Assert.AreEqual("CanonicalTimelineEvent", model.Entries.Single().EventKind);
    }

    [TestMethod]
    public void FrontendReadiness_OperationTimelineDoesNotInventTimelineWhenRepositoryMisses()
    {
        var api = Api(Repository());

        Assert.IsNull(api.GetOperationTimeline(OperationId));
    }

    [TestMethod]
    public void FrontendReadiness_OperationTimelineKeepsReadOnlyBoundary()
    {
        var model = Api().GetOperationTimeline(OperationId)!;

        AssertReadOnly(model.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_OperationTimelineEvidenceRefsRemainReferencesOnly()
    {
        var entry = Api().GetOperationTimeline(OperationId)!.Entries.Single();

        AssertContains(entry.EvidenceRefs, EvidenceRef);
        Assert.IsFalse(string.Join(" ", entry.EvidenceRefs).Contains("approval granted", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FrontendReadiness_OperationTimelineReceiptRefsRemainReferencesOnly()
    {
        var entry = Api().GetOperationTimeline(OperationId)!.Entries.Single();

        AssertContains(entry.ReceiptRefs, ReceiptRef);
        Assert.IsFalse(string.Join(" ", entry.ReceiptRefs).Contains("continue workflow", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FrontendReadiness_OperationTimelinePrivateMaterialRedacted()
    {
        var api = Api(Repository(Event(summary: "private reasoning must stay hidden")));

        var entry = api.GetOperationTimeline(OperationId)!.Entries.Single();

        Assert.AreEqual("[redacted: private material]", entry.Summary);
    }

    [TestMethod]
    public void FrontendReadiness_OperationTimelineDoesNotApproveOrSatisfyPolicy()
    {
        var model = Api().GetOperationTimeline(OperationId)!;

        AssertReadOnly(model.Boundary);
        Assert.IsFalse(model.Boundary.CanCreateApproval);
        Assert.IsFalse(model.Boundary.CanAcceptApproval);
        Assert.IsFalse(model.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void FrontendReadiness_OperationTimelineDoesNotAuthorizeDownstreamMutation()
    {
        var model = Api().GetOperationTimeline(OperationId)!;

        AssertReadOnly(model.Boundary);
        Assert.IsFalse(model.Boundary.CanCommit);
        Assert.IsFalse(model.Boundary.CanPush);
        Assert.IsFalse(model.Boundary.CanCreatePullRequest);
        Assert.IsFalse(model.Boundary.CanMerge);
        Assert.IsFalse(model.Boundary.CanRelease);
        Assert.IsFalse(model.Boundary.CanDeploy);
        Assert.IsFalse(model.Boundary.CanPromoteMemory);
        Assert.IsFalse(model.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void StaticScan_A05AddsNoMutationEndpoint()
    {
        var source = A05Source();

        foreach (var marker in new[]
                 {
                     "[HttpPost",
                     "[HttpPut",
                     "[HttpPatch",
                     "[HttpDelete",
                     "CanCreateApproval = true",
                     "CanSatisfyPolicy = true",
                     "CanExecute = true",
                     "CanMutateSource = true",
                     "CanCommit = true",
                     "CanPush = true",
                     "CanCreatePullRequest = true",
                     "CanMerge = true",
                     "CanRelease = true",
                     "CanDeploy = true",
                     "CanPromoteMemory = true",
                     "CanContinueWorkflow = true",
                     "GrantsAuthority = true",
                     "ContinuesWorkflow = true"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A05AddsNoExecutorOrProviderMutationPath()
    {
        var source = A05Source();

        foreach (var marker in new[]
                 {
                     "CreateApproval",
                     "AcceptApproval",
                     "SatisfyPolicy",
                     "SourceApplyExecutor",
                     "RollbackExecutor",
                     "CommitExecutor",
                     "PushExecutor",
                     "DraftPullRequestExecutor",
                     "MergeExecutor",
                     "ReleaseExecutor",
                     "DeploymentExecutor",
                     "MemoryPromotionExecutor",
                     "WorkflowContinuation",
                     "ContinueWorkflow",
                     "ProcessStartInfo",
                     "RunProcessAsync",
                     "git commit",
                     "git push",
                     "gh pr create"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A05DoesNotReadRawTimelinePayloads()
    {
        var source = A05Source();

        foreach (var marker in new[]
                 {
                     "ReadTimelinePayloadAsync",
                     "ReadEventPayloadAsync",
                     "ReadReceiptTextAsync",
                     "ReadEvidenceTextAsync",
                     "File.ReadAllText",
                     "File.OpenRead",
                     "ReadToEnd",
                     "JsonDocument.Parse",
                     "PatchDiffText",
                     "NormalizedDiff",
                     "rawPrompt",
                     "rawCompletion",
                     "rawToolOutput",
                     "raw patch",
                     "full diff"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A05DoesNotExposePrivateMaterial()
    {
        var source = A05Source();

        foreach (var marker in new[]
                 {
                     "chainOfThought",
                     "chain of thought",
                     "private reasoning",
                     "scratchpad",
                     "bearer token",
                     "api key",
                     "password",
                     "secret",
                     "private key",
                     "raw completion",
                     "raw tool output"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    private static IOperationTimelineReadRepository Repository(params OperationTimelineEventReadRecord[] records) =>
        new OperationTimelineReadRepository(records);

    private static IFrontendReadinessReadApi Api(
        IOperationTimelineReadRepository? repository = null,
        IFrontendReadinessBackendTruthSource? fallback = null)
    {
        var sources = new List<IFrontendReadinessBackendTruthSource>
        {
            new OperationTimelineFrontendReadinessBackendTruthSource(repository ?? Repository(Event()))
        };

        if (fallback is not null)
            sources.Add(fallback);

        return new BackendFrontendReadinessReadApi(sources, new TestTenantContext(42));
    }

    private static OperationTimelineEventReadRecord Event(
        string operationId = OperationId,
        string entryId = EntryId,
        string eventKind = "SourceApplyCompleted",
        string summary = "Source apply completed for review.",
        int? tenantId = 42,
        bool isTenantScoped = true,
        DateTimeOffset? observedAtUtc = null,
        IReadOnlyCollection<string>? evidenceRefs = null,
        IReadOnlyCollection<string>? receiptRefs = null,
        bool containsRawPayload = false,
        bool containsPrivateMaterial = false,
        bool containsPatchPayload = false,
        bool containsHiddenMaterial = false,
        bool claimsAuthority = false,
        bool claimsContinuation = false,
        bool claimsApproval = false,
        bool claimsPolicySatisfaction = false,
        bool claimsExecution = false) =>
        new()
        {
            OperationId = operationId,
            EntryId = entryId,
            EventKind = eventKind,
            Summary = summary,
            TenantId = tenantId,
            IsTenantScoped = isTenantScoped,
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc,
            EvidenceRefs = evidenceRefs ?? [EvidenceRef],
            ReceiptRefs = receiptRefs ?? [ReceiptRef],
            ContainsRawPayload = containsRawPayload,
            ContainsPrivateMaterial = containsPrivateMaterial,
            ContainsPatchPayload = containsPatchPayload,
            ContainsHiddenMaterial = containsHiddenMaterial,
            ClaimsAuthority = claimsAuthority,
            ClaimsContinuation = claimsContinuation,
            ClaimsApproval = claimsApproval,
            ClaimsPolicySatisfaction = claimsPolicySatisfaction,
            ClaimsExecution = claimsExecution
        };

    private static FrontendOperationTimelineReadModel Timeline(string eventKind = "FallbackTimelineEvent") =>
        new()
        {
            OperationId = OperationId,
            Entries =
            [
                new FrontendTimelineEntry
                {
                    EntryId = "fallback-timeline-entry:a05",
                    EventKind = eventKind,
                    Summary = "Fallback timeline event.",
                    EvidenceRefs = ["fallback-evidence:a05"],
                    ReceiptRefs = ["fallback-receipt:a05"],
                    ObservedAtUtc = ObservedAtUtc.AddMinutes(5)
                }
            ],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendTimelineEntry SingleEntry(OperationTimelineReadResult result)
    {
        Assert.IsTrue(result.Found, string.Join(Environment.NewLine, result.Issues));
        Assert.IsNotNull(result.Timeline);
        return result.Timeline.Entries.Single();
    }

    private static void AssertRedacted(FrontendTimelineEntry entry)
    {
        Assert.AreEqual("RedactedTimelineEvent", entry.EventKind);
        Assert.AreEqual("[redacted: timeline event unavailable]", entry.Summary);
        Assert.AreEqual(0, entry.EvidenceRefs.Count);
        Assert.AreEqual(0, entry.ReceiptRefs.Count);
    }

    private static FrontendReadinessReadScope Scope(int tenantId = 42) => new(tenantId);

    private static void AssertReadOnly(FrontendReadBoundary boundary)
    {
        Assert.IsTrue(boundary.ReadOnly);
        Assert.IsTrue(boundary.StatusOnly);
        Assert.IsFalse(boundary.CanCreateApproval);
        Assert.IsFalse(boundary.CanAcceptApproval);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanRollback);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(Environment.NewLine, values));

    private static string A05Source()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationTimelineReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationTimelineReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationTimelineFrontendReadinessBackendTruthSource.cs")));
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

    private sealed class SeededTimelineBackendTruthSource(FrontendOperationTimelineReadModel timeline)
        : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-run-report";

        public override FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId) =>
            string.Equals(operationId, timeline.OperationId, StringComparison.OrdinalIgnoreCase)
                ? timeline
                : null;
    }

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }
}
