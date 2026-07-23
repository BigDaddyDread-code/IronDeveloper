using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;
using Microsoft.Data.SqlClient;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchBuilderPromptPreparationService(
    IDbConnectionFactory connections,
    IBuilderRepositoryBranchObserver branches,
    TimeProvider timeProvider) : IWorkbenchBuilderPromptPreparationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PreparedBuilderAgentRun> PrepareAsync(
        PrepareBuilderAgentRunCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var expectedHash = NormalizeHash(command.ExpectedCoreSha256);
        var payloadHash = Hash(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            command.ProjectId,
            command.BuilderExecutionAuthorizationId,
            command.BuilderWorkPackageCoreId,
            expectedCoreSha256 = expectedHash
        }, JsonOptions));
        var resourceScope = $"builder-authorization:{command.BuilderExecutionAuthorizationId:D}:prepare";

        string canonicalPath;
        using (var readConnection = connections.CreateConnection())
        {
            readConnection.Open();
            canonicalPath = await readConnection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT binding.CanonicalPath
                FROM dbo.RepositoryBindings binding
                INNER JOIN dbo.ProjectMembers member
                    ON member.TenantId=binding.TenantId AND member.ProjectId=binding.ProjectId
                   AND member.UserId=@ActorUserId AND member.Status=N'Active'
                   AND member.ProjectRole IN (N'Owner', N'Contributor')
                INNER JOIN dbo.TenantUsers tenantMember
                    ON tenantMember.TenantId=binding.TenantId AND tenantMember.UserId=@ActorUserId
                INNER JOIN dbo.Users actor ON actor.Id=@ActorUserId AND actor.IsActive=1
                WHERE binding.TenantId=@TenantId AND binding.ProjectId=@ProjectId;
                """,
                new { command.TenantId, command.ActorUserId, command.ProjectId },
                cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? throw new WorkbenchProjectNotAccessibleException();
        }

        var observedBranch = await branches.ObserveAsync(canonicalPath, cancellationToken).ConfigureAwait(false);

        using var connection = connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var existing = await ReadOperationAsync(
                connection, transaction, command, resourceScope, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new BuilderPromptPreparationOperationMismatchException();
                var replay = ReadResult(existing) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            if (!await ValidateAndRenewLeaseAsync(
                    connection, transaction, command, cancellationToken).ConfigureAwait(false))
                throw new WorkbenchLeaseFenceException();

            var authority = await connection.QuerySingleOrDefaultAsync<PreparationAuthorityRow>(
                new CommandDefinition(
                    """
                    SELECT authz.Id AS AuthorizationId, authz.ActorUserId,
                           authz.BuilderWorkPackageCoreId,
                           authz.BuilderWorkPackageCoreHash,
                           authz.GrantedAtUtc, authz.ExpiresAtUtc,
                           authz.SingleUse, authz.ConsumedAtUtc,
                           authz.ConsumedByBuilderExecutionRunId,
                           authz.RevokedAtUtc,
                           core.CanonicalJson, core.CoreHash
                    FROM dbo.BuilderExecutionAuthorizations authz WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.BuilderWorkPackageCores core WITH (HOLDLOCK)
                        ON core.TenantId=authz.TenantId
                       AND core.ProjectId=authz.ProjectId
                       AND core.Id=authz.BuilderWorkPackageCoreId
                    WHERE authz.TenantId=@TenantId
                      AND authz.ProjectId=@ProjectId
                      AND authz.Id=@BuilderExecutionAuthorizationId;
                    """,
                    new
                    {
                        command.TenantId,
                        command.ProjectId,
                        command.BuilderExecutionAuthorizationId
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? throw Conflict(
                    BuilderPromptPreparationReasonCodes.AuthorizationScopeMismatch,
                    "The selected Builder authorization does not exist in this project.");

            EnsureAuthorization(command, authority, expectedHash);
            var core = ReadCore(authority);
            EnsureObservedRepository(core, observedBranch);

            var currentnessReason = await ReadCurrentnessReasonAsync(
                connection, transaction, command, core, cancellationToken).ConfigureAwait(false);
            if (currentnessReason is not null)
                throw Conflict(currentnessReason, "The exact Builder input authority is no longer current.");

            var preparedAt = timeProvider.GetUtcNow();
            if (preparedAt >= new DateTimeOffset(DateTime.SpecifyKind(authority.ExpiresAtUtc, DateTimeKind.Utc)))
                throw Conflict(
                    BuilderPromptPreparationReasonCodes.AuthorizationExpired,
                    "The Builder authorization expired before prompt preparation.");

            var runId = Guid.NewGuid();
            var package = new BuilderWorkPackage
            {
                Core = core,
                CoreSha256 = authority.CoreHash,
                SingleUseAuthorizationId = authority.AuthorizationId,
                AuthorizedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(authority.GrantedAtUtc, DateTimeKind.Utc)),
                ExpiresAtUtc = new DateTimeOffset(DateTime.SpecifyKind(authority.ExpiresAtUtc, DateTimeKind.Utc)),
                SingleUse = authority.SingleUse
            };
            var material = BuilderPromptContract.Materialize(package, runId, preparedAt);
            var operationRecordId = await InsertOperationAsync(
                connection, transaction, command, resourceScope, payloadHash, cancellationToken).ConfigureAwait(false);

            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT dbo.BuilderAgentRuns
                        (Id, TenantId, ProjectId, ActorUserId,
                         BuilderExecutionAuthorizationId, BuilderWorkPackageCoreId,
                         BuilderWorkPackageCoreSha256, BuilderAgentVersion, PromptVersion,
                         ToolPolicyVersion, ContextSchemaVersion, OutputSchemaVersion,
                         EffectiveProfileJson, EffectiveProfileSha256,
                         SystemPrompt, PromptSha256, RoleContextJson, RoleContextSha256,
                         ToolManifestJson, ToolManifestSha256,
                         ProviderInputJson, ProviderInputSha256,
                         ObservedBranchName, ObservedHeadCommit, Status,
                         PreparedAtUtc, ProviderInvocationPermittedAtUtc, ProviderInvokedAtUtc,
                         ClientOperationRecordId, ClientOperationId,
                         WorkbenchSessionId, LeaseEpoch)
                    VALUES
                        (@Id, @TenantId, @ProjectId, @ActorUserId,
                         @AuthorizationId, @CoreId, @CoreHash, @BuilderAgentVersion,
                         @PromptVersion, @ToolPolicyVersion, @ContextSchemaVersion,
                         @OutputSchemaVersion, @EffectiveProfileJson, @EffectiveProfileSha256,
                         @SystemPrompt, @PromptSha256, @RoleContextJson, @RoleContextSha256,
                         @ToolManifestJson, @ToolManifestSha256,
                         @ProviderInputJson, @ProviderInputSha256,
                         @ObservedBranchName, @ObservedHeadCommit, N'Prepared',
                         @PreparedAtUtc, @PreparedAtUtc, NULL,
                         @ClientOperationRecordId, @ClientOperationId,
                         @WorkbenchSessionId, @LeaseEpoch);
                    """,
                    new
                    {
                        Id = runId,
                        command.TenantId,
                        command.ProjectId,
                        command.ActorUserId,
                        authority.AuthorizationId,
                        CoreId = core.Id,
                        CoreHash = authority.CoreHash,
                        core.BuilderAgentVersion,
                        core.PromptVersion,
                        core.ToolPolicyVersion,
                        core.ContextSchemaVersion,
                        core.OutputSchemaVersion,
                        material.EffectiveProfileJson,
                        material.EffectiveProfileSha256,
                        material.SystemPrompt,
                        material.PromptSha256,
                        material.RoleContextJson,
                        material.RoleContextSha256,
                        material.ToolManifestJson,
                        material.ToolManifestSha256,
                        material.ProviderInputJson,
                        material.ProviderInputSha256,
                        ObservedBranchName = observedBranch.BranchName,
                        ObservedHeadCommit = observedBranch.HeadCommit,
                        PreparedAtUtc = preparedAt.UtcDateTime,
                        ClientOperationRecordId = operationRecordId,
                        command.ClientOperationId,
                        command.WorkbenchSessionId,
                        command.LeaseEpoch
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            catch (SqlException exception) when (exception.Number is 51500 or 51501 or 51502)
            {
                throw Conflict(
                    exception.Number == 51501
                        ? BuilderPromptPreparationReasonCodes.WorkPackageChanged
                        : BuilderPromptPreparationReasonCodes.ReadinessChanged,
                    "The Builder authority changed during atomic prompt preparation.");
            }
            catch (SqlException exception) when (exception.Number is 51505 or 51506 or 51507 or 51508)
            {
                throw new BuilderPromptPreparationIntegrityException(
                    "The prepared Builder role context, prompt, tools, or provider input failed server-side validation.");
            }

            var consumed = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.BuilderExecutionAuthorizations
                SET ConsumedAtUtc=@PreparedAtUtc,
                    ConsumedByBuilderExecutionRunId=@BuilderAgentRunId
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@AuthorizationId
                  AND ActorUserId=@ActorUserId
                  AND BuilderWorkPackageCoreId=@CoreId
                  AND BuilderWorkPackageCoreHash=@CoreHash
                  AND ConsumedAtUtc IS NULL AND ConsumedByBuilderExecutionRunId IS NULL
                  AND RevokedAtUtc IS NULL AND ExpiresAtUtc>@PreparedAtUtc;
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    authority.AuthorizationId,
                    command.ActorUserId,
                    CoreId = core.Id,
                    CoreHash = authority.CoreHash,
                    PreparedAtUtc = preparedAt.UtcDateTime,
                    BuilderAgentRunId = runId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (consumed != 1)
                throw Conflict(
                    BuilderPromptPreparationReasonCodes.AuthorizationConsumed,
                    "The single-use Builder authorization could not be consumed.");

            var result = new PreparedBuilderAgentRun
            {
                BuilderAgentRunId = runId,
                ProjectId = command.ProjectId,
                BuilderExecutionAuthorizationId = authority.AuthorizationId,
                BuilderWorkPackageCoreId = core.Id,
                BuilderWorkPackageCoreSha256 = authority.CoreHash,
                Status = BuilderAgentRunStates.Prepared,
                BuilderAgentVersion = core.BuilderAgentVersion,
                PromptVersion = core.PromptVersion,
                ToolPolicyVersion = core.ToolPolicyVersion,
                ContextSchemaVersion = core.ContextSchemaVersion,
                OutputSchemaVersion = core.OutputSchemaVersion,
                EffectiveProfileSha256 = material.EffectiveProfileSha256,
                RoleContextSha256 = material.RoleContextSha256,
                PromptSha256 = material.PromptSha256,
                ToolManifestSha256 = material.ToolManifestSha256,
                ProviderInputSha256 = material.ProviderInputSha256,
                PreparedAtUtc = preparedAt,
                ProviderInvocationPermittedAtUtc = preparedAt,
                ClientOperationId = command.ClientOperationId,
                IsReplay = false
            };
            await CompleteOperationAsync(
                connection, transaction, operationRecordId, runId, result, preparedAt,
                cancellationToken).ConfigureAwait(false);
            await InsertAuditAsync(
                connection, transaction, command, runId, core.Id, authority.AuthorizationId,
                material.ProviderInputSha256, preparedAt, cancellationToken).ConfigureAwait(false);

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void EnsureAuthorization(
        PrepareBuilderAgentRunCommand command,
        PreparationAuthorityRow authority,
        string expectedHash)
    {
        if (authority.ActorUserId != command.ActorUserId ||
            authority.BuilderWorkPackageCoreId != command.BuilderWorkPackageCoreId ||
            !string.Equals(authority.BuilderWorkPackageCoreHash, expectedHash, StringComparison.Ordinal) ||
            !string.Equals(authority.CoreHash, expectedHash, StringComparison.Ordinal))
            throw Conflict(
                BuilderPromptPreparationReasonCodes.AuthorizationScopeMismatch,
                "The authorization does not bind the exact requested Builder work package.");
        if (!authority.SingleUse)
            throw new BuilderPromptPreparationIntegrityException("Builder authorization is not single-use.");
        if (authority.RevokedAtUtc is not null)
            throw Conflict(BuilderPromptPreparationReasonCodes.AuthorizationRevoked, "Builder authorization was revoked.");
        if (authority.ConsumedAtUtc is not null || authority.ConsumedByBuilderExecutionRunId is not null)
            throw Conflict(BuilderPromptPreparationReasonCodes.AuthorizationConsumed, "Builder authorization was already consumed.");
    }

    private static void EnsureObservedRepository(
        BuilderWorkPackageCore core,
        BuilderRepositoryBranchObservation observed)
    {
        if (!string.Equals(core.BranchName, observed.BranchName, StringComparison.Ordinal) ||
            !string.Equals(core.BaselineCommit, observed.HeadCommit, StringComparison.Ordinal))
            throw Conflict(
                BuilderPromptPreparationReasonCodes.RepositoryBaselineChanged,
                "The repository branch or baseline changed before Builder prompt preparation.");
    }

    private static BuilderWorkPackageCore ReadCore(PreparationAuthorityRow authority)
    {
        try
        {
            var core = BuilderWorkPackageCoreCodec.DeserializeCanonical(authority.CanonicalJson);
            if (!string.Equals(
                    BuilderWorkPackageCoreCodec.ComputeHash(core),
                    authority.CoreHash,
                    StringComparison.Ordinal))
                throw new BuilderPromptPreparationIntegrityException("Stored Builder work-package hash mismatch.");
            return core;
        }
        catch (BuilderWorkPackageCoreValidationException exception)
        {
            throw new BuilderPromptPreparationIntegrityException(
                $"Stored Builder work-package content is invalid: {exception.Message}");
        }
    }

    private static async Task<string?> ReadCurrentnessReasonAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        PrepareBuilderAgentRunCommand command,
        BuilderWorkPackageCore core,
        CancellationToken cancellationToken)
    {
        var current = await connection.QuerySingleOrDefaultAsync<CurrentAuthorityRow>(new CommandDefinition(
            """
            SELECT readiness.ExecutionReadiness,
                   readiness.TechnicalReadinessEvidenceId,
                   binding.CurrentRevision AS RepositoryBindingRevision,
                   binding.BaselineCommit,
                   model.ConfigurationId AS BuilderConfigurationId,
                   model.ConfigurationSha256 AS BuilderConfigurationSha256,
                   evidence.SandboxPolicySha256,
                   evidence.ContainerImageDigestSha256,
                   evidence.SandboxEvidenceManifestSha256
            FROM dbo.RepositoryBindings binding WITH (UPDLOCK, HOLDLOCK)
            LEFT JOIN dbo.vw_WorkbenchEffectiveProjectReadiness readiness
                ON readiness.TenantId=binding.TenantId AND readiness.ProjectId=binding.ProjectId
            LEFT JOIN dbo.ProjectTechnicalReadinessEvidence evidence WITH (HOLDLOCK)
                ON evidence.TenantId=readiness.TenantId AND evidence.ProjectId=readiness.ProjectId
               AND evidence.Id=readiness.TechnicalReadinessEvidenceId
            LEFT JOIN dbo.BuilderModelConfigurationRecords model WITH (HOLDLOCK)
                ON model.TenantId=evidence.TenantId AND model.ProjectId=evidence.ProjectId
               AND model.Id=evidence.BuilderModelConfigurationRecordId
            WHERE binding.TenantId=@TenantId AND binding.ProjectId=@ProjectId;
            """,
            new { command.TenantId, command.ProjectId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (current is null ||
            current.RepositoryBindingRevision != core.RepositoryBindingRevision ||
            !string.Equals(current.BaselineCommit, core.BaselineCommit, StringComparison.Ordinal))
            return BuilderPromptPreparationReasonCodes.RepositoryBaselineChanged;
        if (!string.Equals(current.ExecutionReadiness, ProjectExecutionReadinessStates.Ready, StringComparison.Ordinal) ||
            current.TechnicalReadinessEvidenceId != core.ReadinessAssessment.TechnicalEvidenceId)
            return BuilderPromptPreparationReasonCodes.ReadinessChanged;
        if (current.BuilderConfigurationId != core.EffectiveProfile.BuilderConfigurationId ||
            !string.Equals(current.BuilderConfigurationSha256, core.EffectiveProfile.BuilderConfigurationSha256, StringComparison.Ordinal))
            return BuilderPromptPreparationReasonCodes.BuilderProfileChanged;
        if (!string.Equals(current.SandboxPolicySha256, core.Sandbox.PolicySha256, StringComparison.Ordinal) ||
            !string.Equals(current.ContainerImageDigestSha256, core.Sandbox.QualifiedImageDigest, StringComparison.Ordinal) ||
            !string.Equals(current.SandboxEvidenceManifestSha256, core.Sandbox.EvidenceManifestSha256, StringComparison.Ordinal))
            return BuilderPromptPreparationReasonCodes.SandboxPolicyChanged;
        return null;
    }

    private static void Validate(PrepareBuilderAgentRunCommand command)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty ||
            command.BuilderExecutionAuthorizationId == Guid.Empty ||
            command.BuilderWorkPackageCoreId == Guid.Empty)
            throw new BuilderPromptPreparationValidationException(
                "Tenant, actor, project, lease, operation, authorization, and work-package identities are required.");
    }

    private static string NormalizeHash(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.StartsWith("sha256:", StringComparison.Ordinal))
            normalized = normalized[7..];
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
            throw new BuilderPromptPreparationValidationException("ExpectedCoreSha256 must be a SHA-256 value.");
        return normalized;
    }

    private static BuilderPromptPreparationConflictException Conflict(string reason, string message) =>
        new(reason, message);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        PrepareBuilderAgentRunCommand command,
        string resourceScope,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
            """
            SELECT Id, PayloadHash, Status, CanonicalResultJson, ResultHash
            FROM dbo.ClientOperations WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
              AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
              AND ClientOperationId=@ClientOperationId;
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                OperationKind = BuilderPromptPreparationOperationKinds.PrepareAgentRun,
                ResourceScopeId = resourceScope,
                command.ClientOperationId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static PreparedBuilderAgentRun ReadResult(ClientOperationRow row)
    {
        if (row.Status != "Completed" || string.IsNullOrWhiteSpace(row.CanonicalResultJson) ||
            !string.Equals(Hash(row.CanonicalResultJson), row.ResultHash, StringComparison.Ordinal))
            throw new BuilderPromptPreparationIntegrityException("Stored Builder preparation replay failed integrity verification.");
        return JsonSerializer.Deserialize<PreparedBuilderAgentRun>(row.CanonicalResultJson, JsonOptions)
               ?? throw new BuilderPromptPreparationIntegrityException("Stored Builder preparation replay is unreadable.");
    }

    private static async Task<long> InsertOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        PrepareBuilderAgentRunCommand command,
        string resourceScope,
        string payloadHash,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleAsync<long>(new CommandDefinition(
            """
            INSERT dbo.ClientOperations
                (TenantId, ActorUserId, OperationKind, ResourceScopeId,
                 ClientOperationId, PayloadHash, Status, ResultProjectId,
                 ResultWorkbenchSessionId)
            OUTPUT inserted.Id
            VALUES
                (@TenantId, @ActorUserId, @OperationKind, @ResourceScopeId,
                 @ClientOperationId, @PayloadHash, N'Pending', @ProjectId,
                 @WorkbenchSessionId);
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                OperationKind = BuilderPromptPreparationOperationKinds.PrepareAgentRun,
                ResourceScopeId = resourceScope,
                command.ClientOperationId,
                PayloadHash = payloadHash,
                command.ProjectId,
                command.WorkbenchSessionId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static async Task CompleteOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long operationRecordId,
        Guid runId,
        PreparedBuilderAgentRun result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var resultJson = JsonSerializer.Serialize(result, JsonOptions);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ClientOperations
            SET Status=N'Completed', CanonicalResultJson=@ResultJson,
                ResultHash=@ResultHash, ResultBuilderAgentRunId=@RunId,
                CompletedAtUtc=@CompletedAtUtc
            WHERE Id=@Id AND Status=N'Pending';
            """,
            new
            {
                Id = operationRecordId,
                ResultJson = resultJson,
                ResultHash = Hash(resultJson),
                RunId = runId,
                CompletedAtUtc = completedAt.UtcDateTime
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (changed != 1)
            throw new BuilderPromptPreparationIntegrityException(
                "Builder preparation operation did not complete atomically.");
    }

    private static async Task InsertAuditAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        PrepareBuilderAgentRunCommand command,
        Guid runId,
        Guid coreId,
        Guid authorizationId,
        string inputHash,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            builderAgentRunId = runId,
            builderWorkPackageCoreId = coreId,
            builderExecutionAuthorizationId = authorizationId,
            providerInputSha256 = inputHash,
            providerInvoked = false
        }, JsonOptions);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.WorkbenchOutboxEvents
                (EventId, TenantId, ProjectId, WorkbenchSessionId,
                 BuilderWorkPackageCoreId, BuilderExecutionAuthorizationId,
                 BuilderAgentRunId, EventKind, PayloadJson, ClientOperationId, DedupeKey)
            VALUES
                (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId,
                 @CoreId, @AuthorizationId, @RunId, N'BuilderAgentRunPrepared',
                 @Payload, @ClientOperationId, @DedupeKey);

            INSERT dbo.UserMutationAttribution
                (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId,
                 TimestampUtc, SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
            VALUES
                (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                 CONVERT(NVARCHAR(128), @ClientOperationId), CONVERT(NVARCHAR(128), @AuthorizationId),
                 @PreparedAtUtc, N'Workbench', N'IronDev.Api', N'POST',
                 N'/api/workbench/projects/{projectId}/builder/agent-runs',
                 N'Completed', 201);
            """,
            new
            {
                command.TenantId,
                command.ProjectId,
                command.WorkbenchSessionId,
                CoreId = coreId,
                AuthorizationId = authorizationId,
                RunId = runId,
                Payload = payload,
                command.ClientOperationId,
                DedupeKey = $"builder-agent-run-prepared:{runId:D}",
                command.ActorUserId,
                PreparedAtUtc = preparedAt.UtcDateTime
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static Task<bool> ValidateAndRenewLeaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        PrepareBuilderAgentRunCommand command,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE lease
            SET HeartbeatAtUtc=SYSUTCDATETIME(),
                ExpiresAtUtc=DATEADD(MINUTE, 30, SYSUTCDATETIME())
            FROM dbo.WorkbenchWriteLeases lease WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.WorkbenchSessions session
                ON session.TenantId=lease.TenantId AND session.ProjectId=lease.ProjectId
               AND session.Id=lease.WorkbenchSessionId AND session.Status=N'Active'
            INNER JOIN dbo.ProjectMembers member
                ON member.TenantId=lease.TenantId AND member.ProjectId=lease.ProjectId
               AND member.UserId=@ActorUserId AND member.Status=N'Active'
               AND member.ProjectRole IN (N'Owner', N'Contributor')
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=lease.TenantId AND tenantMember.UserId=@ActorUserId
            INNER JOIN dbo.Users actor ON actor.Id=@ActorUserId AND actor.IsActive=1
            WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
              AND lease.WorkbenchSessionId=@WorkbenchSessionId
              AND lease.LeaseEpoch=@LeaseEpoch AND lease.HolderActorUserId=@ActorUserId
              AND lease.RevokedAtUtc IS NULL AND lease.ExpiresAtUtc>SYSUTCDATETIME();
            """,
            command,
            transaction,
            cancellationToken: cancellationToken)).ContinueWith(
                task => task.GetAwaiter().GetResult() == 1,
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

    private sealed record ClientOperationRow(
        long Id,
        string PayloadHash,
        string Status,
        string? CanonicalResultJson,
        string? ResultHash);

    private sealed record PreparationAuthorityRow
    {
        public required Guid AuthorizationId { get; init; }
        public required int ActorUserId { get; init; }
        public required Guid BuilderWorkPackageCoreId { get; init; }
        public required string BuilderWorkPackageCoreHash { get; init; }
        public required DateTime GrantedAtUtc { get; init; }
        public required DateTime ExpiresAtUtc { get; init; }
        public required bool SingleUse { get; init; }
        public DateTime? ConsumedAtUtc { get; init; }
        public Guid? ConsumedByBuilderExecutionRunId { get; init; }
        public DateTime? RevokedAtUtc { get; init; }
        public required string CanonicalJson { get; init; }
        public required string CoreHash { get; init; }
    }

    private sealed record CurrentAuthorityRow
    {
        public string? ExecutionReadiness { get; init; }
        public Guid? TechnicalReadinessEvidenceId { get; init; }
        public long RepositoryBindingRevision { get; init; }
        public string? BaselineCommit { get; init; }
        public Guid? BuilderConfigurationId { get; init; }
        public string? BuilderConfigurationSha256 { get; init; }
        public string? SandboxPolicySha256 { get; init; }
        public string? ContainerImageDigestSha256 { get; init; }
        public string? SandboxEvidenceManifestSha256 { get; init; }
    }
}
