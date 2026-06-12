using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Api.Controllers;

public sealed class SqlToolRequestApiStore : IToolRequestApiStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IToolRequestStore _toolRequestStore;
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlToolRequestApiStore(IToolRequestStore toolRequestStore, IDbConnectionFactory connectionFactory)
    {
        _toolRequestStore = toolRequestStore ?? throw new ArgumentNullException(nameof(toolRequestStore));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public ToolRequestApiStoreSaveResult Save(ToolRequestApiStoredRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var durableId = DurableToolRequestId(record.ToolRequest.Scope.TenantId, record.ToolRequest.Scope.ProjectId, record.ToolRequest.ToolRequestId);
        var existing = _toolRequestStore.GetAsync(durableId).GetAwaiter().GetResult();
        if (existing is not null)
            return new ToolRequestApiStoreSaveResult { Created = false };

        var payloadRecord = record with { Warnings = [] };
        var payloadJson = JsonSerializer.Serialize(payloadRecord, JsonOptions);
        _ = _toolRequestStore.CreateAsync(new ToolRequestCreateRequest
        {
            ToolRequestId = durableId,
            ProjectId = ProjectScopeId(record.ToolRequest.Scope.TenantId, record.ToolRequest.Scope.ProjectId),
            ToolName = record.ToolRequest.ToolKind.ToString(),
            OperationName = record.ToolRequest.RequestType.ToString(),
            RequestedByActorType = record.ToolRequest.Actor.AgentKind.ToString(),
            RequestedByActorId = record.ToolRequest.Actor.AgentId,
            CorrelationId = CorrelationScopeId(record.ToolRequest.Scope.CorrelationId),
            CausationId = null,
            Purpose = record.ToolRequest.Purpose,
            RequestPayloadVersion = 1,
            RequestPayloadJson = payloadJson
        }).GetAwaiter().GetResult();

        return new ToolRequestApiStoreSaveResult { Created = true };
    }

    public ToolRequestApiStoredRecord? Get(string tenantId, string projectId, string toolRequestId)
    {
        var durableId = DurableToolRequestId(tenantId, projectId, toolRequestId);
        var read = _toolRequestStore.GetAsync(durableId).GetAwaiter().GetResult();
        if (read is null)
            return null;

        try
        {
            var record = JsonSerializer.Deserialize<ToolRequestApiStoredRecord>(read.RequestPayloadJson, JsonOptions);
            return record is null
                ? null
                : record with { CreatedAtUtc = read.CreatedUtc };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public int Count()
    {
        using var connection = _connectionFactory.CreateConnection();
        return connection.ExecuteScalar<int>(
            new CommandDefinition(
                """
                IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL
                    SELECT 0;
                ELSE
                    SELECT COUNT(*) FROM governance.ToolRequest;
                """,
                commandType: CommandType.Text));
    }

    private static Guid DurableToolRequestId(string tenantId, string projectId, string toolRequestId) =>
        StableGuid($"tool-request::{tenantId}::{projectId}::{toolRequestId}");

    private static Guid ProjectScopeId(string tenantId, string projectId) =>
        StableGuid($"project::{tenantId}::{projectId}");

    private static Guid CorrelationScopeId(string correlationId) =>
        Guid.TryParse(correlationId, out var parsed)
            ? parsed
            : StableGuid($"correlation::{correlationId}");

    private static Guid StableGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var bytes = hash.Take(16).ToArray();
        return new Guid(bytes);
    }
}
