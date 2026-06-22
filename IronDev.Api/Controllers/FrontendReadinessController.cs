using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/frontend-readiness")]
public sealed class FrontendReadinessController : ControllerBase
{
    private readonly IFrontendReadinessReadApi _readApi;
    private readonly IFrontendControlledActionRequestService _actionRequestService;

    public FrontendReadinessController(
        IFrontendReadinessReadApi readApi,
        IFrontendControlledActionRequestService? actionRequestService = null)
    {
        _readApi = readApi ?? throw new ArgumentNullException(nameof(readApi));
        _actionRequestService = actionRequestService ?? new FrontendControlledActionRequestService();
    }

    [HttpGet("operations/{operationId}/status")]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>> GetOperationStatus(
        string operationId,
        [FromQuery] bool compact = false)
    {
        var model = _readApi.GetOperationStatus(operationId);
        return model is null
            ? NotFound(Envelope<FrontendOperationStatusReadModel>("not_found", null, Error("operationId", "Operation status was not found.")))
            : Ok(Envelope("found", model, compactWarning: compact));
    }

    [HttpGet("operations/{operationId}/timeline")]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendOperationTimelineReadModel>> GetOperationTimeline(
        string operationId,
        [FromQuery] bool compact = false)
    {
        var model = _readApi.GetOperationTimeline(operationId);
        return model is null
            ? NotFound(Envelope<FrontendOperationTimelineReadModel>("not_found", null, Error("operationId", "Operation timeline was not found.")))
            : Ok(Envelope("found", model, compactWarning: compact));
    }

    [HttpGet("patch-packages/{packageId}/metadata")]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendPatchPackageMetadataReadModel>> GetPatchPackageMetadata(
        string packageId,
        [FromQuery] bool compact = false)
    {
        var model = _readApi.GetPatchPackageMetadata(packageId);
        return model is null
            ? NotFound(Envelope<FrontendPatchPackageMetadataReadModel>("not_found", null, Error("packageId", "Patch package metadata was not found.")))
            : Ok(Envelope("found", model, compactWarning: compact));
    }

    [HttpGet("patch-packages/{packageId}/artifacts")]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendPatchPackageArtifactsReadModel>> GetPatchPackageArtifacts(
        string packageId,
        [FromQuery] bool compact = false)
    {
        var model = _readApi.GetPatchPackageArtifacts(packageId);
        return model is null
            ? NotFound(Envelope<FrontendPatchPackageArtifactsReadModel>("not_found", null, Error("packageId", "Patch package artifacts were not found.")))
            : Ok(Envelope("found", model, compactWarning: compact));
    }

    [HttpGet("validation-results/{validationResultId}/metadata")]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendValidationResultMetadataReadModel>> GetValidationResultMetadata(
        string validationResultId,
        [FromQuery] bool compact = false)
    {
        var model = _readApi.GetValidationResultMetadata(validationResultId);
        return model is null
            ? NotFound(Envelope<FrontendValidationResultMetadataReadModel>("not_found", null, Error("validationResultId", "Validation result metadata was not found.")))
            : Ok(Envelope("found", model, compactWarning: compact));
    }

    [HttpGet("evidence/{evidenceRef}/metadata")]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendEvidenceMetadataReadModel>> GetEvidenceMetadata(
        string evidenceRef,
        [FromQuery] bool compact = false)
    {
        var model = _readApi.GetEvidenceMetadata(evidenceRef);
        return model is null
            ? NotFound(Envelope<FrontendEvidenceMetadataReadModel>("not_found", null, Error("evidenceRef", "Evidence metadata was not found.")))
            : Ok(Envelope("found", model, compactWarning: compact));
    }

    [HttpGet("receipts/{receiptRef}/metadata")]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendReceiptMetadataReadModel>> GetReceiptMetadata(
        string receiptRef,
        [FromQuery] bool compact = false)
    {
        var model = _readApi.GetReceiptMetadata(receiptRef);
        return model is null
            ? NotFound(Envelope<FrontendReceiptMetadataReadModel>("not_found", null, Error("receiptRef", "Receipt metadata was not found.")))
            : Ok(Envelope("found", model, compactWarning: compact));
    }

    [HttpPost("action-requests")]
    public ActionResult<ControlledActionRequestCreateResponse> CreateActionRequest(
        [FromBody] ControlledActionRequestCreateRequest request)
    {
        var response = _actionRequestService.Create(request);
        return Ok(response);
    }

    private static FrontendReadinessApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        FrontendReadinessApiError? error = null,
        bool compactWarning = false) =>
        new()
        {
            Status = status,
            Data = data,
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            MutationOccurred = false,
            Warnings = Warnings(compactWarning),
            Errors = error is null ? [] : [error]
        };

    private static IReadOnlyList<string> Warnings(bool compactRequested)
    {
        var warnings = new List<string>
        {
            "Frontend readiness read endpoints are read-only.",
            "Frontend readiness output is not approval, policy satisfaction, execution authority, memory promotion, or workflow continuation.",
            "Forbidden actions and missing evidence remain visible; compact mode cannot hide them."
        };

        if (compactRequested)
            warnings.Add("Compact mode was requested but authority-critical fields are still returned.");

        return warnings;
    }

    private static FrontendReadinessApiError Error(string field, string message) =>
        new()
        {
            Category = "not_found",
            Code = "FRONTEND_READINESS_NOT_FOUND",
            Field = field,
            Message = message
        };
}

public sealed record FrontendReadinessApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<FrontendReadinessApiError> Errors { get; init; } = [];
}

public sealed record FrontendReadinessApiError
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}
