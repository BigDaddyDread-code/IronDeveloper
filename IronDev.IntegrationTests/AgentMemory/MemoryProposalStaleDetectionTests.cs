using IronDev.Core.AgentMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("MemoryProposalStaleDetection")]
public sealed class MemoryProposalStaleDetectionTests
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-eeee-4444-8888-990000000110");
    private static readonly Guid ProposalId = Guid.Parse("20000000-eeee-4444-8888-990000000110");
    private static readonly Guid RelatedProposalId = Guid.Parse("30000000-eeee-4444-8888-990000000110");
    private static readonly Guid StaleCandidateId = Guid.Parse("40000000-eeee-4444-8888-990000000110");
    private static readonly Guid WorkflowRunId = Guid.Parse("50000000-eeee-4444-8888-990000000110");
    private static readonly Guid WorkflowRunStepId = Guid.Parse("60000000-eeee-4444-8888-990000000110");
    private static readonly Guid WorkflowCheckpointId = Guid.Parse("70000000-eeee-4444-8888-990000000110");
    private static readonly DateTimeOffset Now = new(2026, 06, 14, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void MemoryProposalStaleCandidate_ExposesExpectedShape()
    {
        var candidate = new MemoryProposalStaleCandidateValidator().Normalize(CreateRequest());

        Assert.AreEqual(StaleCandidateId, candidate.MemoryProposalStaleCandidateId);
        Assert.AreEqual(ProjectId, candidate.ProjectId);
        Assert.AreEqual(ProposalId, candidate.MemoryProposalId);
        Assert.AreEqual(MemoryProposalStaleCandidateStatus.ReadyForReview, candidate.Status);
        Assert.AreEqual(MemoryProposalStaleReasonType.AgeCandidate, candidate.ReasonType);
        Assert.AreEqual(MemoryProposalStalenessBand.AgeOnlyCandidate, candidate.StalenessBand);
        Assert.AreEqual(1, candidate.EvidenceReferences.Count);
        Assert.AreEqual(1, candidate.ReviewNotes.Count);
        AssertCandidateHasNoAuthority(candidate);
    }

    [TestMethod]
    public void MemoryProposalStaleCandidate_UsesBoundedReasonTypes()
    {
        var names = Enum.GetNames<MemoryProposalStaleReasonType>();
        CollectionAssert.AreEquivalent(
            new[] { "AgeCandidate", "SupersededByProposalCandidate", "ContradictedByProposalCandidate", "DeprecatedTermCandidate", "DeprecatedDecisionCandidate", "ObsoleteWorkflowStateCandidate", "MissingCurrentEvidenceCandidate", "ConflictingEvidenceCandidate", "ProjectScopeChangedCandidate", "PolicyShapeChangedCandidate", "ImplementationChangedCandidate", "NeedsHumanFreshnessReview" },
            names);
        AssertNoForbiddenTokens(string.Join("\n", names), "ConfirmedStale", "RejectedAsStale", "DeletedAsStale", "CorrectedMemory", "TruthUpdated", "MemoryAccepted", "MemoryPromoted", "RetrievalActivated");
    }

    [TestMethod]
    public void MemoryProposalStaleCandidate_UsesBoundedStatuses()
    {
        var names = Enum.GetNames<MemoryProposalStaleCandidateStatus>();
        CollectionAssert.AreEquivalent(
            new[] { "Detected", "ReadyForReview", "NeedsEvidence", "NeedsHumanReview", "Quarantined", "Superseded", "Withdrawn" },
            names);
        AssertNoForbiddenTokens(string.Join("\n", names), "Confirmed", "Rejected", "Deleted", "Corrected", "Accepted", "Approved", "Promoted", "Resolved", "Indexed", "Embedded", "Retrievable", "PolicySatisfied", "AuthorityTransferred");
    }

    [TestMethod]
    public void MemoryProposalStaleCandidate_UsesBoundedStalenessBands()
    {
        var names = Enum.GetNames<MemoryProposalStalenessBand>();
        CollectionAssert.AreEquivalent(
            new[] { "HighStalenessRisk", "MediumStalenessRisk", "LowStalenessRisk", "ContradictionCandidate", "SupersessionCandidate", "AgeOnlyCandidate", "Unknown" },
            names);
        AssertNoForbiddenTokens(string.Join("\n", names), "ConfirmedStale", "AutoReject", "AutoDelete", "AutoCorrect", "TruthChanged", "MemoryInvalid", "MemoryRemoved");
    }

    [TestMethod]
    public void MemoryProposalStaleCandidate_AuthorityFlagsAreFalse()
    {
        AssertCandidateHasNoAuthority(new MemoryProposalStaleCandidateValidator().Normalize(CreateRequest()));
    }

    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsEmptyProjectId() => AssertInvalid(CreateRequest(projectId: Guid.Empty), "project_id_required");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsEmptyMemoryProposalId() => AssertInvalid(CreateRequest(memoryProposalId: Guid.Empty), "memory_proposal_id_required");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsBlankCandidateKey() => AssertInvalid(CreateRequest(candidateKey: " "), "stale_candidate_key_required");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsForbiddenReasonType() => AssertInvalid(CreateRequest(reasonType: (MemoryProposalStaleReasonType)999), "reason_type_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsForbiddenStatus() => AssertInvalid(CreateRequest(status: (MemoryProposalStaleCandidateStatus)999), "status_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsInvalidStalenessScore() => AssertInvalid(CreateRequest(stalenessScore: 1.1m), "staleness_score_invalid");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsForbiddenStalenessBand() => AssertInvalid(CreateRequest(stalenessBand: (MemoryProposalStalenessBand)999), "staleness_band_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsHiddenReasoning() => AssertInvalid(CreateRequest(proposalSummary: "hiddenReasoning leaked"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsRawPrompt() => AssertInvalid(CreateRequest(proposalSummary: "rawPrompt leaked"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsRawCompletion() => AssertInvalid(CreateRequest(proposalSummary: "rawCompletion leaked"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsRawToolOutput() => AssertInvalid(CreateRequest(proposalSummary: "rawToolOutput leaked"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsEntirePatch() => AssertInvalid(CreateRequest(proposalSummary: "entirePatch leaked"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsStaleDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "confirmed stale"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsRejectDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "reject as stale"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsDeleteDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "delete stale"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsCorrectionDecisionLanguage() => AssertInvalid(CreateRequest(reasonSummary: "corrected memory"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsAcceptedMemoryLanguage() => AssertInvalid(CreateRequest(reasonSummary: "accepted memory"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsPromotedMemoryLanguage() => AssertInvalid(CreateRequest(reasonSummary: "promoted memory"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsRetrievalActiveLanguage() => AssertInvalid(CreateRequest(reasonSummary: "retrieval active"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsEmbeddingLanguage() => AssertInvalid(CreateRequest(reasonSummary: "embedding created"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsVectorWriteLanguage() => AssertInvalid(CreateRequest(reasonSummary: "vector write"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsTruthUpdatedLanguage() => AssertInvalid(CreateRequest(reasonSummary: "truth updated"), "stale_candidate_text_unsafe");
    [TestMethod] public void MemoryProposalStaleCandidateValidator_RejectsTrueAuthorityFlags() => AssertInvalid(CreateRequest(deletesProposal: true), "authority_flags_forbidden");

    [TestMethod] public void MemoryProposalStaleCandidate_EvidenceReferenceIsNotDecision() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceIsDecision: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidate_EvidenceReferenceDoesNotRejectProposal() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceRejectsProposal: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidate_EvidenceReferenceDoesNotDeleteProposal() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceDeletesProposal: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidate_EvidenceReferenceDoesNotCorrectProposal() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceCorrectsProposal: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidate_EvidenceReferenceDoesNotAcceptMemory() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceAcceptsMemory: true) }), "evidence_authority_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidate_ReviewNoteIsNotDecision() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteIsDecision: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidate_ReviewNoteDoesNotRejectProposal() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteRejectsProposal: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidate_ReviewNoteDoesNotDeleteProposal() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteDeletesProposal: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidate_ReviewNoteDoesNotCorrectProposal() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteCorrectsProposal: true) }), "review_note_authority_forbidden");
    [TestMethod] public void MemoryProposalStaleCandidate_ReviewNoteDoesNotAcceptMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteAcceptsMemory: true) }), "review_note_authority_forbidden");

    [TestMethod]
    public void MemoryProposalStaleDetector_DetectsAgeCandidate()
    {
        var candidates = Detect(Proposal(ProposalId, "Use append-only governance receipts for audit review.", createdUtc: Now.AddDays(-300), evidenceUtc: Now.AddDays(-1)));
        Assert.IsTrue(candidates.Any(candidate => candidate.ReasonType == MemoryProposalStaleReasonType.AgeCandidate));
    }

    [TestMethod]
    public void MemoryProposalStaleDetector_DetectsDeprecatedTermCandidate()
    {
        var candidates = Detect(Proposal(ProposalId, "Use the legacy workflow table name during diagnostics.", createdUtc: Now.AddDays(-1), evidenceUtc: Now.AddDays(-1)));
        Assert.IsTrue(candidates.Any(candidate => candidate.ReasonType == MemoryProposalStaleReasonType.ObsoleteWorkflowStateCandidate));
    }

    [TestMethod]
    public void MemoryProposalStaleDetector_DetectsSupersededByProposalCandidate()
    {
        var candidates = Detect(
            Proposal(ProposalId, "Use workflow run table for state inspection.", subjectId: "workflow-state", createdUtc: Now.AddDays(-20), evidenceUtc: Now.AddDays(-1)),
            Proposal(RelatedProposalId, "Now use the current workflow run read model for state inspection.", subjectId: "workflow-state", createdUtc: Now.AddDays(-1), evidenceUtc: Now.AddDays(-1)));

        var candidate = candidates.Single(item => item.ReasonType == MemoryProposalStaleReasonType.SupersededByProposalCandidate);
        Assert.AreEqual(RelatedProposalId, candidate.SupersedingMemoryProposalId);
        Assert.AreEqual(MemoryProposalStalenessBand.SupersessionCandidate, candidate.StalenessBand);
    }

    [TestMethod]
    public void MemoryProposalStaleDetector_DetectsContradictedByProposalCandidate()
    {
        var candidates = Detect(
            Proposal(ProposalId, "Use append-only governance receipts for audit review.", subjectId: "governance-receipts", createdUtc: Now.AddDays(-20), evidenceUtc: Now.AddDays(-1)),
            Proposal(RelatedProposalId, "Do not use append-only governance receipts for audit review.", subjectId: "governance-receipts", createdUtc: Now.AddDays(-1), evidenceUtc: Now.AddDays(-1)));

        var candidate = candidates.Single(item => item.ReasonType == MemoryProposalStaleReasonType.ContradictedByProposalCandidate);
        Assert.AreEqual(RelatedProposalId, candidate.ContradictingMemoryProposalId);
        Assert.AreEqual(MemoryProposalStalenessBand.ContradictionCandidate, candidate.StalenessBand);
    }

    [TestMethod]
    public void MemoryProposalStaleDetector_DetectsMissingCurrentEvidenceCandidate()
    {
        var candidates = Detect(Proposal(ProposalId, "Use governed memory proposal evidence packages for review.", createdUtc: Now.AddDays(-1), evidenceUtc: null));
        Assert.IsTrue(candidates.Any(candidate => candidate.ReasonType == MemoryProposalStaleReasonType.MissingCurrentEvidenceCandidate));
    }

    [TestMethod]
    public void MemoryProposalStaleDetector_CapsCandidateCount()
    {
        var proposals = new[]
        {
            Proposal(Guid.Parse("10000000-aaaa-4444-8888-990000000110"), "Use old workflow table for diagnostics.", createdUtc: Now.AddDays(-300), evidenceUtc: null),
            Proposal(Guid.Parse("20000000-aaaa-4444-8888-990000000110"), "Use legacy governance table for diagnostics.", createdUtc: Now.AddDays(-300), evidenceUtc: null)
        };

        var candidates = new MemoryProposalStaleDetector().Detect(proposals, Options(maxCandidateCount: 2));
        Assert.AreEqual(2, candidates.Count);
    }

    [TestMethod]
    public void MemoryProposalStaleDetector_DoesNotMutateInputProposals()
    {
        var proposal = Proposal(ProposalId, "Use old workflow table for diagnostics.", createdUtc: Now.AddDays(-300), evidenceUtc: null);
        var originalStatus = proposal.ProposalStatus;
        var originalText = proposal.SafeProposedMemory;

        _ = Detect(proposal);

        Assert.AreEqual(originalStatus, proposal.ProposalStatus);
        Assert.AreEqual(originalText, proposal.SafeProposedMemory);
        Assert.IsFalse(proposal.PromotesMemory);
        Assert.IsFalse(proposal.IsAcceptedMemory);
    }

    [TestMethod] public void MemoryProposalStaleDetector_DoesNotRejectProposal() => Assert.IsFalse(NormalizedDetectedCandidate().RejectsProposal);
    [TestMethod] public void MemoryProposalStaleDetector_DoesNotDeleteProposal() => Assert.IsFalse(NormalizedDetectedCandidate().DeletesProposal);
    [TestMethod] public void MemoryProposalStaleDetector_DoesNotCorrectProposal() => Assert.IsFalse(NormalizedDetectedCandidate().CorrectsProposal);
    [TestMethod] public void MemoryProposalStaleDetector_DoesNotAcceptMemory() => Assert.IsFalse(NormalizedDetectedCandidate().AcceptsMemory);
    [TestMethod] public void MemoryProposalStaleDetector_DoesNotPromoteMemory() => Assert.IsFalse(NormalizedDetectedCandidate().PromotesMemory);
    [TestMethod] public void MemoryProposalStaleDetector_DoesNotActivateRetrieval() => Assert.IsFalse(NormalizedDetectedCandidate().ActivatesRetrieval);

    [TestMethod] public void MemoryProposalStaleDetection_DoesNotAddSqlMigration() => AssertNoFileNameContains("Database", "memory_proposal_stale", "stale_detection");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotAddApiEndpoint() => AssertDirectoryDoesNotContain("IronDev.Api", "MemoryProposalStale", "memory-proposal-stale", "stale-detection");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotAddCliCommand() => AssertDirectoryDoesNotContain("tools", "MemoryProposalStale", "memory-proposal-stale", "stale-detection");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotAddAcceptedMemoryStore() => AssertCoreFileDoesNotContain("IAcceptedMemoryStore", "AcceptedMemoryStore", "CreateAcceptedMemoryAsync");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotAddPromotionPath() => AssertCoreFileDoesNotContain("IMemoryPromotion", "PromoteMemoryAsync", "PromotionService");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotAddEmbeddingWriter() => AssertCoreFileDoesNotContain("IEmbeddingWriter", "CreateEmbeddingAsync", "EmbeddingClient");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotAddVectorStoreWriter() => AssertCoreFileDoesNotContain("IVectorStoreWriter", "WriteVectorStoreAsync", "VectorStoreClient");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotReferenceWeaviateWrite() => AssertCoreFileDoesNotContain("IWeaviate", "WeaviateClient", "WriteWeaviateAsync");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotReferenceRetrievalActivation() => AssertCoreFileDoesNotContain("IRetrievalActivation", "ActivateRetrievalAsync", "RetrievalActivationService");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotReferenceModelClient() => AssertCoreFileDoesNotContain("IAgentModelAdapter", "IModelClient", "OpenAI", "ChatCompletion");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotReferenceSourceApply() => AssertCoreFileDoesNotContain("ApplySource", "SourceApplyService", "MutateSourceAsync");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotReferenceWorkflowRunner() => AssertCoreFileDoesNotContain("WorkflowRunner", "ContinueWorkflowAsync", "DispatchWorkflow");
    [TestMethod] public void MemoryProposalStaleDetection_DoesNotReferenceAgentDispatcher() => AssertCoreFileDoesNotContain("AgentDispatcher", "DispatchAgent", "IAgentRuntime");

    private static IReadOnlyList<MemoryProposalStaleCandidateCreateRequest> Detect(params MemoryProposal[] proposals) =>
        new MemoryProposalStaleDetector().Detect(proposals, Options());

    private static MemoryProposalStaleCandidate NormalizedDetectedCandidate() =>
        new MemoryProposalStaleCandidateValidator().Normalize(Detect(Proposal(ProposalId, "Use old workflow table for diagnostics.", createdUtc: Now.AddDays(-300), evidenceUtc: null))[0]);

    private static MemoryProposalStaleDetectionOptions Options(int maxCandidateCount = 50) => new()
    {
        CurrentUtc = Now,
        AgeThreshold = TimeSpan.FromDays(180),
        CurrentEvidenceThreshold = TimeSpan.FromDays(90),
        MaxCandidateCount = maxCandidateCount,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-stale-detection-tests"
    };

    private static MemoryProposalStaleCandidateCreateRequest CreateRequest(
        Guid? projectId = null,
        Guid? memoryProposalId = null,
        string candidateKey = "memory-proposal-stale:pr110",
        MemoryProposalStaleCandidateStatus status = MemoryProposalStaleCandidateStatus.ReadyForReview,
        MemoryProposalStaleReasonType reasonType = MemoryProposalStaleReasonType.AgeCandidate,
        decimal stalenessScore = 0.45m,
        MemoryProposalStalenessBand stalenessBand = MemoryProposalStalenessBand.AgeOnlyCandidate,
        string proposalSummary = "Use old workflow table for diagnostics.",
        string? reasonSummary = "Proposal age exceeds configured threshold.",
        bool deletesProposal = false,
        IReadOnlyList<MemoryProposalStaleEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<MemoryProposalStaleReviewNote>? reviewNotes = null) => new()
    {
        MemoryProposalStaleCandidateId = StaleCandidateId,
        ProjectId = projectId ?? ProjectId,
        MemoryProposalId = memoryProposalId ?? ProposalId,
        StaleCandidateKey = candidateKey,
        Status = status,
        ReasonType = reasonType,
        StalenessScore = stalenessScore,
        StalenessBand = stalenessBand,
        SafeProposalSummary = proposalSummary,
        SafeReasonSummary = reasonSummary,
        SafeFreshnessRiskSummary = "Freshness risk requires human review before any memory decision.",
        SafeReviewRecommendation = "Human review remains required before correction, rejection, deletion, acceptance, or promotion.",
        ProposalCreatedUtc = Now.AddDays(-300),
        LastSupportingEvidenceUtc = Now.AddDays(-200),
        EvidenceReferences = evidenceReferences ?? new[] { Evidence() },
        ReviewNotes = reviewNotes ?? new[] { ReviewNote() },
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-stale-detection-tests",
        MetadataVersion = 1,
        MetadataJson = "{\"source\":\"pr110-test\"}",
        DeletesProposal = deletesProposal
    };

    private static MemoryProposalStaleEvidenceReference Evidence(
        bool evidenceIsDecision = false,
        bool evidenceRejectsProposal = false,
        bool evidenceDeletesProposal = false,
        bool evidenceCorrectsProposal = false,
        bool evidenceAcceptsMemory = false) => new()
    {
        EvidenceType = "MemoryProposal",
        EvidenceId = ProposalId.ToString(),
        EvidenceLabel = "Staged memory proposal",
        SafeSummary = "Staged proposal evidence supports freshness review only.",
        AllowedUse = "StaleReview",
        MemoryProposalId = ProposalId,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        EvidenceIsDecision = evidenceIsDecision,
        EvidenceRejectsProposal = evidenceRejectsProposal,
        EvidenceDeletesProposal = evidenceDeletesProposal,
        EvidenceCorrectsProposal = evidenceCorrectsProposal,
        EvidenceAcceptsMemory = evidenceAcceptsMemory
    };

    private static MemoryProposalStaleReviewNote ReviewNote(
        string noteType = "AgeRisk",
        bool noteIsDecision = false,
        bool noteRejectsProposal = false,
        bool noteDeletesProposal = false,
        bool noteCorrectsProposal = false,
        bool noteAcceptsMemory = false) => new()
    {
        NoteType = noteType,
        SafeSummary = "Staleness score is advisory review evidence only.",
        Severity = "warning",
        NoteIsDecision = noteIsDecision,
        NoteRejectsProposal = noteRejectsProposal,
        NoteDeletesProposal = noteDeletesProposal,
        NoteCorrectsProposal = noteCorrectsProposal,
        NoteAcceptsMemory = noteAcceptsMemory
    };

    private static MemoryProposal Proposal(
        Guid id,
        string safeMemory,
        string subjectId = "subject-110",
        DateTimeOffset? createdUtc = null,
        DateTimeOffset? evidenceUtc = null) => new()
    {
        MemoryProposalId = id,
        ProjectId = ProjectId,
        ProposalKey = "proposal-" + id.ToString("N")[..8],
        ProposalType = MemoryProposalType.FailureModeCandidate,
        TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate,
        ProposalStatus = MemoryProposalStatus.Staged,
        SourceType = "MemoryProposalStaleDetectionTests",
        SubjectType = "MemoryFreshnessSubject",
        SubjectId = subjectId,
        SafeProposedMemory = safeMemory,
        ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential,
        SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview,
        WorkflowRunId = WorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "memory-proposal-stale-detection-tests",
        MetadataVersion = 1,
        MetadataJson = "{}",
        CreatedUtc = createdUtc ?? Now,
        EvidenceReferences = evidenceUtc is null
            ? Array.Empty<MemoryProposalEvidenceReference>()
            : new[]
            {
                new MemoryProposalEvidenceReference
                {
                    MemoryProposalEvidenceReferenceId = Guid.NewGuid(),
                    MemoryProposalId = id,
                    ProjectId = ProjectId,
                    EvidenceType = MemoryProposalEvidenceType.HumanNote,
                    EvidenceId = "evidence-" + id.ToString("N")[..8],
                    SafeSummary = "Evidence supports review only.",
                    AllowedUse = MemoryProposalEvidenceAllowedUse.MemoryProposalReview,
                    CreatedUtc = evidenceUtc.Value
                }
            }
    };

    private static void AssertInvalid(MemoryProposalStaleCandidateCreateRequest request, string expectedCode)
    {
        var result = new MemoryProposalStaleCandidateValidator().Validate(request);
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), "Expected issue code: " + expectedCode + ". Actual: " + string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    private static void AssertCandidateHasNoAuthority(MemoryProposalStaleCandidate candidate)
    {
        Assert.IsFalse(candidate.IsDecision);
        Assert.IsFalse(candidate.RejectsProposal);
        Assert.IsFalse(candidate.DeletesProposal);
        Assert.IsFalse(candidate.CorrectsProposal);
        Assert.IsFalse(candidate.MarksProposalStale);
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
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IronDev.Core", "AgentMemory", "MemoryProposalStaleDetectionModels.cs"));
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
