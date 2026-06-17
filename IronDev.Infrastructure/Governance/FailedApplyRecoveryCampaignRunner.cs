using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class FailedApplyRecoveryCampaignRunner : IFailedApplyRecoveryCampaignRunner
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
        "safe to retry",
        "safe to deploy",
        "safe to merge",
        "can retry apply",
        "can deploy",
        "can merge",
        "green to ship",
        "recovery complete",
        "rollback successful therefore continue",
        "release executed",
        "source applied by campaign",
        "rollback executed by campaign",
        "workflow continued by campaign",
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
        "not ",
        "no ",
        "does not ",
        "do not ",
        "must not ",
        "never ",
        "without "
    ];

    public FailedApplyRecoveryCampaignResult Run(FailedApplyRecoveryCampaignRequest? request)
    {
        var findings = new List<FailedApplyRecoveryFinding>();
        var reject = false;
        var missing = false;
        var failed = false;
        var stale = false;

        ValidateRequestShape(request, findings, ref reject);

        var sourceApplyFailureConfirmed = false;
        var rollbackEvidencePresent = request?.RollbackRecovery is not null;
        var rollbackSucceeded = false;
        var rollbackAuditPresent = request?.RollbackAudit is not null;
        var rollbackAuditConsistent = false;
        var staleAuthorityDetected = false;
        var followUpReadinessEvidencePresent = false;

        if (request is not null)
        {
            sourceApplyFailureConfirmed = ValidateSourceApplyFailure(request.SourceApplyFailure, findings, ref reject);
            ScanRequestTexts(request, findings);
        }

        if (!reject && sourceApplyFailureConfirmed)
        {
            if (request!.RollbackRecovery is null)
            {
                missing = true;
                Add(findings, "RollbackRecoveryEvidenceMissing", FailedApplyRecoveryFindingSeverities.Blocking, nameof(request.RollbackRecovery), "Rollback recovery evidence is required before recovery evidence can be satisfied.");
            }
            else
            {
                rollbackSucceeded = ValidateRollbackRecovery(request.RollbackRecovery, findings, ref failed);
            }

            if (rollbackSucceeded)
            {
                if (request.RollbackAudit is null)
                {
                    missing = true;
                    Add(findings, "RollbackAuditEvidenceMissing", FailedApplyRecoveryFindingSeverities.Blocking, nameof(request.RollbackAudit), "Rollback audit evidence is required after rollback recovery evidence.");
                }
                else
                {
                    rollbackAuditConsistent = ValidateRollbackAudit(request.RollbackAudit, request.RollbackRecovery!, findings, ref failed);
                }
            }

            ValidateStaleAuthorityEvidence(request.StaleAuthorityDetection, findings, ref stale, out staleAuthorityDetected);
            followUpReadinessEvidencePresent = ValidateFollowUpReadinessEvidence(request.FollowUpReleaseReadinessDecision, findings, ref failed);
        }

        if (findings.Any(finding => finding.Code is "PrivateRawMaterialRejected" or "AuthorityClaimRejected"))
        {
            reject = true;
        }

        var status = reject
            ? FailedApplyRecoveryCampaignStatuses.Rejected
            : stale
                ? FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceStale
                : failed
                    ? FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed
                    : missing
                        ? FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceMissing
                        : FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceSatisfied;

        var recoveryEvidenceSatisfied = string.Equals(status, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, StringComparison.Ordinal);

        return new FailedApplyRecoveryCampaignResult
        {
            FailedApplyRecoveryCampaignRequestId = request?.FailedApplyRecoveryCampaignRequestId ?? Guid.Empty,
            ProjectId = request?.ProjectId ?? Guid.Empty,
            CampaignName = SafeOutputText(request?.CampaignName),
            Succeeded = recoveryEvidenceSatisfied,
            Status = status,
            SourceApplyFailureConfirmed = sourceApplyFailureConfirmed,
            RollbackEvidencePresent = rollbackEvidencePresent,
            RollbackSucceeded = rollbackSucceeded,
            RollbackAuditPresent = rollbackAuditPresent,
            RollbackAuditConsistent = rollbackAuditConsistent,
            StaleAuthorityDetected = staleAuthorityDetected,
            FollowUpReadinessEvidencePresent = followUpReadinessEvidencePresent,
            Findings = findings,
            EvidenceReferences = CollectEvidenceReferences(request),
            BoundaryMaxims = SafeList(request?.BoundaryMaxims),
            RecoveryEvidenceSatisfied = recoveryEvidenceSatisfied,
            SourceApplyRetried = false,
            SourceApplyExecuted = false,
            RollbackExecutedByCampaign = false,
            RollbackAuditExecutedByCampaign = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            GitOperationExecuted = false,
            AuthorityRefreshed = false,
            EvidenceReissued = false,
            HumanReviewRequired = true,
            CompletedAtUtc = request?.RequestedAtUtc == default ? DateTimeOffset.UtcNow : request!.RequestedAtUtc,
            Boundary = FailedApplyRecoveryCampaignBoundaryText.Boundary
        };
    }

    private static void ValidateRequestShape(
        FailedApplyRecoveryCampaignRequest? request,
        List<FailedApplyRecoveryFinding> findings,
        ref bool reject)
    {
        if (request is null)
        {
            reject = true;
            Add(findings, "RequestRequired", FailedApplyRecoveryFindingSeverities.Blocking, "request", "Failed apply recovery campaign request is required.");
            return;
        }

        RequireGuid(request.FailedApplyRecoveryCampaignRequestId, nameof(request.FailedApplyRecoveryCampaignRequestId), findings, ref reject);
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
            Add(findings, "RequestedAtRequired", FailedApplyRecoveryFindingSeverities.Blocking, nameof(request.RequestedAtUtc), "Requested timestamp is required.");
        }
    }

    private static bool ValidateSourceApplyFailure(
        SourceApplyFailureEvidence? sourceApplyFailure,
        List<FailedApplyRecoveryFinding> findings,
        ref bool reject)
    {
        if (sourceApplyFailure is null)
        {
            reject = true;
            Add(findings, "SourceApplyFailureEvidenceRequired", FailedApplyRecoveryFindingSeverities.Blocking, nameof(FailedApplyRecoveryCampaignRequest.SourceApplyFailure), "Source apply failure evidence is required.");
            return false;
        }

        var valid = true;
        RequireGuid(sourceApplyFailure.SourceApplyRequestId, nameof(sourceApplyFailure.SourceApplyRequestId), findings, ref reject, "SourceApplyFailureEvidenceRequired");
        RequireHash(sourceApplyFailure.SourceApplyRequestHash, nameof(sourceApplyFailure.SourceApplyRequestHash), "SourceApplyFailureHashInvalid", findings, ref reject);
        RequireHash(sourceApplyFailure.SourceBaselineHash, nameof(sourceApplyFailure.SourceBaselineHash), "SourceApplyFailureHashInvalid", findings, ref reject);
        RequireHash(sourceApplyFailure.WorkspaceHash, nameof(sourceApplyFailure.WorkspaceHash), "SourceApplyFailureHashInvalid", findings, ref reject);
        RequireText(sourceApplyFailure.ExpectedBranch, nameof(sourceApplyFailure.ExpectedBranch), findings, ref reject);
        RequireList(sourceApplyFailure.EvidenceReferences, $"{nameof(FailedApplyRecoveryCampaignRequest.SourceApplyFailure)}.{nameof(sourceApplyFailure.EvidenceReferences)}", findings, ref reject);

        if (sourceApplyFailure.SourceApplyReceiptId.HasValue && string.IsNullOrWhiteSpace(sourceApplyFailure.SourceApplyReceiptHash))
        {
            reject = true;
            Add(findings, "SourceApplyFailureHashInvalid", FailedApplyRecoveryFindingSeverities.Blocking, nameof(sourceApplyFailure.SourceApplyReceiptHash), "Source apply receipt hash is required when receipt id is supplied.");
        }

        if (!string.IsNullOrWhiteSpace(sourceApplyFailure.SourceApplyReceiptHash) && !IsSupportedHash(sourceApplyFailure.SourceApplyReceiptHash))
        {
            reject = true;
            Add(findings, "SourceApplyFailureHashInvalid", FailedApplyRecoveryFindingSeverities.Blocking, nameof(sourceApplyFailure.SourceApplyReceiptHash), "Source apply receipt hash must be SHA-256.");
        }

        if (!sourceApplyFailure.SourceApplyAttempted)
        {
            valid = false;
            reject = true;
            Add(findings, "SourceApplyNotAttempted", FailedApplyRecoveryFindingSeverities.Blocking, nameof(sourceApplyFailure.SourceApplyAttempted), "Source apply must have been attempted for a failed-apply recovery campaign.");
        }

        if (sourceApplyFailure.SourceApplySucceeded)
        {
            valid = false;
            reject = true;
            Add(findings, "SourceApplySucceededNotRecovery", FailedApplyRecoveryFindingSeverities.Blocking, nameof(sourceApplyFailure.SourceApplySucceeded), "Successful source apply is not failed-apply recovery evidence.");
        }

        if (!sourceApplyFailure.SourceApplyFailed && !sourceApplyFailure.SourceApplyPartial)
        {
            valid = false;
            reject = true;
            Add(findings, "SourceApplyFailureNotProven", FailedApplyRecoveryFindingSeverities.Blocking, nameof(sourceApplyFailure.SourceApplyFailed), "Source apply failure or partial application must be proven.");
        }

        if (IsNullOrEmpty(sourceApplyFailure.FailedPaths) && IsNullOrEmpty(sourceApplyFailure.AppliedPaths))
        {
            valid = false;
            reject = true;
            Add(findings, "SourceApplyFailurePathsMissing", FailedApplyRecoveryFindingSeverities.Blocking, nameof(sourceApplyFailure.FailedPaths), "Failed or applied source paths are required.");
        }

        if (sourceApplyFailure.AttemptedAtUtc == default)
        {
            valid = false;
            reject = true;
            Add(findings, "SourceApplyAttemptedAtRequired", FailedApplyRecoveryFindingSeverities.Blocking, nameof(sourceApplyFailure.AttemptedAtUtc), "Source apply attempted timestamp is required.");
        }

        return valid && !reject;
    }

    private static bool ValidateRollbackRecovery(
        RollbackRecoveryEvidence rollbackRecovery,
        List<FailedApplyRecoveryFinding> findings,
        ref bool failed)
    {
        RequireGuid(rollbackRecovery.RollbackExecutionReceiptId, nameof(rollbackRecovery.RollbackExecutionReceiptId), findings, ref failed, "RollbackEvidenceInvalid");
        RequireHash(rollbackRecovery.RollbackExecutionReceiptHash, nameof(rollbackRecovery.RollbackExecutionReceiptHash), "RollbackEvidenceInvalid", findings, ref failed);
        RequireHash(rollbackRecovery.RestoredSourceBaselineHash, nameof(rollbackRecovery.RestoredSourceBaselineHash), "RollbackEvidenceInvalid", findings, ref failed);
        RequireHash(rollbackRecovery.RestoredWorkspaceHash, nameof(rollbackRecovery.RestoredWorkspaceHash), "RollbackEvidenceInvalid", findings, ref failed);
        RequireList(rollbackRecovery.EvidenceReferences, $"{nameof(FailedApplyRecoveryCampaignRequest.RollbackRecovery)}.{nameof(rollbackRecovery.EvidenceReferences)}", findings, ref failed);

        if (!rollbackRecovery.RollbackExecuted)
        {
            failed = true;
            Add(findings, "RollbackNotExecuted", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackRecovery.RollbackExecuted), "Rollback recovery evidence does not prove rollback execution.");
        }

        if (!rollbackRecovery.RollbackSucceeded)
        {
            failed = true;
            Add(findings, "RollbackFailed", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackRecovery.RollbackSucceeded), "Rollback recovery evidence reports rollback failure.");
        }

        if (rollbackRecovery.RollbackPartial)
        {
            failed = true;
            Add(findings, "RollbackPartial", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackRecovery.RollbackPartial), "Partial rollback evidence cannot satisfy failed-apply recovery evidence.");
        }

        if (!IsNullOrEmpty(rollbackRecovery.FailedRollbackPaths))
        {
            failed = true;
            Add(findings, "RollbackFailedPathsPresent", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackRecovery.FailedRollbackPaths), "Rollback recovery evidence includes failed rollback paths.");
        }

        if (IsNullOrEmpty(rollbackRecovery.RestoredPaths))
        {
            failed = true;
            Add(findings, "RollbackRestoredPathsMissing", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackRecovery.RestoredPaths), "Rollback recovery evidence must include restored paths.");
        }

        if (rollbackRecovery.ExecutedAtUtc == default)
        {
            failed = true;
            Add(findings, "RollbackExecutedAtRequired", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackRecovery.ExecutedAtUtc), "Rollback execution timestamp is required.");
        }

        return rollbackRecovery.RollbackExecuted &&
            rollbackRecovery.RollbackSucceeded &&
            !rollbackRecovery.RollbackPartial &&
            IsNullOrEmpty(rollbackRecovery.FailedRollbackPaths) &&
            !failed;
    }

    private static bool ValidateRollbackAudit(
        RollbackAuditEvidence rollbackAudit,
        RollbackRecoveryEvidence rollbackRecovery,
        List<FailedApplyRecoveryFinding> findings,
        ref bool failed)
    {
        RequireGuid(rollbackAudit.RollbackAuditReportId, nameof(rollbackAudit.RollbackAuditReportId), findings, ref failed, "RollbackAuditEvidenceInvalid");
        RequireHash(rollbackAudit.RollbackAuditReportHash, nameof(rollbackAudit.RollbackAuditReportHash), "RollbackAuditEvidenceInvalid", findings, ref failed);
        RequireHash(rollbackAudit.AuditedSourceBaselineHash, nameof(rollbackAudit.AuditedSourceBaselineHash), "RollbackAuditEvidenceInvalid", findings, ref failed);
        RequireHash(rollbackAudit.AuditedWorkspaceHash, nameof(rollbackAudit.AuditedWorkspaceHash), "RollbackAuditEvidenceInvalid", findings, ref failed);
        RequireList(rollbackAudit.EvidenceReferences, $"{nameof(FailedApplyRecoveryCampaignRequest.RollbackAudit)}.{nameof(rollbackAudit.EvidenceReferences)}", findings, ref failed);

        if (!rollbackAudit.AuditRan)
        {
            failed = true;
            Add(findings, "RollbackAuditNotRun", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackAudit.AuditRan), "Rollback audit evidence does not prove audit execution.");
        }

        if (!rollbackAudit.AuditConsistent)
        {
            failed = true;
            Add(findings, "RollbackAuditInconsistent", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackAudit.AuditConsistent), "Rollback audit evidence reports inconsistent recovery evidence.");
        }

        if (!HashesEqual(rollbackAudit.AuditedSourceBaselineHash, rollbackRecovery.RestoredSourceBaselineHash))
        {
            failed = true;
            Add(findings, "RollbackAuditBaselineMismatch", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackAudit.AuditedSourceBaselineHash), "Rollback audit baseline does not match restored rollback baseline.");
        }

        if (!HashesEqual(rollbackAudit.AuditedWorkspaceHash, rollbackRecovery.RestoredWorkspaceHash))
        {
            failed = true;
            Add(findings, "RollbackAuditWorkspaceMismatch", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackAudit.AuditedWorkspaceHash), "Rollback audit workspace hash does not match restored rollback workspace hash.");
        }

        if (rollbackAudit.AuditedAtUtc == default)
        {
            failed = true;
            Add(findings, "RollbackAuditAtRequired", FailedApplyRecoveryFindingSeverities.Blocking, nameof(rollbackAudit.AuditedAtUtc), "Rollback audit timestamp is required.");
        }

        return rollbackAudit.AuditRan &&
            rollbackAudit.AuditConsistent &&
            HashesEqual(rollbackAudit.AuditedSourceBaselineHash, rollbackRecovery.RestoredSourceBaselineHash) &&
            HashesEqual(rollbackAudit.AuditedWorkspaceHash, rollbackRecovery.RestoredWorkspaceHash) &&
            !failed;
    }

    private static void ValidateStaleAuthorityEvidence(
        StaleAuthorityDetectionResult? staleAuthorityDetection,
        List<FailedApplyRecoveryFinding> findings,
        ref bool stale,
        out bool staleAuthorityDetected)
    {
        staleAuthorityDetected = false;

        if (staleAuthorityDetection is null)
        {
            Add(findings, "StaleAuthorityEvidenceNotSupplied", FailedApplyRecoveryFindingSeverities.Warning, nameof(FailedApplyRecoveryCampaignRequest.StaleAuthorityDetection), "Stale authority evidence was not supplied; PR226 records this as a warning only.");
            return;
        }

        if (staleAuthorityDetection.HasStaleAuthority && !staleAuthorityDetection.IsCurrent)
        {
            stale = true;
            staleAuthorityDetected = true;
            Add(findings, "StaleAuthorityBlocksRecovery", FailedApplyRecoveryFindingSeverities.Blocking, nameof(FailedApplyRecoveryCampaignRequest.StaleAuthorityDetection), "Supplied stale-authority evidence blocks recovery evidence satisfaction.");
        }
    }

    private static bool ValidateFollowUpReadinessEvidence(
        ReleaseReadinessDecisionRecord? decision,
        List<FailedApplyRecoveryFinding> findings,
        ref bool failed)
    {
        if (decision is null)
        {
            return false;
        }

        if (decision.ReleaseApproved || decision.DeploymentApproved || decision.MergeApproved)
        {
            failed = true;
            Add(findings, "FollowUpReadinessClaimsReleaseApproval", FailedApplyRecoveryFindingSeverities.Blocking, nameof(decision.ReleaseApproved), "Follow-up readiness evidence must not claim release, deployment, or merge approval.");
        }

        if (decision.SourceApplyExecutedByDecision ||
            decision.RollbackExecutedByDecision ||
            decision.WorkflowMutatedByDecision ||
            decision.GitOperationExecutedByDecision ||
            decision.ReleaseExecutedByDecision)
        {
            failed = true;
            Add(findings, "FollowUpReadinessClaimsExecution", FailedApplyRecoveryFindingSeverities.Blocking, nameof(decision.ReleaseExecutedByDecision), "Follow-up readiness evidence must not claim execution or workflow mutation.");
        }

        return string.Equals(decision.DecisionStatus, ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, StringComparison.Ordinal) &&
            decision.ReleaseReadinessEvidenceSatisfied &&
            !decision.ReleaseApproved &&
            decision.HumanReviewRequiredForReleaseApproval;
    }

    private static void ScanRequestTexts(FailedApplyRecoveryCampaignRequest request, List<FailedApplyRecoveryFinding> findings)
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

        if (request.SourceApplyFailure is not null)
        {
            ScanText(request.SourceApplyFailure.SourceApplyRequestHash, nameof(request.SourceApplyFailure.SourceApplyRequestHash), findings);
            ScanText(request.SourceApplyFailure.SourceApplyReceiptHash, nameof(request.SourceApplyFailure.SourceApplyReceiptHash), findings);
            ScanText(request.SourceApplyFailure.ExpectedBranch, nameof(request.SourceApplyFailure.ExpectedBranch), findings);
            ScanText(request.SourceApplyFailure.SourceBaselineHash, nameof(request.SourceApplyFailure.SourceBaselineHash), findings);
            ScanText(request.SourceApplyFailure.WorkspaceHash, nameof(request.SourceApplyFailure.WorkspaceHash), findings);
            ScanTexts(request.SourceApplyFailure.FailedPaths, nameof(request.SourceApplyFailure.FailedPaths), findings);
            ScanTexts(request.SourceApplyFailure.AppliedPaths, nameof(request.SourceApplyFailure.AppliedPaths), findings);
            ScanTexts(request.SourceApplyFailure.EvidenceReferences, nameof(request.SourceApplyFailure.EvidenceReferences), findings);
        }

        if (request.RollbackRecovery is not null)
        {
            ScanText(request.RollbackRecovery.RollbackExecutionReceiptHash, nameof(request.RollbackRecovery.RollbackExecutionReceiptHash), findings);
            ScanText(request.RollbackRecovery.RestoredSourceBaselineHash, nameof(request.RollbackRecovery.RestoredSourceBaselineHash), findings);
            ScanText(request.RollbackRecovery.RestoredWorkspaceHash, nameof(request.RollbackRecovery.RestoredWorkspaceHash), findings);
            ScanTexts(request.RollbackRecovery.RestoredPaths, nameof(request.RollbackRecovery.RestoredPaths), findings);
            ScanTexts(request.RollbackRecovery.FailedRollbackPaths, nameof(request.RollbackRecovery.FailedRollbackPaths), findings);
            ScanTexts(request.RollbackRecovery.EvidenceReferences, nameof(request.RollbackRecovery.EvidenceReferences), findings);
        }

        if (request.RollbackAudit is not null)
        {
            ScanText(request.RollbackAudit.RollbackAuditReportHash, nameof(request.RollbackAudit.RollbackAuditReportHash), findings);
            ScanText(request.RollbackAudit.AuditedSourceBaselineHash, nameof(request.RollbackAudit.AuditedSourceBaselineHash), findings);
            ScanText(request.RollbackAudit.AuditedWorkspaceHash, nameof(request.RollbackAudit.AuditedWorkspaceHash), findings);
            ScanTexts(request.RollbackAudit.Findings, nameof(request.RollbackAudit.Findings), findings);
            ScanTexts(request.RollbackAudit.EvidenceReferences, nameof(request.RollbackAudit.EvidenceReferences), findings);
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

    private static void ScanTexts(IEnumerable<string>? values, string field, List<FailedApplyRecoveryFinding> findings)
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

    private static void ScanText(string? value, string field, List<FailedApplyRecoveryFinding> findings)
    {
        if (ContainsPrivateOrRaw(value))
        {
            Add(findings, "PrivateRawMaterialRejected", FailedApplyRecoveryFindingSeverities.Blocking, field, "Private, raw, prompt, scratchpad, patch, or secret-like material is not allowed.");
        }

        if (ContainsAuthorityClaim(value))
        {
            Add(findings, "AuthorityClaimRejected", FailedApplyRecoveryFindingSeverities.Blocking, field, "Authority claims are not allowed.");
        }
    }

    private static void RequireGuid(
        Guid value,
        string field,
        List<FailedApplyRecoveryFinding> findings,
        ref bool reject,
        string code = "RequiredFieldMissing")
    {
        if (value == Guid.Empty)
        {
            reject = true;
            Add(findings, code, FailedApplyRecoveryFindingSeverities.Blocking, field, $"{field} is required.");
        }
    }

    private static void RequireText(
        string? value,
        string field,
        List<FailedApplyRecoveryFinding> findings,
        ref bool reject)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reject = true;
            Add(findings, "RequiredFieldMissing", FailedApplyRecoveryFindingSeverities.Blocking, field, $"{field} is required.");
        }
    }

    private static void RequireHash(
        string? value,
        string field,
        string code,
        List<FailedApplyRecoveryFinding> findings,
        ref bool reject)
    {
        if (!IsSupportedHash(value))
        {
            reject = true;
            Add(findings, code, FailedApplyRecoveryFindingSeverities.Blocking, field, $"{field} must be SHA-256.");
        }
    }

    private static void RequireList(
        IReadOnlyList<string>? values,
        string field,
        List<FailedApplyRecoveryFinding> findings,
        ref bool reject)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            reject = true;
            Add(findings, "RequiredFieldMissing", FailedApplyRecoveryFindingSeverities.Blocking, field, $"{field} is required.");
        }
    }

    private static IReadOnlyList<string> CollectEvidenceReferences(FailedApplyRecoveryCampaignRequest? request)
    {
        if (request is null)
        {
            return [];
        }

        var references = new List<string>();
        AddRange(references, request.EvidenceReferences);
        AddRange(references, request.SourceApplyFailure?.EvidenceReferences);
        AddRange(references, request.RollbackRecovery?.EvidenceReferences);
        AddRange(references, request.RollbackAudit?.EvidenceReferences);
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
        List<FailedApplyRecoveryFinding> findings,
        string code,
        string severity,
        string field,
        string message) =>
        findings.Add(new FailedApplyRecoveryFinding
        {
            Code = code,
            Severity = severity,
            Field = field,
            Message = message
        });
}
