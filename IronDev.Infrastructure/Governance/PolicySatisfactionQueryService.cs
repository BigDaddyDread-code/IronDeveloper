using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class PolicySatisfactionQueryService : IPolicySatisfactionQueryService
{
    private const string RedactedPrivateReasoning = "[redacted: sensitive policy satisfaction text]";

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

    private readonly IPolicySatisfactionStore _store;

    public PolicySatisfactionQueryService(IPolicySatisfactionStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<PolicySatisfactionReadModel?> GetAsync(Guid projectId, Guid policySatisfactionId, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(projectId, policySatisfactionId, cancellationToken);
        return record is null ? null : ToReadModel(record);
    }

    public async Task<IReadOnlyList<PolicySatisfactionReadModel>> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var records = await _store.ListBySubjectAsync(projectId, subjectKind, subjectId, cancellationToken);
        return records.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<PolicySatisfactionReadModel>> ListByAcceptedApprovalAsync(
        Guid projectId,
        Guid acceptedApprovalId,
        CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByAcceptedApprovalAsync(projectId, acceptedApprovalId, cancellationToken);
        return records.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<PolicySatisfactionReadModel>> ListByProjectAndCorrelationAsync(
        Guid projectId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByProjectAndCorrelationAsync(projectId, correlationId, cancellationToken);
        return records.Select(ToReadModel).ToArray();
    }

    private static PolicySatisfactionReadModel ToReadModel(PolicySatisfactionRecord record)
    {
        var expiresAtUtc = record.ExpiresAtUtc;
        return new PolicySatisfactionReadModel(
            record.PolicySatisfactionId,
            record.ProjectId,
            SafeText(record.PolicyCode),
            SafeText(record.PolicyVersion),
            SafeText(record.SubjectKind),
            SafeText(record.SubjectId),
            SafeText(record.SubjectHash),
            SafeText(record.CapabilityCode),
            record.AcceptedApprovalId,
            SafeText(record.ApprovalRequirementHash),
            record.ApprovalEvaluatedAtUtc,
            record.SatisfiedAtUtc,
            expiresAtUtc,
            SafeText(record.CorrelationId),
            SafeText(record.CausationId),
            record.EvidenceReferences.Select(SafeText).ToArray(),
            record.BoundaryMaxims.Select(SafeText).ToArray(),
            expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTimeOffset.UtcNow,
            PolicySatisfactionReadBoundaryText.AuthorityBoundary,
            new PolicySatisfactionReadBoundary(),
            PolicySatisfactionReadBoundaryText.Warnings);
    }

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
