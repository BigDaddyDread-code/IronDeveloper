using Dapper;
using IronDev.Core.Agents.Audit;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentRunAudit;

public sealed class SqlAgentRunAuditEnvelopeStore : IAgentRunAuditEnvelopeStore
{
    private const string IssueSeverityError = "error";
    private const string DuplicateConflict = "AGENT_RUN_AUDIT_DUPLICATE_CONFLICT";
    private const string InsertFailed = "AGENT_RUN_AUDIT_INSERT_FAILED";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly AgentRunAuditEnvelopeValidator _validator;

    public SqlAgentRunAuditEnvelopeStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new AgentRunAuditEnvelopeValidator())
    {
    }

    internal SqlAgentRunAuditEnvelopeStore(
        IDbConnectionFactory connectionFactory,
        AgentRunAuditEnvelopeValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public AgentRunAuditEnvelopeAppendResult Append(AgentRunAuditEnvelope envelope, DateTimeOffset appendedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var validationIssues = _validator.Validate(envelope);
        if (validationIssues.Count > 0)
        {
            return new AgentRunAuditEnvelopeAppendResult
            {
                Status = AgentRunAuditEnvelopeAppendStatus.Rejected,
                AgentRunId = envelope.Run?.AgentRunId ?? string.Empty,
                Issues = validationIssues.Select(issue => new AgentRunAuditEnvelopeStoreIssue
                {
                    Code = issue.Code,
                    Severity = issue.Severity,
                    Message = issue.Message
                }).ToArray()
            };
        }

        var json = AgentRunAuditEnvelopeJson.Serialize(envelope);
        var sha256 = AgentRunAuditEnvelopeJson.Sha256(json);
        var run = envelope.Run;
        var summary = AgentRunAuditDtoMapper.ToSafetySummary(envelope);

        using var connection = _connectionFactory.CreateConnection();
        var existing = connection.QuerySingleOrDefault<ExistingEnvelopeRow>(
            """
            SELECT EnvelopeSha256
            FROM agent.AgentRunAuditEnvelope
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND AgentRunId = @AgentRunId;
            """,
            new
            {
                run.TenantId,
                run.ProjectId,
                run.AgentRunId
            });

        if (existing is not null)
        {
            if (string.Equals(existing.EnvelopeSha256, sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new AgentRunAuditEnvelopeAppendResult
                {
                    Status = AgentRunAuditEnvelopeAppendStatus.AlreadyExists,
                    AgentRunId = run.AgentRunId,
                    EnvelopeSha256 = sha256,
                    Issues = []
                };
            }

            return new AgentRunAuditEnvelopeAppendResult
            {
                Status = AgentRunAuditEnvelopeAppendStatus.Conflict,
                AgentRunId = run.AgentRunId,
                EnvelopeSha256 = sha256,
                Issues =
                [
                    Issue(
                        DuplicateConflict,
                        "Agent run audit envelope already exists with a different SHA-256 hash.",
                        nameof(AgentRunAuditEnvelope.Run))
                ]
            };
        }

        try
        {
            connection.Execute(
                """
                INSERT INTO agent.AgentRunAuditEnvelope
                (
                    TenantId,
                    ProjectId,
                    CampaignId,
                    RunId,
                    AgentRunId,
                    AgentId,
                    AgentName,
                    AgentKind,
                    ExecutionMode,
                    Status,
                    TriggerType,
                    CreatedAtUtc,
                    CompletedAtUtc,
                    HasRawPrivateReasoning,
                    HasAuthorityClaim,
                    HasApprovalClaim,
                    HasMemoryPromotionClaim,
                    HasRuntimeActionOutput,
                    HasAuthorityCreatingOutput,
                    HasBlockedCapabilityAttempt,
                    HasBoundaryBlock,
                    EnvelopeSha256,
                    EnvelopeJson,
                    AppendedAtUtc
                )
                VALUES
                (
                    @TenantId,
                    @ProjectId,
                    @CampaignId,
                    @RunId,
                    @AgentRunId,
                    @AgentId,
                    @AgentName,
                    @AgentKind,
                    @ExecutionMode,
                    @Status,
                    @TriggerType,
                    @CreatedAtUtc,
                    @CompletedAtUtc,
                    @HasRawPrivateReasoning,
                    @HasAuthorityClaim,
                    @HasApprovalClaim,
                    @HasMemoryPromotionClaim,
                    @HasRuntimeActionOutput,
                    @HasAuthorityCreatingOutput,
                    @HasBlockedCapabilityAttempt,
                    @HasBoundaryBlock,
                    @EnvelopeSha256,
                    @EnvelopeJson,
                    @AppendedAtUtc
                );
                """,
                new
                {
                    run.TenantId,
                    run.ProjectId,
                    run.CampaignId,
                    run.RunId,
                    run.AgentRunId,
                    run.AgentId,
                    run.AgentName,
                    AgentKind = (int)envelope.AgentDefinitionSnapshot.Kind,
                    ExecutionMode = (int)envelope.AgentDefinitionSnapshot.ExecutionMode,
                    Status = (int)run.Status,
                    TriggerType = (int)run.TriggerType,
                    CreatedAtUtc = run.CreatedAtUtc.UtcDateTime,
                    CompletedAtUtc = run.CompletedAtUtc?.UtcDateTime,
                    HasRawPrivateReasoning = summary.ContainsRawPrivateReasoning,
                    HasAuthorityClaim = summary.HasAuthorityClaim,
                    HasApprovalClaim = summary.HasApprovalClaim,
                    HasMemoryPromotionClaim = summary.HasMemoryPromotionClaim,
                    HasRuntimeActionOutput = summary.HasRuntimeActionOutput,
                    HasAuthorityCreatingOutput = summary.HasAuthorityCreatingOutput,
                    HasBlockedCapabilityAttempt = summary.HasBlockedCapabilityAttempt,
                    HasBoundaryBlock = summary.HasBoundaryBlock,
                    EnvelopeSha256 = sha256,
                    EnvelopeJson = json,
                    AppendedAtUtc = appendedAtUtc.UtcDateTime
                });
        }
        catch (Exception ex)
        {
            return new AgentRunAuditEnvelopeAppendResult
            {
                Status = AgentRunAuditEnvelopeAppendStatus.Rejected,
                AgentRunId = run.AgentRunId,
                EnvelopeSha256 = sha256,
                Issues =
                [
                    Issue(InsertFailed, $"Agent run audit envelope could not be appended: {ex.Message}")
                ]
            };
        }

        return new AgentRunAuditEnvelopeAppendResult
        {
            Status = AgentRunAuditEnvelopeAppendStatus.Appended,
            AgentRunId = run.AgentRunId,
            EnvelopeSha256 = sha256,
            Issues = []
        };
    }

    private static AgentRunAuditEnvelopeStoreIssue Issue(string code, string message, string field = "") =>
        new()
        {
            Code = code,
            Severity = IssueSeverityError,
            Message = message,
            Field = field
        };

    private sealed class ExistingEnvelopeRow
    {
        public string EnvelopeSha256 { get; set; } = string.Empty;
    }
}
