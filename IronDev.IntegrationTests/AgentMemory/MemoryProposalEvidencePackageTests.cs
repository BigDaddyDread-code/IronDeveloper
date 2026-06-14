using IronDev.Core.AgentMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("MemoryProposalEvidencePackage")]
public sealed class MemoryProposalEvidencePackageTests
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-eeee-4444-8888-990000000108");
    private static readonly Guid ProposalId = Guid.Parse("20000000-eeee-4444-8888-990000000108");
    private static readonly Guid PackageId = Guid.Parse("30000000-eeee-4444-8888-990000000108");
    private static readonly Guid WorkflowRunId = Guid.Parse("40000000-eeee-4444-8888-990000000108");
    private static readonly Guid WorkflowRunStepId = Guid.Parse("50000000-eeee-4444-8888-990000000108");
    private static readonly Guid WorkflowCheckpointId = Guid.Parse("60000000-eeee-4444-8888-990000000108");
    private static readonly Guid GroundingReferenceId = Guid.Parse("70000000-eeee-4444-8888-990000000108");

    [TestMethod]
    public void MemoryProposalEvidencePackage_ExposesExpectedShape()
    {
        var package = new MemoryProposalEvidencePackageValidator().Normalize(CreateRequest());

        Assert.AreEqual(PackageId, package.MemoryProposalEvidencePackageId);
        Assert.AreEqual(ProposalId, package.MemoryProposalId);
        Assert.AreEqual(ProjectId, package.ProjectId);
        Assert.AreEqual(MemoryProposalEvidencePackageStatus.ReadyForReview, package.Status);
        Assert.AreEqual(MemoryProposalEvidencePackagePurpose.HumanReview, package.Purpose);
        Assert.AreEqual(1, package.EvidenceReferences.Count);
        Assert.AreEqual(1, package.GroundingReferences.Count);
        Assert.AreEqual(1, package.WorkflowReferences.Count);
        Assert.AreEqual(1, package.ReviewNotes.Count);
        AssertPackageHasNoAuthority(package);
    }

    [TestMethod]
    public void MemoryProposalEvidencePackage_UsesBoundedStatuses()
    {
        var names = Enum.GetNames<MemoryProposalEvidencePackageStatus>();
        CollectionAssert.AreEquivalent(
            new[] { "Assembled", "ReadyForReview", "NeedsEvidence", "NeedsClarification", "ContainsRisk", "RequiresSanitization", "Quarantined", "Superseded", "Withdrawn" },
            names);
        AssertNoForbiddenTokens(string.Join("\n", names), "Accepted", "Approved", "Promoted", "Rejected", "Active", "Indexed", "Embedded", "Retrievable", "PolicySatisfied", "AuthorityTransferred", "SourceApplied", "ReleaseApproved");
    }

    [TestMethod]
    public void MemoryProposalEvidencePackage_UsesBoundedPurposes()
    {
        var names = Enum.GetNames<MemoryProposalEvidencePackagePurpose>();
        CollectionAssert.AreEquivalent(
            new[] { "HumanReview", "GovernedReview", "SanitizationReview", "DuplicateReview", "RiskReview", "EvidenceCompletenessReview", "PortableCandidateReview", "ProjectMemoryCandidateReview", "AgentMemoryCandidateReview" },
            names);
        AssertNoForbiddenTokens(string.Join("\n", names), "AcceptMemory", "ApproveMemory", "PromoteMemory", "RejectMemory", "ActivateRetrieval", "CreateEmbedding", "WriteVectorStore", "SatisfyPolicy", "TransferAuthority", "ApproveRelease");
    }

    [TestMethod]
    public void MemoryProposalEvidencePackage_UsesBoundedReviewNoteTypes()
    {
        var validator = new MemoryProposalEvidencePackageValidator();
        var valid = CreateRequest(reviewNotes: new[] { ReviewNote("RiskSummary") });
        Assert.IsTrue(validator.Validate(valid).IsValid);

        var invalid = CreateRequest(reviewNotes: new[] { ReviewNote("PromotionDecision") });
        AssertHasIssue(validator.Validate(invalid), "review_note_type_forbidden");
    }

    [TestMethod]
    public void MemoryProposalEvidencePackage_AuthorityFlagsAreFalse()
    {
        AssertPackageHasNoAuthority(new MemoryProposalEvidencePackageValidator().Normalize(CreateRequest()));
    }

    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsEmptyProposalId() => AssertInvalid(CreateRequest(memoryProposalId: Guid.Empty), "memory_proposal_id_required");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsEmptyProjectId() => AssertInvalid(CreateRequest(projectId: Guid.Empty), "project_id_required");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsBlankPackageKey() => AssertInvalid(CreateRequest(packageKey: " "), "package_key_required");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsForbiddenStatus() => AssertInvalid(CreateRequest(status: (MemoryProposalEvidencePackageStatus)999), "invalid_status");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsForbiddenPurpose() => AssertInvalid(CreateRequest(purpose: (MemoryProposalEvidencePackagePurpose)999), "invalid_purpose");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsHiddenReasoning() => AssertInvalid(CreateRequest(safeMemory: "hiddenReasoning leaked"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsRawPrompt() => AssertInvalid(CreateRequest(safeMemory: "rawPrompt leaked"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsRawCompletion() => AssertInvalid(CreateRequest(safeMemory: "rawCompletion leaked"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsRawToolOutput() => AssertInvalid(CreateRequest(safeMemory: "rawToolOutput leaked"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsEntirePatch() => AssertInvalid(CreateRequest(safeMemory: "entirePatch leaked"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsAcceptedMemoryLanguage() => AssertInvalid(CreateRequest(safeMemory: "accepted memory created"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsPromotedMemoryLanguage() => AssertInvalid(CreateRequest(safeMemory: "promoted memory"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsRetrievalActiveLanguage() => AssertInvalid(CreateRequest(safeMemory: "retrieval active"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsEmbeddingLanguage() => AssertInvalid(CreateRequest(safeMemory: "embedding created"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsVectorWriteLanguage() => AssertInvalid(CreateRequest(safeMemory: "vector write"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsPortableApprovedLanguage() => AssertInvalid(CreateRequest(safeMemory: "portable memory approved"), "package_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackageValidator_RejectsTrueAuthorityFlags() => AssertInvalid(CreateRequest(acceptsMemory: true), "authority_flags_forbidden");

    [TestMethod] public void MemoryProposalEvidencePackage_EvidenceReferenceIsNotApproval() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceIsApproval: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalEvidencePackage_EvidenceReferenceIsNotPermission() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceIsPermission: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalEvidencePackage_EvidenceReferenceDoesNotAcceptMemory() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceAcceptsMemory: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalEvidencePackage_GroundingReferenceIsNotAuthority() => AssertInvalid(CreateRequest(groundingReferences: new[] { Grounding(groundingIsAuthority: true) }), "grounding_authority_forbidden");
    [TestMethod] public void MemoryProposalEvidencePackage_GroundingReferenceDoesNotAcceptMemory() => AssertInvalid(CreateRequest(groundingReferences: new[] { Grounding(groundingAcceptsMemory: true) }), "grounding_authority_forbidden");
    [TestMethod] public void MemoryProposalEvidencePackage_WorkflowReferenceDoesNotAcceptMemory() => AssertInvalid(CreateRequest(workflowReferences: new[] { Workflow(workflowReferenceAcceptsMemory: true) }), "workflow_reference_authority_forbidden");
    [TestMethod] public void MemoryProposalEvidencePackage_WorkflowReferenceDoesNotPromoteMemory() => AssertInvalid(CreateRequest(workflowReferences: new[] { Workflow(workflowReferencePromotesMemory: true) }), "workflow_reference_authority_forbidden");

    [TestMethod] public void MemoryProposalEvidencePackage_RiskNoteDoesNotRejectMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("RiskSummary", noteRejectsMemory: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalEvidencePackage_SanitizationNoteDoesNotApprovePortableMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("SanitizationSummary", summary: "portable memory approved") }), "review_note_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackage_ConfidenceNoteDoesNotMakeTruth() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("EvidenceSummary", summary: "policy satisfied") }), "review_note_text_unsafe");
    [TestMethod] public void MemoryProposalEvidencePackage_RejectsAcceptanceDecisionNote() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("AcceptanceDecision") }), "review_note_type_forbidden");
    [TestMethod] public void MemoryProposalEvidencePackage_RejectsRejectionDecisionNote() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("RejectionDecision") }), "review_note_type_forbidden");
    [TestMethod] public void MemoryProposalEvidencePackage_RejectsPromotionDecisionNote() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("PromotionDecision") }), "review_note_type_forbidden");

    [TestMethod]
    public void MemoryProposalEvidencePackageAssembler_AssemblesFromStagedProposal()
    {
        var package = new MemoryProposalEvidencePackageValidator().Normalize(new MemoryProposalEvidencePackageAssembler().Assemble(CreateStagedProposal()));

        Assert.AreEqual(ProposalId, package.MemoryProposalId);
        Assert.AreEqual(ProjectId, package.ProjectId);
        Assert.AreEqual(MemoryProposalEvidencePackageStatus.ReadyForReview, package.Status);
        Assert.AreEqual("proposal-108.evidence-package", package.PackageKey);
        AssertPackageHasNoAuthority(package);
    }

    [TestMethod]
    public void MemoryProposalEvidencePackageAssembler_CopiesEvidenceReferencesAsEvidenceOnly()
    {
        var request = new MemoryProposalEvidencePackageAssembler().Assemble(CreateStagedProposal());
        Assert.AreEqual(1, request.EvidenceReferences.Count);
        Assert.IsFalse(request.EvidenceReferences[0].EvidenceIsApproval);
        Assert.IsFalse(request.EvidenceReferences[0].EvidenceIsPermission);
        Assert.IsFalse(request.EvidenceReferences[0].EvidenceAcceptsMemory);
    }

    [TestMethod]
    public void MemoryProposalEvidencePackageAssembler_CopiesGroundingReferencesAsTraceabilityOnly()
    {
        var request = new MemoryProposalEvidencePackageAssembler().Assemble(CreateStagedProposal());
        Assert.AreEqual(1, request.GroundingReferences.Count);
        Assert.IsFalse(request.GroundingReferences[0].GroundingIsAuthority);
        Assert.IsFalse(request.GroundingReferences[0].GroundingAcceptsMemory);
    }

    [TestMethod]
    public void MemoryProposalEvidencePackageAssembler_CopiesWorkflowReferencesAsContextOnly()
    {
        var request = new MemoryProposalEvidencePackageAssembler().Assemble(CreateStagedProposal());
        Assert.AreEqual(1, request.WorkflowReferences.Count);
        Assert.IsFalse(request.WorkflowReferences[0].WorkflowReferenceAcceptsMemory);
        Assert.IsFalse(request.WorkflowReferences[0].WorkflowReferencePromotesMemory);
    }

    [TestMethod]
    public void MemoryProposalEvidencePackageAssembler_DoesNotMutateStagedProposal()
    {
        var proposal = CreateStagedProposal();
        var beforeKey = proposal.ProposalKey;
        var beforeEvidenceCount = proposal.EvidenceReferences.Count;

        _ = new MemoryProposalEvidencePackageAssembler().Assemble(proposal);

        Assert.AreEqual(beforeKey, proposal.ProposalKey);
        Assert.AreEqual(beforeEvidenceCount, proposal.EvidenceReferences.Count);
        Assert.IsFalse(proposal.PromotesMemory);
        Assert.IsFalse(proposal.IsAcceptedMemory);
    }

    [TestMethod] public void MemoryProposalEvidencePackageAssembler_DoesNotAcceptMemory() => Assert.IsFalse(new MemoryProposalEvidencePackageValidator().Normalize(new MemoryProposalEvidencePackageAssembler().Assemble(CreateStagedProposal())).AcceptsMemory);
    [TestMethod] public void MemoryProposalEvidencePackageAssembler_DoesNotPromoteMemory() => Assert.IsFalse(new MemoryProposalEvidencePackageValidator().Normalize(new MemoryProposalEvidencePackageAssembler().Assemble(CreateStagedProposal())).PromotesMemory);
    [TestMethod] public void MemoryProposalEvidencePackageAssembler_DoesNotActivateRetrieval() => Assert.IsFalse(new MemoryProposalEvidencePackageValidator().Normalize(new MemoryProposalEvidencePackageAssembler().Assemble(CreateStagedProposal())).ActivatesRetrieval);

    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotAddSqlMigration() => AssertNoFileNameContains("Database", "memory_proposal_evidence_package", "evidence_package");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotAddApiEndpoint() => AssertDirectoryDoesNotContain("IronDev.Api", "MemoryProposalEvidencePackage", "memory-proposal-evidence-package");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotAddCliCommand() => AssertDirectoryDoesNotContain("tools", "MemoryProposalEvidencePackage", "memory-proposal-evidence-package");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotAddAcceptedMemoryStore() => AssertCoreFileDoesNotContain("IAcceptedMemoryStore", "AcceptedMemoryStore", "CreateAcceptedMemoryAsync");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotAddPromotionPath() => AssertCoreFileDoesNotContain("IMemoryPromotion", "PromoteMemoryAsync", "PromotionService");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotAddEmbeddingWriter() => AssertCoreFileDoesNotContain("IEmbeddingWriter", "CreateEmbeddingAsync", "EmbeddingClient");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotAddVectorStoreWriter() => AssertCoreFileDoesNotContain("IVectorStoreWriter", "WriteVectorStoreAsync", "VectorStoreClient");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotReferenceWeaviateWrite() => AssertCoreFileDoesNotContain("IWeaviate", "WeaviateClient", "WriteWeaviateAsync");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotReferenceRetrievalActivation() => AssertCoreFileDoesNotContain("IRetrievalActivation", "ActivateRetrievalAsync", "RetrievalActivationService");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotReferenceModelClient() => AssertCoreFileDoesNotContain("IAgentModelAdapter", "IModelClient", "OpenAI", "ChatCompletion");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotReferenceSourceApply() => AssertCoreFileDoesNotContain("ApplySource", "SourceApplyService", "MutateSourceAsync");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotReferenceWorkflowRunner() => AssertCoreFileDoesNotContain("WorkflowRunner", "ContinueWorkflowAsync", "DispatchWorkflow");
    [TestMethod] public void MemoryProposalEvidencePackage_DoesNotReferenceAgentDispatcher() => AssertCoreFileDoesNotContain("AgentDispatcher", "DispatchAgent", "IAgentRuntime");

    private static MemoryProposalEvidencePackageCreateRequest CreateRequest(
        Guid? memoryProposalId = null,
        Guid? projectId = null,
        string packageKey = "proposal-108.package",
        MemoryProposalEvidencePackageStatus status = MemoryProposalEvidencePackageStatus.ReadyForReview,
        MemoryProposalEvidencePackagePurpose purpose = MemoryProposalEvidencePackagePurpose.HumanReview,
        string safeMemory = "Repeated workflow failure should be reviewed as a possible project-local memory.",
        bool acceptsMemory = false,
        IReadOnlyList<MemoryProposalPackageEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<MemoryProposalPackageGroundingReference>? groundingReferences = null,
        IReadOnlyList<MemoryProposalPackageWorkflowReference>? workflowReferences = null,
        IReadOnlyList<MemoryProposalPackageReviewNote>? reviewNotes = null) => new()
    {
        MemoryProposalEvidencePackageId = PackageId,
        MemoryProposalId = memoryProposalId ?? ProposalId,
        ProjectId = projectId ?? ProjectId,
        PackageKey = packageKey,
        Status = status,
        Purpose = purpose,
        ProposalType = MemoryProposalType.FailureModeCandidate.ToString(),
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate.ToString(),
        ProposalStatus = MemoryProposalStatus.Staged.ToString(),
        SafeProposedMemory = safeMemory,
        SafeRationaleSummary = "Evidence package gathers safe context for review.",
        SafeRiskSummary = "Human review remains required before any memory decision.",
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential.ToString(),
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview.ToString(),
        EvidenceReferences = evidenceReferences ?? new[] { Evidence() },
        GroundingReferences = groundingReferences ?? new[] { Grounding() },
        WorkflowReferences = workflowReferences ?? new[] { Workflow() },
        ReviewNotes = reviewNotes ?? new[] { ReviewNote("HumanReviewNeeded") },
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CorrelationId = Guid.Parse("80000000-eeee-4444-8888-990000000108"),
        CausationId = Guid.Parse("90000000-eeee-4444-8888-990000000108"),
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-evidence-package-tests",
        MetadataVersion = 1,
        MetadataJson = "{\"source\":\"pr108-test\"}",
        AcceptsMemory = acceptsMemory
    };

    private static MemoryProposalPackageEvidenceReference Evidence(bool evidenceIsApproval = false, bool evidenceIsPermission = false, bool evidenceAcceptsMemory = false) => new()
    {
        EvidenceType = "WorkflowRun",
        EvidenceId = WorkflowRunId.ToString(),
        EvidenceLabel = "Workflow run evidence",
        SafeSummary = "Workflow run evidence supports review of the staged proposal.",
        AllowedUse = "Review",
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        EvidenceIsApproval = evidenceIsApproval,
        EvidenceIsPermission = evidenceIsPermission,
        EvidenceAcceptsMemory = evidenceAcceptsMemory
    };

    private static MemoryProposalPackageGroundingReference Grounding(bool groundingIsAuthority = false, bool groundingAcceptsMemory = false) => new()
    {
        GroundingEvidenceReferenceId = GroundingReferenceId,
        ClaimType = "MemoryProposalTrace",
        ClaimId = "claim-108",
        SafeSummary = "Grounding reference links the staged proposal to review evidence.",
        GroundingIsAuthority = groundingIsAuthority,
        GroundingAcceptsMemory = groundingAcceptsMemory
    };

    private static MemoryProposalPackageWorkflowReference Workflow(bool workflowReferenceAcceptsMemory = false, bool workflowReferencePromotesMemory = false) => new()
    {
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        ReferenceType = "GeneratedFrom",
        SafeSummary = "Workflow facts provide context for the package.",
        WorkflowReferenceAcceptsMemory = workflowReferenceAcceptsMemory,
        WorkflowReferencePromotesMemory = workflowReferencePromotesMemory
    };

    private static MemoryProposalPackageReviewNote ReviewNote(string noteType, string summary = "Human review remains required before any memory decision.", bool noteRejectsMemory = false) => new()
    {
        NoteType = noteType,
        SafeSummary = summary,
        Severity = "warning",
        NoteRejectsMemory = noteRejectsMemory
    };

    private static MemoryProposal CreateStagedProposal() => new()
    {
        MemoryProposalId = ProposalId,
        ProjectId = ProjectId,
        ProposalKey = "proposal-108",
        ProposalType = MemoryProposalType.FailureModeCandidate,
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate,
        ProposalStatus = MemoryProposalStatus.Staged,
        SourceType = "ManualRealRunMemoryImprovement",
        SourceId = "run-108",
        SafeProposedMemory = "Repeated workflow failure should be reviewed as a possible project-local memory.",
        SafeRationaleSummary = "The same failure appeared in multiple governed runs.",
        SafeRiskSummary = "The proposal needs review before any memory decision.",
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential,
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-evidence-package-tests",
        MetadataVersion = 1,
        MetadataJson = "{}",
        EvidenceReferences = new[]
        {
            new MemoryProposalEvidenceReference
            {
                MemoryProposalEvidenceReferenceId = Guid.Parse("a0000000-eeee-4444-8888-990000000108"),
                MemoryProposalId = ProposalId,
                ProjectId = ProjectId,
                EvidenceType = MemoryProposalEvidenceType.WorkflowRun,
                EvidenceId = WorkflowRunId.ToString(),
                EvidenceLabel = "Workflow run evidence",
                SafeSummary = "Workflow evidence supports review.",
                AllowedUse = MemoryProposalEvidenceAllowedUse.MemoryProposalReview,
                WorkflowRunStepId = WorkflowRunStepId,
                WorkflowCheckpointId = WorkflowCheckpointId
            }
        },
        GroundingReferences = new[]
        {
            new MemoryProposalGroundingReference
            {
                MemoryProposalGroundingReferenceId = Guid.Parse("b0000000-eeee-4444-8888-990000000108"),
                MemoryProposalId = ProposalId,
                ProjectId = ProjectId,
                GroundingReferenceId = GroundingReferenceId,
                ClaimType = MemoryProposalGroundingClaimType.MemoryProposalTrace,
                ClaimId = "claim-108",
                SafeSummary = "Grounding evidence is traceability only."
            }
        },
        WorkflowReferences = new[]
        {
            new MemoryProposalWorkflowReference
            {
                MemoryProposalWorkflowReferenceId = Guid.Parse("c0000000-eeee-4444-8888-990000000108"),
                MemoryProposalId = ProposalId,
                ProjectId = ProjectId,
                WorkflowRunId = WorkflowRunId,
                WorkflowRunStepId = WorkflowRunStepId,
                WorkflowCheckpointId = WorkflowCheckpointId,
                ReferenceType = MemoryProposalWorkflowReferenceType.GeneratedFrom,
                SafeSummary = "Workflow reference is context only."
            }
        }
    };

    private static void AssertInvalid(MemoryProposalEvidencePackageCreateRequest request, string expectedCode)
    {
        AssertHasIssue(new MemoryProposalEvidencePackageValidator().Validate(request), expectedCode);
    }

    private static void AssertHasIssue(MemoryProposalValidationResult result, string expectedCode)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), "Expected issue code: " + expectedCode + ". Actual: " + string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    private static void AssertPackageHasNoAuthority(MemoryProposalEvidencePackage package)
    {
        Assert.IsFalse(package.GrantsApproval);
        Assert.IsFalse(package.GrantsExecution);
        Assert.IsFalse(package.AcceptsMemory);
        Assert.IsFalse(package.RejectsMemory);
        Assert.IsFalse(package.PromotesMemory);
        Assert.IsFalse(package.CreatesAcceptedMemory);
        Assert.IsFalse(package.CreatesPortableMemory);
        Assert.IsFalse(package.ActivatesRetrieval);
        Assert.IsFalse(package.CreatesEmbedding);
        Assert.IsFalse(package.WritesVectorStore);
        Assert.IsFalse(package.MutatesSource);
        Assert.IsFalse(package.SatisfiesPolicy);
        Assert.IsFalse(package.TransfersAuthority);
        Assert.IsFalse(package.ApprovesRelease);
    }

    private static void AssertCoreFileDoesNotContain(params string[] forbiddenTokens)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IronDev.Core", "AgentMemory", "MemoryProposalEvidencePackageModels.cs"));
        AssertNoForbiddenTokens(text, forbiddenTokens);
    }

    private static void AssertDirectoryDoesNotContain(string directory, params string[] forbiddenTokens)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", directory);
        if (!Directory.Exists(root)) return;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        var text = string.Join("\n", files.Select(File.ReadAllText));
        AssertNoForbiddenTokens(text, forbiddenTokens);
    }

    private static void AssertNoFileNameContains(string directory, params string[] forbiddenTokens)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", directory);
        var text = string.Join("\n", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(Path.GetFileName));
        AssertNoForbiddenTokens(text, forbiddenTokens);
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbiddenTokens)
    {
        foreach (var token in forbiddenTokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), "Forbidden token '" + token + "' was present.");
        }
    }
}

