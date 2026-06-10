using Dapper;
using IronDev.Core.Agents.Audit;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentRunAudit;

public sealed class SqlAgentRunAuditEnvelopeReadRepository : IAgentRunAuditEnvelopeReadRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlAgentRunAuditEnvelopeReadRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public IReadOnlyList<AgentRunAuditEnvelope> List(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return [];

        using var connection = _connectionFactory.CreateConnection();
        var rows = connection.Query<string>(
            """
            SELECT EnvelopeJson
            FROM agent.AgentRunAuditEnvelope
            WHERE ProjectId = @ProjectId
            ORDER BY CreatedAtUtc DESC, AgentRunId ASC;
            """,
            new { ProjectId = projectId });

        return rows
            .Select(AgentRunAuditEnvelopeJson.Deserialize)
            .Where(envelope => envelope is not null)
            .Cast<AgentRunAuditEnvelope>()
            .ToArray();
    }

    public AgentRunAuditEnvelope? Get(string projectId, string agentRunId)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(agentRunId))
            return null;

        using var connection = _connectionFactory.CreateConnection();
        var json = connection.QuerySingleOrDefault<string>(
            """
            SELECT EnvelopeJson
            FROM agent.AgentRunAuditEnvelope
            WHERE ProjectId = @ProjectId
              AND AgentRunId = @AgentRunId;
            """,
            new
            {
                ProjectId = projectId,
                AgentRunId = agentRunId
            });

        return string.IsNullOrWhiteSpace(json)
            ? null
            : AgentRunAuditEnvelopeJson.Deserialize(json);
    }
}
