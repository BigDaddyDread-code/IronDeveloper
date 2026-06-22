using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>> GetOperationStatus(
        string operationId,
        [FromQuery] bool compact = false) =>
        Read(
            () => _readApi.GetOperationStatus(operationId),
            () => _readApi.GetOperationStatusReadState(operationId),
            "operationId",
            compact);

    [HttpGet("operations/{operationId}/timeline")]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendOperationTimelineReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendOperationTimelineReadModel>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendOperationTimelineReadModel>), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendOperationTimelineReadModel>> GetOperationTimeline(
        string operationId,
        [FromQuery] bool compact = false) =>
        Read(
            () => _readApi.GetOperationTimeline(operationId),
            () => _readApi.GetOperationTimelineReadState(operationId),
            "operationId",
            compact);

    [HttpGet("patch-packages/{packageId}/metadata")]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendPatchPackageMetadataReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendPatchPackageMetadataReadModel>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendPatchPackageMetadataReadModel>), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendPatchPackageMetadataReadModel>> GetPatchPackageMetadata(
        string packageId,
        [FromQuery] bool compact = false) =>
        Read(
            () => _readApi.GetPatchPackageMetadata(packageId),
            () => _readApi.GetPatchPackageMetadataReadState(packageId),
            "packageId",
            compact);

    [HttpGet("patch-packages/{packageId}/artifacts")]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendPatchPackageArtifactsReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendPatchPackageArtifactsReadModel>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendPatchPackageArtifactsReadModel>), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendPatchPackageArtifactsReadModel>> GetPatchPackageArtifacts(
        string packageId,
        [FromQuery] bool compact = false) =>
        Read(
            () => _readApi.GetPatchPackageArtifacts(packageId),
            () => _readApi.GetPatchPackageArtifactsReadState(packageId),
            "packageId",
            compact);

    [HttpGet("validation-results/{validationResultId}/metadata")]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendValidationResultMetadataReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendValidationResultMetadataReadModel>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendValidationResultMetadataReadModel>), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendValidationResultMetadataReadModel>> GetValidationResultMetadata(
        string validationResultId,
        [FromQuery] bool compact = false) =>
        Read(
            () => _readApi.GetValidationResultMetadata(validationResultId),
            () => _readApi.GetValidationResultMetadataReadState(validationResultId),
            "validationResultId",
            compact);

    [HttpGet("evidence/{evidenceRef}/metadata")]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendEvidenceMetadataReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendEvidenceMetadataReadModel>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendEvidenceMetadataReadModel>), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendEvidenceMetadataReadModel>> GetEvidenceMetadata(
        string evidenceRef,
        [FromQuery] bool compact = false) =>
        Read(
            () => _readApi.GetEvidenceMetadata(evidenceRef),
            () => _readApi.GetEvidenceMetadataReadState(evidenceRef),
            "evidenceRef",
            compact);

    [HttpGet("receipts/{receiptRef}/metadata")]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendReceiptMetadataReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendReceiptMetadataReadModel>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(FrontendReadinessApiEnvelope<FrontendReceiptMetadataReadModel>), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<FrontendReadinessApiEnvelope<FrontendReceiptMetadataReadModel>> GetReceiptMetadata(
        string receiptRef,
        [FromQuery] bool compact = false) =>
        Read(
            () => _readApi.GetReceiptMetadata(receiptRef),
            () => _readApi.GetReceiptMetadataReadState(receiptRef),
            "receiptRef",
            compact);

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
        FrontendReadinessReadState readState,
        FrontendReadinessApiError? error = null,
        bool compactWarning = false) =>
        new()
        {
            Status = status,
            Data = data,
            ReadState = readState,
            Freshness = readState.Freshness,
            Boundary = FrontendReadBoundary.ReadOnlyStatus,
            MutationOccurred = false,
            Warnings = Warnings(compactWarning).Concat(readState.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Errors = error is null ? [] : [error]
        };

    private static ActionResult<FrontendReadinessApiEnvelope<TData>> Read<TData>(
        Func<TData?> readData,
        Func<FrontendReadinessReadState> readState,
        string field,
        bool compact)
        where TData : class
    {
        try
        {
            var data = readData();
            var state = readState();

            if (state.Kind == FrontendReadinessReadStateKind.Unavailable)
                return new ObjectResult(Envelope<TData>("unavailable", null, state, Error(field, state)))
                {
                    StatusCode = 503
                };

            if (data is null)
                return new NotFoundObjectResult(Envelope<TData>(StatusFor(state), null, state, Error(field, state), compact));

            return new OkObjectResult(Envelope("found", data, state, compactWarning: compact));
        }
        catch (Exception)
        {
            var state = FrontendReadinessReadState.Unavailable();
            return new ObjectResult(Envelope<TData>("unavailable", null, state, Error(field, state), compact))
            {
                StatusCode = 503
            };
        }
    }

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

    private static FrontendReadinessApiError Error(string field, FrontendReadinessReadState state) =>
        new()
        {
            Category = state.Kind.ToString(),
            Code = $"FRONTEND_READINESS_{state.Kind.ToString().ToUpperInvariant()}",
            Field = field,
            Message = state.Kind == FrontendReadinessReadStateKind.NotVisible
                ? "Requested frontend readiness data was not found or not visible."
                : state.Reasons.FirstOrDefault() ?? "Frontend readiness data was unavailable."
        };

    private static string StatusFor(FrontendReadinessReadState state) =>
        state.Kind switch
        {
            FrontendReadinessReadStateKind.NotVisible => "not_visible",
            FrontendReadinessReadStateKind.Unavailable => "unavailable",
            FrontendReadinessReadStateKind.Expired => "expired",
            FrontendReadinessReadStateKind.Stale => "stale",
            FrontendReadinessReadStateKind.Unknown => "unknown",
            _ => "not_found"
        };
}

public sealed record FrontendReadinessApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public required FrontendReadinessReadState ReadState { get; init; }
    public required FrontendReadinessFreshnessState Freshness { get; init; }
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
