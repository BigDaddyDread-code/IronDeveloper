using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public static class ReleaseReadinessGateBoundaryText
{
    public const string Boundary = """
        Release readiness gate evaluator decides evidence satisfaction only.
        Release readiness gate evaluator is not release approval.
        Release readiness gate evaluator is not deployment approval.
        Release readiness gate evaluator is not merge approval.
        Release readiness gate evaluator is not release execution.
        Release readiness gate evaluator is not source apply.
        Release readiness gate evaluator is not rollback execution.
        Release readiness gate evaluator is not workflow continuation.
        Release readiness gate evaluator does not mutate workflow state.
        Release readiness gate evaluator does not run git.
        Release readiness gate evaluator does not call agents, models, tools, API, CLI, UI, memory, or retrieval.
        Release readiness gate evaluator emits a ReleaseReadinessDecisionRecord for human review.
        Human review remains required for release approval, deployment, and merge.
        """;
}

public sealed record ReleaseReadinessGateRequest
{
    public required Guid ReleaseReadinessGateRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required ReleaseReadinessReport ReleaseReadinessReport { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = ReleaseReadinessGateBoundaryText.Boundary;
}

public sealed class ReleaseReadinessGateEvaluator
{
    private static readonly Guid MissingContextId = Guid.Parse("2cf9e487-4ac8-40c1-bae6-8b162a9f2190");

    private static readonly string[] PrivateRawMarkers =
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
        "scratchpad",
        "private reasoning",
        "hidden reasoning",
        "system prompt",
        "developer prompt",
        "entire patch",
        "entirepatch",
        "patch payload",
        "patchpayload",
        "password",
        "api_key",
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
        "safe to deploy",
        "safe to merge",
        "can deploy",
        "can merge",
        "green to ship",
        "release executed",
        "deployed by decision",
        "merged by decision",
        "source applied by decision",
        "rollback executed by decision",
        "workflow continued by decision",
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

    public ReleaseReadinessDecisionRecord Evaluate(ReleaseReadinessGateRequest? request)
    {
        var reasons = new List<ReleaseReadinessDecisionReason>();
        var missingEvidence = false;
        var failedEvidence = false;
        var report = request?.ReleaseReadinessReport;

        if (request is null)
        {
            missingEvidence = true;
            AddBlocking(reasons, "ReportBlockedByMissingEvidence", "request", "Release readiness gate request is required.");
        }
        else
        {
            ValidateRequestShape(request, reasons, ref missingEvidence, ref failedEvidence);
        }

        if (report is null)
        {
            missingEvidence = true;
            AddBlocking(reasons, "ReportRequired", nameof(ReleaseReadinessGateRequest.ReleaseReadinessReport), "Release readiness report evidence is required.");
        }
        else
        {
            ValidateReport(report, reasons, ref missingEvidence, ref failedEvidence);
        }

        var status = missingEvidence
            ? ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence
            : failedEvidence
                ? ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence
                : ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied;

        if (status == ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied)
        {
            AddInfo(reasons, "ReportValidated", nameof(ReleaseReadinessGateRequest.ReleaseReadinessReport), "Release readiness report validates as evidence only.");
            AddInfo(reasons, "ReportHashValid", nameof(ReleaseReadinessReport.ReleaseReadinessReportHash), "Release readiness report hash matches the report content.");
            AddInfo(reasons, "ReportComplete", nameof(ReleaseReadinessReport.Status), "Release readiness report status is complete.");
            AddInfo(reasons, "ApprovalEvidencePresent", nameof(ReleaseReadinessReport.ApprovalEvidencePresent), "Accepted approval evidence is present.");
            AddInfo(reasons, "PolicyEvidencePresent", nameof(ReleaseReadinessReport.PolicyEvidencePresent), "Policy satisfaction evidence is present.");
            if (report?.SourceApplySucceeded == true)
            {
                AddInfo(reasons, "SourceApplyEvidenceSatisfied", nameof(ReleaseReadinessReport.SourceApplySucceeded), "Source apply receipt evidence is satisfied.");
            }
            else
            {
                AddInfo(reasons, "RollbackRecoveryEvidenceSatisfied", nameof(ReleaseReadinessReport.RollbackSucceeded), "Rollback recovery evidence is satisfied.");
            }

            AddInfo(reasons, "WorkflowContinuationEvidenceSatisfied", nameof(ReleaseReadinessReport.WorkflowContinuationSucceeded), "Workflow continuation gate evidence is satisfied.");
            AddInfo(reasons, "WorkflowTransitionEvidenceSatisfied", nameof(ReleaseReadinessReport.WorkflowTransitionRecordValid), "Workflow transition record evidence is valid.");
            AddWarning(reasons, "HumanReviewRequiredForReleaseApproval", nameof(ReleaseReadinessDecisionRecord.HumanReviewRequiredForReleaseApproval), "Human review remains required for release approval, deployment, and merge.");
        }

        if (reasons.Count == 0)
        {
            AddBlocking(reasons, "ReportBlockedByMissingEvidence", "request", "Release readiness evidence is missing.");
            status = ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence;
        }

        return BuildDecisionRecord(request, report, status, reasons);
    }

    private static void ValidateRequestShape(
        ReleaseReadinessGateRequest request,
        List<ReleaseReadinessDecisionReason> reasons,
        ref bool missingEvidence,
        ref bool failedEvidence)
    {
        if (request.ReleaseReadinessGateRequestId == Guid.Empty) { missingEvidence = true; AddBlocking(reasons, "ReleaseReadinessGateRequestIdRequired", nameof(request.ReleaseReadinessGateRequestId), "Release readiness gate request ID is required."); }
        if (request.ProjectId == Guid.Empty) { missingEvidence = true; AddBlocking(reasons, "ProjectIdRequired", nameof(request.ProjectId), "Project ID is required."); }
        if (request.RequestedAtUtc == default) { missingEvidence = true; AddBlocking(reasons, "RequestedAtRequired", nameof(request.RequestedAtUtc), "Requested timestamp is required."); }
        if (request.EvidenceReferences is null || request.EvidenceReferences.Count == 0 || request.EvidenceReferences.Any(string.IsNullOrWhiteSpace)) { missingEvidence = true; AddBlocking(reasons, "GateEvidenceReferencesRequired", nameof(request.EvidenceReferences), "Gate evidence references are required."); }
        if (request.BoundaryMaxims is null || request.BoundaryMaxims.Count == 0 || request.BoundaryMaxims.Any(string.IsNullOrWhiteSpace)) { missingEvidence = true; AddBlocking(reasons, "GateBoundaryMaximsRequired", nameof(request.BoundaryMaxims), "Gate boundary maxims are required."); }
        if (string.IsNullOrWhiteSpace(request.Boundary)) { missingEvidence = true; AddBlocking(reasons, "GateBoundaryRequired", nameof(request.Boundary), "Gate boundary text is required."); }

        ScanText(request.Boundary, nameof(request.Boundary), reasons, ref failedEvidence);
        ScanTexts(request.EvidenceReferences, nameof(request.EvidenceReferences), reasons, ref failedEvidence);
        ScanTexts(request.BoundaryMaxims, nameof(request.BoundaryMaxims), reasons, ref failedEvidence);

        if (request.ReleaseReadinessReport is not null && request.ProjectId != Guid.Empty && request.ReleaseReadinessReport.ProjectId != request.ProjectId)
        {
            failedEvidence = true;
            AddBlocking(reasons, "ReportProjectMismatch", nameof(request.ProjectId), "Release readiness report project must match gate request project.");
        }

    }

    private static void ValidateReport(
        ReleaseReadinessReport report,
        List<ReleaseReadinessDecisionReason> reasons,
        ref bool missingEvidence,
        ref bool failedEvidence)
    {
        foreach (var issue in ReleaseReadinessReportValidation.Validate(report).Issues)
        {
            failedEvidence = true;
            AddBlocking(reasons, $"ReportValidation.{issue.Code}", issue.Field, "Release readiness report validation failed.");
        }

        var recomputed = ReleaseReadinessReportHashing.ComputeReportHash(report);
        if (!string.Equals(report.ReleaseReadinessReportHash, recomputed, StringComparison.Ordinal))
        {
            failedEvidence = true;
            AddBlocking(reasons, "ReportHashMismatch", nameof(report.ReleaseReadinessReportHash), "Release readiness report hash does not match report content.");
        }

        switch (report.Status)
        {
            case ReleaseReadinessReportStatuses.Complete:
                break;
            case ReleaseReadinessReportStatuses.BlockedByMissingEvidence:
                missingEvidence = true;
                AddBlocking(reasons, "ReportBlockedByMissingEvidence", nameof(report.Status), "Release readiness report is blocked by missing evidence.");
                break;
            case ReleaseReadinessReportStatuses.BlockedByFailedEvidence:
                failedEvidence = true;
                AddBlocking(reasons, "ReportBlockedByFailedEvidence", nameof(report.Status), "Release readiness report is blocked by failed evidence.");
                break;
            default:
                failedEvidence = true;
                AddBlocking(reasons, "ReportStatusUnsupported", nameof(report.Status), "Release readiness report status is unsupported.");
                break;
        }

        if (report.Findings?.Any(finding => finding.Severity == ReleaseReadinessFindingSeverities.Blocking) == true)
        {
            failedEvidence = true;
            AddBlocking(reasons, "ReportHasBlockingFindings", nameof(report.Findings), "Release readiness report contains blocking findings.");
        }

        if (!report.ApprovalEvidencePresent) { missingEvidence = true; AddBlocking(reasons, "ApprovalEvidenceMissing", nameof(report.ApprovalEvidencePresent), "Accepted approval evidence is missing."); }
        if (!report.PolicyEvidencePresent) { missingEvidence = true; AddBlocking(reasons, "PolicyEvidenceMissing", nameof(report.PolicyEvidencePresent), "Policy satisfaction evidence is missing."); }

        var rollbackRecoverySatisfied = report.RollbackWasExecuted && report.RollbackSucceeded && !report.RollbackPartial && report.RollbackAuditConsistent;
        if (!report.SourceApplySucceeded && !rollbackRecoverySatisfied)
        {
            failedEvidence = true;
            AddBlocking(reasons, report.SourceApplyPartial ? "PartialSourceApplyWithoutRollbackRecovery" : "FailedSourceApplyWithoutRollbackRecovery", nameof(report.SourceApplySucceeded), "Source apply evidence is not satisfied and no successful rollback recovery evidence is present.");
        }

        if (report.RollbackWasExecuted && (!report.RollbackSucceeded || report.RollbackPartial || !report.RollbackAuditConsistent))
        {
            failedEvidence = true;
            AddBlocking(reasons, "RollbackRecoveryEvidenceFailed", nameof(report.RollbackSucceeded), "Rollback recovery evidence failed or is inconsistent.");
        }

        if (!report.WorkflowContinuationSucceeded)
        {
            failedEvidence = true;
            AddBlocking(reasons, "WorkflowContinuationEvidenceUnsatisfied", nameof(report.WorkflowContinuationSucceeded), "Workflow continuation evidence is not satisfied.");
        }

        if (!report.WorkflowTransitionRecordValid)
        {
            failedEvidence = true;
            AddBlocking(reasons, "WorkflowTransitionEvidenceInvalid", nameof(report.WorkflowTransitionRecordValid), "Workflow transition record evidence is invalid.");
        }

        if (report.ReleaseReadinessDecided) { failedEvidence = true; AddBlocking(reasons, "ReportClaimsReleaseReadinessDecision", nameof(report.ReleaseReadinessDecided), "Report must not decide release readiness."); }
        if (report.ReleaseReady) { failedEvidence = true; AddBlocking(reasons, "ReportClaimsReleaseAuthority", nameof(report.ReleaseReady), "Report must not mark release ready."); }
        if (report.ReleaseApproved) { failedEvidence = true; AddBlocking(reasons, "ReportClaimsReleaseAuthority", nameof(report.ReleaseApproved), "Report must not approve release."); }
        if (report.DeploymentApproved) { failedEvidence = true; AddBlocking(reasons, "ReportClaimsReleaseAuthority", nameof(report.DeploymentApproved), "Report must not approve deployment."); }
        if (report.MergeApproved) { failedEvidence = true; AddBlocking(reasons, "ReportClaimsReleaseAuthority", nameof(report.MergeApproved), "Report must not approve merge."); }
        if (report.SourceApplyExecutedByReport) { failedEvidence = true; AddBlocking(reasons, "ReportClaimsExecutionAuthority", nameof(report.SourceApplyExecutedByReport), "Report must not execute source apply."); }
        if (report.RollbackExecutedByReport) { failedEvidence = true; AddBlocking(reasons, "ReportClaimsExecutionAuthority", nameof(report.RollbackExecutedByReport), "Report must not execute rollback."); }
        if (report.WorkflowMutatedByReport) { failedEvidence = true; AddBlocking(reasons, "ReportClaimsExecutionAuthority", nameof(report.WorkflowMutatedByReport), "Report must not mutate workflow state."); }
        if (report.GitOperationExecutedByReport) { failedEvidence = true; AddBlocking(reasons, "ReportClaimsExecutionAuthority", nameof(report.GitOperationExecutedByReport), "Report must not execute git operations."); }

        if (!report.HumanReviewRequiredForReadiness)
        {
            failedEvidence = true;
            AddBlocking(reasons, "HumanReviewForReadinessMissing", nameof(report.HumanReviewRequiredForReadiness), "Human review remains required for readiness evidence.");
        }

        if (!report.HumanReviewRequiredForReleaseApproval)
        {
            failedEvidence = true;
            AddBlocking(reasons, "HumanReviewForReleaseApprovalMissing", nameof(report.HumanReviewRequiredForReleaseApproval), "Human review remains required for release approval.");
        }

        ScanReportText(report, reasons, ref failedEvidence);

    }

    private static void ScanReportText(ReleaseReadinessReport report, List<ReleaseReadinessDecisionReason> reasons, ref bool failedEvidence)
    {
        ScanText(report.Status, nameof(report.Status), reasons, ref failedEvidence);
        ScanText(report.WorkflowRunId, nameof(report.WorkflowRunId), reasons, ref failedEvidence);
        ScanText(report.WorkflowStepId, nameof(report.WorkflowStepId), reasons, ref failedEvidence);
        ScanText(report.SubjectKind, nameof(report.SubjectKind), reasons, ref failedEvidence);
        ScanText(report.SubjectId, nameof(report.SubjectId), reasons, ref failedEvidence);
        ScanText(report.SubjectHash, nameof(report.SubjectHash), reasons, ref failedEvidence);
        ScanText(report.ReleaseReadinessReportHash, nameof(report.ReleaseReadinessReportHash), reasons, ref failedEvidence);
        ScanText(report.Boundary, nameof(report.Boundary), reasons, ref failedEvidence);
        ScanTexts(report.EvidenceReferences, nameof(report.EvidenceReferences), reasons, ref failedEvidence);
        ScanTexts(report.BoundaryMaxims, nameof(report.BoundaryMaxims), reasons, ref failedEvidence);

        if (report.Findings is null)
        {
            return;
        }

        foreach (var finding in report.Findings)
        {
            ScanText(finding.Code, nameof(finding.Code), reasons, ref failedEvidence);
            ScanText(finding.Severity, nameof(finding.Severity), reasons, ref failedEvidence);
            ScanText(finding.Field, nameof(finding.Field), reasons, ref failedEvidence);
            ScanText(finding.Message, nameof(finding.Message), reasons, ref failedEvidence);
        }
    }

    private static void ScanTexts(IEnumerable<string>? values, string field, List<ReleaseReadinessDecisionReason> reasons, ref bool failedEvidence)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            ScanText(value, field, reasons, ref failedEvidence);
        }
    }

    private static void ScanText(string? value, string field, List<ReleaseReadinessDecisionReason> reasons, ref bool failedEvidence)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (ContainsPrivateRawMarker(value))
        {
            failedEvidence = true;
            AddBlocking(reasons, "PrivateRawMaterialRejected", field, "Unsafe release readiness gate material is not allowed.");
        }

        if (ContainsForbiddenAuthorityMarker(value))
        {
            failedEvidence = true;
            AddBlocking(reasons, "AuthorityClaimRejected", field, "Release readiness gate material must not claim release, deployment, merge, execution, git, memory, retrieval, agent, model, or tool authority.");
        }
    }

    private static ReleaseReadinessDecisionRecord BuildDecisionRecord(
        ReleaseReadinessGateRequest? request,
        ReleaseReadinessReport? report,
        string status,
        IReadOnlyList<ReleaseReadinessDecisionReason> reasons)
    {
        var record = new ReleaseReadinessDecisionRecord
        {
            ReleaseReadinessDecisionRecordId = request?.ReleaseReadinessGateRequestId == Guid.Empty ? DeterministicGuid("missing-gate-request") : request?.ReleaseReadinessGateRequestId ?? DeterministicGuid("missing-gate-request"),
            ProjectId = request?.ProjectId == Guid.Empty ? MissingContextId : request?.ProjectId ?? MissingContextId,
            ReleaseReadinessReportId = report?.ReleaseReadinessReportId == Guid.Empty ? DeterministicGuid("missing-report") : report?.ReleaseReadinessReportId ?? DeterministicGuid("missing-report"),
            ReleaseReadinessReportHash = ToDecisionRecordHash(report?.ReleaseReadinessReportHash, "missing-report-hash"),
            WorkflowRunId = SafeText(report?.WorkflowRunId, "missing-workflow-run"),
            WorkflowStepId = SafeText(report?.WorkflowStepId, "missing-workflow-step"),
            SubjectKind = SafeText(report?.SubjectKind, "ReleasePackage"),
            SubjectId = SafeText(report?.SubjectId, "missing-release-subject"),
            SubjectHash = ToDecisionRecordHash(report?.SubjectHash, "missing-subject-hash"),
            DecisionStatus = status,
            ReleaseReadinessEvidenceSatisfied = status is ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied or ReleaseReadinessDecisionStatuses.BlockedByHumanReviewRequired,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            SourceApplyExecutedByDecision = false,
            RollbackExecutedByDecision = false,
            WorkflowMutatedByDecision = false,
            GitOperationExecutedByDecision = false,
            ReleaseExecutedByDecision = false,
            HumanReviewRequiredForReleaseApproval = true,
            HumanReviewRequiredForDeployment = true,
            HumanReviewRequiredForMerge = true,
            Reasons = reasons,
            EvidenceReferences = EvidenceReferences(request, report),
            BoundaryMaxims = BoundaryMaxims(request, report),
            DecidedAtUtc = request?.RequestedAtUtc == default ? DateTimeOffset.UtcNow : request?.RequestedAtUtc ?? DateTimeOffset.UtcNow,
            ReleaseReadinessDecisionRecordHash = Sha256Hex("pending"),
            Boundary = ReleaseReadinessDecisionRecordBoundaryText.Boundary
        };

        return record with
        {
            ReleaseReadinessDecisionRecordHash = ReleaseReadinessDecisionRecordHashing.ComputeHash(record)
        };
    }

    private static IReadOnlyList<string> EvidenceReferences(ReleaseReadinessGateRequest? request, ReleaseReadinessReport? report)
    {
        var values = new List<string>();
        values.AddRange(SafeList(report?.EvidenceReferences));
        values.AddRange(SafeList(request?.EvidenceReferences));
        if (report is not null)
        {
            values.Add($"release-readiness-report:{report.ReleaseReadinessReportId:D}");
        }

        return values.Distinct(StringComparer.Ordinal).DefaultIfEmpty("release-readiness-gate:evidence-missing").ToArray();
    }

    private static IReadOnlyList<string> BoundaryMaxims(ReleaseReadinessGateRequest? request, ReleaseReadinessReport? report)
    {
        var values = new List<string>();
        values.AddRange(SafeList(report?.BoundaryMaxims));
        values.AddRange(SafeList(request?.BoundaryMaxims));
        values.Add("Release readiness gate evaluator is not release approval.");
        values.Add("Human review remains required for release approval, deployment, and merge.");
        return values.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> SafeList(IEnumerable<string>? values) =>
        values?
            .Select(value => SafeText(value, string.Empty))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray() ?? [];

    private static string SafeText(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || ContainsUnsafeOutputMarker(value))
        {
            return fallback;
        }

        return value.Trim();
    }

    private static string ToDecisionRecordHash(string? value, string fallbackSeed)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..];
        }

        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized.ToLowerInvariant() : Sha256Hex(fallbackSeed);
    }

    private static bool ContainsUnsafeMarker(string value) =>
        ContainsPrivateRawMarker(value) || ContainsForbiddenAuthorityMarker(value);

    private static bool ContainsUnsafeOutputMarker(string value) =>
        ContainsPrivateRawMarker(value) || ContainsAuthorityMarker(value);

    private static bool ContainsAuthorityMarker(string value)
    {
        var normalized = NormalizeForMarkerSearch(value);
        return AuthorityMarkers.Select(NormalizeForMarkerSearch).Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static bool ContainsPrivateRawMarker(string value)
    {
        var normalized = NormalizeForMarkerSearch(value);
        return PrivateRawMarkers.Select(NormalizeForMarkerSearch).Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static bool ContainsForbiddenAuthorityMarker(string value)
    {
        var normalized = NormalizeForMarkerSearch(value);
        foreach (var marker in AuthorityMarkers.Select(NormalizeForMarkerSearch))
        {
            var index = normalized.IndexOf(marker, StringComparison.Ordinal);
            while (index >= 0)
            {
                var prefix = normalized[..index];
                if (!SafeAuthorityPrefixes.Any(safePrefix => prefix.EndsWith(safePrefix, StringComparison.Ordinal)))
                {
                    return true;
                }

                index = normalized.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static string NormalizeForMarkerSearch(string value) =>
        value.Trim().ToLowerInvariant().Replace("_", " ", StringComparison.Ordinal);

    private static void AddInfo(List<ReleaseReadinessDecisionReason> reasons, string code, string field, string message) =>
        Add(reasons, code, ReleaseReadinessDecisionReasonSeverities.Info, field, message);

    private static void AddWarning(List<ReleaseReadinessDecisionReason> reasons, string code, string field, string message) =>
        Add(reasons, code, ReleaseReadinessDecisionReasonSeverities.Warning, field, message);

    private static void AddBlocking(List<ReleaseReadinessDecisionReason> reasons, string code, string field, string message) =>
        Add(reasons, code, ReleaseReadinessDecisionReasonSeverities.Blocking, field, message);

    private static void Add(List<ReleaseReadinessDecisionReason> reasons, string code, string severity, string field, string message)
    {
        if (reasons.Any(reason => string.Equals(reason.Code, code, StringComparison.Ordinal) && string.Equals(reason.Field, field, StringComparison.Ordinal)))
        {
            return;
        }

        reasons.Add(new ReleaseReadinessDecisionReason
        {
            Code = code,
            Severity = severity,
            Field = field,
            Message = message
        });
    }

    private static Guid DeterministicGuid(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var guidBytes = bytes.Take(16).ToArray();
        return new Guid(guidBytes);
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
