using System.Data;
using Dapper;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
using IronDev.Data;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services.Sandbox;

public sealed record WorkbenchSandboxRecoverySummary(
    int CandidatesRead,
    int AttemptsRecovered,
    int AttemptsMaterialized,
    int ActiveAttemptsSkipped,
    int RecoveryFailures);

public interface IWorkbenchSandboxRecoveryService
{
    Task<WorkbenchSandboxRecoverySummary> RecoverStaleAttemptsAsync(
        int maximumCandidates,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Recovers only durable Running attempts for which the same project-scoped SQL
/// application lock is no longer held. That lock is retained across resource cleanup
/// and the one terminal database transaction, so a live attempt or retry cannot race it.
/// </summary>
public sealed class WorkbenchSandboxRecoveryService : IWorkbenchSandboxRecoveryService
{
    private const string Route = "/api/workbench/projects/{projectId}/repository/sandbox-qualifications";
    private readonly IDbConnectionFactory _connections;
    private readonly ISandboxExecutionService _sandbox;
    private readonly ISandboxSourceSnapshotBuilder _snapshots;
    private readonly string _snapshotRoot;
    private readonly string _evidenceRoot;

    public WorkbenchSandboxRecoveryService(
        IDbConnectionFactory connections,
        ISandboxExecutionService sandbox,
        ISandboxSourceSnapshotBuilder snapshots,
        IConfiguration configuration)
    {
        _connections = connections;
        _sandbox = sandbox;
        _snapshots = snapshots;
        _snapshotRoot = configuration["WorkbenchProductionSandbox:SourceSnapshotRoot"]?.Trim() ?? string.Empty;
        _evidenceRoot = configuration["WorkbenchProductionSandbox:EvidenceRoot"]?.Trim() ?? string.Empty;
    }

    public async Task<WorkbenchSandboxRecoverySummary> RecoverStaleAttemptsAsync(
        int maximumCandidates,
        CancellationToken cancellationToken = default)
    {
        if (maximumCandidates is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(maximumCandidates));
        var candidates = await ReadCandidatesAsync(maximumCandidates, cancellationToken).ConfigureAwait(false);
        var recovered = 0;
        var materialized = 0;
        var active = 0;
        var failures = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var recoveryLock = await TryAcquireProjectLockAsync(candidate, cancellationToken)
                    .ConfigureAwait(false);
                if (recoveryLock is null)
                {
                    active++;
                    continue;
                }

                var claim = await ReadClaimAsync(recoveryLock.Connection, candidate, cancellationToken)
                    .ConfigureAwait(false);
                if (claim is null)
                    continue;
                var outcome = await RecoverClaimAsync(claim, cancellationToken).ConfigureAwait(false);
                if (!outcome.CleanupConfirmed)
                {
                    failures++;
                    continue;
                }
                await CompleteAsync(recoveryLock.Connection, claim, outcome, cancellationToken)
                    .ConfigureAwait(false);
                if (outcome.Execution is null)
                    recovered++;
                else
                    materialized++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failures++;
            }
        }
        return new WorkbenchSandboxRecoverySummary(
            candidates.Count,
            recovered,
            materialized,
            active,
            failures);
    }

    private async Task<RecoveryOutcome> RecoverClaimAsync(
        RecoveryClaim claim,
        CancellationToken cancellationToken)
    {
        SandboxExecutionResult? execution = null;
        var exactSnapshotAuthority = !string.IsNullOrWhiteSpace(claim.ManifestSha256) &&
                                     !string.IsNullOrWhiteSpace(claim.ManifestJson) &&
                                     !string.IsNullOrWhiteSpace(claim.GitTreeId);
        if (exactSnapshotAuthority)
        {
            try
            {
                var identity = _snapshots.Describe(new SandboxSourceSnapshotRequest(
                    claim.AttemptId,
                    claim.CanonicalPath,
                    claim.BaselineCommit,
                    claim.GitTreeId!,
                    claim.ManifestJson!,
                    claim.ManifestSha256!,
                    _snapshotRoot));
                execution = await _sandbox.TryRecoverCompletedAsync(
                    CompletedEvidenceRequest(claim, identity),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Invalid or currently inaccessible evidence must not prevent exact
                // container teardown. It remains Running until every resource can be
                // positively recovered or cleaned in a later pass.
                execution = null;
            }
        }

        if (execution is not null)
        {
            var completedSnapshotCleaned = TryCleanupSnapshot(claim, exactSnapshotAuthority);
            return new RecoveryOutcome(execution, execution.CleanedUp && completedSnapshotCleaned);
        }

        var runtimeCleaned = false;
        try
        {
            var cleanup = await _sandbox.RecoverExecutionAsync(
                new SandboxExecutionCleanupRequest(
                    ExecutionId: claim.AttemptId,
                    SandboxPolicySha256: claim.SandboxPolicySha256,
                    TrustedSupervisorVersion: claim.TrustedSupervisorVersion,
                    TrustedSupervisorSha256: claim.TrustedSupervisorSha256,
                    EvidenceOutputPath: EvidencePath(claim.TenantId, claim.ProjectId, claim.AttemptId)),
                cancellationToken).ConfigureAwait(false);
            runtimeCleaned = cleanup.CleanupConfirmed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            runtimeCleaned = false;
        }
        var snapshotCleaned = TryCleanupSnapshot(claim, exactSnapshotAuthority);
        return new RecoveryOutcome(
            Execution: null,
            CleanupConfirmed: runtimeCleaned && snapshotCleaned);
    }

    private bool TryCleanupSnapshot(RecoveryClaim claim, bool exactSnapshotAuthority)
    {
        if (!exactSnapshotAuthority)
            return false;
        try
        {
            return _snapshots.CleanupRecovered(new SandboxSourceSnapshotRecoveryRequest(
                claim.AttemptId,
                claim.ManifestSha256!,
                _snapshotRoot));
        }
        catch
        {
            return false;
        }
    }

    private async Task CompleteAsync(
        IDbConnection connection,
        RecoveryClaim claim,
        RecoveryOutcome outcome,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var authority = await connection.QuerySingleOrDefaultAsync<CompletionAuthority>(new CommandDefinition(
                """
                SELECT attempt.State, attempt.SandboxPolicySha256,
                       attempt.TrustedSupervisorVersion, attempt.TrustedSupervisorSha256,
                       operation.Status AS OperationStatus, operation.PayloadHash
                FROM dbo.SandboxQualificationAttempts attempt WITH (UPDLOCK, HOLDLOCK)
                JOIN dbo.ClientOperations operation WITH (UPDLOCK, HOLDLOCK)
                  ON operation.Id=attempt.ClientOperationRecordId
                 AND operation.TenantId=attempt.TenantId
                 AND operation.ResultProjectId=attempt.ProjectId
                 AND operation.ClientOperationId=attempt.ClientOperationId
                 AND operation.ActorUserId=attempt.ActorUserId
                 AND operation.OperationKind=attempt.ClientOperationKind
                 AND operation.ResourceScopeId=attempt.ClientOperationResourceScopeId
                 AND operation.ResultWorkbenchSessionId=attempt.WorkbenchSessionId
                WHERE attempt.Id=@AttemptId AND attempt.TenantId=@TenantId
                  AND attempt.ProjectId=@ProjectId;
                """,
                new { claim.AttemptId, claim.TenantId, claim.ProjectId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? throw new SandboxQualificationIntegrityException(
                    "The stale sandbox recovery authority disappeared.");
            if (!string.Equals(authority.State, SandboxQualificationStates.Running, StringComparison.Ordinal) ||
                !string.Equals(authority.OperationStatus, "Pending", StringComparison.Ordinal) ||
                !string.Equals(authority.PayloadHash, claim.PayloadHash, StringComparison.Ordinal) ||
                !string.Equals(authority.SandboxPolicySha256, claim.SandboxPolicySha256, StringComparison.Ordinal) ||
                !string.Equals(authority.TrustedSupervisorVersion, claim.TrustedSupervisorVersion, StringComparison.Ordinal) ||
                !string.Equals(authority.TrustedSupervisorSha256, claim.TrustedSupervisorSha256, StringComparison.Ordinal))
                throw new SandboxQualificationIntegrityException(
                    "The stale sandbox recovery authority changed before finalization.");

            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();",
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var terminal = TerminalOutcome(claim, outcome);
            Guid? evidenceId = null;
            if (outcome.Execution is not null)
                ValidateExecution(claim, outcome.Execution);

            var attemptRows = await connection.ExecuteAsync(new CommandDefinition(
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
                    terminal.State,
                    terminal.FailureCode,
                    FailureSummary = terminal.FailureCode is null ? null : terminal.SafeSummary,
                    EvidenceManifestSha256 = outcome.Execution?.EvidenceManifestSha256,
                    outcome.CleanupConfirmed,
                    CompletedAtUtc = now,
                    claim.AttemptId,
                    claim.TenantId,
                    claim.ProjectId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (attemptRows != 1)
                throw new SandboxQualificationIntegrityException(
                    "The stale sandbox attempt could not be terminally recovered.");

            if (outcome.Execution is not null)
            {
                evidenceId = Guid.NewGuid();
                await InsertEvidenceAsync(
                    connection,
                    transaction,
                    claim,
                    outcome.Execution,
                    evidenceId.Value,
                    terminal.State == SandboxQualificationStates.Passed,
                    now,
                    cancellationToken).ConfigureAwait(false);
            }

            var capability = terminal.State == SandboxQualificationStates.Passed
                ? new SandboxCapability(
                    SandboxCapabilityStates.Available,
                    SandboxReasonCodes.Ready,
                    "The production sandbox qualification evidence passed for this exact authority.",
                    claim.SandboxPolicyVersion,
                    claim.SandboxPolicySha256)
                : new SandboxCapability(
                    SandboxCapabilityStates.Unavailable,
                    terminal.FailureCode ?? SandboxReasonCodes.HostRestartRecovered,
                    terminal.SafeSummary,
                    claim.SandboxPolicyVersion,
                    claim.SandboxPolicySha256);
            var snapshot = new SandboxQualificationAttemptSnapshot(
                claim.AttemptId,
                claim.ClientOperationId,
                terminal.State,
                claim.RepositoryBindingId,
                claim.ExpectedBindingRevision,
                claim.ProjectExecutionProfileId,
                claim.ExpectedExecutionProfileRevision,
                claim.BaselineCommit,
                claim.StartedAtUtc,
                now,
                outcome.Execution?.EvidenceManifestSha256,
                terminal.FailureCode,
                terminal.SafeSummary,
                CanRecover: false);
            var result = new WorkbenchSandboxQualificationResult(
                claim.ProjectId,
                claim.ClientOperationId,
                IsReplay: false,
                capability,
                snapshot);
            var resultJson = SandboxCanonicalJson.Serialize(result);
            var operationRows = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ClientOperations
                SET Status=N'Completed', CanonicalResultJson=@ResultJson, ResultHash=@ResultHash,
                    ResultSandboxQualificationAttemptId=@AttemptId,
                    ResultSandboxEvidenceManifestId=@EvidenceId,
                    CompletedAtUtc=@CompletedAtUtc
                WHERE Id=@OperationRecordId AND TenantId=@TenantId AND ActorUserId=@ActorUserId
                  AND Status=N'Pending';
                """,
                new
                {
                    ResultJson = resultJson,
                    ResultHash = SandboxCanonicalJson.Sha256(resultJson),
                    claim.AttemptId,
                    EvidenceId = evidenceId,
                    CompletedAtUtc = now,
                    claim.OperationRecordId,
                    claim.TenantId,
                    claim.ActorUserId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (operationRows != 1)
                throw new SandboxQualificationIntegrityException(
                    "The stale sandbox client operation could not be completed.");

            var effectiveReadiness = await connection.QuerySingleAsync<string>(new CommandDefinition(
                """
                SELECT ExecutionReadiness
                FROM dbo.vw_WorkbenchEffectiveProjectReadiness
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId;
                """,
                new { claim.TenantId, claim.ProjectId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            await InsertOutboxAsync(
                connection,
                transaction,
                claim,
                outcome.Execution is null
                    ? "SandboxQualificationRecovered"
                    : terminal.State == SandboxQualificationStates.Passed
                        ? "SandboxQualificationPassed"
                        : "SandboxQualificationFailed",
                SandboxCanonicalJson.Serialize(new
                {
                    schemaVersion = 1,
                    claim.ProjectId,
                    attemptId = claim.AttemptId,
                    state = terminal.State,
                    evidenceManifestSha256 = outcome.Execution?.EvidenceManifestSha256,
                    cleanupConfirmed = outcome.CleanupConfirmed,
                    executionReadiness = effectiveReadiness
                }),
                now,
                cancellationToken).ConfigureAwait(false);
            await InsertAttributionAsync(
                connection,
                transaction,
                claim,
                now,
                cancellationToken).ConfigureAwait(false);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<IReadOnlyList<RecoveryCandidate>> ReadCandidatesAsync(
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        var candidates = await connection.QueryAsync<RecoveryCandidate>(new CommandDefinition(
            """
            SELECT TOP (@MaximumCandidates) Id AS AttemptId, TenantId, ProjectId
            FROM dbo.SandboxQualificationAttempts
            WHERE State=N'Running'
            ORDER BY CASE WHEN LastRecoveryAttemptAtUtc IS NULL THEN 0 ELSE 1 END,
                     LastRecoveryAttemptAtUtc, StartedAtUtc, AttemptNumber, Id;
            """,
            new { MaximumCandidates = maximumCandidates },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return candidates.AsList();
    }

    private async Task<RecoveryProjectLock?> TryAcquireProjectLockAsync(
        RecoveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        var connection = _connections.CreateConnection();
        try
        {
            connection.Open();
            var resource = LockResource(candidate.TenantId, candidate.ProjectId);
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
            {
                connection.Dispose();
                return null;
            }
            return new RecoveryProjectLock(connection, resource);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static async Task<RecoveryClaim?> ReadClaimAsync(
        IDbConnection connection,
        RecoveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var claim = await connection.QuerySingleOrDefaultAsync<RecoveryClaim>(new CommandDefinition(
                """
                SELECT attempt.Id AS AttemptId, attempt.TenantId, attempt.ProjectId,
                       attempt.RepositoryBindingId, attempt.ProjectExecutionProfileId,
                       attempt.ClientOperationRecordId AS OperationRecordId,
                       attempt.ClientOperationId, attempt.ActorUserId,
                       attempt.WorkbenchSessionId, attempt.LeaseEpoch, attempt.AttemptNumber,
                       attempt.ExpectedBindingRevision, attempt.ExpectedExecutionProfileRevision,
                       attempt.BaselineCommit, attempt.ProfileDefinitionId,
                       attempt.ProfileDescriptorRevision, attempt.DescriptorSha256,
                       attempt.TemplateBundleSha256, attempt.ToolchainManifestId,
                       attempt.ContainerImageDigest AS ContainerImageReference,
                       attempt.OfflineFeedManifestSha256, attempt.SandboxPolicyVersion,
                       attempt.SandboxPolicySha256, attempt.TrustedSupervisorVersion,
                       attempt.TrustedSupervisorSha256, attempt.StartedAtUtc,
                       operation.PayloadHash, operation.Status AS OperationStatus,
                       binding.CanonicalPath,
                       receipt.ManifestSha256, receipt.GitTreeId, receipt.ManifestJson
                FROM dbo.SandboxQualificationAttempts attempt WITH (UPDLOCK, HOLDLOCK)
                JOIN dbo.ClientOperations operation WITH (UPDLOCK, HOLDLOCK)
                  ON operation.Id=attempt.ClientOperationRecordId
                 AND operation.TenantId=attempt.TenantId
                 AND operation.ResultProjectId=attempt.ProjectId
                 AND operation.ClientOperationId=attempt.ClientOperationId
                 AND operation.ActorUserId=attempt.ActorUserId
                 AND operation.OperationKind=attempt.ClientOperationKind
                 AND operation.ResourceScopeId=attempt.ClientOperationResourceScopeId
                 AND operation.ResultWorkbenchSessionId=attempt.WorkbenchSessionId
                JOIN dbo.RepositoryBindings binding
                  ON binding.TenantId=attempt.TenantId AND binding.ProjectId=attempt.ProjectId
                 AND binding.Id=attempt.RepositoryBindingId
                JOIN dbo.RepositoryProvisioningReceipts receipt
                  ON receipt.Id=attempt.RepositoryProvisioningReceiptId
                 AND receipt.TenantId=attempt.TenantId AND receipt.ProjectId=attempt.ProjectId
                 AND receipt.RepositoryBindingId=attempt.RepositoryBindingId
                 AND receipt.ProjectExecutionProfileId=attempt.ProjectExecutionProfileId
                 AND receipt.BaselineCommit=attempt.BaselineCommit
                 AND receipt.ManifestSha256=attempt.SourceManifestSha256
                 AND receipt.GitTreeId=attempt.SourceGitTreeId
                WHERE attempt.Id=@AttemptId AND attempt.TenantId=@TenantId
                  AND attempt.ProjectId=@ProjectId AND attempt.State=N'Running';
                """,
                candidate,
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (claim is not null && !string.Equals(claim.OperationStatus, "Pending", StringComparison.Ordinal))
                throw new SandboxQualificationIntegrityException(
                    "A Running sandbox attempt is not bound to a Pending client operation.");
            if (claim is not null)
            {
                var scheduled = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.SandboxQualificationAttempts
                    SET LastRecoveryAttemptAtUtc=SYSUTCDATETIME()
                    WHERE Id=@AttemptId AND TenantId=@TenantId AND ProjectId=@ProjectId
                      AND State=N'Running';
                    """,
                    candidate,
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (scheduled != 1)
                    throw new SandboxQualificationIntegrityException(
                        "The stale sandbox attempt could not advance its recovery schedule.");
            }
            transaction.Commit();
            return claim;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private SandboxCompletedEvidenceRecoveryRequest CompletedEvidenceRequest(
        RecoveryClaim claim,
        SandboxSourceSnapshotIdentity identity) => new()
    {
        ExecutionId = claim.AttemptId,
        ProjectId = claim.ProjectId,
        RepositoryBindingId = claim.RepositoryBindingId,
        RepositoryBindingRevision = claim.ExpectedBindingRevision,
        BaselineCommit = claim.BaselineCommit,
        WorktreeFingerprint = identity.WorktreeFingerprint,
        ProjectExecutionProfileId = claim.ProjectExecutionProfileId,
        ProjectExecutionProfileRevision = claim.ExpectedExecutionProfileRevision,
        ProfileDefinitionId = claim.ProfileDefinitionId,
        ProfileDescriptorRevision = claim.ProfileDescriptorRevision,
        DescriptorSha256 = claim.DescriptorSha256,
        TemplateBundleSha256 = claim.TemplateBundleSha256,
        ToolchainManifestId = claim.ToolchainManifestId,
        ContainerImageReference = claim.ContainerImageReference,
        SandboxPolicyVersion = claim.SandboxPolicyVersion,
        SandboxPolicySha256 = claim.SandboxPolicySha256,
        TrustedSupervisorVersion = claim.TrustedSupervisorVersion,
        TrustedSupervisorSha256 = claim.TrustedSupervisorSha256,
        OfflineFeedManifestSha256 = claim.OfflineFeedManifestSha256,
        EvidenceOutputPath = EvidencePath(claim.TenantId, claim.ProjectId, claim.AttemptId)
    };

    private string EvidencePath(int tenantId, int projectId, Guid attemptId)
    {
        if (!Path.IsPathFullyQualified(_evidenceRoot))
            throw new SandboxContractValidationException("The owned sandbox evidence root is not configured.");
        return Path.Combine(
            Path.GetFullPath(_evidenceRoot),
            $"tenant-{tenantId}",
            $"project-{projectId}",
            attemptId.ToString("N"));
    }

    private static TerminalRecoveryOutcome TerminalOutcome(RecoveryClaim claim, RecoveryOutcome outcome)
    {
        if (outcome.Execution is null)
            return outcome.CleanupConfirmed
                ? new TerminalRecoveryOutcome(
                    SandboxQualificationStates.Recovered,
                    SandboxReasonCodes.HostRestartRecovered,
                    "The abandoned sandbox attempt was recovered after host restart and exact cleanup was confirmed.")
                : new TerminalRecoveryOutcome(
                    SandboxQualificationStates.Recovered,
                    SandboxReasonCodes.CleanupFailed,
                    "The abandoned sandbox attempt was terminally recovered, but cleanup of every exact owned resource could not be confirmed.");
        var passed = outcome.Execution.Status == SandboxExecutionStatus.Succeeded && outcome.CleanupConfirmed;
        var state = passed
            ? SandboxQualificationStates.Passed
            : outcome.Execution.Status == SandboxExecutionStatus.Cancelled
                ? SandboxQualificationStates.Cancelled
                : SandboxQualificationStates.Failed;
        return new TerminalRecoveryOutcome(
            state,
            passed ? null : !outcome.CleanupConfirmed
                ? SandboxReasonCodes.CleanupFailed
                : outcome.Execution.ReasonCode,
            passed
                ? "The production sandbox qualification completed with teardown confirmed."
                : !outcome.CleanupConfirmed
                    ? "Completed sandbox evidence was recovered, but exact snapshot cleanup could not be confirmed."
                    : outcome.Execution.SafeSummary);
    }

    private static void ValidateExecution(RecoveryClaim claim, SandboxExecutionResult execution)
    {
        if (execution.ExecutionId != claim.AttemptId ||
            !string.Equals(
                execution.EvidenceManifestSha256,
                SandboxCanonicalJson.Sha256(execution.EvidenceManifestJson),
                StringComparison.Ordinal) ||
            execution.EvidenceManifest.ExecutionId != claim.AttemptId ||
            execution.EvidenceManifest.ProjectId != claim.ProjectId ||
            execution.EvidenceManifest.RepositoryBindingId != claim.RepositoryBindingId ||
            execution.EvidenceManifest.RepositoryBindingRevision != claim.ExpectedBindingRevision ||
            execution.EvidenceManifest.ProjectExecutionProfileId != claim.ProjectExecutionProfileId ||
            execution.EvidenceManifest.ProjectExecutionProfileRevision != claim.ExpectedExecutionProfileRevision ||
            !string.Equals(
                execution.EvidenceManifest.SandboxPolicySha256,
                claim.SandboxPolicySha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                execution.EvidenceManifest.TrustedSupervisorVersion,
                claim.TrustedSupervisorVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                execution.EvidenceManifest.TrustedSupervisorSha256,
                claim.TrustedSupervisorSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                execution.EvidenceManifest.Inspection.TrustedSupervisorVersion,
                claim.TrustedSupervisorVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                execution.EvidenceManifest.Inspection.TrustedSupervisorSha256,
                claim.TrustedSupervisorSha256,
                StringComparison.Ordinal))
            throw new SandboxQualificationIntegrityException(
                "Recovered sandbox evidence failed its final durable-authority check.");
    }

    private static Task InsertEvidenceAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RecoveryClaim claim,
        SandboxExecutionResult execution,
        Guid evidenceId,
        bool passed,
        DateTime timestamp,
        CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.SandboxEvidenceManifests
                (Id, TenantId, ProjectId, SandboxQualificationAttemptId,
                 RepositoryBindingId, RepositoryBindingRevision,
                 ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                 ActorUserId, SchemaVersion, Passed, ManifestJson,
                 ManifestSha256, CreatedAtUtc)
            VALUES
                (@Id, @TenantId, @ProjectId, @AttemptId,
                 @RepositoryBindingId, @RepositoryBindingRevision,
                 @ProjectExecutionProfileId, @ProjectExecutionProfileRevision,
                 @ActorUserId, 1, @Passed, @ManifestJson,
                 @ManifestSha256, @CreatedAtUtc);
            """,
            new
            {
                Id = evidenceId,
                claim.TenantId,
                claim.ProjectId,
                claim.AttemptId,
                claim.RepositoryBindingId,
                RepositoryBindingRevision = claim.ExpectedBindingRevision,
                claim.ProjectExecutionProfileId,
                ProjectExecutionProfileRevision = claim.ExpectedExecutionProfileRevision,
                claim.ActorUserId,
                Passed = passed,
                ManifestJson = execution.EvidenceManifestJson,
                ManifestSha256 = execution.EvidenceManifestSha256,
                CreatedAtUtc = timestamp
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task InsertOutboxAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RecoveryClaim claim,
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
                claim.TenantId,
                claim.ProjectId,
                claim.WorkbenchSessionId,
                EventKind = eventKind,
                PayloadJson = payload,
                claim.ClientOperationId,
                OccurredAtUtc = timestamp
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task InsertAttributionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        RecoveryClaim claim,
        DateTime timestamp,
        CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT dbo.UserMutationAttribution
                (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId,
                 TimestampUtc, SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
            VALUES
                (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                 CONVERT(NVARCHAR(128), @ClientOperationId), CONVERT(NVARCHAR(128), @AttemptId),
                 @TimestampUtc, N'Workbench', N'IronDev.Api', N'POST', @Route, N'Recovered', 200);
            """,
            new
            {
                claim.ActorUserId,
                claim.TenantId,
                claim.ProjectId,
                claim.ClientOperationId,
                claim.AttemptId,
                TimestampUtc = timestamp,
                Route
            },
            transaction,
            cancellationToken: cancellationToken));

    private static string LockResource(int tenantId, int projectId) =>
        $"IronDev:SandboxQualification:{tenantId}:{projectId}";

    private sealed record RecoveryCandidate(Guid AttemptId, int TenantId, int ProjectId);

    private sealed class RecoveryClaim
    {
        public Guid AttemptId { get; init; }
        public int TenantId { get; init; }
        public int ProjectId { get; init; }
        public Guid RepositoryBindingId { get; init; }
        public Guid ProjectExecutionProfileId { get; init; }
        public long OperationRecordId { get; init; }
        public Guid ClientOperationId { get; init; }
        public int ActorUserId { get; init; }
        public long WorkbenchSessionId { get; init; }
        public long LeaseEpoch { get; init; }
        public int AttemptNumber { get; init; }
        public long ExpectedBindingRevision { get; init; }
        public long ExpectedExecutionProfileRevision { get; init; }
        public string BaselineCommit { get; init; } = string.Empty;
        public string ProfileDefinitionId { get; init; } = string.Empty;
        public int ProfileDescriptorRevision { get; init; }
        public string DescriptorSha256 { get; init; } = string.Empty;
        public string TemplateBundleSha256 { get; init; } = string.Empty;
        public string ToolchainManifestId { get; init; } = string.Empty;
        public string ContainerImageReference { get; init; } = string.Empty;
        public string OfflineFeedManifestSha256 { get; init; } = string.Empty;
        public string SandboxPolicyVersion { get; init; } = string.Empty;
        public string SandboxPolicySha256 { get; init; } = string.Empty;
        public string TrustedSupervisorVersion { get; init; } = string.Empty;
        public string TrustedSupervisorSha256 { get; init; } = string.Empty;
        public DateTime StartedAtUtc { get; init; }
        public string PayloadHash { get; init; } = string.Empty;
        public string OperationStatus { get; init; } = string.Empty;
        public string CanonicalPath { get; init; } = string.Empty;
        public string? ManifestSha256 { get; init; }
        public string? GitTreeId { get; init; }
        public string? ManifestJson { get; init; }
    }

    private sealed class CompletionAuthority
    {
        public string State { get; init; } = string.Empty;
        public string SandboxPolicySha256 { get; init; } = string.Empty;
        public string TrustedSupervisorVersion { get; init; } = string.Empty;
        public string TrustedSupervisorSha256 { get; init; } = string.Empty;
        public string OperationStatus { get; init; } = string.Empty;
        public string PayloadHash { get; init; } = string.Empty;
    }

    private sealed record RecoveryOutcome(SandboxExecutionResult? Execution, bool CleanupConfirmed);
    private sealed record TerminalRecoveryOutcome(string State, string? FailureCode, string SafeSummary);

    private sealed class RecoveryProjectLock : IDisposable
    {
        private IDbConnection? _connection;
        private readonly string _resource;

        public RecoveryProjectLock(IDbConnection connection, string resource)
        {
            _connection = connection;
            _resource = resource;
        }

        public IDbConnection Connection => _connection
            ?? throw new ObjectDisposedException(nameof(RecoveryProjectLock));

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
