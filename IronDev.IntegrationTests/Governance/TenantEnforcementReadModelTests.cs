using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("TenantIsolation")]
[TestCategory("ReadModel")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class TenantEnforcementReadModelTests
{
    private const int TenantA = 7001;
    private const int TenantB = 7002;
    private const string TenantAId = "tenant-h08-a";
    private const string TenantBId = "tenant-h08-b";
    private const string ProjectId = "project-h08";
    private const string OperationA = "operation-h08-a";
    private const string OperationB = "operation-h08-b";
    private const string EvidenceA = "evidence:h08:a";
    private const string EvidenceB = "evidence:h08:b";
    private const string ReceiptA = "receipt:h08:a";
    private const string ReceiptB = "receipt:h08:b";
    private const string PackageA = "patch-package:h08:a";
    private const string PackageB = "patch-package:h08:b";
    private const string ValidationA = "validation-result:h08:a";
    private const string ValidationB = "validation-result:h08:b";
    private const string HiddenTenantBMarker = "hidden-tenant-b-h08";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-26T08:00:00Z");
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-26T09:00:00Z");

    [TestMethod]
    public void OperationStatusReadModel_DoesNotReturnCrossTenantRecords()
    {
        var repository = new GovernedOperationStatusReadRepository(
        [
            OperationStatusRecord(TenantA, OperationA, "tenant-a-status-h08"),
            OperationStatusRecord(TenantB, OperationB, HiddenTenantBMarker)
        ]);

        var tenantAVisible = repository.GetByOperationId(OperationA, Scope(TenantA));
        var tenantAForeign = repository.GetByOperationId(OperationB, Scope(TenantA));
        var tenantBVisible = repository.GetByOperationId(OperationB, Scope(TenantB));

        Assert.IsTrue(tenantAVisible.Found);
        Assert.IsNotNull(tenantAVisible.Status);
        AssertReadOnly(tenantAVisible.Boundary);
        AssertReadOnlyStatus(tenantAVisible.Status);
        Assert.IsFalse(tenantAVisible.Status.Subject.Contains(HiddenTenantBMarker, StringComparison.OrdinalIgnoreCase));

        AssertNotFoundWithIssue(tenantAForeign, "OperationStatusTenantMismatch");

        Assert.IsTrue(tenantBVisible.Found);
        Assert.IsNotNull(tenantBVisible.Status);
        StringAssert.Contains(tenantBVisible.Status.Subject, HiddenTenantBMarker);
        AssertReadOnlyStatus(tenantBVisible.Status);
    }

    [TestMethod]
    public void OperationTimelineReadModel_DoesNotReturnCrossTenantEntries()
    {
        var repository = new OperationTimelineReadRepository(
        [
            TimelineEvent(TenantA, OperationA, "tenant-a-timeline-h08"),
            TimelineEvent(TenantB, OperationB, HiddenTenantBMarker)
        ]);

        var tenantAVisible = repository.GetByOperationId(OperationA, Scope(TenantA));
        var tenantAForeign = repository.GetByOperationId(OperationB, Scope(TenantA));
        var tenantBVisible = repository.GetByOperationId(OperationB, Scope(TenantB));

        Assert.IsTrue(tenantAVisible.Found);
        Assert.IsNotNull(tenantAVisible.Timeline);
        AssertReadOnly(tenantAVisible.Timeline.Boundary);
        Assert.IsFalse(tenantAVisible.Timeline.Entries.Any(entry =>
            entry.Summary.Contains(HiddenTenantBMarker, StringComparison.OrdinalIgnoreCase)));

        AssertNotFoundWithIssue(tenantAForeign, "TimelineEventTenantMismatch");

        Assert.IsTrue(tenantBVisible.Found);
        Assert.IsNotNull(tenantBVisible.Timeline);
        Assert.IsTrue(tenantBVisible.Timeline.Entries.Any(entry =>
            entry.Summary.Contains(HiddenTenantBMarker, StringComparison.OrdinalIgnoreCase)));
        AssertReadOnly(tenantBVisible.Boundary);
    }

    [TestMethod]
    public void EvidenceMetadataReadModel_DoesNotReturnCrossTenantEvidence()
    {
        var repository = new EvidenceMetadataReadRepository(
        [
            EvidenceRecord(TenantA, EvidenceA, "tenant-a-evidence-h08"),
            EvidenceRecord(TenantB, EvidenceB, HiddenTenantBMarker)
        ]);

        var tenantAVisible = repository.GetByEvidenceRef(EvidenceA, Scope(TenantA));
        var tenantAForeign = repository.GetByEvidenceRef(EvidenceB, Scope(TenantA));

        Assert.IsTrue(tenantAVisible.Found);
        Assert.IsNotNull(tenantAVisible.Metadata);
        Assert.IsTrue(tenantAVisible.Metadata.ReferenceOnly);
        Assert.IsFalse(tenantAVisible.Metadata.ContainsRawPayload);
        AssertReadOnly(tenantAVisible.Metadata.Boundary);
        AssertNotFoundWithIssue(tenantAForeign, "EvidenceMetadataTenantMismatch");
    }

    [TestMethod]
    public void ReceiptMetadataReadModel_DoesNotReturnCrossTenantReceipts()
    {
        var repository = new ReceiptMetadataReadRepository(
        [
            ReceiptRecord(TenantA, ReceiptA, "tenant-a-receipt-h08"),
            ReceiptRecord(TenantB, ReceiptB, HiddenTenantBMarker)
        ]);

        var tenantAVisible = repository.GetByReceiptRef(ReceiptA, Scope(TenantA));
        var tenantAForeign = repository.GetByReceiptRef(ReceiptB, Scope(TenantA));

        Assert.IsTrue(tenantAVisible.Found);
        Assert.IsNotNull(tenantAVisible.Metadata);
        Assert.IsTrue(tenantAVisible.Metadata.ReferenceOnly);
        Assert.IsFalse(tenantAVisible.Metadata.GrantsAuthority);
        Assert.IsFalse(tenantAVisible.Metadata.ContinuesWorkflow);
        AssertReadOnly(tenantAVisible.Metadata.Boundary);
        AssertNotFoundWithIssue(tenantAForeign, "ReceiptMetadataTenantMismatch");
    }

    [TestMethod]
    public void FrontendReadinessReadModel_DoesNotLeakTenantBackendTruth()
    {
        var canonical = new OperationStatusFrontendReadinessBackendTruthSource(
            new GovernedOperationStatusReadRepository(
            [
                OperationStatusRecord(TenantB, OperationB, HiddenTenantBMarker)
            ]));
        var fallback = new SeededStatusSource(OperationB, OperationStatus(OperationB, "tenant-a-fallback-h08"), TenantA);
        var api = new BackendFrontendReadinessReadApi(
            [canonical, fallback],
            new TestTenantContext(TenantA),
            () => AsOfUtc);

        var model = api.GetOperationStatus(OperationB);
        var readState = api.GetOperationStatusReadState(OperationB);

        Assert.IsNull(model);
        Assert.AreEqual(FrontendReadinessReadStateKind.NotVisible, readState.Kind);
        Assert.IsFalse(readState.HasData);
        Assert.IsFalse(readState.IsAuthorityGrant);
        Assert.IsFalse(readState.AllowsMutation);
        AssertReadOnly(readState.Boundary);
    }

    [TestMethod]
    public void TenantScopedReadModels_FailClosedWhenTenantIdIsMissingOrMismatched()
    {
        AssertNotFoundWithIssue(
            new GovernedOperationStatusReadRepository([OperationStatusRecord(TenantA, OperationA, "tenant-a-status-h08")])
                .GetByOperationId(OperationA, FrontendReadinessReadScope.Unscoped),
            "TenantScopedOperationStatusRequiresTenantScope");

        AssertNotFoundWithIssue(
            new GovernedOperationStatusReadRepository([OperationStatusRecord(null, OperationA, "tenantless-status-h08")])
                .GetByOperationId(OperationA, Scope(TenantA)),
            "TenantScopedOperationStatusRecordTenantRequired");

        AssertNotFoundWithIssue(
            new PatchPackageMetadataReadRepository([PatchPackageRecord(TenantB, PackageB, HiddenTenantBMarker)])
                .GetByPackageId(PackageB, Scope(TenantA)),
            "PatchPackageMetadataTenantMismatch");

        AssertNotFoundWithIssue(
            new ValidationResultMetadataReadRepository([ValidationRecord(TenantB, ValidationB, HiddenTenantBMarker)])
                .GetByValidationResultId(ValidationB, Scope(TenantA)),
            "ValidationResultMetadataTenantMismatch");
    }

    [TestMethod]
    public void NewDiagnosticReadModels_FailClosedOnTenantMismatch()
    {
        var interrupted = InterruptedRunReadModelAssembler.Assemble(new InterruptedRunReadModelRequest
        {
            TenantId = TenantAId,
            ProjectId = ProjectId,
            OperationId = OperationA,
            AsOfUtc = AsOfUtc,
            Checkpoints = [Checkpoint(TenantBId)],
            DiagnosticSnapshot = null
        });

        var rollback = RollbackRecoveryReadModelAssembler.Assemble(new RollbackRecoveryReadModelRequest
        {
            TenantId = TenantAId,
            ProjectId = ProjectId,
            OperationId = OperationA,
            AsOfUtc = AsOfUtc,
            Materials = [RollbackMaterial(TenantBId)],
            DiagnosticSnapshot = null
        });

        var freshness = WorktreeBaseHeadFreshnessReadModelAssembler.Assemble(new WorktreeBaseHeadFreshnessReadModelRequest
        {
            TenantId = TenantAId,
            ProjectId = ProjectId,
            OperationId = OperationA,
            AsOfUtc = AsOfUtc,
            Rules = [FreshnessRule(TenantAId)],
            Expectations = [FreshnessExpectation(TenantAId)],
            Observations = [FreshnessObservation(TenantBId)]
        });

        AssertInvalidWithIssue(interrupted, "InterruptedRunCheckpointTenantMismatch");
        AssertInvalidWithIssue(rollback, "RollbackRecoveryMaterialTenantMismatch");
        AssertInvalidWithIssue(freshness, "WorktreeBaseHeadObservationTenantMismatch");
    }

    [TestMethod]
    public void TenantScopedReadModels_DoNotGrantAuthority()
    {
        var status = new GovernedOperationStatusReadRepository([OperationStatusRecord(TenantA, OperationA, "tenant-a-status-h08")])
            .GetByOperationId(OperationA, Scope(TenantA));
        var evidence = new EvidenceMetadataReadRepository([EvidenceRecord(TenantA, EvidenceA, "tenant-a-evidence-h08")])
            .GetByEvidenceRef(EvidenceA, Scope(TenantA));
        var receipt = new ReceiptMetadataReadRepository([ReceiptRecord(TenantA, ReceiptA, "tenant-a-receipt-h08")])
            .GetByReceiptRef(ReceiptA, Scope(TenantA));
        var interrupted = InterruptedRunReadModelAssembler.Assemble(new InterruptedRunReadModelRequest
        {
            TenantId = TenantAId,
            ProjectId = ProjectId,
            OperationId = OperationA,
            AsOfUtc = AsOfUtc,
            Checkpoints = [Checkpoint(TenantAId)],
            DiagnosticSnapshot = null
        });

        AssertReadOnlyStatus(status.Status!);
        AssertReadOnly(evidence.Metadata!.Boundary);
        AssertReadOnly(receipt.Metadata!.Boundary);
        Assert.IsTrue(evidence.Metadata.ReferenceOnly);
        Assert.IsTrue(receipt.Metadata.ReferenceOnly);
        Assert.IsFalse(receipt.Metadata.GrantsAuthority);
        Assert.IsFalse(receipt.Metadata.ContinuesWorkflow);
        AssertContains(interrupted.ForbiddenAuthorityImplications, "interrupted-run read model is not retry permission");
        AssertContains(interrupted.ForbiddenAuthorityImplications, "interrupted-run read model is not rollback");
    }

    [TestMethod]
    public void Receipt_RecordsTenantBoundaryAndLimitations()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "H08_TENANT_ENFORCEMENT_READ_MODEL_TESTS.md"));

        AssertContainsAll(
            receipt,
            "H08 does not add a SQL migration.",
            "H08 does not alter tables.",
            "H08 does not add TenantId columns.",
            "H08 does not add indexes.",
            "H08 does not alter stored procedures.",
            "H08 does not change API/CLI/UI behavior.",
            "H08 does not change workflow/source-apply/rollback/release/deployment authority.",
            "H08 does not change Weaviate behavior.",
            "Tenant filters protect read scope only.",
            "Tenant filtering is not approval.",
            "Tenant filtering is not policy satisfaction.",
            "Tenant filtering is not source-apply authority.",
            "Tenant filtering is not workflow continuation authority.",
            "Tenant filtering is not release readiness.",
            "Tenant filtering is not deployment readiness.",
            "A tenant-scoped lie is still a lie.",
            "GovernedOperationStatusReadRepository: TenantEnforced",
            "OperationTimelineReadRepository: TenantEnforced",
            "EvidenceMetadataReadRepository: TenantEnforced",
            "ReceiptMetadataReadRepository: TenantEnforced",
            "PatchPackageMetadataReadRepository: TenantEnforced",
            "ValidationResultMetadataReadRepository: TenantEnforced",
            "FrontendReadinessReadModels snapshot API: TenantNotApplicable",
            "InterruptedRunReadModelValidator: TenantEnforced",
            "RollbackRecoveryReadModelValidator: TenantEnforced",
            "WorktreeBaseHeadFreshnessReadModelValidator: TenantEnforced");
    }

    private static GovernedOperationStatusReadRecord OperationStatusRecord(
        int? tenantId,
        string operationId,
        string subject) =>
        new()
        {
            OperationId = operationId,
            Status = OperationStatus(operationId, subject),
            IsTenantScoped = true,
            TenantId = tenantId
        };

    private static GovernedOperationStatus OperationStatus(
        string operationId,
        string subject) =>
        new()
        {
            OperationId = operationId,
            OperationKind = "H08ReadModelStatus",
            Subject = $"project:{ProjectId} subject:{subject}",
            State = GovernedOperationState.Blocked,
            BlockedReasons = ["H08StatusBlocked"],
            MissingEvidence = ["tenant-scoped-status-evidence:h08"],
            NextSafeActions = ["inspect tenant-scoped status evidence"],
            ForbiddenActions =
            [
                "do not approve from tenant-scoped status",
                "do not satisfy policy from tenant-scoped status",
                "do not apply source from tenant-scoped status",
                "do not continue workflow from tenant-scoped status",
                "do not release or deploy from tenant-scoped status"
            ],
            EvidenceRefs = [$"status-evidence:{operationId}"],
            ReceiptRefs = [$"status-receipt:{operationId}"],
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1)
        };

    private static OperationTimelineEventReadRecord TimelineEvent(
        int tenantId,
        string operationId,
        string summary) =>
        new()
        {
            OperationId = operationId,
            EntryId = $"timeline-entry:{operationId}",
            EventKind = "H08TimelineObserved",
            Summary = summary,
            IsTenantScoped = true,
            TenantId = tenantId,
            ObservedAtUtc = ObservedAtUtc,
            EvidenceRefs = [$"timeline-evidence:{operationId}"],
            ReceiptRefs = [$"timeline-receipt:{operationId}"]
        };

    private static EvidenceMetadataReadRecord EvidenceRecord(
        int tenantId,
        string evidenceRef,
        string summary) =>
        new()
        {
            EvidenceRef = evidenceRef,
            EvidenceKind = "H08EvidenceMetadata",
            Summary = summary,
            IsTenantScoped = true,
            TenantId = tenantId,
            ContainsRawPayload = false,
            ContainsPrivateMaterial = false,
            ContainsPatchPayload = false,
            ContainsHiddenMaterial = false,
            Warnings = ["Evidence metadata is reference-only."],
            AuthorityWarnings = ["Evidence metadata is not authority."],
            ObservedAtUtc = ObservedAtUtc
        };

    private static ReceiptMetadataReadRecord ReceiptRecord(
        int tenantId,
        string receiptRef,
        string summary) =>
        new()
        {
            ReceiptRef = receiptRef,
            ReceiptKind = "H08ReceiptMetadata",
            Summary = summary,
            OperationId = OperationA,
            OperationKind = "H08Read",
            Subject = ProjectId,
            IsTenantScoped = true,
            TenantId = tenantId,
            ContainsRawPayload = false,
            ContainsPrivateMaterial = false,
            ContainsPatchPayload = false,
            ContainsHiddenMaterial = false,
            ClaimsAuthority = false,
            ClaimsContinuation = false,
            ClaimsApproval = false,
            ClaimsPolicySatisfaction = false,
            Warnings = ["Receipt metadata is reference-only."],
            AuthorityWarnings = ["Receipt metadata is not authority."],
            ObservedAtUtc = ObservedAtUtc
        };

    private static PatchPackageMetadataReadRecord PatchPackageRecord(
        int tenantId,
        string packageId,
        string marker) =>
        new()
        {
            PackageId = packageId,
            Repository = $"repo-{marker}",
            Branch = "main",
            RunId = $"run-{marker}",
            PatchHash = $"sha256:{marker}",
            ProposedFilePaths = ["IronDev.Core/Governance/H08.cs"],
            ArtifactRefs = [$"artifact:{marker}"],
            EvidenceRefs = [$"evidence:{marker}"],
            ReceiptRefs = [$"receipt:{marker}"],
            ReviewSummaryRef = $"review:{marker}",
            KnownRisksRef = $"risk:{marker}",
            IsTenantScoped = true,
            TenantId = tenantId,
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            Warnings = ["Patch package metadata is reference-only."],
            AuthorityWarnings = ["Patch package metadata is not source apply authority."]
        };

    private static ValidationResultMetadataReadRecord ValidationRecord(
        int tenantId,
        string validationResultId,
        string marker) =>
        new()
        {
            ValidationResultId = validationResultId,
            Repository = $"repo-{marker}",
            Branch = "main",
            RunId = $"run-{marker}",
            PatchHash = $"sha256:{marker}",
            Outcome = "Passed",
            WhatRan = ["H08 focused"],
            WhatPassed = ["H08 focused"],
            WhatFailed = [],
            WhatWasSkipped = [],
            EvidenceRefs = [$"evidence:{marker}"],
            ReceiptRefs = [$"receipt:{marker}"],
            FreshnessKnown = true,
            IsStale = false,
            IsTenantScoped = true,
            TenantId = tenantId,
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            Warnings = ["Validation metadata is reference-only."],
            AuthorityWarnings = ["Validation metadata is not approval."]
        };

    private static InterruptedRunCheckpointObservation Checkpoint(string tenantId) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = ProjectId,
            OperationId = OperationA,
            CorrelationId = "correlation-h08",
            CheckpointId = "checkpoint-h08",
            CheckpointKind = InterruptedRunCheckpointKind.SourceApplyStarted,
            AppendPosition = 1,
            ObservedAtUtc = ObservedAtUtc,
            RecordedAtUtc = ObservedAtUtc.AddMinutes(1),
            SurfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
            SurfaceId = "timeline-h08",
            ReferenceKind = OperationReferenceKind.TimelineEventId,
            ReferenceId = "timeline-ref-h08",
            Source = "h08-test"
        };

    private static RollbackRecoveryMaterialObservation RollbackMaterial(string tenantId) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = ProjectId,
            OperationId = OperationA,
            CorrelationId = "correlation-h08",
            MaterialId = "rollback-material-h08",
            MaterialKind = RollbackRecoveryMaterialKind.RollbackPlan,
            AppendPosition = 1,
            ObservedAtUtc = ObservedAtUtc,
            RecordedAtUtc = ObservedAtUtc.AddMinutes(1),
            SurfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
            SurfaceId = "timeline-h08",
            ReferenceKind = OperationReferenceKind.ReceiptId,
            ReferenceId = "receipt-ref-h08",
            Source = "h08-test"
        };

    private static WorktreeBaseHeadFreshnessRule FreshnessRule(string tenantId) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = ProjectId,
            OperationId = OperationA,
            RuleId = "freshness-rule-h08",
            ObservationFreshFor = TimeSpan.FromMinutes(15),
            ObservationExpiresAfter = TimeSpan.FromMinutes(30),
            Source = "h08-test",
            CreatedAtUtc = ObservedAtUtc
        };

    private static ExpectedWorktreeBaseHeadMetadata FreshnessExpectation(string tenantId) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = ProjectId,
            OperationId = OperationA,
            CorrelationId = "correlation-h08",
            ExpectationId = "expectation-h08",
            RepositoryIdentity = "BigDaddyDread-code/IronDeveloper",
            BaseBranch = "main",
            BaseCommitSha = "abc123h08",
            HeadBranch = "feature/h08",
            HeadCommitSha = "def456h08",
            ExpectedWorktreeState = WorktreeStateKind.Clean,
            ExpectedHeadState = HeadStateKind.Attached,
            CapturedAtUtc = ObservedAtUtc,
            RecordedAtUtc = ObservedAtUtc.AddMinutes(1),
            SurfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
            SurfaceId = "timeline-h08",
            ReferenceKind = OperationReferenceKind.TimelineEventId,
            ReferenceId = "timeline-ref-h08",
            Source = "h08-test"
        };

    private static ObservedWorktreeBaseHeadMetadata FreshnessObservation(string tenantId) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = ProjectId,
            OperationId = OperationA,
            CorrelationId = "correlation-h08",
            ObservationId = "observation-h08",
            RepositoryIdentity = "BigDaddyDread-code/IronDeveloper",
            WorktreeState = WorktreeStateKind.Clean,
            HeadState = HeadStateKind.Attached,
            BaseBranch = "main",
            BaseCommitSha = "abc123h08",
            HeadBranch = "feature/h08",
            HeadCommitSha = "def456h08",
            HasUncommittedChanges = false,
            HasUntrackedFiles = false,
            HasConflicts = false,
            ObservedAtUtc = ObservedAtUtc,
            RecordedAtUtc = ObservedAtUtc.AddMinutes(2),
            SurfaceKind = OperationCorrelationSurfaceKind.TimelineEvent,
            SurfaceId = "timeline-h08",
            ReferenceKind = OperationReferenceKind.TimelineEventId,
            ReferenceId = "timeline-ref-h08",
            Source = "h08-test"
        };

    private static FrontendReadinessReadScope Scope(int tenantId) => new(tenantId);

    private static void AssertNotFoundWithIssue(
        GovernedOperationStatusReadResult result,
        string issue)
    {
        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Status);
        AssertContains(result.Issues, issue);
        AssertReadOnly(result.Boundary);
    }

    private static void AssertNotFoundWithIssue(
        OperationTimelineReadResult result,
        string issue)
    {
        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Timeline);
        AssertContains(result.Issues, issue);
        AssertReadOnly(result.Boundary);
    }

    private static void AssertNotFoundWithIssue(
        EvidenceMetadataReadResult result,
        string issue)
    {
        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, issue);
        AssertReadOnly(result.Boundary);
    }

    private static void AssertNotFoundWithIssue(
        ReceiptMetadataReadResult result,
        string issue)
    {
        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, issue);
        AssertReadOnly(result.Boundary);
    }

    private static void AssertNotFoundWithIssue(
        PatchPackageMetadataReadResult result,
        string issue)
    {
        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, issue);
        AssertReadOnly(result.Boundary);
    }

    private static void AssertNotFoundWithIssue(
        ValidationResultMetadataReadResult result,
        string issue)
    {
        Assert.IsFalse(result.Found);
        Assert.IsNull(result.Metadata);
        AssertContains(result.Issues, issue);
        AssertReadOnly(result.Boundary);
    }

    private static void AssertInvalidWithIssue(
        InterruptedRunReadModel result,
        string issue)
    {
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(InterruptedRunReadModelStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, issue);
        AssertContains(result.ForbiddenAuthorityImplications, "interrupted-run read model is not retry permission");
    }

    private static void AssertInvalidWithIssue(
        RollbackRecoveryReadModel result,
        string issue)
    {
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(RollbackRecoveryReadModelStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, issue);
        AssertContains(result.ForbiddenAuthorityImplications, "rollback/recovery read model is not rollback execution");
    }

    private static void AssertInvalidWithIssue(
        WorktreeBaseHeadFreshnessReadModel result,
        string issue)
    {
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(WorktreeBaseHeadFreshnessResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, issue);
        AssertContains(result.ForbiddenAuthorityImplications, "worktree/base/head freshness read model is not source apply");
    }

    private static void AssertReadOnlyStatus(GovernedOperationStatus status)
    {
        AssertContains(status.ForbiddenActions, "do not approve from tenant-scoped status");
        AssertContains(status.ForbiddenActions, "do not satisfy policy from tenant-scoped status");
        AssertContains(status.ForbiddenActions, "do not apply source from tenant-scoped status");
        AssertContains(status.ForbiddenActions, "do not continue workflow from tenant-scoped status");
        AssertContains(status.ForbiddenActions, "do not release or deploy from tenant-scoped status");
    }

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

    private static void AssertContains<T>(
        IEnumerable<T> values,
        T expected)
    {
        if (!values.Contains(expected))
        {
            Assert.Fail($"Expected '{expected}' in: {string.Join(", ", values)}");
        }
    }

    private static void AssertContainsAll(
        string text,
        params string[] expected)
    {
        foreach (var value in expected)
            StringAssert.Contains(text, value);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }

    private sealed class SeededStatusSource(
        string operationId,
        GovernedOperationStatus status,
        int tenantId) : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-h08-status";
        public override int? TenantId => tenantId;

        public override GovernedOperationStatus? GetOperationStatus(string requestedOperationId) =>
            string.Equals(requestedOperationId, operationId, StringComparison.OrdinalIgnoreCase)
                ? status
                : null;
    }
}
