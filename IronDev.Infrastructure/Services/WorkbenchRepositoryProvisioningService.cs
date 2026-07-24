using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchRepositoryProvisioningService : IWorkbenchRepositoryProvisioningService
{
    private const string Route = "/api/workbench/projects/{projectId}/repository/provisionings";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connections;
    private readonly IRepositorySetupProfileCatalog _catalog;
    private readonly IRepositoryProvisioningExecutor _executor;
    private readonly IRepositoryProvisioningFailureInjector _failureInjector;
    private readonly string _approvedWorkspaceRoot;

    public WorkbenchRepositoryProvisioningService(
        IDbConnectionFactory connections,
        IRepositorySetupProfileCatalog catalog,
        IRepositoryProvisioningExecutor executor,
        IRepositoryProvisioningFailureInjector failureInjector,
        IConfiguration configuration)
    {
        _connections = connections;
        _catalog = catalog;
        _executor = executor;
        _failureInjector = failureInjector;
        _approvedWorkspaceRoot =
            configuration["WorkbenchRepositorySetup:ApprovedWorkspaceRoot"]?.Trim() ?? string.Empty;
    }

    public async Task<RepositoryProvisioningResult> ProvisionAsync(
        ProvisionRepositoryCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        await EnsurePreLockAccessAsync(command, cancellationToken);
        var lockKey = $"{command.TenantId}:{command.ProjectId}";
        var gate = OperationLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var distributedLock = await AcquireDistributedOperationLockAsync(command, cancellationToken);
            ProvisioningClaim? claim = null;
            try
            {
                claim = await ClaimAsync(command, cancellationToken);
                if (claim.Replay is not null)
                    return claim.Replay with { IsReplay = true };

                _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.ClaimCommitted);
                var request = ToExecutionRequest(claim);
                var evidence = await _executor.ExecuteOrRecoverAsync(request, cancellationToken);
                _failureInjector.ThrowIfRequested(RepositoryProvisioningFailurePoint.BeforeFinalize);
                return await FinalizeAsync(command, claim, evidence, cancellationToken);
            }
            catch (Exception exception) when (claim is { Replay: null })
            {
                var request = ToExecutionRequest(claim!);
                var published = await _executor.InspectPublishedRepositoryForAttemptAsync(
                    request, CancellationToken.None);
                if (published.State is RepositoryProvisioningPublishedInspectionState.Verified or
                    RepositoryProvisioningPublishedInspectionState.VerificationUnavailable)
                {
                    if (exception is WorkbenchProjectNotAccessibleException or
                        RepositoryProvisioningForbiddenException or WorkbenchLeaseFenceException or
                        RepositoryProvisioningStaleException or ProjectStartOperationMismatchException)
                        throw;
                    throw new RepositoryProvisioningInProgressException();
                }

                var failure = ToFailure(exception);
                await RecordFailureAsync(command, claim, failure, CancellationToken.None);
                if (exception is OperationCanceledException)
                    throw;
                if (exception is WorkbenchLeaseFenceException or WorkbenchProjectNotAccessibleException or
                    RepositoryProvisioningForbiddenException or RepositoryProvisioningStaleException or
                    ProjectStartOperationMismatchException)
                    throw;
                throw failure;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ProvisioningClaim> ClaimAsync(
        ProvisionRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        var resourceScope = ResourceScope(command.ProjectId);
        var payloadHash = PayloadHash(command);
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(connection, transaction, command, requireContributor: false, cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();
            if (!await CanAccessProjectAsync(connection, transaction, command, requireContributor: true, cancellationToken))
                throw new RepositoryProvisioningForbiddenException();

            var operation = await ReadOperationAsync(connection, transaction, command, cancellationToken);
            if (operation is not null)
            {
                if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new ProjectStartOperationMismatchException();
                if (!await ValidateAndRenewLeaseAsync(connection, transaction, command, cancellationToken))
                    throw new WorkbenchLeaseFenceException();
                if (operation.Status == "Completed")
                {
                    var replay = ReadReplay(operation);
                    transaction.Commit();
                    return ProvisioningClaim.ForReplay(replay);
                }
                if (operation.Status == "Failed")
                    throw ReadFailure(operation);
                if (operation.Status != "Pending")
                    throw new RepositoryProvisioningIntegrityException(
                        "The stored provisioning operation has an unsupported state.");

                var existingAttempt = await ReadAttemptByOperationAsync(
                    connection, transaction, operation.Id, cancellationToken)
                    ?? throw new RepositoryProvisioningIntegrityException(
                        "The pending provisioning operation has no durable attempt.");
                if (existingAttempt.State != RepositoryProvisioningStates.Provisioning)
                    throw new RepositoryProvisioningIntegrityException(
                        "The pending provisioning operation and attempt disagree.");
                var existing = await HydrateClaimAsync(
                    connection, transaction, command, operation, existingAttempt, cancellationToken);
                transaction.Commit();
                return existing;
            }

            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command, cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var project = await ReadProjectStateAsync(
                connection, transaction, command, lockRows: true, cancellationToken)
                ?? throw new WorkbenchProjectNotAccessibleException();
            var binding = await ReadBindingAsync(connection, transaction, command, lockRows: true, cancellationToken)
                ?? throw new RepositoryProvisioningNotAllowedException(
                    "Repository setup must be confirmed before provisioning.");
            var profile = await ReadProfileAsync(
                connection, transaction, command, binding.Id, lockRows: true, cancellationToken)
                ?? throw new RepositoryProvisioningNotAllowedException(
                    "The confirmed execution profile is unavailable.");
            var confirmation = await ReadConfirmationAsync(
                connection, transaction, command, cancellationToken)
                ?? throw new RepositoryProvisioningStaleException();

            if (binding.Revision != command.ExpectedRepositoryBindingRevision ||
                profile.Revision != command.ExpectedExecutionProfileRevision ||
                confirmation.Id != command.SetupConfirmationId)
                throw new RepositoryProvisioningStaleException();
            if (binding.BindingState is not (RepositoryBindingStates.SetupConfirmed or RepositoryBindingStates.ProvisioningFailed))
                throw new RepositoryProvisioningNotAllowedException(
                    binding.BindingState == RepositoryBindingStates.Provisioning
                        ? "Retry the exact in-flight provisioning operation."
                        : "Only a confirmed or safely failed greenfield repository can be provisioned.");
            if (binding.RepositoryKind != RepositoryKinds.Greenfield ||
                !string.IsNullOrWhiteSpace(binding.BaselineCommit) ||
                !string.IsNullOrWhiteSpace(project.LocalPath))
                throw new RepositoryProvisioningNotAllowedException(
                    "This project is not an unprovisioned greenfield repository.");
            if (confirmation.RepositoryBindingId != binding.Id ||
                confirmation.ProjectExecutionProfileId != profile.Id)
                throw new RepositoryProvisioningIntegrityException(
                    "The immutable setup confirmation does not match its repository authorities.");

            var immutable = ValidateImmutableInputs(binding, profile, confirmation);
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
                    OperationKind = RepositorySetupOperationKinds.Provision,
                    ResourceScopeId = resourceScope,
                    command.ClientOperationId,
                    PayloadHash = payloadHash,
                    command.ProjectId,
                    command.WorkbenchSessionId
                },
                transaction,
                cancellationToken: cancellationToken));

            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: cancellationToken));
            var attemptNumber = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT COALESCE(MAX(AttemptNumber), 0) + 1
                FROM dbo.RepositoryProvisioningAttempts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId
                  AND RepositoryBindingId=@RepositoryBindingId;
                """,
                new { command.TenantId, command.ProjectId, RepositoryBindingId = binding.Id },
                transaction,
                cancellationToken: cancellationToken));
            var attemptId = Guid.NewGuid();
            var targetName = Path.GetFileName(Path.TrimEndingDirectorySeparator(binding.CanonicalPath));
            var stagingPath = Path.Combine(
                Path.GetDirectoryName(binding.CanonicalPath)!,
                $".{targetName}.irondev-{attemptId:N}.staging");
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.RepositoryProvisioningAttempts
                    (Id, TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                     SetupConfirmationId, ClientOperationRecordId, ClientOperationId,
                     ActorUserId, ClientOperationKind, ClientOperationResourceScopeId,
                     WorkbenchSessionId, LeaseEpoch, AttemptNumber,
                     ExpectedBindingRevision, ExpectedExecutionProfileRevision,
                     PlanHash, DescriptorSha256, TemplateBundleSha256, PlanningBundleSha256,
                     CanonicalTargetPath, StagingPath, State, StartedAtUtc)
                VALUES
                    (@Id, @TenantId, @ProjectId, @RepositoryBindingId, @ProjectExecutionProfileId,
                     @SetupConfirmationId, @ClientOperationRecordId, @ClientOperationId,
                     @ActorUserId, @ClientOperationKind, @ClientOperationResourceScopeId,
                     @WorkbenchSessionId, @LeaseEpoch, @AttemptNumber,
                     @ExpectedBindingRevision, @ExpectedExecutionProfileRevision,
                     @PlanHash, @DescriptorSha256, @TemplateBundleSha256, @PlanningBundleSha256,
                     @CanonicalTargetPath, @StagingPath, N'Provisioning', @StartedAtUtc);
                """,
                new
                {
                    Id = attemptId,
                    command.TenantId,
                    command.ProjectId,
                    RepositoryBindingId = binding.Id,
                    ProjectExecutionProfileId = profile.Id,
                    SetupConfirmationId = confirmation.Id,
                    ClientOperationRecordId = operationRecordId,
                    command.ClientOperationId,
                    command.ActorUserId,
                    ClientOperationKind = RepositorySetupOperationKinds.Provision,
                    ClientOperationResourceScopeId = resourceScope,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    AttemptNumber = attemptNumber,
                    ExpectedBindingRevision = binding.Revision,
                    ExpectedExecutionProfileRevision = profile.Revision,
                    immutable.Plan.PlanHash,
                    profile.DescriptorSha256,
                    profile.TemplateBundleSha256,
                    profile.PlanningBundleSha256,
                    CanonicalTargetPath = binding.CanonicalPath,
                    StagingPath = stagingPath,
                    StartedAtUtc = now
                },
                transaction,
                cancellationToken: cancellationToken));

            var provisioningBinding = binding with
            {
                Revision = binding.Revision + 1,
                BindingState = RepositoryBindingStates.Provisioning
            };
            await UpdateBindingWithRevisionAsync(
                connection, transaction, command, provisioningBinding,
                "ProvisioningStarted", now, cancellationToken);
            var eventPayload = RepositorySetupCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                command.ProjectId,
                attemptId,
                attemptNumber,
                setupConfirmationId = confirmation.Id,
                repositoryBindingId = binding.Id,
                projectExecutionProfileId = profile.Id,
                planHash = immutable.Plan.PlanHash,
                bindingRevision = provisioningBinding.Revision,
                bindingState = provisioningBinding.BindingState
            });
            await InsertOutboxAsync(
                connection, transaction, command, "RepositoryProvisioningStarted", eventPayload, now, cancellationToken);
            await InsertAttributionAsync(
                connection, transaction, command, attemptId, "Attempted", 202, now, cancellationToken);

            transaction.Commit();
            return new ProvisioningClaim(
                operationRecordId,
                attemptId,
                attemptNumber,
                now,
                provisioningBinding,
                profile,
                confirmation,
                immutable.Plan,
                immutable.Bundle,
                project,
                Replay: null);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<ProvisioningClaim> HydrateClaimAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        ClientOperationRow operation,
        AttemptRow attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.ClientOperationId != command.ClientOperationId ||
            attempt.SetupConfirmationId != command.SetupConfirmationId ||
            attempt.ExpectedBindingRevision != command.ExpectedRepositoryBindingRevision ||
            attempt.ExpectedExecutionProfileRevision != command.ExpectedExecutionProfileRevision)
            throw new ProjectStartOperationMismatchException();
        var project = await ReadProjectStateAsync(connection, transaction, command, lockRows: true, cancellationToken)
            ?? throw new WorkbenchProjectNotAccessibleException();
        var binding = await ReadBindingAsync(connection, transaction, command, lockRows: true, cancellationToken)
            ?? throw new RepositoryProvisioningIntegrityException("The provisioning binding is unavailable.");
        var profile = await ReadProfileAsync(connection, transaction, command, attempt.ProjectExecutionProfileId,
            lockRows: true, cancellationToken, identifierIsProfileId: true)
            ?? throw new RepositoryProvisioningIntegrityException("The provisioning profile is unavailable.");
        var confirmation = await ReadConfirmationByIdAsync(
            connection, transaction, command, attempt.SetupConfirmationId, cancellationToken)
            ?? throw new RepositoryProvisioningIntegrityException("The provisioning confirmation is unavailable.");
        if (binding.Id != attempt.RepositoryBindingId ||
            binding.BindingState != RepositoryBindingStates.Provisioning ||
            binding.Revision != attempt.ExpectedBindingRevision + 1 ||
            profile.Revision != attempt.ExpectedExecutionProfileRevision)
            throw new RepositoryProvisioningIntegrityException(
                "The pending provisioning attempt no longer owns the current repository authorities.");
        var immutable = ValidateImmutableInputs(binding, profile, confirmation);
        if (!string.Equals(attempt.PlanHash, immutable.Plan.PlanHash, StringComparison.Ordinal) ||
            !string.Equals(attempt.DescriptorSha256, profile.DescriptorSha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.TemplateBundleSha256, profile.TemplateBundleSha256, StringComparison.Ordinal) ||
            !string.Equals(attempt.PlanningBundleSha256, profile.PlanningBundleSha256, StringComparison.Ordinal) ||
            !PathEquals(attempt.CanonicalTargetPath, binding.CanonicalPath))
            throw new RepositoryProvisioningIntegrityException(
                "The durable provisioning attempt failed immutable input verification.");
        return new ProvisioningClaim(
            operation.Id,
            attempt.Id,
            attempt.AttemptNumber,
            attempt.StartedAtUtc,
            binding,
            profile,
            confirmation,
            immutable.Plan,
            immutable.Bundle,
            project,
            Replay: null);
    }

    private async Task<RepositoryProvisioningResult> FinalizeAsync(
        ProvisionRepositoryCommand command,
        ProvisioningClaim claim,
        RepositoryProvisioningExecutionEvidence evidence,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(connection, transaction, command, requireContributor: false, cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();
            if (!await CanAccessProjectAsync(connection, transaction, command, requireContributor: true, cancellationToken))
                throw new RepositoryProvisioningForbiddenException();
            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command, cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var operation = await ReadOperationAsync(connection, transaction, command, cancellationToken)
                ?? throw new RepositoryProvisioningIntegrityException("The provisioning operation disappeared.");
            if (!string.Equals(operation.PayloadHash, PayloadHash(command), StringComparison.Ordinal))
                throw new ProjectStartOperationMismatchException();
            if (operation.Status == "Completed")
            {
                var replay = ReadReplay(operation);
                transaction.Commit();
                return replay with { IsReplay = true };
            }
            if (operation.Status != "Pending")
                throw new RepositoryProvisioningIntegrityException(
                    "The provisioning operation is not pending finalization.");

            var attempt = await ReadAttemptByOperationAsync(
                connection, transaction, claim.OperationRecordId, cancellationToken)
                ?? throw new RepositoryProvisioningIntegrityException("The provisioning attempt disappeared.");
            var binding = await ReadBindingAsync(connection, transaction, command, lockRows: true, cancellationToken)
                ?? throw new RepositoryProvisioningIntegrityException("The provisioning binding disappeared.");
            var profile = await ReadProfileAsync(connection, transaction, command, claim.Profile.Id,
                lockRows: true, cancellationToken, identifierIsProfileId: true)
                ?? throw new RepositoryProvisioningIntegrityException("The provisioning profile disappeared.");
            var project = await ReadProjectStateAsync(connection, transaction, command, lockRows: true, cancellationToken)
                ?? throw new WorkbenchProjectNotAccessibleException();
            if (attempt.Id != claim.AttemptId || attempt.State != RepositoryProvisioningStates.Provisioning ||
                binding.Id != claim.Binding.Id || binding.BindingState != RepositoryBindingStates.Provisioning ||
                binding.Revision != command.ExpectedRepositoryBindingRevision + 1 ||
                profile.Revision != command.ExpectedExecutionProfileRevision ||
                !PathEquals(binding.CanonicalPath, evidence.CanonicalPath) ||
                !string.Equals(attempt.PlanHash, claim.Plan.PlanHash, StringComparison.Ordinal))
                throw new RepositoryProvisioningStaleException();

            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: cancellationToken));
            var qualifiedBinding = binding with
            {
                Revision = binding.Revision + 1,
                BindingState = RepositoryBindingStates.Qualified,
                DefaultBranch = evidence.BranchName,
                BaselineCommit = evidence.BaselineCommit
            };
            await UpdateBindingWithRevisionAsync(
                connection, transaction, command, qualifiedBinding, "Qualified", now, cancellationToken);

            var projected = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.Projects
                SET LocalPath=@CanonicalPath
                WHERE TenantId=@TenantId AND Id=@ProjectId
                  AND (LocalPath IS NULL OR LTRIM(RTRIM(LocalPath))=N'' OR LocalPath=@CanonicalPath);
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    CanonicalPath = qualifiedBinding.CanonicalPath
                },
                transaction,
                cancellationToken: cancellationToken));
            if (projected != 1)
                throw new RepositoryProvisioningIntegrityException(
                    "The legacy repository-path projection conflicted with qualified authority.");

            var readinessRevision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                SELECT COALESCE(MAX(Revision), 0) + 1
                FROM dbo.ProjectReadinessAssessments WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId=@TenantId AND ProjectId=@ProjectId;
                """,
                new { command.TenantId, command.ProjectId },
                transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.ProjectReadinessAssessments
                    (TenantId, ProjectId, Revision, ExecutionReadiness, ReasonCode,
                     Summary, AssessedByActorUserId, AssessedAtUtc)
                VALUES
                    (@TenantId, @ProjectId, @Revision, N'ValidationRequired', @ReasonCode,
                     @Summary, @ActorUserId, @AssessedAtUtc);
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    Revision = readinessRevision,
                    ReasonCode = RepositorySetupReasonCodes.RepositoryTechnicalValidationPending,
                    Summary = "Repository provisioning completed. Technical restore, build, test, indexing, sandbox, and Builder readiness have not run.",
                    ActorUserId = command.ActorUserId,
                    AssessedAtUtc = now
                },
                transaction,
                cancellationToken: cancellationToken));

            var receiptId = Guid.NewGuid();
            var receiptJson = RepositorySetupCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                receiptId,
                command.ProjectId,
                attemptId = claim.AttemptId,
                setupConfirmationId = claim.Confirmation.Id,
                repositoryBindingId = qualifiedBinding.Id,
                projectExecutionProfileId = profile.Id,
                planHash = claim.Plan.PlanHash,
                branchName = evidence.BranchName,
                baselineCommit = evidence.BaselineCommit,
                manifestSha256 = evidence.ManifestSha256,
                gitTreeId = evidence.GitTreeId,
                evidence.PublishedAtUtc
            });
            var receiptHash = RepositorySetupCanonicalJson.Sha256(receiptJson);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.RepositoryProvisioningReceipts
                    (Id, TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                     ProvisioningAttemptId, SetupConfirmationId, ActorUserId, BranchName, BaselineCommit,
                     PlanHash, ManifestSha256, GitTreeId, ManifestJson, ReceiptJson,
                     ReceiptSha256, PublishedAtUtc, RecordedAtUtc)
                VALUES
                    (@Id, @TenantId, @ProjectId, @RepositoryBindingId, @ProjectExecutionProfileId,
                     @ProvisioningAttemptId, @SetupConfirmationId, @ActorUserId, @BranchName, @BaselineCommit,
                     @PlanHash, @ManifestSha256, @GitTreeId, @ManifestJson, @ReceiptJson,
                     @ReceiptSha256, CONVERT(datetime2(7), @PublishedAtUtcText, 127), @RecordedAtUtc);

                UPDATE dbo.RepositoryProvisioningAttempts
                SET State=N'Qualified', CompletedAtUtc=@RecordedAtUtc
                WHERE Id=@ProvisioningAttemptId AND TenantId=@TenantId AND ProjectId=@ProjectId
                  AND State=N'Provisioning';
                """,
                new
                {
                    Id = receiptId,
                    command.TenantId,
                    command.ProjectId,
                    RepositoryBindingId = qualifiedBinding.Id,
                    ProjectExecutionProfileId = profile.Id,
                    ProvisioningAttemptId = claim.AttemptId,
                    SetupConfirmationId = claim.Confirmation.Id,
                    ActorUserId = command.ActorUserId,
                    evidence.BranchName,
                    evidence.BaselineCommit,
                    PlanHash = claim.Plan.PlanHash,
                    evidence.ManifestSha256,
                    evidence.GitTreeId,
                    evidence.ManifestJson,
                    ReceiptJson = receiptJson,
                    ReceiptSha256 = receiptHash,
                    PublishedAtUtcText = evidence.PublishedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    RecordedAtUtc = now
                },
                transaction,
                cancellationToken: cancellationToken));

            var result = new RepositoryProvisioningResult(
                command.ProjectId,
                claim.AttemptId,
                receiptId,
                command.ClientOperationId,
                false,
                project.ProjectLifecyclePhase,
                ProjectExecutionReadinessStates.ValidationRequired,
                RepositorySetupReasonCodes.RepositoryTechnicalValidationPending,
                qualifiedBinding.CanonicalPath,
                qualifiedBinding,
                profile,
                evidence.BranchName,
                evidence.BaselineCommit,
                evidence.ManifestSha256,
                evidence.GitTreeId);
            var resultJson = RepositorySetupCanonicalJson.Serialize(result);
            var resultHash = RepositorySetupCanonicalJson.Sha256(resultJson);
            var eventPayload = RepositorySetupCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                command.ProjectId,
                attemptId = claim.AttemptId,
                receiptId,
                repositoryBindingId = qualifiedBinding.Id,
                projectExecutionProfileId = profile.Id,
                bindingRevision = qualifiedBinding.Revision,
                bindingState = qualifiedBinding.BindingState,
                branchName = evidence.BranchName,
                baselineCommit = evidence.BaselineCommit,
                manifestSha256 = evidence.ManifestSha256,
                gitTreeId = evidence.GitTreeId,
                executionReadiness = ProjectExecutionReadinessStates.ValidationRequired,
                readinessReasonCode = RepositorySetupReasonCodes.RepositoryTechnicalValidationPending
            });
            await InsertOutboxAsync(
                connection, transaction, command, "RepositoryProvisioningQualified", eventPayload, now, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.UserMutationAttribution
                    (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId,
                     TimestampUtc, SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
                VALUES
                    (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                     CONVERT(NVARCHAR(128), @ClientOperationId), CONVERT(NVARCHAR(128), @AttemptId), @TimestampUtc,
                     N'Workbench', N'IronDev.Api', N'POST', @Route, N'Completed', 200);

                UPDATE dbo.ClientOperations
                SET Status=N'Completed', CanonicalResultJson=@ResultJson, ResultHash=@ResultHash,
                    ResultRepositoryProvisioningAttemptId=@AttemptId,
                    ResultRepositoryProvisioningReceiptId=@ReceiptId,
                    CompletedAtUtc=@TimestampUtc
                WHERE Id=@OperationRecordId AND Status=N'Pending';
                """,
                new
                {
                    command.ActorUserId,
                    command.TenantId,
                    command.ProjectId,
                    command.ClientOperationId,
                    TimestampUtc = now,
                    Route,
                    ResultJson = resultJson,
                    ResultHash = resultHash,
                    AttemptId = claim.AttemptId,
                    ReceiptId = receiptId,
                    OperationRecordId = claim.OperationRecordId
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

    private async Task RecordFailureAsync(
        ProvisionRepositoryCommand command,
        ProvisioningClaim claim,
        RepositoryProvisioningExecutionException failure,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (!await CanAccessProjectAsync(connection, transaction, command, requireContributor: false, cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();
            if (!await CanAccessProjectAsync(connection, transaction, command, requireContributor: true, cancellationToken))
                throw new RepositoryProvisioningForbiddenException();
            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command, cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var attempt = await ReadAttemptByOperationAsync(
                connection, transaction, claim.OperationRecordId, cancellationToken);
            if (attempt is null || attempt.State != RepositoryProvisioningStates.Provisioning)
            {
                transaction.Commit();
                return;
            }
            var binding = await ReadBindingAsync(connection, transaction, command, lockRows: true, cancellationToken);
            if (binding is null || binding.Id != claim.Binding.Id ||
                binding.BindingState != RepositoryBindingStates.Provisioning ||
                binding.Revision != command.ExpectedRepositoryBindingRevision + 1)
                throw new RepositoryProvisioningIntegrityException(
                    "A failed provisioning attempt no longer owns the binding state.");
            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: cancellationToken));
            var failedBinding = binding with
            {
                Revision = binding.Revision + 1,
                BindingState = RepositoryBindingStates.ProvisioningFailed
            };
            await UpdateBindingWithRevisionAsync(
                connection, transaction, command, failedBinding, "ProvisioningFailed", now, cancellationToken);
            var failureEvidence = RepositorySetupCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                stage = "ProvisioningExecution",
                reasonCode = failure.ReasonCode,
                retry = "Create a new client operation from the current failed binding revision."
            });
            var failureEnvelope = RepositorySetupCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                error = RepositoryProvisioningExecutionException.ErrorCode,
                reasonCode = failure.ReasonCode,
                message = failure.Message,
                attemptId = claim.AttemptId
            });
            var failureHash = RepositorySetupCanonicalJson.Sha256(failureEnvelope);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.RepositoryProvisioningAttempts
                SET State=N'ProvisioningFailed', FailureCode=@FailureCode,
                    FailureEvidenceJson=@FailureEvidenceJson, CompletedAtUtc=@CompletedAtUtc
                WHERE Id=@AttemptId AND TenantId=@TenantId AND ProjectId=@ProjectId
                  AND State=N'Provisioning';

                UPDATE dbo.ClientOperations
                SET Status=N'Failed', CanonicalResultJson=@FailureEnvelope,
                    ResultHash=@FailureHash,
                    ResultRepositoryProvisioningAttemptId=@AttemptId,
                    CompletedAtUtc=@CompletedAtUtc
                WHERE Id=@OperationRecordId AND Status=N'Pending';
                """,
                new
                {
                    FailureCode = failure.ReasonCode,
                    FailureEvidenceJson = failureEvidence,
                    CompletedAtUtc = now,
                    AttemptId = claim.AttemptId,
                    command.TenantId,
                    command.ProjectId,
                    FailureEnvelope = failureEnvelope,
                    FailureHash = failureHash,
                    OperationRecordId = claim.OperationRecordId
                },
                transaction,
                cancellationToken: cancellationToken));
            var eventPayload = RepositorySetupCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                command.ProjectId,
                attemptId = claim.AttemptId,
                repositoryBindingId = failedBinding.Id,
                bindingRevision = failedBinding.Revision,
                bindingState = failedBinding.BindingState,
                reasonCode = failure.ReasonCode
            });
            await InsertOutboxAsync(
                connection, transaction, command, "RepositoryProvisioningFailed", eventPayload, now, cancellationToken);
            await InsertAttributionAsync(
                connection, transaction, command, claim.AttemptId, "Failed", 422, now, cancellationToken);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private (RepositorySetupPlanPreview Plan, RepositorySetupTemplateBundle Bundle) ValidateImmutableInputs(
        RepositoryBindingSnapshot binding,
        ProjectExecutionProfileSnapshot profile,
        ConfirmationRow confirmation)
    {
        RepositorySetupPlanPreview plan;
        try
        {
            plan = JsonSerializer.Deserialize<RepositorySetupPlanPreview>(confirmation.PlanJson, JsonOptions)
                   ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new RepositoryProvisioningIntegrityException(
                "The immutable repository setup plan is unreadable.", exception);
        }
        if (!string.Equals(plan.PlanHash, confirmation.PlanHash, StringComparison.Ordinal) ||
            !string.Equals(RepositorySetupPlanCodec.ComputeHash(plan), confirmation.PlanHash, StringComparison.Ordinal) ||
            plan.ProjectId != binding.ProjectId || plan.State != RepositorySetupPreviewStates.ReadyForConfirmation ||
            !PathEquals(plan.TargetPath, binding.CanonicalPath) ||
            !string.Equals(plan.Profile.ProfileDefinitionId, profile.ProfileDefinitionId, StringComparison.Ordinal) ||
            plan.ProfileDescriptorRevision != profile.ProfileDescriptorRevision ||
            !string.Equals(plan.ProfileDescriptorSha256, profile.DescriptorSha256, StringComparison.Ordinal) ||
            !string.Equals(plan.TemplateBundleSha256, profile.TemplateBundleSha256, StringComparison.Ordinal) ||
            !string.Equals(plan.PlanningBundleSha256, profile.PlanningBundleSha256, StringComparison.Ordinal) ||
            !string.Equals(plan.SolutionPath, profile.SolutionPath, StringComparison.Ordinal) ||
            !string.Equals(plan.AppProjectPath, profile.AppProjectPath, StringComparison.Ordinal) ||
            !string.Equals(plan.TestProjectPath, profile.TestProjectPath, StringComparison.Ordinal) ||
            !string.Equals(plan.RestoreCommand, profile.RestoreCommand, StringComparison.Ordinal) ||
            !string.Equals(plan.BuildCommand, profile.BuildCommand, StringComparison.Ordinal) ||
            !string.Equals(plan.TestCommand, profile.TestCommand, StringComparison.Ordinal))
            throw new RepositoryProvisioningIntegrityException(
                "The immutable setup plan no longer matches its exact repository authorities.");

        var descriptor = _catalog.Find(
            profile.ProfileDefinitionId,
            profile.ProfileDescriptorRevision,
            profile.DescriptorSha256)
            ?? throw new RepositoryProvisioningIntegrityException(
                "The exact confirmed profile revision is no longer in the server catalog.");
        if (!string.Equals(descriptor.TemplateBundleSha256, profile.TemplateBundleSha256, StringComparison.Ordinal) ||
            !string.Equals(
                RepositorySetupTemplateBundleCodec.ComputeHash(descriptor.TemplateBundle),
                profile.TemplateBundleSha256,
                StringComparison.Ordinal))
            throw new RepositoryProvisioningIntegrityException(
                "The exact confirmed template bundle failed catalog verification.");
        _ = RepositorySetupTemplateBundleRenderer.Render(descriptor.TemplateBundle, plan);
        return (plan, descriptor.TemplateBundle);
    }

    private RepositoryProvisioningExecutionRequest ToExecutionRequest(ProvisioningClaim claim) => new(
        claim.AttemptId,
        _approvedWorkspaceRoot,
        claim.Plan,
        claim.Bundle,
        claim.StartedAtUtc);

    private static async Task UpdateBindingWithRevisionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        RepositoryBindingSnapshot binding,
        string changeKind,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var snapshotJson = RepositorySetupCanonicalJson.Serialize(binding);
        var snapshotHash = RepositorySetupCanonicalJson.Sha256(snapshotJson);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.RepositoryBindings
            SET CurrentRevision=@Revision, BindingState=@BindingState,
                DefaultBranch=@DefaultBranch, BaselineCommit=@BaselineCommit,
                UpdatedAtUtc=@UpdatedAtUtc
            WHERE Id=@Id AND TenantId=@TenantId AND ProjectId=@ProjectId
              AND CurrentRevision=@PreviousRevision;

            INSERT dbo.RepositoryBindingRevisions
                (TenantId, ProjectId, RepositoryBindingId, Revision, SnapshotJson,
                 SnapshotHash, ActorUserId, ChangeKind, CreatedAtUtc)
            VALUES
                (@TenantId, @ProjectId, @Id, @Revision, @SnapshotJson,
                 @SnapshotHash, @ActorUserId, @ChangeKind, @UpdatedAtUtc);
            """,
            new
            {
                binding.Id,
                command.TenantId,
                command.ProjectId,
                binding.Revision,
                binding.BindingState,
                binding.DefaultBranch,
                binding.BaselineCommit,
                UpdatedAtUtc = now,
                PreviousRevision = binding.Revision - 1,
                SnapshotJson = snapshotJson,
                SnapshotHash = snapshotHash,
                ActorUserId = command.ActorUserId,
                ChangeKind = changeKind
            },
            transaction,
            cancellationToken: cancellationToken));
        if (affected != 2)
            throw new RepositoryProvisioningStaleException();
    }

    private static Task InsertOutboxAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        string eventKind,
        string payload,
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
                command.TenantId,
                command.ProjectId,
                command.WorkbenchSessionId,
                EventKind = eventKind,
                PayloadJson = payload,
                command.ClientOperationId,
                OccurredAtUtc = occurredAtUtc
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task InsertAttributionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        Guid attemptId,
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
                TimestampUtc = timestampUtc,
                Route,
                Phase = phase,
                StatusCode = statusCode
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<bool> CanAccessProjectAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
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
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
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
        ProvisionRepositoryCommand command,
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
            new
            {
                command.TenantId,
                command.ActorUserId,
                command.ProjectId,
                command.WorkbenchSessionId,
                command.LeaseEpoch
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    private static Task<ProjectStateRow?> ReadProjectStateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var sql = $"""
            SELECT project.Id AS ProjectId, project.LocalPath,
                   COALESCE(lifecycle.Phase, N'Shaping') AS ProjectLifecyclePhase
            FROM dbo.Projects project{hint}
            OUTER APPLY
            (
                SELECT TOP (1) value.Phase
                FROM dbo.ProjectLifecyclePhases value{hint}
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC
            ) lifecycle
            WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
            """;
        return connection.QuerySingleOrDefaultAsync<ProjectStateRow>(new CommandDefinition(
            sql,
            new { command.TenantId, command.ProjectId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<RepositoryBindingSnapshot?> ReadBindingAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var sql = $"""
            SELECT Id, ProjectId, CurrentRevision AS Revision, RepositoryKind,
                   CanonicalPath, BindingState, DefaultBranch, BaselineCommit,
                   CreatedByActorUserId, ConfirmedAtUtc
            FROM dbo.RepositoryBindings{hint}
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId;
            """;
        return connection.QuerySingleOrDefaultAsync<RepositoryBindingSnapshot>(new CommandDefinition(
            sql,
            new { command.TenantId, command.ProjectId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<ProjectExecutionProfileSnapshot?> ReadProfileAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        Guid identifier,
        bool lockRows,
        CancellationToken cancellationToken,
        bool identifierIsProfileId = false)
    {
        var hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var identityPredicate = identifierIsProfileId ? "Id=@Identifier" : "RepositoryBindingId=@Identifier";
        var sql = $"""
            SELECT Id, ProjectId, CurrentRevision AS Revision, RepositoryBindingId,
                   ProfileDefinitionId, ProfileDescriptorRevision, DescriptorSha256,
                   TemplateBundleSha256, PlanningBundleSha256, TargetFramework, Language,
                   ApplicationKind, TestFramework, SdkVersion, RuntimeVersion,
                   SolutionPath, AppProjectPath, TestProjectPath, RestoreCommand, BuildCommand,
                   TestCommand, ToolchainManifestId, ExecutionImageReference,
                   PlanningReadiness, CertificationState
            FROM dbo.ProjectExecutionProfiles{hint}
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND {identityPredicate};
            """;
        return connection.QuerySingleOrDefaultAsync<ProjectExecutionProfileSnapshot>(new CommandDefinition(
            sql,
            new { command.TenantId, command.ProjectId, Identifier = identifier },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<ConfirmationRow?> ReadConfirmationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        CancellationToken cancellationToken) => ReadConfirmationByIdAsync(
            connection, transaction, command, command.SetupConfirmationId, cancellationToken);

    private static Task<ConfirmationRow?> ReadConfirmationByIdAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        Guid confirmationId,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<ConfirmationRow>(new CommandDefinition(
            """
            SELECT Id, RepositoryBindingId, ProjectExecutionProfileId, PlanHash,
                   PlanJson, ConfirmedAtUtc
            FROM dbo.RepositorySetupConfirmations WITH (HOLDLOCK)
            WHERE Id=@ConfirmationId AND TenantId=@TenantId AND ProjectId=@ProjectId;
            """,
            new { ConfirmationId = confirmationId, command.TenantId, command.ProjectId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ProvisionRepositoryCommand command,
        CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
            """
            SELECT Id, ActorUserId, PayloadHash, Status, CanonicalResultJson, ResultHash
            FROM dbo.ClientOperations WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId
              AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
              AND ClientOperationId=@ClientOperationId;
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                OperationKind = RepositorySetupOperationKinds.Provision,
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
            SELECT Id, RepositoryBindingId, ProjectExecutionProfileId, SetupConfirmationId,
                   ClientOperationId, AttemptNumber, ExpectedBindingRevision,
                   ExpectedExecutionProfileRevision, PlanHash, DescriptorSha256,
                   TemplateBundleSha256, PlanningBundleSha256, CanonicalTargetPath,
                   StagingPath, State, StartedAtUtc, CompletedAtUtc, FailureCode
            FROM dbo.RepositoryProvisioningAttempts WITH (UPDLOCK, HOLDLOCK)
            WHERE ClientOperationRecordId=@OperationRecordId;
            """,
            new { OperationRecordId = operationRecordId },
            transaction,
            cancellationToken: cancellationToken));

    private static RepositoryProvisioningResult ReadReplay(ClientOperationRow operation)
    {
        VerifyStoredResult(operation);
        return JsonSerializer.Deserialize<RepositoryProvisioningResult>(operation.CanonicalResultJson!, JsonOptions)
               ?? throw new RepositoryProvisioningIntegrityException(
                   "The stored provisioning result is unreadable.");
    }

    private static RepositoryProvisioningExecutionException ReadFailure(ClientOperationRow operation)
    {
        VerifyStoredResult(operation);
        var envelope = JsonSerializer.Deserialize<FailureEnvelope>(operation.CanonicalResultJson!, JsonOptions)
                       ?? throw new RepositoryProvisioningIntegrityException(
                           "The stored provisioning failure is unreadable.");
        return new RepositoryProvisioningExecutionException(envelope.ReasonCode, envelope.Message);
    }

    private static void VerifyStoredResult(ClientOperationRow operation)
    {
        if (string.IsNullOrWhiteSpace(operation.CanonicalResultJson) ||
            !IsLowerHexSha256(operation.ResultHash ?? string.Empty) ||
            !string.Equals(
                RepositorySetupCanonicalJson.Sha256(operation.CanonicalResultJson),
                operation.ResultHash,
                StringComparison.Ordinal))
            throw new RepositoryProvisioningIntegrityException(
                "The stored provisioning operation result failed integrity verification.");
    }

    private static RepositoryProvisioningExecutionException ToFailure(Exception exception) => exception switch
    {
        RepositoryProvisioningExecutionException value => value,
        RepositoryProvisioningIntegrityException => new RepositoryProvisioningExecutionException(
            RepositoryProvisioningFailureCodes.TemplateIntegrityFailed,
            "The immutable repository provisioning inputs failed integrity validation."),
        RepositorySetupIntegrityException => new RepositoryProvisioningExecutionException(
            RepositoryProvisioningFailureCodes.TemplateIntegrityFailed,
            "The pinned repository template failed integrity validation."),
        _ => new RepositoryProvisioningExecutionException(
            RepositoryProvisioningFailureCodes.UnexpectedFailure,
            "Repository provisioning stopped before it could publish safely.")
    };

    private static string ResourceScope(int projectId) => $"project:{projectId}:repository-provisioning";

    private async Task EnsurePreLockAccessAsync(
        ProvisionRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        var role = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT member.ProjectRole
            FROM dbo.Projects project
            INNER JOIN dbo.ProjectMembers member
                ON member.TenantId=project.TenantId AND member.ProjectId=project.Id
               AND member.UserId=@ActorUserId AND member.Status=N'Active'
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=project.TenantId AND tenantMember.UserId=@ActorUserId
            INNER JOIN dbo.Users actor ON actor.Id=@ActorUserId AND actor.IsActive=1
            WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
            """,
            new { command.TenantId, command.ActorUserId, command.ProjectId },
            cancellationToken: cancellationToken));
        if (string.IsNullOrWhiteSpace(role))
            throw new WorkbenchProjectNotAccessibleException();
        if (role is not ("Owner" or "Contributor"))
            throw new RepositoryProvisioningForbiddenException();
    }

    private async Task<IDisposable> AcquireDistributedOperationLockAsync(
        ProvisionRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        var connection = _connections.CreateConnection();
        try
        {
            connection.Open();
            var result = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                DECLARE @result INT;
                EXEC @result = sys.sp_getapplock
                    @Resource=@Resource,
                    @LockMode=N'Exclusive',
                    @LockOwner=N'Session',
                    @LockTimeout=0,
                    @DbPrincipal=N'public';
                SELECT @result;
                """,
                new { Resource = $"IronDev:RepositoryProvisioning:{command.TenantId}:{command.ProjectId}" },
                cancellationToken: cancellationToken));
            if (result < 0)
                throw new RepositoryProvisioningInProgressException();
            return new DistributedOperationLock(connection,
                $"IronDev:RepositoryProvisioning:{command.TenantId}:{command.ProjectId}");
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string PayloadHash(ProvisionRepositoryCommand command) => RepositorySetupCanonicalJson.Sha256(
        $"repository-provision-v1\n{command.ProjectId}\n{command.SetupConfirmationId:D}\n" +
        $"{command.ExpectedRepositoryBindingRevision}\n{command.ExpectedExecutionProfileRevision}");

    private static void Validate(ProvisionRepositoryCommand command)
    {
        if (command.TenantId <= 0 || command.ActorUserId <= 0 || command.ProjectId <= 0 ||
            command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty || command.SetupConfirmationId == Guid.Empty ||
            command.ExpectedRepositoryBindingRevision <= 0 || command.ExpectedExecutionProfileRevision <= 0)
            throw new RepositorySetupValidationException(
                "A current Workbench fence, client operation, setup confirmation, and exact authority revisions are required.");
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool PathEquals(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);

    private sealed record ProvisioningClaim(
        long OperationRecordId,
        Guid AttemptId,
        int AttemptNumber,
        DateTime StartedAtUtc,
        RepositoryBindingSnapshot Binding,
        ProjectExecutionProfileSnapshot Profile,
        ConfirmationRow Confirmation,
        RepositorySetupPlanPreview Plan,
        RepositorySetupTemplateBundle Bundle,
        ProjectStateRow Project,
        RepositoryProvisioningResult? Replay)
    {
        public static ProvisioningClaim ForReplay(RepositoryProvisioningResult replay) => new(
            0, Guid.Empty, 0, default, null!, null!, null!, null!, null!, null!, replay);
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
        public Guid SetupConfirmationId { get; init; }
        public Guid ClientOperationId { get; init; }
        public int AttemptNumber { get; init; }
        public long ExpectedBindingRevision { get; init; }
        public long ExpectedExecutionProfileRevision { get; init; }
        public string PlanHash { get; init; } = string.Empty;
        public string DescriptorSha256 { get; init; } = string.Empty;
        public string TemplateBundleSha256 { get; init; } = string.Empty;
        public string PlanningBundleSha256 { get; init; } = string.Empty;
        public string CanonicalTargetPath { get; init; } = string.Empty;
        public string StagingPath { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public DateTime StartedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public string? FailureCode { get; init; }
    }

    private sealed class ConfirmationRow
    {
        public Guid Id { get; init; }
        public Guid RepositoryBindingId { get; init; }
        public Guid ProjectExecutionProfileId { get; init; }
        public string PlanHash { get; init; } = string.Empty;
        public string PlanJson { get; init; } = string.Empty;
        public DateTime ConfirmedAtUtc { get; init; }
    }

    private sealed class ProjectStateRow
    {
        public int ProjectId { get; init; }
        public string? LocalPath { get; init; }
        public string ProjectLifecyclePhase { get; init; } = ProjectLifecyclePhases.Shaping;
    }

    private sealed record FailureEnvelope(string ReasonCode, string Message);

    private sealed class DistributedOperationLock : IDisposable
    {
        private IDbConnection? _connection;
        private readonly string _resource;

        public DistributedOperationLock(IDbConnection connection, string resource)
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
                        @Resource=@Resource,
                        @LockOwner=N'Session',
                        @DbPrincipal=N'public';
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
