using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchInputService : IWorkbenchInputService
{
    public const string HelpTitle = "Workbench commands";
    public const string HelpMessage =
        "Available commands:\n/help - Show this command menu.\n/ticket [instruction] - Prepare ticket proposals from the current project understanding.";
    public const string TicketTitle = "Ticket proposals";
    public const string TicketMessage =
        "Ticket proposal routing is ready. Proposal generation is enabled by the next Workbench slice.";
    public const string UnknownCommandMessage =
        "Unknown Workbench command. Use /help to see the available commands.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connections;
    private readonly IWorkbenchAgentRunService _agentRuns;

    public WorkbenchInputService(
        IDbConnectionFactory connections,
        IWorkbenchAgentRunService agentRuns)
    {
        _connections = connections;
        _agentRuns = agentRuns;
    }

    public async Task<DispatchWorkbenchInputResult> DispatchAsync(
        DispatchWorkbenchInputCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var route = WorkbenchInputRouter.Parse(command.ComposerText);
        if (route.Kind == WorkbenchInputKinds.Conversation)
        {
            if (command.ChatSessionId is not > 0)
                throw new WorkbenchInputValidationException(
                    "chatSessionId is required for ordinary Workbench conversation.");

            var agentRun = await _agentRuns.SubmitAsync(
                new SubmitWorkbenchAgentRunCommand(
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    command.ClientOperationId,
                    command.ChatSessionId.Value,
                    command.ComposerText),
                cancellationToken);
            return new DispatchWorkbenchInputResult(
                WorkbenchInputKinds.AgentRun,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                command.ClientOperationId,
                NormalizedCommand: null,
                Instruction: null,
                Title: null,
                Message: null,
                agentRun.IsReplay,
                AgentRun: agentRun);
        }

        return await DispatchCommandAsync(command, route, cancellationToken);
    }

    private async Task<DispatchWorkbenchInputResult> DispatchCommandAsync(
        DispatchWorkbenchInputCommand command,
        WorkbenchInputRoute route,
        CancellationToken cancellationToken)
    {
        var resourceScope = WorkbenchAgentRunService.BuildInputResourceScope(
            command.ProjectId,
            command.WorkbenchSessionId);
        var payloadHash = WorkbenchAgentRunService.ComputeInputPayloadHash(
            command.ProjectId,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            command.ChatSessionId,
            command.ComposerText);

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(
                    connection,
                    transaction,
                    command,
                    cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();

            var existing = await ReadOperationAsync(
                connection,
                transaction,
                command,
                resourceScope,
                cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectStartOperationMismatchException();
                var replay = ReadStoredResult(existing) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            if (!await ValidateAndRenewLeaseAsync(
                    connection,
                    transaction,
                    command,
                    cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var operationRecordId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT dbo.ClientOperations
                    (TenantId, ActorUserId, OperationKind, ResourceScopeId, ClientOperationId,
                     PayloadHash, Status)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ActorUserId, @OperationKind, @ResourceScopeId, @ClientOperationId,
                     @PayloadHash, N'Pending');
                """,
                new
                {
                    command.TenantId,
                    command.ActorUserId,
                    OperationKind = WorkbenchAgentRunOperationKinds.DispatchInput,
                    ResourceScopeId = resourceScope,
                    command.ClientOperationId,
                    PayloadHash = payloadHash
                },
                transaction,
                cancellationToken: cancellationToken));

            var rejected = route.Kind == WorkbenchInputKinds.CommandRejected;
            long? rejectionId = null;
            if (rejected)
            {
                rejectionId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                    """
                    INSERT dbo.WorkbenchCommandRejections
                        (TenantId, ProjectId, WorkbenchSessionId, ActorUserId, LeaseEpoch,
                         ClientOperationRecordId, ClientOperationId, RawCommandToken, PayloadHash,
                         ReasonCode)
                    OUTPUT inserted.Id
                    VALUES
                        (@TenantId, @ProjectId, @WorkbenchSessionId, @ActorUserId, @LeaseEpoch,
                         @ClientOperationRecordId, @ClientOperationId, @RawCommandToken, @PayloadHash,
                         @ReasonCode);
                    """,
                    new
                    {
                        command.TenantId,
                        command.ProjectId,
                        command.WorkbenchSessionId,
                        command.ActorUserId,
                        command.LeaseEpoch,
                        ClientOperationRecordId = operationRecordId,
                        command.ClientOperationId,
                        RawCommandToken = route.RawCommandToken,
                        PayloadHash = payloadHash,
                        ReasonCode = WorkbenchCommandRejectionReasons.UnknownCommand
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            var result = BuildCommandResult(command, route);
            var canonicalResultJson = JsonSerializer.Serialize(result, JsonOptions);
            var resultHash = WorkbenchAgentRunService.ComputeHash(canonicalResultJson);
            var eventPayload = JsonSerializer.Serialize(new
            {
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                command.ClientOperationId,
                kind = result.Kind,
                normalizedCommand = result.NormalizedCommand,
                rejectionId
            }, JsonOptions);
            var eventKind = rejected ? "WorkbenchCommandRejected" : "WorkbenchCommandHandled";
            var statusCode = rejected ? 400 : 200;

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchOutboxEvents
                    (EventId, TenantId, ProjectId, WorkbenchSessionId, EventKind, PayloadJson,
                     ClientOperationId, DedupeKey)
                VALUES
                    (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, @EventKind, @EventPayload,
                     @ClientOperationId, @DedupeKey);

                INSERT dbo.UserMutationAttribution
                    (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId, TimestampUtc,
                     SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
                VALUES
                    (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                     CONVERT(NVARCHAR(128), @ClientOperationId), NULL, SYSUTCDATETIME(),
                     N'Workbench', N'IronDev.Api', N'POST',
                     N'/api/workbench/projects/{projectId}/inputs', N'Completed', @StatusCode);

                UPDATE dbo.ClientOperations
                SET Status=N'Completed', ResultProjectId=@ProjectId,
                    ResultWorkbenchSessionId=@WorkbenchSessionId,
                    CanonicalResultJson=@CanonicalResultJson, ResultHash=@ResultHash,
                    CompletedAtUtc=SYSUTCDATETIME()
                WHERE Id=@ClientOperationRecordId;
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.ActorUserId,
                    command.ClientOperationId,
                    EventKind = eventKind,
                    EventPayload = eventPayload,
                    DedupeKey =
                        $"workbench-input:{command.TenantId}:{command.ProjectId}:{command.WorkbenchSessionId}:{command.ClientOperationId:D}",
                    StatusCode = statusCode,
                    CanonicalResultJson = canonicalResultJson,
                    ResultHash = resultHash,
                    ClientOperationRecordId = operationRecordId
                },
                transaction,
                cancellationToken: cancellationToken));

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static DispatchWorkbenchInputResult BuildCommandResult(
        DispatchWorkbenchInputCommand command,
        WorkbenchInputRoute route) => route.Kind switch
        {
            WorkbenchInputKinds.Help => new DispatchWorkbenchInputResult(
                WorkbenchInputKinds.Help,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                command.ClientOperationId,
                WorkbenchSlashCommands.Help,
                route.Instruction,
                HelpTitle,
                HelpMessage,
                IsReplay: false),
            WorkbenchInputKinds.Ticket => new DispatchWorkbenchInputResult(
                WorkbenchInputKinds.Ticket,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                command.ClientOperationId,
                WorkbenchSlashCommands.Ticket,
                route.Instruction,
                TicketTitle,
                TicketMessage,
                IsReplay: false),
            WorkbenchInputKinds.CommandRejected => new DispatchWorkbenchInputResult(
                WorkbenchInputKinds.CommandRejected,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                command.ClientOperationId,
                NormalizedCommand: null,
                Instruction: null,
                Title: null,
                UnknownCommandMessage,
                IsReplay: false,
                RawCommandToken: route.RawCommandToken,
                ReasonCode: WorkbenchCommandRejectionReasons.UnknownCommand),
            _ => throw new InvalidOperationException("The parsed Workbench command route is not supported.")
        };

    private static void Validate(DispatchWorkbenchInputCommand command)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty)
            throw new WorkbenchInputValidationException(
                "A current project, Workbench lease, and client operation ID are required.");
        if (string.IsNullOrWhiteSpace(command.ComposerText))
            throw new WorkbenchInputValidationException("composerText is required.");
        if (command.ComposerText.Length > WorkbenchBusinessAnalystProviderContract.MaximumConversationMessageCharacters)
            throw new WorkbenchInputValidationException(
                $"composerText exceeds the {WorkbenchBusinessAnalystProviderContract.MaximumConversationMessageCharacters} character limit.");
        if (command.ChatSessionId is <= 0)
            throw new WorkbenchInputValidationException("chatSessionId must be positive when supplied.");
    }

    private static async Task<bool> CanAccessProjectAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        DispatchWorkbenchInputCommand command,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.Projects project
            INNER JOIN dbo.ProjectMembers member
                ON member.TenantId=project.TenantId AND member.ProjectId=project.Id
               AND member.UserId=@ActorUserId AND member.Status=N'Active'
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=project.TenantId AND tenantMember.UserId=@ActorUserId
            INNER JOIN dbo.Users actor ON actor.Id=@ActorUserId AND actor.IsActive=1
            WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
            """,
            command,
            transaction,
            cancellationToken: cancellationToken)) > 0;

    private static async Task<bool> ValidateAndRenewLeaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        DispatchWorkbenchInputCommand command,
        CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE lease
            SET HeartbeatAtUtc=SYSUTCDATETIME(), ExpiresAtUtc=DATEADD(MINUTE, 30, SYSUTCDATETIME())
            FROM dbo.WorkbenchWriteLeases lease WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.WorkbenchSessions session
                ON session.TenantId=lease.TenantId AND session.ProjectId=lease.ProjectId
               AND session.Id=lease.WorkbenchSessionId AND session.Status=N'Active'
            INNER JOIN dbo.ProjectMembers member
                ON member.TenantId=lease.TenantId AND member.ProjectId=lease.ProjectId
               AND member.UserId=@ActorUserId AND member.Status=N'Active'
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=lease.TenantId AND tenantMember.UserId=@ActorUserId
            INNER JOIN dbo.Users actor ON actor.Id=@ActorUserId AND actor.IsActive=1
            WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
              AND lease.WorkbenchSessionId=@WorkbenchSessionId AND lease.LeaseEpoch=@LeaseEpoch
              AND lease.HolderActorUserId=@ActorUserId AND lease.RevokedAtUtc IS NULL
              AND lease.ExpiresAtUtc > SYSUTCDATETIME();
            """,
            command,
            transaction,
            cancellationToken: cancellationToken)) == 1;

    private static async Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        DispatchWorkbenchInputCommand command,
        string resourceScope,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
            """
            SELECT PayloadHash, CanonicalResultJson, ResultHash
            FROM dbo.ClientOperations WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
              AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
              AND ClientOperationId=@ClientOperationId;
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                OperationKind = WorkbenchAgentRunOperationKinds.DispatchInput,
                ResourceScopeId = resourceScope,
                command.ClientOperationId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static DispatchWorkbenchInputResult ReadStoredResult(ClientOperationRow row)
    {
        if (string.IsNullOrWhiteSpace(row.CanonicalResultJson) || string.IsNullOrWhiteSpace(row.ResultHash))
            throw new InvalidOperationException("The completed Workbench input operation has no canonical result.");
        if (!string.Equals(
                WorkbenchAgentRunService.ComputeHash(row.CanonicalResultJson),
                row.ResultHash,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stored Workbench input result failed its integrity check.");
        return JsonSerializer.Deserialize<DispatchWorkbenchInputResult>(row.CanonicalResultJson, JsonOptions)
               ?? throw new InvalidOperationException("The stored Workbench input result could not be read.");
    }

    private sealed class ClientOperationRow
    {
        public string PayloadHash { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }
}
