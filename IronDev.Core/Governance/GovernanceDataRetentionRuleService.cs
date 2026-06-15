namespace IronDev.Core.Governance;

public sealed class GovernanceDataRetentionRuleService : IGovernanceDataRetentionRuleService
{
    public static readonly TimeSpan DefaultAuditRetentionWindow = TimeSpan.FromDays(365);
    public const string RedactedUnsafeText = "[redacted governance retention text]";

    private static readonly string[] UnsafeMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "raw command output",
        "rawcommandoutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "private reasoning",
        "privatereasoning",
        "hidden reasoning",
        "payloadjson",
        "source content",
        "sourcecontent",
        "source file contents",
        "patch payload",
        "patchpayload",
        "entirepatch",
        "password",
        "api_key",
        "apikey",
        "secret",
        "credential",
        "bearer "
    ];

    public GovernanceDataRetentionRuleResult Evaluate(GovernanceDataRetentionRuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reasons = new List<GovernanceDataPreservationReason>();
        var recommendations = new List<GovernanceDataCleanupRecommendation>();
        var minimumRetentionPeriod = MinimumRetentionPeriodFor(request.RecordKind);
        var status = GovernanceDataRetentionRuleStatus.RuleEvaluationAvailable;
        var retentionClass = RetentionClassForKnownRecord(request.RecordKind);
        var safeRecordReferenceId = SafeText(request.RecordReferenceId);

        if (string.IsNullOrWhiteSpace(request.RecordReferenceId) || ContainsUnsafeText(request.RecordReferenceId))
        {
            status = GovernanceDataRetentionRuleStatus.InvalidRequest;
            retentionClass = GovernanceDataRetentionClass.PreserveWhileReferenced;
            reasons.Add(Reason(
                "invalid-record-reference",
                GovernanceDataPreservationReasonKind.Unknown,
                "Record reference requires human review before retention classification can be trusted."));
        }

        ApplyHoldReasons(request, reasons);
        ApplyOpenReferenceReasons(request, reasons);
        ApplyRecordKindReasons(request, reasons);
        ApplyHumanReviewReasons(request, reasons);

        if (reasons.Count > 0)
        {
            status = HighestStatus(status, reasons);
            retentionClass = StrongestRetentionClass(retentionClass, reasons);
            recommendations.Add(RecommendationForPreservation(reasons));
        }
        else if (request.CreatedUtc.HasValue && minimumRetentionPeriod.HasValue && HasRetentionWindowElapsed(request.CreatedUtc.Value, minimumRetentionPeriod.Value))
        {
            retentionClass = CleanupReviewClassFor(request.RecordKind);
            recommendations.Add(ReviewRecommendationFor(retentionClass));
        }
        else
        {
            status = GovernanceDataRetentionRuleStatus.PreservationRequired;
            retentionClass = GovernanceDataRetentionClass.PreserveForAuditWindow;
            reasons.Add(Reason(
                "minimum-window:not-elapsed",
                GovernanceDataPreservationReasonKind.MinimumRetentionWindowNotElapsed,
                "Minimum engineering retention window has not elapsed."));
            recommendations.Add(Recommendation(
                "keep:minimum-window",
                GovernanceDataCleanupRecommendationKind.KeepBecauseMinimumWindowNotElapsed,
                "Keep because the minimum engineering retention window has not elapsed.",
                requiresHumanReview: false));
        }

        DateTimeOffset? earliestReviewUtc = minimumRetentionPeriod.HasValue && request.CreatedUtc.HasValue
            ? request.CreatedUtc.Value.Add(minimumRetentionPeriod.Value)
            : null;

        return new GovernanceDataRetentionRuleResult
        {
            Status = status,
            RecordReferenceId = safeRecordReferenceId,
            RecordKind = request.RecordKind,
            RetentionClass = retentionClass,
            MinimumRetentionPeriod = minimumRetentionPeriod,
            EarliestReviewUtc = earliestReviewUtc,
            PreservationReasons = reasons,
            CleanupRecommendations = recommendations,
            SafeSummaryLines = SummaryLines(request, retentionClass, status),
            BoundaryWarnings = GovernanceDataRetentionRuleBoundaries.Warnings,
            IsRuleEvaluationOnly = true,
            IsCleanupExecution = false,
            IsDeletePermission = false,
            IsPurgePermission = false,
            IsArchivePermission = false,
            IsRedactionPermission = false,
            IsLegalHoldOverride = false,
            CanDeleteData = false,
            CanPurgeData = false,
            CanArchiveData = false,
            CanRedactData = false,
            CanRunCleanup = false,
            CanScheduleCleanup = false,
            CanMutateSql = false,
            CanBypassAuditHold = false,
            CanBypassLegalHold = false
        };
    }

    public static bool ContainsUnsafeText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static string SafeText(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return ContainsUnsafeText(normalized) ? RedactedUnsafeText : normalized;
    }

    private static void ApplyHoldReasons(GovernanceDataRetentionRuleRequest request, List<GovernanceDataPreservationReason> reasons)
    {
        if (request.HasLegalHold)
        {
            reasons.Add(Reason(
                "hold:legal",
                GovernanceDataPreservationReasonKind.LegalHoldPresent,
                "Legal hold is present and cannot be bypassed by retention rule evaluation."));
        }

        if (request.HasAuditHold)
        {
            reasons.Add(Reason(
                "hold:audit",
                GovernanceDataPreservationReasonKind.AuditHoldPresent,
                "Audit hold is present and cannot be bypassed by retention rule evaluation."));
        }
    }

    private static void ApplyOpenReferenceReasons(GovernanceDataRetentionRuleRequest request, List<GovernanceDataPreservationReason> reasons)
    {
        if (request.HasOpenWorkflowReference)
            reasons.Add(Reason("reference:workflow", GovernanceDataPreservationReasonKind.OpenWorkflowReference, "Open workflow reference requires preservation."));

        if (request.HasOpenApprovalReference)
            reasons.Add(Reason("reference:approval", GovernanceDataPreservationReasonKind.OpenApprovalReference, "Open approval reference requires preservation."));

        if (request.HasOpenPolicyReference)
            reasons.Add(Reason("reference:policy", GovernanceDataPreservationReasonKind.OpenPolicyReference, "Open policy reference requires preservation."));

        if (request.HasOpenToolGateReference)
            reasons.Add(Reason("reference:tool-gate", GovernanceDataPreservationReasonKind.OpenToolGateReference, "Open tool gate reference requires preservation."));

        if (request.HasOpenMemoryProposalReference)
            reasons.Add(Reason("reference:memory-proposal", GovernanceDataPreservationReasonKind.OpenMemoryProposalReference, "Open memory proposal reference requires preservation."));
    }

    private static void ApplyRecordKindReasons(GovernanceDataRetentionRuleRequest request, List<GovernanceDataPreservationReason> reasons)
    {
        switch (request.RecordKind)
        {
            case GovernanceDataRecordKind.GovernanceEvent:
                reasons.Add(Reason(
                    "record-kind:governance-event",
                    GovernanceDataPreservationReasonKind.GovernanceEventIsAppendOnly,
                    "Governance events are append-only evidence and preserved."));
                break;
            case GovernanceDataRecordKind.ApprovalDecision:
            case GovernanceDataRecordKind.PolicyDecisionEvent:
            case GovernanceDataRecordKind.ToolGateDecision:
                reasons.Add(Reason(
                    $"record-kind:{request.RecordKind}",
                    GovernanceDataPreservationReasonKind.RecordKindRequiresLongTermAudit,
                    "Authority decision records require long-term audit preservation."));
                break;
            case GovernanceDataRecordKind.WorkflowRun:
                reasons.Add(Reason(
                    "record-kind:workflow-run",
                    GovernanceDataPreservationReasonKind.RecordKindRequiresLongTermAudit,
                    "Workflow runs require audit-window preservation."));
                break;
        }
    }

    private static void ApplyHumanReviewReasons(GovernanceDataRetentionRuleRequest request, List<GovernanceDataPreservationReason> reasons)
    {
        if (request.RecordKind == GovernanceDataRecordKind.Unknown)
        {
            reasons.Add(Reason(
                "record-kind:unknown",
                GovernanceDataPreservationReasonKind.UnknownRecordKindRequiresHumanReview,
                "Unknown record kind requires human review before retention classification."));
        }

        if (!request.CreatedUtc.HasValue)
        {
            reasons.Add(Reason(
                "created-utc:missing",
                GovernanceDataPreservationReasonKind.MissingCreatedUtcRequiresHumanReview,
                "Missing created timestamp requires human review before cleanup review eligibility."));
        }

        if (request.ContainsPrivatePayloadRisk)
        {
            reasons.Add(Reason(
                "payload-risk:private",
                GovernanceDataPreservationReasonKind.PrivatePayloadRiskRequiresHumanReview,
                "Private payload risk requires human review and preservation-first handling."));
        }

        if (RequiresCorrelationReference(request.RecordKind) && string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            reasons.Add(Reason(
                "correlation:missing",
                GovernanceDataPreservationReasonKind.MissingCorrelationReferenceRequiresHumanReview,
                "Missing correlation reference requires human review for this record kind."));
        }

        if (request.LastReferencedUtc.HasValue && DateTimeOffset.UtcNow - request.LastReferencedUtc.Value < DefaultAuditRetentionWindow)
        {
            reasons.Add(Reason(
                "last-reference:recent",
                GovernanceDataPreservationReasonKind.RecentReferenceRequiresPreservation,
                "Recent reference requires preservation during the engineering audit window."));
        }
    }

    private static GovernanceDataRetentionRuleStatus HighestStatus(
        GovernanceDataRetentionRuleStatus current,
        IReadOnlyCollection<GovernanceDataPreservationReason> reasons)
    {
        if (current == GovernanceDataRetentionRuleStatus.InvalidRequest)
            return current;

        if (reasons.Any(reason => reason.Kind is GovernanceDataPreservationReasonKind.PrivatePayloadRiskRequiresHumanReview
                or GovernanceDataPreservationReasonKind.MissingCreatedUtcRequiresHumanReview
                or GovernanceDataPreservationReasonKind.UnknownRecordKindRequiresHumanReview
                or GovernanceDataPreservationReasonKind.MissingCorrelationReferenceRequiresHumanReview))
            return GovernanceDataRetentionRuleStatus.HumanReviewRequired;

        return GovernanceDataRetentionRuleStatus.PreservationRequired;
    }

    private static GovernanceDataRetentionClass StrongestRetentionClass(
        GovernanceDataRetentionClass current,
        IReadOnlyCollection<GovernanceDataPreservationReason> reasons)
    {
        if (reasons.Any(reason => reason.Kind is GovernanceDataPreservationReasonKind.GovernanceEventIsAppendOnly
                or GovernanceDataPreservationReasonKind.LegalHoldPresent
                or GovernanceDataPreservationReasonKind.AuditHoldPresent))
            return GovernanceDataRetentionClass.PreserveIndefinitely;

        if (reasons.Any(reason => reason.Kind is GovernanceDataPreservationReasonKind.OpenWorkflowReference
                or GovernanceDataPreservationReasonKind.OpenApprovalReference
                or GovernanceDataPreservationReasonKind.OpenPolicyReference
                or GovernanceDataPreservationReasonKind.OpenToolGateReference
                or GovernanceDataPreservationReasonKind.OpenMemoryProposalReference))
            return GovernanceDataRetentionClass.PreserveWhileReferenced;

        return current == GovernanceDataRetentionClass.Unknown
            ? GovernanceDataRetentionClass.PreserveForAuditWindow
            : current;
    }

    private static GovernanceDataRetentionClass RetentionClassForKnownRecord(GovernanceDataRecordKind recordKind) =>
        recordKind switch
        {
            GovernanceDataRecordKind.GovernanceEvent => GovernanceDataRetentionClass.PreserveIndefinitely,
            GovernanceDataRecordKind.ApprovalDecision => GovernanceDataRetentionClass.PreserveIndefinitely,
            GovernanceDataRecordKind.PolicyDecisionEvent => GovernanceDataRetentionClass.PreserveIndefinitely,
            GovernanceDataRecordKind.ToolGateDecision => GovernanceDataRetentionClass.PreserveIndefinitely,
            GovernanceDataRecordKind.WorkflowCheckpoint => GovernanceDataRetentionClass.PreserveWhileReferenced,
            GovernanceDataRecordKind.MemoryProposal => GovernanceDataRetentionClass.PreserveWhileReferenced,
            GovernanceDataRecordKind.Unknown => GovernanceDataRetentionClass.Unknown,
            _ => GovernanceDataRetentionClass.PreserveForAuditWindow
        };

    private static GovernanceDataRetentionClass CleanupReviewClassFor(GovernanceDataRecordKind recordKind) =>
        recordKind switch
        {
            GovernanceDataRecordKind.AgentHealthReport => GovernanceDataRetentionClass.EligibleForHumanCleanupReview,
            GovernanceDataRecordKind.GovernanceTraceReport => GovernanceDataRetentionClass.EligibleForHumanCleanupReview,
            GovernanceDataRecordKind.FailedWorkflowDiagnosisReport => GovernanceDataRetentionClass.EligibleForHumanCleanupReview,
            GovernanceDataRecordKind.ApprovalGateDogfoodCorrelationReport => GovernanceDataRetentionClass.EligibleForHumanCleanupReview,
            GovernanceDataRecordKind.BackendOperationalHealthReport => GovernanceDataRetentionClass.EligibleForHumanCleanupReview,
            GovernanceDataRecordKind.GovernanceEventReadModel => GovernanceDataRetentionClass.EligibleForHumanCleanupReview,
            _ => GovernanceDataRetentionClass.PreserveForAuditWindow
        };

    private static TimeSpan? MinimumRetentionPeriodFor(GovernanceDataRecordKind recordKind) =>
        recordKind is GovernanceDataRecordKind.GovernanceEvent
            or GovernanceDataRecordKind.ApprovalDecision
            or GovernanceDataRecordKind.PolicyDecisionEvent
            or GovernanceDataRecordKind.ToolGateDecision
                ? null
                : DefaultAuditRetentionWindow;

    private static bool HasRetentionWindowElapsed(DateTimeOffset createdUtc, TimeSpan minimumRetentionPeriod) =>
        DateTimeOffset.UtcNow - createdUtc >= minimumRetentionPeriod;

    private static bool RequiresCorrelationReference(GovernanceDataRecordKind recordKind) =>
        recordKind is GovernanceDataRecordKind.GovernanceTraceReport
            or GovernanceDataRecordKind.FailedWorkflowDiagnosisReport
            or GovernanceDataRecordKind.ApprovalGateDogfoodCorrelationReport;

    private static GovernanceDataPreservationReason Reason(
        string reasonId,
        GovernanceDataPreservationReasonKind kind,
        string safeSummary) =>
        new()
        {
            ReasonId = reasonId,
            Kind = kind,
            SafeSummary = SafeText(safeSummary)
        };

    private static GovernanceDataCleanupRecommendation RecommendationForPreservation(
        IReadOnlyCollection<GovernanceDataPreservationReason> reasons)
    {
        if (reasons.Any(reason => reason.Kind == GovernanceDataPreservationReasonKind.LegalHoldPresent))
            return Recommendation("keep:legal-hold", GovernanceDataCleanupRecommendationKind.KeepBecauseLegalHold, "Keep because legal hold is present.", requiresHumanReview: true);

        if (reasons.Any(reason => reason.Kind == GovernanceDataPreservationReasonKind.AuditHoldPresent))
            return Recommendation("keep:audit-hold", GovernanceDataCleanupRecommendationKind.KeepBecauseAuditHold, "Keep because audit hold is present.", requiresHumanReview: true);

        if (reasons.Any(reason => reason.Kind is GovernanceDataPreservationReasonKind.OpenWorkflowReference
                or GovernanceDataPreservationReasonKind.OpenApprovalReference
                or GovernanceDataPreservationReasonKind.OpenPolicyReference
                or GovernanceDataPreservationReasonKind.OpenToolGateReference
                or GovernanceDataPreservationReasonKind.OpenMemoryProposalReference))
            return Recommendation("keep:referenced", GovernanceDataCleanupRecommendationKind.KeepBecauseReferenced, "Keep because open governance references are present.", requiresHumanReview: true);

        return Recommendation("preserve:governance", GovernanceDataCleanupRecommendationKind.Preserve, "Preserve governance evidence for future governed review.", requiresHumanReview: true);
    }

    private static GovernanceDataCleanupRecommendation ReviewRecommendationFor(GovernanceDataRetentionClass retentionClass) =>
        retentionClass switch
        {
            GovernanceDataRetentionClass.EligibleForFutureArchiveReview => Recommendation(
                "review:future-archive",
                GovernanceDataCleanupRecommendationKind.ReviewForFutureArchive,
                "Record may be reviewed by a future governed archive process.",
                requiresHumanReview: true),
            GovernanceDataRetentionClass.EligibleForFutureRedactionReview => Recommendation(
                "review:future-redaction",
                GovernanceDataCleanupRecommendationKind.ReviewForFutureRedaction,
                "Record may be reviewed by a future governed redaction process.",
                requiresHumanReview: true),
            GovernanceDataRetentionClass.EligibleForHumanCleanupReview => Recommendation(
                "review:future-cleanup",
                GovernanceDataCleanupRecommendationKind.ReviewForFutureCleanup,
                "Record may be reviewed by a future governed cleanup process.",
                requiresHumanReview: true),
            _ => Recommendation(
                "preserve:default",
                GovernanceDataCleanupRecommendationKind.Preserve,
                "Preserve governance data unless a later governed executor exists.",
                requiresHumanReview: true)
        };

    private static GovernanceDataCleanupRecommendation Recommendation(
        string recommendationId,
        GovernanceDataCleanupRecommendationKind kind,
        string safeSummary,
        bool requiresHumanReview) =>
        new()
        {
            RecommendationId = recommendationId,
            Kind = kind,
            SafeSummary = SafeText(safeSummary),
            IsReviewOnly = true,
            IsDeleteCommand = false,
            IsPurgeCommand = false,
            IsArchiveCommand = false,
            IsRedactionCommand = false,
            RequiresHumanReview = requiresHumanReview
        };

    private static IReadOnlyList<string> SummaryLines(
        GovernanceDataRetentionRuleRequest request,
        GovernanceDataRetentionClass retentionClass,
        GovernanceDataRetentionRuleStatus status) =>
        [
            $"Governance data retention rule evaluated {SafeText(request.RecordReferenceId)} as {retentionClass}.",
            $"Retention status is {status}; this is review material only.",
            "No cleanup, deletion, purge, archive, redaction, SQL mutation, or scheduling authority is granted."
        ];
}
