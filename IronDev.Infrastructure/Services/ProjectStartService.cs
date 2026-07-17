using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectStartService : IProjectStartService
{
    private const string ResourceScope = "tenant-project-entry";
    private readonly IDbConnectionFactory _connections;
    private readonly IProjectStartFailureInjector _failureInjector;

    public ProjectStartService(
        IDbConnectionFactory connections,
        IProjectStartFailureInjector failureInjector)
    {
        _connections = connections;
        _failureInjector = failureInjector;
    }

    public async Task<StartProjectResult> StartAsync(
        StartProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeName(command.Name);
        if (command.TenantId <= 0 || command.ActorUserId <= 0)
            throw new ProjectStartValidationException("A selected tenant and authenticated actor are required.");
        if (command.ClientOperationId == Guid.Empty)
            throw new ProjectStartValidationException("clientOperationId is required.");

        var payloadHash = ComputePayloadHash(name);
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            var existing = await connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
                ExistingOperationSql,
                Scope(command),
                transaction,
                cancellationToken: cancellationToken));

            if (existing is not null)
            {
                if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectStartOperationMismatchException();

                var replay = await LoadResultAsync(
                    connection,
                    transaction,
                    existing.ResultProjectId,
                    command,
                    isReplay: true,
                    cancellationToken);
                transaction.Commit();
                return replay;
            }

            var isTenantMember = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.TenantUsers tu
                INNER JOIN dbo.Users u ON u.Id = tu.UserId AND u.IsActive = 1
                WHERE tu.TenantId = @TenantId AND tu.UserId = @ActorUserId;
                """,
                new { command.TenantId, command.ActorUserId },
                transaction,
                cancellationToken: cancellationToken)) > 0;

            if (!isTenantMember)
                throw new UnauthorizedAccessException("The actor is not an active member of the selected tenant.");

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
                    OperationKind = ProjectStartOperationKinds.StartProject,
                    ResourceScopeId = ResourceScope,
                    command.ClientOperationId,
                    PayloadHash = payloadHash
                },
                transaction,
                cancellationToken: cancellationToken));

            var projectId = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                INSERT dbo.Projects (TenantId, Name, Description, LocalPath)
                OUTPUT inserted.Id
                VALUES (@TenantId, @Name, NULL, NULL);
                """,
                new { command.TenantId, Name = name },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(ProjectStartFailurePoint.ProjectCreated);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.ProjectMembers
                    (TenantId, ProjectId, UserId, ProjectRole, Status, AddedByUserId)
                VALUES
                    (@TenantId, @ProjectId, @ActorUserId, N'Owner', N'Active', @ActorUserId);
                """,
                new { command.TenantId, ProjectId = projectId, command.ActorUserId },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(ProjectStartFailurePoint.OwnerMembershipCreated);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.ProjectLifecyclePhases
                    (TenantId, ProjectId, Revision, Phase, ChangedByActorUserId)
                VALUES
                    (@TenantId, @ProjectId, 1, @Phase, @ActorUserId);

                INSERT dbo.ProjectUnderstandings
                    (TenantId, ProjectId, Revision, Status, UnderstandingJson, CreatedByActorUserId)
                VALUES
                    (@TenantId, @ProjectId, 1, N'Draft', N'{}', @ActorUserId);
                """,
                new
                {
                    command.TenantId,
                    ProjectId = projectId,
                    command.ActorUserId,
                    Phase = ProjectLifecyclePhases.Shaping
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(ProjectStartFailurePoint.UnderstandingCreated);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.ProjectReadinessAssessments
                    (TenantId, ProjectId, Revision, ExecutionReadiness, ReasonCode, Summary, AssessedByActorUserId)
                VALUES
                    (@TenantId, @ProjectId, 1, @ExecutionReadiness, N'RepositoryNotConfigured',
                     N'Repository and execution profile have not been configured.', @ActorUserId);
                """,
                new
                {
                    command.TenantId,
                    ProjectId = projectId,
                    command.ActorUserId,
                    ExecutionReadiness = ProjectExecutionReadinessStates.NotConfigured
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(ProjectStartFailurePoint.ReadinessCreated);

            var workbenchSessionId = Guid.NewGuid();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchSessions
                    (Id, TenantId, ProjectId, Status, CreatedByActorUserId)
                VALUES
                    (@WorkbenchSessionId, @TenantId, @ProjectId, N'Active', @ActorUserId);
                """,
                new
                {
                    WorkbenchSessionId = workbenchSessionId,
                    command.TenantId,
                    ProjectId = projectId,
                    command.ActorUserId
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(ProjectStartFailurePoint.WorkbenchSessionCreated);

            var leaseTokenHash = Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(32))).ToLowerInvariant();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchWriteLeases
                    (TenantId, ProjectId, WorkbenchSessionId, HolderActorUserId, LeaseEpoch, LeaseTokenHash,
                     AcquiredAtUtc, HeartbeatAtUtc, ExpiresAtUtc)
                VALUES
                    (@TenantId, @ProjectId, @WorkbenchSessionId, @ActorUserId, 1, @LeaseTokenHash,
                     SYSUTCDATETIME(), SYSUTCDATETIME(), DATEADD(MINUTE, 30, SYSUTCDATETIME()));
                """,
                new
                {
                    command.TenantId,
                    ProjectId = projectId,
                    WorkbenchSessionId = workbenchSessionId,
                    command.ActorUserId,
                    LeaseTokenHash = leaseTokenHash
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(ProjectStartFailurePoint.WriteLeaseCreated);

            var eventPayload = JsonSerializer.Serialize(new
            {
                projectId,
                workbenchSessionId,
                lifecyclePhase = ProjectLifecyclePhases.Shaping,
                executionReadiness = ProjectExecutionReadinessStates.NotConfigured,
                repositoryBinding = (object?)null
            });
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchOutboxEvents
                    (EventId, TenantId, ProjectId, WorkbenchSessionId, EventKind, PayloadJson, ClientOperationId)
                VALUES
                    (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, N'ProjectStarted', @PayloadJson, @ClientOperationId),
                    (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, N'WorkbenchSessionStarted', @PayloadJson, @ClientOperationId);

                INSERT dbo.UserMutationAttribution
                    (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId, TimestampUtc,
                     SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
                VALUES
                    (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                     CONVERT(NVARCHAR(128), @ClientOperationId), NULL, SYSUTCDATETIME(),
                     N'Workbench', N'IronDev.Api', N'POST', N'/api/projects/start', N'Completed', 201);
                """,
                new
                {
                    command.TenantId,
                    ProjectId = projectId,
                    WorkbenchSessionId = workbenchSessionId,
                    command.ActorUserId,
                    command.ClientOperationId,
                    PayloadJson = eventPayload
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(ProjectStartFailurePoint.OutboxEventsCreated);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ClientOperations
                SET Status = N'Completed', ResultProjectId = @ProjectId,
                    ResultWorkbenchSessionId = @WorkbenchSessionId, CompletedAtUtc = SYSUTCDATETIME()
                WHERE TenantId = @TenantId
                  AND ActorUserId = @ActorUserId
                  AND OperationKind = @OperationKind
                  AND ResourceScopeId = @ResourceScopeId
                  AND ClientOperationId = @ClientOperationId;
                """,
                new
                {
                    command.TenantId,
                    command.ActorUserId,
                    OperationKind = ProjectStartOperationKinds.StartProject,
                    ResourceScopeId = ResourceScope,
                    command.ClientOperationId,
                    ProjectId = projectId,
                    WorkbenchSessionId = workbenchSessionId
                },
                transaction,
                cancellationToken: cancellationToken));

            var result = await LoadResultAsync(
                connection,
                transaction,
                projectId,
                command,
                isReplay: false,
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

    private static object Scope(StartProjectCommand command) => new
    {
        command.TenantId,
        command.ActorUserId,
        OperationKind = ProjectStartOperationKinds.StartProject,
        ResourceScopeId = ResourceScope,
        command.ClientOperationId
    };

    private static async Task<StartProjectResult> LoadResultAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int projectId,
        StartProjectCommand command,
        bool isReplay,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleAsync<ProjectStartRow>(new CommandDefinition(
            """
            SELECT p.Id AS ProjectId, p.TenantId, p.Name, p.CreatedDate AS CreatedAtUtc,
                   phase.Phase AS ProjectLifecyclePhase,
                   readiness.ExecutionReadiness,
                   session.Id AS WorkbenchSessionId,
                   lease.LeaseEpoch
            FROM dbo.Projects p
            INNER JOIN dbo.ProjectLifecyclePhases phase
                ON phase.TenantId = p.TenantId AND phase.ProjectId = p.Id AND phase.Revision = 1
            INNER JOIN dbo.ProjectReadinessAssessments readiness
                ON readiness.TenantId = p.TenantId AND readiness.ProjectId = p.Id AND readiness.Revision = 1
            INNER JOIN dbo.WorkbenchSessions session
                ON session.TenantId = p.TenantId AND session.ProjectId = p.Id
            INNER JOIN dbo.WorkbenchWriteLeases lease
                ON lease.TenantId = p.TenantId AND lease.ProjectId = p.Id AND lease.WorkbenchSessionId = session.Id
            WHERE p.TenantId = @TenantId AND p.Id = @ProjectId;
            """,
            new { command.TenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));

        return new StartProjectResult(
            row.ProjectId,
            row.TenantId,
            row.Name,
            row.ProjectLifecyclePhase,
            row.ExecutionReadiness,
            row.WorkbenchSessionId,
            row.LeaseEpoch,
            command.ClientOperationId,
            row.CreatedAtUtc,
            isReplay);
    }

    private static string NormalizeName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new ProjectStartValidationException("Project name is required.");
        if (name.Length > 200)
            throw new ProjectStartValidationException("Project name must be 200 characters or fewer.");
        return name;
    }

    private static string ComputePayloadHash(string name) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"project-start-v1\n{name}"))).ToLowerInvariant();

    private const string ExistingOperationSql = """
        SELECT PayloadHash, ResultProjectId
        FROM dbo.ClientOperations WITH (UPDLOCK, HOLDLOCK)
        WHERE TenantId = @TenantId
          AND ActorUserId = @ActorUserId
          AND OperationKind = @OperationKind
          AND ResourceScopeId = @ResourceScopeId
          AND ClientOperationId = @ClientOperationId;
        """;

    private sealed class ClientOperationRow
    {
        public string PayloadHash { get; init; } = string.Empty;
        public int ResultProjectId { get; init; }
    }

    private sealed class ProjectStartRow
    {
        public int ProjectId { get; init; }
        public int TenantId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ProjectLifecyclePhase { get; init; } = string.Empty;
        public string ExecutionReadiness { get; init; } = string.Empty;
        public Guid WorkbenchSessionId { get; init; }
        public long LeaseEpoch { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }
}
