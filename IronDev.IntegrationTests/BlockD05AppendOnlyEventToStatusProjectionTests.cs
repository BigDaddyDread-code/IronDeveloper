using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD05AppendOnlyEventToStatusProjectionTests
{
    private const string TenantId = "tenant-d05";
    private const string ProjectId = "project-d05";
    private const string OperationId = "op_0000000000000005";
    private const string CorrelationId = "corr_0123456789abcdef";
    private const string ProjectionVersion = "projection-v1";
    private static readonly DateTimeOffset OccurredAtUtc = DateTimeOffset.Parse("2026-06-24T04:00:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T04:05:00Z");
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T03:55:00Z");

    [TestMethod]
    public void ValidProjectionEvent_Passes()
    {
        var result = OperationStatusProjectionValidator.ValidateEvent(Event());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(0, result.Issues.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "projection is read-only");
        AssertContains(result.ForbiddenAuthorityImplications, "projected status is display truth only");
    }

    [TestMethod]
    public void EmptyValidEventList_ProjectsNoEvents()
    {
        var result = Project();

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.NoEvents, result.ProjectedStatus!.ProjectedStatusKind);
        Assert.AreEqual(0, result.ProjectedStatus.SourceEventIds.Count);
        Assert.IsNull(result.ProjectedStatus.LastStatusChangingEventId);
    }

    [DataTestMethod]
    [DataRow(OperationStatusProjectionEventKind.OperationMinted, OperationProjectedStatusKind.Minted)]
    [DataRow(OperationStatusProjectionEventKind.RunStarted, OperationProjectedStatusKind.RunObserved)]
    [DataRow(OperationStatusProjectionEventKind.RunLinked, OperationProjectedStatusKind.RunObserved)]
    [DataRow(OperationStatusProjectionEventKind.PatchArtifactCreated, OperationProjectedStatusKind.PatchArtifactObserved)]
    [DataRow(OperationStatusProjectionEventKind.PatchArtifactLinked, OperationProjectedStatusKind.PatchArtifactObserved)]
    [DataRow(OperationStatusProjectionEventKind.SourceApplyStarted, OperationProjectedStatusKind.SourceApplyObserved)]
    [DataRow(OperationStatusProjectionEventKind.SourceApplyObserved, OperationProjectedStatusKind.SourceApplyObserved)]
    [DataRow(OperationStatusProjectionEventKind.CommitPackageCreated, OperationProjectedStatusKind.CommitPackageObserved)]
    [DataRow(OperationStatusProjectionEventKind.CommitObserved, OperationProjectedStatusKind.CommitObserved)]
    [DataRow(OperationStatusProjectionEventKind.PushObserved, OperationProjectedStatusKind.PushObserved)]
    [DataRow(OperationStatusProjectionEventKind.PullRequestObserved, OperationProjectedStatusKind.PullRequestObserved)]
    [DataRow(OperationStatusProjectionEventKind.BlockedObserved, OperationProjectedStatusKind.BlockedObserved)]
    [DataRow(OperationStatusProjectionEventKind.InterruptedObserved, OperationProjectedStatusKind.InterruptedObserved)]
    [DataRow(OperationStatusProjectionEventKind.RecoveryObserved, OperationProjectedStatusKind.RecoveryObserved)]
    [DataRow(OperationStatusProjectionEventKind.RollbackObserved, OperationProjectedStatusKind.RollbackObserved)]
    [DataRow(OperationStatusProjectionEventKind.FailedObserved, OperationProjectedStatusKind.FailedObserved)]
    [DataRow(OperationStatusProjectionEventKind.CompletedObserved, OperationProjectedStatusKind.CompletedObserved)]
    public void StatusChangingEvents_ProjectExplicitStatus(
        OperationStatusProjectionEventKind eventKind,
        OperationProjectedStatusKind expectedStatus)
    {
        var result = Project(Event(eventKind: eventKind));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(expectedStatus, result.ProjectedStatus!.ProjectedStatusKind);
        Assert.AreEqual(eventKind, result.ProjectedStatus.LastStatusChangingEventKind);
        Assert.AreEqual("projection-event-1", result.ProjectedStatus.LastStatusChangingEventId);
    }

    [DataTestMethod]
    [DataRow(OperationStatusProjectionEventKind.EvidenceObserved)]
    [DataRow(OperationStatusProjectionEventKind.ReceiptObserved)]
    [DataRow(OperationStatusProjectionEventKind.ValidationObserved)]
    [DataRow(OperationStatusProjectionEventKind.AuthorityBoundaryObserved)]
    public void MetadataOnlyEvents_DoNotChangeProjectedStatus(OperationStatusProjectionEventKind metadataKind)
    {
        var result = Project(
            Event(OperationStatusProjectionEventKind.RunStarted, projectionEventId: "projection-event-run", appendPosition: 0),
            Event(metadataKind, projectionEventId: "projection-event-metadata", appendPosition: 1, surfaceId: "metadata-surface"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.RunObserved, result.ProjectedStatus!.ProjectedStatusKind);
        Assert.AreEqual(OperationStatusProjectionEventKind.RunStarted, result.ProjectedStatus.LastStatusChangingEventKind);
        CollectionAssert.AreEqual(
            new[] { "projection-event-run", "projection-event-metadata" },
            result.ProjectedStatus.SourceEventIds.ToArray());
    }

    [TestMethod]
    public void ProjectionUsesAppendPositionAsPrimaryOrderInsteadOfOccurredTime()
    {
        var result = Project(
            Event(OperationStatusProjectionEventKind.CompletedObserved, projectionEventId: "projection-event-completed", appendPosition: 1, occurredAtUtc: OccurredAtUtc.AddMinutes(-10)),
            Event(OperationStatusProjectionEventKind.RunStarted, projectionEventId: "projection-event-run", appendPosition: 0, occurredAtUtc: OccurredAtUtc.AddMinutes(10)));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.CompletedObserved, result.ProjectedStatus!.ProjectedStatusKind);
        CollectionAssert.AreEqual(
            new[] { "projection-event-run", "projection-event-completed" },
            result.ProjectedStatus.SourceEventIds.ToArray());
    }

    [TestMethod]
    public void ProjectionOrdering_IsDeterministic()
    {
        var result = Project(
            Event(OperationStatusProjectionEventKind.PushObserved, projectionEventId: "projection-event-3", appendPosition: 3, recordedAtUtc: RecordedAtUtc.AddMinutes(3)),
            Event(OperationStatusProjectionEventKind.OperationMinted, projectionEventId: "projection-event-1", appendPosition: 1, recordedAtUtc: RecordedAtUtc.AddMinutes(1)),
            Event(OperationStatusProjectionEventKind.CommitObserved, projectionEventId: "projection-event-2", appendPosition: 2, recordedAtUtc: RecordedAtUtc.AddMinutes(2)));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        CollectionAssert.AreEqual(
            new[] { "projection-event-1", "projection-event-2", "projection-event-3" },
            result.ProjectedStatus!.SourceEventIds.ToArray());
        Assert.AreEqual(OperationProjectedStatusKind.PushObserved, result.ProjectedStatus.ProjectedStatusKind);
    }

    [TestMethod]
    public void DuplicateProjectionEventIds_FailClosed()
    {
        var result = Project(
            Event(projectionEventId: "projection-event-duplicate", appendPosition: 0, surfaceId: "surface-a"),
            Event(projectionEventId: "projection-event-duplicate", appendPosition: 1, surfaceId: "surface-b"));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationStatusProjectionDuplicateEventId");
    }

    [TestMethod]
    public void DuplicateAppendPositions_FailClosed()
    {
        var result = Project(
            Event(projectionEventId: "projection-event-a", appendPosition: 0, surfaceId: "surface-a"),
            Event(projectionEventId: "projection-event-b", appendPosition: 0, surfaceId: "surface-b"));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationStatusProjectionDuplicateAppendPosition");
    }

    [DataTestMethod]
    [DataRow(null, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionTenantIdRequired")]
    [DataRow("", ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionTenantIdRequired")]
    [DataRow("tenant d05", ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionTenantIdInvalid")]
    [DataRow(TenantId, null, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionProjectIdRequired")]
    [DataRow(TenantId, "", OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionProjectIdRequired")]
    [DataRow(TenantId, "project d05", OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionProjectIdInvalid")]
    [DataRow(TenantId, ProjectId, null, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationIdRequired")]
    [DataRow(TenantId, ProjectId, "run-123", CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow(TenantId, ProjectId, OperationId, null, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionCorrelation:OperationCorrelationIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, "run-123", OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionCorrelation:OperationCorrelationIdCannotLookLikeRunId")]
    [DataRow(TenantId, ProjectId, OperationId, OperationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionCorrelation:OperationCorrelationIdCannotReplaceOperationId")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.Unknown, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionEventKindRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, null, 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionEventIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection event 1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionEventIdInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "https://example.test/event", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionEventIdInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "approval granted", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionEventIdInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", -1L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionAppendPositionInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, null, OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionSourceRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "source with space", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionSourceInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "approval granted", OperationCorrelationSurfaceKind.OperationStatus, "status-record-1", "OperationStatusProjectionSourceInvalid")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.Unknown, "status-record-1", "OperationStatusProjectionSurfaceKindRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, null, "OperationStatusProjectionSurfaceIdRequired")]
    [DataRow(TenantId, ProjectId, OperationId, CorrelationId, OperationStatusProjectionEventKind.RunStarted, "projection-event-1", 0L, "d05-source", OperationCorrelationSurfaceKind.OperationStatus, "status record 1", "OperationStatusProjectionSurfaceIdInvalid")]
    public void ProjectionEventValidation_FailsClosedForInvalidShape(
        string? tenantId,
        string? projectId,
        string? operationId,
        string? correlationId,
        OperationStatusProjectionEventKind eventKind,
        string? projectionEventId,
        long appendPosition,
        string? source,
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        string expectedIssue)
    {
        var result = OperationStatusProjectionValidator.ValidateEvent(Event(
            tenantId: tenantId!,
            projectId: projectId!,
            operationId: operationId!,
            correlationId: correlationId!,
            eventKind: eventKind,
            projectionEventId: projectionEventId!,
            appendPosition: appendPosition,
            source: source!,
            surfaceKind: surfaceKind,
            surfaceId: surfaceId!));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(true, null, "OperationStatusProjectionRedactionReasonRequired")]
    [DataRow(true, "", "OperationStatusProjectionRedactionReasonRequired")]
    [DataRow(true, "approval granted", "OperationStatusProjectionRedactionReasonInvalid")]
    [DataRow(true, "api key leaked", "OperationStatusProjectionRedactionReasonInvalid")]
    public void RedactedProjectionEvent_RequiresSafeRedactionReason(
        bool isRedacted,
        string? redactionReason,
        string expectedIssue)
    {
        var result = OperationStatusProjectionValidator.ValidateEvent(Event(
            isRedacted: isRedacted,
            redactionReason: redactionReason));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void ProjectionEventValidation_RequiresOccurredTimestamp()
    {
        var result = OperationStatusProjectionValidator.ValidateEvent(Event(
            occurredAtUtc: default(DateTimeOffset)));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationStatusProjectionOccurredAtRequired");
    }

    [TestMethod]
    public void ProjectionEventValidation_RequiresRecordedTimestamp()
    {
        var result = OperationStatusProjectionValidator.ValidateEvent(Event(
            recordedAtUtc: default(DateTimeOffset)));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationStatusProjectionRecordedAtRequired");
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "OperationStatusProjectionTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "OperationStatusProjectionProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000006", "OperationStatusProjectionOperationMismatch")]
    public void ProjectionRequest_FailsClosedForMismatchedEventScope(
        string eventTenantId,
        string eventProjectId,
        string eventOperationId,
        string expectedIssue)
    {
        var result = Project(Event(
            tenantId: eventTenantId,
            projectId: eventProjectId,
            operationId: eventOperationId));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void Projection_AllowsMultipleCorrelationIdsUnderOneOperation()
    {
        var result = Project(
            Event(projectionEventId: "projection-event-a", appendPosition: 0, correlationId: "corr_aaaaaaaaaaaaaaaa", surfaceId: "surface-a"),
            Event(projectionEventId: "projection-event-b", appendPosition: 1, correlationId: "corr_bbbbbbbbbbbbbbbb", surfaceId: "surface-b"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(2, result.ProjectedStatus!.SourceEventIds.Count);
    }

    [TestMethod]
    public void Projection_DoesNotAllowCorrelationIdToReplaceOperationId()
    {
        var result = Project(Event(correlationId: OperationId));

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationStatusProjectionEvent:OperationStatusProjectionCorrelation:OperationCorrelationIdCannotReplaceOperationId");
    }

    [TestMethod]
    public void Projection_DoesNotInferStatusFromSurfaceReferenceOrCorrelationText()
    {
        var result = Project(
            Event(OperationStatusProjectionEventKind.RunStarted, projectionEventId: "projection-event-run", appendPosition: 0, surfaceId: "surface-run"),
            Event(
                OperationStatusProjectionEventKind.EvidenceObserved,
                projectionEventId: "projection-event-text",
                appendPosition: 1,
                surfaceId: "completed-observed",
                referenceKind: OperationReferenceKind.EvidenceId,
                referenceId: "completed-observed"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.RunObserved, result.ProjectedStatus!.ProjectedStatusKind);
    }

    [DataTestMethod]
    [DataRow("projection is not blocked-state explanation")]
    [DataRow("projection is not missing evidence resolution")]
    [DataRow("projection is not forbidden action resolution")]
    [DataRow("projection is not validation freshness")]
    [DataRow("projection is not next-safe-action formatting")]
    [DataRow("projection is not approval")]
    [DataRow("projection is not policy satisfaction")]
    [DataRow("projection is not push")]
    [DataRow("projection is not PR creation")]
    [DataRow("projection is not merge readiness")]
    [DataRow("projection is not release readiness")]
    [DataRow("projection is not deployment readiness")]
    [DataRow("projection is not retry permission")]
    [DataRow("projection is not rollback")]
    [DataRow("projection is not workflow continuation")]
    [DataRow("projection order is not authority order")]
    [DataRow("projected status is not permission")]
    public void Projection_DoesNotInferAuthorityOrResolverOutputs(string expectedForbiddenImplication)
    {
        var result = Project(
            Event(OperationStatusProjectionEventKind.CommitObserved, projectionEventId: "projection-event-commit", appendPosition: 0),
            Event(OperationStatusProjectionEventKind.PushObserved, projectionEventId: "projection-event-push", appendPosition: 1, surfaceId: "push-surface"),
            Event(OperationStatusProjectionEventKind.PullRequestObserved, projectionEventId: "projection-event-pr", appendPosition: 2, surfaceId: "pr-surface"),
            Event(OperationStatusProjectionEventKind.CompletedObserved, projectionEventId: "projection-event-completed", appendPosition: 3, surfaceId: "completed-surface"),
            Event(OperationStatusProjectionEventKind.InterruptedObserved, projectionEventId: "projection-event-interrupted", appendPosition: 4, surfaceId: "interrupted-surface"),
            Event(OperationStatusProjectionEventKind.RollbackObserved, projectionEventId: "projection-event-rollback", appendPosition: 5, surfaceId: "rollback-surface"),
            Event(OperationStatusProjectionEventKind.RecoveryObserved, projectionEventId: "projection-event-recovery", appendPosition: 6, surfaceId: "recovery-surface"));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, expectedForbiddenImplication);
        AssertContains(result.ProjectedStatus!.ForbiddenAuthorityImplications, expectedForbiddenImplication);
    }

    [TestMethod]
    public void ProjectedStatusModel_ExposesNoAuthorityProperties()
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
            Source = "d05-source"
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
            TimelineEventId = "timeline-event-d05",
            EventKind = GovernedOperationTimelineEventKind.StatusObserved,
            OccurredAtUtc = OccurredAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "d05-source",
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
    public void D05CoreFiles_DoNotMintIdsOrCallLookupOrTimelineAssembler()
    {
        var source = D05CoreSource();

        foreach (var marker in new[]
        {
            "Guid.NewGuid",
            "RandomNumberGenerator",
            "OperationIdentityLookupResolver.Resolve",
            "GovernedOperationTimelineAssembler.Assemble"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void ExistingA02StatusReadAdapter_RemainsReadOnly()
    {
        var source = A02StatusSource();

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
            "CanContinueWorkflow = true"
        })
        {
            AssertDoesNotContain(source, marker);
        }
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
            "CanContinueWorkflow = true"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void StaticScan_D05CoreFilesAddNoApiSqlUiStoreExecutorOrMutationSurface()
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
    public void Receipt_RecordsProjectedStatusIsNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D05_APPEND_ONLY_EVENT_TO_STATUS_PROJECTION.md"));

        Assert.IsTrue(receipt.Contains(
            "Append-only operation events can project deterministic display status. Projected status does not mint identity, perform lookup, assemble timelines, resolve evidence, determine blockers, validate freshness, choose next safe action, approve work, satisfy policy, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.",
            StringComparison.Ordinal));
    }

    private static OperationStatusProjectionResult Project(params OperationStatusProjectionEvent[] events) =>
        OperationStatusProjector.Project(Request(events));

    private static OperationStatusProjectionRequest Request(IReadOnlyList<OperationStatusProjectionEvent> events) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            ProjectionVersion = ProjectionVersion,
            Events = events
        };

    private static OperationStatusProjectionEvent Event(
        OperationStatusProjectionEventKind eventKind = OperationStatusProjectionEventKind.RunStarted,
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string correlationId = CorrelationId,
        string projectionEventId = "projection-event-1",
        long appendPosition = 0,
        DateTimeOffset? occurredAtUtc = null,
        DateTimeOffset? recordedAtUtc = null,
        string source = "d05-source",
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
        string surfaceId = "status-record-1",
        OperationReferenceKind referenceKind = OperationReferenceKind.RunId,
        string referenceId = "run-123",
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            CorrelationId = correlationId,
            ProjectionEventId = projectionEventId,
            AppendPosition = appendPosition,
            EventKind = eventKind,
            OccurredAtUtc = occurredAtUtc ?? OccurredAtUtc,
            RecordedAtUtc = recordedAtUtc ?? RecordedAtUtc,
            Source = source,
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
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
                    Source = "d05-reference-source"
                }
            ],
            CorrelationId = CorrelationId
        };

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
}
