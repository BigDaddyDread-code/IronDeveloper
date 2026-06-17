using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class FailedContinuationRecoveryCampaignRunner : IFailedContinuationRecoveryCampaignRunner
{
    private static readonly string[] PrivateOrRawMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "entire patch",
        "entirepatch",
        "patch payload",
        "patchpayload",
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "release approved",
        "approved for release",
        "deployment approved",
        "merge approved",
        "safe to continue",
        "safe to retry",
        "safe to deploy",
        "safe to merge",
        "can continue workflow",
        "can retry continuation",
        "can deploy",
        "can merge",
        "green to ship",
        "recovery complete",
        "workflow continuation recovered",
        "workflow continued by campaign",
        "workflow transition created by campaign",
        "release executed",
        "source applied by campaign",
        "rollback executed by campaign",
        "git " + "committed",
        "git " + "pushed",
        "tag created",
        "pull request created",
        "memory promoted",
        "retrieval activated",
        "agent dispatched",
        "tool executed",
        "model called"
    ];

    private static readonly string[] SafeAuthorityPrefixes =
    [
        "not",
        "no",
        "does not",
        "do not",
        "must not",
        "never",
        "without"
    ];

    public FailedContinuationRecoveryCampaignResult Run(FailedContinuationRecoveryCampaignRequest? request)
    {
        var findings = new List<FailedContinuationRecoveryFinding>();
        var reject = false;
        var missing = false;
        var failed = false;
        var stale = false;

        ValidateRequestShape(request, findings, ref reject);

        var continuationFailureConfirmed = false;
        var workflowWasMutatedDuringFailure = request?.WorkflowContinuationFailure?.WorkflowMutated == true;
        var transitionRecoveryEvidencePresent = request?.WorkflowTransitionRecovery is not null;
        var workflowStateConfirmedUnchanged = false;
        var retryRequiresHumanReview = false;
        var staleAuthorityDetected = false;
        var followUpReadinessEvidencePresent = false;

        if (request is not null)
        {
            continuationFailureConfirmed = ValidateContinuationFailure(request.WorkflowContinuationFailure, findings, ref reject, out workflowWasMutatedDuringFailure);
            ScanRequestTexts(request, findings);
        }

        if (!reject && continuationFailureConfirmed)
        {
            if (request!.WorkflowTransitionRecovery is null)
            {
                missing = true;
                Add(findings, "WorkflowTransitionRecoveryEvidenceMissing", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(request.WorkflowTransitionRecovery), "Workflow transition recovery evidence is required before recovery evidence can be satisfied.");
            }
            else
            {
                var transitionRecoveryValid = ValidateTransitionRecovery(request.WorkflowTransitionRecovery, request.WorkflowContinuationFailure, request, findings, ref failed);
                workflowStateConfirmedUnchanged = transitionRecoveryValid && request.WorkflowTransitionRecovery.WorkflowStateConfirmedUnchanged;
                retryRequiresHumanReview = transitionRecoveryValid && request.WorkflowTransitionRecovery.RetryRequiresHumanReview;
            }

            ValidateStaleAuthorityEvidence(request.StaleAuthorityDetection, findings, ref stale, out staleAuthorityDetected);
            followUpReadinessEvidencePresent = ValidateFollowUpReadinessEvidence(request.FollowUpReleaseReadinessDecision, findings, ref failed);
        }

        if (findings.Any(finding => finding.Code is "PrivateRawMaterialRejected" or "AuthorityClaimRejected"))
        {
            reject = true;
        }

        var status = reject
            ? FailedContinuationRecoveryCampaignStatuses.Rejected
            : stale
                ? FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceStale
                : failed
                    ? FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed
                    : missing
                        ? FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceMissing
                        : FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceSatisfied;

        var recoveryEvidenceSatisfied = string.Equals(status, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, StringComparison.Ordinal);

        return new FailedContinuationRecoveryCampaignResult
        {
            FailedContinuationRecoveryCampaignRequestId = request?.FailedContinuationRecoveryCampaignRequestId ?? Guid.Empty,
            ProjectId = request?.ProjectId ?? Guid.Empty,
            CampaignName = SafeOutputText(request?.CampaignName),
            Succeeded = recoveryEvidenceSatisfied,
            Status = status,
            ContinuationFailureConfirmed = continuationFailureConfirmed,
            WorkflowWasMutatedDuringFailure = workflowWasMutatedDuringFailure,
            TransitionRecoveryEvidencePresent = transitionRecoveryEvidencePresent,
            WorkflowStateConfirmedUnchanged = workflowStateConfirmedUnchanged,
            RetryRequiresHumanReview = retryRequiresHumanReview,
            StaleAuthorityDetected = staleAuthorityDetected,
            FollowUpReadinessEvidencePresent = followUpReadinessEvidencePresent,
            Findings = findings,
            EvidenceReferences = CollectEvidenceReferences(request),
            BoundaryMaxims = SafeList(request?.BoundaryMaxims),
            RecoveryEvidenceSatisfied = recoveryEvidenceSatisfied,
            WorkflowContinuationRetried = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            WorkflowTransitionRecordCreated = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            RollbackAuditExecuted = false,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            GitOperationExecuted = false,
            AuthorityRefreshed = false,
            EvidenceReissued = false,
            HumanReviewRequired = true,
            CompletedAtUtc = request?.RequestedAtUtc == default ? DateTimeOffset.UtcNow : request!.RequestedAtUtc,
            Boundary = FailedContinuationRecoveryCampaignBoundaryText.Boundary
        };
    }

    private static void ValidateRequestShape(
        FailedContinuationRecoveryCampaignRequest? request,
        List<FailedContinuationRecoveryFinding> findings,
        ref bool reject)
    {
        if (request is null)
        {
            reject = true;
            Add(findings, "RequestRequired", FailedContinuationRecoveryFindingSeverities.Blocking, "request", "Failed continuation recovery campaign request is required.");
            return;
        }

        RequireGuid(request.FailedContinuationRecoveryCampaignRequestId, nameof(request.FailedContinuationRecoveryCampaignRequestId), findings, ref reject);
        RequireGuid(request.ProjectId, nameof(request.ProjectId), findings, ref reject);
        RequireText(request.CampaignName, nameof(request.CampaignName), findings, ref reject);
        RequireText(request.RequestedBy, nameof(request.RequestedBy), findings, ref reject);
        RequireText(request.WorkflowRunId, nameof(request.WorkflowRunId), findings, ref reject);
        RequireText(request.WorkflowStepId, nameof(request.WorkflowStepId), findings, ref reject);
        RequireText(request.SubjectKind, nameof(request.SubjectKind), findings, ref reject);
        RequireText(request.SubjectId, nameof(request.SubjectId), findings, ref reject);
        RequireHash(request.SubjectHash, nameof(request.SubjectHash), "SubjectHashInvalid", findings, ref reject);
        RequireText(request.Boundary, nameof(request.Boundary), findings, ref reject);
        RequireList(request.EvidenceReferences, nameof(request.EvidenceReferences), findings, ref reject);
        RequireList(request.BoundaryMaxims, nameof(request.BoundaryMaxims), findings, ref reject);

        if (request.RequestedAtUtc == default)
        {
            reject = true;
            Add(findings, "RequestedAtRequired", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(request.RequestedAtUtc), "Requested timestamp is required.");
        }
    }

    private static bool ValidateContinuationFailure(
        WorkflowContinuationFailureEvidence? failure,
        List<FailedContinuationRecoveryFinding> findings,
        ref bool reject,
        out bool workflowWasMutatedDuringFailure)
    {
        workflowWasMutatedDuringFailure = failure?.WorkflowMutated == true;

        if (failure is null)
        {
            reject = true;
            Add(findings, "WorkflowContinuationFailureEvidenceRequired", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(FailedContinuationRecoveryCampaignRequest.WorkflowContinuationFailure), "Workflow continuation failure evidence is required.");
            return false;
        }

        var valid = true;
        RequireGuid(failure.GovernedWorkflowContinuationRequestId, nameof(failure.GovernedWorkflowContinuationRequestId), findings, ref reject, "WorkflowContinuationFailureEvidenceRequired");
        RequireHash(failure.GovernedWorkflowContinuationRequestHash, nameof(failure.GovernedWorkflowContinuationRequestHash), "WorkflowContinuationFailureHashInvalid", findings, ref reject);
        RequireHash(failure.ExpectedWorkflowStateHash, nameof(failure.ExpectedWorkflowStateHash), "WorkflowContinuationFailureHashInvalid", findings, ref reject);
        RequireHash(failure.ObservedWorkflowStateHash, nameof(failure.ObservedWorkflowStateHash), "WorkflowContinuationFailureHashInvalid", findings, ref reject);
        RequireText(failure.FromWorkflowStepId, nameof(failure.FromWorkflowStepId), findings, ref reject);
        RequireText(failure.IntendedToWorkflowStepId, nameof(failure.IntendedToWorkflowStepId), findings, ref reject);
        RequireList(failure.EvidenceReferences, $"{nameof(FailedContinuationRecoveryCampaignRequest.WorkflowContinuationFailure)}.{nameof(failure.EvidenceReferences)}", findings, ref reject);

        if (failure.WorkflowTransitionRecordId.HasValue && string.IsNullOrWhiteSpace(failure.WorkflowTransitionRecordHash))
        {
            reject = true;
            Add(findings, "WorkflowContinuationFailureHashInvalid", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(failure.WorkflowTransitionRecordHash), "Workflow transition record hash is required when transition record id is supplied.");
        }

        if (!string.IsNullOrWhiteSpace(failure.WorkflowTransitionRecordHash) && !IsSupportedHash(failure.WorkflowTransitionRecordHash))
        {
            reject = true;
            Add(findings, "WorkflowContinuationFailureHashInvalid", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(failure.WorkflowTransitionRecordHash), "Workflow transition record hash must be SHA-256.");
        }

        if (!failure.ContinuationAttempted)
        {
            valid = false;
            reject = true;
            Add(findings, "WorkflowContinuationNotAttempted", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(failure.ContinuationAttempted), "Workflow continuation must have been attempted for a failed-continuation recovery campaign.");
        }

        if (failure.ContinuationSucceeded)
        {
            valid = false;
            reject = true;
            Add(findings, "WorkflowContinuationSucceededNotRecovery", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(failure.ContinuationSucceeded), "Successful workflow continuation is not failed-continuation recovery evidence.");
        }

        if (!failure.ContinuationFailed)
        {
            valid = false;
            reject = true;
            Add(findings, "WorkflowContinuationFailureNotProven", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(failure.ContinuationFailed), "Workflow continuation failure must be proven.");
        }

        if (failure.WorkflowMutated)
        {
            valid = false;
            reject = true;
            Add(findings, "WorkflowMutatedDuringFailure", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(failure.WorkflowMutated), "Workflow mutation during continuation failure needs a separate audit or reversal lane.");
        }

        if (IsNullOrEmpty(failure.FailedTransitionReasons))
        {
            valid = false;
            reject = true;
            Add(findings, "WorkflowContinuationFailureReasonsMissing", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(failure.FailedTransitionReasons), "Failed transition reasons are required.");
        }

        if (failure.AttemptedAtUtc == default)
        {
            valid = false;
            reject = true;
            Add(findings, "WorkflowContinuationAttemptedAtRequired", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(failure.AttemptedAtUtc), "Workflow continuation attempted timestamp is required.");
        }

        return valid && !reject;
    }

    private static bool ValidateTransitionRecovery(
        WorkflowTransitionRecoveryEvidence recovery,
        WorkflowContinuationFailureEvidence failure,
        FailedContinuationRecoveryCampaignRequest request,
        List<FailedContinuationRecoveryFinding> findings,
        ref bool failed)
    {
        var localFailed = false;
        RequireGuid(recovery.RecoveryEvidenceId, nameof(recovery.RecoveryEvidenceId), findings, ref failed, "WorkflowTransitionRecoveryEvidenceInvalid");
        RequireHash(recovery.RecoveryEvidenceHash, nameof(recovery.RecoveryEvidenceHash), "WorkflowTransitionRecoveryEvidenceInvalid", findings, ref failed);
        RequireText(recovery.ConfirmedWorkflowRunId, nameof(recovery.ConfirmedWorkflowRunId), findings, ref failed);
        RequireText(recovery.ConfirmedWorkflowStepId, nameof(recovery.ConfirmedWorkflowStepId), findings, ref failed);
        RequireHash(recovery.ConfirmedWorkflowStateHash, nameof(recovery.ConfirmedWorkflowStateHash), "WorkflowTransitionRecoveryEvidenceInvalid", findings, ref failed);
        RequireList(recovery.EvidenceReferences, $"{nameof(FailedContinuationRecoveryCampaignRequest.WorkflowTransitionRecovery)}.{nameof(recovery.EvidenceReferences)}", findings, ref failed);

        if (!recovery.FailureReviewed)
        {
            localFailed = true;
            failed = true;
            Add(findings, "ContinuationFailureNotReviewed", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(recovery.FailureReviewed), "Workflow continuation failure must be reviewed before recovery evidence can be satisfied.");
        }

        if (!recovery.WorkflowStateConfirmedUnchanged)
        {
            localFailed = true;
            failed = true;
            Add(findings, "WorkflowStateNotConfirmedUnchanged", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(recovery.WorkflowStateConfirmedUnchanged), "Recovery evidence must confirm workflow state stayed unchanged.");
        }

        if (!recovery.RetryRequiresHumanReview)
        {
            localFailed = true;
            failed = true;
            Add(findings, "RetryHumanReviewNotRequired", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(recovery.RetryRequiresHumanReview), "Recovery evidence must keep human review required before retry.");
        }

        if (!string.Equals(recovery.ConfirmedWorkflowRunId, request.WorkflowRunId, StringComparison.Ordinal))
        {
            localFailed = true;
            failed = true;
            Add(findings, "RecoveryWorkflowRunMismatch", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(recovery.ConfirmedWorkflowRunId), "Recovery evidence workflow run does not match the failed continuation request.");
        }

        if (!string.Equals(recovery.ConfirmedWorkflowStepId, request.WorkflowStepId, StringComparison.Ordinal))
        {
            localFailed = true;
            failed = true;
            Add(findings, "RecoveryWorkflowStepMismatch", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(recovery.ConfirmedWorkflowStepId), "Recovery evidence workflow step does not match the failed continuation request.");
        }

        if (!HashesEqual(recovery.ConfirmedWorkflowStateHash, failure.ObservedWorkflowStateHash))
        {
            localFailed = true;
            failed = true;
            Add(findings, "RecoveryWorkflowStateHashMismatch", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(recovery.ConfirmedWorkflowStateHash), "Recovery evidence state hash does not match observed failed-continuation state hash.");
        }

        if (IsNullOrEmpty(recovery.Findings))
        {
            localFailed = true;
            failed = true;
            Add(findings, "RecoveryFindingsMissing", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(recovery.Findings), "Recovery evidence findings are required.");
        }

        if (recovery.ReviewedAtUtc == default)
        {
            localFailed = true;
            failed = true;
            Add(findings, "RecoveryReviewedAtRequired", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(recovery.ReviewedAtUtc), "Recovery evidence reviewed timestamp is required.");
        }

        return !localFailed &&
            recovery.FailureReviewed &&
            recovery.WorkflowStateConfirmedUnchanged &&
            recovery.RetryRequiresHumanReview &&
            string.Equals(recovery.ConfirmedWorkflowRunId, request.WorkflowRunId, StringComparison.Ordinal) &&
            string.Equals(recovery.ConfirmedWorkflowStepId, request.WorkflowStepId, StringComparison.Ordinal) &&
            HashesEqual(recovery.ConfirmedWorkflowStateHash, failure.ObservedWorkflowStateHash);
    }

    private static void ValidateStaleAuthorityEvidence(
        StaleAuthorityDetectionResult? staleAuthorityDetection,
        List<FailedContinuationRecoveryFinding> findings,
        ref bool stale,
        out bool staleAuthorityDetected)
    {
        staleAuthorityDetected = false;

        if (staleAuthorityDetection is null)
        {
            Add(findings, "StaleAuthorityEvidenceNotSupplied", FailedContinuationRecoveryFindingSeverities.Warning, nameof(FailedContinuationRecoveryCampaignRequest.StaleAuthorityDetection), "Stale authority evidence was not supplied; PR227 records this as a warning only.");
            return;
        }

        if (staleAuthorityDetection.HasStaleAuthority && !staleAuthorityDetection.IsCurrent)
        {
            stale = true;
            staleAuthorityDetected = true;
            Add(findings, "StaleAuthorityBlocksContinuationRecovery", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(FailedContinuationRecoveryCampaignRequest.StaleAuthorityDetection), "Supplied stale-authority evidence blocks failed-continuation recovery evidence satisfaction.");
        }
    }

    private static bool ValidateFollowUpReadinessEvidence(
        ReleaseReadinessDecisionRecord? decision,
        List<FailedContinuationRecoveryFinding> findings,
        ref bool failed)
    {
        if (decision is null)
        {
            return false;
        }

        if (decision.ReleaseApproved || decision.DeploymentApproved || decision.MergeApproved)
        {
            failed = true;
            Add(findings, "FollowUpReadinessClaimsReleaseApproval", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(decision.ReleaseApproved), "Follow-up readiness evidence must not claim release, deployment, or merge approval.");
        }

        if (decision.SourceApplyExecutedByDecision ||
            decision.RollbackExecutedByDecision ||
            decision.WorkflowMutatedByDecision ||
            decision.GitOperationExecutedByDecision ||
            decision.ReleaseExecutedByDecision)
        {
            failed = true;
            Add(findings, "FollowUpReadinessClaimsExecution", FailedContinuationRecoveryFindingSeverities.Blocking, nameof(decision.ReleaseExecutedByDecision), "Follow-up readiness evidence must not claim execution or workflow mutation.");
        }

        return string.Equals(decision.DecisionStatus, ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, StringComparison.Ordinal) &&
            decision.ReleaseReadinessEvidenceSatisfied &&
            !decision.ReleaseApproved &&
            decision.HumanReviewRequiredForReleaseApproval;
    }

    private static void ScanRequestTexts(FailedContinuationRecoveryCampaignRequest request, List<FailedContinuationRecoveryFinding> findings)
    {
        ScanText(request.CampaignName, nameof(request.CampaignName), findings);
        ScanText(request.RequestedBy, nameof(request.RequestedBy), findings);
        ScanText(request.WorkflowRunId, nameof(request.WorkflowRunId), findings);
        ScanText(request.WorkflowStepId, nameof(request.WorkflowStepId), findings);
        ScanText(request.SubjectKind, nameof(request.SubjectKind), findings);
        ScanText(request.SubjectId, nameof(request.SubjectId), findings);
        ScanText(request.SubjectHash, nameof(request.SubjectHash), findings);
        ScanText(request.Boundary, nameof(request.Boundary), findings);
        ScanTexts(request.EvidenceReferences, nameof(request.EvidenceReferences), findings);
        ScanTexts(request.BoundaryMaxims, nameof(request.BoundaryMaxims), findings);

        if (request.WorkflowContinuationFailure is not null)
        {
            ScanText(request.WorkflowContinuationFailure.GovernedWorkflowContinuationRequestHash, nameof(request.WorkflowContinuationFailure.GovernedWorkflowContinuationRequestHash), findings);
            ScanText(request.WorkflowContinuationFailure.WorkflowTransitionRecordHash, nameof(request.WorkflowContinuationFailure.WorkflowTransitionRecordHash), findings);
            ScanText(request.WorkflowContinuationFailure.FromWorkflowStepId, nameof(request.WorkflowContinuationFailure.FromWorkflowStepId), findings);
            ScanText(request.WorkflowContinuationFailure.IntendedToWorkflowStepId, nameof(request.WorkflowContinuationFailure.IntendedToWorkflowStepId), findings);
            ScanText(request.WorkflowContinuationFailure.ExpectedWorkflowStateHash, nameof(request.WorkflowContinuationFailure.ExpectedWorkflowStateHash), findings);
            ScanText(request.WorkflowContinuationFailure.ObservedWorkflowStateHash, nameof(request.WorkflowContinuationFailure.ObservedWorkflowStateHash), findings);
            ScanTexts(request.WorkflowContinuationFailure.FailedTransitionReasons, nameof(request.WorkflowContinuationFailure.FailedTransitionReasons), findings);
            ScanTexts(request.WorkflowContinuationFailure.EvidenceReferences, nameof(request.WorkflowContinuationFailure.EvidenceReferences), findings);
        }

        if (request.WorkflowTransitionRecovery is not null)
        {
            ScanText(request.WorkflowTransitionRecovery.RecoveryEvidenceHash, nameof(request.WorkflowTransitionRecovery.RecoveryEvidenceHash), findings);
            ScanText(request.WorkflowTransitionRecovery.ConfirmedWorkflowRunId, nameof(request.WorkflowTransitionRecovery.ConfirmedWorkflowRunId), findings);
            ScanText(request.WorkflowTransitionRecovery.ConfirmedWorkflowStepId, nameof(request.WorkflowTransitionRecovery.ConfirmedWorkflowStepId), findings);
            ScanText(request.WorkflowTransitionRecovery.ConfirmedWorkflowStateHash, nameof(request.WorkflowTransitionRecovery.ConfirmedWorkflowStateHash), findings);
            ScanTexts(request.WorkflowTransitionRecovery.Findings, nameof(request.WorkflowTransitionRecovery.Findings), findings);
            ScanTexts(request.WorkflowTransitionRecovery.EvidenceReferences, nameof(request.WorkflowTransitionRecovery.EvidenceReferences), findings);
        }

        if (request.StaleAuthorityDetection is not null)
        {
            ScanText(request.StaleAuthorityDetection.SubjectKind, nameof(request.StaleAuthorityDetection.SubjectKind), findings);
            ScanText(request.StaleAuthorityDetection.SubjectId, nameof(request.StaleAuthorityDetection.SubjectId), findings);
            ScanText(request.StaleAuthorityDetection.CurrentSubjectHash, nameof(request.StaleAuthorityDetection.CurrentSubjectHash), findings);
            ScanTexts(request.StaleAuthorityDetection.EvidenceReferences, nameof(request.StaleAuthorityDetection.EvidenceReferences), findings);
            ScanTexts(request.StaleAuthorityDetection.BoundaryMaxims, nameof(request.StaleAuthorityDetection.BoundaryMaxims), findings);
        }

        if (request.FollowUpReleaseReadinessDecision is not null)
        {
            var decision = request.FollowUpReleaseReadinessDecision;
            ScanText(decision.DecisionStatus, nameof(decision.DecisionStatus), findings);
            ScanText(decision.SubjectKind, nameof(decision.SubjectKind), findings);
            ScanText(decision.SubjectId, nameof(decision.SubjectId), findings);
            ScanText(decision.SubjectHash, nameof(decision.SubjectHash), findings);
            ScanTexts(decision.EvidenceReferences, nameof(decision.EvidenceReferences), findings);
            ScanTexts(decision.BoundaryMaxims, nameof(decision.BoundaryMaxims), findings);
            if (decision.Reasons is not null)
            {
                foreach (var reason in decision.Reasons)
                {
                    ScanText(reason.Code, nameof(reason.Code), findings);
                    ScanText(reason.Field, nameof(reason.Field), findings);
                    ScanText(reason.Message, nameof(reason.Message), findings);
                }
            }
        }
    }

    private static void ScanTexts(IEnumerable<string>? values, string field, List<FailedContinuationRecoveryFinding> findings)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            ScanText(value, field, findings);
        }
    }

    private static void ScanText(string? value, string field, List<FailedContinuationRecoveryFinding> findings)
    {
        if (ContainsPrivateOrRaw(value))
        {
            Add(findings, "PrivateRawMaterialRejected", FailedContinuationRecoveryFindingSeverities.Blocking, field, "Private, raw, prompt, scratchpad, patch, or secret-like material is not allowed.");
        }

        if (ContainsAuthorityClaim(value))
        {
            Add(findings, "AuthorityClaimRejected", FailedContinuationRecoveryFindingSeverities.Blocking, field, "Authority claims are not allowed.");
        }
    }

    private static void RequireGuid(
        Guid value,
        string field,
        List<FailedContinuationRecoveryFinding> findings,
        ref bool reject,
        string code = "RequiredFieldMissing")
    {
        if (value == Guid.Empty)
        {
            reject = true;
            Add(findings, code, FailedContinuationRecoveryFindingSeverities.Blocking, field, $"{field} is required.");
        }
    }

    private static void RequireText(
        string? value,
        string field,
        List<FailedContinuationRecoveryFinding> findings,
        ref bool reject)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reject = true;
            Add(findings, "RequiredFieldMissing", FailedContinuationRecoveryFindingSeverities.Blocking, field, $"{field} is required.");
        }
    }

    private static void RequireHash(
        string? value,
        string field,
        string code,
        List<FailedContinuationRecoveryFinding> findings,
        ref bool reject)
    {
        if (!IsSupportedHash(value))
        {
            reject = true;
            Add(findings, code, FailedContinuationRecoveryFindingSeverities.Blocking, field, $"{field} must be SHA-256.");
        }
    }

    private static void RequireList(
        IReadOnlyList<string>? values,
        string field,
        List<FailedContinuationRecoveryFinding> findings,
        ref bool reject)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            reject = true;
            Add(findings, "RequiredFieldMissing", FailedContinuationRecoveryFindingSeverities.Blocking, field, $"{field} is required.");
        }
    }

    private static IReadOnlyList<string> CollectEvidenceReferences(FailedContinuationRecoveryCampaignRequest? request)
    {
        if (request is null)
        {
            return [];
        }

        var references = new List<string>();
        AddRange(references, request.EvidenceReferences);
        AddRange(references, request.WorkflowContinuationFailure?.EvidenceReferences);
        AddRange(references, request.WorkflowTransitionRecovery?.EvidenceReferences);
        AddRange(references, request.StaleAuthorityDetection?.EvidenceReferences);
        AddRange(references, request.FollowUpReleaseReadinessDecision?.EvidenceReferences);
        return SafeList(references);
    }

    private static void AddRange(List<string> target, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        target.AddRange(values);
    }

    private static IReadOnlyList<string> SafeList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        return values.Select(SafeOutputText).Where(value => value.Length > 0).ToArray();
    }

    private static string SafeOutputText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return ContainsPrivateOrRaw(value) || ContainsAuthorityClaim(value) ? "[redacted]" : value.Trim();
    }

    private static bool ContainsPrivateOrRaw(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        PrivateOrRawMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAuthorityClaim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeForMarkerSearch(value);
        foreach (var marker in AuthorityMarkers.Select(NormalizeForMarkerSearch))
        {
            var index = normalized.IndexOf(marker, StringComparison.Ordinal);
            while (index >= 0)
            {
                var prefix = normalized[..index].TrimEnd();
                if (!SafeAuthorityPrefixes.Any(safePrefix => prefix.EndsWith(safePrefix, StringComparison.Ordinal)))
                {
                    return true;
                }

                index = normalized.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static bool HashesEqual(string? left, string? right) =>
        string.Equals(NormalizeHash(left), NormalizeHash(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedHash(string? value)
    {
        var normalized = NormalizeHash(value);
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit);
    }

    private static string NormalizeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? trimmed[7..] : trimmed;
    }

    private static string NormalizeForMarkerSearch(string value) =>
        value.Trim().ToLowerInvariant().Replace("_", " ", StringComparison.Ordinal);

    private static bool IsNullOrEmpty(IReadOnlyList<string>? values) =>
        values is null || values.Count == 0;

    private static void Add(
        List<FailedContinuationRecoveryFinding> findings,
        string code,
        string severity,
        string field,
        string message) =>
        findings.Add(new FailedContinuationRecoveryFinding
        {
            Code = code,
            Severity = severity,
            Field = field,
            Message = message
        });
}
