using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD10EvidenceResolverRedactionTests
{
    private const string TenantId = "tenant-d10";
    private const string ProjectId = "project-d10";
    private const string OperationId = "op_0000000000000010";
    private const string CorrelationId = "corr_1123456789abcdea";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T10:00:00Z");

    [TestMethod]
    public void ValidRequestWithNoReferences_ReturnsNoReferences()
    {
        var result = Resolve([], [], []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(EvidenceResolutionStatus.NoReferences, result.ResolutionStatus);
        Assert.AreEqual(0, result.ResolvedEvidence.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "evidence found is not authority");
    }

    [TestMethod]
    public void AllRequestedReferencesResolved_ReturnsResolved()
    {
        var result = Resolve(
            [
                Requested("evidence-b", EvidenceReferenceKind.CommitEvidence),
                Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence)
            ],
            [
                Available("evidence-b", EvidenceReferenceKind.CommitEvidence),
                Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)
            ],
            []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(EvidenceResolutionStatus.Resolved, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            new[] { "evidence-a", "evidence-b" },
            result.ResolvedEvidence.Select(static item => item.EvidenceId).ToArray());
        AssertContains(result.Warnings, "complete evidence resolution is not action allowed");
    }

    [TestMethod]
    public void SomeResolvedAndSomeMissing_ReturnsPartiallyResolved()
    {
        var result = Resolve(
            [
                Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence),
                Requested("evidence-missing", EvidenceReferenceKind.CommitEvidence)
            ],
            [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)],
            []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(EvidenceResolutionStatus.PartiallyResolved, result.ResolutionStatus);
        Assert.AreEqual(1, result.ResolvedEvidence.Count);
        Assert.AreEqual(1, result.UnresolvedEvidence.Count);
        Assert.AreEqual("MatchingEvidenceMetadataNotFound", result.UnresolvedEvidence[0].Reason);
    }

    [TestMethod]
    public void NoneResolved_ReturnsNotFound()
    {
        var result = Resolve(
            [
                Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence),
                Requested("evidence-b", EvidenceReferenceKind.CommitEvidence)
            ],
            [Available("evidence-c", EvidenceReferenceKind.RollbackEvidence)],
            []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(EvidenceResolutionStatus.NotFound, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            new[] { "evidence-a", "evidence-b" },
            result.UnresolvedEvidence.Select(static item => item.EvidenceReferenceId).ToArray());
        AssertContains(result.Warnings, "missing evidence does not choose next safe action");
    }

    [TestMethod]
    public void DuplicateRequestedReferenceIds_ReturnAmbiguousEvidence()
    {
        var result = Resolve(
            [
                Requested("evidence-duplicate", EvidenceReferenceKind.ValidationEvidence),
                Requested("evidence-duplicate", EvidenceReferenceKind.ValidationEvidence)
            ],
            [Available("evidence-duplicate", EvidenceReferenceKind.ValidationEvidence)],
            []);

        AssertAmbiguous(result, "DuplicateRequestedEvidenceReferenceId:evidence-duplicate");
    }

    [TestMethod]
    public void DuplicateAvailableEvidenceIds_ReturnAmbiguousEvidence()
    {
        var result = Resolve(
            [Requested("evidence-duplicate", EvidenceReferenceKind.ValidationEvidence)],
            [
                Available("evidence-duplicate", EvidenceReferenceKind.ValidationEvidence),
                Available("evidence-duplicate", EvidenceReferenceKind.ValidationEvidence)
            ],
            []);

        AssertAmbiguous(result, "DuplicateAvailableEvidenceId:evidence-duplicate");
    }

    [TestMethod]
    public void ConflictingMetadataForSameEvidenceId_ReturnsAmbiguousEvidence()
    {
        var result = Resolve(
            [Requested("evidence-conflict", EvidenceReferenceKind.ValidationEvidence)],
            [
                Available("evidence-conflict", EvidenceReferenceKind.ValidationEvidence, source: "source-a"),
                Available("evidence-conflict", EvidenceReferenceKind.ValidationEvidence, source: "source-b")
            ],
            []);

        AssertAmbiguous(result, "ConflictingAvailableEvidenceMetadata:evidence-conflict");
    }

    [TestMethod]
    public void DuplicateSuppliedPayloadIds_ReturnAmbiguousEvidence()
    {
        var result = Resolve(
            [Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, requestRedactedPreview: true)],
            [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)],
            [
                Payload("evidence-a", "safe payload"),
                Payload("evidence-a", "safe payload")
            ]);

        AssertAmbiguous(result, "DuplicateSuppliedEvidencePayloadId:evidence-a");
    }

    [TestMethod]
    public void ConflictingSuppliedPayloadMetadata_ReturnsAmbiguousEvidence()
    {
        var result = Resolve(
            [Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, requestRedactedPreview: true)],
            [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)],
            [
                Payload("evidence-a", "safe payload", source: "payload-a"),
                Payload("evidence-a", "safe payload", source: "payload-b")
            ]);

        Assert.AreEqual(EvidenceResolutionStatus.AmbiguousEvidence, result.ResolutionStatus);
        AssertContains(result.AmbiguousEvidence, "DuplicateSuppliedEvidencePayloadId:evidence-a");
        AssertContains(result.AmbiguousEvidence, "ConflictingSuppliedEvidencePayloadMetadata:evidence-a");
    }

    [TestMethod]
    public void MultipleMatchingEvidenceRecordsForOneReference_ReturnsAmbiguousEvidence()
    {
        var requested = Requested(
            "request-ref",
            EvidenceReferenceKind.CommitEvidence,
            referenceKind: OperationReferenceKind.CommitSha,
            referenceId: "commit-ref-1");
        var result = Resolve(
            [requested],
            [
                Available(
                    "evidence-a",
                    EvidenceReferenceKind.CommitEvidence,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1"),
                Available(
                    "evidence-b",
                    EvidenceReferenceKind.CommitEvidence,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1")
            ],
            []);

        AssertAmbiguous(result, "MultipleAvailableEvidenceMatchReference:request-ref");
    }

    [TestMethod]
    public void AmbiguityDoesNotChooseWinner()
    {
        var result = Resolve(
            [
                Requested(
                    "request-a",
                    EvidenceReferenceKind.CommitEvidence,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1"),
                Requested(
                    "request-b",
                    EvidenceReferenceKind.CommitEvidence,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1")
            ],
            [
                Available(
                    "evidence-a",
                    EvidenceReferenceKind.CommitEvidence,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1")
            ],
            []);

        Assert.AreEqual(EvidenceResolutionStatus.AmbiguousEvidence, result.ResolutionStatus);
        Assert.AreEqual(0, result.ResolvedEvidence.Count);
        AssertContains(result.AmbiguousEvidence, $"IndistinguishableRequestedEvidenceReferences:{EvidenceReferenceKind.CommitEvidence}:{OperationReferenceKind.CommitSha}:commit-ref-1:{CorrelationId}");
        AssertContains(result.AmbiguousEvidence, "AmbiguousEvidenceAssignment:evidence-a");
        AssertContains(result.Warnings, "ambiguous evidence does not choose a winner");
    }

    [TestMethod]
    public void ResolvedAndUnresolvedEvidenceSortDeterministically()
    {
        var result = Resolve(
            [
                Requested("evidence-c", EvidenceReferenceKind.RollbackEvidence),
                Requested("evidence-b", EvidenceReferenceKind.CommitEvidence),
                Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence)
            ],
            [
                Available("evidence-b", EvidenceReferenceKind.CommitEvidence),
                Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)
            ],
            []);

        Assert.AreEqual(EvidenceResolutionStatus.PartiallyResolved, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            new[] { "evidence-a", "evidence-b" },
            result.ResolvedEvidence.Select(static item => item.EvidenceId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "evidence-c" },
            result.UnresolvedEvidence.Select(static item => item.EvidenceReferenceId).ToArray());
    }

    [TestMethod]
    public void DirectEvidenceIdReferenceResolvesByEvidenceId()
    {
        var result = Resolve(
            [Requested("evidence-direct", EvidenceReferenceKind.ValidationEvidence)],
            [Available("evidence-direct", EvidenceReferenceKind.ValidationEvidence)],
            []);

        Assert.AreEqual(EvidenceResolutionStatus.Resolved, result.ResolutionStatus);
        Assert.AreEqual("evidence-direct", result.ResolvedEvidence[0].EvidenceId);
    }

    [DataTestMethod]
    [DataRow(EvidenceReferenceKind.CommitEvidence, EvidenceReferenceKind.PushEvidence)]
    [DataRow(EvidenceReferenceKind.ReleaseReadinessEvidence, EvidenceReferenceKind.DeploymentReadinessEvidence)]
    public void EvidenceKindMustMatchExactly(
        EvidenceReferenceKind requestedKind,
        EvidenceReferenceKind availableKind)
    {
        var result = Resolve(
            [Requested("evidence-a", requestedKind)],
            [Available("evidence-a", availableKind)],
            []);

        Assert.AreEqual(EvidenceResolutionStatus.NotFound, result.ResolutionStatus);
        Assert.AreEqual(0, result.ResolvedEvidence.Count);
    }

    [TestMethod]
    public void ReferenceKindAndIdMustMatchExactlyWhenSupplied()
    {
        var result = Resolve(
            [
                Requested(
                    "request-ref",
                    EvidenceReferenceKind.PushEvidence,
                    referenceKind: OperationReferenceKind.PushId,
                    referenceId: "push-1")
            ],
            [
                Available(
                    "evidence-a",
                    EvidenceReferenceKind.PushEvidence,
                    referenceKind: OperationReferenceKind.PushId,
                    referenceId: "push-2")
            ],
            []);

        Assert.AreEqual(EvidenceResolutionStatus.NotFound, result.ResolutionStatus);
    }

    [TestMethod]
    public void CorrelationIdMustMatch()
    {
        var result = Resolve(
            [Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence)],
            [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence, correlationId: "corr_2223456789abcdef")],
            []);

        Assert.AreEqual(EvidenceResolutionStatus.NotFound, result.ResolutionStatus);
    }

    [TestMethod]
    public void SurfaceIdOrSourceTextDoesNotCreateFuzzyMatch()
    {
        var result = Resolve(
            [Requested("request-ref", EvidenceReferenceKind.ValidationEvidence)],
            [
                Available(
                    "evidence-other",
                    EvidenceReferenceKind.ValidationEvidence,
                    surfaceId: "request-ref",
                    source: "request-ref")
            ],
            []);

        Assert.AreEqual(EvidenceResolutionStatus.NotFound, result.ResolutionStatus);
    }

    [TestMethod]
    public void RedactedEvidenceMetadataRemainsVisibleAsResolvedMetadata()
    {
        var result = Resolve(
            [Requested("evidence-redacted", EvidenceReferenceKind.AuditEvidence)],
            [
                Available(
                    "evidence-redacted",
                    EvidenceReferenceKind.AuditEvidence,
                    isRedacted: true,
                    redactionReason: "metadata-redacted")
            ],
            []);

        Assert.AreEqual(EvidenceResolutionStatus.Resolved, result.ResolutionStatus);
        Assert.IsTrue(result.ResolvedEvidence[0].IsRedacted);
        Assert.AreEqual("metadata-redacted", result.ResolvedEvidence[0].RedactionReason);
        AssertContains(result.Warnings, "redacted evidence preview is not raw payload");
    }

    [TestMethod]
    public void RedactedEvidenceMetadataRequiresRedactionReason()
    {
        var result = Resolve(
            [Requested("evidence-redacted", EvidenceReferenceKind.AuditEvidence)],
            [Available("evidence-redacted", EvidenceReferenceKind.AuditEvidence, isRedacted: true, redactionReason: null)],
            []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "AvailableEvidenceRedactionReasonRequired");
    }

    [DataTestMethod]
    [DataRow(null, ProjectId, OperationId, "EvidenceResolverTenantIdRequired")]
    [DataRow("", ProjectId, OperationId, "EvidenceResolverTenantIdRequired")]
    [DataRow("tenant d10", ProjectId, OperationId, "EvidenceResolverTenantIdInvalid")]
    [DataRow(TenantId, null, OperationId, "EvidenceResolverProjectIdRequired")]
    [DataRow(TenantId, "", OperationId, "EvidenceResolverProjectIdRequired")]
    [DataRow(TenantId, "project d10", OperationId, "EvidenceResolverProjectIdInvalid")]
    [DataRow(TenantId, ProjectId, null, "OperationIdRequired")]
    [DataRow(TenantId, ProjectId, "run-123", "OperationIdMustBeBackendMintedCanonicalId")]
    public void RequestScopeValidation_FailsClosed(
        string? tenantId,
        string? projectId,
        string? operationId,
        string expectedIssue)
    {
        var result = EvidenceResolver.Resolve(new EvidenceResolverRequest
        {
            TenantId = tenantId!,
            ProjectId = projectId!,
            OperationId = operationId!,
            RequestedReferences = [],
            AvailableEvidence = [],
            SuppliedPayloadsForRedaction = []
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void NullRequestedReferencesList_FailsClosed()
    {
        var result = EvidenceResolver.Resolve(new EvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = null!,
            AvailableEvidence = [],
            SuppliedPayloadsForRedaction = []
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "EvidenceRequestedReferencesRequired");
    }

    [TestMethod]
    public void NullAvailableEvidenceList_FailsClosed()
    {
        var result = EvidenceResolver.Resolve(new EvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = [],
            AvailableEvidence = null!,
            SuppliedPayloadsForRedaction = []
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "AvailableEvidenceRequired");
    }

    [TestMethod]
    public void NullSuppliedPayloadsList_FailsClosed()
    {
        var result = EvidenceResolver.Resolve(new EvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = [],
            AvailableEvidence = [],
            SuppliedPayloadsForRedaction = null!
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "SuppliedEvidencePayloadsRequired");
    }

    [DataTestMethod]
    [DataRow(null, "EvidenceReferenceIdRequired")]
    [DataRow("", "EvidenceReferenceIdRequired")]
    [DataRow("evidence id", "EvidenceReferenceIdInvalid")]
    [DataRow("https://example.test/evidence", "EvidenceReferenceIdInvalid")]
    public void RequestedReference_FailsClosedForMissingOrUnsafeReferenceId(
        string? evidenceReferenceId,
        string expectedIssue)
    {
        var result = Resolve([Requested(evidenceReferenceId!, EvidenceReferenceKind.ValidationEvidence)], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void RequestedReference_FailsClosedForUnknownEvidenceKind()
    {
        var result = Resolve([Requested("evidence-a", EvidenceReferenceKind.Unknown)], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "EvidenceReferenceKindRequired");
    }

    [DataTestMethod]
    [DataRow(null, "EvidenceReferenceCorrelationIdRequired")]
    [DataRow("", "EvidenceReferenceCorrelationIdRequired")]
    [DataRow("corr invalid", "EvidenceReferenceCorrelationIdInvalid")]
    [DataRow("run-123", "EvidenceReferenceCorrelationIdInvalid")]
    public void RequestedReference_FailsClosedForInvalidCorrelationId(
        string? correlationId,
        string expectedIssue)
    {
        var result = Resolve([Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, correlationId: correlationId!)], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "EvidenceReferenceSourceRequired")]
    [DataRow("", "EvidenceReferenceSourceRequired")]
    [DataRow("source with space", "EvidenceReferenceSourceInvalid")]
    [DataRow("policy satisfied", "EvidenceReferenceSourceInvalid")]
    public void RequestedReference_FailsClosedForMissingOrUnsafeSource(
        string? source,
        string expectedIssue)
    {
        var result = Resolve([Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, source: source!)], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void RequestedReference_FailsClosedForMissingTimestamp()
    {
        var result = Resolve([Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, requestedAtUtc: default(DateTimeOffset))], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "EvidenceReferenceRequestedAtRequired");
    }

    [DataTestMethod]
    [DataRow(OperationReferenceKind.EvidenceId, null, "EvidenceReferenceReferenceIdRequired")]
    [DataRow(OperationReferenceKind.Unknown, "evidence-ref", "EvidenceReferenceReferenceKindRequired")]
    [DataRow(OperationReferenceKind.EvidenceId, "evidence ref", "EvidenceReferenceReferenceIdInvalid")]
    [DataRow(OperationReferenceKind.EvidenceId, "https://example.test/ref", "EvidenceReferenceReferenceIdInvalid")]
    public void RequestedReference_FailsClosedForUnsafeExternalReferencePair(
        OperationReferenceKind referenceKind,
        string? referenceId,
        string expectedIssue)
    {
        var result = Resolve([
            Requested(
                "request-ref",
                EvidenceReferenceKind.GenericEvidence,
                referenceKind: referenceKind,
                referenceId: referenceId)
        ], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "AvailableEvidenceIdRequired")]
    [DataRow("", "AvailableEvidenceIdRequired")]
    [DataRow("evidence id", "AvailableEvidenceIdInvalid")]
    [DataRow("https://example.test/evidence", "AvailableEvidenceIdInvalid")]
    public void AvailableEvidence_FailsClosedForMissingOrUnsafeEvidenceId(
        string? evidenceId,
        string expectedIssue)
    {
        var result = Resolve([], [Available(evidenceId!, EvidenceReferenceKind.ValidationEvidence)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void AvailableEvidence_FailsClosedForUnknownEvidenceKind()
    {
        var result = Resolve([], [Available("evidence-a", EvidenceReferenceKind.Unknown)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "AvailableEvidenceKindRequired");
    }

    [DataTestMethod]
    [DataRow(null, "AvailableEvidenceCorrelationIdRequired")]
    [DataRow("", "AvailableEvidenceCorrelationIdRequired")]
    [DataRow("corr invalid", "AvailableEvidenceCorrelationIdInvalid")]
    public void AvailableEvidence_FailsClosedForInvalidCorrelationId(
        string? correlationId,
        string expectedIssue)
    {
        var result = Resolve([], [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence, correlationId: correlationId!)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(OperationCorrelationSurfaceKind.Unknown, "surface-1", "AvailableEvidenceSurfaceKindRequired")]
    [DataRow(OperationCorrelationSurfaceKind.EvidenceMetadata, null, "AvailableEvidenceSurfaceIdRequired")]
    [DataRow(OperationCorrelationSurfaceKind.EvidenceMetadata, "surface 1", "AvailableEvidenceSurfaceIdInvalid")]
    public void AvailableEvidence_FailsClosedForUnsafeSurfaceMetadata(
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        string expectedIssue)
    {
        var result = Resolve([], [
            Available(
                "evidence-a",
                EvidenceReferenceKind.ValidationEvidence,
                surfaceKind: surfaceKind,
                surfaceId: surfaceId!)
        ], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "AvailableEvidenceSourceRequired")]
    [DataRow("", "AvailableEvidenceSourceRequired")]
    [DataRow("source with space", "AvailableEvidenceSourceInvalid")]
    [DataRow("raw evidence payload", "AvailableEvidenceSourceInvalid")]
    public void AvailableEvidence_FailsClosedForMissingOrUnsafeSource(
        string? source,
        string expectedIssue)
    {
        var result = Resolve([], [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence, source: source!)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void AvailableEvidence_FailsClosedForMissingCreatedTimestamp()
    {
        var result = Resolve([], [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence, createdAtUtc: default(DateTimeOffset))], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "AvailableEvidenceCreatedAtRequired");
    }

    [TestMethod]
    public void AvailableEvidence_FailsClosedForUnknownPayloadState()
    {
        var result = Resolve([], [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence, payloadState: EvidencePayloadState.Unknown)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "AvailableEvidencePayloadStateRequired");
    }

    [DataTestMethod]
    [DataRow(OperationReferenceKind.EvidenceId, null, "AvailableEvidenceReferenceIdRequired")]
    [DataRow(OperationReferenceKind.Unknown, "evidence-ref", "AvailableEvidenceReferenceKindRequired")]
    [DataRow(OperationReferenceKind.EvidenceId, "evidence ref", "AvailableEvidenceReferenceIdInvalid")]
    public void AvailableEvidence_FailsClosedForUnsafeExternalReferencePair(
        OperationReferenceKind referenceKind,
        string? referenceId,
        string expectedIssue)
    {
        var result = Resolve([], [
            Available(
                "evidence-a",
                EvidenceReferenceKind.GenericEvidence,
                referenceKind: referenceKind,
                referenceId: referenceId)
        ], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "EvidenceReferenceTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "EvidenceReferenceProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000009", "EvidenceReferenceOperationMismatch")]
    public void CrossScopeRequestedReference_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string expectedIssue)
    {
        var result = Resolve([
            Requested(
                "evidence-a",
                EvidenceReferenceKind.ValidationEvidence,
                tenantId: tenantId,
                projectId: projectId,
                operationId: operationId)
        ], [], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "AvailableEvidenceTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "AvailableEvidenceProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000009", "AvailableEvidenceOperationMismatch")]
    public void CrossScopeAvailableEvidence_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string expectedIssue)
    {
        var result = Resolve([], [
            Available(
                "evidence-a",
                EvidenceReferenceKind.ValidationEvidence,
                tenantId: tenantId,
                projectId: projectId,
                operationId: operationId)
        ], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "SuppliedEvidencePayloadTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "SuppliedEvidencePayloadProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000009", "SuppliedEvidencePayloadOperationMismatch")]
    public void CrossScopeSuppliedPayload_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string expectedIssue)
    {
        var result = Resolve([], [], [
            Payload(
                "evidence-a",
                "payload",
                tenantId: tenantId,
                projectId: projectId,
                operationId: operationId)
        ]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void SuppliedPayloadIsIgnoredWhenNoMatchingResolvedEvidenceExists()
    {
        var result = Resolve(
            [Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, requestRedactedPreview: true)],
            [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)],
            [Payload("evidence-other", "secret=abc")]);

        Assert.AreEqual(EvidenceResolutionStatus.Resolved, result.ResolutionStatus);
        Assert.IsNull(result.ResolvedEvidence[0].RedactedPreview);
    }

    [TestMethod]
    public void SafeSuppliedPayloadCanProduceRedactedPreviewWithoutReturningRaw()
    {
        var result = Resolve(
            [Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, requestRedactedPreview: true)],
            [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)],
            [Payload("evidence-a", "build passed with normal output")]);

        var preview = result.ResolvedEvidence[0].RedactedPreview;
        Assert.IsNotNull(preview);
        Assert.AreEqual("build passed with normal output", preview.PreviewText);
        Assert.IsFalse(preview.WasSuppressed);
        Assert.IsFalse(preview.WasRedacted);
        Assert.IsFalse(preview.PreviewText.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RedactedPreviewIsBoundedAndTruncationMarked()
    {
        var result = Resolve(
            [Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, requestRedactedPreview: true)],
            [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)],
            [Payload("evidence-a", new string('a', EvidencePayloadRedactor.MaxPreviewLength + 40))]);

        var preview = result.ResolvedEvidence[0].RedactedPreview!;
        Assert.AreEqual(EvidencePayloadRedactor.MaxPreviewLength, preview.PreviewText.Length);
        Assert.IsTrue(preview.PreviewTruncated);
        AssertContains(preview.RedactionReasons, EvidenceRedactionReasonKind.PayloadTooLarge);
    }

    [DataTestMethod]
    [DataRow("Authorization: Basic abc123", "[REDACTED_AUTHORIZATION]", EvidenceRedactionReasonKind.AuthorizationHeaderDetected)]
    [DataRow("Bearer abc.def.ghi", "[REDACTED_SECRET]", EvidenceRedactionReasonKind.TokenDetected)]
    [DataRow("api_key=abc123", "[REDACTED_SECRET]", EvidenceRedactionReasonKind.SecretDetected)]
    [DataRow("password=abc123", "[REDACTED_SECRET]", EvidenceRedactionReasonKind.SecretDetected)]
    [DataRow("token=abc123", "[REDACTED_SECRET]", EvidenceRedactionReasonKind.TokenDetected)]
    [DataRow("Server=db;User Id=sa;Password=pw", "[REDACTED_SECRET]", EvidenceRedactionReasonKind.ConnectionStringDetected)]
    [DataRow("hidden chain-of-thought: secret path", "[REDACTED_PRIVATE_REASONING]", EvidenceRedactionReasonKind.PrivateReasoningDetected)]
    [DataRow("private reasoning: secret path", "[REDACTED_PRIVATE_REASONING]", EvidenceRedactionReasonKind.PrivateReasoningDetected)]
    [DataRow("scratchpad: hidden plan", "[REDACTED_PRIVATE_REASONING]", EvidenceRedactionReasonKind.PrivateReasoningDetected)]
    [DataRow("prompt text: system says x", "[REDACTED_PROMPT_OR_MODEL_TEXT]", EvidenceRedactionReasonKind.PromptOrModelTextDetected)]
    [DataRow("model response text: answer", "[REDACTED_PROMPT_OR_MODEL_TEXT]", EvidenceRedactionReasonKind.PromptOrModelTextDetected)]
    [DataRow("raw evidence payload: abc", "[REDACTED_RAW_PAYLOAD]", EvidenceRedactionReasonKind.RawPayloadMarkerDetected)]
    [DataRow("raw receipt payload: abc", "[REDACTED_RAW_PAYLOAD]", EvidenceRedactionReasonKind.RawPayloadMarkerDetected)]
    [DataRow("raw validation log: abc", "[REDACTED_RAW_PAYLOAD]", EvidenceRedactionReasonKind.ValidationLogContentDetected)]
    [DataRow("raw request body: abc", "[REDACTED_RAW_PAYLOAD]", EvidenceRedactionReasonKind.RequestResponseBodyDetected)]
    [DataRow("raw response body: abc", "[REDACTED_RAW_PAYLOAD]", EvidenceRedactionReasonKind.RequestResponseBodyDetected)]
    [DataRow("diff --git a/file b/file", "[REDACTED_PATCH_CONTENT]", EvidenceRedactionReasonKind.PatchOrDiffContentDetected)]
    [DataRow("@@ -1 +1 @@", "[REDACTED_PATCH_CONTENT]", EvidenceRedactionReasonKind.PatchOrDiffContentDetected)]
    public void UnsafePayloadMarkersAreRedacted(
        string payloadText,
        string expectedMarker,
        EvidenceRedactionReasonKind expectedReason)
    {
        var result = Resolve(
            [Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, requestRedactedPreview: true)],
            [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)],
            [Payload("evidence-a", payloadText)]);

        var preview = result.ResolvedEvidence[0].RedactedPreview!;
        Assert.IsTrue(preview.WasRedacted);
        Assert.IsFalse(preview.WasSuppressed);
        Assert.IsTrue(preview.PreviewText.Contains(expectedMarker, StringComparison.Ordinal));
        AssertContains(preview.RedactionReasons, expectedReason);
        Assert.IsFalse(preview.PreviewText.Contains("abc123", StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow("-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----", EvidenceRedactionReasonKind.PrivateKeyDetected)]
    [DataRow("unsafe\u0001control", EvidenceRedactionReasonKind.UnsafeControlCharacters)]
    public void UnsafePayloadsAreSuppressedInsteadOfLeaked(
        string payloadText,
        EvidenceRedactionReasonKind expectedReason)
    {
        var result = Resolve(
            [Requested("evidence-a", EvidenceReferenceKind.ValidationEvidence, requestRedactedPreview: true)],
            [Available("evidence-a", EvidenceReferenceKind.ValidationEvidence)],
            [Payload("evidence-a", payloadText)]);

        var preview = result.ResolvedEvidence[0].RedactedPreview!;
        Assert.IsTrue(preview.WasSuppressed);
        Assert.AreEqual("[SUPPRESSED_UNSAFE_PAYLOAD]", preview.PreviewText);
        AssertContains(preview.RedactionReasons, expectedReason);
        Assert.IsFalse(preview.PreviewText.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow(EvidenceReferenceKind.ValidationEvidence, "validation freshness")]
    [DataRow(EvidenceReferenceKind.ApprovalEvidenceReference, "accepted approval")]
    [DataRow(EvidenceReferenceKind.PolicyEvidenceReference, "policy satisfaction")]
    [DataRow(EvidenceReferenceKind.SourceApplyEvidence, "source apply authority")]
    [DataRow(EvidenceReferenceKind.RollbackEvidence, "rollback authority")]
    [DataRow(EvidenceReferenceKind.CommitEvidence, "push")]
    [DataRow(EvidenceReferenceKind.PushEvidence, "PR creation")]
    [DataRow(EvidenceReferenceKind.PullRequestEvidence, "merge readiness")]
    [DataRow(EvidenceReferenceKind.ReleaseReadinessEvidence, "release readiness")]
    [DataRow(EvidenceReferenceKind.DeploymentReadinessEvidence, "deployment readiness")]
    [DataRow(EvidenceReferenceKind.MemoryPromotionEvidence, "memory promotion")]
    [DataRow(EvidenceReferenceKind.WorkflowContinuationEvidence, "workflow continuation")]
    public void EvidenceFoundDoesNotImplyDownstreamAuthority(
        EvidenceReferenceKind kind,
        string authorityMarker)
    {
        var result = Resolve([Requested("evidence-a", kind)], [Available("evidence-a", kind)], []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(EvidenceResolutionStatus.Resolved, result.ResolutionStatus);
        AssertContains(result.ForbiddenAuthorityImplications, "evidence found is not authority");
        Assert.IsFalse(result.Warnings.Any(item => item.Equals(authorityMarker, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void MissingEvidenceDoesNotChooseNextSafeAction()
    {
        var result = Resolve([Requested("evidence-missing", EvidenceReferenceKind.AuditEvidence)], [], []);

        Assert.AreEqual(EvidenceResolutionStatus.NotFound, result.ResolutionStatus);
        AssertContains(result.Warnings, "missing evidence does not choose next safe action");
    }

    [TestMethod]
    public void CompleteResolutionDoesNotImplyActionAllowed()
    {
        var result = Resolve(
            [Requested("evidence-a", EvidenceReferenceKind.GenericEvidence)],
            [Available("evidence-a", EvidenceReferenceKind.GenericEvidence)],
            []);

        Assert.AreEqual(EvidenceResolutionStatus.Resolved, result.ResolutionStatus);
        AssertContains(result.ForbiddenAuthorityImplications, "complete evidence resolution is not action allowed");
    }

    [TestMethod]
    public void ResultModelsExposeNoAuthorityApprovalPolicyFreshnessNextActionProofOrRawPayloadProperties()
    {
        var names = typeof(EvidenceResolverResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Concat(typeof(ResolvedEvidenceReference).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(static property => property.Name))
            .Concat(typeof(UnresolvedEvidenceReference).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(static property => property.Name))
            .Concat(typeof(RedactedEvidencePreview).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(static property => property.Name))
            .ToArray();

        foreach (var forbiddenName in new[]
        {
            "Allowed",
            "ActionAllowed",
            "AuthorityGranted",
            "ApprovalStatus",
            "PolicySatisfied",
            "ValidationFresh",
            "NextSafeAction",
            "EvidenceVerified",
            "ExecutionProven",
            "Authorized",
            "Executable",
            "RawPayload",
            "PayloadText"
        })
        {
            Assert.IsFalse(names.Contains(forbiddenName, StringComparer.Ordinal));
        }

        Assert.IsFalse(names.Any(static property => property.StartsWith("Can", StringComparison.Ordinal)));
        Assert.IsFalse(Enum.GetNames<EvidenceResolutionStatus>().Any(static name =>
            name is "Approved" or "Verified" or "Authorized" or "Executable" or "Fresh" or "Allowed" or "PolicySatisfied"));
    }

    [TestMethod]
    public void D01ThroughD09CompatibilityTypesRemainAvailable()
    {
        Assert.IsTrue(OperationIdentityValidator.ValidateOperationId(OperationId).IsValid);
        Assert.AreEqual(OperationIdentityLookupStatus.NotFound, OperationIdentityLookupStatus.NotFound);

        var correlation = OperationCorrelationValidator.ValidateLink(new OperationCorrelationLink
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            SurfaceKind = OperationCorrelationSurfaceKind.EvidenceMetadata,
            SurfaceId = "surface-d10",
            ObservedAtUtc = ObservedAtUtc,
            Source = "d10-test"
        });
        Assert.IsTrue(correlation.IsValid, string.Join(", ", correlation.Issues));

        Assert.AreEqual(GovernedOperationTimelineEventKind.CompletedObserved, GovernedOperationTimelineEventKind.CompletedObserved);
        Assert.AreEqual(OperationProjectedStatusKind.CompletedObserved, OperationProjectedStatusKind.CompletedObserved);

        var missingEvidence = MissingEvidenceResolver.Resolve(new MissingEvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            Requirements = [],
            ObservedEvidence = []
        });
        Assert.AreEqual(MissingEvidenceResolutionStatus.NoRequirements, missingEvidence.ResolutionStatus);

        var forbiddenAction = ForbiddenActionResolver.Resolve(new ForbiddenActionResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            ActionKind = ForbiddenActionKind.SourceApply,
            Facts = []
        });
        Assert.AreEqual(ForbiddenActionResolutionStatus.NoForbiddenFactsObserved, forbiddenAction.ResolutionStatus);

        var receipts = ReceiptReferenceResolver.Resolve(new ReceiptReferenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = [],
            AvailableReceipts = []
        });
        Assert.AreEqual(ReceiptReferenceResolutionStatus.NoReferences, receipts.ResolutionStatus);
    }

    [TestMethod]
    public void A02AndA05ReadAdaptersRemainReadOnly()
    {
        AssertDoesNotContain(A02StatusReadSource(), "CanExecute = true");
        AssertDoesNotContain(A02StatusReadSource(), "SaveChanges");
        AssertDoesNotContain(A05TimelineSource(), "CanExecute = true");
        AssertDoesNotContain(A05TimelineSource(), "SaveChanges");
    }

    [TestMethod]
    public void StaticScan_D10CoreFilesAddNoApiSqlUiStoreExecutorMutationRawStoreReaderOrResolverCalls()
    {
        var source = D10CoreSource();

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
            "EvidenceStore",
            "ReceiptStore",
            "Repository",
            "SaveChanges",
            ".Save",
            ".Update",
            ".Delete",
            ".Remove",
            "File.ReadAllText",
            "ReadRawPayload",
            "RawPayloadReader",
            "OperationIdentityLookupResolver.Resolve",
            "GovernedOperationTimelineAssembler.Assemble",
            "OperationStatusProjector.Project(",
            "MissingEvidenceResolver.Resolve",
            "ForbiddenActionResolver.Resolve",
            "ReceiptReferenceResolver.Resolve",
            "FreshnessResolver",
            "BlockedStateFormatter",
            "NextSafeActionFormatter",
            "AuthorityWarningFormatter",
            "EvidenceVerifier",
            "VerifyEvidence",
            "ReceiptVerifier",
            "VerifyReceipt",
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
            "AcceptApproval"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void Receipt_RecordsEvidenceFoundIsNotAuthorityAndRawPayloadNeverReturnedBoundaries()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D10_EVIDENCE_RESOLVER_RAW_PAYLOAD_REDACTION.md"));

        Assert.IsTrue(receipt.Contains(
            "The evidence resolver resolves scoped evidence metadata and optional redacted previews from supplied payload text only. It does not fetch raw evidence payloads, return raw payloads, verify evidence authenticity, accept approval, satisfy policy, validate freshness, grant authority, choose next safe action, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.",
            StringComparison.Ordinal));
        Assert.IsTrue(receipt.Contains("Evidence found is not authority.", StringComparison.Ordinal));
        Assert.IsTrue(receipt.Contains("Raw payload is never returned.", StringComparison.Ordinal));
    }

    private static EvidenceResolverResult Resolve(
        IReadOnlyList<EvidenceReferenceRequestItem> requested,
        IReadOnlyList<AvailableEvidenceMetadata> available,
        IReadOnlyList<SuppliedEvidencePayloadForRedaction> payloads) =>
        EvidenceResolver.Resolve(new EvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = requested,
            AvailableEvidence = available,
            SuppliedPayloadsForRedaction = payloads
        });

    private static EvidenceReferenceRequestItem Requested(
        string evidenceReferenceId,
        EvidenceReferenceKind kind,
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string correlationId = CorrelationId,
        OperationReferenceKind referenceKind = OperationReferenceKind.Unknown,
        string? referenceId = null,
        string source = "d10-source",
        DateTimeOffset? requestedAtUtc = null,
        bool requestRedactedPreview = false) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            CorrelationId = correlationId,
            EvidenceReferenceId = evidenceReferenceId,
            EvidenceKind = kind,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
            Source = source,
            RequestedAtUtc = requestedAtUtc ?? ObservedAtUtc,
            RequestRedactedPreview = requestRedactedPreview
        };

    private static AvailableEvidenceMetadata Available(
        string evidenceId,
        EvidenceReferenceKind kind,
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string correlationId = CorrelationId,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.EvidenceMetadata,
        string surfaceId = "surface-1",
        OperationReferenceKind referenceKind = OperationReferenceKind.Unknown,
        string? referenceId = null,
        DateTimeOffset? createdAtUtc = null,
        string source = "d10-source",
        EvidencePayloadState payloadState = EvidencePayloadState.MetadataOnly,
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            CorrelationId = correlationId,
            EvidenceId = evidenceId,
            EvidenceKind = kind,
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
            CreatedAtUtc = createdAtUtc ?? ObservedAtUtc,
            Source = source,
            PayloadState = payloadState,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static SuppliedEvidencePayloadForRedaction Payload(
        string evidenceId,
        string payloadText,
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string payloadContentType = "text/plain",
        string source = "d10-payload",
        DateTimeOffset? suppliedAtUtc = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            EvidenceId = evidenceId,
            PayloadText = payloadText,
            PayloadContentType = payloadContentType,
            Source = source,
            SuppliedAtUtc = suppliedAtUtc ?? ObservedAtUtc
        };

    private static void AssertAmbiguous(EvidenceResolverResult result, string expectedIssue)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(EvidenceResolutionStatus.AmbiguousEvidence, result.ResolutionStatus);
        AssertContains(result.AmbiguousEvidence, expectedIssue);
        Assert.AreEqual(0, result.ResolvedEvidence.Count);
        Assert.AreEqual(0, result.UnresolvedEvidence.Count);
    }

    private static string D10CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "EvidenceResolverModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "EvidenceResolverValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "EvidencePayloadRedactor.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "EvidenceResolver.cs")));
    }

    private static string A02StatusReadSource()
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

    private static void AssertContains<T>(IEnumerable<T> values, T expected) =>
        Assert.IsTrue(
            values.Contains(expected),
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
