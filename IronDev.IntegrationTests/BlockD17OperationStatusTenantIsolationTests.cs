using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed partial class BlockD17OperationStatusTenantIsolationTests
{
    private const string TenantA = "tenant-d17-a";
    private const string TenantB = "tenant-d17-b";
    private const string ProjectA = "project-d17-a";
    private const string ProjectB = "project-d17-b";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset Stamp = DateTimeOffset.Parse("2026-06-24T10:00:00Z");

    [TestMethod]
    public void MixedTenantRows_FailClosedWithoutItemsCursorOrCounts()
    {
        var result = Page(
            Row(1, TenantA, ProjectA),
            Row(2, TenantB, ProjectA));

        AssertInvalidIsolation(result, "OperationStatusPageRowTenantMismatch");
    }

    [TestMethod]
    public void MixedProjectRows_FailClosedWithoutItemsCursorOrCounts()
    {
        var result = Page(
            Row(1, TenantA, ProjectA),
            Row(2, TenantA, ProjectB));

        AssertInvalidIsolation(result, "OperationStatusPageRowProjectMismatch");
    }

    [TestMethod]
    public void SingleForeignTenantRow_FailsClosedRatherThanNoMatches()
    {
        var result = Page(Row(1, TenantB, ProjectA));

        AssertInvalidIsolation(result, "OperationStatusPageRowTenantMismatch");
    }

    [TestMethod]
    public void SingleForeignProjectRow_FailsClosedRatherThanNoMatches()
    {
        var result = Page(Row(1, TenantA, ProjectB));

        AssertInvalidIsolation(result, "OperationStatusPageRowProjectMismatch");
    }

    [DataTestMethod]
    [DataRow("operation-id")]
    [DataRow("correlation-id")]
    [DataRow("projected-status")]
    [DataRow("surface")]
    [DataRow("reference")]
    [DataRow("created-range")]
    [DataRow("updated-range")]
    [DataRow("last-event-range")]
    [DataRow("missing-evidence")]
    [DataRow("forbidden-action")]
    [DataRow("receipt-resolution")]
    [DataRow("evidence-resolution")]
    [DataRow("validation-staleness")]
    [DataRow("patch-base")]
    [DataRow("worktree-base-head")]
    [DataRow("interrupted-run")]
    [DataRow("rollback-recovery")]
    public void FiltersMatchingForeignTenantRows_FailClosedWithoutLeakingMatches(string filterKind)
    {
        var foreign = HostileRow(7, TenantB, ProjectA);
        var result = Page([foreign], filter: FilterFor(filterKind, foreign));

        AssertInvalidIsolation(result, "OperationStatusPageRowTenantMismatch");
    }

    [DataTestMethod]
    [DataRow("operation-id")]
    [DataRow("correlation-id")]
    [DataRow("projected-status")]
    [DataRow("surface")]
    [DataRow("reference")]
    [DataRow("created-range")]
    [DataRow("updated-range")]
    [DataRow("last-event-range")]
    public void FiltersMatchingForeignProjectRows_FailClosedWithoutLeakingMatches(string filterKind)
    {
        var foreign = HostileRow(8, TenantA, ProjectB);
        var result = Page([foreign], filter: FilterFor(filterKind, foreign));

        AssertInvalidIsolation(result, "OperationStatusPageRowProjectMismatch");
    }

    [DataTestMethod]
    [DataRow("operation-id")]
    [DataRow("correlation-id")]
    [DataRow("surface")]
    [DataRow("reference")]
    public void ScopedRowsWithForeignFilterValues_ReturnNoMatchesWithoutForeignSignal(string filterKind)
    {
        var scoped = HostileRow(1, TenantA, ProjectA) with
        {
            SurfaceId = "scoped-surface",
            ReferenceId = "scoped-reference"
        };
        var foreign = HostileRow(2, TenantB, ProjectA) with
        {
            SurfaceId = "foreign-surface",
            ReferenceId = "foreign-reference"
        };
        var result = Page([scoped], filter: FilterFor(filterKind, foreign));

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationStatusPageResolutionStatus.NoMatches, result.ResolutionStatus);
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, result.MatchedCount);
        Assert.AreEqual(1, result.ScannedCount);
        Assert.IsFalse(result.HasMore);
        Assert.IsNull(result.NextCursor);
        AssertContains(result.ForbiddenAuthorityImplications, "empty page is not denial");
    }

    [TestMethod]
    public void RedactedInclusionCannotLeakForeignTenantMetadata()
    {
        var result = Page(
            [HostileRow(9, TenantB, ProjectA) with { IsRedacted = true, RedactionReason = "same-redaction" }],
            filter: new OperationStatusPageFilter { IncludeRedacted = true });

        AssertInvalidIsolation(result, "OperationStatusPageRowTenantMismatch");
    }

    [TestMethod]
    public void RedactedInclusionCannotLeakForeignProjectMetadata()
    {
        var result = Page(
            [HostileRow(9, TenantA, ProjectB) with { IsRedacted = true, RedactionReason = "same-redaction" }],
            filter: new OperationStatusPageFilter { IncludeRedacted = true });

        AssertInvalidIsolation(result, "OperationStatusPageRowProjectMismatch");
    }

    [TestMethod]
    public void ScopedRedactedRows_AreStillExcludedByDefaultAndIncludedOnlyWhenRequested()
    {
        var scopedRedacted = Row(10, TenantA, ProjectA) with
        {
            IsRedacted = true,
            RedactionReason = "scoped-redaction"
        };

        var excluded = Page([Row(1, TenantA, ProjectA), scopedRedacted]);
        var included = Page(
            [Row(1, TenantA, ProjectA), scopedRedacted],
            filter: new OperationStatusPageFilter { IncludeRedacted = true });

        AssertPage(excluded, "op_0000000000000001");
        AssertPage(included, "op_0000000000000001", "op_000000000000000a");
    }

    [TestMethod]
    public void CursorCopiedFromForeignTenantAgainstScopedRows_ReturnsNoForeignData()
    {
        var foreignCursor = CursorFor(HostileRow(3, TenantB, ProjectA));
        var result = Page([Row(1, TenantA, ProjectA), Row(2, TenantA, ProjectA)], cursor: foreignCursor);

        AssertCursorExhaustedWithoutLeak(result);
    }

    [TestMethod]
    public void CursorCopiedFromForeignProjectAgainstScopedRows_ReturnsNoForeignData()
    {
        var foreignCursor = CursorFor(HostileRow(3, TenantA, ProjectB));
        var result = Page([Row(1, TenantA, ProjectA), Row(2, TenantA, ProjectA)], cursor: foreignCursor);

        AssertCursorExhaustedWithoutLeak(result);
    }

    [TestMethod]
    public void ForeignCursorPlusMixedRows_FailsClosedBeforePaging()
    {
        var foreign = HostileRow(3, TenantB, ProjectA);
        var result = Page([Row(1, TenantA, ProjectA), foreign], cursor: CursorFor(foreign));

        AssertInvalidIsolation(result, "OperationStatusPageRowTenantMismatch");
    }

    [TestMethod]
    public void SameSortValuesAcrossTenants_DoNotLeakRows()
    {
        var result = Page(
            Row(1, TenantA, ProjectA) with { CreatedAtUtc = Stamp },
            Row(2, TenantB, ProjectA) with { CreatedAtUtc = Stamp });

        AssertInvalidIsolation(result, "OperationStatusPageRowTenantMismatch");
    }

    [TestMethod]
    public void SameSortValuesAcrossProjects_DoNotLeakRows()
    {
        var result = Page(
            Row(1, TenantA, ProjectA) with { CreatedAtUtc = Stamp },
            Row(2, TenantA, ProjectB) with { CreatedAtUtc = Stamp });

        AssertInvalidIsolation(result, "OperationStatusPageRowProjectMismatch");
    }

    [TestMethod]
    public void SameDiagnosticStatusesAcrossTenants_DoNotLeakRows()
    {
        var result = Page(
            HostileRow(1, TenantA, ProjectA),
            HostileRow(2, TenantB, ProjectA));

        AssertInvalidIsolation(result, "OperationStatusPageRowTenantMismatch");
    }

    [TestMethod]
    public void PageSizeOne_DoesNotLeakAdjacentForeignRow()
    {
        var result = Page(
            [Row(1, TenantA, ProjectA), Row(2, TenantB, ProjectA)],
            pageSize: 1);

        AssertInvalidIsolation(result, "OperationStatusPageRowTenantMismatch");
    }

    [TestMethod]
    public void DescendingSort_DoesNotLeakAdjacentForeignRow()
    {
        var result = Page(
            [Row(1, TenantA, ProjectA), Row(2, TenantB, ProjectA)],
            sortDirection: OperationStatusPageSortDirection.Descending);

        AssertInvalidIsolation(result, "OperationStatusPageRowTenantMismatch");
    }

    [TestMethod]
    public void HasMoreAndNextCursorDoNotConsiderForeignRowsWhenScopedInputIsValid()
    {
        var result = Page([Row(1, TenantA, ProjectA)], pageSize: 1);

        AssertPage(result, "op_0000000000000001");
        Assert.IsFalse(result.HasMore);
        Assert.IsNull(result.NextCursor);
    }

    [TestMethod]
    public void AmbiguousCursorDoesNotSelectForeignRows()
    {
        var scoped = HostileRow(1, TenantA, ProjectA);
        var result = Page(
            [scoped, scoped],
            cursor: CursorFor(scoped));

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(OperationStatusPageResolutionStatus.AmbiguousCursor, result.ResolutionStatus);
        Assert.AreEqual(0, result.Items.Count);
        Assert.IsNull(result.NextCursor);
        AssertContains(result.Issues, "OperationStatusPageCursorAmbiguous");
    }

    [TestMethod]
    public void ScopedHappyPath_StillReturnsScopedRows()
    {
        var result = Page(
            Row(1, TenantA, ProjectA),
            Row(2, TenantA, ProjectA));

        AssertPage(result, "op_0000000000000001", "op_0000000000000002");
    }

    [TestMethod]
    public void ScopedPagination_StillWorks()
    {
        var first = Page([Row(1, TenantA, ProjectA), Row(2, TenantA, ProjectA), Row(3, TenantA, ProjectA)], pageSize: 2);
        var second = Page([Row(1, TenantA, ProjectA), Row(2, TenantA, ProjectA), Row(3, TenantA, ProjectA)], pageSize: 2, cursor: first.NextCursor);

        AssertPage(first, "op_0000000000000001", "op_0000000000000002");
        AssertPage(second, "op_0000000000000003");
        Assert.IsFalse(second.HasMore);
    }

    [TestMethod]
    public void ScopedFiltering_StillWorks()
    {
        var target = HostileRow(4, TenantA, ProjectA);
        var result = Page(
            [Row(1, TenantA, ProjectA), target],
            filter: FilterFor("rollback-recovery", target));

        AssertPage(result, "op_0000000000000004");
        AssertContains(result.Items[0].MatchedFilterReasons, "RollbackRecoveryStatus");
    }

    [TestMethod]
    public void UnsafeFilterStillFailsClosed()
    {
        var result = Page(
            [Row(1, TenantA, ProjectA)],
            filter: new OperationStatusPageFilter { SurfaceId = "https://example.test/tenant-b" });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationStatusPageFilterSurfaceIdInvalid");
    }

    [TestMethod]
    public void InvalidCursorStillFailsClosed()
    {
        var cursor = CursorFor(Row(1, TenantA, ProjectA)) with
        {
            SortField = OperationStatusPageSortField.UpdatedAtUtc
        };

        var result = Page([Row(1, TenantA, ProjectA)], cursor: cursor);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationStatusPageCursorSortMismatch");
    }

    [TestMethod]
    public void UnboundedPageSizeStillFailsClosed()
    {
        var result = Page([Row(1, TenantA, ProjectA)], pageSize: OperationStatusPaginationValidator.MaxPageSize + 1);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "OperationStatusPageSizeExceedsMaximum");
    }

    [TestMethod]
    public void ListedFilteredStatusAndCursorRemainNonAuthority()
    {
        var result = Page(
            [HostileRow(1, TenantA, ProjectA)],
            filter: new OperationStatusPageFilter
            {
                ProjectedStatuses = [OperationProjectedStatusKind.BlockedObserved],
                RollbackRecoveryStatuses = [RollbackRecoveryReadModelStatus.MissingMaterial]
            });

        AssertPage(result, "op_0000000000000001");
        AssertContains(result.ForbiddenAuthorityImplications, "listed operation is not action allowed");
        AssertContains(result.ForbiddenAuthorityImplications, "filtered operation is not approved");
        AssertContains(result.ForbiddenAuthorityImplications, "matching status is not permission");
        AssertContains(result.ForbiddenAuthorityImplications, "cursor is not authority");
        AssertContains(result.ForbiddenAuthorityImplications, "page selection is not workflow selection");
    }

    [TestMethod]
    public void D01IdentityValidation_StillPasses()
    {
        Assert.IsTrue(OperationIdentityValidator.ValidateOperationId(Row(1, TenantA, ProjectA).OperationId).IsValid);
    }

    [TestMethod]
    public void D03CorrelationValidation_StillPasses()
    {
        var row = Row(1, TenantA, ProjectA);
        var result = OperationCorrelationValidator.ValidateLink(new OperationCorrelationLink
        {
            TenantId = row.TenantId,
            ProjectId = row.ProjectId,
            OperationId = row.OperationId,
            CorrelationId = row.CorrelationId,
            SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
            SurfaceId = "surface-d17",
            ObservedAtUtc = Stamp,
            Source = "d17-test"
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
    }

    [TestMethod]
    public void StaticScan_D17AddsNoApiSqlUiStoreExecutorMutationSurface()
    {
        var source = StripStrings(D17Source());

        AssertDoesNotContain(source, "Controller");
        AssertDoesNotContain(source, "Route(");
        AssertDoesNotContain(source, "MapGet");
        AssertDoesNotContain(source, "OpenApi");
        AssertDoesNotContain(source, "DbContext");
        AssertDoesNotContain(source, "SqlConnection");
        AssertDoesNotContain(source, "Repository");
        AssertDoesNotContain(source, "AuthorizationService");
        AssertDoesNotContain(source, "Authentication");
        AssertDoesNotContain(source, "File.ReadAllBytes");
        AssertDoesNotContain(source, "Directory.");
        AssertDoesNotContain(source, "Process.Start");
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
    public void StaticScan_D17DoesNotInvokeUpstreamResolversOrExecutors()
    {
        var source = StripStrings(D17Source());

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
        AssertDoesNotContain(source, "RetryExecutor");
        AssertDoesNotContain(source, "ResumeExecutor");
        AssertDoesNotContain(source, "RecoveryExecutor");
        AssertDoesNotContain(source, "RollbackExecutor");
    }

    [TestMethod]
    public void Receipt_RecordsTenantIsolationHardBoundary()
    {
        var receipt = File.ReadAllText(RepoPath("Docs", "receipts", "D17_OPERATION_STATUS_TENANT_ISOLATION_TESTS.md"));

        StringAssert.Contains(receipt, "The operation status tenant isolation tests prove that pagination and filtering over supplied status rows fail closed on cross-tenant or cross-project input and do not leak foreign rows, counts, cursors, filters, redaction metadata, diagnostic statuses, or action authority.");
        StringAssert.Contains(receipt, "Tenant isolation is not a UI convenience. It is a hard boundary.");
    }

    private static OperationStatusPageResult Page(
        params OperationStatusSummaryRow[] rows) =>
        Page(rows, pageSize: 25);

    private static OperationStatusPageResult Page(
        IReadOnlyList<OperationStatusSummaryRow> rows,
        int pageSize = 25,
        OperationStatusPageFilter? filter = null,
        OperationStatusPageCursor? cursor = null,
        OperationStatusPageSortDirection sortDirection = OperationStatusPageSortDirection.Ascending) =>
        OperationStatusPaginator.Page(new OperationStatusPageRequest
        {
            TenantId = TenantA,
            ProjectId = ProjectA,
            AsOfUtc = AsOfUtc,
            PageSize = pageSize,
            SortField = OperationStatusPageSortField.CreatedAtUtc,
            SortDirection = sortDirection,
            Filter = filter ?? new OperationStatusPageFilter(),
            Cursor = cursor,
            Rows = rows
        });

    private static OperationStatusSummaryRow Row(
        int index,
        string tenantId,
        string projectId)
    {
        var stamp = index.ToString("x16");
        return new OperationStatusSummaryRow
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = $"op_{stamp}",
            CorrelationId = $"corr_1{stamp[1..]}",
            ProjectedStatus = OperationProjectedStatusKind.Minted,
            CreatedAtUtc = Stamp.AddMinutes(index),
            UpdatedAtUtc = Stamp.AddMinutes(index + 10),
            LastEventAtUtc = Stamp.AddMinutes(index + 20),
            TimelineEventCount = index,
            SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
            SurfaceId = "shared-surface",
            ReferenceKind = OperationReferenceKind.StatusRecordId,
            ReferenceId = "shared-reference",
            MissingEvidenceStatus = MissingEvidenceResolutionStatus.Complete,
            ForbiddenActionStatus = ForbiddenActionResolutionStatus.NoForbiddenFactsObserved,
            ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.Resolved,
            EvidenceResolutionStatus = EvidenceResolutionStatus.Resolved,
            ValidationStalenessStatus = ValidationStalenessResolutionStatus.Assessed,
            PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.Assessed,
            WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.Assessed,
            InterruptedRunStatus = InterruptedRunReadModelStatus.NoInterruptionObserved,
            RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.NoMaterial,
            Source = "d17-test"
        };
    }

    private static OperationStatusSummaryRow HostileRow(
        int index,
        string tenantId,
        string projectId) =>
        Row(index, tenantId, projectId) with
        {
            ProjectedStatus = OperationProjectedStatusKind.BlockedObserved,
            MissingEvidenceStatus = MissingEvidenceResolutionStatus.MissingEvidence,
            ForbiddenActionStatus = ForbiddenActionResolutionStatus.Forbidden,
            ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.PartiallyResolved,
            EvidenceResolutionStatus = EvidenceResolutionStatus.PartiallyResolved,
            ValidationStalenessStatus = ValidationStalenessResolutionStatus.MixedStaleness,
            PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.MixedFreshness,
            WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.MixedFreshness,
            InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted,
            RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.MissingMaterial,
            SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
            SurfaceId = "shared-surface",
            ReferenceKind = OperationReferenceKind.StatusRecordId,
            ReferenceId = "shared-reference",
            CreatedAtUtc = Stamp,
            UpdatedAtUtc = Stamp.AddMinutes(10),
            LastEventAtUtc = Stamp.AddMinutes(20)
        };

    private static OperationStatusPageFilter FilterFor(
        string filterKind,
        OperationStatusSummaryRow row) =>
        filterKind switch
        {
            "operation-id" => new OperationStatusPageFilter { OperationId = row.OperationId },
            "correlation-id" => new OperationStatusPageFilter { CorrelationId = row.CorrelationId },
            "projected-status" => new OperationStatusPageFilter { ProjectedStatuses = [row.ProjectedStatus] },
            "surface" => new OperationStatusPageFilter { SurfaceKind = row.SurfaceKind, SurfaceId = row.SurfaceId },
            "reference" => new OperationStatusPageFilter { ReferenceKind = row.ReferenceKind, ReferenceId = row.ReferenceId },
            "created-range" => new OperationStatusPageFilter { CreatedFromUtc = row.CreatedAtUtc, CreatedToUtc = row.CreatedAtUtc },
            "updated-range" => new OperationStatusPageFilter { UpdatedFromUtc = row.UpdatedAtUtc, UpdatedToUtc = row.UpdatedAtUtc },
            "last-event-range" => new OperationStatusPageFilter { LastEventFromUtc = row.LastEventAtUtc, LastEventToUtc = row.LastEventAtUtc },
            "missing-evidence" => new OperationStatusPageFilter { MissingEvidenceStatuses = [row.MissingEvidenceStatus] },
            "forbidden-action" => new OperationStatusPageFilter { ForbiddenActionStatuses = [row.ForbiddenActionStatus] },
            "receipt-resolution" => new OperationStatusPageFilter { ReceiptResolutionStatuses = [row.ReceiptResolutionStatus] },
            "evidence-resolution" => new OperationStatusPageFilter { EvidenceResolutionStatuses = [row.EvidenceResolutionStatus] },
            "validation-staleness" => new OperationStatusPageFilter { ValidationStalenessStatuses = [row.ValidationStalenessStatus] },
            "patch-base" => new OperationStatusPageFilter { PatchBaseFreshnessStatuses = [row.PatchBaseFreshnessStatus] },
            "worktree-base-head" => new OperationStatusPageFilter { WorktreeBaseHeadFreshnessStatuses = [row.WorktreeBaseHeadFreshnessStatus] },
            "interrupted-run" => new OperationStatusPageFilter { InterruptedRunStatuses = [row.InterruptedRunStatus] },
            "rollback-recovery" => new OperationStatusPageFilter { RollbackRecoveryStatuses = [row.RollbackRecoveryStatus] },
            _ => throw new ArgumentOutOfRangeException(nameof(filterKind), filterKind, null)
        };

    private static OperationStatusPageCursor CursorFor(OperationStatusSummaryRow row) =>
        new()
        {
            SortField = OperationStatusPageSortField.CreatedAtUtc,
            SortDirection = OperationStatusPageSortDirection.Ascending,
            LastSortValue = row.CreatedAtUtc.ToString("O"),
            LastOperationId = row.OperationId,
            LastCorrelationId = row.CorrelationId
        };

    private static void AssertInvalidIsolation(
        OperationStatusPageResult result,
        string expectedIssue)
    {
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(OperationStatusPageResolutionStatus.InvalidRequest, result.ResolutionStatus);
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(0, result.MatchedCount);
        Assert.AreEqual(0, result.ScannedCount);
        Assert.IsFalse(result.HasMore);
        Assert.IsNull(result.NextCursor);
        AssertContains(result.Issues, expectedIssue);
    }

    private static void AssertCursorExhaustedWithoutLeak(OperationStatusPageResult result)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationStatusPageResolutionStatus.CursorExhausted, result.ResolutionStatus);
        Assert.AreEqual(0, result.Items.Count);
        Assert.IsFalse(result.HasMore);
        Assert.IsNull(result.NextCursor);
    }

    private static void AssertPage(
        OperationStatusPageResult result,
        params string[] operationIds)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationStatusPageResolutionStatus.PageReturned, result.ResolutionStatus);
        CollectionAssert.AreEqual(operationIds, result.Items.Select(static item => item.OperationId).ToArray());
        Assert.IsTrue(result.Items.All(static item => item.TenantId == TenantA));
        Assert.IsTrue(result.Items.All(static item => item.ProjectId == ProjectA));
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

    private static string D17Source() =>
        File.ReadAllText(RepoPath("IronDev.IntegrationTests", "BlockD17OperationStatusTenantIsolationTests.cs"));

    private static void AssertDoesNotContain(string source, string marker)
    {
        if (source.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail($"Unexpected marker '{marker}' found in D17 source.");
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
