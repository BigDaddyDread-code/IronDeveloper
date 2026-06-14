using IronDev.Core.AgentMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("MemoryCannotPromoteItself")]
public sealed class MemoryCannotPromoteItselfTests
{
    private static readonly Guid ProjectId = Id(115);
    private static readonly Guid ProposalId = Id(116);
    private static readonly Guid EvidencePackageId = Id(117);
    private static readonly Guid PromotionPackageId = Id(118);
    private static readonly Guid WorkflowRunId = Id(119);
    private static readonly Guid WorkflowRunStepId = Id(120);
    private static readonly Guid WorkflowCheckpointId = Id(121);
    private static readonly Guid GroundingReferenceId = Id(122);
    private static readonly Guid ThoughtLedgerEntryId = Id(123);
    private static readonly Guid GovernanceEventId = Id(124);
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public void MemoryCannotPromoteItself_StagedProposalIsReviewOnly()
    {
        var proposal = StagedProposal();

        Assert.IsTrue(new MemoryProposalThoughtLedgerTraceabilityValidator().ValidateStagedProposal(proposal).IsValid);
        AssertStagedProposalHasNoAuthority(proposal);
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_StagedProposalCannotBeAcceptedMemory()
    {
        var result = new MemoryProposalThoughtLedgerTraceabilityValidator().ValidateStagedProposal(StagedProposal(isAcceptedMemory: true));

        AssertHasIssue(result, "proposal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_StagedProposalCannotPromoteMemory()
    {
        var result = new MemoryProposalThoughtLedgerTraceabilityValidator().ValidateStagedProposal(StagedProposal(promotesMemory: true));

        AssertHasIssue(result, "proposal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_StagedProposalCannotActivateRetrieval()
    {
        var result = new MemoryProposalThoughtLedgerTraceabilityValidator().ValidateStagedProposal(StagedProposal(isRetrievalAuthority: true));

        AssertHasIssue(result, "proposal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_StagedProposalCannotGrantApproval()
    {
        var result = new MemoryProposalThoughtLedgerTraceabilityValidator().ValidateStagedProposal(StagedProposal(grantsApproval: true));

        AssertHasIssue(result, "proposal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_StagedProposalStatusVocabularyHasNoAcceptedOrPromotedState()
    {
        var names = string.Join('\n', Enum.GetNames<MemoryProposalStatus>());

        AssertNoForbiddenTokens(names, "Accepted", "Promoted", "PromotionApproved", "RetrievalActive", "PolicySatisfied", "ApprovalSatisfied", "ReleaseApproved");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_StagedProposalTargetScopeVocabularyHasNoLiveMemoryTarget()
    {
        var names = string.Join('\n', Enum.GetNames<MemoryProposalTargetScope>());

        AssertNoForbiddenTokens(names, "Accepted", "Promoted", "Live", "Active", "RetrievalActive", "PolicyActive", "AuthorityActive");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageIsReviewOnly()
    {
        var package = EvidencePackage();

        Assert.IsTrue(new MemoryProposalThoughtLedgerTraceabilityValidator().ValidateEvidencePackage(package).IsValid);
        AssertEvidencePackageHasNoAuthority(package);
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageValidatorRejectsAcceptedMemoryFlag()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(acceptsMemory: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageValidatorRejectsPromotionFlag()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(promotesMemory: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageValidatorRejectsRetrievalActivationFlag()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(activatesRetrieval: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageValidatorRejectsAcceptedMemoryLanguage()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(safeMemory: "accepted memory created"), "package_text_unsafe");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageValidatorRejectsPromotedMemoryLanguage()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(safeMemory: "promoted memory"), "package_text_unsafe");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageValidatorRejectsRetrievalActiveLanguage()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(safeMemory: "retrieval active"), "package_text_unsafe");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageValidatorRejectsPolicySatisfiedLanguage()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(safeRiskSummary: "policy satisfied"), "package_text_unsafe");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageValidatorRejectsApprovalGrantedLanguage()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(safeRiskSummary: "approval granted"), "package_text_unsafe");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageEvidenceReferenceCannotAcceptMemory()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(evidenceReferences: new[] { PackageEvidence(evidenceAcceptsMemory: true) }), "evidence_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageEvidenceReferenceCannotBeApproval()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(evidenceReferences: new[] { PackageEvidence(evidenceIsApproval: true) }), "evidence_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageGroundingCannotAcceptMemory()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(groundingReferences: new[] { PackageGrounding(groundingAcceptsMemory: true) }), "grounding_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageGroundingCannotBeAuthority()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(groundingReferences: new[] { PackageGrounding(groundingIsAuthority: true) }), "grounding_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageWorkflowCannotAcceptMemory()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(workflowReferences: new[] { PackageWorkflow(workflowReferenceAcceptsMemory: true) }), "workflow_reference_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageWorkflowCannotPromoteMemory()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(workflowReferences: new[] { PackageWorkflow(workflowReferencePromotesMemory: true) }), "workflow_reference_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageReviewNoteCannotAcceptMemory()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(reviewNotes: new[] { PackageReviewNote("HumanReviewNeeded", noteAcceptsMemory: true) }), "review_note_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageReviewNoteCannotPromoteMemory()
    {
        AssertEvidencePackageInvalid(EvidencePackageRequest(reviewNotes: new[] { PackageReviewNote("HumanReviewNeeded", notePromotesMemory: true) }), "review_note_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_EvidencePackageAssemblerDoesNotCreateAuthority()
    {
        var package = new MemoryProposalEvidencePackageValidator().Normalize(new MemoryProposalEvidencePackageAssembler().Assemble(StagedProposal()));

        AssertEvidencePackageHasNoAuthority(package);
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageIsReviewOnly()
    {
        var package = PromotionRequestPackage();

        Assert.IsTrue(new MemoryProposalThoughtLedgerTraceabilityValidator().ValidatePromotionRequestPackage(package).IsValid);
        AssertPromotionRequestPackageHasNoAuthority(package);
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageValidatorRejectsAcceptedMemoryFlag()
    {
        AssertPromotionRequestInvalid(PromotionRequest(acceptsMemory: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageValidatorRejectsPromotedMemoryFlag()
    {
        AssertPromotionRequestInvalid(PromotionRequest(promotesMemory: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageValidatorRejectsPortableMemoryCreationFlag()
    {
        AssertPromotionRequestInvalid(PromotionRequest(createsPortableMemory: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageValidatorRejectsRetrievalActivationFlag()
    {
        AssertPromotionRequestInvalid(PromotionRequest(activatesRetrieval: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageValidatorRejectsEmbeddingFlag()
    {
        AssertPromotionRequestInvalid(PromotionRequest(createsEmbedding: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageValidatorRejectsVectorStoreFlag()
    {
        AssertPromotionRequestInvalid(PromotionRequest(writesVectorStore: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageValidatorRejectsApprovalFlag()
    {
        AssertPromotionRequestInvalid(PromotionRequest(grantsApproval: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageValidatorRejectsPolicyFlag()
    {
        AssertPromotionRequestInvalid(PromotionRequest(satisfiesPolicy: true), "authority_flags_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageRejectsAcceptedMemoryLanguage()
    {
        AssertPromotionRequestInvalid(PromotionRequest(safeMemory: "accepted memory"), "promotion_request_package_text_unsafe");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageRejectsPromotedMemoryLanguage()
    {
        AssertPromotionRequestInvalid(PromotionRequest(safeMemory: "promoted memory"), "promotion_request_package_text_unsafe");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageRejectsPortableMemoryApprovedLanguage()
    {
        AssertPromotionRequestInvalid(PromotionRequest(reviewerInstructions: "portable memory approved"), "promotion_request_package_text_unsafe");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestPackageRejectsTruthConfirmedLanguage()
    {
        AssertPromotionRequestInvalid(PromotionRequest(reviewerInstructions: "truth confirmed"), "promotion_request_package_text_unsafe");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestEvidenceCannotGrantApproval()
    {
        AssertPromotionRequestInvalid(PromotionRequest(evidenceReferences: new[] { PromotionEvidence(evidenceGrantsApproval: true) }), "evidence_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestEvidenceCannotSatisfyPolicy()
    {
        AssertPromotionRequestInvalid(PromotionRequest(evidenceReferences: new[] { PromotionEvidence(evidenceSatisfiesPolicy: true) }), "evidence_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestEvidenceCannotAcceptMemory()
    {
        AssertPromotionRequestInvalid(PromotionRequest(evidenceReferences: new[] { PromotionEvidence(evidenceAcceptsMemory: true) }), "evidence_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestEvidenceCannotPromoteMemory()
    {
        AssertPromotionRequestInvalid(PromotionRequest(evidenceReferences: new[] { PromotionEvidence(evidencePromotesMemory: true) }), "evidence_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestGroundingCannotPromoteMemory()
    {
        AssertPromotionRequestInvalid(PromotionRequest(groundingReferences: new[] { PromotionGrounding(groundingPromotesMemory: true) }), "grounding_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DuplicateSignalCannotAllowPromotion()
    {
        AssertPromotionRequestInvalid(PromotionRequest(signalReferences: new[] { Signal("DuplicateCandidate", signalAllowsPromotion: true) }), "signal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_StaleSignalCannotBlockPromotion()
    {
        AssertPromotionRequestInvalid(PromotionRequest(signalReferences: new[] { Signal("StaleCandidate", signalBlocksPromotion: true) }), "signal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_ConflictSignalCannotDecideTruth()
    {
        AssertPromotionRequestInvalid(PromotionRequest(signalReferences: new[] { Signal("ConflictCandidate", signalIsDecision: true) }), "signal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_CrossRunPatternSignalCannotPromoteMemory()
    {
        AssertPromotionRequestInvalid(PromotionRequest(signalReferences: new[] { Signal("CrossRunPatternCandidate", signalPromotesMemory: true) }), "signal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_ApprovalRequirementReferenceCannotBeApproval()
    {
        AssertPromotionRequestInvalid(PromotionRequest(approvalRequirementReferences: new[] { ApprovalRequirement(requirementIsApproval: true) }), "approval_requirement_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_ApprovalRequirementReferenceCannotSatisfyPolicy()
    {
        AssertPromotionRequestInvalid(PromotionRequest(approvalRequirementReferences: new[] { ApprovalRequirement(requirementSatisfiesPolicy: true) }), "approval_requirement_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_ApprovalRequirementReferenceCannotAllowPromotion()
    {
        AssertPromotionRequestInvalid(PromotionRequest(approvalRequirementReferences: new[] { ApprovalRequirement(requirementAllowsPromotion: true) }), "approval_requirement_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestReviewNoteCannotRejectMemory()
    {
        AssertPromotionRequestInvalid(PromotionRequest(reviewNotes: new[] { PromotionReviewNote("DuplicateRisk", noteRejectsMemory: true) }), "review_note_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestReviewNoteCannotPromoteMemory()
    {
        AssertPromotionRequestInvalid(PromotionRequest(reviewNotes: new[] { PromotionReviewNote("PromotionRationale", notePromotesMemory: true) }), "review_note_authority_forbidden");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_PromotionRequestAssemblerDoesNotCreateAuthority()
    {
        var package = new MemoryPromotionRequestPackageAssembler().Assemble(
            StagedProposal(),
            EvidencePackage(),
            duplicateCandidates: new[] { new MemoryProposalDuplicateCandidate { MemoryProposalDuplicateCandidateId = Id(130), SafeReasonSummary = "Duplicate signal needs review." } },
            staleCandidates: new[] { new MemoryProposalStaleCandidate { MemoryProposalStaleCandidateId = Id(131), SafeReasonSummary = "Stale signal needs review." } },
            conflictCandidates: new[] { new MemoryProposalConflictCandidate { MemoryProposalConflictCandidateId = Id(132), SafeConflictSummary = "Conflict signal needs review." } },
            patternCandidates: new[] { new CrossRunMemoryPatternCandidate { CrossRunMemoryPatternCandidateId = Id(133), SafePatternSummary = "Pattern signal needs review." } });

        AssertPromotionRequestPackageHasNoAuthority(new MemoryPromotionRequestPackageValidator().Normalize(package));
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_FullReviewChainCannotPromoteMemory()
    {
        var chain = FullReviewChain();

        AssertStagedProposalHasNoAuthority(chain.Proposal);
        AssertEvidencePackageHasNoAuthority(chain.EvidencePackage);
        AssertPromotionRequestPackageHasNoAuthority(chain.PromotionRequestPackage);
        Assert.IsFalse(chain.AnyArtifactCreatesAcceptedMemory);
        Assert.IsFalse(chain.AnyArtifactCreatesPortableMemory);
        Assert.IsFalse(chain.AnyArtifactPromotesMemory);
        Assert.IsFalse(chain.AnyArtifactActivatesRetrieval);
        Assert.IsFalse(chain.AnyArtifactCreatesEmbedding);
        Assert.IsFalse(chain.AnyArtifactWritesVectorStore);
        Assert.IsFalse(chain.AnyArtifactGrantsApproval);
        Assert.IsFalse(chain.AnyArtifactSatisfiesPolicy);
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_FullReviewChainStillRequiresHumanReview()
    {
        var package = FullReviewChain().PromotionRequestPackage;

        Assert.AreEqual(MemoryPromotionRequestPurpose.ProjectMemoryReview, package.Purpose);
        Assert.IsTrue(package.ApprovalRequirementReferences.Any(reference => reference.RequirementType == "HumanReview"));
        Assert.IsFalse(package.ApprovalRequirementReferences.Any(reference => reference.RequirementIsApproval || reference.RequirementAllowsPromotion));
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_FullReviewChainDoesNotMutateProposalState()
    {
        var proposal = StagedProposal();
        var beforeStatus = proposal.ProposalStatus;
        var evidence = new MemoryProposalEvidencePackageValidator().Normalize(new MemoryProposalEvidencePackageAssembler().Assemble(proposal));
        _ = new MemoryPromotionRequestPackageValidator().Normalize(new MemoryPromotionRequestPackageAssembler().Assemble(proposal, evidence));

        Assert.AreEqual(beforeStatus, proposal.ProposalStatus);
        Assert.IsFalse(proposal.IsAcceptedMemory);
        Assert.IsFalse(proposal.PromotesMemory);
        Assert.IsFalse(proposal.WritesVectorIndex);
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_ReceiptDocumentsBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot, "Docs", "receipts", "PR115_MEMORY_CANNOT_PROMOTE_ITSELF.md"));

        AssertContainsAll(text,
            "PR115 proves memory cannot promote itself.",
            "No staged proposal accepts memory.",
            "No evidence package accepts memory.",
            "No trace reference accepts memory.",
            "No signal promotes memory.",
            "No promotion request package promotes memory.",
            "No composition of memory proposal artifacts creates accepted memory.",
            "No composition of memory proposal artifacts creates promoted memory.",
            "No composition of memory proposal artifacts activates retrieval.",
            "No composition of memory proposal artifacts satisfies approval.",
            "No composition of memory proposal artifacts satisfies policy.",
            "PR115 proves the whole review chain is still a review chain. It is not the promotion button.");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_BlockKDocLinksReceipt()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot, "Docs", "BLOCK_K_GOVERNED_MEMORY_PROPOSAL_SUBSTRATE.md"));

        AssertContainsAll(text,
            "PR115 - Memory Cannot Promote Itself Test Pack",
            "PR115_MEMORY_CANNOT_PROMOTE_ITSELF.md",
            "A promotion request package is not promotion approval.",
            "The full Block K proposal chain can request review. It cannot accept memory, promote memory, activate retrieval, create embeddings, write vectors, satisfy policy, satisfy approval, approve release, or make memory live.");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DoesNotAddSqlMigration()
    {
        AssertNoPr115ReferencesUnder("Database");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DoesNotAddApiEndpoint()
    {
        AssertNoPr115ReferencesUnder("IronDev.Api");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DoesNotAddCliCommand()
    {
        AssertNoPr115ReferencesUnder(Path.Combine("tools", "IronDev.Cli"));
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DoesNotAddInfrastructureRuntime()
    {
        AssertNoPr115ReferencesUnder("IronDev.Infrastructure");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DoesNotAddAcceptedMemoryStore()
    {
        AssertProductionDoesNotContain("IAcceptedMemoryStore", "AcceptedMemoryStore", "CreateAcceptedMemoryAsync");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DoesNotAddPromotionStoreOrService()
    {
        AssertProductionDoesNotContain("IMemoryPromotionStore", "MemoryPromotionStore", "PromoteMemoryAsync", "PromotionService");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DoesNotAddRetrievalActivationOrIndexing()
    {
        AssertProductionDoesNotContain("IRetrievalActivation", "ActivateRetrievalAsync", "IEmbeddingWriter", "IVectorStoreWriter", "WeaviateWriter");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DoesNotAddModelOrWorkflowRuntime()
    {
        AssertProductionDoesNotContain("IAgentModelAdapter", "IModelClient", "WorkflowRunner", "IWorkflowRunner", "AgentDispatcher", "IAgentDispatcher");
    }

    [TestMethod]
    public void MemoryCannotPromoteItself_DoesNotAddSourceApplyOrReleaseApprovalPath()
    {
        AssertProductionDoesNotContain("SourceApplyService", "ApplyToSource", "ApplySourceAsync", "ReleaseApprovalService", "ApproveReleaseAsync");
    }

    private static MemoryReviewChain FullReviewChain()
    {
        var proposal = StagedProposal();
        var evidencePackage = new MemoryProposalEvidencePackageValidator().Normalize(new MemoryProposalEvidencePackageAssembler().Assemble(proposal));
        var promotionRequestPackage = new MemoryPromotionRequestPackageValidator().Normalize(new MemoryPromotionRequestPackageAssembler().Assemble(proposal, evidencePackage));
        return new MemoryReviewChain(proposal, evidencePackage, promotionRequestPackage);
    }

    private sealed record MemoryReviewChain(MemoryProposal Proposal, MemoryProposalEvidencePackage EvidencePackage, MemoryPromotionRequestPackage PromotionRequestPackage)
    {
        public bool AnyArtifactCreatesAcceptedMemory => Proposal.IsAcceptedMemory || EvidencePackage.CreatesAcceptedMemory || PromotionRequestPackage.CreatesAcceptedMemory;
        public bool AnyArtifactCreatesPortableMemory => EvidencePackage.CreatesPortableMemory || PromotionRequestPackage.CreatesPortableMemory;
        public bool AnyArtifactPromotesMemory => Proposal.PromotesMemory || EvidencePackage.PromotesMemory || PromotionRequestPackage.PromotesMemory;
        public bool AnyArtifactActivatesRetrieval => Proposal.IsRetrievalAuthority || EvidencePackage.ActivatesRetrieval || PromotionRequestPackage.ActivatesRetrieval;
        public bool AnyArtifactCreatesEmbedding => EvidencePackage.CreatesEmbedding || PromotionRequestPackage.CreatesEmbedding;
        public bool AnyArtifactWritesVectorStore => Proposal.WritesVectorIndex || EvidencePackage.WritesVectorStore || PromotionRequestPackage.WritesVectorStore;
        public bool AnyArtifactGrantsApproval => Proposal.GrantsApproval || EvidencePackage.GrantsApproval || PromotionRequestPackage.GrantsApproval;
        public bool AnyArtifactSatisfiesPolicy => EvidencePackage.SatisfiesPolicy || PromotionRequestPackage.SatisfiesPolicy;
    }

    private static MemoryProposal StagedProposal(
        bool grantsApproval = false,
        bool isAcceptedMemory = false,
        bool promotesMemory = false,
        bool isRetrievalAuthority = false) => new()
    {
        MemoryProposalId = ProposalId,
        ProjectId = ProjectId,
        ProposalKey = "proposal-115",
        ProposalType = MemoryProposalType.ProjectFactCandidate,
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate,
        ProposalStatus = MemoryProposalStatus.ReadyForReview,
        SourceType = "system_test_fixture",
        SubjectType = "governed-memory-proposal",
        SubjectId = "pr115",
        SafeProposedMemory = "Prefer deterministic memory proposal review boundaries.",
        SafeRationaleSummary = "The proposal is useful enough for review, but remains only a candidate.",
        SafeRiskSummary = "Human review remains required before any later memory action.",
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential,
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-cannot-promote-itself-tests",
        MetadataVersion = 1,
        MetadataJson = "{}",
        GrantsApproval = grantsApproval,
        IsAcceptedMemory = isAcceptedMemory,
        PromotesMemory = promotesMemory,
        IsRetrievalAuthority = isRetrievalAuthority,
        EvidenceReferences = new[] { ProposalEvidence() },
        GroundingReferences = new[] { ProposalGrounding() },
        WorkflowReferences = new[] { ProposalWorkflow() },
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static MemoryProposalEvidenceReference ProposalEvidence() => new()
    {
        MemoryProposalEvidenceReferenceId = Id(134),
        MemoryProposalId = ProposalId,
        ProjectId = ProjectId,
        EvidenceType = MemoryProposalEvidenceType.ThoughtLedgerReference,
        EvidenceId = ThoughtLedgerEntryId.ToString("N"),
        EvidenceLabel = "ThoughtLedger trace",
        SafeSummary = "ThoughtLedger trace supports review only.",
        AllowedUse = MemoryProposalEvidenceAllowedUse.Traceability,
        ThoughtLedgerEntryId = ThoughtLedgerEntryId,
        GovernanceEventId = GovernanceEventId,
        WorkflowRunEvidenceReferenceId = Id(135),
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static MemoryProposalGroundingReference ProposalGrounding() => new()
    {
        MemoryProposalGroundingReferenceId = Id(136),
        MemoryProposalId = ProposalId,
        ProjectId = ProjectId,
        GroundingReferenceId = GroundingReferenceId,
        ClaimType = MemoryProposalGroundingClaimType.MemoryProposalTrace,
        ClaimId = "claim-115",
        SafeSummary = "Grounding reference traces the proposal for review only.",
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static MemoryProposalWorkflowReference ProposalWorkflow() => new()
    {
        MemoryProposalWorkflowReferenceId = Id(137),
        MemoryProposalId = ProposalId,
        ProjectId = ProjectId,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        ReferenceType = MemoryProposalWorkflowReferenceType.Traceability,
        SafeSummary = "Workflow reference supports traceability only.",
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static MemoryProposalEvidencePackage EvidencePackage() => new MemoryProposalEvidencePackageValidator().Normalize(EvidencePackageRequest());

    private static MemoryProposalEvidencePackageCreateRequest EvidencePackageRequest(
        string safeMemory = "Prefer deterministic memory proposal review boundaries.",
        string? safeRiskSummary = "Human review remains required before any memory decision.",
        bool acceptsMemory = false,
        bool promotesMemory = false,
        bool activatesRetrieval = false,
        IReadOnlyList<MemoryProposalPackageEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<MemoryProposalPackageGroundingReference>? groundingReferences = null,
        IReadOnlyList<MemoryProposalPackageWorkflowReference>? workflowReferences = null,
        IReadOnlyList<MemoryProposalPackageReviewNote>? reviewNotes = null) => new()
    {
        MemoryProposalEvidencePackageId = EvidencePackageId,
        MemoryProposalId = ProposalId,
        ProjectId = ProjectId,
        PackageKey = "proposal-115.evidence-package",
        Status = MemoryProposalEvidencePackageStatus.ReadyForReview,
        Purpose = MemoryProposalEvidencePackagePurpose.HumanReview,
        ProposalType = MemoryProposalType.ProjectFactCandidate.ToString(),
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate.ToString(),
        ProposalStatus = MemoryProposalStatus.ReadyForReview.ToString(),
        SafeProposedMemory = safeMemory,
        SafeRationaleSummary = "Evidence package gathers safe review context.",
        SafeRiskSummary = safeRiskSummary,
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential.ToString(),
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview.ToString(),
        EvidenceReferences = evidenceReferences ?? new[] { PackageEvidence() },
        GroundingReferences = groundingReferences ?? new[] { PackageGrounding() },
        WorkflowReferences = workflowReferences ?? new[] { PackageWorkflow() },
        ReviewNotes = reviewNotes ?? new[] { PackageReviewNote("HumanReviewNeeded") },
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-cannot-promote-itself-tests",
        MetadataVersion = 1,
        MetadataJson = "{}",
        AcceptsMemory = acceptsMemory,
        PromotesMemory = promotesMemory,
        ActivatesRetrieval = activatesRetrieval
    };

    private static MemoryProposalPackageEvidenceReference PackageEvidence(bool evidenceIsApproval = false, bool evidenceAcceptsMemory = false) => new()
    {
        EvidenceType = "ThoughtLedgerReference",
        EvidenceId = ThoughtLedgerEntryId.ToString("N"),
        EvidenceLabel = "ThoughtLedger trace",
        SafeSummary = "Evidence reference is for review only.",
        AllowedUse = "Traceability",
        ThoughtLedgerEntryId = ThoughtLedgerEntryId,
        GovernanceEventId = GovernanceEventId,
        WorkflowRunEvidenceReferenceId = Id(138),
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        EvidenceIsApproval = evidenceIsApproval,
        EvidenceAcceptsMemory = evidenceAcceptsMemory
    };

    private static MemoryProposalPackageGroundingReference PackageGrounding(bool groundingIsAuthority = false, bool groundingAcceptsMemory = false) => new()
    {
        GroundingEvidenceReferenceId = GroundingReferenceId,
        ClaimType = "MemoryProposalTrace",
        ClaimId = "claim-115",
        SafeSummary = "Grounding supports traceability only.",
        GroundingIsAuthority = groundingIsAuthority,
        GroundingAcceptsMemory = groundingAcceptsMemory
    };

    private static MemoryProposalPackageWorkflowReference PackageWorkflow(bool workflowReferenceAcceptsMemory = false, bool workflowReferencePromotesMemory = false) => new()
    {
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        ReferenceType = "Traceability",
        SafeSummary = "Workflow reference is context only.",
        WorkflowReferenceAcceptsMemory = workflowReferenceAcceptsMemory,
        WorkflowReferencePromotesMemory = workflowReferencePromotesMemory
    };

    private static MemoryProposalPackageReviewNote PackageReviewNote(string noteType, bool noteAcceptsMemory = false, bool notePromotesMemory = false) => new()
    {
        NoteType = noteType,
        SafeSummary = "Human review remains required before any memory decision.",
        Severity = "warning",
        NoteAcceptsMemory = noteAcceptsMemory,
        NotePromotesMemory = notePromotesMemory
    };

    private static MemoryPromotionRequestPackage PromotionRequestPackage() => new MemoryPromotionRequestPackageValidator().Normalize(PromotionRequest());

    private static MemoryPromotionRequestPackageCreateRequest PromotionRequest(
        string safeMemory = "Prefer deterministic memory proposal review boundaries.",
        string? reviewerInstructions = "Human review remains required before any later memory action.",
        bool acceptsMemory = false,
        bool promotesMemory = false,
        bool createsPortableMemory = false,
        bool activatesRetrieval = false,
        bool createsEmbedding = false,
        bool writesVectorStore = false,
        bool grantsApproval = false,
        bool satisfiesPolicy = false,
        IReadOnlyList<MemoryPromotionRequestEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<MemoryPromotionRequestGroundingReference>? groundingReferences = null,
        IReadOnlyList<MemoryPromotionRequestSignalReference>? signalReferences = null,
        IReadOnlyList<MemoryPromotionApprovalRequirementReference>? approvalRequirementReferences = null,
        IReadOnlyList<MemoryPromotionRequestReviewNote>? reviewNotes = null) => new()
    {
        MemoryPromotionRequestPackageId = PromotionPackageId,
        ProjectId = ProjectId,
        MemoryProposalId = ProposalId,
        PromotionRequestKey = "proposal-115.promotion-review",
        Status = MemoryPromotionRequestPackageStatus.ReadyForReview,
        Purpose = MemoryPromotionRequestPurpose.HumanPromotionReview,
        ProposalType = MemoryProposalType.ProjectFactCandidate.ToString(),
        CurrentProposalStatus = MemoryProposalStatus.ReadyForReview.ToString(),
        RequestedTargetMemoryScope = MemoryPromotionRequestedTargetMemoryScope.ProjectLocalCandidateForPromotion,
        SafeProposedMemory = safeMemory,
        SafePromotionRationale = "Package gathers staged proposal material for governed promotion review only.",
        SafeRiskSummary = "Human review remains required before any later memory action.",
        SafeSanitizationSummary = "Sanitization status must be reviewed before any later memory action.",
        SafeReviewerInstructions = reviewerInstructions,
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential.ToString(),
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview.ToString(),
        EvidenceReferences = evidenceReferences ?? new[] { PromotionEvidence() },
        GroundingReferences = groundingReferences ?? new[] { PromotionGrounding() },
        SignalReferences = signalReferences ?? new[] { Signal("EvidenceGap") },
        ApprovalRequirementReferences = approvalRequirementReferences ?? new[] { ApprovalRequirement() },
        ReviewNotes = reviewNotes ?? new[] { PromotionReviewNote("HumanReviewNeeded") },
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-cannot-promote-itself-tests",
        MetadataVersion = 1,
        MetadataJson = "{}",
        AcceptsMemory = acceptsMemory,
        PromotesMemory = promotesMemory,
        CreatesPortableMemory = createsPortableMemory,
        ActivatesRetrieval = activatesRetrieval,
        CreatesEmbedding = createsEmbedding,
        WritesVectorStore = writesVectorStore,
        GrantsApproval = grantsApproval,
        SatisfiesPolicy = satisfiesPolicy
    };

    private static MemoryPromotionRequestEvidenceReference PromotionEvidence(bool evidenceGrantsApproval = false, bool evidenceSatisfiesPolicy = false, bool evidenceAcceptsMemory = false, bool evidencePromotesMemory = false) => new()
    {
        EvidenceType = "MemoryProposalEvidencePackage",
        EvidenceId = EvidencePackageId.ToString(),
        EvidenceLabel = "Evidence package",
        SafeSummary = "Evidence package supports promotion review only.",
        AllowedUse = "PromotionReview",
        MemoryProposalId = ProposalId,
        MemoryProposalEvidencePackageId = EvidencePackageId,
        GovernanceEventId = GovernanceEventId,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        EvidenceGrantsApproval = evidenceGrantsApproval,
        EvidenceSatisfiesPolicy = evidenceSatisfiesPolicy,
        EvidenceAcceptsMemory = evidenceAcceptsMemory,
        EvidencePromotesMemory = evidencePromotesMemory
    };

    private static MemoryPromotionRequestGroundingReference PromotionGrounding(bool groundingPromotesMemory = false) => new()
    {
        GroundingEvidenceReferenceId = GroundingReferenceId,
        ClaimType = "EvidenceSupport",
        ClaimId = "claim-115",
        SafeSummary = "Grounding supports traceability only.",
        GroundingPromotesMemory = groundingPromotesMemory
    };

    private static MemoryPromotionRequestSignalReference Signal(string signalType, bool signalIsDecision = false, bool signalBlocksPromotion = false, bool signalAllowsPromotion = false, bool signalPromotesMemory = false) => new()
    {
        SignalType = signalType,
        SignalId = Id(139),
        SafeSummary = "Signal supports review only.",
        Severity = "warning",
        SignalIsDecision = signalIsDecision,
        SignalBlocksPromotion = signalBlocksPromotion,
        SignalAllowsPromotion = signalAllowsPromotion,
        SignalPromotesMemory = signalPromotesMemory
    };

    private static MemoryPromotionApprovalRequirementReference ApprovalRequirement(bool requirementIsApproval = false, bool requirementSatisfiesPolicy = false, bool requirementAllowsPromotion = false) => new()
    {
        RequirementType = "HumanReview",
        RequirementId = "human-review-115",
        SafeSummary = "Human review is required later.",
        RequirementIsApproval = requirementIsApproval,
        RequirementSatisfiesPolicy = requirementSatisfiesPolicy,
        RequirementAllowsPromotion = requirementAllowsPromotion
    };

    private static MemoryPromotionRequestReviewNote PromotionReviewNote(string noteType, bool noteRejectsMemory = false, bool notePromotesMemory = false) => new()
    {
        NoteType = noteType,
        SafeSummary = "Human review remains required before any later memory action.",
        Severity = "warning",
        NoteRejectsMemory = noteRejectsMemory,
        NotePromotesMemory = notePromotesMemory
    };

    private static void AssertStagedProposalHasNoAuthority(MemoryProposal proposal)
    {
        Assert.IsFalse(proposal.IsApproval);
        Assert.IsFalse(proposal.GrantsApproval);
        Assert.IsFalse(proposal.IsAcceptedMemory);
        Assert.IsFalse(proposal.PromotesMemory);
        Assert.IsFalse(proposal.IsRetrievalAuthority);
        Assert.IsFalse(proposal.WritesVectorIndex);
    }

    private static void AssertEvidencePackageHasNoAuthority(MemoryProposalEvidencePackage package)
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

    private static void AssertPromotionRequestPackageHasNoAuthority(MemoryPromotionRequestPackage package)
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

    private static void AssertEvidencePackageInvalid(MemoryProposalEvidencePackageCreateRequest request, string expectedCode)
    {
        AssertHasIssue(new MemoryProposalEvidencePackageValidator().Validate(request), expectedCode);
    }

    private static void AssertPromotionRequestInvalid(MemoryPromotionRequestPackageCreateRequest request, string expectedCode)
    {
        AssertHasIssue(new MemoryPromotionRequestPackageValidator().Validate(request), expectedCode);
    }

    private static void AssertHasIssue(MemoryProposalValidationResult result, string expectedCode)
    {
        Assert.IsFalse(result.IsValid, "Expected validation to fail.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), "Expected issue code " + expectedCode + "; actual: " + string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    private static void AssertContainsAll(string text, params string[] required)
    {
        foreach (var value in required)
        {
            Assert.IsTrue(text.Contains(value, StringComparison.Ordinal), "Missing required text: " + value);
        }
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbiddenTokens)
    {
        foreach (var token in forbiddenTokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), "Forbidden token '" + token + "' was present.");
        }
    }

    private static void AssertNoPr115ReferencesUnder(string relativeDirectory)
    {
        var directory = Path.Combine(RepositoryRoot, relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var references = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(IsTextFile)
            .Where(path => File.ReadAllText(path).Contains("MemoryCannotPromoteItself", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(path).Contains("PR115_MEMORY_CANNOT_PROMOTE_ITSELF", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(0, references.Length, "PR115 should not be wired into forbidden surfaces: " + string.Join(", ", references));
    }

    private static void AssertProductionDoesNotContain(params string[] forbiddenTokens)
    {
        var files = new[]
        {
            Path.Combine(RepositoryRoot, "Docs", "BLOCK_K_GOVERNED_MEMORY_PROPOSAL_SUBSTRATE.md"),
            Path.Combine(RepositoryRoot, "Docs", "receipts", "PR115_MEMORY_CANNOT_PROMOTE_ITSELF.md")
        };

        var text = string.Join('\n', files.Select(File.ReadAllText));
        AssertNoForbiddenTokens(text, forbiddenTokens);
    }

    private static bool IsTextFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".sql" or ".md" or ".json" or ".ps1";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
}
