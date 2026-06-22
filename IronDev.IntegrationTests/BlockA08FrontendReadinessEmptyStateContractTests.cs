using IronDev.Api.Controllers;
using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA08FrontendReadinessEmptyStateContractTests
{
    private const string OperationId = "operation-a08";
    private const string EvidenceRef = "evidence:a08";
    private const string ReceiptRef = "receipt:a08";
    private const string PackageId = "patch-package:a08";
    private const string ValidationResultId = "validation-result:a08";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-23T06:00:00Z");

    [TestMethod]
    public void FrontendReadiness_ReadState_DefaultsToNoAuthority() =>
        AssertNoAuthority(FrontendReadinessReadState.Available());

    [TestMethod]
    public void FrontendReadiness_ReadState_UsesReadOnlyBoundary() =>
        AssertReadOnly(FrontendReadinessReadState.Available().Boundary);

    [TestMethod]
    public void FrontendReadiness_ReadState_UnknownDoesNotGrantAuthority() =>
        AssertNoAuthority(FrontendReadinessReadState.Unknown());

    [TestMethod]
    public void FrontendReadiness_ReadState_NotFoundDoesNotGrantAuthority() =>
        AssertNoAuthority(FrontendReadinessReadState.NotFound("MissingRecord"));

    [TestMethod]
    public void FrontendReadiness_ReadState_EmptyDoesNotGrantAuthority() =>
        AssertNoAuthority(FrontendReadinessReadState.Empty("NoVisibleEntries"));

    [TestMethod]
    public void FrontendReadiness_ReadState_RedactedDoesNotGrantAuthority() =>
        AssertNoAuthority(FrontendReadinessReadState.Redacted("UnsafeOrPrivateMaterialRedacted"));

    [TestMethod]
    public void FrontendReadiness_ReadState_UnavailableDoesNotGrantAuthority() =>
        AssertNoAuthority(FrontendReadinessReadState.Unavailable());

    [TestMethod]
    public void FrontendReadiness_ReadState_StaleDoesNotGrantAuthority() =>
        AssertNoAuthority(FrontendReadinessReadState.Stale("ValidationResultStale"));

    [TestMethod]
    public void FrontendReadiness_ReadState_InvalidDoesNotGrantAuthority() =>
        AssertNoAuthority(FrontendReadinessReadState.Invalid("InvalidStoredMetadata"));

    [TestMethod]
    public void FrontendReadiness_MissingOperationStatusReturnsNotFoundState()
    {
        var envelope = Envelope(Controller(FrontendReadinessReadApi.Empty).GetOperationStatus("missing-operation"));

        Assert.AreEqual(FrontendReadinessReadStateKind.NotFound, envelope.ReadState.Kind);
        Assert.IsNull(envelope.Data);
        AssertContains(envelope.ReadState.Reasons, "OperationStatusNotFound");
    }

    [TestMethod]
    public void FrontendReadiness_MissingEvidenceMetadataReturnsNotFoundState()
    {
        var envelope = Envelope(Controller(FrontendReadinessReadApi.Empty).GetEvidenceMetadata("missing-evidence"));

        Assert.AreEqual(FrontendReadinessReadStateKind.NotFound, envelope.ReadState.Kind);
        Assert.IsNull(envelope.Data);
        AssertContains(envelope.ReadState.Reasons, "EvidenceMetadataNotFound");
    }

    [TestMethod]
    public void FrontendReadiness_MissingReceiptMetadataReturnsNotFoundState()
    {
        var envelope = Envelope(Controller(FrontendReadinessReadApi.Empty).GetReceiptMetadata("missing-receipt"));

        Assert.AreEqual(FrontendReadinessReadStateKind.NotFound, envelope.ReadState.Kind);
        Assert.IsNull(envelope.Data);
        AssertContains(envelope.ReadState.Reasons, "ReceiptMetadataNotFound");
    }

    [TestMethod]
    public void FrontendReadiness_MissingTimelineReturnsNotFoundState()
    {
        var envelope = Envelope(Controller(FrontendReadinessReadApi.Empty).GetOperationTimeline("missing-operation"));

        Assert.AreEqual(FrontendReadinessReadStateKind.NotFound, envelope.ReadState.Kind);
        Assert.IsNull(envelope.Data);
        AssertContains(envelope.ReadState.Reasons, "OperationTimelineNotFound");
    }

    [TestMethod]
    public void FrontendReadiness_MissingPatchPackageMetadataReturnsNotFoundState()
    {
        var envelope = Envelope(Controller(FrontendReadinessReadApi.Empty).GetPatchPackageMetadata("missing-package"));

        Assert.AreEqual(FrontendReadinessReadStateKind.NotFound, envelope.ReadState.Kind);
        Assert.IsNull(envelope.Data);
        AssertContains(envelope.ReadState.Reasons, "PatchPackageMetadataNotFound");
    }

    [TestMethod]
    public void FrontendReadiness_MissingValidationResultMetadataReturnsNotFoundState()
    {
        var envelope = Envelope(Controller(FrontendReadinessReadApi.Empty).GetValidationResultMetadata("missing-validation"));

        Assert.AreEqual(FrontendReadinessReadStateKind.NotFound, envelope.ReadState.Kind);
        Assert.IsNull(envelope.Data);
        AssertContains(envelope.ReadState.Reasons, "ValidationResultMetadataNotFound");
    }

    [TestMethod]
    public void FrontendReadiness_EmptyTimelineReturnsEmptyStateOnlyWhenExistenceKnown()
    {
        var envelope = Envelope(Controller(Api(Timeline(entries: []))).GetOperationTimeline(OperationId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Empty, envelope.ReadState.Kind);
        Assert.IsNotNull(envelope.Data);
        AssertContains(envelope.ReadState.Reasons, "NoVisibleTimelineEntries");
    }

    [TestMethod]
    public void FrontendReadiness_EmptyRefsReturnAvailableWithEmptyCollections()
    {
        var envelope = Envelope(Controller(Api(PatchPackage(artifactRefs: [], evidenceRefs: [], receiptRefs: [])))
            .GetPatchPackageMetadata(PackageId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, envelope.ReadState.Kind);
        Assert.AreEqual(0, envelope.Data!.ArtifactRefs.Count);
        Assert.AreEqual(0, envelope.Data.EvidenceRefs.Count);
        Assert.AreEqual(0, envelope.Data.ReceiptRefs.Count);
    }

    [TestMethod]
    public void FrontendReadiness_EmptyIsNotSuccessForMissingRecord()
    {
        var envelope = Envelope(Controller(FrontendReadinessReadApi.Empty).GetOperationTimeline("missing-operation"));

        Assert.AreNotEqual(FrontendReadinessReadStateKind.Empty, envelope.ReadState.Kind);
        Assert.AreEqual(FrontendReadinessReadStateKind.NotFound, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_RedactedEvidenceMetadataReturnsRedactedState()
    {
        var envelope = Envelope(Controller(Api(Evidence(redacted: true))).GetEvidenceMetadata(EvidenceRef));

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, envelope.ReadState.Kind);
        Assert.IsTrue(envelope.ReadState.IsRedacted);
        AssertContains(envelope.ReadState.Reasons, "EvidenceMetadataUnsafe");
    }

    [TestMethod]
    public void FrontendReadiness_RedactedReceiptMetadataReturnsRedactedState()
    {
        var envelope = Envelope(Controller(Api(Receipt(redacted: true))).GetReceiptMetadata(ReceiptRef));

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, envelope.ReadState.Kind);
        AssertContains(envelope.ReadState.Reasons, "ReceiptMetadataUnsafe");
    }

    [TestMethod]
    public void FrontendReadiness_RedactedTimelineEntryReturnsRedactedState()
    {
        var envelope = Envelope(Controller(Api(Timeline(entries: [RedactedTimelineEntry()])))
            .GetOperationTimeline(OperationId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, envelope.ReadState.Kind);
        AssertContains(envelope.ReadState.Reasons, "TimelineEventRedacted");
    }

    [TestMethod]
    public void FrontendReadiness_RedactedPatchPackageMetadataReturnsRedactedState()
    {
        var envelope = Envelope(Controller(Api(PatchPackage(redacted: true))).GetPatchPackageMetadata(PackageId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, envelope.ReadState.Kind);
        AssertContains(envelope.ReadState.Reasons, "PatchPackageMetadataUnsafe");
    }

    [TestMethod]
    public void FrontendReadiness_RedactedValidationMetadataReturnsRedactedState()
    {
        var envelope = Envelope(Controller(Api(Validation(redacted: true))).GetValidationResultMetadata(ValidationResultId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, envelope.ReadState.Kind);
        AssertContains(envelope.ReadState.Reasons, "ValidationResultMetadataUnsafe");
    }

    [TestMethod]
    public void FrontendReadiness_RedactedStateDoesNotFallbackToUnsafeRunReport()
    {
        var canonical = new SeededBackendTruthSource { Evidence = Map(Evidence(redacted: true)) };
        var fallback = new SeededBackendTruthSource { Evidence = Map(Evidence(summary: "fallback evidence should not win")) };
        var api = new BackendFrontendReadinessReadApi([canonical, fallback], new TestTenantContext(42));

        var envelope = Envelope(Controller(api).GetEvidenceMetadata(EvidenceRef));

        Assert.AreEqual(FrontendReadinessReadStateKind.Redacted, envelope.ReadState.Kind);
        Assert.AreNotEqual("fallback evidence should not win", envelope.Data!.Summary);
    }

    [TestMethod]
    public void FrontendReadiness_StaleValidationReturnsStaleState()
    {
        var envelope = Envelope(Controller(Api(Validation(isStale: true))).GetValidationResultMetadata(ValidationResultId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, envelope.ReadState.Kind);
        Assert.IsTrue(envelope.ReadState.IsStale);
    }

    [TestMethod]
    public void FrontendReadiness_FreshnessUnknownReturnsStaleState()
    {
        var envelope = Envelope(Controller(Api(Validation(isStale: true, skipped: ["FreshnessUnknown"])))
            .GetValidationResultMetadata(ValidationResultId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_FreshnessUnknownIncludesSkippedMarker()
    {
        var envelope = Envelope(Controller(Api(Validation(isStale: true, skipped: ["FreshnessUnknown"])))
            .GetValidationResultMetadata(ValidationResultId));

        AssertContains(envelope.Data!.WhatWasSkipped, "FreshnessUnknown");
    }

    [TestMethod]
    public void FrontendReadiness_PassedButStaleValidationDoesNotBecomeAvailableFresh()
    {
        var envelope = Envelope(Controller(Api(Validation(outcome: "Passed", isStale: true)))
            .GetValidationResultMetadata(ValidationResultId));

        Assert.AreEqual("Passed", envelope.Data!.Outcome);
        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_ExpiredValidationReturnsStaleState()
    {
        var envelope = Envelope(Controller(Api(Validation(isStale: true, skipped: ["ValidationExpired"])))
            .GetValidationResultMetadata(ValidationResultId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Stale, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_WrongTenantRecordReturnsNotVisibleWithoutLeakingDetails()
    {
        var hiddenSource = new SeededBackendTruthSource { Tenant = 41, Evidence = Map(Evidence(summary: "foreign evidence")) };
        var api = new BackendFrontendReadinessReadApi([hiddenSource], new TestTenantContext(42));

        var envelope = Envelope(Controller(api).GetEvidenceMetadata(EvidenceRef));

        Assert.AreEqual(FrontendReadinessReadStateKind.NotVisible, envelope.ReadState.Kind);
        Assert.IsNull(envelope.Data);
        AssertContains(envelope.ReadState.Reasons, "NotFoundOrNotVisible");
        Assert.IsFalse(string.Join(" ", envelope.ReadState.Reasons).Contains("foreign evidence", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FrontendReadiness_TenantlessTenantScopedRecordReturnsNotVisibleOrNotFound()
    {
        var envelope = Envelope(Controller(FrontendReadinessReadApi.Empty).GetEvidenceMetadata(EvidenceRef));

        Assert.IsTrue(
            envelope.ReadState.Kind is FrontendReadinessReadStateKind.NotFound or FrontendReadinessReadStateKind.NotVisible);
    }

    [TestMethod]
    public void FrontendReadiness_UnscopedTenantReadReturnsNotVisibleOrNotFound()
    {
        var source = new SeededBackendTruthSource { Tenant = 42, Evidence = Map(Evidence()) };
        var api = new BackendFrontendReadinessReadApi([source], tenantContext: null);

        var envelope = Envelope(Controller(api).GetEvidenceMetadata(EvidenceRef));

        Assert.IsTrue(
            envelope.ReadState.Kind is FrontendReadinessReadStateKind.NotFound or FrontendReadinessReadStateKind.NotVisible);
    }

    [TestMethod]
    public void FrontendReadiness_NotVisibleStateDoesNotGrantAuthority() =>
        AssertNoAuthority(FrontendReadinessReadState.NotVisible());

    [TestMethod]
    public void FrontendReadiness_BackendTruthUnavailableReturnsUnavailableState()
    {
        var envelope = Envelope(Controller(new ThrowingReadApi()).GetOperationStatus(OperationId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Unavailable, envelope.ReadState.Kind);
        Assert.IsNull(envelope.Data);
    }

    [TestMethod]
    public void FrontendReadiness_BackendTruthUnavailableDoesNotFallbackToRunReport()
    {
        var throwing = new ThrowingSource();
        var fallback = new SeededBackendTruthSource { Statuses = Map(Status()) };
        var api = new BackendFrontendReadinessReadApi([throwing, fallback], new TestTenantContext(42));

        var envelope = Envelope(Controller(api).GetOperationStatus(OperationId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Unavailable, envelope.ReadState.Kind);
        Assert.IsNull(envelope.Data);
    }

    [TestMethod]
    public void FrontendReadiness_BackendTruthNotFoundMayUseFallback()
    {
        var missing = new SeededBackendTruthSource();
        var fallback = new SeededBackendTruthSource { Statuses = Map(Status()) };
        var api = new BackendFrontendReadinessReadApi([missing, fallback], new TestTenantContext(42));

        var envelope = Envelope(Controller(api).GetOperationStatus(OperationId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Available, envelope.ReadState.Kind);
        Assert.IsNotNull(envelope.Data);
    }

    [TestMethod]
    public void FrontendReadiness_InvalidCanonicalStateDoesNotFallbackToRunReport()
    {
        var invalid = Status(blockedReasons: ["StoredOperationStatusInvalid"]);
        var canonical = new SeededBackendTruthSource { Statuses = Map(invalid) };
        var fallback = new SeededBackendTruthSource { Statuses = Map(Status(operationKind: "FallbackStatus")) };
        var api = new BackendFrontendReadinessReadApi([canonical, fallback], new TestTenantContext(42));

        var envelope = Envelope(Controller(api).GetOperationStatus(OperationId));

        Assert.AreEqual(FrontendReadinessReadStateKind.Invalid, envelope.ReadState.Kind);
        Assert.AreNotEqual("FallbackStatus", envelope.Data!.OperationKind);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelopeAlwaysIncludesReadState()
    {
        var envelope = Envelope(Controller(Api(Status())).GetOperationStatus(OperationId));

        Assert.IsNotNull(envelope.ReadState);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelopeNullDataHasExplicitReason()
    {
        var envelope = Envelope(Controller(FrontendReadinessReadApi.Empty).GetOperationStatus("missing-operation"));

        Assert.IsNull(envelope.Data);
        Assert.IsTrue(envelope.ReadState.Reasons.Count > 0);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelopeWarningsPreserveAuthorityBoundary()
    {
        var envelope = Envelope(Controller(Api(Status())).GetOperationStatus(OperationId));

        AssertContains(envelope.Warnings, "Read state is not approval.");
        AssertContains(envelope.Warnings, "Read state does not allow mutation or workflow continuation.");
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelopeCompactModeCannotHideReadState()
    {
        var envelope = Envelope(Controller(Api(Status())).GetOperationStatus(OperationId, compact: true));

        Assert.IsNotNull(envelope.ReadState);
        Assert.AreEqual(FrontendReadinessReadStateKind.Available, envelope.ReadState.Kind);
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelopeCompactModeCannotHideWarnings()
    {
        var envelope = Envelope(Controller(Api(Status())).GetOperationStatus(OperationId, compact: true));

        AssertContains(envelope.Warnings, "Compact mode was requested but authority-critical fields are still returned.");
        AssertContains(envelope.Warnings, "Read state is not approval.");
    }

    [TestMethod]
    public void FrontendReadiness_ApiEnvelopeCompactModeCannotHideBoundary()
    {
        var envelope = Envelope(Controller(Api(Status())).GetOperationStatus(OperationId, compact: true));

        AssertReadOnly(envelope.Boundary);
        AssertReadOnly(envelope.ReadState.Boundary);
    }

    [TestMethod]
    public void FrontendReadiness_EmptyStateCannotCreateApproval() =>
        Assert.IsFalse(FrontendReadinessReadState.Empty("NoVisibleEntries").Boundary.CanCreateApproval);

    [TestMethod]
    public void FrontendReadiness_EmptyStateCannotSatisfyPolicy() =>
        Assert.IsFalse(FrontendReadinessReadState.Empty("NoVisibleEntries").Boundary.CanSatisfyPolicy);

    [TestMethod]
    public void FrontendReadiness_EmptyStateCannotExecute() =>
        Assert.IsFalse(FrontendReadinessReadState.Empty("NoVisibleEntries").Boundary.CanExecute);

    [TestMethod]
    public void FrontendReadiness_EmptyStateCannotMutateSource() =>
        Assert.IsFalse(FrontendReadinessReadState.Empty("NoVisibleEntries").Boundary.CanMutateSource);

    [TestMethod]
    public void FrontendReadiness_EmptyStateCannotCommitPushOrCreatePr()
    {
        var state = FrontendReadinessReadState.Empty("NoVisibleEntries");

        Assert.IsFalse(state.Boundary.CanCommit);
        Assert.IsFalse(state.Boundary.CanPush);
        Assert.IsFalse(state.Boundary.CanCreatePullRequest);
    }

    [TestMethod]
    public void FrontendReadiness_EmptyStateCannotMergeReleaseOrDeploy()
    {
        var state = FrontendReadinessReadState.Empty("NoVisibleEntries");

        Assert.IsFalse(state.Boundary.CanMerge);
        Assert.IsFalse(state.Boundary.CanRelease);
        Assert.IsFalse(state.Boundary.CanDeploy);
    }

    [TestMethod]
    public void FrontendReadiness_EmptyStateCannotPromoteMemoryOrContinueWorkflow()
    {
        var state = FrontendReadinessReadState.Empty("NoVisibleEntries");

        Assert.IsFalse(state.Boundary.CanPromoteMemory);
        Assert.IsFalse(state.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void StaticScan_A08AddsNoMutationEndpoint()
    {
        var source = A08Source();

        foreach (var marker in new[]
                 {
                     "[HttpPost]",
                     "[HttpPut]",
                     "[HttpPatch]",
                     "[HttpDelete]",
                     "CreateApproval",
                     "AcceptApproval",
                     "SatisfyPolicy",
                     "GrantsAuthority = true",
                     "AllowsMutation = true",
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
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A08AddsNoExecutorOrProviderMutationPath()
    {
        var source = A08Source();

        foreach (var marker in new[]
                 {
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
                     "ApplyPatch",
                     "ApplySource",
                     "ProcessStartInfo",
                     "RunProcessAsync",
                     "git apply",
                     "git commit",
                     "git push",
                     "gh pr create"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A08DoesNotReadRawPayloads()
    {
        var source = A08Source();

        foreach (var marker in new[]
                 {
                     "ReadValidationLogAsync",
                     "ReadValidationOutputAsync",
                     "ReadCommandOutputAsync",
                     "ReadBuildOutputAsync",
                     "ReadTestOutputAsync",
                     "ReadPatchPayloadAsync",
                     "ReadPatchTextAsync",
                     "ReadDiffTextAsync",
                     "ReadTimelinePayloadAsync",
                     "ReadEventPayloadAsync",
                     "ReadReceiptTextAsync",
                     "ReadEvidenceTextAsync",
                     "rawPrompt",
                     "rawCompletion",
                     "rawToolOutput",
                     "rawPatch",
                     "rawLog",
                     "full diff",
                     "diff --git"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    [TestMethod]
    public void StaticScan_A08DoesNotExposePrivateMaterial()
    {
        var source = A08Source();

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
                     "private key"
                 })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), marker);
        }
    }

    private static FrontendReadinessController Controller(IFrontendReadinessReadApi api) => new(api);

    private static IFrontendReadinessReadApi Api(params object[] values)
    {
        var snapshot = new FrontendReadinessReadSnapshot
        {
            OperationStatuses = values.OfType<GovernedOperationStatus>().ToDictionary(item => item.OperationId, StringComparer.OrdinalIgnoreCase),
            Timelines = values.OfType<FrontendOperationTimelineReadModel>().ToDictionary(item => item.OperationId, StringComparer.OrdinalIgnoreCase),
            PatchPackages = values.OfType<FrontendPatchPackageMetadataReadModel>().ToDictionary(item => item.PackageId, StringComparer.OrdinalIgnoreCase),
            ValidationResults = values.OfType<FrontendValidationResultMetadataReadModel>().ToDictionary(item => item.ValidationResultId, StringComparer.OrdinalIgnoreCase),
            Evidence = values.OfType<FrontendEvidenceMetadataReadModel>().ToDictionary(item => item.EvidenceRef, StringComparer.OrdinalIgnoreCase),
            Receipts = values.OfType<FrontendReceiptMetadataReadModel>().ToDictionary(item => item.ReceiptRef, StringComparer.OrdinalIgnoreCase)
        };

        return new FrontendReadinessReadApi(snapshot);
    }

    private static GovernedOperationStatus Status(
        string operationKind = "SourceApply",
        IReadOnlyList<string>? blockedReasons = null) =>
        new()
        {
            OperationId = OperationId,
            OperationKind = operationKind,
            Subject = "repo:BigDaddyDread-code/IronDeveloper branch:frontend/readiness-empty-state-contract",
            State = GovernedOperationState.Blocked,
            BlockedReasons = blockedReasons ?? ["BlockedForA08"],
            MissingEvidence = ["a08-required-evidence"],
            NextSafeActions = ["inspect frontend readiness read state"],
            ForbiddenActions = ["do not mutate from frontend readiness read state"],
            EvidenceRefs = [EvidenceRef],
            ReceiptRefs = [ReceiptRef],
            ObservedAtUtc = ObservedAtUtc,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1)
        };

    private static FrontendOperationTimelineReadModel Timeline(IReadOnlyCollection<FrontendTimelineEntry>? entries = null) =>
        new()
        {
            OperationId = OperationId,
            Entries = entries ??
            [
                new FrontendTimelineEntry
                {
                    EntryId = "timeline-entry:a08",
                    EventKind = "BackendReadObserved",
                    Summary = "Backend read observed.",
                    EvidenceRefs = [EvidenceRef],
                    ReceiptRefs = [ReceiptRef],
                    ObservedAtUtc = ObservedAtUtc
                }
            ],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendTimelineEntry RedactedTimelineEntry() =>
        new()
        {
            EntryId = "timeline-entry:a08:redacted",
            EventKind = "RedactedTimelineEvent",
            Summary = "[redacted: timeline event unavailable]",
            EvidenceRefs = [],
            ReceiptRefs = [],
            ObservedAtUtc = ObservedAtUtc
        };

    private static FrontendPatchPackageMetadataReadModel PatchPackage(
        IReadOnlyCollection<string>? artifactRefs = null,
        IReadOnlyCollection<string>? evidenceRefs = null,
        IReadOnlyCollection<string>? receiptRefs = null,
        bool redacted = false) =>
        new()
        {
            PackageId = PackageId,
            Repository = redacted ? "[redacted]" : "BigDaddyDread-code/IronDeveloper",
            Branch = redacted ? "[redacted]" : "frontend/readiness-empty-state-contract",
            RunId = redacted ? "[redacted]" : "run-a08",
            PatchHash = redacted ? "[redacted]" : "sha256:a08",
            ProposedFilePaths = redacted ? [] : ["IronDev.Core/Governance/FrontendReadinessReadModels.cs"],
            ArtifactRefs = artifactRefs ?? ["patch-artifact:a08"],
            EvidenceRefs = evidenceRefs ?? [EvidenceRef],
            ReceiptRefs = receiptRefs ?? [ReceiptRef],
            ReviewSummaryRef = redacted ? "[redacted]" : "review-summary:a08",
            KnownRisksRef = redacted ? "[redacted]" : "known-risks:a08",
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendValidationResultMetadataReadModel Validation(
        string outcome = "Passed",
        bool isStale = false,
        bool redacted = false,
        IReadOnlyCollection<string>? skipped = null) =>
        new()
        {
            ValidationResultId = ValidationResultId,
            Repository = redacted ? "[redacted]" : "BigDaddyDread-code/IronDeveloper",
            Branch = redacted ? "[redacted]" : "frontend/readiness-empty-state-contract",
            RunId = redacted ? "[redacted]" : "run-a08",
            PatchHash = redacted ? "[redacted]" : "sha256:a08",
            Outcome = redacted ? "UnsafeValidationMetadata" : outcome,
            WhatRan = redacted ? [] : ["A08 focused"],
            WhatPassed = redacted ? [] : ["A08 focused"],
            WhatFailed = [],
            WhatWasSkipped = redacted ? ["ValidationMetadataUnsafe"] : skipped ?? [],
            IsStale = redacted || isStale,
            EvidenceRefs = redacted ? [] : [EvidenceRef],
            ReceiptRefs = redacted ? [] : [ReceiptRef],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendEvidenceMetadataReadModel Evidence(string summary = "Evidence metadata.", bool redacted = false) =>
        new()
        {
            EvidenceRef = EvidenceRef,
            EvidenceKind = redacted ? "RedactedEvidenceMetadata" : "ValidationEvidence",
            Summary = redacted ? "[redacted: evidence metadata unavailable]" : summary,
            ReferenceOnly = true,
            ContainsRawPayload = false,
            Warnings = redacted ? ["Evidence metadata was redacted because it contained unsafe or private material."] : ["Evidence is reference-only."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendReceiptMetadataReadModel Receipt(bool redacted = false) =>
        new()
        {
            ReceiptRef = ReceiptRef,
            ReceiptKind = redacted ? "RedactedReceiptMetadata" : "ValidationReceipt",
            Summary = redacted ? "[redacted: receipt metadata unavailable]" : "Receipt metadata.",
            ReferenceOnly = true,
            GrantsAuthority = false,
            ContinuesWorkflow = false,
            Warnings = redacted ? ["Receipt metadata was redacted because it contained unsafe or private material."] : ["Receipt is reference-only."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static IReadOnlyDictionary<string, T> Map<T>(T item)
    {
        var key = item switch
        {
            GovernedOperationStatus status => status.OperationId,
            FrontendEvidenceMetadataReadModel evidence => evidence.EvidenceRef,
            _ => throw new InvalidOperationException("Unsupported map item.")
        };

        return new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = item
        };
    }

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

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(Environment.NewLine, values));

    private static string A08Source()
    {
        var root = FindRepositoryRoot();
        var core = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "FrontendReadinessReadModels.cs"));
        var a08Core = Slice(core, "public enum FrontendReadinessReadStateKind", "public sealed record FrontendOperationStatusReadModel");
        var backend = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "BackendFrontendReadinessReadApi.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "FrontendReadinessController.cs"))
            .Replace("[HttpPost(\"action-requests\")]", string.Empty, StringComparison.Ordinal);

        return string.Join(Environment.NewLine, a08Core, backend, controller);
    }

    private static string Slice(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        var endIndex = value.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex)
            throw new InvalidOperationException("Could not find A08 source slice.");

        return value[startIndex..endIndex];
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

    private sealed class SeededBackendTruthSource : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "seeded-a08";
        public int? Tenant { get; init; }
        public override int? TenantId => Tenant;
        public IReadOnlyDictionary<string, GovernedOperationStatus> Statuses { get; init; } =
            new Dictionary<string, GovernedOperationStatus>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, FrontendEvidenceMetadataReadModel> Evidence { get; init; } =
            new Dictionary<string, FrontendEvidenceMetadataReadModel>(StringComparer.OrdinalIgnoreCase);

        public override GovernedOperationStatus? GetOperationStatus(string operationId) =>
            Statuses.GetValueOrDefault(operationId);

        public override FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(string evidenceRef) =>
            Evidence.GetValueOrDefault(evidenceRef);
    }

    private sealed class ThrowingSource : FrontendReadinessBackendTruthSource
    {
        public override string SourceName => "throwing-a08";

        public override GovernedOperationStatus? GetOperationStatus(string operationId) =>
            throw new InvalidOperationException("backend unavailable");
    }

    private sealed class ThrowingReadApi : IFrontendReadinessReadApi
    {
        public FrontendOperationStatusReadModel? GetOperationStatus(string operationId) =>
            throw new InvalidOperationException("backend unavailable");

        public FrontendReadinessReadState GetOperationStatusReadState(string operationId) =>
            FrontendReadinessReadState.Unavailable();

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

    private sealed class TestTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }
}
