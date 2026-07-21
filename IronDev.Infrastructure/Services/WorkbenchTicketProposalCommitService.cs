using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Deterministically commits one exact reviewed proposal revision. No BA/model call
/// occurs here: tickets, Work Items, provenance, receipts, and lifecycle share one
/// serializable transaction.
/// </summary>
public sealed class WorkbenchTicketProposalCommitService : IWorkbenchTicketProposalCommitService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connections;
    private readonly ITicketProposalCommitFailureInjector _failureInjector;

    public WorkbenchTicketProposalCommitService(
        IDbConnectionFactory connections,
        ITicketProposalCommitFailureInjector failureInjector)
    {
        _connections = connections;
        _failureInjector = failureInjector;
    }

    public async Task<TicketProposalCommitResult> CommitAsync(
        CommitTicketProposalSetCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var resourceScope =
            $"project:{command.ProjectId}:ticket-proposal-set:{command.TicketProposalSetId:D}";
        var payloadHash = Hash(JsonSerializer.Serialize(new
        {
            v = 1,
            command.ProjectId,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            command.TicketProposalSetId,
            command.ExpectedProposalSetRevision
        }, JsonOptions));

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(connection, transaction, command, cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();

            var existing = await ReadOperationAsync(
                connection, transaction, command, resourceScope, cancellationToken);
            if (existing is not null)
            {
                EnsureMatchingOperation(existing, payloadHash);
                var replay = ReadStoredResult(existing) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command, cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var set = await ReadSetAsync(connection, transaction, command, cancellationToken)
                ?? throw new WorkbenchProjectNotAccessibleException();
            if (set.Status == TicketProposalSetStatuses.Committed)
                throw new TicketProposalAlreadyCommittedException();
            if (set.CurrentRevision != command.ExpectedProposalSetRevision)
                throw new TicketProposalRevisionConflictException(set.CurrentRevision);

            var reviewed = ReadDocument(set, command);
            if (reviewed.Status != TicketProposalSetStatuses.Ready)
                throw new TicketProposalSetNotReadyException();
            if (reviewed.OpenQuestions.Any(value => value.Status == TicketProposalIssueStatuses.Open) ||
                reviewed.PotentialConflicts.Any(value => value.Status == TicketProposalIssueStatuses.Open))
                throw new TicketProposalBlockingIssuesException();
            if (reviewed.Proposals.Any(
                    value => value.Title.Length > TicketProposalConstraints.MaximumTitleCharacters))
                throw new TicketProposalCommitBoundaryException();

            var lifecycle = await ReadLifecycleAsync(connection, transaction, command, cancellationToken)
                ?? throw new InvalidOperationException("The project lifecycle state is missing.");
            if (lifecycle.Phase != ProjectLifecyclePhases.Shaping)
                throw new TicketProposalProjectNotShapingException();
            var executionReadiness = await ReadExecutionReadinessAsync(
                    connection, transaction, command, cancellationToken)
                ?? throw new InvalidOperationException("The project readiness assessment is missing.");
            var sourceMessages = await ReadAndValidateSourceMessagesAsync(
                connection, transaction, command, reviewed, cancellationToken);
            var changedAtUtc = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();",
                transaction: transaction,
                cancellationToken: cancellationToken));

            var operationRecordId = await InsertOperationAsync(
                connection, transaction, command, resourceScope, payloadHash, cancellationToken);
            _failureInjector.ThrowIfRequested(TicketProposalCommitFailurePoint.ClientOperationCreated);

            var commitmentId = Guid.NewGuid();
            var createdTickets = new List<CreatedTicket>(reviewed.Proposals.Count);
            foreach (var proposal in reviewed.Proposals.OrderBy(value => value.SuggestedOrder))
            {
                var proposalSources = proposal.SourceMessageIds.Count > 0
                    ? proposal.SourceMessageIds.Distinct().Order().ToArray()
                    : reviewed.SourceMessageIds.Distinct().Order().ToArray();
                var firstSource = proposalSources.Length > 0
                    ? sourceMessages[proposalSources[0]]
                    : null;
                var acceptanceCriteria = FormatAcceptanceCriteria(proposal.AcceptanceCriteria);
                var provenance =
                    $"Created from reviewed Workbench ticket proposal {proposal.TicketProposalId:D} " +
                    $"in set {reviewed.TicketProposalSetId:D}, revision {reviewed.Revision}.";

                var ticketId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                    """
                    INSERT dbo.ProjectTickets
                        (TenantId, ProjectId, SessionId, Title, TicketType, Priority,
                         Summary, Background, Problem, AcceptanceCriteria, TechnicalNotes,
                         Status, Content, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols,
                         BlockedByTicketIds, UnitTests, IntegrationTests, ManualTests, RegressionTests,
                         BuildValidation, ContextSummary, IsGenerated, GenerationNote,
                         SourceChatSessionId, SourceChatMessageId, SourceDocumentVersionId)
                    OUTPUT inserted.Id
                    VALUES
                        (@TenantId, @ProjectId, NULL, @Title, N'Task', N'Medium',
                         @Summary, NULL, @Problem, @AcceptanceCriteria, @TechnicalNotes,
                         N'Draft', @Content, NULL, NULL, NULL,
                         NULL, NULL, NULL, NULL, NULL,
                         NULL, @ContextSummary, 1, @GenerationNote,
                         @SourceChatSessionId, @SourceChatMessageId, NULL);
                    """,
                    new
                    {
                        command.TenantId,
                        command.ProjectId,
                        proposal.Title,
                        Summary = proposal.ProposedChange,
                        proposal.Problem,
                        AcceptanceCriteria = acceptanceCriteria,
                        TechnicalNotes = provenance,
                        Content = FormatTicketContent(proposal),
                        ContextSummary = reviewed.SplitReason,
                        GenerationNote = provenance,
                        SourceChatSessionId = firstSource?.ChatSessionId,
                        SourceChatMessageId = firstSource?.Id
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                await InsertWorkItemAsync(
                    connection, transaction, command, reviewed, proposal, ticketId,
                    proposalSources, firstSource?.ChatSessionId, acceptanceCriteria,
                    provenance, cancellationToken);
                await InsertSourceReferencesAsync(
                    connection, transaction, command, proposal, ticketId, proposalSources,
                    cancellationToken);
                createdTickets.Add(new CreatedTicket(proposal, ticketId));
            }
            _failureInjector.ThrowIfRequested(TicketProposalCommitFailurePoint.TicketsCreated);

            var result = await CommitReviewedSetAsync(
                connection, transaction, command, reviewed, set, lifecycle, executionReadiness,
                operationRecordId, payloadHash, commitmentId, createdTickets, changedAtUtc,
                cancellationToken);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<TicketProposalCommitResult> CommitReviewedSetAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        TicketProposalSetDocument reviewed,
        ProposalSetRow set,
        LifecycleRow lifecycle,
        string executionReadiness,
        long operationRecordId,
        string payloadHash,
        Guid commitmentId,
        IReadOnlyList<CreatedTicket> createdTickets,
        DateTime changedAtUtc,
        CancellationToken cancellationToken)
    {
        var committed = reviewed with
        {
            Revision = checked(reviewed.Revision + 1),
            Status = TicketProposalSetStatuses.Committed,
            UpdatedAtUtc = changedAtUtc
        };
        var committedJson = TicketProposalSetDocumentCodec.Serialize(committed);
        var committedHash = TicketProposalSetDocumentCodec.ComputeHash(committedJson);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.TicketProposalSetRevisions
                (TenantId, ProjectId, TicketProposalSetId, Revision, SnapshotJson,
                 SnapshotHash, ActorUserId, AgentRunId, ChangeKind)
            VALUES
                (@TenantId, @ProjectId, @TicketProposalSetId, @Revision, @SnapshotJson,
                 @SnapshotHash, @ActorUserId, NULL, @ChangeKind);

            UPDATE dbo.TicketProposalSets
            SET CurrentRevision=@Revision,
                Status=N'Committed',
                CommittedRevision=@Revision,
                CommittedByActorUserId=@ActorUserId,
                CommittedByClientOperationId=@ClientOperationId,
                CommittedAtUtc=@CommittedAtUtc,
                UpdatedAtUtc=@CommittedAtUtc
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@TicketProposalSetId
              AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch
              AND CurrentRevision=@ReviewedRevision AND Status=N'Ready';

            IF @@ROWCOUNT <> 1
                THROW 51165, 'The reviewed ticket proposal revision is no longer current.', 1;
            """,
            new
            {
                command.TenantId,
                command.ProjectId,
                TicketProposalSetId = reviewed.TicketProposalSetId,
                Revision = committed.Revision,
                SnapshotJson = committedJson,
                SnapshotHash = committedHash,
                command.ActorUserId,
                ChangeKind = TicketProposalRevisionChangeKinds.Committed,
                command.ClientOperationId,
                CommittedAtUtc = changedAtUtc,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                ReviewedRevision = reviewed.Revision
            },
            transaction,
            cancellationToken: cancellationToken));
        _failureInjector.ThrowIfRequested(TicketProposalCommitFailurePoint.ProposalCommitted);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.TicketProposalCommitments
                (Id, TenantId, ProjectId, TicketProposalSetId, ReviewedRevision,
                 CommittedRevision, ReviewedSnapshotHash, ActorUserId, WorkbenchSessionId,
                 LeaseEpoch, ClientOperationId, PayloadHash, TicketCount, CommittedAtUtc)
            VALUES
                (@CommitmentId, @TenantId, @ProjectId, @TicketProposalSetId, @ReviewedRevision,
                 @CommittedRevision, @ReviewedSnapshotHash, @ActorUserId, @WorkbenchSessionId,
                 @LeaseEpoch, @ClientOperationId, @PayloadHash, @TicketCount, @CommittedAtUtc);
            """,
            new
            {
                CommitmentId = commitmentId,
                command.TenantId,
                command.ProjectId,
                TicketProposalSetId = reviewed.TicketProposalSetId,
                ReviewedRevision = reviewed.Revision,
                CommittedRevision = committed.Revision,
                ReviewedSnapshotHash = set.SnapshotHash,
                command.ActorUserId,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                command.ClientOperationId,
                PayloadHash = payloadHash,
                TicketCount = createdTickets.Count,
                CommittedAtUtc = changedAtUtc
            },
            transaction,
            cancellationToken: cancellationToken));

        var createdByProposal = createdTickets.ToDictionary(value => value.Proposal.TicketProposalId);
        foreach (var created in createdTickets)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.TicketProposalCommitmentTickets
                    (TenantId, ProjectId, TicketProposalCommitmentId, TicketProposalId,
                     ProjectTicketId, SuggestedOrder, CreatedAtUtc)
                VALUES
                    (@TenantId, @ProjectId, @CommitmentId, @TicketProposalId,
                     @ProjectTicketId, @SuggestedOrder, @CreatedAtUtc);
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    CommitmentId = commitmentId,
                    created.Proposal.TicketProposalId,
                    ProjectTicketId = created.TicketId,
                    created.Proposal.SuggestedOrder,
                    CreatedAtUtc = changedAtUtc
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        foreach (var created in createdTickets)
        {
            var blockedBy = created.Proposal.DependencyProposalIds
                .Select(value => createdByProposal[value].TicketId)
                .ToArray();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ProjectTickets
                SET BlockedByTicketIds=@BlockedByTicketIds
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@ProjectTicketId;
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    ProjectTicketId = created.TicketId,
                    BlockedByTicketIds = blockedBy.Length == 0 ? null : string.Join(',', blockedBy)
                },
                transaction,
                cancellationToken: cancellationToken));

            foreach (var dependsOnProposalId in created.Proposal.DependencyProposalIds)
            {
                var dependsOn = createdByProposal[dependsOnProposalId];
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT dbo.TicketProposalCommitmentDependencies
                        (TenantId, ProjectId, TicketProposalCommitmentId,
                         DependentTicketProposalId, DependentProjectTicketId,
                         DependsOnTicketProposalId, DependsOnProjectTicketId, CreatedAtUtc)
                    VALUES
                        (@TenantId, @ProjectId, @CommitmentId,
                         @DependentTicketProposalId, @DependentProjectTicketId,
                         @DependsOnTicketProposalId, @DependsOnProjectTicketId, @CreatedAtUtc);
                    """,
                    new
                    {
                        command.TenantId,
                        command.ProjectId,
                        CommitmentId = commitmentId,
                        DependentTicketProposalId = created.Proposal.TicketProposalId,
                        DependentProjectTicketId = created.TicketId,
                        DependsOnTicketProposalId = dependsOn.Proposal.TicketProposalId,
                        DependsOnProjectTicketId = dependsOn.TicketId,
                        CreatedAtUtc = changedAtUtc
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }
        _failureInjector.ThrowIfRequested(TicketProposalCommitFailurePoint.CommitmentRecorded);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.ProjectLifecyclePhases
                (TenantId, ProjectId, Revision, Phase, ChangedByActorUserId, ChangedAtUtc)
            VALUES
                (@TenantId, @ProjectId, @Revision, @Phase, @ActorUserId, @ChangedAtUtc);
            """,
            new
            {
                command.TenantId,
                command.ProjectId,
                Revision = checked(lifecycle.Revision + 1),
                Phase = ProjectLifecyclePhases.Delivery,
                ActorUserId = command.ActorUserId,
                ChangedAtUtc = changedAtUtc
            },
            transaction,
            cancellationToken: cancellationToken));
        _failureInjector.ThrowIfRequested(TicketProposalCommitFailurePoint.LifecycleAdvanced);

        var ticketResults = createdTickets
            .OrderBy(value => value.Proposal.SuggestedOrder)
            .Select(value => new CommittedProjectTicketReadModel(
                value.Proposal.TicketProposalId,
                value.TicketId,
                value.Proposal.Title,
                value.Proposal.SuggestedOrder,
                value.Proposal.DependencyProposalIds
                    .Select(id => createdByProposal[id].TicketId)
                    .ToArray()))
            .ToArray();
        var result = new TicketProposalCommitResult(
            TicketProposalSetReadModel.FromDocument(committed),
            new TicketProposalCommitReadModel(
                commitmentId,
                reviewed.TicketProposalSetId,
                reviewed.Revision,
                committed.Revision,
                set.SnapshotHash,
                command.ActorUserId,
                changedAtUtc,
                ticketResults),
            ProjectLifecyclePhases.Delivery,
            executionReadiness,
            command.ClientOperationId,
            IsReplay: false);

        await CompleteOperationAsync(
            connection, transaction, operationRecordId, command, result, cancellationToken);
        await InsertOutboxAndAttributionAsync(
            connection, transaction, command, commitmentId, result, cancellationToken);
        _failureInjector.ThrowIfRequested(TicketProposalCommitFailurePoint.OutboxRecorded);
        return result;
    }

    private static void Validate(CommitTicketProposalSetCommand command)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty || command.TicketProposalSetId == Guid.Empty ||
            command.ExpectedProposalSetRevision <= 0)
            throw new TicketProposalValidationException(
                "A project, current Workbench lease, proposal set, expected revision, and client operation ID are required.");
    }

    private static async Task<bool> CanAccessProjectAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
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
            new { command.TenantId, command.ActorUserId, command.ProjectId },
            transaction,
            cancellationToken: cancellationToken)) > 0;

    private static async Task<bool> ValidateAndRenewLeaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
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
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    private static Task<ProposalSetRow?> ReadSetAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ProposalSetRow>(new CommandDefinition(
            """
            SELECT value.CurrentRevision, value.Status,
                   revision.SnapshotJson, revision.SnapshotHash
            FROM dbo.TicketProposalSets value WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.TicketProposalSetRevisions revision
                ON revision.TenantId=value.TenantId AND revision.ProjectId=value.ProjectId
               AND revision.TicketProposalSetId=value.Id AND revision.Revision=value.CurrentRevision
            WHERE value.TenantId=@TenantId AND value.ProjectId=@ProjectId
              AND value.Id=@TicketProposalSetId
              AND value.WorkbenchSessionId=@WorkbenchSessionId AND value.LeaseEpoch=@LeaseEpoch;
            """,
            new
            {
                command.TenantId,
                command.ProjectId,
                command.TicketProposalSetId,
                command.WorkbenchSessionId,
                command.LeaseEpoch
            },
            transaction,
            cancellationToken: cancellationToken));

    private static TicketProposalSetDocument ReadDocument(
        ProposalSetRow row,
        CommitTicketProposalSetCommand command)
    {
        if (string.IsNullOrWhiteSpace(row.SnapshotJson) || string.IsNullOrWhiteSpace(row.SnapshotHash) ||
            !string.Equals(
                TicketProposalSetDocumentCodec.ComputeHash(row.SnapshotJson),
                row.SnapshotHash,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The stored ticket proposal-set revision failed its integrity check.");

        var document = TicketProposalSetDocumentCodec.Deserialize(row.SnapshotJson);
        if (document.TicketProposalSetId != command.TicketProposalSetId ||
            document.ProjectId != command.ProjectId ||
            document.WorkbenchSessionId != command.WorkbenchSessionId ||
            document.LeaseEpoch != command.LeaseEpoch ||
            document.Revision != row.CurrentRevision)
            throw new InvalidOperationException(
                "The stored ticket proposal-set revision identity is inconsistent.");
        return document;
    }

    private static Task<LifecycleRow?> ReadLifecycleAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<LifecycleRow>(new CommandDefinition(
            """
            SELECT TOP (1) Revision, Phase
            FROM dbo.ProjectLifecyclePhases WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
            ORDER BY Revision DESC;
            """,
            new { command.TenantId, command.ProjectId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<string?> ReadExecutionReadinessAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT TOP (1) ExecutionReadiness
            FROM dbo.ProjectReadinessAssessments WITH (HOLDLOCK)
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
            ORDER BY Revision DESC;
            """,
            new { command.TenantId, command.ProjectId },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task<IReadOnlyDictionary<long, SourceMessageRow>> ReadAndValidateSourceMessagesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        TicketProposalSetDocument document,
        CancellationToken cancellationToken)
    {
        var sourceIds = document.SourceMessageIds
            .Concat(document.Proposals.SelectMany(value => value.SourceMessageIds))
            .Concat(document.OpenQuestions.SelectMany(value => value.SourceMessageIds))
            .Concat(document.PotentialConflicts.SelectMany(value => value.SourceMessageIds))
            .Distinct()
            .Order()
            .ToArray();
        if (sourceIds.Length == 0)
            throw new InvalidOperationException(
                "A permanent ticket commitment requires server-owned source-message provenance.");

        var rows = (await connection.QueryAsync<SourceMessageRow>(new CommandDefinition(
            """
            SELECT Id, ChatSessionId
            FROM dbo.ChatMessages WITH (HOLDLOCK)
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id IN @SourceMessageIds;
            """,
            new
            {
                command.TenantId,
                command.ProjectId,
                SourceMessageIds = sourceIds
            },
            transaction,
            cancellationToken: cancellationToken))).ToArray();
        if (rows.Length != sourceIds.Length)
            throw new InvalidOperationException(
                "One or more ticket proposal source messages are outside the project scope.");
        return rows.ToDictionary(value => value.Id);
    }

    private static async Task InsertWorkItemAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        TicketProposalSetDocument reviewed,
        TicketProposalDocument proposal,
        long ticketId,
        IReadOnlyList<long> sourceMessageIds,
        long? sourceChatSessionId,
        string acceptanceCriteria,
        string provenance,
        CancellationToken cancellationToken)
    {
        var sourceMessageIdsJson = JsonSerializer.Serialize(sourceMessageIds, JsonOptions);
        var contractHash = Hash(JsonSerializer.Serialize(new
        {
            v = 1,
            reviewed.TicketProposalSetId,
            reviewedRevision = reviewed.Revision,
            proposal.TicketProposalId,
            proposal.Title,
            proposal.Problem,
            proposal.ProposedChange,
            proposal.AcceptanceCriteria,
            sourceMessageIds
        }, JsonOptions));

        var contractId = await connection.QuerySingleAsync<long>(new CommandDefinition(
            """
            INSERT dbo.WorkItems
                (Id, TenantId, ProjectId, Title, OriginKind, OriginReference, LegacyTicketId,
                 CurrentStage, CurrentState, CreatedByUserId, CreatedUtc, UpdatedUtc)
            VALUES
                (@TicketId, @TenantId, @ProjectId, @Title, N'Workshop', @OriginReference, @TicketId,
                 N'Ticket', N'Draft', @ActorUserId, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT dbo.WorkItemContracts
                (TenantId, ProjectId, WorkItemId, ContractVersion, SourceTicketId,
                 Title, Summary, Problem, AcceptanceCriteria, TechnicalNotes,
                 SourceWorkshopSessionId, SourceWorkshopMessageIds, CreatedByUserId, ContractHash)
            OUTPUT inserted.Id
            VALUES
                (@TenantId, @ProjectId, @TicketId, 1, @TicketId,
                 @Title, @Summary, @Problem, @AcceptanceCriteria, @TechnicalNotes,
                 @SourceWorkshopSessionId, @SourceWorkshopMessageIds, @ActorUserId, @ContractHash);
            """,
            new
            {
                TicketId = ticketId,
                command.TenantId,
                command.ProjectId,
                proposal.Title,
                OriginReference = $"TicketProposal:{proposal.TicketProposalId:D}",
                command.ActorUserId,
                Summary = proposal.ProposedChange,
                proposal.Problem,
                AcceptanceCriteria = acceptanceCriteria,
                TechnicalNotes = provenance,
                SourceWorkshopSessionId = sourceChatSessionId,
                SourceWorkshopMessageIds = sourceMessageIdsJson,
                ContractHash = contractHash
            },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.WorkItems
            SET CurrentContractId=@ContractId, UpdatedUtc=SYSUTCDATETIME()
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@TicketId;
            """,
            new { ContractId = contractId, command.TenantId, command.ProjectId, TicketId = ticketId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task InsertSourceReferencesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        TicketProposalDocument proposal,
        long ticketId,
        IReadOnlyList<long> sourceMessageIds,
        CancellationToken cancellationToken)
    {
        foreach (var sourceMessageId in sourceMessageIds)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.ArtifactSourceReferences
                    (TenantId, ProjectId, ArtifactType, ArtifactId, SourceType, SourceId,
                     ReferenceType, Summary, RelevanceScore, IsRequired, CreatedBy, CreatedUtc)
                VALUES
                    (@TenantId, @ProjectId, N'Ticket', @TicketId, N'ChatMessage', @SourceMessageId,
                     N'CreatedFrom', @Summary, 1, 1, @CreatedBy, SYSUTCDATETIME());
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    TicketId = ticketId,
                    SourceMessageId = sourceMessageId,
                    Summary = $"Source for ticket proposal {proposal.TicketProposalId:D}.",
                    CreatedBy = command.ActorUserId.ToString(CultureInfo.InvariantCulture)
                },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    private static Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        string resourceScope,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
            """
            SELECT Status, PayloadHash, CanonicalResultJson, ResultHash
            FROM dbo.ClientOperations WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
              AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
              AND ClientOperationId=@ClientOperationId;
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                OperationKind = TicketProposalCommitOperationKinds.Commit,
                ResourceScopeId = resourceScope,
                command.ClientOperationId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<long> InsertOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        string resourceScope,
        string payloadHash,
        CancellationToken cancellationToken) =>
        connection.QuerySingleAsync<long>(new CommandDefinition(
            """
            INSERT dbo.ClientOperations
                (TenantId, ActorUserId, OperationKind, ResourceScopeId,
                 ClientOperationId, PayloadHash, Status)
            OUTPUT inserted.Id
            VALUES
                (@TenantId, @ActorUserId, @OperationKind, @ResourceScopeId,
                 @ClientOperationId, @PayloadHash, N'Pending');
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                OperationKind = TicketProposalCommitOperationKinds.Commit,
                ResourceScopeId = resourceScope,
                command.ClientOperationId,
                PayloadHash = payloadHash
            },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task CompleteOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long operationRecordId,
        CommitTicketProposalSetCommand command,
        TicketProposalCommitResult result,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ClientOperations
            SET Status=N'Completed', ResultProjectId=@ProjectId,
                ResultWorkbenchSessionId=@WorkbenchSessionId,
                CanonicalResultJson=@CanonicalResultJson, ResultHash=@ResultHash,
                CompletedAtUtc=SYSUTCDATETIME()
            WHERE Id=@OperationRecordId;
            """,
            new
            {
                OperationRecordId = operationRecordId,
                command.ProjectId,
                command.WorkbenchSessionId,
                CanonicalResultJson = json,
                ResultHash = Hash(json)
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task InsertOutboxAndAttributionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CommitTicketProposalSetCommand command,
        Guid commitmentId,
        TicketProposalCommitResult result,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.WorkbenchOutboxEvents
                (EventId, TenantId, ProjectId, WorkbenchSessionId, EventKind,
                 PayloadJson, ClientOperationId, DedupeKey)
            VALUES
                (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId,
                 N'TicketProposalSetCommitted', @PayloadJson, @ClientOperationId, @DedupeKey);

            INSERT dbo.UserMutationAttribution
                (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId, TimestampUtc,
                 SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
            VALUES
                (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                 CONVERT(NVARCHAR(128), @ClientOperationId), NULL, SYSUTCDATETIME(),
                 N'Workbench', N'IronDev.Api', N'POST',
                 N'/api/workbench/projects/{projectId}/ticket-proposal-sets/{ticketProposalSetId}/commits',
                 N'Completed', 200);
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.ClientOperationId,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    command.ProjectId,
                    command.TicketProposalSetId,
                    commitmentId,
                    result.Commitment.ReviewedRevision,
                    result.Commitment.CommittedRevision,
                    ticketIds = result.Commitment.Tickets.Select(value => value.ProjectTicketId)
                }, JsonOptions),
                DedupeKey = $"ticket-proposal-commit:{commitmentId:D}"
            },
            transaction,
            cancellationToken: cancellationToken));

    private static void EnsureMatchingOperation(ClientOperationRow operation, string payloadHash)
    {
        if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            throw new ProjectStartOperationMismatchException();
    }

    private static TicketProposalCommitResult ReadStoredResult(ClientOperationRow row)
    {
        if (!string.Equals(row.Status, "Completed", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(row.CanonicalResultJson) ||
            string.IsNullOrWhiteSpace(row.ResultHash) ||
            !string.Equals(Hash(row.CanonicalResultJson), row.ResultHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The stored ticket proposal commitment failed its integrity check.");
        return JsonSerializer.Deserialize<TicketProposalCommitResult>(row.CanonicalResultJson, JsonOptions)
            ?? throw new InvalidOperationException(
                "The stored ticket proposal commitment could not be read.");
    }

    private static string FormatAcceptanceCriteria(IReadOnlyList<string> criteria) =>
        string.Join('\n', criteria.Select(value => $"- {value}"));

    private static string FormatTicketContent(TicketProposalDocument proposal) =>
        $"# {proposal.Title}\n\n" +
        $"## Problem\n\n{proposal.Problem}\n\n" +
        $"## Proposed change\n\n{proposal.ProposedChange}\n\n" +
        $"## Acceptance criteria\n\n{FormatAcceptanceCriteria(proposal.AcceptanceCriteria)}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class ProposalSetRow
    {
        public long CurrentRevision { get; init; }
        public string Status { get; init; } = string.Empty;
        public string SnapshotJson { get; init; } = string.Empty;
        public string SnapshotHash { get; init; } = string.Empty;
    }

    private sealed class LifecycleRow
    {
        public long Revision { get; init; }
        public string Phase { get; init; } = string.Empty;
    }

    private sealed class SourceMessageRow
    {
        public long Id { get; init; }
        public long ChatSessionId { get; init; }
    }

    private sealed class ClientOperationRow
    {
        public string Status { get; init; } = string.Empty;
        public string PayloadHash { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }

    private sealed record CreatedTicket(TicketProposalDocument Proposal, long TicketId);
}
