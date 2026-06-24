using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD07MissingEvidenceResolverTests
{
    private const string TenantId = "tenant-d07";
    private const string ProjectId = "project-d07";
    private const string OperationId = "op_0000000000000007";
    private const string CorrelationId = "corr_0123456789abcdef";
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T06:00:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-24T06:05:00Z");

    [TestMethod]
    public void ValidRequestWithNoRequirements_ReturnsNoRequirements()
    {
        var result = Resolve([], []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.NoRequirements, result.ResolutionStatus);
        Assert.AreEqual(0, result.MissingEvidence.Count);
        Assert.AreEqual(0, result.SatisfiedEvidence.Count);
        AssertContains(result.ForbiddenAuthorityImplications, "complete evidence presence is not action allowed");
    }

    [TestMethod]
    public void ValidRequestWithAllRequirementsSatisfied_ReturnsComplete()
    {
        var requirements = AllRequirementKinds()
            .Select((kind, index) => Requirement(kind, $"requirement-{index}", $"Required {kind}"))
            .ToArray();
        var observed = AllRequirementKinds()
            .Select((kind, index) => Observed(Map(kind), $"observed-{index}", surfaceId: $"surface-{index}"))
            .ToArray();

        var result = Resolve(requirements, observed);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.Complete, result.ResolutionStatus);
        Assert.AreEqual(0, result.MissingEvidence.Count);
        Assert.AreEqual(requirements.Length, result.SatisfiedEvidence.Count);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceRequirementKind.PatchArtifactMetadata)]
    [DataRow(MissingEvidenceRequirementKind.ValidationResultMetadata)]
    [DataRow(MissingEvidenceRequirementKind.EvidenceMetadata)]
    [DataRow(MissingEvidenceRequirementKind.ReceiptMetadata)]
    [DataRow(MissingEvidenceRequirementKind.ApprovalRecordReference)]
    [DataRow(MissingEvidenceRequirementKind.PolicySatisfactionRecordReference)]
    [DataRow(MissingEvidenceRequirementKind.SourceApplyReceiptReference)]
    [DataRow(MissingEvidenceRequirementKind.CommitReceiptReference)]
    [DataRow(MissingEvidenceRequirementKind.PushReceiptReference)]
    [DataRow(MissingEvidenceRequirementKind.PullRequestReceiptReference)]
    [DataRow(MissingEvidenceRequirementKind.RollbackReceiptReference)]
    [DataRow(MissingEvidenceRequirementKind.RecoveryReceiptReference)]
    public void MissingRequirementKinds_ReturnMissingEvidence(MissingEvidenceRequirementKind requirementKind)
    {
        var result = Resolve([Requirement(requirementKind)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.MissingEvidence, result.ResolutionStatus);
        Assert.AreEqual(1, result.MissingEvidence.Count);
        Assert.AreEqual(requirementKind, result.MissingEvidence[0].RequirementKind);
        Assert.AreEqual($"MissingObservedEvidenceKind:{requirementKind}", result.MissingEvidence[0].MissingReason);
    }

    [TestMethod]
    public void RedactedSatisfiedEvidence_RemainsMetadataOnlyAndVisible()
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.ReceiptMetadata)],
            [Observed(ObservedEvidenceKind.ReceiptMetadata, isRedacted: true, redactionReason: "private-material")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.Complete, result.ResolutionStatus);
        Assert.IsTrue(result.SatisfiedEvidence.Single().IsRedacted);
        AssertContains(result.Warnings, "redacted metadata is not raw payload");
    }

    [DataTestMethod]
    [DataRow(true, null, "MissingEvidenceObservedRedactionReasonRequired")]
    [DataRow(true, "", "MissingEvidenceObservedRedactionReasonRequired")]
    [DataRow(true, "approval granted", "MissingEvidenceObservedRedactionReasonInvalid")]
    [DataRow(true, "api key leaked", "MissingEvidenceObservedRedactionReasonInvalid")]
    public void RedactedEvidence_RequiresSafeRedactionReason(
        bool isRedacted,
        string? redactionReason,
        string expectedIssue)
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.ReceiptMetadata)],
            [Observed(ObservedEvidenceKind.ReceiptMetadata, isRedacted: isRedacted, redactionReason: redactionReason)]);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(MissingEvidenceResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, ProjectId, OperationId, "MissingEvidenceTenantIdRequired")]
    [DataRow("", ProjectId, OperationId, "MissingEvidenceTenantIdRequired")]
    [DataRow("tenant d07", ProjectId, OperationId, "MissingEvidenceTenantIdInvalid")]
    [DataRow(TenantId, null, OperationId, "MissingEvidenceProjectIdRequired")]
    [DataRow(TenantId, "", OperationId, "MissingEvidenceProjectIdRequired")]
    [DataRow(TenantId, "project d07", OperationId, "MissingEvidenceProjectIdInvalid")]
    [DataRow(TenantId, ProjectId, null, "OperationIdRequired")]
    [DataRow(TenantId, ProjectId, "run-123", "OperationIdMustBeBackendMintedCanonicalId")]
    public void RequestScopeValidation_FailsClosed(
        string? tenantId,
        string? projectId,
        string? operationId,
        string expectedIssue)
    {
        var result = MissingEvidenceResolver.Resolve(new MissingEvidenceResolverRequest
        {
            TenantId = tenantId!,
            ProjectId = projectId!,
            OperationId = operationId!,
            Requirements = [Requirement(MissingEvidenceRequirementKind.EvidenceMetadata)],
            ObservedEvidence = []
        });

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(MissingEvidenceResolutionStatus.InvalidRequest, result.ResolutionStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void NullRequirementsList_FailsClosed()
    {
        var result = MissingEvidenceResolver.Resolve(new MissingEvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            Requirements = null!,
            ObservedEvidence = []
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "MissingEvidenceRequirementsRequired");
    }

    [TestMethod]
    public void NullObservedEvidenceList_FailsClosed()
    {
        var result = MissingEvidenceResolver.Resolve(new MissingEvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            Requirements = [Requirement(MissingEvidenceRequirementKind.EvidenceMetadata)],
            ObservedEvidence = null!
        });

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "MissingEvidenceObservedEvidenceRequired");
    }

    [TestMethod]
    public void DuplicateRequirementIds_ReturnAmbiguousEvidence()
    {
        var result = Resolve(
            [
                Requirement(MissingEvidenceRequirementKind.EvidenceMetadata, requirementId: "requirement-duplicate", requiredLabel: "Evidence one"),
                Requirement(MissingEvidenceRequirementKind.ReceiptMetadata, requirementId: "requirement-duplicate", requiredLabel: "Evidence two")
            ],
            []);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.AmbiguousEvidence, result.ResolutionStatus);
        AssertContains(result.AmbiguousEvidence, "DuplicateMissingEvidenceRequirementId:requirement-duplicate");
    }

    [TestMethod]
    public void DuplicateObservedEvidenceIds_ReturnAmbiguousEvidence()
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.EvidenceMetadata)],
            [
                Observed(ObservedEvidenceKind.EvidenceMetadata, observedEvidenceId: "observed-duplicate", surfaceId: "surface-a"),
                Observed(ObservedEvidenceKind.ReceiptMetadata, observedEvidenceId: "observed-duplicate", surfaceId: "surface-b")
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.AmbiguousEvidence, result.ResolutionStatus);
        AssertContains(result.AmbiguousEvidence, "DuplicateObservedEvidenceId:observed-duplicate");
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceRequirementKind.Unknown, MissingEvidenceRequirementSeverity.Required, "MissingEvidenceRequirementKindRequired")]
    [DataRow(MissingEvidenceRequirementKind.EvidenceMetadata, MissingEvidenceRequirementSeverity.Unknown, "MissingEvidenceRequirementSeverityRequired")]
    public void RequirementValidation_FailsClosedForUnknownKindOrSeverity(
        MissingEvidenceRequirementKind requirementKind,
        MissingEvidenceRequirementSeverity severity,
        string expectedIssue)
    {
        var result = Resolve([Requirement(requirementKind, severity: severity)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(null, "MissingEvidenceRequirementIdRequired")]
    [DataRow("", "MissingEvidenceRequirementIdRequired")]
    [DataRow("requirement id", "MissingEvidenceRequirementIdInvalid")]
    [DataRow("approval granted", "MissingEvidenceRequirementIdInvalid")]
    public void RequirementValidation_FailsClosedForMissingOrUnsafeId(
        string? requirementId,
        string expectedIssue)
    {
        var result = Resolve([Requirement(MissingEvidenceRequirementKind.EvidenceMetadata, requirementId: requirementId!)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow(ObservedEvidenceKind.Unknown, "MissingEvidenceObservedEvidenceKindRequired")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedEvidenceIdRequired", null, "surface-1", CorrelationId, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, "evidence-123", "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedEvidenceIdInvalid", "observed id", "surface-1", CorrelationId, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, "evidence-123", "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedCorrelation:OperationCorrelationIdRequired", "observed-1", "surface-1", null, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, "evidence-123", "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedCorrelation:OperationCorrelationIdCannotLookLikeRunId", "observed-1", "surface-1", "run-123", OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, "evidence-123", "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedSurfaceKindRequired", "observed-1", "surface-1", CorrelationId, OperationCorrelationSurfaceKind.Unknown, OperationReferenceKind.EvidenceId, "evidence-123", "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedSurfaceIdRequired", "observed-1", null, CorrelationId, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, "evidence-123", "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedSurfaceIdInvalid", "observed-1", "surface 1", CorrelationId, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, "evidence-123", "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedReferenceIdRequired", "observed-1", "surface-1", CorrelationId, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, null, "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedReferenceKindRequired", "observed-1", "surface-1", CorrelationId, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.Unknown, "evidence-123", "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedReferenceIdInvalid", "observed-1", "surface-1", CorrelationId, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, "evidence 123", "d07-source")]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedSourceRequired", "observed-1", "surface-1", CorrelationId, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, "evidence-123", null)]
    [DataRow(ObservedEvidenceKind.EvidenceMetadata, "MissingEvidenceObservedSourceInvalid", "observed-1", "surface-1", CorrelationId, OperationCorrelationSurfaceKind.EvidenceMetadata, OperationReferenceKind.EvidenceId, "evidence-123", "source with space")]
    public void ObservedEvidenceValidation_FailsClosedForInvalidShape(
        ObservedEvidenceKind evidenceKind,
        string expectedIssue,
        string? observedEvidenceId = "observed-1",
        string? surfaceId = "surface-1",
        string? correlationId = CorrelationId,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.EvidenceMetadata,
        OperationReferenceKind referenceKind = OperationReferenceKind.EvidenceId,
        string? referenceId = "evidence-123",
        string? source = "d07-source")
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.EvidenceMetadata)],
            [Observed(
                evidenceKind,
                observedEvidenceId: observedEvidenceId!,
                surfaceId: surfaceId!,
                correlationId: correlationId!,
                surfaceKind: surfaceKind,
                referenceKind: referenceKind,
                referenceId: referenceId!,
                source: source!)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void ObservedEvidenceValidation_RequiresObservedTimestamp()
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.EvidenceMetadata)],
            [Observed(ObservedEvidenceKind.EvidenceMetadata, observedAtUtc: default(DateTimeOffset))]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "MissingEvidenceObservedAtRequired");
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "MissingEvidenceRequirementTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "MissingEvidenceRequirementProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000008", "MissingEvidenceRequirementOperationMismatch")]
    public void CrossScopeRequirement_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string expectedIssue)
    {
        var result = Resolve([Requirement(MissingEvidenceRequirementKind.EvidenceMetadata, tenantId: tenantId, projectId: projectId, operationId: operationId)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [DataTestMethod]
    [DataRow("tenant-other", ProjectId, OperationId, "MissingEvidenceObservedTenantMismatch")]
    [DataRow(TenantId, "project-other", OperationId, "MissingEvidenceObservedProjectMismatch")]
    [DataRow(TenantId, ProjectId, "op_0000000000000008", "MissingEvidenceObservedOperationMismatch")]
    public void CrossScopeObservedEvidence_FailsClosed(
        string tenantId,
        string projectId,
        string operationId,
        string expectedIssue)
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.EvidenceMetadata)],
            [Observed(ObservedEvidenceKind.EvidenceMetadata, tenantId: tenantId, projectId: projectId, operationId: operationId)]);

        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public void MultipleIndistinguishableMatchingEvidenceRecords_ReturnAmbiguousEvidence()
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.EvidenceMetadata)],
            [
                Observed(ObservedEvidenceKind.EvidenceMetadata, observedEvidenceId: "observed-a", surfaceId: "surface-a"),
                Observed(ObservedEvidenceKind.EvidenceMetadata, observedEvidenceId: "observed-b", surfaceId: "surface-b")
            ]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.AmbiguousEvidence, result.ResolutionStatus);
        AssertContains(result.AmbiguousEvidence, "AmbiguousObservedEvidenceForRequirementKind:EvidenceMetadata");
    }

    [TestMethod]
    public void ResolverDoesNotUseTextInferenceToSatisfyRequirements()
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.PolicySatisfactionRecordReference)],
            [Observed(ObservedEvidenceKind.EvidenceMetadata, referenceKind: OperationReferenceKind.EvidenceId, referenceId: "policy-satisfaction-record-reference")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.MissingEvidence, result.ResolutionStatus);
        Assert.AreEqual(MissingEvidenceRequirementKind.PolicySatisfactionRecordReference, result.MissingEvidence.Single().RequirementKind);
    }

    [TestMethod]
    public void ResolverDoesNotUseCorrelationIdToSubstituteEvidenceKind()
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.EvidenceMetadata)],
            [Observed(ObservedEvidenceKind.CorrelationLink, referenceKind: OperationReferenceKind.CorrelationId, referenceId: CorrelationId)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.MissingEvidence, result.ResolutionStatus);
    }

    [TestMethod]
    public void ResolverDoesNotUseStatusProjectionToSatisfyRequirements()
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.ApprovalRecordReference)],
            [Observed(ObservedEvidenceKind.StatusProjectionEvent, referenceKind: OperationReferenceKind.StatusRecordId, referenceId: "status-record-1")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.MissingEvidence, result.ResolutionStatus);
    }

    [TestMethod]
    public void TimelineSurfaceDoesNotSatisfyTimelineRequirementUnlessEvidenceKindIsTimelineEntry()
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.TimelineEntry)],
            [Observed(ObservedEvidenceKind.EvidenceMetadata, surfaceKind: OperationCorrelationSurfaceKind.TimelineEvent, surfaceId: "timeline-event-1")]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.MissingEvidence, result.ResolutionStatus);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceRequirementKind.ValidationResultMetadata, ObservedEvidenceKind.ValidationResultMetadata, "missing evidence resolver is not validation freshness")]
    [DataRow(MissingEvidenceRequirementKind.ApprovalRecordReference, ObservedEvidenceKind.ApprovalRecordReference, "missing evidence resolver is not approval")]
    [DataRow(MissingEvidenceRequirementKind.PolicySatisfactionRecordReference, ObservedEvidenceKind.PolicySatisfactionRecordReference, "missing evidence resolver is not policy satisfaction")]
    [DataRow(MissingEvidenceRequirementKind.ReceiptMetadata, ObservedEvidenceKind.ReceiptMetadata, "missing evidence resolver is not receipt resolution")]
    [DataRow(MissingEvidenceRequirementKind.PullRequestReceiptReference, ObservedEvidenceKind.PullRequestReceiptReference, "missing evidence resolver is not merge readiness")]
    [DataRow(MissingEvidenceRequirementKind.ReleaseReadinessEvidenceReference, ObservedEvidenceKind.ReleaseReadinessEvidenceReference, "missing evidence resolver is not release readiness")]
    [DataRow(MissingEvidenceRequirementKind.DeploymentReadinessEvidenceReference, ObservedEvidenceKind.DeploymentReadinessEvidenceReference, "missing evidence resolver is not deployment readiness")]
    public void EvidencePresence_DoesNotImplyAuthority(
        MissingEvidenceRequirementKind requirementKind,
        ObservedEvidenceKind observedKind,
        string expectedForbiddenImplication)
    {
        var result = Resolve([Requirement(requirementKind)], [Observed(observedKind)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.Complete, result.ResolutionStatus);
        AssertContains(result.ForbiddenAuthorityImplications, expectedForbiddenImplication);
        AssertContains(result.Warnings, "evidence present is not action allowed");
    }

    [TestMethod]
    public void MissingEvidenceResult_DoesNotChooseNextSafeAction()
    {
        var result = Resolve([Requirement(MissingEvidenceRequirementKind.SourceApplyReceiptReference)]);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MissingEvidenceResolutionStatus.MissingEvidence, result.ResolutionStatus);
        AssertContains(result.ForbiddenAuthorityImplications, "missing evidence resolver is not next-safe-action formatting");
    }

    [TestMethod]
    public void AmbiguousEvidenceResult_DoesNotChooseWinner()
    {
        var result = Resolve(
            [Requirement(MissingEvidenceRequirementKind.EvidenceMetadata)],
            [
                Observed(ObservedEvidenceKind.EvidenceMetadata, observedEvidenceId: "observed-a", surfaceId: "surface-a"),
                Observed(ObservedEvidenceKind.EvidenceMetadata, observedEvidenceId: "observed-b", surfaceId: "surface-b")
            ]);

        Assert.AreEqual(MissingEvidenceResolutionStatus.AmbiguousEvidence, result.ResolutionStatus);
        Assert.AreEqual(0, result.SatisfiedEvidence.Count);
        AssertContains(result.Warnings, "ambiguous evidence does not choose a winner");
    }

    [TestMethod]
    public void ResultModels_ExposeNoAuthorityProperties()
    {
        foreach (var property in typeof(MissingEvidenceResolutionResult).GetProperties()
            .Concat(typeof(MissingEvidenceItem).GetProperties())
            .Concat(typeof(SatisfiedEvidenceItem).GetProperties()))
        {
            AssertDoesNotContain(property.Name, "Can");
            AssertDoesNotContain(property.Name, "ApprovalStatus");
            AssertDoesNotContain(property.Name, "PolicySatisfied");
            AssertDoesNotContain(property.Name, "ValidationFresh");
            AssertDoesNotContain(property.Name, "NextSafeAction");
            AssertDoesNotContain(property.Name, "ActionAllowed");
            AssertDoesNotContain(property.Name, "AuthorityGranted");
            AssertDoesNotContain(property.Name, "ReleaseReady");
            AssertDoesNotContain(property.Name, "DeployReady");
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
            ObservedAtUtc = ObservedAtUtc,
            Source = "d07-source"
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
            TimelineEventId = "timeline-event-d07",
            EventKind = GovernedOperationTimelineEventKind.StatusObserved,
            OccurredAtUtc = ObservedAtUtc,
            RecordedAtUtc = ObservedAtUtc.AddMinutes(1),
            Source = "d07-source",
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
    public void D05ProjectionValidationStillPasses()
    {
        var result = OperationStatusProjector.Project(new OperationStatusProjectionRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            ProjectionVersion = "projection-v1",
            Events =
            [
                new OperationStatusProjectionEvent
                {
                    TenantId = TenantId,
                    ProjectId = ProjectId,
                    OperationId = OperationId,
                    CorrelationId = CorrelationId,
                    ProjectionEventId = "projection-event-d07",
                    AppendPosition = 0,
                    EventKind = OperationStatusProjectionEventKind.RunStarted,
                    OccurredAtUtc = ObservedAtUtc,
                    RecordedAtUtc = ObservedAtUtc.AddMinutes(1),
                    Source = "d07-source",
                    SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
                    SurfaceId = "status-record-1",
                    ReferenceKind = OperationReferenceKind.RunId,
                    ReferenceId = "run-123"
                }
            ]
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(OperationProjectedStatusKind.RunObserved, result.ProjectedStatus!.ProjectedStatusKind);
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
    public void StaticScan_D07CoreFilesAddNoApiSqlUiStoreExecutorOrMutationSurface()
    {
        var source = D07CoreSource();

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
            "OperationIdentityLookupResolver.Resolve",
            "GovernedOperationTimelineAssembler.Assemble",
            "OperationStatusProjector.Project(",
            "ReceiptResolver",
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
            "AcceptedApproval"
        })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void Receipt_RecordsMissingEvidenceIsNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D07_MISSING_EVIDENCE_RESOLVER.md"));

        Assert.IsTrue(receipt.Contains(
            "The missing evidence resolver explains absent scoped evidence metadata. It does not resolve raw evidence, accept approval, satisfy policy, validate freshness, determine forbidden actions, choose next safe action, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.",
            StringComparison.Ordinal));
    }

    private static MissingEvidenceResolutionResult Resolve(
        IReadOnlyList<MissingEvidenceRequirement> requirements,
        IReadOnlyList<ObservedEvidenceReference>? observedEvidence = null) =>
        MissingEvidenceResolver.Resolve(new MissingEvidenceResolverRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            Requirements = requirements,
            ObservedEvidence = observedEvidence ?? []
        });

    private static MissingEvidenceRequirement Requirement(
        MissingEvidenceRequirementKind requirementKind,
        string requirementId = "requirement-1",
        string requiredLabel = "Required evidence",
        string requiredFor = "operation review",
        MissingEvidenceRequirementSeverity severity = MissingEvidenceRequirementSeverity.Required,
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string source = "d07-source",
        DateTimeOffset? createdAtUtc = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            RequirementId = requirementId,
            RequirementKind = requirementKind,
            RequiredLabel = requiredLabel,
            RequiredFor = requiredFor,
            Severity = severity,
            Source = source,
            CreatedAtUtc = createdAtUtc ?? CreatedAtUtc
        };

    private static ObservedEvidenceReference Observed(
        ObservedEvidenceKind evidenceKind,
        string observedEvidenceId = "observed-1",
        string tenantId = TenantId,
        string projectId = ProjectId,
        string operationId = OperationId,
        string correlationId = CorrelationId,
        OperationCorrelationSurfaceKind surfaceKind = OperationCorrelationSurfaceKind.EvidenceMetadata,
        string surfaceId = "surface-1",
        OperationReferenceKind referenceKind = OperationReferenceKind.EvidenceId,
        string referenceId = "evidence-123",
        DateTimeOffset? observedAtUtc = null,
        string source = "d07-source",
        bool isRedacted = false,
        string? redactionReason = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            CorrelationId = correlationId,
            ObservedEvidenceId = observedEvidenceId,
            EvidenceKind = evidenceKind,
            SurfaceKind = surfaceKind,
            SurfaceId = surfaceId,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
            ObservedAtUtc = observedAtUtc ?? ObservedAtUtc,
            Source = source,
            IsRedacted = isRedacted,
            RedactionReason = redactionReason
        };

    private static MissingEvidenceRequirementKind[] AllRequirementKinds() =>
    [
        MissingEvidenceRequirementKind.OperationIdentity,
        MissingEvidenceRequirementKind.CorrelationLink,
        MissingEvidenceRequirementKind.TimelineEntry,
        MissingEvidenceRequirementKind.StatusProjectionEvent,
        MissingEvidenceRequirementKind.PatchArtifactMetadata,
        MissingEvidenceRequirementKind.ValidationResultMetadata,
        MissingEvidenceRequirementKind.EvidenceMetadata,
        MissingEvidenceRequirementKind.ReceiptMetadata,
        MissingEvidenceRequirementKind.ApprovalRecordReference,
        MissingEvidenceRequirementKind.PolicySatisfactionRecordReference,
        MissingEvidenceRequirementKind.SourceApplyReceiptReference,
        MissingEvidenceRequirementKind.CommitPackageReceiptReference,
        MissingEvidenceRequirementKind.CommitReceiptReference,
        MissingEvidenceRequirementKind.PushReceiptReference,
        MissingEvidenceRequirementKind.PullRequestReceiptReference,
        MissingEvidenceRequirementKind.RollbackReceiptReference,
        MissingEvidenceRequirementKind.RecoveryReceiptReference,
        MissingEvidenceRequirementKind.ReleaseReadinessEvidenceReference,
        MissingEvidenceRequirementKind.DeploymentReadinessEvidenceReference
    ];

    private static ObservedEvidenceKind Map(MissingEvidenceRequirementKind requirementKind) =>
        Enum.Parse<ObservedEvidenceKind>(requirementKind.ToString());

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
                    ObservedAtUtc = ObservedAtUtc,
                    Source = "d07-reference-source"
                }
            ],
            CorrelationId = CorrelationId
        };

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

    private static string D07CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "MissingEvidenceResolverModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "MissingEvidenceResolverValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "MissingEvidenceResolver.cs")));
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
