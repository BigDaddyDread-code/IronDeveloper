using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class AcceptedApprovalQueryService : IAcceptedApprovalQueryService
{
    private const string RedactedPrivateReasoning = "[redacted: sensitive accepted approval text]";

    private static readonly string[] PrivateReasoningMarkers =
    [
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "hidden reasoning",
        "hidden deliberation",
        "private reasoning",
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "entire patch",
        "entirepatch"
    ];

    private readonly IAcceptedApprovalStore _store;

    public AcceptedApprovalQueryService(IAcceptedApprovalStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<AcceptedApprovalReadModel?> GetAsync(Guid projectId, Guid acceptedApprovalId, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(projectId, acceptedApprovalId, cancellationToken);
        return record is null ? null : ToReadModel(record);
    }

    public async Task<IReadOnlyList<AcceptedApprovalReadModel>> ListByTargetAsync(
        Guid projectId,
        string approvalTargetKind,
        string approvalTargetId,
        CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByTargetAsync(projectId, approvalTargetKind, approvalTargetId, cancellationToken);
        return records.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<AcceptedApprovalReadModel>> ListByProjectAndCorrelationAsync(
        Guid projectId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByProjectAndCorrelationAsync(projectId, correlationId, cancellationToken);
        return records.Select(ToReadModel).ToArray();
    }

    private static AcceptedApprovalReadModel ToReadModel(AcceptedApprovalRecord record)
    {
        var expiresAtUtc = record.ExpiresAtUtc;
        return new AcceptedApprovalReadModel(
            record.AcceptedApprovalId,
            record.ProjectId,
            SafeText(record.ApprovalTargetKind),
            SafeText(record.ApprovalTargetId),
            SafeText(record.ApprovalTargetHash),
            SafeText(record.CapabilityCode),
            SafeText(record.ApprovalPurpose),
            SafeText(record.ApprovedByActorId),
            SafeOptionalText(record.ApprovedByActorDisplayName),
            record.AcceptedAtUtc,
            expiresAtUtc,
            SafeText(record.CorrelationId),
            SafeText(record.CausationId),
            record.EvidenceReferences.Select(SafeText).ToArray(),
            record.BoundaryMaxims.Select(SafeText).ToArray(),
            expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTimeOffset.UtcNow,
            AcceptedApprovalReadBoundaryText.AuthorityBoundary,
            new AcceptedApprovalReadBoundary(),
            AcceptedApprovalReadBoundaryText.Warnings);
    }

    private static string? SafeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : SafeText(value);

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return PrivateReasoningMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? RedactedPrivateReasoning
            : value.Trim();
    }
}
