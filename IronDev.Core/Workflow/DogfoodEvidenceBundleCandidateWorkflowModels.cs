namespace IronDev.Core.Workflow;

public interface IDogfoodEvidenceBundleCandidateWorkflow
{
    DogfoodEvidenceBundleCandidateResult Prepare(DogfoodEvidenceBundleCandidateRequest? request);
}

public sealed class DogfoodEvidenceBundleCandidateWorkflow : IDogfoodEvidenceBundleCandidateWorkflow
{
    private static readonly string[] UnsafeMarkers =
    [
        "private reasoning",
        "hidden reasoning",
        "chainofthought",
        "chain of thought",
        "chain-of-thought",
        "scratchpad",
        "rawprompt",
        "raw prompt",
        "rawcompletion",
        "raw completion",
        "rawtooloutput",
        "raw tool output",
        "raw log",
        "raw logs",
        "rawtrace",
        "raw trace",
        "rawreport",
        "raw report",
        "artifact payload",
        "command payload",
        "stdout",
        "stderr",
        "source content",
        "source file contents",
        "wholepatch",
        "whole patch",
        "entirepatch",
        "entire patch",
        "patchpayload",
        "patch payload",
        "dogfood ran",
        "tests passed",
        "validation passed",
        "release ready",
        "approved",
        "approval granted",
        "approval satisfied",
        "policy satisfied",
        "workflow may continue",
        "workflow continued",
        "source mutation authorized",
        "tool execution authorized",
        "memory promotion authorized",
        "retrieval activation authorized",
        "execution allowed",
        "agent dispatched",
        "tool invoked",
        "source mutated",
        "patch applied",
        "ticket created",
        "memory promoted",
        "retrieval activated",
        "sql written",
        "model called",
        "prompt built"
    ];

    public DogfoodEvidenceBundleCandidateResult Prepare(DogfoodEvidenceBundleCandidateRequest? request)
    {
        if (request is null)
        {
            return Result(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                DogfoodEvidenceBundleCandidateStatus.InvalidRequest,
                [DogfoodEvidenceBundleCandidateReason.MissingWorkflowRunId],
                [],
                [],
                [],
                [],
                [],
                [],
                ["workflow run id"],
                []);
        }

        var workflowRunId = NormalizeId(request.WorkflowRunId);
        var workflowStepId = NormalizeId(request.WorkflowStepId);
        var bundleReferenceInputId = NormalizeId(request.DogfoodEvidenceBundleReferenceId);
        var projectReferenceId = NormalizeId(request.ProjectReferenceId);
        var dogfoodRunReferenceId = NormalizeId(request.DogfoodRunReferenceId);

        var invalidReasons = new List<DogfoodEvidenceBundleCandidateReason>();
        AddMissingIfNull(workflowRunId, invalidReasons, DogfoodEvidenceBundleCandidateReason.MissingWorkflowRunId);
        AddMissingIfNull(workflowStepId, invalidReasons, DogfoodEvidenceBundleCandidateReason.MissingWorkflowStepId);
        AddMissingIfNull(bundleReferenceInputId, invalidReasons, DogfoodEvidenceBundleCandidateReason.MissingBundleReference);
        AddMissingIfNull(projectReferenceId, invalidReasons, DogfoodEvidenceBundleCandidateReason.MissingProjectReference);
        AddMissingIfNull(dogfoodRunReferenceId, invalidReasons, DogfoodEvidenceBundleCandidateReason.MissingDogfoodRunReference);

        if (ContainsUnsafeMaterial(request.WorkflowRunId) ||
            ContainsUnsafeMaterial(request.WorkflowStepId) ||
            ContainsUnsafeMaterial(request.DogfoodEvidenceBundleReferenceId) ||
            ContainsUnsafeMaterial(request.ProjectReferenceId) ||
            ContainsUnsafeMaterial(request.DogfoodRunReferenceId) ||
            ContainsUnsafeMaterial(request.SafeRunLabel) ||
            ContainsUnsafeMaterial(request.SafeCommandDisplayReferenceId) ||
            ContainsUnsafeMaterial(request.SafeOutcomeSummary) ||
            ContainsUnsafeMaterial(request.CorrelationId) ||
            HasUnsafeReferences(request.EvidenceReferences) ||
            HasUnsafeReferences(request.ValidationReferences) ||
            HasUnsafeReferences(request.ArtifactReferences) ||
            HasUnsafeReferences(request.CandidatePackageReferences) ||
            HasUnsafeReferences(request.GateHints) ||
            HasUnsafeReferences(request.Risks))
        {
            invalidReasons.Add(DogfoodEvidenceBundleCandidateReason.UnsafeInput);
        }

        if (request.EvidenceReferences.Any(reference => reference.Kind == DogfoodEvidenceKind.Unknown ||
            string.IsNullOrWhiteSpace(NormalizeId(reference.ReferenceId))))
        {
            invalidReasons.Add(DogfoodEvidenceBundleCandidateReason.MissingEvidenceReference);
        }

        if (request.ValidationReferences.Any(reference => reference.Kind == DogfoodValidationKind.Unknown ||
            reference.OutcomeHint == DogfoodValidationOutcomeHint.Unknown ||
            string.IsNullOrWhiteSpace(NormalizeId(reference.ReferenceId))))
        {
            invalidReasons.Add(DogfoodEvidenceBundleCandidateReason.MissingValidationReference);
        }

        if (request.ArtifactReferences.Any(reference => reference.Kind == DogfoodArtifactKind.Unknown ||
            string.IsNullOrWhiteSpace(NormalizeId(reference.ReferenceId))))
        {
            invalidReasons.Add(DogfoodEvidenceBundleCandidateReason.MissingArtifactReference);
        }

        if (request.CandidatePackageReferences.Any(reference => reference.Kind == DogfoodCandidatePackageKind.Unknown ||
            string.IsNullOrWhiteSpace(NormalizeId(reference.ReferenceId))))
        {
            invalidReasons.Add(DogfoodEvidenceBundleCandidateReason.MissingEvidenceReference);
        }

        if (request.GateHints.Any(hint => hint.Kind == DogfoodEvidenceGateKind.Unknown))
            invalidReasons.Add(DogfoodEvidenceBundleCandidateReason.MissingGateHint);

        if (request.Risks.Any(risk => risk.Kind == DogfoodEvidenceRiskKind.Unknown))
            invalidReasons.Add(DogfoodEvidenceBundleCandidateReason.UnsafeInput);

        var evidenceReferences = SafeEvidenceReferences(request.EvidenceReferences).ToList();
        var validationReferences = SafeValidationReferences(request.ValidationReferences).ToList();
        var artifactReferences = SafeArtifactReferences(request.ArtifactReferences).ToList();
        var candidatePackageReferences = SafeCandidatePackageReferences(request.CandidatePackageReferences).ToList();
        var gateHints = SafeGateHints(request.GateHints).ToList();
        var risks = SafeRisks(request.Risks).ToList();

        if (invalidReasons.Count > 0)
        {
            return Result(
                workflowRunId ?? string.Empty,
                workflowStepId ?? string.Empty,
                bundleReferenceInputId ?? string.Empty,
                projectReferenceId ?? string.Empty,
                dogfoodRunReferenceId ?? string.Empty,
                DogfoodEvidenceBundleCandidateStatus.InvalidRequest,
                invalidReasons,
                evidenceReferences,
                validationReferences,
                artifactReferences,
                candidatePackageReferences,
                gateHints,
                risks,
                [],
                []);
        }

        var blockReasons = new List<DogfoodEvidenceBundleCandidateReason>();
        AddWorkflowGateBlocks(request, blockReasons);
        AddUpstreamPackageBlocks(request, blockReasons);

        if (blockReasons.Count > 0)
        {
            return Result(
                workflowRunId!,
                workflowStepId!,
                bundleReferenceInputId!,
                projectReferenceId!,
                dogfoodRunReferenceId!,
                DogfoodEvidenceBundleCandidateStatus.BlockedByWorkflowGate,
                blockReasons,
                evidenceReferences,
                validationReferences,
                artifactReferences,
                candidatePackageReferences,
                gateHints,
                risks,
                [],
                []);
        }

        AddUpstreamPackageReferences(request, evidenceReferences, candidatePackageReferences);

        var missingEvidence = MissingEvidenceFor(request, evidenceReferences, validationReferences, artifactReferences, gateHints);
        if (missingEvidence.Count > 0)
        {
            return Result(
                workflowRunId!,
                workflowStepId!,
                bundleReferenceInputId!,
                projectReferenceId!,
                dogfoodRunReferenceId!,
                DogfoodEvidenceBundleCandidateStatus.MissingRequiredEvidence,
                MissingReasonsFor(missingEvidence),
                evidenceReferences,
                validationReferences,
                artifactReferences,
                candidatePackageReferences,
                gateHints,
                risks,
                missingEvidence,
                []);
        }

        return Result(
            workflowRunId!,
            workflowStepId!,
            bundleReferenceInputId!,
            projectReferenceId!,
            dogfoodRunReferenceId!,
            DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced,
            [
                DogfoodEvidenceBundleCandidateReason.BundleOnly,
                DogfoodEvidenceBundleCandidateReason.SuppliedEvidenceOnly
            ],
            evidenceReferences,
            validationReferences,
            artifactReferences,
            candidatePackageReferences,
            gateHints,
            risks,
            [],
            SummaryLines(request, evidenceReferences, validationReferences, artifactReferences, candidatePackageReferences, gateHints, risks));
    }

    private static void AddWorkflowGateBlocks(DogfoodEvidenceBundleCandidateRequest request, List<DogfoodEvidenceBundleCandidateReason> reasons)
    {
        if (request.StepEvaluation is not null &&
            request.StepEvaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
        {
            reasons.Add(DogfoodEvidenceBundleCandidateReason.BlockedByRunnerEvaluation);
        }

        if (request.DryRunResult is not null &&
            request.DryRunResult.Status != WorkflowDryRunStatus.DryRunCompleted)
        {
            reasons.Add(DogfoodEvidenceBundleCandidateReason.BlockedByDryRun);
        }

        if (request.RouteSuggestion is not null)
        {
            var blockingLabel = request.RouteSuggestion.Label is not BoxedLangGraphRouteLabel.EligibleForDryRun and
                not BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable;

            var authorityClaim = request.RouteSuggestion.WorkflowDecisionAuthority ||
                request.RouteSuggestion.WorkflowStateChangeAllowed ||
                request.RouteSuggestion.StepWorkAllowed ||
                request.RouteSuggestion.AgentSendAllowed ||
                request.RouteSuggestion.A2aSendAllowed ||
                request.RouteSuggestion.ToolUseAllowed ||
                request.RouteSuggestion.ApprovalChangeAllowed ||
                request.RouteSuggestion.PolicySatisfactionAllowed ||
                request.RouteSuggestion.SourceChangeAllowed ||
                request.RouteSuggestion.MemoryPromotionAllowed ||
                request.RouteSuggestion.RetrievalActivationAllowed;

            if (blockingLabel || authorityClaim)
                reasons.Add(DogfoodEvidenceBundleCandidateReason.BlockedByRouteSuggestion);
        }
    }

    private static void AddUpstreamPackageBlocks(DogfoodEvidenceBundleCandidateRequest request, List<DogfoodEvidenceBundleCandidateReason> reasons)
    {
        if (request.HumanApprovalPackage is not null &&
            request.HumanApprovalPackage.Status != HumanApprovalPackageCandidateStatus.ApprovalPackageProduced)
        {
            reasons.Add(DogfoodEvidenceBundleCandidateReason.BlockedByHumanApprovalPackage);
        }

        if (request.MemoryImprovementPackage is not null &&
            request.MemoryImprovementPackage.Status != MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced)
        {
            reasons.Add(DogfoodEvidenceBundleCandidateReason.BlockedByMemoryImprovementPackage);
        }

        if (request.ToolRequestGatePreview is not null &&
            request.ToolRequestGatePreview.Status != ToolRequestGatePreviewCandidateStatus.GatePreviewProduced)
        {
            reasons.Add(DogfoodEvidenceBundleCandidateReason.BlockedByToolRequestGatePreview);
        }

        if (request.ImplementationProposal is not null &&
            request.ImplementationProposal.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced)
        {
            reasons.Add(DogfoodEvidenceBundleCandidateReason.BlockedByImplementationProposal);
        }

        if (request.CriticReviewRequest is not null &&
            request.CriticReviewRequest.Status != CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced)
        {
            reasons.Add(DogfoodEvidenceBundleCandidateReason.BlockedByCriticReviewRequest);
        }

        if (request.TestFailureReview is not null &&
            request.TestFailureReview.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced)
        {
            reasons.Add(DogfoodEvidenceBundleCandidateReason.BlockedByTestFailureReview);
        }
    }

    private static void AddUpstreamPackageReferences(
        DogfoodEvidenceBundleCandidateRequest request,
        List<DogfoodEvidenceReference> evidenceReferences,
        List<DogfoodCandidatePackageReference> candidatePackageReferences)
    {
        AddHumanApprovalPackage(request.HumanApprovalPackage, evidenceReferences, candidatePackageReferences);
        AddMemoryPackage(request.MemoryImprovementPackage, evidenceReferences, candidatePackageReferences);
        AddToolPreview(request.ToolRequestGatePreview, evidenceReferences, candidatePackageReferences);
        AddImplementationProposal(request.ImplementationProposal, evidenceReferences, candidatePackageReferences);
        AddCriticReview(request.CriticReviewRequest, evidenceReferences, candidatePackageReferences);
        AddTestFailureReview(request.TestFailureReview, evidenceReferences, candidatePackageReferences);
    }

    private static void AddHumanApprovalPackage(
        HumanApprovalPackageCandidateResult? package,
        List<DogfoodEvidenceReference> evidenceReferences,
        List<DogfoodCandidatePackageReference> candidatePackageReferences)
    {
        if (package?.Status != HumanApprovalPackageCandidateStatus.ApprovalPackageProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(package.PackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new DogfoodEvidenceReference
        {
            Kind = DogfoodEvidenceKind.CandidatePackageReference,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Human approval package supplied as review material."
        });

        candidatePackageReferences.Add(new DogfoodCandidatePackageReference
        {
            Kind = DogfoodCandidatePackageKind.HumanApprovalPackageCandidate,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Human approval package candidate."
        });
    }

    private static void AddMemoryPackage(
        MemoryImprovementPackageCandidateResult? package,
        List<DogfoodEvidenceReference> evidenceReferences,
        List<DogfoodCandidatePackageReference> candidatePackageReferences)
    {
        if (package?.Status != MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(package.PackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new DogfoodEvidenceReference
        {
            Kind = DogfoodEvidenceKind.CandidatePackageReference,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Memory improvement package supplied as review material."
        });

        candidatePackageReferences.Add(new DogfoodCandidatePackageReference
        {
            Kind = DogfoodCandidatePackageKind.MemoryImprovementPackageCandidate,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Memory improvement package candidate."
        });
    }

    private static void AddToolPreview(
        ToolRequestGatePreviewCandidateResult? preview,
        List<DogfoodEvidenceReference> evidenceReferences,
        List<DogfoodCandidatePackageReference> candidatePackageReferences)
    {
        if (preview?.Status != ToolRequestGatePreviewCandidateStatus.GatePreviewProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(preview.PreviewPackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new DogfoodEvidenceReference
        {
            Kind = DogfoodEvidenceKind.CandidatePackageReference,
            ReferenceId = preview.PreviewPackageReferenceId,
            SafeSummary = "Tool request gate preview supplied as review material."
        });

        candidatePackageReferences.Add(new DogfoodCandidatePackageReference
        {
            Kind = DogfoodCandidatePackageKind.ToolRequestGatePreviewCandidate,
            ReferenceId = preview.PreviewPackageReferenceId,
            SafeSummary = "Tool request gate preview candidate."
        });
    }

    private static void AddImplementationProposal(
        ImplementationProposalPackageCandidateResult? proposal,
        List<DogfoodEvidenceReference> evidenceReferences,
        List<DogfoodCandidatePackageReference> candidatePackageReferences)
    {
        if (proposal?.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(proposal.ProposalPackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new DogfoodEvidenceReference
        {
            Kind = DogfoodEvidenceKind.CandidatePackageReference,
            ReferenceId = proposal.ProposalPackageReferenceId,
            SafeSummary = "Implementation proposal package supplied as review material."
        });

        candidatePackageReferences.Add(new DogfoodCandidatePackageReference
        {
            Kind = DogfoodCandidatePackageKind.ImplementationProposalPackageCandidate,
            ReferenceId = proposal.ProposalPackageReferenceId,
            SafeSummary = "Implementation proposal package candidate."
        });
    }

    private static void AddCriticReview(
        CriticReviewRequestCandidateResult? review,
        List<DogfoodEvidenceReference> evidenceReferences,
        List<DogfoodCandidatePackageReference> candidatePackageReferences)
    {
        if (review?.Status != CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(review.ReviewPackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new DogfoodEvidenceReference
        {
            Kind = DogfoodEvidenceKind.CandidatePackageReference,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Critic review request supplied as review material."
        });

        candidatePackageReferences.Add(new DogfoodCandidatePackageReference
        {
            Kind = DogfoodCandidatePackageKind.CriticReviewRequestCandidate,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Critic review request candidate."
        });
    }

    private static void AddTestFailureReview(
        TestFailureReviewCandidateResult? review,
        List<DogfoodEvidenceReference> evidenceReferences,
        List<DogfoodCandidatePackageReference> candidatePackageReferences)
    {
        if (review?.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(review.ReviewPackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new DogfoodEvidenceReference
        {
            Kind = DogfoodEvidenceKind.CandidatePackageReference,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Test failure review supplied as review material."
        });

        candidatePackageReferences.Add(new DogfoodCandidatePackageReference
        {
            Kind = DogfoodCandidatePackageKind.TestFailureReviewCandidate,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Test failure review candidate."
        });
    }

    private static List<string> MissingEvidenceFor(
        DogfoodEvidenceBundleCandidateRequest request,
        IReadOnlyList<DogfoodEvidenceReference> evidenceReferences,
        IReadOnlyList<DogfoodValidationReference> validationReferences,
        IReadOnlyList<DogfoodArtifactReference> artifactReferences,
        IReadOnlyList<DogfoodEvidenceGateHint> gateHints)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(request.SafeOutcomeSummary))
            missing.Add("safe outcome summary");

        if (evidenceReferences.Count == 0)
            missing.Add("dogfood evidence reference");

        if (validationReferences.Count == 0)
            missing.Add("validation reference");

        if (artifactReferences.Count == 0)
            missing.Add("artifact reference");

        if (gateHints.Count == 0)
            missing.Add("evidence gate hint");

        return missing;
    }

    private static IReadOnlyList<DogfoodEvidenceBundleCandidateReason> MissingReasonsFor(IReadOnlyList<string> missingEvidence)
    {
        var reasons = new List<DogfoodEvidenceBundleCandidateReason>();

        if (missingEvidence.Any(value => value.Contains("evidence", StringComparison.OrdinalIgnoreCase)))
            reasons.Add(DogfoodEvidenceBundleCandidateReason.MissingEvidenceReference);

        if (missingEvidence.Any(value => value.Contains("validation", StringComparison.OrdinalIgnoreCase)))
            reasons.Add(DogfoodEvidenceBundleCandidateReason.MissingValidationReference);

        if (missingEvidence.Any(value => value.Contains("artifact", StringComparison.OrdinalIgnoreCase)))
            reasons.Add(DogfoodEvidenceBundleCandidateReason.MissingArtifactReference);

        if (missingEvidence.Any(value => value.Contains("gate", StringComparison.OrdinalIgnoreCase)))
            reasons.Add(DogfoodEvidenceBundleCandidateReason.MissingGateHint);

        return reasons.Count == 0 ? [DogfoodEvidenceBundleCandidateReason.MissingEvidenceReference] : reasons;
    }

    private static IReadOnlyList<string> SummaryLines(
        DogfoodEvidenceBundleCandidateRequest request,
        IReadOnlyList<DogfoodEvidenceReference> evidenceReferences,
        IReadOnlyList<DogfoodValidationReference> validationReferences,
        IReadOnlyList<DogfoodArtifactReference> artifactReferences,
        IReadOnlyList<DogfoodCandidatePackageReference> candidatePackageReferences,
        IReadOnlyList<DogfoodEvidenceGateHint> gateHints,
        IReadOnlyList<DogfoodEvidenceRisk> risks)
    {
        var lines = new List<string>
        {
            "Dogfood evidence bundle was produced from supplied evidence references only.",
            "No dogfood run was executed.",
            "No tests were run.",
            "No files, logs, traces, or artifacts were read.",
            "Validation outcome hints are supplied references only.",
            "Release readiness is not claimed.",
            "Workflow was not transitioned.",
            $"Evidence references: {evidenceReferences.Count}.",
            $"Validation references: {validationReferences.Count}.",
            $"Artifact references: {artifactReferences.Count}.",
            $"Candidate package references: {candidatePackageReferences.Count}.",
            $"Gate hints: {gateHints.Count}.",
            $"Risks: {risks.Count}."
        };

        if (!string.IsNullOrWhiteSpace(request.SafeRunLabel) && !ContainsUnsafeMaterial(request.SafeRunLabel))
            lines.Add($"Safe run label: {request.SafeRunLabel.Trim()}");

        if (!string.IsNullOrWhiteSpace(request.SafeCommandDisplayReferenceId) && !ContainsUnsafeMaterial(request.SafeCommandDisplayReferenceId))
            lines.Add($"Command display reference: {request.SafeCommandDisplayReferenceId.Trim()}");

        if (!string.IsNullOrWhiteSpace(request.SafeOutcomeSummary) && !ContainsUnsafeMaterial(request.SafeOutcomeSummary))
            lines.Add($"Safe outcome summary: {request.SafeOutcomeSummary.Trim()}");

        return lines;
    }

    private static IEnumerable<DogfoodEvidenceReference> SafeEvidenceReferences(IEnumerable<DogfoodEvidenceReference> references) =>
        references
            .Where(reference => reference.Kind != DogfoodEvidenceKind.Unknown)
            .Select(reference => new DogfoodEvidenceReference
            {
                Kind = reference.Kind,
                ReferenceId = NormalizeId(reference.ReferenceId) ?? string.Empty,
                SafeSummary = SafeText(reference.SafeSummary)
            })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId));

    private static IEnumerable<DogfoodValidationReference> SafeValidationReferences(IEnumerable<DogfoodValidationReference> references) =>
        references
            .Where(reference => reference.Kind != DogfoodValidationKind.Unknown && reference.OutcomeHint != DogfoodValidationOutcomeHint.Unknown)
            .Select(reference => new DogfoodValidationReference
            {
                Kind = reference.Kind,
                ReferenceId = NormalizeId(reference.ReferenceId) ?? string.Empty,
                OutcomeHint = reference.OutcomeHint,
                SafeSummary = SafeText(reference.SafeSummary)
            })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId));

    private static IEnumerable<DogfoodArtifactReference> SafeArtifactReferences(IEnumerable<DogfoodArtifactReference> references) =>
        references
            .Where(reference => reference.Kind != DogfoodArtifactKind.Unknown)
            .Select(reference => new DogfoodArtifactReference
            {
                Kind = reference.Kind,
                ReferenceId = NormalizeId(reference.ReferenceId) ?? string.Empty,
                SafeSummary = SafeText(reference.SafeSummary)
            })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId));

    private static IEnumerable<DogfoodCandidatePackageReference> SafeCandidatePackageReferences(IEnumerable<DogfoodCandidatePackageReference> references) =>
        references
            .Where(reference => reference.Kind != DogfoodCandidatePackageKind.Unknown)
            .Select(reference => new DogfoodCandidatePackageReference
            {
                Kind = reference.Kind,
                ReferenceId = NormalizeId(reference.ReferenceId) ?? string.Empty,
                SafeSummary = SafeText(reference.SafeSummary)
            })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId));

    private static IEnumerable<DogfoodEvidenceGateHint> SafeGateHints(IEnumerable<DogfoodEvidenceGateHint> hints) =>
        hints
            .Where(hint => hint.Kind != DogfoodEvidenceGateKind.Unknown)
            .Select(hint => new DogfoodEvidenceGateHint
            {
                Kind = hint.Kind,
                SeverityHint = hint.SeverityHint,
                SafeSummary = SafeText(hint.SafeSummary)
            });

    private static IEnumerable<DogfoodEvidenceRisk> SafeRisks(IEnumerable<DogfoodEvidenceRisk> risks) =>
        risks
            .Where(risk => risk.Kind != DogfoodEvidenceRiskKind.Unknown)
            .Select(risk => new DogfoodEvidenceRisk
            {
                Kind = risk.Kind,
                SeverityHint = risk.SeverityHint,
                SafeSummary = SafeText(risk.SafeSummary)
            });

    private static DogfoodEvidenceBundleCandidateResult Result(
        string workflowRunId,
        string workflowStepId,
        string dogfoodEvidenceBundleReferenceId,
        string projectReferenceId,
        string dogfoodRunReferenceId,
        DogfoodEvidenceBundleCandidateStatus status,
        IReadOnlyList<DogfoodEvidenceBundleCandidateReason> reasons,
        IReadOnlyList<DogfoodEvidenceReference> evidenceReferences,
        IReadOnlyList<DogfoodValidationReference> validationReferences,
        IReadOnlyList<DogfoodArtifactReference> artifactReferences,
        IReadOnlyList<DogfoodCandidatePackageReference> candidatePackageReferences,
        IReadOnlyList<DogfoodEvidenceGateHint> gateHints,
        IReadOnlyList<DogfoodEvidenceRisk> risks,
        IReadOnlyList<string> missingEvidence,
        IReadOnlyList<string> summaryLines)
    {
        var nonAuthorityReasons = new[]
        {
            DogfoodEvidenceBundleCandidateReason.DogfoodNotRun,
            DogfoodEvidenceBundleCandidateReason.TestsNotRun,
            DogfoodEvidenceBundleCandidateReason.CommandNotRun,
            DogfoodEvidenceBundleCandidateReason.FilesNotRead,
            DogfoodEvidenceBundleCandidateReason.LogsNotRead,
            DogfoodEvidenceBundleCandidateReason.TraceNotRead,
            DogfoodEvidenceBundleCandidateReason.ValidationNotProven,
            DogfoodEvidenceBundleCandidateReason.ReleaseReadinessNotClaimed,
            DogfoodEvidenceBundleCandidateReason.ToolNotInvoked,
            DogfoodEvidenceBundleCandidateReason.AgentNotDispatched,
            DogfoodEvidenceBundleCandidateReason.ModelNotCalled,
            DogfoodEvidenceBundleCandidateReason.PromptNotBuilt,
            DogfoodEvidenceBundleCandidateReason.ApprovalNotSatisfied,
            DogfoodEvidenceBundleCandidateReason.PolicyNotSatisfied,
            DogfoodEvidenceBundleCandidateReason.WorkflowNotTransitioned,
            DogfoodEvidenceBundleCandidateReason.SourceNotMutated,
            DogfoodEvidenceBundleCandidateReason.PatchNotApplied,
            DogfoodEvidenceBundleCandidateReason.TicketNotCreated,
            DogfoodEvidenceBundleCandidateReason.MemoryNotPromoted,
            DogfoodEvidenceBundleCandidateReason.RetrievalNotActivated,
            DogfoodEvidenceBundleCandidateReason.SqlNotWritten
        };

        var bundleReferenceId = string.IsNullOrWhiteSpace(workflowRunId) ||
            string.IsNullOrWhiteSpace(workflowStepId) ||
            string.IsNullOrWhiteSpace(dogfoodEvidenceBundleReferenceId)
                ? string.Empty
                : $"dogfood-evidence-bundle:{workflowRunId}:{workflowStepId}:{dogfoodEvidenceBundleReferenceId}:{dogfoodRunReferenceId}";

        return new DogfoodEvidenceBundleCandidateResult
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            DogfoodEvidenceBundleReferenceId = dogfoodEvidenceBundleReferenceId,
            BundleReferenceId = bundleReferenceId,
            ProjectReferenceId = projectReferenceId,
            DogfoodRunReferenceId = dogfoodRunReferenceId,
            Status = status,
            Reasons = reasons
                .Concat(nonAuthorityReasons)
                .Where(reason => reason != DogfoodEvidenceBundleCandidateReason.Unknown)
                .Distinct()
                .OrderBy(reason => reason)
                .ToArray(),
            EvidenceReferences = evidenceReferences
                .Where(reference => reference.Kind != DogfoodEvidenceKind.Unknown)
                .OrderBy(reference => reference.Kind)
                .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
                .ToArray(),
            ValidationReferences = validationReferences
                .Where(reference => reference.Kind != DogfoodValidationKind.Unknown)
                .OrderBy(reference => reference.Kind)
                .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
                .ToArray(),
            ArtifactReferences = artifactReferences
                .Where(reference => reference.Kind != DogfoodArtifactKind.Unknown)
                .OrderBy(reference => reference.Kind)
                .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
                .ToArray(),
            CandidatePackageReferences = candidatePackageReferences
                .Where(reference => reference.Kind != DogfoodCandidatePackageKind.Unknown)
                .OrderBy(reference => reference.Kind)
                .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
                .ToArray(),
            GateHints = gateHints
                .Where(hint => hint.Kind != DogfoodEvidenceGateKind.Unknown)
                .OrderBy(hint => hint.Kind)
                .ThenBy(hint => hint.SeverityHint)
                .ToArray(),
            Risks = risks
                .Where(risk => risk.Kind != DogfoodEvidenceRiskKind.Unknown)
                .OrderBy(risk => risk.Kind)
                .ThenBy(risk => risk.SeverityHint)
                .ToArray(),
            MissingEvidence = missingEvidence
                .Where(value => !ContainsUnsafeMaterial(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            SafeBundleSummaryLines = summaryLines
                .Where(value => !ContainsUnsafeMaterial(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            IsBundleOnly = true,
            IsValidationProof = false,
            IsReleaseReady = false,
            CanRunDogfood = false,
            CanRunTests = false,
            CanRunCommand = false,
            CanReadFiles = false,
            CanReadLogs = false,
            CanReadTrace = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanSatisfyApproval = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanMutateSource = false,
            CanApplyPatch = false,
            CanCreateTicket = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false,
            CanWriteSql = false
        };
    }

    private static void AddMissingIfNull(
        string? value,
        List<DogfoodEvidenceBundleCandidateReason> reasons,
        DogfoodEvidenceBundleCandidateReason reason)
    {
        if (value is null)
            reasons.Add(reason);
    }

    private static bool HasUnsafeReferences(IEnumerable<DogfoodEvidenceReference> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.ReferenceId) || ContainsUnsafeMaterial(reference.SafeSummary));

    private static bool HasUnsafeReferences(IEnumerable<DogfoodValidationReference> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.ReferenceId) || ContainsUnsafeMaterial(reference.SafeSummary));

    private static bool HasUnsafeReferences(IEnumerable<DogfoodArtifactReference> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.ReferenceId) || ContainsUnsafeMaterial(reference.SafeSummary));

    private static bool HasUnsafeReferences(IEnumerable<DogfoodCandidatePackageReference> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.ReferenceId) || ContainsUnsafeMaterial(reference.SafeSummary));

    private static bool HasUnsafeReferences(IEnumerable<DogfoodEvidenceGateHint> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.SafeSummary));

    private static bool HasUnsafeReferences(IEnumerable<DogfoodEvidenceRisk> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.SafeSummary));

    private static string? NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMaterial(value) ? null : value.Trim();

    private static string? SafeText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMaterial(value) ? null : value.Trim();

    private static bool ContainsUnsafeMaterial(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record DogfoodEvidenceBundleCandidateRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string DogfoodEvidenceBundleReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string DogfoodRunReferenceId { get; init; }
    public string? SafeRunLabel { get; init; }
    public string? SafeCommandDisplayReferenceId { get; init; }
    public string? SafeOutcomeSummary { get; init; }
    public IReadOnlyList<DogfoodEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<DogfoodValidationReference> ValidationReferences { get; init; } = [];
    public IReadOnlyList<DogfoodArtifactReference> ArtifactReferences { get; init; } = [];
    public IReadOnlyList<DogfoodCandidatePackageReference> CandidatePackageReferences { get; init; } = [];
    public IReadOnlyList<DogfoodEvidenceGateHint> GateHints { get; init; } = [];
    public IReadOnlyList<DogfoodEvidenceRisk> Risks { get; init; } = [];
    public HumanApprovalPackageCandidateResult? HumanApprovalPackage { get; init; }
    public MemoryImprovementPackageCandidateResult? MemoryImprovementPackage { get; init; }
    public ToolRequestGatePreviewCandidateResult? ToolRequestGatePreview { get; init; }
    public ImplementationProposalPackageCandidateResult? ImplementationProposal { get; init; }
    public CriticReviewRequestCandidateResult? CriticReviewRequest { get; init; }
    public TestFailureReviewCandidateResult? TestFailureReview { get; init; }
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record DogfoodEvidenceReference
{
    public required DogfoodEvidenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record DogfoodValidationReference
{
    public required DogfoodValidationKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public DogfoodValidationOutcomeHint OutcomeHint { get; init; } = DogfoodValidationOutcomeHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record DogfoodArtifactReference
{
    public required DogfoodArtifactKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record DogfoodCandidatePackageReference
{
    public required DogfoodCandidatePackageKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record DogfoodEvidenceGateHint
{
    public required DogfoodEvidenceGateKind Kind { get; init; }
    public DogfoodEvidenceSeverityHint SeverityHint { get; init; } = DogfoodEvidenceSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record DogfoodEvidenceRisk
{
    public required DogfoodEvidenceRiskKind Kind { get; init; }
    public DogfoodEvidenceSeverityHint SeverityHint { get; init; } = DogfoodEvidenceSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record DogfoodEvidenceBundleCandidateResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string DogfoodEvidenceBundleReferenceId { get; init; }
    public required string BundleReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string DogfoodRunReferenceId { get; init; }
    public required DogfoodEvidenceBundleCandidateStatus Status { get; init; }
    public required IReadOnlyList<DogfoodEvidenceBundleCandidateReason> Reasons { get; init; }
    public required IReadOnlyList<DogfoodEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<DogfoodValidationReference> ValidationReferences { get; init; }
    public required IReadOnlyList<DogfoodArtifactReference> ArtifactReferences { get; init; }
    public required IReadOnlyList<DogfoodCandidatePackageReference> CandidatePackageReferences { get; init; }
    public required IReadOnlyList<DogfoodEvidenceGateHint> GateHints { get; init; }
    public required IReadOnlyList<DogfoodEvidenceRisk> Risks { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> SafeBundleSummaryLines { get; init; }
    public required bool IsBundleOnly { get; init; }
    public required bool IsValidationProof { get; init; }
    public required bool IsReleaseReady { get; init; }
    public required bool CanRunDogfood { get; init; }
    public required bool CanRunTests { get; init; }
    public required bool CanRunCommand { get; init; }
    public required bool CanReadFiles { get; init; }
    public required bool CanReadLogs { get; init; }
    public required bool CanReadTrace { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanBuildPrompt { get; init; }
    public required bool CanSatisfyApproval { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
    public required bool CanWriteSql { get; init; }
}

public enum DogfoodEvidenceKind
{
    Unknown = 0,
    GovernanceEventReference = 1,
    WorkflowStepEvaluationReference = 2,
    DryRunResultReference = 3,
    ApprovalHaltReference = 4,
    RunReportReference = 5,
    TestReportReference = 6,
    TraceReference = 7,
    CandidatePackageReference = 8,
    ExternalArtifactReference = 9
}

public enum DogfoodValidationKind
{
    Unknown = 0,
    FocusedTestBand = 1,
    WorkflowSweep = 2,
    GovernanceSweep = 3,
    MemoryProposalSweep = 4,
    ApiCliGate = 5,
    BuildValidation = 6,
    DiffCheck = 7,
    ManualReview = 8
}

public enum DogfoodValidationOutcomeHint
{
    Unknown = 0,
    SuppliedPassed = 1,
    SuppliedFailed = 2,
    SuppliedBlocked = 3,
    SuppliedNotRun = 4,
    SuppliedPartial = 5
}

public enum DogfoodArtifactKind
{
    Unknown = 0,
    RunReportReference = 1,
    TraceReference = 2,
    TestOutputReference = 3,
    BuildOutputReference = 4,
    DiffCheckReference = 5,
    ScreenshotReference = 6,
    ExternalArtifactReference = 7
}

public enum DogfoodCandidatePackageKind
{
    Unknown = 0,
    TestFailureReviewCandidate = 1,
    CriticReviewRequestCandidate = 2,
    ImplementationProposalPackageCandidate = 3,
    ToolRequestGatePreviewCandidate = 4,
    MemoryImprovementPackageCandidate = 5,
    HumanApprovalPackageCandidate = 6
}

public enum DogfoodEvidenceGateKind
{
    Unknown = 0,
    HumanReviewRequired = 1,
    ApprovalRequired = 2,
    PolicyEvidenceRequired = 3,
    A2aValidationRequired = 4,
    ThoughtLedgerReferenceRequired = 5,
    DryRunRequired = 6,
    ValidationEvidenceRequired = 7,
    SourceMutationForbidden = 8,
    ToolExecutionForbidden = 9,
    ReleaseReadinessNotClaimed = 10
}

public enum DogfoodEvidenceRiskKind
{
    Unknown = 0,
    MissingEvidence = 1,
    IncompleteValidation = 2,
    StaleEvidence = 3,
    UnverifiedPassClaim = 4,
    ReleaseReadinessOverclaim = 5,
    SourceMutationRisk = 6,
    ToolExecutionRisk = 7,
    ApprovalRisk = 8,
    PolicyRisk = 9,
    TraceabilityRisk = 10,
    OverclaimRisk = 11
}

public enum DogfoodEvidenceSeverityHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum DogfoodEvidenceBundleCandidateStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredEvidence = 3,
    EvidenceBundleProduced = 4
}

public enum DogfoodEvidenceBundleCandidateReason
{
    Unknown = 0,
    BundleOnly = 1,
    SuppliedEvidenceOnly = 2,
    MissingWorkflowRunId = 3,
    MissingWorkflowStepId = 4,
    MissingBundleReference = 5,
    MissingProjectReference = 6,
    MissingDogfoodRunReference = 7,
    MissingEvidenceReference = 8,
    MissingValidationReference = 9,
    MissingArtifactReference = 10,
    MissingGateHint = 11,
    UnsafeInput = 12,
    BlockedByRunnerEvaluation = 13,
    BlockedByDryRun = 14,
    BlockedByRouteSuggestion = 15,
    BlockedByHumanApprovalPackage = 16,
    BlockedByMemoryImprovementPackage = 17,
    BlockedByToolRequestGatePreview = 18,
    BlockedByImplementationProposal = 19,
    BlockedByCriticReviewRequest = 20,
    BlockedByTestFailureReview = 21,
    DogfoodNotRun = 22,
    TestsNotRun = 23,
    CommandNotRun = 24,
    FilesNotRead = 25,
    LogsNotRead = 26,
    TraceNotRead = 27,
    ValidationNotProven = 28,
    ReleaseReadinessNotClaimed = 29,
    ToolNotInvoked = 30,
    AgentNotDispatched = 31,
    ModelNotCalled = 32,
    PromptNotBuilt = 33,
    ApprovalNotSatisfied = 34,
    PolicyNotSatisfied = 35,
    WorkflowNotTransitioned = 36,
    SourceNotMutated = 37,
    PatchNotApplied = 38,
    TicketNotCreated = 39,
    MemoryNotPromoted = 40,
    RetrievalNotActivated = 41,
    SqlNotWritten = 42
}
