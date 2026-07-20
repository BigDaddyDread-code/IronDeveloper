using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchTicketProposalService : IWorkbenchTicketProposalService
{
    private const int MaximumTitleCharacters = 300;
    private const int MaximumProblemCharacters = 4_000;
    private const int MaximumProposedChangeCharacters = 8_000;
    private const int MaximumAcceptanceCriteria = 20;
    private const int MaximumAcceptanceCriterionCharacters = 2_000;
    private const int MaximumResolutionCharacters = 4_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connections;
    private readonly IWorkbenchAgentRunService _agentRuns;

    public WorkbenchTicketProposalService(
        IDbConnectionFactory connections,
        IWorkbenchAgentRunService agentRuns)
    {
        _connections = connections;
        _agentRuns = agentRuns;
    }

    public async Task<TicketProposalSetReadModel?> GetCurrentAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken = default)
    {
        ValidateReadIdentity(tenantId, actorUserId, projectId, workbenchSessionId, leaseEpoch);
        using var connection = _connections.CreateConnection();
        connection.Open();
        await EnsureReadFenceAsync(
            connection, tenantId, actorUserId, projectId, workbenchSessionId, leaseEpoch,
            cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<ProposalSetRow>(new CommandDefinition(
            """
            SELECT TOP (1) value.Id AS TicketProposalSetId, value.CurrentRevision,
                   revision.SnapshotJson, revision.SnapshotHash
            FROM dbo.TicketProposalSets value
            INNER JOIN dbo.TicketProposalSetRevisions revision
                ON revision.TenantId=value.TenantId AND revision.ProjectId=value.ProjectId
               AND revision.TicketProposalSetId=value.Id AND revision.Revision=value.CurrentRevision
            WHERE value.TenantId=@TenantId AND value.ProjectId=@ProjectId
              AND value.WorkbenchSessionId=@WorkbenchSessionId AND value.LeaseEpoch=@LeaseEpoch
            ORDER BY value.UpdatedAtUtc DESC, value.Id DESC;
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                LeaseEpoch = leaseEpoch
            },
            cancellationToken: cancellationToken));
        return row is null
            ? null
            : ToReadModel(ReadDocument(
                row, row.TicketProposalSetId, projectId, workbenchSessionId, leaseEpoch));
    }

    public async Task<TicketProposalSetReadModel> GetAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        Guid ticketProposalSetId,
        CancellationToken cancellationToken = default)
    {
        ValidateReadIdentity(tenantId, actorUserId, projectId, workbenchSessionId, leaseEpoch);
        if (ticketProposalSetId == Guid.Empty)
            throw new TicketProposalValidationException("ticketProposalSetId is required.");

        using var connection = _connections.CreateConnection();
        connection.Open();
        await EnsureReadFenceAsync(
            connection, tenantId, actorUserId, projectId, workbenchSessionId, leaseEpoch,
            cancellationToken);
        var row = await ReadSetAsync(
            connection, transaction: null, tenantId, projectId, workbenchSessionId, leaseEpoch,
            ticketProposalSetId, lockForUpdate: false, cancellationToken);
        return ToReadModel(ReadDocument(
            row ?? throw new WorkbenchProjectNotAccessibleException(),
            ticketProposalSetId,
            projectId,
            workbenchSessionId,
            leaseEpoch));
    }

    public async Task<IReadOnlyList<TicketProposalSetHistoryEntry>> GetHistoryAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        Guid ticketProposalSetId,
        CancellationToken cancellationToken = default)
    {
        ValidateReadIdentity(tenantId, actorUserId, projectId, workbenchSessionId, leaseEpoch);
        if (ticketProposalSetId == Guid.Empty)
            throw new TicketProposalValidationException("ticketProposalSetId is required.");

        using var connection = _connections.CreateConnection();
        connection.Open();
        await EnsureReadFenceAsync(
            connection, tenantId, actorUserId, projectId, workbenchSessionId, leaseEpoch,
            cancellationToken);

        var ownsSet = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.TicketProposalSets
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@TicketProposalSetId
              AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                TicketProposalSetId = ticketProposalSetId,
                WorkbenchSessionId = workbenchSessionId,
                LeaseEpoch = leaseEpoch
            },
            cancellationToken: cancellationToken)) == 1;
        if (!ownsSet)
            throw new WorkbenchProjectNotAccessibleException();

        var rows = await connection.QueryAsync<ProposalHistoryRow>(new CommandDefinition(
            """
            SELECT Revision, SnapshotJson, SnapshotHash, ActorUserId, AgentRunId,
                   ChangeKind, CreatedAtUtc
            FROM dbo.TicketProposalSetRevisions
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
              AND TicketProposalSetId=@TicketProposalSetId
            ORDER BY Revision DESC;
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                TicketProposalSetId = ticketProposalSetId
            },
            cancellationToken: cancellationToken));

        return rows.Select(row => new TicketProposalSetHistoryEntry(
                row.Revision,
                row.ChangeKind,
                row.ActorUserId,
                row.AgentRunId,
                row.CreatedAtUtc,
                ToReadModel(ReadDocument(
                    row,
                    ticketProposalSetId,
                    projectId,
                    workbenchSessionId,
                    leaseEpoch))))
            .ToArray();
    }

    public Task<TicketProposalSetMutationResult> EditAsync(
        EditTicketProposalCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationIdentity(
            command.TenantId, command.ActorUserId, command.ProjectId, command.WorkbenchSessionId,
            command.LeaseEpoch, command.ClientOperationId, command.TicketProposalSetId,
            command.ExpectedProposalSetRevision);
        if (command.TicketProposalId == Guid.Empty)
            throw new TicketProposalValidationException("ticketProposalId is required.");

        var title = NormalizeRequired(command.Title, MaximumTitleCharacters, "title");
        var problem = NormalizeRequired(command.Problem, MaximumProblemCharacters, "problem");
        var proposedChange = NormalizeRequired(
            command.ProposedChange, MaximumProposedChangeCharacters, "proposedChange");
        var acceptanceCriteria = NormalizeAcceptanceCriteria(command.AcceptanceCriteria);
        var payload = new
        {
            v = 1,
            command.ProjectId,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            command.TicketProposalSetId,
            command.TicketProposalId,
            command.ExpectedProposalSetRevision,
            title,
            problem,
            proposedChange,
            acceptanceCriteria
        };

        return MutateAsync(
            command.TenantId, command.ActorUserId, command.ProjectId, command.WorkbenchSessionId,
            command.LeaseEpoch, command.ClientOperationId, command.TicketProposalSetId,
            command.ExpectedProposalSetRevision, TicketProposalReviewOperationKinds.Edit,
            TicketProposalRevisionChangeKinds.Edited, payload,
            (document, revision, changedAtUtc) =>
            {
                if (document.Proposals.All(value => value.TicketProposalId != command.TicketProposalId))
                    throw new WorkbenchProjectNotAccessibleException();
                return document with
                {
                    Revision = revision,
                    UpdatedAtUtc = changedAtUtc,
                    Proposals = document.Proposals.Select(proposal =>
                            proposal.TicketProposalId == command.TicketProposalId
                                ? proposal with
                                {
                                    Title = title,
                                    Problem = problem,
                                    ProposedChange = proposedChange,
                                    AcceptanceCriteria = acceptanceCriteria
                                }
                                : proposal)
                        .ToArray()
                };
            },
            cancellationToken);
    }

    public Task<TicketProposalSetMutationResult> ReorderAsync(
        ReorderTicketProposalsCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationIdentity(
            command.TenantId, command.ActorUserId, command.ProjectId, command.WorkbenchSessionId,
            command.LeaseEpoch, command.ClientOperationId, command.TicketProposalSetId,
            command.ExpectedProposalSetRevision);
        if (command.OrderedProposalIds is null || command.OrderedProposalIds.Count == 0 ||
            command.OrderedProposalIds.Any(value => value == Guid.Empty) ||
            command.OrderedProposalIds.Distinct().Count() != command.OrderedProposalIds.Count)
            throw new TicketProposalValidationException(
                "orderedProposalIds must be a non-empty list of unique proposal IDs.");

        var orderedIds = command.OrderedProposalIds.ToArray();
        var payload = new
        {
            v = 1,
            command.ProjectId,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            command.TicketProposalSetId,
            command.ExpectedProposalSetRevision,
            orderedProposalIds = orderedIds
        };

        return MutateAsync(
            command.TenantId, command.ActorUserId, command.ProjectId, command.WorkbenchSessionId,
            command.LeaseEpoch, command.ClientOperationId, command.TicketProposalSetId,
            command.ExpectedProposalSetRevision, TicketProposalReviewOperationKinds.Reorder,
            TicketProposalRevisionChangeKinds.Reordered, payload,
            (document, revision, changedAtUtc) =>
            {
                var currentIds = document.Proposals.Select(value => value.TicketProposalId).ToHashSet();
                if (orderedIds.Length != currentIds.Count || orderedIds.Any(value => !currentIds.Contains(value)))
                    throw new TicketProposalValidationException(
                        "orderedProposalIds must be a complete permutation of the current proposal set.");
                var byId = document.Proposals.ToDictionary(value => value.TicketProposalId);
                var positions = orderedIds
                    .Select((id, index) => (id, index))
                    .ToDictionary(value => value.id, value => value.index);
                if (document.Proposals.Any(proposal => proposal.DependencyProposalIds.Any(
                        dependencyId => positions[dependencyId] >= positions[proposal.TicketProposalId])))
                    throw new TicketProposalDependencyException(
                        "A proposal cannot be ordered before one of its dependencies.");
                return document with
                {
                    Revision = revision,
                    UpdatedAtUtc = changedAtUtc,
                    Proposals = orderedIds.Select((id, index) =>
                            byId[id] with { SuggestedOrder = index + 1 })
                        .ToArray()
                };
            },
            cancellationToken);
    }

    public Task<TicketProposalSetMutationResult> RemoveAsync(
        RemoveTicketProposalCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationIdentity(
            command.TenantId, command.ActorUserId, command.ProjectId, command.WorkbenchSessionId,
            command.LeaseEpoch, command.ClientOperationId, command.TicketProposalSetId,
            command.ExpectedProposalSetRevision);
        if (command.TicketProposalId == Guid.Empty)
            throw new TicketProposalValidationException("ticketProposalId is required.");
        var payload = new
        {
            v = 1,
            command.ProjectId,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            command.TicketProposalSetId,
            command.TicketProposalId,
            command.ExpectedProposalSetRevision
        };

        return MutateAsync(
            command.TenantId, command.ActorUserId, command.ProjectId, command.WorkbenchSessionId,
            command.LeaseEpoch, command.ClientOperationId, command.TicketProposalSetId,
            command.ExpectedProposalSetRevision, TicketProposalReviewOperationKinds.Remove,
            TicketProposalRevisionChangeKinds.Removed, payload,
            (document, revision, changedAtUtc) =>
            {
                if (document.Proposals.All(value => value.TicketProposalId != command.TicketProposalId))
                    throw new WorkbenchProjectNotAccessibleException();
                if (document.Proposals.Count == 1)
                    throw new TicketProposalFinalRemovalException();
                if (document.Proposals.Any(value =>
                        value.TicketProposalId != command.TicketProposalId &&
                        value.DependencyProposalIds.Contains(command.TicketProposalId)))
                    throw new TicketProposalDependencyException(
                        "Remove proposals that depend on this proposal before removing it.");

                var remaining = document.Proposals
                    .Where(value => value.TicketProposalId != command.TicketProposalId)
                    .OrderBy(value => value.SuggestedOrder)
                    .Select((value, index) => value with { SuggestedOrder = index + 1 })
                    .ToArray();
                return document with
                {
                    Revision = revision,
                    UpdatedAtUtc = changedAtUtc,
                    Proposals = remaining
                };
            },
            cancellationToken);
    }

    public Task<TicketProposalSetMutationResult> ResolveIssueAsync(
        ResolveTicketProposalIssueCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationIdentity(
            command.TenantId, command.ActorUserId, command.ProjectId, command.WorkbenchSessionId,
            command.LeaseEpoch, command.ClientOperationId, command.TicketProposalSetId,
            command.ExpectedProposalSetRevision);
        if (command.IssueId == Guid.Empty)
            throw new TicketProposalValidationException("issueId is required.");
        var resolution = NormalizeRequired(command.Resolution, MaximumResolutionCharacters, "resolution");
        var payload = new
        {
            v = 1,
            command.ProjectId,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            command.TicketProposalSetId,
            command.IssueId,
            command.ExpectedProposalSetRevision,
            resolution
        };

        return MutateAsync(
            command.TenantId, command.ActorUserId, command.ProjectId, command.WorkbenchSessionId,
            command.LeaseEpoch, command.ClientOperationId, command.TicketProposalSetId,
            command.ExpectedProposalSetRevision, TicketProposalReviewOperationKinds.ResolveIssue,
            TicketProposalRevisionChangeKinds.IssueResolved, payload,
            (document, revision, changedAtUtc) =>
            {
                var issue = document.OpenQuestions.Concat(document.PotentialConflicts)
                    .SingleOrDefault(value => value.IssueId == command.IssueId);
                if (issue is null)
                    throw new WorkbenchProjectNotAccessibleException();
                if (issue.Status != TicketProposalIssueStatuses.Open)
                    throw new TicketProposalIssueNotOpenException();

                TicketProposalIssueDocument Resolve(TicketProposalIssueDocument value) =>
                    value.IssueId == command.IssueId
                        ? value with
                        {
                            Status = TicketProposalIssueStatuses.Resolved,
                            Resolution = resolution
                        }
                        : value;
                return document with
                {
                    Revision = revision,
                    UpdatedAtUtc = changedAtUtc,
                    OpenQuestions = document.OpenQuestions.Select(Resolve).ToArray(),
                    PotentialConflicts = document.PotentialConflicts.Select(Resolve).ToArray()
                };
            },
            cancellationToken);
    }

    public Task<SubmitWorkbenchAgentRunResult> RegenerateAsync(
        RegenerateTicketProposalSetCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationIdentity(
            command.TenantId, command.ActorUserId, command.ProjectId, command.WorkbenchSessionId,
            command.LeaseEpoch, command.ClientOperationId, command.TicketProposalSetId,
            command.ExpectedProposalSetRevision);
        if (command.ChatSessionId <= 0)
            throw new TicketProposalValidationException("chatSessionId is required.");
        var instruction = NormalizeRequired(
            command.Instruction, MaximumProblemCharacters, "instruction");
        return _agentRuns.SubmitAsync(
            new SubmitWorkbenchAgentRunCommand(
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch,
                command.ClientOperationId,
                command.ChatSessionId,
                $"/ticket {instruction}",
                WorkbenchAgentInvocationKinds.TicketProposalRegeneration,
                instruction,
                command.TicketProposalSetId,
                command.ExpectedProposalSetRevision),
            cancellationToken);
    }

    private async Task<TicketProposalSetMutationResult> MutateAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        Guid clientOperationId,
        Guid ticketProposalSetId,
        long expectedRevision,
        string operationKind,
        string changeKind,
        object payload,
        Func<TicketProposalSetDocument, long, DateTime, TicketProposalSetDocument> mutate,
        CancellationToken cancellationToken)
    {
        var resourceScope = $"project:{projectId}:ticket-proposal-set:{ticketProposalSetId:D}";
        var payloadHash = Hash(JsonSerializer.Serialize(payload, JsonOptions));

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(
                    connection, transaction, tenantId, actorUserId, projectId, cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();

            var existing = await ReadOperationAsync(
                connection, transaction, tenantId, actorUserId, operationKind, resourceScope,
                clientOperationId, cancellationToken);
            if (existing is not null)
            {
                EnsureMatchingOperation(existing, payloadHash);
                var replay = ReadStoredResult(existing) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            if (!await ValidateAndRenewLeaseAsync(
                    connection, transaction, tenantId, actorUserId, projectId, workbenchSessionId,
                    leaseEpoch, cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var currentRow = await ReadSetAsync(
                connection, transaction, tenantId, projectId, workbenchSessionId, leaseEpoch,
                ticketProposalSetId, lockForUpdate: true, cancellationToken)
                ?? throw new WorkbenchProjectNotAccessibleException();
            if (currentRow.CurrentRevision != expectedRevision)
                throw new TicketProposalRevisionConflictException(currentRow.CurrentRevision);

            var current = ReadDocument(
                currentRow, ticketProposalSetId, projectId, workbenchSessionId, leaseEpoch);
            var changedAtUtc = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();",
                transaction: transaction,
                cancellationToken: cancellationToken));
            var next = mutate(current, checked(current.Revision + 1), changedAtUtc);
            TicketProposalSetDocumentCodec.Validate(next);
            var snapshotJson = TicketProposalSetDocumentCodec.Serialize(next);
            var snapshotHash = TicketProposalSetDocumentCodec.ComputeHash(snapshotJson);

            var operationRecordId = await InsertOperationAsync(
                connection, transaction, tenantId, actorUserId, operationKind, resourceScope,
                clientOperationId, payloadHash, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.TicketProposalSetRevisions
                    (TenantId, ProjectId, TicketProposalSetId, Revision, SnapshotJson,
                     SnapshotHash, ActorUserId, AgentRunId, ChangeKind)
                VALUES
                    (@TenantId, @ProjectId, @TicketProposalSetId, @Revision, @SnapshotJson,
                     @SnapshotHash, @ActorUserId, NULL, @ChangeKind);
                """,
                new
                {
                    TenantId = tenantId,
                    ProjectId = projectId,
                    TicketProposalSetId = ticketProposalSetId,
                    Revision = next.Revision,
                    SnapshotJson = snapshotJson,
                    SnapshotHash = snapshotHash,
                    ActorUserId = actorUserId,
                    ChangeKind = changeKind
                },
                transaction,
                cancellationToken: cancellationToken));

            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.TicketProposalSets
                SET CurrentRevision=@NextRevision, Status=@Status, SplitReason=@SplitReason,
                    UpdatedAtUtc=@UpdatedAtUtc
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@TicketProposalSetId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch
                  AND CurrentRevision=@ExpectedRevision;
                """,
                new
                {
                    TenantId = tenantId,
                    ProjectId = projectId,
                    TicketProposalSetId = ticketProposalSetId,
                    WorkbenchSessionId = workbenchSessionId,
                    LeaseEpoch = leaseEpoch,
                    ExpectedRevision = expectedRevision,
                    NextRevision = next.Revision,
                    next.Status,
                    next.SplitReason,
                    UpdatedAtUtc = changedAtUtc
                },
                transaction,
                cancellationToken: cancellationToken));
            if (updated != 1)
                throw new TicketProposalRevisionConflictException(currentRow.CurrentRevision);

            var result = new TicketProposalSetMutationResult(
                ToReadModel(next), clientOperationId, IsReplay: false);
            await CompleteOperationAsync(
                connection, transaction, operationRecordId, projectId, workbenchSessionId, result,
                cancellationToken);
            await InsertOutboxAndAttributionAsync(
                connection, transaction, tenantId, actorUserId, projectId, workbenchSessionId,
                clientOperationId, ticketProposalSetId, next.Revision, changeKind, cancellationToken);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static TicketProposalSetReadModel ToReadModel(TicketProposalSetDocument document) =>
        TicketProposalSetReadModel.FromDocument(document);

    private static void ValidateReadIdentity(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch)
    {
        if (tenantId <= 0 || actorUserId <= 0 || projectId <= 0 ||
            workbenchSessionId <= 0 || leaseEpoch <= 0)
            throw new TicketProposalValidationException(
                "A current project and Workbench lease are required.");
    }

    private static void ValidateMutationIdentity(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        Guid clientOperationId,
        Guid ticketProposalSetId,
        long expectedRevision)
    {
        ValidateReadIdentity(tenantId, actorUserId, projectId, workbenchSessionId, leaseEpoch);
        if (clientOperationId == Guid.Empty || ticketProposalSetId == Guid.Empty || expectedRevision <= 0)
            throw new TicketProposalValidationException(
                "A proposal set, expected revision, and client operation ID are required.");
    }

    private static string NormalizeRequired(string? value, int maximumCharacters, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumCharacters)
            throw new TicketProposalValidationException(
                $"{field} is required and cannot exceed {maximumCharacters} characters.");
        return normalized;
    }

    private static IReadOnlyList<string> NormalizeAcceptanceCriteria(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count is < 1 or > MaximumAcceptanceCriteria)
            throw new TicketProposalValidationException(
                $"acceptanceCriteria must contain one to {MaximumAcceptanceCriteria} items.");
        var normalized = values.Select(value =>
                NormalizeRequired(value, MaximumAcceptanceCriterionCharacters, "acceptanceCriteria item"))
            .ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new TicketProposalValidationException("acceptanceCriteria cannot contain duplicates.");
        return normalized;
    }

    private static async Task EnsureReadFenceAsync(
        IDbConnection connection,
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessProjectAsync(
                connection, null, tenantId, actorUserId, projectId, cancellationToken))
            throw new WorkbenchProjectNotAccessibleException();
        if (!await HasCurrentLeaseAsync(
                connection, tenantId, actorUserId, projectId, workbenchSessionId, leaseEpoch,
                cancellationToken))
            throw new WorkbenchLeaseFenceException();
    }

    private static async Task<bool> CanAccessProjectAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
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

    private static async Task<bool> HasCurrentLeaseAsync(
        IDbConnection connection,
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.WorkbenchWriteLeases lease
            INNER JOIN dbo.WorkbenchSessions session
                ON session.TenantId=lease.TenantId AND session.ProjectId=lease.ProjectId
               AND session.Id=lease.WorkbenchSessionId AND session.Status=N'Active'
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
            cancellationToken: cancellationToken)) == 1;

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
            cancellationToken: cancellationToken)) == 1;

    private static Task<ProposalSetRow?> ReadSetAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        Guid ticketProposalSetId,
        bool lockForUpdate,
        CancellationToken cancellationToken)
    {
        var lockHint = lockForUpdate ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var sql = $"""
            SELECT value.Id AS TicketProposalSetId, value.CurrentRevision,
                   revision.SnapshotJson, revision.SnapshotHash
            FROM dbo.TicketProposalSets value{lockHint}
            INNER JOIN dbo.TicketProposalSetRevisions revision
                ON revision.TenantId=value.TenantId AND revision.ProjectId=value.ProjectId
               AND revision.TicketProposalSetId=value.Id AND revision.Revision=value.CurrentRevision
            WHERE value.TenantId=@TenantId AND value.ProjectId=@ProjectId AND value.Id=@TicketProposalSetId
              AND value.WorkbenchSessionId=@WorkbenchSessionId AND value.LeaseEpoch=@LeaseEpoch;
            """;
        return connection.QuerySingleOrDefaultAsync<ProposalSetRow>(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                LeaseEpoch = leaseEpoch,
                TicketProposalSetId = ticketProposalSetId
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static TicketProposalSetDocument ReadDocument(
        IProposalRevisionRow row,
        Guid expectedTicketProposalSetId,
        int expectedProjectId,
        long expectedWorkbenchSessionId,
        long expectedLeaseEpoch)
    {
        if (string.IsNullOrWhiteSpace(row.SnapshotJson) || string.IsNullOrWhiteSpace(row.SnapshotHash) ||
            !string.Equals(
                TicketProposalSetDocumentCodec.ComputeHash(row.SnapshotJson),
                row.SnapshotHash,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stored ticket proposal-set revision failed its integrity check.");
        var document = TicketProposalSetDocumentCodec.Deserialize(row.SnapshotJson);
        if (document.TicketProposalSetId != expectedTicketProposalSetId ||
            document.ProjectId != expectedProjectId ||
            document.WorkbenchSessionId != expectedWorkbenchSessionId ||
            document.LeaseEpoch != expectedLeaseEpoch ||
            document.Revision != row.Revision)
            throw new InvalidOperationException("The stored ticket proposal-set revision identity is inconsistent.");
        return document;
    }

    private static Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        string operationKind,
        string resourceScopeId,
        Guid clientOperationId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
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

    private static async Task<long> InsertOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        string operationKind,
        string resourceScopeId,
        Guid clientOperationId,
        string payloadHash,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleAsync<long>(new CommandDefinition(
            """
            INSERT dbo.ClientOperations
                (TenantId, ActorUserId, OperationKind, ResourceScopeId, ClientOperationId, PayloadHash, Status)
            OUTPUT inserted.Id
            VALUES
                (@TenantId, @ActorUserId, @OperationKind, @ResourceScopeId, @ClientOperationId, @PayloadHash, N'Pending');
            """,
            new
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                OperationKind = operationKind,
                ResourceScopeId = resourceScopeId,
                ClientOperationId = clientOperationId,
                PayloadHash = payloadHash
            },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task CompleteOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long operationRecordId,
        int projectId,
        long workbenchSessionId,
        TicketProposalSetMutationResult result,
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
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                CanonicalResultJson = json,
                ResultHash = Hash(json)
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task InsertOutboxAndAttributionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        Guid clientOperationId,
        Guid ticketProposalSetId,
        long revision,
        string changeKind,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.WorkbenchOutboxEvents
                (EventId, TenantId, ProjectId, WorkbenchSessionId, EventKind,
                 PayloadJson, ClientOperationId, DedupeKey)
            VALUES
                (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, N'TicketProposalSetReviewed',
                 @PayloadJson, @ClientOperationId, @DedupeKey);

            INSERT dbo.UserMutationAttribution
                (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId, TimestampUtc,
                 SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
            VALUES
                (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                 CONVERT(NVARCHAR(128), @ClientOperationId), NULL, SYSUTCDATETIME(),
                 N'Workbench', N'IronDev.Api', N'POST',
                 N'/api/workbench/projects/{projectId}/ticket-proposal-sets/{ticketProposalSetId}/review',
                 N'Completed', 200);
            """,
            new
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                ClientOperationId = clientOperationId,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    projectId,
                    ticketProposalSetId,
                    revision,
                    changeKind
                }, JsonOptions),
                DedupeKey = $"ticket-proposal-review:{ticketProposalSetId:D}:{revision}"
            },
            transaction,
            cancellationToken: cancellationToken));

    private static void EnsureMatchingOperation(ClientOperationRow operation, string payloadHash)
    {
        if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            throw new ProjectStartOperationMismatchException();
    }

    private static TicketProposalSetMutationResult ReadStoredResult(ClientOperationRow row)
    {
        if (string.IsNullOrWhiteSpace(row.CanonicalResultJson) || string.IsNullOrWhiteSpace(row.ResultHash) ||
            !string.Equals(Hash(row.CanonicalResultJson), row.ResultHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The stored ticket proposal review operation failed its integrity check.");
        return JsonSerializer.Deserialize<TicketProposalSetMutationResult>(row.CanonicalResultJson, JsonOptions)
            ?? throw new InvalidOperationException(
                "The stored ticket proposal review operation could not be read.");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private interface IProposalRevisionRow
    {
        long Revision { get; }
        string SnapshotJson { get; }
        string SnapshotHash { get; }
    }

    private sealed class ProposalSetRow : IProposalRevisionRow
    {
        public Guid TicketProposalSetId { get; init; }
        public long CurrentRevision { get; init; }
        public long Revision => CurrentRevision;
        public string SnapshotJson { get; init; } = string.Empty;
        public string SnapshotHash { get; init; } = string.Empty;
    }

    private sealed class ProposalHistoryRow : IProposalRevisionRow
    {
        public long Revision { get; init; }
        public string SnapshotJson { get; init; } = string.Empty;
        public string SnapshotHash { get; init; } = string.Empty;
        public int ActorUserId { get; init; }
        public Guid? AgentRunId { get; init; }
        public string ChangeKind { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class ClientOperationRow
    {
        public string PayloadHash { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }
}
