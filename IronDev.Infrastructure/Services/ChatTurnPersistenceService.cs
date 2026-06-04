using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class ChatTurnPersistenceService : IChatTurnPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public ChatTurnPersistenceService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task PersistAsync(
        ChatTurnPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            return;

        var envelope = ParseEnvelope(request.Tags);
        if (envelope is null)
            return;

        using var connection = _connectionFactory.CreateConnection();
        await EnsureTablesAsync(connection, cancellationToken).ConfigureAwait(false);

        const string deleteSql = """
            DELETE FROM dbo.ChatTurnTraces WHERE ChatMessageId = @ChatMessageId AND TenantId = @TenantId;
            DELETE FROM dbo.ChatTurnClarifications WHERE ChatMessageId = @ChatMessageId AND TenantId = @TenantId;
            DELETE FROM dbo.ChatTurnGovernance WHERE ChatMessageId = @ChatMessageId AND TenantId = @TenantId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            deleteSql,
            new { request.ChatMessageId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        const string governanceSql = """
            INSERT INTO dbo.ChatTurnGovernance
                (TenantId, ProjectId, ChatSessionId, ChatMessageId, Mode, ModeConfidence, ModeReason, GateJson)
            VALUES
                (@TenantId, @ProjectId, @ChatSessionId, @ChatMessageId, @Mode, @ModeConfidence, @ModeReason, @GateJson);
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            governanceSql,
            new
            {
                TenantId = _tenant.TenantId,
                request.ProjectId,
                request.ChatSessionId,
                request.ChatMessageId,
                Mode = envelope.Mode.ToString(),
                envelope.ModeConfidence,
                ModeReason = envelope.ModeReason,
                GateJson = JsonSerializer.Serialize(envelope.Gate, JsonOptions)
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        const string clarificationSql = """
            INSERT INTO dbo.ChatTurnClarifications
                (TenantId, ProjectId, ChatSessionId, ChatMessageId, Required, Kind, Reason, QuestionsJson)
            VALUES
                (@TenantId, @ProjectId, @ChatSessionId, @ChatMessageId, @Required, @Kind, @Reason, @QuestionsJson);
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            clarificationSql,
            new
            {
                TenantId = _tenant.TenantId,
                request.ProjectId,
                request.ChatSessionId,
                request.ChatMessageId,
                Required = envelope.Clarification.Required,
                Kind = envelope.Clarification.Kind.ToString(),
                envelope.Clarification.Reason,
                QuestionsJson = JsonSerializer.Serialize(envelope.Clarification.Questions, JsonOptions)
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        const string traceSql = """
            INSERT INTO dbo.ChatTurnTraces
                (TenantId, ProjectId, ChatSessionId, ChatMessageId, RouteTraceId, DogfoodTraceId, ContextSummary, LinkedFilePaths, LinkedSymbols)
            VALUES
                (@TenantId, @ProjectId, @ChatSessionId, @ChatMessageId, @RouteTraceId, @DogfoodTraceId, @ContextSummary, @LinkedFilePaths, @LinkedSymbols);
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            traceSql,
            new
            {
                TenantId = _tenant.TenantId,
                request.ProjectId,
                request.ChatSessionId,
                request.ChatMessageId,
                envelope.RouteTraceId,
                envelope.DogfoodTraceId,
                request.ContextSummary,
                request.LinkedFilePaths,
                request.LinkedSymbols
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<ChatTurnPersistenceSnapshot?> GetByMessageIdAsync(
        long chatMessageId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await EnsureTablesAsync(connection, cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT
                g.ChatMessageId,
                g.Mode,
                g.ModeConfidence,
                g.ModeReason,
                g.GateJson,
                c.Required,
                c.Kind,
                c.Reason AS ClarificationReason,
                c.QuestionsJson,
                t.RouteTraceId,
                t.DogfoodTraceId,
                t.ContextSummary,
                t.LinkedFilePaths,
                t.LinkedSymbols
            FROM dbo.ChatTurnGovernance g
            LEFT JOIN dbo.ChatTurnClarifications c
                ON c.ChatMessageId = g.ChatMessageId AND c.TenantId = g.TenantId
            LEFT JOIN dbo.ChatTurnTraces t
                ON t.ChatMessageId = g.ChatMessageId AND t.TenantId = g.TenantId
            WHERE g.ChatMessageId = @ChatMessageId
              AND g.TenantId = @TenantId;
            """;

        var row = await connection.QuerySingleOrDefaultAsync<PersistedTurnRow>(new CommandDefinition(
            sql,
            new { ChatMessageId = chatMessageId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (row is null)
            return null;

        var mode = Enum.TryParse<ChatGovernanceMode>(row.Mode, ignoreCase: true, out var parsedMode)
            ? parsedMode
            : ChatGovernanceMode.Confirmation;
        var kind = Enum.TryParse<ChatClarificationKind>(row.Kind, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : ChatClarificationKind.None;
        var gate = string.IsNullOrWhiteSpace(row.GateJson)
            ? ChatGovernanceGate.FromDecision(new ChatModeDecision(mode, row.ModeConfidence, row.ModeReason ?? string.Empty))
            : JsonSerializer.Deserialize<ChatGovernanceGate>(row.GateJson, JsonOptions)
                ?? ChatGovernanceGate.FromDecision(new ChatModeDecision(mode, row.ModeConfidence, row.ModeReason ?? string.Empty));
        var questions = string.IsNullOrWhiteSpace(row.QuestionsJson)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(row.QuestionsJson, JsonOptions) ?? Array.Empty<string>();

        var clarification = row.Required
            ? new ChatClarificationState(true, kind, questions, row.ClarificationReason)
            : ChatClarificationState.None;

        return new ChatTurnPersistenceSnapshot(
            row.ChatMessageId,
            mode,
            row.ModeConfidence,
            row.ModeReason ?? string.Empty,
            clarification,
            gate,
            row.RouteTraceId,
            row.DogfoodTraceId,
            row.ContextSummary,
            row.LinkedFilePaths,
            row.LinkedSymbols);
    }

    private static ChatTurnEnvelope? ParseEnvelope(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<ChatTurnEnvelope>(tags, JsonOptions);
            return envelope?.V == 1 ? envelope : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task EnsureTablesAsync(
        System.Data.IDbConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID('dbo.ChatTurnGovernance', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChatTurnGovernance
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    ChatSessionId BIGINT NOT NULL,
                    ChatMessageId BIGINT NOT NULL,
                    Mode NVARCHAR(50) NOT NULL,
                    ModeConfidence FLOAT NOT NULL,
                    ModeReason NVARCHAR(MAX) NOT NULL,
                    GateJson NVARCHAR(MAX) NOT NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnGovernance_CreatedUtc DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX UX_ChatTurnGovernance_MessageTenant ON dbo.ChatTurnGovernance(ChatMessageId, TenantId);
            END

            IF OBJECT_ID('dbo.ChatTurnClarifications', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChatTurnClarifications
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    ChatSessionId BIGINT NOT NULL,
                    ChatMessageId BIGINT NOT NULL,
                    Required BIT NOT NULL,
                    Kind NVARCHAR(100) NOT NULL,
                    Reason NVARCHAR(MAX) NULL,
                    QuestionsJson NVARCHAR(MAX) NOT NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnClarifications_CreatedUtc DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX UX_ChatTurnClarifications_MessageTenant ON dbo.ChatTurnClarifications(ChatMessageId, TenantId);
            END

            IF OBJECT_ID('dbo.ChatTurnTraces', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChatTurnTraces
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    ChatSessionId BIGINT NOT NULL,
                    ChatMessageId BIGINT NOT NULL,
                    RouteTraceId NVARCHAR(200) NULL,
                    DogfoodTraceId NVARCHAR(200) NULL,
                    ContextSummary NVARCHAR(MAX) NULL,
                    LinkedFilePaths NVARCHAR(MAX) NULL,
                    LinkedSymbols NVARCHAR(MAX) NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnTraces_CreatedUtc DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX UX_ChatTurnTraces_MessageTenant ON dbo.ChatTurnTraces(ChatMessageId, TenantId);
            END
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed class PersistedTurnRow
    {
        public long ChatMessageId { get; set; }
        public string Mode { get; set; } = string.Empty;
        public double ModeConfidence { get; set; }
        public string? ModeReason { get; set; }
        public string? GateJson { get; set; }
        public bool Required { get; set; }
        public string? Kind { get; set; }
        public string? ClarificationReason { get; set; }
        public string? QuestionsJson { get; set; }
        public string? RouteTraceId { get; set; }
        public string? DogfoodTraceId { get; set; }
        public string? ContextSummary { get; set; }
        public string? LinkedFilePaths { get; set; }
        public string? LinkedSymbols { get; set; }
    }
}
