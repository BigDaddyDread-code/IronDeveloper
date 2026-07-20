using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

public sealed class WorkbenchRepositorySetupService : IWorkbenchRepositorySetupService
{
    private const string ResourcePolicy =
        "PR-05A records planning authority only. Provisioning must revalidate the isolated sandbox and resource policy before writing files.";
    private const string SandboxValidation =
        "The target is a server-derived direct child of the configured isolated repository root; PR-05B must revalidate before provisioning.";

    private readonly IDbConnectionFactory _connections;
    private readonly IRepositorySetupProfileCatalog _catalog;
    private readonly IRepositorySetupPathPolicy _pathPolicy;
    private readonly IRepositorySetupConfirmationFailureInjector _failureInjector;
    private readonly string _approvedWorkspaceRoot;

    public WorkbenchRepositorySetupService(
        IDbConnectionFactory connections,
        IRepositorySetupProfileCatalog catalog,
        IRepositorySetupPathPolicy pathPolicy,
        IRepositorySetupConfirmationFailureInjector failureInjector,
        IConfiguration configuration)
    {
        _connections = connections;
        _catalog = catalog;
        _pathPolicy = pathPolicy;
        _failureInjector = failureInjector;
        _approvedWorkspaceRoot =
            configuration["WorkbenchRepositorySetup:ApprovedWorkspaceRoot"]?.Trim() ?? string.Empty;
    }

    public async Task<RepositorySetupContext> GetContextAsync(
        GetRepositorySetupContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(query.TenantId, query.ActorUserId, query.ProjectId);
        using var connection = _connections.CreateConnection();
        connection.Open();

        if (!await CanAccessProjectAsync(
                connection, null, query.TenantId, query.ActorUserId, query.ProjectId,
                requireContributor: false, cancellationToken))
            throw new WorkbenchProjectNotAccessibleException();

        var project = await ReadProjectAsync(
                connection, null, query.TenantId, query.ProjectId, lockRows: false, cancellationToken)
            ?? throw new WorkbenchProjectNotAccessibleException();
        var binding = await ReadBindingAsync(
            connection, null, query.TenantId, query.ProjectId, cancellationToken);
        binding ??= RepositoryBindingProjection.CreateLegacy(project.ProjectId, project.LocalPath);
        var profile = binding is null
            ? null
            : await ReadExecutionProfileAsync(
                connection, null, query.TenantId, query.ProjectId, binding.Id, cancellationToken);
        var confirmation = await ReadLatestConfirmationAsync(
            connection, null, query.TenantId, query.ProjectId, cancellationToken);
        var compatibility = EvaluateCompatibility(project.UnderstandingJson);
        var suggestedNames = RepositorySetupSafeNames.FromProject(project.Name, project.ProjectId);
        var environmentPath = _pathPolicy.Assess(
            _approvedWorkspaceRoot,
            suggestedNames.DirectoryName,
            inspectEnvironment: true);
        var environmentCapability = new RepositorySetupEnvironmentCapability(
            environmentPath.IsUnsafe
                ? RepositorySetupEnvironmentCapabilityStates.Unsafe
                : environmentPath.IsAvailable
                    ? RepositorySetupEnvironmentCapabilityStates.Available
                    : RepositorySetupEnvironmentCapabilityStates.Unavailable,
            environmentPath.ReasonCode,
            environmentPath.Message,
            environmentPath.TargetPath);

        return new RepositorySetupContext(
            query.ProjectId,
            query.TenantId,
            project.Name,
            project.ProjectLifecyclePhase,
            project.ExecutionReadiness,
            project.ReadinessReasonCode,
            binding,
            profile,
            confirmation,
            environmentCapability,
            _catalog.GetAll()
                .Select(value => ToSummary(value, compatibility))
                .ToArray());
    }

    public async Task<RepositorySetupPlanPreview> PreviewAsync(
        CreateRepositorySetupPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(command.TenantId, command.ActorUserId, command.ProjectId);
        if (command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            string.IsNullOrWhiteSpace(command.ProfileDefinitionId))
            throw new RepositorySetupValidationException(
                "A current Workbench session, lease epoch, and profile definition are required.");

        using var connection = _connections.CreateConnection();
        connection.Open();
        if (!await CanAccessProjectAsync(
                connection, null, command.TenantId, command.ActorUserId, command.ProjectId,
                requireContributor: false, cancellationToken))
            throw new WorkbenchProjectNotAccessibleException();
        if (!await HasCurrentFenceAsync(
                connection, null, command.TenantId, command.ActorUserId, command.ProjectId,
                command.WorkbenchSessionId, command.LeaseEpoch, cancellationToken))
            throw new WorkbenchLeaseFenceException();

        var project = await ReadProjectAsync(
                connection, null, command.TenantId, command.ProjectId, lockRows: false, cancellationToken)
            ?? throw new WorkbenchProjectNotAccessibleException();
        if (await HasRepositoryBindingAsync(
                connection, null, command.TenantId, command.ProjectId, cancellationToken) ||
            !string.IsNullOrWhiteSpace(project.LocalPath))
            throw new RepositorySetupAlreadyBoundException();

        var descriptor = _catalog.Find(command.ProfileDefinitionId.Trim());
        if (descriptor is null)
            throw new RepositorySetupUnsupportedProfileException(command.ProfileDefinitionId.Trim());

        return BuildPlan(
            project,
            command.WorkbenchSessionId,
            command.LeaseEpoch,
            descriptor,
            inspectEnvironment: true);
    }

    public async Task<RepositorySetupConfirmationResult> ConfirmAsync(
        ConfirmRepositorySetupCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(command.TenantId, command.ActorUserId, command.ProjectId);
        if (command.WorkbenchSessionId <= 0 || command.LeaseEpoch <= 0 ||
            command.ClientOperationId == Guid.Empty ||
            !IsLowerHexSha256(command.ExpectedPlanHash))
            throw new RepositorySetupValidationException(
                "A current Workbench fence, client operation ID, and exact setup-plan hash are required.");

        var resourceScope = $"project:{command.ProjectId}:repository-setup";
        var payloadHash = RepositorySetupCanonicalJson.Sha256(
            $"repository-setup-confirm-v1\n{command.ProjectId}\n{command.WorkbenchSessionId}\n" +
            $"{command.LeaseEpoch}\n{command.ExpectedPlanHash}");
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            if (!await CanAccessProjectAsync(
                    connection, transaction, command.TenantId, command.ActorUserId,
                    command.ProjectId, requireContributor: false, cancellationToken))
                throw new WorkbenchProjectNotAccessibleException();
            if (!await CanAccessProjectAsync(
                    connection, transaction, command.TenantId, command.ActorUserId,
                    command.ProjectId, requireContributor: true, cancellationToken))
                throw new RepositorySetupForbiddenException();

            var operation = await ReadOperationAsync(
                connection, transaction, command, resourceScope, cancellationToken);
            if (operation is not null)
            {
                if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new ProjectStartOperationMismatchException();
                var replay = ReadReplay(operation);
                transaction.Commit();
                return replay with { IsReplay = true };
            }

            if (!await ValidateAndRenewLeaseAsync(connection, transaction, command, cancellationToken))
                throw new WorkbenchLeaseFenceException();

            var project = await ReadProjectAsync(
                    connection, transaction, command.TenantId, command.ProjectId,
                    lockRows: true, cancellationToken)
                ?? throw new WorkbenchProjectNotAccessibleException();
            if (await HasRepositoryBindingAsync(
                    connection, transaction, command.TenantId, command.ProjectId, cancellationToken) ||
                !string.IsNullOrWhiteSpace(project.LocalPath))
                throw new RepositorySetupAlreadyBoundException();

            RepositorySetupPlanPreview? plan = null;
            RepositorySetupProfileDescriptor? selectedDescriptor = null;
            foreach (var descriptor in _catalog.GetAll())
            {
                var candidate = BuildPlan(
                    project,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    descriptor,
                    inspectEnvironment: true);
                if (string.Equals(candidate.PlanHash, command.ExpectedPlanHash, StringComparison.Ordinal))
                {
                    plan = candidate;
                    selectedDescriptor = descriptor;
                    break;
                }
            }

            if (plan is null || selectedDescriptor is null)
                throw new RepositorySetupPlanChangedException();
            if (plan.State != RepositorySetupPreviewStates.ReadyForConfirmation)
                throw new RepositorySetupPlanNotConfirmableException(plan.Message);

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
                    OperationKind = RepositorySetupOperationKinds.Confirm,
                    ResourceScopeId = resourceScope,
                    command.ClientOperationId,
                    PayloadHash = payloadHash,
                    command.ProjectId,
                    command.WorkbenchSessionId
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(RepositorySetupConfirmationFailurePoint.ClientOperationCreated);

            var now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT SYSUTCDATETIME();",
                transaction: transaction,
                cancellationToken: cancellationToken));
            var bindingId = Guid.NewGuid();
            var profileId = Guid.NewGuid();
            var confirmationId = Guid.NewGuid();
            var binding = new RepositoryBindingSnapshot(
                bindingId,
                command.ProjectId,
                1,
                RepositoryKinds.Greenfield,
                plan.TargetPath,
                RepositoryBindingStates.SetupConfirmed,
                "main",
                null,
                command.ActorUserId,
                now);
            var executionProfile = new ProjectExecutionProfileSnapshot(
                profileId,
                command.ProjectId,
                1,
                bindingId,
                selectedDescriptor.ProfileDefinitionId,
                selectedDescriptor.Revision,
                selectedDescriptor.DescriptorSha256,
                plan.TemplateBundleSha256,
                plan.PlanningBundleSha256,
                plan.TargetFramework,
                plan.Language,
                plan.ApplicationKind,
                plan.TestFramework,
                plan.SdkVersion,
                plan.RuntimeVersion,
                plan.SolutionPath,
                plan.AppProjectPath,
                plan.TestProjectPath,
                plan.RestoreCommand,
                plan.BuildCommand,
                plan.TestCommand,
                plan.ToolchainManifestId,
                plan.ExecutionImageReference,
                selectedDescriptor.PlanningReadiness,
                selectedDescriptor.CertificationState);

            var bindingJson = RepositorySetupCanonicalJson.Serialize(binding);
            var bindingHash = RepositorySetupCanonicalJson.Sha256(bindingJson);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.RepositoryBindings
                    (Id, TenantId, ProjectId, CurrentRevision, RepositoryKind,
                     CanonicalPath, BindingState, DefaultBranch, BaselineCommit,
                     CreatedByActorUserId, ConfirmedAtUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@Id, @TenantId, @ProjectId, 1, @RepositoryKind,
                     @CanonicalPath, @BindingState, @DefaultBranch, NULL,
                     @CreatedByActorUserId, @ConfirmedAtUtc, @ConfirmedAtUtc, @ConfirmedAtUtc);

                INSERT dbo.RepositoryBindingRevisions
                    (TenantId, ProjectId, RepositoryBindingId, Revision, SnapshotJson,
                     SnapshotHash, ActorUserId, ChangeKind, CreatedAtUtc)
                VALUES
                    (@TenantId, @ProjectId, @Id, 1, @SnapshotJson,
                     @SnapshotHash, @CreatedByActorUserId, N'SetupConfirmed', @ConfirmedAtUtc);
                """,
                new
                {
                    binding.Id,
                    command.TenantId,
                    command.ProjectId,
                    binding.RepositoryKind,
                    binding.CanonicalPath,
                    binding.BindingState,
                    binding.DefaultBranch,
                    binding.CreatedByActorUserId,
                    binding.ConfirmedAtUtc,
                    SnapshotJson = bindingJson,
                    SnapshotHash = bindingHash
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(RepositorySetupConfirmationFailurePoint.BindingCreated);

            var profileJson = RepositorySetupCanonicalJson.Serialize(executionProfile);
            var profileHash = RepositorySetupCanonicalJson.Sha256(profileJson);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.ProjectExecutionProfiles
                    (Id, TenantId, ProjectId, RepositoryBindingId, CurrentRevision,
                     ProfileDefinitionId, DescriptorSha256, TemplateBundleSha256,
                     ProfileDescriptorRevision,
                     PlanningBundleSha256, TargetFramework, Language, ApplicationKind,
                     TestFramework, SdkVersion, RuntimeVersion,
                     SolutionPath, AppProjectPath, TestProjectPath,
                     RestoreCommand, BuildCommand, TestCommand, ToolchainManifestId,
                     ExecutionImageReference, PlanningReadiness, CertificationState,
                     CreatedByActorUserId, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@Id, @TenantId, @ProjectId, @RepositoryBindingId, 1,
                     @ProfileDefinitionId, @DescriptorSha256, @TemplateBundleSha256,
                     @ProfileDescriptorRevision,
                     @PlanningBundleSha256, @TargetFramework, @Language, @ApplicationKind,
                     @TestFramework, @SdkVersion, @RuntimeVersion,
                     @SolutionPath, @AppProjectPath, @TestProjectPath,
                     @RestoreCommand, @BuildCommand, @TestCommand, @ToolchainManifestId,
                     @ExecutionImageReference, @PlanningReadiness, @CertificationState,
                     @ActorUserId, @CreatedAtUtc, @CreatedAtUtc);

                INSERT dbo.ProjectExecutionProfileRevisions
                    (TenantId, ProjectId, ProjectExecutionProfileId, Revision,
                     SnapshotJson, SnapshotHash, ActorUserId, ChangeKind, CreatedAtUtc)
                VALUES
                    (@TenantId, @ProjectId, @Id, 1,
                     @SnapshotJson, @SnapshotHash, @ActorUserId, N'SetupConfirmed', @CreatedAtUtc);
                """,
                new
                {
                    executionProfile.Id,
                    command.TenantId,
                    command.ProjectId,
                    executionProfile.RepositoryBindingId,
                    executionProfile.ProfileDefinitionId,
                    executionProfile.ProfileDescriptorRevision,
                    executionProfile.DescriptorSha256,
                    executionProfile.TemplateBundleSha256,
                    executionProfile.PlanningBundleSha256,
                    executionProfile.TargetFramework,
                    executionProfile.Language,
                    executionProfile.ApplicationKind,
                    executionProfile.TestFramework,
                    executionProfile.SdkVersion,
                    executionProfile.RuntimeVersion,
                    executionProfile.SolutionPath,
                    executionProfile.AppProjectPath,
                    executionProfile.TestProjectPath,
                    executionProfile.RestoreCommand,
                    executionProfile.BuildCommand,
                    executionProfile.TestCommand,
                    executionProfile.ToolchainManifestId,
                    executionProfile.ExecutionImageReference,
                    executionProfile.PlanningReadiness,
                    executionProfile.CertificationState,
                    ActorUserId = command.ActorUserId,
                    CreatedAtUtc = now,
                    SnapshotJson = profileJson,
                    SnapshotHash = profileHash
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(RepositorySetupConfirmationFailurePoint.ExecutionProfileCreated);

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
                    (@TenantId, @ProjectId, @Revision, N'NotConfigured',
                     @ReasonCode, @Summary, @ActorUserId, @AssessedAtUtc);
                """,
                new
                {
                    command.TenantId,
                    command.ProjectId,
                    Revision = readinessRevision,
                    ReasonCode = RepositorySetupReasonCodes.RepositoryProvisioningPending,
                    Summary = "Repository setup is confirmed. Provisioning and technical validation have not run.",
                    ActorUserId = command.ActorUserId,
                    AssessedAtUtc = now
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(RepositorySetupConfirmationFailurePoint.ReadinessCreated);

            var planJson = RepositorySetupCanonicalJson.Serialize(plan);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.RepositorySetupConfirmations
                    (Id, TenantId, ProjectId, RepositoryBindingId, ProjectExecutionProfileId,
                     ActorUserId, WorkbenchSessionId, LeaseEpoch, ClientOperationId,
                     PlanHash, PlanJson, ConfirmedAtUtc)
                VALUES
                    (@Id, @TenantId, @ProjectId, @RepositoryBindingId, @ProjectExecutionProfileId,
                     @ActorUserId, @WorkbenchSessionId, @LeaseEpoch, @ClientOperationId,
                     @PlanHash, @PlanJson, @ConfirmedAtUtc);
                """,
                new
                {
                    Id = confirmationId,
                    command.TenantId,
                    command.ProjectId,
                    RepositoryBindingId = bindingId,
                    ProjectExecutionProfileId = profileId,
                    ActorUserId = command.ActorUserId,
                    command.WorkbenchSessionId,
                    command.LeaseEpoch,
                    command.ClientOperationId,
                    PlanHash = command.ExpectedPlanHash,
                    PlanJson = planJson,
                    ConfirmedAtUtc = now
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(RepositorySetupConfirmationFailurePoint.ConfirmationCreated);

            var result = new RepositorySetupConfirmationResult(
                command.ProjectId,
                confirmationId,
                command.ClientOperationId,
                false,
                project.ProjectLifecyclePhase,
                ProjectExecutionReadinessStates.NotConfigured,
                RepositorySetupReasonCodes.RepositoryProvisioningPending,
                binding,
                executionProfile,
                plan);
            var resultJson = RepositorySetupCanonicalJson.Serialize(result);
            var resultHash = RepositorySetupCanonicalJson.Sha256(resultJson);
            var eventPayload = RepositorySetupCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                command.ProjectId,
                confirmationId,
                repositoryBindingId = bindingId,
                projectExecutionProfileId = profileId,
                planHash = command.ExpectedPlanHash,
                bindingState = RepositoryBindingStates.SetupConfirmed,
                executionReadiness = ProjectExecutionReadinessStates.NotConfigured,
                readinessReasonCode = RepositorySetupReasonCodes.RepositoryProvisioningPending
            });
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.WorkbenchOutboxEvents
                    (EventId, TenantId, ProjectId, WorkbenchSessionId, EventKind,
                     PayloadJson, ClientOperationId, OccurredAtUtc)
                VALUES
                    (NEWID(), @TenantId, @ProjectId, @WorkbenchSessionId,
                     N'RepositorySetupConfirmed', @PayloadJson, @ClientOperationId, @OccurredAtUtc);

                INSERT dbo.UserMutationAttribution
                    (ActorUserId, TenantId, ProjectId, CorrelationId, CausationId,
                     TimestampUtc, SourceSurface, SourceClient, Method, Route, Phase, StatusCode)
                VALUES
                    (@ActorUserId, @TenantId, CONVERT(NVARCHAR(128), @ProjectId),
                     CONVERT(NVARCHAR(128), @ClientOperationId), NULL, @OccurredAtUtc,
                     N'Workbench', N'IronDev.Api', N'POST',
                     N'/api/workbench/projects/{projectId}/repository/setup-confirmations',
                     N'Completed', 200);

                UPDATE dbo.ClientOperations
                SET Status=N'Completed', CanonicalResultJson=@ResultJson,
                    ResultHash=@ResultHash, CompletedAtUtc=@OccurredAtUtc
                WHERE Id=@OperationRecordId AND Status=N'Pending';
                """,
                new
                {
                    command.TenantId,
                    command.ActorUserId,
                    command.ProjectId,
                    command.WorkbenchSessionId,
                    PayloadJson = eventPayload,
                    command.ClientOperationId,
                    OccurredAtUtc = now,
                    ResultJson = resultJson,
                    ResultHash = resultHash,
                    OperationRecordId = operationRecordId
                },
                transaction,
                cancellationToken: cancellationToken));
            _failureInjector.ThrowIfRequested(RepositorySetupConfirmationFailurePoint.OutboxCreated);

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private RepositorySetupPlanPreview BuildPlan(
        ProjectRow project,
        long workbenchSessionId,
        long leaseEpoch,
        RepositorySetupProfileDescriptor descriptor,
        bool inspectEnvironment)
    {
        ValidateDescriptor(descriptor);
        var compatibility = EvaluateCompatibility(project.UnderstandingJson);
        var summary = ToSummary(descriptor, compatibility);
        var names = RepositorySetupSafeNames.FromProject(project.Name, project.ProjectId);
        var compatibilityAllowsPlanning = compatibility.State is
            RepositoryProfileCompatibilityStates.Compatible or
            RepositoryProfileCompatibilityStates.NoPreference;
        var path = compatibilityAllowsPlanning
            ? _pathPolicy.Assess(
                _approvedWorkspaceRoot,
                names.DirectoryName,
                inspectEnvironment)
            : new RepositorySetupPathAssessment(
                false,
                false,
                RepositorySetupReasonCodes.PreferenceNeedsConfirmation,
                compatibility.Reason,
                string.Empty,
                string.Empty);
        if (compatibilityAllowsPlanning && path.IsUnsafe)
            throw new RepositorySetupUnsafePathException(path.Message);

        var solutionPath = Render(descriptor.SolutionPathTemplate, names, string.Empty, string.Empty, string.Empty);
        var appProjectPath = Render(descriptor.AppProjectPathTemplate, names, solutionPath, string.Empty, string.Empty);
        var testProjectPath = Render(descriptor.TestProjectPathTemplate, names, solutionPath, appProjectPath, string.Empty);
        var restore = Render(descriptor.RestoreCommandTemplate, names, solutionPath, appProjectPath, testProjectPath);
        var build = Render(descriptor.BuildCommandTemplate, names, solutionPath, appProjectPath, testProjectPath);
        var test = Render(descriptor.TestCommandTemplate, names, solutionPath, appProjectPath, testProjectPath);
        var planningBundlePayload = RepositorySetupCanonicalJson.Serialize(new
        {
            schemaVersion = 1,
            descriptor.ProfileDefinitionId,
            descriptor.DescriptorSha256,
            descriptor.TemplateBundleSha256,
            descriptor.Revision,
            targetPath = path.TargetPath,
            names.SolutionName,
            names.AppProjectName,
            names.TestProjectName,
            solutionPath,
            appProjectPath,
            testProjectPath,
            restore,
            build,
            test,
            descriptor.ToolchainManifestId,
            descriptor.ExecutionImageReference,
            descriptor.SdkVersion,
            descriptor.RuntimeVersion,
            defaultBranch = "main",
            initializeGit = true,
            indexAfterProvisioning = true,
            sandboxValidation = SandboxValidation,
            resourcePolicy = ResourcePolicy
        });
        var planningBundleHash = RepositorySetupCanonicalJson.Sha256(planningBundlePayload);

        var state = RepositorySetupPreviewStates.ReadyForConfirmation;
        var reason = RepositorySetupReasonCodes.Ready;
        var message = "Review this deterministic planning bundle before explicitly confirming repository setup.";
        if (compatibility.State == RepositoryProfileCompatibilityStates.Incompatible)
        {
            state = RepositorySetupPreviewStates.UnsupportedProfile;
            reason = RepositorySetupReasonCodes.IncompatibleProfile;
            message = compatibility.Reason;
        }
        else if (compatibility.State == RepositoryProfileCompatibilityStates.NeedsConfirmation)
        {
            state = RepositorySetupPreviewStates.NeedsConfirmation;
            reason = RepositorySetupReasonCodes.PreferenceNeedsConfirmation;
            message = compatibility.Reason;
        }
        else if (!path.IsAvailable)
        {
            state = RepositorySetupPreviewStates.EnvironmentUnavailable;
            reason = path.ReasonCode;
            message = path.Message;
        }

        var draft = new RepositorySetupPlanPreview(
            SchemaVersion: 1,
            Source: "ProjectUnderstanding",
            ProjectId: project.ProjectId,
            CanonicalProjectName: project.Name,
            WorkbenchSessionId: workbenchSessionId,
            LeaseEpoch: leaseEpoch,
            BasedOnUnderstandingRevision: project.UnderstandingRevision,
            BasedOnUnderstandingHash: RepositorySetupCanonicalJson.Sha256(project.UnderstandingJson ?? string.Empty),
            ProfileDescriptorRevision: descriptor.Revision,
            ProfileDescriptorSha256: descriptor.DescriptorSha256,
            State: state,
            ReasonCode: reason,
            Message: message,
            Profile: summary,
            TargetPath: path.TargetPath,
            SolutionName: names.SolutionName,
            AppProjectName: names.AppProjectName,
            TestProjectName: names.TestProjectName,
            SolutionPath: solutionPath,
            AppProjectPath: appProjectPath,
            TestProjectPath: testProjectPath,
            TemplateBundleSha256: descriptor.TemplateBundleSha256,
            PlanningBundleSha256: planningBundleHash,
            TargetFramework: descriptor.TargetFramework,
            Language: descriptor.Language,
            ApplicationKind: descriptor.ApplicationKind,
            TestFramework: descriptor.TestFramework,
            SdkVersion: descriptor.SdkVersion,
            RuntimeVersion: descriptor.RuntimeVersion,
            RestoreCommand: restore,
            BuildCommand: build,
            TestCommand: test,
            ToolchainManifestId: descriptor.ToolchainManifestId,
            ExecutionImageReference: descriptor.ExecutionImageReference,
            DefaultBranch: "main",
            InitializeGit: true,
            IndexAfterProvisioning: true,
            SandboxValidation: SandboxValidation,
            ResourcePolicy: ResourcePolicy,
            PlanHash: string.Empty);
        var finalized = draft with { PlanHash = RepositorySetupPlanCodec.ComputeHash(draft) };
        _ = RepositorySetupTemplateBundleRenderer.Render(descriptor.TemplateBundle, finalized);
        return finalized;
    }

    private static RepositorySetupProfileSummary ToSummary(
        RepositorySetupProfileDescriptor descriptor,
        CompatibilityResult compatibility) => new(
        descriptor.ProfileDefinitionId,
        descriptor.DisplayName,
        compatibility.State,
        compatibility.Reason,
        descriptor.PlanningReadiness,
        descriptor.CertificationState,
        descriptor.DescriptorSha256,
        descriptor.TemplateBundleSha256);

    private static CompatibilityResult EvaluateCompatibility(string? understandingJson)
    {
        ProjectUnderstandingDocument understanding;
        try
        {
            understanding = ProjectUnderstandingDocumentCodec.Deserialize(understandingJson ?? string.Empty);
        }
        catch (ProjectUnderstandingValidationException)
        {
            return new CompatibilityResult(
                RepositoryProfileCompatibilityStates.NeedsConfirmation,
                "The current project understanding cannot safely establish profile compatibility.");
        }

        var technologyKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "DesiredLanguage", "DesiredFramework", "ApplicationType",
            "DesiredTestApproach", "TargetPlatform"
        };
        if (understanding.Conflicts.Any(value =>
                value.Status == ProjectUnderstandingConflictStates.Open &&
                technologyKeys.Contains(value.FactKey)))
            return new CompatibilityResult(
                RepositoryProfileCompatibilityStates.NeedsConfirmation,
                "Resolve the open technology preference conflict before selecting an execution profile.");

        var facts = understanding.Facts.Where(value => technologyKeys.Contains(value.Key)).ToArray();
        if (facts.Length == 0)
            return new CompatibilityResult(
                RepositoryProfileCompatibilityStates.NoPreference,
                "No implementation preference is recorded; the pinned planning profile may be selected explicitly.");

        foreach (var fact in facts)
        {
            if (fact.State is ProjectUnderstandingFactStates.Inferred or ProjectUnderstandingFactStates.Conflicted)
                return new CompatibilityResult(
                    RepositoryProfileCompatibilityStates.NeedsConfirmation,
                    $"Confirm or resolve the inferred {fact.Key} preference before selecting this profile.");

            if (fact.State == ProjectUnderstandingFactStates.Confirmed &&
                !RepositorySetupProfileCompatibility.IsPinnedWinFormsFactCompatible(fact.Key, fact.Value))
                return new CompatibilityResult(
                    RepositoryProfileCompatibilityStates.Incompatible,
                    $"The confirmed {fact.Key} preference is not supported by the pinned Workbench v0.1 profile. You can continue shaping and creating tickets.");
        }

        return new CompatibilityResult(
            RepositoryProfileCompatibilityStates.Compatible,
            "The confirmed implementation preferences are compatible with this pinned planning profile.");
    }

    private static string Render(
        string template,
        RepositorySetupSafeNames names,
        string solutionPath,
        string appProjectPath,
        string testProjectPath) => template
        .Replace("{SolutionName}", names.SolutionName, StringComparison.Ordinal)
        .Replace("{AppProjectName}", names.AppProjectName, StringComparison.Ordinal)
        .Replace("{TestProjectName}", names.TestProjectName, StringComparison.Ordinal)
        .Replace("{SolutionPath}", solutionPath, StringComparison.Ordinal)
        .Replace("{AppProjectPath}", appProjectPath, StringComparison.Ordinal)
        .Replace("{TestProjectPath}", testProjectPath, StringComparison.Ordinal);

    private static void ValidateDescriptor(RepositorySetupProfileDescriptor value)
    {
        if (string.IsNullOrWhiteSpace(value.ProfileDefinitionId) || value.Revision <= 0 ||
            string.IsNullOrWhiteSpace(value.DisplayName) ||
            string.IsNullOrWhiteSpace(value.TargetFramework) ||
            string.IsNullOrWhiteSpace(value.Language) ||
            string.IsNullOrWhiteSpace(value.ApplicationKind) ||
            string.IsNullOrWhiteSpace(value.TestFramework) ||
            string.IsNullOrWhiteSpace(value.SdkVersion) ||
            string.IsNullOrWhiteSpace(value.RuntimeVersion) ||
            string.IsNullOrWhiteSpace(value.ToolchainManifestId) ||
            string.IsNullOrWhiteSpace(value.ExecutionImageReference) ||
            string.IsNullOrWhiteSpace(value.SolutionPathTemplate) ||
            string.IsNullOrWhiteSpace(value.AppProjectPathTemplate) ||
            string.IsNullOrWhiteSpace(value.TestProjectPathTemplate) ||
            string.IsNullOrWhiteSpace(value.RestoreCommandTemplate) ||
            string.IsNullOrWhiteSpace(value.BuildCommandTemplate) ||
            string.IsNullOrWhiteSpace(value.TestCommandTemplate) ||
            value.PlanningReadiness != RepositoryPlanningReadinessStates.PreviewPlanningOnly ||
            value.CertificationState != RepositoryProfileCertificationStates.NotCertificationReady ||
            !IsLowerHexSha256(value.DescriptorSha256) ||
            !IsLowerHexSha256(value.TemplateBundleSha256) ||
            !string.Equals(
                RepositorySetupTemplateBundleCodec.ComputeHash(value.TemplateBundle),
                value.TemplateBundleSha256,
                StringComparison.Ordinal))
            throw new RepositorySetupIntegrityException(
                $"Repository setup profile '{value.ProfileDefinitionId}' is not a complete, honest planning bundle.");
    }

    private static void ValidateIdentity(int tenantId, int actorUserId, int projectId)
    {
        if (tenantId <= 0 || actorUserId <= 0 || projectId <= 0)
            throw new RepositorySetupValidationException(
                "A selected tenant, authenticated actor, and project are required.");
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

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

    private static async Task<bool> HasCurrentFenceAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int actorUserId,
        int projectId,
        long sessionId,
        long leaseEpoch,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.WorkbenchWriteLeases lease
            INNER JOIN dbo.WorkbenchSessions session
                ON session.TenantId=lease.TenantId AND session.ProjectId=lease.ProjectId
               AND session.Id=lease.WorkbenchSessionId AND session.Status=N'Active'
            WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
              AND lease.WorkbenchSessionId=@SessionId AND lease.LeaseEpoch=@LeaseEpoch
              AND lease.HolderActorUserId=@ActorUserId AND lease.RevokedAtUtc IS NULL
              AND lease.ExpiresAtUtc > SYSUTCDATETIME();
            """,
            new
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                ProjectId = projectId,
                SessionId = sessionId,
                LeaseEpoch = leaseEpoch
            },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    private static async Task<bool> ValidateAndRenewLeaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ConfirmRepositorySetupCommand command,
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

    private static Task<ProjectRow?> ReadProjectAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var lockHint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var sql = $"""
            SELECT project.Id AS ProjectId, project.Name, project.LocalPath,
                   COALESCE(lifecycle.Phase, N'Shaping') AS ProjectLifecyclePhase,
                   COALESCE(readiness.ExecutionReadiness, N'NotConfigured') AS ExecutionReadiness,
                   COALESCE(readiness.ReasonCode, N'RepositoryNotConfigured') AS ReadinessReasonCode,
                   COALESCE(understanding.Revision, 0) AS UnderstandingRevision,
                   COALESCE(understanding.UnderstandingJson, NCHAR(123)+NCHAR(125)) AS UnderstandingJson
            FROM dbo.Projects project{lockHint}
            OUTER APPLY (
                SELECT TOP (1) value.Phase
                FROM dbo.ProjectLifecyclePhases value{lockHint}
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC
            ) lifecycle
            OUTER APPLY (
                SELECT TOP (1) value.ExecutionReadiness, value.ReasonCode
                FROM dbo.ProjectReadinessAssessments value{lockHint}
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC
            ) readiness
            OUTER APPLY (
                SELECT TOP (1) value.Revision, value.UnderstandingJson
                FROM dbo.ProjectUnderstandings value{lockHint}
                WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id
                ORDER BY value.Revision DESC
            ) understanding
            WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
            """;
        return connection.QuerySingleOrDefaultAsync<ProjectRow>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<bool> HasRepositoryBindingAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.RepositoryBindings WHERE TenantId=@TenantId AND ProjectId=@ProjectId;",
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken)) > 0;

    private static Task<RepositoryBindingSnapshot?> ReadBindingAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<RepositoryBindingSnapshot>(new CommandDefinition(
            """
            SELECT Id, ProjectId, CurrentRevision AS Revision, RepositoryKind,
                   CanonicalPath, BindingState, DefaultBranch, BaselineCommit,
                   CreatedByActorUserId, ConfirmedAtUtc
            FROM dbo.RepositoryBindings
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<ProjectExecutionProfileSnapshot?> ReadExecutionProfileAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        Guid repositoryBindingId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ProjectExecutionProfileSnapshot>(new CommandDefinition(
            """
            SELECT Id, ProjectId, CurrentRevision AS Revision, RepositoryBindingId,
                   ProfileDefinitionId, ProfileDescriptorRevision, DescriptorSha256, TemplateBundleSha256,
                   PlanningBundleSha256, TargetFramework, Language, ApplicationKind,
                   TestFramework, SdkVersion, RuntimeVersion,
                   SolutionPath, AppProjectPath, TestProjectPath,
                   RestoreCommand, BuildCommand, TestCommand, ToolchainManifestId,
                   ExecutionImageReference, PlanningReadiness, CertificationState
            FROM dbo.ProjectExecutionProfiles
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
              AND RepositoryBindingId=@RepositoryBindingId;
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                RepositoryBindingId = repositoryBindingId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<RepositorySetupConfirmationSnapshot?> ReadLatestConfirmationAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<RepositorySetupConfirmationSnapshot>(new CommandDefinition(
            """
            SELECT TOP (1)
                   Id AS ConfirmationId, PlanHash, ConfirmedAtUtc,
                   ClientOperationId, WorkbenchSessionId, LeaseEpoch
            FROM dbo.RepositorySetupConfirmations
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId
            ORDER BY ConfirmedAtUtc DESC, Id DESC;
            """,
            new { TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<ClientOperationRow?> ReadOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ConfirmRepositorySetupCommand command,
        string resourceScope,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ClientOperationRow>(new CommandDefinition(
            """
            SELECT PayloadHash, Status, CanonicalResultJson, ResultHash
            FROM dbo.ClientOperations WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
              AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
              AND ClientOperationId=@ClientOperationId;
            """,
            new
            {
                command.TenantId,
                command.ActorUserId,
                OperationKind = RepositorySetupOperationKinds.Confirm,
                ResourceScopeId = resourceScope,
                command.ClientOperationId
            },
            transaction,
            cancellationToken: cancellationToken));

    private static RepositorySetupConfirmationResult ReadReplay(ClientOperationRow operation)
    {
        if (operation.Status != "Completed" || string.IsNullOrWhiteSpace(operation.CanonicalResultJson) ||
            !IsLowerHexSha256(operation.ResultHash ?? string.Empty) ||
            !string.Equals(
                RepositorySetupCanonicalJson.Sha256(operation.CanonicalResultJson),
                operation.ResultHash,
                StringComparison.Ordinal))
            throw new RepositorySetupIntegrityException(
                "The stored repository setup operation does not contain a complete verified result.");
        return JsonSerializer.Deserialize<RepositorySetupConfirmationResult>(
                   operation.CanonicalResultJson,
                   new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new RepositorySetupIntegrityException(
                   "The stored repository setup operation result is unreadable.");
    }

    private sealed record CompatibilityResult(string State, string Reason);

    private sealed class ProjectRow
    {
        public int ProjectId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? LocalPath { get; init; }
        public string ProjectLifecyclePhase { get; init; } = ProjectLifecyclePhases.Shaping;
        public string ExecutionReadiness { get; init; } = ProjectExecutionReadinessStates.NotConfigured;
        public string ReadinessReasonCode { get; init; } = string.Empty;
        public long UnderstandingRevision { get; init; }
        public string UnderstandingJson { get; init; } = string.Empty;
    }

    private sealed class ClientOperationRow
    {
        public string PayloadHash { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }
}

public sealed record RepositorySetupSafeNames(
    string DirectoryName,
    string SolutionName,
    string AppProjectName,
    string TestProjectName)
{
    private static readonly HashSet<string> WindowsReserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5",
        "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
        "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static RepositorySetupSafeNames FromProject(string projectName, int projectId)
    {
        var codeParts = new string(projectName
                .Select(character => IsAsciiLetterOrDigit(character) ? character : ' ')
                .ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ToIdentifierPart)
            .Where(value => value.Length > 0)
            .ToArray();
        var codeName = string.Concat(codeParts);
        if (string.IsNullOrWhiteSpace(codeName))
            codeName = $"Project{projectId}";
        if (!char.IsLetter(codeName[0]) && codeName[0] != '_')
            codeName = "Project" + codeName;
        if (codeName.Length > 48)
            codeName = codeName[..48];
        if (WindowsReserved.Contains(codeName))
            codeName += "Project";

        var directoryBase = string.Join('-', codeParts.Select(value => value.ToLowerInvariant()));
        if (string.IsNullOrWhiteSpace(directoryBase))
            directoryBase = "project";
        if (directoryBase.Length > 48)
            directoryBase = directoryBase[..48].TrimEnd('-');
        if (WindowsReserved.Contains(directoryBase))
            directoryBase += "-project";
        var directory = $"{directoryBase}-{projectId.ToString(CultureInfo.InvariantCulture)}";
        return new RepositorySetupSafeNames(
            directory,
            codeName,
            codeName + ".App",
            codeName + ".Tests");
    }

    private static string ToIdentifierPart(string value)
    {
        if (value.Length == 0)
            return string.Empty;
        var first = char.ToUpperInvariant(value[0]);
        return value.Length == 1 ? first.ToString() : first + value[1..];
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
