using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD04GovernedOperationTimelineReadModelTests
{
    private const string TenantId = "tenant-d04";
    private const string ProjectId = "project-d04";
    private const string OperationId = "op_0000000000000004";
    private const string CorrelationId = "corr_0123456789abcdef";
    private static readonly DateTimeOffset OccurredAtUtc = DateTimeOffset.Parse("2026-06-24T03:00:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T03:05:00Z");
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T02:55:00Z");

    [TestMethod]
    public void ValidTimelineEntry_Passes()
    {
        var result = GovernedOperationTimelineValidator.ValidateEntry(Entry());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(0, result.Issues.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "timeline is read-only");
        AssertContains(result.ForbiddenAuthorityImplications, "displayed event is not permission");
    }

    [TestMethod]
    public void ValidTimelineReadModelWithStatusEvidenceReceiptValidationTimelineAndGovernanceSurfaces_Passes()
    {
        var result = Assemble(
            Entry(GovernedOperationTimelineEventKind.StatusObserved, OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", timelineEventId: "timeline-event-status"),
            Entry(GovernedOperationTimelineEventKind.EvidenceObserved, OperationCorrelationSurfaceKind.EvidenceMetadata, "evidence-metadata-1", timelineEventId: "timeline-event-evidence"),
            Entry(GovernedOperationTimelineEventKind.ReceiptObserved, OperationCorrelationSurfaceKind.ReceiptMetadata, "receipt-metadata-1", timelineEventId: "timeline-event-receipt"),
            Entry(GovernedOperationTimelineEventKind.ValidationObserved, OperationCorrelationSurfaceKind.ValidationResult, "validation-result-1", timelineEventId: "timeline-event-validation"),
            Entry(GovernedOperationTimelineEventKind.RunLinked, OperationCorrelationSurfaceKind.TimelineEvent, "timeline-event-1", timelineEventId: "timeline-event-run"),
            Entry(GovernedOperationTimelineEventKind.AuthorityBoundaryObserved, OperationCorrelationSurfaceKind.GovernanceEvent, "governance-event-1", timelineEventId: "timeline-event-governance"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.IsNotNull(result.ReadModel);
        Assert.AreEqual(6, result.ReadModel.Entries.Count);
    }

    [DataTestMethod]
    [DataRow(null, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineTenantIdRequired")]
    [DataRow("", ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineTenantIdRequired")]
    [DataRow("tenant d04", ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineTenantIdInvalid")]
    [DataRow(TenantId, null, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineProjectIdRequired")]
    [DataRow(TenantId, "", OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineProjectIdRequired")]
    [DataRow(TenantId, "project d04", OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineProjectIdInvalid")]
    [DataRow(TenantId, ProjectId, null, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "OperationIdRequired")]
    [DataRow(TenantId, ProjectId, "run-123", CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow(TenantId, ProjectId, OperationId, null, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineCorrelation:OperationCorrelationIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, "run-123", GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineCorrelation:OperationCorrelationIdCannotLookLikeRunId")]
    [DataRow(TenantId, ProjectId, OperationId, OperationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineCorrelation:OperationCorrelationIdCannotReplaceOperationId")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.Unknown, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineEventKindRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, null, OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineEventIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event 1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineEventIdInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "https://example.test/event", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineEventIdInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "approval granted", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineEventIdInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", null, "Observed event", "Metadata summary", "GovernedOperationTimelineSourceRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "source with space", "Observed event", "Metadata summary", "GovernedOperationTimelineSourceInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.Unknown, "status-1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineSurfaceKindRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, null, "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineSurfaceIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status 1", "d04-source", "Observed event", "Metadata summary", "GovernedOperationTimelineSurfaceIdInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "approval granted", "Metadata summary", "GovernedOperationTimelineDisplayTitleInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "policy satisfied", "GovernedOperationTimelineDisplaySummaryInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "raw evidence payload", "GovernedOperationTimelineDisplaySummaryInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "api key leaked", "GovernedOperationTimelineDisplaySummaryInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "authorization: bearer token", "GovernedOperationTimelineDisplaySummaryInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "private key block", "GovernedOperationTimelineDisplaySummaryInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "chain of thought", "GovernedOperationTimelineDisplaySummaryInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, GovernedOperationTimelineEventKind.StatusObserved, "event-1", OperationCorrelationSurfaceKind.OperationStatus, "status-1", "d04-source", "Observed event", "diff --git a/file b/file", "GovernedOperationTimelineDisplaySummaryInvalid")]
    public void TimelineEntryValidation_FailsClosedForInvalidShape(
        string? tenantId,
        string? projectId,
        string? operationId,
        string? correlationId,
        GovernedOperationTimelineEventKind eventKind,
        string? timelineEventId,
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        string? source,
        string? displayTitle,
        string? displaySummary,
        string expectedIssue)
    {
        var result = GovernedOperationTimelineValidator.ValidateEntry(Entry(
            tenantId: tenantId!,
            projectId: projectId!,
            operationId: operationId!,
            correlationId: correlationId!,
            eventKind: eventKind,
            timelineEventId: timelineEventId!,
            surfaceKind: surfaceKind,
            surfaceId: surfaceId!,
            source: source!,
            displayTitle: displayTitle!,
            displaySummary: displaySummary!));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(true, null, "GovernedOperationTimelineRedactionReasonRequired")]
    [DataRow(true, "", "GovernedOperationTimelineRedactionReasonRequired")]
    [DataRow(true, "approval granted", "GovernedOperationTimelineRedactionReasonInvalid")]
    public void RedactedEntry_RequiresSafeRedactionReason(
        bool isRedacted,
        string? redactionReason,
        string expectedIssue)
    {
        var result = GovernedOperationTimelineValidator.ValidateEntry(Entry(
            isRedacted: isRedacted,
            redactionReason: redactionReason));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void RedactedEntry_RemainsVisibleInReadModel()
    {
        var result = Assemble(Entry(
            timelineEventId: "event-redacted",
            displayTitle: "Redacted metadata",
            displaySummary: "Metadata redacted",
            isRedacted: true,
            redactionReason: "private-material"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        var entry = result.ReadModel!.Entries.Single();
        Assert.IsTrue(entry.IsRedacted);
        Assert.AreEqual("private-material", entry.RedactionReason);
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "GovernedOperationTimelineTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "GovernedOperationTimelineProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000005", "GovernedOperationTimelineOperationMismatch")]
    public void ReadModelValidation_FailsClosedForMismatchedScope(
        string entryTenantId,
        string entryProjectId,
        string entryOperationId,
        string expectedIssue)
    {
        var result = GovernedOperationTimelineValidator.ValidateReadModel(
            TenantId,
            ProjectId,
            OperationId,
            [Entry(tenantId: entryTenantId, projectId: entryProjectId, operationId: entryOperationId)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void ReadModel_AllowsMultipleCorrelationIdsUnderOneOperation()
    {
        var result = Assemble(
            Entry(timelineEventId: "event-corr-a", correlationId: "corr_aaaaaaaaaaaaaaaa", surfaceId: "status-a"),
            Entry(timelineEventId: "event-corr-b", correlationId: "corr_bbbbbbbbbbbbbbbb", surfaceId: "status-b"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(2, result.ReadModel!.Entries.Select(static entry => entry.CorrelationId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void ReadModel_DoesNotAllowCorrelationIdToReplaceOperationId()
    {
        var result = Assemble(Entry(correlationId: OperationId));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "GovernedOperationTimelineEntry:GovernedOperationTimelineCorrelation:OperationCorrelationIdCannotReplaceOperationId");
    }

    [TestMethod]
    public void Entries_SortDeterministicallyByOccurredRecordedEventSurfaceAndSource()
    {
        var late = Entry(timelineEventId: "event-e", surfaceId: "surface-late", occurredAtUtc: OccurredAtUtc.AddMinutes(1));
        var eventC = Entry(timelineEventId: "event-c", surfaceId: "surface-same", source: "source-b");
        var eventB = Entry(timelineEventId: "event-b", surfaceId: "surface-a", source: "source-c");
        var recordedLater = Entry(timelineEventId: "event-a", surfaceId: "surface-z", recordedAtUtc: RecordedAtUtc.AddMinutes(1));
        var first = Entry(timelineEventId: "event-a0", surfaceId: "surface-a", recordedAtUtc: RecordedAtUtc);

        var result = Assemble(late, eventC, eventB, recordedLater, first);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        var actual = result.ReadModel!.Entries.Select(static entry => $"{entry.TimelineEventId}:{entry.SurfaceId}:{entry.Source}").ToArray();
        var expected = new[]
        {
            "event-a0:surface-a:d04-source",
            "event-b:surface-a:source-c",
            "event-c:surface-same:source-b",
            "event-a:surface-z:d04-source",
            "event-e:surface-late:d04-source"
        };

        Assert.IsTrue(
            expected.SequenceEqual(actual),
            $"Expected order {string.Join(", ", expected)}; actual {string.Join(", ", actual)}");
    }

    [TestMethod]
    public void DuplicateTimelineEventIds_AreRejected()
    {
        var result = Assemble(
            Entry(timelineEventId: "event-duplicate", surfaceId: "surface-a"),
            Entry(timelineEventId: "event-duplicate", surfaceId: "surface-b"));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "GovernedOperationTimelineDuplicateEventId");
    }

    [DataTestMethod]
    [DataRow("timeline is not current status")]
    [DataRow("timeline is not blocked-state explanation")]
    [DataRow("timeline is not missing evidence resolution")]
    [DataRow("timeline is not forbidden action resolution")]
    [DataRow("timeline is not validation freshness")]
    [DataRow("timeline is not next-safe-action formatting")]
    [DataRow("timeline is not approval")]
    [DataRow("timeline is not policy satisfaction")]
    [DataRow("timeline is not push")]
    [DataRow("timeline is not pull request creation")]
    [DataRow("timeline is not merge readiness")]
    [DataRow("timeline is not release readiness")]
    [DataRow("timeline is not deployment readiness")]
    [DataRow("timeline is not retry permission")]
    [DataRow("timeline is not rollback")]
    [DataRow("timeline is not workflow continuation")]
    [DataRow("timeline event order is not authority order")]
    public void Timeline_DoesNotInferAuthorityOrStatusTruth(string expectedForbiddenImplication)
    {
        var result = Assemble(
            Entry(GovernedOperationTimelineEventKind.CommitObserved, OperationCorrelationSurfaceKind.CommitPackageReceipt, "commit-receipt-1", timelineEventId: "timeline-event-commit"),
            Entry(GovernedOperationTimelineEventKind.PushObserved, OperationCorrelationSurfaceKind.PushReceipt, "push-receipt-1", timelineEventId: "timeline-event-push"),
            Entry(GovernedOperationTimelineEventKind.PullRequestObserved, OperationCorrelationSurfaceKind.PullRequestReceipt, "pr-receipt-1", timelineEventId: "timeline-event-pr"),
            Entry(GovernedOperationTimelineEventKind.CompletedObserved, OperationCorrelationSurfaceKind.ReceiptMetadata, "completed-receipt-1", timelineEventId: "timeline-event-completed"),
            Entry(GovernedOperationTimelineEventKind.InterruptedObserved, OperationCorrelationSurfaceKind.ReceiptMetadata, "interrupted-receipt-1", timelineEventId: "timeline-event-interrupted"),
            Entry(GovernedOperationTimelineEventKind.RollbackObserved, OperationCorrelationSurfaceKind.SourceApplyReceipt, "rollback-receipt-1", timelineEventId: "timeline-event-rollback"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, expectedForbiddenImplication);
        AssertContains(result.ReadModel!.ForbiddenAuthorityImplications, expectedForbiddenImplication);
    }

    [TestMethod]
    public void TimelineModel_ExposesNoAuthorityProperties()
    {
        foreach (var property in typeof(GovernedOperationTimelineEntry).GetProperties()
            .Concat(typeof(GovernedOperationTimelineReadModel).GetProperties())
            .Concat(typeof(GovernedOperationTimelineAssemblyResult).GetProperties()))
        {
            AssertDoesNotContain(property.Name, "Can");
            AssertDoesNotContain(property.Name, "Approval");
            AssertDoesNotContain(property.Name, "Policy");
            AssertDoesNotContain(property.Name, "Fresh");
            AssertDoesNotContain(property.Name, "NextSafeAction");
            AssertDoesNotContain(property.Name, "Release");
            AssertDoesNotContain(property.Name, "Deploy");
            AssertDoesNotContain(property.Name, "Rollback");
            AssertDoesNotContain(property.Name, "Retry");
            AssertDoesNotContain(property.Name, "Continue");
        }
    }

    [TestMethod]
    public void D01OperationIdentityValidationStillPasses()
    {
        var result = OperationIdentityValidator.ValidateRecord(IdentityRecord());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [TestMethod]
    public void D02LookupValidationStillPasses()
    {
        var result = OperationIdentityLookupValidator.ValidateRequest(new OperationIdentityLookupRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            ReferenceKind = OperationReferenceKind.RunId,
            ReferenceId = "run-123"
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [TestMethod]
    public void D03CorrelationValidationStillPasses()
    {
        var result = OperationCorrelationValidator.ValidateLink(new OperationCorrelationLink
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
            SurfaceId = "status-record-1",
            ObservedAtUtc = OccurredAtUtc,
            Source = "d04-source"
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [TestMethod]
    public void D04CoreFiles_DoNotCallD02LookupResolver()
    {
        AssertDoesNotContain(D04CoreSource(), "OperationIdentityLookupResolver.Resolve");
    }

    [TestMethod]
    public void ExistingA05TimelineReadAdapter_RemainsReadOnly()
    {
        var source = A05TimelineSource();

        foreach (var marker in new[]
        {
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
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void StaticScan_D04CoreFilesAddNoApiSqlUiProjectionResolverExecutorOrMutationSurface()
    {
        var source = D04CoreSource();

        foreach (var marker in new[]
        {
            "Controller",
            "MapGet",
            "Route(",
            "OpenApi",
            "SqlConnection",
            "DbContext",
            "MigrationBuilder",
            "EventStore",
            "Projector",
            "ProjectionStore",
            "OperationIdentityLookupResolver.Resolve",
            "EvidenceResolver",
            "ReceiptResolver",
            "MissingEvidenceResolver",
            "ForbiddenActionResolver",
            "FreshnessResolver",
            "BlockedStateFormatter",
            "NextSafeActionFormatter",
            "AuthorityWarningFormatter",
            "Process.Start",
            "RunProcessAsync",
            "File.Write",
            "HttpClient",
            "SourceApplyExecutor",
            "ControlledCommitExecutor",
            "ControlledPushExecutor",
            "DraftPullRequestGateway",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "PromoteMemory",
            "ContinueWorkflow",
            "AcceptedApproval",
            "PolicySatisfaction"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void Receipt_RecordsTimelineWitnessBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D04_GOVERNED_OPERATION_TIMELINE_READ_MODEL.md"));

        Assert.IsTrue(receipt.Contains(
            "The governed operation timeline is a metadata-only witness of observed operation history. It does not mint identity, perform lookup, project status, resolve evidence, determine blockers, validate freshness, choose next safe action, approve work, satisfy policy, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.",
            StringComparison.Ordinal));
    }

    private static GovernedOperationTimelineAssemblyResult Assemble(params GovernedOperationTimelineEntry[] entries) =>
        GovernedOperationTimelineAssembler.Assemble(TenantId, ProjectId, OperationId, entries);

    private static GovernedOperationTimelineEntry Entry(
        GovernedOperationTimelineEventKind eventKind = GovernedOperationTimelineEventKind.StatusObserved,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
        string surfaceId = "status-record-1",
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string correlationId = CorrelationId,
        string timelineEventId = "timeline-event-1",
        DateTimeOffset? occurredAtUtc = null,
        DateTimeOffset? recordedAtUtc = null,
        string source = "d04-source",
        OperationReferenceKind referenceKind = OperationReferenceKind.RunId,
        string referenceId = "run-123",
        string displayTitle = "Observed event",
        string displaySummary = "Metadata summary",
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            CorrelationId = correlationId,
            TimelineEventId = timelineEventId,
            EventKind = eventKind,
            OccurredAtUtc = occurredAtUtc ?? OccurredAtUtc,
            RecordedAtUtc = recordedAtUtc ?? RecordedAtUtc,
            Source = source,
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
            DisplayTitle = displayTitle,
            DisplaySummary = displaySummary,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static OperationIdentityRecord IdentityRecord() =>
        new()
        {
            OperationId = OperationId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            CreatedAtUtc = CreatedAtUtc,
            CreatedBy = "backend-operation-identity-service",
            LifecycleState = OperationIdentityLifecycleState.LinkedToRun,
            References =
            [
                new OperationIdentityReference
                {
                    ReferenceKind = OperationReferenceKind.RunId,
                    ReferenceId = "run-123",
                    ObservedAtUtc = OccurredAtUtc,
                    Source = "d04-reference-source"
                }
            ],
            CorrelationId = CorrelationId
        };

    private static string D04CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationTimelineModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationTimelineValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationTimelineAssembler.cs")));
    }

    private static string A05TimelineSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationTimelineReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationTimelineReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationTimelineFrontendReadinessBackendTruthSource.cs")));
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(
            values.Contains(expected, StringComparer.Ordinal),
            $"Expected '{expected}' in [{string.Join(", ", values)}].");

    private static void AssertDoesNotContain(string value, string unexpected) =>
        Assert.IsFalse(
            value.Contains(unexpected, StringComparison.Ordinal),
            $"Unexpected marker '{unexpected}' was present.");

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
