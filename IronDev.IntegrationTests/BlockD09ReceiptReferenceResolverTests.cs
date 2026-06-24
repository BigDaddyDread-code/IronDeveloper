using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD09ReceiptReferenceResolverTests
{
    private const string TenantId = "tenant-d09";
    private const string ProjectId = "project-d09";
    private const string OperationId = "op_0000000000000009";
    private const string CorrelationId = "corr_1123456789abcdef";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T09:00:00Z");

    [TestMethod]
    public void ValidRequestWithNoReferences_ReturnsNoReferences()
    {
        var result = Resolve([], []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ReceiptReferenceResolutionStatus.NoReferences, result.ResolutionStatus);
        Assert.AreEqual(0, result.ResolvedReceipts.Count);
        Assert.AreEqual(0, result.UnresolvedReceipts.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "receipt found is not authority");
    }

    [TestMethod]
    public void AllRequestedReferencesResolved_ReturnsResolved()
    {
        var result = Resolve(
            [
                Requested("receipt-b", ReceiptReferenceKind.CommitReceipt),
                Requested("receipt-a", ReceiptReferenceKind.SourceApplyReceipt)
            ],
            [
                Available("receipt-b", ReceiptReferenceKind.CommitReceipt),
                Available("receipt-a", ReceiptReferenceKind.SourceApplyReceipt)
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ReceiptReferenceResolutionStatus.Resolved, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            new[] { "receipt-a", "receipt-b" },
            result.ResolvedReceipts.Select(static item => item.ReceiptId).ToArray());
        AssertContains(result.Warnings, "complete receipt resolution is not action allowed");
    }

    [TestMethod]
    public void SomeResolvedAndSomeMissing_ReturnsPartiallyResolved()
    {
        var result = Resolve(
            [
                Requested("receipt-a", ReceiptReferenceKind.SourceApplyReceipt),
                Requested("receipt-missing", ReceiptReferenceKind.CommitReceipt)
            ],
            [Available("receipt-a", ReceiptReferenceKind.SourceApplyReceipt)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ReceiptReferenceResolutionStatus.PartiallyResolved, result.ResolutionStatus);
        Assert.AreEqual(1, result.ResolvedReceipts.Count);
        Assert.AreEqual(1, result.UnresolvedReceipts.Count);
        Assert.AreEqual("MatchingReceiptMetadataNotFound", result.UnresolvedReceipts[0].Reason);
    }

    [TestMethod]
    public void NoneResolved_ReturnsNotFound()
    {
        var result = Resolve(
            [
                Requested("receipt-a", ReceiptReferenceKind.SourceApplyReceipt),
                Requested("receipt-b", ReceiptReferenceKind.CommitReceipt)
            ],
            [Available("receipt-c", ReceiptReferenceKind.RollbackReceipt)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ReceiptReferenceResolutionStatus.NotFound, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            new[] { "receipt-a", "receipt-b" },
            result.UnresolvedReceipts.Select(static item => item.ReceiptReferenceId).ToArray());
        AssertContains(result.Warnings, "missing receipt does not choose next safe action");
    }

    [TestMethod]
    public void DuplicateRequestedReferenceIds_ReturnAmbiguousReferences()
    {
        var result = Resolve(
            [
                Requested("receipt-duplicate", ReceiptReferenceKind.SourceApplyReceipt),
                Requested("receipt-duplicate", ReceiptReferenceKind.SourceApplyReceipt)
            ],
            [Available("receipt-duplicate", ReceiptReferenceKind.SourceApplyReceipt)]);

        AssertAmbiguous(result, "DuplicateRequestedReceiptReferenceId:receipt-duplicate");
    }

    [TestMethod]
    public void DuplicateAvailableReceiptIds_ReturnAmbiguousReferences()
    {
        var result = Resolve(
            [Requested("receipt-duplicate", ReceiptReferenceKind.SourceApplyReceipt)],
            [
                Available("receipt-duplicate", ReceiptReferenceKind.SourceApplyReceipt),
                Available("receipt-duplicate", ReceiptReferenceKind.SourceApplyReceipt)
            ]);

        AssertAmbiguous(result, "DuplicateAvailableReceiptId:receipt-duplicate");
    }

    [TestMethod]
    public void ConflictingMetadataForSameReceiptId_ReturnsAmbiguousReferences()
    {
        var result = Resolve(
            [Requested("receipt-conflict", ReceiptReferenceKind.SourceApplyReceipt)],
            [
                Available("receipt-conflict", ReceiptReferenceKind.SourceApplyReceipt, source: "source-a"),
                Available("receipt-conflict", ReceiptReferenceKind.SourceApplyReceipt, source: "source-b")
            ]);

        AssertAmbiguous(result, "ConflictingAvailableReceiptMetadata:receipt-conflict");
    }

    [TestMethod]
    public void MultipleMatchingReceiptsForOneReference_ReturnsAmbiguousReferences()
    {
        var requested = Requested(
            "request-ref",
            ReceiptReferenceKind.CommitReceipt,
            referenceKind: OperationReferenceKind.CommitSha,
            referenceId: "commit-ref-1");
        var result = Resolve(
            [requested],
            [
                Available(
                    "receipt-a",
                    ReceiptReferenceKind.CommitReceipt,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1"),
                Available(
                    "receipt-b",
                    ReceiptReferenceKind.CommitReceipt,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1")
            ]);

        AssertAmbiguous(result, "MultipleAvailableReceiptsMatchReference:request-ref");
    }

    [TestMethod]
    public void AmbiguityDoesNotChooseWinner()
    {
        var result = Resolve(
            [
                Requested(
                    "request-a",
                    ReceiptReferenceKind.CommitReceipt,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1"),
                Requested(
                    "request-b",
                    ReceiptReferenceKind.CommitReceipt,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1")
            ],
            [
                Available(
                    "receipt-a",
                    ReceiptReferenceKind.CommitReceipt,
                    referenceKind: OperationReferenceKind.CommitSha,
                    referenceId: "commit-ref-1")
            ]);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.AmbiguousReferences, result.ResolutionStatus);
        Assert.AreEqual(0, result.ResolvedReceipts.Count);
        AssertContains(result.AmbiguousReceipts, $"IndistinguishableRequestedReceiptReferences:{ReceiptReferenceKind.CommitReceipt}:{OperationReferenceKind.CommitSha}:commit-ref-1:{CorrelationId}");
        AssertContains(result.AmbiguousReceipts, "AmbiguousReceiptAssignment:receipt-a");
        AssertContains(result.Warnings, "ambiguous receipt references do not choose a winner");
    }

    [TestMethod]
    public void ResolvedAndUnresolvedReceiptsSortDeterministically()
    {
        var result = Resolve(
            [
                Requested("receipt-c", ReceiptReferenceKind.RollbackReceipt),
                Requested("receipt-b", ReceiptReferenceKind.CommitReceipt),
                Requested("receipt-a", ReceiptReferenceKind.SourceApplyReceipt)
            ],
            [
                Available("receipt-b", ReceiptReferenceKind.CommitReceipt),
                Available("receipt-a", ReceiptReferenceKind.SourceApplyReceipt)
            ]);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.PartiallyResolved, result.ResolutionStatus);
        CollectionAssert.AreEqual(
            new[] { "receipt-a", "receipt-b" },
            result.ResolvedReceipts.Select(static item => item.ReceiptId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "receipt-c" },
            result.UnresolvedReceipts.Select(static item => item.ReceiptReferenceId).ToArray());
    }

    [TestMethod]
    public void DirectReceiptIdReferenceResolvesByReceiptId()
    {
        var result = Resolve(
            [Requested("receipt-direct", ReceiptReferenceKind.ValidationReceipt)],
            [Available("receipt-direct", ReceiptReferenceKind.ValidationReceipt)]);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.Resolved, result.ResolutionStatus);
        Assert.AreEqual("receipt-direct", result.ResolvedReceipts[0].ReceiptId);
    }

    [DataTestMethod]
    [DataRow(ReceiptReferenceKind.CommitReceipt, ReceiptReferenceKind.PushReceipt)]
    [DataRow(ReceiptReferenceKind.ReleaseReadinessReceipt, ReceiptReferenceKind.DeploymentReadinessReceipt)]
    public void ReceiptKindMustMatchExactly(
        ReceiptReferenceKind requestedKind,
        ReceiptReferenceKind availableKind)
    {
        var result = Resolve(
            [Requested("receipt-a", requestedKind)],
            [Available("receipt-a", availableKind)]);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.NotFound, result.ResolutionStatus);
        Assert.AreEqual(0, result.ResolvedReceipts.Count);
    }

    [TestMethod]
    public void ReferenceKindAndIdMustMatchExactlyWhenSupplied()
    {
        var result = Resolve(
            [
                Requested(
                    "request-ref",
                    ReceiptReferenceKind.PushReceipt,
                    referenceKind: OperationReferenceKind.PushId,
                    referenceId: "push-1")
            ],
            [
                Available(
                    "receipt-a",
                    ReceiptReferenceKind.PushReceipt,
                    referenceKind: OperationReferenceKind.PushId,
                    referenceId: "push-2")
            ]);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.NotFound, result.ResolutionStatus);
    }

    [TestMethod]
    public void CorrelationIdMustMatch()
    {
        var result = Resolve(
            [Requested("receipt-a", ReceiptReferenceKind.SourceApplyReceipt)],
            [Available("receipt-a", ReceiptReferenceKind.SourceApplyReceipt, correlationId: "corr_2223456789abcdef")]);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.NotFound, result.ResolutionStatus);
    }

    [TestMethod]
    public void SurfaceIdOrSourceTextDoesNotCreateFuzzyMatch()
    {
        var result = Resolve(
            [Requested("request-ref", ReceiptReferenceKind.SourceApplyReceipt)],
            [
                Available(
                    "receipt-other",
                    ReceiptReferenceKind.SourceApplyReceipt,
                    surfaceId: "request-ref",
                    source: "request-ref")
            ]);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.NotFound, result.ResolutionStatus);
    }

    [TestMethod]
    public void RedactedReceiptMetadataRemainsVisibleAsResolvedMetadata()
    {
        var result = Resolve(
            [Requested("receipt-redacted", ReceiptReferenceKind.AuditReceipt)],
            [
                Available(
                    "receipt-redacted",
                    ReceiptReferenceKind.AuditReceipt,
                    isRedacted: true,
                    redactionReason: "metadata-redacted")
            ]);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.Resolved, result.ResolutionStatus);
        Assert.IsTrue(result.ResolvedReceipts[0].IsRedacted);
        Assert.AreEqual("metadata-redacted", result.ResolvedReceipts[0].RedactionReason);
        AssertContains(result.Warnings, "redacted receipt metadata is not raw payload");
    }

    [TestMethod]
    public void RedactedReceiptRequiresRedactionReason()
    {
        var result = Resolve(
            [Requested("receipt-redacted", ReceiptReferenceKind.AuditReceipt)],
            [Available("receipt-redacted", ReceiptReferenceKind.AuditReceipt, isRedacted: true, redactionReason: null)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "AvailableReceiptRedactionReasonRequired");
    }

    [DataTestMethod]
    [DataRow(null, ProjectId, OperationId, "ReceiptReferenceTenantIdRequired")]
    [DataRow("", ProjectId, OperationId, "ReceiptReferenceTenantIdRequired")]
    [DataRow("tenant d09", ProjectId, OperationId, "ReceiptReferenceTenantIdInvalid")]
    [DataRow(TenantId, null, OperationId, "ReceiptReferenceProjectIdRequired")]
    [DataRow(TenantId, "", OperationId, "ReceiptReferenceProjectIdRequired")]
    [DataRow(TenantId, "project d09", OperationId, "ReceiptReferenceProjectIdInvalid")]
    [DataRow(TenantId, ProjectId, null, "OperationIdRequired")]
    [DataRow(TenantId, ProjectId, "run-123", "OperationIdMustBeBackendMintedCanonicalId")]
    public void RequestScopeValidation_FailsClosed(
        string? tenantId,
        string? projectId,
        string? operationId,
        string expectedIssue)
    {
        var result = ReceiptReferenceResolver.Resolve(new ReceiptReferenceResolverRequest
        {
            TenantId = tenantId!,
            ProjectId = projectId!,
            OperationId = operationId!,
            RequestedReferences = [],
            AvailableReceipts = []
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void NullRequestedReferencesList_FailsClosed()
    {
        var result = ReceiptReferenceResolver.Resolve(new ReceiptReferenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = null!,
            AvailableReceipts = []
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "ReceiptReferenceRequestedReferencesRequired");
    }

    [TestMethod]
    public void NullAvailableReceiptsList_FailsClosed()
    {
        var result = ReceiptReferenceResolver.Resolve(new ReceiptReferenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = [],
            AvailableReceipts = null!
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "ReceiptReferenceAvailableReceiptsRequired");
    }

    [DataTestMethod]
    [DataRow(null, "ReceiptReferenceIdRequired")]
    [DataRow("", "ReceiptReferenceIdRequired")]
    [DataRow("receipt id", "ReceiptReferenceIdInvalid")]
    [DataRow("https://example.test/receipt", "ReceiptReferenceIdInvalid")]
    public void RequestedReference_FailsClosedForMissingOrUnsafeReferenceId(
        string? receiptReferenceId,
        string expectedIssue)
    {
        var result = Resolve([Requested(receiptReferenceId!, ReceiptReferenceKind.SourceApplyReceipt)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(ReceiptReferenceKind.Unknown, "ReceiptReferenceKindRequired")]
    public void RequestedReference_FailsClosedForUnknownReceiptKind(
        ReceiptReferenceKind kind,
        string expectedIssue)
    {
        var result = Resolve([Requested("receipt-a", kind)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "ReceiptReferenceCorrelationIdRequired")]
    [DataRow("", "ReceiptReferenceCorrelationIdRequired")]
    [DataRow("corr invalid", "ReceiptReferenceCorrelationIdInvalid")]
    [DataRow("run-123", "ReceiptReferenceCorrelationIdInvalid")]
    public void RequestedReference_FailsClosedForInvalidCorrelationId(
        string? correlationId,
        string expectedIssue)
    {
        var result = Resolve([Requested("receipt-a", ReceiptReferenceKind.SourceApplyReceipt, correlationId: correlationId!)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "ReceiptReferenceSourceRequired")]
    [DataRow("", "ReceiptReferenceSourceRequired")]
    [DataRow("source with space", "ReceiptReferenceSourceInvalid")]
    [DataRow("policy satisfied", "ReceiptReferenceSourceInvalid")]
    public void RequestedReference_FailsClosedForMissingOrUnsafeSource(
        string? source,
        string expectedIssue)
    {
        var result = Resolve([Requested("receipt-a", ReceiptReferenceKind.SourceApplyReceipt, source: source!)], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void RequestedReference_FailsClosedForMissingTimestamp()
    {
        var result = Resolve([Requested("receipt-a", ReceiptReferenceKind.SourceApplyReceipt, requestedAtUtc: default(DateTimeOffset))], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "ReceiptReferenceRequestedAtRequired");
    }

    [DataTestMethod]
    [DataRow(OperationReferenceKind.ReceiptId, null, "ReceiptReferenceReferenceIdRequired")]
    [DataRow(OperationReferenceKind.Unknown, "receipt-ref", "ReceiptReferenceReferenceKindRequired")]
    [DataRow(OperationReferenceKind.ReceiptId, "receipt ref", "ReceiptReferenceReferenceIdInvalid")]
    [DataRow(OperationReferenceKind.ReceiptId, "https://example.test/ref", "ReceiptReferenceReferenceIdInvalid")]
    public void RequestedReference_FailsClosedForUnsafeExternalReferencePair(
        OperationReferenceKind referenceKind,
        string? referenceId,
        string expectedIssue)
    {
        var result = Resolve([
            Requested(
                "request-ref",
                ReceiptReferenceKind.GenericReceipt,
                referenceKind: referenceKind,
                referenceId: referenceId)
        ], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "AvailableReceiptIdRequired")]
    [DataRow("", "AvailableReceiptIdRequired")]
    [DataRow("receipt id", "AvailableReceiptIdInvalid")]
    [DataRow("https://example.test/receipt", "AvailableReceiptIdInvalid")]
    public void AvailableReceipt_FailsClosedForMissingOrUnsafeReceiptId(
        string? receiptId,
        string expectedIssue)
    {
        var result = Resolve([], [Available(receiptId!, ReceiptReferenceKind.SourceApplyReceipt)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(ReceiptReferenceKind.Unknown, "AvailableReceiptKindRequired")]
    public void AvailableReceipt_FailsClosedForUnknownReceiptKind(
        ReceiptReferenceKind kind,
        string expectedIssue)
    {
        var result = Resolve([], [Available("receipt-a", kind)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "AvailableReceiptCorrelationIdRequired")]
    [DataRow("", "AvailableReceiptCorrelationIdRequired")]
    [DataRow("corr invalid", "AvailableReceiptCorrelationIdInvalid")]
    public void AvailableReceipt_FailsClosedForInvalidCorrelationId(
        string? correlationId,
        string expectedIssue)
    {
        var result = Resolve([], [Available("receipt-a", ReceiptReferenceKind.SourceApplyReceipt, correlationId: correlationId!)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(OperationCorrelationSurfaceKind.Unknown, "surface-1", "AvailableReceiptSurfaceKindRequired")]
    [DataRow(OperationCorrelationSurfaceKind.ReceiptMetadata, null, "AvailableReceiptSurfaceIdRequired")]
    [DataRow(OperationCorrelationSurfaceKind.ReceiptMetadata, "surface 1", "AvailableReceiptSurfaceIdInvalid")]
    public void AvailableReceipt_FailsClosedForUnsafeSurfaceMetadata(
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        string expectedIssue)
    {
        var result = Resolve([], [
            Available(
                "receipt-a",
                ReceiptReferenceKind.SourceApplyReceipt,
                surfaceKind: surfaceKind,
                surfaceId: surfaceId!)
        ]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "AvailableReceiptSourceRequired")]
    [DataRow("", "AvailableReceiptSourceRequired")]
    [DataRow("source with space", "AvailableReceiptSourceInvalid")]
    [DataRow("raw receipt payload", "AvailableReceiptSourceInvalid")]
    public void AvailableReceipt_FailsClosedForMissingOrUnsafeSource(
        string? source,
        string expectedIssue)
    {
        var result = Resolve([], [Available("receipt-a", ReceiptReferenceKind.SourceApplyReceipt, source: source!)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void AvailableReceipt_FailsClosedForMissingCreatedTimestamp()
    {
        var result = Resolve([], [Available("receipt-a", ReceiptReferenceKind.SourceApplyReceipt, createdAtUtc: default(DateTimeOffset))]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "AvailableReceiptCreatedAtRequired");
    }

    [DataTestMethod]
    [DataRow(OperationReferenceKind.ReceiptId, null, "AvailableReceiptReferenceIdRequired")]
    [DataRow(OperationReferenceKind.Unknown, "receipt-ref", "AvailableReceiptReferenceKindRequired")]
    [DataRow(OperationReferenceKind.ReceiptId, "receipt ref", "AvailableReceiptReferenceIdInvalid")]
    public void AvailableReceipt_FailsClosedForUnsafeExternalReferencePair(
        OperationReferenceKind referenceKind,
        string? referenceId,
        string expectedIssue)
    {
        var result = Resolve([], [
            Available(
                "receipt-a",
                ReceiptReferenceKind.GenericReceipt,
                referenceKind: referenceKind,
                referenceId: referenceId)
        ]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "ReceiptReferenceTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "ReceiptReferenceProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000008", "ReceiptReferenceOperationMismatch")]
    public void CrossScopeRequestedReference_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string expectedIssue)
    {
        var result = Resolve([
            Requested(
                "receipt-a",
                ReceiptReferenceKind.SourceApplyReceipt,
                tenantId: tenantId,
                projectId: projectId,
                operationId: operationId)
        ], []);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "AvailableReceiptTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "AvailableReceiptProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000008", "AvailableReceiptOperationMismatch")]
    public void CrossScopeAvailableReceipt_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string expectedIssue)
    {
        var result = Resolve([], [
            Available(
                "receipt-a",
                ReceiptReferenceKind.SourceApplyReceipt,
                tenantId: tenantId,
                projectId: projectId,
                operationId: operationId)
        ]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(ReceiptReferenceKind.ValidationReceipt, "validation freshness")]
    [DataRow(ReceiptReferenceKind.SourceApplyReceipt, "source apply authority")]
    [DataRow(ReceiptReferenceKind.RollbackReceipt, "rollback authority")]
    [DataRow(ReceiptReferenceKind.CommitReceipt, "push")]
    [DataRow(ReceiptReferenceKind.PushReceipt, "PR creation")]
    [DataRow(ReceiptReferenceKind.PullRequestReceipt, "merge readiness")]
    [DataRow(ReceiptReferenceKind.ReleaseReadinessReceipt, "release readiness")]
    [DataRow(ReceiptReferenceKind.DeploymentReadinessReceipt, "deployment readiness")]
    [DataRow(ReceiptReferenceKind.MemoryPromotionReceipt, "memory promotion")]
    [DataRow(ReceiptReferenceKind.WorkflowContinuationReceipt, "workflow continuation")]
    public void ReceiptFoundDoesNotImplyDownstreamAuthority(
        ReceiptReferenceKind kind,
        string authorityMarker)
    {
        var result = Resolve([Requested("receipt-a", kind)], [Available("receipt-a", kind)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ReceiptReferenceResolutionStatus.Resolved, result.ResolutionStatus);
        AssertContains(result.ForbiddenAuthorityImplications, "receipt found is not authority");
        Assert.IsFalse(result.Warnings.Any(item => item.Equals(authorityMarker, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void MissingReceiptDoesNotChooseNextSafeAction()
    {
        var result = Resolve([Requested("receipt-missing", ReceiptReferenceKind.AuditReceipt)], []);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.NotFound, result.ResolutionStatus);
        AssertContains(result.Warnings, "missing receipt does not choose next safe action");
    }

    [TestMethod]
    public void CompleteResolutionDoesNotImplyActionAllowed()
    {
        var result = Resolve(
            [Requested("receipt-a", ReceiptReferenceKind.GenericReceipt)],
            [Available("receipt-a", ReceiptReferenceKind.GenericReceipt)]);

        Assert.AreEqual(ReceiptReferenceResolutionStatus.Resolved, result.ResolutionStatus);
        AssertContains(result.ForbiddenAuthorityImplications, "complete receipt resolution is not action allowed");
    }

    [TestMethod]
    public void ResultModelsExposeNoAuthorityApprovalPolicyFreshnessNextActionOrProofProperties()
    {
        var names = typeof(ReceiptReferenceResolverResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Concat(typeof(ResolvedReceiptReference).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(static property => property.Name))
            .Concat(typeof(UnresolvedReceiptReference).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(static property => property.Name))
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
            "ReceiptVerified",
            "ExecutionProven",
            "Authorized",
            "Executable"
        })
        {
            Assert.IsFalse(names.Contains(forbiddenName, StringComparer.Ordinal));
        }

        Assert.IsFalse(names.Any(static property => property.StartsWith("Can", StringComparison.Ordinal)));
        Assert.IsFalse(Enum.GetNames<ReceiptReferenceResolutionStatus>().Any(static name =>
            name is "Approved" or "Verified" or "Authorized" or "Executable" or "Fresh" or "Allowed"));
    }

    [TestMethod]
    public void D01ThroughD08CompatibilityTypesRemainAvailable()
    {
        Assert.IsTrue(OperationIdentityValidator.ValidateOperationId(OperationId).IsValid);
        Assert.AreEqual(OperationIdentityLookupStatus.NotFound, OperationIdentityLookupStatus.NotFound);

        var correlation = OperationCorrelationValidator.ValidateLink(new OperationCorrelationLink
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            SurfaceKind = OperationCorrelationSurfaceKind.ReceiptMetadata,
            SurfaceId = "surface-d09",
            ObservedAtUtc = ObservedAtUtc,
            Source = "d09-test"
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
    public void StaticScan_D09CoreFilesAddNoApiSqlUiStoreExecutorMutationRawPayloadOrResolverSurface()
    {
        var source = D09CoreSource();

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
            "RawPayloadReader",
            "ReadRawPayload",
            "RawReceipt",
            "RawEvidence",
            "OperationIdentityLookupResolver.Resolve",
            "GovernedOperationTimelineAssembler.Assemble",
            "OperationStatusProjector.Project(",
            "MissingEvidenceResolver.Resolve",
            "ForbiddenActionResolver.Resolve",
            "EvidenceResolver",
            "FreshnessResolver",
            "BlockedStateFormatter",
            "NextSafeActionFormatter",
            "AuthorityWarningFormatter",
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
            "AcceptApproval",
            "PolicySatisfaction"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void Receipt_RecordsReceiptReferenceIsNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D09_RECEIPT_REFERENCE_RESOLVER.md"));

        Assert.IsTrue(receipt.Contains(
            "The receipt reference resolver resolves scoped receipt metadata references only. It does not fetch raw receipt payloads, verify receipt authenticity, prove execution, accept approval, satisfy policy, validate freshness, grant authority, choose next safe action, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.",
            StringComparison.Ordinal));
    }

    private static ReceiptReferenceResolverResult Resolve(
        IReadOnlyList<ReceiptReferenceRequestItem> requested,
        IReadOnlyList<AvailableReceiptMetadata> available) =>
        ReceiptReferenceResolver.Resolve(new ReceiptReferenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            RequestedReferences = requested,
            AvailableReceipts = available
        });

    private static ReceiptReferenceRequestItem Requested(
        string receiptReferenceId,
        ReceiptReferenceKind kind,
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string correlationId = CorrelationId,
        OperationReferenceKind referenceKind = OperationReferenceKind.Unknown,
        string? referenceId = null,
        string source = "d09-source",
        DateTimeOffset? requestedAtUtc = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            CorrelationId = correlationId,
            ReceiptReferenceId = receiptReferenceId,
            ReceiptKind = kind,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
            Source = source,
            RequestedAtUtc = requestedAtUtc ?? ObservedAtUtc
        };

    private static AvailableReceiptMetadata Available(
        string receiptId,
        ReceiptReferenceKind kind,
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string correlationId = CorrelationId,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.ReceiptMetadata,
        string surfaceId = "surface-1",
        OperationReferenceKind referenceKind = OperationReferenceKind.Unknown,
        string? referenceId = null,
        DateTimeOffset? createdAtUtc = null,
        string source = "d09-source",
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            CorrelationId = correlationId,
            ReceiptId = receiptId,
            ReceiptKind = kind,
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
            CreatedAtUtc = createdAtUtc ?? ObservedAtUtc,
            Source = source,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static void AssertAmbiguous(ReceiptReferenceResolverResult result, string expectedIssue)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(ReceiptReferenceResolutionStatus.AmbiguousReferences, result.ResolutionStatus);
        AssertContains(result.AmbiguousReceipts, expectedIssue);
        Assert.AreEqual(0, result.ResolvedReceipts.Count);
        Assert.AreEqual(0, result.UnresolvedReceipts.Count);
    }

    private static string D09CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ReceiptReferenceResolverModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ReceiptReferenceResolverValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ReceiptReferenceResolver.cs")));
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
