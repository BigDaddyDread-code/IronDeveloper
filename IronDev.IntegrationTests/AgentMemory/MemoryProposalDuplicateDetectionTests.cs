using IronDev.Core.AgentMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("MemoryProposalDuplicateDetection")]
public sealed class MemoryProposalDuplicateDetectionTests
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-eeee-4444-8888-990000000109");
    private static readonly Guid PrimaryProposalId = Guid.Parse("20000000-eeee-4444-8888-990000000109");
    private static readonly Guid CandidateProposalId = Guid.Parse("30000000-eeee-4444-8888-990000000109");
    private static readonly Guid DuplicateCandidateId = Guid.Parse("40000000-eeee-4444-8888-990000000109");
    private static readonly Guid WorkflowRunId = Guid.Parse("50000000-eeee-4444-8888-990000000109");
    private static readonly Guid WorkflowRunStepId = Guid.Parse("60000000-eeee-4444-8888-990000000109");
    private static readonly Guid WorkflowCheckpointId = Guid.Parse("70000000-eeee-4444-8888-990000000109");

    [TestMethod]
    public void MemoryProposalDuplicateCandidate_ExposesExpectedShape()
    {
        var candidate = new MemoryProposalDuplicateCandidateValidator().Normalize(CreateRequest());

        Assert.AreEqual(DuplicateCandidateId, candidate.MemoryProposalDuplicateCandidateId);
        Assert.AreEqual(ProjectId, candidate.ProjectId);
        Assert.AreEqual(PrimaryProposalId, candidate.PrimaryMemoryProposalId);
        Assert.AreEqual(CandidateProposalId, candidate.CandidateMemoryProposalId);
        Assert.AreEqual(MemoryProposalDuplicateCandidateStatus.ReadyForReview, candidate.Status);
        Assert.AreEqual(MemoryProposalDuplicateRelationshipType.NearDuplicateCandidate, candidate.RelationshipType);
        Assert.AreEqual(MemoryProposalDuplicateSimilarityBand.HighSimilarity, candidate.SimilarityBand);
        Assert.AreEqual(1, candidate.EvidenceReferences.Count);
        Assert.AreEqual(1, candidate.ReviewNotes.Count);
        AssertCandidateHasNoAuthority(candidate);
    }

    [TestMethod]
    public void MemoryProposalDuplicateCandidate_UsesBoundedRelationshipTypes()
    {
        var names = Enum.GetNames<MemoryProposalDuplicateRelationshipType>();
        CollectionAssert.AreEquivalent(
            new[] { "ExactTextCandidate", "NearDuplicateCandidate", "SameDecisionCandidate", "SameFactCandidate", "SameRiskCandidate", "SameConventionCandidate", "ContradictoryCandidate", "OverlappingCandidate", "RelatedButDistinctCandidate", "NeedsHumanReview" },
            names);
        AssertNoForbiddenTokens(string.Join("\n", names), "ConfirmedDuplicate", "MergedDuplicate", "RejectedDuplicate", "AcceptedDuplicate", "TruthSelected", "MemoryAccepted", "MemoryPromoted", "RetrievalActivated");
    }

    [TestMethod]
    public void MemoryProposalDuplicateCandidate_UsesBoundedStatuses()
    {
        var names = Enum.GetNames<MemoryProposalDuplicateCandidateStatus>();
        CollectionAssert.AreEquivalent(
            new[] { "Detected", "ReadyForReview", "NeedsEvidence", "NeedsHumanReview", "Quarantined", "Superseded", "Withdrawn" },
            names);
        AssertNoForbiddenTokens(string.Join("\n", names), "Confirmed", "Merged", "Rejected", "Accepted", "Approved", "Promoted", "Deleted", "Indexed", "Embedded", "Retrievable", "PolicySatisfied", "AuthorityTransferred");
    }

    [TestMethod]
    public void MemoryProposalDuplicateCandidate_UsesBoundedSimilarityBands()
    {
        var names = Enum.GetNames<MemoryProposalDuplicateSimilarityBand>();
        CollectionAssert.AreEquivalent(
            new[] { "ExactText", "HighSimilarity", "MediumSimilarity", "LowSimilarity", "RelatedOnly", "ContradictionCandidate", "Unknown" },
            names);
        AssertNoForbiddenTokens(string.Join("\n", names), "ApprovedDuplicate", "RejectedDuplicate", "MergeAllowed", "AutoMerge", "AutoReject", "TruthWinner");
    }

    [TestMethod]
    public void MemoryProposalDuplicateCandidate_AuthorityFlagsAreFalse()
    {
        AssertCandidateHasNoAuthority(new MemoryProposalDuplicateCandidateValidator().Normalize(CreateRequest()));
    }

    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsEmptyProjectId() => AssertInvalid(CreateRequest(projectId: Guid.Empty), "project_id_required");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsEmptyPrimaryProposalId() => AssertInvalid(CreateRequest(primaryProposalId: Guid.Empty), "primary_memory_proposal_id_required");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsEmptyCandidateProposalId() => AssertInvalid(CreateRequest(candidateProposalId: Guid.Empty), "candidate_memory_proposal_id_required");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsSamePrimaryAndCandidate() => AssertInvalid(CreateRequest(primaryProposalId: PrimaryProposalId, candidateProposalId: PrimaryProposalId), "proposal_ids_must_differ");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsBlankCandidateKey() => AssertInvalid(CreateRequest(candidateKey: " "), "duplicate_candidate_key_required");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsForbiddenRelationshipType() => AssertInvalid(CreateRequest(relationshipType: (MemoryProposalDuplicateRelationshipType)999), "relationship_type_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsForbiddenStatus() => AssertInvalid(CreateRequest(status: (MemoryProposalDuplicateCandidateStatus)999), "status_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsInvalidSimilarityScore() => AssertInvalid(CreateRequest(similarityScore: 1.1m), "similarity_score_invalid");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsForbiddenSimilarityBand() => AssertInvalid(CreateRequest(similarityBand: (MemoryProposalDuplicateSimilarityBand)999), "similarity_band_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsHiddenReasoning() => AssertInvalid(CreateRequest(primarySummary: "hiddenReasoning leaked"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsRawPrompt() => AssertInvalid(CreateRequest(primarySummary: "rawPrompt leaked"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsRawCompletion() => AssertInvalid(CreateRequest(primarySummary: "rawCompletion leaked"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsRawToolOutput() => AssertInvalid(CreateRequest(primarySummary: "rawToolOutput leaked"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsEntirePatch() => AssertInvalid(CreateRequest(primarySummary: "entirePatch leaked"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsMergeDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "merge approved"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsRejectDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "auto reject"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsAcceptedMemoryLanguage() => AssertInvalid(CreateRequest(reasonSummary: "accepted memory"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsPromotedMemoryLanguage() => AssertInvalid(CreateRequest(reasonSummary: "promoted memory"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsRetrievalActiveLanguage() => AssertInvalid(CreateRequest(reasonSummary: "retrieval active"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsEmbeddingLanguage() => AssertInvalid(CreateRequest(reasonSummary: "embedding created"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsVectorWriteLanguage() => AssertInvalid(CreateRequest(reasonSummary: "vector write"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsTruthWinnerLanguage() => AssertInvalid(CreateRequest(reasonSummary: "truth selected"), "duplicate_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalDuplicateCandidateValidator_RejectsTrueAuthorityFlags() => AssertInvalid(CreateRequest(mergesProposal: true), "authority_flags_forbidden");

    [TestMethod] public void MemoryProposalDuplicateCandidate_EvidenceReferenceIsNotDecision() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceIsDecision: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidate_EvidenceReferenceDoesNotMergeProposal() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceMergesProposal: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidate_EvidenceReferenceDoesNotRejectProposal() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceRejectsProposal: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidate_EvidenceReferenceDoesNotAcceptMemory() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceAcceptsMemory: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidate_ReviewNoteIsNotDecision() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteIsDecision: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidate_ReviewNoteDoesNotMergeProposal() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteMergesProposal: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidate_ReviewNoteDoesNotRejectProposal() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteRejectsProposal: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalDuplicateCandidate_ReviewNoteDoesNotAcceptMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteAcceptsMemory: true) }), "review_note_authority_forbidden");

    [TestMethod]
    public void MemoryProposalDuplicateDetector_DetectsExactTextCandidate()
    {
        var candidates = Detect(
            Proposal(PrimaryProposalId, "Append-only governance events are the audit source of truth."),
            Proposal(CandidateProposalId, "Append-only governance events are the audit source of truth."));

        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(MemoryProposalDuplicateRelationshipType.ExactTextCandidate, candidates[0].RelationshipType);
        Assert.AreEqual(MemoryProposalDuplicateSimilarityBand.ExactText, candidates[0].SimilarityBand);
    }

    [TestMethod]
    public void MemoryProposalDuplicateDetector_DetectsNearDuplicateCandidate()
    {
        var candidates = Detect(
            Proposal(PrimaryProposalId, "Append-only governance event records preserve audit evidence."),
            Proposal(CandidateProposalId, "Append-only governance events preserve audit evidence records."));

        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(MemoryProposalDuplicateRelationshipType.NearDuplicateCandidate, candidates[0].RelationshipType);
        Assert.AreEqual(MemoryProposalDuplicateSimilarityBand.HighSimilarity, candidates[0].SimilarityBand);
    }

    [TestMethod]
    public void MemoryProposalDuplicateDetector_DetectsRelatedButDistinctCandidate()
    {
        var candidates = Detect(
            Proposal(PrimaryProposalId, "Governance review notes include concise reviewer context for later audit."),
            Proposal(CandidateProposalId, "Workflow receipts keep governance review evidence visible to operators during diagnostics."));

        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(MemoryProposalDuplicateRelationshipType.RelatedButDistinctCandidate, candidates[0].RelationshipType);
        Assert.AreEqual(MemoryProposalDuplicateSimilarityBand.RelatedOnly, candidates[0].SimilarityBand);
    }

    [TestMethod]
    public void MemoryProposalDuplicateDetector_DetectsContradictionCandidate()
    {
        var candidates = Detect(
            Proposal(PrimaryProposalId, "Use append-only governance event records for audit evidence."),
            Proposal(CandidateProposalId, "Do not use append-only governance event records for audit evidence."));

        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(MemoryProposalDuplicateRelationshipType.ContradictoryCandidate, candidates[0].RelationshipType);
        Assert.AreEqual(MemoryProposalDuplicateSimilarityBand.ContradictionCandidate, candidates[0].SimilarityBand);
    }

    [TestMethod]
    public void MemoryProposalDuplicateDetector_DoesNotCompareProposalToItself()
    {
        var proposals = new[] { Proposal(PrimaryProposalId, "Append-only governance events preserve audit evidence."), Proposal(PrimaryProposalId, "Append-only governance events preserve audit evidence.") };
        Assert.AreEqual(0, new MemoryProposalDuplicateDetector().Detect(proposals).Count);
    }

    [TestMethod]
    public void MemoryProposalDuplicateDetector_DoesNotReturnReversedDuplicatePairs()
    {
        var candidates = Detect(
            Proposal(PrimaryProposalId, "Append-only governance events preserve audit evidence."),
            Proposal(CandidateProposalId, "Append-only governance events preserve audit evidence."));

        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(PrimaryProposalId, candidates[0].PrimaryMemoryProposalId);
        Assert.AreEqual(CandidateProposalId, candidates[0].CandidateMemoryProposalId);
    }

    [TestMethod]
    public void MemoryProposalDuplicateDetector_CapsCandidateCount()
    {
        var proposals = new[]
        {
            Proposal(Guid.Parse("10000000-aaaa-4444-8888-990000000109"), "Append-only governance events preserve audit evidence."),
            Proposal(Guid.Parse("20000000-aaaa-4444-8888-990000000109"), "Append-only governance events preserve audit evidence."),
            Proposal(Guid.Parse("30000000-aaaa-4444-8888-990000000109"), "Append-only governance events preserve audit evidence.")
        };

        var candidates = new MemoryProposalDuplicateDetector().Detect(proposals, new MemoryProposalDuplicateDetectionOptions { MaxCandidateCount = 2 });
        Assert.AreEqual(2, candidates.Count);
    }

    [TestMethod]
    public void MemoryProposalDuplicateDetector_DoesNotMutateInputProposals()
    {
        var primary = Proposal(PrimaryProposalId, "Append-only governance events preserve audit evidence.");
        var candidate = Proposal(CandidateProposalId, "Append-only governance events preserve audit evidence.");
        var originalStatus = primary.ProposalStatus;
        var originalText = primary.SafeProposedMemory;

        _ = Detect(primary, candidate);

        Assert.AreEqual(originalStatus, primary.ProposalStatus);
        Assert.AreEqual(originalText, primary.SafeProposedMemory);
        Assert.IsFalse(primary.PromotesMemory);
        Assert.IsFalse(primary.IsAcceptedMemory);
    }

    [TestMethod] public void MemoryProposalDuplicateDetector_DoesNotAcceptMemory() => Assert.IsFalse(NormalizedDetectedCandidate().AcceptsMemory);
    [TestMethod] public void MemoryProposalDuplicateDetector_DoesNotRejectProposal() => Assert.IsFalse(NormalizedDetectedCandidate().RejectsProposal);
    [TestMethod] public void MemoryProposalDuplicateDetector_DoesNotPromoteMemory() => Assert.IsFalse(NormalizedDetectedCandidate().PromotesMemory);
    [TestMethod] public void MemoryProposalDuplicateDetector_DoesNotActivateRetrieval() => Assert.IsFalse(NormalizedDetectedCandidate().ActivatesRetrieval);

    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotAddSqlMigration() => AssertNoFileNameContains("Database", "memory_proposal_duplicate", "duplicate_detection");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotAddApiEndpoint() => AssertDirectoryDoesNotContain("IronDev.Api", "MemoryProposalDuplicate", "memory-proposal-duplicate", "duplicate-detection");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotAddCliCommand() => AssertDirectoryDoesNotContain("tools", "MemoryProposalDuplicate", "memory-proposal-duplicate", "duplicate-detection");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotAddAcceptedMemoryStore() => AssertCoreFileDoesNotContain("IAcceptedMemoryStore", "AcceptedMemoryStore", "CreateAcceptedMemoryAsync");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotAddPromotionPath() => AssertCoreFileDoesNotContain("IMemoryPromotion", "PromoteMemoryAsync", "PromotionService");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotAddEmbeddingWriter() => AssertCoreFileDoesNotContain("IEmbeddingWriter", "CreateEmbeddingAsync", "EmbeddingClient");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotAddVectorStoreWriter() => AssertCoreFileDoesNotContain("IVectorStoreWriter", "WriteVectorStoreAsync", "VectorStoreClient");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotReferenceWeaviateWrite() => AssertCoreFileDoesNotContain("IWeaviate", "WeaviateClient", "WriteWeaviateAsync");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotReferenceRetrievalActivation() => AssertCoreFileDoesNotContain("IRetrievalActivation", "ActivateRetrievalAsync", "RetrievalActivationService");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotReferenceModelClient() => AssertCoreFileDoesNotContain("IAgentModelAdapter", "IModelClient", "OpenAI", "ChatCompletion");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotReferenceSourceApply() => AssertCoreFileDoesNotContain("ApplySource", "SourceApplyService", "MutateSourceAsync");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotReferenceWorkflowRunner() => AssertCoreFileDoesNotContain("WorkflowRunner", "ContinueWorkflowAsync", "DispatchWorkflow");
    [TestMethod] public void MemoryProposalDuplicateDetection_DoesNotReferenceAgentDispatcher() => AssertCoreFileDoesNotContain("AgentDispatcher", "DispatchAgent", "IAgentRuntime");

    private static IReadOnlyList<MemoryProposalDuplicateCandidateCreateRequest> Detect(params MemoryProposal[] proposals) =>
        new MemoryProposalDuplicateDetector().Detect(proposals);

    private static MemoryProposalDuplicateCandidate NormalizedDetectedCandidate() =>
        new MemoryProposalDuplicateCandidateValidator().Normalize(Detect(
            Proposal(PrimaryProposalId, "Append-only governance events preserve audit evidence."),
            Proposal(CandidateProposalId, "Append-only governance events preserve audit evidence."))[0]);

    private static MemoryProposalDuplicateCandidateCreateRequest CreateRequest(
        Guid? projectId = null,
        Guid? primaryProposalId = null,
        Guid? candidateProposalId = null,
        string candidateKey = "memory-proposal-duplicate:pr109",
        MemoryProposalDuplicateCandidateStatus status = MemoryProposalDuplicateCandidateStatus.ReadyForReview,
        MemoryProposalDuplicateRelationshipType relationshipType = MemoryProposalDuplicateRelationshipType.NearDuplicateCandidate,
        decimal similarityScore = 0.90m,
        MemoryProposalDuplicateSimilarityBand similarityBand = MemoryProposalDuplicateSimilarityBand.HighSimilarity,
        string primarySummary = "Append-only governance events preserve audit evidence.",
        string candidateSummary = "Append-only governance event records preserve audit evidence.",
        string? reasonSummary = "The staged proposals have high normalized token overlap.",
        bool mergesProposal = false,
        IReadOnlyList<MemoryProposalDuplicateEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<MemoryProposalDuplicateReviewNote>? reviewNotes = null) => new()
    {
        MemoryProposalDuplicateCandidateId = DuplicateCandidateId,
        ProjectId = projectId ?? ProjectId,
        PrimaryMemoryProposalId = primaryProposalId ?? PrimaryProposalId,
        CandidateMemoryProposalId = candidateProposalId ?? CandidateProposalId,
        DuplicateCandidateKey = candidateKey,
        Status = status,
        RelationshipType = relationshipType,
        SimilarityScore = similarityScore,
        SimilarityBand = similarityBand,
        SafePrimarySummary = primarySummary,
        SafeCandidateSummary = candidateSummary,
        SafeReasonSummary = reasonSummary,
        SafeDifferenceSummary = "Human review remains required before any duplicate decision.",
        EvidenceReferences = evidenceReferences ?? new[] { Evidence() },
        ReviewNotes = reviewNotes ?? new[] { ReviewNote() },
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-duplicate-detection-tests",
        MetadataVersion = 1,
        MetadataJson = "{\"source\":\"pr109-test\"}",
        MergesProposal = mergesProposal
    };

    private static MemoryProposalDuplicateEvidenceReference Evidence(
        bool evidenceIsDecision = false,
        bool evidenceMergesProposal = false,
        bool evidenceRejectsProposal = false,
        bool evidenceAcceptsMemory = false) => new()
    {
        EvidenceType = "MemoryProposal",
        EvidenceId = PrimaryProposalId.ToString(),
        EvidenceLabel = "Primary memory proposal",
        SafeSummary = "Staged proposal evidence supports duplicate review only.",
        AllowedUse = "DuplicateReview",
        PrimaryMemoryProposalId = PrimaryProposalId,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        EvidenceIsDecision = evidenceIsDecision,
        EvidenceMergesProposal = evidenceMergesProposal,
        EvidenceRejectsProposal = evidenceRejectsProposal,
        EvidenceAcceptsMemory = evidenceAcceptsMemory
    };

    private static MemoryProposalDuplicateReviewNote ReviewNote(
        string noteType = "SimilarityReason",
        bool noteIsDecision = false,
        bool noteMergesProposal = false,
        bool noteRejectsProposal = false,
        bool noteAcceptsMemory = false) => new()
    {
        NoteType = noteType,
        SafeSummary = "Similarity score is advisory review evidence only.",
        Severity = "info",
        NoteIsDecision = noteIsDecision,
        NoteMergesProposal = noteMergesProposal,
        NoteRejectsProposal = noteRejectsProposal,
        NoteAcceptsMemory = noteAcceptsMemory
    };

    private static MemoryProposal Proposal(Guid id, string safeMemory) => new()
    {
        MemoryProposalId = id,
        ProjectId = ProjectId,
        ProposalKey = "proposal-" + id.ToString("N")[..8],
        ProposalType = MemoryProposalType.FailureModeCandidate,
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate,
        ProposalStatus = MemoryProposalStatus.Staged,
        SourceType = "MemoryProposalDuplicateDetectionTests",
        SafeProposedMemory = safeMemory,
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential,
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-duplicate-detection-tests",
        MetadataVersion = 1,
        MetadataJson = "{}"
    };

    private static void AssertInvalid(MemoryProposalDuplicateCandidateCreateRequest request, string expectedCode)
    {
        var result = new MemoryProposalDuplicateCandidateValidator().Validate(request);
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), "Expected issue code: " + expectedCode + ". Actual: " + string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    private static void AssertCandidateHasNoAuthority(MemoryProposalDuplicateCandidate candidate)
    {
        Assert.IsFalse(candidate.IsDecision);
        Assert.IsFalse(candidate.RejectsProposal);
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
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IronDev.Core", "AgentMemory", "MemoryProposalDuplicateDetectionModels.cs"));
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
