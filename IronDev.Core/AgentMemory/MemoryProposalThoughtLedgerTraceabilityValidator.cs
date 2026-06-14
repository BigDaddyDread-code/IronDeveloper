namespace IronDev.Core.AgentMemory;

public sealed class MemoryProposalThoughtLedgerTraceabilityValidator
{
    private static readonly string[] UnsafeMarkers =
    {
        "hiddenreasoning",
        "hidden reasoning",
        "chainofthought",
        "chain of thought",
        "chain-of-thought",
        "private reasoning",
        "scratchpad",
        "rawprompt",
        "raw prompt",
        "rawcompletion",
        "raw completion",
        "rawtooloutput",
        "raw tool output",
        "entirepatch",
        "entire patch",
        "accepted memory",
        "memory accepted",
        "promoted memory",
        "memory promoted",
        "promotion approved",
        "approval granted",
        "approval satisfied",
        "policy satisfied",
        "retrieval active",
        "indexed for retrieval",
        "embedding created",
        "vector write",
        "weaviate write",
        "portable memory approved",
        "portable engineering memory accepted",
        "truth confirmed",
        "authority transferred",
        "source applied",
        "release approved"
    };

    public MemoryProposalValidationResult ValidateStagedProposal(MemoryProposal? proposal)
    {
        var issues = new List<MemoryProposalValidationIssue>();
        if (proposal is null)
        {
            Add(issues, "proposal_required", "Memory proposal is required.");
            return Result(issues);
        }

        if (!HasSafeTrace(proposal))
        {
            Add(issues, "traceability_required", "Memory proposal must have a safe ThoughtLedger, governance, workflow, evidence, or grounding trace reference.");
        }

        if (proposal.IsAcceptedMemory || proposal.CreatesAcceptedMemory || proposal.PromotesMemory || proposal.WritesCollectiveMemory || proposal.WritesAgentMemory || proposal.WritesVectorIndex || proposal.IsRetrievalAuthority || proposal.IsPolicy || proposal.IsApproval || proposal.SatisfiesPolicy || proposal.GrantsApproval || proposal.GrantsExecution || proposal.StartsWorkflow || proposal.ContinuesWorkflow || proposal.MutatesSource || proposal.ApprovesRelease)
        {
            Add(issues, "proposal_authority_forbidden", "Traceability cannot approve, accept, promote, activate retrieval, satisfy policy, execute, continue workflow, mutate source, or approve release.");
        }

        ScanText(issues, "proposal_text_unsafe", proposal.ProposalKey, proposal.SourceType, proposal.SourceId, proposal.SourceAgentRole, proposal.SourceAgentId, proposal.SubjectType, proposal.SubjectId, proposal.SafeProposedMemory, proposal.SafeRationaleSummary, proposal.SafeRiskSummary, proposal.ConfidenceLabel, proposal.MetadataJson);

        foreach (var evidence in proposal.EvidenceReferences ?? Array.Empty<MemoryProposalEvidenceReference>())
        {
            ScanText(issues, "proposal_evidence_text_unsafe", evidence.EvidenceType.ToString(), evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary, evidence.AllowedUse?.ToString());
        }

        foreach (var grounding in proposal.GroundingReferences ?? Array.Empty<MemoryProposalGroundingReference>())
        {
            ScanText(issues, "proposal_grounding_text_unsafe", grounding.ClaimType.ToString(), grounding.ClaimId, grounding.SafeSummary);
        }

        foreach (var workflow in proposal.WorkflowReferences ?? Array.Empty<MemoryProposalWorkflowReference>())
        {
            ScanText(issues, "proposal_workflow_text_unsafe", workflow.ReferenceType.ToString(), workflow.SafeSummary);
        }

        return Result(issues);
    }

    public MemoryProposalValidationResult ValidateEvidencePackage(MemoryProposalEvidencePackage? package)
    {
        var issues = new List<MemoryProposalValidationIssue>();
        if (package is null)
        {
            Add(issues, "evidence_package_required", "Memory proposal evidence package is required.");
            return Result(issues);
        }

        if (!HasSafeTrace(package))
        {
            Add(issues, "traceability_required", "Memory proposal evidence package must have a safe ThoughtLedger, governance, workflow, evidence, or grounding trace reference.");
        }

        if (package.GrantsApproval || package.GrantsExecution || package.AcceptsMemory || package.RejectsMemory || package.PromotesMemory || package.CreatesAcceptedMemory || package.CreatesPortableMemory || package.ActivatesRetrieval || package.CreatesEmbedding || package.WritesVectorStore || package.MutatesSource || package.SatisfiesPolicy || package.TransfersAuthority || package.ApprovesRelease)
        {
            Add(issues, "evidence_package_authority_forbidden", "Evidence package traceability cannot approve, accept, reject, promote, activate retrieval, satisfy policy, execute, mutate source, or approve release.");
        }

        ScanText(issues, "evidence_package_text_unsafe", package.PackageKey, package.ProposalType, package.TargetMemoryScope, package.ProposalStatus, package.SafeProposedMemory, package.SafeRationaleSummary, package.SafeRiskSummary, package.ConfidentialityLabel, package.SanitizationStatus, package.MetadataJson);

        foreach (var evidence in package.EvidenceReferences ?? Array.Empty<MemoryProposalPackageEvidenceReference>())
        {
            if (evidence.EvidenceIsApproval || evidence.EvidenceIsPermission || evidence.EvidenceAcceptsMemory)
            {
                Add(issues, "evidence_reference_authority_forbidden", "Evidence references are evidence only and cannot approve, grant permission, or accept memory.");
            }

            ScanText(issues, "evidence_package_reference_text_unsafe", evidence.EvidenceType, evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary, evidence.AllowedUse);
        }

        foreach (var grounding in package.GroundingReferences ?? Array.Empty<MemoryProposalPackageGroundingReference>())
        {
            if (grounding.GroundingIsAuthority || grounding.GroundingAcceptsMemory)
            {
                Add(issues, "grounding_reference_authority_forbidden", "Grounding references are traceability only and cannot create authority or accept memory.");
            }

            ScanText(issues, "evidence_package_grounding_text_unsafe", grounding.ClaimType, grounding.ClaimId, grounding.SafeSummary);
        }

        foreach (var workflow in package.WorkflowReferences ?? Array.Empty<MemoryProposalPackageWorkflowReference>())
        {
            if (workflow.WorkflowReferenceAcceptsMemory || workflow.WorkflowReferencePromotesMemory)
            {
                Add(issues, "workflow_reference_authority_forbidden", "Workflow references are context only and cannot accept or promote memory.");
            }

            ScanText(issues, "evidence_package_workflow_text_unsafe", workflow.ReferenceType, workflow.SafeSummary);
        }

        foreach (var note in package.ReviewNotes ?? Array.Empty<MemoryProposalPackageReviewNote>())
        {
            if (note.NoteAcceptsMemory || note.NoteRejectsMemory || note.NotePromotesMemory)
            {
                Add(issues, "review_note_authority_forbidden", "Review notes cannot accept, reject, or promote memory.");
            }

            ScanText(issues, "evidence_package_review_note_text_unsafe", note.NoteType, note.SafeSummary, note.Severity);
        }

        return Result(issues);
    }

    public MemoryProposalValidationResult ValidatePromotionRequestPackage(MemoryPromotionRequestPackage? package)
    {
        var issues = new List<MemoryProposalValidationIssue>();
        if (package is null)
        {
            Add(issues, "promotion_request_package_required", "Memory promotion request package is required.");
            return Result(issues);
        }

        if (!HasSafeTrace(package))
        {
            Add(issues, "traceability_required", "Memory promotion request package must have a safe ThoughtLedger, governance, workflow, evidence, or grounding trace reference.");
        }

        if (package.IsDecision || package.GrantsApproval || package.SatisfiesPolicy || package.AcceptsMemory || package.RejectsMemory || package.PromotesMemory || package.CreatesAcceptedMemory || package.CreatesPortableMemory || package.ActivatesRetrieval || package.CreatesEmbedding || package.WritesVectorStore || package.TransfersAuthority || package.MutatesSource || package.ApprovesRelease)
        {
            Add(issues, "promotion_request_package_authority_forbidden", "Promotion request traceability cannot decide, approve, accept, reject, promote, activate retrieval, satisfy policy, mutate source, or approve release.");
        }

        ScanText(issues, "promotion_request_package_text_unsafe", package.PromotionRequestKey, package.ProposalType, package.CurrentProposalStatus, package.RequestedTargetMemoryScope.ToString(), package.SafeProposedMemory, package.SafePromotionRationale, package.SafeRiskSummary, package.SafeSanitizationSummary, package.SafeReviewerInstructions, package.ConfidentialityLabel, package.SanitizationStatus, package.MetadataJson);

        foreach (var evidence in package.EvidenceReferences ?? Array.Empty<MemoryPromotionRequestEvidenceReference>())
        {
            if (evidence.EvidenceIsDecision || evidence.EvidenceGrantsApproval || evidence.EvidenceSatisfiesPolicy || evidence.EvidenceAcceptsMemory || evidence.EvidencePromotesMemory)
            {
                Add(issues, "promotion_evidence_authority_forbidden", "Promotion request evidence references are evidence only and cannot decide, approve, satisfy policy, accept, or promote memory.");
            }

            ScanText(issues, "promotion_evidence_text_unsafe", evidence.EvidenceType, evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary, evidence.AllowedUse);
        }

        foreach (var grounding in package.GroundingReferences ?? Array.Empty<MemoryPromotionRequestGroundingReference>())
        {
            if (grounding.GroundingIsAuthority || grounding.GroundingAcceptsMemory || grounding.GroundingPromotesMemory)
            {
                Add(issues, "promotion_grounding_authority_forbidden", "Promotion request grounding references are traceability only and cannot create authority, accept memory, or promote memory.");
            }

            ScanText(issues, "promotion_grounding_text_unsafe", grounding.ClaimType, grounding.ClaimId, grounding.SafeSummary);
        }

        foreach (var signal in package.SignalReferences ?? Array.Empty<MemoryPromotionRequestSignalReference>())
        {
            if (signal.SignalIsDecision || signal.SignalBlocksPromotion || signal.SignalAllowsPromotion || signal.SignalAcceptsMemory || signal.SignalPromotesMemory)
            {
                Add(issues, "promotion_signal_authority_forbidden", "Promotion request signals are review-only and cannot decide, block, allow, accept, or promote memory.");
            }

            ScanText(issues, "promotion_signal_text_unsafe", signal.SignalType, signal.SafeSummary, signal.Severity);
        }

        foreach (var requirement in package.ApprovalRequirementReferences ?? Array.Empty<MemoryPromotionApprovalRequirementReference>())
        {
            if (requirement.RequirementIsApproval || requirement.RequirementSatisfiesPolicy || requirement.RequirementAllowsPromotion)
            {
                Add(issues, "promotion_approval_requirement_authority_forbidden", "Approval requirement references remain unsatisfied requirements only.");
            }

            ScanText(issues, "promotion_approval_requirement_text_unsafe", requirement.RequirementType, requirement.RequirementId, requirement.SafeSummary);
        }

        foreach (var note in package.ReviewNotes ?? Array.Empty<MemoryPromotionRequestReviewNote>())
        {
            if (note.NoteIsDecision || note.NoteGrantsApproval || note.NoteAcceptsMemory || note.NoteRejectsMemory || note.NotePromotesMemory)
            {
                Add(issues, "promotion_review_note_authority_forbidden", "Promotion request review notes cannot decide, approve, accept, reject, or promote memory.");
            }

            ScanText(issues, "promotion_review_note_text_unsafe", note.NoteType, note.SafeSummary, note.Severity);
        }

        return Result(issues);
    }

    private static bool HasSafeTrace(MemoryProposal proposal)
    {
        return proposal.EvidenceReferences.Any(HasSafeTrace)
            || proposal.GroundingReferences.Any(reference => reference.GroundingReferenceId != Guid.Empty)
            || proposal.WorkflowReferences.Any(reference => reference.WorkflowRunId.HasValue || reference.WorkflowRunStepId.HasValue || reference.WorkflowCheckpointId.HasValue)
            || proposal.WorkflowRunId.HasValue
            || proposal.WorkflowRunStepId.HasValue
            || proposal.WorkflowCheckpointId.HasValue;
    }

    private static bool HasSafeTrace(MemoryProposalEvidenceReference reference)
    {
        return reference.ThoughtLedgerEntryId.HasValue
            || reference.GovernanceEventId.HasValue
            || reference.WorkflowRunEvidenceReferenceId.HasValue
            || reference.WorkflowRunStepId.HasValue
            || reference.WorkflowCheckpointId.HasValue;
    }

    private static bool HasSafeTrace(MemoryProposalEvidencePackage package)
    {
        return package.EvidenceReferences.Any(HasSafeTrace)
            || package.GroundingReferences.Any(reference => reference.GroundingEvidenceReferenceId != Guid.Empty)
            || package.WorkflowReferences.Any(reference => reference.WorkflowRunId.HasValue || reference.WorkflowRunStepId.HasValue || reference.WorkflowCheckpointId.HasValue)
            || package.WorkflowRunId.HasValue
            || package.WorkflowRunStepId.HasValue
            || package.WorkflowCheckpointId.HasValue;
    }

    private static bool HasSafeTrace(MemoryProposalPackageEvidenceReference reference)
    {
        return reference.ThoughtLedgerEntryId.HasValue
            || reference.GovernanceEventId.HasValue
            || reference.WorkflowRunEvidenceReferenceId.HasValue
            || reference.WorkflowRunStepId.HasValue
            || reference.WorkflowCheckpointId.HasValue;
    }

    private static bool HasSafeTrace(MemoryPromotionRequestPackage package)
    {
        return package.EvidenceReferences.Any(HasSafeTrace)
            || package.GroundingReferences.Any(reference => reference.GroundingEvidenceReferenceId != Guid.Empty)
            || package.WorkflowRunId.HasValue
            || package.WorkflowRunStepId.HasValue
            || package.WorkflowCheckpointId.HasValue;
    }

    private static bool HasSafeTrace(MemoryPromotionRequestEvidenceReference reference)
    {
        return reference.MemoryProposalEvidencePackageId.HasValue
            || reference.MemoryProposalId.HasValue
            || reference.GovernanceEventId.HasValue
            || reference.WorkflowRunId.HasValue
            || reference.WorkflowRunStepId.HasValue
            || reference.WorkflowCheckpointId.HasValue
            || reference.EvidenceType.Contains("ThoughtLedger", StringComparison.OrdinalIgnoreCase)
            || reference.EvidenceType.Contains("Governance", StringComparison.OrdinalIgnoreCase)
            || reference.EvidenceType.Contains("Workflow", StringComparison.OrdinalIgnoreCase);
    }

    private static void ScanText(ICollection<MemoryProposalValidationIssue> issues, string code, params string?[] values)
    {
        var text = string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();
        if (UnsafeMarkers.Any(text.Contains))
        {
            Add(issues, code, "Traceability text contains hidden/private reasoning, raw material, authority, approval, policy, retrieval, vector, source, or release language.");
        }
    }

    private static MemoryProposalValidationResult Result(IReadOnlyList<MemoryProposalValidationIssue> issues) => new() { Issues = issues };
    private static void Add(ICollection<MemoryProposalValidationIssue> issues, string code, string message) => issues.Add(new MemoryProposalValidationIssue(code, message));
}
