using IronDev.Api.Controllers;
using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA10FrontendReadinessForbiddenActionPreservationTests
{
    private const string OperationId = "operation-a10";
    private const string EvidenceRef = "evidence:a10";
    private const string ReceiptRef = "receipt:a10";
    private const string PackageId = "patch-package:a10";
    private const string ValidationResultId = "validation-result:a10";
    private const string ForbiddenAction = "do not execute hidden mutation from frontend readiness";
    private const string FallbackForbiddenAction = "do not use fallback action text";
    private const string MissingEvidence = "missing-a10-governed-evidence";
    private const string FallbackMissingEvidence = "missing-fallback-evidence";
    private const string ValidationWarning = "Validation evidence is not approval.";
    private const string MemoryWarning = "Memory text is not approval authority.";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-23T12:00:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = Now.AddMinutes(-5);
    private static readonly DateTimeOffset FutureExpiry = Now.AddMinutes(30);
    private static readonly DateTimeOffset PastExpiry = Now.AddMinutes(-1);

    [TestMethod]
    public void FrontendReadiness_OperationStatus_PreservesForbiddenActions()
    {
        var envelope = OperationStatusEnvelope(Status());

        AssertContains(envelope.Data!.ForbiddenActions, ForbiddenAction);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatus_PreservesMissingEvidence()
    {
        var envelope = OperationStatusEnvelope(Status());

        AssertContains(envelope.Data!.MissingEvidence, MissingEvidence);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatus_PreservesAuthorityWarnings()
    {
        var envelope = OperationStatusEnvelope(Status(evidenceRefs: ["validation:a10"]));

        AssertContains(envelope.Data!.AuthorityWarnings, ValidationWarning);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatus_CompactModePreservesForbiddenActions()
    {
        var envelope = OperationStatusEnvelope(Status(), compact: true);

        AssertContains(envelope.Data!.ForbiddenActions, ForbiddenAction);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatus_CompactModePreservesMissingEvidence()
    {
        var envelope = OperationStatusEnvelope(Status(), compact: true);

        AssertContains(envelope.Data!.MissingEvidence, MissingEvidence);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatus_CompactModePreservesAuthorityWarnings()
    {
        var envelope = OperationStatusEnvelope(Status(evidenceRefs: ["validation:a10"]), compact: true);

        AssertContains(envelope.Data!.AuthorityWarnings, ValidationWarning);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatus_BlockedStatePreservesForbiddenActions()
    {
        var envelope = OperationStatusEnvelope(Status(state: GovernedOperationState.Blocked));

        AssertContains(envelope.Data!.ForbiddenActions, ForbiddenAction);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatus_InvalidStatePreservesForbiddenActions()
    {
        var envelope = OperationStatusEnvelopeWithState(
            Status(),
            FrontendReadinessReadState.Invalid("StoredOperationStatusInvalid"));

        Assert.AreEqual(FrontendReadinessReadStateKind.Invalid, envelope.ReadState.Kind);
        AssertContains(envelope.Data!.ForbiddenActions, ForbiddenAction);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatus_StaleStatePreservesForbiddenActions()
    {
        var envelope = OperationStatusEnvelopeWithState(
            Status(),
            FrontendReadinessReadState.Stale("OperationStatusStale"));

        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, envelope.ReadState.Kind);
        AssertContains(envelope.Data!.ForbiddenActions, ForbiddenAction);
    }

    [TestMethod]
    public void FrontendReadiness_OperationStatus_ExpiredStatePreservesForbiddenActions()
    {
        var envelope = OperationStatusEnvelope(Status(expiresAtUtc: PastExpiry));

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, envelope.ReadState.Kind);
        AssertContains(envelope.Data!.ForbiddenActions, ForbiddenAction);
    }

    [TestMethod]
    public void FrontendReadiness_AvailableStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.Available());

    [TestMethod]
    public void FrontendReadiness_NotFoundStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.NotFound("MissingRecord"));

    [TestMethod]
    public void FrontendReadiness_EmptyStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.Empty("NoVisibleEntries"));

    [TestMethod]
    public void FrontendReadiness_RedactedStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.Redacted("UnsafeRecord"));

    [TestMethod]
    public void FrontendReadiness_UnavailableStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.Unavailable());

    [TestMethod]
    public void FrontendReadiness_InvalidStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.Invalid("InvalidRecord"));

    [TestMethod]
    public void FrontendReadiness_ExpiredStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.Expired("ExpiredRecord"));

    [TestMethod]
    public void FrontendReadiness_StaleStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.Stale("StaleRecord"));

    [TestMethod]
    public void FrontendReadiness_NotVisibleStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.NotVisible());

    [TestMethod]
    public void FrontendReadiness_UnknownStatePreservesReadStateWarnings() =>
        AssertReadStateWarnings(FrontendReadinessReadState.Unknown());

    [TestMethod]
    public void FrontendReadiness_CurrentFreshnessPreservesWarnings() =>
        AssertFreshnessWarnings(FrontendReadinessFreshnessClassifier.Current("Current", ObservedAtUtc, FutureExpiry, Now));

    [TestMethod]
    public void FrontendReadiness_StaleFreshnessPreservesWarnings() =>
        AssertFreshnessWarnings(FrontendReadinessFreshnessClassifier.Stale("Stale", ObservedAtUtc, FutureExpiry, Now));

    [TestMethod]
    public void FrontendReadiness_ExpiredFreshnessPreservesWarnings() =>
        AssertFreshnessWarnings(FrontendReadinessFreshnessClassifier.Expired("Expired", ObservedAtUtc, PastExpiry, Now));

    [TestMethod]
    public void FrontendReadiness_UnknownFreshnessPreservesWarnings() =>
        AssertFreshnessWarnings(FrontendReadinessFreshnessClassifier.Unknown("Unknown", null, null, Now));

    [TestMethod]
    public void FrontendReadiness_NotApplicableFreshnessPreservesWarnings() =>
        AssertFreshnessWarnings(FrontendReadinessFreshnessClassifier.NotApplicable("NotApplicable", Now));

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_PreservesReadOnlyWarning()
    {
        var envelope = OperationStatusEnvelope(Status());

        AssertContains(envelope.Warnings, "Frontend readiness read endpoints are read-only.");
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_PreservesNotApprovalWarning()
    {
        var envelope = OperationStatusEnvelope(Status());

        AssertContains(envelope.Warnings, "Frontend readiness output is not approval, policy satisfaction, execution authority, memory promotion, or workflow continuation.");
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_PreservesCompactModeWarning()
    {
        var envelope = OperationStatusEnvelope(Status(), compact: true);

        AssertContains(envelope.Warnings, "Compact mode was requested but authority-critical fields are still returned.");
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_PreservesBoundary()
    {
        var envelope = OperationStatusEnvelope(Status());

        AssertReadOnly(envelope.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_PreservesReadState()
    {
        var envelope = OperationStatusEnvelope(Status(expiresAtUtc: PastExpiry));

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_PreservesFreshness()
    {
        var envelope = OperationStatusEnvelope(Status(expiresAtUtc: PastExpiry));

        Assert.AreEqual(envelope.ReadState.Freshness, envelope.Freshness);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Expired, envelope.Freshness.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_PreservesErrorsForNullData()
    {
        var envelope = NullOperationStatusEnvelope(FrontendReadinessReadState.NotFound("OperationStatusNotFound", MissingEvidence));

        Assert.IsTrue(envelope.Errors.Count > 0);
        Assert.AreEqual("FRONTEND_READINESS_NOTFOUND", envelope.Errors[0].Code);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_NullDataPreservesReason()
    {
        var envelope = NullOperationStatusEnvelope(FrontendReadinessReadState.NotFound("OperationStatusNotFound", MissingEvidence));

        AssertContains(envelope.ReadState.Reasons, "OperationStatusNotFound");
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_NullDataPreservesMissingRefs()
    {
        var envelope = NullOperationStatusEnvelope(FrontendReadinessReadState.NotFound("OperationStatusNotFound", MissingEvidence));

        AssertContains(envelope.ReadState.MissingRefs, MissingEvidence);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_CompactModeCannotHideWarnings()
    {
        var envelope = OperationStatusEnvelope(Status(), compact: true);

        AssertReadStateWarnings(envelope.ReadState);
        AssertFreshnessWarnings(envelope.Freshness);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_CompactModeCannotHideReadState()
    {
        var envelope = OperationStatusEnvelope(Status(expiresAtUtc: PastExpiry), compact: true);

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_CompactModeCannotHideFreshness()
    {
        var envelope = OperationStatusEnvelope(Status(expiresAtUtc: PastExpiry), compact: true);

        Assert.AreEqual(FrontendReadinessFreshnessKind.Expired, envelope.Freshness.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelope_CompactModeCannotHideBoundary()
    {
        var envelope = OperationStatusEnvelope(Status(), compact: true);

        AssertReadOnly(envelope.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_CanonicalForbiddenActionsWinOverFallback()
    {
        var envelope = BackendOperationStatusEnvelope(
            OperationStatusSource(Status()),
            OperationStatusSource(Status(forbiddenActions: [FallbackForbiddenAction])));

        AssertContains(envelope.Data!.ForbiddenActions, ForbiddenAction);
        AssertNotContains(envelope.Data.ForbiddenActions, FallbackForbiddenAction);
    }

    [TestMethod]
    public void FrontendReadiness_CanonicalMissingEvidenceWinsOverFallback()
    {
        var envelope = BackendOperationStatusEnvelope(
            OperationStatusSource(Status()),
            OperationStatusSource(Status(missingEvidence: [FallbackMissingEvidence])));

        AssertContains(envelope.Data!.MissingEvidence, MissingEvidence);
        AssertNotContains(envelope.Data.MissingEvidence, FallbackMissingEvidence);
    }

    [TestMethod]
    public void FrontendReadiness_CanonicalAuthorityWarningsWinOverFallback()
    {
        var envelope = BackendOperationStatusEnvelope(
            OperationStatusSource(Status(evidenceRefs: ["validation:a10"])),
            OperationStatusSource(Status(evidenceRefs: ["memory:a10"])));

        AssertContains(envelope.Data!.AuthorityWarnings, ValidationWarning);
        AssertNotContains(envelope.Data.AuthorityWarnings, MemoryWarning);
    }

    [TestMethod]
    public void FrontendReadiness_RedactedCanonicalDoesNotFallbackToCleanerData()
    {
        var api = BackendApi(
            EvidenceSource(Evidence(redacted: true)),
            EvidenceSource(Evidence(summary: "fallback clean evidence")));

        var envelope = Envelope(Controller(api).GetEvidenceMetadata(EvidenceRef));

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, envelope.ReadState.Kind);
        Assert.AreNotEqual("fallback clean evidence", envelope.Data!.Summary);
    }

    [TestMethod]
    public void FrontendReadiness_StaleCanonicalDoesNotFallbackToFreshData()
    {
        var api = BackendApi(
            ValidationSource(Validation(isStale: true)),
            ValidationSource(Validation(isStale: false)));

        var envelope = Envelope(Controller(api).GetValidationResultMetadata(ValidationResultId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, envelope.ReadState.Kind);
        Assert.IsTrue(envelope.Data!.IsStale);
    }

    [TestMethod]
    public void FrontendReadiness_ExpiredCanonicalDoesNotFallbackToCurrentData()
    {
        var api = BackendApi(
            PatchPackageSource(PatchPackage(expiresAtUtc: PastExpiry)),
            PatchPackageSource(PatchPackage(expiresAtUtc: FutureExpiry)));

        var envelope = Envelope(Controller(api).GetPatchPackageMetadata(PackageId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_InvalidCanonicalDoesNotFallbackToValidData()
    {
        var api = BackendApi(
            StateSource<GovernedOperationStatus>(FrontendReadinessReadState.Invalid("StoredOperationStatusInvalid")),
            OperationStatusSource(Status()));

        var envelope = Envelope(Controller(api).GetOperationStatus(OperationId));

        Assert.IsNull(envelope.Data);
        Assert.AreEqual(FrontendReadinessReadStateKind.Invalid, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_NotVisibleCanonicalDoesNotFallbackToVisibleData()
    {
        var api = BackendApi(
            OperationStatusSource(Status(), visible: false),
            OperationStatusSource(Status(forbiddenActions: [FallbackForbiddenAction])));

        var envelope = Envelope(Controller(api).GetOperationStatus(OperationId));

        Assert.IsNull(envelope.Data);
        Assert.AreEqual(FrontendReadinessReadStateKind.NotVisible, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_UnavailableCanonicalDoesNotFallbackToFallbackData()
    {
        var api = BackendApi(
            ThrowingSource(),
            OperationStatusSource(Status(forbiddenActions: [FallbackForbiddenAction])));

        var envelope = Envelope(Controller(api).GetOperationStatus(OperationId));

        Assert.IsNull(envelope.Data);
        Assert.AreEqual(FrontendReadinessReadStateKind.Unavailable, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_TrueNotFoundMayFallbackWithoutDroppingBoundaryWarnings()
    {
        var api = BackendApi(
            StateSource<GovernedOperationStatus>(FrontendReadinessReadState.NotFound("OperationStatusNotFound")),
            OperationStatusSource(Status()));

        var envelope = Envelope(Controller(api).GetOperationStatus(OperationId));

        Assert.IsNotNull(envelope.Data);
        AssertReadStateWarnings(envelope.ReadState);
        AssertContains(envelope.Warnings, "Frontend readiness read endpoints are read-only.");
    }

    [TestMethod]
    public void FrontendReadiness_EvidenceMetadata_PreservesReferenceOnlyWarnings()
    {
        var envelope = Envelope(Controller(Api(Evidence(warnings: ["custom evidence warning"]))).GetEvidenceMetadata(EvidenceRef));

        Assert.IsTrue(envelope.Data!.ReferenceOnly);
        AssertContains(envelope.Data.Warnings, "custom evidence warning");
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadata_PreservesReferenceOnlyWarnings()
    {
        var envelope = Envelope(Controller(Api(Receipt(warnings: ["custom receipt warning"]))).GetReceiptMetadata(ReceiptRef));

        Assert.IsTrue(envelope.Data!.ReferenceOnly);
        AssertContains(envelope.Data.Warnings, "custom receipt warning");
    }

    [TestMethod]
    public void FrontendReadiness_ReceiptMetadata_PreservesNoAuthorityFlags()
    {
        var envelope = Envelope(Controller(Api(Receipt())).GetReceiptMetadata(ReceiptRef));

        Assert.IsFalse(envelope.Data!.GrantsAuthority);
        Assert.IsFalse(envelope.Data.ContinuesWorkflow);
    }

    [TestMethod]
    public void FrontendReadiness_Timeline_PreservesEventRefsWithoutAuthority()
    {
        var envelope = Envelope(Controller(Api(Timeline(entries: [TimelineEntry()]))).GetOperationTimeline(OperationId));

        AssertContains(envelope.Data!.Entries.Single().EvidenceRefs, "evidence:a10:timeline");
        AssertContains(envelope.Data.Entries.Single().ReceiptRefs, "receipt:a10:timeline");
        AssertNoAuthority(envelope.ReadState);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageMetadata_PreservesRefsWithoutApplyAuthority()
    {
        var envelope = Envelope(Controller(Api(PatchPackage())).GetPatchPackageMetadata(PackageId));

        AssertContains(envelope.Data!.EvidenceRefs, "evidence:a10");
        AssertContains(envelope.Data.ReceiptRefs, "receipt:a10");
        AssertNoAuthority(envelope.ReadState);
    }

    [TestMethod]
    public void FrontendReadiness_PatchPackageArtifacts_PreservesAuthorityWarnings()
    {
        var envelope = Envelope(Controller(Api(Artifacts(authorityWarnings: ["artifact warning survives"]))).GetPatchPackageArtifacts(PackageId));

        AssertContains(envelope.Data!.AuthorityWarnings, "artifact warning survives");
    }

    [TestMethod]
    public void FrontendReadiness_ValidationMetadata_PreservesEvidenceAndReceiptRefsWithoutApproval()
    {
        var envelope = Envelope(Controller(Api(Validation())).GetValidationResultMetadata(ValidationResultId));

        AssertContains(envelope.Data!.EvidenceRefs, "evidence:a10");
        AssertContains(envelope.Data.ReceiptRefs, "receipt:a10");
        AssertNoAuthority(envelope.ReadState);
    }

    [TestMethod]
    public void FrontendReadiness_AllReadStates_KeepCanCreateApprovalFalse() =>
        AssertAllReadStates(boundary => Assert.IsFalse(boundary.CanCreateApproval));

    [TestMethod]
    public void FrontendReadiness_AllReadStates_KeepCanSatisfyPolicyFalse() =>
        AssertAllReadStates(boundary => Assert.IsFalse(boundary.CanSatisfyPolicy));

    [TestMethod]
    public void FrontendReadiness_AllReadStates_KeepCanExecuteFalse() =>
        AssertAllReadStates(boundary => Assert.IsFalse(boundary.CanExecute));

    [TestMethod]
    public void FrontendReadiness_AllReadStates_KeepCanMutateSourceFalse() =>
        AssertAllReadStates(boundary => Assert.IsFalse(boundary.CanMutateSource));

    [TestMethod]
    public void FrontendReadiness_AllReadStates_KeepCanCommitPushPrFalse() =>
        AssertAllReadStates(boundary =>
        {
            Assert.IsFalse(boundary.CanCommit);
            Assert.IsFalse(boundary.CanPush);
            Assert.IsFalse(boundary.CanCreatePullRequest);
            Assert.IsFalse(boundary.CanMarkReadyForReview);
        });

    [TestMethod]
    public void FrontendReadiness_AllReadStates_KeepCanMergeReleaseDeployFalse() =>
        AssertAllReadStates(boundary =>
        {
            Assert.IsFalse(boundary.CanMerge);
            Assert.IsFalse(boundary.CanRelease);
            Assert.IsFalse(boundary.CanDeploy);
        });

    [TestMethod]
    public void FrontendReadiness_AllReadStates_KeepCanPromoteMemoryContinueWorkflowFalse() =>
        AssertAllReadStates(boundary =>
        {
            Assert.IsFalse(boundary.CanPromoteMemory);
            Assert.IsFalse(boundary.CanContinueWorkflow);
        });

    [TestMethod]
    public void StaticScan_A10AddsNoMutationEndpoint()
    {
        var source = A10Source();

        AssertNoMarkers(source, ["[Http" + "Post]", "[Http" + "Put]", "[Http" + "Patch]", "[Http" + "Delete]"]);
        AssertNoMarkers(source, [
            "GrantsAuthority = " + "true",
            "AllowsMutation = " + "true",
            "CanCreateApproval = " + "true",
            "CanSatisfyPolicy = " + "true",
            "CanExecute = " + "true"
        ]);
    }

    [TestMethod]
    public void StaticScan_A10AddsNoExecutorOrProviderMutationPath()
    {
        var source = A10Source();

        AssertNoMarkers(source, [
            "SourceApply" + "Executor",
            "Rollback" + "Executor",
            "Commit" + "Executor",
            "Push" + "Executor",
            "DraftPullRequest" + "Executor",
            "Merge" + "Executor",
            "Release" + "Executor",
            "Deployment" + "Executor",
            "MemoryPromotion" + "Executor",
            "Workflow" + "Continuation",
            "Process" + "StartInfo",
            "Run" + "ProcessAsync",
            "git " + "apply",
            "git " + "commit",
            "git " + "push",
            "gh pr " + "create"
        ]);
    }

    [TestMethod]
    public void StaticScan_A10DoesNotReadRawPayloads()
    {
        var source = A10Source();

        AssertNoMarkers(source, [
            "ReadValidation" + "LogAsync",
            "ReadValidation" + "OutputAsync",
            "ReadCommand" + "OutputAsync",
            "ReadBuild" + "OutputAsync",
            "ReadTest" + "OutputAsync",
            "ReadPatch" + "PayloadAsync",
            "ReadPatch" + "TextAsync",
            "ReadDiff" + "TextAsync",
            "ReadTimeline" + "PayloadAsync",
            "ReadEvent" + "PayloadAsync",
            "ReadReceipt" + "TextAsync",
            "ReadEvidence" + "TextAsync"
        ]);
    }

    [TestMethod]
    public void StaticScan_A10DoesNotExposePrivateMaterial()
    {
        var source = A10Source();

        AssertNoMarkers(source, [
            "raw" + "Prompt",
            "raw" + "Completion",
            "raw" + "ToolOutput",
            "raw" + "Patch",
            "raw" + "Log",
            "full " + "diff",
            "chain" + "OfThought",
            "private " + "reasoning",
            "scratch" + "pad",
            "bearer " + "token",
            "api " + "key",
            "pass" + "word",
            "sec" + "ret",
            "private " + "key"
        ]);
    }

    [TestMethod]
    public void StaticScan_A10DoesNotCreateActionRequests()
    {
        var source = A10Source();

        AssertNoMarkers(source, ["CreateAction" + "Request(", "ControlledAction" + "RequestCreate"]);
    }

    [TestMethod]
    public void StaticScan_A10DoesNotRefreshOrRunValidation()
    {
        var source = A10Source();

        AssertNoMarkers(source, [
            "Refresh" + "Validation(",
            "Run" + "ValidationAsync",
            "Validation" + "Runner",
            "Retry" + "Validation(",
            "Repair" + "Validation("
        ]);
    }

    private static FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel> OperationStatusEnvelope(
        GovernedOperationStatus status,
        bool compact = false) =>
        Envelope(Controller(Api(status)).GetOperationStatus(OperationId, compact));

    private static FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel> OperationStatusEnvelopeWithState(
        GovernedOperationStatus status,
        FrontendReadinessReadState state,
        bool compact = false) =>
        Envelope(Controller(new OperationStatusOverrideReadApi(status, state)).GetOperationStatus(OperationId, compact));

    private static FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel> NullOperationStatusEnvelope(
        FrontendReadinessReadState state,
        bool compact = false) =>
        Envelope(Controller(new StateOnlyReadApi(state)).GetOperationStatus(OperationId, compact));

    private static FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel> BackendOperationStatusEnvelope(
        params IFrontendReadinessBackendTruthSource[] sources) =>
        Envelope(Controller(BackendApi(sources)).GetOperationStatus(OperationId));

    private static FrontendReadinessController Controller(IFrontendReadinessReadApi api) =>
        new(api);

    private static FrontendReadinessReadApi Api(params object[] values)
    {
        var snapshot = new FrontendReadinessReadSnapshot
        {
            OperationStatuses = values.OfType<GovernedOperationStatus>().ToDictionary(item => item.OperationId, StringComparer.OrdinalIgnoreCase),
            Evidence = values.OfType<FrontendEvidenceMetadataReadModel>().ToDictionary(item => item.EvidenceRef, StringComparer.OrdinalIgnoreCase),
            Receipts = values.OfType<FrontendReceiptMetadataReadModel>().ToDictionary(item => item.ReceiptRef, StringComparer.OrdinalIgnoreCase),
            Timelines = values.OfType<FrontendOperationTimelineReadModel>().ToDictionary(item => item.OperationId, StringComparer.OrdinalIgnoreCase),
            PatchPackages = values.OfType<FrontendPatchPackageMetadataReadModel>().ToDictionary(item => item.PackageId, StringComparer.OrdinalIgnoreCase),
            PatchPackageArtifacts = values.OfType<FrontendPatchPackageArtifactsReadModel>().ToDictionary(item => item.PackageId, StringComparer.OrdinalIgnoreCase),
            ValidationResults = values.OfType<FrontendValidationResultMetadataReadModel>().ToDictionary(item => item.ValidationResultId, StringComparer.OrdinalIgnoreCase)
        };

        return new FrontendReadinessReadApi(snapshot, () => Now);
    }

    private static BackendFrontendReadinessReadApi BackendApi(params IFrontendReadinessBackendTruthSource[] sources) =>
        new(sources, new TestTenantContext(), () => Now);

    private static GovernedOperationStatus Status(
        GovernedOperationState state = GovernedOperationState.Blocked,
        DateTimeOffset? expiresAtUtc = null,
        IReadOnlyCollection<string>? forbiddenActions = null,
        IReadOnlyCollection<string>? missingEvidence = null,
        IReadOnlyCollection<string>? evidenceRefs = null) =>
        new()
        {
            OperationId = OperationId,
            OperationKind = "PatchProposal",
            Subject = "repo repo-a10 branch branch-a10 run run-a10 patch patch-a10",
            State = state,
            BlockedReasons = state == GovernedOperationState.Blocked ? ["WaitingForGovernedEvidence"] : [],
            MissingEvidence = (missingEvidence ?? [MissingEvidence]).ToArray(),
            NextSafeActions = ["inspect backend truth"],
            ForbiddenActions = (forbiddenActions ?? [ForbiddenAction]).ToArray(),
            EvidenceRefs = (evidenceRefs ?? ["evidence:a10:status"]).ToArray(),
            ReceiptRefs = ["receipt:a10:status"],
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };

    private static FrontendEvidenceMetadataReadModel Evidence(
        bool redacted = false,
        string summary = "safe evidence metadata",
        IReadOnlyCollection<string>? warnings = null) =>
        new()
        {
            EvidenceRef = EvidenceRef,
            EvidenceKind = redacted ? "RedactedEvidenceMetadata" : "PatchPackageEvidence",
            Summary = redacted ? "[redacted: evidence metadata unavailable]" : summary,
            ReferenceOnly = true,
            ContainsRawPayload = false,
            Warnings = warnings ?? ["Evidence metadata is reference-only."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = null,
            FreshnessKnown = true
        };

    private static FrontendReceiptMetadataReadModel Receipt(IReadOnlyCollection<string>? warnings = null) =>
        new()
        {
            ReceiptRef = ReceiptRef,
            ReceiptKind = "PatchPackageReceipt",
            Summary = "safe receipt metadata",
            ReferenceOnly = true,
            GrantsAuthority = false,
            ContinuesWorkflow = false,
            Warnings = warnings ?? ["Receipt metadata is reference-only."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = null,
            FreshnessKnown = true
        };

    private static FrontendOperationTimelineReadModel Timeline(IReadOnlyCollection<FrontendTimelineEntry> entries) =>
        new()
        {
            OperationId = OperationId,
            Entries = entries,
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = null,
            FreshnessKnown = true
        };

    private static FrontendTimelineEntry TimelineEntry() =>
        new()
        {
            EntryId = "timeline:a10",
            EventKind = "OperationObserved",
            Summary = "operation observed",
            EvidenceRefs = ["evidence:a10:timeline"],
            ReceiptRefs = ["receipt:a10:timeline"],
            ObservedAtUtc = ObservedAtUtc
        };

    private static FrontendPatchPackageMetadataReadModel PatchPackage(DateTimeOffset? expiresAtUtc = null) =>
        new()
        {
            PackageId = PackageId,
            Repository = "repo-a10",
            Branch = "branch-a10",
            RunId = "run-a10",
            PatchHash = "sha256:patch-a10",
            ProposedFilePaths = ["src/file.cs"],
            ArtifactRefs = ["artifact:a10"],
            EvidenceRefs = ["evidence:a10"],
            ReceiptRefs = ["receipt:a10"],
            ReviewSummaryRef = "summary:a10",
            KnownRisksRef = "risks:a10",
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            FreshnessKnown = true
        };

    private static FrontendPatchPackageArtifactsReadModel Artifacts(IReadOnlyCollection<string>? authorityWarnings = null) =>
        new()
        {
            PackageId = PackageId,
            Repository = "repo-a10",
            Branch = "branch-a10",
            RunId = "run-a10",
            PatchHash = "sha256:patch-a10",
            PatchDiffText = "patch summary only",
            ReviewSummaryText = "review summary",
            KnownRisksText = "known risks",
            ValidationSummaryText = "validation summary",
            ValidationOutcome = "Passed",
            WhatRan = ["build"],
            WhatPassed = ["build"],
            WhatFailed = [],
            WhatWasSkipped = [],
            ValidationIsStale = false,
            ProposedFilePaths = ["src/file.cs"],
            ArtifactRefs = ["artifact:a10"],
            EvidenceRefs = ["evidence:a10"],
            ReceiptRefs = ["receipt:a10"],
            AuthorityWarnings = authorityWarnings ?? ["Patch package artifacts are not authority."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = null,
            FreshnessKnown = true
        };

    private static FrontendValidationResultMetadataReadModel Validation(bool isStale = false) =>
        new()
        {
            ValidationResultId = ValidationResultId,
            Repository = "repo-a10",
            Branch = "branch-a10",
            RunId = "run-a10",
            PatchHash = "sha256:patch-a10",
            Outcome = "Passed",
            WhatRan = ["build"],
            WhatPassed = ["build"],
            WhatFailed = [],
            WhatWasSkipped = [],
            IsStale = isStale,
            EvidenceRefs = ["evidence:a10"],
            ReceiptRefs = ["receipt:a10"],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = null,
            FreshnessKnown = true
        };

    private static SeededBackendTruthSource OperationStatusSource(GovernedOperationStatus status, bool visible = true) =>
        new() { OperationStatus = status, Visible = visible };

    private static SeededBackendTruthSource EvidenceSource(FrontendEvidenceMetadataReadModel evidence) =>
        new() { Evidence = evidence };

    private static SeededBackendTruthSource ValidationSource(FrontendValidationResultMetadataReadModel validation) =>
        new() { Validation = validation };

    private static SeededBackendTruthSource PatchPackageSource(FrontendPatchPackageMetadataReadModel metadata) =>
        new() { PatchPackage = metadata };

    private static SeededBackendTruthSource StateSource<TData>(FrontendReadinessReadState state)
        where TData : class =>
        new() { State = state };

    private static SeededBackendTruthSource ThrowingSource() =>
        new() { ThrowOnRead = true };

    private static IEnumerable<FrontendReadinessReadState> AllReadStates()
    {
        yield return FrontendReadinessReadState.Available();
        yield return FrontendReadinessReadState.NotFound("MissingRecord");
        yield return FrontendReadinessReadState.Empty("NoVisibleEntries");
        yield return FrontendReadinessReadState.Redacted("UnsafeRecord");
        yield return FrontendReadinessReadState.Unavailable();
        yield return FrontendReadinessReadState.Invalid("InvalidRecord");
        yield return FrontendReadinessReadState.Expired("ExpiredRecord");
        yield return FrontendReadinessReadState.Stale("StaleRecord");
        yield return FrontendReadinessReadState.NotVisible();
        yield return FrontendReadinessReadState.Unknown();
    }

    private static void AssertAllReadStates(Action<FrontendReadBoundary> assert)
    {
        foreach (var state in AllReadStates())
            assert(state.Boundary);
    }

    private static void AssertReadStateWarnings(FrontendReadinessReadState state)
    {
        AssertContains(state.Warnings, "Read state is not approval.");
        AssertContains(state.Warnings, "Read state is not policy satisfaction.");
        AssertContains(state.Warnings, "Read state is not source apply authority.");
        AssertContains(state.Warnings, "Read state does not allow mutation or workflow continuation.");
    }

    private static void AssertFreshnessWarnings(FrontendReadinessFreshnessState freshness)
    {
        AssertContains(freshness.Warnings, "Freshness is not approval.");
        AssertContains(freshness.Warnings, "Freshness is not policy satisfaction.");
        AssertContains(freshness.Warnings, "Freshness is not source apply authority.");
        AssertContains(freshness.Warnings, "Freshness does not allow mutation or workflow continuation.");
    }

    private static void AssertNoAuthority(FrontendReadinessReadState state)
    {
        Assert.IsFalse(state.IsAuthorityGrant);
        Assert.IsFalse(state.AllowsMutation);
        AssertReadOnly(state.Boundary);
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

    private static FrontendReadinessApiEnvelope<T> Envelope<T>(ActionResult<FrontendReadinessApiEnvelope<T>> result)
    {
        var objectResult = result.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        return (FrontendReadinessApiEnvelope<T>)objectResult.Value!;
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), $"Expected '{expected}'.");

    private static void AssertNotContains(IEnumerable<string> values, string forbidden) =>
        Assert.IsFalse(values.Contains(forbidden, StringComparer.OrdinalIgnoreCase), $"Forbidden '{forbidden}' was present.");

    private static void AssertNoMarkers(string source, IEnumerable<string> markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Forbidden marker '{marker}' was present.");
    }

    private static string A10Source() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "BlockA10FrontendReadinessForbiddenActionPreservationTests.cs"));

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        Assert.Fail("Could not locate repo root.");
        return string.Empty;
    }

    private sealed class OperationStatusOverrideReadApi : IFrontendReadinessReadApi
    {
        private readonly FrontendReadinessReadApi _inner;
        private readonly FrontendReadinessReadState _state;

        public OperationStatusOverrideReadApi(GovernedOperationStatus status, FrontendReadinessReadState state)
        {
            _inner = Api(status);
            _state = state;
        }

        public FrontendOperationStatusReadModel? GetOperationStatus(string operationId) => _inner.GetOperationStatus(operationId);
        public FrontendReadinessReadState GetOperationStatusReadState(string operationId) => _state;
        public FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId) => null;
        public FrontendReadinessReadState GetOperationTimelineReadState(string operationId) => FrontendReadinessReadState.NotFound("OperationTimelineNotFound", operationId);
        public FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId) => null;
        public FrontendReadinessReadState GetPatchPackageMetadataReadState(string packageId) => FrontendReadinessReadState.NotFound("PatchPackageMetadataNotFound", packageId);
        public FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId) => null;
        public FrontendReadinessReadState GetPatchPackageArtifactsReadState(string packageId) => FrontendReadinessReadState.NotFound("PatchPackageArtifactsNotFound", packageId);
        public FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId) => null;
        public FrontendReadinessReadState GetValidationResultMetadataReadState(string validationResultId) => FrontendReadinessReadState.NotFound("ValidationResultMetadataNotFound", validationResultId);
        public FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef) => null;
        public FrontendReadinessReadState GetEvidenceMetadataReadState(string evidenceRef) => FrontendReadinessReadState.NotFound("EvidenceMetadataNotFound", evidenceRef);
        public FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef) => null;
        public FrontendReadinessReadState GetReceiptMetadataReadState(string receiptRef) => FrontendReadinessReadState.NotFound("ReceiptMetadataNotFound", receiptRef);
    }

    private sealed class StateOnlyReadApi : IFrontendReadinessReadApi
    {
        private readonly FrontendReadinessReadState _state;

        public StateOnlyReadApi(FrontendReadinessReadState state) => _state = state;

        public FrontendOperationStatusReadModel? GetOperationStatus(string operationId) => null;
        public FrontendReadinessReadState GetOperationStatusReadState(string operationId) => _state;
        public FrontendOperationTimelineReadModel? GetOperationTimeline(string operationId) => null;
        public FrontendReadinessReadState GetOperationTimelineReadState(string operationId) => _state;
        public FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(string packageId) => null;
        public FrontendReadinessReadState GetPatchPackageMetadataReadState(string packageId) => _state;
        public FrontendPatchPackageArtifactsReadModel? GetPatchPackageArtifacts(string packageId) => null;
        public FrontendReadinessReadState GetPatchPackageArtifactsReadState(string packageId) => _state;
        public FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(string validationResultId) => null;
        public FrontendReadinessReadState GetValidationResultMetadataReadState(string validationResultId) => _state;
        public FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef) => null;
        public FrontendReadinessReadState GetEvidenceMetadataReadState(string evidenceRef) => _state;
        public FrontendReceiptMetadataReadModel? GetReceiptMetadata(string receiptRef) => null;
        public FrontendReadinessReadState GetReceiptMetadataReadState(string receiptRef) => _state;
    }

    private sealed class SeededBackendTruthSource : FrontendReadinessBackendTruthSource
    {
        public bool Visible { get; init; } = true;
        public bool ThrowOnRead { get; init; }
        public FrontendReadinessReadState? State { get; init; }
        public GovernedOperationStatus? OperationStatus { get; init; }
        public FrontendEvidenceMetadataReadModel? Evidence { get; init; }
        public FrontendValidationResultMetadataReadModel? Validation { get; init; }
        public FrontendPatchPackageMetadataReadModel? PatchPackage { get; init; }

        public override string SourceName => "seeded-a10";

        public override bool IsVisibleTo(FrontendReadinessReadScope scope) => Visible;

        public override FrontendReadinessBackendReadResult<GovernedOperationStatus> ReadOperationStatus(
            string operationId,
            FrontendReadinessReadScope scope) =>
            Read(OperationStatus, "OperationStatusNotFound");

        public override FrontendReadinessBackendReadResult<FrontendEvidenceMetadataReadModel> ReadEvidenceMetadata(
            string evidenceRef,
            FrontendReadinessReadScope scope) =>
            Read(Evidence, "EvidenceMetadataNotFound");

        public override FrontendReadinessBackendReadResult<FrontendValidationResultMetadataReadModel> ReadValidationResultMetadata(
            string validationResultId,
            FrontendReadinessReadScope scope) =>
            Read(Validation, "ValidationResultMetadataNotFound");

        public override FrontendReadinessBackendReadResult<FrontendPatchPackageMetadataReadModel> ReadPatchPackageMetadata(
            string packageId,
            FrontendReadinessReadScope scope) =>
            Read(PatchPackage, "PatchPackageMetadataNotFound");

        private FrontendReadinessBackendReadResult<TModel> Read<TModel>(TModel? value, string notFoundReason)
            where TModel : class
        {
            if (ThrowOnRead)
                throw new InvalidOperationException("backend truth source unavailable");

            if (value is not null)
                return FrontendReadinessBackendReadResult<TModel>.WithData(value, FrontendReadinessReadState.Available("BackendTruthAvailable"));

            return FrontendReadinessBackendReadResult<TModel>.WithoutData(State ?? FrontendReadinessReadState.NotFound(notFoundReason));
        }
    }

    private sealed class TestTenantContext : ICurrentTenantContext
    {
        public int TenantId => 42;
    }
}
