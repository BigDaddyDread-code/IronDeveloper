using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using IronDev.Core.RunReadiness;
using IronDev.Core.Workbench;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class WorkbenchRepositorySetupApiTests : ApiTestBase
{
    private const string ProfileId = RepositorySetupProfileIds.GreenfieldWinFormsNet10MstestV1;

    [TestMethod]
    public async Task ContextAndPreview_ExposePinnedPlanningTruthWithoutCreatingAuthorityOrFiles()
    {
        var approvedRoot = SafeApprovedRoot();
        var fileSystem = RecordingFileSystem.WithDirectories(approvedRoot);
        using var factory = RepositoryFactory(approvedRoot, fileSystem);
        using var client = await AuthenticatedClientAsync(factory);
        var project = await StartProjectAsync(client, "Pinned repository plan");

        var contextResponse = await client.GetAsync(ContextUrl(project.ProjectId));
        Assert.AreEqual(HttpStatusCode.OK, contextResponse.StatusCode);
        var context = await contextResponse.Content.ReadFromJsonAsync<RepositorySetupContext>();
        Assert.IsNotNull(context);
        Assert.AreEqual(project.ProjectId, context.ProjectId);
        Assert.AreEqual(AssignedTenantId, context.TenantId);
        Assert.AreEqual("Pinned repository plan", context.ProjectName);
        Assert.AreEqual(ProjectLifecyclePhases.Shaping, context.ProjectLifecyclePhase);
        Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured, context.ExecutionReadiness);
        Assert.IsNull(context.RepositoryBinding);
        Assert.IsNull(context.ExecutionProfile);
        Assert.IsNull(context.LatestConfirmation);
        Assert.AreEqual(RepositorySetupEnvironmentCapabilityStates.Available,
            context.EnvironmentCapability.State);
        Assert.AreEqual(RepositorySetupReasonCodes.Ready, context.EnvironmentCapability.ReasonCode);
        Assert.AreEqual(1, context.AvailableProfiles.Count);
        var profileSummary = context.AvailableProfiles.Single();
        Assert.AreEqual(ProfileId, profileSummary.ProfileDefinitionId);
        Assert.AreEqual(RepositoryProfileCompatibilityStates.NoPreference, profileSummary.Compatibility);
        Assert.AreEqual(RepositoryPlanningReadinessStates.PreviewPlanningOnly,
            profileSummary.PlanningReadiness);
        Assert.AreEqual(RepositoryProfileCertificationStates.NotCertificationReady,
            profileSummary.CertificationState);
        AssertLowerSha256(profileSummary.DescriptorSha256);
        AssertLowerSha256(profileSummary.TemplateBundleSha256);

        var previewResponse = await client.PostAsJsonAsync(
            PlanUrl(project.ProjectId),
            PlanPayload(project));
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode);
        var plan = await previewResponse.Content.ReadFromJsonAsync<RepositorySetupPlanPreview>();
        Assert.IsNotNull(plan);

        Assert.AreEqual(1, plan.SchemaVersion);
        Assert.AreEqual("ProjectUnderstanding", plan.Source);
        Assert.AreEqual(project.ProjectId, plan.ProjectId);
        Assert.AreEqual("Pinned repository plan", plan.CanonicalProjectName);
        Assert.AreEqual(project.WorkbenchSessionId, plan.WorkbenchSessionId);
        Assert.AreEqual(project.LeaseEpoch, plan.LeaseEpoch);
        Assert.AreEqual(1L, plan.BasedOnUnderstandingRevision);
        Assert.AreEqual(RepositorySetupPreviewStates.ReadyForConfirmation, plan.State);
        Assert.AreEqual(RepositorySetupReasonCodes.Ready, plan.ReasonCode);
        Assert.AreEqual(ProfileId, plan.Profile.ProfileDefinitionId);
        Assert.AreEqual("PinnedRepositoryPlan", plan.SolutionName);
        Assert.AreEqual("PinnedRepositoryPlan.App", plan.AppProjectName);
        Assert.AreEqual("PinnedRepositoryPlan.Tests", plan.TestProjectName);
        Assert.AreEqual("PinnedRepositoryPlan.slnx", plan.SolutionPath);
        Assert.AreEqual(
            "src/PinnedRepositoryPlan.App/PinnedRepositoryPlan.App.csproj",
            plan.AppProjectPath.Replace('\\', '/'));
        Assert.AreEqual(
            "tests/PinnedRepositoryPlan.Tests/PinnedRepositoryPlan.Tests.csproj",
            plan.TestProjectPath.Replace('\\', '/'));
        Assert.AreEqual("net10.0-windows", plan.TargetFramework);
        Assert.AreEqual("C#", plan.Language);
        Assert.AreEqual("WinForms", plan.ApplicationKind);
        Assert.AreEqual("MSTest", plan.TestFramework);
        Assert.AreEqual("10.0.302", plan.SdkVersion);
        Assert.AreEqual("10.0.10", plan.RuntimeVersion);
        Assert.AreEqual("dotnet-sdk-10.0.302-runtime-10.0.10-planning-v1",
            plan.ToolchainManifestId);
        Assert.AreEqual(
            "mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2025",
            plan.ExecutionImageReference);
        Assert.AreEqual("main", plan.DefaultBranch);
        Assert.IsTrue(plan.InitializeGit);
        Assert.IsTrue(plan.IndexAfterProvisioning);
        StringAssert.Contains(plan.RestoreCommand, "--locked-mode");
        StringAssert.Contains(plan.RestoreCommand, "C:\\IronDev\\NuGet.Config");
        StringAssert.Contains(plan.BuildCommand, "--no-restore");
        StringAssert.Contains(plan.TestCommand, "--no-build");
        StringAssert.Contains(plan.SandboxValidation, "PR-05B");
        StringAssert.Contains(plan.ResourcePolicy, "planning authority only");
        AssertLowerSha256(plan.BasedOnUnderstandingHash);
        AssertLowerSha256(plan.ProfileDescriptorSha256);
        AssertLowerSha256(plan.TemplateBundleSha256);
        AssertLowerSha256(plan.PlanningBundleSha256);
        AssertLowerSha256(plan.PlanHash);
        Assert.AreEqual(plan.PlanHash, RepositorySetupPlanCodec.ComputeHash(plan));
        Assert.AreEqual(context.EnvironmentCapability.SuggestedTarget, plan.TargetPath);
        Assert.IsFalse(Directory.Exists(plan.TargetPath));
        Assert.IsTrue(fileSystem.DirectoryProbes.Contains(plan.TargetPath),
            "Preview must establish that the exact server-derived target does not already exist.");

        await AssertNoSetupAuthorityAsync(project.ProjectId);
    }

    [TestMethod]
    public async Task Preview_ReportsUnsupportedAndUncertainTechnologyWithoutInspectingTheFileSystem()
    {
        var approvedRoot = SafeApprovedRoot();
        var fileSystem = RecordingFileSystem.WithDirectories(approvedRoot);
        using var factory = RepositoryFactory(approvedRoot, fileSystem);
        using var client = await AuthenticatedClientAsync(factory);

        var incompatibleProject = await StartProjectAsync(client, "Python preference");
        await AppendUnderstandingAsync(
            incompatibleProject.ProjectId,
            "DesiredLanguage",
            "Python",
            ProjectUnderstandingFactStates.Confirmed);
        var incompatible = await PreviewAsync(client, incompatibleProject);
        Assert.AreEqual(RepositorySetupPreviewStates.UnsupportedProfile, incompatible.State);
        Assert.AreEqual(RepositorySetupReasonCodes.IncompatibleProfile, incompatible.ReasonCode);
        Assert.AreEqual(RepositoryProfileCompatibilityStates.Incompatible,
            incompatible.Profile.Compatibility);
        Assert.AreEqual(string.Empty, incompatible.TargetPath);
        StringAssert.Contains(incompatible.Message, "continue shaping and creating tickets");

        var inferredProject = await StartProjectAsync(client, "Inferred preference");
        await AppendUnderstandingAsync(
            inferredProject.ProjectId,
            "DesiredFramework",
            ".NET",
            ProjectUnderstandingFactStates.Inferred);
        var inferred = await PreviewAsync(client, inferredProject);
        Assert.AreEqual(RepositorySetupPreviewStates.NeedsConfirmation, inferred.State);
        Assert.AreEqual(RepositorySetupReasonCodes.PreferenceNeedsConfirmation, inferred.ReasonCode);
        Assert.AreEqual(RepositoryProfileCompatibilityStates.NeedsConfirmation,
            inferred.Profile.Compatibility);
        Assert.AreEqual(string.Empty, inferred.TargetPath);

        var conflictedProject = await StartProjectAsync(client, "Conflicted preference");
        await AppendUnderstandingAsync(
            conflictedProject.ProjectId,
            "ApplicationType",
            "Desktop or web",
            ProjectUnderstandingFactStates.Conflicted);
        var conflicted = await PreviewAsync(client, conflictedProject);
        Assert.AreEqual(RepositorySetupPreviewStates.NeedsConfirmation, conflicted.State);
        Assert.AreEqual(RepositoryProfileCompatibilityStates.NeedsConfirmation,
            conflicted.Profile.Compatibility);
        Assert.AreEqual(string.Empty, conflicted.TargetPath);

        Assert.AreEqual(0, fileSystem.TotalProbeCount,
            "Incompatible, inferred, and conflicted technology preferences must be decided from server-owned understanding before any filesystem inspection.");
        await AssertNoSetupAuthorityAsync(incompatibleProject.ProjectId);
        await AssertNoSetupAuthorityAsync(inferredProject.ProjectId);
        await AssertNoSetupAuthorityAsync(conflictedProject.ProjectId);
    }

    [TestMethod]
    public async Task Preview_UsesTypedUnknownMissingEnvironmentAndUnsafePathOutcomes()
    {
        var approvedRoot = SafeApprovedRoot();
        var fileSystem = RecordingFileSystem.WithDirectories(approvedRoot);
        using var readyFactory = RepositoryFactory(approvedRoot, fileSystem);
        using var readyClient = await AuthenticatedClientAsync(readyFactory);
        var unknownProject = await StartProjectAsync(readyClient, "Unknown profile");

        var unknown = await readyClient.PostAsJsonAsync(
            PlanUrl(unknownProject.ProjectId),
            PlanPayload(unknownProject, "not-in-the-pinned-catalog"));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        await AssertErrorAsync(unknown, RepositorySetupUnsupportedProfileException.ErrorCode);
        Assert.AreEqual(0, fileSystem.TotalProbeCount);

        var unavailableFileSystem = new RecordingFileSystem();
        using var unavailableFactory = RepositoryFactory(string.Empty, unavailableFileSystem);
        using var unavailableClient = await AuthenticatedClientAsync(unavailableFactory);
        var unavailableProject = await StartProjectAsync(unavailableClient, "No repository environment");
        var unavailableContext = await unavailableClient.GetFromJsonAsync<RepositorySetupContext>(
            ContextUrl(unavailableProject.ProjectId));
        Assert.IsNotNull(unavailableContext);
        Assert.AreEqual(RepositorySetupEnvironmentCapabilityStates.Unavailable,
            unavailableContext.EnvironmentCapability.State);
        Assert.AreEqual(RepositorySetupReasonCodes.WorkspaceRootNotConfigured,
            unavailableContext.EnvironmentCapability.ReasonCode);
        var unavailablePlan = await PreviewAsync(unavailableClient, unavailableProject);
        Assert.AreEqual(RepositorySetupPreviewStates.EnvironmentUnavailable, unavailablePlan.State);
        Assert.AreEqual(RepositorySetupReasonCodes.WorkspaceRootNotConfigured,
            unavailablePlan.ReasonCode);
        Assert.AreEqual(string.Empty, unavailablePlan.TargetPath);
        Assert.AreEqual(0, unavailableFileSystem.TotalProbeCount);

        var unsafeFileSystem = new RecordingFileSystem();
        var driveRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        using var unsafeFactory = RepositoryFactory(driveRoot, unsafeFileSystem);
        using var unsafeClient = await AuthenticatedClientAsync(unsafeFactory);
        var unsafeProject = await StartProjectAsync(unsafeClient, "Unsafe repository root");
        var unsafeResponse = await unsafeClient.PostAsJsonAsync(
            PlanUrl(unsafeProject.ProjectId),
            PlanPayload(unsafeProject));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, unsafeResponse.StatusCode);
        await AssertErrorAsync(unsafeResponse, RepositorySetupUnsafePathException.ErrorCode);
        Assert.AreEqual(0, unsafeFileSystem.TotalProbeCount);

        await AssertNoSetupAuthorityAsync(unknownProject.ProjectId);
        await AssertNoSetupAuthorityAsync(unavailableProject.ProjectId);
        await AssertNoSetupAuthorityAsync(unsafeProject.ProjectId);
    }

    [TestMethod]
    public async Task Confirm_AtomicallyRecordsPlanningAuthority_ReplaysExactIds_AndProjectsItOnReads()
    {
        var approvedRoot = SafeApprovedRoot();
        var fileSystem = RecordingFileSystem.WithDirectories(approvedRoot);
        using var factory = RepositoryFactory(approvedRoot, fileSystem);
        using var client = await AuthenticatedClientAsync(factory);
        var project = await StartProjectAsync(client, "Atomic repository confirmation");
        var plan = await PreviewAsync(client, project);
        var operationId = Guid.NewGuid();

        var confirmResponse = await client.PostAsJsonAsync(
            ConfirmationUrl(project.ProjectId),
            ConfirmationPayload(project, operationId, plan.PlanHash));
        Assert.AreEqual(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmed = await confirmResponse.Content
            .ReadFromJsonAsync<RepositorySetupConfirmationResult>();
        Assert.IsNotNull(confirmed);
        Assert.IsFalse(confirmed.IsReplay);
        Assert.AreEqual(operationId, confirmed.ClientOperationId);
        Assert.AreNotEqual(Guid.Empty, confirmed.ConfirmationId);
        Assert.AreEqual(ProjectLifecyclePhases.Shaping, confirmed.ProjectLifecyclePhase);
        Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured,
            confirmed.ExecutionReadiness);
        Assert.AreEqual(RepositorySetupReasonCodes.RepositoryProvisioningPending,
            confirmed.ReadinessReasonCode);
        Assert.AreEqual(RepositoryKinds.Greenfield, confirmed.RepositoryBinding.RepositoryKind);
        Assert.AreEqual(RepositoryBindingStates.SetupConfirmed,
            confirmed.RepositoryBinding.BindingState);
        Assert.AreEqual(1L, confirmed.RepositoryBinding.Revision);
        Assert.AreEqual(plan.TargetPath, confirmed.RepositoryBinding.CanonicalPath);
        Assert.AreEqual("main", confirmed.RepositoryBinding.DefaultBranch);
        Assert.IsNull(confirmed.RepositoryBinding.BaselineCommit);
        Assert.AreEqual(1, confirmed.RepositoryBinding.CreatedByActorUserId);
        Assert.IsNotNull(confirmed.RepositoryBinding.ConfirmedAtUtc);
        Assert.AreEqual(1L, confirmed.ExecutionProfile.Revision);
        Assert.AreEqual(confirmed.RepositoryBinding.Id,
            confirmed.ExecutionProfile.RepositoryBindingId);
        Assert.AreEqual(ProfileId, confirmed.ExecutionProfile.ProfileDefinitionId);
        Assert.AreEqual("10.0.302", confirmed.ExecutionProfile.SdkVersion);
        Assert.AreEqual("10.0.10", confirmed.ExecutionProfile.RuntimeVersion);
        Assert.AreEqual(RepositoryPlanningReadinessStates.PreviewPlanningOnly,
            confirmed.ExecutionProfile.PlanningReadiness);
        Assert.AreEqual(RepositoryProfileCertificationStates.NotCertificationReady,
            confirmed.ExecutionProfile.CertificationState);
        Assert.AreEqual(plan.PlanHash, confirmed.SetupPlan.PlanHash);
        Assert.IsFalse(Directory.Exists(plan.TargetPath));

        await using (var connection = new SqlConnection(ConnectionString))
        {
            var state = await ReadSetupStateAsync(connection, project.ProjectId, operationId);
            Assert.AreEqual(1, state.Bindings);
            Assert.AreEqual(1, state.BindingRevisions);
            Assert.AreEqual(1, state.ExecutionProfiles);
            Assert.AreEqual(1, state.ExecutionProfileRevisions);
            Assert.AreEqual(1, state.Confirmations);
            Assert.AreEqual(2, state.ReadinessRevisions);
            Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured,
                state.ExecutionReadiness);
            Assert.AreEqual(RepositorySetupReasonCodes.RepositoryProvisioningPending,
                state.ReadinessReasonCode);
            Assert.AreEqual(1, state.LifecycleRevisions);
            Assert.AreEqual(ProjectLifecyclePhases.Shaping, state.LifecyclePhase);
            Assert.AreEqual(1, state.OperationRows);
            Assert.AreEqual(1, state.CompletedOperations);
            Assert.AreEqual(1, state.OutboxEvents);
            Assert.AreEqual(1, state.Attributions);
            Assert.IsNull(state.LocalPath);
            Assert.AreEqual(0, state.ProjectFiles);
        }

        var replayResponse = await client.PostAsJsonAsync(
            ConfirmationUrl(project.ProjectId),
            ConfirmationPayload(project, operationId, plan.PlanHash));
        Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = await replayResponse.Content
            .ReadFromJsonAsync<RepositorySetupConfirmationResult>();
        Assert.IsNotNull(replay);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(confirmed.ConfirmationId, replay.ConfirmationId);
        Assert.AreEqual(confirmed.RepositoryBinding.Id, replay.RepositoryBinding.Id);
        Assert.AreEqual(confirmed.ExecutionProfile.Id, replay.ExecutionProfile.Id);

        var secondOperation = await client.PostAsJsonAsync(
            ConfirmationUrl(project.ProjectId),
            ConfirmationPayload(project, Guid.NewGuid(), plan.PlanHash));
        Assert.AreEqual(HttpStatusCode.Conflict, secondOperation.StatusCode);
        await AssertErrorAsync(secondOperation, RepositorySetupAlreadyBoundException.ErrorCode);

        var mismatchHash = new string(plan.PlanHash[0] == 'a' ? 'b' : 'a', 64);
        var mismatch = await client.PostAsJsonAsync(
            ConfirmationUrl(project.ProjectId),
            ConfirmationPayload(project, operationId, mismatchHash));
        Assert.AreEqual(HttpStatusCode.Conflict, mismatch.StatusCode);
        await AssertErrorAsync(mismatch, ProjectStartOperationMismatchException.ErrorCode);

        var context = await client.GetFromJsonAsync<RepositorySetupContext>(
            ContextUrl(project.ProjectId));
        Assert.IsNotNull(context);
        Assert.AreEqual(confirmed.RepositoryBinding.Id, context.RepositoryBinding?.Id);
        Assert.AreEqual(confirmed.ExecutionProfile.Id, context.ExecutionProfile?.Id);
        Assert.AreEqual(confirmed.ConfirmationId, context.LatestConfirmation?.ConfirmationId);
        Assert.AreEqual(operationId, context.LatestConfirmation?.ClientOperationId);
        Assert.AreEqual(RepositoryReadinessReasonCodes.RepositoryNotConfigured,
            context.ReadinessReasonCode);

        var understandingResponse = await client.GetAsync(
            $"/api/workbench/projects/{project.ProjectId}/understanding");
        Assert.AreEqual(HttpStatusCode.OK, understandingResponse.StatusCode);
        var understanding = await understandingResponse.Content
            .ReadFromJsonAsync<ProjectUnderstandingSnapshot>();
        Assert.IsNotNull(understanding);
        Assert.AreEqual(confirmed.RepositoryBinding.Id,
            understanding.OperationalProjections.RepositoryBinding?.Id);

        var openResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/open",
            new { clientOperationId = Guid.NewGuid(), takeOver = false });
        Assert.AreEqual(HttpStatusCode.OK, openResponse.StatusCode);
        var opened = await openResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(confirmed.RepositoryBinding.Id,
            opened.GetProperty("repositoryBinding").GetProperty("id").GetGuid());

        await using var replayConnection = new SqlConnection(ConnectionString);
        var replayState = await ReadSetupStateAsync(replayConnection, project.ProjectId, operationId);
        Assert.AreEqual(1, replayState.Bindings);
        Assert.AreEqual(1, replayState.ExecutionProfiles);
        Assert.AreEqual(1, replayState.Confirmations);
        Assert.AreEqual(1, replayState.OperationRows);
        Assert.AreEqual(1, replayState.CompletedOperations);
        Assert.AreEqual(1, replayState.OutboxEvents);
        Assert.AreEqual(1, replayState.Attributions);
    }

    [TestMethod]
    public async Task Confirm_InvalidatesPlanAfterUnderstandingRenameCatalogOrTargetRootChanges()
    {
        var rootA = SafeApprovedRoot();
        var rootB = SafeApprovedRoot();
        var fileSystem = RecordingFileSystem.WithDirectories(rootA, rootB);
        using var factoryA = RepositoryFactory(rootA, fileSystem);
        using var clientA = await AuthenticatedClientAsync(factoryA);

        var understandingProject = await StartProjectAsync(clientA, "Understanding drift");
        var understandingPlan = await PreviewAsync(clientA, understandingProject);
        await AppendUnderstandingAsync(
            understandingProject.ProjectId,
            "DesiredLanguage",
            "C#",
            ProjectUnderstandingFactStates.Confirmed);
        await AssertChangedPlanRejectedAsync(clientA, understandingProject, understandingPlan.PlanHash);

        var renamedProject = await StartProjectAsync(clientA, "Rename before confirmation");
        var renamePlan = await PreviewAsync(clientA, renamedProject);
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.Projects SET Name=N'Renamed authoritative project' WHERE TenantId=1 AND Id=@ProjectId;",
                new { renamedProject.ProjectId });
        }
        await AssertChangedPlanRejectedAsync(clientA, renamedProject, renamePlan.PlanHash);

        var catalogProject = await StartProjectAsync(clientA, "Catalog drift");
        var catalogPlan = await PreviewAsync(clientA, catalogProject);
        using (var catalogFactory = RepositoryFactory(
                   rootA,
                   fileSystem,
                   services =>
                   {
                       services.RemoveAll<IRepositorySetupProfileCatalog>();
                       services.AddSingleton<IRepositorySetupProfileCatalog,
                           EmptyRepositorySetupProfileCatalog>();
                   }))
        using (var catalogClient = await AuthenticatedClientAsync(catalogFactory))
        {
            await AssertChangedPlanRejectedAsync(
                catalogClient,
                catalogProject,
                catalogPlan.PlanHash);
        }

        var targetProject = await StartProjectAsync(clientA, "Target-root drift");
        var targetPlan = await PreviewAsync(clientA, targetProject);
        using (var rootBFactory = RepositoryFactory(rootB, fileSystem))
        using (var rootBClient = await AuthenticatedClientAsync(rootBFactory))
        {
            await AssertChangedPlanRejectedAsync(rootBClient, targetProject, targetPlan.PlanHash);
        }

        await AssertNoSetupAuthorityAsync(understandingProject.ProjectId);
        await AssertNoSetupAuthorityAsync(renamedProject.ProjectId);
        await AssertNoSetupAuthorityAsync(catalogProject.ProjectId);
        await AssertNoSetupAuthorityAsync(targetProject.ProjectId);
    }

    [TestMethod]
    public async Task Confirm_RequiresContributorAccessCurrentFenceAndAConfirmablePlan()
    {
        var approvedRoot = SafeApprovedRoot();
        var fileSystem = RecordingFileSystem.WithDirectories(approvedRoot);
        using var factory = RepositoryFactory(approvedRoot, fileSystem);
        using var owner = await AuthenticatedClientAsync(factory);

        var viewerProject = await StartProjectAsync(owner, "Viewer confirmation boundary");
        var viewerPlan = await PreviewAsync(owner, viewerProject);
        using var viewer = await CreateProjectViewerClientAsync(owner, factory, viewerProject.ProjectId);
        var viewerAttempt = await viewer.PostAsJsonAsync(
            ConfirmationUrl(viewerProject.ProjectId),
            ConfirmationPayload(viewerProject, Guid.NewGuid(), viewerPlan.PlanHash));
        Assert.AreEqual(HttpStatusCode.Forbidden, viewerAttempt.StatusCode);
        await AssertErrorAsync(viewerAttempt, RepositorySetupForbiddenException.ErrorCode);
        await AssertNoSetupAuthorityAsync(viewerProject.ProjectId);

        var staleProject = await StartProjectAsync(owner, "Stale setup fence");
        var stalePlan = await PreviewAsync(owner, staleProject);
        var wrongSession = await owner.PostAsJsonAsync(
            ConfirmationUrl(staleProject.ProjectId),
            new
            {
                workbenchSessionId = staleProject.WorkbenchSessionId + 100_000,
                staleProject.LeaseEpoch,
                clientOperationId = Guid.NewGuid(),
                expectedPlanHash = stalePlan.PlanHash
            });
        Assert.AreEqual(HttpStatusCode.Conflict, wrongSession.StatusCode);
        await AssertErrorAsync(wrongSession, WorkbenchLeaseFenceException.ErrorCode);

        var wrongEpoch = await owner.PostAsJsonAsync(
            ConfirmationUrl(staleProject.ProjectId),
            new
            {
                staleProject.WorkbenchSessionId,
                leaseEpoch = staleProject.LeaseEpoch + 1,
                clientOperationId = Guid.NewGuid(),
                expectedPlanHash = stalePlan.PlanHash
            });
        Assert.AreEqual(HttpStatusCode.Conflict, wrongEpoch.StatusCode);
        await AssertErrorAsync(wrongEpoch, WorkbenchLeaseFenceException.ErrorCode);

        using (var contributor = await CreateProjectUserClientAsync(
                   owner,
                   factory,
                   staleProject.ProjectId,
                   tenantRole: "Member",
                   projectRole: "Contributor"))
        {
            var wrongHolder = await contributor.PostAsJsonAsync(
                ConfirmationUrl(staleProject.ProjectId),
                ConfirmationPayload(staleProject, Guid.NewGuid(), stalePlan.PlanHash));
            Assert.AreEqual(HttpStatusCode.Conflict, wrongHolder.StatusCode);
            await AssertErrorAsync(wrongHolder, WorkbenchLeaseFenceException.ErrorCode);
        }

        var expiredProject = await StartProjectAsync(owner, "Expired setup fence");
        var expiredPlan = await PreviewAsync(owner, expiredProject);
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                UPDATE dbo.WorkbenchWriteLeases
                SET ExpiresAtUtc=DATEADD(MINUTE, -1, SYSUTCDATETIME())
                WHERE TenantId=1 AND ProjectId=@ExpiredProjectId
                  AND WorkbenchSessionId=@ExpiredSessionId AND LeaseEpoch=@ExpiredLeaseEpoch;

                UPDATE dbo.WorkbenchWriteLeases
                SET RevokedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=1 AND ProjectId=@ProjectId
                  AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
                """,
                new
                {
                    ExpiredProjectId = expiredProject.ProjectId,
                    ExpiredSessionId = expiredProject.WorkbenchSessionId,
                    ExpiredLeaseEpoch = expiredProject.LeaseEpoch,
                    staleProject.ProjectId,
                    staleProject.WorkbenchSessionId,
                    staleProject.LeaseEpoch
                });
        }
        var expiredAttempt = await owner.PostAsJsonAsync(
            ConfirmationUrl(expiredProject.ProjectId),
            ConfirmationPayload(expiredProject, Guid.NewGuid(), expiredPlan.PlanHash));
        Assert.AreEqual(HttpStatusCode.Conflict, expiredAttempt.StatusCode);
        await AssertErrorAsync(expiredAttempt, WorkbenchLeaseFenceException.ErrorCode);
        await AssertNoSetupAuthorityAsync(expiredProject.ProjectId);

        var staleAttempt = await owner.PostAsJsonAsync(
            ConfirmationUrl(staleProject.ProjectId),
            ConfirmationPayload(staleProject, Guid.NewGuid(), stalePlan.PlanHash));
        Assert.AreEqual(HttpStatusCode.Conflict, staleAttempt.StatusCode);
        await AssertErrorAsync(staleAttempt, WorkbenchLeaseFenceException.ErrorCode);
        await AssertNoSetupAuthorityAsync(staleProject.ProjectId);

        var incompatibleProject = await StartProjectAsync(owner, "Unconfirmable setup plan");
        await AppendUnderstandingAsync(
            incompatibleProject.ProjectId,
            "TargetPlatform",
            "Linux server",
            ProjectUnderstandingFactStates.Confirmed);
        var incompatiblePlan = await PreviewAsync(owner, incompatibleProject);
        Assert.AreEqual(RepositorySetupPreviewStates.UnsupportedProfile, incompatiblePlan.State);
        var unconfirmable = await owner.PostAsJsonAsync(
            ConfirmationUrl(incompatibleProject.ProjectId),
            ConfirmationPayload(incompatibleProject, Guid.NewGuid(), incompatiblePlan.PlanHash));
        Assert.AreEqual(HttpStatusCode.Conflict, unconfirmable.StatusCode);
        await AssertErrorAsync(unconfirmable, RepositorySetupPlanNotConfirmableException.ErrorCode);
        await AssertNoSetupAuthorityAsync(incompatibleProject.ProjectId);

        var inaccessibleProject = await StartProjectAsync(owner, "Nonmember setup boundary");
        var inaccessiblePlan = await PreviewAsync(owner, inaccessibleProject);
        using var nonmember = await CreateProjectViewerClientAsync(owner, factory, projectId: null);
        var concealedRead = await nonmember.GetAsync(ContextUrl(inaccessibleProject.ProjectId));
        Assert.AreEqual(HttpStatusCode.NotFound, concealedRead.StatusCode);
        var inaccessibleConfirm = await nonmember.PostAsJsonAsync(
            ConfirmationUrl(inaccessibleProject.ProjectId),
            ConfirmationPayload(inaccessibleProject, Guid.NewGuid(), inaccessiblePlan.PlanHash));
        Assert.AreEqual(HttpStatusCode.NotFound, inaccessibleConfirm.StatusCode);
        await AssertErrorAsync(inaccessibleConfirm, "project_not_found");
        await AssertNoSetupAuthorityAsync(inaccessibleProject.ProjectId);

        var removedProject = await StartProjectAsync(owner, "Removed member setup boundary");
        var removedPlan = await PreviewAsync(owner, removedProject);
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                UPDATE dbo.ProjectMembers
                SET Status=N'Removed', RemovedUtc=SYSUTCDATETIME(), RemovedByUserId=1
                WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=1;
                """,
                new { removedProject.ProjectId });
        }
        var removedAttempt = await owner.PostAsJsonAsync(
            ConfirmationUrl(removedProject.ProjectId),
            ConfirmationPayload(removedProject, Guid.NewGuid(), removedPlan.PlanHash));
        Assert.AreEqual(HttpStatusCode.NotFound, removedAttempt.StatusCode);
        await AssertErrorAsync(removedAttempt, "project_not_found");
        await AssertNoSetupAuthorityAsync(removedProject.ProjectId);

        var inactiveProject = await StartProjectAsync(owner, "Inactive actor setup boundary");
        var inactivePlan = await PreviewAsync(owner, inactiveProject);
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE dbo.Users SET IsActive=0 WHERE Id=1;");
        }
        try
        {
            var inactiveAttempt = await owner.PostAsJsonAsync(
                ConfirmationUrl(inactiveProject.ProjectId),
                ConfirmationPayload(inactiveProject, Guid.NewGuid(), inactivePlan.PlanHash));
            Assert.AreEqual(HttpStatusCode.NotFound, inactiveAttempt.StatusCode);
            await AssertErrorAsync(inactiveAttempt, "project_not_found");
        }
        finally
        {
            await using var restore = new SqlConnection(ConnectionString);
            await restore.ExecuteAsync("UPDATE dbo.Users SET IsActive=1 WHERE Id=1;");
        }
        await AssertNoSetupAuthorityAsync(inactiveProject.ProjectId);

        var crossTenantProject = await StartProjectAsync(owner, "Cross-tenant setup boundary");
        var crossTenantPlan = await PreviewAsync(owner, crossTenantProject);
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE TenantId=2 AND UserId=1)
                    INSERT dbo.TenantUsers(TenantId, UserId, Role) VALUES (2, 1, N'Owner');
                """);
        }
        var crossTenantToken = await SelectTenantAsync(await LoginAsync(), UnassignedTenantId);
        using (var crossTenant = factory.CreateClient())
        {
            crossTenant.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", crossTenantToken);
            var crossTenantAttempt = await crossTenant.PostAsJsonAsync(
                ConfirmationUrl(crossTenantProject.ProjectId),
                ConfirmationPayload(crossTenantProject, Guid.NewGuid(), crossTenantPlan.PlanHash));
            Assert.AreEqual(HttpStatusCode.NotFound, crossTenantAttempt.StatusCode);
            await AssertErrorAsync(crossTenantAttempt, "project_not_found");
        }
        await AssertNoSetupAuthorityAsync(crossTenantProject.ProjectId);

        var nonexistentAttempt = await owner.PostAsJsonAsync(
            ConfirmationUrl(int.MaxValue),
            ConfirmationPayload(crossTenantProject, Guid.NewGuid(), crossTenantPlan.PlanHash));
        Assert.AreEqual(HttpStatusCode.NotFound, nonexistentAttempt.StatusCode);
        await AssertErrorAsync(nonexistentAttempt, "project_not_found");
    }

    [TestMethod]
    public async Task Confirm_RollsBackEveryTransactionalStage()
    {
        var approvedRoot = SafeApprovedRoot();
        var fileSystem = RecordingFileSystem.WithDirectories(approvedRoot);
        var injector = new TestRepositorySetupFailureInjector();
        using var factory = RepositoryFactory(
            approvedRoot,
            fileSystem,
            services =>
            {
                services.RemoveAll<IRepositorySetupConfirmationFailureInjector>();
                services.AddSingleton<IRepositorySetupConfirmationFailureInjector>(injector);
            });
        using var client = await AuthenticatedClientAsync(factory);

        foreach (var failurePoint in Enum.GetValues<RepositorySetupConfirmationFailurePoint>())
        {
            var project = await StartProjectAsync(client, $"Repository rollback {failurePoint}");
            var plan = await PreviewAsync(client, project);
            var operationId = Guid.NewGuid();
            var leaseBefore = await ReadLeaseStateAsync(project);
            injector.FailurePoint = failurePoint;
            try
            {
                await Assert.ThrowsExceptionAsync<InjectedRepositorySetupFailureException>(() =>
                    client.PostAsJsonAsync(
                        ConfirmationUrl(project.ProjectId),
                        ConfirmationPayload(project, operationId, plan.PlanHash)));
            }
            finally
            {
                injector.FailurePoint = null;
            }

            await AssertNoSetupAuthorityAsync(project.ProjectId, operationId);
            var leaseAfter = await ReadLeaseStateAsync(project);
            Assert.AreEqual(leaseBefore.HeartbeatAtUtc, leaseAfter.HeartbeatAtUtc,
                $"Lease heartbeat must roll back with failure at {failurePoint}.");
            Assert.AreEqual(leaseBefore.ExpiresAtUtc, leaseAfter.ExpiresAtUtc,
                $"Lease expiry renewal must roll back with failure at {failurePoint}.");
            Assert.IsFalse(Directory.Exists(plan.TargetPath));
        }
    }

    [TestMethod]
    public async Task LegacyLocalPath_BackfillsUnverifiedBinding_AndV2LegacyMutationsMintNoAuthority()
    {
        var approvedRoot = SafeApprovedRoot();
        var fileSystem = RecordingFileSystem.WithDirectories(approvedRoot);
        var applyCapability = new RecordingApplyCapabilityService();
        using var factory = RepositoryFactory(
            approvedRoot,
            fileSystem,
            services =>
            {
                services.RemoveAll<IProjectApplyCapabilityService>();
                services.AddSingleton<IProjectApplyCapabilityService>(applyCapability);
            });
        using var client = await AuthenticatedClientAsync(factory);
        var project = await StartProjectAsync(client, "Legacy repository evidence");
        const string legacyPath = @"D:\Legacy\UnverifiedRepo";

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.Projects SET LocalPath=@LegacyPath WHERE TenantId=1 AND Id=@ProjectId;",
                new { LegacyPath = legacyPath, project.ProjectId });
            await ApplyRepositorySetupMigrationAsync(connection);

            var backfill = await connection.QuerySingleAsync<LegacyBindingRow>(
                """
                SELECT binding.Id, binding.RepositoryKind, binding.BindingState,
                       binding.CanonicalPath, binding.DefaultBranch, binding.BaselineCommit,
                       binding.ConfirmedAtUtc, binding.CreatedByActorUserId,
                       revision.ChangeKind, revision.Revision, revision.ActorUserId
                FROM dbo.RepositoryBindings binding
                INNER JOIN dbo.RepositoryBindingRevisions revision
                    ON revision.TenantId=binding.TenantId
                   AND revision.ProjectId=binding.ProjectId
                   AND revision.RepositoryBindingId=binding.Id
                WHERE binding.TenantId=1 AND binding.ProjectId=@ProjectId;
                """,
                new { project.ProjectId });
            Assert.AreNotEqual(Guid.Empty, backfill.Id);
            Assert.AreEqual(RepositoryKinds.Existing, backfill.RepositoryKind);
            Assert.AreEqual(RepositoryBindingStates.LegacyUnverified, backfill.BindingState);
            Assert.AreEqual(legacyPath, backfill.CanonicalPath);
            Assert.IsNull(backfill.DefaultBranch);
            Assert.IsNull(backfill.BaselineCommit);
            Assert.IsNull(backfill.ConfirmedAtUtc);
            Assert.IsNull(backfill.CreatedByActorUserId);
            Assert.IsNull(backfill.ActorUserId);
            Assert.AreEqual("LegacyBackfill", backfill.ChangeKind);
            Assert.AreEqual(1L, backfill.Revision);

            await connection.ExecuteAsync(
                """
                DISABLE TRIGGER dbo.TR_RepositoryBindingRevisions_AppendOnly
                    ON dbo.RepositoryBindingRevisions;
                DELETE FROM dbo.RepositoryBindingRevisions
                WHERE TenantId=1 AND ProjectId=@ProjectId;
                ENABLE TRIGGER dbo.TR_RepositoryBindingRevisions_AppendOnly
                    ON dbo.RepositoryBindingRevisions;
                """,
                new { project.ProjectId });
            Assert.AreEqual(
                0,
                await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM dbo.RepositoryBindingRevisions WHERE TenantId=1 AND ProjectId=@ProjectId;",
                    new { project.ProjectId }));
            await ApplyRepositorySetupMigrationAsync(connection);
            Assert.AreEqual(
                1,
                await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(1)
                    FROM dbo.RepositoryBindingRevisions
                    WHERE TenantId=1 AND ProjectId=@ProjectId
                      AND ChangeKind=N'LegacyBackfill' AND Revision=1;
                    """,
                    new { project.ProjectId }),
                "A migration rerun must repair the missing immutable revision even when the binding row already exists.");
        }

        var context = await client.GetFromJsonAsync<RepositorySetupContext>(ContextUrl(project.ProjectId));
        Assert.IsNotNull(context);
        Assert.IsNotNull(context.RepositoryBinding);
        Assert.AreEqual(RepositoryKinds.Existing, context.RepositoryBinding.RepositoryKind);
        Assert.AreEqual(RepositoryBindingStates.LegacyUnverified,
            context.RepositoryBinding.BindingState);
        Assert.IsNull(context.ExecutionProfile);
        Assert.IsNull(context.LatestConfirmation);

        var understanding = await client.GetFromJsonAsync<ProjectUnderstandingSnapshot>(
            $"/api/workbench/projects/{project.ProjectId}/understanding");
        Assert.IsNotNull(understanding);
        Assert.AreEqual(context.RepositoryBinding.Id,
            understanding.OperationalProjections.RepositoryBinding?.Id);

        var openResponse = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{project.ProjectId}/open",
            new { clientOperationId = Guid.NewGuid(), takeOver = false });
        Assert.AreEqual(HttpStatusCode.OK, openResponse.StatusCode);
        var opened = await openResponse.Content.ReadFromJsonAsync<WorkbenchProjectEntryContext>();
        Assert.IsNotNull(opened);
        Assert.AreEqual(context.RepositoryBinding.Id, opened.RepositoryBinding?.Id);

        var localPathMutation = await client.PutAsJsonAsync(
            $"/api/projects/{project.ProjectId}/local-path",
            new { localPath = @"D:\Replacement" });
        Assert.AreEqual(HttpStatusCode.Gone, localPathMutation.StatusCode);
        await AssertErrorAsync(localPathMutation, "legacy_local_path_mutation_disabled");

        var patchMutation = await client.PatchAsJsonAsync(
            $"/api/projects/{project.ProjectId}",
            new
            {
                id = project.ProjectId,
                tenantId = AssignedTenantId,
                name = "Legacy repository evidence",
                localPath = @"D:\Replacement"
            });
        Assert.AreEqual(HttpStatusCode.Conflict, patchMutation.StatusCode);
        await AssertErrorAsync(patchMutation, "legacy_local_path_mutation_disabled");

        var select = await client.PostAsync($"/api/projects/{project.ProjectId}/select", null);
        Assert.AreEqual(HttpStatusCode.OK, select.StatusCode);
        Assert.AreEqual(0, applyCapability.QualificationCalls,
            "V2 project selection may establish UI context but must not mint legacy disposable-apply qualification.");

        var codeIndex = await client.PostAsync(
            $"/api/projects/{project.ProjectId}/provisioning/code-index",
            null);
        Assert.AreEqual(HttpStatusCode.Gone, codeIndex.StatusCode);
        await AssertGovernedErrorAsync(codeIndex, "legacy_project_provisioning_mutation_disabled");

        var builderPermission = await client.PutAsJsonAsync(
            $"/api/projects/{project.ProjectId}/provisioning/builder-workspace-permission",
            new { enabled = true });
        Assert.AreEqual(HttpStatusCode.Gone, builderPermission.StatusCode);
        await AssertGovernedErrorAsync(
            builderPermission,
            "legacy_project_provisioning_mutation_disabled");

        var indexProjection = await client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/mark-index-stale",
            new { reason = "Must not mutate under Workbench V2" });
        Assert.AreEqual(HttpStatusCode.Gone, indexProjection.StatusCode);
        await AssertErrorAsync(indexProjection, "legacy_project_index_mutation_disabled");

        await using var verification = new SqlConnection(ConnectionString);
        var state = await verification.QuerySingleAsync<LegacyMutationState>(
            """
            SELECT
                (SELECT LocalPath FROM dbo.Projects WHERE TenantId=1 AND Id=@ProjectId) AS LocalPath,
                (SELECT COUNT(1) FROM dbo.RepositoryBindings
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS Bindings,
                (SELECT COUNT(1) FROM dbo.ProjectExecutionProfiles
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS ExecutionProfiles,
                (SELECT COUNT(1) FROM dbo.RepositorySetupConfirmations
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS Confirmations;
            """,
            new { project.ProjectId });
        Assert.AreEqual(legacyPath, state.LocalPath);
        Assert.AreEqual(1, state.Bindings);
        Assert.AreEqual(0, state.ExecutionProfiles);
        Assert.AreEqual(0, state.Confirmations);

        var synthesizedProject = await StartProjectAsync(client, "Synthesized legacy projection");
        const string synthesizedPath = @"E:\Legacy\ProjectionOnly";
        await verification.ExecuteAsync(
            "UPDATE dbo.Projects SET LocalPath=@Path WHERE TenantId=1 AND Id=@ProjectId;",
            new { Path = synthesizedPath, synthesizedProject.ProjectId });
        var synthesizedContext = await client.GetFromJsonAsync<RepositorySetupContext>(
            ContextUrl(synthesizedProject.ProjectId));
        Assert.IsNotNull(synthesizedContext?.RepositoryBinding);
        Assert.AreEqual(RepositoryKinds.Existing,
            synthesizedContext.RepositoryBinding.RepositoryKind);
        Assert.AreEqual(RepositoryBindingStates.LegacyUnverified,
            synthesizedContext.RepositoryBinding.BindingState);
        Assert.AreEqual(synthesizedPath, synthesizedContext.RepositoryBinding.CanonicalPath);
        Assert.IsNull(synthesizedContext.RepositoryBinding.CreatedByActorUserId);
        var probesAfterContext = fileSystem.TotalProbeCount;
        var synthesizedUnderstanding = await client.GetFromJsonAsync<ProjectUnderstandingSnapshot>(
            $"/api/workbench/projects/{synthesizedProject.ProjectId}/understanding");
        Assert.IsNotNull(synthesizedUnderstanding);
        Assert.AreEqual(
            synthesizedContext.RepositoryBinding.Id,
            synthesizedUnderstanding.OperationalProjections.RepositoryBinding?.Id);
        Assert.IsNull(
            synthesizedUnderstanding.OperationalProjections.RepositoryBinding?.CreatedByActorUserId);
        var synthesizedOpen = await client.PostAsJsonAsync(
            $"/api/workbench/projects/{synthesizedProject.ProjectId}/open",
            new { clientOperationId = Guid.NewGuid(), takeOver = false });
        Assert.AreEqual(HttpStatusCode.OK, synthesizedOpen.StatusCode);
        var synthesizedEntry = await synthesizedOpen.Content
            .ReadFromJsonAsync<WorkbenchProjectEntryContext>();
        Assert.IsNotNull(synthesizedEntry);
        Assert.AreEqual(synthesizedContext.RepositoryBinding.Id,
            synthesizedEntry.RepositoryBinding?.Id);
        Assert.IsNull(synthesizedEntry.RepositoryBinding?.CreatedByActorUserId);
        Assert.AreEqual(probesAfterContext, fileSystem.TotalProbeCount,
            "Reading an already-bound LocalPath projection through understanding/open must not inspect repository files.");
    }

    private static WebApplicationFactory<Program> RepositoryFactory(
        string approvedRoot,
        RecordingFileSystem fileSystem,
        Action<IServiceCollection>? configureServices = null) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WorkbenchV2:Enabled", "true");
            builder.UseSetting("WorkbenchRepositorySetup:ApprovedWorkspaceRoot", approvedRoot);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRepositorySetupFileSystemInspector>();
                services.AddSingleton<IRepositorySetupFileSystemInspector>(fileSystem);
                configureServices?.Invoke(services);
            });
        });

    private static async Task<HttpClient> AuthenticatedClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var token = await SelectTenantAsync(await LoginAsync());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<StartedProject> StartProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/projects/start",
            new { clientOperationId = Guid.NewGuid(), name });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new StartedProject(
            body.GetProperty("projectId").GetInt32(),
            body.GetProperty("workbenchSessionId").GetInt64(),
            body.GetProperty("leaseEpoch").GetInt64());
    }

    private static async Task<RepositorySetupPlanPreview> PreviewAsync(
        HttpClient client,
        StartedProject project)
    {
        var response = await client.PostAsJsonAsync(PlanUrl(project.ProjectId), PlanPayload(project));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<RepositorySetupPlanPreview>();
        Assert.IsNotNull(plan);
        return plan;
    }

    private static async Task AppendUnderstandingAsync(
        int projectId,
        string key,
        string value,
        string state)
    {
        var document = new ProjectUnderstandingDocument(
            ProjectUnderstandingContract.SchemaVersion,
            [
                new ProjectUnderstandingFact(
                    key,
                    value,
                    state,
                    UserLocked: false,
                    ProjectUnderstandingAuthorKinds.Actor,
                    AuthorActorUserId: 1,
                    AuthorAgentRunId: null,
                    SourceMessageIds: [],
                    EvidenceSummary: "Explicit integration-test technology preference.",
                    Revision: 2)
            ],
            [],
            []);
        var json = ProjectUnderstandingDocumentCodec.Serialize(document);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT dbo.ProjectUnderstandings
                (TenantId, ProjectId, Revision, Status, UnderstandingJson,
                 CreatedByActorUserId, DocumentSchemaVersion, BasedOnRevision)
            VALUES
                (1, @ProjectId, 2, N'Draft', @UnderstandingJson, 1, 1, 1);
            """,
            new { ProjectId = projectId, UnderstandingJson = json });
    }

    private static async Task<HttpClient> CreateProjectViewerClientAsync(
        HttpClient owner,
        WebApplicationFactory<Program> factory,
        int? projectId) =>
        await CreateProjectUserClientAsync(owner, factory, projectId, "Viewer", "Viewer");

    private static async Task<HttpClient> CreateProjectUserClientAsync(
        HttpClient owner,
        WebApplicationFactory<Program> factory,
        int? projectId,
        string tenantRole,
        string projectRole)
    {
        var email = $"repository-viewer-{Guid.NewGuid():N}@irondev.local";
        const string password = "repository-viewer-test-password";
        var create = await owner.PostAsJsonAsync(
            $"/api/tenants/{AssignedTenantId}/users",
            new { email, displayName = "Repository Boundary User", password, role = tenantRole });
        Assert.AreEqual(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("id").GetInt32();
        if (projectId.HasValue)
        {
            var membership = await owner.PutAsJsonAsync(
                $"/api/projects/{projectId.Value}/members/{userId}",
                new { projectRole });
            Assert.AreEqual(HttpStatusCode.OK, membership.StatusCode);
        }

        var token = await SelectTenantAsync(await LoginAsync(email, password));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AssertChangedPlanRejectedAsync(
        HttpClient client,
        StartedProject project,
        string planHash)
    {
        var response = await client.PostAsJsonAsync(
            ConfirmationUrl(project.ProjectId),
            ConfirmationPayload(project, Guid.NewGuid(), planHash));
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorAsync(response, RepositorySetupPlanChangedException.ErrorCode);
    }

    private static async Task AssertNoSetupAuthorityAsync(
        int projectId,
        Guid? clientOperationId = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var state = await ReadSetupStateAsync(connection, projectId, clientOperationId);
        Assert.AreEqual(0, state.Bindings);
        Assert.AreEqual(0, state.BindingRevisions);
        Assert.AreEqual(0, state.ExecutionProfiles);
        Assert.AreEqual(0, state.ExecutionProfileRevisions);
        Assert.AreEqual(0, state.Confirmations);
        Assert.AreEqual(1, state.ReadinessRevisions);
        Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured, state.ExecutionReadiness);
        Assert.AreEqual(1, state.LifecycleRevisions);
        Assert.AreEqual(ProjectLifecyclePhases.Shaping, state.LifecyclePhase);
        Assert.AreEqual(0, state.OperationRows,
            "Rollback and rejected confirmations must not strand Pending or Failed client-operation rows.");
        Assert.AreEqual(0, state.CompletedOperations);
        Assert.AreEqual(0, state.OutboxEvents);
        Assert.AreEqual(0, state.Attributions);
        Assert.IsNull(state.LocalPath);
        Assert.AreEqual(0, state.ProjectFiles);
    }

    private static Task<SetupState> ReadSetupStateAsync(
        SqlConnection connection,
        int projectId,
        Guid? operationId) =>
        connection.QuerySingleAsync<SetupState>(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.RepositoryBindings
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS Bindings,
                (SELECT COUNT(1) FROM dbo.RepositoryBindingRevisions
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS BindingRevisions,
                (SELECT COUNT(1) FROM dbo.ProjectExecutionProfiles
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS ExecutionProfiles,
                (SELECT COUNT(1) FROM dbo.ProjectExecutionProfileRevisions
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS ExecutionProfileRevisions,
                (SELECT COUNT(1) FROM dbo.RepositorySetupConfirmations
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS Confirmations,
                (SELECT COUNT(1) FROM dbo.ProjectReadinessAssessments
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS ReadinessRevisions,
                (SELECT TOP (1) ExecutionReadiness FROM dbo.ProjectReadinessAssessments
                 WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY Revision DESC) AS ExecutionReadiness,
                (SELECT TOP (1) ReasonCode FROM dbo.ProjectReadinessAssessments
                 WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY Revision DESC) AS ReadinessReasonCode,
                (SELECT COUNT(1) FROM dbo.ProjectLifecyclePhases
                 WHERE TenantId=1 AND ProjectId=@ProjectId) AS LifecycleRevisions,
                (SELECT TOP (1) Phase FROM dbo.ProjectLifecyclePhases
                 WHERE TenantId=1 AND ProjectId=@ProjectId ORDER BY Revision DESC) AS LifecyclePhase,
                (SELECT COUNT(1) FROM dbo.ClientOperations
                 WHERE TenantId=1 AND OperationKind=N'ConfirmRepositorySetup'
                   AND ResourceScopeId=CONCAT(N'project:', @ProjectId, N':repository-setup')
                   AND (@OperationId IS NULL OR ClientOperationId=@OperationId)) AS OperationRows,
                (SELECT COUNT(1) FROM dbo.ClientOperations
                 WHERE TenantId=1 AND OperationKind=N'ConfirmRepositorySetup'
                   AND ResourceScopeId=CONCAT(N'project:', @ProjectId, N':repository-setup')
                   AND (@OperationId IS NULL OR ClientOperationId=@OperationId)
                   AND Status=N'Completed') AS CompletedOperations,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents
                 WHERE TenantId=1 AND ProjectId=@ProjectId
                   AND EventKind=N'RepositorySetupConfirmed'
                   AND (@OperationId IS NULL OR ClientOperationId=@OperationId)) AS OutboxEvents,
                (SELECT COUNT(1) FROM dbo.UserMutationAttribution
                 WHERE TenantId=1 AND ProjectId=CONVERT(NVARCHAR(128), @ProjectId)
                   AND Route=N'/api/workbench/projects/{projectId}/repository/setup-confirmations'
                   AND (@OperationId IS NULL OR CorrelationId=CONVERT(NVARCHAR(128), @OperationId))) AS Attributions,
                (SELECT LocalPath FROM dbo.Projects
                 WHERE TenantId=1 AND Id=@ProjectId) AS LocalPath,
                (SELECT COUNT(1) FROM dbo.ProjectFiles
                 WHERE ProjectId=@ProjectId) AS ProjectFiles;
            """,
            new { ProjectId = projectId, OperationId = operationId });

    private static async Task<LeaseState> ReadLeaseStateAsync(StartedProject project)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.QuerySingleAsync<LeaseState>(
            """
            SELECT HeartbeatAtUtc, ExpiresAtUtc
            FROM dbo.WorkbenchWriteLeases
            WHERE TenantId=1 AND ProjectId=@ProjectId
              AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=@LeaseEpoch;
            """,
            project);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string expected)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(expected, body.GetProperty("error").GetString());
    }

    private static async Task AssertGovernedErrorAsync(HttpResponseMessage response, string expected)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(expected, body.GetProperty("reasonCode").GetString());
    }

    private static void AssertLowerSha256(string value)
    {
        Assert.IsTrue(Regex.IsMatch(value, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant),
            $"Expected a lowercase SHA-256 value, got '{value}'.");
    }

    private static object PlanPayload(StartedProject project, string profileId = ProfileId) => new
    {
        project.WorkbenchSessionId,
        project.LeaseEpoch,
        profileDefinitionId = profileId
    };

    private static object ConfirmationPayload(
        StartedProject project,
        Guid operationId,
        string planHash) => new
    {
        project.WorkbenchSessionId,
        project.LeaseEpoch,
        clientOperationId = operationId,
        expectedPlanHash = planHash
    };

    private static string ContextUrl(int projectId) =>
        $"/api/workbench/projects/{projectId}/repository";

    private static string PlanUrl(int projectId) =>
        $"/api/workbench/projects/{projectId}/repository/setup-plans";

    private static string ConfirmationUrl(int projectId) =>
        $"/api/workbench/projects/{projectId}/repository/setup-confirmations";

    private static string SafeApprovedRoot() => Path.Combine(
        Path.GetPathRoot(Environment.SystemDirectory)!,
        "IronDev.RepositorySetup.Tests",
        Guid.NewGuid().ToString("N"));

    private static async Task ApplyRepositorySetupMigrationAsync(SqlConnection connection)
    {
        var root = FindRepositoryRoot();
        var sql = await File.ReadAllTextAsync(
            Path.Combine(root, "Database", "migrate_workbench_repository_setup.sql"));
        var batches = Regex.Split(
                sql.Replace("\r\n", "\n", StringComparison.Ordinal),
                @"(?im)^\s*GO\s*$")
            .Select(value => value.Trim())
            .Where(value => value.Length > 0);
        foreach (var batch in batches)
            await connection.ExecuteAsync(batch);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")) &&
                    Directory.Exists(Path.Combine(current.FullName, "Database")))
                    return current.FullName;
                current = current.Parent;
            }
        }
        throw new InvalidOperationException("Could not locate the IronDev repository root.");
    }

    private sealed record StartedProject(
        int ProjectId,
        long WorkbenchSessionId,
        long LeaseEpoch);

    private sealed class SetupState
    {
        public int Bindings { get; init; }
        public int BindingRevisions { get; init; }
        public int ExecutionProfiles { get; init; }
        public int ExecutionProfileRevisions { get; init; }
        public int Confirmations { get; init; }
        public int ReadinessRevisions { get; init; }
        public string ExecutionReadiness { get; init; } = string.Empty;
        public string ReadinessReasonCode { get; init; } = string.Empty;
        public int LifecycleRevisions { get; init; }
        public string LifecyclePhase { get; init; } = string.Empty;
        public int OperationRows { get; init; }
        public int CompletedOperations { get; init; }
        public int OutboxEvents { get; init; }
        public int Attributions { get; init; }
        public string? LocalPath { get; init; }
        public int ProjectFiles { get; init; }
    }

    private sealed class LeaseState
    {
        public DateTime HeartbeatAtUtc { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
    }

    private sealed class LegacyBindingRow
    {
        public Guid Id { get; init; }
        public string RepositoryKind { get; init; } = string.Empty;
        public string BindingState { get; init; } = string.Empty;
        public string CanonicalPath { get; init; } = string.Empty;
        public string? DefaultBranch { get; init; }
        public string? BaselineCommit { get; init; }
        public DateTime? ConfirmedAtUtc { get; init; }
        public int? CreatedByActorUserId { get; init; }
        public string ChangeKind { get; init; } = string.Empty;
        public long Revision { get; init; }
        public int? ActorUserId { get; init; }
    }

    private sealed class LegacyMutationState
    {
        public string? LocalPath { get; init; }
        public int Bindings { get; init; }
        public int ExecutionProfiles { get; init; }
        public int Confirmations { get; init; }
    }

    private sealed class RecordingFileSystem : IRepositorySetupFileSystemInspector
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> DirectoryProbes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FileProbes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AttributeProbes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int TotalProbeCount => DirectoryProbes.Count + FileProbes.Count + AttributeProbes.Count;

        public static RecordingFileSystem WithDirectories(params string[] paths)
        {
            var result = new RecordingFileSystem();
            foreach (var path in paths)
                result._directories.Add(Normalize(path));
            return result;
        }

        public bool DirectoryExists(string path)
        {
            DirectoryProbes.Add(Normalize(path));
            return _directories.Contains(Normalize(path));
        }

        public bool FileExists(string path)
        {
            FileProbes.Add(Normalize(path));
            return false;
        }

        public FileAttributes GetAttributes(string path)
        {
            AttributeProbes.Add(Normalize(path));
            return FileAttributes.Directory;
        }

        private static string Normalize(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private sealed class EmptyRepositorySetupProfileCatalog : IRepositorySetupProfileCatalog
    {
        public IReadOnlyList<RepositorySetupProfileDescriptor> GetAll() => [];

        public RepositorySetupProfileDescriptor? Find(
            string profileDefinitionId,
            int? revision = null,
            string? descriptorSha256 = null) => null;
    }

    private sealed class TestRepositorySetupFailureInjector : IRepositorySetupConfirmationFailureInjector
    {
        public RepositorySetupConfirmationFailurePoint? FailurePoint { get; set; }

        public void ThrowIfRequested(RepositorySetupConfirmationFailurePoint point)
        {
            if (FailurePoint == point)
                throw new InjectedRepositorySetupFailureException(point);
        }
    }

    private sealed class InjectedRepositorySetupFailureException(
        RepositorySetupConfirmationFailurePoint point)
        : Exception($"Injected repository-setup failure at {point}.");

    private sealed class RecordingApplyCapabilityService : IProjectApplyCapabilityService
    {
        public int QualificationCalls { get; private set; }

        public Task<ProjectApplyCapability> EvaluateAsync(
            int projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectApplyCapability { ProjectId = projectId });

        public Task<ProjectApplyCapability> QualifyDisposableProjectAsync(
            int projectId,
            int qualifyingActorUserId,
            CancellationToken cancellationToken = default)
        {
            QualificationCalls++;
            return Task.FromResult(new ProjectApplyCapability { ProjectId = projectId });
        }
    }
}
