using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD06StatusProjectionRebuildTests
{
    private const string TenantId = "tenant-d06";
    private const string ProjectId = "project-d06";
    private const string OperationId = "op_0000000000000006";
    private const string CorrelationId = "corr_0123456789abcdef";
    private const string ProjectionVersion = "projection-v1";
    private static readonly DateTimeOffset OccurredAtUtc = DateTimeOffset.Parse("2026-06-24T05:00:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T05:05:00Z");
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T04:55:00Z");

    [TestMethod]
    public void EmptyValidStream_RebuildsToNoEvents()
    {
        var result = Project([]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.NoEvents, result.ProjectedStatus!.ProjectedStatusKind);
        Assert.IsNull(result.ProjectedStatus.LastStatusChangingEventId);
        Assert.AreEqual(0, result.ProjectedStatus.SourceEventIds.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "projected status is display truth only");
    }

    [TestMethod]
    public void OrderedStream_RebuildsToExpectedFinalDisplayStatus()
    {
        var result = Project(OrderedStream());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.PullRequestObserved, result.ProjectedStatus!.ProjectedStatusKind);
        Assert.AreEqual("projection-event-pr", result.ProjectedStatus.LastStatusChangingEventId);
        Assert.AreEqual(OperationStatusProjectionEventKind.PullRequestObserved, result.ProjectedStatus.LastStatusChangingEventKind);
        CollectionAssert.AreEqual(OrderedEventIds(), result.ProjectedStatus.SourceEventIds.ToArray());
    }

    [TestMethod]
    public void ShuffledStream_RebuildsIdenticallyToOrderedStream()
    {
        var ordered = Project(OrderedStream());
        var shuffled = Project(ShuffledStream());

        AssertProjectedStatusEquivalent(ordered, shuffled);
    }

    [TestMethod]
    public void RepeatedRebuildOverOrderedStream_IsIdentical()
    {
        var events = OrderedStream();
        var first = Project(events);
        var second = Project(events);

        AssertProjectedStatusEquivalent(first, second);
    }

    [TestMethod]
    public void RepeatedRebuildOverShuffledStream_IsIdentical()
    {
        var events = ShuffledStream();
        var first = Project(events);
        var second = Project(events);

        AssertProjectedStatusEquivalent(first, second);
    }

    [DataTestMethod]
    [DataRow(OperationStatusProjectionEventKind.EvidenceObserved)]
    [DataRow(OperationStatusProjectionEventKind.ReceiptObserved)]
    [DataRow(OperationStatusProjectionEventKind.ValidationObserved)]
    [DataRow(OperationStatusProjectionEventKind.AuthorityBoundaryObserved)]
    public void MetadataOnlyEvents_DoNotChangeProjectedStatus(OperationStatusProjectionEventKind metadataKind)
    {
        var result = Project(
            Event(OperationStatusProjectionEventKind.RunStarted, "projection-event-run", 0, surfaceId: "surface-run"),
            Event(metadataKind, "projection-event-metadata", 1, surfaceId: "surface-metadata"),
            Event(OperationStatusProjectionEventKind.CommitObserved, "projection-event-commit", 2, surfaceId: "surface-commit"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.CommitObserved, result.ProjectedStatus!.ProjectedStatusKind);
        Assert.AreEqual("projection-event-commit", result.ProjectedStatus.LastStatusChangingEventId);
        CollectionAssert.AreEqual(
            new[] { "projection-event-run", "projection-event-metadata", "projection-event-commit" },
            result.ProjectedStatus.SourceEventIds.ToArray());
    }

    [TestMethod]
    public void MetadataOnlyEvents_RemainInSourceEventIds()
    {
        var result = Project(StreamWithMetadataEvents());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        CollectionAssert.AreEqual(
            new[]
            {
                "projection-event-minted",
                "projection-event-evidence",
                "projection-event-receipt",
                "projection-event-validation",
                "projection-event-boundary",
                "projection-event-pr"
            },
            result.ProjectedStatus!.SourceEventIds.ToArray());
        Assert.AreEqual(OperationProjectedStatusKind.PullRequestObserved, result.ProjectedStatus.ProjectedStatusKind);
    }

    [TestMethod]
    public void ProjectionUsesAppendPositionAsPrimaryOrder()
    {
        var result = Project(
            Event(OperationStatusProjectionEventKind.CompletedObserved, "projection-event-completed", 2, occurredAtUtc: OccurredAtUtc.AddMinutes(-30)),
            Event(OperationStatusProjectionEventKind.RunStarted, "projection-event-run", 0, occurredAtUtc: OccurredAtUtc.AddMinutes(30)),
            Event(OperationStatusProjectionEventKind.PushObserved, "projection-event-push", 1, occurredAtUtc: OccurredAtUtc.AddMinutes(-10)));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.CompletedObserved, result.ProjectedStatus!.ProjectedStatusKind);
        CollectionAssert.AreEqual(
            new[] { "projection-event-run", "projection-event-push", "projection-event-completed" },
            result.ProjectedStatus.SourceEventIds.ToArray());
    }

    [TestMethod]
    public void OccurredTimeCannotOverrideAppendOrder()
    {
        var result = Project(
            Event(OperationStatusProjectionEventKind.PullRequestObserved, "projection-event-pr", 1, occurredAtUtc: OccurredAtUtc.AddMinutes(-60)),
            Event(OperationStatusProjectionEventKind.OperationMinted, "projection-event-minted", 0, occurredAtUtc: OccurredAtUtc.AddMinutes(60)));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.PullRequestObserved, result.ProjectedStatus!.ProjectedStatusKind);
    }

    [TestMethod]
    public void RecordedTimeCannotRescueDuplicateAppendPositions()
    {
        var result = Project(
            Event(OperationStatusProjectionEventKind.RunStarted, "projection-event-run", 0, recordedAtUtc: RecordedAtUtc.AddMinutes(1)),
            Event(OperationStatusProjectionEventKind.PushObserved, "projection-event-push", 0, recordedAtUtc: RecordedAtUtc.AddMinutes(2), surfaceId: "surface-push"));

        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.ProjectedStatus);
        AssertContains(result.Issues, "OperationStatusProjectionDuplicateAppendPosition");
    }

    [DataTestMethod]
    [DataRow("duplicate-event-id")]
    [DataRow("duplicate-append-position")]
    [DataRow("negative-append-position")]
    [DataRow("mismatched-tenant")]
    [DataRow("mismatched-project")]
    [DataRow("mismatched-operation")]
    [DataRow("invalid-operation")]
    [DataRow("invalid-correlation")]
    [DataRow("missing-projection-version")]
    [DataRow("missing-source")]
    [DataRow("unsafe-source")]
    [DataRow("unsafe-surface-id")]
    [DataRow("unsafe-reference-id")]
    public void InvalidStreams_FailClosedAndDoNotProduceProjectedStatus(string scenario)
    {
        var result = ProjectInvalidScenario(scenario);

        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.ProjectedStatus);
        Assert.IsTrue(result.Issues.Count > 0);
    }

    [DataTestMethod]
    [DataRow("duplicate-event-id", "OperationStatusProjectionDuplicateEventId")]
    [DataRow("duplicate-append-position", "OperationStatusProjectionDuplicateAppendPosition")]
    [DataRow("negative-append-position", "OperationStatusProjectionEvent:OperationStatusProjectionAppendPositionInvalid")]
    [DataRow("mismatched-tenant", "OperationStatusProjectionTenantMismatch")]
    [DataRow("mismatched-project", "OperationStatusProjectionProjectMismatch")]
    [DataRow("mismatched-operation", "OperationStatusProjectionOperationMismatch")]
    [DataRow("invalid-operation", "OperationStatusProjectionEvent:OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow("invalid-correlation", "OperationStatusProjectionEvent:OperationStatusProjectionCorrelation:OperationCorrelationIdCannotLookLikeRunId")]
    [DataRow("missing-projection-version", "OperationStatusProjectionVersionRequired")]
    [DataRow("missing-source", "OperationStatusProjectionEvent:OperationStatusProjectionSourceRequired")]
    [DataRow("unsafe-source", "OperationStatusProjectionEvent:OperationStatusProjectionSourceInvalid")]
    [DataRow("unsafe-surface-id", "OperationStatusProjectionEvent:OperationStatusProjectionSurfaceIdInvalid")]
    [DataRow("unsafe-reference-id", "OperationStatusProjectionEvent:OperationStatusProjectionReferenceIdInvalid")]
    public void InvalidStreams_ReportExpectedIssues(string scenario, string expectedIssue)
    {
        var result = ProjectInvalidScenario(scenario);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(OperationStatusProjectionEventKind.CompletedObserved, OperationProjectedStatusKind.CompletedObserved, "projection is not release readiness")]
    [DataRow(OperationStatusProjectionEventKind.InterruptedObserved, OperationProjectedStatusKind.InterruptedObserved, "projection is not retry permission")]
    [DataRow(OperationStatusProjectionEventKind.RollbackObserved, OperationProjectedStatusKind.RollbackObserved, "projection is not rollback")]
    [DataRow(OperationStatusProjectionEventKind.RecoveryObserved, OperationProjectedStatusKind.RecoveryObserved, "projection is not workflow continuation")]
    [DataRow(OperationStatusProjectionEventKind.PullRequestObserved, OperationProjectedStatusKind.PullRequestObserved, "projection is not merge readiness")]
    [DataRow(OperationStatusProjectionEventKind.CommitObserved, OperationProjectedStatusKind.CommitObserved, "projection is not push")]
    [DataRow(OperationStatusProjectionEventKind.PushObserved, OperationProjectedStatusKind.PushObserved, "projection is not PR creation")]
    public void TerminalOrDownstreamLookingStatuses_DoNotImplyAuthority(
        OperationStatusProjectionEventKind eventKind,
        OperationProjectedStatusKind expectedStatus,
        string expectedForbiddenImplication)
    {
        var result = Project(Event(eventKind, "projection-event-terminal", 0));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(expectedStatus, result.ProjectedStatus!.ProjectedStatusKind);
        AssertContains(result.ProjectedStatus.ForbiddenAuthorityImplications, expectedForbiddenImplication);
    }

    [TestMethod]
    public void ProjectionWarnings_IncludeDisplayStatusOnlyWarning()
    {
        var result = Project(OrderedStream());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ProjectedStatus!.Warnings, "projection is display status only");
    }

    [TestMethod]
    public void ProjectionForbiddenAuthorityImplications_ArePresentOnValidResult()
    {
        var result = Project(OrderedStream());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "projected status is not permission");
        AssertContains(result.ProjectedStatus!.ForbiddenAuthorityImplications, "projected status is not permission");
    }

    [TestMethod]
    public void ProjectionForbiddenAuthorityImplications_ArePresentOnInvalidResult()
    {
        var result = ProjectInvalidScenario("unsafe-source");

        Assert.IsFalse(result.IsValid);
        AssertContains(result.ForbiddenAuthorityImplications, "projected status is display truth only");
    }

    [TestMethod]
    public void ProjectionResult_ExposesNoAuthorityProperties()
    {
        foreach (var property in typeof(OperationProjectedStatus).GetProperties()
            .Concat(typeof(OperationStatusProjectionResult).GetProperties()))
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
            AssertDoesNotContain(property.Name, "AuthorityGranted");
        }
    }

    [TestMethod]
    public void Projector_DoesNotMutateInputEventList()
    {
        var events = ShuffledStream().ToList();
        var expectedOrder = events.Select(static projectionEvent => projectionEvent.ProjectionEventId).ToArray();

        _ = Project(events.ToArray());

        CollectionAssert.AreEqual(expectedOrder, events.Select(static projectionEvent => projectionEvent.ProjectionEventId).ToArray());
    }

    [TestMethod]
    public void Projector_DoesNotMutateInputEventObjects()
    {
        var events = StreamWithMetadataEvents().ToList();
        var snapshots = events.Select(EventSnapshot.From).ToArray();

        var first = Project(events.ToArray());
        var second = Project(events.ToArray());

        CollectionAssert.AreEqual(snapshots, events.Select(EventSnapshot.From).ToArray());
        AssertProjectedStatusEquivalent(first, second);
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
            Source = "d06-source"
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [TestMethod]
    public void D04TimelineValidationStillPasses()
    {
        var result = GovernedOperationTimelineValidator.ValidateEntry(new GovernedOperationTimelineEntry
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            TimelineEventId = "timeline-event-d06",
            EventKind = GovernedOperationTimelineEventKind.StatusObserved,
            OccurredAtUtc = OccurredAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "d06-source",
            SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
            SurfaceId = "status-record-1",
            ReferenceKind = OperationReferenceKind.RunId,
            ReferenceId = "run-123",
            DisplayTitle = "Observed event",
            DisplaySummary = "Metadata summary"
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [TestMethod]
    public void D05FocusedProjectionBehaviorStillPasses()
    {
        var result = Project(Event(OperationStatusProjectionEventKind.CompletedObserved, "projection-event-completed", 0));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.CompletedObserved, result.ProjectedStatus!.ProjectedStatusKind);
        AssertContains(result.ProjectedStatus.ForbiddenAuthorityImplications, "projection is not release readiness");
    }

    [TestMethod]
    public void ExistingA02StatusReadAdapter_RemainsReadOnly()
    {
        var source = A02StatusSource();

        foreach (var marker in ReadOnlyAuthorityMarkers())
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void ExistingA05TimelineReadAdapter_RemainsReadOnly()
    {
        var source = A05TimelineSource();

        foreach (var marker in ReadOnlyAuthorityMarkers())
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void StaticScan_D06AddsNoProductionRebuildCode()
    {
        var root = FindRepositoryRoot();
        var d06ProductionFiles = Directory.EnumerateFiles(Path.Combine(root, "IronDev.Core"), "*D06*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "IronDev.Core"), "*Rebuild*.cs", SearchOption.AllDirectories))
            .Where(static path => !path.EndsWith("BlockD06StatusProjectionRebuildTests.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(0, d06ProductionFiles.Length, $"Unexpected D06 production files: {string.Join(", ", d06ProductionFiles)}");
    }

    [TestMethod]
    public void StaticScan_D05ProductionProjectionStillHasNoApiSqlUiStoreExecutorOrMutationSurface()
    {
        var source = D05CoreSource();

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
            "ProjectionStore",
            "Repository",
            "SaveChanges",
            ".Save",
            ".Update",
            ".Delete",
            ".Remove",
            "Compact",
            "OperationIdentityLookupResolver.Resolve",
            "GovernedOperationTimelineAssembler.Assemble",
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
    public void Receipt_RecordsRebuildIsNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D06_STATUS_PROJECTION_REBUILD_TEST.md"));

        Assert.IsTrue(receipt.Contains(
            "Status projection rebuild proof shows deterministic display status can be rebuilt from append-only events. It does not mint identity, perform lookup, assemble timelines, resolve evidence, determine blockers, validate freshness, choose next safe action, approve work, satisfy policy, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.",
            StringComparison.Ordinal));
    }

    private static OperationStatusProjectionResult Project(params OperationStatusProjectionEvent[] events) =>
        OperationStatusProjector.Project(new OperationStatusProjectionRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            ProjectionVersion = ProjectionVersion,
            Events = events
        });

    private static OperationStatusProjectionResult ProjectInvalidScenario(string scenario) =>
        scenario switch
        {
            "duplicate-event-id" => Project(
            [
                Event(OperationStatusProjectionEventKind.RunStarted, "projection-event-duplicate", 0, surfaceId: "surface-a"),
                Event(OperationStatusProjectionEventKind.PushObserved, "projection-event-duplicate", 1, surfaceId: "surface-b")
            ]),
            "duplicate-append-position" => Project(
            [
                Event(OperationStatusProjectionEventKind.RunStarted, "projection-event-run", 0, surfaceId: "surface-a"),
                Event(OperationStatusProjectionEventKind.PushObserved, "projection-event-push", 0, surfaceId: "surface-b")
            ]),
            "negative-append-position" => Project([Event(appendPosition: -1)]),
            "mismatched-tenant" => Project([Event(tenantId: "tenant-other")]),
            "mismatched-project" => Project([Event(projectId: "project-other")]),
            "mismatched-operation" => Project([Event(operationId: "op_0000000000000007")]),
            "invalid-operation" => Project([Event(operationId: "run-123")]),
            "invalid-correlation" => Project([Event(correlationId: "run-123")]),
            "missing-projection-version" => OperationStatusProjector.Project(new OperationStatusProjectionRequest
            {
                TenantId = TenantId,
                ProjectId = ProjectId,
                OperationId = OperationId,
                ProjectionVersion = "",
                Events = [Event()]
            }),
            "missing-source" => Project([Event(source: null!)]),
            "unsafe-source" => Project([Event(source: "approval granted")]),
            "unsafe-surface-id" => Project([Event(surfaceId: "surface with space")]),
            "unsafe-reference-id" => Project([Event(referenceId: "reference with space")]),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown invalid scenario.")
        };

    private static OperationStatusProjectionEvent[] OrderedStream() =>
    [
        Event(OperationStatusProjectionEventKind.OperationMinted, "projection-event-minted", 0, surfaceId: "surface-minted"),
        Event(OperationStatusProjectionEventKind.RunStarted, "projection-event-run", 1, surfaceId: "surface-run"),
        Event(OperationStatusProjectionEventKind.PatchArtifactCreated, "projection-event-patch", 2, surfaceId: "surface-patch"),
        Event(OperationStatusProjectionEventKind.CommitObserved, "projection-event-commit", 3, surfaceId: "surface-commit"),
        Event(OperationStatusProjectionEventKind.PushObserved, "projection-event-push", 4, surfaceId: "surface-push"),
        Event(OperationStatusProjectionEventKind.PullRequestObserved, "projection-event-pr", 5, surfaceId: "surface-pr")
    ];

    private static OperationStatusProjectionEvent[] ShuffledStream()
    {
        var ordered = OrderedStream();
        return [ordered[3], ordered[0], ordered[5], ordered[1], ordered[4], ordered[2]];
    }

    private static OperationStatusProjectionEvent[] StreamWithMetadataEvents() =>
    [
        Event(OperationStatusProjectionEventKind.OperationMinted, "projection-event-minted", 0, surfaceId: "surface-minted"),
        Event(OperationStatusProjectionEventKind.EvidenceObserved, "projection-event-evidence", 1, surfaceId: "surface-evidence", referenceKind: OperationReferenceKind.EvidenceId, referenceId: "evidence-123"),
        Event(OperationStatusProjectionEventKind.ReceiptObserved, "projection-event-receipt", 2, surfaceId: "surface-receipt", referenceKind: OperationReferenceKind.ReceiptId, referenceId: "receipt-123"),
        Event(OperationStatusProjectionEventKind.ValidationObserved, "projection-event-validation", 3, surfaceId: "surface-validation", referenceKind: OperationReferenceKind.EvidenceId, referenceId: "validation-123"),
        Event(OperationStatusProjectionEventKind.AuthorityBoundaryObserved, "projection-event-boundary", 4, surfaceId: "surface-boundary", referenceKind: OperationReferenceKind.EvidenceId, referenceId: "boundary-123"),
        Event(OperationStatusProjectionEventKind.PullRequestObserved, "projection-event-pr", 5, surfaceId: "surface-pr")
    ];

    private static string[] OrderedEventIds() =>
    [
        "projection-event-minted",
        "projection-event-run",
        "projection-event-patch",
        "projection-event-commit",
        "projection-event-push",
        "projection-event-pr"
    ];

    private static OperationStatusProjectionEvent Event(
        OperationStatusProjectionEventKind eventKind = OperationStatusProjectionEventKind.RunStarted,
        string projectionEventId = "projection-event-1",
        long appendPosition = 0,
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string correlationId = CorrelationId,
        DateTimeOffset? occurredAtUtc = null,
        DateTimeOffset? recordedAtUtc = null,
        string source = "d06-source",
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
        string surfaceId = "status-record-1",
        OperationReferenceKind referenceKind = OperationReferenceKind.RunId,
        string referenceId = "run-123") =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            CorrelationId = correlationId,
            ProjectionEventId = projectionEventId,
            AppendPosition = appendPosition,
            EventKind = eventKind,
            OccurredAtUtc = occurredAtUtc ?? OccurredAtUtc.AddMinutes(appendPosition),
            RecordedAtUtc = recordedAtUtc ?? RecordedAtUtc.AddMinutes(appendPosition),
            Source = source,
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId
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
                    Source = "d06-reference-source"
                }
            ],
            CorrelationId = CorrelationId
        };

    private static void AssertProjectedStatusEquivalent(
        OperationStatusProjectionResult expected,
        OperationStatusProjectionResult actual)
    {
        Assert.AreEqual(expected.IsValid, actual.IsValid);
        CollectionAssert.AreEqual(expected.Issues.ToArray(), actual.Issues.ToArray());
        CollectionAssert.AreEqual(expected.ForbiddenAuthorityImplications.ToArray(), actual.ForbiddenAuthorityImplications.ToArray());

        Assert.IsNotNull(expected.ProjectedStatus);
        Assert.IsNotNull(actual.ProjectedStatus);
        Assert.AreEqual(expected.ProjectedStatus!.ProjectedStatusKind, actual.ProjectedStatus!.ProjectedStatusKind);
        Assert.AreEqual(expected.ProjectedStatus.LastStatusChangingEventId, actual.ProjectedStatus.LastStatusChangingEventId);
        Assert.AreEqual(expected.ProjectedStatus.LastStatusChangingEventKind, actual.ProjectedStatus.LastStatusChangingEventKind);
        Assert.AreEqual(expected.ProjectedStatus.LastStatusChangedAtUtc, actual.ProjectedStatus.LastStatusChangedAtUtc);
        Assert.AreEqual(expected.ProjectedStatus.LastRecordedAtUtc, actual.ProjectedStatus.LastRecordedAtUtc);
        CollectionAssert.AreEqual(expected.ProjectedStatus.SourceEventIds.ToArray(), actual.ProjectedStatus.SourceEventIds.ToArray());
        CollectionAssert.AreEqual(expected.ProjectedStatus.Warnings.ToArray(), actual.ProjectedStatus.Warnings.ToArray());
        CollectionAssert.AreEqual(expected.ProjectedStatus.ForbiddenAuthorityImplications.ToArray(), actual.ProjectedStatus.ForbiddenAuthorityImplications.ToArray());
    }

    private static IReadOnlyList<string> ReadOnlyAuthorityMarkers() =>
    [
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
        "CanContinueWorkflow = true"
    ];

    private static string D05CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationStatusProjectionModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationStatusProjectionValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationStatusProjector.cs")));
    }

    private static string A02StatusSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedOperationStatusReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "GovernedOperationStatusReadRepository.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "OperationStatusFrontendReadinessBackendTruthSource.cs")));
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

    private sealed record EventSnapshot(
        string TenantId,
        string ProjectId,
        string OperationId,
        string CorrelationId,
        string ProjectionEventId,
        long AppendPosition,
        OperationStatusProjectionEventKind EventKind,
        DateTimeOffset OccurredAtUtc,
        DateTimeOffset RecordedAtUtc,
        string Source,
        OperationCorrelationSurfaceKind SurfaceKind,
        string SurfaceId,
        OperationReferenceKind ReferenceKind,
        string ReferenceId,
        bool IsRedacted,
        string? RedactionReason)
    {
        public static EventSnapshot From(OperationStatusProjectionEvent projectionEvent) =>
            new(
                projectionEvent.TenantId,
                projectionEvent.ProjectId,
                projectionEvent.OperationId,
                projectionEvent.CorrelationId,
                projectionEvent.ProjectionEventId,
                projectionEvent.AppendPosition,
                projectionEvent.EventKind,
                projectionEvent.OccurredAtUtc,
                projectionEvent.RecordedAtUtc,
                projectionEvent.Source,
                projectionEvent.SurfaceKind,
                projectionEvent.SurfaceId,
                projectionEvent.ReferenceKind,
                projectionEvent.ReferenceId,
                projectionEvent.IsRedacted,
                projectionEvent.RedactionReason);
    }
}
