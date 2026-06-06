using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data;
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
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            await PersistAsync(request, connection, transaction, cancellationToken).ConfigureAwait(false);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task PersistAsync(
        ChatTurnPersistenceRequest request,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            return;

        var envelope = ParseEnvelope(request.Tags);
        if (envelope is null)
            return;
        var clarification = NormalizeClarification(envelope.Clarification);

        const string deleteSql = """
            DELETE FROM dbo.ChatTurnTraces WHERE ChatMessageId = @ChatMessageId AND TenantId = @TenantId;
            DELETE FROM dbo.ChatTurnClarifications WHERE ChatMessageId = @ChatMessageId AND TenantId = @TenantId;
            DELETE FROM dbo.ChatTurnGovernance WHERE ChatMessageId = @ChatMessageId AND TenantId = @TenantId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            deleteSql,
            new { request.ChatMessageId, TenantId = _tenant.TenantId },
            transaction,
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
            transaction,
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
                Required = clarification.Required,
                Kind = clarification.Kind.ToString(),
                clarification.Reason,
                QuestionsJson = JsonSerializer.Serialize(clarification.Questions, JsonOptions)
            },
            transaction,
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
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<ChatTurnPersistenceSnapshot?> GetByMessageIdAsync(
        long chatMessageId,
        CancellationToken cancellationToken = default)
    {
        return await GetByMessageCoreAsync(null, null, chatMessageId, allowTagFallback: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatTurnPersistenceSnapshot?> GetByMessageAsync(
        int projectId,
        long chatSessionId,
        long chatMessageId,
        CancellationToken cancellationToken = default)
    {
        return await GetByMessageCoreAsync(projectId, chatSessionId, chatMessageId, allowTagFallback: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatTurnPersistenceSnapshot?> GetByMessageCoreAsync(
        int? projectId,
        long? chatSessionId,
        long chatMessageId,
        bool allowTagFallback,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

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
              AND g.TenantId = @TenantId
              AND (@ProjectId IS NULL OR g.ProjectId = @ProjectId)
              AND (@ChatSessionId IS NULL OR g.ChatSessionId = @ChatSessionId);
            """;

        var row = await connection.QuerySingleOrDefaultAsync<PersistedTurnRow>(new CommandDefinition(
            sql,
            new { ChatMessageId = chatMessageId, TenantId = _tenant.TenantId, ProjectId = projectId, ChatSessionId = chatSessionId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (row is null && allowTagFallback)
            return await GetTagFallbackByMessageCoreAsync(connection, projectId, chatSessionId, chatMessageId, cancellationToken)
                .ConfigureAwait(false);
        if (row is null)
            return null;

        return BuildSnapshot(row, isFallbackEvidence: false);
    }

    private async Task<ChatTurnPersistenceSnapshot?> GetTagFallbackByMessageCoreAsync(
        IDbConnection connection,
        int? projectId,
        long? chatSessionId,
        long chatMessageId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                Id AS ChatMessageId,
                Role,
                Tags,
                ContextSummary,
                LinkedFilePaths,
                LinkedSymbols
            FROM dbo.ChatMessages
            WHERE Id = @ChatMessageId
              AND TenantId = @TenantId
              AND (@ProjectId IS NULL OR ProjectId = @ProjectId)
              AND (@ChatSessionId IS NULL OR ChatSessionId = @ChatSessionId);
            """;

        var row = await connection.QuerySingleOrDefaultAsync<PersistedMessageRow>(new CommandDefinition(
            sql,
            new { ChatMessageId = chatMessageId, TenantId = _tenant.TenantId, ProjectId = projectId, ChatSessionId = chatSessionId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (row is null)
            return null;
        if (!string.Equals(row.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            return null;

        var envelope = ParseEnvelope(row.Tags);
        if (envelope is null)
            return null;

        return new ChatTurnPersistenceSnapshot(
            row.ChatMessageId,
            envelope.Mode,
            envelope.ModeConfidence,
            envelope.ModeReason,
            NormalizeClarification(envelope.Clarification),
            envelope.Gate,
            envelope.RouteTraceId,
            envelope.DogfoodTraceId,
            row.ContextSummary,
            row.LinkedFilePaths,
            row.LinkedSymbols,
            IsFallbackEvidence: true);
    }

    private static ChatTurnPersistenceSnapshot BuildSnapshot(PersistedTurnRow row, bool isFallbackEvidence)
    {
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

        var clarification = NormalizeClarification(row.Required, kind, questions, row.ClarificationReason);

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
            row.LinkedSymbols,
            isFallbackEvidence);
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

    private static ChatClarificationState NormalizeClarification(ChatClarificationState clarification) =>
        NormalizeClarification(
            clarification.Required,
            clarification.Kind,
            clarification.Questions,
            clarification.Reason);

    private static ChatClarificationState NormalizeClarification(
        bool required,
        ChatClarificationKind kind,
        IReadOnlyList<string> questions,
        string? reason)
    {
        var normalizedQuestions = questions
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .Select(question => question.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!required || (kind == ChatClarificationKind.None && normalizedQuestions.Count == 0))
            return ChatClarificationState.None;

        var normalizedKind = kind == ChatClarificationKind.None
            ? ChatClarificationKind.GeneralScope
            : kind;

        return new ChatClarificationState(
            true,
            normalizedKind,
            normalizedQuestions,
            string.IsNullOrWhiteSpace(reason) ? $"Clarification required for {normalizedKind}." : reason.Trim());
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

    private sealed class PersistedMessageRow
    {
        public long ChatMessageId { get; set; }
        public string Role { get; set; } = string.Empty;
        public string? Tags { get; set; }
        public string? ContextSummary { get; set; }
        public string? LinkedFilePaths { get; set; }
        public string? LinkedSymbols { get; set; }
    }
}
