using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed partial class BlockD16OperationStatusPaginationFilteringTests
{
    private const string TenantId = "tenant-d16";
    private const string ProjectId = "project-d16";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset BaseTime = DateTimeOffset.Parse("2026-06-24T10:00:00Z");

    [TestMethod]
    public void ValidRequestWithEmptyRows_ReturnsNoRows()
    {
        var result = Page([]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationStatusPageResolutionStatus.NoRows, result.ResolutionStatus);
        Assert.AreEqual(0, result.MatchedCount);
        Assert.AreEqual(0, result.ScannedCount);
        AssertContains(result.ForbiddenAuthorityImplications, "filtered operation is not approved");
    }

    [TestMethod]
    public void ValidRequestWithScopedRows_ReturnsFirstPage()
    {
        var result = Page(Rows(3), pageSize: 2);

        AssertPage(result, "op_0000000000000001", "op_0000000000000002");
        Assert.IsTrue(result.HasMore);
        Assert.IsNotNull(result.NextCursor);
        Assert.AreEqual(3, result.MatchedCount);
        Assert.AreEqual(3, result.ScannedCount);
    }

    [TestMethod]
    public void PageSizeZero_FailsClosed()
    {
        var result = Page(Rows(1), pageSize: 0);

        AssertInvalid(result, "OperationStatusPageSizeRequired");
    }

    [TestMethod]
    public void PageSizeAboveMaximum_FailsClosed()
    {
        var result = Page(Rows(1), pageSize: OperationStatusPaginationValidator.MaxPageSize + 1);

        AssertInvalid(result, "OperationStatusPageSizeExceedsMaximum");
    }

    [TestMethod]
    public void MaximumPageSize_DoesNotReturnAllWhenMoreRowsExist()
    {
        var result = Page(Rows(OperationStatusPaginationValidator.MaxPageSize + 1), pageSize: OperationStatusPaginationValidator.MaxPageSize);

        Assert.AreEqual(OperationStatusPaginationValidator.MaxPageSize, result.Items.Count);
        Assert.IsTrue(result.HasMore);
        Assert.IsNotNull(result.NextCursor);
    }

    [TestMethod]
    public void NextCursorOnlyAppearsWhenMoreMatchesExist()
    {
        var exact = Page(Rows(2), pageSize: 2);
        var partial = Page(Rows(3), pageSize: 2);

        Assert.IsFalse(exact.HasMore);
        Assert.IsNull(exact.NextCursor);
        Assert.IsTrue(partial.HasMore);
        Assert.IsNotNull(partial.NextCursor);
    }

    [TestMethod]
    public void CursorFetch_ReturnsNextDeterministicPage()
    {
        var first = Page(Rows(4), pageSize: 2);
        var second = Page(Rows(4), pageSize: 2, cursor: first.NextCursor);

        AssertPage(first, "op_0000000000000001", "op_0000000000000002");
        AssertPage(second, "op_0000000000000003", "op_0000000000000004");
        Assert.IsFalse(second.HasMore);
        Assert.IsNull(second.NextCursor);
    }

    [TestMethod]
    public void CursorAtEnd_ReturnsCursorExhausted()
    {
        var lastRow = Row(2);
        var cursor = new OperationStatusPageCursor
        {
            SortField = OperationStatusPageSortField.CreatedAtUtc,
            SortDirection = OperationStatusPageSortDirection.Ascending,
            LastSortValue = lastRow.CreatedAtUtc.ToString("O"),
            LastOperationId = lastRow.OperationId,
            LastCorrelationId = lastRow.CorrelationId
        };

        var exhausted = Page(Rows(2), pageSize: 1, cursor: cursor);

        Assert.AreEqual(OperationStatusPageResolutionStatus.CursorExhausted, exhausted.ResolutionStatus);
        Assert.IsTrue(exhausted.IsValid);
        Assert.AreEqual(0, exhausted.Items.Count);
    }

    [TestMethod]
    public void CursorSortMismatch_FailsClosed()
    {
        var first = Page(Rows(3), pageSize: 1);
        var cursor = first.NextCursor! with
        {
            SortField = OperationStatusPageSortField.UpdatedAtUtc
        };

        var result = Page(Rows(3), cursor: cursor);

        AssertInvalid(result, "OperationStatusPageCursorSortMismatch");
    }

    [TestMethod]
    public void AmbiguousCursor_FailsClosed()
    {
        var rows = new[]
        {
            Row(1),
            Row(1)
        };

        var cursor = new OperationStatusPageCursor
        {
            SortField = OperationStatusPageSortField.CreatedAtUtc,
            SortDirection = OperationStatusPageSortDirection.Ascending,
            LastSortValue = Row(1).CreatedAtUtc.ToString("O"),
            LastOperationId = Row(1).OperationId,
            LastCorrelationId = Row(1).CorrelationId
        };

        var result = Page(rows, cursor: cursor);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(OperationStatusPageResolutionStatus.AmbiguousCursor, result.ResolutionStatus);
        AssertContains(result.Issues, "OperationStatusPageCursorAmbiguous");
    }

    [DataTestMethod]
    [DataRow(OperationStatusPageSortField.CreatedAtUtc, OperationStatusPageSortDirection.Ascending, "op_0000000000000001", "op_0000000000000002", "op_0000000000000003")]
    [DataRow(OperationStatusPageSortField.CreatedAtUtc, OperationStatusPageSortDirection.Descending, "op_0000000000000003", "op_0000000000000002", "op_0000000000000001")]
    [DataRow(OperationStatusPageSortField.UpdatedAtUtc, OperationStatusPageSortDirection.Ascending, "op_0000000000000001", "op_0000000000000002", "op_0000000000000003")]
    [DataRow(OperationStatusPageSortField.LastEventAtUtc, OperationStatusPageSortDirection.Ascending, "op_0000000000000001", "op_0000000000000002", "op_0000000000000003")]
    [DataRow(OperationStatusPageSortField.OperationId, OperationStatusPageSortDirection.Descending, "op_0000000000000003", "op_0000000000000002", "op_0000000000000001")]
    [DataRow(OperationStatusPageSortField.CorrelationId, OperationStatusPageSortDirection.Descending, "op_0000000000000003", "op_0000000000000002", "op_0000000000000001")]
    [DataRow(OperationStatusPageSortField.ProjectedStatus, OperationStatusPageSortDirection.Ascending, "op_0000000000000001", "op_0000000000000002", "op_0000000000000003")]
    public void SortModes_AreDeterministic(
        OperationStatusPageSortField sortField,
        OperationStatusPageSortDirection sortDirection,
        string first,
        string second,
        string third)
    {
        var rows = new[]
        {
            Row(3, projectedStatus: OperationProjectedStatusKind.CommitObserved),
            Row(1, projectedStatus: OperationProjectedStatusKind.Minted),
            Row(2, projectedStatus: OperationProjectedStatusKind.RunObserved)
        };

        var result = Page(rows, pageSize: 3, sortField: sortField, sortDirection: sortDirection);

        AssertPage(result, first, second, third);
    }

    [TestMethod]
    public void TieBreakerUsesOperationIdThenCorrelationId()
    {
        var sameCreated = BaseTime;
        var rows = new[]
        {
            Row(3) with { CreatedAtUtc = sameCreated, CorrelationId = "corr_1000000000000003" },
            Row(1) with { CreatedAtUtc = sameCreated, CorrelationId = "corr_1000000000000002" },
            Row(1) with { CreatedAtUtc = sameCreated, CorrelationId = "corr_1000000000000001" }
        };

        var result = Page(rows, pageSize: 3);

        Assert.AreEqual("corr_1000000000000001", result.Items[0].CorrelationId);
        Assert.AreEqual("corr_1000000000000002", result.Items[1].CorrelationId);
        Assert.AreEqual("op_0000000000000003", result.Items[2].OperationId);
    }

    [DataTestMethod]
    [DataRow("operation-id", "OperationId", "op_0000000000000002")]
    [DataRow("correlation-id", "CorrelationId", "op_0000000000000002")]
    [DataRow("projected-status", "ProjectedStatus", "op_0000000000000002")]
    [DataRow("surface", "Surface", "op_0000000000000002")]
    [DataRow("reference", "Reference", "op_0000000000000002")]
    [DataRow("created-range", "CreatedAtUtc", "op_0000000000000002")]
    [DataRow("updated-range", "UpdatedAtUtc", "op_0000000000000002")]
    [DataRow("last-event-range", "LastEventAtUtc", "op_0000000000000002")]
    [DataRow("missing-evidence", "MissingEvidenceStatus", "op_0000000000000002")]
    [DataRow("forbidden-action", "ForbiddenActionStatus", "op_0000000000000002")]
    [DataRow("receipt-resolution", "ReceiptResolutionStatus", "op_0000000000000002")]
    [DataRow("evidence-resolution", "EvidenceResolutionStatus", "op_0000000000000002")]
    [DataRow("validation-staleness", "ValidationStalenessStatus", "op_0000000000000002")]
    [DataRow("patch-base", "PatchBaseFreshnessStatus", "op_0000000000000002")]
    [DataRow("worktree-base-head", "WorktreeBaseHeadFreshnessStatus", "op_0000000000000002")]
    [DataRow("interrupted-run", "InterruptedRunStatus", "op_0000000000000002")]
    [DataRow("rollback-recovery", "RollbackRecoveryStatus", "op_0000000000000002")]
    public void Filters_AreConjunctiveAndDiagnosticOnly(
        string scenario,
        string expectedReason,
        string expectedOperationId)
    {
        var rows = Rows(3)
            .Select(row => row.OperationId.EndsWith('2')
                ? row with
                {
                    ProjectedStatus = OperationProjectedStatusKind.BlockedObserved,
                    SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
                    SurfaceId = "surface-d16",
                    ReferenceKind = OperationReferenceKind.StatusRecordId,
                    ReferenceId = "status-record-d16",
                    MissingEvidenceStatus = MissingEvidenceResolutionStatus.MissingEvidence,
                    ForbiddenActionStatus = ForbiddenActionResolutionStatus.Forbidden,
                    ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.PartiallyResolved,
                    EvidenceResolutionStatus = EvidenceResolutionStatus.PartiallyResolved,
                    ValidationStalenessStatus = ValidationStalenessResolutionStatus.MixedStaleness,
                    PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.MixedFreshness,
                    WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.MixedFreshness,
                    InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted,
                    RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.MissingMaterial
                }
                : row)
            .ToArray();

        var filter = scenario switch
        {
            "operation-id" => new OperationStatusPageFilter { OperationId = expectedOperationId },
            "correlation-id" => new OperationStatusPageFilter { CorrelationId = "corr_1000000000000002" },
            "projected-status" => new OperationStatusPageFilter { ProjectedStatuses = [OperationProjectedStatusKind.BlockedObserved] },
            "surface" => new OperationStatusPageFilter { SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus, SurfaceId = "surface-d16" },
            "reference" => new OperationStatusPageFilter { ReferenceKind = OperationReferenceKind.StatusRecordId, ReferenceId = "status-record-d16" },
            "created-range" => new OperationStatusPageFilter { CreatedFromUtc = BaseTime.AddMinutes(2), CreatedToUtc = BaseTime.AddMinutes(2) },
            "updated-range" => new OperationStatusPageFilter { UpdatedFromUtc = BaseTime.AddMinutes(7), UpdatedToUtc = BaseTime.AddMinutes(7) },
            "last-event-range" => new OperationStatusPageFilter { LastEventFromUtc = BaseTime.AddMinutes(12), LastEventToUtc = BaseTime.AddMinutes(12) },
            "missing-evidence" => new OperationStatusPageFilter { MissingEvidenceStatuses = [MissingEvidenceResolutionStatus.MissingEvidence] },
            "forbidden-action" => new OperationStatusPageFilter { ForbiddenActionStatuses = [ForbiddenActionResolutionStatus.Forbidden] },
            "receipt-resolution" => new OperationStatusPageFilter { ReceiptResolutionStatuses = [ReceiptReferenceResolutionStatus.PartiallyResolved] },
            "evidence-resolution" => new OperationStatusPageFilter { EvidenceResolutionStatuses = [EvidenceResolutionStatus.PartiallyResolved] },
            "validation-staleness" => new OperationStatusPageFilter { ValidationStalenessStatuses = [ValidationStalenessResolutionStatus.MixedStaleness] },
            "patch-base" => new OperationStatusPageFilter { PatchBaseFreshnessStatuses = [PatchBaseFreshnessResolutionStatus.MixedFreshness] },
            "worktree-base-head" => new OperationStatusPageFilter { WorktreeBaseHeadFreshnessStatuses = [WorktreeBaseHeadFreshnessResolutionStatus.MixedFreshness] },
            "interrupted-run" => new OperationStatusPageFilter { InterruptedRunStatuses = [InterruptedRunReadModelStatus.Interrupted] },
            "rollback-recovery" => new OperationStatusPageFilter { RollbackRecoveryStatuses = [RollbackRecoveryReadModelStatus.MissingMaterial] },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        var result = Page(rows, filter: filter);

        AssertPage(result, expectedOperationId);
        AssertContains(result.Items[0].MatchedFilterReasons, expectedReason);
        AssertContains(result.ForbiddenAuthorityImplications, "selected page rows are not action candidates");
    }

    [TestMethod]
    public void InvalidDateRange_FailsClosed()
    {
        var result = Page(Rows(1), filter: new OperationStatusPageFilter
        {
            CreatedFromUtc = BaseTime.AddHours(1),
            CreatedToUtc = BaseTime
        });

        AssertInvalid(result, "OperationStatusPageFilterCreatedRangeInvalid");
    }

    [TestMethod]
    public void RedactedRows_AreExcludedByDefault()
    {
        var result = Page([Row(1), Row(2) with { IsRedacted = true, RedactionReason = "tenant-visible-redaction" }]);

        AssertPage(result, "op_0000000000000001");
        Assert.AreEqual(1, result.MatchedCount);
        Assert.AreEqual(2, result.ScannedCount);
    }

    [TestMethod]
    public void RedactedRows_AreIncludedOnlyWhenRequested()
    {
        var result = Page(
            [Row(1), Row(2) with { IsRedacted = true, RedactionReason = "tenant-visible-redaction" }],
            filter: new OperationStatusPageFilter { IncludeRedacted = true });

        AssertPage(result, "op_0000000000000001", "op_0000000000000002");
        AssertContains(result.Items[1].MatchedFilterReasons, "RedactedIncluded");
    }

    [DataTestMethod]
    [DataRow("redacted-without-reason", "OperationStatusPageRowRedactionReasonRequired")]
    [DataRow("redacted-unsafe-reason", "OperationStatusPageRowRedactionReasonInvalid")]
    [DataRow("missing-tenant", "OperationStatusPageTenantIdRequired")]
    [DataRow("missing-project", "OperationStatusPageProjectIdRequired")]
    [DataRow("missing-asof", "OperationStatusPageAsOfUtcRequired")]
    [DataRow("null-rows", "OperationStatusPageRowsRequired")]
    [DataRow("cross-tenant-row", "OperationStatusPageRowTenantMismatch")]
    [DataRow("cross-project-row", "OperationStatusPageRowProjectMismatch")]
    [DataRow("missing-operation-id", "OperationStatusPageRow:OperationIdRequired")]
    [DataRow("invalid-operation-id", "OperationStatusPageRow:OperationIdMustBeBackendMintedCanonicalId")]
    [DataRow("invalid-correlation-id", "OperationStatusPageRowCorrelationIdInvalid")]
    [DataRow("unknown-projected-status", "OperationStatusPageRowProjectedStatusRequired")]
    [DataRow("missing-created", "OperationStatusPageRowCreatedAtRequired")]
    [DataRow("missing-updated", "OperationStatusPageRowUpdatedAtRequired")]
    [DataRow("missing-last-event", "OperationStatusPageRowLastEventAtRequired")]
    [DataRow("updated-before-created", "OperationStatusPageRowUpdatedBeforeCreated")]
    [DataRow("last-event-before-created", "OperationStatusPageRowLastEventBeforeCreated")]
    [DataRow("negative-timeline-count", "OperationStatusPageRowTimelineEventCountInvalid")]
    [DataRow("surface-id-without-kind", "OperationStatusPageRowSurfaceKindRequired")]
    [DataRow("invalid-surface-kind", "OperationStatusPageRowSurfaceKindInvalid")]
    [DataRow("reference-kind-without-id", "OperationStatusPageRowReferenceIdRequired")]
    [DataRow("reference-id-without-kind", "OperationStatusPageRowReferenceKindRequired")]
    [DataRow("unsafe-filter-value", "OperationStatusPageFilterSurfaceIdInvalid")]
    [DataRow("invalid-diagnostic-enum", "OperationStatusPageRowMissingEvidenceStatusInvalid")]
    public void InvalidRequests_FailClosed(string scenario, string expectedIssue)
    {
        var request = scenario switch
        {
            "redacted-without-reason" => Request([Row(1) with { IsRedacted = true, RedactionReason = null }]),
            "redacted-unsafe-reason" => Request([Row(1) with { IsRedacted = true, RedactionReason = "secret token" }]),
            "missing-tenant" => Request(Rows(1)) with { TenantId = "" },
            "missing-project" => Request(Rows(1)) with { ProjectId = "" },
            "missing-asof" => Request(Rows(1)) with { AsOfUtc = default },
            "null-rows" => Request(null),
            "cross-tenant-row" => Request([Row(1) with { TenantId = "tenant-other" }]),
            "cross-project-row" => Request([Row(1) with { ProjectId = "project-other" }]),
            "missing-operation-id" => Request([Row(1) with { OperationId = "" }]),
            "invalid-operation-id" => Request([Row(1) with { OperationId = "run_123" }]),
            "invalid-correlation-id" => Request([Row(1) with { CorrelationId = "corr approved" }]),
            "unknown-projected-status" => Request([Row(1) with { ProjectedStatus = OperationProjectedStatusKind.Unknown }]),
            "missing-created" => Request([Row(1) with { CreatedAtUtc = default }]),
            "missing-updated" => Request([Row(1) with { UpdatedAtUtc = default }]),
            "missing-last-event" => Request([Row(1) with { LastEventAtUtc = default }]),
            "updated-before-created" => Request([Row(1) with { UpdatedAtUtc = BaseTime.AddMinutes(-1) }]),
            "last-event-before-created" => Request([Row(1) with { LastEventAtUtc = BaseTime.AddMinutes(-1) }]),
            "negative-timeline-count" => Request([Row(1) with { TimelineEventCount = -1 }]),
            "surface-id-without-kind" => Request([Row(1) with { SurfaceKind = OperationCorrelationSurfaceKind.Unknown, SurfaceId = "surface-d16" }]),
            "invalid-surface-kind" => Request([Row(1) with { SurfaceKind = (OperationCorrelationSurfaceKind)999, SurfaceId = "surface-d16" }]),
            "reference-kind-without-id" => Request([Row(1) with { ReferenceKind = OperationReferenceKind.StatusRecordId, ReferenceId = null }]),
            "reference-id-without-kind" => Request([Row(1) with { ReferenceKind = OperationReferenceKind.Unknown, ReferenceId = "status-record-d16" }]),
            "unsafe-filter-value" => Request(Rows(1)) with { Filter = new OperationStatusPageFilter { SurfaceId = "https://example.test/raw" } },
            "invalid-diagnostic-enum" => Request([Row(1) with { MissingEvidenceStatus = (MissingEvidenceResolutionStatus)999 }]),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        var result = OperationStatusPaginator.Page(request);

        AssertInvalid(result, expectedIssue);
    }

    [TestMethod]
    public void UnknownSortFieldAndDirection_FailClosed()
    {
        var fieldResult = OperationStatusPaginator.Page(Request(Rows(1)) with { SortField = OperationStatusPageSortField.Unknown });
        var directionResult = OperationStatusPaginator.Page(Request(Rows(1)) with { SortDirection = OperationStatusPageSortDirection.Unknown });

        AssertInvalid(fieldResult, "OperationStatusPageSortFieldRequired");
        AssertInvalid(directionResult, "OperationStatusPageSortDirectionRequired");
    }

    [TestMethod]
    public void EmptyFilterWithNoMatches_IsNotDenial()
    {
        var result = Page(Rows(2), filter: new OperationStatusPageFilter { OperationId = "op_0000000000000099" });

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(OperationStatusPageResolutionStatus.NoMatches, result.ResolutionStatus);
        Assert.AreEqual(0, result.Items.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "empty page is not denial");
    }

    [TestMethod]
    public void FullPage_IsNotApprovalQueue()
    {
        var result = Page(Rows(2), pageSize: 2);

        Assert.AreEqual(2, result.Items.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "full page is not approval queue");
    }

    [TestMethod]
    public void FreshInterruptedAndRollbackMatches_DoNotExposeAuthority()
    {
        var rows = new[]
        {
            Row(1) with
            {
                PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.Assessed,
                WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.Assessed,
                InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted,
                RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.Assessed
            }
        };

        var result = Page(rows, filter: new OperationStatusPageFilter
        {
            PatchBaseFreshnessStatuses = [PatchBaseFreshnessResolutionStatus.Assessed],
            WorktreeBaseHeadFreshnessStatuses = [WorktreeBaseHeadFreshnessResolutionStatus.Assessed],
            InterruptedRunStatuses = [InterruptedRunReadModelStatus.Interrupted],
            RollbackRecoveryStatuses = [RollbackRecoveryReadModelStatus.Assessed]
        });

        AssertPage(result, "op_0000000000000001");
        AssertNoAuthorityProperties(typeof(OperationStatusPageResult));
        AssertNoAuthorityProperties(typeof(OperationStatusPageItem));
        AssertContains(result.ForbiddenAuthorityImplications, "operation status pagination is not retry permission");
        AssertContains(result.ForbiddenAuthorityImplications, "operation status pagination is not rollback execution");
    }

    [TestMethod]
    public void ResultItems_DoNotExposeRawPayloadProperties()
    {
        var propertyNames = typeof(OperationStatusPageItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(propertyNames, "RawPayload");
        CollectionAssert.DoesNotContain(propertyNames, "Patch");
        CollectionAssert.DoesNotContain(propertyNames, "Diff");
        CollectionAssert.DoesNotContain(propertyNames, "SourceContent");
        CollectionAssert.DoesNotContain(propertyNames, "ValidationLog");
    }

    [TestMethod]
    public void ResultModels_ExposeNoAuthorityFields()
    {
        AssertNoAuthorityProperties(typeof(OperationStatusPageResult));
        AssertNoAuthorityProperties(typeof(OperationStatusPageItem));
        AssertNoAuthorityProperties(typeof(OperationStatusSummaryRow));
        AssertNoAuthorityProperties(typeof(OperationStatusPageCursor));
    }

    [TestMethod]
    public void D01IdentityValidation_StillPassesForCanonicalRowIdentity()
    {
        var row = Row(1);

        Assert.IsTrue(OperationIdentityValidator.ValidateOperationId(row.OperationId).IsValid);
    }

    [TestMethod]
    public void D03CorrelationValidation_StillPassesForCanonicalRowCorrelation()
    {
        var row = Row(1);
        var result = OperationCorrelationValidator.ValidateLink(new OperationCorrelationLink
        {
            TenantId = row.TenantId,
            ProjectId = row.ProjectId,
            OperationId = row.OperationId,
            CorrelationId = row.CorrelationId,
            SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
            SurfaceId = "surface-d16",
            ObservedAtUtc = BaseTime,
            Source = "d16-test"
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [TestMethod]
    public void StaticScan_D16AddsNoStoreApiUiOrMutationSurface()
    {
        var source = StripStrings(D16CoreSource());

        AssertDoesNotContain(source, "Controller");
        AssertDoesNotContain(source, "Route(");
        AssertDoesNotContain(source, "MapGet");
        AssertDoesNotContain(source, "OpenApi");
        AssertDoesNotContain(source, "DbContext");
        AssertDoesNotContain(source, "SqlConnection");
        AssertDoesNotContain(source, "Repository");
        AssertDoesNotContain(source, "File.Read");
        AssertDoesNotContain(source, "Directory.");
        AssertDoesNotContain(source, "Process.Start");
        AssertDoesNotContain(source, "LibGit2Sharp");
        AssertDoesNotContain(source, "git ");
        AssertDoesNotContain(source, "Execute");
        AssertDoesNotContain(source, "Commit");
        AssertDoesNotContain(source, "Push");
        AssertDoesNotContain(source, "Merge");
        AssertDoesNotContain(source, "Release");
        AssertDoesNotContain(source, "Deploy");
        AssertDoesNotContain(source, "WorkflowContinuation");
    }

    [TestMethod]
    public void StaticScan_D16UsesNoSystemClock()
    {
        var source = D16CoreSource();

        AssertDoesNotContain(source, "DateTime.UtcNow");
        AssertDoesNotContain(source, "DateTimeOffset.UtcNow");
        AssertDoesNotContain(source, "SystemClock");
    }

    [TestMethod]
    public void StaticScan_D16DoesNotInvokeUpstreamResolversOrAssemblers()
    {
        var source = StripStrings(D16CoreSource());

        AssertDoesNotContain(source, "OperationIdentityLookup");
        AssertDoesNotContain(source, "GovernedOperationTimelineAssembler");
        AssertDoesNotContain(source, "OperationStatusProjector");
        AssertDoesNotContain(source, "MissingEvidenceResolver.");
        AssertDoesNotContain(source, "ForbiddenActionResolver.");
        AssertDoesNotContain(source, "ReceiptReferenceResolver.");
        AssertDoesNotContain(source, "EvidenceResolver.");
        AssertDoesNotContain(source, "ValidationStalenessResolver.");
        AssertDoesNotContain(source, "PatchBaseFreshnessResolver.");
        AssertDoesNotContain(source, "WorktreeBaseHeadFreshnessReadModelAssembler.");
        AssertDoesNotContain(source, "InterruptedRunReadModelAssembler.");
        AssertDoesNotContain(source, "RollbackRecoveryReadModelAssembler.");
    }

    [TestMethod]
    public void Receipt_RecordsPaginationIsNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(RepoPath("Docs", "receipts", "D16_OPERATION_STATUS_PAGINATION_FILTERING.md"));

        StringAssert.Contains(receipt, "The operation status paginator filters and pages supplied operation status summary rows only.");
        StringAssert.Contains(receipt, "It does not fetch operation status from stores, invoke diagnostic resolvers, approve operations, satisfy policy, choose next safe action, execute mutation, retry, rollback, recover, apply patches, commit, push, create PRs, merge, release, deploy, promote memory, or continue workflow.");
    }

    private static OperationStatusPageResult Page(
        IReadOnlyList<OperationStatusSummaryRow>? rows,
        int pageSize = 25,
        OperationStatusPageFilter? filter = null,
        OperationStatusPageCursor? cursor = null,
        OperationStatusPageSortField sortField = OperationStatusPageSortField.CreatedAtUtc,
        OperationStatusPageSortDirection sortDirection = OperationStatusPageSortDirection.Ascending) =>
        OperationStatusPaginator.Page(Request(rows, pageSize, filter, cursor, sortField, sortDirection));

    private static OperationStatusPageRequest Request(
        IReadOnlyList<OperationStatusSummaryRow>? rows,
        int pageSize = 25,
        OperationStatusPageFilter? filter = null,
        OperationStatusPageCursor? cursor = null,
        OperationStatusPageSortField sortField = OperationStatusPageSortField.CreatedAtUtc,
        OperationStatusPageSortDirection sortDirection = OperationStatusPageSortDirection.Ascending) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            AsOfUtc = AsOfUtc,
            PageSize = pageSize,
            SortField = sortField,
            SortDirection = sortDirection,
            Filter = filter ?? new OperationStatusPageFilter(),
            Cursor = cursor,
            Rows = rows
        };

    private static OperationStatusSummaryRow[] Rows(int count) =>
        Enumerable.Range(1, count)
            .Select(index => Row(index))
            .ToArray();

    private static OperationStatusSummaryRow Row(
        int index,
        OperationProjectedStatusKind projectedStatus = OperationProjectedStatusKind.Minted)
    {
        var stamp = index.ToString("x16");
        return new OperationStatusSummaryRow
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = $"op_{stamp}",
            CorrelationId = $"corr_1{stamp[1..]}",
            ProjectedStatus = projectedStatus,
            CreatedAtUtc = BaseTime.AddMinutes(index),
            UpdatedAtUtc = BaseTime.AddMinutes(index + 5),
            LastEventAtUtc = BaseTime.AddMinutes(index + 10),
            TimelineEventCount = index,
            SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
            SurfaceId = $"surface-{stamp}",
            ReferenceKind = OperationReferenceKind.StatusRecordId,
            ReferenceId = $"status-record-{stamp}",
            MissingEvidenceStatus = MissingEvidenceResolutionStatus.Complete,
            ForbiddenActionStatus = ForbiddenActionResolutionStatus.NoForbiddenFactsObserved,
            ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.Resolved,
            EvidenceResolutionStatus = EvidenceResolutionStatus.Resolved,
            ValidationStalenessStatus = ValidationStalenessResolutionStatus.Assessed,
            PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.Assessed,
            WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.Assessed,
            InterruptedRunStatus = InterruptedRunReadModelStatus.NoInterruptionObserved,
            RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.NoMaterial,
            Source = "d16-test"
        };
    }

    private static void AssertPage(
        OperationStatusPageResult result,
        params string[] operationIds)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationStatusPageResolutionStatus.PageReturned, result.ResolutionStatus);
        CollectionAssert.AreEqual(operationIds, result.Items.Select(static item => item.OperationId).ToArray());
    }

    private static void AssertInvalid(
        OperationStatusPageResult result,
        string issue)
    {
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(OperationStatusPageResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, issue);
    }

    private static void AssertContains(
        IEnumerable<string> values,
        string expected)
    {
        if (!values.Any(value => string.Equals(value, expected, StringComparison.Ordinal)))
        {
            Assert.Fail($"Expected '{expected}' in: {string.Join(", ", values)}");
        }
    }

    private static void AssertNoAuthorityProperties(Type type)
    {
        var blocked = new[]
        {
            "CanApply",
            "CanCommit",
            "CanPush",
            "CanCreatePullRequest",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "CanRollback",
            "CanRecover",
            "CanRetry",
            "CanResume",
            "CanContinue",
            "ApprovalStatus",
            "PolicySatisfied",
            "NextSafeAction",
            "AuthorityGranted",
            "ActionAllowed"
        };

        var names = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        foreach (var blockedName in blocked)
        {
            CollectionAssert.DoesNotContain(names, blockedName, $"{type.Name} exposes authority-shaped property {blockedName}");
        }
    }

    private static string D16CoreSource()
    {
        var paths = new[]
        {
            RepoPath("IronDev.Core", "Governance", "OperationStatusPaginationModels.cs"),
            RepoPath("IronDev.Core", "Governance", "OperationStatusPaginationValidator.cs"),
            RepoPath("IronDev.Core", "Governance", "OperationStatusPaginator.cs")
        };

        return string.Join("\n", paths.Select(File.ReadAllText));
    }

    private static void AssertDoesNotContain(string source, string marker)
    {
        if (source.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail($"Unexpected marker '{marker}' found in D16 source.");
        }
    }

    private static string StripStrings(string source) =>
        StringLiteralRegex().Replace(source, "\"\"");

    private static string RepoPath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            {
                return Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    [GeneratedRegex("\"(?:\\\\.|[^\"\\\\])*\"")]
    private static partial Regex StringLiteralRegex();
}
