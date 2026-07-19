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

            var existing = await connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
                ExistingOperationSql,
                Scope(command),
                transaction,
                cancellationToken: cancellationToken));

            if (existing is not null)
            {
                if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectStartOperationMismatchException();

                var replay = ReadStoredResult(existing, command.ClientOperationId) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

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

            var workbenchSessionId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT dbo.WorkbenchSessions
                    (TenantId, ProjectId, Status, CreatedByActorUserId)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ProjectId, N'Active', @ActorUserId);
                """,
                new
                {
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

            var createdAtUtc = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT CreatedDate FROM dbo.Projects WHERE TenantId=@TenantId AND Id=@ProjectId;",
                new { command.TenantId, ProjectId = projectId },
                transaction,
                cancellationToken: cancellationToken));
            var result = new StartProjectResult(
                projectId,
                command.TenantId,
                name,
                ProjectLifecyclePhases.Shaping,
                ProjectExecutionReadinessStates.NotConfigured,
                workbenchSessionId,
                1,
                command.ClientOperationId,
                createdAtUtc,
                false);
            var canonicalResultJson = JsonSerializer.Serialize(result);
            var resultHash = ComputeHash(canonicalResultJson);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ClientOperations
                SET Status = N'Completed', ResultProjectId = @ProjectId,
                    ResultWorkbenchSessionId = @WorkbenchSessionId,
                    CanonicalResultJson = @CanonicalResultJson, ResultHash = @ResultHash,
                    CompletedAtUtc = SYSUTCDATETIME()
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
                    WorkbenchSessionId = workbenchSessionId,
                    CanonicalResultJson = canonicalResultJson,
                    ResultHash = resultHash
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

    private static object Scope(StartProjectCommand command) => new
    {
        command.TenantId,
        command.ActorUserId,
        OperationKind = ProjectStartOperationKinds.StartProject,
        ResourceScopeId = ResourceScope,
        command.ClientOperationId
    };

    private static StartProjectResult ReadStoredResult(ClientOperationRow existing, Guid clientOperationId)
    {
        if (string.IsNullOrWhiteSpace(existing.CanonicalResultJson) || string.IsNullOrWhiteSpace(existing.ResultHash))
            throw new InvalidOperationException("The completed project-start operation has no canonical result.");
        if (!string.Equals(ComputeHash(existing.CanonicalResultJson), existing.ResultHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stored project-start result failed its integrity check.");

        var result = JsonSerializer.Deserialize<StartProjectResult>(existing.CanonicalResultJson)
            ?? throw new InvalidOperationException("The stored project-start result could not be read.");
        if (result.ClientOperationId != clientOperationId)
            throw new InvalidOperationException("The stored project-start result belongs to another operation.");
        return result;
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
        ComputeHash($"project-start-v1\n{name}");

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private const string ExistingOperationSql = """
        SELECT PayloadHash, CanonicalResultJson, ResultHash
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
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }
}
