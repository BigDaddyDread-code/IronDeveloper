namespace IronDev.Core.Agents.Audit;

public enum AgentRunAuditEnvelopeAppendStatus
{
    Appended = 1,
    AlreadyExists = 2,
    Rejected = 3,
    Conflict = 4
}

public sealed record AgentRunAuditEnvelopeStoreIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record AgentRunAuditEnvelopeAppendResult
{
    public required AgentRunAuditEnvelopeAppendStatus Status { get; init; }
    public required string AgentRunId { get; init; }
    public string EnvelopeSha256 { get; init; } = string.Empty;
    public IReadOnlyList<AgentRunAuditEnvelopeStoreIssue> Issues { get; init; } = [];
}

public interface IAgentRunAuditEnvelopeStore
{
    AgentRunAuditEnvelopeAppendResult Append(AgentRunAuditEnvelope envelope, DateTimeOffset appendedAtUtc);
}
