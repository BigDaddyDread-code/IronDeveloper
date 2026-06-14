namespace IronDev.Core.Workflow;

public interface IHumanApprovalPackageCandidateWorkflow
{
    HumanApprovalPackageCandidateResult Prepare(HumanApprovalPackageCandidateRequest? request);
}

public sealed class HumanApprovalPackageCandidateWorkflow : IHumanApprovalPackageCandidateWorkflow
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
        "wholepatch",
        "whole patch",
        "entirepatch",
        "entire patch",
        "patchpayload",
        "patch payload",
        "approval granted",
        "approval satisfied",
        "policy satisfied",
        "workflow may continue",
        "workflow continued",
        "source mutation authorized",
        "tool execution authorized",
        "memory promotion authorized",
        "retrieval activation authorized",
        "release approved",
        "decision made",
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

    public HumanApprovalPackageCandidateResult Prepare(HumanApprovalPackageCandidateRequest? request)
    {
        if (request is null)
        {
            return Result(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                HumanApprovalPackageCandidateStatus.InvalidRequest,
                HumanApprovalTargetKind.Unknown,
                string.Empty,
                HumanApprovalKind.Unknown,
                HumanApprovalRequestedDecision.Unknown,
                [HumanApprovalPackageCandidateReason.MissingWorkflowRunId],
                [],
                [],
                [],
                [],
                ["workflow run id"],
                []);
        }

        var workflowRunId = NormalizeId(request.WorkflowRunId);
        var workflowStepId = NormalizeId(request.WorkflowStepId);
        var approvalPackageReferenceId = NormalizeId(request.ApprovalPackageReferenceId);
        var projectReferenceId = NormalizeId(request.ProjectReferenceId);
        var targetReferenceId = NormalizeId(request.TargetReferenceId);

        var invalidReasons = new List<HumanApprovalPackageCandidateReason>();
        var missingEvidence = new List<string>();

        AddMissingIfNull(workflowRunId, invalidReasons, HumanApprovalPackageCandidateReason.MissingWorkflowRunId);
        AddMissingIfNull(workflowStepId, invalidReasons, HumanApprovalPackageCandidateReason.MissingWorkflowStepId);
        AddMissingIfNull(approvalPackageReferenceId, invalidReasons, HumanApprovalPackageCandidateReason.MissingPackageReference);
        AddMissingIfNull(projectReferenceId, invalidReasons, HumanApprovalPackageCandidateReason.MissingProjectReference);
        AddMissingIfNull(targetReferenceId, invalidReasons, HumanApprovalPackageCandidateReason.MissingTargetReference);

        if (request.TargetKind == HumanApprovalTargetKind.Unknown)
            invalidReasons.Add(HumanApprovalPackageCandidateReason.InvalidTargetKind);

        if (request.ApprovalKind == HumanApprovalKind.Unknown)
            invalidReasons.Add(HumanApprovalPackageCandidateReason.InvalidApprovalKind);

        if (request.RequestedDecision == HumanApprovalRequestedDecision.Unknown)
            invalidReasons.Add(HumanApprovalPackageCandidateReason.InvalidRequestedDecision);

        if (ContainsUnsafeMaterial(request.WorkflowRunId) ||
            ContainsUnsafeMaterial(request.WorkflowStepId) ||
            ContainsUnsafeMaterial(request.ApprovalPackageReferenceId) ||
            ContainsUnsafeMaterial(request.ProjectReferenceId) ||
            ContainsUnsafeMaterial(request.TargetReferenceId) ||
            ContainsUnsafeMaterial(request.SafeApprovalSummary) ||
            ContainsUnsafeMaterial(request.CorrelationId) ||
            HasUnsafeReferences(request.EvidenceReferences) ||
            HasUnsafeReferences(request.CandidatePackageReferences) ||
            HasUnsafeReferences(request.GateHints) ||
            HasUnsafeReferences(request.Risks))
        {
            invalidReasons.Add(HumanApprovalPackageCandidateReason.UnsafeInput);
        }

        if (request.EvidenceReferences.Any(reference => reference.Kind == HumanApprovalEvidenceKind.Unknown ||
            string.IsNullOrWhiteSpace(NormalizeId(reference.ReferenceId))))
        {
            invalidReasons.Add(HumanApprovalPackageCandidateReason.MissingEvidenceReference);
        }

        if (request.GateHints.Any(hint => hint.Kind == HumanApprovalGateKind.Unknown))
            invalidReasons.Add(HumanApprovalPackageCandidateReason.MissingGateHint);

        if (request.CandidatePackageReferences.Any(reference => reference.Kind == HumanApprovalCandidatePackageKind.Unknown ||
            string.IsNullOrWhiteSpace(NormalizeId(reference.ReferenceId))))
        {
            invalidReasons.Add(HumanApprovalPackageCandidateReason.MissingEvidenceReference);
        }

        if (request.Risks.Any(risk => risk.Kind == HumanApprovalRiskKind.Unknown))
            invalidReasons.Add(HumanApprovalPackageCandidateReason.UnsafeInput);

        var evidenceReferences = SafeEvidenceReferences(request.EvidenceReferences).ToList();
        var candidatePackageReferences = SafeCandidatePackageReferences(request.CandidatePackageReferences).ToList();
        var gateHints = SafeGateHints(request.GateHints).ToList();
        var risks = SafeRisks(request.Risks).ToList();

        if (invalidReasons.Count > 0)
        {
            return Result(
                workflowRunId ?? string.Empty,
                workflowStepId ?? string.Empty,
                approvalPackageReferenceId ?? string.Empty,
                projectReferenceId ?? string.Empty,
                targetReferenceId ?? string.Empty,
                HumanApprovalPackageCandidateStatus.InvalidRequest,
                request.TargetKind,
                targetReferenceId ?? string.Empty,
                request.ApprovalKind,
                request.RequestedDecision,
                invalidReasons,
                evidenceReferences,
                candidatePackageReferences,
                gateHints,
                risks,
                missingEvidence,
                []);
        }

        var blockReasons = new List<HumanApprovalPackageCandidateReason>();
        AddWorkflowGateBlocks(request, blockReasons);
        AddUpstreamPackageBlocks(request, blockReasons);

        if (blockReasons.Count > 0)
        {
            return Result(
                workflowRunId!,
                workflowStepId!,
                approvalPackageReferenceId!,
                projectReferenceId!,
                targetReferenceId!,
                HumanApprovalPackageCandidateStatus.BlockedByWorkflowGate,
                request.TargetKind,
                targetReferenceId!,
                request.ApprovalKind,
                request.RequestedDecision,
                blockReasons,
                evidenceReferences,
                candidatePackageReferences,
                gateHints,
                risks,
                missingEvidence,
                []);
        }

        AddUpstreamPackageEvidence(request, evidenceReferences, candidatePackageReferences);
        AddMissingRequiredEvidence(request, evidenceReferences, gateHints, missingEvidence);

        if (missingEvidence.Count > 0)
        {
            return Result(
                workflowRunId!,
                workflowStepId!,
                approvalPackageReferenceId!,
                projectReferenceId!,
                targetReferenceId!,
                HumanApprovalPackageCandidateStatus.MissingRequiredApprovalEvidence,
                request.TargetKind,
                targetReferenceId!,
                request.ApprovalKind,
                request.RequestedDecision,
                [HumanApprovalPackageCandidateReason.MissingEvidenceReference],
                evidenceReferences,
                candidatePackageReferences,
                gateHints,
                risks,
                missingEvidence,
                []);
        }

        var summaryLines = SummaryLines(request, evidenceReferences, candidatePackageReferences, gateHints, risks);

        return Result(
            workflowRunId!,
            workflowStepId!,
            approvalPackageReferenceId!,
            projectReferenceId!,
            targetReferenceId!,
            HumanApprovalPackageCandidateStatus.ApprovalPackageProduced,
            request.TargetKind,
            targetReferenceId!,
            request.ApprovalKind,
            request.RequestedDecision,
            [
                HumanApprovalPackageCandidateReason.PackageOnly,
                HumanApprovalPackageCandidateReason.SuppliedEvidenceOnly
            ],
            evidenceReferences,
            candidatePackageReferences,
            gateHints,
            risks,
            [],
            summaryLines);
    }

    private static void AddWorkflowGateBlocks(HumanApprovalPackageCandidateRequest request, List<HumanApprovalPackageCandidateReason> reasons)
    {
        if (request.StepEvaluation is not null &&
            request.StepEvaluation.Eligibility is not WorkflowStepRunnerEligibility.EligibleForFutureExecution and
                not WorkflowStepRunnerEligibility.BlockedApprovalRequired)
        {
            reasons.Add(HumanApprovalPackageCandidateReason.BlockedByRunnerEvaluation);
        }

        if (request.DryRunResult is not null &&
            request.DryRunResult.Status is not WorkflowDryRunStatus.DryRunCompleted and
                not WorkflowDryRunStatus.BlockedByApprovalRequiredHalt)
        {
            reasons.Add(HumanApprovalPackageCandidateReason.BlockedByDryRun);
        }

        if (request.RouteSuggestion is not null &&
            request.RouteSuggestion.Label is not BoxedLangGraphRouteLabel.EligibleForDryRun and
                not BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable and
                not BoxedLangGraphRouteLabel.BlockedApprovalRequired)
        {
            reasons.Add(HumanApprovalPackageCandidateReason.BlockedByRouteSuggestion);
        }
    }

    private static void AddUpstreamPackageBlocks(HumanApprovalPackageCandidateRequest request, List<HumanApprovalPackageCandidateReason> reasons)
    {
        if (request.MemoryImprovementPackage is not null &&
            request.MemoryImprovementPackage.Status != MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced)
        {
            reasons.Add(HumanApprovalPackageCandidateReason.BlockedByMemoryImprovementPackage);
        }

        if (request.ToolRequestGatePreview is not null &&
            request.ToolRequestGatePreview.Status != ToolRequestGatePreviewCandidateStatus.GatePreviewProduced)
        {
            reasons.Add(HumanApprovalPackageCandidateReason.BlockedByToolRequestGatePreview);
        }

        if (request.ImplementationProposal is not null &&
            request.ImplementationProposal.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced)
        {
            reasons.Add(HumanApprovalPackageCandidateReason.BlockedByImplementationProposal);
        }

        if (request.CriticReviewRequest is not null &&
            request.CriticReviewRequest.Status != CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced)
        {
            reasons.Add(HumanApprovalPackageCandidateReason.BlockedByCriticReviewRequest);
        }

        if (request.TestFailureReview is not null &&
            request.TestFailureReview.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced)
        {
            reasons.Add(HumanApprovalPackageCandidateReason.BlockedByTestFailureReview);
        }
    }

    private static void AddUpstreamPackageEvidence(
        HumanApprovalPackageCandidateRequest request,
        List<HumanApprovalEvidenceReference> evidenceReferences,
        List<HumanApprovalCandidatePackageReference> candidatePackageReferences)
    {
        AddMemoryPackage(request.MemoryImprovementPackage, evidenceReferences, candidatePackageReferences);
        AddToolPreview(request.ToolRequestGatePreview, evidenceReferences, candidatePackageReferences);
        AddImplementationProposal(request.ImplementationProposal, evidenceReferences, candidatePackageReferences);
        AddCriticReview(request.CriticReviewRequest, evidenceReferences, candidatePackageReferences);
        AddTestFailureReview(request.TestFailureReview, evidenceReferences, candidatePackageReferences);
    }

    private static void AddMemoryPackage(
        MemoryImprovementPackageCandidateResult? package,
        List<HumanApprovalEvidenceReference> evidenceReferences,
        List<HumanApprovalCandidatePackageReference> candidatePackageReferences)
    {
        if (package?.Status != MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(package.PackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new HumanApprovalEvidenceReference
        {
            Kind = HumanApprovalEvidenceKind.MemoryImprovementPackageReference,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Memory improvement package supplied as review material."
        });

        candidatePackageReferences.Add(new HumanApprovalCandidatePackageReference
        {
            Kind = HumanApprovalCandidatePackageKind.MemoryImprovementPackageCandidate,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Memory improvement package candidate."
        });
    }

    private static void AddToolPreview(
        ToolRequestGatePreviewCandidateResult? preview,
        List<HumanApprovalEvidenceReference> evidenceReferences,
        List<HumanApprovalCandidatePackageReference> candidatePackageReferences)
    {
        if (preview?.Status != ToolRequestGatePreviewCandidateStatus.GatePreviewProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(preview.PreviewPackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new HumanApprovalEvidenceReference
        {
            Kind = HumanApprovalEvidenceKind.ToolRequestPreviewReference,
            ReferenceId = preview.PreviewPackageReferenceId,
            SafeSummary = "Tool request gate preview supplied as review material."
        });

        candidatePackageReferences.Add(new HumanApprovalCandidatePackageReference
        {
            Kind = HumanApprovalCandidatePackageKind.ToolRequestGatePreviewCandidate,
            ReferenceId = preview.PreviewPackageReferenceId,
            SafeSummary = "Tool request gate preview candidate."
        });
    }

    private static void AddImplementationProposal(
        ImplementationProposalPackageCandidateResult? proposal,
        List<HumanApprovalEvidenceReference> evidenceReferences,
        List<HumanApprovalCandidatePackageReference> candidatePackageReferences)
    {
        if (proposal?.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(proposal.ProposalPackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new HumanApprovalEvidenceReference
        {
            Kind = HumanApprovalEvidenceKind.ImplementationProposalReference,
            ReferenceId = proposal.ProposalPackageReferenceId,
            SafeSummary = "Implementation proposal package supplied as review material."
        });

        candidatePackageReferences.Add(new HumanApprovalCandidatePackageReference
        {
            Kind = HumanApprovalCandidatePackageKind.ImplementationProposalPackageCandidate,
            ReferenceId = proposal.ProposalPackageReferenceId,
            SafeSummary = "Implementation proposal package candidate."
        });
    }

    private static void AddCriticReview(
        CriticReviewRequestCandidateResult? review,
        List<HumanApprovalEvidenceReference> evidenceReferences,
        List<HumanApprovalCandidatePackageReference> candidatePackageReferences)
    {
        if (review?.Status != CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(review.ReviewPackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new HumanApprovalEvidenceReference
        {
            Kind = HumanApprovalEvidenceKind.CriticReviewRequestReference,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Critic review request package supplied as review material."
        });

        candidatePackageReferences.Add(new HumanApprovalCandidatePackageReference
        {
            Kind = HumanApprovalCandidatePackageKind.CriticReviewRequestCandidate,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Critic review request candidate."
        });
    }

    private static void AddTestFailureReview(
        TestFailureReviewCandidateResult? review,
        List<HumanApprovalEvidenceReference> evidenceReferences,
        List<HumanApprovalCandidatePackageReference> candidatePackageReferences)
    {
        if (review?.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced ||
            string.IsNullOrWhiteSpace(NormalizeId(review.ReviewPackageReferenceId)))
        {
            return;
        }

        evidenceReferences.Add(new HumanApprovalEvidenceReference
        {
            Kind = HumanApprovalEvidenceKind.TestFailureReviewReference,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Test failure review package supplied as review material."
        });

        candidatePackageReferences.Add(new HumanApprovalCandidatePackageReference
        {
            Kind = HumanApprovalCandidatePackageKind.TestFailureReviewCandidate,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Test failure review candidate."
        });
    }

    private static void AddMissingRequiredEvidence(
        HumanApprovalPackageCandidateRequest request,
        IReadOnlyList<HumanApprovalEvidenceReference> evidenceReferences,
        IReadOnlyList<HumanApprovalGateHint> gateHints,
        List<string> missingEvidence)
    {
        if (string.IsNullOrWhiteSpace(request.SafeApprovalSummary))
            missingEvidence.Add("safe approval summary");

        if (evidenceReferences.Count == 0)
            missingEvidence.Add("approval evidence reference");

        if (gateHints.Count == 0)
            missingEvidence.Add("approval gate hint");

        if (RequiresMemoryPackage(request.TargetKind) && request.MemoryImprovementPackage is null)
            missingEvidence.Add("memory improvement package candidate");

        if (RequiresToolPreview(request.TargetKind) && request.ToolRequestGatePreview is null)
            missingEvidence.Add("tool request gate preview candidate");

        if (RequiresImplementationProposal(request.TargetKind) && request.ImplementationProposal is null)
            missingEvidence.Add("implementation proposal package candidate");

        if (request.TargetKind == HumanApprovalTargetKind.CriticReviewRequest && request.CriticReviewRequest is null)
            missingEvidence.Add("critic review request candidate");

        if (request.TargetKind == HumanApprovalTargetKind.TestFailureReview && request.TestFailureReview is null)
            missingEvidence.Add("test failure review candidate");
    }

    private static IReadOnlyList<string> SummaryLines(
        HumanApprovalPackageCandidateRequest request,
        IReadOnlyList<HumanApprovalEvidenceReference> evidenceReferences,
        IReadOnlyList<HumanApprovalCandidatePackageReference> candidatePackageReferences,
        IReadOnlyList<HumanApprovalGateHint> gateHints,
        IReadOnlyList<HumanApprovalRisk> risks)
    {
        var lines = new List<string>
        {
            "Human approval package was produced from supplied evidence only.",
            "No approval was granted.",
            "No rejection was recorded.",
            "Approval was not satisfied.",
            "Policy was not satisfied.",
            "Workflow was not transitioned.",
            "Package requires later human/governed review.",
            $"Target kind: {request.TargetKind}.",
            $"Approval kind: {request.ApprovalKind}.",
            $"Requested decision for later review: {request.RequestedDecision}.",
            $"Evidence references: {evidenceReferences.Count}.",
            $"Candidate package references: {candidatePackageReferences.Count}.",
            $"Gate hints: {gateHints.Count}.",
            $"Risks: {risks.Count}."
        };

        if (!string.IsNullOrWhiteSpace(request.SafeApprovalSummary) && !ContainsUnsafeMaterial(request.SafeApprovalSummary))
            lines.Add($"Safe approval summary: {request.SafeApprovalSummary.Trim()}");

        return lines;
    }

    private static bool RequiresMemoryPackage(HumanApprovalTargetKind targetKind) =>
        targetKind is HumanApprovalTargetKind.MemoryImprovementPackage or
            HumanApprovalTargetKind.MemoryPromotionCandidate or
            HumanApprovalTargetKind.RetrievalActivationCandidate;

    private static bool RequiresToolPreview(HumanApprovalTargetKind targetKind) =>
        targetKind == HumanApprovalTargetKind.ToolRequestGatePreview;

    private static bool RequiresImplementationProposal(HumanApprovalTargetKind targetKind) =>
        targetKind is HumanApprovalTargetKind.ImplementationProposalPackage or
            HumanApprovalTargetKind.SourceApplyCandidate;

    private static IEnumerable<HumanApprovalEvidenceReference> SafeEvidenceReferences(IEnumerable<HumanApprovalEvidenceReference> references) =>
        references
            .Where(reference => reference.Kind != HumanApprovalEvidenceKind.Unknown)
            .Select(reference => new HumanApprovalEvidenceReference
            {
                Kind = reference.Kind,
                ReferenceId = NormalizeId(reference.ReferenceId) ?? string.Empty,
                SafeSummary = SafeText(reference.SafeSummary)
            })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId));

    private static IEnumerable<HumanApprovalCandidatePackageReference> SafeCandidatePackageReferences(IEnumerable<HumanApprovalCandidatePackageReference> references) =>
        references
            .Where(reference => reference.Kind != HumanApprovalCandidatePackageKind.Unknown)
            .Select(reference => new HumanApprovalCandidatePackageReference
            {
                Kind = reference.Kind,
                ReferenceId = NormalizeId(reference.ReferenceId) ?? string.Empty,
                SafeSummary = SafeText(reference.SafeSummary)
            })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId));

    private static IEnumerable<HumanApprovalGateHint> SafeGateHints(IEnumerable<HumanApprovalGateHint> hints) =>
        hints
            .Where(hint => hint.Kind != HumanApprovalGateKind.Unknown)
            .Select(hint => new HumanApprovalGateHint
            {
                Kind = hint.Kind,
                SeverityHint = hint.SeverityHint,
                SafeSummary = SafeText(hint.SafeSummary)
            });

    private static IEnumerable<HumanApprovalRisk> SafeRisks(IEnumerable<HumanApprovalRisk> risks) =>
        risks
            .Where(risk => risk.Kind != HumanApprovalRiskKind.Unknown)
            .Select(risk => new HumanApprovalRisk
            {
                Kind = risk.Kind,
                SeverityHint = risk.SeverityHint,
                SafeSummary = SafeText(risk.SafeSummary)
            });

    private static HumanApprovalPackageCandidateResult Result(
        string workflowRunId,
        string workflowStepId,
        string approvalPackageReferenceId,
        string projectReferenceId,
        string targetReferenceId,
        HumanApprovalPackageCandidateStatus status,
        HumanApprovalTargetKind targetKind,
        string targetReferenceIdForResult,
        HumanApprovalKind approvalKind,
        HumanApprovalRequestedDecision requestedDecision,
        IReadOnlyList<HumanApprovalPackageCandidateReason> reasons,
        IReadOnlyList<HumanApprovalEvidenceReference> evidenceReferences,
        IReadOnlyList<HumanApprovalCandidatePackageReference> candidatePackageReferences,
        IReadOnlyList<HumanApprovalGateHint> gateHints,
        IReadOnlyList<HumanApprovalRisk> risks,
        IReadOnlyList<string> missingEvidence,
        IReadOnlyList<string> summaryLines)
    {
        var nonAuthorityReasons = new[]
        {
            HumanApprovalPackageCandidateReason.ApprovalNotGranted,
            HumanApprovalPackageCandidateReason.RejectionNotRecorded,
            HumanApprovalPackageCandidateReason.ApprovalNotSatisfied,
            HumanApprovalPackageCandidateReason.PolicyNotSatisfied,
            HumanApprovalPackageCandidateReason.WorkflowNotTransitioned,
            HumanApprovalPackageCandidateReason.SourceNotMutated,
            HumanApprovalPackageCandidateReason.PatchNotApplied,
            HumanApprovalPackageCandidateReason.ToolNotInvoked,
            HumanApprovalPackageCandidateReason.AgentNotDispatched,
            HumanApprovalPackageCandidateReason.ModelNotCalled,
            HumanApprovalPackageCandidateReason.PromptNotBuilt,
            HumanApprovalPackageCandidateReason.TicketNotCreated,
            HumanApprovalPackageCandidateReason.MemoryNotPromoted,
            HumanApprovalPackageCandidateReason.RetrievalNotActivated,
            HumanApprovalPackageCandidateReason.SqlNotWritten
        };

        var packageReferenceId = string.IsNullOrWhiteSpace(workflowRunId) ||
            string.IsNullOrWhiteSpace(workflowStepId) ||
            string.IsNullOrWhiteSpace(approvalPackageReferenceId)
                ? string.Empty
                : $"human-approval-package:{workflowRunId}:{workflowStepId}:{approvalPackageReferenceId}:{targetReferenceId}";

        return new HumanApprovalPackageCandidateResult
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            ApprovalPackageReferenceId = approvalPackageReferenceId,
            PackageReferenceId = packageReferenceId,
            ProjectReferenceId = projectReferenceId,
            Status = status,
            TargetKind = targetKind,
            TargetReferenceId = targetReferenceIdForResult,
            ApprovalKind = approvalKind,
            RequestedDecision = requestedDecision,
            Reasons = reasons
                .Concat(nonAuthorityReasons)
                .Where(reason => reason != HumanApprovalPackageCandidateReason.Unknown)
                .Distinct()
                .OrderBy(reason => reason)
                .ToArray(),
            EvidenceReferences = evidenceReferences
                .Where(reference => reference.Kind != HumanApprovalEvidenceKind.Unknown)
                .OrderBy(reference => reference.Kind)
                .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
                .ToArray(),
            CandidatePackageReferences = candidatePackageReferences
                .Where(reference => reference.Kind != HumanApprovalCandidatePackageKind.Unknown)
                .OrderBy(reference => reference.Kind)
                .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
                .ToArray(),
            GateHints = gateHints
                .Where(hint => hint.Kind != HumanApprovalGateKind.Unknown)
                .OrderBy(hint => hint.Kind)
                .ThenBy(hint => hint.SeverityHint)
                .ToArray(),
            Risks = risks
                .Where(risk => risk.Kind != HumanApprovalRiskKind.Unknown)
                .OrderBy(risk => risk.Kind)
                .ThenBy(risk => risk.SeverityHint)
                .ToArray(),
            MissingEvidence = missingEvidence
                .Where(value => !ContainsUnsafeMaterial(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            SafePackageSummaryLines = summaryLines
                .Where(value => !ContainsUnsafeMaterial(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            IsPackageOnly = true,
            IsApprovalDecision = false,
            IsApproved = false,
            IsRejected = false,
            CanSatisfyApproval = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanMutateSource = false,
            CanApplyPatch = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanCreateTicket = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false,
            CanWriteSql = false
        };
    }

    private static void AddMissingIfNull(
        string? value,
        List<HumanApprovalPackageCandidateReason> reasons,
        HumanApprovalPackageCandidateReason reason)
    {
        if (value is null)
            reasons.Add(reason);
    }

    private static bool HasUnsafeReferences(IEnumerable<HumanApprovalEvidenceReference> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.ReferenceId) || ContainsUnsafeMaterial(reference.SafeSummary));

    private static bool HasUnsafeReferences(IEnumerable<HumanApprovalCandidatePackageReference> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.ReferenceId) || ContainsUnsafeMaterial(reference.SafeSummary));

    private static bool HasUnsafeReferences(IEnumerable<HumanApprovalGateHint> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.SafeSummary));

    private static bool HasUnsafeReferences(IEnumerable<HumanApprovalRisk> references) =>
        references.Any(reference => ContainsUnsafeMaterial(reference.SafeSummary));

    private static string? NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMaterial(value) ? null : value.Trim();

    private static string? SafeText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMaterial(value) ? null : value.Trim();

    private static bool ContainsUnsafeMaterial(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record HumanApprovalPackageCandidateRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ApprovalPackageReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required HumanApprovalTargetKind TargetKind { get; init; }
    public required string TargetReferenceId { get; init; }
    public required HumanApprovalKind ApprovalKind { get; init; }
    public required HumanApprovalRequestedDecision RequestedDecision { get; init; }
    public string? SafeApprovalSummary { get; init; }
    public IReadOnlyList<HumanApprovalEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<HumanApprovalCandidatePackageReference> CandidatePackageReferences { get; init; } = [];
    public IReadOnlyList<HumanApprovalGateHint> GateHints { get; init; } = [];
    public IReadOnlyList<HumanApprovalRisk> Risks { get; init; } = [];
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

public sealed record HumanApprovalEvidenceReference
{
    public required HumanApprovalEvidenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record HumanApprovalCandidatePackageReference
{
    public required HumanApprovalCandidatePackageKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record HumanApprovalGateHint
{
    public required HumanApprovalGateKind Kind { get; init; }
    public HumanApprovalSeverityHint SeverityHint { get; init; } = HumanApprovalSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record HumanApprovalRisk
{
    public required HumanApprovalRiskKind Kind { get; init; }
    public HumanApprovalSeverityHint SeverityHint { get; init; } = HumanApprovalSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record HumanApprovalPackageCandidateResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ApprovalPackageReferenceId { get; init; }
    public required string PackageReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required HumanApprovalPackageCandidateStatus Status { get; init; }
    public required HumanApprovalTargetKind TargetKind { get; init; }
    public required string TargetReferenceId { get; init; }
    public required HumanApprovalKind ApprovalKind { get; init; }
    public required HumanApprovalRequestedDecision RequestedDecision { get; init; }
    public required IReadOnlyList<HumanApprovalPackageCandidateReason> Reasons { get; init; }
    public required IReadOnlyList<HumanApprovalEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<HumanApprovalCandidatePackageReference> CandidatePackageReferences { get; init; }
    public required IReadOnlyList<HumanApprovalGateHint> GateHints { get; init; }
    public required IReadOnlyList<HumanApprovalRisk> Risks { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> SafePackageSummaryLines { get; init; }
    public required bool IsPackageOnly { get; init; }
    public required bool IsApprovalDecision { get; init; }
    public required bool IsApproved { get; init; }
    public required bool IsRejected { get; init; }
    public required bool CanSatisfyApproval { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanBuildPrompt { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
    public required bool CanWriteSql { get; init; }
}

public enum HumanApprovalTargetKind
{
    Unknown = 0,
    ImplementationProposalPackage = 1,
    ToolRequestGatePreview = 2,
    MemoryImprovementPackage = 3,
    CriticReviewRequest = 4,
    TestFailureReview = 5,
    SourceApplyCandidate = 6,
    RetrievalActivationCandidate = 7,
    MemoryPromotionCandidate = 8,
    WorkflowContinuationCandidate = 9
}

public enum HumanApprovalKind
{
    Unknown = 0,
    ReviewOnly = 1,
    SourceApplyApprovalRequired = 2,
    ToolExecutionApprovalRequired = 3,
    MemoryPromotionApprovalRequired = 4,
    RetrievalActivationApprovalRequired = 5,
    WorkflowContinuationApprovalRequired = 6,
    PolicyExceptionApprovalRequired = 7
}

public enum HumanApprovalRequestedDecision
{
    Unknown = 0,
    RequestHumanReview = 1,
    RequestApproveOrRejectLater = 2,
    RequestMoreEvidenceLater = 3,
    RequestPolicyReviewLater = 4
}

public enum HumanApprovalEvidenceKind
{
    Unknown = 0,
    GovernanceEventReference = 1,
    WorkflowStepEvaluationReference = 2,
    DryRunResultReference = 3,
    TestFailureReviewReference = 4,
    CriticReviewRequestReference = 5,
    ImplementationProposalReference = 6,
    ToolRequestPreviewReference = 7,
    MemoryImprovementPackageReference = 8,
    ApprovalHaltReference = 9,
    ExternalArtifactReference = 10
}

public enum HumanApprovalCandidatePackageKind
{
    Unknown = 0,
    TestFailureReviewCandidate = 1,
    CriticReviewRequestCandidate = 2,
    ImplementationProposalPackageCandidate = 3,
    ToolRequestGatePreviewCandidate = 4,
    MemoryImprovementPackageCandidate = 5
}

public enum HumanApprovalGateKind
{
    Unknown = 0,
    HumanReviewRequired = 1,
    ApprovalRequired = 2,
    PolicyEvidenceRequired = 3,
    A2aValidationRequired = 4,
    ThoughtLedgerReferenceRequired = 5,
    DryRunRequired = 6,
    SourceMutationForbiddenUntilApproved = 7,
    ToolExecutionForbiddenUntilApproved = 8,
    MemoryPromotionForbiddenUntilApproved = 9,
    RetrievalActivationForbiddenUntilApproved = 10,
    WorkflowContinuationForbiddenUntilApproved = 11
}

public enum HumanApprovalRiskKind
{
    Unknown = 0,
    InsufficientEvidence = 1,
    ApprovalAuthorityRisk = 2,
    PolicyRisk = 3,
    SourceMutationRisk = 4,
    ToolExecutionRisk = 5,
    MemoryPromotionRisk = 6,
    RetrievalActivationRisk = 7,
    WorkflowContinuationRisk = 8,
    OverclaimRisk = 9
}

public enum HumanApprovalSeverityHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum HumanApprovalPackageCandidateStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredApprovalEvidence = 3,
    ApprovalPackageProduced = 4
}

public enum HumanApprovalPackageCandidateReason
{
    Unknown = 0,
    PackageOnly = 1,
    SuppliedEvidenceOnly = 2,
    MissingWorkflowRunId = 3,
    MissingWorkflowStepId = 4,
    MissingPackageReference = 5,
    MissingProjectReference = 6,
    MissingTargetReference = 7,
    InvalidTargetKind = 8,
    InvalidApprovalKind = 9,
    InvalidRequestedDecision = 10,
    MissingEvidenceReference = 11,
    MissingGateHint = 12,
    MissingApprovalSummary = 13,
    UnsafeInput = 14,
    BlockedByRunnerEvaluation = 15,
    BlockedByDryRun = 16,
    BlockedByRouteSuggestion = 17,
    BlockedByMemoryImprovementPackage = 18,
    BlockedByToolRequestGatePreview = 19,
    BlockedByImplementationProposal = 20,
    BlockedByCriticReviewRequest = 21,
    BlockedByTestFailureReview = 22,
    ApprovalNotGranted = 23,
    RejectionNotRecorded = 24,
    ApprovalNotSatisfied = 25,
    PolicyNotSatisfied = 26,
    WorkflowNotTransitioned = 27,
    SourceNotMutated = 28,
    PatchNotApplied = 29,
    ToolNotInvoked = 30,
    AgentNotDispatched = 31,
    ModelNotCalled = 32,
    PromptNotBuilt = 33,
    TicketNotCreated = 34,
    MemoryNotPromoted = 35,
    RetrievalNotActivated = 36,
    SqlNotWritten = 37
}
