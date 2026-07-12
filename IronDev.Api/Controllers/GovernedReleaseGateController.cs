using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/release-readiness/gate")]
public sealed class GovernedReleaseGateController : ControllerBase
{
    private static readonly string[] UnsafeRequestMarkers =
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
        "bearer",
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
        "source applied by decision",
        "rollback executed by decision",
        "workflow continued by decision",
        string.Concat("git ", "committed"),
        string.Concat("git ", "pushed"),
        "tag created",
        "pull request created",
        "memory promoted",
        "retrieval activated",
        "agent dispatched",
        "tool executed",
        "model called"
    ];

    private readonly IGovernedReleaseGateService _service;

    public GovernedReleaseGateController(IGovernedReleaseGateService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [HttpPost("governed")]
    public async Task<ActionResult<GovernedReleaseGateApiEnvelope>> EvaluateGoverned(
        [FromRoute] Guid projectId,
        [FromBody] GovernedReleaseGateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(Envelope(
                GovernedReleaseGateStatuses.Rejected,
                null,
                [new GovernedReleaseGateApiError("RequestRequired", "request", "Governed release gate request is required.")],
                releaseReadinessGateRan: false,
                refused: true));
        }

        if (request.ProjectId != projectId)
        {
            return BadRequest(Envelope(
                GovernedReleaseGateStatuses.Rejected,
                null,
                [new GovernedReleaseGateApiError("ProjectMismatch", "projectId", "Route projectId must match request projectId.")],
                releaseReadinessGateRan: false,
                refused: true));
        }

        if (ContainsUnsafeRequestMaterial(request))
        {
            return BadRequest(Envelope(
                GovernedReleaseGateStatuses.Rejected,
                null,
                [new GovernedReleaseGateApiError("UnsafeRequestMaterialRejected", "request", "Governed release gate request contains unsafe private, raw, secret-like, authority, or execution material.")],
                releaseReadinessGateRan: false,
                refused: true));
        }

        var result = await _service.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        var data = SanitizeResult(result);
        var envelope = Envelope(
            result.Status,
            data,
            result.Issues.Select(issue => new GovernedReleaseGateApiError(issue.Code, issue.Field, issue.Message)).ToArray(),
            result.ReleaseReadinessGateRan,
            result.Warnings,
            refused: !result.Succeeded);

        if (result.Succeeded)
            return Ok(envelope);

        if (result.Status is GovernedReleaseGateStatuses.DecisionRecordSaveFailed or GovernedReleaseGateStatuses.DecisionRecordReadBackFailed)
            return StatusCode(StatusCodes.Status500InternalServerError, envelope);

        return BadRequest(envelope);
    }

    private static bool ContainsUnsafeRequestMaterial(GovernedReleaseGateRequest request)
    {
        var serialized = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return UnsafeRequestMarkers.Any(marker => serialized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private GovernedReleaseGateApiEnvelope Envelope(
        string status,
        GovernedReleaseGateApiResult? data,
        IReadOnlyList<GovernedReleaseGateApiError>? errors = null,
        bool releaseReadinessGateRan = false,
        IReadOnlyList<string>? warnings = null,
        bool refused = false)
    {
        var normalizedErrors = errors ?? [];
        return new(
            status,
            data,
            normalizedErrors,
            warnings ?? GovernedReleaseGateBoundaryText.Warnings,
            new GovernedReleaseGateApiBoundary(
                ReleaseReadinessGateRan: releaseReadinessGateRan,
                DecisionRecordStored: data?.DecisionRecordStored == true,
                ReleaseStateMutated: false,
                WorkflowStateMutated: false,
                SourceStateMutated: false,
                GitStateMutated: false,
                ReleaseApproved: false,
                DeploymentApproved: false,
                MergeApproved: false,
                ReleaseExecuted: false,
                SourceApplyExecuted: false,
                RollbackExecuted: false,
                WorkflowContinued: false,
                GitOperationExecuted: false,
                HumanReviewRequired: true,
                Boundary: GovernedReleaseGateBoundaryText.Boundary))
        {
            Refusal = refused ? ReleaseRefusal(status, normalizedErrors) : null
        };
    }

    private GovernedRefusalEnvelope ReleaseRefusal(
        string status,
        IReadOnlyList<GovernedReleaseGateApiError> errors) =>
        GovernedRefusal.Create(
            errors.FirstOrDefault()?.Code ?? status,
            errors.FirstOrDefault()?.Message ?? "Release readiness evaluation was refused.",
            HttpContext.TraceIdentifier,
            blockedReasons: errors.Select(error => error.Message),
            missingEvidence: errors.Where(error => error.Code.Contains("Missing", StringComparison.OrdinalIgnoreCase)).Select(error => error.Message),
            nextSafeActions: ["Resolve the blocked reasons and submit fresh release-readiness evidence."],
            forbiddenActions: ["Approve release", "Approve deployment", "Approve merge", "Execute source apply"]);

    private static GovernedReleaseGateApiResult SanitizeResult(GovernedReleaseGateResult result) =>
        new(
            result.GovernedReleaseGateRequestId,
            result.ProjectId,
            result.Succeeded,
            result.Status,
            result.ReleaseReadinessGateRan,
            result.DecisionRecordStored,
            result.DecisionRecord is null
                ? null
                : new GovernedReleaseGateApiDecisionRecord(
                    result.DecisionRecord.ReleaseReadinessDecisionRecordId,
                    result.DecisionRecord.ProjectId,
                    result.DecisionRecord.ReleaseReadinessReportId,
                    Safe(result.DecisionRecord.ReleaseReadinessReportHash),
                    Safe(result.DecisionRecord.WorkflowRunId),
                    Safe(result.DecisionRecord.WorkflowStepId),
                    Safe(result.DecisionRecord.SubjectKind),
                    Safe(result.DecisionRecord.SubjectId),
                    Safe(result.DecisionRecord.SubjectHash),
                    Safe(result.DecisionRecord.DecisionStatus),
                    result.DecisionRecord.ReleaseReadinessEvidenceSatisfied,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    true,
                    true,
                    true,
                    result.DecisionRecord.DecidedAtUtc,
                    Safe(result.DecisionRecord.ReleaseReadinessDecisionRecordHash)),
            result.ReleaseReadinessEvidenceSatisfied,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            result.CompletedAtUtc);

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return UnsafeRequestMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? "[redacted]"
            : value.Trim();
    }
}

public sealed record GovernedReleaseGateApiEnvelope(
    string Status,
    GovernedReleaseGateApiResult? Data,
    IReadOnlyList<GovernedReleaseGateApiError> Errors,
    IReadOnlyList<string> Warnings,
    GovernedReleaseGateApiBoundary Boundary)
{
    public GovernedRefusalEnvelope? Refusal { get; init; }
}

public sealed record GovernedReleaseGateApiResult(
    Guid GovernedReleaseGateRequestId,
    Guid ProjectId,
    bool Succeeded,
    string Status,
    bool ReleaseReadinessGateRan,
    bool DecisionRecordStored,
    GovernedReleaseGateApiDecisionRecord? DecisionRecord,
    bool ReleaseReadinessEvidenceSatisfied,
    bool ReleaseApproved,
    bool DeploymentApproved,
    bool MergeApproved,
    bool ReleaseExecuted,
    bool SourceApplyExecuted,
    bool RollbackExecuted,
    bool WorkflowContinued,
    bool WorkflowMutated,
    bool GitOperationExecuted,
    bool HumanReviewRequiredForReleaseApproval,
    bool HumanReviewRequiredForDeployment,
    bool HumanReviewRequiredForMerge,
    DateTimeOffset CompletedAtUtc);

public sealed record GovernedReleaseGateApiDecisionRecord(
    Guid ReleaseReadinessDecisionRecordId,
    Guid ProjectId,
    Guid ReleaseReadinessReportId,
    string ReleaseReadinessReportHash,
    string WorkflowRunId,
    string WorkflowStepId,
    string SubjectKind,
    string SubjectId,
    string SubjectHash,
    string DecisionStatus,
    bool ReleaseReadinessEvidenceSatisfied,
    bool ReleaseApproved,
    bool DeploymentApproved,
    bool MergeApproved,
    bool SourceApplyExecutedByDecision,
    bool RollbackExecutedByDecision,
    bool WorkflowMutatedByDecision,
    bool GitOperationExecutedByDecision,
    bool ReleaseExecutedByDecision,
    bool HumanReviewRequiredForReleaseApproval,
    bool HumanReviewRequiredForDeployment,
    bool HumanReviewRequiredForMerge,
    DateTimeOffset DecidedAtUtc,
    string ReleaseReadinessDecisionRecordHash);

public sealed record GovernedReleaseGateApiError(string Code, string Field, string Message);

public sealed record GovernedReleaseGateApiBoundary(
    bool ReleaseReadinessGateRan,
    bool DecisionRecordStored,
    bool ReleaseStateMutated,
    bool WorkflowStateMutated,
    bool SourceStateMutated,
    bool GitStateMutated,
    bool ReleaseApproved,
    bool DeploymentApproved,
    bool MergeApproved,
    bool ReleaseExecuted,
    bool SourceApplyExecuted,
    bool RollbackExecuted,
    bool WorkflowContinued,
    bool GitOperationExecuted,
    bool HumanReviewRequired,
    string Boundary);
