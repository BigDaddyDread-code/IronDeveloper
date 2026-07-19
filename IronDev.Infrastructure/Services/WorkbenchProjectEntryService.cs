using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchProjectEntryService : IWorkbenchProjectEntryService
{
    private readonly IDbConnectionFactory _connections;

    public WorkbenchProjectEntryService(IDbConnectionFactory connections) => _connections = connections;

    public async Task<WorkbenchProjectEntryContext> OpenAsync(
        OpenWorkbenchProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0)
            throw new ProjectStartValidationException("A selected tenant, authenticated actor, and project are required.");
        if (command.ClientOperationId == Guid.Empty)
            throw new ProjectStartValidationException("clientOperationId is required.");

        var resourceScope = $"project:{command.ProjectId}";
        var payloadHash = ComputeHash($"workbench-open-v1\n{command.ProjectId}\n{command.TakeOver}");
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            var canAccessProject = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.Projects project
                INNER JOIN dbo.ProjectMembers member
                    ON member.TenantId=project.TenantId AND member.ProjectId=project.Id
                   AND member.UserId=@ActorUserId AND member.Status=N'Active'
                INNER JOIN dbo.TenantUsers tenantMember
                    ON tenantMember.TenantId=project.TenantId AND tenantMember.UserId=member.UserId
                INNER JOIN dbo.Users actor ON actor.Id=member.UserId AND actor.IsActive=1
                WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));
            if (canAccessProject == 0)
                throw new WorkbenchProjectNotAccessibleException();

            var operation = await connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
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
                    OperationKind = ProjectStartOperationKinds.OpenWorkbenchProject,
                    ResourceScopeId = resourceScope,
                    command.ClientOperationId
                },
                transaction,
                cancellationToken: cancellationToken));

            if (operation is not null)
            {
                if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
                    throw new ProjectStartOperationMismatchException();
                var replay = ReadStoredResult(operation, command);
                transaction.Commit();
                return replay;
            }

            var project = await connection.QuerySingleOrDefaultAsync<ProjectEntryRow>(new CommandDefinition(
                """
                SELECT p.Id AS ProjectId, p.TenantId, p.Name,
                       COALESCE(phase.Phase, N'Shaping') AS ProjectLifecyclePhase,
                       COALESCE(readiness.ExecutionReadiness, N'NotConfigured') AS ExecutionReadiness
                FROM dbo.Projects p
                INNER JOIN dbo.ProjectMembers member
                    ON member.TenantId=p.TenantId AND member.ProjectId=p.Id
                   AND member.UserId=@ActorUserId AND member.Status=N'Active'
                INNER JOIN dbo.TenantUsers tenantMember
                    ON tenantMember.TenantId=p.TenantId AND tenantMember.UserId=member.UserId
                INNER JOIN dbo.Users actor ON actor.Id=member.UserId AND actor.IsActive=1
                OUTER APPLY (
                    SELECT TOP (1) value.Phase
                    FROM dbo.ProjectLifecyclePhases value
                    WHERE value.TenantId=p.TenantId AND value.ProjectId=p.Id
                    ORDER BY value.Revision DESC
                ) phase
                OUTER APPLY (
                    SELECT TOP (1) value.ExecutionReadiness
                    FROM dbo.ProjectReadinessAssessments value
                    WHERE value.TenantId=p.TenantId AND value.ProjectId=p.Id
                    ORDER BY value.Revision DESC
                ) readiness
                WHERE p.TenantId=@TenantId AND p.Id=@ProjectId;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));

            if (project is null)
                throw new WorkbenchProjectNotAccessibleException();

            await connection.ExecuteAsync(new CommandDefinition(
                """
                IF NOT EXISTS (SELECT 1 FROM dbo.ProjectLifecyclePhases WHERE TenantId=@TenantId AND ProjectId=@ProjectId)
                    INSERT dbo.ProjectLifecyclePhases (TenantId, ProjectId, Revision, Phase, ChangedByActorUserId)
                    VALUES (@TenantId, @ProjectId, 1, N'Shaping', @ActorUserId);
                IF NOT EXISTS (SELECT 1 FROM dbo.ProjectUnderstandings WHERE TenantId=@TenantId AND ProjectId=@ProjectId)
                    INSERT dbo.ProjectUnderstandings (TenantId, ProjectId, Revision, Status, UnderstandingJson, CreatedByActorUserId)
                    VALUES (@TenantId, @ProjectId, 1, N'Draft', N'{}', @ActorUserId);
                IF NOT EXISTS (SELECT 1 FROM dbo.ProjectReadinessAssessments WHERE TenantId=@TenantId AND ProjectId=@ProjectId)
                    INSERT dbo.ProjectReadinessAssessments
                        (TenantId, ProjectId, Revision, ExecutionReadiness, ReasonCode, Summary, AssessedByActorUserId)
                    VALUES
                        (@TenantId, @ProjectId, 1, N'NotConfigured', N'RepositoryNotConfigured',
                         N'Repository and execution profile have not been configured.', @ActorUserId);
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));

            // Serialize every open/takeover/expiry path on the project's active-lease key range
            // before any AgentRun row is locked or changed.
            _ = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                """
                SELECT TOP (1) lease.Id
                FROM dbo.WorkbenchWriteLeases lease WITH (UPDLOCK, HOLDLOCK)
                WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
                  AND lease.RevokedAtUtc IS NULL
                ORDER BY lease.LeaseEpoch DESC;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchOutboxEvents
                    (EventId, TenantId, ProjectId, WorkbenchSessionId, AgentRunId, EventKind,
                     PayloadJson, ClientOperationId, DedupeKey)
                SELECT NEWID(), run.TenantId, run.ProjectId, run.WorkbenchSessionId, run.AgentRunId,
                       N'AgentRunStale',
                       CONCAT(N'{"agentRunId":"', CONVERT(NVARCHAR(36), run.AgentRunId),
                              N'","reason":"lease_expired"}'),
                       run.ClientOperationId,
                       CONCAT(N'agent-run-stale:', CONVERT(NVARCHAR(36), run.AgentRunId))
                FROM dbo.WorkbenchAgentRuns run
                INNER JOIN dbo.WorkbenchWriteLeases lease
                    ON lease.TenantId=run.TenantId AND lease.ProjectId=run.ProjectId
                   AND lease.WorkbenchSessionId=run.WorkbenchSessionId AND lease.LeaseEpoch=run.LeaseEpoch
                WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
                  AND lease.RevokedAtUtc IS NULL AND lease.ExpiresAtUtc <= SYSUTCDATETIME()
                  AND run.Status IN (N'Pending', N'Running')
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM dbo.WorkbenchOutboxEvents existing
                      WHERE existing.DedupeKey=CONCAT(N'agent-run-stale:', CONVERT(NVARCHAR(36), run.AgentRunId))
                  );

                UPDATE run
                SET Status=N'Stale',
                    CancellationRequestedAtUtc=CASE WHEN run.Status=N'Running'
                        THEN COALESCE(run.CancellationRequestedAtUtc, SYSUTCDATETIME())
                        ELSE run.CancellationRequestedAtUtc END,
                    DiagnosticCode=COALESCE(run.DiagnosticCode, N'lease_expired'),
                    DiagnosticAtUtc=COALESCE(run.DiagnosticAtUtc, SYSUTCDATETIME()),
                    CompletedAtUtc=COALESCE(run.CompletedAtUtc, SYSUTCDATETIME()),
                    ClaimExpiresAtUtc=NULL,
                    ActiveRunSlot=NULL
                FROM dbo.WorkbenchAgentRuns run
                INNER JOIN dbo.WorkbenchWriteLeases lease
                    ON lease.TenantId=run.TenantId AND lease.ProjectId=run.ProjectId
                   AND lease.WorkbenchSessionId=run.WorkbenchSessionId AND lease.LeaseEpoch=run.LeaseEpoch
                WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
                  AND lease.RevokedAtUtc IS NULL AND lease.ExpiresAtUtc <= SYSUTCDATETIME()
                  AND run.Status IN (N'Pending', N'Running');

                UPDATE attempt
                SET Outcome=N'Stale',
                    DiagnosticCode=N'lease_expired',
                    CompletedAtUtc=SYSUTCDATETIME()
                FROM dbo.WorkbenchAgentRunAttempts attempt
                INNER JOIN dbo.WorkbenchAgentRuns run
                    ON run.AgentRunId=attempt.AgentRunId
                INNER JOIN dbo.WorkbenchWriteLeases lease
                    ON lease.TenantId=run.TenantId AND lease.ProjectId=run.ProjectId
                  AND lease.WorkbenchSessionId=run.WorkbenchSessionId AND lease.LeaseEpoch=run.LeaseEpoch
                WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
                  AND lease.RevokedAtUtc IS NULL AND lease.ExpiresAtUtc <= SYSUTCDATETIME()
                  AND run.Status=N'Stale'
                  AND attempt.CompletedAtUtc IS NULL;

                UPDATE session
                SET Status=N'Historical', ClosedAtUtc=COALESCE(ClosedAtUtc, SYSUTCDATETIME())
                FROM dbo.WorkbenchSessions session
                INNER JOIN dbo.WorkbenchWriteLeases lease
                    ON lease.TenantId=session.TenantId AND lease.ProjectId=session.ProjectId
                   AND lease.WorkbenchSessionId=session.Id
                WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
                  AND lease.RevokedAtUtc IS NULL AND lease.ExpiresAtUtc <= SYSUTCDATETIME();

                UPDATE dbo.WorkbenchWriteLeases
                SET RevokedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId
                  AND RevokedAtUtc IS NULL AND ExpiresAtUtc <= SYSUTCDATETIME();
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));

            var activeLease = await connection.QuerySingleOrDefaultAsync<LeaseRow>(new CommandDefinition(
                """
                SELECT lease.Id, lease.WorkbenchSessionId, lease.HolderActorUserId, lease.LeaseEpoch
                FROM dbo.WorkbenchWriteLeases lease WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN dbo.WorkbenchSessions session
                    ON session.TenantId=lease.TenantId AND session.ProjectId=lease.ProjectId
                   AND session.Id=lease.WorkbenchSessionId
                WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
                  AND lease.RevokedAtUtc IS NULL;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken));

            if (activeLease is not null && activeLease.HolderActorUserId != command.ActorUserId && !command.TakeOver)
                throw new WorkbenchLeaseTakeoverRequiredException();

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
                    OperationKind = ProjectStartOperationKinds.OpenWorkbenchProject,
                    ResourceScopeId = resourceScope,
                    command.ClientOperationId,
                    PayloadHash = payloadHash
                },
                transaction,
                cancellationToken: cancellationToken));

            long workbenchSessionId;
            long leaseEpoch;
            var wasResumed = activeLease is not null && activeLease.HolderActorUserId == command.ActorUserId;
            var wasTakenOver = activeLease is not null && activeLease.HolderActorUserId != command.ActorUserId;

            if (wasResumed)
            {
                workbenchSessionId = activeLease!.WorkbenchSessionId;
                leaseEpoch = activeLease.LeaseEpoch;
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.WorkbenchWriteLeases
                    SET HeartbeatAtUtc=SYSUTCDATETIME(), ExpiresAtUtc=DATEADD(MINUTE, 30, SYSUTCDATETIME())
                    WHERE Id=@Id AND RevokedAtUtc IS NULL;
                    """,
                    new { activeLease.Id },
                    transaction,
                    cancellationToken: cancellationToken));

            }
            else
            {
                if (activeLease is not null)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE dbo.WorkbenchWriteLeases SET RevokedAtUtc=SYSUTCDATETIME() WHERE Id=@Id AND RevokedAtUtc IS NULL;
                        UPDATE dbo.WorkbenchSessions SET Status=N'Historical', ClosedAtUtc=SYSUTCDATETIME()
                        WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@WorkbenchSessionId;
                        """,
                        new
                        {
                            activeLease.Id,
                            command.TenantId,
                            command.ProjectId,
                            activeLease.WorkbenchSessionId
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                }

                leaseEpoch = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT COALESCE(MAX(LeaseEpoch), 0) + 1 FROM dbo.WorkbenchWriteLeases WHERE TenantId=@TenantId AND ProjectId=@ProjectId;",
                    command,
                    transaction,
                    cancellationToken: cancellationToken));
                workbenchSessionId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                    """
                    INSERT dbo.WorkbenchSessions (TenantId, ProjectId, Status, CreatedByActorUserId)
                    OUTPUT inserted.Id
                    VALUES (@TenantId, @ProjectId, N'Active', @ActorUserId);
                    """,
                    command,
                    transaction,
                    cancellationToken: cancellationToken));
                var leaseTokenHash = ComputeHash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT dbo.WorkbenchWriteLeases
                        (TenantId, ProjectId, WorkbenchSessionId, HolderActorUserId, LeaseEpoch, LeaseTokenHash,
                         AcquiredAtUtc, HeartbeatAtUtc, ExpiresAtUtc)
                    VALUES
                        (@TenantId, @ProjectId, @WorkbenchSessionId, @ActorUserId, @LeaseEpoch, @LeaseTokenHash,
                         SYSUTCDATETIME(), SYSUTCDATETIME(), DATEADD(MINUTE, 30, SYSUTCDATETIME()));
                    """,
                    new
                    {
                        command.TenantId,
                        command.ProjectId,
                        WorkbenchSessionId = workbenchSessionId,
                        command.ActorUserId,
                        LeaseEpoch = leaseEpoch,
                        LeaseTokenHash = leaseTokenHash
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                if (wasTakenOver)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT dbo.WorkbenchOutboxEvents
                            (EventId, TenantId, ProjectId, WorkbenchSessionId, AgentRunId, EventKind,
                             PayloadJson, ClientOperationId, DedupeKey)
                        SELECT NEWID(), run.TenantId, run.ProjectId, run.WorkbenchSessionId, run.AgentRunId,
                               N'AgentRunSuperseded',
                               CONCAT(N'{"agentRunId":"', CONVERT(NVARCHAR(36), run.AgentRunId),
                                      N'","supersededByWorkbenchSessionId":', CONVERT(NVARCHAR(30), @NewWorkbenchSessionId),
                                      N',"supersededByLeaseEpoch":', CONVERT(NVARCHAR(30), @NewLeaseEpoch), N'}'),
                               run.ClientOperationId,
                               CONCAT(N'agent-run-superseded:', CONVERT(NVARCHAR(36), run.AgentRunId),
                                      N':', CONVERT(NVARCHAR(30), @NewLeaseEpoch))
                        FROM dbo.WorkbenchAgentRuns run
                        WHERE run.TenantId=@TenantId AND run.ProjectId=@ProjectId
                          AND run.WorkbenchSessionId=@OldWorkbenchSessionId AND run.LeaseEpoch=@OldLeaseEpoch
                          AND run.Status IN (N'Pending', N'Running');

                        UPDATE dbo.WorkbenchAgentRuns
                        SET Status=N'Superseded',
                            CancellationRequestedAtUtc=COALESCE(CancellationRequestedAtUtc, SYSUTCDATETIME()),
                            SupersededAtUtc=COALESCE(SupersededAtUtc, SYSUTCDATETIME()),
                            SupersededByWorkbenchSessionId=@NewWorkbenchSessionId,
                            SupersededByLeaseEpoch=@NewLeaseEpoch,
                            DiagnosticCode=COALESCE(DiagnosticCode, N'workbench_lease_taken_over'),
                            DiagnosticAtUtc=COALESCE(DiagnosticAtUtc, SYSUTCDATETIME()),
                            CompletedAtUtc=COALESCE(CompletedAtUtc, SYSUTCDATETIME()),
                            ClaimExpiresAtUtc=NULL,
                            ActiveRunSlot=NULL
                        WHERE TenantId=@TenantId AND ProjectId=@ProjectId
                          AND WorkbenchSessionId=@OldWorkbenchSessionId AND LeaseEpoch=@OldLeaseEpoch
                          AND Status IN (N'Pending', N'Running');

                        UPDATE attempt
                        SET Outcome=N'Superseded',
                            DiagnosticCode=N'workbench_lease_taken_over',
                            CompletedAtUtc=SYSUTCDATETIME()
                        FROM dbo.WorkbenchAgentRunAttempts attempt
                        INNER JOIN dbo.WorkbenchAgentRuns run
                            ON run.AgentRunId=attempt.AgentRunId
                        WHERE run.TenantId=@TenantId AND run.ProjectId=@ProjectId
                          AND run.WorkbenchSessionId=@OldWorkbenchSessionId AND run.LeaseEpoch=@OldLeaseEpoch
                          AND run.Status=N'Superseded'
                          AND run.SupersededByWorkbenchSessionId=@NewWorkbenchSessionId
                          AND run.SupersededByLeaseEpoch=@NewLeaseEpoch
                          AND attempt.CompletedAtUtc IS NULL;
                        """,
                        new
                        {
                            command.TenantId,
                            command.ProjectId,
                            OldWorkbenchSessionId = activeLease!.WorkbenchSessionId,
                            OldLeaseEpoch = activeLease.LeaseEpoch,
                            NewWorkbenchSessionId = workbenchSessionId,
                            NewLeaseEpoch = leaseEpoch
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                }
            }

            var result = new WorkbenchProjectEntryContext(
                project.ProjectId,
                project.TenantId,
                project.Name,
                project.ProjectLifecyclePhase,
                project.ExecutionReadiness,
                workbenchSessionId,
                leaseEpoch,
                wasResumed,
                wasTakenOver,
                command.ClientOperationId);
            var canonicalResultJson = JsonSerializer.Serialize(result);
            var resultHash = ComputeHash(canonicalResultJson);
            var eventKind = wasTakenOver ? "WorkbenchLeaseTakenOver" : wasResumed ? "WorkbenchSessionResumed" : "WorkbenchSessionOpened";

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchOutboxEvents
                    (EventId, TenantId, ProjectId, WorkbenchSessionId, EventKind, PayloadJson, ClientOperationId)
                VALUES
                    (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, @EventKind, @PayloadJson, @ClientOperationId);

                INSERT dbo.UserMutationAttribution
                    (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId, TimestampUtc,
                     SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
                VALUES
                    (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                     CONVERT(NVARCHAR(128), @ClientOperationId), NULL, SYSUTCDATETIME(),
                     N'Workbench', N'IronDev.Api', N'POST', N'/api/workbench/projects/{projectId}/open', N'Completed', 200);

                UPDATE dbo.ClientOperations
                SET Status=N'Completed', ResultProjectId=@ProjectId, ResultWorkbenchSessionId=@WorkbenchSessionId,
                    CanonicalResultJson=@CanonicalResultJson, ResultHash=@ResultHash, CompletedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
                  AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
                  AND ClientOperationId=@ClientOperationId;
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    WorkbenchSessionId = workbenchSessionId,
                    command.ActorUserId,
                    command.ClientOperationId,
                    EventKind = eventKind,
                    PayloadJson = canonicalResultJson,
                    CanonicalResultJson = canonicalResultJson,
                    ResultHash = resultHash,
                    OperationKind = ProjectStartOperationKinds.OpenWorkbenchProject,
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

    public async Task<bool> ValidateAndRenewCurrentWriteLeaseAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connections.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE lease
            SET HeartbeatAtUtc=SYSUTCDATETIME(),
                ExpiresAtUtc=DATEADD(MINUTE, 30, SYSUTCDATETIME())
            FROM dbo.WorkbenchWriteLeases lease
            INNER JOIN dbo.WorkbenchSessions session
                ON session.TenantId=lease.TenantId AND session.ProjectId=lease.ProjectId
               AND session.Id=lease.WorkbenchSessionId AND session.Status=N'Active'
            INNER JOIN dbo.ProjectMembers member
                ON member.TenantId=lease.TenantId AND member.ProjectId=lease.ProjectId
               AND member.UserId=@ActorUserId AND member.Status=N'Active'
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=lease.TenantId AND tenantMember.UserId=@ActorUserId
            INNER JOIN dbo.Users actor
                ON actor.Id=@ActorUserId AND actor.IsActive=1
            WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
              AND lease.WorkbenchSessionId=@WorkbenchSessionId AND lease.LeaseEpoch=@LeaseEpoch
              AND lease.HolderActorUserId=@ActorUserId AND lease.RevokedAtUtc IS NULL
              AND lease.ExpiresAtUtc > SYSUTCDATETIME();
            """,
            new { TenantId = tenantId, ActorUserId = actorUserId, ProjectId = projectId, WorkbenchSessionId = workbenchSessionId, LeaseEpoch = leaseEpoch },
            cancellationToken: cancellationToken)) > 0;
    }

    private static WorkbenchProjectEntryContext ReadStoredResult(ClientOperationRow row, OpenWorkbenchProjectCommand command)
    {
        if (string.IsNullOrWhiteSpace(row.CanonicalResultJson) || string.IsNullOrWhiteSpace(row.ResultHash))
            throw new InvalidOperationException("The completed Workbench-open operation has no canonical result.");
        if (!string.Equals(ComputeHash(row.CanonicalResultJson), row.ResultHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stored Workbench-open result failed its integrity check.");
        var result = JsonSerializer.Deserialize<WorkbenchProjectEntryContext>(row.CanonicalResultJson)
            ?? throw new InvalidOperationException("The stored Workbench-open result could not be read.");
        if (result.ProjectId != command.ProjectId || result.ClientOperationId != command.ClientOperationId)
            throw new InvalidOperationException("The stored Workbench-open result belongs to another operation.");
        return result;
    }

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class ClientOperationRow
    {
        public string PayloadHash { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }

    private sealed class ProjectEntryRow
    {
        public int ProjectId { get; init; }
        public int TenantId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ProjectLifecyclePhase { get; init; } = string.Empty;
        public string ExecutionReadiness { get; init; } = string.Empty;
    }

    private sealed class LeaseRow
    {
        public long Id { get; init; }
        public long WorkbenchSessionId { get; init; }
        public int HolderActorUserId { get; init; }
        public long LeaseEpoch { get; init; }
    }
}
