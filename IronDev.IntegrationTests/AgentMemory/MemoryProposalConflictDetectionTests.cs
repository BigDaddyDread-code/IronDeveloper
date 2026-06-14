using IronDev.Core.AgentMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("MemoryProposalConflictDetection")]
public sealed class MemoryProposalConflictDetectionTests
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-eeee-4444-8888-990000000111");
    private static readonly Guid PrimaryProposalId = Guid.Parse("20000000-eeee-4444-8888-990000000111");
    private static readonly Guid ConflictingProposalId = Guid.Parse("30000000-eeee-4444-8888-990000000111");
    private static readonly Guid ConflictCandidateId = Guid.Parse("40000000-eeee-4444-8888-990000000111");
    private static readonly Guid WorkflowRunId = Guid.Parse("50000000-eeee-4444-8888-990000000111");
    private static readonly Guid WorkflowRunStepId = Guid.Parse("60000000-eeee-4444-8888-990000000111");
    private static readonly Guid WorkflowCheckpointId = Guid.Parse("70000000-eeee-4444-8888-990000000111");

    [TestMethod]
    public void MemoryProposalConflictCandidate_ExposesExpectedShape()
    {
        var candidate = new MemoryProposalConflictCandidateValidator().Normalize(CreateRequest());
        Assert.AreEqual(ConflictCandidateId, candidate.MemoryProposalConflictCandidateId);
        Assert.AreEqual(ProjectId, candidate.ProjectId);
        Assert.AreEqual(PrimaryProposalId, candidate.PrimaryMemoryProposalId);
        Assert.AreEqual(ConflictingProposalId, candidate.ConflictingMemoryProposalId);
        Assert.AreEqual(MemoryProposalConflictCandidateStatus.ReadyForReview, candidate.Status);
        Assert.AreEqual(MemoryProposalConflictType.DirectContradictionCandidate, candidate.ConflictType);
        Assert.AreEqual(MemoryProposalConflictBand.DirectContradiction, candidate.ConflictBand);
        Assert.AreEqual(1, candidate.EvidenceReferences.Count);
        Assert.AreEqual(1, candidate.ReviewNotes.Count);
        AssertCandidateHasNoAuthority(candidate);
    }

    [TestMethod]
    public void MemoryProposalConflictCandidate_UsesBoundedConflictTypes()
    {
        var names = Enum.GetNames<MemoryProposalConflictType>();
        CollectionAssert.AreEquivalent(new[] { "DirectContradictionCandidate", "NegationCandidate", "IncompatiblePolicyCandidate", "IncompatibleScopeCandidate", "IncompatibleStatusCandidate", "IncompatibleDecisionCandidate", "IncompatibleWorkflowStateCandidate", "IncompatibleMemoryBoundaryCandidate", "ConflictingEvidenceCandidate", "ConflictingTerminologyCandidate", "ConflictingPortableMemoryCandidate", "NeedsHumanConflictReview" }, names);
        AssertNoForbiddenTokens(string.Join("\n", names), "ConfirmedConflict", "ResolvedConflict", "TruthSelected", "PrimaryWins", "ConflictingProposalRejected", "ProposalDeleted", "ProposalCorrected", "MemoryAccepted", "MemoryPromoted", "RetrievalActivated", "PolicySatisfied", "AuthorityTransferred");
    }

    [TestMethod]
    public void MemoryProposalConflictCandidate_UsesBoundedStatuses()
    {
        var names = Enum.GetNames<MemoryProposalConflictCandidateStatus>();
        CollectionAssert.AreEquivalent(new[] { "Detected", "ReadyForReview", "NeedsEvidence", "NeedsHumanReview", "Quarantined", "Superseded", "Withdrawn" }, names);
        AssertNoForbiddenTokens(string.Join("\n", names), "Confirmed", "Resolved", "Rejected", "Deleted", "Corrected", "Accepted", "Approved", "Promoted", "Indexed", "Embedded", "Retrievable", "PolicySatisfied", "AuthorityTransferred", "TruthSelected");
    }

    [TestMethod]
    public void MemoryProposalConflictCandidate_UsesBoundedConflictBands()
    {
        var names = Enum.GetNames<MemoryProposalConflictBand>();
        CollectionAssert.AreEquivalent(new[] { "HighConflictRisk", "MediumConflictRisk", "LowConflictRisk", "DirectContradiction", "ScopeMismatch", "PolicyMismatch", "TerminologyMismatch", "Unknown" }, names);
        AssertNoForbiddenTokens(string.Join("\n", names), "ConfirmedConflict", "AutoReject", "AutoCorrect", "TruthWinner", "PrimaryWins", "CandidateInvalid", "MemoryInvalid");
    }

    [TestMethod] public void MemoryProposalConflictCandidate_AuthorityFlagsAreFalse() => AssertCandidateHasNoAuthority(new MemoryProposalConflictCandidateValidator().Normalize(CreateRequest()));
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsEmptyProjectId() => AssertInvalid(CreateRequest(projectId: Guid.Empty), "project_id_required");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsEmptyPrimaryProposalId() => AssertInvalid(CreateRequest(primaryProposalId: Guid.Empty), "primary_memory_proposal_id_required");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsEmptyConflictingProposalId() => AssertInvalid(CreateRequest(conflictingProposalId: Guid.Empty), "conflicting_memory_proposal_id_required");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsSamePrimaryAndConflictingProposal() => AssertInvalid(CreateRequest(primaryProposalId: PrimaryProposalId, conflictingProposalId: PrimaryProposalId), "proposal_ids_must_differ");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsBlankCandidateKey() => AssertInvalid(CreateRequest(candidateKey: " "), "conflict_candidate_key_required");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsForbiddenConflictType() => AssertInvalid(CreateRequest(conflictType: (MemoryProposalConflictType)999), "conflict_type_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsForbiddenStatus() => AssertInvalid(CreateRequest(status: (MemoryProposalConflictCandidateStatus)999), "status_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsInvalidConflictScore() => AssertInvalid(CreateRequest(conflictScore: 1.1m), "conflict_score_invalid");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsForbiddenConflictBand() => AssertInvalid(CreateRequest(conflictBand: (MemoryProposalConflictBand)999), "conflict_band_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsHiddenReasoning() => AssertInvalid(CreateRequest(primarySummary: "hiddenReasoning leaked"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsRawPrompt() => AssertInvalid(CreateRequest(primarySummary: "rawPrompt leaked"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsRawCompletion() => AssertInvalid(CreateRequest(primarySummary: "rawCompletion leaked"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsRawToolOutput() => AssertInvalid(CreateRequest(primarySummary: "rawToolOutput leaked"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsEntirePatch() => AssertInvalid(CreateRequest(primarySummary: "entirePatch leaked"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsTruthDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "truth selected"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsRejectDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "reject proposal"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsDeleteDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "delete proposal"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsCorrectionDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "correct proposal"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsAcceptedMemoryLanguage() => AssertInvalid(CreateRequest(reasonSummary: "accepted memory"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsPromotedMemoryLanguage() => AssertInvalid(CreateRequest(reasonSummary: "promoted memory"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsRetrievalActiveLanguage() => AssertInvalid(CreateRequest(reasonSummary: "retrieval active"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsEmbeddingLanguage() => AssertInvalid(CreateRequest(reasonSummary: "embedding created"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsVectorWriteLanguage() => AssertInvalid(CreateRequest(reasonSummary: "vector write"), "conflict_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalConflictCandidateValidator_RejectsTrueAuthorityFlags() => AssertInvalid(CreateRequest(choosesTruth: true), "authority_flags_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_EvidenceReferenceIsNotDecision() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceIsDecision: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_EvidenceReferenceDoesNotChooseTruth() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceChoosesTruth: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_EvidenceReferenceDoesNotRejectProposal() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceRejectsProposal: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_EvidenceReferenceDoesNotDeleteProposal() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceDeletesProposal: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_EvidenceReferenceDoesNotCorrectProposal() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceCorrectsProposal: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_EvidenceReferenceDoesNotAcceptMemory() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceAcceptsMemory: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_ReviewNoteIsNotDecision() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteIsDecision: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_ReviewNoteDoesNotChooseTruth() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteChoosesTruth: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_ReviewNoteDoesNotRejectProposal() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteRejectsProposal: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_ReviewNoteDoesNotDeleteProposal() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteDeletesProposal: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_ReviewNoteDoesNotCorrectProposal() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteCorrectsProposal: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalConflictCandidate_ReviewNoteDoesNotAcceptMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteAcceptsMemory: true) }), "review_note_authority_forbidden");

    [TestMethod]
    public void MemoryProposalConflictDetector_DetectsDirectContradictionCandidate()
    {
        var candidates = Detect(Proposal(PrimaryProposalId, "Workflow checkpoints are resumable workflow state markers."), Proposal(ConflictingProposalId, "Workflow checkpoints are not resumable workflow state markers."));
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(MemoryProposalConflictType.DirectContradictionCandidate, candidates[0].ConflictType);
        Assert.AreEqual(MemoryProposalConflictBand.DirectContradiction, candidates[0].ConflictBand);
    }

    [TestMethod]
    public void MemoryProposalConflictDetector_DetectsNegationCandidate()
    {
        var candidates = Detect(Proposal(PrimaryProposalId, "Use workflow checkpoint records for diagnostic evidence."), Proposal(ConflictingProposalId, "Do not use workflow checkpoint records for diagnostic evidence."));
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(MemoryProposalConflictType.NegationCandidate, candidates[0].ConflictType);
    }

    [TestMethod]
    public void MemoryProposalConflictDetector_DetectsIncompatiblePolicyCandidate()
    {
        var candidates = Detect(Proposal(PrimaryProposalId, "Memory proposal review may happen automatically for non-sensitive cases."), Proposal(ConflictingProposalId, "Memory proposal review requires human review for non-sensitive cases."));
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(MemoryProposalConflictType.IncompatiblePolicyCandidate, candidates[0].ConflictType);
        Assert.AreEqual(MemoryProposalConflictBand.PolicyMismatch, candidates[0].ConflictBand);
    }

    [TestMethod]
    public void MemoryProposalConflictDetector_DetectsIncompatibleScopeCandidate()
    {
        var candidates = Detect(Proposal(PrimaryProposalId, "Portable engineering memory may include reusable project patterns."), Proposal(ConflictingProposalId, "Portable engineering memory must avoid project confidential patterns."));
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(MemoryProposalConflictType.IncompatibleScopeCandidate, candidates[0].ConflictType);
        Assert.AreEqual(MemoryProposalConflictBand.ScopeMismatch, candidates[0].ConflictBand);
    }

    [TestMethod]
    public void MemoryProposalConflictDetector_DetectsIncompatibleMemoryBoundaryCandidate()
    {
        var candidates = Detect(Proposal(PrimaryProposalId, "Staged memory proposals are review evidence only for later human review."), Proposal(ConflictingProposalId, "Staged memory proposals become final project memory automatically."));
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(MemoryProposalConflictType.IncompatibleMemoryBoundaryCandidate, candidates[0].ConflictType);
    }

    [TestMethod]
    public void MemoryProposalConflictDetector_CapsCandidateCount()
    {
        var proposals = new[]
        {
            Proposal(Guid.Parse("10000000-aaaa-4444-8888-990000000111"), "Workflow checkpoints are resumable workflow state markers."),
            Proposal(Guid.Parse("20000000-aaaa-4444-8888-990000000111"), "Workflow checkpoints are not resumable workflow state markers."),
            Proposal(Guid.Parse("30000000-aaaa-4444-8888-990000000111"), "Workflow checkpoints are not resumable workflow state markers.")
        };
        var candidates = new MemoryProposalConflictDetector().Detect(proposals, new MemoryProposalConflictDetectionOptions { MaxCandidateCount = 2 });
        Assert.AreEqual(2, candidates.Count);
    }

    [TestMethod]
    public void MemoryProposalConflictDetector_DoesNotCompareProposalToItself()
    {
        var proposals = new[] { Proposal(PrimaryProposalId, "Workflow checkpoints are resumable workflow state markers."), Proposal(PrimaryProposalId, "Workflow checkpoints are not resumable workflow state markers.") };
        Assert.AreEqual(0, new MemoryProposalConflictDetector().Detect(proposals).Count);
    }

    [TestMethod]
    public void MemoryProposalConflictDetector_DoesNotReturnReversedPairs()
    {
        var candidates = Detect(Proposal(PrimaryProposalId, "Workflow checkpoints are resumable workflow state markers."), Proposal(ConflictingProposalId, "Workflow checkpoints are not resumable workflow state markers."));
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(PrimaryProposalId, candidates[0].PrimaryMemoryProposalId);
        Assert.AreEqual(ConflictingProposalId, candidates[0].ConflictingMemoryProposalId);
    }

    [TestMethod]
    public void MemoryProposalConflictDetector_DoesNotMutateInputProposals()
    {
        var primary = Proposal(PrimaryProposalId, "Workflow checkpoints are resumable workflow state markers.");
        var conflicting = Proposal(ConflictingProposalId, "Workflow checkpoints are not resumable workflow state markers.");
        var originalStatus = primary.ProposalStatus;
        var originalText = primary.SafeProposedMemory;
        _ = Detect(primary, conflicting);
        Assert.AreEqual(originalStatus, primary.ProposalStatus);
        Assert.AreEqual(originalText, primary.SafeProposedMemory);
        Assert.IsFalse(primary.PromotesMemory);
        Assert.IsFalse(primary.IsAcceptedMemory);
    }

    [TestMethod] public void MemoryProposalConflictDetector_DoesNotChooseTruth() => Assert.IsFalse(NormalizedDetectedCandidate().ChoosesTruth);
    [TestMethod] public void MemoryProposalConflictDetector_DoesNotRejectProposal() => Assert.IsFalse(NormalizedDetectedCandidate().RejectsProposal);
    [TestMethod] public void MemoryProposalConflictDetector_DoesNotDeleteProposal() => Assert.IsFalse(NormalizedDetectedCandidate().DeletesProposal);
    [TestMethod] public void MemoryProposalConflictDetector_DoesNotCorrectProposal() => Assert.IsFalse(NormalizedDetectedCandidate().CorrectsProposal);
    [TestMethod] public void MemoryProposalConflictDetector_DoesNotAcceptMemory() => Assert.IsFalse(NormalizedDetectedCandidate().AcceptsMemory);
    [TestMethod] public void MemoryProposalConflictDetector_DoesNotPromoteMemory() => Assert.IsFalse(NormalizedDetectedCandidate().PromotesMemory);
    [TestMethod] public void MemoryProposalConflictDetector_DoesNotActivateRetrieval() => Assert.IsFalse(NormalizedDetectedCandidate().ActivatesRetrieval);
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotAddSqlMigration() => AssertNoFileNameContains("Database", "memory_proposal_conflict", "conflict_detection");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotAddApiEndpoint() => AssertDirectoryDoesNotContain("IronDev.Api", "MemoryProposalConflict", "memory-proposal-conflict", "conflict-detection");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotAddCliCommand() => AssertDirectoryDoesNotContain("tools", "MemoryProposalConflict", "memory-proposal-conflict", "conflict-detection");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotAddAcceptedMemoryStore() => AssertCoreFileDoesNotContain("IAcceptedMemoryStore", "AcceptedMemoryStore", "CreateAcceptedMemoryAsync");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotAddPromotionPath() => AssertCoreFileDoesNotContain("IMemoryPromotion", "PromoteMemoryAsync", "PromotionService");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotAddEmbeddingWriter() => AssertCoreFileDoesNotContain("IEmbeddingWriter", "CreateEmbeddingAsync", "EmbeddingClient");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotAddVectorStoreWriter() => AssertCoreFileDoesNotContain("IVectorStoreWriter", "WriteVectorStoreAsync", "VectorStoreClient");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotReferenceWeaviateWrite() => AssertCoreFileDoesNotContain("IWeaviate", "WeaviateClient", "WriteWeaviateAsync");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotReferenceRetrievalActivation() => AssertCoreFileDoesNotContain("IRetrievalActivation", "ActivateRetrievalAsync", "RetrievalActivationService");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotReferenceModelClient() => AssertCoreFileDoesNotContain("IAgentModelAdapter", "IModelClient", "OpenAI", "ChatCompletion");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotReferenceSourceApply() => AssertCoreFileDoesNotContain("ApplySource", "SourceApplyService", "MutateSourceAsync");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotReferenceWorkflowRunner() => AssertCoreFileDoesNotContain("WorkflowRunner", "ContinueWorkflowAsync", "DispatchWorkflow");
    [TestMethod] public void MemoryProposalConflictDetection_DoesNotReferenceAgentDispatcher() => AssertCoreFileDoesNotContain("AgentDispatcher", "DispatchAgent", "IAgentRuntime");

    private static IReadOnlyList<MemoryProposalConflictCandidateCreateRequest> Detect(params MemoryProposal[] proposals) => new MemoryProposalConflictDetector().Detect(proposals);
    private static MemoryProposalConflictCandidate NormalizedDetectedCandidate() => new MemoryProposalConflictCandidateValidator().Normalize(Detect(Proposal(PrimaryProposalId, "Workflow checkpoints are resumable workflow state markers."), Proposal(ConflictingProposalId, "Workflow checkpoints are not resumable workflow state markers."))[0]);

    private static MemoryProposalConflictCandidateCreateRequest CreateRequest(Guid? projectId = null, Guid? primaryProposalId = null, Guid? conflictingProposalId = null, string candidateKey = "memory-proposal-conflict:pr111", MemoryProposalConflictCandidateStatus status = MemoryProposalConflictCandidateStatus.ReadyForReview, MemoryProposalConflictType conflictType = MemoryProposalConflictType.DirectContradictionCandidate, decimal conflictScore = 0.90m, MemoryProposalConflictBand conflictBand = MemoryProposalConflictBand.DirectContradiction, string primarySummary = "Workflow checkpoints are resumable workflow state markers.", string conflictingSummary = "Workflow checkpoints are not resumable workflow state markers.", string? reasonSummary = "The staged proposals contain direct contradiction wording.", bool choosesTruth = false, IReadOnlyList<MemoryProposalConflictEvidenceReference>? evidenceReferences = null, IReadOnlyList<MemoryProposalConflictReviewNote>? reviewNotes = null) => new()
    {
        MemoryProposalConflictCandidateId = ConflictCandidateId,
        ProjectId = projectId ?? ProjectId,
        PrimaryMemoryProposalId = primaryProposalId ?? PrimaryProposalId,
        ConflictingMemoryProposalId = conflictingProposalId ?? ConflictingProposalId,
        ConflictCandidateKey = candidateKey,
        Status = status,
        ConflictType = conflictType,
        ConflictScore = conflictScore,
        ConflictBand = conflictBand,
        SafePrimarySummary = primarySummary,
        SafeConflictingSummary = conflictingSummary,
        SafeConflictSummary = reasonSummary,
        SafeReviewRecommendation = "Human review remains required before any conflict decision.",
        EvidenceReferences = evidenceReferences ?? new[] { Evidence() },
        ReviewNotes = reviewNotes ?? new[] { ReviewNote() },
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-conflict-detection-tests",
        MetadataVersion = 1,
        MetadataJson = "{\"source\":\"pr111-test\"}",
        ChoosesTruth = choosesTruth
    };

    private static MemoryProposalConflictEvidenceReference Evidence(bool evidenceIsDecision = false, bool evidenceChoosesTruth = false, bool evidenceRejectsProposal = false, bool evidenceDeletesProposal = false, bool evidenceCorrectsProposal = false, bool evidenceAcceptsMemory = false) => new()
    {
        EvidenceType = "MemoryProposal",
        EvidenceId = PrimaryProposalId.ToString(),
        EvidenceLabel = "Primary memory proposal",
        SafeSummary = "Staged proposal evidence supports conflict review only.",
        AllowedUse = "ConflictReview",
        PrimaryMemoryProposalId = PrimaryProposalId,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        EvidenceIsDecision = evidenceIsDecision,
        EvidenceChoosesTruth = evidenceChoosesTruth,
        EvidenceRejectsProposal = evidenceRejectsProposal,
        EvidenceDeletesProposal = evidenceDeletesProposal,
        EvidenceCorrectsProposal = evidenceCorrectsProposal,
        EvidenceAcceptsMemory = evidenceAcceptsMemory
    };

    private static MemoryProposalConflictReviewNote ReviewNote(string noteType = "ContradictionReason", bool noteIsDecision = false, bool noteChoosesTruth = false, bool noteRejectsProposal = false, bool noteDeletesProposal = false, bool noteCorrectsProposal = false, bool noteAcceptsMemory = false) => new()
    {
        NoteType = noteType,
        SafeSummary = "Conflict score is advisory review evidence only.",
        Severity = "warning",
        NoteIsDecision = noteIsDecision,
        NoteChoosesTruth = noteChoosesTruth,
        NoteRejectsProposal = noteRejectsProposal,
        NoteDeletesProposal = noteDeletesProposal,
        NoteCorrectsProposal = noteCorrectsProposal,
        NoteAcceptsMemory = noteAcceptsMemory
    };

    private static MemoryProposal Proposal(Guid id, string safeMemory) => new()
    {
        MemoryProposalId = id,
        ProjectId = ProjectId,
        ProposalKey = "proposal-" + id.ToString("N")[..8],
        ProposalType = MemoryProposalType.ProjectFactCandidate,
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate,
        ProposalStatus = MemoryProposalStatus.Staged,
        SourceType = "MemoryProposalConflictDetectionTests",
        SubjectType = "workflow-boundary",
        SubjectId = "workflow-checkpoint-boundary",
        SafeProposedMemory = safeMemory,
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential,
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-conflict-detection-tests",
        MetadataVersion = 1,
        MetadataJson = "{}"
    };

    private static void AssertInvalid(MemoryProposalConflictCandidateCreateRequest request, string expectedCode)
    {
        var result = new MemoryProposalConflictCandidateValidator().Validate(request);
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), "Expected issue code: " + expectedCode + ". Actual: " + string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    private static void AssertCandidateHasNoAuthority(MemoryProposalConflictCandidate candidate)
    {
        Assert.IsFalse(candidate.IsDecision);
        Assert.IsFalse(candidate.ChoosesTruth);
        Assert.IsFalse(candidate.RejectsProposal);
        Assert.IsFalse(candidate.DeletesProposal);
        Assert.IsFalse(candidate.CorrectsProposal);
        Assert.IsFalse(candidate.MergesProposal);
        Assert.IsFalse(candidate.AcceptsMemory);
        Assert.IsFalse(candidate.PromotesMemory);
        Assert.IsFalse(candidate.CreatesAcceptedMemory);
        Assert.IsFalse(candidate.ActivatesRetrieval);
        Assert.IsFalse(candidate.CreatesEmbedding);
        Assert.IsFalse(candidate.WritesVectorStore);
        Assert.IsFalse(candidate.SatisfiesPolicy);
        Assert.IsFalse(candidate.TransfersAuthority);
    }

    private static void AssertCoreFileDoesNotContain(params string[] forbiddenTokens)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IronDev.Core", "AgentMemory", "MemoryProposalConflictDetectionModels.cs"));
        AssertNoForbiddenTokens(text, forbiddenTokens);
    }

    private static void AssertDirectoryDoesNotContain(string directory, params string[] forbiddenTokens)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", directory);
        if (!Directory.Exists(root)) return;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(file => !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
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
