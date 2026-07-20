using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchProjectUnderstandingService : IWorkbenchProjectUnderstandingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connections;

    public WorkbenchProjectUnderstandingService(IDbConnectionFactory connections) =>
        _connections = connections;

    public async Task<ProjectUnderstandingSnapshot> GetAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || actorUserId <= 0 || projectId <= 0)
            throw new ProjectUnderstandingValidationException("A current project and actor are required.");

        using var connection = _connections.CreateConnection();
        connection.Open();
        if (!await CanAccessProjectAsync(connection, null, tenantId, actorUserId, projectId, cancellationToken))
            throw new WorkbenchProjectNotAccessibleException();
        return await ReadSnapshotAsync(connection, null, tenantId, projectId, cancellationToken);
    }

    public async Task<PutProjectUnderstandingFactResult> PutFactAsync(
        PutProjectUnderstandingFactCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidatePut(command);
        var normalizedValue = command.Value?.Trim();
        var resourceScope = $"project:{command.ProjectId}:understanding:fact:{command.FactKey}";
        var payloadHash = Hash(JsonSerializer.Serialize(new
        {
            v = 2,
            command.ProjectId,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            command.ExpectedUnderstandingRevision,
            command.FactKey,
            command.Action,
            command.ConflictId,
            value = normalizedValue,
            command.UserLocked
        }, JsonOptions));

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(
                    connection, transaction, command.TenantId, command.ActorUserId, command.ProjectId,
                    cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();

            var existing = await ReadOperationAsync(
                connection, transaction, command.TenantId, command.ActorUserId,
                ProjectUnderstandingOperationKinds.PutFact, resourceScope, command.ClientOperationId,
                cancellationToken);
            if (existing is not null)
            {
                EnsureMatchingOperation(existing, payloadHash);
                var replay = ReadStoredResult<PutProjectUnderstandingFactResult>(existing) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command.TenantId,
                    command.ActorUserId, command.ProjectId, command.WorkbenchSessionId, command.LeaseEpoch,
                    cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var operationRecordId = await InsertOperationAsync(
                connection, transaction, command.TenantId, command.ActorUserId,
                ProjectUnderstandingOperationKinds.PutFact, resourceScope, command.ClientOperationId,
                payloadHash, cancellationToken);

            var current = await ReadCurrentUnderstandingForUpdateAsync(
                connection, transaction, command.TenantId, command.ProjectId, cancellationToken);
            if (current.Revision != command.ExpectedUnderstandingRevision)
                throw new ProjectUnderstandingRevisionConflictException(current.Revision);

            var document = ProjectUnderstandingDocumentCodec.Deserialize(current.UnderstandingJson);
            var nextRevision = checked(current.Revision + 1);
            var facts = document.Facts.ToDictionary(value => value.Key, StringComparer.Ordinal);
            var mutation = ApplyFactAction(
                command,
                normalizedValue,
                nextRevision,
                facts.GetValueOrDefault(command.FactKey),
                document.Conflicts);
            facts[command.FactKey] = mutation.Fact;
            var next = new ProjectUnderstandingDocument(
                ProjectUnderstandingContract.SchemaVersion,
                facts.Values.ToArray(),
                mutation.Conflicts,
                document.OpenQuestions);
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
                     @CurrentRevision, @ActorUserId, NULL);
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    CurrentRevision = current.Revision,
                    NextRevision = nextRevision,
                    UnderstandingJson = understandingJson,
                    command.ActorUserId
                },
                transaction,
                cancellationToken: cancellationToken));

            var snapshot = await ReadSnapshotAsync(
                connection, transaction, command.TenantId, command.ProjectId, cancellationToken);
            var result = new PutProjectUnderstandingFactResult(snapshot, command.ClientOperationId, IsReplay: false);
            await CompleteOperationAsync(
                connection, transaction, operationRecordId, command.ProjectId, command.WorkbenchSessionId,
                result, cancellationToken);
            await InsertOutboxAndAttributionAsync(
                connection, transaction, command.TenantId, command.ActorUserId, command.ProjectId,
                command.WorkbenchSessionId, command.ClientOperationId,
                "ProjectUnderstandingFactUpdated",
                JsonSerializer.Serialize(new
                {
                    command.ProjectId,
                    command.FactKey,
                    command.Action,
                    command.ConflictId,
                    revision = nextRevision,
                    command.UserLocked
                }, JsonOptions),
                $"project-understanding-fact:{command.ProjectId}:{nextRevision}:{command.FactKey}",
                "PUT", "/api/workbench/projects/{projectId}/understanding/facts/{factKey}",
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

    public async Task<AcceptProjectRenameProposalResult> AcceptRenameAsync(
        AcceptProjectRenameProposalCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ProposalId == Guid.Empty || command.ClientOperationId == Guid.Empty)
            throw new ProjectUnderstandingValidationException(
                "A current project, proposal, Workbench lease, and client operation ID are required.");

        var resourceScope = $"project:{command.ProjectId}:rename-proposal:{command.ProposalId:D}";
        var payloadHash = Hash(JsonSerializer.Serialize(new
        {
            v = 1,
            command.ProjectId,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            command.ProposalId
        }, JsonOptions));

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(
                    connection, transaction, command.TenantId, command.ActorUserId, command.ProjectId,
                    cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();

            var existing = await ReadOperationAsync(
                connection, transaction, command.TenantId, command.ActorUserId,
                ProjectUnderstandingOperationKinds.AcceptRename, resourceScope, command.ClientOperationId,
                cancellationToken);
            if (existing is not null)
            {
                EnsureMatchingOperation(existing, payloadHash);
                var replay = ReadStoredResult<AcceptProjectRenameProposalResult>(existing) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command.TenantId,
                    command.ActorUserId, command.ProjectId, command.WorkbenchSessionId, command.LeaseEpoch,
                    cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var proposal = await connection.QuerySingleOrDefaultAsync<RenameProposalRow>(new CommandDefinition(
                """
                SELECT ProposalId, ProposedName, Status, BasedOnProjectName
                FROM dbo.ProjectRenameProposals WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND ProposalId=@ProposalId;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));
            if (proposal is null)
                throw new WorkbenchProjectNotAccessibleException();
            if (proposal.Status != ProjectRenameProposalStates.Pending)
                throw new ProjectRenameProposalNotPendingException();

            var currentProjectName = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT Name
                FROM dbo.Projects WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND Id=@ProjectId;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));
            if (currentProjectName is null)
                throw new WorkbenchProjectNotAccessibleException();
            if (!string.Equals(currentProjectName, proposal.BasedOnProjectName, StringComparison.Ordinal))
                throw new ProjectRenameProposalStaleException();

            var operationRecordId = await InsertOperationAsync(
                connection, transaction, command.TenantId, command.ActorUserId,
                ProjectUnderstandingOperationKinds.AcceptRename, resourceScope, command.ClientOperationId,
                payloadHash, cancellationToken);

            var renamed = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.Projects
                SET Name=@ProposedName, UpdatedDate=SYSUTCDATETIME()
                WHERE TenantId=@TenantId AND Id=@ProjectId AND Name=@BasedOnProjectName;
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    command.ProposalId,
                    command.ActorUserId,
                    proposal.ProposedName,
                    proposal.BasedOnProjectName,
                    OperationRecordId = operationRecordId
                },
                transaction,
                cancellationToken: cancellationToken));
            if (renamed != 1)
                throw new ProjectRenameProposalStaleException();

            var accepted = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ProjectRenameProposals
                SET Status=N'Accepted', DecisionByActorUserId=@ActorUserId,
                    DecisionClientOperationRecordId=@OperationRecordId, DecisionAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND ProposalId=@ProposalId
                  AND Status=N'Pending';
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    command.ProposalId,
                    command.ActorUserId,
                    OperationRecordId = operationRecordId
                },
                transaction,
                cancellationToken: cancellationToken));
            if (accepted != 1)
                throw new ProjectRenameProposalNotPendingException();

            var snapshot = await ReadSnapshotAsync(
                connection, transaction, command.TenantId, command.ProjectId, cancellationToken);
            var result = new AcceptProjectRenameProposalResult(
                snapshot,
                command.ClientOperationId,
                IsReplay: false);
            await CompleteOperationAsync(
                connection, transaction, operationRecordId, command.ProjectId, command.WorkbenchSessionId,
                result, cancellationToken);
            await InsertOutboxAndAttributionAsync(
                connection, transaction, command.TenantId, command.ActorUserId, command.ProjectId,
                command.WorkbenchSessionId, command.ClientOperationId,
                "ProjectRenameProposalAccepted",
                JsonSerializer.Serialize(new
                {
                    command.ProjectId,
                    command.ProposalId,
                    projectName = proposal.ProposedName
                }, JsonOptions),
                $"project-rename-accepted:{command.ProposalId:D}",
                "POST", "/api/workbench/projects/{projectId}/rename-proposals/{proposalId}/accept",
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

    private static void ValidatePut(PutProjectUnderstandingFactCommand command)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty || command.ExpectedUnderstandingRevision <= 0)
            throw new ProjectUnderstandingValidationException(
                "A current project, Workbench lease, revision, and client operation ID are required.");
        if (!ProjectUnderstandingContract.IsKnownFactKey(command.FactKey))
            throw new ProjectUnderstandingValidationException("The project-understanding fact key is not supported.");
        if (string.IsNullOrWhiteSpace(command.Action) ||
            !ProjectUnderstandingFactActions.IsKnown(command.Action))
            throw new ProjectUnderstandingValidationException("The project-understanding fact action is not supported.");

        var normalizedValue = command.Value?.Trim();
        var hasValidValue = !string.IsNullOrEmpty(normalizedValue) &&
            normalizedValue.Length <= ProjectUnderstandingContract.MaximumFactValueCharacters;
        var validCombination = command.Action switch
        {
            ProjectUnderstandingFactActions.Edit =>
                hasValidValue && command.ConflictId is null && command.UserLocked is null,
            ProjectUnderstandingFactActions.Confirm =>
                command.Value is null && command.ConflictId is null && command.UserLocked is null,
            ProjectUnderstandingFactActions.SetLock =>
                command.Value is null && command.ConflictId is null && command.UserLocked is not null,
            ProjectUnderstandingFactActions.ResolveConflict =>
                hasValidValue && command.ConflictId is not null && command.UserLocked is null,
            _ => false
        };
        if (!validCombination)
            throw new ProjectUnderstandingValidationException(
                "The fact action contains an invalid value, lock, or conflict selection.");
    }

    private static FactMutation ApplyFactAction(
        PutProjectUnderstandingFactCommand command,
        string? normalizedValue,
        long nextRevision,
        ProjectUnderstandingFact? currentFact,
        IReadOnlyList<ProjectUnderstandingConflict> currentConflicts)
    {
        if (currentFact is null && command.Action != ProjectUnderstandingFactActions.Edit)
            throw new ProjectUnderstandingValidationException(
                "A missing project-understanding fact can only be created with Edit.");

        var openConflicts = currentConflicts
            .Where(conflict => conflict.FactKey == command.FactKey &&
                               conflict.Status == ProjectUnderstandingConflictStates.Open)
            .ToArray();
        ProjectUnderstandingFact fact;
        IReadOnlyList<ProjectUnderstandingConflict> conflicts = currentConflicts;

        switch (command.Action)
        {
            case ProjectUnderstandingFactActions.Edit:
                fact = ActorFact(
                    command,
                    normalizedValue!,
                    openConflicts.Length == 0
                        ? ProjectUnderstandingFactStates.Confirmed
                        : ProjectUnderstandingFactStates.Conflicted,
                    currentFact?.UserLocked ?? false,
                    nextRevision,
                    "Edited in the project-understanding panel.");
                break;

            case ProjectUnderstandingFactActions.Confirm:
                fact = ActorFact(
                    command,
                    currentFact!.Value,
                    openConflicts.Length == 0
                        ? ProjectUnderstandingFactStates.Confirmed
                        : ProjectUnderstandingFactStates.Conflicted,
                    currentFact.UserLocked,
                    nextRevision,
                    "Confirmed in the project-understanding panel.");
                break;

            case ProjectUnderstandingFactActions.SetLock:
                fact = currentFact! with
                {
                    UserLocked = command.UserLocked!.Value,
                    Revision = nextRevision
                };
                break;

            case ProjectUnderstandingFactActions.ResolveConflict:
                var selected = openConflicts.SingleOrDefault(conflict =>
                    conflict.ConflictId == command.ConflictId);
                if (selected is null)
                    throw new ProjectUnderstandingConflictNotOpenException();
                if (!string.Equals(normalizedValue, currentFact!.Value, StringComparison.Ordinal) &&
                    !string.Equals(normalizedValue, selected.ProposedValue, StringComparison.Ordinal))
                    throw new ProjectUnderstandingValidationException(
                        "A conflict must be resolved to its current or proposed value.");

                conflicts = currentConflicts.Select(conflict =>
                        conflict.ConflictId == selected.ConflictId
                            ? conflict with
                            {
                                Status = ProjectUnderstandingConflictStates.Resolved,
                                ResolvedAtRevision = nextRevision,
                                ResolvedByActorUserId = command.ActorUserId
                            }
                            : conflict)
                    .ToArray();
                var hasOtherOpenConflict = openConflicts.Any(conflict =>
                    conflict.ConflictId != selected.ConflictId);
                fact = ActorFact(
                    command,
                    normalizedValue!,
                    hasOtherOpenConflict
                        ? ProjectUnderstandingFactStates.Conflicted
                        : ProjectUnderstandingFactStates.Confirmed,
                    currentFact.UserLocked,
                    nextRevision,
                    "Resolved one explicit project-understanding conflict.");
                break;

            default:
                throw new ProjectUnderstandingValidationException(
                    "The project-understanding fact action is not supported.");
        }

        return new FactMutation(fact, conflicts);
    }

    private static ProjectUnderstandingFact ActorFact(
        PutProjectUnderstandingFactCommand command,
        string value,
        string state,
        bool userLocked,
        long revision,
        string evidenceSummary) => new(
            command.FactKey,
            value,
            state,
            userLocked,
            ProjectUnderstandingAuthorKinds.Actor,
            command.ActorUserId,
            AuthorAgentRunId: null,
            SourceMessageIds: [],
            evidenceSummary,
            revision);

    private static async Task<ProjectUnderstandingSnapshot> ReadSnapshotAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<UnderstandingSnapshotRow>(new CommandDefinition(
            """
            SELECT project.Name AS ProjectName,
                   understanding.Revision, understanding.UnderstandingJson,
                   phase.Phase AS ProjectLifecyclePhase,
                   readiness.ExecutionReadiness
            FROM dbo.Projects project
            CROSS APPLY
            (
                SELECT TOP (1) value.Revision, value.UnderstandingJson
                FROM dbo.ProjectUnderstandings value
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC
            ) understanding
            CROSS APPLY
            (
                SELECT TOP (1) value.Phase
                FROM dbo.ProjectLifecyclePhases value
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC
            ) phase
            CROSS APPLY
            (
                SELECT TOP (1) value.ExecutionReadiness
                FROM dbo.ProjectReadinessAssessments value
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC
            ) readiness
            WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken))
            ?? throw new WorkbenchProjectNotAccessibleException();
        var proposal = await connection.QuerySingleOrDefaultAsync<RenameProposalReadRow>(new CommandDefinition(
            """
            SELECT TOP (1) ProposalId, ProposedName, Status, BasedOnProjectName,
                   BasedOnUnderstandingRevision, ProposedByAgentRunId, InitiatingActorUserId,
                   SourceMessageIdsJson, EvidenceSummary, CreatedAtUtc
            FROM dbo.ProjectRenameProposals
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Status=N'Pending'
            ORDER BY CreatedAtUtc DESC;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));

        var document = ProjectUnderstandingDocumentCodec.Deserialize(row.UnderstandingJson);
        return new ProjectUnderstandingSnapshot(
            projectId,
            tenantId,
            row.ProjectName,
            row.Revision,
            document.Facts,
            document.Conflicts,
            document.OpenQuestions,
            proposal is null ? null : ToSnapshot(proposal),
            new ProjectOperationalProjection(
                row.ProjectLifecyclePhase,
                "ProjectLifecyclePhase",
                row.ExecutionReadiness,
                "ProjectReadinessAssessment",
                RepositoryBinding: null));
    }

    private static ProjectRenameProposalSnapshot ToSnapshot(RenameProposalReadRow row) => new(
        row.ProposalId,
        row.ProposedName,
        row.Status,
        row.BasedOnProjectName,
        row.BasedOnUnderstandingRevision,
        row.ProposedByAgentRunId,
        row.InitiatingActorUserId,
        JsonSerializer.Deserialize<long[]>(row.SourceMessageIdsJson, JsonOptions) ?? [],
        row.EvidenceSummary,
        row.CreatedAtUtc);

    private static async Task<CurrentUnderstandingRow> ReadCurrentUnderstandingForUpdateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<CurrentUnderstandingRow>(new CommandDefinition(
            """
            SELECT TOP (1) Revision, UnderstandingJson
            FROM dbo.ProjectUnderstandings WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
            ORDER BY Revision DESC;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken))
        ?? throw new InvalidOperationException("The project understanding is missing.");

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

    private static async Task CompleteOperationAsync<T>(
        IDbConnection connection,
        IDbTransaction transaction,
        long operationRecordId,
        int projectId,
        long workbenchSessionId,
        T result,
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
        string eventKind,
        string payloadJson,
        string dedupeKey,
        string method,
        string route,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.WorkbenchOutboxEvents
                (EventId, TenantId, ProjectId, WorkbenchSessionId, EventKind,
                 PayloadJson, ClientOperationId, DedupeKey)
            VALUES
                (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, @EventKind,
                 @PayloadJson, @ClientOperationId, @DedupeKey);

            INSERT dbo.UserMutationAttribution
                (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId, TimestampUtc,
                 SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
            VALUES
                (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                 CONVERT(NVARCHAR(128), @ClientOperationId), NULL, SYSUTCDATETIME(),
                 N'Workbench', N'IronDev.Api', @Method, @Route, N'Completed', 200);
            """,
            new
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                EventKind = eventKind,
                PayloadJson = payloadJson,
                ClientOperationId = clientOperationId,
                DedupeKey = dedupeKey,
                Method = method,
                Route = route
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
        if (string.IsNullOrWhiteSpace(row.CanonicalResultJson) || string.IsNullOrWhiteSpace(row.ResultHash) ||
            !string.Equals(Hash(row.CanonicalResultJson), row.ResultHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stored project-understanding operation result failed its integrity check.");
        return JsonSerializer.Deserialize<T>(row.CanonicalResultJson, JsonOptions)
            ?? throw new InvalidOperationException("The stored project-understanding operation result could not be read.");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class ClientOperationRow
    {
        public string PayloadHash { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }

    private class CurrentUnderstandingRow
    {
        public long Revision { get; init; }
        public string UnderstandingJson { get; init; } = string.Empty;
    }

    private sealed class UnderstandingSnapshotRow : CurrentUnderstandingRow
    {
        public string ProjectName { get; init; } = string.Empty;
        public string ProjectLifecyclePhase { get; init; } = string.Empty;
        public string ExecutionReadiness { get; init; } = string.Empty;
    }

    private class RenameProposalRow
    {
        public Guid ProposalId { get; init; }
        public string ProposedName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string BasedOnProjectName { get; init; } = string.Empty;
    }

    private sealed class RenameProposalReadRow : RenameProposalRow
    {
        public long BasedOnUnderstandingRevision { get; init; }
        public Guid ProposedByAgentRunId { get; init; }
        public int InitiatingActorUserId { get; init; }
        public string SourceMessageIdsJson { get; init; } = "[]";
        public string EvidenceSummary { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed record FactMutation(
        ProjectUnderstandingFact Fact,
        IReadOnlyList<ProjectUnderstandingConflict> Conflicts);
}
