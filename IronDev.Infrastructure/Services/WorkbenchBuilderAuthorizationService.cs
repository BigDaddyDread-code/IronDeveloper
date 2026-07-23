using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Prepares one immutable, authorization-free Builder work-package core and grants or revokes
/// a short-lived single-use authorization for its exact hash. This service never starts Builder.
/// </summary>
public sealed class WorkbenchBuilderAuthorizationService : IWorkbenchBuilderAuthorizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(15);
    private const string SourceSurface = "Workbench";
    private const string SourceClient = "IronDev.Api";

    private readonly IDbConnectionFactory _connections;
    private readonly IWorkbenchRepositoryReadinessService _readiness;
    private readonly IBuilderRepositoryBranchObserver _branches;
    private readonly TimeProvider _time;

    public WorkbenchBuilderAuthorizationService(
        IDbConnectionFactory connections,
        IWorkbenchRepositoryReadinessService readiness,
        IBuilderRepositoryBranchObserver branches,
        TimeProvider time)
    {
        _connections = connections;
        _readiness = readiness;
        _branches = branches;
        _time = time;
    }

    public async Task<WorkbenchBuilderContext> GetContextAsync(
        GetWorkbenchBuilderContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(query.TenantId, query.ActorUserId, query.ProjectId);
        if (query.TicketId is <= 0)
            throw new BuilderAuthorizationValidationException("TicketId must be positive when supplied.");

        var readiness = await _readiness.GetContextAsync(
            new GetWorkbenchRepositoryReadinessContextQuery(
                query.TenantId,
                query.ActorUserId,
                query.ProjectId),
            cancellationToken).ConfigureAwait(false);

        using var connection = _connections.CreateConnection();
        connection.Open();
        if (!await CanAccessProjectAsync(
                connection,
                null,
                query.TenantId,
                query.ActorUserId,
                query.ProjectId,
                requireContributor: false,
                cancellationToken).ConfigureAwait(false))
            throw new WorkbenchProjectNotAccessibleException();

        var project = await ReadProjectContextAsync(
                connection,
                null,
                query.TenantId,
                query.ProjectId,
                lockRows: false,
                cancellationToken).ConfigureAwait(false)
            ?? throw new WorkbenchProjectNotAccessibleException();
        var ticketExists = query.TicketId is null || await TicketExistsAsync(
            connection,
            null,
            query.TenantId,
            query.ProjectId,
            query.TicketId.Value,
            cancellationToken).ConfigureAwait(false);

        BuilderRepositoryBranchObservation? branch = null;
        if (!string.IsNullOrWhiteSpace(project.CanonicalPath))
        {
            try
            {
                branch = await _branches.ObserveAsync(
                    project.CanonicalPath,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (BuilderAuthorizationNotAllowedException)
            {
                // The read context remains available and reports that authorization is unavailable.
            }
        }

        var coreRow = query.TicketId is null
            ? null
            : await ReadLatestCoreForTicketAsync(
                connection,
                query.TenantId,
                query.ProjectId,
                query.TicketId.Value,
                cancellationToken).ConfigureAwait(false);
        BuilderWorkPackageCore? core = null;
        BuilderWorkPackageResult? workPackage = null;
        if (coreRow is not null)
        {
            core = ReadCore(coreRow);
            workPackage = new BuilderWorkPackageResult(
                core,
                coreRow.CoreHash,
                coreRow.ClientOperationId,
                IsReplay: false);
        }

        var scopeReason = query.TicketId is null || !ticketExists
            ? BuilderAuthorizationReasonCodes.TicketRequired
            : project.ProjectLifecyclePhase != ProjectLifecyclePhases.Delivery
                ? BuilderAuthorizationReasonCodes.ProjectNotInDelivery
                : readiness.Evaluation.ExecutionReadiness != ProjectExecutionReadinessStates.Ready
                    ? BuilderAuthorizationReasonCodes.TechnicalReadinessNotCurrent
                    : core is null
                        ? BuilderAuthorizationReasonCodes.WorkPackageRequired
                        : await CurrentnessReasonAsync(
                            connection,
                            null,
                            query.TenantId,
                            query.ProjectId,
                            core,
                            branch,
                            lockRows: false,
                            cancellationToken).ConfigureAwait(false);

        BuilderExecutionAuthorizationSnapshot? authorization = null;
        if (core is not null)
        {
            var authorizationRow = await ReadLatestAuthorizationAsync(
                connection,
                null,
                query.TenantId,
                query.ProjectId,
                query.ActorUserId,
                core.Id,
                lockRows: false,
                cancellationToken).ConfigureAwait(false);
            if (authorizationRow is not null)
            {
                var actorStillAuthorized = await ActorStillAuthorizedAsync(
                    connection,
                    null,
                    authorizationRow.TenantId,
                    authorizationRow.ProjectId,
                    authorizationRow.ActorUserId,
                    cancellationToken).ConfigureAwait(false);
                authorization = ToSnapshot(
                    authorizationRow,
                    scopeReason,
                    actorStillAuthorized,
                    UtcNow());
            }
        }

        var canPrepare = query.TicketId is not null && ticketExists &&
                         project.ProjectLifecyclePhase == ProjectLifecyclePhases.Delivery &&
                         readiness.Evaluation.ExecutionReadiness == ProjectExecutionReadinessStates.Ready &&
                         branch is not null &&
                         string.Equals(branch.HeadCommit, project.BaselineCommit, StringComparison.Ordinal);
        var canGrant = core is not null &&
                       string.Equals(scopeReason, BuilderAuthorizationReasonCodes.Ready, StringComparison.Ordinal);
        return new WorkbenchBuilderContext(
            query.ProjectId,
            query.TicketId,
            project.ProjectLifecyclePhase,
            readiness.Evaluation.ExecutionReadiness,
            branch?.BranchName,
            project.BaselineCommit,
            workPackage,
            authorization,
            canPrepare,
            canGrant,
            canGrant || (canPrepare && core is null)
                ? BuilderAuthorizationReasonCodes.Ready
                : scopeReason);
    }

    public async Task<BuilderWorkPackageResult> CreateWorkPackageAsync(
        CreateBuilderWorkPackageCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        await EnsureContributorAsync(
            command.TenantId,
            command.ActorUserId,
            command.ProjectId,
            cancellationToken).ConfigureAwait(false);
        var resourceScope = $"project:{command.ProjectId}:builder-work-package";
        var payloadHash = Hash(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            command.ProjectId,
            ticketIds = command.TicketIds
        }, JsonOptions));

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    requireContributor: true,
                    cancellationToken).ConfigureAwait(false))
                throw new BuilderAuthorizationForbiddenException(
                    "Only an active project owner or contributor can prepare a Builder work package.");

            var existing = await ReadOperationAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                BuilderAuthorizationOperationKinds.CreateWorkPackage,
                resourceScope,
                command.ClientOperationId,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                EnsureOperation(existing, payloadHash);
                var replay = ReadResult<BuilderWorkPackageResult>(existing) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            await RequireLiveReadyAsync(
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                cancellationToken).ConfigureAwait(false);
            var branch = await ObserveBranchForProjectAsync(
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                cancellationToken).ConfigureAwait(false);

            if (!await ValidateAndRenewLeaseAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    cancellationToken).ConfigureAwait(false))
                throw new WorkbenchLeaseFenceException();

            var authority = await RequireReadyAuthorityAsync(
                connection,
                transaction,
                command.TenantId,
                command.ProjectId,
                lockRows: true,
                cancellationToken).ConfigureAwait(false);
            EnsureBranchMatches(authority, branch);
            var tickets = await ReadTicketsAsync(
                connection,
                transaction,
                command.TenantId,
                command.ProjectId,
                command.TicketIds,
                lockRows: true,
                cancellationToken).ConfigureAwait(false);
            var understanding = await ReadCurrentUnderstandingAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ProjectId,
                    lockRows: true,
                    cancellationToken).ConfigureAwait(false)
                ?? throw new BuilderAuthorizationIntegrityException(
                    "The project has no governing Project Understanding.");

            var createdAt = UtcNow();
            var core = BuilderWorkPackageCoreCodec.NormalizeAndValidate(new BuilderWorkPackageCore
            {
                Id = Guid.NewGuid(),
                CanonicalizationVersion = BuilderWorkPackageCoreContract.CanonicalizationVersion1,
                TenantId = command.TenantId,
                ProjectId = command.ProjectId,
                Tickets = command.TicketIds.Select((ticketId, index) =>
                {
                    var ticket = tickets[ticketId];
                    return new BuilderWorkPackageTicketReference
                    {
                        Ordinal = index + 1,
                        WorkItemId = ticket.WorkItemId,
                        WorkItemVersion = ticket.WorkItemVersion,
                        WorkItemContractId = ticket.WorkItemContractId,
                        WorkItemContractRevision = ticket.WorkItemContractRevision,
                        WorkItemContractSha256 = ticket.WorkItemContractSha256,
                        TicketId = ticket.Id,
                        TicketRevision = ticket.Revision,
                        AcceptanceCriteria = ticket.AcceptanceCriteria,
                        PermittedFiles = ParsePermittedFiles(ticket.PermittedFiles)
                    };
                }).ToArray(),
                GoverningArtifacts =
                [
                    new BuilderWorkPackageArtifactReference(
                        1,
                        BuilderWorkPackageGoverningArtifactKinds.ProjectUnderstanding,
                        understanding.Id,
                        understanding.Revision)
                ],
                RepositoryBindingId = authority.RepositoryBindingId,
                RepositoryBindingRevision = authority.RepositoryBindingRevision,
                BranchName = branch.BranchName,
                BaselineCommit = authority.BaselineCommit,
                ReadinessAssessment = new BuilderReadinessAssessmentSnapshot
                {
                    Id = authority.ReadinessAssessmentId,
                    Revision = authority.ReadinessAssessmentRevision,
                    TechnicalEvidenceId = authority.TechnicalReadinessEvidenceId,
                    EvidenceSha256 = authority.TechnicalReadinessEvidenceSha256,
                    AssessedAtUtc = AsUtc(authority.ReadinessAssessedAtUtc)
                },
                RepositoryObservation = new BuilderRepositoryObservationSnapshot
                {
                    Id = authority.RepositoryStateObservationId,
                    EvidenceSha256 = authority.RepositoryObservationEvidenceSha256,
                    HeadCommit = authority.RepositoryHeadCommit,
                    GitTreeId = authority.RepositoryGitTreeId,
                    WorktreeState = authority.RepositoryWorktreeState,
                    WorktreeFingerprint = authority.WorktreeFingerprint,
                    ObservedAtUtc = AsUtc(authority.RepositoryObservedAtUtc)
                },
                CodeIndex = new BuilderCodeIndexSnapshot
                {
                    Id = authority.CodeIndexSnapshotId,
                    Revision = authority.CodeIndexSnapshotRevision,
                    EvidenceSha256 = authority.CodeIndexEvidenceSha256,
                    SchemaVersion = authority.CodeIndexSchemaVersion,
                    IndexerVersion = authority.IndexerVersion,
                    IndexedContentSha256 = authority.IndexContentSha256,
                    Sources = ParseCodeIndexSources(authority.CodeIndexSourcesJson),
                    IndexedAtUtc = AsUtc(authority.CodeIndexIndexedAtUtc)
                },
                RestoreCommandSha256 = authority.RestoreCommandSha256,
                BuildCommandSha256 = authority.BuildCommandSha256,
                TestCommandSha256 = authority.TestCommandSha256,
                BuilderAgentVersion = BuilderRoleContract.BuilderAgentVersion,
                PromptVersion = BuilderRoleContract.PromptVersion,
                ToolPolicyVersion = BuilderRoleContract.ToolPolicyVersion,
                ContextSchemaVersion = BuilderRoleContract.ContextSchemaVersion,
                OutputSchemaVersion = BuilderRoleContract.OutputSchemaVersion,
                EffectiveProfile = new BuilderEffectiveProfileSnapshot
                {
                    ProjectExecutionProfileId = authority.ProjectExecutionProfileId,
                    ProjectExecutionProfileRevision = authority.ProjectExecutionProfileRevision,
                    ProfileDefinitionId = authority.ProfileDefinitionId,
                    ProfileDescriptorRevision = authority.ProfileDescriptorRevision,
                    ProfileDescriptorSha256 = authority.ProfileDescriptorSha256,
                    BuilderConfigurationId = authority.BuilderConfigurationId,
                    BuilderConfigurationRevision = authority.BuilderConfigurationRevision,
                    ProviderId = authority.ProviderId,
                    ModelId = authority.ModelId,
                    BuilderConfigurationSha256 = authority.BuilderConfigurationSha256
                },
                Sandbox = new BuilderSandboxAuthoritySnapshot
                {
                    QualificationAttemptId = authority.SandboxQualificationAttemptId,
                    EvidenceManifestId = authority.SandboxEvidenceManifestId,
                    EvidenceManifestSha256 = authority.SandboxEvidenceManifestSha256,
                    PolicyVersion = authority.SandboxPolicyVersion,
                    PolicySha256 = authority.SandboxPolicySha256,
                    QualifiedImageDigest = authority.ContainerImageDigestSha256,
                    ToolchainManifestId = authority.ToolchainManifestId,
                    ToolchainManifestSha256 = authority.ToolchainManifestSha256,
                    OfflineFeedManifestSha256 = authority.OfflineFeedManifestSha256,
                    TemplateBundleSha256 = authority.TemplateBundleSha256
                },
                CreatedAtUtc = createdAt
            });
            var canonicalJson = BuilderWorkPackageCoreCodec.SerializeCanonical(core);
            var coreHash = BuilderWorkPackageCoreCodec.ComputeHash(core);
            var operationRecordId = await InsertOperationAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.WorkbenchSessionId,
                BuilderAuthorizationOperationKinds.CreateWorkPackage,
                resourceScope,
                command.ClientOperationId,
                payloadHash,
                cancellationToken).ConfigureAwait(false);

            await InsertCoreAsync(
                connection,
                transaction,
                command.TenantId,
                command.ProjectId,
                core,
                canonicalJson,
                coreHash,
                cancellationToken).ConfigureAwait(false);
            var result = new BuilderWorkPackageResult(core, coreHash, command.ClientOperationId, IsReplay: false);
            await CompleteOperationAsync(
                connection,
                transaction,
                operationRecordId,
                result,
                resultBuilderWorkPackageCoreId: core.Id,
                resultBuilderExecutionAuthorizationId: null,
                createdAt.UtcDateTime,
                cancellationToken).ConfigureAwait(false);
            await InsertOutboxAsync(
                connection,
                transaction,
                command.TenantId,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.ClientOperationId,
                "BuilderWorkPackagePrepared",
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    command.ProjectId,
                    builderWorkPackageCoreId = core.Id,
                    builderWorkPackageCoreHash = coreHash,
                    ticketIds = command.TicketIds
                }, JsonOptions),
                createdAt.UtcDateTime,
                cancellationToken).ConfigureAwait(false);
            await InsertAttributionAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.ClientOperationId,
                core.Id,
                "/api/workbench/projects/{projectId}/builder/work-packages",
                "Completed",
                201,
                createdAt.UtcDateTime,
                cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<BuilderAuthorizationResult> GrantAsync(
        GrantBuilderExecutionAuthorizationCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        await EnsureContributorAsync(
            command.TenantId,
            command.ActorUserId,
            command.ProjectId,
            cancellationToken).ConfigureAwait(false);
        var expectedHash = NormalizeSha256(command.ExpectedCoreHash);
        var resourceScope = $"project:{command.ProjectId}:builder-authorization";
        var payloadHash = Hash(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            command.ProjectId,
            command.BuilderWorkPackageCoreId,
            expectedCoreHash = expectedHash
        }, JsonOptions));

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    requireContributor: true,
                    cancellationToken).ConfigureAwait(false))
                throw new BuilderAuthorizationForbiddenException(
                    "Only an active project owner or contributor can grant Builder authorization.");

            var existing = await ReadOperationAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                BuilderAuthorizationOperationKinds.GrantAuthorization,
                resourceScope,
                command.ClientOperationId,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                EnsureOperation(existing, payloadHash);
                var replay = ReadResult<BuilderAuthorizationResult>(existing) with { IsReplay = true };
                transaction.Commit();
                return replay;
            }

            var liveReadiness = await _readiness.GetContextAsync(
                new GetWorkbenchRepositoryReadinessContextQuery(
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId),
                cancellationToken).ConfigureAwait(false);
            if (liveReadiness.Evaluation.ExecutionReadiness != ProjectExecutionReadinessStates.Ready)
                throw new BuilderAuthorizationStaleScopeException(
                    BuilderAuthorizationReasonCodes.TechnicalReadinessNotCurrent,
                    "The reviewed Builder work package is no longer technically ready.");
            var branch = await ObserveBranchForProjectAsync(
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                cancellationToken).ConfigureAwait(false);

            if (!await ValidateAndRenewLeaseAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    cancellationToken).ConfigureAwait(false))
                throw new WorkbenchLeaseFenceException();

            var coreRow = await ReadCoreAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ProjectId,
                    command.BuilderWorkPackageCoreId,
                    lockRows: true,
                    cancellationToken).ConfigureAwait(false)
                ?? throw new BuilderAuthorizationNotAllowedException(
                    BuilderAuthorizationReasonCodes.WorkPackageRequired,
                    "The selected Builder work package does not exist in this project.");
            var core = ReadCore(coreRow);
            if (!string.Equals(coreRow.CoreHash, expectedHash, StringComparison.Ordinal))
                throw new BuilderAuthorizationStaleScopeException(
                    BuilderAuthorizationReasonCodes.WorkPackageRequired,
                    "The reviewed Builder work-package hash is no longer the selected hash.");

            var reason = await CurrentnessReasonAsync(
                connection,
                transaction,
                command.TenantId,
                command.ProjectId,
                core,
                branch,
                lockRows: true,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(reason, BuilderAuthorizationReasonCodes.Ready, StringComparison.Ordinal))
                throw new BuilderAuthorizationStaleScopeException(
                    reason,
                    "The Builder work-package scope is no longer current. Prepare and review a new package.");

            var grantedAt = UtcNow();
            var expiresAt = grantedAt.Add(AuthorizationLifetime);
            var operationRecordId = await InsertOperationAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.WorkbenchSessionId,
                BuilderAuthorizationOperationKinds.GrantAuthorization,
                resourceScope,
                command.ClientOperationId,
                payloadHash,
                cancellationToken).ConfigureAwait(false);
            var authorizationId = Guid.NewGuid();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.BuilderExecutionAuthorizations
                    (Id, TenantId, ProjectId, ActorUserId, BuilderWorkPackageCoreId,
                     BuilderWorkPackageCoreHash, GrantedAtUtc, ExpiresAtUtc, SingleUse,
                     ConsumedAtUtc, ConsumedByBuilderExecutionRunId, RevokedAtUtc,
                     GrantedByWorkbenchSessionId, GrantedUnderLeaseEpoch,
                     GrantClientOperationRecordId, GrantClientOperationId)
                VALUES
                    (@Id, @TenantId, @ProjectId, @ActorUserId, @BuilderWorkPackageCoreId,
                     @BuilderWorkPackageCoreHash, @GrantedAtUtc, @ExpiresAtUtc, 1,
                     NULL, NULL, NULL, @WorkbenchSessionId, @LeaseEpoch,
                     @GrantClientOperationRecordId, @ClientOperationId);
                """,
                new
                {
                    Id = authorizationId,
                    command.TenantId,
                    command.ProjectId,
                    command.ActorUserId,
                    command.BuilderWorkPackageCoreId,
                    BuilderWorkPackageCoreHash = coreRow.CoreHash,
                    GrantedAtUtc = grantedAt.UtcDateTime,
                    ExpiresAtUtc = expiresAt.UtcDateTime,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    GrantClientOperationRecordId = operationRecordId,
                    command.ClientOperationId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var snapshot = new BuilderExecutionAuthorizationSnapshot
            {
                Id = authorizationId,
                ProjectId = command.ProjectId,
                ActorUserId = command.ActorUserId,
                BuilderWorkPackageCoreId = command.BuilderWorkPackageCoreId,
                BuilderWorkPackageCoreHash = coreRow.CoreHash,
                GrantedAtUtc = grantedAt.UtcDateTime,
                ExpiresAtUtc = expiresAt.UtcDateTime,
                SingleUse = true,
                State = BuilderExecutionAuthorizationStates.Valid,
                ReasonCode = BuilderAuthorizationReasonCodes.Ready
            };
            var package = new BuilderWorkPackage
            {
                Core = core,
                CoreSha256 = coreRow.CoreHash,
                SingleUseAuthorizationId = authorizationId,
                AuthorizedAtUtc = grantedAt,
                ExpiresAtUtc = expiresAt,
                SingleUse = true
            };
            var result = new BuilderAuthorizationResult(
                snapshot,
                package,
                command.ClientOperationId,
                IsReplay: false);
            await CompleteOperationAsync(
                connection,
                transaction,
                operationRecordId,
                result,
                resultBuilderWorkPackageCoreId: command.BuilderWorkPackageCoreId,
                resultBuilderExecutionAuthorizationId: authorizationId,
                grantedAt.UtcDateTime,
                cancellationToken).ConfigureAwait(false);
            await InsertOutboxAsync(
                connection,
                transaction,
                command.TenantId,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.ClientOperationId,
                "BuilderExecutionAuthorized",
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    command.ProjectId,
                    builderExecutionAuthorizationId = authorizationId,
                    command.BuilderWorkPackageCoreId,
                    builderWorkPackageCoreHash = coreRow.CoreHash,
                    expiresAtUtc = expiresAt
                }, JsonOptions),
                grantedAt.UtcDateTime,
                cancellationToken).ConfigureAwait(false);
            await InsertAttributionAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.ClientOperationId,
                authorizationId,
                "/api/workbench/projects/{projectId}/builder/authorizations",
                "Completed",
                201,
                grantedAt.UtcDateTime,
                cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<BuilderAuthorizationRevocationResult> RevokeAsync(
        RevokeBuilderExecutionAuthorizationCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        await EnsureContributorAsync(
            command.TenantId,
            command.ActorUserId,
            command.ProjectId,
            cancellationToken).ConfigureAwait(false);
        var resourceScope =
            $"project:{command.ProjectId}:builder-authorization:{command.BuilderExecutionAuthorizationId:D}:revoke";
        var payloadHash = Hash(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            command.ProjectId,
            command.BuilderExecutionAuthorizationId
        }, JsonOptions));

        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    requireContributor: true,
                    cancellationToken).ConfigureAwait(false))
                throw new BuilderAuthorizationForbiddenException(
                    "Only an active project owner or contributor can revoke Builder authorization.");
            var existing = await ReadOperationAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                BuilderAuthorizationOperationKinds.RevokeAuthorization,
                resourceScope,
                command.ClientOperationId,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                EnsureOperation(existing, payloadHash);
                var replay = ReadResult<BuilderAuthorizationRevocationResult>(existing) with { IsReplay = true };
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
                    cancellationToken).ConfigureAwait(false))
                throw new WorkbenchLeaseFenceException();

            var row = await ReadAuthorizationByIdAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ProjectId,
                    command.BuilderExecutionAuthorizationId,
                    lockRows: true,
                    cancellationToken).ConfigureAwait(false)
                ?? throw new BuilderAuthorizationNotAllowedException(
                    BuilderAuthorizationReasonCodes.WorkPackageRequired,
                    "The Builder authorization does not exist in this project.");
            if (row.ActorUserId != command.ActorUserId)
                throw new BuilderAuthorizationForbiddenException(
                    "Only the actor who granted this Builder authorization can revoke it.");
            if (row.ConsumedAtUtc is not null)
                throw new BuilderAuthorizationNotAllowedException(
                    BuilderAuthorizationReasonCodes.AuthorizationConsumed,
                    "A consumed Builder authorization cannot be revoked.");

            var revokedAt = UtcNow();
            var operationRecordId = await InsertOperationAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.WorkbenchSessionId,
                BuilderAuthorizationOperationKinds.RevokeAuthorization,
                resourceScope,
                command.ClientOperationId,
                payloadHash,
                cancellationToken).ConfigureAwait(false);
            if (row.RevokedAtUtc is null)
            {
                var changed = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.BuilderExecutionAuthorizations
                    SET RevokedAtUtc=@RevokedAtUtc
                    WHERE Id=@Id AND TenantId=@TenantId AND ProjectId=@ProjectId
                      AND ActorUserId=@ActorUserId AND RevokedAtUtc IS NULL
                      AND ConsumedAtUtc IS NULL;
                    """,
                    new
                    {
                        Id = command.BuilderExecutionAuthorizationId,
                        command.TenantId,
                        command.ProjectId,
                        command.ActorUserId,
                        RevokedAtUtc = revokedAt.UtcDateTime
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (changed != 1)
                    throw new BuilderAuthorizationStaleScopeException(
                        BuilderAuthorizationReasonCodes.AuthorizationConsumed,
                        "The Builder authorization changed before revocation completed.");
                row = row with { RevokedAtUtc = revokedAt.UtcDateTime };
            }

            var snapshot = ToSnapshot(
                row,
                BuilderAuthorizationReasonCodes.Ready,
                actorStillAuthorized: true,
                revokedAt);
            var result = new BuilderAuthorizationRevocationResult(
                snapshot,
                command.ClientOperationId,
                IsReplay: false);
            await CompleteOperationAsync(
                connection,
                transaction,
                operationRecordId,
                result,
                resultBuilderWorkPackageCoreId: null,
                resultBuilderExecutionAuthorizationId: row.Id,
                revokedAt.UtcDateTime,
                cancellationToken).ConfigureAwait(false);
            await InsertOutboxAsync(
                connection,
                transaction,
                command.TenantId,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.ClientOperationId,
                "BuilderExecutionAuthorizationRevoked",
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    command.ProjectId,
                    builderExecutionAuthorizationId = row.Id,
                    builderWorkPackageCoreId = row.BuilderWorkPackageCoreId
                }, JsonOptions),
                revokedAt.UtcDateTime,
                cancellationToken).ConfigureAwait(false);
            await InsertAttributionAsync(
                connection,
                transaction,
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.ClientOperationId,
                row.Id,
                "/api/workbench/projects/{projectId}/builder/authorizations/{authorizationId}/revocations",
                "Completed",
                200,
                revokedAt.UtcDateTime,
                cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task RequireLiveReadyAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        CancellationToken cancellationToken)
    {
        var context = await _readiness.GetContextAsync(
            new GetWorkbenchRepositoryReadinessContextQuery(
                tenantId,
                actorUserId,
                projectId),
            cancellationToken).ConfigureAwait(false);
        if (context.Evaluation.ExecutionReadiness != ProjectExecutionReadinessStates.Ready)
            throw new BuilderAuthorizationNotAllowedException(
                BuilderAuthorizationReasonCodes.TechnicalReadinessNotCurrent,
                "Current technical readiness is required before preparing or authorizing Builder work.");
    }

    private async Task<BuilderRepositoryBranchObservation> ObserveBranchForProjectAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        if (!await CanAccessProjectAsync(
                connection,
                null,
                tenantId,
                actorUserId,
                projectId,
                requireContributor: false,
                cancellationToken).ConfigureAwait(false))
            throw new WorkbenchProjectNotAccessibleException();
        var project = await ReadProjectContextAsync(
                connection,
                null,
                tenantId,
                projectId,
                lockRows: false,
                cancellationToken).ConfigureAwait(false)
            ?? throw new WorkbenchProjectNotAccessibleException();
        if (string.IsNullOrWhiteSpace(project.CanonicalPath))
            throw new BuilderAuthorizationNotAllowedException(
                BuilderAuthorizationReasonCodes.TechnicalReadinessNotCurrent,
                "A qualified repository is required before preparing Builder work.");
        return await _branches.ObserveAsync(project.CanonicalPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureContributorAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        if (!await CanAccessProjectAsync(
                connection,
                null,
                tenantId,
                actorUserId,
                projectId,
                requireContributor: false,
                cancellationToken).ConfigureAwait(false))
            throw new WorkbenchProjectNotAccessibleException();
        if (!await CanAccessProjectAsync(
                connection,
                null,
                tenantId,
                actorUserId,
                projectId,
                requireContributor: true,
                cancellationToken).ConfigureAwait(false))
            throw new BuilderAuthorizationForbiddenException(
                "Only an active project owner or contributor can manage Builder authorization.");
    }

    private static async Task<string> CurrentnessReasonAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        BuilderWorkPackageCore core,
        BuilderRepositoryBranchObservation? branch,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        if (core.TenantId != tenantId || core.ProjectId != projectId)
            return BuilderAuthorizationReasonCodes.TechnicalEvidenceChanged;
        var authority = await ReadReadyAuthorityAsync(
            connection,
            transaction,
            tenantId,
            projectId,
            lockRows,
            cancellationToken).ConfigureAwait(false);
        if (authority is null ||
            authority.ProjectLifecyclePhase != ProjectLifecyclePhases.Delivery)
            return BuilderAuthorizationReasonCodes.ProjectNotInDelivery;
        if (authority.ExecutionReadiness != ProjectExecutionReadinessStates.Ready)
            return BuilderAuthorizationReasonCodes.TechnicalReadinessNotCurrent;
        if (authority.RepositoryBindingId != core.RepositoryBindingId ||
            authority.RepositoryBindingRevision != core.RepositoryBindingRevision)
            return BuilderAuthorizationReasonCodes.RepositoryRevisionChanged;
        if (!string.Equals(authority.BaselineCommit, core.BaselineCommit, StringComparison.Ordinal))
            return BuilderAuthorizationReasonCodes.RepositoryBaselineChanged;
        if (branch is null || !string.Equals(branch.BranchName, core.BranchName, StringComparison.Ordinal))
            return BuilderAuthorizationReasonCodes.RepositoryBranchChanged;
        if (!string.Equals(branch.HeadCommit, core.BaselineCommit, StringComparison.Ordinal))
            return BuilderAuthorizationReasonCodes.RepositoryBaselineChanged;
        if (!string.Equals(
                authority.WorktreeFingerprint,
                core.RepositoryObservation.WorktreeFingerprint,
                StringComparison.Ordinal))
            return BuilderAuthorizationReasonCodes.RepositoryFingerprintChanged;
        if (!AuthorityMatchesCore(authority, core))
            return BuilderAuthorizationReasonCodes.TechnicalEvidenceChanged;

        var ticketRows = await ReadTicketRevisionsAsync(
            connection,
            transaction,
            tenantId,
            projectId,
            core.Tickets.Select(static value => value.TicketId).ToArray(),
            lockRows,
            cancellationToken).ConfigureAwait(false);
        if (ticketRows.Count != core.Tickets.Count || core.Tickets.Any(ticket =>
                !ticketRows.TryGetValue(ticket.TicketId, out var current) ||
                !TicketMatches(ticket, current)))
            return BuilderAuthorizationReasonCodes.TicketRevisionChanged;

        foreach (var artifact in core.GoverningArtifacts)
        {
            if (artifact.ArtifactKind != BuilderWorkPackageGoverningArtifactKinds.ProjectUnderstanding)
                return BuilderAuthorizationReasonCodes.TechnicalEvidenceChanged;
            var current = await ReadCurrentUnderstandingAsync(
                connection,
                transaction,
                tenantId,
                projectId,
                lockRows,
                cancellationToken).ConfigureAwait(false);
            if (current is null || current.Id != artifact.ArtifactReferenceId ||
                current.Revision != artifact.Revision)
                return BuilderAuthorizationReasonCodes.TechnicalEvidenceChanged;
        }
        return BuilderAuthorizationReasonCodes.Ready;
    }

    private static bool TicketMatches(
        BuilderWorkPackageTicketReference expected,
        TicketRow current) =>
        expected.TicketRevision == current.Revision &&
        expected.WorkItemId == current.WorkItemId &&
        expected.WorkItemVersion == current.WorkItemVersion &&
        expected.WorkItemContractId == current.WorkItemContractId &&
        expected.WorkItemContractRevision == current.WorkItemContractRevision &&
        string.Equals(expected.WorkItemContractSha256, current.WorkItemContractSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.AcceptanceCriteria, current.AcceptanceCriteria, StringComparison.Ordinal) &&
        expected.PermittedFiles.SequenceEqual(ParsePermittedFiles(current.PermittedFiles), StringComparer.Ordinal);

    private static bool AuthorityMatchesCore(
        ReadyAuthorityRow current,
        BuilderWorkPackageCore expected)
    {
        var indexSources = ParseCodeIndexSources(current.CodeIndexSourcesJson);
        return expected.ReadinessAssessment.Id == current.ReadinessAssessmentId &&
               expected.ReadinessAssessment.Revision == current.ReadinessAssessmentRevision &&
               expected.ReadinessAssessment.TechnicalEvidenceId == current.TechnicalReadinessEvidenceId &&
               Equal(expected.ReadinessAssessment.EvidenceSha256, current.TechnicalReadinessEvidenceSha256) &&
               expected.ReadinessAssessment.AssessedAtUtc == AsUtc(current.ReadinessAssessedAtUtc) &&
               expected.RepositoryObservation.Id == current.RepositoryStateObservationId &&
               Equal(expected.RepositoryObservation.EvidenceSha256, current.RepositoryObservationEvidenceSha256) &&
               Equal(expected.RepositoryObservation.HeadCommit, current.RepositoryHeadCommit) &&
               Equal(expected.RepositoryObservation.GitTreeId, current.RepositoryGitTreeId) &&
               Equal(expected.RepositoryObservation.WorktreeState, current.RepositoryWorktreeState) &&
               expected.RepositoryObservation.ObservedAtUtc == AsUtc(current.RepositoryObservedAtUtc) &&
               expected.CodeIndex.Id == current.CodeIndexSnapshotId &&
               expected.CodeIndex.Revision == current.CodeIndexSnapshotRevision &&
               Equal(expected.CodeIndex.EvidenceSha256, current.CodeIndexEvidenceSha256) &&
               expected.CodeIndex.SchemaVersion == current.CodeIndexSchemaVersion &&
               Equal(expected.CodeIndex.IndexerVersion, current.IndexerVersion) &&
               Equal(expected.CodeIndex.IndexedContentSha256, current.IndexContentSha256) &&
               expected.CodeIndex.Sources.SequenceEqual(indexSources) &&
               expected.CodeIndex.IndexedAtUtc == AsUtc(current.CodeIndexIndexedAtUtc) &&
               Equal(expected.RestoreCommandSha256, current.RestoreCommandSha256) &&
               Equal(expected.BuildCommandSha256, current.BuildCommandSha256) &&
               Equal(expected.TestCommandSha256, current.TestCommandSha256) &&
               expected.EffectiveProfile.ProjectExecutionProfileId == current.ProjectExecutionProfileId &&
               expected.EffectiveProfile.ProjectExecutionProfileRevision == current.ProjectExecutionProfileRevision &&
               Equal(expected.EffectiveProfile.ProfileDefinitionId, current.ProfileDefinitionId) &&
               expected.EffectiveProfile.ProfileDescriptorRevision == current.ProfileDescriptorRevision &&
               Equal(expected.EffectiveProfile.ProfileDescriptorSha256, current.ProfileDescriptorSha256) &&
               expected.EffectiveProfile.BuilderConfigurationId == current.BuilderConfigurationId &&
               expected.EffectiveProfile.BuilderConfigurationRevision == current.BuilderConfigurationRevision &&
               Equal(expected.EffectiveProfile.ProviderId, current.ProviderId) &&
               Equal(expected.EffectiveProfile.ModelId, current.ModelId) &&
               Equal(expected.EffectiveProfile.BuilderConfigurationSha256, current.BuilderConfigurationSha256) &&
               expected.Sandbox.QualificationAttemptId == current.SandboxQualificationAttemptId &&
               expected.Sandbox.EvidenceManifestId == current.SandboxEvidenceManifestId &&
               Equal(expected.Sandbox.EvidenceManifestSha256, current.SandboxEvidenceManifestSha256) &&
               Equal(expected.Sandbox.PolicyVersion, current.SandboxPolicyVersion) &&
               Equal(expected.Sandbox.PolicySha256, current.SandboxPolicySha256) &&
               Equal(expected.Sandbox.QualifiedImageDigest, current.ContainerImageDigestSha256) &&
               Equal(expected.Sandbox.ToolchainManifestId, current.ToolchainManifestId) &&
               Equal(expected.Sandbox.ToolchainManifestSha256, current.ToolchainManifestSha256) &&
               Equal(expected.Sandbox.OfflineFeedManifestSha256, current.OfflineFeedManifestSha256) &&
               Equal(expected.Sandbox.TemplateBundleSha256, current.TemplateBundleSha256) &&
               Equal(expected.BuilderAgentVersion, BuilderRoleContract.BuilderAgentVersion) &&
               Equal(expected.PromptVersion, BuilderRoleContract.PromptVersion) &&
               Equal(expected.ToolPolicyVersion, BuilderRoleContract.ToolPolicyVersion) &&
               Equal(expected.ContextSchemaVersion, BuilderRoleContract.ContextSchemaVersion) &&
               Equal(expected.OutputSchemaVersion, BuilderRoleContract.OutputSchemaVersion);
    }

    private static bool Equal(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static async Task<ReadyAuthorityRow> RequireReadyAuthorityAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int projectId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var authority = await ReadReadyAuthorityAsync(
            connection,
            transaction,
            tenantId,
            projectId,
            lockRows,
            cancellationToken).ConfigureAwait(false);
        if (authority is null || authority.ProjectLifecyclePhase != ProjectLifecyclePhases.Delivery)
            throw new BuilderAuthorizationNotAllowedException(
                BuilderAuthorizationReasonCodes.ProjectNotInDelivery,
                "Permanent ticket delivery state is required before preparing Builder work.");
        if (authority.ExecutionReadiness != ProjectExecutionReadinessStates.Ready)
            throw new BuilderAuthorizationNotAllowedException(
                BuilderAuthorizationReasonCodes.TechnicalReadinessNotCurrent,
                "Current nine-gate technical readiness is required before preparing Builder work.");
        return authority;
    }

    private static Task<ReadyAuthorityRow?> ReadReadyAuthorityAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        return connection.QuerySingleOrDefaultAsync<ReadyAuthorityRow>(new CommandDefinition(
            $$"""
            SELECT effective.ExecutionReadiness, effective.ReasonCode,
                   lifecycle.Phase AS ProjectLifecyclePhase,
                   binding.Id AS RepositoryBindingId,
                   binding.CurrentRevision AS RepositoryBindingRevision,
                   binding.CanonicalPath, binding.BaselineCommit,
                   effective.ProjectReadinessAssessmentId AS ReadinessAssessmentId,
                   evidence.ProjectReadinessRevision AS ReadinessAssessmentRevision,
                   evidence.Id AS TechnicalReadinessEvidenceId,
                   evidence.EvidenceSha256 AS TechnicalReadinessEvidenceSha256,
                   evidence.AssessedAtUtc AS ReadinessAssessedAtUtc,
                   evidence.RepositoryStateObservationId,
                   evidence.RepositoryObservationEvidenceSha256,
                   observation.HeadCommit AS RepositoryHeadCommit,
                   observation.GitTreeId AS RepositoryGitTreeId,
                   observation.DirtyState AS RepositoryWorktreeState,
                   observation.ObservedAtUtc AS RepositoryObservedAtUtc,
                   evidence.RepositoryFingerprintSha256 AS WorktreeFingerprint,
                   evidence.ProjectExecutionProfileId,
                   evidence.ProjectExecutionProfileRevision,
                   evidence.ProfileDefinitionId,
                   evidence.ProfileDescriptorRevision,
                   evidence.ProfileDescriptorSha256,
                   evidence.CodeIndexSnapshotId,
                   technical.AttemptNumber AS CodeIndexSnapshotRevision,
                   evidence.CodeIndexEvidenceSha256,
                   codeIndex.IndexSchemaVersion AS CodeIndexSchemaVersion,
                   codeIndex.IndexerVersion,
                   codeIndex.IndexContentSha256,
                   codeIndex.SourcesJson AS CodeIndexSourcesJson,
                   codeIndex.CompletedAtUtc AS CodeIndexIndexedAtUtc,
                   evidence.RestoreCommandSha256,
                   evidence.BuildCommandSha256,
                   evidence.TestCommandSha256,
                   technical.SandboxPolicyVersion,
                   evidence.SandboxPolicySha256,
                   technical.ToolchainManifestId,
                   evidence.ToolchainManifestSha256,
                   evidence.ContainerImageDigestSha256,
                   evidence.OfflineFeedManifestSha256,
                   evidence.TemplateBundleSha256,
                   evidence.SandboxQualificationAttemptId,
                   evidence.SandboxEvidenceManifestId,
                   evidence.SandboxEvidenceManifestSha256,
                   model.ConfigurationId AS BuilderConfigurationId,
                   model.ConfigurationRevision AS BuilderConfigurationRevision,
                   model.ProviderId,
                   model.ModelId,
                   model.ConfigurationSha256 AS BuilderConfigurationSha256
            FROM dbo.vw_WorkbenchEffectiveProjectReadiness effective
            INNER JOIN dbo.RepositoryBindings binding{{hint}}
                ON binding.TenantId=effective.TenantId AND binding.ProjectId=effective.ProjectId
            INNER JOIN dbo.ProjectTechnicalReadinessEvidence evidence{{hint}}
                ON evidence.TenantId=effective.TenantId AND evidence.ProjectId=effective.ProjectId
               AND evidence.Id=effective.TechnicalReadinessEvidenceId
            INNER JOIN dbo.TechnicalValidationAttempts technical{{hint}}
                ON technical.TenantId=evidence.TenantId AND technical.ProjectId=evidence.ProjectId
               AND technical.Id=evidence.TechnicalValidationAttemptId
            INNER JOIN dbo.RepositoryStateObservations observation{{hint}}
                ON observation.TenantId=evidence.TenantId AND observation.ProjectId=evidence.ProjectId
               AND observation.Id=evidence.RepositoryStateObservationId
            INNER JOIN dbo.CodeIndexSnapshots codeIndex{{hint}}
                ON codeIndex.TenantId=evidence.TenantId AND codeIndex.ProjectId=evidence.ProjectId
               AND codeIndex.Id=evidence.CodeIndexSnapshotId
            INNER JOIN dbo.BuilderModelConfigurationRecords model{{hint}}
                ON model.TenantId=evidence.TenantId AND model.ProjectId=evidence.ProjectId
               AND model.Id=evidence.BuilderModelConfigurationRecordId
            OUTER APPLY
            (
                SELECT TOP (1) value.Phase
                FROM dbo.ProjectLifecyclePhases value{{hint}}
                WHERE value.TenantId=effective.TenantId AND value.ProjectId=effective.ProjectId
                ORDER BY value.Revision DESC, value.Id DESC
            ) lifecycle
            WHERE effective.TenantId=@TenantId AND effective.ProjectId=@ProjectId;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static void EnsureBranchMatches(
        ReadyAuthorityRow authority,
        BuilderRepositoryBranchObservation branch)
    {
        if (!string.Equals(branch.HeadCommit, authority.BaselineCommit, StringComparison.Ordinal))
            throw new BuilderAuthorizationStaleScopeException(
                BuilderAuthorizationReasonCodes.RepositoryBaselineChanged,
                "The current branch HEAD no longer matches the qualified repository baseline.");
    }

    private static async Task<Dictionary<long, TicketRow>> ReadTicketsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int projectId,
        IReadOnlyList<long> ticketIds,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var rows = (await connection.QueryAsync<TicketRow>(new CommandDefinition(
            $"""
            SELECT ticket.Id, ticket.Revision,
                   item.Id AS WorkItemId, item.Version AS WorkItemVersion,
                   contract.Id AS WorkItemContractId,
                   contract.ContractVersion AS WorkItemContractRevision,
                   contract.ContractHash AS WorkItemContractSha256,
                   contract.AcceptanceCriteria,
                   contract.LinkedFilePaths AS PermittedFiles
            FROM dbo.ProjectTickets ticket{hint}
            INNER JOIN dbo.WorkItems item{hint}
                ON item.TenantId=ticket.TenantId AND item.ProjectId=ticket.ProjectId
               AND item.LegacyTicketId=ticket.Id
            INNER JOIN dbo.WorkItemContracts contract{hint}
                ON contract.TenantId=item.TenantId AND contract.ProjectId=item.ProjectId
               AND contract.WorkItemId=item.Id AND contract.Id=item.CurrentContractId
            WHERE ticket.TenantId=@TenantId AND ticket.ProjectId=@ProjectId
              AND ticket.IsDeleted=0 AND ticket.Id IN @TicketIds;
            """,
            new { TenantId = tenantId, ProjectId = projectId, TicketIds = ticketIds },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();
        if (rows.Length != ticketIds.Count)
            throw new BuilderAuthorizationNotAllowedException(
                BuilderAuthorizationReasonCodes.TicketRequired,
                "Every selected ticket must have one current permanent Work Item contract in this project.");
        if (rows.Any(static row =>
                string.IsNullOrWhiteSpace(row.AcceptanceCriteria) ||
                string.IsNullOrWhiteSpace(row.PermittedFiles)))
            throw new BuilderAuthorizationNotAllowedException(
                BuilderAuthorizationReasonCodes.TicketRequired,
                "Every Builder ticket contract requires acceptance criteria and an explicit permitted-file scope.");
        return rows.ToDictionary(static value => value.Id);
    }

    private static async Task<IReadOnlyDictionary<long, TicketRow>> ReadTicketRevisionsAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        IReadOnlyList<long> ticketIds,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var rows = await connection.QueryAsync<TicketRow>(new CommandDefinition(
            $"""
            SELECT ticket.Id, ticket.Revision,
                   item.Id AS WorkItemId, item.Version AS WorkItemVersion,
                   contract.Id AS WorkItemContractId,
                   contract.ContractVersion AS WorkItemContractRevision,
                   contract.ContractHash AS WorkItemContractSha256,
                   contract.AcceptanceCriteria,
                   contract.LinkedFilePaths AS PermittedFiles
            FROM dbo.ProjectTickets ticket{hint}
            INNER JOIN dbo.WorkItems item{hint}
                ON item.TenantId=ticket.TenantId AND item.ProjectId=ticket.ProjectId
               AND item.LegacyTicketId=ticket.Id
            INNER JOIN dbo.WorkItemContracts contract{hint}
                ON contract.TenantId=item.TenantId AND contract.ProjectId=item.ProjectId
               AND contract.WorkItemId=item.Id AND contract.Id=item.CurrentContractId
            WHERE ticket.TenantId=@TenantId AND ticket.ProjectId=@ProjectId
              AND ticket.IsDeleted=0 AND ticket.Id IN @TicketIds;
            """,
            new { TenantId = tenantId, ProjectId = projectId, TicketIds = ticketIds },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.ToDictionary(static value => value.Id);
    }

    private static Task<UnderstandingRow?> ReadCurrentUnderstandingAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        return connection.QueryFirstOrDefaultAsync<UnderstandingRow>(new CommandDefinition(
            $$"""
            SELECT TOP (1) Id, Revision
            FROM dbo.ProjectUnderstandings{{hint}}
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
              AND Status<>N'Superseded'
            ORDER BY Revision DESC, Id DESC;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<ProjectContextRow?> ReadProjectContextAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        return connection.QuerySingleOrDefaultAsync<ProjectContextRow>(new CommandDefinition(
            $$"""
            SELECT lifecycle.Phase AS ProjectLifecyclePhase,
                   binding.CanonicalPath, binding.BaselineCommit
            FROM dbo.Projects project{{hint}}
            OUTER APPLY
            (
                SELECT TOP (1) value.Phase
                FROM dbo.ProjectLifecyclePhases value{{hint}}
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC, value.Id DESC
            ) lifecycle
            LEFT JOIN dbo.RepositoryBindings binding{{hint}}
                ON binding.TenantId=project.TenantId AND binding.ProjectId=project.Id
            WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<bool> TicketExistsAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        long ticketId,
        CancellationToken cancellationToken) => connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.ProjectTickets
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@TicketId AND IsDeleted=0;
            """,
            new { TenantId = tenantId, ProjectId = projectId, TicketId = ticketId },
            transaction,
            cancellationToken: cancellationToken)).ContinueWith(
                task => task.GetAwaiter().GetResult() == 1,
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

    private static Task<CoreRow?> ReadLatestCoreForTicketAsync(
        IDbConnection connection,
        int tenantId,
        int projectId,
        long ticketId,
        CancellationToken cancellationToken) => connection.QueryFirstOrDefaultAsync<CoreRow>(new CommandDefinition(
            """
            SELECT TOP (1) core.Id, core.CanonicalJson, core.CoreHash,
                   operation.ClientOperationId
            FROM dbo.BuilderWorkPackageCores core
            INNER JOIN dbo.BuilderWorkPackageTickets ticket
                ON ticket.TenantId=core.TenantId AND ticket.ProjectId=core.ProjectId
               AND ticket.BuilderWorkPackageCoreId=core.Id AND ticket.TicketId=@TicketId
            LEFT JOIN dbo.ClientOperations operation
                ON operation.ResultBuilderWorkPackageCoreId=core.Id
               AND operation.OperationKind=N'CreateBuilderWorkPackage'
               AND operation.Status=N'Completed'
            WHERE core.TenantId=@TenantId AND core.ProjectId=@ProjectId
            ORDER BY core.CreatedAtUtc DESC, core.Id DESC;
            """,
            new { TenantId = tenantId, ProjectId = projectId, TicketId = ticketId },
            cancellationToken: cancellationToken));

    private static Task<CoreRow?> ReadCoreAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int projectId,
        Guid coreId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        return connection.QuerySingleOrDefaultAsync<CoreRow>(new CommandDefinition(
            $$"""
            SELECT core.Id, core.CanonicalJson, core.CoreHash,
                   operation.ClientOperationId
            FROM dbo.BuilderWorkPackageCores core{{hint}}
            LEFT JOIN dbo.ClientOperations operation
                ON operation.ResultBuilderWorkPackageCoreId=core.Id
               AND operation.OperationKind=N'CreateBuilderWorkPackage'
               AND operation.Status=N'Completed'
            WHERE core.TenantId=@TenantId AND core.ProjectId=@ProjectId AND core.Id=@CoreId;
            """,
            new { TenantId = tenantId, ProjectId = projectId, CoreId = coreId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static BuilderWorkPackageCore ReadCore(CoreRow row)
    {
        BuilderWorkPackageCore core;
        try
        {
            core = BuilderWorkPackageCoreCodec.DeserializeCanonical(row.CanonicalJson);
        }
        catch (Exception exception) when (exception is JsonException or BuilderWorkPackageCoreValidationException)
        {
            throw new BuilderAuthorizationIntegrityException(
                "The stored Builder work-package core is invalid.");
        }
        var canonical = BuilderWorkPackageCoreCodec.SerializeCanonical(core);
        var hash = Hash(canonical);
        if (core.Id != row.Id || !string.Equals(hash, row.CoreHash, StringComparison.Ordinal))
            throw new BuilderAuthorizationIntegrityException(
                "The stored Builder work-package core hash does not match its canonical content.");
        return core;
    }

    private static async Task InsertCoreAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int projectId,
        BuilderWorkPackageCore core,
        string canonicalJson,
        string coreHash,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.BuilderWorkPackageCores
                (Id, TenantId, ProjectId, CanonicalizationVersion,
                 CanonicalJson, CoreHash, CreatedAtUtc)
            VALUES
                (@Id, @TenantId, @ProjectId, @CanonicalizationVersion,
                 @CanonicalJson, @CoreHash, @CreatedAtUtc);
            """,
            new
            {
                core.Id,
                TenantId = tenantId,
                ProjectId = projectId,
                core.CanonicalizationVersion,
                CanonicalJson = canonicalJson,
                CoreHash = coreHash,
                CreatedAtUtc = core.CreatedAtUtc.UtcDateTime
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        foreach (var ticket in core.Tickets)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.BuilderWorkPackageTickets
                    (TenantId, ProjectId, BuilderWorkPackageCoreId,
                     Ordinal, TicketId, TicketRevision)
                VALUES
                    (@TenantId, @ProjectId, @BuilderWorkPackageCoreId,
                     @Ordinal, @TicketId, @TicketRevision);
                """,
                new
                {
                    TenantId = tenantId,
                    ProjectId = projectId,
                    BuilderWorkPackageCoreId = core.Id,
                    ticket.Ordinal,
                    ticket.TicketId,
                    ticket.TicketRevision
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        foreach (var artifact in core.GoverningArtifacts)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.BuilderWorkPackageArtifactReferences
                    (TenantId, ProjectId, BuilderWorkPackageCoreId, Ordinal,
                     ArtifactKind, ArtifactReferenceId, ArtifactRevision)
                VALUES
                    (@TenantId, @ProjectId, @BuilderWorkPackageCoreId, @Ordinal,
                     @ArtifactKind, @ArtifactReferenceId, @ArtifactRevision);
                """,
                new
                {
                    TenantId = tenantId,
                    ProjectId = projectId,
                    BuilderWorkPackageCoreId = core.Id,
                    artifact.Ordinal,
                    artifact.ArtifactKind,
                    artifact.ArtifactReferenceId,
                    ArtifactRevision = artifact.Revision
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.BuilderWorkPackageRepositoryContexts
                (BuilderWorkPackageCoreId, TenantId, ProjectId,
                 RepositoryBindingId, RepositoryBindingRevision, BranchName,
                 BaselineCommit, RepositoryStateObservationId, WorktreeFingerprint,
                 ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                 CodeIndexSnapshotId, CodeIndexSnapshotRevision,
                 BuildCommandSha256, TestCommandSha256,
                 SandboxPolicyVersion, ToolchainManifestId)
            VALUES
                (@BuilderWorkPackageCoreId, @TenantId, @ProjectId,
                 @RepositoryBindingId, @RepositoryBindingRevision, @BranchName,
                 @BaselineCommit, @RepositoryStateObservationId, @WorktreeFingerprint,
                 @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                 @CodeIndexSnapshotId, @CodeIndexSnapshotRevision,
                 @BuildCommandSha256, @TestCommandSha256,
                 @SandboxPolicyVersion, @ToolchainManifestId);
            """,
            new
            {
                BuilderWorkPackageCoreId = core.Id,
                TenantId = tenantId,
                ProjectId = projectId,
                core.RepositoryBindingId,
                core.RepositoryBindingRevision,
                core.BranchName,
                core.BaselineCommit,
                RepositoryStateObservationId = core.RepositoryObservation.Id,
                WorktreeFingerprint = core.RepositoryObservation.WorktreeFingerprint,
                core.EffectiveProfile.ProjectExecutionProfileId,
                core.EffectiveProfile.ProjectExecutionProfileRevision,
                CodeIndexSnapshotId = core.CodeIndex.Id,
                CodeIndexSnapshotRevision = core.CodeIndex.Revision,
                core.BuildCommandSha256,
                core.TestCommandSha256,
                SandboxPolicyVersion = core.Sandbox.PolicyVersion,
                core.Sandbox.ToolchainManifestId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        string operationKind,
        string resourceScope,
        Guid clientOperationId,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
            """
            SELECT Id, PayloadHash, Status, CanonicalResultJson, ResultHash
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
                ResourceScopeId = resourceScope,
                ClientOperationId = clientOperationId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task<long> InsertOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        string operationKind,
        string resourceScope,
        Guid clientOperationId,
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
                TenantId = tenantId,
                ActorUserId = actorUserId,
                OperationKind = operationKind,
                ResourceScopeId = resourceScope,
                ClientOperationId = clientOperationId,
                PayloadHash = payloadHash,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static async Task CompleteOperationAsync<T>(
        IDbConnection connection,
        IDbTransaction transaction,
        long operationRecordId,
        T result,
        Guid? resultBuilderWorkPackageCoreId,
        Guid? resultBuilderExecutionAuthorizationId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var hash = Hash(json);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ClientOperations
            SET Status=N'Completed', CanonicalResultJson=@CanonicalResultJson,
                ResultHash=@ResultHash,
                ResultBuilderWorkPackageCoreId=@ResultBuilderWorkPackageCoreId,
                ResultBuilderExecutionAuthorizationId=@ResultBuilderExecutionAuthorizationId,
                CompletedAtUtc=@CompletedAtUtc
            WHERE Id=@Id AND Status=N'Pending';
            """,
            new
            {
                Id = operationRecordId,
                CanonicalResultJson = json,
                ResultHash = hash,
                ResultBuilderWorkPackageCoreId = resultBuilderWorkPackageCoreId,
                ResultBuilderExecutionAuthorizationId = resultBuilderExecutionAuthorizationId,
                CompletedAtUtc = completedAtUtc
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (changed != 1)
            throw new BuilderAuthorizationIntegrityException(
                "The Builder client operation could not be completed atomically.");
    }

    private static T ReadResult<T>(ClientOperationRow row)
    {
        if (row.Status != "Completed" || string.IsNullOrWhiteSpace(row.CanonicalResultJson) ||
            string.IsNullOrWhiteSpace(row.ResultHash) ||
            !string.Equals(Hash(row.CanonicalResultJson), row.ResultHash, StringComparison.Ordinal))
            throw new BuilderAuthorizationIntegrityException(
                "The stored Builder client-operation result failed integrity verification.");
        return JsonSerializer.Deserialize<T>(row.CanonicalResultJson, JsonOptions)
               ?? throw new BuilderAuthorizationIntegrityException(
                   "The stored Builder client-operation result is unreadable.");
    }

    private static void EnsureOperation(ClientOperationRow row, string payloadHash)
    {
        if (!string.Equals(row.PayloadHash, payloadHash, StringComparison.Ordinal))
            throw new BuilderAuthorizationOperationMismatchException();
        if (row.Status != "Completed")
            throw new BuilderAuthorizationNotAllowedException(
                BuilderAuthorizationReasonCodes.WorkPackageRequired,
                "The exact Builder client operation has not completed.");
    }

    private static Task<bool> ValidateAndRenewLeaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
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
            new
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                LeaseEpoch = leaseEpoch
            },
            transaction,
            cancellationToken: cancellationToken)).ContinueWith(
                task => task.GetAwaiter().GetResult() == 1,
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

    private static Task<bool> CanAccessProjectAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int actorUserId,
        int projectId,
        bool requireContributor,
        CancellationToken cancellationToken) => connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.Projects project
            INNER JOIN dbo.ProjectMembers member
                ON member.TenantId=project.TenantId AND member.ProjectId=project.Id
               AND member.UserId=@ActorUserId AND member.Status=N'Active'
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=project.TenantId AND tenantMember.UserId=@ActorUserId
            INNER JOIN dbo.Users actor ON actor.Id=@ActorUserId AND actor.IsActive=1
            WHERE project.TenantId=@TenantId AND project.Id=@ProjectId
              AND (@RequireContributor=0 OR member.ProjectRole IN (N'Owner', N'Contributor'));
            """,
            new
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                ProjectId = projectId,
                RequireContributor = requireContributor ? 1 : 0
            },
            transaction,
            cancellationToken: cancellationToken)).ContinueWith(
                task => task.GetAwaiter().GetResult() > 0,
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

    private static Task<bool> ActorStillAuthorizedAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        int actorUserId,
        CancellationToken cancellationToken) => CanAccessProjectAsync(
            connection,
            transaction,
            tenantId,
            actorUserId,
            projectId,
            requireContributor: true,
            cancellationToken);

    private static Task<AuthorizationRow?> ReadLatestAuthorizationAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        int actorUserId,
        Guid coreId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        return connection.QueryFirstOrDefaultAsync<AuthorizationRow>(new CommandDefinition(
            $$"""
            SELECT TOP (1) Id, TenantId, ProjectId, ActorUserId,
                   BuilderWorkPackageCoreId, BuilderWorkPackageCoreHash,
                   GrantedAtUtc, ExpiresAtUtc, SingleUse,
                   ConsumedAtUtc, ConsumedByBuilderExecutionRunId, RevokedAtUtc
            FROM dbo.BuilderExecutionAuthorizations{{hint}}
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
              AND ActorUserId=@ActorUserId AND BuilderWorkPackageCoreId=@CoreId
            ORDER BY GrantedAtUtc DESC, Id DESC;
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                ActorUserId = actorUserId,
                CoreId = coreId
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<AuthorizationRow?> ReadAuthorizationByIdAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int projectId,
        Guid authorizationId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        return connection.QuerySingleOrDefaultAsync<AuthorizationRow>(new CommandDefinition(
            $$"""
            SELECT Id, TenantId, ProjectId, ActorUserId,
                   BuilderWorkPackageCoreId, BuilderWorkPackageCoreHash,
                   GrantedAtUtc, ExpiresAtUtc, SingleUse,
                   ConsumedAtUtc, ConsumedByBuilderExecutionRunId, RevokedAtUtc
            FROM dbo.BuilderExecutionAuthorizations{{hint}}
            WHERE Id=@Id AND TenantId=@TenantId AND ProjectId=@ProjectId;
            """,
            new { Id = authorizationId, TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static BuilderExecutionAuthorizationSnapshot ToSnapshot(
        AuthorizationRow row,
        string scopeReason,
        bool actorStillAuthorized,
        DateTimeOffset now)
    {
        var (state, reason) = row.ConsumedAtUtc is not null
            ? (BuilderExecutionAuthorizationStates.Consumed,
                BuilderAuthorizationReasonCodes.AuthorizationConsumed)
            : row.RevokedAtUtc is not null
                ? (BuilderExecutionAuthorizationStates.Revoked,
                    BuilderAuthorizationReasonCodes.AuthorizationRevoked)
                : row.ExpiresAtUtc <= now.UtcDateTime
                    ? (BuilderExecutionAuthorizationStates.Expired,
                        BuilderAuthorizationReasonCodes.AuthorizationExpired)
                    : !actorStillAuthorized
                        ? (BuilderExecutionAuthorizationStates.ScopeStale,
                            BuilderAuthorizationReasonCodes.ActorAuthorizationLost)
                        : !string.Equals(scopeReason, BuilderAuthorizationReasonCodes.Ready, StringComparison.Ordinal)
                            ? (BuilderExecutionAuthorizationStates.ScopeStale, scopeReason)
                            : (BuilderExecutionAuthorizationStates.Valid,
                                BuilderAuthorizationReasonCodes.Ready);
        return new BuilderExecutionAuthorizationSnapshot
        {
            Id = row.Id,
            ProjectId = row.ProjectId,
            ActorUserId = row.ActorUserId,
            BuilderWorkPackageCoreId = row.BuilderWorkPackageCoreId,
            BuilderWorkPackageCoreHash = row.BuilderWorkPackageCoreHash,
            GrantedAtUtc = row.GrantedAtUtc,
            ExpiresAtUtc = row.ExpiresAtUtc,
            SingleUse = row.SingleUse,
            ConsumedAtUtc = row.ConsumedAtUtc,
            ConsumedByBuilderExecutionRunId = row.ConsumedByBuilderExecutionRunId,
            RevokedAtUtc = row.RevokedAtUtc,
            State = state,
            ReasonCode = reason
        };
    }

    private static Task InsertOutboxAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int projectId,
        long workbenchSessionId,
        Guid clientOperationId,
        string eventKind,
        string payloadJson,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.WorkbenchOutboxEvents
                (EventId, TenantId, ProjectId, WorkbenchSessionId, EventKind,
                 PayloadJson, ClientOperationId, OccurredAtUtc)
            VALUES
                (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId, @EventKind,
                 @PayloadJson, @ClientOperationId, @OccurredAtUtc);
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                EventKind = eventKind,
                PayloadJson = payloadJson,
                ClientOperationId = clientOperationId,
                OccurredAtUtc = occurredAtUtc
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task InsertAttributionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int actorUserId,
        int projectId,
        Guid clientOperationId,
        Guid causationId,
        string route,
        string phase,
        int statusCode,
        DateTime timestampUtc,
        CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.UserMutationAttribution
                (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId,
                 TimestampUtc, SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
            VALUES
                (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                 CONVERT(NVARCHAR(128), @ClientOperationId), CONVERT(NVARCHAR(128), @CausationId),
                 @TimestampUtc, @SourceSurface, @SourceClient, N'POST', @Route, @Phase, @StatusCode);
            """,
            new
            {
                ActorUserId = actorUserId,
                TenantId = tenantId,
                ProjectId = projectId,
                ClientOperationId = clientOperationId,
                CausationId = causationId,
                TimestampUtc = timestampUtc,
                SourceSurface,
                SourceClient,
                Route = route,
                Phase = phase,
                StatusCode = statusCode
            },
            transaction,
            cancellationToken: cancellationToken));

    private DateTimeOffset UtcNow() => _time.GetUtcNow().ToUniversalTime();

    private static void ValidateIdentity(int tenantId, int actorUserId, int projectId)
    {
        if (tenantId <= 0 || actorUserId <= 0 || projectId <= 0)
            throw new BuilderAuthorizationValidationException(
                "Tenant, actor, and project IDs must be positive.");
    }

    private static void Validate(CreateBuilderWorkPackageCommand command)
    {
        ValidateIdentity(command.TenantId, command.ActorUserId, command.ProjectId);
        if (command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty || command.TicketIds is null ||
            command.TicketIds.Count is 0 or > BuilderWorkPackageCoreContract.MaximumTickets ||
            command.TicketIds.Any(static value => value <= 0) ||
            command.TicketIds.Distinct().Count() != command.TicketIds.Count)
            throw new BuilderAuthorizationValidationException(
                "A current Workbench fence, client operation, and ordered distinct permanent ticket selection are required.");
    }

    private static void Validate(GrantBuilderExecutionAuthorizationCommand command)
    {
        ValidateIdentity(command.TenantId, command.ActorUserId, command.ProjectId);
        if (command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty || command.BuilderWorkPackageCoreId == Guid.Empty)
            throw new BuilderAuthorizationValidationException(
                "A current Workbench fence, client operation, and work-package core are required.");
        _ = NormalizeSha256(command.ExpectedCoreHash);
    }

    private static void Validate(RevokeBuilderExecutionAuthorizationCommand command)
    {
        ValidateIdentity(command.TenantId, command.ActorUserId, command.ProjectId);
        if (command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty || command.BuilderExecutionAuthorizationId == Guid.Empty)
            throw new BuilderAuthorizationValidationException(
                "A current Workbench fence, client operation, and authorization are required.");
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new BuilderAuthorizationValidationException(
                "ExpectedCoreHash must be a lowercase SHA-256 value.");
        return normalized;
    }

    private static IReadOnlyList<string> ParsePermittedFiles(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        return value
            .Split(['\r', '\n', ';', '|', ','],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<BuilderCodeIndexSourceSnapshot> ParseCodeIndexSources(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BuilderCodeIndexSourceSnapshot[]>(json, JsonOptions)
                   ?? throw new BuilderAuthorizationIntegrityException(
                       "The current code-index source snapshot is unreadable.");
        }
        catch (JsonException)
        {
            throw new BuilderAuthorizationIntegrityException(
                "The current code-index source snapshot is unreadable.");
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ClientOperationRow(
        long Id,
        string PayloadHash,
        string Status,
        string? CanonicalResultJson,
        string? ResultHash);

    private sealed record CoreRow(
        Guid Id,
        string CanonicalJson,
        string CoreHash,
        Guid ClientOperationId);

    private sealed record ProjectContextRow(
        string ProjectLifecyclePhase,
        string? CanonicalPath,
        string? BaselineCommit);

    private sealed record ReadyAuthorityRow
    {
        public required string ExecutionReadiness { get; init; }
        public required string ReasonCode { get; init; }
        public required string ProjectLifecyclePhase { get; init; }
        public required Guid RepositoryBindingId { get; init; }
        public required long RepositoryBindingRevision { get; init; }
        public required string CanonicalPath { get; init; }
        public required string BaselineCommit { get; init; }
        public required long ReadinessAssessmentId { get; init; }
        public required long ReadinessAssessmentRevision { get; init; }
        public required Guid TechnicalReadinessEvidenceId { get; init; }
        public required string TechnicalReadinessEvidenceSha256 { get; init; }
        public required DateTime ReadinessAssessedAtUtc { get; init; }
        public required Guid RepositoryStateObservationId { get; init; }
        public required string RepositoryObservationEvidenceSha256 { get; init; }
        public required string RepositoryHeadCommit { get; init; }
        public required string RepositoryGitTreeId { get; init; }
        public required string RepositoryWorktreeState { get; init; }
        public required DateTime RepositoryObservedAtUtc { get; init; }
        public required string WorktreeFingerprint { get; init; }
        public required Guid ProjectExecutionProfileId { get; init; }
        public required long ProjectExecutionProfileRevision { get; init; }
        public required string ProfileDefinitionId { get; init; }
        public required int ProfileDescriptorRevision { get; init; }
        public required string ProfileDescriptorSha256 { get; init; }
        public required Guid CodeIndexSnapshotId { get; init; }
        public required long CodeIndexSnapshotRevision { get; init; }
        public required string CodeIndexEvidenceSha256 { get; init; }
        public required int CodeIndexSchemaVersion { get; init; }
        public required string IndexerVersion { get; init; }
        public required string IndexContentSha256 { get; init; }
        public required string CodeIndexSourcesJson { get; init; }
        public required DateTime CodeIndexIndexedAtUtc { get; init; }
        public required string RestoreCommandSha256 { get; init; }
        public required string BuildCommandSha256 { get; init; }
        public required string TestCommandSha256 { get; init; }
        public required string SandboxPolicyVersion { get; init; }
        public required string SandboxPolicySha256 { get; init; }
        public required string ToolchainManifestId { get; init; }
        public required string ToolchainManifestSha256 { get; init; }
        public required string ContainerImageDigestSha256 { get; init; }
        public required string OfflineFeedManifestSha256 { get; init; }
        public required string TemplateBundleSha256 { get; init; }
        public required Guid SandboxQualificationAttemptId { get; init; }
        public required Guid SandboxEvidenceManifestId { get; init; }
        public required string SandboxEvidenceManifestSha256 { get; init; }
        public required Guid BuilderConfigurationId { get; init; }
        public required long BuilderConfigurationRevision { get; init; }
        public required string ProviderId { get; init; }
        public required string ModelId { get; init; }
        public required string BuilderConfigurationSha256 { get; init; }
    }

    private sealed record TicketRow
    {
        public required long Id { get; init; }
        public required long Revision { get; init; }
        public required long WorkItemId { get; init; }
        public required long WorkItemVersion { get; init; }
        public required long WorkItemContractId { get; init; }
        public required int WorkItemContractRevision { get; init; }
        public required string WorkItemContractSha256 { get; init; }
        public required string AcceptanceCriteria { get; init; }
        public required string PermittedFiles { get; init; }
    }
    private sealed record UnderstandingRow(long Id, long Revision);

    private sealed record AuthorizationRow
    {
        public required Guid Id { get; init; }
        public required int TenantId { get; init; }
        public required int ProjectId { get; init; }
        public required int ActorUserId { get; init; }
        public required Guid BuilderWorkPackageCoreId { get; init; }
        public required string BuilderWorkPackageCoreHash { get; init; }
        public required DateTime GrantedAtUtc { get; init; }
        public required DateTime ExpiresAtUtc { get; init; }
        public required bool SingleUse { get; init; }
        public DateTime? ConsumedAtUtc { get; init; }
        public Guid? ConsumedByBuilderExecutionRunId { get; init; }
        public DateTime? RevokedAtUtc { get; init; }
    }
}
