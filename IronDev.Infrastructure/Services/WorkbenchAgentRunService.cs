using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchAgentRunService : IWorkbenchAgentRunService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ChatEnvelopeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IDbConnectionFactory _connections;
    private readonly IChatTurnPersistenceService _turnPersistence;
    private readonly IWorkbenchAgentRunFailureInjector _failureInjector;
    private readonly IWorkbenchAgentRunSubmissionAvailability _availability;

    public WorkbenchAgentRunService(
        IDbConnectionFactory connections,
        IChatTurnPersistenceService turnPersistence,
        IWorkbenchAgentRunFailureInjector? failureInjector = null,
        IWorkbenchAgentRunSubmissionAvailability? availability = null)
    {
        _connections = connections;
        _turnPersistence = turnPersistence;
        _failureInjector = failureInjector ?? new NoOpWorkbenchAgentRunFailureInjector();
        _availability = availability ?? new UnavailableWorkbenchAgentRunSubmissionAvailability();
    }

    public async Task<WorkbenchAgentRunSubmissionAvailability> GetSubmissionAvailabilityAsync(
        int tenantId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var availability = await _availability.CheckAsync(tenantId, projectId, cancellationToken)
            .ConfigureAwait(false);
        return availability.IsAvailable
            ? WorkbenchAgentRunSubmissionAvailability.Available
            : new WorkbenchAgentRunSubmissionAvailability(
                false,
                availability.FailureCategory ?? WorkbenchAgentRunFailureCategories.ServiceUnavailable);
    }

    public async Task<SubmitWorkbenchAgentRunResult> SubmitAsync(
        SubmitWorkbenchAgentRunCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateSubmit(command);

        var resourceScope =
            $"project:{command.ProjectId}:workbench-session:{command.WorkbenchSessionId}:chat:{command.ChatSessionId}:purpose:{command.InvocationKind}:proposal-set:{command.TicketProposalSetId?.ToString("D") ?? "new"}";
        var payloadHash = ComputeHash(JsonSerializer.Serialize(new
        {
            v = 2,
            command.ProjectId,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            command.ChatSessionId,
            message = command.Message,
            command.InvocationKind,
            command.TicketInstruction,
            command.TicketProposalSetId,
            command.ExpectedTicketProposalRevision
        }, JsonOptions));

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            if (!await CanAccessProjectAsync(connection, transaction, command.TenantId, command.ActorUserId, command.ProjectId, cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();

            var existingOperation = await ReadOperationAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                WorkbenchAgentRunOperationKinds.Submit,
                resourceScope,
                command.ClientOperationId,
                cancellationToken);

            if (existingOperation is not null)
            {
                EnsureMatchingOperation(existingOperation, payloadHash);
                var replay = ReadStoredResult<SubmitWorkbenchAgentRunResult>(existingOperation) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            if (!await IsLeaseCurrentAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var availability = await GetSubmissionAvailabilityAsync(
                command.TenantId,
                command.ProjectId,
                cancellationToken).ConfigureAwait(false);
            if (!availability.IsAvailable)
                throw new WorkbenchAgentRunUnavailableException(
                    availability.FailureCategory ?? WorkbenchAgentRunFailureCategories.ServiceUnavailable);

            if (!await ValidateAndRenewLeaseAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    cancellationToken))
                throw new WorkbenchLeaseFenceException();

            if (command.InvocationKind == WorkbenchAgentInvocationKinds.TicketProposalRegeneration)
            {
                var proposalSet = await connection.QuerySingleOrDefaultAsync<ExistingTicketProposalSetRow>(new CommandDefinition(
                    """
                    SELECT Id, CurrentRevision, CreatedByAgentRunId, CreatedAtUtc
                    FROM dbo.TicketProposalSets WITH (UPDLOCK, HOLDLOCK)
                    WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@TicketProposalSetId
                      AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                    """,
                    command,
                    transaction,
                    cancellationToken: cancellationToken));
                if (proposalSet is null)
                    throw new WorkbenchProjectNotAccessibleException();
                if (proposalSet.CurrentRevision != command.ExpectedTicketProposalRevision)
                    throw new TicketProposalRevisionConflictException(proposalSet.CurrentRevision);
            }

            var chatSessionExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.ProjectChatSessions WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@ChatSessionId;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));
            if (chatSessionExists == 0)
                throw new WorkbenchAgentRunValidationException("The selected chat session does not belong to this project.");

            var conversationBound = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.WorkbenchSessions WITH (UPDLOCK, HOLDLOCK)
                SET ActiveChatSessionId=COALESCE(ActiveChatSessionId, @ChatSessionId)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@WorkbenchSessionId
                  AND Status=N'Active'
                  AND (ActiveChatSessionId IS NULL OR ActiveChatSessionId=@ChatSessionId);
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));
            if (conversationBound != 1)
                throw new WorkbenchChatSessionBindingException();

            var activeRunId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                """
                SELECT TOP (1) AgentRunId
                FROM dbo.WorkbenchAgentRuns WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND ActiveRunSlot=1;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));
            if (activeRunId.HasValue)
                throw new WorkbenchAgentRunAlreadyActiveException(activeRunId.Value);

            var agentRunId = Guid.NewGuid();
            var createdAtUtc = DateTime.UtcNow;
            var ticketProposalSetId = command.InvocationKind == WorkbenchAgentInvocationKinds.TicketProposalGeneration
                ? Guid.NewGuid()
                : command.TicketProposalSetId;
            var proposalPurpose = WorkbenchAgentInvocationKinds.IsTicketProposal(command.InvocationKind);
            var promptVersion = proposalPurpose
                ? WorkbenchBusinessAnalystContract.PromptVersion3
                : WorkbenchBusinessAnalystContract.PromptVersion2;
            var contextSchemaVersion = proposalPurpose
                ? WorkbenchBusinessAnalystContract.ContextSchemaVersion3
                : WorkbenchBusinessAnalystContract.ContextSchemaVersion2;
            var contextCanonicalizationVersion = proposalPurpose
                ? WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion3
                : WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion2;
            var outputSchemaVersion = proposalPurpose
                ? WorkbenchBusinessAnalystContract.OutputSchemaVersion3
                : WorkbenchBusinessAnalystContract.OutputSchemaVersion2;

            var clientOperationRecordId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT dbo.ClientOperations
                    (TenantId, ActorUserId, OperationKind, ResourceScopeId, ClientOperationId, PayloadHash, Status)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ActorUserId, @OperationKind, @ResourceScopeId, @ClientOperationId, @PayloadHash, N'Pending');
                """,
                new
                {
                    command.TenantId,
                    command.ActorUserId,
                    OperationKind = WorkbenchAgentRunOperationKinds.Submit,
                    ResourceScopeId = resourceScope,
                    command.ClientOperationId,
                    PayloadHash = payloadHash
                },
                transaction,
                cancellationToken: cancellationToken));

            var userMessageId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT dbo.ChatMessages
                    (TenantId, ProjectId, ChatSessionId, Role, Message, CreatedDate)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ProjectId, @ChatSessionId, N'user', @Message, @CreatedAtUtc);

                UPDATE dbo.ProjectChatSessions
                SET UpdatedDate=@CreatedAtUtc
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@ChatSessionId;
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    command.ChatSessionId,
                    command.Message,
                    CreatedAtUtc = createdAtUtc
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(WorkbenchAgentRunFailurePoint.UserMessagePersisted);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchAgentRuns
                    (AgentRunId, TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch, ActorUserId,
                     ChatSessionId, SourceUserMessageId, ClientOperationRecordId, ClientOperationId,
                     AgentVersion, PromptVersion, ToolPolicyVersion, ContextSchemaVersion,
                     ContextCanonicalizationVersion, OutputSchemaVersion, InvocationKind,
                     TicketInstruction, TicketProposalSetId, TicketProposalRevision,
                     Status, ActiveRunSlot, CreatedAtUtc)
                VALUES
                    (@AgentRunId, @TenantId, @ProjectId, @WorkbenchSessionId, @LeaseEpoch, @ActorUserId,
                     @ChatSessionId, @SourceUserMessageId, @ClientOperationRecordId, @ClientOperationId,
                     @AgentVersion, @PromptVersion, @ToolPolicyVersion, @ContextSchemaVersion,
                     @ContextCanonicalizationVersion, @OutputSchemaVersion, @InvocationKind,
                     @TicketInstruction, @TicketProposalSetId, @TicketProposalRevision,
                     N'Pending', 1, @CreatedAtUtc);
                """,
                new
                {
                    AgentRunId = agentRunId,
                    command.TenantId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    command.ActorUserId,
                    command.ChatSessionId,
                    SourceUserMessageId = userMessageId,
                    ClientOperationRecordId = clientOperationRecordId,
                    command.ClientOperationId,
                    AgentVersion = WorkbenchBusinessAnalystContract.AgentVersion,
                    PromptVersion = promptVersion,
                    ToolPolicyVersion = WorkbenchBusinessAnalystContract.ToolPolicyVersion,
                    ContextSchemaVersion = contextSchemaVersion,
                    ContextCanonicalizationVersion = contextCanonicalizationVersion,
                    OutputSchemaVersion = outputSchemaVersion,
                    command.InvocationKind,
                    command.TicketInstruction,
                    TicketProposalSetId = ticketProposalSetId,
                    TicketProposalRevision = command.ExpectedTicketProposalRevision,
                    CreatedAtUtc = createdAtUtc
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(WorkbenchAgentRunFailurePoint.AgentRunCreated);

            var result = new SubmitWorkbenchAgentRunResult(
                agentRunId,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                command.ChatSessionId,
                userMessageId,
                WorkbenchAgentRunStates.Pending,
                command.ClientOperationId,
                createdAtUtc,
                IsReplay: false,
                command.InvocationKind,
                ticketProposalSetId,
                command.ExpectedTicketProposalRevision);
            var canonicalResultJson = JsonSerializer.Serialize(result, JsonOptions);
            var resultHash = ComputeHash(canonicalResultJson);
            var eventPayload = JsonSerializer.Serialize(new
            {
                agentRunId,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                command.ClientOperationId
                ,command.InvocationKind
                ,ticketProposalSetId
            }, JsonOptions);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchOutboxEvents
                    (EventId, TenantId, ProjectId, WorkbenchSessionId, AgentRunId, EventKind,
                     PayloadJson, ClientOperationId, DedupeKey)
                VALUES
                    (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, @AgentRunId, N'AgentRunRequested',
                     @EventPayload, @ClientOperationId, @DedupeKey);

                INSERT dbo.UserMutationAttribution
                    (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId, TimestampUtc,
                     SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
                VALUES
                    (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                     CONVERT(NVARCHAR(128), @ClientOperationId), NULL, SYSUTCDATETIME(),
                     N'Workbench', N'IronDev.Api', N'POST',
                     N'/api/workbench/projects/{projectId}/agent-runs', N'Completed', 202);

                UPDATE dbo.ClientOperations
                SET Status=N'Completed', ResultProjectId=@ProjectId,
                    ResultWorkbenchSessionId=@WorkbenchSessionId, ResultAgentRunId=@AgentRunId,
                    CanonicalResultJson=@CanonicalResultJson, ResultHash=@ResultHash,
                    CompletedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
                  AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
                  AND ClientOperationId=@ClientOperationId;
                """,
                new
                {
                    AgentRunId = agentRunId,
                    command.TenantId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.ActorUserId,
                    command.ClientOperationId,
                    EventPayload = eventPayload,
                    DedupeKey = $"agent-run-requested:{agentRunId:D}",
                    CanonicalResultJson = canonicalResultJson,
                    ResultHash = resultHash,
                    OperationKind = WorkbenchAgentRunOperationKinds.Submit,
                    ResourceScopeId = resourceScope
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(WorkbenchAgentRunFailurePoint.OutboxEnqueued);

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<CancelWorkbenchAgentRunResult> CancelAsync(
        CancelWorkbenchAgentRunCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 || command.AgentRunId == Guid.Empty ||
            command.ClientOperationId == Guid.Empty)
            throw new WorkbenchAgentRunValidationException("A current project, Workbench lease, agent run, and client operation ID are required.");

        var resourceScope = $"agent-run:{command.AgentRunId}";
        var payloadHash = ComputeHash($"workbench-agent-run-cancel-v1\n{command.ProjectId}\n{command.WorkbenchSessionId}\n{command.LeaseEpoch}\n{command.AgentRunId:D}");
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            if (!await CanAccessProjectAsync(connection, transaction, command.TenantId, command.ActorUserId, command.ProjectId, cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();

            var existingOperation = await ReadOperationAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                WorkbenchAgentRunOperationKinds.Cancel,
                resourceScope,
                command.ClientOperationId,
                cancellationToken);
            if (existingOperation is not null)
            {
                EnsureMatchingOperation(existingOperation, payloadHash);
                var replay = ReadStoredResult<CancelWorkbenchAgentRunResult>(existingOperation) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            if (!await ValidateAndRenewLeaseAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var run = await connection.QuerySingleOrDefaultAsync<AgentRunRow>(new CommandDefinition(
                """
                SELECT *
                FROM dbo.WorkbenchAgentRuns WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND AgentRunId=@AgentRunId;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));
            if (run is null)
                throw new WorkbenchAgentRunNotFoundException();

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.ClientOperations
                    (TenantId, ActorUserId, OperationKind, ResourceScopeId, ClientOperationId, PayloadHash, Status)
                VALUES
                    (@TenantId, @ActorUserId, @OperationKind, @ResourceScopeId, @ClientOperationId, @PayloadHash, N'Pending');
                """,
                new
                {
                    command.TenantId,
                    command.ActorUserId,
                    OperationKind = WorkbenchAgentRunOperationKinds.Cancel,
                    ResourceScopeId = resourceScope,
                    command.ClientOperationId,
                    PayloadHash = payloadHash
                },
                transaction,
                cancellationToken: cancellationToken));

            var cancellationRequested = run.Status is WorkbenchAgentRunStates.Pending or WorkbenchAgentRunStates.Running;
            var status = cancellationRequested ? WorkbenchAgentRunStates.Cancelled : run.Status;
            if (cancellationRequested)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.WorkbenchAgentRuns
                    SET Status=N'Cancelled', CancellationRequestedAtUtc=COALESCE(CancellationRequestedAtUtc, SYSUTCDATETIME()),
                        CompletedAtUtc=COALESCE(CompletedAtUtc, SYSUTCDATETIME()), ClaimExpiresAtUtc=NULL,
                        ActiveRunSlot=NULL
                    WHERE AgentRunId=@AgentRunId;
                    """,
                    new { command.AgentRunId },
                    transaction,
                    cancellationToken: cancellationToken));

                await CloseUnfinishedAttemptAsync(
                    connection,
                    transaction,
                    command.AgentRunId,
                    "Cancelled",
                    "run_cancelled_before_result",
                    cancellationToken);
            }

            var result = new CancelWorkbenchAgentRunResult(
                command.AgentRunId,
                status,
                cancellationRequested,
                command.ClientOperationId,
                IsReplay: false);
            var canonicalResultJson = JsonSerializer.Serialize(result, JsonOptions);
            var resultHash = ComputeHash(canonicalResultJson);
            var eventPayload = JsonSerializer.Serialize(new
            {
                command.AgentRunId,
                status,
                cancellationRequested,
                command.ClientOperationId
            }, JsonOptions);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                IF @CancellationRequested = 1
                BEGIN
                    INSERT dbo.WorkbenchOutboxEvents
                        (EventId, TenantId, ProjectId, WorkbenchSessionId, AgentRunId, EventKind,
                         PayloadJson, ClientOperationId, DedupeKey)
                    VALUES
                        (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, @AgentRunId,
                         N'AgentRunCancelled', @EventPayload, @ClientOperationId, @DedupeKey);
                END;

                UPDATE dbo.ClientOperations
                SET Status=N'Completed', ResultProjectId=@ProjectId,
                    ResultWorkbenchSessionId=@WorkbenchSessionId, ResultAgentRunId=@AgentRunId,
                    CanonicalResultJson=@CanonicalResultJson, ResultHash=@ResultHash,
                    CompletedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
                  AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
                  AND ClientOperationId=@ClientOperationId;
                """,
                new
                {
                    command.AgentRunId,
                    command.TenantId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.ActorUserId,
                    command.ClientOperationId,
                    CancellationRequested = cancellationRequested,
                    EventPayload = eventPayload,
                    DedupeKey = $"agent-run-cancelled:{command.AgentRunId:D}:{command.ClientOperationId:D}",
                    CanonicalResultJson = canonicalResultJson,
                    ResultHash = resultHash,
                    OperationKind = WorkbenchAgentRunOperationKinds.Cancel,
                    ResourceScopeId = resourceScope
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

    public async Task<WorkbenchAgentRunSnapshot> GetAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        Guid agentRunId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || actorUserId <= 0 || projectId <= 0 || agentRunId == Guid.Empty)
            throw new WorkbenchAgentRunValidationException("A selected project and agent run are required.");

        using var connection = _connections.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<AgentRunRow>(new CommandDefinition(
            """
            SELECT run.*
            FROM dbo.WorkbenchAgentRuns run
            INNER JOIN dbo.ProjectMembers member
                ON member.TenantId=run.TenantId AND member.ProjectId=run.ProjectId
               AND member.UserId=@ActorUserId AND member.Status=N'Active'
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=run.TenantId AND tenantMember.UserId=@ActorUserId
            INNER JOIN dbo.Users actor ON actor.Id=@ActorUserId AND actor.IsActive=1
            WHERE run.TenantId=@TenantId AND run.ProjectId=@ProjectId AND run.AgentRunId=@AgentRunId;
            """,
            new { TenantId = tenantId, ActorUserId = actorUserId, ProjectId = projectId, AgentRunId = agentRunId },
            cancellationToken: cancellationToken));
        if (row is null)
            throw new WorkbenchAgentRunNotFoundException();
        return ToSnapshot(row);
    }

    public async Task<WorkbenchAgentRunCurrentState> GetCurrentActiveAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        long? chatSessionId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || actorUserId <= 0 || projectId <= 0 ||
            workbenchSessionId <= 0 || leaseEpoch <= 0 || chatSessionId < 0)
            throw new WorkbenchAgentRunValidationException(
                "A current project and Workbench lease are required.");

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(
                    connection,
                    transaction,
                    tenantId,
                    actorUserId,
                    projectId,
                    cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();

            if (!await IsLeaseCurrentAsync(
                    connection,
                    transaction,
                    tenantId,
                    actorUserId,
                    projectId,
                    workbenchSessionId,
                    leaseEpoch,
                    cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var chatBinding = await connection.QuerySingleOrDefaultAsync<WorkbenchSessionChatBindingRow>(new CommandDefinition(
                """
                SELECT session.ActiveChatSessionId
                FROM dbo.WorkbenchSessions session WITH (HOLDLOCK)
                WHERE session.TenantId=@TenantId AND session.ProjectId=@ProjectId
                  AND session.Id=@WorkbenchSessionId AND session.Status=N'Active';
                """,
                new
                {
                    TenantId = tenantId,
                    ProjectId = projectId,
                    WorkbenchSessionId = workbenchSessionId
                },
                transaction,
                cancellationToken: cancellationToken));
            if (chatBinding is null)
                throw new WorkbenchLeaseFenceException();

            var requestedChatSessionId = chatSessionId is > 0 ? chatSessionId : null;
            if (requestedChatSessionId.HasValue)
            {
                var chatExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(1)
                    FROM dbo.ProjectChatSessions WITH (HOLDLOCK)
                    WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@ChatSessionId;
                    """,
                    new
                    {
                        TenantId = tenantId,
                        ProjectId = projectId,
                        ChatSessionId = requestedChatSessionId.Value
                    },
                    transaction,
                    cancellationToken: cancellationToken));
                if (chatExists == 0)
                    throw new WorkbenchChatSessionBindingException();
            }

            if (chatBinding.ActiveChatSessionId.HasValue && requestedChatSessionId.HasValue &&
                chatBinding.ActiveChatSessionId.Value != requestedChatSessionId.Value)
                throw new WorkbenchChatSessionBindingException();

            var effectiveChatSessionId = chatBinding.ActiveChatSessionId ?? requestedChatSessionId;
            AgentRunRow? latestRun = null;
            if (effectiveChatSessionId.HasValue)
            {
                latestRun = await connection.QuerySingleOrDefaultAsync<AgentRunRow>(new CommandDefinition(
                    """
                    SELECT TOP (1) *
                    FROM dbo.WorkbenchAgentRuns WITH (HOLDLOCK)
                    WHERE TenantId=@TenantId AND ProjectId=@ProjectId
                      AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch
                      AND ChatSessionId=@ChatSessionId
                    ORDER BY CreatedAtUtc DESC, AgentRunId DESC;
                    """,
                    new
                    {
                        TenantId = tenantId,
                        ProjectId = projectId,
                        WorkbenchSessionId = workbenchSessionId,
                        LeaseEpoch = leaseEpoch,
                        ChatSessionId = effectiveChatSessionId.Value
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            transaction.Commit();
            var latestSnapshot = latestRun is null ? null : ToSnapshot(latestRun);
            return new WorkbenchAgentRunCurrentState(
                chatBinding.ActiveChatSessionId,
                latestSnapshot is not null && !WorkbenchAgentRunStates.IsTerminal(latestSnapshot.Status)
                    ? latestSnapshot
                    : null,
                latestSnapshot);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<WorkbenchAgentRunClaim?> ClaimAsync(
        Guid agentRunId,
        string workerId,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default)
    {
        if (agentRunId == Guid.Empty || string.IsNullOrWhiteSpace(workerId) || claimDuration <= TimeSpan.Zero)
            throw new WorkbenchAgentRunValidationException("A run, worker ID, and positive claim duration are required.");

        workerId = workerId.Trim();
        if (workerId.Length > 200)
            throw new WorkbenchAgentRunValidationException("workerId exceeds the 200 character limit.");

        using var connection = _connections.CreateConnection();
        connection.Open();
        var discoveredRun = await connection.QuerySingleOrDefaultAsync<AgentRunRow>(new CommandDefinition(
            "SELECT * FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId;",
            new { AgentRunId = agentRunId },
            cancellationToken: cancellationToken));
        if (discoveredRun is null || WorkbenchAgentRunStates.IsTerminal(discoveredRun.Status))
            return null;

        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var currentFence = await IsRunFenceCurrentAsync(connection, transaction, discoveredRun, cancellationToken);

            var run = await connection.QuerySingleOrDefaultAsync<AgentRunRow>(new CommandDefinition(
                "SELECT * FROM dbo.WorkbenchAgentRuns WITH (UPDLOCK, HOLDLOCK) WHERE AgentRunId=@AgentRunId;",
                new { AgentRunId = agentRunId },
                transaction,
                cancellationToken: cancellationToken));
            if (run is null || WorkbenchAgentRunStates.IsTerminal(run.Status))
            {
                transaction.Commit();
                return null;
            }

            if (run.Status == WorkbenchAgentRunStates.Running && run.ClaimExpiresAtUtc > DateTime.UtcNow)
            {
                transaction.Commit();
                return null;
            }

            if (!currentFence || !SameFence(discoveredRun, run))
            {
                await MarkRunWithoutCurrentFenceAsync(connection, transaction, run, "claim_lease_not_current", null, cancellationToken);
                transaction.Commit();
                return null;
            }

            if (run.Status == WorkbenchAgentRunStates.Running && run.ClaimToken is { } expiredClaimToken)
            {
                await CloseAttemptWithoutResponseAsync(
                    connection,
                    transaction,
                    expiredClaimToken,
                    "ClaimExpired",
                    "claim_expired_before_reclaim",
                    cancellationToken);
            }

            var claimToken = Guid.NewGuid();
            var claimExpiresAtUtc = DateTime.UtcNow.Add(claimDuration);
            var attemptCount = run.AttemptCount + 1;
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.WorkbenchAgentRuns
                SET Status=N'Running', AttemptCount=@AttemptCount, ClaimToken=@ClaimToken,
                    ClaimedBy=@WorkerId, ClaimedAtUtc=SYSUTCDATETIME(), ClaimExpiresAtUtc=@ClaimExpiresAtUtc,
                    StartedAtUtc=COALESCE(StartedAtUtc, SYSUTCDATETIME())
                WHERE AgentRunId=@AgentRunId;
                """,
                new { AgentRunId = agentRunId, AttemptCount = attemptCount, ClaimToken = claimToken, WorkerId = workerId, ClaimExpiresAtUtc = claimExpiresAtUtc },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchAgentRunAttempts
                    (AgentRunId, AttemptNumber, ClaimToken, WorkerId)
                VALUES
                    (@AgentRunId, @AttemptNumber, @ClaimToken, @WorkerId);
                """,
                new { AgentRunId = agentRunId, AttemptNumber = attemptCount, ClaimToken = claimToken, WorkerId = workerId },
                transaction,
                cancellationToken: cancellationToken));

            var payload = JsonSerializer.Serialize(new { agentRunId, claimToken, attemptCount }, JsonOptions);
            await InsertOutboxAsync(connection, transaction, run, "AgentRunClaimed", payload, cancellationToken);
            transaction.Commit();
            return new WorkbenchAgentRunClaim(
                run.AgentRunId,
                claimToken,
                run.TenantId,
                run.ProjectId,
                run.WorkbenchSessionId,
                run.LeaseEpoch,
                run.ActorUserId,
                run.ChatSessionId,
                run.SourceUserMessageId,
                attemptCount,
                run.AgentVersion,
                run.PromptVersion,
                run.ToolPolicyVersion,
                run.ContextSchemaVersion,
                run.ContextCanonicalizationVersion,
                run.OutputSchemaVersion,
                run.InvocationKind,
                run.TicketInstruction,
                run.TicketProposalSetId,
                run.TicketProposalRevision);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> AuthorizeInvocationAsync(
        WorkbenchAgentRunClaim claim,
        TimeSpan renewedClaimDuration,
        CancellationToken cancellationToken = default)
    {
        if (claim.AgentRunId == Guid.Empty || claim.ClaimToken == Guid.Empty || renewedClaimDuration <= TimeSpan.Zero)
            throw new WorkbenchAgentRunValidationException(
                "A current run claim and positive renewed claim duration are required.");
        if (renewedClaimDuration.TotalSeconds > int.MaxValue)
            throw new WorkbenchAgentRunValidationException("The renewed claim duration is too large.");

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // Invocation authorization follows the same global order as takeover and materialization:
            // lock the exact lease fence before locking the run. Profile/prompt preparation happens
            // before this method; a successful return is therefore the last awaited authority check
            // before the external provider call.
            var fenceCurrent = await IsClaimFenceCurrentAsync(
                connection,
                transaction,
                claim,
                cancellationToken);
            var run = await connection.QuerySingleOrDefaultAsync<AgentRunRow>(new CommandDefinition(
                "SELECT * FROM dbo.WorkbenchAgentRuns WITH (UPDLOCK, HOLDLOCK) WHERE AgentRunId=@AgentRunId;",
                new { claim.AgentRunId },
                transaction,
                cancellationToken: cancellationToken));
            if (run is null)
                throw new WorkbenchAgentRunNotFoundException();

            var exactClaim = run.Status == WorkbenchAgentRunStates.Running &&
                             run.ClaimToken == claim.ClaimToken &&
                             RunReferencesMatchClaim(run, claim);
            if (!exactClaim)
            {
                transaction.Commit();
                return false;
            }

            if (!fenceCurrent)
            {
                await MarkRunWithoutCurrentFenceAsync(
                    connection,
                    transaction,
                    run,
                    "invocation_authority_not_current",
                    diagnosticHash: null,
                    cancellationToken);
                transaction.Commit();
                return false;
            }

            var renewed = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.WorkbenchAgentRuns
                SET ClaimExpiresAtUtc=DATEADD(SECOND, @RenewedClaimSeconds, SYSUTCDATETIME())
                WHERE AgentRunId=@AgentRunId AND Status=N'Running' AND ClaimToken=@ClaimToken
                  AND CancellationRequestedAtUtc IS NULL
                  AND ClaimExpiresAtUtc > SYSUTCDATETIME();
                """,
                new
                {
                    claim.AgentRunId,
                    claim.ClaimToken,
                    RenewedClaimSeconds = checked((int)Math.Ceiling(renewedClaimDuration.TotalSeconds))
                },
                transaction,
                cancellationToken: cancellationToken));
            transaction.Commit();
            return renewed == 1;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<WorkbenchAgentRunMaterializationResult> MaterializeAsync(
        WorkbenchAgentRunClaim claim,
        WorkbenchBusinessAnalystContext context,
        WorkbenchBusinessAnalystOutput output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        var outputJson = WorkbenchBusinessAnalystOutputValidator.Serialize(output);
        var outputHash = ComputeHash(outputJson);

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // Takeover locks the lease before affected runs. Materialization uses the same order.
            var fenceCurrent = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
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
                claim,
                transaction,
                cancellationToken: cancellationToken)) > 0;

            var run = await connection.QuerySingleOrDefaultAsync<AgentRunRow>(new CommandDefinition(
                "SELECT * FROM dbo.WorkbenchAgentRuns WITH (UPDLOCK, HOLDLOCK) WHERE AgentRunId=@AgentRunId;",
                new { claim.AgentRunId },
                transaction,
                cancellationToken: cancellationToken)) ?? throw new WorkbenchAgentRunNotFoundException();

            if (run.AssistantMessageId is not null)
            {
                var exactClaim = run.ClaimToken == claim.ClaimToken &&
                                 RunReferencesMatchClaim(run, claim);
                var authoritativeReplay = exactClaim &&
                                          TryLoadAndValidateAuthoritativeContext(
                                              run,
                                              claim,
                                              context,
                                              out var storedReplayContext) &&
                                          OutputMatchesContext(output, storedReplayContext!);
                var exactReplay = authoritativeReplay &&
                                  string.Equals(run.OutputHash, outputHash, StringComparison.OrdinalIgnoreCase);
                if (!exactReplay)
                {
                    var diagnosticCode = !exactClaim
                        ? "late_result_after_reclaim"
                        : !authoritativeReplay
                            ? "materialization_replay_context_mismatch"
                            : "materialization_replay_output_mismatch";
                    await RecordRestrictedLateResultAsync(
                        connection,
                        transaction,
                        run.AgentRunId,
                        outputHash,
                        diagnosticCode,
                        cancellationToken);
                    await CompleteAttemptAsync(
                        connection,
                        transaction,
                        claim.ClaimToken,
                        "LateRestricted",
                        outputHash,
                        diagnosticCode,
                        cancellationToken);
                    transaction.Commit();
                    return new WorkbenchAgentRunMaterializationResult(
                        run.AgentRunId,
                        run.Status,
                        Materialized: false,
                        AssistantMessageId: null,
                        IsReplay: false,
                        RejectionReason: diagnosticCode);
                }

                transaction.Commit();
                return new WorkbenchAgentRunMaterializationResult(
                    run.AgentRunId,
                    run.Status,
                    Materialized: true,
                    run.AssistantMessageId,
                    IsReplay: true,
                    RejectionReason: null,
                    run.TicketProposalSetId,
                    run.MaterializedTicketProposalRevision);
            }

            if (run.Status != WorkbenchAgentRunStates.Running)
            {
                await RecordRestrictedLateResultAsync(connection, transaction, run.AgentRunId, outputHash, "late_result_after_terminal_state", cancellationToken);
                await CompleteAttemptAsync(connection, transaction, claim.ClaimToken, "LateRestricted", outputHash, "late_result_after_terminal_state", cancellationToken);
                transaction.Commit();
                return new WorkbenchAgentRunMaterializationResult(
                    run.AgentRunId,
                    run.Status,
                    Materialized: false,
                    AssistantMessageId: null,
                    IsReplay: false,
                    RejectionReason: "agent_run_not_running");
            }

            if (run.ClaimToken != claim.ClaimToken)
            {
                await RecordRestrictedLateResultAsync(connection, transaction, run.AgentRunId, outputHash, "late_result_after_reclaim", cancellationToken);
                await CompleteAttemptAsync(connection, transaction, claim.ClaimToken, "LateRestricted", outputHash, "late_result_after_reclaim", cancellationToken);
                transaction.Commit();
                return new WorkbenchAgentRunMaterializationResult(
                    run.AgentRunId,
                    run.Status,
                    Materialized: false,
                    AssistantMessageId: null,
                    IsReplay: false,
                    RejectionReason: "claim_token_not_current");
            }

            if (!RunReferencesMatchClaim(run, claim))
            {
                await MarkFailedCoreAsync(connection, transaction, run, "run_reference_mismatch", outputHash, cancellationToken);
                await CompleteAttemptAsync(connection, transaction, claim.ClaimToken, "Rejected", outputHash, "run_reference_mismatch", cancellationToken);
                transaction.Commit();
                return new WorkbenchAgentRunMaterializationResult(
                    run.AgentRunId,
                    WorkbenchAgentRunStates.Failed,
                    Materialized: false,
                    AssistantMessageId: null,
                    IsReplay: false,
                    RejectionReason: "run_reference_mismatch");
            }

            if (!TryLoadAndValidateAuthoritativeContext(run, claim, context, out var storedContext))
            {
                await MarkFailedCoreAsync(connection, transaction, run, "context_snapshot_mismatch", outputHash, cancellationToken);
                await CompleteAttemptAsync(connection, transaction, claim.ClaimToken, "Rejected", outputHash, "context_snapshot_mismatch", cancellationToken);
                transaction.Commit();
                return new WorkbenchAgentRunMaterializationResult(
                    run.AgentRunId,
                    WorkbenchAgentRunStates.Failed,
                    Materialized: false,
                    AssistantMessageId: null,
                    IsReplay: false,
                    RejectionReason: "context_snapshot_mismatch");
            }

            try
            {
                // The persisted snapshot is the final materialization authority. The caller context is
                // compared above only as an additional guard against in-process mutation.
                WorkbenchBusinessAnalystOutputValidator.Validate(output, storedContext!);
            }
            catch (WorkbenchAgentOutputValidationException)
            {
                await MarkFailedCoreAsync(connection, transaction, run, "agent_output_schema_invalid", outputHash, cancellationToken);
                await CompleteAttemptAsync(connection, transaction, claim.ClaimToken, "Rejected", outputHash, "agent_output_schema_invalid", cancellationToken);
                transaction.Commit();
                return new WorkbenchAgentRunMaterializationResult(
                    run.AgentRunId,
                    WorkbenchAgentRunStates.Failed,
                    Materialized: false,
                    AssistantMessageId: null,
                    IsReplay: false,
                    RejectionReason: "agent_output_schema_invalid");
            }

            if (!fenceCurrent)
            {
                await MarkRunWithoutCurrentFenceAsync(connection, transaction, run, "late_result_lease_not_current", outputHash, cancellationToken);
                var state = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT Status FROM dbo.WorkbenchAgentRuns WHERE AgentRunId=@AgentRunId;",
                    new { run.AgentRunId },
                    transaction,
                    cancellationToken: cancellationToken)) ?? WorkbenchAgentRunStates.Stale;
                transaction.Commit();
                return new WorkbenchAgentRunMaterializationResult(
                    run.AgentRunId,
                    state,
                    Materialized: false,
                    AssistantMessageId: null,
                    IsReplay: false,
                    RejectionReason: "lease_epoch_not_current");
            }

            var validReferences = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.ProjectChatSessions session
                INNER JOIN dbo.ChatMessages source
                    ON source.TenantId=session.TenantId AND source.ProjectId=session.ProjectId
                   AND source.ChatSessionId=session.Id AND source.Id=@SourceUserMessageId AND source.Role=N'user'
                WHERE session.TenantId=@TenantId AND session.ProjectId=@ProjectId AND session.Id=@ChatSessionId;
                """,
                run,
                transaction,
                cancellationToken: cancellationToken));

            if (validReferences == 0 ||
                !await ValidateOutputSourceReferencesAsync(
                    connection,
                    transaction,
                    run,
                    storedContext!,
                    output,
                    cancellationToken))
            {
                await MarkFailedCoreAsync(connection, transaction, run, "source_reference_invalid", outputHash, cancellationToken);
                await CompleteAttemptAsync(
                    connection,
                    transaction,
                    claim.ClaimToken,
                    "Rejected",
                    outputHash,
                    "source_reference_invalid",
                    cancellationToken);
                transaction.Commit();
                return new WorkbenchAgentRunMaterializationResult(
                    run.AgentRunId,
                    WorkbenchAgentRunStates.Failed,
                    Materialized: false,
                    AssistantMessageId: null,
                    IsReplay: false,
                    RejectionReason: "source_reference_invalid");
            }

            await ApplyUnderstandingOutputAsync(
                connection,
                transaction,
                run,
                storedContext!,
                output,
                cancellationToken);

            var materializedTicketProposalRevision = await ApplyTicketProposalOutputAsync(
                connection,
                transaction,
                run,
                storedContext!,
                output,
                cancellationToken);

            await CompleteAttemptAsync(
                connection,
                transaction,
                claim.ClaimToken,
                output.Outcome,
                outputHash,
                diagnosticCode: null,
                cancellationToken);

            var assistantTags = BuildWorkbenchAssistantTags(run, output);
            var assistantMessageId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT dbo.ChatMessages
                    (TenantId, ProjectId, ChatSessionId, Role, Message, Tags, ReplyToMessageId, CreatedDate)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ProjectId, @ChatSessionId, N'assistant', @AssistantMessage,
                     @AssistantTags, @SourceUserMessageId, SYSUTCDATETIME());

                UPDATE dbo.ProjectChatSessions
                SET UpdatedDate=SYSUTCDATETIME()
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@ChatSessionId;
                """,
                new
                {
                    run.TenantId,
                    run.ProjectId,
                    run.ChatSessionId,
                    run.SourceUserMessageId,
                    output.AssistantMessage,
                    AssistantTags = assistantTags
                },
                transaction,
                cancellationToken: cancellationToken));

            await _turnPersistence.PersistAsync(
                new ChatTurnPersistenceRequest(
                    assistantMessageId,
                    run.TenantId,
                    run.ProjectId,
                    run.ChatSessionId,
                    "assistant",
                    assistantTags,
                    ContextSummary: null,
                    LinkedFilePaths: null,
                    LinkedSymbols: null),
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false);

            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.WorkbenchAgentRuns
                SET Status=@Status, ValidatedOutputJson=@OutputJson, OutputHash=@OutputHash,
                    AssistantMessageId=@AssistantMessageId, MaterializedAtUtc=SYSUTCDATETIME(),
                    MaterializedTicketProposalRevision=@MaterializedTicketProposalRevision,
                    CompletedAtUtc=SYSUTCDATETIME(), ClaimExpiresAtUtc=NULL, ActiveRunSlot=NULL
                WHERE AgentRunId=@AgentRunId AND Status=N'Running' AND ClaimToken=@ClaimToken
                  AND AssistantMessageId IS NULL;
                """,
                new
                {
                    run.AgentRunId,
                    claim.ClaimToken,
                    Status = output.Outcome,
                    OutputJson = outputJson,
                    OutputHash = outputHash,
                    AssistantMessageId = assistantMessageId,
                    MaterializedTicketProposalRevision = materializedTicketProposalRevision
                },
                transaction,
                cancellationToken: cancellationToken));
            if (updated != 1)
                throw new InvalidOperationException("The claimed Workbench agent run could not be materialized atomically.");

            var payload = JsonSerializer.Serialize(new
            {
                run.AgentRunId,
                status = output.Outcome,
                assistantMessageId,
                outputHash
            }, JsonOptions);
            await InsertOutboxAsync(connection, transaction, run, "AgentRunMaterialized", payload, cancellationToken);
            transaction.Commit();
            return new WorkbenchAgentRunMaterializationResult(
                run.AgentRunId,
                output.Outcome,
                Materialized: true,
                assistantMessageId,
                IsReplay: false,
                RejectionReason: null,
                run.TicketProposalSetId,
                materializedTicketProposalRevision);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task MarkFailedAsync(
        WorkbenchAgentRunClaim claim,
        string diagnosticCode,
        string diagnosticHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(diagnosticCode) || diagnosticCode.Length > 100 ||
            string.IsNullOrWhiteSpace(diagnosticHash) || diagnosticHash.Length != 64)
            throw new WorkbenchAgentRunValidationException("A bounded diagnostic code and SHA-256 diagnostic hash are required.");

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // Match takeover/materialization lock order: exact lease fence, then run.
            var fenceCurrent = await IsClaimFenceCurrentAsync(
                connection,
                transaction,
                claim,
                cancellationToken);
            var run = await connection.QuerySingleOrDefaultAsync<AgentRunRow>(new CommandDefinition(
                "SELECT * FROM dbo.WorkbenchAgentRuns WITH (UPDLOCK, HOLDLOCK) WHERE AgentRunId=@AgentRunId;",
                new { claim.AgentRunId },
                transaction,
                cancellationToken: cancellationToken));
            if (run is null)
                throw new WorkbenchAgentRunNotFoundException();

            var currentClaim = run.Status == WorkbenchAgentRunStates.Running &&
                               run.ClaimToken == claim.ClaimToken &&
                               RunReferencesMatchClaim(run, claim);
            if (currentClaim && fenceCurrent)
            {
                await MarkFailedCoreAsync(connection, transaction, run, diagnosticCode, diagnosticHash, cancellationToken);
                await CompleteAttemptAsync(connection, transaction, claim.ClaimToken, "Failed", diagnosticHash, diagnosticCode, cancellationToken);
            }
            else if (currentClaim)
            {
                await MarkRunWithoutCurrentFenceAsync(
                    connection,
                    transaction,
                    run,
                    "failure_after_lease_not_current",
                    diagnosticHash,
                    cancellationToken);
                await CompleteAttemptAsync(connection, transaction, claim.ClaimToken, "LateRestricted", diagnosticHash, diagnosticCode, cancellationToken);
            }
            else
            {
                await RecordRestrictedLateResultAsync(connection, transaction, run.AgentRunId, diagnosticHash, diagnosticCode, cancellationToken);
                await CompleteAttemptAsync(connection, transaction, claim.ClaimToken, "LateRestricted", diagnosticHash, diagnosticCode, cancellationToken);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static string BuildWorkbenchAssistantTags(
        AgentRunRow run,
        WorkbenchBusinessAnalystOutput output)
    {
        var mode = new ChatModeDecision(
            ChatGovernanceMode.Exploration,
            1,
            "Repository-independent Workbench shaping turn.");
        var clarification = output.Outcome == WorkbenchAgentRunStates.NeedsInput
            ? new ChatClarificationState(
                true,
                ChatClarificationKind.GeneralScope,
                Array.Empty<string>(),
                "The Business Analyst needs more project input.")
            : ChatClarificationState.None;
        var envelope = new ChatTurnEnvelope(
            1,
            mode.Mode,
            mode.Confidence,
            mode.Reason,
            clarification,
            ChatGovernanceGate.FromDecision(mode),
            RouteTraceId: run.AgentRunId.ToString("D"),
            DogfoodTraceId: null,
            RouteSource: "WorkbenchAgentRun");
        return JsonSerializer.Serialize(envelope, ChatEnvelopeJsonOptions);
    }

    private static void ValidateSubmit(SubmitWorkbenchAgentRunCommand command)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 || command.ChatSessionId <= 0 ||
            command.ClientOperationId == Guid.Empty)
            throw new WorkbenchAgentRunValidationException(
                "A current project, Workbench lease, chat session, and client operation ID are required.");
        if (string.IsNullOrWhiteSpace(command.Message))
            throw new WorkbenchAgentRunValidationException("message is required.");
        if (command.Message.Length > WorkbenchBusinessAnalystProviderContract.MaximumConversationMessageCharacters)
            throw new WorkbenchAgentRunValidationException(
                $"message exceeds the {WorkbenchBusinessAnalystProviderContract.MaximumConversationMessageCharacters} character limit.");
        if (!WorkbenchAgentInvocationKinds.IsSupported(command.InvocationKind))
            throw new WorkbenchAgentRunValidationException("invocationKind is not supported.");
        var route = WorkbenchInputRouter.Parse(command.Message);
        if (command.InvocationKind == WorkbenchAgentInvocationKinds.Conversation)
        {
            if (route.IsCommand || command.TicketProposalSetId is not null ||
                command.ExpectedTicketProposalRevision is not null || command.TicketInstruction is not null)
                throw new WorkbenchCommandRoutingRequiredException();
            return;
        }
        if (route.Kind != WorkbenchInputKinds.Ticket ||
            !string.Equals(route.Instruction, command.TicketInstruction, StringComparison.Ordinal))
            throw new WorkbenchAgentRunValidationException(
                "A proposal-purpose run requires the authoritative /ticket route and matching instruction.");
        if (command.InvocationKind == WorkbenchAgentInvocationKinds.TicketProposalGeneration &&
            (command.TicketProposalSetId is not null || command.ExpectedTicketProposalRevision is not null))
            throw new WorkbenchAgentRunValidationException(
                "Initial ticket proposal generation cannot target an existing set.");
        if (command.InvocationKind == WorkbenchAgentInvocationKinds.TicketProposalRegeneration &&
            (command.TicketProposalSetId is null || command.TicketProposalSetId == Guid.Empty ||
             command.ExpectedTicketProposalRevision is not > 0))
            throw new WorkbenchAgentRunValidationException(
                "Ticket proposal regeneration requires an existing set and expected revision.");
    }

    private static async Task<bool> CanAccessProjectAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        int projectId,
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
            new { TenantId = tenantId, ActorUserId = actorUserId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken)) > 0;

    private static async Task<bool> ValidateAndRenewLeaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
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
            new
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                LeaseEpoch = leaseEpoch
            },
            transaction,
            cancellationToken: cancellationToken)) > 0;

    private static async Task<bool> IsLeaseCurrentAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.WorkbenchWriteLeases lease WITH (HOLDLOCK)
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
            new
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                LeaseEpoch = leaseEpoch
            },
            transaction,
            cancellationToken: cancellationToken)) > 0;

    private static async Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        string operationKind,
        string resourceScopeId,
        Guid clientOperationId,
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
                TenantId = tenantId,
                ActorUserId = actorUserId,
                OperationKind = operationKind,
                ResourceScopeId = resourceScopeId,
                ClientOperationId = clientOperationId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static void EnsureMatchingOperation(ClientOperationRow operation, string payloadHash)
    {
        if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            throw new ProjectStartOperationMismatchException();
    }

    private static T ReadStoredResult<T>(ClientOperationRow row)
    {
        if (string.IsNullOrWhiteSpace(row.CanonicalResultJson) || string.IsNullOrWhiteSpace(row.ResultHash))
            throw new InvalidOperationException("The completed agent-run operation has no canonical result.");
        if (!string.Equals(ComputeHash(row.CanonicalResultJson), row.ResultHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stored agent-run operation result failed its integrity check.");
        return JsonSerializer.Deserialize<T>(row.CanonicalResultJson, JsonOptions)
            ?? throw new InvalidOperationException("The stored agent-run operation result could not be read.");
    }

    private static async Task<bool> IsRunFenceCurrentAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        AgentRunRow run,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
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
            run,
            transaction,
            cancellationToken: cancellationToken)) > 0;

    private static async Task<bool> IsClaimFenceCurrentAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        WorkbenchAgentRunClaim claim,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
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
            claim,
            transaction,
            cancellationToken: cancellationToken)) > 0;

    private static bool RunReferencesMatchClaim(AgentRunRow run, WorkbenchAgentRunClaim claim) =>
        run.AgentRunId == claim.AgentRunId &&
        run.TenantId == claim.TenantId &&
        run.ProjectId == claim.ProjectId &&
        run.WorkbenchSessionId == claim.WorkbenchSessionId &&
        run.LeaseEpoch == claim.LeaseEpoch &&
        run.ActorUserId == claim.ActorUserId &&
        run.ChatSessionId == claim.ChatSessionId &&
        run.SourceUserMessageId == claim.SourceUserMessageId &&
        run.AgentVersion == claim.AgentVersion &&
        run.PromptVersion == claim.PromptVersion &&
        run.ToolPolicyVersion == claim.ToolPolicyVersion &&
        run.ContextSchemaVersion == claim.ContextSchemaVersion &&
        run.ContextCanonicalizationVersion == claim.ContextCanonicalizationVersion &&
        run.OutputSchemaVersion == claim.OutputSchemaVersion &&
        run.InvocationKind == claim.InvocationKind &&
        run.TicketInstruction == claim.TicketInstruction &&
        run.TicketProposalSetId == claim.TicketProposalSetId &&
        run.TicketProposalRevision == claim.TicketProposalRevision;

    private static bool TryLoadAndValidateAuthoritativeContext(
        AgentRunRow run,
        WorkbenchAgentRunClaim claim,
        WorkbenchBusinessAnalystContext suppliedContext,
        out WorkbenchBusinessAnalystContext? storedContext)
    {
        storedContext = null;
        try
        {
            if (string.IsNullOrWhiteSpace(run.ContextSnapshotJson) ||
                string.IsNullOrWhiteSpace(run.ContextHash) ||
                run.BasedOnUnderstandingRevision is null ||
                !RunReferencesMatchClaim(run, claim))
                return false;

            storedContext = WorkbenchBusinessAnalystContextCodec.Deserialize(
                run.ContextSnapshotJson,
                run.ContextSchemaVersion,
                run.ContextCanonicalizationVersion);
            if (storedContext.AgentRunId != run.AgentRunId || storedContext.AgentRunId != claim.AgentRunId ||
                storedContext.TenantId != run.TenantId || storedContext.TenantId != claim.TenantId ||
                storedContext.ProjectId != run.ProjectId || storedContext.ProjectId != claim.ProjectId ||
                storedContext.WorkbenchSessionId != run.WorkbenchSessionId ||
                storedContext.WorkbenchSessionId != claim.WorkbenchSessionId ||
                storedContext.LeaseEpoch != run.LeaseEpoch || storedContext.LeaseEpoch != claim.LeaseEpoch ||
                storedContext.ChatSessionId != run.ChatSessionId || storedContext.ChatSessionId != claim.ChatSessionId ||
                storedContext.SourceUserMessageId != run.SourceUserMessageId ||
                storedContext.SourceUserMessageId != claim.SourceUserMessageId ||
                storedContext.UnderstandingRevision != run.BasedOnUnderstandingRevision ||
                storedContext.AgentVersion != run.AgentVersion || storedContext.AgentVersion != claim.AgentVersion ||
                storedContext.PromptVersion != run.PromptVersion || storedContext.PromptVersion != claim.PromptVersion ||
                storedContext.ToolPolicyVersion != run.ToolPolicyVersion ||
                storedContext.ToolPolicyVersion != claim.ToolPolicyVersion ||
                storedContext.ContextSchemaVersion != run.ContextSchemaVersion ||
                storedContext.ContextSchemaVersion != claim.ContextSchemaVersion ||
                storedContext.ContextCanonicalizationVersion != run.ContextCanonicalizationVersion ||
                storedContext.ContextCanonicalizationVersion != claim.ContextCanonicalizationVersion ||
                storedContext.OutputSchemaVersion != run.OutputSchemaVersion ||
                storedContext.OutputSchemaVersion != claim.OutputSchemaVersion ||
                storedContext.InvocationKind != run.InvocationKind ||
                storedContext.InvocationKind != claim.InvocationKind ||
                storedContext.TicketInstruction != run.TicketInstruction ||
                storedContext.TicketInstruction != claim.TicketInstruction ||
                storedContext.TicketProposalSetId != run.TicketProposalSetId ||
                storedContext.TicketProposalSetId != claim.TicketProposalSetId ||
                storedContext.TicketProposalRevision != run.TicketProposalRevision ||
                storedContext.TicketProposalRevision != claim.TicketProposalRevision ||
                !string.Equals(storedContext.ContextHash, run.ContextHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    WorkbenchBusinessAnalystContextCodec.ComputeHash(storedContext),
                    run.ContextHash,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            // Hash the actual supplied contents rather than trusting its ContextHash property. Exact
            // canonical equality makes caller mutation observable even when the old hash is retained.
            return string.Equals(suppliedContext.ContextHash, run.ContextHash, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       WorkbenchBusinessAnalystContextCodec.CanonicalizeWithoutHash(suppliedContext),
                       WorkbenchBusinessAnalystContextCodec.CanonicalizeWithoutHash(storedContext),
                       StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            storedContext = null;
            return false;
        }
    }

    private static bool OutputMatchesContext(
        WorkbenchBusinessAnalystOutput output,
        WorkbenchBusinessAnalystContext context)
    {
        try
        {
            WorkbenchBusinessAnalystOutputValidator.Validate(output, context);
            return true;
        }
        catch (WorkbenchAgentOutputValidationException)
        {
            return false;
        }
    }

    private static bool SameFence(AgentRunRow left, AgentRunRow right) =>
        left.TenantId == right.TenantId &&
        left.ProjectId == right.ProjectId &&
        left.WorkbenchSessionId == right.WorkbenchSessionId &&
        left.LeaseEpoch == right.LeaseEpoch &&
        left.ActorUserId == right.ActorUserId;

    private static async Task MarkRunWithoutCurrentFenceAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        AgentRunRow run,
        string diagnosticCode,
        string? diagnosticHash,
        CancellationToken cancellationToken)
    {
        var successor = await connection.QuerySingleOrDefaultAsync<SuccessorFenceRow>(new CommandDefinition(
            """
            SELECT TOP (1) WorkbenchSessionId, LeaseEpoch
            FROM dbo.WorkbenchWriteLeases
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND RevokedAtUtc IS NULL
              AND LeaseEpoch > @LeaseEpoch
            ORDER BY LeaseEpoch DESC;
            """,
            run,
            transaction,
            cancellationToken: cancellationToken));
        var status = successor is not null ? WorkbenchAgentRunStates.Superseded : WorkbenchAgentRunStates.Stale;

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.WorkbenchAgentRuns
            SET Status=@Status,
                CancellationRequestedAtUtc=CASE WHEN @Status=N'Superseded' THEN COALESCE(CancellationRequestedAtUtc, SYSUTCDATETIME()) ELSE CancellationRequestedAtUtc END,
                SupersededAtUtc=CASE WHEN @Status=N'Superseded' THEN COALESCE(SupersededAtUtc, SYSUTCDATETIME()) ELSE SupersededAtUtc END,
                SupersededByWorkbenchSessionId=CASE WHEN @Status=N'Superseded' THEN @SupersededByWorkbenchSessionId ELSE SupersededByWorkbenchSessionId END,
                SupersededByLeaseEpoch=CASE WHEN @Status=N'Superseded' THEN @SupersededByLeaseEpoch ELSE SupersededByLeaseEpoch END,
                DiagnosticCode=COALESCE(DiagnosticCode, @DiagnosticCode),
                DiagnosticHash=COALESCE(DiagnosticHash, @DiagnosticHash),
                DiagnosticAtUtc=COALESCE(DiagnosticAtUtc, SYSUTCDATETIME()),
                CompletedAtUtc=COALESCE(CompletedAtUtc, SYSUTCDATETIME()), ClaimExpiresAtUtc=NULL,
                ActiveRunSlot=NULL
            WHERE AgentRunId=@AgentRunId AND Status IN (N'Pending', N'Running');
            """,
            new
            {
                run.AgentRunId,
                Status = status,
                SupersededByWorkbenchSessionId = successor?.WorkbenchSessionId,
                SupersededByLeaseEpoch = successor?.LeaseEpoch,
                DiagnosticCode = diagnosticCode,
                DiagnosticHash = diagnosticHash
            },
            transaction,
            cancellationToken: cancellationToken));

        if (run.Status == WorkbenchAgentRunStates.Running && run.ClaimToken is { } claimToken)
        {
            await CloseAttemptWithoutResponseAsync(
                connection,
                transaction,
                claimToken,
                status,
                diagnosticCode,
                cancellationToken);
        }

        var payload = JsonSerializer.Serialize(new { run.AgentRunId, status, reason = diagnosticCode }, JsonOptions);
        await InsertOutboxAsync(connection, transaction, run, status == WorkbenchAgentRunStates.Superseded ? "AgentRunSuperseded" : "AgentRunStale", payload, cancellationToken);
    }

    private static async Task MarkFailedCoreAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        AgentRunRow run,
        string diagnosticCode,
        string diagnosticHash,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.WorkbenchAgentRuns
            SET Status=N'Failed', DiagnosticCode=@DiagnosticCode, DiagnosticHash=@DiagnosticHash,
                DiagnosticAtUtc=SYSUTCDATETIME(), CompletedAtUtc=SYSUTCDATETIME(), ClaimExpiresAtUtc=NULL,
                ActiveRunSlot=NULL
            WHERE AgentRunId=@AgentRunId AND Status=N'Running';
            """,
            new { run.AgentRunId, DiagnosticCode = diagnosticCode, DiagnosticHash = diagnosticHash },
            transaction,
            cancellationToken: cancellationToken));
        var payload = JsonSerializer.Serialize(new { run.AgentRunId, diagnosticCode, diagnosticHash }, JsonOptions);
        await InsertOutboxAsync(connection, transaction, run, "AgentRunFailed", payload, cancellationToken);
    }

    private static async Task RecordRestrictedLateResultAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid agentRunId,
        string diagnosticHash,
        string diagnosticCode,
        CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.WorkbenchAgentRuns
            SET DiagnosticCode=COALESCE(DiagnosticCode, @DiagnosticCode),
                DiagnosticHash=COALESCE(DiagnosticHash, @DiagnosticHash),
                DiagnosticAtUtc=COALESCE(DiagnosticAtUtc, SYSUTCDATETIME())
            WHERE AgentRunId=@AgentRunId;
            """,
            new { AgentRunId = agentRunId, DiagnosticCode = diagnosticCode, DiagnosticHash = diagnosticHash },
            transaction,
            cancellationToken: cancellationToken));

    private static Task InsertOutboxAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        AgentRunRow run,
        string eventKind,
        string payloadJson,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.WorkbenchOutboxEvents
                (EventId, TenantId, ProjectId, WorkbenchSessionId, AgentRunId, EventKind, PayloadJson, ClientOperationId)
            VALUES
                (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, @AgentRunId, @EventKind, @PayloadJson, @ClientOperationId);
            """,
            new
            {
                run.TenantId,
                run.ProjectId,
                run.WorkbenchSessionId,
                run.AgentRunId,
                EventKind = eventKind,
                PayloadJson = payloadJson,
                run.ClientOperationId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task<bool> ValidateOutputSourceReferencesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        AgentRunRow run,
        WorkbenchBusinessAnalystContext storedContext,
        WorkbenchBusinessAnalystOutput output,
        CancellationToken cancellationToken)
    {
        if (output.OutputSchemaVersion == WorkbenchBusinessAnalystContract.OutputSchemaVersion1)
            return true;

        var sourceMessageIds = (output.UnderstandingPatch?.FactChanges ?? [])
            .SelectMany(change => change.SourceMessageIds)
            .Concat(output.RenameProposal?.SourceMessageIds ?? [])
            .Concat(output.TicketProposalSet?.SourceMessageIds ?? [])
            .Concat(output.TicketProposalSet?.Proposals.SelectMany(value => value.SourceMessageIds) ?? [])
            .Concat(output.TicketProposalSet?.OpenQuestions.SelectMany(value => value.SourceMessageIds) ?? [])
            .Concat(output.TicketProposalSet?.PotentialConflicts.SelectMany(value => value.SourceMessageIds) ?? [])
            .Distinct()
            .ToArray();
        if (sourceMessageIds.Length == 0)
            return true;

        if (output.OutputSchemaVersion == WorkbenchBusinessAnalystContract.OutputSchemaVersion3)
        {
            var frozenSourceIds = storedContext.Messages
                .Where(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                .Select(message => message.MessageId)
                .ToHashSet();
            var understanding = ProjectUnderstandingDocumentCodec.Deserialize(storedContext.UnderstandingJson);
            frozenSourceIds.UnionWith(understanding.Facts.SelectMany(fact => fact.SourceMessageIds));
            frozenSourceIds.UnionWith(understanding.Conflicts.SelectMany(conflict => conflict.SourceMessageIds));
            if (storedContext.TicketProposalSnapshotJson is not null)
            {
                var reviewed = TicketProposalSetDocumentCodec.Deserialize(
                    storedContext.TicketProposalSnapshotJson);
                frozenSourceIds.UnionWith(reviewed.SourceMessageIds);
                frozenSourceIds.UnionWith(reviewed.Proposals.SelectMany(proposal => proposal.SourceMessageIds));
                frozenSourceIds.UnionWith(reviewed.OpenQuestions.SelectMany(issue => issue.SourceMessageIds));
                frozenSourceIds.UnionWith(reviewed.PotentialConflicts.SelectMany(issue => issue.SourceMessageIds));
            }
            if (sourceMessageIds.Any(id => !frozenSourceIds.Contains(id)))
                return false;
        }

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            output.OutputSchemaVersion == WorkbenchBusinessAnalystContract.OutputSchemaVersion3
                ? """
            SELECT COUNT(1)
            FROM dbo.ChatMessages message WITH (HOLDLOCK)
            WHERE message.TenantId=@TenantId AND message.ProjectId=@ProjectId
              AND message.Role=N'user' AND message.Id IN @SourceMessageIds;
            """
                : """
            SELECT COUNT(1)
            FROM dbo.ChatMessages message WITH (HOLDLOCK)
            WHERE message.TenantId=@TenantId AND message.ProjectId=@ProjectId
              AND message.ChatSessionId=@ChatSessionId AND message.Role=N'user'
              AND message.Id IN @SourceMessageIds;
            """,
            new
            {
                run.TenantId,
                run.ProjectId,
                run.ChatSessionId,
                SourceMessageIds = sourceMessageIds
            },
            transaction,
            cancellationToken: cancellationToken));
        return count == sourceMessageIds.Length;
    }

    private static async Task ApplyUnderstandingOutputAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        AgentRunRow run,
        WorkbenchBusinessAnalystContext storedContext,
        WorkbenchBusinessAnalystOutput output,
        CancellationToken cancellationToken)
    {
        if (output.OutputSchemaVersion is WorkbenchBusinessAnalystContract.OutputSchemaVersion1 or
            WorkbenchBusinessAnalystContract.OutputSchemaVersion3)
            return;

        var current = await connection.QuerySingleOrDefaultAsync<UnderstandingMaterializationRow>(new CommandDefinition(
            """
            SELECT TOP (1) understanding.Revision, understanding.UnderstandingJson, project.Name AS ProjectName
            FROM dbo.ProjectUnderstandings understanding WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.Projects project WITH (UPDLOCK, HOLDLOCK)
                ON project.TenantId=understanding.TenantId AND project.Id=understanding.ProjectId
            WHERE understanding.TenantId=@TenantId AND understanding.ProjectId=@ProjectId
            ORDER BY understanding.Revision DESC;
            """,
            run,
            transaction,
            cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException("The project understanding required for materialization is missing.");

        // Decoding the frozen base is intentional even when the latest revision advanced: it
        // proves the result was based on a valid typed document or an explicit PR-01 placeholder.
        var baseDocument = ProjectUnderstandingDocumentCodec.Deserialize(storedContext.UnderstandingJson);
        var document = ProjectUnderstandingDocumentCodec.Deserialize(current.UnderstandingJson);
        var nextRevision = checked(current.Revision + 1);
        var facts = document.Facts.ToDictionary(value => value.Key, StringComparer.Ordinal);
        var baseFacts = baseDocument.Facts.ToDictionary(value => value.Key, StringComparer.Ordinal);
        var conflicts = document.Conflicts.ToList();
        var changed = false;

        foreach (var proposed in output.UnderstandingPatch?.FactChanges ?? [])
        {
            var value = proposed.Value.Trim();
            var evidence = proposed.EvidenceSummary.Trim();
            var sources = proposed.SourceMessageIds.Distinct().Order().ToArray();
            if (!facts.TryGetValue(proposed.Key, out var currentFact))
            {
                facts[proposed.Key] = AgentFact(proposed.Key, value, proposed.State, sources, evidence, run, nextRevision);
                changed = true;
                continue;
            }

            if (string.Equals(currentFact.Value, value, StringComparison.Ordinal))
            {
                if (currentFact.State == ProjectUnderstandingFactStates.Inferred &&
                    proposed.State == ProjectUnderstandingFactStates.Confirmed)
                {
                    facts[proposed.Key] = AgentFact(
                        proposed.Key,
                        value,
                        ProjectUnderstandingFactStates.Confirmed,
                        sources,
                        evidence,
                        run,
                        nextRevision) with { UserLocked = currentFact.UserLocked };
                    changed = true;
                }
                continue;
            }

            var changedSinceFrozenBase = !baseFacts.TryGetValue(proposed.Key, out var baseFact) ||
                !SameFactSemantics(baseFact, currentFact);
            if (currentFact.State == ProjectUnderstandingFactStates.Inferred &&
                !currentFact.UserLocked &&
                !changedSinceFrozenBase)
            {
                facts[proposed.Key] = AgentFact(
                    proposed.Key,
                    value,
                    proposed.State,
                    sources,
                    evidence,
                    run,
                    nextRevision);
                changed = true;
                continue;
            }

            var duplicate = conflicts.Any(conflict =>
                conflict.Status == ProjectUnderstandingConflictStates.Open &&
                conflict.FactKey == proposed.Key &&
                string.Equals(conflict.CurrentValue, currentFact.Value, StringComparison.Ordinal) &&
                string.Equals(conflict.ProposedValue, value, StringComparison.Ordinal));
            if (!duplicate)
            {
                conflicts.Add(new ProjectUnderstandingConflict(
                    Guid.NewGuid(),
                    proposed.Key,
                    currentFact.Value,
                    value,
                    sources,
                    evidence,
                    run.AgentRunId,
                    nextRevision,
                    ProjectUnderstandingConflictStates.Open));
                changed = true;
            }
            if (currentFact.State != ProjectUnderstandingFactStates.Conflicted)
            {
                facts[proposed.Key] = currentFact with
                {
                    State = ProjectUnderstandingFactStates.Conflicted,
                    Revision = nextRevision
                };
                changed = true;
            }
        }

        var openQuestions = document.OpenQuestions;
        if (output.UnderstandingPatch?.OpenQuestions is { } proposedQuestions &&
            !openQuestions.SequenceEqual(proposedQuestions, StringComparer.Ordinal))
        {
            openQuestions = proposedQuestions.Select(value => value.Trim()).ToArray();
            changed = true;
        }

        if (changed)
        {
            var next = new ProjectUnderstandingDocument(
                ProjectUnderstandingContract.SchemaVersion,
                facts.Values.ToArray(),
                conflicts,
                openQuestions);
            var understandingJson = ProjectUnderstandingDocumentCodec.Serialize(next);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ProjectUnderstandings
                SET Status=N'Superseded'
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Revision=@CurrentRevision;

                INSERT dbo.ProjectUnderstandings
                    (TenantId, ProjectId, Revision, Status, UnderstandingJson, DocumentSchemaVersion,
                     BasedOnRevision, CreatedByActorUserId, CreatedByAgentRunId)
                VALUES
                    (@TenantId, @ProjectId, @NextRevision, N'Draft', @UnderstandingJson, 1,
                     @BasedOnRevision, @ActorUserId, @AgentRunId);
                """,
                new
                {
                    run.TenantId,
                    run.ProjectId,
                    CurrentRevision = current.Revision,
                    NextRevision = nextRevision,
                    UnderstandingJson = understandingJson,
                    BasedOnRevision = output.BasedOnUnderstandingRevision,
                    run.ActorUserId,
                    run.AgentRunId
                },
                transaction,
                cancellationToken: cancellationToken));
            await InsertOutboxAsync(
                connection,
                transaction,
                run,
                "ProjectUnderstandingRevised",
                JsonSerializer.Serialize(new
                {
                    run.AgentRunId,
                    revision = nextRevision,
                    basedOnRevision = output.BasedOnUnderstandingRevision
                }, JsonOptions),
                cancellationToken);
        }

        if (output.RenameProposal is not { } rename)
            return;
        var proposedName = rename.ProposedName.Trim();
        if (string.Equals(current.ProjectName, proposedName, StringComparison.Ordinal))
            return;

        var existingPending = await connection.QuerySingleOrDefaultAsync<PendingRenameRow>(new CommandDefinition(
            """
            SELECT TOP (1) ProposalId, ProposedName, BasedOnProjectName
            FROM dbo.ProjectRenameProposals WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Status=N'Pending';
            """,
            run,
            transaction,
            cancellationToken: cancellationToken));
        if (existingPending is not null &&
            string.Equals(existingPending.ProposedName, proposedName, StringComparison.Ordinal) &&
            string.Equals(existingPending.BasedOnProjectName, current.ProjectName, StringComparison.Ordinal))
            return;

        if (existingPending is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ProjectRenameProposals
                SET Status=N'Superseded', DecisionAtUtc=SYSUTCDATETIME()
                WHERE ProposalId=@ProposalId AND Status=N'Pending';
                """,
                new { existingPending.ProposalId },
                transaction,
                cancellationToken: cancellationToken));
        }

        var proposalId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.ProjectRenameProposals
                (ProposalId, TenantId, ProjectId, ProposedName, Status, BasedOnProjectName,
                 BasedOnUnderstandingRevision, ProposedByAgentRunId, InitiatingActorUserId,
                 SourceMessageIdsJson, EvidenceSummary)
            VALUES
                (@ProposalId, @TenantId, @ProjectId, @ProposedName, N'Pending', @BasedOnProjectName,
                 @BasedOnUnderstandingRevision, @AgentRunId, @ActorUserId,
                 @SourceMessageIdsJson, @EvidenceSummary);
            """,
            new
            {
                ProposalId = proposalId,
                run.TenantId,
                run.ProjectId,
                ProposedName = proposedName,
                BasedOnProjectName = current.ProjectName,
                BasedOnUnderstandingRevision = output.BasedOnUnderstandingRevision,
                run.AgentRunId,
                run.ActorUserId,
                SourceMessageIdsJson = JsonSerializer.Serialize(
                    rename.SourceMessageIds.Distinct().Order().ToArray(),
                    JsonOptions),
                EvidenceSummary = rename.EvidenceSummary.Trim()
            },
            transaction,
            cancellationToken: cancellationToken));
        await InsertOutboxAsync(
            connection,
            transaction,
            run,
            "ProjectRenameProposed",
            JsonSerializer.Serialize(new { run.AgentRunId, proposalId }, JsonOptions),
            cancellationToken);
    }

    private static async Task<long?> ApplyTicketProposalOutputAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        AgentRunRow run,
        WorkbenchBusinessAnalystContext storedContext,
        WorkbenchBusinessAnalystOutput output,
        CancellationToken cancellationToken)
    {
        if (!WorkbenchAgentInvocationKinds.IsTicketProposal(run.InvocationKind))
            return null;
        if (output.OutputSchemaVersion != WorkbenchBusinessAnalystContract.OutputSchemaVersion3 ||
            output.TicketProposalSet is null || run.TicketProposalSetId is null)
            throw new InvalidOperationException("The proposal-purpose run has no validated ticket proposal artifact.");

        var now = DateTime.UtcNow;
        ExistingTicketProposalSetRow? existing = null;
        long revision;
        DateTime createdAtUtc;
        Guid createdByAgentRunId;
        if (run.InvocationKind == WorkbenchAgentInvocationKinds.TicketProposalRegeneration)
        {
            existing = await connection.QuerySingleOrDefaultAsync<ExistingTicketProposalSetRow>(new CommandDefinition(
                """
                SELECT Id, CurrentRevision, CreatedByAgentRunId, CreatedAtUtc
                FROM dbo.TicketProposalSets WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@TicketProposalSetId
                  AND WorkbenchSessionId=@WorkbenchSessionId;
                """,
                run,
                transaction,
                cancellationToken: cancellationToken));
            if (existing is null || existing.CurrentRevision != run.TicketProposalRevision)
                throw new TicketProposalRevisionConflictException(existing?.CurrentRevision ?? 0);
            revision = checked(existing.CurrentRevision + 1);
            createdAtUtc = existing.CreatedAtUtc;
            createdByAgentRunId = existing.CreatedByAgentRunId;
        }
        else
        {
            var duplicate = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM dbo.TicketProposalSets WITH (UPDLOCK, HOLDLOCK) WHERE Id=@TicketProposalSetId;",
                run,
                transaction,
                cancellationToken: cancellationToken));
            if (duplicate != 0)
                throw new InvalidOperationException("The server-owned ticket proposal set identifier is already in use.");
            revision = 1;
            createdAtUtc = now;
            createdByAgentRunId = run.AgentRunId;
        }

        var document = TicketProposalSetDocumentCodec.Materialize(
            output.TicketProposalSet,
            run.ProjectId,
            run.WorkbenchSessionId,
            run.LeaseEpoch,
            storedContext.UnderstandingRevision,
            createdByAgentRunId,
            run.TicketProposalSetId,
            revision,
            createdAtUtc,
            now,
            output.Outcome);
        var snapshotJson = TicketProposalSetDocumentCodec.Serialize(document);
        var snapshotHash = TicketProposalSetDocumentCodec.ComputeHash(snapshotJson);
        var sourceMessageIdsJson = JsonSerializer.Serialize(document.SourceMessageIds, JsonOptions);

        if (existing is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.TicketProposalSets
                    (Id, TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch, CurrentRevision,
                     BasedOnUnderstandingRevision, Status, SplitReason, SourceMessageIdsJson,
                     CreatedByAgentRunId, CreatedByActorUserId, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@TicketProposalSetId, @TenantId, @ProjectId, @WorkbenchSessionId, @LeaseEpoch,
                     @Revision, @BasedOnUnderstandingRevision, @Status, @SplitReason,
                     @SourceMessageIdsJson, @CreatedByAgentRunId, @ActorUserId, @CreatedAtUtc, @UpdatedAtUtc);
                """,
                new
                {
                    document.TicketProposalSetId,
                    run.TenantId,
                    run.ProjectId,
                    run.WorkbenchSessionId,
                    run.LeaseEpoch,
                    document.Revision,
                    document.BasedOnUnderstandingRevision,
                    document.Status,
                    document.SplitReason,
                    SourceMessageIdsJson = sourceMessageIdsJson,
                    document.CreatedByAgentRunId,
                    run.ActorUserId,
                    document.CreatedAtUtc,
                    document.UpdatedAtUtc
                },
                transaction,
                cancellationToken: cancellationToken));
        }
        else
        {
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.TicketProposalSets
                SET CurrentRevision=@Revision,
                    BasedOnUnderstandingRevision=@BasedOnUnderstandingRevision,
                    Status=@Status, SplitReason=@SplitReason,
                    SourceMessageIdsJson=@SourceMessageIdsJson, UpdatedAtUtc=@UpdatedAtUtc
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@TicketProposalSetId
                  AND CurrentRevision=@ExpectedRevision;
                """,
                new
                {
                    run.TenantId,
                    run.ProjectId,
                    document.TicketProposalSetId,
                    document.Revision,
                    document.BasedOnUnderstandingRevision,
                    document.Status,
                    document.SplitReason,
                    SourceMessageIdsJson = sourceMessageIdsJson,
                    document.UpdatedAtUtc,
                    ExpectedRevision = run.TicketProposalRevision
                },
                transaction,
                cancellationToken: cancellationToken));
            if (updated != 1)
                throw new TicketProposalRevisionConflictException(existing.CurrentRevision);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.TicketProposalSetRevisions
                (TenantId, ProjectId, TicketProposalSetId, Revision, SnapshotJson, SnapshotHash,
                 ActorUserId, AgentRunId, ChangeKind, CreatedAtUtc)
            VALUES
                (@TenantId, @ProjectId, @TicketProposalSetId, @Revision, @SnapshotJson, @SnapshotHash,
                 @ActorUserId, @AgentRunId, @ChangeKind, @UpdatedAtUtc);
            """,
            new
            {
                run.TenantId,
                run.ProjectId,
                document.TicketProposalSetId,
                document.Revision,
                SnapshotJson = snapshotJson,
                SnapshotHash = snapshotHash,
                run.ActorUserId,
                AgentRunId = run.AgentRunId,
                ChangeKind = existing is null
                    ? TicketProposalRevisionChangeKinds.Generated
                    : TicketProposalRevisionChangeKinds.Regenerated,
                document.UpdatedAtUtc
            },
            transaction,
            cancellationToken: cancellationToken));

        return revision;
    }

    private static ProjectUnderstandingFact AgentFact(
        string key,
        string value,
        string state,
        IReadOnlyList<long> sources,
        string evidence,
        AgentRunRow run,
        long revision) =>
        new(
            key,
            value,
            state,
            UserLocked: false,
            ProjectUnderstandingAuthorKinds.Agent,
            AuthorActorUserId: null,
            run.AgentRunId,
            sources,
            evidence,
            revision);

    private static bool SameFactSemantics(
        ProjectUnderstandingFact left,
        ProjectUnderstandingFact right) =>
        string.Equals(left.Value, right.Value, StringComparison.Ordinal) &&
        string.Equals(left.State, right.State, StringComparison.Ordinal) &&
        left.UserLocked == right.UserLocked;

    private static Task CompleteAttemptAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid claimToken,
        string outcome,
        string responseHash,
        string? diagnosticCode,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.WorkbenchAgentRunAttempts
            SET Outcome=COALESCE(Outcome, @Outcome), ResponseHash=COALESCE(ResponseHash, @ResponseHash),
                DiagnosticCode=COALESCE(DiagnosticCode, @DiagnosticCode),
                CompletedAtUtc=COALESCE(CompletedAtUtc, SYSUTCDATETIME())
            WHERE ClaimToken=@ClaimToken;
            """,
            new { ClaimToken = claimToken, Outcome = outcome, ResponseHash = responseHash, DiagnosticCode = diagnosticCode },
            transaction,
            cancellationToken: cancellationToken));

    private static Task CloseAttemptWithoutResponseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid claimToken,
        string outcome,
        string diagnosticCode,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.WorkbenchAgentRunAttempts
            SET Outcome=@Outcome, DiagnosticCode=@DiagnosticCode, CompletedAtUtc=SYSUTCDATETIME()
            WHERE ClaimToken=@ClaimToken AND CompletedAtUtc IS NULL;
            """,
            new { ClaimToken = claimToken, Outcome = outcome, DiagnosticCode = diagnosticCode },
            transaction,
            cancellationToken: cancellationToken));

    private static Task CloseUnfinishedAttemptAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid agentRunId,
        string outcome,
        string diagnosticCode,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.WorkbenchAgentRunAttempts
            SET Outcome=@Outcome, DiagnosticCode=@DiagnosticCode, CompletedAtUtc=SYSUTCDATETIME()
            WHERE AgentRunId=@AgentRunId AND CompletedAtUtc IS NULL;
            """,
            new { AgentRunId = agentRunId, Outcome = outcome, DiagnosticCode = diagnosticCode },
            transaction,
            cancellationToken: cancellationToken));

    // PR-02C-A deliberately has no failed-run retry mutation. The durable run is uniquely
    // bound to its source user message and audited attempts; only the original submit
    // operation may be replayed to recover its receipt. A new user turn is a new message.
    private static WorkbenchAgentRunSnapshot ToSnapshot(AgentRunRow run) => new(
        run.AgentRunId,
        run.TenantId,
        run.ProjectId,
        run.WorkbenchSessionId,
        run.LeaseEpoch,
        run.ActorUserId,
        run.ChatSessionId,
        run.SourceUserMessageId,
        run.Status,
        run.AttemptCount,
        run.AssistantMessageId,
        run.CreatedAtUtc,
        run.StartedAtUtc,
        run.CompletedAtUtc,
        run.CancellationRequestedAtUtc,
        MapFailureCategory(run),
        Retryable: false,
        run.InvocationKind,
        run.TicketProposalSetId,
        run.MaterializedTicketProposalRevision);

    private static string? MapFailureCategory(AgentRunRow run)
    {
        if (run.Status is WorkbenchAgentRunStates.Stale or WorkbenchAgentRunStates.Superseded)
            return WorkbenchAgentRunFailureCategories.AuthorityChanged;
        if (run.Status != WorkbenchAgentRunStates.Failed)
            return null;

        return run.DiagnosticCode switch
        {
            "agent_provider_timeout" => WorkbenchAgentRunFailureCategories.ProviderTimeout,
            "agent_output_schema_invalid" => WorkbenchAgentRunFailureCategories.InvalidResponse,
            "agent_context_too_large" => WorkbenchAgentRunFailureCategories.ContextTooLarge,
            "agent_contract_version_unsupported" or
            "agent_provider_timeout_invalid" or
            "agent_provider_role_hierarchy_unsupported" or
            "agent_provider_envelope_invalid" =>
                WorkbenchAgentRunFailureCategories.Configuration,
            _ => WorkbenchAgentRunFailureCategories.ExecutionFailed
        };
    }

    internal static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static string BuildInputResourceScope(int projectId, long workbenchSessionId) =>
        $"project:{projectId}:workbench-session:{workbenchSessionId}:input";

    internal static string ComputeInputPayloadHash(
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        long? chatSessionId,
        string composerText) =>
        ComputeHash(JsonSerializer.Serialize(new
        {
            v = 1,
            projectId,
            workbenchSessionId,
            leaseEpoch,
            chatSessionId,
            message = composerText
        }, JsonOptions));

    private sealed class ClientOperationRow
    {
        public string PayloadHash { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }

    private sealed class UnderstandingMaterializationRow
    {
        public long Revision { get; init; }
        public string UnderstandingJson { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
    }

    private sealed class PendingRenameRow
    {
        public Guid ProposalId { get; init; }
        public string ProposedName { get; init; } = string.Empty;
        public string BasedOnProjectName { get; init; } = string.Empty;
    }

    private sealed class ExistingTicketProposalSetRow
    {
        public Guid Id { get; init; }
        public long CurrentRevision { get; init; }
        public Guid CreatedByAgentRunId { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class AgentRunRow
    {
        public Guid AgentRunId { get; init; }
        public int TenantId { get; init; }
        public int ProjectId { get; init; }
        public long WorkbenchSessionId { get; init; }
        public long LeaseEpoch { get; init; }
        public int ActorUserId { get; init; }
        public long ChatSessionId { get; init; }
        public long SourceUserMessageId { get; init; }
        public Guid ClientOperationId { get; init; }
        public string AgentVersion { get; init; } = string.Empty;
        public string PromptVersion { get; init; } = string.Empty;
        public string ToolPolicyVersion { get; init; } = string.Empty;
        public int ContextSchemaVersion { get; init; }
        public int ContextCanonicalizationVersion { get; init; }
        public int OutputSchemaVersion { get; init; }
        public string InvocationKind { get; init; } = WorkbenchAgentInvocationKinds.Conversation;
        public string? TicketInstruction { get; init; }
        public Guid? TicketProposalSetId { get; init; }
        public long? TicketProposalRevision { get; init; }
        public long? MaterializedTicketProposalRevision { get; init; }
        public string Status { get; init; } = string.Empty;
        public int AttemptCount { get; init; }
        public Guid? ClaimToken { get; init; }
        public DateTime? ClaimExpiresAtUtc { get; init; }
        public long? AssistantMessageId { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? StartedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public DateTime? CancellationRequestedAtUtc { get; init; }
        public string? ContextSnapshotJson { get; init; }
        public string? ContextHash { get; init; }
        public long? BasedOnUnderstandingRevision { get; init; }
        public string? OutputHash { get; init; }
        public string? DiagnosticCode { get; init; }
    }

    private sealed class SuccessorFenceRow
    {
        public long WorkbenchSessionId { get; init; }
        public long LeaseEpoch { get; init; }
    }

    private sealed class WorkbenchSessionChatBindingRow
    {
        public long? ActiveChatSessionId { get; init; }
    }
}
