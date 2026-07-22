using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
using IronDev.Data;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services.Sandbox;

public static class SandboxQualificationOperationKinds
{
    public const string Qualify = "QualifyProductionSandbox";
}

public sealed class SandboxQualificationForbiddenException()
    : Exception("Only a project owner or contributor can run sandbox qualification.")
{
    public const string ErrorCode = "sandbox_qualification_forbidden";
}

public sealed class SandboxQualificationNotAllowedException(string message) : Exception(message)
{
    public const string ErrorCode = "sandbox_qualification_not_allowed";
}

public sealed class SandboxQualificationStaleException()
    : Exception("The repository or execution-profile authority changed. Refresh before qualifying the sandbox.")
{
    public const string ErrorCode = "sandbox_qualification_stale";
}

public sealed class SandboxQualificationInProgressException()
    : Exception("A sandbox qualification operation is already in progress for this project.")
{
    public const string ErrorCode = "sandbox_qualification_in_progress";
}

public sealed class SandboxQualificationUnavailableException(SandboxCapability capability)
    : Exception(capability.Message)
{
    public const string ErrorCode = "sandbox_qualification_unavailable";
    public SandboxCapability Capability { get; } = capability;
}

public sealed class SandboxQualificationIntegrityException(string message) : Exception(message)
{
    public const string ErrorCode = "sandbox_qualification_integrity_failed";
}

public sealed class SandboxQualificationValidationException(string message) : Exception(message)
{
    public const string ErrorCode = "sandbox_qualification_invalid";
}

public sealed class WorkbenchSandboxQualificationService : IWorkbenchSandboxQualificationService
{
    private const string Route = "/api/workbench/projects/{projectId}/repository/sandbox-qualifications";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connections;
    private readonly ISandboxRuntimePolicyCatalog _policies;
    private readonly ISandboxExecutionService _sandbox;
    private readonly ISandboxSourceSnapshotBuilder _snapshots;
    private readonly string _snapshotRoot;
    private readonly string _evidenceRoot;

    public WorkbenchSandboxQualificationService(
        IDbConnectionFactory connections,
        ISandboxRuntimePolicyCatalog policies,
        ISandboxExecutionService sandbox,
        ISandboxSourceSnapshotBuilder snapshots,
        IConfiguration configuration)
    {
        _connections = connections;
        _policies = policies;
        _sandbox = sandbox;
        _snapshots = snapshots;
        _snapshotRoot = configuration["WorkbenchProductionSandbox:SourceSnapshotRoot"]?.Trim() ?? string.Empty;
        _evidenceRoot = configuration["WorkbenchProductionSandbox:EvidenceRoot"]?.Trim() ?? string.Empty;
    }

    public async Task<WorkbenchSandboxContext> GetContextAsync(
        GetWorkbenchSandboxContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(query.TenantId, query.ActorUserId, query.ProjectId);
        using var connection = _connections.CreateConnection();
        connection.Open();
        if (!await CanAccessProjectAsync(
                connection, null, query.TenantId, query.ActorUserId, query.ProjectId,
                requireContributor: false, cancellationToken).ConfigureAwait(false))
            throw new WorkbenchProjectNotAccessibleException();

        var project = await ReadProjectAsync(
            connection, null, query.TenantId, query.ProjectId, cancellationToken).ConfigureAwait(false)
            ?? throw new WorkbenchProjectNotAccessibleException();
        var binding = await ReadBindingAsync(
            connection, null, query.TenantId, query.ProjectId, lockRows: false, cancellationToken).ConfigureAwait(false);
        var profile = binding is null
            ? null
            : await ReadProfileAsync(
                connection, null, query.TenantId, query.ProjectId, binding.Id,
                lockRows: false, cancellationToken).ConfigureAwait(false);
        var latest = await ReadLatestAttemptAsync(
            connection, null, query.TenantId, query.ProjectId, cancellationToken).ConfigureAwait(false);

        WorkbenchSandboxRepositoryAuthority? authority = null;
        SandboxCapability capability;
        if (binding is null || profile is null ||
            !string.Equals(binding.BindingState, RepositoryBindingStates.Qualified, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(binding.BaselineCommit))
        {
            capability = Unavailable(
                "SandboxRepositoryNotQualified",
                "Provision and qualify the repository before running sandbox qualification.");
        }
        else
        {
            authority = new WorkbenchSandboxRepositoryAuthority(
                binding.Id,
                binding.Revision,
                profile.Id,
                profile.Revision,
                binding.BaselineCommit);
            var resolution = _policies.Resolve(ToBinding(profile));
            capability = resolution.IsAvailable
                ? await CombineRuntimeCapabilityAsync(resolution, cancellationToken).ConfigureAwait(false)
                : resolution.Capability;
        }

        return new WorkbenchSandboxContext(
            query.ProjectId,
            project.ProjectLifecyclePhase,
            project.ExecutionReadiness,
            authority,
            capability,
            latest is null ? null : ToSnapshot(latest, query.ActorUserId));
    }

    public async Task<WorkbenchSandboxQualificationResult> StartAsync(
        StartWorkbenchSandboxQualificationCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        await EnsurePreLockAccessAsync(command, cancellationToken).ConfigureAwait(false);
        var key = $"{command.TenantId}:{command.ProjectId}";
        var localGate = LocalLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await localGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new SandboxQualificationInProgressException();
        try
        {
            using var distributed = await AcquireDistributedLockAsync(command, cancellationToken).ConfigureAwait(false);
            var claim = await ClaimAsync(command, cancellationToken).ConfigureAwait(false);
            if (claim.Replay is not null)
                return claim.Replay with { IsReplay = true };

            SandboxSourceSnapshot? snapshot = null;
            SandboxExecutionResult? execution = null;
            Exception? executionFailure = null;
            var snapshotCleaned = false;
            var sandboxExecutionAttempted = false;
            var pendingAttemptRecovered = false;
            var completedEvidenceRecovered = false;
            try
            {
                var snapshotRequest = new SandboxSourceSnapshotRequest(
                    claim.AttemptId,
                    claim.Binding.CanonicalPath,
                    claim.Binding.BaselineCommit!,
                    claim.Receipt.GitTreeId,
                    claim.Receipt.ManifestJson,
                    claim.Receipt.ManifestSha256,
                    _snapshotRoot);
                var snapshotIdentity = _snapshots.Describe(snapshotRequest);
                if (claim.IsExistingPending)
                {
                    execution = await _sandbox.TryRecoverCompletedAsync(
                        new SandboxCompletedEvidenceRecoveryRequest
                        {
                            ExecutionId = claim.AttemptId,
                            ProjectId = command.ProjectId,
                            RepositoryBindingId = claim.Binding.Id,
                            RepositoryBindingRevision = claim.Binding.Revision,
                            BaselineCommit = claim.Binding.BaselineCommit!,
                            WorktreeFingerprint = snapshotIdentity.WorktreeFingerprint,
                            ProjectExecutionProfileId = claim.Profile.Id,
                            ProjectExecutionProfileRevision = claim.Profile.Revision,
                            ProfileDefinitionId = claim.Policy.ProfileDefinitionId,
                            ProfileDescriptorRevision = claim.Policy.ProfileDescriptorRevision,
                            DescriptorSha256 = claim.Policy.DescriptorSha256,
                            TemplateBundleSha256 = claim.Policy.TemplateBundleSha256,
                            ToolchainManifestId = claim.Policy.ToolchainManifestId,
                            ContainerImageReference = claim.Policy.ContainerImageReference,
                            SandboxPolicyVersion = claim.Policy.PolicyVersion,
                            SandboxPolicySha256 = claim.Policy.PolicySha256,
                            TrustedSupervisorVersion = claim.Policy.TrustedSupervisorVersion,
                            TrustedSupervisorSha256 = claim.Policy.TrustedSupervisorSha256,
                            OfflineFeedManifestSha256 = claim.Policy.OfflineFeedManifestSha256,
                            EvidenceOutputPath = EvidencePath(command, claim.AttemptId),
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                completedEvidenceRecovered = execution is not null;
                if (completedEvidenceRecovered)
                {
                    snapshotCleaned = _snapshots.CleanupRecovered(
                        new SandboxSourceSnapshotRecoveryRequest(
                            claim.AttemptId,
                            claim.Receipt.ManifestSha256,
                            _snapshotRoot));
                }
                if (execution is null)
                {
                    sandboxExecutionAttempted = false;
                    if (claim.IsExistingPending)
                    {
                        pendingAttemptRecovered = true;
                        var runtimeCleaned = false;
                        try
                        {
                            var cleanup = await _sandbox.RecoverExecutionAsync(
                                new SandboxExecutionCleanupRequest(
                                    ExecutionId: claim.AttemptId,
                                    SandboxPolicySha256: claim.Policy.PolicySha256,
                                    TrustedSupervisorVersion: claim.Policy.TrustedSupervisorVersion,
                                    TrustedSupervisorSha256: claim.Policy.TrustedSupervisorSha256,
                                    EvidenceOutputPath: EvidencePath(command, claim.AttemptId)),
                                cancellationToken).ConfigureAwait(false);
                            runtimeCleaned = cleanup.CleanupConfirmed;
                        }
                        catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
                        {
                            runtimeCleaned = false;
                        }
                        var recoveredSnapshotCleaned = _snapshots.CleanupRecovered(
                            new SandboxSourceSnapshotRecoveryRequest(
                                claim.AttemptId,
                                claim.Receipt.ManifestSha256,
                                _snapshotRoot));
                        snapshotCleaned = runtimeCleaned && recoveredSnapshotCleaned;
                    }
                    else
                    {
                        snapshot = await _snapshots.CreateOrRecoverAsync(
                            snapshotRequest,
                            cancellationToken).ConfigureAwait(false);
                        if (!string.Equals(
                                snapshot.WorktreeFingerprint,
                                snapshotIdentity.WorktreeFingerprint,
                                StringComparison.Ordinal))
                            throw new SandboxQualificationIntegrityException(
                                "The sandbox source snapshot identity drifted during materialization.");
                        sandboxExecutionAttempted = true;
                        execution = await _sandbox.ExecuteAsync(
                            new SandboxExecutionRequest
                            {
                                ExecutionId = claim.AttemptId,
                                ProjectId = command.ProjectId,
                                RepositoryBindingId = claim.Binding.Id,
                                RepositoryBindingRevision = claim.Binding.Revision,
                                BaselineCommit = claim.Binding.BaselineCommit!,
                                WorktreeFingerprint = snapshot.WorktreeFingerprint,
                                ProjectExecutionProfileId = claim.Profile.Id,
                                ProjectExecutionProfileRevision = claim.Profile.Revision,
                                SourceSnapshotPath = snapshot.SourcePath,
                                EvidenceOutputPath = EvidencePath(command, claim.AttemptId),
                                Policy = claim.Policy
                            },
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
            {
                if (claim.IsExistingPending)
                    throw new SandboxQualificationInProgressException();
                executionFailure = exception;
            }
            finally
            {
                if (!pendingAttemptRecovered && !completedEvidenceRecovered)
                    snapshotCleaned = snapshot is null || _snapshots.Cleanup(snapshot);
            }

            if (completedEvidenceRecovered && !snapshotCleaned)
                throw new SandboxQualificationInProgressException();

            if (pendingAttemptRecovered)
            {
                if (!snapshotCleaned)
                    throw new SandboxQualificationInProgressException();
                return await CompleteAsync(
                    command,
                    claim,
                    SandboxQualificationStates.Recovered,
                    SandboxReasonCodes.HostRestartRecovered,
                    "The abandoned sandbox operation was recovered without rerunning project code.",
                    execution: null,
                    cleanupConfirmed: snapshotCleaned,
                    CancellationToken.None).ConfigureAwait(false);
            }

            if (execution is not null)
            {
                if (!execution.CleanedUp || !snapshotCleaned)
                    throw new SandboxQualificationInProgressException();
                return await FinalizeAsync(
                    command, claim, execution, snapshotCleaned, CancellationToken.None).ConfigureAwait(false);
            }

            if (sandboxExecutionAttempted)
            {
                var runtimeCleaned = false;
                try
                {
                    var cleanup = await _sandbox.RecoverExecutionAsync(
                        new SandboxExecutionCleanupRequest(
                            ExecutionId: claim.AttemptId,
                            SandboxPolicySha256: claim.Policy.PolicySha256,
                            TrustedSupervisorVersion: claim.Policy.TrustedSupervisorVersion,
                            TrustedSupervisorSha256: claim.Policy.TrustedSupervisorSha256,
                            EvidenceOutputPath: EvidencePath(command, claim.AttemptId)),
                        CancellationToken.None).ConfigureAwait(false);
                    runtimeCleaned = cleanup.CleanupConfirmed;
                }
                catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
                {
                    runtimeCleaned = false;
                }

                var recoveredSnapshotCleaned = false;
                try
                {
                    recoveredSnapshotCleaned = _snapshots.CleanupRecovered(
                        new SandboxSourceSnapshotRecoveryRequest(
                            claim.AttemptId,
                            claim.Receipt.ManifestSha256,
                            _snapshotRoot));
                }
                catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
                {
                    recoveredSnapshotCleaned = false;
                }
                snapshotCleaned = snapshotCleaned || recoveredSnapshotCleaned;
                if (!runtimeCleaned || !snapshotCleaned)
                    throw new SandboxQualificationInProgressException();
            }

            if (!snapshotCleaned || executionFailure is SandboxSourceSnapshotCleanupException)
                throw new SandboxQualificationInProgressException();

            return await FinalizeFailureAsync(
                command,
                claim,
                executionFailure ?? new SandboxQualificationIntegrityException(
                    "Sandbox qualification stopped without an execution result."),
                cleanupConfirmed: true,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            localGate.Release();
        }
    }

    private async Task<QualificationClaim> ClaimAsync(
        StartWorkbenchSandboxQualificationCommand command,
        CancellationToken cancellationToken)
    {
        var payloadHash = PayloadHash(command);
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(connection, transaction, command.TenantId, command.ActorUserId,
                    command.ProjectId, requireContributor: false, cancellationToken).ConfigureAwait(false))
                throw new WorkbenchProjectNotAccessibleException();
            if (!await CanAccessProjectAsync(connection, transaction, command.TenantId, command.ActorUserId,
                    command.ProjectId, requireContributor: true, cancellationToken).ConfigureAwait(false))
                throw new SandboxQualificationForbiddenException();
            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command, cancellationToken).ConfigureAwait(false))
                throw new WorkbenchLeaseFenceException();

            var operation = await ReadOperationAsync(connection, transaction, command, cancellationToken)
                .ConfigureAwait(false);
            if (operation is not null)
            {
                if (operation.ActorUserId != command.ActorUserId ||
                    !string.Equals(operation.PayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new ProjectStartOperationMismatchException();
                if (operation.Status == "Completed")
                {
                    var replay = ReadReplay(operation);
                    transaction.Commit();
                    return QualificationClaim.ForReplay(replay);
                }
                if (operation.Status != "Pending")
                    throw new SandboxQualificationIntegrityException(
                        "The stored sandbox operation has an unsupported state.");
                var attempt = await ReadAttemptByOperationAsync(
                    connection, transaction, operation.Id, cancellationToken).ConfigureAwait(false)
                    ?? throw new SandboxQualificationIntegrityException(
                        "The pending sandbox operation has no durable attempt.");
                var hydrated = await HydrateClaimAsync(
                    connection, transaction, command, operation, attempt, cancellationToken).ConfigureAwait(false);
                transaction.Commit();
                return hydrated;
            }

            var anotherAttemptIsRunning = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.SandboxQualificationAttempts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND State=N'Running';
                """,
                new { command.TenantId, command.ProjectId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
            if (anotherAttemptIsRunning)
                throw new SandboxQualificationInProgressException();

            var binding = await ReadBindingAsync(
                connection, transaction, command.TenantId, command.ProjectId,
                lockRows: true, cancellationToken).ConfigureAwait(false)
                ?? throw new SandboxQualificationNotAllowedException(
                    "A qualified repository is required before sandbox qualification.");
            var profile = await ReadProfileAsync(
                connection, transaction, command.TenantId, command.ProjectId, binding.Id,
                lockRows: true, cancellationToken).ConfigureAwait(false)
                ?? throw new SandboxQualificationNotAllowedException(
                    "A current execution profile is required before sandbox qualification.");
            if (binding.Revision != command.ExpectedRepositoryBindingRevision ||
                profile.Revision != command.ExpectedExecutionProfileRevision)
                throw new SandboxQualificationStaleException();
            if (!string.Equals(binding.BindingState, RepositoryBindingStates.Qualified, StringComparison.Ordinal) ||
                !IsLowerHex(binding.BaselineCommit ?? string.Empty, 40) ||
                profile.RepositoryBindingId != binding.Id)
                throw new SandboxQualificationNotAllowedException(
                    "Only a qualified repository with an immutable baseline can enter sandbox qualification.");

            var receipt = await ReadProvisioningReceiptAsync(
                connection, transaction, command, binding, profile, cancellationToken).ConfigureAwait(false)
                ?? throw new SandboxQualificationIntegrityException(
                    "The qualified repository has no matching immutable provisioning receipt.");
            var resolution = _policies.Resolve(ToBinding(profile));
            if (!resolution.IsAvailable || resolution.Policy is null)
                throw new SandboxQualificationUnavailableException(resolution.Capability);
            var effectiveCapability = await CombineRuntimeCapabilityAsync(resolution, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    effectiveCapability.State,
                    SandboxCapabilityStates.Available,
                    StringComparison.Ordinal))
                throw new SandboxQualificationUnavailableException(effectiveCapability);
            ValidateRoots();

            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: cancellationToken));
            var operationRecordId = await connection.QuerySingleAsync<long>(new CommandDefinition(
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
                    OperationKind = SandboxQualificationOperationKinds.Qualify,
                    ResourceScopeId = ResourceScope(command.ProjectId),
                    command.ClientOperationId,
                    PayloadHash = payloadHash,
                    command.ProjectId,
                    command.WorkbenchSessionId
                },
                transaction,
                cancellationToken: cancellationToken));
            var attemptNumber = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT COALESCE(MAX(AttemptNumber), 0) + 1
                FROM dbo.SandboxQualificationAttempts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId
                  AND RepositoryBindingId=@RepositoryBindingId;
                """,
                new { command.TenantId, command.ProjectId, RepositoryBindingId = binding.Id },
                transaction,
                cancellationToken: cancellationToken));
            var attemptId = Guid.NewGuid();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.SandboxQualificationAttempts
                    (Id, TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                     RepositoryProvisioningReceiptId,
                     ClientOperationRecordId, ClientOperationId, ActorUserId,
                     ClientOperationKind, ClientOperationResourceScopeId,
                     WorkbenchSessionId, LeaseEpoch, AttemptNumber,
                     ExpectedBindingRevision, ExpectedExecutionProfileRevision,
                     BaselineCommit, SourceManifestSha256, SourceGitTreeId,
                     ProfileDefinitionId, ProfileDescriptorRevision,
                     DescriptorSha256, TemplateBundleSha256, ToolchainManifestId,
                     ContainerImageDigest, OfflineFeedManifestSha256,
                     SandboxPolicyVersion, SandboxPolicySha256,
                     TrustedSupervisorVersion, TrustedSupervisorSha256,
                     State, StartedAtUtc)
                VALUES
                    (@Id, @TenantId, @ProjectId, @RepositoryBindingId, @ProjectExecutionProfileId,
                     @RepositoryProvisioningReceiptId,
                     @ClientOperationRecordId, @ClientOperationId, @ActorUserId,
                     @ClientOperationKind, @ClientOperationResourceScopeId,
                     @WorkbenchSessionId, @LeaseEpoch, @AttemptNumber,
                     @ExpectedBindingRevision, @ExpectedExecutionProfileRevision,
                     @BaselineCommit, @SourceManifestSha256, @SourceGitTreeId,
                     @ProfileDefinitionId, @ProfileDescriptorRevision,
                     @DescriptorSha256, @TemplateBundleSha256, @ToolchainManifestId,
                     @ContainerImageDigest, @OfflineFeedManifestSha256,
                     @SandboxPolicyVersion, @SandboxPolicySha256,
                     @TrustedSupervisorVersion, @TrustedSupervisorSha256,
                     N'Running', @StartedAtUtc);
                """,
                new
                {
                    Id = attemptId,
                    command.TenantId,
                    command.ProjectId,
                    RepositoryBindingId = binding.Id,
                    ProjectExecutionProfileId = profile.Id,
                    RepositoryProvisioningReceiptId = receipt.Id,
                    ClientOperationRecordId = operationRecordId,
                    command.ClientOperationId,
                    command.ActorUserId,
                    ClientOperationKind = SandboxQualificationOperationKinds.Qualify,
                    ClientOperationResourceScopeId = ResourceScope(command.ProjectId),
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    AttemptNumber = attemptNumber,
                    ExpectedBindingRevision = binding.Revision,
                    ExpectedExecutionProfileRevision = profile.Revision,
                    BaselineCommit = binding.BaselineCommit,
                    SourceManifestSha256 = receipt.ManifestSha256,
                    SourceGitTreeId = receipt.GitTreeId,
                    profile.ProfileDefinitionId,
                    profile.ProfileDescriptorRevision,
                    profile.DescriptorSha256,
                    profile.TemplateBundleSha256,
                    profile.ToolchainManifestId,
                    ContainerImageDigest = resolution.Policy.ContainerImageReference,
                    resolution.Policy.OfflineFeedManifestSha256,
                    SandboxPolicyVersion = resolution.Policy.PolicyVersion,
                    SandboxPolicySha256 = resolution.Policy.PolicySha256,
                    resolution.Policy.TrustedSupervisorVersion,
                    resolution.Policy.TrustedSupervisorSha256,
                    StartedAtUtc = now
                },
                transaction,
                cancellationToken: cancellationToken));
            await InsertOutboxAsync(
                connection, transaction, command, "SandboxQualificationStarted",
                SandboxCanonicalJson.Serialize(new
                {
                    schemaVersion = 1,
                    command.ProjectId,
                    attemptId,
                    attemptNumber,
                    repositoryBindingId = binding.Id,
                    repositoryBindingRevision = binding.Revision,
                    projectExecutionProfileId = profile.Id,
                    projectExecutionProfileRevision = profile.Revision,
                    sandboxPolicySha256 = resolution.Policy.PolicySha256
                }),
                now,
                cancellationToken).ConfigureAwait(false);
            await InsertAttributionAsync(
                connection, transaction, command, attemptId, "Attempted", 202, now, cancellationToken)
                .ConfigureAwait(false);
            transaction.Commit();
            return new QualificationClaim(
                operationRecordId,
                attemptId,
                attemptNumber,
                now,
                binding,
                profile,
                receipt,
                resolution.Policy,
                IsExistingPending: false,
                Replay: null);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<QualificationClaim> HydrateClaimAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StartWorkbenchSandboxQualificationCommand command,
        ClientOperationRow operation,
        AttemptRow attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.ClientOperationId != command.ClientOperationId ||
            attempt.ExpectedBindingRevision != command.ExpectedRepositoryBindingRevision ||
            attempt.ExpectedExecutionProfileRevision != command.ExpectedExecutionProfileRevision ||
            attempt.State != SandboxQualificationStates.Running)
            throw new ProjectStartOperationMismatchException();
        var binding = await ReadBindingAsync(connection, transaction, command.TenantId, command.ProjectId,
            lockRows: true, cancellationToken).ConfigureAwait(false)
            ?? throw new SandboxQualificationIntegrityException("The sandbox repository authority disappeared.");
        var profile = await ReadProfileAsync(connection, transaction, command.TenantId, command.ProjectId,
                binding.Id, lockRows: true, cancellationToken).ConfigureAwait(false)
            ?? throw new SandboxQualificationIntegrityException("The sandbox profile authority disappeared.");
        if (binding.Id != attempt.RepositoryBindingId || profile.Id != attempt.ProjectExecutionProfileId)
            throw new SandboxQualificationIntegrityException(
                "The durable sandbox repository or profile identity disappeared during recovery.");
        var receipt = await ReadProvisioningReceiptForAttemptAsync(
                connection, transaction, command, attempt, cancellationToken).ConfigureAwait(false)
            ?? throw new SandboxQualificationIntegrityException(
                "The sandbox repository receipt disappeared during recovery.");
        ValidateRoots();
        var recoveryBinding = binding with
        {
            Revision = attempt.ExpectedBindingRevision,
            BindingState = RepositoryBindingStates.Qualified,
            BaselineCommit = attempt.BaselineCommit
        };
        var recoveryProfile = profile with
        {
            Revision = attempt.ExpectedExecutionProfileRevision,
            ProfileDefinitionId = attempt.ProfileDefinitionId,
            ProfileDescriptorRevision = attempt.ProfileDescriptorRevision,
            DescriptorSha256 = attempt.DescriptorSha256,
            TemplateBundleSha256 = attempt.TemplateBundleSha256,
            ToolchainManifestId = attempt.ToolchainManifestId,
            ExecutionImageReference = attempt.ContainerImageDigest
        };
        return new QualificationClaim(
            operation.Id,
            attempt.Id,
            attempt.AttemptNumber,
            attempt.StartedAtUtc,
            recoveryBinding,
            recoveryProfile,
            receipt,
            RecoveryPolicy(attempt, recoveryProfile),
            IsExistingPending: true,
            Replay: null);
    }

    private async Task<WorkbenchSandboxQualificationResult> FinalizeAsync(
        StartWorkbenchSandboxQualificationCommand command,
        QualificationClaim claim,
        SandboxExecutionResult execution,
        bool snapshotCleaned,
        CancellationToken cancellationToken)
    {
        if (!execution.CleanedUp || !snapshotCleaned)
            throw new SandboxQualificationInProgressException();
        var passed = execution.Status == SandboxExecutionStatus.Succeeded &&
                      execution.CleanedUp && snapshotCleaned;
        var state = passed ? SandboxQualificationStates.Passed :
            execution.Status == SandboxExecutionStatus.Cancelled
                ? SandboxQualificationStates.Cancelled
                : SandboxQualificationStates.Failed;
        var failureCode = passed ? null : !snapshotCleaned
            ? SandboxReasonCodes.CleanupFailed
            : execution.ReasonCode;
        var safeSummary = passed
            ? "The production sandbox qualification completed with teardown confirmed."
            : !snapshotCleaned
                ? "Sandbox execution ended, but its owned source snapshot could not be cleaned up."
                : execution.SafeSummary;
        return await CompleteAsync(
            command,
            claim,
            state,
            failureCode,
            safeSummary,
            execution,
            execution.CleanedUp && snapshotCleaned,
            cancellationToken).ConfigureAwait(false);
    }

    private Task<WorkbenchSandboxQualificationResult> FinalizeFailureAsync(
        StartWorkbenchSandboxQualificationCommand command,
        QualificationClaim claim,
        Exception exception,
        bool cleanupConfirmed,
        CancellationToken cancellationToken)
    {
        if (!cleanupConfirmed)
            throw new SandboxQualificationInProgressException();
        var cancelled = exception is OperationCanceledException;
        var reason = !cleanupConfirmed
            ? SandboxReasonCodes.CleanupFailed
            : exception switch
        {
            HcsContainerRuntimeException runtime => runtime.ReasonCode,
            SandboxContractValidationException => SandboxReasonCodes.PolicyIntegrityFailed,
            _ => SandboxReasonCodes.ExecutionRejected
        };
        var summary = !cleanupConfirmed
            ? "Sandbox qualification stopped, but teardown of every owned resource could not be confirmed."
            : cancelled
            ? "Sandbox qualification was cancelled and cleanup was attempted."
            : "Sandbox qualification stopped safely before it could produce passing evidence.";
        return CompleteAsync(
            command,
            claim,
            cancelled ? SandboxQualificationStates.Cancelled : SandboxQualificationStates.Failed,
            reason,
            summary,
            execution: null,
            cleanupConfirmed,
            cancellationToken);
    }

    private async Task<WorkbenchSandboxQualificationResult> CompleteAsync(
        StartWorkbenchSandboxQualificationCommand command,
        QualificationClaim claim,
        string state,
        string? failureCode,
        string safeSummary,
        SandboxExecutionResult? execution,
        bool cleanupConfirmed,
        CancellationToken cancellationToken)
    {
        if (!cleanupConfirmed)
            throw new SandboxQualificationInProgressException();
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var operation = await ReadOperationAsync(connection, transaction, command, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new SandboxQualificationIntegrityException("The sandbox operation disappeared.");
            if (operation.Status == "Completed")
            {
                var replay = ReadReplay(operation);
                transaction.Commit();
                return replay with { IsReplay = true };
            }
            if (operation.Id != claim.OperationRecordId || operation.Status != "Pending" ||
                !string.Equals(operation.PayloadHash, PayloadHash(command), StringComparison.Ordinal))
                throw new SandboxQualificationIntegrityException("The sandbox operation cannot be finalized.");
            var attempt = await ReadAttemptByOperationAsync(
                connection, transaction, operation.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new SandboxQualificationIntegrityException("The sandbox attempt disappeared.");
            if (attempt.Id != claim.AttemptId || attempt.State != SandboxQualificationStates.Running ||
                !string.Equals(attempt.SandboxPolicySha256, claim.Policy.PolicySha256, StringComparison.Ordinal) ||
                !string.Equals(attempt.TrustedSupervisorVersion, claim.Policy.TrustedSupervisorVersion, StringComparison.Ordinal) ||
                !string.Equals(attempt.TrustedSupervisorSha256, claim.Policy.TrustedSupervisorSha256, StringComparison.Ordinal))
                throw new SandboxQualificationIntegrityException("The sandbox attempt authority drifted.");

            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: cancellationToken));
            Guid? evidenceId = null;
            if (execution is not null)
            {
                if (!string.Equals(
                        execution.EvidenceManifestSha256,
                        SandboxCanonicalJson.Sha256(execution.EvidenceManifestJson),
                        StringComparison.Ordinal) ||
                    execution.EvidenceManifest.ExecutionId != claim.AttemptId ||
                    execution.EvidenceManifest.RepositoryBindingId != claim.Binding.Id ||
                    execution.EvidenceManifest.RepositoryBindingRevision != claim.Binding.Revision ||
                    execution.EvidenceManifest.ProjectExecutionProfileId != claim.Profile.Id ||
                    execution.EvidenceManifest.ProjectExecutionProfileRevision != claim.Profile.Revision ||
                    !string.Equals(
                        execution.EvidenceManifest.TrustedSupervisorVersion,
                        claim.Policy.TrustedSupervisorVersion,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        execution.EvidenceManifest.TrustedSupervisorSha256,
                        claim.Policy.TrustedSupervisorSha256,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        execution.EvidenceManifest.Inspection.TrustedSupervisorVersion,
                        claim.Policy.TrustedSupervisorVersion,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        execution.EvidenceManifest.Inspection.TrustedSupervisorSha256,
                        claim.Policy.TrustedSupervisorSha256,
                        StringComparison.Ordinal))
                    throw new SandboxQualificationIntegrityException(
                        "The sandbox evidence manifest failed exact authority verification.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.SandboxQualificationAttempts
                SET State=@State, FailureCode=@FailureCode, FailureSummary=@FailureSummary,
                    EvidenceManifestSha256=@EvidenceManifestSha256,
                    CleanupConfirmed=@CleanupConfirmed, CompletedAtUtc=@CompletedAtUtc
                WHERE Id=@AttemptId AND TenantId=@TenantId AND ProjectId=@ProjectId
                  AND State=N'Running';
                """,
                new
                {
                    State = state,
                    FailureCode = failureCode,
                    FailureSummary = failureCode is null ? null : safeSummary,
                    EvidenceManifestSha256 = execution?.EvidenceManifestSha256,
                    CleanupConfirmed = cleanupConfirmed,
                    CompletedAtUtc = now,
                    AttemptId = claim.AttemptId,
                    command.TenantId,
                    command.ProjectId
                },
                transaction,
                cancellationToken: cancellationToken));

            if (execution is not null)
            {
                evidenceId = Guid.NewGuid();
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT dbo.SandboxEvidenceManifests
                        (Id, TenantId, ProjectId, SandboxQualificationAttemptId,
                         RepositoryBindingId, RepositoryBindingRevision,
                         ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                         ActorUserId, SchemaVersion, Passed, ManifestJson,
                         ManifestSha256, CreatedAtUtc)
                    VALUES
                        (@Id, @TenantId, @ProjectId, @SandboxQualificationAttemptId,
                         @RepositoryBindingId, @RepositoryBindingRevision,
                         @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                         @ActorUserId, 1, @Passed, @ManifestJson,
                         @ManifestSha256, @CreatedAtUtc);
                    """,
                    new
                    {
                        Id = evidenceId,
                        command.TenantId,
                        command.ProjectId,
                        SandboxQualificationAttemptId = claim.AttemptId,
                        RepositoryBindingId = claim.Binding.Id,
                        RepositoryBindingRevision = claim.Binding.Revision,
                        ProjectExecutionProfileId = claim.Profile.Id,
                        ProjectExecutionProfileRevision = claim.Profile.Revision,
                        command.ActorUserId,
                        Passed = state == SandboxQualificationStates.Passed,
                        ManifestJson = execution.EvidenceManifestJson,
                        ManifestSha256 = execution.EvidenceManifestSha256,
                        CreatedAtUtc = now
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            var capability = state == SandboxQualificationStates.Passed
                ? new SandboxCapability(
                    SandboxCapabilityStates.Available,
                    SandboxReasonCodes.Ready,
                    "The production sandbox qualification evidence passed for this exact authority.",
                    claim.Policy.PolicyVersion,
                    claim.Policy.PolicySha256)
                : new SandboxCapability(
                    SandboxCapabilityStates.Unavailable,
                    failureCode ?? SandboxReasonCodes.ExecutionRejected,
                    safeSummary,
                    claim.Policy.PolicyVersion,
                    claim.Policy.PolicySha256);
            var snapshot = new SandboxQualificationAttemptSnapshot(
                claim.AttemptId,
                command.ClientOperationId,
                state,
                claim.Binding.Id,
                claim.Binding.Revision,
                claim.Profile.Id,
                claim.Profile.Revision,
                claim.Binding.BaselineCommit!,
                claim.StartedAtUtc,
                now,
                execution?.EvidenceManifestSha256,
                failureCode,
                safeSummary,
                CanRecover: false);
            var result = new WorkbenchSandboxQualificationResult(
                command.ProjectId,
                command.ClientOperationId,
                IsReplay: false,
                capability,
                snapshot);
            var resultJson = SandboxCanonicalJson.Serialize(result);
            var resultHash = SandboxCanonicalJson.Sha256(resultJson);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ClientOperations
                SET Status=N'Completed', CanonicalResultJson=@ResultJson, ResultHash=@ResultHash,
                    ResultSandboxQualificationAttemptId=@AttemptId,
                    ResultSandboxEvidenceManifestId=@EvidenceId,
                    CompletedAtUtc=@CompletedAtUtc
                WHERE Id=@OperationRecordId AND Status=N'Pending';
                """,
                new
                {
                    ResultJson = resultJson,
                    ResultHash = resultHash,
                    AttemptId = claim.AttemptId,
                    EvidenceId = evidenceId,
                    CompletedAtUtc = now,
                    OperationRecordId = claim.OperationRecordId
                },
                transaction,
                cancellationToken: cancellationToken));
            await InsertOutboxAsync(
                connection,
                transaction,
                command,
                state == SandboxQualificationStates.Passed
                    ? "SandboxQualificationPassed"
                    : "SandboxQualificationFailed",
                SandboxCanonicalJson.Serialize(new
                {
                    schemaVersion = 1,
                    command.ProjectId,
                    attemptId = claim.AttemptId,
                    state,
                    evidenceManifestSha256 = execution?.EvidenceManifestSha256,
                    cleanupConfirmed,
                    executionReadiness = ProjectExecutionReadinessStates.NotConfigured
                }),
                now,
                cancellationToken).ConfigureAwait(false);
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
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<SandboxCapability> CombineRuntimeCapabilityAsync(
        SandboxPolicyResolution resolution,
        CancellationToken cancellationToken)
    {
        var runtime = await _sandbox.GetCapabilityAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(runtime.State, SandboxCapabilityStates.Available, StringComparison.Ordinal)
            ? resolution.Capability
            : runtime with
            {
                PolicyVersion = resolution.Policy!.PolicyVersion,
                PolicySha256 = resolution.Policy.PolicySha256
            };
    }

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

    private static async Task<bool> ValidateAndRenewLeaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StartWorkbenchSandboxQualificationCommand command,
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
               AND member.ProjectRole IN (N'Owner', N'Contributor')
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=lease.TenantId AND tenantMember.UserId=@ActorUserId
            INNER JOIN dbo.Users actor ON actor.Id=@ActorUserId AND actor.IsActive=1
            WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
              AND lease.WorkbenchSessionId=@WorkbenchSessionId AND lease.LeaseEpoch=@LeaseEpoch
              AND lease.HolderActorUserId=@ActorUserId AND lease.RevokedAtUtc IS NULL
              AND lease.ExpiresAtUtc > SYSUTCDATETIME();
            """,
            command,
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;

    private static Task<ProjectRow?> ReadProjectAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<ProjectRow>(new CommandDefinition(
            """
            SELECT project.Id AS ProjectId,
                   COALESCE(lifecycle.Phase, N'Shaping') AS ProjectLifecyclePhase,
                   COALESCE(readiness.ExecutionReadiness, N'NotConfigured') AS ExecutionReadiness
            FROM dbo.Projects project
            OUTER APPLY
            (
                SELECT TOP (1) value.Phase
                FROM dbo.ProjectLifecyclePhases value
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC
            ) lifecycle
            OUTER APPLY
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
        Guid repositoryBindingId,
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
            new { TenantId = tenantId, ProjectId = projectId, RepositoryBindingId = repositoryBindingId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<ProvisioningReceiptRow?> ReadProvisioningReceiptAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StartWorkbenchSandboxQualificationCommand command,
        RepositoryBindingSnapshot binding,
        ProjectExecutionProfileSnapshot profile,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<ProvisioningReceiptRow>(new CommandDefinition(
            """
            SELECT TOP (1) Id, BaselineCommit, ManifestSha256, GitTreeId, ManifestJson
            FROM dbo.RepositoryProvisioningReceipts WITH (HOLDLOCK)
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
              AND RepositoryBindingId=@RepositoryBindingId
              AND ProjectExecutionProfileId=@ProjectExecutionProfileId
              AND BaselineCommit=@BaselineCommit
            ORDER BY RecordedAtUtc DESC, Id DESC;
            """,
            new
            {
                command.TenantId,
                command.ProjectId,
                RepositoryBindingId = binding.Id,
                ProjectExecutionProfileId = profile.Id,
                BaselineCommit = binding.BaselineCommit
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<ProvisioningReceiptRow?> ReadProvisioningReceiptForAttemptAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StartWorkbenchSandboxQualificationCommand command,
        AttemptRow attempt,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<ProvisioningReceiptRow>(new CommandDefinition(
            """
            SELECT Id, BaselineCommit, ManifestSha256, GitTreeId, ManifestJson
            FROM dbo.RepositoryProvisioningReceipts WITH (HOLDLOCK)
            WHERE Id=@RepositoryProvisioningReceiptId
              AND TenantId=@TenantId AND ProjectId=@ProjectId
              AND RepositoryBindingId=@RepositoryBindingId
              AND ProjectExecutionProfileId=@ProjectExecutionProfileId
              AND BaselineCommit=@BaselineCommit
              AND ManifestSha256=@SourceManifestSha256
              AND GitTreeId=@SourceGitTreeId;
            """,
            new
            {
                command.TenantId,
                command.ProjectId,
                attempt.RepositoryProvisioningReceiptId,
                attempt.RepositoryBindingId,
                attempt.ProjectExecutionProfileId,
                attempt.BaselineCommit,
                attempt.SourceManifestSha256,
                attempt.SourceGitTreeId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StartWorkbenchSandboxQualificationCommand command,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
            """
            SELECT Id, ActorUserId, PayloadHash, Status, CanonicalResultJson, ResultHash
            FROM dbo.ClientOperations WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId AND OperationKind=@OperationKind
              AND ResourceScopeId=@ResourceScopeId AND ActorUserId=@ActorUserId
              AND ClientOperationId=@ClientOperationId;
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                OperationKind = SandboxQualificationOperationKinds.Qualify,
                ResourceScopeId = ResourceScope(command.ProjectId),
                command.ClientOperationId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<AttemptRow?> ReadAttemptByOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long operationRecordId,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<AttemptRow>(new CommandDefinition(
            """
            SELECT Id, RepositoryBindingId, ProjectExecutionProfileId, RepositoryProvisioningReceiptId,
                   ClientOperationId, ActorUserId,
                   AttemptNumber, ExpectedBindingRevision, ExpectedExecutionProfileRevision,
                   BaselineCommit, SourceManifestSha256, SourceGitTreeId,
                   ProfileDefinitionId, ProfileDescriptorRevision,
                   DescriptorSha256, TemplateBundleSha256, ToolchainManifestId,
                   ContainerImageDigest, OfflineFeedManifestSha256,
                   SandboxPolicyVersion, SandboxPolicySha256,
                   TrustedSupervisorVersion, TrustedSupervisorSha256,
                   State, StartedAtUtc, CompletedAtUtc,
                   EvidenceManifestSha256, FailureCode, FailureSummary
            FROM dbo.SandboxQualificationAttempts WITH (UPDLOCK, HOLDLOCK)
            WHERE ClientOperationRecordId=@OperationRecordId;
            """,
            new { OperationRecordId = operationRecordId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<AttemptRow?> ReadLatestAttemptAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken) => connection.QueryFirstOrDefaultAsync<AttemptRow>(new CommandDefinition(
            """
            SELECT TOP (1) Id, RepositoryBindingId, ProjectExecutionProfileId,
                   ClientOperationId, ActorUserId, AttemptNumber, ExpectedBindingRevision,
                   ExpectedExecutionProfileRevision, BaselineCommit,
                   ContainerImageDigest, OfflineFeedManifestSha256,
                   SandboxPolicySha256, TrustedSupervisorVersion, TrustedSupervisorSha256,
                   State, StartedAtUtc, CompletedAtUtc,
                   EvidenceManifestSha256, FailureCode, FailureSummary
            FROM dbo.SandboxQualificationAttempts
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
            ORDER BY StartedAtUtc DESC, AttemptNumber DESC;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));

    private static WorkbenchSandboxQualificationResult ReadReplay(ClientOperationRow operation)
    {
        if (string.IsNullOrWhiteSpace(operation.CanonicalResultJson) ||
            !IsLowerHex(operation.ResultHash ?? string.Empty, 64) ||
            !string.Equals(SandboxCanonicalJson.Sha256(operation.CanonicalResultJson),
                operation.ResultHash, StringComparison.Ordinal))
            throw new SandboxQualificationIntegrityException(
                "The stored sandbox operation result failed integrity verification.");
        return JsonSerializer.Deserialize<WorkbenchSandboxQualificationResult>(
                   operation.CanonicalResultJson,
                   JsonOptions)
               ?? throw new SandboxQualificationIntegrityException(
                   "The stored sandbox operation result is unreadable.");
    }

    private static SandboxQualificationAttemptSnapshot ToSnapshot(AttemptRow attempt, int requestingActorUserId) => new(
        attempt.Id,
        attempt.ClientOperationId,
        attempt.State,
        attempt.RepositoryBindingId,
        attempt.ExpectedBindingRevision,
        attempt.ProjectExecutionProfileId,
        attempt.ExpectedExecutionProfileRevision,
        attempt.BaselineCommit,
        attempt.StartedAtUtc,
        attempt.CompletedAtUtc,
        attempt.EvidenceManifestSha256,
        attempt.FailureCode,
        attempt.FailureSummary,
        attempt.State == SandboxQualificationStates.Running && attempt.ActorUserId == requestingActorUserId);

    private static SandboxExecutionProfileBinding ToBinding(ProjectExecutionProfileSnapshot profile) => new()
    {
        ProfileDefinitionId = profile.ProfileDefinitionId,
        ProfileDescriptorRevision = profile.ProfileDescriptorRevision,
        DescriptorSha256 = profile.DescriptorSha256,
        TemplateBundleSha256 = profile.TemplateBundleSha256,
        ToolchainManifestId = profile.ToolchainManifestId,
        ExecutionImageReference = profile.ExecutionImageReference,
        RestoreCommand = profile.RestoreCommand,
        BuildCommand = profile.BuildCommand,
        TestCommand = profile.TestCommand
    };

    private static SandboxRuntimePolicy RecoveryPolicy(
        AttemptRow attempt,
        ProjectExecutionProfileSnapshot profile)
    {
        var resources = SandboxResourcePolicy.WorkbenchV01;
        return new SandboxRuntimePolicy
        {
            SchemaVersion = 1,
            PolicyVersion = attempt.SandboxPolicyVersion,
            IsolationMode = SandboxIsolationModes.HcsHyperV,
            ProfileDefinitionId = attempt.ProfileDefinitionId,
            ProfileDescriptorRevision = attempt.ProfileDescriptorRevision,
            DescriptorSha256 = attempt.DescriptorSha256,
            TemplateBundleSha256 = attempt.TemplateBundleSha256,
            ToolchainManifestId = attempt.ToolchainManifestId,
            ContainerImageReference = attempt.ContainerImageDigest,
            ContainerImageDigest = ImageDigest(attempt.ContainerImageDigest),
            OfflineFeedPath = string.Empty,
            OfflineFeedManifestSha256 = attempt.OfflineFeedManifestSha256,
            RepositoryInputReadOnly = true,
            OfflineFeedReadOnly = true,
            TrustedSupervisorVersion = attempt.TrustedSupervisorVersion,
            TrustedSupervisorSha256 = attempt.TrustedSupervisorSha256,
            Resources = resources,
            Restore = RecoveryCommand(
                SandboxExecutionStage.Restore,
                profile.RestoreCommand,
                resources.RestoreTimeoutSeconds),
            Build = RecoveryCommand(
                SandboxExecutionStage.Build,
                profile.BuildCommand,
                resources.BuildTimeoutSeconds),
            Test = RecoveryCommand(
                SandboxExecutionStage.Test,
                profile.TestCommand,
                resources.TestTimeoutSeconds),
            EnvironmentAllowList = [],
            PolicySha256 = attempt.SandboxPolicySha256
        };
    }

    private static SandboxCommandPolicy RecoveryCommand(
        SandboxExecutionStage stage,
        string command,
        int timeoutSeconds) => new(
        stage,
        command,
        SandboxCanonicalJson.Sha256(command),
        timeoutSeconds);

    private static string ImageDigest(string reference)
    {
        var separator = reference.LastIndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        if (separator <= 0)
            throw new SandboxQualificationIntegrityException(
                "The durable sandbox image authority is not digest pinned.");
        return SandboxCanonicalJson.NormalizeSha256(
            reference[(separator + "@sha256:".Length)..],
            nameof(reference));
    }

    private static Task InsertOutboxAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StartWorkbenchSandboxQualificationCommand command,
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
        StartWorkbenchSandboxQualificationCommand command,
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

    private async Task EnsurePreLockAccessAsync(
        StartWorkbenchSandboxQualificationCommand command,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        if (!await CanAccessProjectAsync(connection, null, command.TenantId, command.ActorUserId,
                command.ProjectId, requireContributor: false, cancellationToken).ConfigureAwait(false))
            throw new WorkbenchProjectNotAccessibleException();
        if (!await CanAccessProjectAsync(connection, null, command.TenantId, command.ActorUserId,
                command.ProjectId, requireContributor: true, cancellationToken).ConfigureAwait(false))
            throw new SandboxQualificationForbiddenException();
    }

    private async Task<IDisposable> AcquireDistributedLockAsync(
        StartWorkbenchSandboxQualificationCommand command,
        CancellationToken cancellationToken)
    {
        var connection = _connections.CreateConnection();
        try
        {
            connection.Open();
            var resource = $"IronDev:SandboxQualification:{command.TenantId}:{command.ProjectId}";
            var result = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                DECLARE @result INT;
                EXEC @result = sys.sp_getapplock
                    @Resource=@Resource, @LockMode=N'Exclusive', @LockOwner=N'Session',
                    @LockTimeout=0, @DbPrincipal=N'public';
                SELECT @result;
                """,
                new { Resource = resource },
                cancellationToken: cancellationToken));
            if (result < 0)
                throw new SandboxQualificationInProgressException();
            return new DistributedLock(connection, resource);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void ValidateRoots()
    {
        if (!Path.IsPathFullyQualified(_snapshotRoot) || !Path.IsPathFullyQualified(_evidenceRoot))
            throw new SandboxQualificationUnavailableException(Unavailable(
                SandboxReasonCodes.UnsafeHostPath,
                "Owned sandbox snapshot and evidence roots are not configured."));
    }

    private string EvidencePath(StartWorkbenchSandboxQualificationCommand command, Guid attemptId)
    {
        ValidateRoots();
        return Path.Combine(
            Path.GetFullPath(_evidenceRoot),
            $"tenant-{command.TenantId}",
            $"project-{command.ProjectId}",
            attemptId.ToString("N"));
    }

    private static string PayloadHash(StartWorkbenchSandboxQualificationCommand command) =>
        SandboxCanonicalJson.Sha256(
            $"workbench-sandbox-qualification-v1\n{command.ProjectId}\n" +
            $"{command.ExpectedRepositoryBindingRevision}\n{command.ExpectedExecutionProfileRevision}");

    private static string ResourceScope(int projectId) =>
        $"project:{projectId}:sandbox-qualification";

    private static void Validate(StartWorkbenchSandboxQualificationCommand command)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty ||
            command.ExpectedRepositoryBindingRevision <= 0 ||
            command.ExpectedExecutionProfileRevision <= 0)
            throw new SandboxQualificationValidationException(
                "A current Workbench fence, client operation, and exact repository/profile revisions are required.");
    }

    private static void ValidateIdentity(int tenantId, int actorUserId, int projectId)
    {
        if (tenantId <= 0 || actorUserId <= 0 || projectId <= 0)
            throw new SandboxQualificationValidationException(
                "A current tenant, actor, and project are required.");
    }

    private static bool IsLowerHex(string value, int length) =>
        value?.Length == length && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static SandboxCapability Unavailable(string reasonCode, string message) => new(
        SandboxCapabilityStates.Unavailable,
        reasonCode,
        message,
        SandboxPolicyVersions.WorkbenchV01,
        PolicySha256: null);

    private sealed record QualificationClaim(
        long OperationRecordId,
        Guid AttemptId,
        int AttemptNumber,
        DateTime StartedAtUtc,
        RepositoryBindingSnapshot Binding,
        ProjectExecutionProfileSnapshot Profile,
        ProvisioningReceiptRow Receipt,
        SandboxRuntimePolicy Policy,
        bool IsExistingPending,
        WorkbenchSandboxQualificationResult? Replay)
    {
        public static QualificationClaim ForReplay(WorkbenchSandboxQualificationResult replay) => new(
            0, Guid.Empty, 0, default, null!, null!, null!, null!, false, replay);
    }

    private sealed class ClientOperationRow
    {
        public long Id { get; init; }
        public int ActorUserId { get; init; }
        public string PayloadHash { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }

    private sealed class AttemptRow
    {
        public Guid Id { get; init; }
        public Guid RepositoryBindingId { get; init; }
        public Guid ProjectExecutionProfileId { get; init; }
        public Guid RepositoryProvisioningReceiptId { get; init; }
        public Guid ClientOperationId { get; init; }
        public int ActorUserId { get; init; }
        public int AttemptNumber { get; init; }
        public long ExpectedBindingRevision { get; init; }
        public long ExpectedExecutionProfileRevision { get; init; }
        public string BaselineCommit { get; init; } = string.Empty;
        public string SourceManifestSha256 { get; init; } = string.Empty;
        public string SourceGitTreeId { get; init; } = string.Empty;
        public string ProfileDefinitionId { get; init; } = string.Empty;
        public int ProfileDescriptorRevision { get; init; }
        public string DescriptorSha256 { get; init; } = string.Empty;
        public string TemplateBundleSha256 { get; init; } = string.Empty;
        public string ToolchainManifestId { get; init; } = string.Empty;
        public string ContainerImageDigest { get; init; } = string.Empty;
        public string OfflineFeedManifestSha256 { get; init; } = string.Empty;
        public string SandboxPolicyVersion { get; init; } = string.Empty;
        public string SandboxPolicySha256 { get; init; } = string.Empty;
        public string TrustedSupervisorVersion { get; init; } = string.Empty;
        public string TrustedSupervisorSha256 { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public DateTime StartedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public string? EvidenceManifestSha256 { get; init; }
        public string? FailureCode { get; init; }
        public string? FailureSummary { get; init; }
    }

    private sealed class ProvisioningReceiptRow
    {
        public Guid Id { get; init; }
        public string BaselineCommit { get; init; } = string.Empty;
        public string ManifestSha256 { get; init; } = string.Empty;
        public string GitTreeId { get; init; } = string.Empty;
        public string ManifestJson { get; init; } = string.Empty;
    }

    private sealed class ProjectRow
    {
        public int ProjectId { get; init; }
        public string ProjectLifecyclePhase { get; init; } = ProjectLifecyclePhases.Shaping;
        public string ExecutionReadiness { get; init; } = ProjectExecutionReadinessStates.NotConfigured;
    }

    private sealed class DistributedLock : IDisposable
    {
        private IDbConnection? _connection;
        private readonly string _resource;

        public DistributedLock(IDbConnection connection, string resource)
        {
            _connection = connection;
            _resource = resource;
        }

        public void Dispose()
        {
            var connection = Interlocked.Exchange(ref _connection, null);
            if (connection is null)
                return;
            try
            {
                connection.Execute(
                    """
                    DECLARE @result INT;
                    EXEC @result = sys.sp_releaseapplock
                        @Resource=@Resource, @LockOwner=N'Session', @DbPrincipal=N'public';
                    """,
                    new { Resource = _resource });
            }
            finally
            {
                connection.Dispose();
            }
        }
    }
}
