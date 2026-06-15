using IronDev.Core.Workflow;

namespace IronDev.Infrastructure.Workflow;

public sealed class ApplyPreviewService : IApplyPreviewService
{
    private const int DefaultTake = 10;
    private const int MaxTake = 50;

    private static readonly string[] UnsafeMarkers =
    [
        "private reasoning",
        "hidden reasoning",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "entire patch",
        "entirepatch",
        "patch payload",
        "patch applied",
        "patchapplied",
        "ready to apply",
        "readytoapply",
        "validation passed",
        "validationpassed",
        "rollback completed",
        "rollbackcompleted",
        "approval granted",
        "approvalgranted",
        "policy satisfied",
        "policysatisfied",
        "execution allowed",
        "executionallowed",
        "tool executed",
        "toolexecuted",
        "source mutated",
        "sourcemutated",
        "memory promoted",
        "memorypromoted",
        "authority transferred",
        "authoritytransferred",
        "release approved",
        "releaseapproved"
    ];

    private readonly IApplyDryRunStore _dryRunStore;

    public ApplyPreviewService(IApplyDryRunStore dryRunStore)
    {
        _dryRunStore = dryRunStore ?? throw new ArgumentNullException(nameof(dryRunStore));
    }

    public async Task<ApplyPreviewResponse> GetPreviewAsync(ApplyPreviewRequest request, CancellationToken cancellationToken = default)
    {
        request ??= new ApplyPreviewRequest();

        var issues = Validate(request);
        if (issues.Count > 0)
        {
            return Response(
                ApplyPreviewStatus.InvalidRequest,
                workflowRunId: SafeText(request.WorkflowRunId),
                workflowStepId: SafeText(request.WorkflowStepId),
                controlledApplyPlanReferenceId: SafeText(request.ControlledApplyPlanReferenceId),
                dryRuns: [],
                issues: issues);
        }

        var workflowRunId = Normalize(request.WorkflowRunId);
        var workflowStepId = Normalize(request.WorkflowStepId);
        var planId = Normalize(request.ControlledApplyPlanReferenceId);
        var take = Math.Clamp(request.TakeDryRuns <= 0 ? DefaultTake : request.TakeDryRuns, 1, MaxTake);

        var summaries = request.IncludeDryRunSummaries
            ? await LoadSummariesAsync(workflowRunId, workflowStepId, planId, take, cancellationToken)
            : [];

        if (summaries.Count == 0)
        {
            return Response(
                ApplyPreviewStatus.MissingPreviewEvidence,
                workflowRunId,
                workflowStepId,
                planId,
                dryRuns: [],
                issues:
                [
                    new ApplyPreviewIssue
                    {
                        Kind = ApplyPreviewIssueKind.MissingDryRunEvidence,
                        Field = nameof(ApplyDryRunSummary),
                        Message = "No apply dry-run receipt summaries were found for this workflow run and step."
                    }
                ]);
        }

        var effectivePlanId = string.IsNullOrWhiteSpace(planId)
            ? summaries[0].ControlledApplyPlanReferenceId
            : planId;

        return Response(
            ApplyPreviewStatus.PreviewAvailable,
            workflowRunId,
            workflowStepId,
            effectivePlanId,
            dryRuns: summaries,
            issues: []);
    }

    private async Task<IReadOnlyList<ApplyDryRunSummary>> LoadSummariesAsync(
        string workflowRunId,
        string workflowStepId,
        string controlledApplyPlanReferenceId,
        int take,
        CancellationToken cancellationToken)
    {
        var summaries = string.IsNullOrWhiteSpace(controlledApplyPlanReferenceId)
            ? await _dryRunStore.ListByWorkflowRunAsync(workflowRunId, take, cancellationToken)
            : await _dryRunStore.ListByControlledApplyPlanAsync(controlledApplyPlanReferenceId, take, cancellationToken);

        return summaries
            .Where(summary => string.Equals(summary.WorkflowRunId, workflowRunId, StringComparison.Ordinal) &&
                              string.Equals(summary.WorkflowStepId, workflowStepId, StringComparison.Ordinal) &&
                              (string.IsNullOrWhiteSpace(controlledApplyPlanReferenceId) ||
                               string.Equals(summary.ControlledApplyPlanReferenceId, controlledApplyPlanReferenceId, StringComparison.Ordinal)))
            .Take(take)
            .ToArray();
    }

    private static List<ApplyPreviewIssue> Validate(ApplyPreviewRequest request)
    {
        var issues = new List<ApplyPreviewIssue>();

        Require(request.WorkflowRunId, ApplyPreviewIssueKind.MissingWorkflowRunId, nameof(request.WorkflowRunId), issues);
        Require(request.WorkflowStepId, ApplyPreviewIssueKind.MissingWorkflowStepId, nameof(request.WorkflowStepId), issues);
        ValidateSafe(request.WorkflowRunId, nameof(request.WorkflowRunId), issues);
        ValidateSafe(request.WorkflowStepId, nameof(request.WorkflowStepId), issues);
        ValidateSafe(request.ControlledApplyPlanReferenceId, nameof(request.ControlledApplyPlanReferenceId), issues);

        return issues;
    }

    private static ApplyPreviewResponse Response(
        ApplyPreviewStatus status,
        string workflowRunId,
        string workflowStepId,
        string controlledApplyPlanReferenceId,
        IReadOnlyList<ApplyDryRunSummary> dryRuns,
        IReadOnlyList<ApplyPreviewIssue> issues)
    {
        var safeRunId = SafeText(workflowRunId);
        var safeStepId = SafeText(workflowStepId);
        var safePlanId = SafeText(controlledApplyPlanReferenceId);
        var hasDryRuns = dryRuns.Count > 0;

        return new ApplyPreviewResponse
        {
            Status = status,
            PreviewReferenceId = BuildPreviewId(safeRunId, safeStepId),
            WorkflowRunId = safeRunId,
            WorkflowStepId = safeStepId,
            ControlledApplyPlanReferenceId = safePlanId,
            SourceApplyApprovalRequirementReferenceId = string.Empty,
            PatchProposalEvidencePackageReferenceId = string.Empty,
            DryRunSummaries = dryRuns,
            Gates =
            [
                Gate("human-review-required", "HumanReviewRequired", "Human review remains required before any source apply."),
                Gate("source-apply-approval-required", "SourceApplyApprovalRequired", "Source apply approval remains unsatisfied."),
                Gate("policy-satisfaction-required", "PolicySatisfactionRequired", "Policy satisfaction remains unsatisfied."),
                Gate("implementation-unavailable", "ImplementationUnavailable", "Apply implementation remains unavailable from this API.")
            ],
            Risks =
            [
                new ApplyPreviewRisk
                {
                    RiskId = "source-change-risk",
                    RiskKind = "SourceChangeRisk",
                    Severity = hasDryRuns ? "Medium" : "High",
                    SafeSummary = "Any future source apply remains a separate high-control action requiring human review."
                }
            ],
            MissingEvidence = hasDryRuns
                ? []
                :
                [
                    new ApplyPreviewMissingEvidence
                    {
                        EvidenceId = "apply-dry-run-receipt-missing",
                        EvidenceKind = "ApplyDryRunReceipt",
                        SafeSummary = "A stored apply dry-run receipt summary is required before preview can be considered complete."
                    }
                ],
            Issues = issues,
            SafeSummaryLines = SummaryLines(hasDryRuns),
            IsPreviewOnly = true
        };
    }

    private static IReadOnlyList<string> SummaryLines(bool hasDryRuns)
    {
        var lines = new List<string>
        {
            "Apply preview was assembled from stored references only.",
            "Preview is not source apply.",
            "Preview is not patch apply.",
            "Preview is not dry-run execution.",
            "Dry-run summaries are receipts only.",
            "Approval was not satisfied.",
            "Policy was not satisfied.",
            "Workflow was not transitioned.",
            "Source apply remains unimplemented.",
            "Patch apply remains unimplemented.",
            "Apply dry-run execution remains unimplemented."
        };

        if (!hasDryRuns)
            lines.Add("Apply dry-run receipt evidence is missing.");

        return lines;
    }

    private static ApplyPreviewGate Gate(string id, string kind, string summary) =>
        new()
        {
            GateId = id,
            GateKind = kind,
            SafeSummary = summary,
            IsSatisfied = false,
            IsApproval = false,
            IsExecutionPermission = false
        };

    private static void Require(string? value, ApplyPreviewIssueKind kind, string field, List<ApplyPreviewIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ApplyPreviewIssue
            {
                Kind = kind,
                Field = field,
                Message = "Required apply preview request value is missing."
            });
        }
    }

    private static void ValidateSafe(string? value, string field, List<ApplyPreviewIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new ApplyPreviewIssue
            {
                Kind = ApplyPreviewIssueKind.UnsafeRequestText,
                Field = field,
                Message = "Apply preview request text contains unsafe material or authority-claiming language."
            });
        }
    }

    private static string BuildPreviewId(string workflowRunId, string workflowStepId) =>
        string.IsNullOrWhiteSpace(workflowRunId) || string.IsNullOrWhiteSpace(workflowStepId)
            ? "apply-preview:invalid"
            : $"apply-preview:{workflowRunId}:{workflowStepId}";

    private static string SafeText(string? value) => Unsafe(value) ? string.Empty : Normalize(value);

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static bool Unsafe(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
