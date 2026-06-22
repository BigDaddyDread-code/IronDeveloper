using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA09FrontendReadinessFreshnessMarkerContractTests
{
    private const string OperationId = "operation-a09";
    private const string EvidenceRef = "evidence:a09";
    private const string ReceiptRef = "receipt:a09";
    private const string PackageId = "patch-package:a09";
    private const string ValidationResultId = "validation-result:a09";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-23T12:00:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = Now.AddMinutes(-5);
    private static readonly DateTimeOffset FutureExpiry = Now.AddMinutes(5);
    private static readonly DateTimeOffset PastExpiry = Now.AddMinutes(-1);

    [TestMethod]
    public void A09_FreshnessKind_IncludesCurrent() =>
        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, Enum.Parse<FrontendReadinessFreshnessKind>("Current"));

    [TestMethod]
    public void A09_FreshnessKind_IncludesStale() =>
        Assert.AreEqual(FrontendReadinessFreshnessKind.Stale, Enum.Parse<FrontendReadinessFreshnessKind>("Stale"));

    [TestMethod]
    public void A09_FreshnessKind_IncludesExpired() =>
        Assert.AreEqual(FrontendReadinessFreshnessKind.Expired, Enum.Parse<FrontendReadinessFreshnessKind>("Expired"));

    [TestMethod]
    public void A09_FreshnessKind_IncludesUnknown() =>
        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, Enum.Parse<FrontendReadinessFreshnessKind>("Unknown"));

    [TestMethod]
    public void A09_FreshnessKind_IncludesNotApplicable() =>
        Assert.AreEqual(FrontendReadinessFreshnessKind.NotApplicable, Enum.Parse<FrontendReadinessFreshnessKind>("NotApplicable"));

    [TestMethod]
    public void A09_FreshnessClassifier_CurrentWhenObservedAndNotExpired()
    {
        var freshness = Freshness(expiresAtUtc: FutureExpiry);

        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, freshness.Kind);
        Assert.IsTrue(freshness.FreshnessKnown);
        Assert.IsFalse(freshness.IsStale);
        Assert.IsFalse(freshness.IsExpired);
    }

    [TestMethod]
    public void A09_FreshnessClassifier_StaleWhenExplicitlyStale()
    {
        var freshness = Freshness(explicitStale: true, expiresAtUtc: FutureExpiry);

        Assert.AreEqual(FrontendReadinessFreshnessKind.Stale, freshness.Kind);
        Assert.IsTrue(freshness.IsStale);
        Assert.IsFalse(freshness.IsExpired);
    }

    [TestMethod]
    public void A09_FreshnessClassifier_ExpiredWhenExpiryAtEvaluationTime()
    {
        var freshness = Freshness(expiresAtUtc: Now);

        Assert.AreEqual(FrontendReadinessFreshnessKind.Expired, freshness.Kind);
        Assert.IsTrue(freshness.IsStale);
        Assert.IsTrue(freshness.IsExpired);
    }

    [TestMethod]
    public void A09_FreshnessClassifier_ExpiredBeatsExplicitStale()
    {
        var freshness = Freshness(explicitStale: true, expiresAtUtc: PastExpiry);

        Assert.AreEqual(FrontendReadinessFreshnessKind.Expired, freshness.Kind);
        Assert.IsTrue(freshness.IsExpired);
    }

    [TestMethod]
    public void A09_FreshnessClassifier_UnknownWhenObservedTimeMissing()
    {
        var freshness = FrontendReadinessFreshnessClassifier.Evaluate(
            observedAtUtc: null,
            expiresAtUtc: null,
            explicitStale: false,
            freshnessKnown: true,
            evaluatedAtUtc: Now,
            subject: "TestSubject");

        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, freshness.Kind);
        Assert.IsFalse(freshness.FreshnessKnown);
        Assert.IsTrue(freshness.IsStale);
    }

    [TestMethod]
    public void A09_FreshnessClassifier_UnknownWhenFreshnessNotKnown()
    {
        var freshness = Freshness(freshnessKnown: false);

        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, freshness.Kind);
        Assert.IsTrue(freshness.IsStale);
    }

    [TestMethod]
    public void A09_FreshnessClassifier_NotApplicableIsNonCurrentButNotStale()
    {
        var freshness = FrontendReadinessFreshnessClassifier.NotApplicable("NoRecordFreshnessNotApplicable", Now);

        Assert.AreEqual(FrontendReadinessFreshnessKind.NotApplicable, freshness.Kind);
        Assert.IsFalse(freshness.FreshnessKnown);
        Assert.IsFalse(freshness.IsStale);
        Assert.IsFalse(freshness.IsExpired);
    }

    [TestMethod]
    public void A09_FreshnessClassifier_WarnsFreshnessIsNotApproval()
    {
        var freshness = Freshness();

        AssertContains(freshness.Warnings, "Freshness is not approval.");
    }

    [TestMethod]
    public void A09_FreshnessClassifier_WarnsFreshnessIsNotPolicySatisfaction()
    {
        var freshness = Freshness();

        AssertContains(freshness.Warnings, "Freshness is not policy satisfaction.");
    }

    [TestMethod]
    public void A09_FreshnessClassifier_WarnsFreshnessIsNotSourceApplyAuthority()
    {
        var freshness = Freshness();

        AssertContains(freshness.Warnings, "Freshness is not source apply authority.");
    }

    [TestMethod]
    public void A09_ReadState_CarriesFreshnessMarker()
    {
        var state = FrontendReadinessReadState.Available(Freshness(), "OperationStatusAvailable");

        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, state.Freshness.Kind);
        Assert.IsFalse(state.IsStale);
        Assert.IsFalse(state.IsExpired);
    }

    [TestMethod]
    public void A09_ReadState_StaleCarriesStaleMarker()
    {
        var state = FrontendReadinessReadState.Stale("ValidationResultStale", Freshness(explicitStale: true));

        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Stale, state.Freshness.Kind);
        AssertNoAuthority(state);
    }

    [TestMethod]
    public void A09_ReadState_ExpiredCarriesExpiredMarker()
    {
        var state = FrontendReadinessReadState.Expired("OperationStatusExpired", Freshness(expiresAtUtc: PastExpiry));

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Expired, state.Freshness.Kind);
        Assert.IsTrue(state.IsExpired);
        AssertNoAuthority(state);
    }

    [TestMethod]
    public void A09_NotFoundReadState_UsesNotApplicableFreshness()
    {
        var state = FrontendReadinessReadState.NotFound("MissingRecord");

        Assert.AreEqual(FrontendReadinessFreshnessKind.NotApplicable, state.Freshness.Kind);
        AssertNoAuthority(state);
    }

    [TestMethod]
    public void A09_UnknownReadState_UsesUnknownFreshness()
    {
        var state = FrontendReadinessReadState.Unknown();

        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, state.Freshness.Kind);
        AssertNoAuthority(state);
    }

    [TestMethod]
    public void A09_OperationStatus_CurrentMapsToAvailable()
    {
        var state = FrontendReadinessReadStateClassifier.OperationStatus(Status(), OperationId, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_OperationStatus_ExpiredMapsToExpired()
    {
        var state = FrontendReadinessReadStateClassifier.OperationStatus(Status(expiresAtUtc: PastExpiry), OperationId, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
        Assert.IsTrue(state.IsExpired);
    }

    [TestMethod]
    public void A09_OperationStatus_MissingObservedMapsToUnknownFreshness()
    {
        var state = FrontendReadinessReadStateClassifier.OperationStatus(Status(includeObservedAt: false), OperationId, Now);

        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, state.Freshness.Kind);
        Assert.IsTrue(state.IsStale);
    }

    [TestMethod]
    public void A09_OperationStatus_InvalidStoredStatusWinsOverFreshness()
    {
        var state = FrontendReadinessReadStateClassifier.OperationStatus(
            Status(blockedReasons: ["StoredOperationStatusInvalid"], expiresAtUtc: PastExpiry),
            OperationId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Invalid, state.Kind);
        Assert.AreNotEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
    }

    [TestMethod]
    public void A09_EvidenceMetadata_CurrentMapsToAvailable()
    {
        var state = FrontendReadinessReadStateClassifier.EvidenceMetadata(Evidence(observedAtUtc: ObservedAtUtc), EvidenceRef, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_EvidenceMetadata_ExpiredMapsToExpired()
    {
        var state = FrontendReadinessReadStateClassifier.EvidenceMetadata(
            Evidence(observedAtUtc: ObservedAtUtc, expiresAtUtc: PastExpiry),
            EvidenceRef,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
    }

    [TestMethod]
    public void A09_EvidenceMetadata_NoTimestampMapsToUnknownFreshness()
    {
        var state = FrontendReadinessReadStateClassifier.EvidenceMetadata(Evidence(), EvidenceRef, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_EvidenceMetadata_RedactedWinsOverExpired()
    {
        var state = FrontendReadinessReadStateClassifier.EvidenceMetadata(
            Evidence(redacted: true, observedAtUtc: ObservedAtUtc, expiresAtUtc: PastExpiry),
            EvidenceRef,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, state.Kind);
    }

    [TestMethod]
    public void A09_ReceiptMetadata_CurrentMapsToAvailable()
    {
        var state = FrontendReadinessReadStateClassifier.ReceiptMetadata(Receipt(observedAtUtc: ObservedAtUtc), ReceiptRef, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_ReceiptMetadata_ExpiredMapsToExpired()
    {
        var state = FrontendReadinessReadStateClassifier.ReceiptMetadata(
            Receipt(observedAtUtc: ObservedAtUtc, expiresAtUtc: PastExpiry),
            ReceiptRef,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
    }

    [TestMethod]
    public void A09_ReceiptMetadata_NoTimestampMapsToUnknownFreshness()
    {
        var state = FrontendReadinessReadStateClassifier.ReceiptMetadata(Receipt(), ReceiptRef, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_ReceiptMetadata_RedactedWinsOverExpired()
    {
        var state = FrontendReadinessReadStateClassifier.ReceiptMetadata(
            Receipt(redacted: true, observedAtUtc: ObservedAtUtc, expiresAtUtc: PastExpiry),
            ReceiptRef,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, state.Kind);
    }

    [TestMethod]
    public void A09_Timeline_EntryTimestampMapsToCurrent()
    {
        var state = FrontendReadinessReadStateClassifier.OperationTimeline(Timeline(entries: [TimelineEntry()]), OperationId, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_Timeline_EmptyWithoutObservedTimestampMapsToUnknownFreshness()
    {
        var state = FrontendReadinessReadStateClassifier.OperationTimeline(Timeline(entries: []), OperationId, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Empty, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_Timeline_ExpiredMapsToExpired()
    {
        var state = FrontendReadinessReadStateClassifier.OperationTimeline(
            Timeline(entries: [TimelineEntry()], expiresAtUtc: PastExpiry),
            OperationId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
    }

    [TestMethod]
    public void A09_Timeline_RedactedWinsOverExpired()
    {
        var state = FrontendReadinessReadStateClassifier.OperationTimeline(
            Timeline(entries: [RedactedTimelineEntry()], expiresAtUtc: PastExpiry),
            OperationId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, state.Kind);
    }

    [TestMethod]
    public void A09_PatchPackageMetadata_CurrentMapsToAvailable()
    {
        var state = FrontendReadinessReadStateClassifier.PatchPackageMetadata(PatchPackage(observedAtUtc: ObservedAtUtc), PackageId, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_PatchPackageMetadata_ExpiredMapsToExpired()
    {
        var state = FrontendReadinessReadStateClassifier.PatchPackageMetadata(
            PatchPackage(observedAtUtc: ObservedAtUtc, expiresAtUtc: PastExpiry),
            PackageId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
    }

    [TestMethod]
    public void A09_PatchPackageMetadata_NoTimestampMapsToUnknownFreshness()
    {
        var state = FrontendReadinessReadStateClassifier.PatchPackageMetadata(PatchPackage(), PackageId, Now);

        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_PatchPackageMetadata_RedactedWinsOverExpired()
    {
        var state = FrontendReadinessReadStateClassifier.PatchPackageMetadata(
            PatchPackage(redacted: true, observedAtUtc: ObservedAtUtc, expiresAtUtc: PastExpiry),
            PackageId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, state.Kind);
    }

    [TestMethod]
    public void A09_PatchPackageArtifacts_CurrentMapsToAvailable()
    {
        var state = FrontendReadinessReadStateClassifier.PatchPackageArtifacts(Artifacts(observedAtUtc: ObservedAtUtc), PackageId, Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_PatchPackageArtifacts_ValidationStaleMapsToStale()
    {
        var state = FrontendReadinessReadStateClassifier.PatchPackageArtifacts(
            Artifacts(validationIsStale: true, observedAtUtc: ObservedAtUtc),
            PackageId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, state.Kind);
        AssertContains(state.Reasons, "PatchPackageValidationStale");
    }

    [TestMethod]
    public void A09_PatchPackageArtifacts_ExpiredMapsToExpired()
    {
        var state = FrontendReadinessReadStateClassifier.PatchPackageArtifacts(
            Artifacts(observedAtUtc: ObservedAtUtc, expiresAtUtc: PastExpiry),
            PackageId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
    }

    [TestMethod]
    public void A09_ValidationResult_CurrentMapsToAvailable()
    {
        var state = FrontendReadinessReadStateClassifier.ValidationResultMetadata(
            Validation(observedAtUtc: ObservedAtUtc),
            ValidationResultId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_ValidationResult_StaleMapsToStale()
    {
        var state = FrontendReadinessReadStateClassifier.ValidationResultMetadata(
            Validation(isStale: true, observedAtUtc: ObservedAtUtc),
            ValidationResultId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, state.Kind);
        AssertContains(state.Reasons, "ValidationResultStale");
    }

    [TestMethod]
    public void A09_ValidationResult_ExpiredMapsToExpired()
    {
        var state = FrontendReadinessReadStateClassifier.ValidationResultMetadata(
            Validation(observedAtUtc: ObservedAtUtc, expiresAtUtc: PastExpiry),
            ValidationResultId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
    }

    [TestMethod]
    public void A09_ValidationResult_FreshnessUnknownMapsToUnknownMarker()
    {
        var state = FrontendReadinessReadStateClassifier.ValidationResultMetadata(
            Validation(observedAtUtc: ObservedAtUtc, freshnessKnown: false),
            ValidationResultId,
            Now);

        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, state.Freshness.Kind);
        Assert.IsTrue(state.IsStale);
    }

    [TestMethod]
    public void A09_ValidationResult_RedactedWinsOverExpired()
    {
        var state = FrontendReadinessReadStateClassifier.ValidationResultMetadata(
            Validation(redacted: true, observedAtUtc: ObservedAtUtc, expiresAtUtc: PastExpiry),
            ValidationResultId,
            Now);

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, state.Kind);
    }

    [TestMethod]
    public void A09_ApiEnvelope_ExposesFreshness()
    {
        var envelope = Envelope(Controller(Api(Status(expiresAtUtc: FutureExpiry))).GetOperationStatus(OperationId));

        Assert.AreEqual(envelope.ReadState.Freshness, envelope.Freshness);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Current, envelope.Freshness.Kind);
    }

    [TestMethod]
    public void A09_ApiEnvelope_WarningsIncludeFreshnessWarnings()
    {
        var envelope = Envelope(Controller(Api(Status(expiresAtUtc: DateTimeOffset.UnixEpoch))).GetOperationStatus(OperationId));

        AssertContains(envelope.Warnings, "Expired is not current.");
        AssertContains(envelope.Warnings, "Freshness is not approval.");
    }

    [TestMethod]
    public void A09_ApiEnvelope_ExpiredNullDataReturnsExpiredStatus()
    {
        var envelope = Envelope(Controller(new StateOnlyReadApi(FrontendReadinessReadState.Expired("OperationStatusExpired")))
            .GetOperationStatus(OperationId));

        Assert.AreEqual("expired", envelope.Status);
        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void A09_ApiEnvelope_StaleNullDataReturnsStaleStatus()
    {
        var envelope = Envelope(Controller(new StateOnlyReadApi(FrontendReadinessReadState.Stale("ValidationResultStale")))
            .GetOperationStatus(OperationId));

        Assert.AreEqual("stale", envelope.Status);
        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void A09_ApiEnvelope_CompactModeCannotHideFreshnessWarnings()
    {
        var envelope = Envelope(Controller(Api(Status(expiresAtUtc: DateTimeOffset.UnixEpoch))).GetOperationStatus(OperationId, compact: true));

        AssertContains(envelope.Warnings, "Compact mode was requested but authority-critical fields are still returned.");
        AssertContains(envelope.Warnings, "Freshness is not source apply authority.");
    }

    [TestMethod]
    public void A09_BackendApi_ReadStateUsesSourceFreshness()
    {
        var source = new SeededBackendTruthSource
        {
            OperationStatuses = Map(Status(expiresAtUtc: DateTimeOffset.UnixEpoch))
        };
        var api = new BackendFrontendReadinessReadApi([source], new TestTenantContext(42));

        var state = api.GetOperationStatusReadState(OperationId);

        Assert.AreEqual(FrontendReadinessReadStateKind.Expired, state.Kind);
    }

    [TestMethod]
    public void A09_BackendApi_NotVisibleKeepsUnknownFreshness()
    {
        var source = new SeededBackendTruthSource(tenantId: 7)
        {
            OperationStatuses = Map(Status())
        };
        var api = new BackendFrontendReadinessReadApi([source], new TestTenantContext(42));

        var state = api.GetOperationStatusReadState(OperationId);

        Assert.AreEqual(FrontendReadinessReadStateKind.NotVisible, state.Kind);
        Assert.AreEqual(FrontendReadinessFreshnessKind.Unknown, state.Freshness.Kind);
    }

    [TestMethod]
    public void A09_ReadState_ExpiredDoesNotAllowMutation()
    {
        var state = FrontendReadinessReadState.Expired("OperationStatusExpired");

        AssertNoAuthority(state);
        Assert.IsFalse(state.AllowsMutation);
    }

    [TestMethod]
    public void A09_ReadState_StaleDoesNotAllowWorkflowContinuation()
    {
        var state = FrontendReadinessReadState.Stale("ValidationResultStale");

        AssertNoAuthority(state);
        Assert.IsFalse(state.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void A09_FreshnessWarnings_DoNotRemoveReadStateWarnings()
    {
        var state = FrontendReadinessReadState.Expired("OperationStatusExpired", Freshness(expiresAtUtc: PastExpiry));

        AssertContains(state.Warnings, "Read state is not approval.");
        AssertContains(state.Warnings, "Freshness does not allow mutation or workflow continuation.");
    }

    [TestMethod]
    public void A09_ExpiredState_ShowsRefreshAsGuidanceOnly()
    {
        var state = FrontendReadinessReadState.Expired("OperationStatusExpired");

        AssertContains(state.NextSafeActions, "refresh backend truth source");
        AssertNoAuthority(state);
    }

    [TestMethod]
    public void A09_StaleState_ShowsRefreshAsGuidanceOnly()
    {
        var state = FrontendReadinessReadState.Stale("ValidationResultStale");

        AssertContains(state.NextSafeActions, "refresh validation evidence");
        AssertNoAuthority(state);
    }

    [TestMethod]
    public void A09_FreshnessClassifier_DoesNotReadSystemClock()
    {
        var source = SourceSlice(
            "public static class FrontendReadinessFreshnessClassifier",
            "public sealed record FrontendReadinessReadState");

        AssertNotContains(source, "DateTimeOffset.UtcNow");
    }

    [TestMethod]
    public void A09_StaticContract_DoesNotAddMutationSurface()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "FrontendReadinessReadModels.cs"));

        AssertNotContains(source, "Process.Start");
        AssertNotContains(source, "RunProcessAsync");
        AssertNotContains(source, "git push");
        AssertNotContains(source, "git commit");
        AssertNotContains(source, "gh pr");
    }

    [TestMethod]
    public void A09_StaticContract_DoesNotAddValidationExecutionSurface()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "FrontendReadinessReadModels.cs"));

        AssertNotContains(source, "dotnet test");
        AssertNotContains(source, "npm test");
        AssertNotContains(source, "validation runner");
        AssertNotContains(source, "rerun validation");
    }

    [TestMethod]
    public void A09_Receipt_DocumentsStaleExpiredBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "A09_FRONTEND_READINESS_FRESHNESS_MARKER_CONTRACT.md"));

        AssertContainsText(receipt, "Stale is not safe. Expired is not current.");
        AssertContainsText(receipt, "Old truth is not fresh authority.");
    }

    [TestMethod]
    public void A09_FreshnessMarker_IsNotAuthorityAcrossAllKinds()
    {
        foreach (var kind in Enum.GetValues<FrontendReadinessFreshnessKind>())
        {
            var freshness = kind switch
            {
                FrontendReadinessFreshnessKind.Current => FrontendReadinessFreshnessClassifier.Current("Current", ObservedAtUtc, FutureExpiry, Now),
                FrontendReadinessFreshnessKind.Stale => FrontendReadinessFreshnessClassifier.Stale("Stale", ObservedAtUtc, FutureExpiry, Now),
                FrontendReadinessFreshnessKind.Expired => FrontendReadinessFreshnessClassifier.Expired("Expired", ObservedAtUtc, PastExpiry, Now),
                FrontendReadinessFreshnessKind.NotApplicable => FrontendReadinessFreshnessClassifier.NotApplicable("NotApplicable", Now),
                _ => FrontendReadinessFreshnessClassifier.Unknown("Unknown", null, null, Now)
            };

            AssertContains(freshness.Warnings, "Freshness is not approval.");
            AssertContains(freshness.Warnings, "Freshness does not allow mutation or workflow continuation.");
        }
    }

    [TestMethod]
    public void A09_FreshnessMarker_PreservesEvaluatedAtUtc()
    {
        var freshness = Freshness();

        Assert.AreEqual(Now, freshness.EvaluatedAtUtc);
    }

    [TestMethod]
    public void A09_FreshnessMarker_PreservesObservedAndExpiresAt()
    {
        var freshness = Freshness(expiresAtUtc: FutureExpiry);

        Assert.AreEqual(ObservedAtUtc, freshness.ObservedAtUtc);
        Assert.AreEqual(FutureExpiry, freshness.ExpiresAtUtc);
    }

    private static FrontendReadinessFreshnessState Freshness(
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        bool explicitStale = false,
        bool freshnessKnown = true) =>
        FrontendReadinessFreshnessClassifier.Evaluate(
            observedAtUtc ?? ObservedAtUtc,
            expiresAtUtc,
            explicitStale,
            freshnessKnown,
            Now,
            "TestSubject");

    private static FrontendReadinessController Controller(IFrontendReadinessReadApi api) =>
        new(api);

    private static FrontendReadinessReadApi Api(params object[] values)
    {
        var snapshot = new FrontendReadinessReadSnapshot
        {
            OperationStatuses = values
                .OfType<FrontendOperationStatusReadModel>()
                .ToDictionary(item => item.OperationId, item => ToGovernedStatus(item), StringComparer.OrdinalIgnoreCase),
            Evidence = values.OfType<FrontendEvidenceMetadataReadModel>().ToDictionary(item => item.EvidenceRef, StringComparer.OrdinalIgnoreCase),
            Receipts = values.OfType<FrontendReceiptMetadataReadModel>().ToDictionary(item => item.ReceiptRef, StringComparer.OrdinalIgnoreCase),
            Timelines = values.OfType<FrontendOperationTimelineReadModel>().ToDictionary(item => item.OperationId, StringComparer.OrdinalIgnoreCase),
            PatchPackages = values.OfType<FrontendPatchPackageMetadataReadModel>().ToDictionary(item => item.PackageId, StringComparer.OrdinalIgnoreCase),
            PatchPackageArtifacts = values.OfType<FrontendPatchPackageArtifactsReadModel>().ToDictionary(item => item.PackageId, StringComparer.OrdinalIgnoreCase),
            ValidationResults = values.OfType<FrontendValidationResultMetadataReadModel>().ToDictionary(item => item.ValidationResultId, StringComparer.OrdinalIgnoreCase)
        };

        return new FrontendReadinessReadApi(snapshot);
    }

    private static IReadOnlyDictionary<string, GovernedOperationStatus> Map(FrontendOperationStatusReadModel model) =>
        new Dictionary<string, GovernedOperationStatus>(StringComparer.OrdinalIgnoreCase)
        {
            [model.OperationId] = new()
            {
                OperationId = model.OperationId,
                OperationKind = model.OperationKind,
                Subject = model.Subject,
                State = Enum.TryParse<GovernedOperationState>(model.State, out var state) ? state : GovernedOperationState.Blocked,
                BlockedReasons = model.BlockedReasons.ToArray(),
                MissingEvidence = model.MissingEvidence.ToArray(),
                NextSafeActions = model.NextSafeActions.ToArray(),
                ForbiddenActions = model.ForbiddenActions.ToArray(),
                EvidenceRefs = model.EvidenceRefs.ToArray(),
                ReceiptRefs = model.ReceiptRefs.ToArray(),
                ObservedAtUtc = model.ObservedAtUtc,
                ExpiresAtUtc = model.ExpiresAtUtc
            }
        };

    private static GovernedOperationStatus ToGovernedStatus(FrontendOperationStatusReadModel model) =>
        new()
        {
            OperationId = model.OperationId,
            OperationKind = model.OperationKind,
            Subject = model.Subject,
            State = Enum.TryParse<GovernedOperationState>(model.State, out var state) ? state : GovernedOperationState.Blocked,
            BlockedReasons = model.BlockedReasons.ToArray(),
            MissingEvidence = model.MissingEvidence.ToArray(),
            NextSafeActions = model.NextSafeActions.ToArray(),
            ForbiddenActions = model.ForbiddenActions.ToArray(),
            EvidenceRefs = model.EvidenceRefs.ToArray(),
            ReceiptRefs = model.ReceiptRefs.ToArray(),
            ObservedAtUtc = model.ObservedAtUtc,
            ExpiresAtUtc = model.ExpiresAtUtc
        };

    private static FrontendOperationStatusReadModel Status(
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        IReadOnlyCollection<string>? blockedReasons = null,
        bool includeObservedAt = true) =>
        new()
        {
            OperationId = OperationId,
            OperationKind = "PatchProposal",
            Subject = "patch proposal",
            State = GovernedOperationState.Completed.ToString(),
            BlockedReasons = blockedReasons ?? [],
            MissingEvidence = [],
            NextSafeActions = ["inspect status"],
            ForbiddenActions = ["do not execute from status"],
            EvidenceRefs = ["evidence:a09:status"],
            ReceiptRefs = [],
            AuthorityWarnings = ["Status is not authority."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = includeObservedAt ? observedAtUtc ?? ObservedAtUtc : default,
            ExpiresAtUtc = expiresAtUtc
        };

    private static FrontendEvidenceMetadataReadModel Evidence(
        bool redacted = false,
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null) =>
        new()
        {
            EvidenceRef = EvidenceRef,
            EvidenceKind = redacted ? "RedactedEvidenceMetadata" : "PatchPackageEvidence",
            Summary = redacted ? "[redacted: evidence metadata unavailable]" : "safe evidence metadata",
            ReferenceOnly = true,
            ContainsRawPayload = false,
            Warnings = ["Evidence metadata is reference-only."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = observedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            FreshnessKnown = observedAtUtc.HasValue
        };

    private static FrontendReceiptMetadataReadModel Receipt(
        bool redacted = false,
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null) =>
        new()
        {
            ReceiptRef = ReceiptRef,
            ReceiptKind = redacted ? "RedactedReceiptMetadata" : "PatchPackageReceipt",
            Summary = redacted ? "[redacted: receipt metadata unavailable]" : "safe receipt metadata",
            ReferenceOnly = true,
            GrantsAuthority = false,
            ContinuesWorkflow = false,
            Warnings = ["Receipt metadata is reference-only."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = observedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            FreshnessKnown = observedAtUtc.HasValue
        };

    private static FrontendOperationTimelineReadModel Timeline(
        IReadOnlyCollection<FrontendTimelineEntry> entries,
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null) =>
        new()
        {
            OperationId = OperationId,
            Entries = entries,
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = observedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            FreshnessKnown = observedAtUtc.HasValue || entries.Count > 0
        };

    private static FrontendTimelineEntry TimelineEntry() =>
        new()
        {
            EntryId = "timeline:a09",
            EventKind = "OperationObserved",
            Summary = "operation observed",
            EvidenceRefs = ["evidence:a09:timeline"],
            ReceiptRefs = [],
            ObservedAtUtc = ObservedAtUtc
        };

    private static FrontendTimelineEntry RedactedTimelineEntry() =>
        TimelineEntry() with
        {
            EventKind = "RedactedTimelineEvent",
            Summary = "[redacted: timeline event unavailable]"
        };

    private static FrontendPatchPackageMetadataReadModel PatchPackage(
        bool redacted = false,
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null) =>
        new()
        {
            PackageId = PackageId,
            Repository = redacted ? "[redacted]" : "repo",
            Branch = redacted ? "[redacted]" : "branch",
            RunId = redacted ? "[redacted]" : "run-a09",
            PatchHash = redacted ? "[redacted]" : "sha256:patch-a09",
            ProposedFilePaths = redacted ? [] : ["src/file.cs"],
            ArtifactRefs = redacted ? [] : ["artifact:a09"],
            EvidenceRefs = redacted ? [] : ["evidence:a09"],
            ReceiptRefs = redacted ? [] : ["receipt:a09"],
            ReviewSummaryRef = redacted ? "[redacted]" : "summary:a09",
            KnownRisksRef = redacted ? "[redacted]" : "risks:a09",
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = observedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            FreshnessKnown = observedAtUtc.HasValue
        };

    private static FrontendPatchPackageArtifactsReadModel Artifacts(
        bool validationIsStale = false,
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null) =>
        new()
        {
            PackageId = PackageId,
            Repository = "repo",
            Branch = "branch",
            RunId = "run-a09",
            PatchHash = "sha256:patch-a09",
            PatchDiffText = "diff --git a/src/file.cs b/src/file.cs",
            ReviewSummaryText = "review summary",
            KnownRisksText = "known risks",
            ValidationSummaryText = "validation summary",
            ValidationOutcome = "Passed",
            WhatRan = ["build"],
            WhatPassed = ["build"],
            WhatFailed = [],
            WhatWasSkipped = [],
            ValidationIsStale = validationIsStale,
            ProposedFilePaths = ["src/file.cs"],
            ArtifactRefs = ["artifact:a09"],
            EvidenceRefs = ["evidence:a09"],
            ReceiptRefs = ["receipt:a09"],
            AuthorityWarnings = ["Patch package artifacts are not authority."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = observedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            FreshnessKnown = observedAtUtc.HasValue
        };

    private static FrontendValidationResultMetadataReadModel Validation(
        bool isStale = false,
        bool redacted = false,
        bool freshnessKnown = true,
        DateTimeOffset? observedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null) =>
        new()
        {
            ValidationResultId = ValidationResultId,
            Repository = redacted ? "[redacted]" : "repo",
            Branch = redacted ? "[redacted]" : "branch",
            RunId = redacted ? "[redacted]" : "run-a09",
            PatchHash = redacted ? "[redacted]" : "sha256:patch-a09",
            Outcome = redacted ? "UnsafeValidationMetadata" : "Passed",
            WhatRan = redacted ? [] : ["build"],
            WhatPassed = redacted ? [] : ["build"],
            WhatFailed = [],
            WhatWasSkipped = redacted ? ["ValidationMetadataUnsafe"] : [],
            IsStale = isStale,
            EvidenceRefs = redacted ? [] : ["evidence:a09"],
            ReceiptRefs = redacted ? [] : ["receipt:a09"],
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            ObservedAtUtc = observedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            FreshnessKnown = freshnessKnown
        };

    private static FrontendReadinessApiEnvelope<T> Envelope<T>(ActionResult<FrontendReadinessApiEnvelope<T>> result)
    {
        var objectResult = result.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        return (FrontendReadinessApiEnvelope<T>)objectResult.Value!;
    }

    private static void AssertNoAuthority(FrontendReadinessReadState state)
    {
        Assert.IsFalse(state.IsAuthorityGrant);
        Assert.IsFalse(state.AllowsMutation);
        Assert.IsFalse(state.Boundary.CanCreateApproval);
        Assert.IsFalse(state.Boundary.CanAcceptApproval);
        Assert.IsFalse(state.Boundary.CanSatisfyPolicy);
        Assert.IsFalse(state.Boundary.CanExecute);
        Assert.IsFalse(state.Boundary.CanMutateSource);
        Assert.IsFalse(state.Boundary.CanRollback);
        Assert.IsFalse(state.Boundary.CanCommit);
        Assert.IsFalse(state.Boundary.CanPush);
        Assert.IsFalse(state.Boundary.CanCreatePullRequest);
        Assert.IsFalse(state.Boundary.CanMarkReadyForReview);
        Assert.IsFalse(state.Boundary.CanMerge);
        Assert.IsFalse(state.Boundary.CanRelease);
        Assert.IsFalse(state.Boundary.CanDeploy);
        Assert.IsFalse(state.Boundary.CanPromoteMemory);
        Assert.IsFalse(state.Boundary.CanContinueWorkflow);
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), $"Expected '{expected}'.");

    private static void AssertContainsText(string value, string expected) =>
        Assert.IsTrue(value.Contains(expected, StringComparison.OrdinalIgnoreCase), $"Expected text '{expected}'.");

    private static void AssertNotContains(string value, string forbidden) =>
        Assert.IsFalse(value.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Forbidden text '{forbidden}' was present.");

    private static string SourceSlice(string start, string end)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "FrontendReadinessReadModels.cs"));
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.IsTrue(startIndex >= 0);
        Assert.IsTrue(endIndex > startIndex);
        return source[startIndex..endIndex];
    }

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
        private readonly int? _tenantId;

        public SeededBackendTruthSource(int? tenantId = 42) => _tenantId = tenantId;

        public override string SourceName => "seeded-a09";
        public override int? TenantId => _tenantId;
        public IReadOnlyDictionary<string, GovernedOperationStatus> OperationStatuses { get; init; } =
            new Dictionary<string, GovernedOperationStatus>(StringComparer.OrdinalIgnoreCase);

        public override FrontendReadinessBackendReadResult<GovernedOperationStatus> ReadOperationStatus(
            string operationId,
            FrontendReadinessReadScope scope)
        {
            if (!IsVisibleTo(scope))
                return FrontendReadinessBackendReadResult<GovernedOperationStatus>.WithoutData(FrontendReadinessReadState.NotVisible());

            return OperationStatuses.TryGetValue(operationId, out var status)
                ? FrontendReadinessBackendReadResult<GovernedOperationStatus>.WithData(status, FrontendReadinessReadState.Available("OperationStatusAvailable"))
                : FrontendReadinessBackendReadResult<GovernedOperationStatus>.WithoutData(FrontendReadinessReadState.NotFound("OperationStatusNotFound"));
        }
    }

    private sealed class TestTenantContext : IronDev.Core.Auth.ICurrentTenantContext
    {
        public TestTenantContext(int tenantId) => TenantId = tenantId;

        public int TenantId { get; }
    }
}
