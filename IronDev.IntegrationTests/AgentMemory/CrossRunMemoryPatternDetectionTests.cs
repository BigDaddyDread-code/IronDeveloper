using IronDev.Core.AgentMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("CrossRunMemoryPatternDetection")]
public sealed class CrossRunMemoryPatternDetectionTests
{
    private static readonly Guid ProjectId = Id(112);
    private static readonly Guid PatternCandidateId = Id(120);
    private static readonly Guid FirstProposalId = Id(121);
    private static readonly Guid SecondProposalId = Id(122);
    private static readonly Guid ThirdProposalId = Id(123);
    private static readonly Guid FirstWorkflowRunId = Id(131);
    private static readonly Guid SecondWorkflowRunId = Id(132);
    private static readonly Guid ThirdWorkflowRunId = Id(133);
    private static readonly Guid WorkflowRunStepId = Id(141);
    private static readonly Guid WorkflowCheckpointId = Id(151);

    [TestMethod]
    public void CrossRunMemoryPatternCandidate_ExposesExpectedShape()
    {
        var candidate = new CrossRunMemoryPatternValidator().Normalize(CreateRequest());

        Assert.AreEqual(PatternCandidateId, candidate.CrossRunMemoryPatternCandidateId);
        Assert.AreEqual(ProjectId, candidate.ProjectId);
        Assert.AreEqual("cross-run-pattern:boundary", candidate.PatternCandidateKey);
        Assert.AreEqual(CrossRunMemoryPatternCandidateStatus.ReadyForReview, candidate.Status);
        Assert.AreEqual(CrossRunMemoryPatternType.RepeatedBoundaryCandidate, candidate.PatternType);
        Assert.AreEqual(CrossRunMemoryPatternBand.CrossRunCandidate, candidate.PatternBand);
        Assert.AreEqual(2, candidate.MemoryProposalIds.Count);
        Assert.AreEqual(2, candidate.WorkflowRunIds.Count);
        Assert.AreEqual(1, candidate.EvidenceReferences.Count);
        Assert.AreEqual(1, candidate.ReviewNotes.Count);
        AssertCandidateHasNoAuthority(candidate);
    }

    [TestMethod]
    public void CrossRunMemoryPatternCandidate_UsesBoundedStatuses()
    {
        var names = Enum.GetNames<CrossRunMemoryPatternCandidateStatus>();
        CollectionAssert.AreEquivalent(new[] { "Detected", "ReadyForReview", "NeedsEvidence", "NeedsHumanReview", "RequiresSanitizationReview", "Quarantined", "Superseded", "Withdrawn" }, names);
        AssertNoForbiddenTokens(string.Join("\n", names), "Accepted", "Approved", "Promoted", "Indexed", "Embedded", "Retrievable", "PolicySatisfied", "AuthorityTransferred", "TruthSelected");
    }

    [TestMethod]
    public void CrossRunMemoryPatternCandidate_UsesBoundedPatternTypes()
    {
        var names = Enum.GetNames<CrossRunMemoryPatternType>();
        CollectionAssert.AreEquivalent(new[] { "RepeatedFactCandidate", "RepeatedDecisionCandidate", "RepeatedBoundaryCandidate", "RepeatedFailureModeCandidate", "RepeatedRiskCandidate", "RepeatedConventionCandidate", "RepeatedDebuggingLessonCandidate", "RepeatedValidationFindingCandidate", "RepeatedReviewFindingCandidate", "RepeatedWorkflowPatternCandidate", "RepeatedPolicyInvariantCandidate", "PortableEngineeringMemoryPatternCandidate", "NeedsHumanPatternReview" }, names);
        AssertNoForbiddenTokens(string.Join("\n", names), "AcceptedMemory", "MemoryPromoted", "RetrievalActivated", "VectorIndexed", "EmbeddingCreated", "TruthSelected", "AutoPromoted");
    }

    [TestMethod]
    public void CrossRunMemoryPatternCandidate_UsesBoundedPatternBands()
    {
        var names = Enum.GetNames<CrossRunMemoryPatternBand>();
        CollectionAssert.AreEquivalent(new[] { "HighRecurrence", "MediumRecurrence", "LowRecurrence", "CrossRunCandidate", "PortableCandidateRequiresReview", "Unknown" }, names);
        AssertNoForbiddenTokens(string.Join("\n", names), "Approved", "Accepted", "Promoted", "AutoApply", "Truth");
    }

    [TestMethod] public void CrossRunMemoryPatternCandidate_AuthorityFlagsAreFalse() => AssertCandidateHasNoAuthority(new CrossRunMemoryPatternValidator().Normalize(CreateRequest()));
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsEmptyProjectId() => AssertInvalid(CreateRequest(projectId: Guid.Empty), "project_id_required");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsEmptyCandidateId() => AssertInvalid(CreateRequest(candidateId: Guid.Empty), "pattern_candidate_id_empty");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsBlankCandidateKey() => AssertInvalid(CreateRequest(candidateKey: " "), "pattern_candidate_key_required");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsBlankSummary() => AssertInvalid(CreateRequest(patternSummary: " "), "safe_pattern_summary_required");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsForbiddenStatus() => AssertInvalid(CreateRequest(status: (CrossRunMemoryPatternCandidateStatus)999), "status_forbidden");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsForbiddenPatternType() => AssertInvalid(CreateRequest(patternType: (CrossRunMemoryPatternType)999), "pattern_type_forbidden");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsForbiddenPatternBand() => AssertInvalid(CreateRequest(patternBand: (CrossRunMemoryPatternBand)999), "pattern_band_forbidden");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsInvalidRecurrenceScore() => AssertInvalid(CreateRequest(recurrenceScore: 1.1m), "recurrence_score_invalid");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsTooFewProposalIds() => AssertInvalid(CreateRequest(memoryProposalIds: new[] { FirstProposalId }), "memory_proposal_ids_minimum_required");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsTooFewWorkflowRunIds() => AssertInvalid(CreateRequest(workflowRunIds: new[] { FirstWorkflowRunId }), "workflow_run_ids_minimum_required");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsInvalidMetadataJson() => AssertInvalid(CreateRequest(metadataJson: "{"), "metadata_json_invalid");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsHiddenReasoning() => AssertInvalid(CreateRequest(patternSummary: "hiddenReasoning leaked"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsRawPrompt() => AssertInvalid(CreateRequest(patternSummary: "rawPrompt leaked"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsRawCompletion() => AssertInvalid(CreateRequest(patternSummary: "rawCompletion leaked"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsRawToolOutput() => AssertInvalid(CreateRequest(patternSummary: "rawToolOutput leaked"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsEntirePatch() => AssertInvalid(CreateRequest(patternSummary: "entirePatch leaked"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsTruthSelectionLanguage() => AssertInvalid(CreateRequest(reviewRecommendation: "truth selected"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsAcceptedMemoryLanguage() => AssertInvalid(CreateRequest(reviewRecommendation: "accepted memory"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsPromotedMemoryLanguage() => AssertInvalid(CreateRequest(reviewRecommendation: "promoted memory"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsRetrievalActivationLanguage() => AssertInvalid(CreateRequest(reviewRecommendation: "retrieval active"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsEmbeddingLanguage() => AssertInvalid(CreateRequest(reviewRecommendation: "embedding created"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsVectorWriteLanguage() => AssertInvalid(CreateRequest(reviewRecommendation: "vector write"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsPortableApprovedLanguage() => AssertInvalid(CreateRequest(reviewRecommendation: "portable approved"), "pattern_candidate_text_unsafe");
    [TestMethod] public void CrossRunMemoryPatternValidator_RejectsTrueAuthorityFlags() => AssertInvalid(CreateRequest(acceptsMemory: true), "authority_flags_forbidden");

    [TestMethod] public void CrossRunMemoryPattern_EvidenceReferenceIsNotDecision() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceIsDecision: true) }), "evidence_authority_forbidden");
    [TestMethod] public void CrossRunMemoryPattern_EvidenceReferenceDoesNotAcceptMemory() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceAcceptsMemory: true) }), "evidence_authority_forbidden");
    [TestMethod] public void CrossRunMemoryPattern_EvidenceReferenceDoesNotPromoteMemory() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidencePromotesMemory: true) }), "evidence_authority_forbidden");
    [TestMethod] public void CrossRunMemoryPattern_EvidenceReferenceDoesNotActivateRetrieval() => AssertInvalid(CreateRequest(evidenceReferences: new[] { Evidence(evidenceActivatesRetrieval: true) }), "evidence_authority_forbidden");
    [TestMethod] public void CrossRunMemoryPattern_ReviewNoteIsNotDecision() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteIsDecision: true) }), "review_note_authority_forbidden");
    [TestMethod] public void CrossRunMemoryPattern_ReviewNoteDoesNotAcceptMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteAcceptsMemory: true) }), "review_note_authority_forbidden");
    [TestMethod] public void CrossRunMemoryPattern_ReviewNoteDoesNotPromoteMemory() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(notePromotesMemory: true) }), "review_note_authority_forbidden");
    [TestMethod] public void CrossRunMemoryPattern_ReviewNoteDoesNotActivateRetrieval() => AssertInvalid(CreateRequest(reviewNotes: new[] { ReviewNote(noteActivatesRetrieval: true) }), "review_note_authority_forbidden");

    [TestMethod]
    public void CrossRunMemoryPatternDetector_DetectsRepeatedBoundaryCandidateAcrossRuns()
    {
        var candidates = Detect(
            Proposal(FirstProposalId, FirstWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.", MemoryProposalType.ProjectConventionCandidate, subjectId: "audit-approval-boundary"),
            Proposal(SecondProposalId, SecondWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.", MemoryProposalType.ProjectConventionCandidate, subjectId: "audit-approval-boundary"));

        AssertHasPattern(candidates, CrossRunMemoryPatternType.RepeatedBoundaryCandidate);
    }

    [TestMethod]
    public void CrossRunMemoryPatternDetector_DetectsRepeatedFailureModeCandidateAcrossRuns()
    {
        var candidates = Detect(
            Proposal(FirstProposalId, FirstWorkflowRunId, "SQL reset failure appears when schema cleanup order misses dependent events.", MemoryProposalType.FailureModeCandidate, subjectId: "sql-reset-order"),
            Proposal(SecondProposalId, SecondWorkflowRunId, "SQL reset failure appears when schema cleanup order misses dependent events.", MemoryProposalType.FailureModeCandidate, subjectId: "sql-reset-order"));

        AssertHasPattern(candidates, CrossRunMemoryPatternType.RepeatedFailureModeCandidate);
    }

    [TestMethod]
    public void CrossRunMemoryPatternDetector_DetectsRepeatedRiskCandidateAcrossRuns()
    {
        var candidates = Detect(
            Proposal(FirstProposalId, FirstWorkflowRunId, "Runtime DDL is a backend cleanup risk before contract freeze.", MemoryProposalType.ProjectRiskCandidate, subjectId: "runtime-ddl-risk"),
            Proposal(SecondProposalId, SecondWorkflowRunId, "Runtime DDL is a backend cleanup risk before contract freeze.", MemoryProposalType.ProjectRiskCandidate, subjectId: "runtime-ddl-risk"));

        AssertHasPattern(candidates, CrossRunMemoryPatternType.RepeatedRiskCandidate);
    }

    [TestMethod]
    public void CrossRunMemoryPatternDetector_DetectsRepeatedConventionCandidateAcrossRuns()
    {
        var candidates = Detect(
            Proposal(FirstProposalId, FirstWorkflowRunId, "Use neutral naming when memory proposal references handoff context.", MemoryProposalType.ProjectConventionCandidate, subjectId: "neutral-handoff-naming"),
            Proposal(SecondProposalId, SecondWorkflowRunId, "Use neutral naming when memory proposal references handoff context.", MemoryProposalType.ProjectConventionCandidate, subjectId: "neutral-handoff-naming"));

        AssertHasPattern(candidates, CrossRunMemoryPatternType.RepeatedConventionCandidate);
    }

    [TestMethod]
    public void CrossRunMemoryPatternDetector_DetectsPortableEngineeringMemoryPatternCandidateAsReviewOnly()
    {
        var candidates = Detect(
            Proposal(FirstProposalId, FirstWorkflowRunId, "Portable review pattern may be useful after sanitization.", MemoryProposalType.PortableEngineeringMemoryCandidate, MemoryProposalTargetScope.PortableEngineeringMemoryCandidate, "portable-pattern"),
            Proposal(SecondProposalId, SecondWorkflowRunId, "Portable review pattern may be useful after sanitization.", MemoryProposalType.PortableEngineeringMemoryCandidate, MemoryProposalTargetScope.PortableEngineeringMemoryCandidate, "portable-pattern"));

        var candidate = candidates.Single(candidate => candidate.PatternType == CrossRunMemoryPatternType.PortableEngineeringMemoryPatternCandidate);
        Assert.AreEqual(CrossRunMemoryPatternCandidateStatus.RequiresSanitizationReview, candidate.Status);
        Assert.AreEqual(CrossRunMemoryPatternBand.PortableCandidateRequiresReview, candidate.PatternBand);
        Assert.IsFalse(candidate.AcceptsMemory);
        Assert.IsFalse(candidate.PromotesMemory);
        Assert.IsFalse(candidate.ActivatesRetrieval);
    }

    [TestMethod]
    public void CrossRunMemoryPatternDetector_RequiresMultipleWorkflowRunsByDefault()
    {
        var candidates = Detect(
            Proposal(FirstProposalId, FirstWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.", MemoryProposalType.ProjectConventionCandidate, subjectId: "single-run-boundary"),
            Proposal(SecondProposalId, FirstWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.", MemoryProposalType.ProjectConventionCandidate, subjectId: "single-run-boundary"));

        Assert.AreEqual(0, candidates.Count);
    }

    [TestMethod]
    public void CrossRunMemoryPatternDetector_RequiresMinimumRecurrenceCount()
    {
        var candidates = new CrossRunMemoryPatternDetector().Detect(new[] { Proposal(FirstProposalId, FirstWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.") });
        Assert.AreEqual(0, candidates.Count);
    }

    [TestMethod]
    public void CrossRunMemoryPatternDetector_CapsCandidateCount()
    {
        var candidates = new CrossRunMemoryPatternDetector().Detect(new[]
        {
            Proposal(FirstProposalId, FirstWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.", subjectId: "first"),
            Proposal(SecondProposalId, SecondWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.", subjectId: "first"),
            Proposal(ThirdProposalId, ThirdWorkflowRunId, "SQL reset failure appears when schema cleanup order misses dependent events.", MemoryProposalType.FailureModeCandidate, subjectId: "second"),
            Proposal(Id(124), Id(134), "SQL reset failure appears when schema cleanup order misses dependent events.", MemoryProposalType.FailureModeCandidate, subjectId: "second")
        }, new CrossRunMemoryPatternDetectionOptions { MaxCandidateCount = 1 });

        Assert.AreEqual(1, candidates.Count);
    }

    [TestMethod]
    public void CrossRunMemoryPatternDetector_DoesNotMutateInputProposals()
    {
        var first = Proposal(FirstProposalId, FirstWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.");
        var second = Proposal(SecondProposalId, SecondWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.");
        var originalStatus = first.ProposalStatus;
        var originalText = first.SafeProposedMemory;

        _ = Detect(first, second);

        Assert.AreEqual(originalStatus, first.ProposalStatus);
        Assert.AreEqual(originalText, first.SafeProposedMemory);
        Assert.IsFalse(first.PromotesMemory);
        Assert.IsFalse(first.IsAcceptedMemory);
        Assert.IsFalse(first.WritesVectorIndex);
    }

    [TestMethod] public void CrossRunMemoryPatternDetector_DoesNotChooseTruth() => Assert.IsFalse(NormalizedDetectedCandidate().ChoosesTruth);
    [TestMethod] public void CrossRunMemoryPatternDetector_DoesNotAcceptMemory() => Assert.IsFalse(NormalizedDetectedCandidate().AcceptsMemory);
    [TestMethod] public void CrossRunMemoryPatternDetector_DoesNotPromoteMemory() => Assert.IsFalse(NormalizedDetectedCandidate().PromotesMemory);
    [TestMethod] public void CrossRunMemoryPatternDetector_DoesNotActivateRetrieval() => Assert.IsFalse(NormalizedDetectedCandidate().ActivatesRetrieval);
    [TestMethod] public void CrossRunMemoryPatternDetector_DoesNotWriteVectorIndex() => Assert.IsFalse(NormalizedDetectedCandidate().WritesVectorIndex);
    [TestMethod] public void CrossRunMemoryPatternDetector_DoesNotCreateEmbedding() => Assert.IsFalse(NormalizedDetectedCandidate().CreatesEmbedding);

    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotAddSqlMigration() => AssertNoFileNameContains("Database", "cross_run_memory_pattern", "memory_pattern_detection");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotAddApiEndpoint() => AssertDirectoryDoesNotContain("IronDev.Api", "CrossRunMemoryPattern", "cross-run-memory-pattern", "pattern-detection");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotAddCliCommand() => AssertDirectoryDoesNotContain("tools", "CrossRunMemoryPattern", "cross-run-memory-pattern", "pattern-detection");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotAddAcceptedMemoryStore() => AssertCoreFileDoesNotContain("IAcceptedMemoryStore", "AcceptedMemoryStore", "CreateAcceptedMemoryAsync");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotAddPromotionPath() => AssertCoreFileDoesNotContain("IMemoryPromotion", "PromoteMemoryAsync", "PromotionService");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotAddEmbeddingWriter() => AssertCoreFileDoesNotContain("IEmbeddingWriter", "CreateEmbeddingAsync", "EmbeddingClient");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotAddVectorStoreWriter() => AssertCoreFileDoesNotContain("IVectorStoreWriter", "WriteVectorStoreAsync", "VectorStoreClient");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotReferenceWeaviateWrite() => AssertCoreFileDoesNotContain("IWeaviate", "WeaviateClient", "WriteWeaviateAsync");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotReferenceRetrievalActivation() => AssertCoreFileDoesNotContain("IRetrievalActivation", "ActivateRetrievalAsync", "RetrievalActivationService");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotReferenceModelClient() => AssertCoreFileDoesNotContain("IAgentModelAdapter", "IModelClient", "OpenAI", "ChatCompletion");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotReferenceSourceApply() => AssertCoreFileDoesNotContain("ApplySource", "SourceApplyService", "MutateSourceAsync");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotReferenceWorkflowRunner() => AssertCoreFileDoesNotContain("WorkflowRunner", "ContinueWorkflowAsync", "DispatchWorkflow");
    [TestMethod] public void CrossRunMemoryPatternDetection_DoesNotReferenceAgentDispatcher() => AssertCoreFileDoesNotContain("AgentDispatcher", "DispatchAgent", "IAgentRuntime");

    private static IReadOnlyList<CrossRunMemoryPatternCandidateCreateRequest> Detect(params MemoryProposal[] proposals) =>
        new CrossRunMemoryPatternDetector().Detect(proposals);

    private static CrossRunMemoryPatternCandidate NormalizedDetectedCandidate() =>
        new CrossRunMemoryPatternValidator().Normalize(Detect(
            Proposal(FirstProposalId, FirstWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.", subjectId: "audit-approval-boundary"),
            Proposal(SecondProposalId, SecondWorkflowRunId, "Audit evidence remains separate from approval in the governance boundary.", subjectId: "audit-approval-boundary"))[0]);

    private static CrossRunMemoryPatternCandidateCreateRequest CreateRequest(
        Guid? projectId = null,
        Guid? candidateId = null,
        string candidateKey = "cross-run-pattern:boundary",
        CrossRunMemoryPatternCandidateStatus status = CrossRunMemoryPatternCandidateStatus.ReadyForReview,
        CrossRunMemoryPatternType patternType = CrossRunMemoryPatternType.RepeatedBoundaryCandidate,
        CrossRunMemoryPatternBand patternBand = CrossRunMemoryPatternBand.CrossRunCandidate,
        decimal recurrenceScore = 0.75m,
        IReadOnlyList<Guid>? memoryProposalIds = null,
        IReadOnlyList<Guid>? workflowRunIds = null,
        string patternSummary = "Repeated boundary pattern appears across staged proposals.",
        string? evidenceSummary = "Recurring staged proposals were observed across separate workflow runs.",
        string? reviewRecommendation = "Human review remains required before any later memory action.",
        bool acceptsMemory = false,
        IReadOnlyList<CrossRunMemoryPatternEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<CrossRunMemoryPatternReviewNote>? reviewNotes = null,
        string metadataJson = "{\"source\":\"pr112-test\"}") => new()
    {
        CrossRunMemoryPatternCandidateId = candidateId ?? PatternCandidateId,
        ProjectId = projectId ?? ProjectId,
        PatternCandidateKey = candidateKey,
        Status = status,
        PatternType = patternType,
        PatternBand = patternBand,
        RecurrenceScore = recurrenceScore,
        MemoryProposalIds = memoryProposalIds ?? new[] { FirstProposalId, SecondProposalId },
        WorkflowRunIds = workflowRunIds ?? new[] { FirstWorkflowRunId, SecondWorkflowRunId },
        WorkflowRunStepIds = new[] { WorkflowRunStepId },
        WorkflowCheckpointIds = new[] { WorkflowCheckpointId },
        SafePatternSummary = patternSummary,
        SafeEvidenceSummary = evidenceSummary,
        SafeReviewRecommendation = reviewRecommendation,
        EvidenceReferences = evidenceReferences ?? new[] { Evidence() },
        ReviewNotes = reviewNotes ?? new[] { ReviewNote() },
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "cross-run-memory-pattern-detection-tests",
        MetadataVersion = 1,
        MetadataJson = metadataJson,
        AcceptsMemory = acceptsMemory
    };

    private static CrossRunMemoryPatternEvidenceReference Evidence(bool evidenceIsDecision = false, bool evidenceAcceptsMemory = false, bool evidencePromotesMemory = false, bool evidenceActivatesRetrieval = false) => new()
    {
        EvidenceType = "MemoryProposal",
        EvidenceId = FirstProposalId.ToString(),
        EvidenceLabel = "Staged memory proposal",
        SafeSummary = "Staged proposal evidence supports pattern review only.",
        AllowedUse = "PatternReview",
        MemoryProposalId = FirstProposalId,
        WorkflowRunId = FirstWorkflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        EvidenceIsDecision = evidenceIsDecision,
        EvidenceAcceptsMemory = evidenceAcceptsMemory,
        EvidencePromotesMemory = evidencePromotesMemory,
        EvidenceActivatesRetrieval = evidenceActivatesRetrieval
    };

    private static CrossRunMemoryPatternReviewNote ReviewNote(bool noteIsDecision = false, bool noteAcceptsMemory = false, bool notePromotesMemory = false, bool noteActivatesRetrieval = false) => new()
    {
        NoteType = "PatternReview",
        SafeSummary = "Pattern score is advisory review evidence only.",
        Severity = "info",
        NoteIsDecision = noteIsDecision,
        NoteAcceptsMemory = noteAcceptsMemory,
        NotePromotesMemory = notePromotesMemory,
        NoteActivatesRetrieval = noteActivatesRetrieval
    };

    private static MemoryProposal Proposal(
        Guid proposalId,
        Guid workflowRunId,
        string safeProposedMemory,
        MemoryProposalType type = MemoryProposalType.ProjectConventionCandidate,
        MemoryProposalTargetScope scope = MemoryProposalTargetScope.ProjectLocalCandidate,
        string subjectId = "audit-approval-boundary",
        MemoryProposalStatus status = MemoryProposalStatus.ReadyForReview) => new()
    {
        MemoryProposalId = proposalId,
        ProjectId = ProjectId,
        ProposalKey = $"proposal:{proposalId:N}",
        ProposalType = type,
        TargetMemoryScope = scope,
        ProposalStatus = status,
        SourceType = "manual_test_fixture",
        SubjectType = "topic",
        SubjectId = subjectId,
        SafeProposedMemory = safeProposedMemory,
        WorkflowRunId = workflowRunId,
        WorkflowRunStepId = WorkflowRunStepId,
        WorkflowCheckpointId = WorkflowCheckpointId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "cross-run-memory-pattern-detection-tests",
        MetadataVersion = 1,
        MetadataJson = "{}",
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static void AssertHasPattern(IReadOnlyList<CrossRunMemoryPatternCandidateCreateRequest> candidates, CrossRunMemoryPatternType patternType)
    {
        var candidate = candidates.SingleOrDefault(candidate => candidate.PatternType == patternType);
        Assert.IsNotNull(candidate, $"Expected pattern type {patternType}.");
        Assert.AreEqual(2, candidate.MemoryProposalIds.Count);
        Assert.AreEqual(2, candidate.WorkflowRunIds.Count);
        Assert.IsFalse(candidate.AcceptsMemory);
        Assert.IsFalse(candidate.PromotesMemory);
        Assert.IsFalse(candidate.ActivatesRetrieval);
        Assert.IsFalse(candidate.WritesVectorIndex);
        Assert.IsFalse(candidate.CreatesEmbedding);
    }

    private static void AssertCandidateHasNoAuthority(CrossRunMemoryPatternCandidate candidate)
    {
        Assert.IsFalse(candidate.ChoosesTruth);
        Assert.IsFalse(candidate.AcceptsMemory);
        Assert.IsFalse(candidate.PromotesMemory);
        Assert.IsFalse(candidate.ActivatesRetrieval);
        Assert.IsFalse(candidate.WritesVectorIndex);
        Assert.IsFalse(candidate.CreatesEmbedding);
        Assert.IsFalse(candidate.SatisfiesPolicy);
        Assert.IsFalse(candidate.GrantsApproval);
        Assert.IsFalse(candidate.GrantsExecution);
        Assert.IsFalse(candidate.StartsWorkflow);
        Assert.IsFalse(candidate.ContinuesWorkflow);
        Assert.IsFalse(candidate.MutatesSource);
        Assert.IsFalse(candidate.ApprovesRelease);
    }

    private static void AssertInvalid(CrossRunMemoryPatternCandidateCreateRequest request, string expectedCode)
    {
        var result = new CrossRunMemoryPatternValidator().Validate(request);
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
        var path = Path.Combine(root, "IronDev.Core", "AgentMemory", "CrossRunMemoryPatternDetectionModels.cs");
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
