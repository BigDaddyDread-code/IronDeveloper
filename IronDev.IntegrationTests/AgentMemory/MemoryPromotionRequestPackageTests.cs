using IronDev.Core.AgentMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("MemoryPromotionRequestPackage")]
public sealed class MemoryPromotionRequestPackageTests
{
    private static readonly Guid ProjectId = Id(113);
    private static readonly Guid ProposalId = Id(114);
    private static readonly Guid EvidencePackageId = Id(115);
    private static readonly Guid PromotionPackageId = Id(116);
    private static readonly Guid WorkflowRunId = Id(117);
    private static readonly Guid WorkflowRunStepId = Id(118);
    private static readonly Guid WorkflowCheckpointId = Id(119);
    private static readonly Guid GroundingReferenceId = Id(120);
    private static readonly Guid DuplicateCandidateId = Id(121);
    private static readonly Guid StaleCandidateId = Id(122);
    private static readonly Guid ConflictCandidateId = Id(123);
    private static readonly Guid PatternCandidateId = Id(124);

    [TestMethod]
    public void MemoryPromotionRequestPackage_ExposesExpectedShape()
    {
        var package = new MemoryPromotionRequestPackageValidator().Normalize(CreateRequest());

        Assert.AreEqual(PromotionPackageId, package.MemoryPromotionRequestPackageId);
        Assert.AreEqual(ProjectId, package.ProjectId);
        Assert.AreEqual(ProposalId, package.MemoryProposalId);
        Assert.AreEqual(MemoryPromotionRequestPackageStatus.ReadyForReview, package.Status);
        Assert.AreEqual(MemoryPromotionRequestPurpose.HumanPromotionReview, package.Purpose);
        Assert.AreEqual(MemoryPromotionRequestedTargetMemoryScope.ProjectLocalCandidateForPromotion, package.RequestedTargetMemoryScope);
        Assert.AreEqual(1, package.EvidenceReferences.Count);
        Assert.AreEqual(1, package.GroundingReferences.Count);
        Assert.AreEqual(1, package.SignalReferences.Count);
        Assert.AreEqual(1, package.ApprovalRequirementReferences.Count);
        Assert.AreEqual(1, package.ReviewNotes.Count);
        AssertPackageHasNoAuthority(package);
    }

    [TestMethod]
    public void MemoryPromotionRequestPackage_UsesBoundedStatuses()
    {
        var names = Enum.GetNames<MemoryPromotionRequestPackageStatus>();
        CollectionAssert.AreEquivalent(new[] { "Assembled", "ReadyForReview", "NeedsEvidence", "NeedsHumanReview", "RequiresApprovalReview", "RequiresSanitizationReview", "ContainsDuplicateRisk", "ContainsStaleRisk", "ContainsConflictRisk", "Quarantined", "Superseded", "Withdrawn" }, names);
        AssertNoForbiddenTokens(string.Join("\n", names), "Approved", "Accepted", "Promoted", "Rejected", "Active", "Indexed", "Embedded", "Retrievable", "PolicySatisfied", "ApprovalSatisfied", "AuthorityTransferred", "ReleaseApproved");
    }

    [TestMethod]
    public void MemoryPromotionRequestPackage_UsesBoundedPurposes()
    {
        var names = Enum.GetNames<MemoryPromotionRequestPurpose>();
        CollectionAssert.AreEquivalent(new[] { "HumanPromotionReview", "GovernedPromotionReview", "ProjectMemoryReview", "AgentLocalMemoryReview", "PortableEngineeringMemoryReview", "SanitizationReview", "RiskReview", "EvidenceCompletenessReview" }, names);
        AssertNoForbiddenTokens(string.Join("\n", names), "AcceptMemory", "ApproveMemory", "PromoteMemory", "RejectMemory", "ActivateRetrieval", "CreateEmbedding", "WriteVectorStore", "SatisfyPolicy", "TransferAuthority", "ApproveRelease");
    }

    [TestMethod]
    public void MemoryPromotionRequestPackage_UsesBoundedTargetScopes()
    {
        var names = Enum.GetNames<MemoryPromotionRequestedTargetMemoryScope>();
        CollectionAssert.AreEquivalent(new[] { "ProjectLocalCandidateForPromotion", "AgentLocalCandidateForPromotion", "PortableEngineeringMemoryCandidateForPromotion", "RequiresTriage" }, names);
        AssertNoForbiddenTokens(string.Join("\n", names), "Accepted", "Global", "CrossProject", "RetrievalActive", "PolicyActive", "AuthorityActive");
    }

    [TestMethod] public void MemoryPromotionRequestPackage_AuthorityFlagsAreFalse() => AssertPackageHasNoAuthority(new MemoryPromotionRequestPackageValidator().Normalize(CreateRequest()));
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsEmptyProjectId() => AssertInvalid(CreateRequest(projectId: Guid.Empty), "project_id_required");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsEmptyMemoryProposalId() => AssertInvalid(CreateRequest(memoryProposalId: Guid.Empty), "memory_proposal_id_required");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsBlankPromotionRequestKey() => AssertInvalid(CreateRequest(promotionRequestKey: " "), "promotion_request_key_required");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsForbiddenStatus() => AssertInvalid(CreateRequest(status: (MemoryPromotionRequestPackageStatus)999), "status_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsForbiddenPurpose() => AssertInvalid(CreateRequest(purpose: (MemoryPromotionRequestPurpose)999), "purpose_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsForbiddenTargetScope() => AssertInvalid(CreateRequest(targetScope: (MemoryPromotionRequestedTargetMemoryScope)999), "target_scope_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsHiddenReasoning() => AssertInvalid(CreateRequest(safeMemory: "hiddenReasoning leaked"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsRawPrompt() => AssertInvalid(CreateRequest(safeMemory: "rawPrompt leaked"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsRawCompletion() => AssertInvalid(CreateRequest(safeMemory: "rawCompletion leaked"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsRawToolOutput() => AssertInvalid(CreateRequest(safeMemory: "rawToolOutput leaked"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsEntirePatch() => AssertInvalid(CreateRequest(safeMemory: "entirePatch leaked"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsAcceptedMemoryLanguage() => AssertInvalid(CreateRequest(safeMemory: "accepted memory"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsPromotedMemoryLanguage() => AssertInvalid(CreateRequest(safeMemory: "promoted memory"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsApprovalDecisionLanguage() => AssertInvalid(CreateRequest(reviewerInstructions: "approval granted"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsPolicySatisfiedLanguage() => AssertInvalid(CreateRequest(reviewerInstructions: "policy satisfied"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsRetrievalActiveLanguage() => AssertInvalid(CreateRequest(reviewerInstructions: "retrieval active"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsEmbeddingLanguage() => AssertInvalid(CreateRequest(reviewerInstructions: "embedding created"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsVectorWriteLanguage() => AssertInvalid(CreateRequest(reviewerInstructions: "vector write"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsPortableApprovedLanguage() => AssertInvalid(CreateRequest(reviewerInstructions: "portable memory approved"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsTruthConfirmedLanguage() => AssertInvalid(CreateRequest(reviewerInstructions: "truth confirmed"), "promotion_request_package_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackageValidator_RejectsTrueAuthorityFlags() => AssertInvalid(CreateRequest(acceptsMemory: true), "authority_flags_forbidden");

    [TestMethod] public void MemoryPromotionRequestPackage_EvidenceReferenceIsNotDecision() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceIsDecision: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_EvidenceReferenceDoesNotGrantApproval() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceGrantsApproval: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_EvidenceReferenceDoesNotSatisfyPolicy() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceSatisfiesPolicy: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_EvidenceReferenceDoesNotAcceptMemory() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceAcceptsMemory: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_EvidenceReferenceDoesNotPromoteMemory() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidencePromotesMemory: true) }), "evidence_authority_forbidden");

    [TestMethod] public void MemoryPromotionRequestPackage_GroundingReferenceIsNotAuthority() => AssertInvalid(CreateRequest(groundingReferences: new[] { Grounding(groundingIsAuthority: true) }), "grounding_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_GroundingReferenceDoesNotAcceptMemory() => AssertInvalid(CreateRequest(groundingReferences: new[] { Grounding(groundingAcceptsMemory: true) }), "grounding_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_GroundingReferenceDoesNotPromoteMemory() => AssertInvalid(CreateRequest(groundingReferences: new[] { Grounding(groundingPromotesMemory: true) }), "grounding_authority_forbidden");

    [TestMethod] public void MemoryPromotionRequestPackage_DuplicateSignalDoesNotBlockOrAllowPromotion() => AssertInvalid(CreateRequest(signalReferences: new[] { Signal("DuplicateCandidate", signalBlocksPromotion: true) }), "signal_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_StaleSignalDoesNotBlockOrAllowPromotion() => AssertInvalid(CreateRequest(signalReferences: new[] { Signal("StaleCandidate", signalAllowsPromotion: true) }), "signal_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_ConflictSignalDoesNotBlockOrAllowPromotion() => AssertInvalid(CreateRequest(signalReferences: new[] { Signal("ConflictCandidate", signalBlocksPromotion: true) }), "signal_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_PatternSignalDoesNotBlockOrAllowPromotion() => AssertInvalid(CreateRequest(signalReferences: new[] { Signal("CrossRunPatternCandidate", signalAllowsPromotion: true) }), "signal_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_RejectsForbiddenSignalType() => AssertInvalid(CreateRequest(signalReferences: new[] { Signal("PromotionApproved") }), "signal_type_forbidden");

    [TestMethod] public void MemoryPromotionRequestPackage_ApprovalRequirementIsNotApproval() => AssertInvalid(CreateRequest(approvalRequirementReferences: new[] { ApprovalRequirement(requirementIsApproval: true) }), "approval_requirement_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_ApprovalRequirementDoesNotSatisfyPolicy() => AssertInvalid(CreateRequest(approvalRequirementReferences: new[] { ApprovalRequirement(requirementSatisfiesPolicy: true) }), "approval_requirement_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_ApprovalRequirementDoesNotAllowPromotion() => AssertInvalid(CreateRequest(approvalRequirementReferences: new[] { ApprovalRequirement(requirementAllowsPromotion: true) }), "approval_requirement_authority_forbidden");

    [TestMethod] public void MemoryPromotionRequestPackage_RiskNoteDoesNotRejectMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("DuplicateRisk", noteRejectsMemory: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_SanitizationNoteDoesNotApprovePortableMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("SanitizationNeeded", summary: "portable memory approved") }), "review_note_text_unsafe");
    [TestMethod] public void MemoryPromotionRequestPackage_PromotionRationaleDoesNotPromoteMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("PromotionRationale", notePromotesMemory: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_RejectsApprovalDecisionNote() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("ApprovalDecision") }), "review_note_type_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_RejectsAcceptanceDecisionNote() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("AcceptanceDecision") }), "review_note_type_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_RejectsRejectionDecisionNote() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("RejectionDecision") }), "review_note_type_forbidden");
    [TestMethod] public void MemoryPromotionRequestPackage_RejectsPromotionDecisionNote() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote("PromotionDecision") }), "review_note_type_forbidden");

    [TestMethod]
    public void MemoryPromotionRequestPackageAssembler_AssemblesFromStagedProposalAndEvidencePackage()
    {
        var package = Assemble();

        Assert.AreEqual(ProjectId, package.ProjectId);
        Assert.AreEqual(ProposalId, package.MemoryProposalId);
        Assert.AreEqual(MemoryPromotionRequestPurpose.ProjectMemoryReview, package.Purpose);
        Assert.AreEqual(MemoryPromotionRequestedTargetMemoryScope.ProjectLocalCandidateForPromotion, package.RequestedTargetMemoryScope);
        Assert.IsTrue(package.EvidenceReferences.Count >= 2);
        Assert.IsTrue(package.ApprovalRequirementReferences.Count >= 2);
        AssertPackageHasNoAuthority(package);
    }

    [TestMethod] public void MemoryPromotionRequestPackageAssembler_IncludesDuplicateSignalsAsReviewOnly() => AssertSignalReviewOnly(Assemble(duplicates: new[] { DuplicateCandidate() }), "DuplicateCandidate");
    [TestMethod] public void MemoryPromotionRequestPackageAssembler_IncludesStaleSignalsAsReviewOnly() => AssertSignalReviewOnly(Assemble(stale: new[] { StaleCandidate() }), "StaleCandidate");
    [TestMethod] public void MemoryPromotionRequestPackageAssembler_IncludesConflictSignalsAsReviewOnly() => AssertSignalReviewOnly(Assemble(conflicts: new[] { ConflictCandidate() }), "ConflictCandidate");
    [TestMethod] public void MemoryPromotionRequestPackageAssembler_IncludesCrossRunPatternSignalsAsReviewOnly() => AssertSignalReviewOnly(Assemble(patterns: new[] { PatternCandidate() }), "CrossRunPatternCandidate");

    [TestMethod]
    public void MemoryPromotionRequestPackageAssembler_DoesNotMutateStagedProposal()
    {
        var proposal = StagedProposal();
        var beforeStatus = proposal.ProposalStatus;
        var beforeText = proposal.SafeProposedMemory;

        _ = new MemoryPromotionRequestPackageAssembler().Assemble(proposal, EvidencePackage());

        Assert.AreEqual(beforeStatus, proposal.ProposalStatus);
        Assert.AreEqual(beforeText, proposal.SafeProposedMemory);
        Assert.IsFalse(proposal.IsAcceptedMemory);
        Assert.IsFalse(proposal.PromotesMemory);
        Assert.IsFalse(proposal.WritesVectorIndex);
    }

    [TestMethod] public void MemoryPromotionRequestPackageAssembler_DoesNotAcceptMemory() => Assert.IsFalse(Assemble().AcceptsMemory);
    [TestMethod] public void MemoryPromotionRequestPackageAssembler_DoesNotRejectMemory() => Assert.IsFalse(Assemble().RejectsMemory);
    [TestMethod] public void MemoryPromotionRequestPackageAssembler_DoesNotPromoteMemory() => Assert.IsFalse(Assemble().PromotesMemory);
    [TestMethod] public void MemoryPromotionRequestPackageAssembler_DoesNotActivateRetrieval() => Assert.IsFalse(Assemble().ActivatesRetrieval);

    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotAddSqlMigration() => AssertNoFileNameContains("Database", "memory_promotion_request", "promotion_request_package");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotAddApiEndpoint() => AssertDirectoryDoesNotContain("IronDev.Api", "MemoryPromotionRequestPackage", "memory-promotion-request");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotAddCliCommand() => AssertDirectoryDoesNotContain("tools", "MemoryPromotionRequestPackage", "memory-promotion-request");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotAddAcceptedMemoryStore() => AssertCoreFileDoesNotContain("IAcceptedMemoryStore", "AcceptedMemoryStore", "CreateAcceptedMemoryAsync");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotAddPromotionPath() => AssertCoreFileDoesNotContain("IMemoryPromotion", "PromoteMemoryAsync", "PromotionService");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotAddEmbeddingWriter() => AssertCoreFileDoesNotContain("IEmbeddingWriter", "CreateEmbeddingAsync", "EmbeddingClient");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotAddVectorStoreWriter() => AssertCoreFileDoesNotContain("IVectorStoreWriter", "WriteVectorStoreAsync", "VectorStoreClient");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotReferenceWeaviateWrite() => AssertCoreFileDoesNotContain("IWeaviate", "WeaviateClient", "WriteWeaviateAsync");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotReferenceRetrievalActivation() => AssertCoreFileDoesNotContain("IRetrievalActivation", "ActivateRetrievalAsync", "RetrievalActivationService");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotReferenceModelClient() => AssertCoreFileDoesNotContain("IAgentModelAdapter", "IModelClient", "OpenAI", "ChatCompletion");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotReferenceSourceApply() => AssertCoreFileDoesNotContain("ApplySource", "SourceApplyService", "MutateSourceAsync");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotReferenceWorkflowRunner() => AssertCoreFileDoesNotContain("WorkflowRunner", "ContinueWorkflowAsync", "DispatchWorkflow");
    [TestMethod] public void MemoryPromotionRequestPackage_DoesNotReferenceAgentDispatcher() => AssertCoreFileDoesNotContain("AgentDispatcher", "DispatchAgent", "IAgentRuntime");

    private static MemoryPromotionRequestPackage Assemble(
        IReadOnlyList<MemoryProposalDuplicateCandidate>? duplicates = null,
        IReadOnlyList<MemoryProposalStaleCandidate>? stale = null,
        IReadOnlyList<MemoryProposalConflictCandidate>? conflicts = null,
        IReadOnlyList<CrossRunMemoryPatternCandidate>? patterns = null) =>
        new MemoryPromotionRequestPackageValidator().Normalize(new MemoryPromotionRequestPackageAssembler().Assemble(StagedProposal(), EvidencePackage(), duplicates, stale, conflicts, patterns));

    private static MemoryPromotionRequestPackageCreateRequest CreateRequest(
        Guid? projectId = null,
        Guid? memoryProposalId = null,
        string promotionRequestKey = "proposal-113.promotion-review",
        MemoryPromotionRequestPackageStatus status = MemoryPromotionRequestPackageStatus.ReadyForReview,
        MemoryPromotionRequestPurpose purpose = MemoryPromotionRequestPurpose.HumanPromotionReview,
        MemoryPromotionRequestedTargetMemoryScope targetScope = MemoryPromotionRequestedTargetMemoryScope.ProjectLocalCandidateForPromotion,
        string safeMemory = "Repeated workflow failure should be reviewed as a possible project-local memory.",
        string? reviewerInstructions = "Human review remains required before any later memory action.",
        bool acceptsMemory = false,
        IReadOnlyList<MemoryPromotionRequestEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<MemoryPromotionRequestGroundingReference>? groundingReferences = null,
        IReadOnlyList<MemoryPromotionRequestSignalReference>? signalReferences = null,
        IReadOnlyList<MemoryPromotionApprovalRequirementReference>? approvalRequirementReferences = null,
        IReadOnlyList<MemoryPromotionRequestReviewNote>? reviewNotes = null) => new()
    {
        MemoryPromotionRequestPackageId = PromotionPackageId,
        ProjectId = projectId ?? ProjectId,
        MemoryProposalId = memoryProposalId ?? ProposalId,
        PromotionRequestKey = promotionRequestKey,
        Status = status,
        Purpose = purpose,
        ProposalType = MemoryProposalType.FailureModeCandidate.ToString(),
        CurrentProposalStatus = MemoryProposalStatus.ReadyForReview.ToString(),
        RequestedTargetMemoryScope = targetScope,
        SafeProposedMemory = safeMemory,
        SafePromotionRationale = "Package gathers staged proposal material for governed promotion review only.",
        SafeRiskSummary = "Human review remains required before any later memory action.",
        SafeSanitizationSummary = "Sanitization status must be reviewed before any later memory action.",
        SafeReviewerInstructions = reviewerInstructions,
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential.ToString(),
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview.ToString(),
        EvidenceReferences = evidenceReferences ?? new[] { Evidence() },
        GroundingReferences = groundingReferences ?? new[] { Grounding() },
        SignalReferences = signalReferences ?? new[] { Signal("EvidenceGap") },
        ApprovalRequirementReferences = approvalRequirementReferences ?? new[] { ApprovalRequirement() },
        ReviewNotes = reviewNotes ?? new[] { ReviewNote("HumanReviewNeeded") },
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CorrelationId = Id(125),
        CausationId = Id(126),
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-promotion-request-package-tests",
        MetadataVersion = 1,
        MetadataJson = "{\"source\":\"pr113-test\"}",
        AcceptsMemory = acceptsMemory
    };

    private static MemoryPromotionRequestEvidenceReference Evidence(bool evidenceIsDecision = false, bool evidenceGrantsApproval = false, bool evidenceSatisfiesPolicy = false, bool evidenceAcceptsMemory = false, bool evidencePromotesMemory = false) => new()
    {
        EvidenceType = "MemoryProposalEvidencePackage",
        EvidenceId = EvidencePackageId.ToString(),
        EvidenceLabel = "Evidence package",
        SafeSummary = "Evidence package supports promotion review only.",
        AllowedUse = "PromotionReview",
        MemoryProposalId = ProposalId,
        MemoryProposalEvidencePackageId = EvidencePackageId,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        EvidenceIsDecision = evidenceIsDecision,
        EvidenceGrantsApproval = evidenceGrantsApproval,
        EvidenceSatisfiesPolicy = evidenceSatisfiesPolicy,
        EvidenceAcceptsMemory = evidenceAcceptsMemory,
        EvidencePromotesMemory = evidencePromotesMemory
    };

    private static MemoryPromotionRequestGroundingReference Grounding(bool groundingIsAuthority = false, bool groundingAcceptsMemory = false, bool groundingPromotesMemory = false) => new()
    {
        GroundingEvidenceReferenceId = GroundingReferenceId,
        ClaimType = "EvidenceSupport",
        ClaimId = "claim-113",
        SafeSummary = "Grounding supports traceability only.",
        GroundingIsAuthority = groundingIsAuthority,
        GroundingAcceptsMemory = groundingAcceptsMemory,
        GroundingPromotesMemory = groundingPromotesMemory
    };

    private static MemoryPromotionRequestSignalReference Signal(string signalType, bool signalBlocksPromotion = false, bool signalAllowsPromotion = false) => new()
    {
        SignalType = signalType,
        SignalId = DuplicateCandidateId,
        SafeSummary = "Signal supports review only.",
        Severity = "warning",
        SignalBlocksPromotion = signalBlocksPromotion,
        SignalAllowsPromotion = signalAllowsPromotion
    };

    private static MemoryPromotionApprovalRequirementReference ApprovalRequirement(bool requirementIsApproval = false, bool requirementSatisfiesPolicy = false, bool requirementAllowsPromotion = false) => new()
    {
        RequirementType = "HumanReview",
        RequirementId = "human-review-113",
        SafeSummary = "Human review is required later.",
        RequirementIsApproval = requirementIsApproval,
        RequirementSatisfiesPolicy = requirementSatisfiesPolicy,
        RequirementAllowsPromotion = requirementAllowsPromotion
    };

    private static MemoryPromotionRequestReviewNote ReviewNote(string noteType, string summary = "Human review remains required before any later memory action.", bool noteRejectsMemory = false, bool notePromotesMemory = false) => new()
    {
        NoteType = noteType,
        SafeSummary = summary,
        Severity = "warning",
        NoteRejectsMemory = noteRejectsMemory,
        NotePromotesMemory = notePromotesMemory
    };

    private static MemoryProposal StagedProposal() => new()
    {
        MemoryProposalId = ProposalId,
        ProjectId = ProjectId,
        ProposalKey = "proposal-113",
        ProposalType = MemoryProposalType.FailureModeCandidate,
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate,
        ProposalStatus = MemoryProposalStatus.ReadyForReview,
        SourceType = "system_test_fixture",
        SubjectType = "failure-mode",
        SubjectId = "sql-reset-order",
        SafeProposedMemory = "SQL reset failure should be reviewed as a recurring project-local memory candidate.",
        SafeRationaleSummary = "Evidence package and signals can support later governed review.",
        SafeRiskSummary = "Signals are advisory only.",
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential,
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-promotion-request-package-tests",
        MetadataVersion = 1,
        MetadataJson = "{}",
        EvidenceReferences = new[] { new MemoryProposalEvidenceReference { MemoryProposalEvidenceReferenceId = Id(127), MemoryProposalId = ProposalId, ProjectId = ProjectId, EvidenceType = MemoryProposalEvidenceType.WorkflowRun, EvidenceId = WorkflowRunId.ToString(), SafeSummary = "Workflow run evidence supports review.", AllowedUse = MemoryProposalEvidenceAllowedUse.MemoryProposalReview } },
        GroundingReferences = new[] { new MemoryProposalGroundingReference { MemoryProposalGroundingReferenceId = Id(128), MemoryProposalId = ProposalId, ProjectId = ProjectId, GroundingReferenceId = GroundingReferenceId, ClaimType = MemoryProposalGroundingClaimType.EvidenceSupport, ClaimId = "claim-113", SafeSummary = "Grounding supports traceability." } },
        WorkflowReferences = new[] { new MemoryProposalWorkflowReference { MemoryProposalWorkflowReferenceId = Id(129), MemoryProposalId = ProposalId, ProjectId = ProjectId, WorkflowRunId = WorkflowRunId, WorkflowRunStepId = WorkflowRunStepId, WorkflowCheckpointId = WorkflowCheckpointId, ReferenceType = MemoryProposalWorkflowReferenceType.Traceability, SafeSummary = "Workflow reference supports traceability." } },
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static MemoryProposalEvidencePackage EvidencePackage() => new()
    {
        MemoryProposalEvidencePackageId = EvidencePackageId,
        MemoryProposalId = ProposalId,
        ProjectId = ProjectId,
        PackageKey = "proposal-113.evidence-package",
        Status = MemoryProposalEvidencePackageStatus.ReadyForReview,
        Purpose = MemoryProposalEvidencePackagePurpose.HumanReview,
        ProposalType = MemoryProposalType.FailureModeCandidate.ToString(),
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate.ToString(),
        ProposalStatus = MemoryProposalStatus.ReadyForReview.ToString(),
        SafeProposedMemory = "SQL reset failure should be reviewed.",
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential.ToString(),
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview.ToString(),
        EvidenceReferences = new[] { new MemoryProposalPackageEvidenceReference { EvidenceType = "WorkflowRun", EvidenceId = WorkflowRunId.ToString(), SafeSummary = "Workflow evidence supports review.", AllowedUse = "Review" } },
        GroundingReferences = new[] { new MemoryProposalPackageGroundingReference { GroundingEvidenceReferenceId = GroundingReferenceId, ClaimType = "EvidenceSupport", ClaimId = "claim-113", SafeSummary = "Grounding supports traceability." } },
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-promotion-request-package-tests",
        MetadataVersion = 1,
        MetadataJson = "{}",
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static MemoryProposalDuplicateCandidate DuplicateCandidate() => new() { MemoryProposalDuplicateCandidateId = DuplicateCandidateId, SafeReasonSummary = "Duplicate signal needs review." };
    private static MemoryProposalStaleCandidate StaleCandidate() => new() { MemoryProposalStaleCandidateId = StaleCandidateId, SafeReasonSummary = "Stale signal needs review." };
    private static MemoryProposalConflictCandidate ConflictCandidate() => new() { MemoryProposalConflictCandidateId = ConflictCandidateId, SafeConflictSummary = "Conflict signal needs review." };
    private static CrossRunMemoryPatternCandidate PatternCandidate() => new() { CrossRunMemoryPatternCandidateId = PatternCandidateId, SafePatternSummary = "Cross-run pattern signal needs review." };

    private static void AssertSignalReviewOnly(MemoryPromotionRequestPackage package, string signalType)
    {
        var signal = package.SignalReferences.Single(item => item.SignalType == signalType);
        Assert.IsFalse(signal.SignalIsDecision);
        Assert.IsFalse(signal.SignalBlocksPromotion);
        Assert.IsFalse(signal.SignalAllowsPromotion);
        Assert.IsFalse(signal.SignalAcceptsMemory);
        Assert.IsFalse(signal.SignalPromotesMemory);
    }

    private static void AssertPackageHasNoAuthority(MemoryPromotionRequestPackage package)
    {
        Assert.IsFalse(package.IsDecision);
        Assert.IsFalse(package.GrantsApproval);
        Assert.IsFalse(package.SatisfiesPolicy);
        Assert.IsFalse(package.AcceptsMemory);
        Assert.IsFalse(package.RejectsMemory);
        Assert.IsFalse(package.PromotesMemory);
        Assert.IsFalse(package.CreatesAcceptedMemory);
        Assert.IsFalse(package.CreatesPortableMemory);
        Assert.IsFalse(package.ActivatesRetrieval);
        Assert.IsFalse(package.CreatesEmbedding);
        Assert.IsFalse(package.WritesVectorStore);
        Assert.IsFalse(package.TransfersAuthority);
        Assert.IsFalse(package.MutatesSource);
        Assert.IsFalse(package.ApprovesRelease);
    }

    private static void AssertInvalid(MemoryPromotionRequestPackageCreateRequest request, string expectedCode)
    {
        var result = new MemoryPromotionRequestPackageValidator().Validate(request);
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), $"Expected {expectedCode}; actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token present: {token}");
        }
    }

    private static void AssertNoFileNameContains(string directory, params string[] tokens)
    {
        var root = FindRepoRoot();
        var fullDirectory = Path.Combine(root, directory);
        var files = Directory.Exists(fullDirectory) ? Directory.GetFiles(fullDirectory, "*", SearchOption.AllDirectories) : Array.Empty<string>();
        foreach (var token in tokens)
        {
            Assert.IsFalse(files.Any(file => Path.GetFileName(file).Contains(token, StringComparison.OrdinalIgnoreCase)), $"Unexpected file for token {token}.");
        }
    }

    private static void AssertDirectoryDoesNotContain(string directory, params string[] tokens)
    {
        var root = FindRepoRoot();
        var fullDirectory = Path.Combine(root, directory);
        if (!Directory.Exists(fullDirectory)) return;
        var text = string.Join("\n", Directory.GetFiles(fullDirectory, "*", SearchOption.AllDirectories).Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)).Select(File.ReadAllText));
        AssertNoForbiddenTokens(text, tokens);
    }

    private static void AssertCoreFileDoesNotContain(params string[] tokens)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "IronDev.Core", "AgentMemory", "MemoryPromotionRequestPackageModels.cs");
        var text = File.ReadAllText(path);
        AssertNoForbiddenTokens(text, tokens);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "Could not find repository root.");
        return directory!.FullName;
    }

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
}
