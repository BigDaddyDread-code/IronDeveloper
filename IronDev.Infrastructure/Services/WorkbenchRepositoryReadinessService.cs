using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public static class RepositoryReadinessOperationKinds
{
    public const string Validate = "ValidateRepositoryTechnicalReadiness";
}

public sealed class RepositoryReadinessForbiddenException()
    : Exception("Only a project owner or contributor can validate technical readiness.")
{
    public const string ErrorCode = "repository_readiness_forbidden";
}

public sealed class RepositoryReadinessNotAllowedException(string message) : Exception(message)
{
    public const string ErrorCode = "repository_readiness_not_allowed";
}

public sealed class RepositoryReadinessInProgressException()
    : Exception("A technical-readiness validation is already in progress for this project.")
{
    public const string ErrorCode = "repository_readiness_in_progress";
}

public sealed class RepositoryReadinessIntegrityException(string message) : Exception(message)
{
    public const string ErrorCode = "repository_readiness_integrity_failed";
}

public sealed class RepositoryReadinessExecutionException(string reasonCode, string message)
    : Exception(message)
{
    public const string ErrorCode = "repository_readiness_validation_failed";
    public string ReasonCode { get; } = reasonCode;
}

public sealed class WorkbenchRepositoryReadinessService : IWorkbenchRepositoryReadinessService
{
    private const string Route = "/api/workbench/projects/{projectId}/repository/readiness-validations";
    private const string BuilderPolicyVersion = "workbench-builder-stable-config-v1";
    private const string IndexerVersion = "workbench-git-tree-index-v1";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connections;
    private readonly IRepositoryReadinessObserver _observer;
    private readonly IBuilderStableConfigurationProvider _builderConfiguration;
    private readonly IExecutionAvailabilityChecker _availability;
    private readonly IRepositoryReadinessRefreshFailureInjector _failureInjector;

    public WorkbenchRepositoryReadinessService(
        IDbConnectionFactory connections,
        IRepositoryReadinessObserver observer,
        IBuilderStableConfigurationProvider builderConfiguration,
        IExecutionAvailabilityChecker availability,
        IRepositoryReadinessRefreshFailureInjector? failureInjector = null)
    {
        _connections = connections;
        _observer = observer;
        _builderConfiguration = builderConfiguration;
        _availability = availability;
        _failureInjector = failureInjector ?? new NoOpRepositoryReadinessRefreshFailureInjector();
    }

    public async Task<WorkbenchRepositoryReadinessContext> GetContextAsync(
        GetWorkbenchRepositoryReadinessContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(query.TenantId, query.ActorUserId, query.ProjectId);
        ProjectRow project;
        AuthorityPackage? package;
        StoredContextRow? stored;
        SandboxAttemptIdentity? latestSandbox;
        using (var connection = _connections.CreateConnection())
        {
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
            project = await ReadProjectAsync(
                    connection,
                    null,
                    query.TenantId,
                    query.ProjectId,
                    cancellationToken).ConfigureAwait(false)
                ?? throw new WorkbenchProjectNotAccessibleException();
            package = await ReadAuthorityPackageAsync(
                connection,
                null,
                query.TenantId,
                query.ProjectId,
                lockRows: false,
                cancellationToken).ConfigureAwait(false);
            stored = await ReadLatestStoredContextAsync(
                connection,
                query.TenantId,
                query.ProjectId,
                cancellationToken).ConfigureAwait(false);
            latestSandbox = await ReadLatestSandboxIdentityAsync(
                connection,
                query.TenantId,
                query.ProjectId,
                cancellationToken).ConfigureAwait(false);
        }

        var builder = await _builderConfiguration.GetCurrentAsync(
            query.TenantId,
            query.ProjectId,
            cancellationToken).ConfigureAwait(false);
        var availability = builder is null
            ? null
            : await SafeAvailabilityAsync(
                new ExecutionAvailabilityRequest(
                    query.TenantId,
                    query.ActorUserId,
                    query.ProjectId,
                    builder.ConfigurationId,
                    builder.Revision),
                cancellationToken).ConfigureAwait(false);

        if (package is null)
        {
            return new WorkbenchRepositoryReadinessContext(
                query.ProjectId,
                project.ProjectLifecyclePhase,
                RepositoryReadinessEvaluator.Evaluate(new RepositoryReadinessEvaluationContext
                {
                    ProjectId = query.ProjectId,
                    RepositoryBindingState = RepositoryBindingStates.LegacyUnverified,
                    CurrentAuthority = null,
                    RepositoryObservation = null,
                    BuildValidation = null,
                    TestValidation = null,
                    CodeIndex = null,
                    SandboxQualification = null,
                    CurrentBuilderConfiguration = builder,
                    BuilderConfigurationEvidence = null,
                    Availability = availability
                }));
        }

        RepositoryObservationResult? live = null;
        try
        {
            live = await _observer.ObserveAsync(
                new ObserveRepositoryStateRequest(
                    query.ProjectId,
                    package.Binding.Id,
                    package.Binding.Revision,
                    package.Binding.CanonicalPath,
                    package.Binding.BaselineCommit!,
                    package.ProvisioningSnapshot?.ManifestJson,
                    package.ProvisioningSnapshot?.ManifestSha256),
                cancellationToken).ConfigureAwait(false);
        }
        catch (RepositoryReadinessObservationException)
        {
            // A read context remains available and honestly reports validation required.
        }

        if (stored is not null && live is not null &&
            StoredContextIsCurrent(stored, package, live.Observation))
        {
            var result = ReadStoredResult(stored);
            var cachedEvaluation = result.Evaluation;
            if (!BuilderConfigurationIsCurrent(stored, builder))
            {
                cachedEvaluation = InvalidateBuilderGate(
                    cachedEvaluation,
                    RepositoryReadinessAuthorityCodec.ComputeHash(package.Authority));
            }
            if (!StoredSandboxIsCurrent(stored, package.Sandbox, latestSandbox))
            {
                cachedEvaluation = InvalidateSandboxGate(
                    cachedEvaluation,
                    RepositoryReadinessAuthorityCodec.ComputeHash(package.Authority));
            }

            return new WorkbenchRepositoryReadinessContext(
                query.ProjectId,
                project.ProjectLifecyclePhase,
                cachedEvaluation with { Availability = availability });
        }

        var transientSandbox = live is null || package.Sandbox is null ||
                               !string.Equals(
                                   package.Sandbox.Manifest.WorktreeFingerprint,
                                   live.Observation.WorktreeFingerprint,
                                   StringComparison.Ordinal)
            ? null
            : ToSandboxEvidence(package, ToEvidenceBinding(package.Authority, live.Observation));
        var evaluation = RepositoryReadinessEvaluator.Evaluate(new RepositoryReadinessEvaluationContext
        {
            ProjectId = query.ProjectId,
            RepositoryBindingState = package.Binding.BindingState,
            CurrentAuthority = package.Authority,
            RepositoryObservation = live?.Observation,
            BuildValidation = null,
            TestValidation = null,
            CodeIndex = null,
            SandboxQualification = transientSandbox,
            CurrentBuilderConfiguration = builder,
            BuilderConfigurationEvidence = null,
            Availability = availability
        });
        return new WorkbenchRepositoryReadinessContext(
            query.ProjectId,
            project.ProjectLifecyclePhase,
            evaluation);
    }

    public async Task<RefreshRepositoryReadinessResult> RefreshAsync(
        RefreshRepositoryReadinessCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        await EnsurePreLockAccessAsync(command, cancellationToken).ConfigureAwait(false);
        var key = $"{command.TenantId}:{command.ProjectId}";
        var localGate = LocalLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        if (!await localGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new RepositoryReadinessInProgressException();

        ValidationClaim? claim = null;
        try
        {
            using var distributed = await AcquireDistributedLockAsync(command, cancellationToken)
                .ConfigureAwait(false);
            claim = await ClaimAsync(command, cancellationToken).ConfigureAwait(false);
            if (claim.TerminalizedFailure is not null)
                throw claim.TerminalizedFailure;
            if (claim.Replay is not null)
                return claim.Replay with { IsReplay = true };

            var observed = await _observer.ObserveAsync(
                new ObserveRepositoryStateRequest(
                    command.ProjectId,
                    claim.Package.Binding.Id,
                    claim.Package.Binding.Revision,
                    claim.Package.Binding.CanonicalPath,
                    claim.Package.Binding.BaselineCommit!,
                    claim.Package.ProvisioningSnapshot?.ManifestJson,
                    claim.Package.ProvisioningSnapshot?.ManifestSha256),
                cancellationToken).ConfigureAwait(false);
            var builder = await _builderConfiguration.GetCurrentAsync(
                command.TenantId,
                command.ProjectId,
                cancellationToken).ConfigureAwait(false);
            var availability = builder is null
                ? null
                : await SafeAvailabilityAsync(
                    new ExecutionAvailabilityRequest(
                        command.TenantId,
                        command.ActorUserId,
                        command.ProjectId,
                        builder.ConfigurationId,
                        builder.Revision),
                    cancellationToken).ConfigureAwait(false);
            var materialized = Materialize(claim, observed, builder, availability);
            var finalObserved = await _observer.ObserveAsync(
                new ObserveRepositoryStateRequest(
                    command.ProjectId,
                    claim.Package.Binding.Id,
                    claim.Package.Binding.Revision,
                    claim.Package.Binding.CanonicalPath,
                    claim.Package.Binding.BaselineCommit!,
                    claim.Package.ProvisioningSnapshot?.ManifestJson,
                    claim.Package.ProvisioningSnapshot?.ManifestSha256),
                cancellationToken).ConfigureAwait(false);
            EnsureObservationStillCurrent(materialized, finalObserved);
            return await CompleteAsync(command, claim, materialized, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (claim?.RequiresFailureFinalization == true)
            {
                var recovered = await FinalizeFailureBestEffortAsync(
                    command,
                    claim,
                    "RepositoryReadinessCancelled",
                    "Technical-readiness validation was cancelled.",
                    cancelled: true).ConfigureAwait(false);
                if (recovered is not null)
                    return recovered with { IsReplay = true };
            }
            throw;
        }
        catch (Exception exception)
        {
            var (reason, summary) = SafeFailure(exception);
            if (claim?.RequiresFailureFinalization == true)
            {
                var recovered = await FinalizeFailureBestEffortAsync(
                    command,
                    claim,
                    reason,
                    summary,
                    cancelled: false).ConfigureAwait(false);
                if (recovered is not null)
                    return recovered with { IsReplay = true };
            }
            if (exception is WorkbenchProjectNotAccessibleException or
                RepositoryReadinessForbiddenException or
                WorkbenchLeaseFenceException or
                RepositoryReadinessStaleConfigurationException or
                RepositoryReadinessOperationMismatchException or
                RepositoryReadinessInProgressException or
                RepositoryReadinessExecutionException or
                RepositoryReadinessNotAllowedException or
                RepositoryReadinessIntegrityException or
                RepositoryReadinessObservationException or
                RepositoryReadinessValidationException)
                throw;
            throw new RepositoryReadinessExecutionException(reason, summary);
        }
        finally
        {
            localGate.Release();
        }
    }

    private async Task<ValidationClaim> ClaimAsync(
        RefreshRepositoryReadinessCommand command,
        CancellationToken cancellationToken)
    {
        var payloadHash = PayloadHash(command);
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
                    requireContributor: false,
                    cancellationToken).ConfigureAwait(false))
                throw new WorkbenchProjectNotAccessibleException();
            if (!await CanAccessProjectAsync(
                    connection,
                    transaction,
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    requireContributor: true,
                    cancellationToken).ConfigureAwait(false))
                throw new RepositoryReadinessForbiddenException();
            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command, cancellationToken)
                    .ConfigureAwait(false))
                throw new WorkbenchLeaseFenceException();

            var existing = await ReadOperationAsync(
                connection,
                transaction,
                command,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new RepositoryReadinessOperationMismatchException();
                if (string.Equals(existing.Status, "Completed", StringComparison.Ordinal))
                {
                    var replay = ReadStoredResult(existing);
                    transaction.Commit();
                    return ValidationClaim.ForReplay(replay);
                }
                var priorAttempt = await ReadAttemptByOperationAsync(
                    connection,
                    transaction,
                    existing.Id,
                    cancellationToken).ConfigureAwait(false)
                    ?? throw new RepositoryReadinessIntegrityException(
                        "The durable readiness operation has no matching attempt.");
                if (string.Equals(existing.Status, "Failed", StringComparison.Ordinal))
                    throw new RepositoryReadinessExecutionException(
                        priorAttempt.FailureCode ?? "RepositoryReadinessValidationFailed",
                        priorAttempt.FailureSummary ?? "Technical-readiness validation failed safely.");
                if (!string.Equals(existing.Status, "Pending", StringComparison.Ordinal) ||
                    !string.Equals(priorAttempt.State, "Running", StringComparison.Ordinal))
                    throw new RepositoryReadinessIntegrityException(
                        "The durable readiness operation is in an unsupported state.");

                AuthorityPackage recoveryPackage;
                try
                {
                    recoveryPackage = await RequireAuthorityPackageAsync(
                        connection,
                        transaction,
                        command,
                        lockRows: true,
                        cancellationToken).ConfigureAwait(false);
                    EnsureAttemptAuthority(priorAttempt, recoveryPackage, command);
                }
                catch (Exception exception) when (exception is
                           RepositoryReadinessStaleConfigurationException or
                           RepositoryReadinessNotAllowedException or
                           RepositoryReadinessIntegrityException or
                           RepositoryReadinessValidationException)
                {
                    var (reasonCode, summary) = SafeFailure(exception);
                    if (!await TransitionClaimFailureAsync(
                            connection,
                            transaction,
                            command,
                            existing.Id,
                            priorAttempt.Id,
                            "Failed",
                            reasonCode,
                            summary,
                            409,
                            cancellationToken).ConfigureAwait(false))
                        throw new RepositoryReadinessIntegrityException(
                            "The stale readiness claim could not be terminalized exactly.");
                    transaction.Commit();
                    return ValidationClaim.ForTerminalized(
                        existing.Id,
                        priorAttempt,
                        exception);
                }
                transaction.Commit();
                return new ValidationClaim(
                    existing.Id,
                    priorAttempt.Id,
                    priorAttempt.AttemptNumber,
                    priorAttempt.StartedAtUtc,
                    recoveryPackage,
                    Replay: null,
                    TerminalizedFailure: null);
            }

            var running = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.TechnicalValidationAttempts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND State=N'Running';
                """,
                command,
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (running > 0)
                throw new RepositoryReadinessInProgressException();

            var package = await RequireAuthorityPackageAsync(
                connection,
                transaction,
                command,
                lockRows: true,
                cancellationToken).ConfigureAwait(false);
            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();",
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var operationId = await connection.QuerySingleAsync<long>(new CommandDefinition(
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
                    OperationKind = RepositoryReadinessOperationKinds.Validate,
                    ResourceScopeId = ResourceScope(command.ProjectId),
                    command.ClientOperationId,
                    PayloadHash = payloadHash,
                    command.ProjectId,
                    command.WorkbenchSessionId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            _failureInjector.ThrowIfRequested(RepositoryReadinessRefreshFailurePoint.ClientOperationCreated);

            var attemptNumber = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT COALESCE(MAX(AttemptNumber), 0) + 1
                FROM dbo.TechnicalValidationAttempts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId
                  AND RepositoryBindingId=@RepositoryBindingId;
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    RepositoryBindingId = package.Binding.Id
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var attemptId = Guid.NewGuid();
            var data = AttemptAuthority(package);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.TechnicalValidationAttempts
                    (Id, TenantId, ProjectId, ClientOperationRecordId, ClientOperationId,
                     ActorUserId, ClientOperationKind, ClientOperationResourceScopeId,
                     WorkbenchSessionId, LeaseEpoch, AttemptNumber,
                     RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                     ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                     ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                     RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                     ToolchainManifestId, ToolchainManifestSha256,
                     ContainerImageDigest, ContainerImageDigestSha256,
                     SandboxPolicyVersion, SandboxPolicySha256,
                     OfflineFeedManifestSha256, TemplateBundleSha256,
                     SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                     SandboxEvidenceManifestSha256, State, StartedAtUtc)
                VALUES
                    (@Id, @TenantId, @ProjectId, @ClientOperationRecordId, @ClientOperationId,
                     @ActorUserId, @ClientOperationKind, @ClientOperationResourceScopeId,
                     @WorkbenchSessionId, @LeaseEpoch, @AttemptNumber,
                     @RepositoryBindingId, @RepositoryBindingRevision, @BaselineCommit,
                     @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                     @ProfileDefinitionId, @ProfileDescriptorRevision, @ProfileDescriptorSha256,
                     @RestoreCommandSha256, @BuildCommandSha256, @TestCommandSha256,
                     @ToolchainManifestId, @ToolchainManifestSha256,
                     @ContainerImageDigest, @ContainerImageDigestSha256,
                     @SandboxPolicyVersion, @SandboxPolicySha256,
                     @OfflineFeedManifestSha256, @TemplateBundleSha256,
                     @SandboxQualificationAttemptId, @SandboxEvidenceManifestId,
                     @SandboxEvidenceManifestSha256, N'Running', @StartedAtUtc);
                """,
                new
                {
                    Id = attemptId,
                    command.TenantId,
                    command.ProjectId,
                    ClientOperationRecordId = operationId,
                    command.ClientOperationId,
                    command.ActorUserId,
                    ClientOperationKind = RepositoryReadinessOperationKinds.Validate,
                    ClientOperationResourceScopeId = ResourceScope(command.ProjectId),
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    AttemptNumber = attemptNumber,
                    data.RepositoryBindingId,
                    data.RepositoryBindingRevision,
                    data.BaselineCommit,
                    data.ProjectExecutionProfileId,
                    data.ProjectExecutionProfileRevision,
                    data.ProfileDefinitionId,
                    data.ProfileDescriptorRevision,
                    data.ProfileDescriptorSha256,
                    data.RestoreCommandSha256,
                    data.BuildCommandSha256,
                    data.TestCommandSha256,
                    data.ToolchainManifestId,
                    data.ToolchainManifestSha256,
                    data.ContainerImageDigest,
                    data.ContainerImageDigestSha256,
                    data.SandboxPolicyVersion,
                    data.SandboxPolicySha256,
                    data.OfflineFeedManifestSha256,
                    data.TemplateBundleSha256,
                    data.SandboxQualificationAttemptId,
                    data.SandboxEvidenceManifestId,
                    data.SandboxEvidenceManifestSha256,
                    StartedAtUtc = now
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            await InsertOutboxAsync(
                connection,
                transaction,
                command,
                "RepositoryTechnicalValidationStarted",
                RepositoryReadinessCanonicalJson.Serialize(new
                {
                    schemaVersion = 1,
                    command.ProjectId,
                    attemptId,
                    attemptNumber,
                    repositoryBindingId = package.Binding.Id,
                    repositoryBindingRevision = package.Binding.Revision,
                    executionProfileId = package.Profile.Id,
                    executionProfileRevision = package.Profile.Revision
                }),
                now,
                cancellationToken).ConfigureAwait(false);
            await InsertAttributionAsync(
                connection,
                transaction,
                command,
                attemptId,
                "Attempted",
                202,
                now,
                cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            return new ValidationClaim(
                operationId,
                attemptId,
                attemptNumber,
                now,
                package,
                Replay: null,
                TerminalizedFailure: null);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static MaterializedValidation Materialize(
        ValidationClaim claim,
        RepositoryObservationResult observed,
        BuilderStableConfigurationBinding? builder,
        ExecutionAvailabilityCheck? availability)
    {
        var observation = RepositoryStateObservationCodec.NormalizeAndValidate(observed.Observation);
        if (observation.RepositoryBindingId != claim.Package.Binding.Id ||
            observation.RepositoryBindingRevision != claim.Package.Binding.Revision ||
            !string.Equals(observation.BaselineCommit, claim.Package.Binding.BaselineCommit, StringComparison.Ordinal))
            throw new RepositoryReadinessIntegrityException(
                "The repository observer returned evidence for a different authority.");

        var sandbox = claim.Package.Sandbox
            ?? throw new RepositoryReadinessIntegrityException(
                "The validation claim has no passed sandbox evidence.");
        var manifest = sandbox.Manifest;
        if (!string.Equals(
                manifest.WorktreeFingerprint,
                observation.WorktreeFingerprint,
                StringComparison.Ordinal))
            throw new RepositoryReadinessIntegrityException(
                "The passed sandbox source snapshot does not match the observed repository worktree.");
        var restoreStage = RequireStage(manifest, SandboxExecutionStage.Restore);
        var buildStage = RequireStage(manifest, SandboxExecutionStage.Build);
        var testStage = RequireStage(manifest, SandboxExecutionStage.Test);
        var preIndexBinding = ToEvidenceBinding(claim.Package.Authority, observation);
        var build = new BuildValidationRecord
        {
            Id = Guid.NewGuid(),
            Revision = claim.AttemptNumber,
            Binding = preIndexBinding,
            RestoreResult = ToCommandResult(restoreStage),
            BuildResult = ToCommandResult(buildStage),
            ValidatedAtUtc = manifest.CompletedAtUtc,
            EvidenceManifestSha256 = claim.Package.Sandbox.ManifestSha256
        };
        build = RepositoryValidationRecordCodec.NormalizeAndValidate(build);
        var test = new TestValidationRecord
        {
            Id = Guid.NewGuid(),
            Revision = claim.AttemptNumber,
            Binding = preIndexBinding,
            TestResult = ToCommandResult(testStage),
            ValidatedAtUtc = manifest.CompletedAtUtc,
            EvidenceManifestSha256 = claim.Package.Sandbox.ManifestSha256
        };
        test = RepositoryValidationRecordCodec.NormalizeAndValidate(test);

        var indexId = Guid.NewGuid();
        var indexBinding = preIndexBinding with
        {
            CodeIndexSnapshotId = indexId,
            CodeIndexSnapshotRevision = claim.AttemptNumber
        };
        var indexManifestHash = RepositoryReadinessCanonicalJson.Sha256(
            RepositoryReadinessCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                indexerVersion = IndexerVersion,
                observation.EvidenceHash,
                sources = observed.Sources.Select(static value => new
                {
                    value.Ordinal,
                    value.RelativePath,
                    value.ContentSha256
                }).ToArray()
            }));
        var index = new CodeIndexSnapshot
        {
            Id = indexId,
            Revision = claim.AttemptNumber,
            Binding = indexBinding,
            State = RepositoryCodeIndexStates.Current,
            IndexFormatVersion = IndexerVersion,
            Sources = observed.Sources,
            IndexedContentSha256 = CodeIndexSnapshotCodec.ComputeIndexedContentHash(observed.Sources),
            IndexedAtUtc = observation.ObservedAtUtc,
            EvidenceManifestSha256 = indexManifestHash
        };
        index = CodeIndexSnapshotCodec.NormalizeAndValidate(index);

        var effectiveBuilder = builder ?? UnconfiguredBuilder(claim.AttemptId);
        var builderManifestHash = RepositoryReadinessCanonicalJson.Sha256(
            RepositoryReadinessCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                policyVersion = BuilderPolicyVersion,
                effectiveBuilder.ConfigurationId,
                effectiveBuilder.Revision,
                effectiveBuilder.ProviderId,
                effectiveBuilder.ModelId,
                effectiveBuilder.ConfigurationSha256,
                configured = builder is not null
            }));
        var builderEvidence = BuilderStableConfigurationEvidenceCodec.NormalizeAndValidate(
            new BuilderStableConfigurationEvidence
            {
                Binding = effectiveBuilder,
                IsConfigured = builder is not null,
                ValidatedAtUtc = observation.ObservedAtUtc,
                EvidenceManifestSha256 = builderManifestHash
            });
        var sandboxEvidence = ToSandboxEvidence(claim.Package, preIndexBinding);
        var evaluation = RepositoryReadinessEvaluator.Evaluate(new RepositoryReadinessEvaluationContext
        {
            ProjectId = claim.Package.Binding.ProjectId,
            RepositoryBindingState = claim.Package.Binding.BindingState,
            CurrentAuthority = claim.Package.Authority,
            RepositoryObservation = observation,
            BuildValidation = build,
            TestValidation = test,
            CodeIndex = index,
            SandboxQualification = sandboxEvidence,
            CurrentBuilderConfiguration = builder,
            BuilderConfigurationEvidence = builderEvidence,
            Availability = availability
        });
        return new MaterializedValidation(
            observation,
            build,
            test,
            index,
            builderEvidence,
            sandboxEvidence,
            evaluation,
            restoreStage,
            buildStage,
            testStage);
    }

    private async Task<RefreshRepositoryReadinessResult> CompleteAsync(
        RefreshRepositoryReadinessCommand command,
        ValidationClaim claim,
        MaterializedValidation materialized,
        CancellationToken cancellationToken)
    {
        var latestBuilder = await _builderConfiguration.GetCurrentAsync(
            command.TenantId,
            command.ProjectId,
            cancellationToken).ConfigureAwait(false);
        if (!SameBuilder(latestBuilder, materialized.Evaluation.Gates
                .Single(value => value.Gate == RepositoryReadinessGateName.BuilderModelConfigured).Passed
                ? materialized.BuilderConfiguration.Binding
                : null))
            throw new RepositoryReadinessStaleConfigurationException();

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
                throw new RepositoryReadinessForbiddenException();
            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command, cancellationToken)
                    .ConfigureAwait(false))
                throw new WorkbenchLeaseFenceException();
            var operation = await ReadOperationAsync(
                connection,
                transaction,
                command,
                cancellationToken).ConfigureAwait(false)
                ?? throw new RepositoryReadinessIntegrityException("The readiness operation disappeared.");
            if (string.Equals(operation.Status, "Completed", StringComparison.Ordinal))
            {
                var replay = ReadStoredResult(operation);
                transaction.Commit();
                return replay with { IsReplay = true };
            }
            if (operation.Id != claim.OperationRecordId ||
                !string.Equals(operation.Status, "Pending", StringComparison.Ordinal) ||
                !string.Equals(operation.PayloadHash, PayloadHash(command), StringComparison.Ordinal))
                throw new RepositoryReadinessIntegrityException(
                    "The readiness operation cannot be finalized.");

            var attempt = await ReadAttemptByOperationAsync(
                connection,
                transaction,
                operation.Id,
                cancellationToken).ConfigureAwait(false)
                ?? throw new RepositoryReadinessIntegrityException("The readiness attempt disappeared.");
            if (attempt.Id != claim.AttemptId || !string.Equals(attempt.State, "Running", StringComparison.Ordinal))
                throw new RepositoryReadinessIntegrityException(
                    "The readiness attempt is not the current running attempt.");
            var finalPackage = await RequireAuthorityPackageAsync(
                connection,
                transaction,
                command,
                lockRows: true,
                cancellationToken).ConfigureAwait(false);
            EnsureAttemptAuthority(attempt, finalPackage, command);
            if (!string.Equals(
                    RepositoryReadinessAuthorityCodec.ComputeHash(finalPackage.Authority),
                    RepositoryReadinessAuthorityCodec.ComputeHash(claim.Package.Authority),
                    StringComparison.Ordinal))
                throw new RepositoryReadinessStaleConfigurationException();

            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();",
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var data = AttemptAuthority(finalPackage);
            await InsertObservationAsync(
                connection,
                transaction,
                command,
                claim,
                data,
                materialized,
                cancellationToken).ConfigureAwait(false);
            _failureInjector.ThrowIfRequested(RepositoryReadinessRefreshFailurePoint.RepositoryObservationCreated);
            await InsertBuildAsync(connection, transaction, command, claim, data, materialized, now, cancellationToken)
                .ConfigureAwait(false);
            _failureInjector.ThrowIfRequested(RepositoryReadinessRefreshFailurePoint.BuildValidationCreated);
            await InsertTestAsync(connection, transaction, command, claim, data, materialized, cancellationToken)
                .ConfigureAwait(false);
            _failureInjector.ThrowIfRequested(RepositoryReadinessRefreshFailurePoint.TestValidationCreated);
            await InsertIndexAsync(connection, transaction, command, claim, data, materialized, cancellationToken)
                .ConfigureAwait(false);
            await ProjectLegacyIndexAsync(
                connection,
                transaction,
                command,
                materialized.Index,
                cancellationToken).ConfigureAwait(false);
            _failureInjector.ThrowIfRequested(RepositoryReadinessRefreshFailurePoint.CodeIndexSnapshotCreated);
            var builderRecordId = Guid.NewGuid();
            var builderEvidenceHash = await InsertBuilderConfigurationAsync(
                connection,
                transaction,
                command,
                claim,
                data,
                materialized,
                builderRecordId,
                cancellationToken).ConfigureAwait(false);

            var assessmentRevision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                SELECT COALESCE(MAX(Revision), 0) + 1
                FROM dbo.ProjectReadinessAssessments WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId;
                """,
                command,
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var assessmentId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT dbo.ProjectReadinessAssessments
                    (TenantId, ProjectId, Revision, ExecutionReadiness, ReasonCode,
                     Summary, AssessedByActorUserId, AssessedAtUtc)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ProjectId, @Revision, @ExecutionReadiness, @ReasonCode,
                     @Summary, @ActorUserId, @AssessedAtUtc);
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    Revision = assessmentRevision,
                    materialized.Evaluation.ExecutionReadiness,
                    materialized.Evaluation.ReasonCode,
                    Summary = materialized.Evaluation.IsReady
                        ? "Technical readiness is current for the exact repository, profile, sandbox, index, and Builder configuration."
                        : "Technical validation completed, but one or more exact readiness gates remain unsatisfied.",
                    command.ActorUserId,
                    AssessedAtUtc = now
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            _failureInjector.ThrowIfRequested(RepositoryReadinessRefreshFailurePoint.ReadinessAssessmentCreated);

            var observationHash = materialized.Observation.EvidenceHash;
            var buildEvidenceHash = RepositoryValidationRecordCodec.ComputeHash(materialized.Build);
            var testEvidenceHash = RepositoryValidationRecordCodec.ComputeHash(materialized.Test);
            var indexEvidenceHash = CodeIndexSnapshotCodec.ComputeHash(materialized.Index);
            var gateJson = GateResultsJson(materialized.Evaluation.Gates);
            var gateHash = RepositoryReadinessCanonicalJson.Sha256(gateJson);
            var technicalEvidenceId = Guid.NewGuid();
            var technicalEvidenceHash = RepositoryReadinessCanonicalJson.Sha256(
                RepositoryReadinessCanonicalJson.Serialize(new
                {
                    schemaVersion = 1,
                    technicalEvidenceId,
                    claim.AttemptId,
                    assessmentId,
                    assessmentRevision,
                    observationHash,
                    buildEvidenceHash,
                    testEvidenceHash,
                    indexEvidenceHash,
                    builderEvidenceHash,
                    gateHash,
                    materialized.Evaluation.ExecutionReadiness,
                    materialized.Evaluation.ReasonCode,
                    assessedAtUtc = now
                }));
            // The attempt records whether the bounded technical validation completed and
            // materialized trustworthy evidence. Aggregate readiness is carried separately by
            // the assessment/evidence row, so a single unsatisfied gate is not an execution
            // failure and must not hide the other current passing gates on subsequent reads.
            const string attemptState = "Passed";
            var attemptEvidenceHash = RepositoryReadinessCanonicalJson.Sha256(
                RepositoryReadinessCanonicalJson.Serialize(new
                {
                    schemaVersion = 1,
                    claim.AttemptId,
                    attemptState,
                    observationHash,
                    buildEvidenceHash,
                    testEvidenceHash,
                    indexEvidenceHash,
                    builderEvidenceHash,
                    technicalEvidenceHash
                }));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.TechnicalValidationAttempts
                SET State=@State, FailureCode=@FailureCode, FailureSummary=@FailureSummary,
                    EvidenceSha256=@EvidenceSha256, CompletedAtUtc=@CompletedAtUtc
                WHERE Id=@AttemptId AND TenantId=@TenantId AND ProjectId=@ProjectId
                  AND State=N'Running';
                """,
                new
                {
                    State = attemptState,
                    FailureCode = (string?)null,
                    FailureSummary = (string?)null,
                    EvidenceSha256 = attemptEvidenceHash,
                    CompletedAtUtc = now,
                    AttemptId = claim.AttemptId,
                    command.TenantId,
                    command.ProjectId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            // Companion evidence is accepted only for a terminal attempt. Both writes remain in
            // this serializable transaction, so no observer can see the intermediate state.
            await InsertTechnicalEvidenceAsync(
                connection,
                transaction,
                command,
                claim,
                data,
                materialized,
                assessmentId,
                assessmentRevision,
                technicalEvidenceId,
                builderRecordId,
                observationHash,
                buildEvidenceHash,
                testEvidenceHash,
                indexEvidenceHash,
                builderEvidenceHash,
                gateJson,
                gateHash,
                technicalEvidenceHash,
                now,
                cancellationToken).ConfigureAwait(false);

            var result = new RefreshRepositoryReadinessResult(
                command.ProjectId,
                command.ClientOperationId,
                IsReplay: false,
                materialized.Observation.Id,
                materialized.Build.Id,
                materialized.Test.Id,
                materialized.Index.Id,
                materialized.Evaluation);
            var resultJson = RepositoryReadinessCanonicalJson.Serialize(result);
            var resultHash = RepositoryReadinessCanonicalJson.Sha256(resultJson);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ClientOperations
                SET Status=N'Completed', CanonicalResultJson=@ResultJson, ResultHash=@ResultHash,
                    ResultTechnicalValidationAttemptId=@AttemptId,
                    ResultProjectTechnicalReadinessEvidenceId=@TechnicalEvidenceId,
                    ResultProjectReadinessAssessmentId=@AssessmentId,
                    CompletedAtUtc=@CompletedAtUtc
                WHERE Id=@OperationRecordId AND Status=N'Pending';
                """,
                new
                {
                    ResultJson = resultJson,
                    ResultHash = resultHash,
                    AttemptId = claim.AttemptId,
                    TechnicalEvidenceId = technicalEvidenceId,
                    AssessmentId = assessmentId,
                    CompletedAtUtc = now,
                    OperationRecordId = claim.OperationRecordId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            await InsertOutboxAsync(
                connection,
                transaction,
                command,
                materialized.Evaluation.IsReady
                    ? "RepositoryTechnicalReadinessReady"
                    : "RepositoryTechnicalReadinessValidationRequired",
                RepositoryReadinessCanonicalJson.Serialize(new
                {
                    schemaVersion = 1,
                    command.ProjectId,
                    attemptId = claim.AttemptId,
                    technicalEvidenceId,
                    assessmentId,
                    materialized.Evaluation.ExecutionReadiness,
                    materialized.Evaluation.ReasonCode
                }),
                now,
                cancellationToken).ConfigureAwait(false);
            _failureInjector.ThrowIfRequested(RepositoryReadinessRefreshFailurePoint.OutboxEventsCreated);
            await InsertAttributionAsync(
                connection,
                transaction,
                command,
                claim.AttemptId,
                "Completed",
                200,
                now,
                cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            _failureInjector.ThrowIfRequested(RepositoryReadinessRefreshFailurePoint.CompletionCommitted);
            return result;
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
                // Commit can succeed even when its acknowledgement is lost. The outer exact
                // recovery path reads the durable ClientOperation before emitting failure.
            }
            throw;
        }
    }

    private static Task InsertObservationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        ValidationClaim claim,
        AttemptAuthorityData data,
        MaterializedValidation materialized,
        CancellationToken cancellationToken)
    {
        var parameters = EvidenceParameters(command, claim, data, materialized.Observation);
        parameters.Add("Id", materialized.Observation.Id);
        parameters.Add("HeadCommit", materialized.Observation.HeadCommit);
        parameters.Add("GitTreeId", materialized.Observation.GitTreeId);
        parameters.Add("DirtyState", materialized.Observation.WorktreeState);
        parameters.Add("ObservedAtUtc", materialized.Observation.ObservedAtUtc);
        parameters.Add("EvidenceSha256", materialized.Observation.EvidenceHash);
        return connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.RepositoryStateObservations
                (Id, TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                 ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                 ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                 RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                 ToolchainManifestSha256, ContainerImageDigestSha256,
                 SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                 SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                 SandboxEvidenceManifestSha256, RepositoryFingerprintSha256,
                 HeadCommit, GitTreeId, DirtyState, ObservedAtUtc, EvidenceSha256)
            VALUES
                (@Id, @TenantId, @ProjectId, @TechnicalValidationAttemptId,
                 @RepositoryBindingId, @RepositoryBindingRevision, @BaselineCommit,
                 @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                 @ProfileDefinitionId, @ProfileDescriptorRevision, @ProfileDescriptorSha256,
                 @RestoreCommandSha256, @BuildCommandSha256, @TestCommandSha256,
                 @ToolchainManifestSha256, @ContainerImageDigestSha256,
                 @SandboxPolicySha256, @OfflineFeedManifestSha256, @TemplateBundleSha256,
                 @SandboxQualificationAttemptId, @SandboxEvidenceManifestId,
                 @SandboxEvidenceManifestSha256, @RepositoryFingerprintSha256,
                 @HeadCommit, @GitTreeId, @DirtyState, @ObservedAtUtc, @EvidenceSha256);
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task InsertBuildAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        ValidationClaim claim,
        AttemptAuthorityData data,
        MaterializedValidation materialized,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        var restoreStart = materialized.PackageStartedAtUtc;
        var restoreCompleted = AddDuration(restoreStart, materialized.RestoreStage.DurationMilliseconds);
        var buildStart = restoreCompleted;
        var buildCompleted = AddDuration(buildStart, materialized.BuildStage.DurationMilliseconds);
        var parameters = EvidenceParameters(command, claim, data, materialized.Observation);
        parameters.Add("Id", materialized.Build.Id);
        parameters.Add("RepositoryStateObservationId", materialized.Observation.Id);
        parameters.Add("RestoreOutcome", Outcome(materialized.Build.RestoreResult));
        parameters.Add("RestoreExitCode", materialized.Build.RestoreResult.ExitCode);
        parameters.Add("RestoreStartedAtUtc", restoreStart);
        parameters.Add("RestoreCompletedAtUtc", restoreCompleted);
        parameters.Add("RestoreEvidenceSha256", StageHash(materialized.RestoreStage));
        parameters.Add("BuildOutcome", Outcome(materialized.Build.BuildResult));
        parameters.Add("BuildExitCode", materialized.Build.BuildResult.ExitCode);
        parameters.Add("BuildStartedAtUtc", buildStart);
        parameters.Add("BuildCompletedAtUtc", buildCompleted);
        parameters.Add("BuildEvidenceSha256", StageHash(materialized.BuildStage));
        parameters.Add("CreatedAtUtc", createdAtUtc);
        parameters.Add("EvidenceSha256", RepositoryValidationRecordCodec.ComputeHash(materialized.Build));
        return connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.BuildValidationRecords
                (Id, TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                 RepositoryStateObservationId, RepositoryFingerprintSha256,
                 ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                 ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                 RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                 ToolchainManifestSha256, ContainerImageDigestSha256,
                 SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                 SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                 SandboxEvidenceManifestSha256,
                 RestoreOutcome, RestoreExitCode, RestoreStartedAtUtc,
                 RestoreCompletedAtUtc, RestoreEvidenceSha256,
                 BuildOutcome, BuildExitCode, BuildStartedAtUtc,
                 BuildCompletedAtUtc, BuildEvidenceSha256, CreatedAtUtc, EvidenceSha256)
            VALUES
                (@Id, @TenantId, @ProjectId, @TechnicalValidationAttemptId,
                 @RepositoryBindingId, @RepositoryBindingRevision, @BaselineCommit,
                 @RepositoryStateObservationId, @RepositoryFingerprintSha256,
                 @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                 @ProfileDefinitionId, @ProfileDescriptorRevision, @ProfileDescriptorSha256,
                 @RestoreCommandSha256, @BuildCommandSha256, @TestCommandSha256,
                 @ToolchainManifestSha256, @ContainerImageDigestSha256,
                 @SandboxPolicySha256, @OfflineFeedManifestSha256, @TemplateBundleSha256,
                 @SandboxQualificationAttemptId, @SandboxEvidenceManifestId,
                 @SandboxEvidenceManifestSha256,
                 @RestoreOutcome, @RestoreExitCode, @RestoreStartedAtUtc,
                 @RestoreCompletedAtUtc, @RestoreEvidenceSha256,
                 @BuildOutcome, @BuildExitCode, @BuildStartedAtUtc,
                 @BuildCompletedAtUtc, @BuildEvidenceSha256, @CreatedAtUtc, @EvidenceSha256);
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task InsertTestAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        ValidationClaim claim,
        AttemptAuthorityData data,
        MaterializedValidation materialized,
        CancellationToken cancellationToken)
    {
        var started = AddDuration(
            AddDuration(materialized.PackageStartedAtUtc, materialized.RestoreStage.DurationMilliseconds),
            materialized.BuildStage.DurationMilliseconds);
        var completed = AddDuration(started, materialized.TestStage.DurationMilliseconds);
        var parameters = EvidenceParameters(command, claim, data, materialized.Observation);
        parameters.Add("Id", materialized.Test.Id);
        parameters.Add("RepositoryStateObservationId", materialized.Observation.Id);
        parameters.Add("TestOutcome", Outcome(materialized.Test.TestResult));
        parameters.Add("TestExitCode", materialized.Test.TestResult.ExitCode);
        parameters.Add("TotalTests", 0);
        parameters.Add("PassedTests", 0);
        parameters.Add("FailedTests", 0);
        parameters.Add("SkippedTests", 0);
        parameters.Add("StartedAtUtc", started);
        parameters.Add("CompletedAtUtc", completed);
        parameters.Add("EvidenceSha256", RepositoryValidationRecordCodec.ComputeHash(materialized.Test));
        return connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.TestValidationRecords
                (Id, TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                 RepositoryStateObservationId, RepositoryFingerprintSha256,
                 ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                 ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                 RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                 ToolchainManifestSha256, ContainerImageDigestSha256,
                 SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                 SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                 SandboxEvidenceManifestSha256, TestOutcome, TestExitCode,
                 TotalTests, PassedTests, FailedTests, SkippedTests,
                 StartedAtUtc, CompletedAtUtc, EvidenceSha256)
            VALUES
                (@Id, @TenantId, @ProjectId, @TechnicalValidationAttemptId,
                 @RepositoryBindingId, @RepositoryBindingRevision, @BaselineCommit,
                 @RepositoryStateObservationId, @RepositoryFingerprintSha256,
                 @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                 @ProfileDefinitionId, @ProfileDescriptorRevision, @ProfileDescriptorSha256,
                 @RestoreCommandSha256, @BuildCommandSha256, @TestCommandSha256,
                 @ToolchainManifestSha256, @ContainerImageDigestSha256,
                 @SandboxPolicySha256, @OfflineFeedManifestSha256, @TemplateBundleSha256,
                 @SandboxQualificationAttemptId, @SandboxEvidenceManifestId,
                 @SandboxEvidenceManifestSha256, @TestOutcome, @TestExitCode,
                 @TotalTests, @PassedTests, @FailedTests, @SkippedTests,
                 @StartedAtUtc, @CompletedAtUtc, @EvidenceSha256);
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task InsertIndexAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        ValidationClaim claim,
        AttemptAuthorityData data,
        MaterializedValidation materialized,
        CancellationToken cancellationToken)
    {
        var parameters = EvidenceParameters(command, claim, data, materialized.Observation);
        parameters.Add("Id", materialized.Index.Id);
        parameters.Add("RepositoryStateObservationId", materialized.Observation.Id);
        parameters.Add("IndexState", materialized.Index.State == RepositoryCodeIndexStates.Current ? "Ready" : "Failed");
        parameters.Add("IndexSchemaVersion", 1);
        parameters.Add("IndexerVersion", IndexerVersion);
        parameters.Add("IndexedFileCount", materialized.Index.Sources.Count);
        parameters.Add("IndexedChunkCount", materialized.Index.Sources.Count);
        parameters.Add("IndexContentSha256", materialized.Index.IndexedContentSha256);
        var sourcesJson = SourcesJson(materialized.Index.Sources);
        parameters.Add("SourcesJson", sourcesJson);
        parameters.Add("SourcesSha256", Hash(sourcesJson));
        parameters.Add("StartedAtUtc", materialized.Index.IndexedAtUtc);
        parameters.Add("CompletedAtUtc", materialized.Index.IndexedAtUtc);
        parameters.Add("EvidenceSha256", CodeIndexSnapshotCodec.ComputeHash(materialized.Index));
        return connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.CodeIndexSnapshots
                (Id, TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                 RepositoryStateObservationId, RepositoryFingerprintSha256,
                 ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                 ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                 RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                 ToolchainManifestSha256, ContainerImageDigestSha256,
                 SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                 SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                 SandboxEvidenceManifestSha256, IndexState, IndexSchemaVersion,
                 IndexerVersion, IndexedFileCount, IndexedChunkCount, IndexContentSha256,
                 SourcesJson, SourcesSha256, StartedAtUtc, CompletedAtUtc, EvidenceSha256)
            VALUES
                (@Id, @TenantId, @ProjectId, @TechnicalValidationAttemptId,
                 @RepositoryBindingId, @RepositoryBindingRevision, @BaselineCommit,
                 @RepositoryStateObservationId, @RepositoryFingerprintSha256,
                 @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                 @ProfileDefinitionId, @ProfileDescriptorRevision, @ProfileDescriptorSha256,
                 @RestoreCommandSha256, @BuildCommandSha256, @TestCommandSha256,
                 @ToolchainManifestSha256, @ContainerImageDigestSha256,
                 @SandboxPolicySha256, @OfflineFeedManifestSha256, @TemplateBundleSha256,
                 @SandboxQualificationAttemptId, @SandboxEvidenceManifestId,
                 @SandboxEvidenceManifestSha256, @IndexState, @IndexSchemaVersion,
                 @IndexerVersion, @IndexedFileCount, @IndexedChunkCount, @IndexContentSha256,
                 @SourcesJson, @SourcesSha256, @StartedAtUtc, @CompletedAtUtc, @EvidenceSha256);
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<string> InsertBuilderConfigurationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        ValidationClaim claim,
        AttemptAuthorityData data,
        MaterializedValidation materialized,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var evidenceHash = BuilderStableConfigurationEvidenceCodec.ComputeHash(
            materialized.BuilderConfiguration);
        var parameters = EvidenceParameters(command, claim, data, materialized.Observation);
        parameters.Add("Id", recordId);
        parameters.Add("RepositoryStateObservationId", materialized.Observation.Id);
        parameters.Add("ConfigurationId", materialized.BuilderConfiguration.Binding.ConfigurationId);
        parameters.Add("ConfigurationState", materialized.BuilderConfiguration.IsConfigured ? "Configured" : "Unavailable");
        parameters.Add("ProviderId", materialized.BuilderConfiguration.Binding.ProviderId);
        parameters.Add("ModelId", materialized.BuilderConfiguration.Binding.ModelId);
        parameters.Add("ConfigurationRevision", materialized.BuilderConfiguration.Binding.Revision);
        parameters.Add("ConfigurationSha256", materialized.BuilderConfiguration.Binding.ConfigurationSha256);
        parameters.Add("PolicyVersion", BuilderPolicyVersion);
        parameters.Add("PolicySha256", BuilderPolicySha256());
        parameters.Add("RecordedAtUtc", materialized.BuilderConfiguration.ValidatedAtUtc);
        parameters.Add("EvidenceSha256", evidenceHash);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.BuilderModelConfigurationRecords
                (Id, TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                 RepositoryStateObservationId, RepositoryFingerprintSha256,
                 ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                 ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                 RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                 ToolchainManifestSha256, ContainerImageDigestSha256,
                 SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                 SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                 SandboxEvidenceManifestSha256, ConfigurationId, ConfigurationState,
                 ProviderId, ModelId, ConfigurationRevision, ConfigurationSha256,
                 PolicyVersion, PolicySha256, RecordedAtUtc, EvidenceSha256)
            VALUES
                (@Id, @TenantId, @ProjectId, @TechnicalValidationAttemptId,
                 @RepositoryBindingId, @RepositoryBindingRevision, @BaselineCommit,
                 @RepositoryStateObservationId, @RepositoryFingerprintSha256,
                 @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                 @ProfileDefinitionId, @ProfileDescriptorRevision, @ProfileDescriptorSha256,
                 @RestoreCommandSha256, @BuildCommandSha256, @TestCommandSha256,
                 @ToolchainManifestSha256, @ContainerImageDigestSha256,
                 @SandboxPolicySha256, @OfflineFeedManifestSha256, @TemplateBundleSha256,
                 @SandboxQualificationAttemptId, @SandboxEvidenceManifestId,
                 @SandboxEvidenceManifestSha256, @ConfigurationId, @ConfigurationState,
                 @ProviderId, @ModelId, @ConfigurationRevision, @ConfigurationSha256,
                 @PolicyVersion, @PolicySha256, @RecordedAtUtc, @EvidenceSha256);
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return evidenceHash;
    }

    private static Task InsertTechnicalEvidenceAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        ValidationClaim claim,
        AttemptAuthorityData data,
        MaterializedValidation materialized,
        long assessmentId,
        long assessmentRevision,
        Guid technicalEvidenceId,
        Guid builderRecordId,
        string observationHash,
        string buildHash,
        string testHash,
        string indexHash,
        string builderHash,
        string gateJson,
        string gateHash,
        string evidenceHash,
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        var parameters = EvidenceParameters(command, claim, data, materialized.Observation);
        parameters.Add("Id", technicalEvidenceId);
        parameters.Add("ProjectReadinessAssessmentId", assessmentId);
        parameters.Add("ProjectReadinessRevision", assessmentRevision);
        parameters.Add("RepositoryStateObservationId", materialized.Observation.Id);
        parameters.Add("RepositoryObservationEvidenceSha256", observationHash);
        parameters.Add("BuildValidationRecordId", materialized.Build.Id);
        parameters.Add("BuildValidationEvidenceSha256", buildHash);
        parameters.Add("TestValidationRecordId", materialized.Test.Id);
        parameters.Add("TestValidationEvidenceSha256", testHash);
        parameters.Add("CodeIndexSnapshotId", materialized.Index.Id);
        parameters.Add("CodeIndexEvidenceSha256", indexHash);
        parameters.Add("BuilderModelConfigurationRecordId", builderRecordId);
        parameters.Add("BuilderModelConfigurationEvidenceSha256", builderHash);
        foreach (var gate in materialized.Evaluation.Gates)
            parameters.Add(gate.Gate.ToString(), gate.Passed);
        parameters.Add("GateResultsJson", gateJson);
        parameters.Add("GateResultsSha256", gateHash);
        parameters.Add("ExecutionReadiness", materialized.Evaluation.ExecutionReadiness);
        parameters.Add("ReasonCode", materialized.Evaluation.ReasonCode);
        parameters.Add("AssessedAtUtc", timestamp);
        parameters.Add("EvidenceSha256", evidenceHash);
        parameters.Add("CreatedAtUtc", timestamp);
        return connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.ProjectTechnicalReadinessEvidence
                (Id, TenantId, ProjectId, ProjectReadinessAssessmentId,
                 ProjectReadinessRevision, TechnicalValidationAttemptId,
                 RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                 RepositoryStateObservationId, RepositoryFingerprintSha256,
                 RepositoryObservationEvidenceSha256,
                 ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                 ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                 RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                 ToolchainManifestSha256, ContainerImageDigestSha256,
                 SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                 SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                 SandboxEvidenceManifestSha256, BuildValidationRecordId,
                 BuildValidationEvidenceSha256, TestValidationRecordId,
                 TestValidationEvidenceSha256, CodeIndexSnapshotId,
                 CodeIndexEvidenceSha256, BuilderModelConfigurationRecordId,
                 BuilderModelConfigurationEvidenceSha256,
                 RepositoryBindingQualified, RepositoryCleanAtBaseline,
                 ExecutionProfilePinned, RestorePassed, BuildPassed,
                 TestCommandPassed, CodeIndexCurrent, SandboxQualified,
                 BuilderModelConfigured, GateResultsJson, GateResultsSha256,
                 ExecutionReadiness, ReasonCode, AssessedAtUtc, EvidenceSha256,
                 CreatedAtUtc)
            VALUES
                (@Id, @TenantId, @ProjectId, @ProjectReadinessAssessmentId,
                 @ProjectReadinessRevision, @TechnicalValidationAttemptId,
                 @RepositoryBindingId, @RepositoryBindingRevision, @BaselineCommit,
                 @RepositoryStateObservationId, @RepositoryFingerprintSha256,
                 @RepositoryObservationEvidenceSha256,
                 @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                 @ProfileDefinitionId, @ProfileDescriptorRevision, @ProfileDescriptorSha256,
                 @RestoreCommandSha256, @BuildCommandSha256, @TestCommandSha256,
                 @ToolchainManifestSha256, @ContainerImageDigestSha256,
                 @SandboxPolicySha256, @OfflineFeedManifestSha256, @TemplateBundleSha256,
                 @SandboxQualificationAttemptId, @SandboxEvidenceManifestId,
                 @SandboxEvidenceManifestSha256, @BuildValidationRecordId,
                 @BuildValidationEvidenceSha256, @TestValidationRecordId,
                 @TestValidationEvidenceSha256, @CodeIndexSnapshotId,
                 @CodeIndexEvidenceSha256, @BuilderModelConfigurationRecordId,
                 @BuilderModelConfigurationEvidenceSha256,
                 @RepositoryBindingQualified, @RepositoryCleanAtBaseline,
                 @ExecutionProfilePinned, @RestorePassed, @BuildPassed,
                 @TestCommandPassed, @CodeIndexCurrent, @SandboxQualified,
                 @BuilderModelConfigured, @GateResultsJson, @GateResultsSha256,
                 @ExecutionReadiness, @ReasonCode, @AssessedAtUtc, @EvidenceSha256,
                 @CreatedAtUtc);
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task ProjectLegacyIndexAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        CodeIndexSnapshot index,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ProjectFiles WHERE TenantId=@TenantId AND ProjectId=@ProjectId;",
            command,
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        foreach (var source in index.Sources)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.ProjectFiles
                    (TenantId, ProjectId, FilePath, FileExtension, ContentHash,
                     Content, LastIndexedDate)
                VALUES
                    (@TenantId, @ProjectId, @FilePath, @FileExtension, @ContentHash,
                     N'', @LastIndexedDate);
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    FilePath = source.RelativePath,
                    FileExtension = BoundedExtension(source.RelativePath),
                    ContentHash = source.ContentSha256,
                    LastIndexedDate = index.IndexedAtUtc
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private async Task<RefreshRepositoryReadinessResult?> FinalizeFailureBestEffortAsync(
        RefreshRepositoryReadinessCommand command,
        ValidationClaim claim,
        string reasonCode,
        string summary,
        bool cancelled)
    {
        try
        {
            using var connection = _connections.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();",
                transaction: transaction,
                cancellationToken: CancellationToken.None)).ConfigureAwait(false);
            if (!await TransitionClaimFailureAsync(
                    connection,
                    transaction,
                    command,
                    claim.OperationRecordId,
                    claim.AttemptId,
                    cancelled ? "Cancelled" : "Failed",
                    reasonCode,
                    summary,
                    422,
                    CancellationToken.None,
                    now).ConfigureAwait(false))
            {
                transaction.Rollback();
                return await ReadCompletedResultAfterAmbiguousFailureAsync(
                    connection,
                    command).ConfigureAwait(false);
            }
            transaction.Commit();
            return null;
        }
        catch
        {
            // The durable Running attempt remains the recovery fence if failure finalization is uncertain.
            return null;
        }
    }

    private static async Task<RefreshRepositoryReadinessResult?> ReadCompletedResultAfterAmbiguousFailureAsync(
        IDbConnection connection,
        RefreshRepositoryReadinessCommand command)
    {
        var operation = await connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
            """
            SELECT Id, PayloadHash, Status, CanonicalResultJson, ResultHash
            FROM dbo.ClientOperations
            WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
              AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
              AND ClientOperationId=@ClientOperationId;
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                OperationKind = RepositoryReadinessOperationKinds.Validate,
                ResourceScopeId = ResourceScope(command.ProjectId),
                command.ClientOperationId
            },
            cancellationToken: CancellationToken.None)).ConfigureAwait(false);
        return operation is not null &&
               string.Equals(operation.Status, "Completed", StringComparison.Ordinal) &&
               string.Equals(operation.PayloadHash, PayloadHash(command), StringComparison.Ordinal)
            ? ReadStoredResult(operation)
            : null;
    }

    private static async Task<bool> TransitionClaimFailureAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        long operationRecordId,
        Guid attemptId,
        string terminalState,
        string reasonCode,
        string summary,
        int statusCode,
        CancellationToken cancellationToken,
        DateTime? completedAtUtc = null)
    {
        var now = completedAtUtc ?? await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
            "SELECT SYSUTCDATETIME();",
            transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        var safeReason = Bounded(reasonCode, 100, "RepositoryReadinessValidationFailed");
        var safeSummary = Bounded(summary, 1000, "Technical-readiness validation failed safely.");
        var evidenceHash = RepositoryReadinessCanonicalJson.Sha256(
            RepositoryReadinessCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                attemptId,
                state = terminalState,
                reasonCode = safeReason,
                summary = safeSummary
            }));
        var attemptRows = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.TechnicalValidationAttempts
            SET State=@State, FailureCode=@FailureCode, FailureSummary=@FailureSummary,
                EvidenceSha256=@EvidenceSha256, CompletedAtUtc=@CompletedAtUtc
            WHERE Id=@AttemptId AND TenantId=@TenantId AND ProjectId=@ProjectId
              AND State=N'Running';
            """,
            new
            {
                State = terminalState,
                FailureCode = safeReason,
                FailureSummary = safeSummary,
                EvidenceSha256 = evidenceHash,
                CompletedAtUtc = now,
                AttemptId = attemptId,
                command.TenantId,
                command.ProjectId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        var operationRows = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ClientOperations
            SET Status=N'Failed', ResultTechnicalValidationAttemptId=@AttemptId,
                CompletedAtUtc=@CompletedAtUtc
            WHERE Id=@OperationRecordId AND TenantId=@TenantId
              AND ResultProjectId=@ProjectId AND Status=N'Pending';
            """,
            new
            {
                AttemptId = attemptId,
                CompletedAtUtc = now,
                OperationRecordId = operationRecordId,
                command.TenantId,
                command.ProjectId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (attemptRows != 1 || operationRows != 1)
            return false;

        await InsertOutboxAsync(
            connection,
            transaction,
            command,
            "RepositoryTechnicalValidationFailed",
            RepositoryReadinessCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                command.ProjectId,
                attemptId,
                reasonCode = safeReason
            }),
            now,
            cancellationToken).ConfigureAwait(false);
        await InsertAttributionAsync(
            connection,
            transaction,
            command,
            attemptId,
            "Failed",
            statusCode,
            now,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<AuthorityPackage?> ReadAuthorityPackageAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var binding = await ReadBindingAsync(
            connection,
            transaction,
            tenantId,
            projectId,
            lockRows,
            cancellationToken).ConfigureAwait(false);
        if (binding is null)
            return null;
        var profile = await ReadProfileAsync(
            connection,
            transaction,
            tenantId,
            projectId,
            binding.Id,
            lockRows,
            cancellationToken).ConfigureAwait(false);
        if (profile is null || string.IsNullOrWhiteSpace(binding.BaselineCommit))
            return null;
        var provisioningSnapshot = await ReadProvisioningSnapshotAsync(
            connection,
            transaction,
            tenantId,
            projectId,
            binding,
            profile,
            lockRows,
            cancellationToken).ConfigureAwait(false);
        var sandbox = await ReadPassedSandboxAsync(
            connection,
            transaction,
            tenantId,
            projectId,
            binding,
            profile,
            lockRows,
            cancellationToken).ConfigureAwait(false);
        var authority = BuildAuthority(binding, profile, sandbox);
        return new AuthorityPackage(binding, profile, authority, sandbox, provisioningSnapshot);
    }

    private static Task<ProvisioningSnapshot?> ReadProvisioningSnapshotAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        RepositoryBindingSnapshot binding,
        ProjectExecutionProfileSnapshot profile,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (HOLDLOCK)" : string.Empty;
        return connection.QueryFirstOrDefaultAsync<ProvisioningSnapshot>(new CommandDefinition(
            $"""
            SELECT TOP (1) ManifestJson, ManifestSha256, GitTreeId
            FROM dbo.RepositoryProvisioningReceipts{hint}
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
              AND RepositoryBindingId=@RepositoryBindingId
              AND ProjectExecutionProfileId=@ProjectExecutionProfileId
              AND BaselineCommit=@BaselineCommit
            ORDER BY RecordedAtUtc DESC, Id DESC;
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                RepositoryBindingId = binding.Id,
                ProjectExecutionProfileId = profile.Id,
                binding.BaselineCommit
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<AuthorityPackage> RequireAuthorityPackageAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var package = await ReadAuthorityPackageAsync(
            connection,
            transaction,
            command.TenantId,
            command.ProjectId,
            lockRows,
            cancellationToken).ConfigureAwait(false)
            ?? throw new RepositoryReadinessNotAllowedException(
                "A qualified repository and pinned execution profile are required before technical validation.");
        if (package.Binding.Revision != command.ExpectedRepositoryBindingRevision ||
            package.Profile.Revision != command.ExpectedExecutionProfileRevision)
            throw new RepositoryReadinessStaleConfigurationException();
        if (!string.Equals(package.Binding.BindingState, RepositoryBindingStates.Qualified, StringComparison.Ordinal) ||
            package.Binding.BaselineCommit?.Length != 40)
            throw new RepositoryReadinessNotAllowedException(
                "The repository must be qualified at an immutable baseline before technical validation.");
        if (package.Sandbox is null)
            throw new RepositoryReadinessNotAllowedException(
                "Passing production-sandbox qualification evidence is required before technical validation.");
        return package;
    }

    private static RepositoryReadinessAuthority BuildAuthority(
        RepositoryBindingSnapshot binding,
        ProjectExecutionProfileSnapshot profile,
        SandboxPackage? sandbox) => RepositoryReadinessAuthorityCodec.NormalizeAndValidate(
        new RepositoryReadinessAuthority
        {
            ProjectId = binding.ProjectId,
            RepositoryBindingId = binding.Id,
            RepositoryBindingRevision = binding.Revision,
            BaselineCommit = binding.BaselineCommit!,
            ProjectExecutionProfileId = profile.Id,
            ProjectExecutionProfileRevision = profile.Revision,
            ProfileDefinitionId = profile.ProfileDefinitionId,
            ProfileDescriptorRevision = profile.ProfileDescriptorRevision,
            ProfileDescriptorSha256 = profile.DescriptorSha256,
            RestoreCommandSha256 = Hash(profile.RestoreCommand),
            BuildCommandSha256 = Hash(profile.BuildCommand),
            TestCommandSha256 = Hash(profile.TestCommand),
            SdkToolchainManifestId = profile.ToolchainManifestId,
            ContainerImageDigest = sandbox is null ? null : $"sha256:{sandbox.ContainerImageDigestSha256}",
            SandboxPolicyVersion = sandbox?.Manifest.SandboxPolicyVersion,
            SandboxPolicySha256 = sandbox?.Manifest.SandboxPolicySha256,
            OfflineFeedManifestSha256 = sandbox?.Manifest.OfflineFeedManifestSha256,
            TemplateBundleSha256 = profile.TemplateBundleSha256
        });

    private static async Task<SandboxPackage?> ReadPassedSandboxAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        RepositoryBindingSnapshot binding,
        ProjectExecutionProfileSnapshot profile,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var row = await connection.QueryFirstOrDefaultAsync<SandboxRow>(new CommandDefinition(
            $"""
            SELECT TOP (1)
                   attempt.Id AS AttemptId, attempt.State, attempt.CleanupConfirmed,
                   attempt.RepositoryBindingId,
                   attempt.ExpectedBindingRevision AS RepositoryBindingRevision,
                   attempt.BaselineCommit,
                   attempt.ProjectExecutionProfileId,
                   attempt.ExpectedExecutionProfileRevision AS ProjectExecutionProfileRevision,
                   attempt.EvidenceManifestSha256 AS AttemptEvidenceManifestSha256,
                   manifest.Id AS EvidenceManifestId,
                   manifest.ManifestJson, manifest.ManifestSha256,
                   attempt.ContainerImageDigest AS ContainerImageReference,
                   attempt.CompletedAtUtc,
                   receipt.ManifestJson AS ProvisioningManifestJson,
                   receipt.ManifestSha256 AS ProvisioningManifestSha256,
                   receipt.GitTreeId AS ProvisioningGitTreeId
            FROM dbo.SandboxQualificationAttempts attempt{hint}
            INNER JOIN dbo.RepositoryProvisioningReceipts receipt{hint}
                ON receipt.TenantId=attempt.TenantId
               AND receipt.ProjectId=attempt.ProjectId
               AND receipt.Id=attempt.RepositoryProvisioningReceiptId
            LEFT JOIN dbo.SandboxEvidenceManifests manifest{hint}
                ON manifest.TenantId=attempt.TenantId
               AND manifest.ProjectId=attempt.ProjectId
               AND manifest.SandboxQualificationAttemptId=attempt.Id
            WHERE attempt.TenantId=@TenantId AND attempt.ProjectId=@ProjectId
            ORDER BY attempt.AttemptNumber DESC, attempt.StartedAtUtc DESC;
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                RepositoryBindingId = binding.Id,
                RepositoryBindingRevision = binding.Revision,
                binding.BaselineCommit,
                ProjectExecutionProfileId = profile.Id,
                ProjectExecutionProfileRevision = profile.Revision
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (row is null)
            return null;
        if (!string.Equals(row.State, SandboxQualificationStates.Passed, StringComparison.Ordinal) ||
            row.CleanupConfirmed != true ||
            row.RepositoryBindingId != binding.Id ||
            row.RepositoryBindingRevision != binding.Revision ||
            !string.Equals(row.BaselineCommit, binding.BaselineCommit, StringComparison.Ordinal) ||
            row.ProjectExecutionProfileId != profile.Id ||
            row.ProjectExecutionProfileRevision != profile.Revision)
            return null;
        if (row.EvidenceManifestId is null || string.IsNullOrWhiteSpace(row.ManifestJson) ||
            string.IsNullOrWhiteSpace(row.ManifestSha256) || row.CompletedAtUtc is null ||
            !string.Equals(
                row.AttemptEvidenceManifestSha256,
                row.ManifestSha256,
                StringComparison.Ordinal))
            throw new RepositoryReadinessIntegrityException(
                "The latest passed sandbox attempt has no exact evidence manifest.");
        SandboxEvidenceManifest manifest;
        try
        {
            manifest = SandboxEvidenceManifestCodec.DeserializeCanonical(row.ManifestJson);
        }
        catch (SandboxContractValidationException)
        {
            throw new RepositoryReadinessIntegrityException(
                "The passed sandbox manifest is not canonical.");
        }
        if (!string.Equals(
                SandboxEvidenceManifestCodec.ComputeHash(manifest),
                row.ManifestSha256,
                StringComparison.Ordinal) ||
            manifest.ExecutionId != row.AttemptId ||
            manifest.ProjectId != projectId ||
            manifest.RepositoryBindingId != binding.Id ||
            manifest.RepositoryBindingRevision != binding.Revision ||
            !string.Equals(manifest.BaselineCommit, binding.BaselineCommit, StringComparison.Ordinal) ||
            manifest.ProjectExecutionProfileId != profile.Id ||
            manifest.ProjectExecutionProfileRevision != profile.Revision ||
            !string.Equals(manifest.TemplateBundleSha256, profile.TemplateBundleSha256, StringComparison.Ordinal) ||
            !string.Equals(manifest.ToolchainManifestId, profile.ToolchainManifestId, StringComparison.Ordinal) ||
            manifest.Status != SandboxExecutionStatus.Succeeded)
            throw new RepositoryReadinessIntegrityException(
                "The passed sandbox manifest does not match current repository authority.");
        var restore = RequireStage(manifest, SandboxExecutionStage.Restore);
        var build = RequireStage(manifest, SandboxExecutionStage.Build);
        var test = RequireStage(manifest, SandboxExecutionStage.Test);
        if (!string.Equals(restore.CommandSha256, Hash(profile.RestoreCommand), StringComparison.Ordinal) ||
            !string.Equals(build.CommandSha256, Hash(profile.BuildCommand), StringComparison.Ordinal) ||
            !string.Equals(test.CommandSha256, Hash(profile.TestCommand), StringComparison.Ordinal))
            throw new RepositoryReadinessIntegrityException(
                "The passed sandbox command evidence does not match the current execution profile.");
        var imageDigest = ImageDigest(row.ContainerImageReference);
        if (!string.Equals(imageDigest, manifest.ContainerImageDigest, StringComparison.Ordinal))
            throw new RepositoryReadinessIntegrityException(
                "The passed sandbox image evidence does not match its durable attempt.");
        return new SandboxPackage(
            row.AttemptId,
            row.EvidenceManifestId.Value,
            row.ManifestJson,
            row.ManifestSha256,
            row.ContainerImageReference,
            imageDigest,
            manifest.CompletedAtUtc,
            row.ProvisioningManifestJson,
            row.ProvisioningManifestSha256,
            row.ProvisioningGitTreeId,
            manifest);
    }

    private static Task<ProjectRow?> ReadProjectAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<ProjectRow>(new CommandDefinition(
            """
            SELECT project.Id AS ProjectId,
                   COALESCE(lifecycle.Phase, N'Shaping') AS ProjectLifecyclePhase
            FROM dbo.Projects project
            OUTER APPLY
            (
                SELECT TOP (1) value.Phase
                FROM dbo.ProjectLifecyclePhases value
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC
            ) lifecycle
            WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<RepositoryBindingSnapshot?> ReadBindingAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        return connection.QuerySingleOrDefaultAsync<RepositoryBindingSnapshot>(new CommandDefinition(
            $"""
            SELECT Id, ProjectId, CurrentRevision AS Revision, RepositoryKind,
                   CanonicalPath, BindingState, DefaultBranch, BaselineCommit,
                   CreatedByActorUserId, ConfirmedAtUtc
            FROM dbo.RepositoryBindings{hint}
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<ProjectExecutionProfileSnapshot?> ReadProfileAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        Guid bindingId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        return connection.QuerySingleOrDefaultAsync<ProjectExecutionProfileSnapshot>(new CommandDefinition(
            $"""
            SELECT Id, ProjectId, CurrentRevision AS Revision, RepositoryBindingId,
                   ProfileDefinitionId, ProfileDescriptorRevision, DescriptorSha256,
                   TemplateBundleSha256, PlanningBundleSha256, TargetFramework, Language,
                   ApplicationKind, TestFramework, SdkVersion, RuntimeVersion,
                   SolutionPath, AppProjectPath, TestProjectPath, RestoreCommand, BuildCommand,
                   TestCommand, ToolchainManifestId, ExecutionImageReference,
                   PlanningReadiness, CertificationState
            FROM dbo.ProjectExecutionProfiles{hint}
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
              AND RepositoryBindingId=@RepositoryBindingId;
            """,
            new { TenantId = tenantId, ProjectId = projectId, RepositoryBindingId = bindingId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<StoredContextRow?> ReadLatestStoredContextAsync(
        IDbConnection connection,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken) => connection.QueryFirstOrDefaultAsync<StoredContextRow>(new CommandDefinition(
            """
            SELECT TOP (1) operation.CanonicalResultJson, operation.ResultHash,
                   observation.RepositoryBindingId, observation.RepositoryBindingRevision,
                   observation.BaselineCommit, observation.HeadCommit, observation.GitTreeId,
                   observation.DirtyState, observation.RepositoryFingerprintSha256,
                   observation.EvidenceSha256 AS ObservationEvidenceSha256,
                   attempt.ProjectExecutionProfileId,
                   attempt.ProjectExecutionProfileRevision,
                   attempt.ProfileDefinitionId, attempt.ProfileDescriptorRevision,
                   attempt.ProfileDescriptorSha256, attempt.RestoreCommandSha256,
                   attempt.BuildCommandSha256, attempt.TestCommandSha256,
                   attempt.ToolchainManifestId, attempt.TemplateBundleSha256,
                   attempt.SandboxQualificationAttemptId,
                   attempt.SandboxEvidenceManifestId,
                   attempt.SandboxEvidenceManifestSha256,
                   model.ConfigurationId, model.ConfigurationRevision,
                   model.ConfigurationState,
                   model.ConfigurationSha256
            FROM dbo.ClientOperations operation
            INNER JOIN dbo.TechnicalValidationAttempts attempt
                ON attempt.Id=operation.ResultTechnicalValidationAttemptId
            INNER JOIN dbo.RepositoryStateObservations observation
                ON observation.TechnicalValidationAttemptId=attempt.Id
            INNER JOIN dbo.BuilderModelConfigurationRecords model
                ON model.TechnicalValidationAttemptId=attempt.Id
            WHERE operation.TenantId=@TenantId AND operation.ResultProjectId=@ProjectId
              AND operation.OperationKind=N'ValidateRepositoryTechnicalReadiness'
              AND operation.Status=N'Completed'
              AND attempt.State=N'Passed'
              AND attempt.Id=
              (
                  SELECT TOP (1) latest.Id
                  FROM dbo.TechnicalValidationAttempts latest
                  WHERE latest.TenantId=@TenantId AND latest.ProjectId=@ProjectId
                  ORDER BY latest.AttemptNumber DESC, latest.StartedAtUtc DESC
              )
            ORDER BY operation.CompletedAtUtc DESC;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            cancellationToken: cancellationToken));

    private static Task<SandboxAttemptIdentity?> ReadLatestSandboxIdentityAsync(
        IDbConnection connection,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken) =>
        connection.QueryFirstOrDefaultAsync<SandboxAttemptIdentity>(new CommandDefinition(
            """
            SELECT TOP (1) Id, State
            FROM dbo.SandboxQualificationAttempts
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
            ORDER BY AttemptNumber DESC, StartedAtUtc DESC;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            cancellationToken: cancellationToken));

    private static Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
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
                command.TenantId,
                command.ActorUserId,
                OperationKind = RepositoryReadinessOperationKinds.Validate,
                ResourceScopeId = ResourceScope(command.ProjectId),
                command.ClientOperationId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<AttemptRow?> ReadAttemptByOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long operationId,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<AttemptRow>(new CommandDefinition(
            """
            SELECT Id, AttemptNumber, RepositoryBindingId, RepositoryBindingRevision,
                   BaselineCommit, ProjectExecutionProfileId,
                   ProjectExecutionProfileRevision, ProfileDefinitionId,
                   ProfileDescriptorRevision, ProfileDescriptorSha256, RestoreCommandSha256,
                   BuildCommandSha256, TestCommandSha256, ToolchainManifestId,
                   ToolchainManifestSha256, ContainerImageDigest,
                   ContainerImageDigestSha256, SandboxPolicyVersion,
                   SandboxPolicySha256, OfflineFeedManifestSha256,
                   TemplateBundleSha256, SandboxQualificationAttemptId,
                   SandboxEvidenceManifestId, SandboxEvidenceManifestSha256,
                   State, FailureCode, FailureSummary, StartedAtUtc
            FROM dbo.TechnicalValidationAttempts WITH (UPDLOCK, HOLDLOCK)
            WHERE ClientOperationRecordId=@OperationId;
            """,
            new { OperationId = operationId },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task<bool> ValidateAndRenewLeaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
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
              AND lease.LeaseEpoch=@LeaseEpoch
              AND lease.HolderActorUserId=@ActorUserId
              AND lease.RevokedAtUtc IS NULL
              AND lease.ExpiresAtUtc > SYSUTCDATETIME();
            """,
            command,
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;

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

    private async Task EnsurePreLockAccessAsync(
        RefreshRepositoryReadinessCommand command,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        if (!await CanAccessProjectAsync(
                connection,
                null,
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                requireContributor: false,
                cancellationToken).ConfigureAwait(false))
            throw new WorkbenchProjectNotAccessibleException();
        if (!await CanAccessProjectAsync(
                connection,
                null,
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                requireContributor: true,
                cancellationToken).ConfigureAwait(false))
            throw new RepositoryReadinessForbiddenException();
    }

    private async Task<IDisposable> AcquireDistributedLockAsync(
        RefreshRepositoryReadinessCommand command,
        CancellationToken cancellationToken)
    {
        var connection = _connections.CreateConnection();
        try
        {
            connection.Open();
            var resource = $"IronDev:RepositoryReadiness:{command.TenantId}:{command.ProjectId}";
            var result = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                DECLARE @result INT;
                EXEC @result = sys.sp_getapplock
                    @Resource=@Resource, @LockMode=N'Exclusive', @LockOwner=N'Session',
                    @LockTimeout=0, @DbPrincipal=N'public';
                SELECT @result;
                """,
                new { Resource = resource },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (result < 0)
                throw new RepositoryReadinessInProgressException();
            return new DistributedLock(connection, resource);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static Task InsertOutboxAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        string eventKind,
        string payload,
        DateTime timestamp,
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
                command.TenantId,
                command.ProjectId,
                command.WorkbenchSessionId,
                EventKind = eventKind,
                PayloadJson = payload,
                command.ClientOperationId,
                OccurredAtUtc = timestamp
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task InsertAttributionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RefreshRepositoryReadinessCommand command,
        Guid attemptId,
        string phase,
        int statusCode,
        DateTime timestamp,
        CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.UserMutationAttribution
                (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId,
                 TimestampUtc, SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
            VALUES
                (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                 CONVERT(NVARCHAR(128), @ClientOperationId), CONVERT(NVARCHAR(128), @AttemptId),
                 @TimestampUtc, N'Workbench', N'IronDev.Api', N'POST', @Route, @Phase, @StatusCode);
            """,
            new
            {
                command.ActorUserId,
                command.TenantId,
                command.ProjectId,
                command.ClientOperationId,
                AttemptId = attemptId,
                TimestampUtc = timestamp,
                Route,
                Phase = phase,
                StatusCode = statusCode
            },
            transaction,
            cancellationToken: cancellationToken));

    private static RepositoryReadinessEvidenceBinding ToEvidenceBinding(
        RepositoryReadinessAuthority authority,
        RepositoryStateObservation observation)
    {
        if (authority.ContainerImageDigest is null || authority.SandboxPolicyVersion is null ||
            authority.SandboxPolicySha256 is null || authority.OfflineFeedManifestSha256 is null)
            throw new RepositoryReadinessIntegrityException(
                "Complete sandbox authority is required to bind technical evidence.");
        return RepositoryReadinessEvidenceBindingCodec.NormalizeAndValidate(
            new RepositoryReadinessEvidenceBinding
            {
                ProjectId = authority.ProjectId,
                RepositoryBindingId = authority.RepositoryBindingId,
                RepositoryBindingRevision = authority.RepositoryBindingRevision,
                BaselineCommit = authority.BaselineCommit,
                RepositoryStateObservationId = observation.Id,
                WorktreeFingerprint = observation.WorktreeFingerprint,
                ProjectExecutionProfileId = authority.ProjectExecutionProfileId,
                ProjectExecutionProfileRevision = authority.ProjectExecutionProfileRevision,
                ProfileDefinitionId = authority.ProfileDefinitionId,
                ProfileDescriptorRevision = authority.ProfileDescriptorRevision,
                ProfileDescriptorSha256 = authority.ProfileDescriptorSha256,
                RestoreCommandSha256 = authority.RestoreCommandSha256,
                BuildCommandSha256 = authority.BuildCommandSha256,
                TestCommandSha256 = authority.TestCommandSha256,
                SdkToolchainManifestId = authority.SdkToolchainManifestId,
                ContainerImageDigest = authority.ContainerImageDigest,
                SandboxPolicyVersion = authority.SandboxPolicyVersion,
                SandboxPolicySha256 = authority.SandboxPolicySha256,
                OfflineFeedManifestSha256 = authority.OfflineFeedManifestSha256,
                TemplateBundleSha256 = authority.TemplateBundleSha256
            });
    }

    private static RepositorySandboxQualificationEvidence ToSandboxEvidence(
        AuthorityPackage package,
        RepositoryReadinessEvidenceBinding binding)
    {
        var sandbox = package.Sandbox ?? throw new RepositoryReadinessIntegrityException(
            "Passing sandbox evidence is unavailable.");
        return RepositorySandboxQualificationEvidenceCodec.NormalizeAndValidate(
            new RepositorySandboxQualificationEvidence
            {
                QualificationAttemptId = sandbox.AttemptId,
                Revision = 1,
                Binding = binding.WithoutCodeIndex(),
                State = RepositorySandboxQualificationEvidenceStates.Passed,
                ValidatedAtUtc = sandbox.CompletedAtUtc,
                EvidenceManifestSha256 = sandbox.ManifestSha256
            });
    }

    private static RepositoryValidationCommandResult ToCommandResult(SandboxStageEvidence stage) => new()
    {
        CommandSha256 = stage.CommandSha256,
        Outcome = stage.TimedOut
            ? RepositoryValidationOutcome.TimedOut
            : stage.ExitCode == 0
                ? RepositoryValidationOutcome.Passed
                : RepositoryValidationOutcome.Failed,
        ExitCode = stage.ExitCode,
        TimedOut = stage.TimedOut,
        DurationMilliseconds = stage.DurationMilliseconds,
        StandardOutputSha256 = stage.StandardOutputSha256,
        StandardErrorSha256 = stage.StandardErrorSha256
    };

    private static SandboxStageEvidence RequireStage(
        SandboxEvidenceManifest manifest,
        SandboxExecutionStage stage)
    {
        var matches = manifest.Stages.Where(value => value.Stage == stage).ToArray();
        if (matches.Length != 1)
            throw new RepositoryReadinessIntegrityException(
                "The passed sandbox manifest does not contain one exact restore/build/test stage set.");
        return matches[0];
    }

    private static AttemptAuthorityData AttemptAuthority(AuthorityPackage package)
    {
        var sandbox = package.Sandbox ?? throw new RepositoryReadinessIntegrityException(
            "Passing sandbox evidence is required for technical validation.");
        return new AttemptAuthorityData(
            package.Binding.Id,
            package.Binding.Revision,
            package.Binding.BaselineCommit!,
            package.Profile.Id,
            package.Profile.Revision,
            package.Profile.ProfileDefinitionId,
            package.Profile.ProfileDescriptorRevision,
            package.Profile.DescriptorSha256,
            Hash(package.Profile.RestoreCommand),
            Hash(package.Profile.BuildCommand),
            Hash(package.Profile.TestCommand),
            package.Profile.ToolchainManifestId,
            Hash(package.Profile.ToolchainManifestId),
            sandbox.ContainerImageReference,
            sandbox.ContainerImageDigestSha256,
            sandbox.Manifest.SandboxPolicyVersion,
            sandbox.Manifest.SandboxPolicySha256,
            sandbox.Manifest.OfflineFeedManifestSha256,
            sandbox.Manifest.TemplateBundleSha256,
            sandbox.AttemptId,
            sandbox.EvidenceManifestId,
            sandbox.ManifestSha256);
    }

    private static DynamicParameters EvidenceParameters(
        RefreshRepositoryReadinessCommand command,
        ValidationClaim claim,
        AttemptAuthorityData data,
        RepositoryStateObservation observation)
    {
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", command.TenantId);
        parameters.Add("ProjectId", command.ProjectId);
        parameters.Add("TechnicalValidationAttemptId", claim.AttemptId);
        parameters.Add("RepositoryBindingId", data.RepositoryBindingId);
        parameters.Add("RepositoryBindingRevision", data.RepositoryBindingRevision);
        parameters.Add("BaselineCommit", data.BaselineCommit);
        parameters.Add("RepositoryFingerprintSha256", observation.WorktreeFingerprint);
        parameters.Add("ProjectExecutionProfileId", data.ProjectExecutionProfileId);
        parameters.Add("ProjectExecutionProfileRevision", data.ProjectExecutionProfileRevision);
        parameters.Add("ProfileDefinitionId", data.ProfileDefinitionId);
        parameters.Add("ProfileDescriptorRevision", data.ProfileDescriptorRevision);
        parameters.Add("ProfileDescriptorSha256", data.ProfileDescriptorSha256);
        parameters.Add("RestoreCommandSha256", data.RestoreCommandSha256);
        parameters.Add("BuildCommandSha256", data.BuildCommandSha256);
        parameters.Add("TestCommandSha256", data.TestCommandSha256);
        parameters.Add("ToolchainManifestSha256", data.ToolchainManifestSha256);
        parameters.Add("ContainerImageDigestSha256", data.ContainerImageDigestSha256);
        parameters.Add("SandboxPolicySha256", data.SandboxPolicySha256);
        parameters.Add("OfflineFeedManifestSha256", data.OfflineFeedManifestSha256);
        parameters.Add("TemplateBundleSha256", data.TemplateBundleSha256);
        parameters.Add("SandboxQualificationAttemptId", data.SandboxQualificationAttemptId);
        parameters.Add("SandboxEvidenceManifestId", data.SandboxEvidenceManifestId);
        parameters.Add("SandboxEvidenceManifestSha256", data.SandboxEvidenceManifestSha256);
        return parameters;
    }

    private static void EnsureAttemptAuthority(
        AttemptRow attempt,
        AuthorityPackage package,
        RefreshRepositoryReadinessCommand command)
    {
        var expected = AttemptAuthority(package);
        if (attempt.RepositoryBindingId != expected.RepositoryBindingId ||
            attempt.RepositoryBindingRevision != expected.RepositoryBindingRevision ||
            !string.Equals(attempt.BaselineCommit, expected.BaselineCommit, StringComparison.Ordinal) ||
            attempt.ProjectExecutionProfileId != expected.ProjectExecutionProfileId ||
            attempt.ProjectExecutionProfileRevision != expected.ProjectExecutionProfileRevision ||
            !string.Equals(attempt.ProfileDefinitionId, expected.ProfileDefinitionId, StringComparison.Ordinal) ||
            attempt.ProfileDescriptorRevision != expected.ProfileDescriptorRevision ||
            !string.Equals(attempt.ProfileDescriptorSha256, expected.ProfileDescriptorSha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.RestoreCommandSha256, expected.RestoreCommandSha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.BuildCommandSha256, expected.BuildCommandSha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.TestCommandSha256, expected.TestCommandSha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.ToolchainManifestSha256, expected.ToolchainManifestSha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.ContainerImageDigestSha256, expected.ContainerImageDigestSha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.SandboxPolicySha256, expected.SandboxPolicySha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.OfflineFeedManifestSha256, expected.OfflineFeedManifestSha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.TemplateBundleSha256, expected.TemplateBundleSha256, StringComparison.Ordinal) ||
            attempt.SandboxQualificationAttemptId != expected.SandboxQualificationAttemptId ||
            attempt.SandboxEvidenceManifestId != expected.SandboxEvidenceManifestId ||
            !string.Equals(attempt.SandboxEvidenceManifestSha256, expected.SandboxEvidenceManifestSha256, StringComparison.Ordinal) ||
            attempt.RepositoryBindingRevision != command.ExpectedRepositoryBindingRevision ||
            attempt.ProjectExecutionProfileRevision != command.ExpectedExecutionProfileRevision)
            throw new RepositoryReadinessStaleConfigurationException();
    }

    private static bool StoredContextIsCurrent(
        StoredContextRow stored,
        AuthorityPackage package,
        RepositoryStateObservation live)
    {
        return stored.RepositoryBindingId == package.Binding.Id &&
               stored.RepositoryBindingRevision == package.Binding.Revision &&
               string.Equals(stored.BaselineCommit, package.Binding.BaselineCommit, StringComparison.Ordinal) &&
               stored.ProjectExecutionProfileId == package.Profile.Id &&
               stored.ProjectExecutionProfileRevision == package.Profile.Revision &&
               string.Equals(stored.ProfileDefinitionId, package.Profile.ProfileDefinitionId, StringComparison.Ordinal) &&
               stored.ProfileDescriptorRevision == package.Profile.ProfileDescriptorRevision &&
               string.Equals(stored.ProfileDescriptorSha256, package.Profile.DescriptorSha256, StringComparison.Ordinal) &&
               string.Equals(stored.RestoreCommandSha256, Hash(package.Profile.RestoreCommand), StringComparison.Ordinal) &&
               string.Equals(stored.BuildCommandSha256, Hash(package.Profile.BuildCommand), StringComparison.Ordinal) &&
               string.Equals(stored.TestCommandSha256, Hash(package.Profile.TestCommand), StringComparison.Ordinal) &&
               string.Equals(stored.ToolchainManifestId, package.Profile.ToolchainManifestId, StringComparison.Ordinal) &&
               string.Equals(stored.TemplateBundleSha256, package.Profile.TemplateBundleSha256, StringComparison.Ordinal) &&
               string.Equals(stored.HeadCommit, live.HeadCommit, StringComparison.Ordinal) &&
               string.Equals(stored.GitTreeId, live.GitTreeId, StringComparison.Ordinal) &&
               string.Equals(stored.DirtyState, live.WorktreeState, StringComparison.Ordinal) &&
               string.Equals(stored.RepositoryFingerprintSha256, live.WorktreeFingerprint, StringComparison.Ordinal);
    }

    private static bool BuilderConfigurationIsCurrent(
        StoredContextRow stored,
        BuilderStableConfigurationBinding? builder) => builder is null
        ? string.Equals(stored.ConfigurationState, "Unavailable", StringComparison.Ordinal)
        : string.Equals(stored.ConfigurationState, "Configured", StringComparison.Ordinal) &&
          stored.ConfigurationId == builder.ConfigurationId &&
          stored.ConfigurationRevision == builder.Revision &&
          string.Equals(stored.ConfigurationSha256, builder.ConfigurationSha256, StringComparison.Ordinal);

    private static bool StoredSandboxIsCurrent(
        StoredContextRow stored,
        SandboxPackage? sandbox,
        SandboxAttemptIdentity? latestSandbox) =>
        sandbox is not null &&
        latestSandbox is not null &&
        string.Equals(latestSandbox.State, SandboxQualificationStates.Passed, StringComparison.Ordinal) &&
        latestSandbox.Id == sandbox.AttemptId &&
        stored.SandboxQualificationAttemptId == sandbox.AttemptId &&
        stored.SandboxEvidenceManifestId == sandbox.EvidenceManifestId &&
        string.Equals(
            stored.SandboxEvidenceManifestSha256,
            sandbox.ManifestSha256,
            StringComparison.Ordinal);

    private static RepositoryReadinessEvaluationResult InvalidateSandboxGate(
        RepositoryReadinessEvaluationResult evaluation,
        string currentAuthoritySha256) => evaluation with
    {
        ExecutionReadiness = ProjectExecutionReadinessStates.ValidationRequired,
        ReasonCode = RepositoryReadinessReasonCodes.SandboxQualificationRequired,
        CurrentAuthoritySha256 = currentAuthoritySha256,
        Gates = evaluation.Gates.Select(static gate =>
            gate.Gate == RepositoryReadinessGateName.SandboxQualified
                ? gate with
                {
                    Passed = false,
                    ReasonCode = RepositoryReadinessReasonCodes.SandboxQualificationRequired
                }
                : gate).ToArray()
    };

    private static RepositoryReadinessEvaluationResult InvalidateBuilderGate(
        RepositoryReadinessEvaluationResult evaluation,
        string currentAuthoritySha256) => evaluation with
    {
        ExecutionReadiness = ProjectExecutionReadinessStates.ValidationRequired,
        ReasonCode = RepositoryReadinessReasonCodes.BuilderModelConfigurationRequired,
        CurrentAuthoritySha256 = currentAuthoritySha256,
        Gates = evaluation.Gates.Select(static gate =>
            gate.Gate == RepositoryReadinessGateName.BuilderModelConfigured
                ? gate with
                {
                    Passed = false,
                    ReasonCode = RepositoryReadinessReasonCodes.BuilderModelConfigurationRequired
                }
                : gate).ToArray()
    };

    private static RefreshRepositoryReadinessResult ReadStoredResult(ClientOperationRow row) =>
        ReadStoredResult(row.CanonicalResultJson, row.ResultHash);

    private static RefreshRepositoryReadinessResult ReadStoredResult(StoredContextRow row) =>
        ReadStoredResult(row.CanonicalResultJson, row.ResultHash);

    private static RefreshRepositoryReadinessResult ReadStoredResult(string? json, string? hash)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(hash) ||
            !string.Equals(RepositoryReadinessCanonicalJson.Sha256(json), hash, StringComparison.Ordinal))
            throw new RepositoryReadinessIntegrityException(
                "The stored readiness operation result failed integrity verification.");
        return JsonSerializer.Deserialize<RefreshRepositoryReadinessResult>(json, JsonOptions)
               ?? throw new RepositoryReadinessIntegrityException(
                   "The stored readiness operation result is unreadable.");
    }

    private async Task<ExecutionAvailabilityCheck> SafeAvailabilityAsync(
        ExecutionAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _availability.CheckAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ExecutionAvailabilityCheck
            {
                State = ExecutionAvailabilityStates.Unavailable,
                ReasonCode = "BuilderAvailabilityCheckUnavailable",
                SafeMessage = "Builder execution availability could not be checked.",
                CheckedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private static BuilderStableConfigurationBinding UnconfiguredBuilder(Guid attemptId) =>
        BuilderStableConfigurationEvidenceCodec.NormalizeAndValidate(new BuilderStableConfigurationBinding
        {
            ConfigurationId = DeterministicGuid($"unconfigured-builder-v1\n{attemptId:D}"),
            Revision = 1,
            ProviderId = "unconfigured",
            ModelId = "unconfigured",
            ConfigurationSha256 = Hash("unconfigured-builder-v1")
        });

    private static bool SameBuilder(
        BuilderStableConfigurationBinding? actual,
        BuilderStableConfigurationBinding? expected) =>
        actual is null && expected is null ||
        actual is not null && expected is not null &&
        string.Equals(
            BuilderStableConfigurationEvidenceCodec.SerializeCanonical(actual),
            BuilderStableConfigurationEvidenceCodec.SerializeCanonical(expected),
            StringComparison.Ordinal);

    private static void EnsureObservationStillCurrent(
        MaterializedValidation materialized,
        RepositoryObservationResult finalObserved)
    {
        var finalObservation = RepositoryStateObservationCodec.NormalizeAndValidate(
            finalObserved.Observation);
        var materializedObservation = materialized.Observation;
        if (finalObservation.RepositoryBindingId != materializedObservation.RepositoryBindingId ||
            finalObservation.RepositoryBindingRevision != materializedObservation.RepositoryBindingRevision ||
            !string.Equals(finalObservation.BaselineCommit, materializedObservation.BaselineCommit,
                StringComparison.Ordinal) ||
            !string.Equals(finalObservation.HeadCommit, materializedObservation.HeadCommit,
                StringComparison.Ordinal) ||
            !string.Equals(finalObservation.GitTreeId, materializedObservation.GitTreeId,
                StringComparison.Ordinal) ||
            !string.Equals(finalObservation.WorktreeState, materializedObservation.WorktreeState,
                StringComparison.Ordinal) ||
            !string.Equals(finalObservation.WorktreeFingerprint, materializedObservation.WorktreeFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(SourcesJson(finalObserved.Sources), SourcesJson(materialized.Index.Sources),
                StringComparison.Ordinal))
            throw new RepositoryReadinessStaleConfigurationException();
    }

    private static string PayloadHash(RefreshRepositoryReadinessCommand command) =>
        RepositoryReadinessCanonicalJson.Sha256(RepositoryReadinessCanonicalJson.Serialize(new
        {
            schemaVersion = 1,
            command.ProjectId,
            command.ExpectedRepositoryBindingRevision,
            command.ExpectedExecutionProfileRevision
        }));

    private static string ResourceScope(int projectId) => $"project:{projectId}:technical-readiness";

    private static string SourcesJson(IReadOnlyList<CodeIndexSourceFingerprint> sources) =>
        RepositoryReadinessCanonicalJson.Serialize(sources.Select(static source => new
        {
            source.Ordinal,
            source.RelativePath,
            source.ContentSha256
        }).ToArray());

    private static string GateResultsJson(IReadOnlyList<RepositoryReadinessGateResult> gates) =>
        RepositoryReadinessCanonicalJson.Serialize(gates.Select(static gate => new
        {
            gate = gate.Gate.ToString(),
            gate.Passed,
            gate.ReasonCode
        }).ToArray());

    private static string StageHash(SandboxStageEvidence stage) =>
        RepositoryReadinessCanonicalJson.Sha256(RepositoryReadinessCanonicalJson.Serialize(new
        {
            stage = stage.Stage.ToString(),
            stage.CommandSha256,
            stage.ExitCode,
            stage.TimedOut,
            stage.DurationMilliseconds,
            stage.StandardOutputSha256,
            stage.StandardErrorSha256,
            stage.StandardOutputTruncated,
            stage.StandardErrorTruncated
        }));

    private static string Outcome(RepositoryValidationCommandResult result) => result.Outcome.ToString();
    private static DateTimeOffset AddDuration(DateTimeOffset value, long milliseconds) =>
        value.AddMilliseconds(Math.Max(0, milliseconds));
    private static string BuilderPolicySha256() => Hash(BuilderPolicyVersion);
    private static string Hash(string value) => RepositoryReadinessCanonicalJson.Sha256(value);
    private static Guid DeterministicGuid(string value)
    {
        var bytes = Convert.FromHexString(Hash(value))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static string ImageDigest(string reference)
    {
        var separator = reference.LastIndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        if (separator <= 0)
            throw new RepositoryReadinessIntegrityException(
                "The durable sandbox image authority is not digest pinned.");
        return RepositoryReadinessCanonicalJson.NormalizeSha256(
            reference[(separator + "@sha256:".Length)..],
            nameof(reference));
    }

    private static string BoundedExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Length <= 50 ? extension : extension[..50];
    }

    private static string Bounded(string? value, int maximum, string fallback)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(static character => !char.IsControl(character))
            .Take(maximum)
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static (string ReasonCode, string Summary) SafeFailure(Exception exception) => exception switch
    {
        WorkbenchProjectNotAccessibleException =>
            ("RepositoryReadinessProjectAccessLost", "Project access changed during technical-readiness validation."),
        RepositoryReadinessForbiddenException =>
            ("RepositoryReadinessWriteAccessLost", "Project write access changed during technical-readiness validation."),
        WorkbenchLeaseFenceException =>
            ("RepositoryReadinessLeaseFenceLost", "The Workbench write fence changed during technical-readiness validation."),
        RepositoryReadinessStaleConfigurationException =>
            ("RepositoryReadinessConfigurationStale", "Repository authority changed during technical-readiness validation."),
        RepositoryReadinessObservationException observation =>
            (observation.ReasonCode, "The controlled repository observation failed safely."),
        RepositoryReadinessIntegrityException =>
            ("RepositoryReadinessIntegrityFailed", "Technical-readiness evidence failed integrity validation."),
        RepositoryReadinessValidationException =>
            ("RepositoryReadinessEvidenceInvalid", "Technical-readiness evidence was invalid."),
        RepositoryReadinessNotAllowedException =>
            ("RepositoryReadinessNotAllowed", "Technical-readiness validation is not available for the current project state."),
        _ => ("RepositoryReadinessValidationFailed", "Technical-readiness validation failed safely.")
    };

    private static void Validate(RefreshRepositoryReadinessCommand command)
    {
        ValidateIdentity(command.TenantId, command.ActorUserId, command.ProjectId);
        if (command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty || command.ExpectedRepositoryBindingRevision <= 0 ||
            command.ExpectedExecutionProfileRevision <= 0)
            throw new RepositoryReadinessValidationException(
                "A current session, lease, operation ID, and expected repository/profile revisions are required.");
    }

    private static void ValidateIdentity(int tenantId, int actorUserId, int projectId)
    {
        if (tenantId <= 0 || actorUserId <= 0 || projectId <= 0)
            throw new RepositoryReadinessValidationException(
                "Tenant, actor, and project identities must be positive.");
    }

    private sealed record ValidationClaim(
        long OperationRecordId,
        Guid AttemptId,
        int AttemptNumber,
        DateTimeOffset StartedAtUtc,
        AuthorityPackage Package,
        RefreshRepositoryReadinessResult? Replay,
        Exception? TerminalizedFailure)
    {
        public bool RequiresFailureFinalization => Replay is null && TerminalizedFailure is null;

        public static ValidationClaim ForReplay(RefreshRepositoryReadinessResult replay) =>
            new(0, Guid.Empty, 0, default, null!, replay, null);

        public static ValidationClaim ForTerminalized(
            long operationRecordId,
            AttemptRow attempt,
            Exception failure) => new(
                operationRecordId,
                attempt.Id,
                attempt.AttemptNumber,
                attempt.StartedAtUtc,
                null!,
                null,
                failure);
    }

    private sealed record AuthorityPackage(
        RepositoryBindingSnapshot Binding,
        ProjectExecutionProfileSnapshot Profile,
        RepositoryReadinessAuthority Authority,
        SandboxPackage? Sandbox,
        ProvisioningSnapshot? ProvisioningSnapshot);

    private sealed record ProvisioningSnapshot(
        string ManifestJson,
        string ManifestSha256,
        string GitTreeId);

    private sealed record SandboxPackage(
        Guid AttemptId,
        Guid EvidenceManifestId,
        string ManifestJson,
        string ManifestSha256,
        string ContainerImageReference,
        string ContainerImageDigestSha256,
        DateTimeOffset CompletedAtUtc,
        string ProvisioningManifestJson,
        string ProvisioningManifestSha256,
        string ProvisioningGitTreeId,
        SandboxEvidenceManifest Manifest);

    private sealed record MaterializedValidation(
        RepositoryStateObservation Observation,
        BuildValidationRecord Build,
        TestValidationRecord Test,
        CodeIndexSnapshot Index,
        BuilderStableConfigurationEvidence BuilderConfiguration,
        RepositorySandboxQualificationEvidence Sandbox,
        RepositoryReadinessEvaluationResult Evaluation,
        SandboxStageEvidence RestoreStage,
        SandboxStageEvidence BuildStage,
        SandboxStageEvidence TestStage)
    {
        public DateTimeOffset PackageStartedAtUtc => Sandbox.ValidatedAtUtc
            .Subtract(TimeSpan.FromMilliseconds(
                RestoreStage.DurationMilliseconds + BuildStage.DurationMilliseconds + TestStage.DurationMilliseconds));
    }

    private sealed record AttemptAuthorityData(
        Guid RepositoryBindingId,
        long RepositoryBindingRevision,
        string BaselineCommit,
        Guid ProjectExecutionProfileId,
        long ProjectExecutionProfileRevision,
        string ProfileDefinitionId,
        int ProfileDescriptorRevision,
        string ProfileDescriptorSha256,
        string RestoreCommandSha256,
        string BuildCommandSha256,
        string TestCommandSha256,
        string ToolchainManifestId,
        string ToolchainManifestSha256,
        string ContainerImageDigest,
        string ContainerImageDigestSha256,
        string SandboxPolicyVersion,
        string SandboxPolicySha256,
        string OfflineFeedManifestSha256,
        string TemplateBundleSha256,
        Guid SandboxQualificationAttemptId,
        Guid SandboxEvidenceManifestId,
        string SandboxEvidenceManifestSha256);

    private sealed class ProjectRow
    {
        public int ProjectId { get; init; }
        public string ProjectLifecyclePhase { get; init; } = ProjectLifecyclePhases.Shaping;
    }

    private sealed class ClientOperationRow
    {
        public long Id { get; init; }
        public string PayloadHash { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }

    private sealed class AttemptRow
    {
        public Guid Id { get; init; }
        public int AttemptNumber { get; init; }
        public Guid RepositoryBindingId { get; init; }
        public long RepositoryBindingRevision { get; init; }
        public string BaselineCommit { get; init; } = string.Empty;
        public Guid ProjectExecutionProfileId { get; init; }
        public long ProjectExecutionProfileRevision { get; init; }
        public string ProfileDefinitionId { get; init; } = string.Empty;
        public int ProfileDescriptorRevision { get; init; }
        public string ProfileDescriptorSha256 { get; init; } = string.Empty;
        public string RestoreCommandSha256 { get; init; } = string.Empty;
        public string BuildCommandSha256 { get; init; } = string.Empty;
        public string TestCommandSha256 { get; init; } = string.Empty;
        public string ToolchainManifestId { get; init; } = string.Empty;
        public string ToolchainManifestSha256 { get; init; } = string.Empty;
        public string ContainerImageDigest { get; init; } = string.Empty;
        public string ContainerImageDigestSha256 { get; init; } = string.Empty;
        public string SandboxPolicyVersion { get; init; } = string.Empty;
        public string SandboxPolicySha256 { get; init; } = string.Empty;
        public string OfflineFeedManifestSha256 { get; init; } = string.Empty;
        public string TemplateBundleSha256 { get; init; } = string.Empty;
        public Guid SandboxQualificationAttemptId { get; init; }
        public Guid SandboxEvidenceManifestId { get; init; }
        public string SandboxEvidenceManifestSha256 { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string? FailureCode { get; init; }
        public string? FailureSummary { get; init; }
        public DateTimeOffset StartedAtUtc { get; init; }
    }

    private sealed class SandboxRow
    {
        public Guid AttemptId { get; init; }
        public string State { get; init; } = string.Empty;
        public bool? CleanupConfirmed { get; init; }
        public Guid RepositoryBindingId { get; init; }
        public long RepositoryBindingRevision { get; init; }
        public string BaselineCommit { get; init; } = string.Empty;
        public Guid ProjectExecutionProfileId { get; init; }
        public long ProjectExecutionProfileRevision { get; init; }
        public string? AttemptEvidenceManifestSha256 { get; init; }
        public Guid? EvidenceManifestId { get; init; }
        public string? ManifestJson { get; init; }
        public string? ManifestSha256 { get; init; }
        public string ContainerImageReference { get; init; } = string.Empty;
        public DateTimeOffset? CompletedAtUtc { get; init; }
        public string ProvisioningManifestJson { get; init; } = string.Empty;
        public string ProvisioningManifestSha256 { get; init; } = string.Empty;
        public string ProvisioningGitTreeId { get; init; } = string.Empty;
    }

    private sealed class StoredContextRow
    {
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
        public Guid RepositoryBindingId { get; init; }
        public long RepositoryBindingRevision { get; init; }
        public string BaselineCommit { get; init; } = string.Empty;
        public string HeadCommit { get; init; } = string.Empty;
        public string GitTreeId { get; init; } = string.Empty;
        public string DirtyState { get; init; } = string.Empty;
        public string RepositoryFingerprintSha256 { get; init; } = string.Empty;
        public string ObservationEvidenceSha256 { get; init; } = string.Empty;
        public Guid ProjectExecutionProfileId { get; init; }
        public long ProjectExecutionProfileRevision { get; init; }
        public string ProfileDefinitionId { get; init; } = string.Empty;
        public int ProfileDescriptorRevision { get; init; }
        public string ProfileDescriptorSha256 { get; init; } = string.Empty;
        public string RestoreCommandSha256 { get; init; } = string.Empty;
        public string BuildCommandSha256 { get; init; } = string.Empty;
        public string TestCommandSha256 { get; init; } = string.Empty;
        public string ToolchainManifestId { get; init; } = string.Empty;
        public string TemplateBundleSha256 { get; init; } = string.Empty;
        public Guid SandboxQualificationAttemptId { get; init; }
        public Guid SandboxEvidenceManifestId { get; init; }
        public string SandboxEvidenceManifestSha256 { get; init; } = string.Empty;
        public Guid ConfigurationId { get; init; }
        public long ConfigurationRevision { get; init; }
        public string ConfigurationState { get; init; } = string.Empty;
        public string ConfigurationSha256 { get; init; } = string.Empty;
    }

    private sealed class SandboxAttemptIdentity
    {
        public Guid Id { get; init; }
        public string State { get; init; } = string.Empty;
    }

    private sealed class DistributedLock(IDbConnection connection, string resource) : IDisposable
    {
        private IDbConnection? _connection = connection;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _connection, null);
            if (current is null)
                return;
            try
            {
                current.Execute(
                    "EXEC sys.sp_releaseapplock @Resource=@Resource, @LockOwner=N'Session';",
                    new { Resource = resource });
            }
            finally
            {
                current.Dispose();
            }
        }
    }
}
